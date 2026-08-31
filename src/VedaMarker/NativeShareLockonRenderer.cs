using System.Runtime.InteropServices;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;

namespace VedaMarker;

internal sealed unsafe class NativeShareLockonRenderer : IDisposable
{
    private const string SharePath = "vfx/lockon/eff/com_share3t.avfx";
    private const string ActorVfxCreateSignature =
        "40 53 55 56 57 48 81 EC ?? ?? ?? ?? 0F 29 B4 24 ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 84 24 ?? ?? ?? ?? 0F B6 AC 24 ?? ?? ?? ?? 0F 28 F3 49 8B F8";
    private const string ActorVfxRemovePointerSignature = "0F 11 48 10 48 8D 05";

    private readonly IObjectTable objectTable;
    private readonly IPluginLog log;
    private readonly List<ActiveVfx> activeVfx = [];
    private readonly ActorVfxCreateDelegate? create;
    private readonly ActorVfxRemoveDelegate? remove;

    public NativeShareLockonRenderer(
        ISigScanner sigScanner,
        IObjectTable objectTable,
        IPluginLog log)
    {
        this.objectTable = objectTable;
        this.log = log;
        try
        {
            create = Marshal.GetDelegateForFunctionPointer<ActorVfxCreateDelegate>(
                sigScanner.ScanText(ActorVfxCreateSignature));

            var removeDisplacement = sigScanner.ScanText(ActorVfxRemovePointerSignature) + 7;
            var removePointerAddress = removeDisplacement + Marshal.ReadInt32(removeDisplacement) + 4;
            var removeAddress = Marshal.ReadIntPtr(removePointerAddress);
            if (removeAddress == 0)
            {
                throw new InvalidOperationException("角色 VFX 清理入口为空。");
            }

            remove = Marshal.GetDelegateForFunctionPointer<ActorVfxRemoveDelegate>(removeAddress);
            IsAvailable = true;
            LastStatus = "游戏原生分摊 LockOn 创建器已就绪";
        }
        catch (Exception exception)
        {
            IsAvailable = false;
            LastStatus = $"当前客户端无法启用原生分摊特效：{exception.Message}";
            log.Error(exception, "VedaMarker native share LockOn signatures unavailable");
        }
    }

    public bool IsAvailable { get; private set; }

    public int ActiveCount => activeVfx.Count;

    public string LastStatus { get; private set; }

    public bool Replace(IEnumerable<IGameObject> targets)
    {
        if (!IsAvailable || create is null || remove is null)
        {
            return false;
        }

        var uniqueTargets = targets
            .Where(target => target.Address != 0)
            .DistinctBy(target => target.Address)
            .ToArray();

        Clear();
        if (uniqueTargets.Length == 0)
        {
            LastStatus = "当前目标中没有需要显示的原生分摊特效";
            return true;
        }

        try
        {
            foreach (var target in uniqueTargets)
            {
                var vfx = create(SharePath, target.Address, target.Address, -1f, 0, 0, 0);
                if (vfx == null)
                {
                    throw new InvalidOperationException(
                        $"游戏未能在角色 0x{target.EntityId:X8} 上创建分摊 LockOn。");
                }

                activeVfx.Add(new ActiveVfx((nint)vfx, target.EntityId, target.Address));
            }

            LastStatus = $"已在本机为 {activeVfx.Count} 个目标显示游戏原生分摊特效";
            return true;
        }
        catch (Exception exception)
        {
            Clear();
            IsAvailable = false;
            LastStatus = $"游戏原生分摊特效创建失败并已清理：{exception.Message}";
            log.Error(exception, "VedaMarker native share LockOn creation failed");
            return false;
        }
    }

    public void Clear()
    {
        var hadActiveVfx = activeVfx.Count != 0;
        for (var index = activeVfx.Count - 1; index >= 0; index--)
        {
            TryRemove(activeVfx[index]);
        }

        activeVfx.Clear();
        if (hadActiveVfx)
        {
            LastStatus = "游戏原生分摊特效已清除";
        }
    }

    public void Dispose() => Clear();

    private void TryRemove(ActiveVfx active)
    {
        if (active.Address == 0 || remove is null)
        {
            return;
        }

        try
        {
            var target = objectTable.SearchByEntityId(active.EntityId);
            if (target is null || target.Address != active.TargetAddress)
            {
                log.Debug(
                    "Skipping native share LockOn cleanup for unavailable actor {EntityId:X8}",
                    active.EntityId);
                return;
            }

            remove((VfxObject*)active.Address, 1);
        }
        catch (Exception exception)
        {
            log.Error(
                exception,
                "VedaMarker native share LockOn cleanup failed for {Address:X}",
                active.Address);
        }
    }

    private readonly record struct ActiveVfx(nint Address, uint EntityId, nint TargetAddress);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private delegate VfxObject* ActorVfxCreateDelegate(
        [MarshalAs(UnmanagedType.LPStr)] string path,
        nint source,
        nint target,
        float a4,
        byte a5,
        ushort a6,
        byte a7);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint ActorVfxRemoveDelegate(VfxObject* vfx, byte a2);
}
