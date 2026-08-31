using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using VedaMarker.Core;

namespace VedaMarker;

internal sealed unsafe class NativeOmenRenderer : IDisposable
{
    private const string PoolName = "Client.System.Scheduler.Instance.VfxObject";
    private const string CirclePath = "vfx/omen/eff/m0347_sircle_01m1.avfx";
    private const string ConePath = "vfx/omen/eff/z6r3_b4_fan90_k2.avfx";
    private const string RunSignature = "E8 ?? ?? ?? ?? B0 02 EB 02";
    private const string RemoveSignature =
        "40 53 48 83 EC 20 48 8B D9 48 8B 89 ?? ?? ?? ?? 48 85 C9 74 28 33 D2 E8 ?? ?? ?? ?? 48 8B 8B ?? ?? ?? ?? 48 85 C9";

    private readonly IPluginLog log;
    private readonly List<nint> activeVfx = [];
    private readonly StaticVfxCreateDelegate? create;
    private readonly StaticVfxRunDelegate? run;
    private readonly StaticVfxRemoveDelegate? remove;

    public NativeOmenRenderer(ISigScanner sigScanner, IPluginLog log)
    {
        this.log = log;
        try
        {
            create = Marshal.GetDelegateForFunctionPointer<StaticVfxCreateDelegate>(
                VfxObject.Addresses.Create.Value);
            run = Marshal.GetDelegateForFunctionPointer<StaticVfxRunDelegate>(
                sigScanner.ScanText(RunSignature));
            remove = Marshal.GetDelegateForFunctionPointer<StaticVfxRemoveDelegate>(
                sigScanner.ScanText(RemoveSignature));
            IsAvailable = true;
            LastStatus = "游戏原生 Omen 创建器已就绪";
        }
        catch (Exception exception)
        {
            IsAvailable = false;
            LastStatus = $"当前客户端无法启用游戏原生 Omen：{exception.Message}";
            log.Error(exception, "VedaMarker native omen signatures unavailable");
        }
    }

    public bool IsAvailable { get; private set; }

    public int ActiveCount => activeVfx.Count;

    public string LastStatus { get; private set; }

    public bool Replace(Vector3 arenaCenter, ForsakenTelegraphPlan plan)
    {
        if (!IsAvailable || create is null || run is null || remove is null)
        {
            return false;
        }

        var replacement = new List<nint>(plan.Telegraphs.Count);
        try
        {
            foreach (var telegraph in plan.Telegraphs)
            {
                replacement.Add(CreateTelegraph(arenaCenter, telegraph));
            }

            Clear();
            activeVfx.AddRange(replacement);
            LastStatus =
                $"Wave {plan.Wave} / Direction {plan.Direction8}：已创建 {activeVfx.Count} 个游戏原生 AOE 范围";
            return true;
        }
        catch (Exception exception)
        {
            foreach (var address in replacement)
            {
                TryRemove(address);
            }

            Clear();
            IsAvailable = false;
            LastStatus = $"游戏原生 AOE 创建失败并已清理：{exception.Message}";
            log.Error(exception, "VedaMarker native omen creation failed");
            return false;
        }
    }

    public void Clear()
    {
        foreach (var address in activeVfx)
        {
            TryRemove(address);
        }

        activeVfx.Clear();
    }

    public void Dispose() => Clear();

    private nint CreateTelegraph(Vector3 arenaCenter, ForsakenTelegraph telegraph)
    {
        var path = telegraph.Kind switch
        {
            ForsakenTelegraphKind.Circle => CirclePath,
            ForsakenTelegraphKind.Cone => ConePath,
            _ => throw new ArgumentOutOfRangeException(nameof(telegraph.Kind)),
        };

        var pathBytes = Encoding.UTF8.GetBytes(path + '\0');
        var poolBytes = Encoding.UTF8.GetBytes(PoolName + '\0');
        fixed (byte* pathPointer = pathBytes)
        fixed (byte* poolPointer = poolBytes)
        {
            var vfx = create!(pathPointer, poolPointer);
            if (vfx == null)
            {
                throw new InvalidOperationException($"游戏未能创建 Omen 资源：{path}");
            }

            run!(vfx, 0f, uint.MaxValue);
            vfx->Position = new Vector3(
                arenaCenter.X + telegraph.Origin.X,
                arenaCenter.Y + 0.05f,
                arenaCenter.Z + telegraph.Origin.Y);
            vfx->Scale = new Vector3(telegraph.Range);
            if (telegraph.Kind == ForsakenTelegraphKind.Cone)
            {
                var direction = telegraph.Target - telegraph.Origin;
                var rotation = MathF.Atan2(direction.X, direction.Y);
                vfx->Rotation = FFXIVClientStructs.FFXIV.Common.Math.Quaternion.CreateFromYawPitchRoll(
                    rotation,
                    0f,
                    0f);
            }

            vfx->UpdateTransforms(true);
            return (nint)vfx;
        }
    }

    private void TryRemove(nint address)
    {
        if (address == 0 || remove is null)
        {
            return;
        }

        try
        {
            remove((VfxObject*)address);
        }
        catch (Exception exception)
        {
            log.Error(exception, "VedaMarker native omen cleanup failed for {Address:X}", address);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate VfxObject* StaticVfxCreateDelegate(byte* path, byte* poolName);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint StaticVfxRunDelegate(VfxObject* vfx, float time, uint flags);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint StaticVfxRemoveDelegate(VfxObject* vfx);
}
