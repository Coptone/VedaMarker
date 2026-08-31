using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Plugin.Services;
using VedaMarker.Core;

namespace VedaMarker;

internal sealed class LocalMarkerProvider : IMarkerProvider
{
    private const float DefaultWorldHeight = 2f;

    private static readonly IReadOnlyDictionary<PartyMarker, uint> MarkerIconIds =
        new Dictionary<PartyMarker, uint>
        {
            [PartyMarker.Attack1] = 61201,
            [PartyMarker.Attack2] = 61202,
            [PartyMarker.Attack3] = 61203,
            [PartyMarker.Attack4] = 61204,
            [PartyMarker.Bind1] = 61211,
            [PartyMarker.Bind2] = 61212,
            [PartyMarker.Ignore1] = 61221,
            [PartyMarker.Ignore2] = 61222,
        };

    private readonly IGameGui gameGui;
    private readonly IObjectTable objectTable;
    private readonly ITextureProvider textureProvider;
    private readonly Func<float> resolveScale;
    private readonly Func<uint?> resolveLocalActorId;
    private readonly Func<int, uint?> resolvePartySlotActorId;
    private readonly Dictionary<PartyMarker, ISharedImmediateTexture> textures = [];
    private readonly Dictionary<uint, PartyMarker> activeMarkers = [];

    public LocalMarkerProvider(
        IGameGui gameGui,
        IObjectTable objectTable,
        ITextureProvider textureProvider,
        Func<float> resolveScale,
        Func<uint?> resolveLocalActorId,
        Func<int, uint?> resolvePartySlotActorId)
    {
        this.gameGui = gameGui;
        this.objectTable = objectTable;
        this.textureProvider = textureProvider;
        this.resolveScale = resolveScale;
        this.resolveLocalActorId = resolveLocalActorId;
        this.resolvePartySlotActorId = resolvePartySlotActorId;

        foreach (var marker in MarkerIconIds)
        {
            textures.Add(
                marker.Key,
                textureProvider.GetFromGameIcon(new GameIconLookup(marker.Value)));
        }
    }

    public string Name => "本地软标点";

    public bool ProducesMarkers => true;

    public int PendingOperationCount => 0;

    public int ActiveMarkerCount => activeMarkers.Count;

    public string LastOperation { get; private set; } = "尚未显示本地标点";

    public string LastDrawStatus { get; private set; } = "八种本地图标已预加载";

    public void Submit(
        ValidatedMarkerAssignment assignment,
        IReadOnlyCollection<RoleSlot> targetRoles,
        RoleSlot localRole,
        IReadOnlyDictionary<RoleSlot, int> partySlots)
    {
        var targets = PartyMarkerSubmissionValidator.Validate(
            assignment,
            targetRoles,
            localRole,
            partySlots);
        var nextMarkers = targets.ToDictionary(
            role => ResolveActorId(role, localRole, partySlots),
            role => assignment.Markers[role]);

        activeMarkers.Clear();
        foreach (var marker in nextMarkers)
        {
            activeMarkers.Add(marker.Key, marker.Value);
        }

        LastOperation = $"已清除上一轮并显示 {activeMarkers.Count} 个本地标点";
    }

    public void SubmitDiagnosticSelfMarker(PartyMarker marker)
    {
        var actorId = ResolveLocalActorId();
        activeMarkers.Clear();
        activeMarkers.Add(actorId, marker);
        LastOperation = $"本人本地预览：{marker}";
    }

    public void SubmitDiagnosticSelfClear()
    {
        activeMarkers.Clear();
        LastOperation = "已清除本人本地预览";
    }

    public bool TryGetMarker(uint actorId, out PartyMarker marker) =>
        activeMarkers.TryGetValue(actorId, out marker);

    public void Draw()
    {
        if (activeMarkers.Count == 0)
        {
            return;
        }

        var drawList = ImGui.GetForegroundDrawList();
        var scale = Math.Clamp(resolveScale(), 0.5f, 1.5f);
        var drawn = 0;
        foreach (var entry in activeMarkers)
        {
            var actor = objectTable.SearchByEntityId(entry.Key);
            if (actor is null || !gameGui.WorldToScreen(MarkerPosition(actor), out var screen))
            {
                continue;
            }

            IDalamudTextureWrap texture;
            try
            {
                texture = GetTexture(entry.Value).GetWrapOrEmpty();
            }
            catch (Exception exception)
            {
                LastDrawStatus =
                    $"{entry.Value} 图标资源暂未就绪，保留标点并在下一帧重试：{exception.Message}";
                continue;
            }

            if (texture.Handle == 0 || texture.Width <= 0 || texture.Height <= 0)
            {
                LastDrawStatus = $"{entry.Value} 图标资源正在加载，保留标点并在下一帧重试";
                continue;
            }

            var size = new Vector2(texture.Width, texture.Height) * (2f * scale);
            var topLeft = screen - new Vector2(size.X / 2f, size.Y);
            drawList.AddImage(texture.Handle, topLeft, topLeft + size);
            drawn++;
        }

        if (drawn == activeMarkers.Count)
        {
            LastDrawStatus = $"当前 {drawn} 个本地标点均已绘制";
        }
    }

    public void Tick(long now)
    {
    }

    public void Clear(bool immediate = false)
    {
        activeMarkers.Clear();
        LastOperation = "本地标点已清空";
    }

    private ISharedImmediateTexture GetTexture(PartyMarker marker)
    {
        if (textures.TryGetValue(marker, out var texture))
        {
            return texture;
        }

        if (!MarkerIconIds.TryGetValue(marker, out var iconId))
        {
            throw new MarkerAssignmentException($"未知本地标点图标：{marker}");
        }

        texture = textureProvider.GetFromGameIcon(new GameIconLookup(iconId));
        textures.Add(marker, texture);
        return texture;
    }

    private uint ResolveActorId(
        RoleSlot role,
        RoleSlot localRole,
        IReadOnlyDictionary<RoleSlot, int> partySlots)
    {
        if (role == localRole)
        {
            return ResolveLocalActorId();
        }

        if (!partySlots.TryGetValue(role, out var slot))
        {
            throw new MarkerAssignmentException($"无法解析 {role} 的队伍序号。");
        }

        return resolvePartySlotActorId(slot) is { } actorId && actorId != 0
            ? actorId
            : throw new MarkerAssignmentException($"无法解析队伍第 {slot} 位的游戏对象。");
    }

    private uint ResolveLocalActorId() =>
        resolveLocalActorId() is { } actorId && actorId != 0
            ? actorId
            : throw new MarkerAssignmentException("当前无法识别插件使用者本人。");

    private static Vector3 MarkerPosition(IGameObject actor) =>
        actor.Position + new Vector3(0f, DefaultWorldHeight, 0f);
}
