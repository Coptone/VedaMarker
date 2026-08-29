using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Command;
using Dalamud.Game.DutyState;
using Dalamud.Hooking;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using VedaMarker.Capture;
using VedaMarker.Core;
using LuminaAction = Lumina.Excel.Sheets.Action;

namespace VedaMarker;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/vedamarker";
    private const int MarkerDiagnosticObservationTimeoutMs = 2500;
    private static readonly RoleSlot[] RoleOrder = Enum.GetValues<RoleSlot>();
    private static readonly PartyMarker[] MarkerDiagnosticOrder = Enum.GetValues<PartyMarker>();
    private static readonly IReadOnlyDictionary<PartyMarker, int> MarkerMemoryIndices =
        new Dictionary<PartyMarker, int>
        {
            [PartyMarker.Attack1] = 0,
            [PartyMarker.Attack2] = 1,
            [PartyMarker.Attack3] = 2,
            [PartyMarker.Attack4] = 3,
            [PartyMarker.Bind1] = 5,
            [PartyMarker.Bind2] = 6,
            [PartyMarker.Ignore1] = 8,
            [PartyMarker.Ignore2] = 9,
        };

    [PluginService] private static IDalamudPluginInterface PluginInterface { get; set; } = null!;
    [PluginService] private static ICommandManager CommandManager { get; set; } = null!;
    [PluginService] private static IClientState ClientState { get; set; } = null!;
    [PluginService] private static ICondition Condition { get; set; } = null!;
    [PluginService] private static IDutyState DutyState { get; set; } = null!;
    [PluginService] private static IObjectTable ObjectTable { get; set; } = null!;
    [PluginService] private static IPartyList PartyList { get; set; } = null!;
    [PluginService] private static IFramework Framework { get; set; } = null!;
    [PluginService] private static IGameInteropProvider Interop { get; set; } = null!;
    [PluginService] private static ISigScanner SigScanner { get; set; } = null!;
    [PluginService] private static IGameGui GameGui { get; set; } = null!;
    [PluginService] private static IDataManager DataManager { get; set; } = null!;
    [PluginService] private static IPluginLog Log { get; set; } = null!;

    private readonly PluginConfiguration configuration;
    private readonly PartyRoleCoordinator roleCoordinator = new();
    private readonly ForsakenAutomationEngine automationEngine = new();
    private readonly DryRunMarkerProvider dryRunMarkerProvider = new();
    private readonly NativeMarkerProvider gameMarkerProvider;
    private readonly WorldTelegraphRenderer worldTelegraphRenderer;
    private readonly CaptureRecorder captureRecorder;
    private readonly Dictionary<uint, CaptureActionMetadata> actionMetadataCache = [];
    private IMarkerProvider activeMarkerProvider;
    private Hook<ReceiveActionEffectDelegate>? actionEffectHook;
    private Hook<ApplyMapEffectDelegate>? mapEffectHook;
    private IReadOnlyList<RuntimePartyMember> currentParty = Array.Empty<RuntimePartyMember>();
    private ValidatedMarkerAssignment? currentAssignment;
    private string partySignature = string.Empty;
    private string status = "P0/P1：等待读取队伍";
    private bool showWindow;
    private bool rolesConfirmed;
    private bool controllerArmed;
    private long lastCapturePollAt;
    private uint lastTerritoryId;
    private readonly List<MarkerDiagnosticResult> markerDiagnosticResults = [];
    private bool markerDiagnosticRunning;
    private int markerDiagnosticIndex;
    private MarkerDiagnosticPhase markerDiagnosticPhase;
    private long markerDiagnosticObservationDeadline;
    private string markerDiagnosticSummary = "全部标点与清除尚未测试";
    private bool simulationArmed;
    private int simulationWave;
    private bool simulationSoloMode;
    private RoleSlot simulationLocalRole = RoleSlot.MT;
    private RoleSlot soloSimulationRole = RoleSlot.MT;
    private uint soloSimulationJobId;
    private bool localAoeSimulationActive;
    private int localAoeWave = 1;
    private int localAoeDirection8;
    private Vector3 localAoeCenter;
    private string localAoeStatus = "本地 AOE 模拟尚未启动";

    public Plugin()
    {
        configuration = PluginInterface.GetPluginConfig() as PluginConfiguration ?? new PluginConfiguration();
        var configurationChanged = false;
        if (configuration.Version < 2)
        {
            configuration.EnableExperimentalPartyMarkers = false;
            configuration.MarkerCommandIntervalMs = 150;
            configurationChanged = true;
        }

        if (configuration.Version < 3 || !Enum.IsDefined(configuration.MarkerTargetMode))
        {
            configuration.MarkerTargetMode = MarkerTargetMode.SelfOnly;
            configuration.CustomMarkerRoleMask = 0;
            configurationChanged = true;
        }

        gameMarkerProvider = new NativeMarkerProvider(
            SigScanner,
            () => configuration.MarkerCommandIntervalMs,
            () => ObjectTable.LocalPlayer?.EntityId,
            partySlot => currentParty.FirstOrDefault(member => member.PartyIndex + 1 == partySlot)?.EntityId);
        worldTelegraphRenderer = new WorldTelegraphRenderer(GameGui);
        if (configuration.EnableExperimentalPartyMarkers && !gameMarkerProvider.IsAvailable)
        {
            configuration.EnableExperimentalPartyMarkers = false;
            configurationChanged = true;
        }

        if (configurationChanged)
        {
            configuration.Version = 3;
            PluginInterface.SavePluginConfig(configuration);
        }

        activeMarkerProvider = dryRunMarkerProvider;
        captureRecorder = new CaptureRecorder(PluginInterface.GetPluginConfigDirectory());
        lastTerritoryId = ClientState.TerritoryType;

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "打开控制台；capture start/stop 开始或导出脱敏采集；roles 重新识别职责",
        });
        PluginInterface.UiBuilder.Draw += Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleWindow;
        PluginInterface.UiBuilder.OpenMainUi += ToggleWindow;
        Framework.Update += OnFrameworkUpdate;
        DutyState.DutyWiped += OnDutyWiped;
        DutyState.DutyRecommenced += OnDutyRecommenced;
        DutyState.DutyCompleted += OnDutyCompleted;

        RefreshPartyRoles(force: true);
        Log.Information("VedaMarker native marker provider: {Status}", gameMarkerProvider.AvailabilityStatus);
        InstallActionEffectHook();
        InstallMapEffectHook();
    }

    public void Dispose()
    {
        try
        {
            DisarmController("插件卸载", immediateCleanup: true);
        }
        catch (Exception exception)
        {
            Log.Error(exception, "VedaMarker unload marker cleanup failed");
        }

        mapEffectHook?.Dispose();
        actionEffectHook?.Dispose();
        captureRecorder.Dispose();
        DutyState.DutyCompleted -= OnDutyCompleted;
        DutyState.DutyRecommenced -= OnDutyRecommenced;
        DutyState.DutyWiped -= OnDutyWiped;
        Framework.Update -= OnFrameworkUpdate;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleWindow;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleWindow;
        PluginInterface.UiBuilder.Draw -= Draw;
        CommandManager.RemoveHandler(CommandName);
    }

    private unsafe void InstallActionEffectHook()
    {
        try
        {
            actionEffectHook = Interop.HookFromAddress<ReceiveActionEffectDelegate>(
                ActionEffectHandler.Addresses.Receive.Value,
                OnReceiveActionEffect);
            actionEffectHook.Enable();
            Log.Information("VedaMarker ActionEffect capture hook enabled");
        }
        catch (Exception exception)
        {
            status = "ActionEffect 采集钩子未启用；状态与读条采集仍可使用";
            Log.Error(exception, "Unable to install VedaMarker ActionEffect capture hook");
        }
    }

    private unsafe void OnReceiveActionEffect(
        uint casterEntityId,
        Character* caster,
        Vector3* targetPosition,
        ActionEffectHandler.Header* header,
        ActionEffectHandler.TargetEffects* effects,
        GameObjectId* targetEntityIds)
    {
        CaptureActionMetadata? action = null;
        CapturePosition? sourcePosition = null;
        float? sourceRotation = null;
        CapturePosition? effectTargetPosition = null;
        float? actionRotation = null;
        IReadOnlyList<CaptureActionTarget> targets = Array.Empty<CaptureActionTarget>();

        if (captureRecorder.IsActive && header != null)
        {
            try
            {
                action = ResolveActionMetadata(header->ActionId);
                var source = ObjectTable.SearchByEntityId(casterEntityId);
                if (source is not null)
                {
                    sourcePosition = ToCapturePosition(source.Position);
                    sourceRotation = source.Rotation;
                }

                if (targetPosition != null)
                {
                    effectTargetPosition = ToCapturePosition(*targetPosition);
                }

                actionRotation = (header->RotationInt / 65535f * MathF.Tau) - MathF.PI;
                var targetCount = Math.Min((int)header->NumTargets, 32);
                var observedTargets = new List<CaptureActionTarget>(targetCount);
                for (var index = 0; index < targetCount; index++)
                {
                    var targetId = targetEntityIds == null ? 0u : targetEntityIds[index].ObjectId;
                    if (targetId == 0)
                    {
                        continue;
                    }

                    var target = ObjectTable.SearchByEntityId(targetId);
                    observedTargets.Add(new CaptureActionTarget(
                        targetId,
                        target is null ? null : ToCapturePosition(target.Position)));
                }

                targets = observedTargets;
            }
            catch (Exception exception)
            {
                Log.Error(exception, "Unable to snapshot VedaMarker ActionEffect capture payload");
            }
        }

        actionEffectHook!.Original(casterEntityId, caster, targetPosition, header, effects, targetEntityIds);
        if (header != null && action is not null)
        {
            try
            {
                captureRecorder.RecordActionEffect(
                    casterEntityId,
                    header->ActionId,
                    action,
                    sourcePosition,
                    sourceRotation,
                    effectTargetPosition,
                    actionRotation,
                    targets);
            }
            catch (Exception exception)
            {
                Log.Error(exception, "Unable to write VedaMarker ActionEffect capture event");
            }
        }
    }

    private unsafe void OnApplyMapEffect(
        ContentDirector* director,
        uint index,
        ushort state,
        ushort timelineIndex)
    {
        mapEffectHook!.Original(director, index, state, timelineIndex);
        if (captureRecorder.IsActive)
        {
            try
            {
                captureRecorder.RecordMapEffect(index, state, timelineIndex, SnapshotRelevantWorldObjects());
            }
            catch (Exception exception)
            {
                Log.Error(exception, "Unable to write VedaMarker MapEffect capture event");
            }
        }
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        var now = Environment.TickCount64;
        if (lastTerritoryId != ClientState.TerritoryType)
        {
            lastTerritoryId = ClientState.TerritoryType;
            DisarmController("区域发生变化，主控已自动停止");
            localAoeSimulationActive = false;
            localAoeStatus = "区域发生变化，本地 AOE 模拟已停止";
            rolesConfirmed = false;
        }

        RefreshPartyRoles(force: false);
        try
        {
            gameMarkerProvider.Tick(now);
            AdvanceMarkerDiagnostic(now);
        }
        catch (Exception exception)
        {
            controllerArmed = false;
            simulationArmed = false;
            simulationWave = 0;
            simulationSoloMode = false;
            markerDiagnosticRunning = false;
            markerDiagnosticSummary = "测试中断：原生游戏标点提交失败";
            automationEngine.Reset();
            currentAssignment = null;
            gameMarkerProvider.Clear();
            status = "原生游戏标点提交失败，主控已停止；仍会继续尝试清理已提交标点";
            Log.Error(exception, "VedaMarker native party marker operation failed");
        }

        if (!captureRecorder.IsActive && !controllerArmed)
        {
            return;
        }

        var interval = Math.Clamp(configuration.CapturePollingIntervalMs, 50, 1000);
        if (now - lastCapturePollAt < interval)
        {
            return;
        }

        lastCapturePollAt = now;
        PollGameState();
    }

    private unsafe void PollGameState()
    {
        try
        {
            var entityRoles = roleCoordinator.Assignments.ToDictionary(
                entry => entry.Value,
                entry => entry.Key);
            var party = currentParty.Select(member =>
            {
                var gameObject = ObjectTable.SearchByEntityId(member.EntityId);
                return new CapturePartyMember(
                    member.PartyIndex,
                    member.EntityId,
                    member.JobId,
                    roleCoordinator.TryGetRole(member.EntityId, out var role) ? role : null,
                    gameObject is null ? null : ToCapturePosition(gameObject.Position),
                    gameObject?.Rotation ?? 0,
                    gameObject?.HitboxRadius ?? 0,
                    gameObject?.IsDead ?? false);
            }).ToArray();

            var statuses = new List<CaptureStatusObservation>();
            var casts = new List<CaptureCastObservation>();
            var forsakenStatuses = new List<ForsakenStatusObservation>();
            foreach (var gameObject in ObjectTable)
            {
                if (gameObject is not IBattleChara battleChara)
                {
                    continue;
                }

                if (entityRoles.TryGetValue(battleChara.EntityId, out var role))
                {
                    foreach (var actorStatus in battleChara.StatusList)
                    {
                        if (actorStatus.StatusId == 0)
                        {
                            continue;
                        }

                        statuses.Add(new CaptureStatusObservation(
                            battleChara.EntityId,
                            actorStatus.StatusId,
                            actorStatus.Param,
                            actorStatus.RemainingTime));
                        if (ForsakenEncounterIds.IsMechanicStatus(actorStatus.StatusId))
                        {
                            forsakenStatuses.Add(new ForsakenStatusObservation(
                                role,
                                actorStatus.StatusId,
                                actorStatus.Param));
                        }
                    }
                }

                if (battleChara.IsCasting && battleChara.CastActionId != 0)
                {
                    var targetId = unchecked((uint)battleChara.CastTargetObjectId);
                    var target = targetId == 0 ? null : ObjectTable.SearchByEntityId(targetId);
                    CapturePosition? targetLocation = null;
                    float? castRotation = null;
                    if (battleChara.Address != 0)
                    {
                        var castInfo = ((Character*)battleChara.Address)->GetCastInfo();
                        if (castInfo != null)
                        {
                            targetLocation = new CapturePosition(
                                castInfo->TargetLocation.X,
                                castInfo->TargetLocation.Y,
                                castInfo->TargetLocation.Z);
                            castRotation = castInfo->Rotation;
                        }
                    }

                    casts.Add(new CaptureCastObservation(
                        battleChara.EntityId,
                        battleChara.CastActionId,
                        ResolveActionMetadata(battleChara.CastActionId),
                        battleChara.CurrentCastTime,
                        battleChara.TotalCastTime,
                        ToCapturePosition(battleChara.Position),
                        battleChara.Rotation,
                        battleChara.HitboxRadius,
                        targetId == 0 ? null : targetId,
                        target is null ? null : ToCapturePosition(target.Position),
                        targetLocation,
                        castRotation));
                }
            }

            if (captureRecorder.IsActive)
            {
                captureRecorder.Observe(
                    ClientState.TerritoryType,
                    Condition[ConditionFlag.InCombat],
                    party,
                    statuses,
                    casts,
                    SnapshotRelevantWorldObjects());
            }

            if (controllerArmed)
            {
                ProcessForsakenAutomation(forsakenStatuses);
            }
        }
        catch (Exception exception)
        {
            DisarmController("状态识别出现异常，主控已停止并清理");
            Log.Error(exception, "VedaMarker state polling failed");
        }
    }

    private unsafe void InstallMapEffectHook()
    {
        try
        {
            mapEffectHook = Interop.HookFromAddress<ApplyMapEffectDelegate>(
                ContentDirector.Addresses.ApplyMapEffect.Value,
                OnApplyMapEffect);
            mapEffectHook.Enable();
            Log.Information("VedaMarker MapEffect capture hook enabled");
        }
        catch (Exception exception)
        {
            status = "MapEffect 采集钩子未启用；其他采集仍可使用";
            Log.Error(exception, "Unable to install VedaMarker MapEffect capture hook");
        }
    }

    private IReadOnlyList<CaptureObjectObservation> SnapshotRelevantWorldObjects()
    {
        var observations = new List<CaptureObjectObservation>();
        foreach (var gameObject in ObjectTable)
        {
            var kind = gameObject.ObjectKind.ToString();
            if (kind is not ("BattleNpc" or "EventObj" or "AreaObject" or "ReactionEventObject"))
            {
                continue;
            }

            observations.Add(new CaptureObjectObservation(
                gameObject.ObjectIndex,
                gameObject.EntityId,
                gameObject.BaseId,
                kind,
                ToCapturePosition(gameObject.Position),
                gameObject.Rotation,
                gameObject.HitboxRadius,
                gameObject.IsDead));
        }

        return observations;
    }

    private CaptureActionMetadata ResolveActionMetadata(uint actionId)
    {
        if (actionMetadataCache.TryGetValue(actionId, out var cached))
        {
            return cached;
        }

        CaptureActionMetadata metadata;
        try
        {
            var row = DataManager.GetExcelSheet<LuminaAction>().GetRow(actionId);
            var name = row.Name.ToString();
            metadata = new CaptureActionMetadata(
                string.IsNullOrWhiteSpace(name) ? null : name,
                Convert.ToUInt32(row.CastType),
                Convert.ToUInt32(row.EffectRange),
                Convert.ToUInt32(row.XAxisModifier));
        }
        catch (Exception exception)
        {
            Log.Debug(exception, "Unable to resolve Action sheet row {ActionId}", actionId);
            metadata = new CaptureActionMetadata(null, null, null, null);
        }

        actionMetadataCache[actionId] = metadata;
        return metadata;
    }

    private static CapturePosition ToCapturePosition(Vector3 position) =>
        new(position.X, position.Y, position.Z);

    private void ProcessForsakenAutomation(IReadOnlyList<ForsakenStatusObservation> statuses)
    {
        if (ClientState.TerritoryType != ForsakenEncounterIds.Territory)
        {
            status = "主控已启动，等待进入绝妖星 P2 对应区域";
            return;
        }

        var update = automationEngine.Observe(statuses);
        if (!update.Changed)
        {
            return;
        }

        if (update.Assignment is not null)
        {
            var localRole = ResolveLocalRole();
            var targetRoles = ResolveMarkerTargets(localRole);
            var partySlots = BuildPartySlots();
            activeMarkerProvider.Submit(update.Assignment, targetRoles, localRole, partySlots);
            currentAssignment = update.Assignment;
            status = $"{update.Message}；完整八人逻辑已确认，已对 {string.Join('/', targetRoles)} 按清标→新标顺序提交";
        }

        if (update.Completed)
        {
            controllerArmed = false;
            currentAssignment = null;
            activeMarkerProvider.Clear();
            status = $"{update.Message}；标点清理已提交";
        }
    }

    private RoleSlot ResolveLocalRole()
    {
        var localPlayer = ObjectTable.LocalPlayer
            ?? throw new MarkerAssignmentException("当前无法识别插件使用者本人。");
        if (!roleCoordinator.TryGetRole(localPlayer.EntityId, out var localRole))
        {
            throw new MarkerAssignmentException("插件使用者本人未包含在已确认的八人职责中。");
        }

        return localRole;
    }

    private IReadOnlyList<RoleSlot> ResolveMarkerTargets(RoleSlot localRole) =>
        configuration.MarkerTargetMode switch
        {
            MarkerTargetMode.SelfOnly => [localRole],
            MarkerTargetMode.AllRoles => RoleOrder,
            MarkerTargetMode.CustomRoles => RoleOrder
                .Where(role => (configuration.CustomMarkerRoleMask & (1 << (int)role)) != 0)
                .ToArray(),
            _ => throw new MarkerAssignmentException("标点目标模式无效。"),
        };

    private IReadOnlyDictionary<RoleSlot, int> BuildPartySlots()
    {
        var result = new Dictionary<RoleSlot, int>();
        foreach (var entry in roleCoordinator.Assignments)
        {
            var member = currentParty.SingleOrDefault(candidate => candidate.EntityId == entry.Value)
                ?? throw new MarkerAssignmentException($"{entry.Key} 对应队员已不在队伍中。");
            result[entry.Key] = member.PartyIndex + 1;
        }

        return result;
    }

    private unsafe LocalMarkerObservation ReadLocalMarker()
    {
        var localPlayer = ObjectTable.LocalPlayer;
        var markingController = MarkingController.Instance();
        if (localPlayer is null || markingController == null)
        {
            return new LocalMarkerObservation(false, null);
        }

        foreach (var entry in MarkerMemoryIndices)
        {
            if (markingController->Markers[entry.Value].ObjectId == localPlayer.EntityId)
            {
                return new LocalMarkerObservation(true, entry.Key);
            }
        }

        return new LocalMarkerObservation(true, null);
    }

    private string ReadLocalMarkerStatus() => FormatMarkerObservation(ReadLocalMarker());

    private void StartMarkerDiagnostic()
    {
        markerDiagnosticResults.Clear();
        markerDiagnosticResults.AddRange(MarkerDiagnosticOrder.Select(marker => new MarkerDiagnosticResult(marker)));
        markerDiagnosticRunning = true;
        markerDiagnosticIndex = 0;
        markerDiagnosticPhase = MarkerDiagnosticPhase.WaitingForMarker;
        markerDiagnosticObservationDeadline = 0;
        markerDiagnosticSummary = $"测试 1/{MarkerDiagnosticOrder.Length}：正在验证{MarkerDisplayName(MarkerDiagnosticOrder[0])}";
        gameMarkerProvider.SubmitDiagnosticSelfMarker(MarkerDiagnosticOrder[0]);
    }

    private void AdvanceMarkerDiagnostic(long now)
    {
        if (!markerDiagnosticRunning || gameMarkerProvider.PendingCommandCount != 0)
        {
            return;
        }

        if (markerDiagnosticObservationDeadline == 0)
        {
            markerDiagnosticObservationDeadline = now + MarkerDiagnosticObservationTimeoutMs;
        }

        var expected = MarkerDiagnosticOrder[markerDiagnosticIndex];
        var result = markerDiagnosticResults[markerDiagnosticIndex];
        var observation = ReadLocalMarker();
        if (markerDiagnosticPhase == MarkerDiagnosticPhase.WaitingForMarker)
        {
            if (observation.Available && observation.Marker == expected)
            {
                result.MarkerPassed = true;
                result.MarkerObserved = FormatMarkerObservation(observation);
                BeginMarkerDiagnosticClear(expected);
            }
            else if (now >= markerDiagnosticObservationDeadline)
            {
                result.MarkerPassed = false;
                result.MarkerObserved = FormatMarkerObservation(observation);
                BeginMarkerDiagnosticClear(expected);
            }

            return;
        }

        if (observation.Available && observation.Marker is null)
        {
            result.ClearPassed = true;
            result.ClearObserved = FormatMarkerObservation(observation);
            ContinueMarkerDiagnostic();
        }
        else if (now >= markerDiagnosticObservationDeadline)
        {
            result.ClearPassed = false;
            result.ClearObserved = FormatMarkerObservation(observation);
            ContinueMarkerDiagnostic();
        }
    }

    private void BeginMarkerDiagnosticClear(PartyMarker marker)
    {
        markerDiagnosticPhase = MarkerDiagnosticPhase.WaitingForClear;
        markerDiagnosticObservationDeadline = 0;
        markerDiagnosticSummary =
            $"测试 {markerDiagnosticIndex + 1}/{MarkerDiagnosticOrder.Length}：正在验证{MarkerDisplayName(marker)}清除";
        gameMarkerProvider.SubmitDiagnosticSelfClear();
    }

    private void ContinueMarkerDiagnostic()
    {
        markerDiagnosticIndex++;
        if (markerDiagnosticIndex >= MarkerDiagnosticOrder.Length)
        {
            markerDiagnosticRunning = false;
            markerDiagnosticPhase = MarkerDiagnosticPhase.Idle;
            markerDiagnosticObservationDeadline = 0;
            var passed = markerDiagnosticResults.Count(result =>
                result.MarkerPassed == true && result.ClearPassed == true);
            markerDiagnosticSummary = passed == MarkerDiagnosticOrder.Length
                ? "测试完成：8 种标点与逐个清除全部成功"
                : $"测试完成：{passed}/8 项完全成功；请查看下方失败项";
            status = markerDiagnosticSummary;
            return;
        }

        var marker = MarkerDiagnosticOrder[markerDiagnosticIndex];
        markerDiagnosticPhase = MarkerDiagnosticPhase.WaitingForMarker;
        markerDiagnosticObservationDeadline = 0;
        markerDiagnosticSummary =
            $"测试 {markerDiagnosticIndex + 1}/{MarkerDiagnosticOrder.Length}：正在验证{MarkerDisplayName(marker)}";
        gameMarkerProvider.SubmitDiagnosticSelfMarker(marker);
    }

    private void StopMarkerDiagnostic(string reason, bool immediateCleanup = false)
    {
        if (!markerDiagnosticRunning)
        {
            return;
        }

        markerDiagnosticRunning = false;
        markerDiagnosticPhase = MarkerDiagnosticPhase.Idle;
        markerDiagnosticObservationDeadline = 0;
        markerDiagnosticSummary = reason;
        gameMarkerProvider.Clear(immediateCleanup);
    }

    private static string FormatMarkerObservation(LocalMarkerObservation observation) =>
        !observation.Available
            ? "无法读取"
            : observation.Marker is { } marker
                ? MarkerDisplayName(marker)
                : "无";

    private void RefreshPartyRoles(bool force)
    {
        var observed = ReadCurrentParty();
        var signature = string.Join('|', observed.Select(member =>
            $"{member.PartyIndex}:{member.EntityId}:{member.JobId}"));
        if (!force && signature == partySignature)
        {
            return;
        }

        currentParty = observed;
        partySignature = signature;
        rolesConfirmed = false;
        DisarmController("队伍构成变化，主控已停止");
        if (roleCoordinator.Refresh(currentParty))
        {
            status = roleCoordinator.LastStatus;
        }
        else
        {
            status = $"自动识别失败：{roleCoordinator.LastStatus}";
        }
    }

    private static IReadOnlyList<RuntimePartyMember> ReadCurrentParty()
    {
        var members = new List<RuntimePartyMember>();
        for (var index = 0; index < PartyList.Length; index++)
        {
            var member = PartyList[index];
            if (member is null || member.EntityId == 0)
            {
                continue;
            }

            members.Add(new RuntimePartyMember(
                index,
                member.EntityId,
                member.ClassJob.RowId,
                member.Name.TextValue));
        }

        return members;
    }

    private void Draw()
    {
        if (localAoeSimulationActive)
        {
            try
            {
                worldTelegraphRenderer.Draw(
                    localAoeCenter,
                    ForsakenTelegraphPlanner.Create(localAoeWave, localAoeDirection8));
            }
            catch (Exception exception)
            {
                localAoeSimulationActive = false;
                localAoeStatus = $"本地 AOE 绘制失败：{exception.Message}";
                Log.Error(exception, "VedaMarker local AOE simulation draw failed");
            }
        }

        if (!showWindow)
        {
            return;
        }

        ImGui.SetNextWindowSize(new Vector2(720, 720), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("VedaMarker 控制台###VedaMarkerMain", ref showWindow))
        {
            ImGui.End();
            return;
        }

        DrawSafetyPanel();
        DrawRolePanel();
        DrawSimulationPanel();
        DrawLocalAoeSimulationPanel();
        DrawAutomationPanel();
        DrawCapturePanel();
        ImGui.End();
    }

    private void DrawSafetyPanel()
    {
        DrawSectionHeader("主控状态");
        var anyControllerArmed = controllerArmed || simulationArmed;
        var controllerStatus = simulationArmed
            ? $"{activeMarkerProvider.Name}模拟测试已手动启动（Wave {simulationWave}/8）"
            : controllerArmed
                ? $"{activeMarkerProvider.Name}主控已手动启动"
                : "主控未启动";
        ImGui.TextColored(
            anyControllerArmed ? new Vector4(1f, 0.75f, 0.25f, 1f) : new Vector4(0.55f, 0.9f, 0.55f, 1f),
            controllerStatus);
        ImGui.TextWrapped("整场技能与位置/方向采集已扩展；本地 AOE 模拟可单人验证，但尚未接入绝妖星自动触发。");

        var safetyControlsLocked = anyControllerArmed || markerDiagnosticRunning;
        if (safetyControlsLocked)
        {
            ImGui.BeginDisabled();
        }
        var experimentalMarkers = configuration.EnableExperimentalPartyMarkers;
        if (!gameMarkerProvider.IsAvailable)
        {
            ImGui.BeginDisabled();
        }
        if (ImGui.Checkbox("启用原生真实团队标点（实验，默认关闭）", ref experimentalMarkers))
        {
            configuration.EnableExperimentalPartyMarkers = experimentalMarkers;
            if (!experimentalMarkers)
            {
                gameMarkerProvider.Clear();
            }

            PluginInterface.SavePluginConfig(configuration);
            status = experimentalMarkers
                ? "真实团队标点已允许；正式主控需核对完整八人职责，仅本人模拟可单人手动启动"
                : "已切回 Dry-run 模式";
        }
        if (!gameMarkerProvider.IsAvailable)
        {
            ImGui.EndDisabled();
        }
        ImGui.TextWrapped($"原生标点状态：{gameMarkerProvider.AvailabilityStatus}");
        DrawMarkerTargetConfiguration();
        if (safetyControlsLocked)
        {
            ImGui.EndDisabled();
        }

        if (configuration.EnableExperimentalPartyMarkers)
        {
            ImGui.TextColored(new Vector4(1f, 0.55f, 0.25f, 1f),
                "默认只标本人；选择自定义职责或全队后，会实际清除并标记所选队员，且 Party Target Marker 对全队可见。 ");
            ImGui.TextWrapped("一键自检会依次真实标记本人并逐个清除，标点对全队可见；建议在进机制前测试：");
            if (anyControllerArmed || markerDiagnosticRunning)
            {
                ImGui.BeginDisabled();
            }

            if (ImGui.Button("一键测试全部8种标点与清除"))
            {
                StartMarkerDiagnostic();
                status = "已开始逐项验证本人全部8种标点与清除";
            }

            ImGui.SameLine();
            if (ImGui.Button("清除本人测试标点"))
            {
                gameMarkerProvider.SubmitDiagnosticSelfClear();
                status = "已排队清除本人测试标点";
            }

            if (anyControllerArmed || markerDiagnosticRunning)
            {
                ImGui.EndDisabled();
            }

            if (markerDiagnosticRunning)
            {
                ImGui.SameLine();
                if (ImGui.Button("停止测试并清除"))
                {
                    StopMarkerDiagnostic("测试已由用户停止，并已排队清除本人标点");
                    status = markerDiagnosticSummary;
                }
            }

            ImGui.TextUnformatted($"游戏内当前本人标记：{ReadLocalMarkerStatus()}");
            ImGui.TextWrapped(markerDiagnosticSummary);
            foreach (var result in markerDiagnosticResults.Where(result =>
                         result.MarkerPassed.HasValue || result.ClearPassed.HasValue))
            {
                var markerResult = result.MarkerPassed == true
                    ? "成功"
                    : $"失败（读到：{result.MarkerObserved}）";
                var clearResult = result.ClearPassed switch
                {
                    true => "成功",
                    false => $"失败（读到：{result.ClearObserved}）",
                    null => "等待中",
                };
                ImGui.TextUnformatted($"{MarkerDisplayName(result.Marker)}：标记{markerResult}；清除{clearResult}");
            }

            if (!string.IsNullOrWhiteSpace(gameMarkerProvider.LastOperation))
            {
                ImGui.TextWrapped(
                    $"最近操作：{gameMarkerProvider.LastOperation}（累计 {gameMarkerProvider.SubmittedOperationCount} 次原生调用）");
            }
        }

        ImGui.TextWrapped($"Marker Provider：{activeMarkerProvider.Name}；待处理操作：{gameMarkerProvider.PendingCommandCount}");

        var canArm = rolesConfirmed
            && roleCoordinator.Assignments.Count == 8
            && HasConfiguredMarkerTargets()
            && (!configuration.EnableExperimentalPartyMarkers || gameMarkerProvider.IsAvailable)
            && !markerDiagnosticRunning
            && !simulationArmed
            && !controllerArmed;
        if (!canArm)
        {
            ImGui.BeginDisabled();
        }
        var armButton = configuration.EnableExperimentalPartyMarkers
            ? "手动启动真实标点（实验）"
            : "手动启动 Dry-run 主控";
        if (ImGui.Button(armButton))
        {
            automationEngine.Reset();
            currentAssignment = null;
            activeMarkerProvider = configuration.EnableExperimentalPartyMarkers
                ? gameMarkerProvider
                : dryRunMarkerProvider;
            controllerArmed = true;
            lastCapturePollAt = 0;
            status = configuration.EnableExperimentalPartyMarkers
                ? "真实标点已启动；等待遗弃末世开场八人点名"
                : "Dry-run 主控已启动；等待遗弃末世开场八人点名";
        }
        if (!canArm)
        {
            ImGui.EndDisabled();
        }

        ImGui.SameLine();
        if (!anyControllerArmed)
        {
            ImGui.BeginDisabled();
        }
        if (ImGui.Button("停止并清理"))
        {
            DisarmController(simulationArmed ? "模拟测试已停止并提交清理" : "用户手动停止主控");
        }
        if (!anyControllerArmed)
        {
            ImGui.EndDisabled();
        }

        ImGui.TextWrapped(status);
    }

    private void DrawMarkerTargetConfiguration()
    {
        var currentLabel = MarkerTargetModeLabel(configuration.MarkerTargetMode);
        if (ImGui.BeginCombo("标点目标范围", currentLabel))
        {
            foreach (var mode in Enum.GetValues<MarkerTargetMode>())
            {
                if (ImGui.Selectable(MarkerTargetModeLabel(mode), mode == configuration.MarkerTargetMode))
                {
                    configuration.MarkerTargetMode = mode;
                    PluginInterface.SavePluginConfig(configuration);
                }
            }

            ImGui.EndCombo();
        }

        if (configuration.MarkerTargetMode != MarkerTargetMode.CustomRoles)
        {
            return;
        }

        ImGui.TextWrapped("选择需要由本插件清除并标记的职责：");
        foreach (var role in RoleOrder)
        {
            var selected = (configuration.CustomMarkerRoleMask & (1 << (int)role)) != 0;
            if (ImGui.Checkbox($"{role}##MarkerTarget-{role}", ref selected))
            {
                if (selected)
                {
                    configuration.CustomMarkerRoleMask |= 1 << (int)role;
                }
                else
                {
                    configuration.CustomMarkerRoleMask &= ~(1 << (int)role);
                }

                PluginInterface.SavePluginConfig(configuration);
            }

            if (role is not RoleSlot.H2 and not RoleSlot.D4)
            {
                ImGui.SameLine();
            }
        }

        if (configuration.CustomMarkerRoleMask == 0)
        {
            ImGui.TextColored(new Vector4(1f, 0.55f, 0.25f, 1f), "至少选择一个职责后才能启动。 ");
        }
    }

    private bool HasConfiguredMarkerTargets() =>
        configuration.MarkerTargetMode != MarkerTargetMode.CustomRoles
        || configuration.CustomMarkerRoleMask != 0;

    private static string MarkerTargetModeLabel(MarkerTargetMode mode) => mode switch
    {
        MarkerTargetMode.SelfOnly => "仅自己（默认）",
        MarkerTargetMode.CustomRoles => "自定义职责",
        MarkerTargetMode.AllRoles => "全队八人",
        _ => "未知",
    };

    private void RefreshSoloSimulationRoleRecommendation()
    {
        var localPlayer = ObjectTable.LocalPlayer;
        if (localPlayer is null || localPlayer.ClassJob.RowId == soloSimulationJobId)
        {
            return;
        }

        soloSimulationJobId = localPlayer.ClassJob.RowId;
        soloSimulationRole = JobCatalog.Category(soloSimulationJobId) switch
        {
            CombatRoleCategory.Tank => RoleSlot.MT,
            CombatRoleCategory.Healer => RoleSlot.H1,
            CombatRoleCategory.Melee => RoleSlot.D1,
            CombatRoleCategory.PhysicalRanged => RoleSlot.D3,
            CombatRoleCategory.MagicalRanged => RoleSlot.D4,
            _ => RoleSlot.D1,
        };
    }

    private void DrawSimulationPanel()
    {
        DrawSectionHeader("跨副本模拟测试（O8S 可用）");
        ImGui.TextWrapped(
            "模拟测试不读取当前副本机制，只用正式奇偶轮分配器生成完整八人标点。每轮必须手动提交，并按正式流程先清标再标；不会执行移动或战斗操作。");

        if (!simulationArmed)
        {
            RefreshSoloSimulationRoleRecommendation();
            var hasConfirmedParty = rolesConfirmed && roleCoordinator.Assignments.Count == 8;
            var soloMode = !hasConfirmedParty && configuration.MarkerTargetMode == MarkerTargetMode.SelfOnly;
            var canStart = (hasConfirmedParty || soloMode)
                && ObjectTable.LocalPlayer is not null
                && HasConfiguredMarkerTargets()
                && (!configuration.EnableExperimentalPartyMarkers || gameMarkerProvider.IsAvailable)
                && !controllerArmed
                && !markerDiagnosticRunning;

            if (soloMode)
            {
                ImGui.TextColored(new Vector4(0.55f, 0.9f, 0.55f, 1f),
                    "单人模式：仅操作本人，不要求八人队伍或职责确认；自定义职责和全队模式仍要求完整八人队伍。");
                if (ImGui.BeginCombo("本人模拟职责", soloSimulationRole.ToString()))
                {
                    foreach (var role in RoleOrder)
                    {
                        if (ImGui.Selectable(role.ToString(), role == soloSimulationRole))
                        {
                            soloSimulationRole = role;
                        }
                    }

                    ImGui.EndCombo();
                }
            }

            if (!canStart)
            {
                ImGui.BeginDisabled();
            }

            var label = configuration.EnableExperimentalPartyMarkers
                ? soloMode
                    ? "手动启动单人模拟（真实标点）"
                    : "手动启动模拟测试（真实标点）"
                : soloMode
                    ? "手动启动单人模拟（Dry-run）"
                    : "手动启动模拟测试（Dry-run）";
            if (ImGui.Button(label))
            {
                var selectedSimulationRole = soloMode ? soloSimulationRole : ResolveLocalRole();
                automationEngine.Reset();
                currentAssignment = null;
                activeMarkerProvider = configuration.EnableExperimentalPartyMarkers
                    ? gameMarkerProvider
                    : dryRunMarkerProvider;
                simulationArmed = true;
                simulationWave = 0;
                simulationSoloMode = soloMode;
                simulationLocalRole = selectedSimulationRole;
                status = soloMode
                    ? $"单人模拟已启动；本人按 {simulationLocalRole} 测试，请手动提交模拟第1轮"
                    : "模拟测试已启动；请手动提交模拟第1轮";
            }

            if (!canStart)
            {
                ImGui.EndDisabled();
            }

            if (!hasConfirmedParty && configuration.MarkerTargetMode != MarkerTargetMode.SelfOnly)
            {
                ImGui.TextColored(new Vector4(1f, 0.55f, 0.25f, 1f),
                    "自定义职责或全队模拟仍需要在完整八人队伍中确认职责；单人测试请把标点目标范围改为‘仅自己’。");
            }

            return;
        }

        var operationsPending = activeMarkerProvider.ProducesGameMarkers
            && gameMarkerProvider.PendingCommandCount != 0;
        var canSubmitNext = simulationWave < 8 && !operationsPending;
        if (!canSubmitNext)
        {
            ImGui.BeginDisabled();
        }

        if (ImGui.Button(simulationWave < 8
                ? $"提交模拟第 {simulationWave + 1} 轮"
                : "八轮已全部提交"))
        {
            SubmitNextSimulationWave();
        }

        if (!canSubmitNext)
        {
            ImGui.EndDisabled();
        }

        ImGui.SameLine();
        if (ImGui.Button("结束模拟并清理"))
        {
            DisarmController("模拟测试已结束并提交清理");
            return;
        }

        ImGui.TextWrapped(operationsPending
            ? $"模拟 Wave {simulationWave}/8：正在执行清标→新标队列"
            : simulationWave == 8
                ? "模拟八轮已全部提交；观察完成后请点击结束模拟并清理"
                : $"模拟 Wave {simulationWave}/8：可手动提交下一轮");

        if (currentAssignment is null)
        {
            return;
        }

        if (ImGui.BeginTable("SimulationAssignment", 2, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
        {
            ImGui.TableSetupColumn("职责");
            ImGui.TableSetupColumn("模拟标点");
            ImGui.TableHeadersRow();
            foreach (var role in RoleOrder)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(role.ToString());
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(MarkerDisplayName(currentAssignment.Markers[role]));
            }

            ImGui.EndTable();
        }
    }

    private void DrawLocalAoeSimulationPanel()
    {
        DrawSectionHeader("本地 AOE 范围模拟（任意副本可用）");
        ImGui.TextWrapped(
            "只在本机绘制，不会给队友显示，也不会操作角色。以启动时本人位置为模拟场中，按奇偶轮显示站位名、30m/90° 扇形和 5m 钢铁范围。方向 0-7 每档旋转 45°。");

        if (!localAoeSimulationActive)
        {
            var canStart = ObjectTable.LocalPlayer is not null;
            if (!canStart)
            {
                ImGui.BeginDisabled();
            }

            if (ImGui.Button("以本人当前位置启动 AOE 模拟"))
            {
                localAoeCenter = ObjectTable.LocalPlayer!.Position;
                localAoeWave = 1;
                localAoeDirection8 = 0;
                localAoeSimulationActive = true;
                localAoeStatus = "本地 AOE 模拟已启动：Wave 1 / Direction 0";
            }

            if (!canStart)
            {
                ImGui.EndDisabled();
            }

            ImGui.TextWrapped(localAoeStatus);
            return;
        }

        ImGui.SliderInt("模拟轮次", ref localAoeWave, 1, 8);
        ImGui.SliderInt("方向 Direction8", ref localAoeDirection8, 0, 7);
        if (ImGui.Button("把模拟中心移到本人当前位置") && ObjectTable.LocalPlayer is { } localPlayer)
        {
            localAoeCenter = localPlayer.Position;
            localAoeStatus = $"已重新定位模拟中心：Wave {localAoeWave} / Direction {localAoeDirection8}";
        }

        ImGui.SameLine();
        if (ImGui.Button("停止 AOE 模拟"))
        {
            localAoeSimulationActive = false;
            localAoeStatus = "本地 AOE 模拟已由用户停止";
            return;
        }

        var plan = ForsakenTelegraphPlanner.Create(localAoeWave, localAoeDirection8);
        ImGui.TextWrapped(
            $"当前：Wave {localAoeWave} / Direction {localAoeDirection8}；{plan.Stations.Count} 个站位，{plan.Telegraphs.Count} 个已确认范围。关闭控制台后仍会继续显示。");
    }

    private void SubmitNextSimulationWave()
    {
        try
        {
            if (!simulationArmed || simulationWave >= 8)
            {
                throw new MarkerAssignmentException("模拟测试尚未启动或八轮已经完成。");
            }

            if (activeMarkerProvider.ProducesGameMarkers && gameMarkerProvider.PendingCommandCount != 0)
            {
                throw new MarkerAssignmentException("上一轮原生标点操作仍在处理中。");
            }

            var wave = simulationWave + 1;
            var assignment = ForsakenSimulationAssignmentFactory.Create(wave);
            var localRole = simulationLocalRole;
            IReadOnlyList<RoleSlot> targetRoles = simulationSoloMode
                ? [localRole]
                : ResolveMarkerTargets(localRole);
            IReadOnlyDictionary<RoleSlot, int> partySlots = simulationSoloMode
                ? new Dictionary<RoleSlot, int>()
                : BuildPartySlots();
            activeMarkerProvider.Submit(assignment, targetRoles, localRole, partySlots);
            simulationWave = wave;
            currentAssignment = assignment;
            status = $"模拟第 {wave} 轮已按清标→新标顺序提交到 {string.Join('/', targetRoles)}";
        }
        catch (Exception exception)
        {
            DisarmController("模拟测试提交失败，已停止并尝试清理");
            Log.Error(exception, "VedaMarker simulation marker submission failed");
        }
    }

    private void DrawAutomationPanel()
    {
        DrawSectionHeader("遗弃末世识别结果");
        var snapshot = automationEngine.Snapshot;
        ImGui.TextWrapped(snapshot.Status == ForsakenEncounterStatus.Inactive
            ? "等待开场八人点名（建议在遗弃末世读条前启动）"
            : $"当前：Wave {snapshot.CurrentWave} / {snapshot.Status}");

        if (currentAssignment is null || snapshot.Players.Count != 8)
        {
            return;
        }

        if (!ImGui.BeginTable("ForsakenAssignment", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
        {
            return;
        }

        ImGui.TableSetupColumn("职责");
        ImGui.TableSetupColumn("组别");
        ImGui.TableSetupColumn("点名");
        ImGui.TableSetupColumn("标点");
        ImGui.TableHeadersRow();
        foreach (var role in RoleOrder)
        {
            var player = snapshot.Players[role];
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(role.ToString());
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(player.InitialGroup == InitialGroup.InitialTower ? "初始踩塔" : "初始待机");
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(MechanicName(player.CurrentMechanic));
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(currentAssignment.Markers[role].ToString());
        }

        ImGui.EndTable();
    }

    private void DrawRolePanel()
    {
        DrawSectionHeader("八人职责（自动识别后必须人工确认）");
        if (ImGui.Button("重新自动识别"))
        {
            rolesConfirmed = false;
            DisarmController("重新识别职责，主控已停止");
            status = roleCoordinator.Refresh(currentParty)
                ? roleCoordinator.LastStatus
                : $"自动识别失败：{roleCoordinator.LastStatus}";
        }

        if (roleCoordinator.Assignments.Count == 8
            && ImGui.BeginTable("RoleAssignments", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
        {
            ImGui.TableSetupColumn("职责");
            ImGui.TableSetupColumn("Pair");
            ImGui.TableSetupColumn("职业");
            ImGui.TableSetupColumn("队员（可手动换位）");
            ImGui.TableHeadersRow();
            foreach (var role in RoleOrder)
            {
                DrawRoleRow(role);
            }
            ImGui.EndTable();
        }
        else if (roleCoordinator.Assignments.Count != 8)
        {
            ImGui.TextWrapped(roleCoordinator.LastStatus);
        }

        var previousConfirmation = rolesConfirmed;
        ImGui.Checkbox("我已核对当前八人职责", ref rolesConfirmed);
        if (previousConfirmation != rolesConfirmed && rolesConfirmed)
        {
            status = "职责已确认，可以手动启动 Dry-run 主控";
        }
    }

    private void DrawRoleRow(RoleSlot role)
    {
        roleCoordinator.Assignments.TryGetValue(role, out var entityId);
        var selected = currentParty.FirstOrDefault(member => member.EntityId == entityId);
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(role.ToString());
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(role.Pair().ToString());
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(selected is null ? "-" : JobName(selected.JobId));
        ImGui.TableNextColumn();

        var preview = selected is null ? "未分配" : $"{selected.DisplayName}  [{JobName(selected.JobId)}]";
        if (!ImGui.BeginCombo($"##Role-{role}", preview))
        {
            return;
        }

        foreach (var member in currentParty.OrderBy(member => member.PartyIndex))
        {
            var isSelected = member.EntityId == entityId;
            if (ImGui.Selectable($"{member.DisplayName}  [{JobName(member.JobId)}]##{role}-{member.EntityId}", isSelected))
            {
                roleCoordinator.Assign(role, member.EntityId);
                rolesConfirmed = false;
                DisarmController("职责已手动调整，主控已停止");
                status = roleCoordinator.LastStatus;
            }
        }

        ImGui.EndCombo();
    }

    private void DrawCapturePanel()
    {
        DrawSectionHeader("采集与诊断");
        ImGui.TextWrapped("脱敏采集需手动开始，不会上传数据。它会记录整场技能 ID/名称、读条、命中目标、坐标/朝向、MapEffect 和 P1/N1 等会话别名，不记录角色名、账号/Content ID、服务器或聊天。建议进本后立即开始，通关或结束采集时再导出 ZIP。");
        if (!captureRecorder.IsActive)
        {
            if (ImGui.Button("开始脱敏采集"))
            {
                StartCapture();
            }
        }
        else
        {
            ImGui.TextColored(new Vector4(1f, 0.75f, 0.25f, 1f),
                $"正在采集：{captureRecorder.EventCount} 条事件");
            if (ImGui.Button("停止并导出 ZIP"))
            {
                StopCapture("user_export");
            }
        }

        if (!string.IsNullOrWhiteSpace(captureRecorder.LastExportPath))
        {
            ImGui.TextWrapped($"最新导出：{captureRecorder.LastExportPath}");
            if (ImGui.Button("复制导出路径"))
            {
                ImGui.SetClipboardText(captureRecorder.LastExportPath);
            }
        }
    }

    private void StartCapture()
    {
        try
        {
            captureRecorder.Start(ClientState.TerritoryType, PluginVersion());
            lastCapturePollAt = 0;
            PollGameState();
            status = "脱敏采集已开始";
        }
        catch (Exception exception)
        {
            status = $"无法开始采集：{exception.Message}";
            Log.Error(exception, "Unable to start VedaMarker capture");
        }
    }

    private void StopCapture(string reason)
    {
        try
        {
            var path = captureRecorder.StopAndExport(reason);
            status = $"采集已导出：{path}";
        }
        catch (Exception exception)
        {
            status = $"无法导出采集：{exception.Message}";
            Log.Error(exception, "Unable to export VedaMarker capture");
        }
    }

    private void OnDutyWiped(IDutyStateEventArgs args)
    {
        captureRecorder.RecordLifecycle("duty_wiped");
        localAoeSimulationActive = false;
        localAoeStatus = "团灭，本地 AOE 模拟已停止";
        DisarmController(rolesConfirmed
            ? "团灭：主控已停止并完成清理；本次副本的职责确认已保留，请手动重新启动"
            : "团灭：主控已停止并完成清理");
    }

    private void OnDutyRecommenced(IDutyStateEventArgs args)
    {
        captureRecorder.RecordLifecycle("duty_recommenced");
        DisarmController(rolesConfirmed
            ? "副本重新开始：本次副本的职责确认已保留，请手动重新启动主控"
            : "副本重新开始：请核对职责并手动启动主控");
    }

    private void OnDutyCompleted(IDutyStateEventArgs args)
    {
        captureRecorder.RecordLifecycle("duty_completed");
        localAoeSimulationActive = false;
        localAoeStatus = "副本完成，本地 AOE 模拟已停止";
        DisarmController("副本完成：主控已停止并完成清理");
    }

    private void DisarmController(string reason, bool immediateCleanup = false)
    {
        StopMarkerDiagnostic(reason, immediateCleanup);
        controllerArmed = false;
        simulationArmed = false;
        simulationWave = 0;
        simulationSoloMode = false;
        currentAssignment = null;
        automationEngine.Reset();
        activeMarkerProvider.Clear(immediateCleanup);
        if (!ReferenceEquals(activeMarkerProvider, gameMarkerProvider))
        {
            gameMarkerProvider.Clear(immediateCleanup);
        }
        status = reason;
    }

    private void OnCommand(string command, string arguments)
    {
        switch (arguments.Trim().ToLowerInvariant())
        {
            case "capture start":
                StartCapture();
                showWindow = true;
                break;
            case "capture stop":
                StopCapture("command_export");
                showWindow = true;
                break;
            case "roles":
                RefreshPartyRoles(force: true);
                showWindow = true;
                break;
            default:
                showWindow = !showWindow;
                break;
        }
    }

    private static string JobName(uint jobId) => jobId switch
    {
        19 => "骑士",
        21 => "战士",
        32 => "暗黑骑士",
        37 => "绝枪战士",
        24 => "白魔法师",
        28 => "学者",
        33 => "占星术士",
        40 => "贤者",
        20 => "武僧",
        22 => "龙骑士",
        30 => "忍者",
        34 => "武士",
        39 => "钐镰客",
        41 => "蝰蛇剑士",
        23 => "吟游诗人",
        31 => "机工士",
        38 => "舞者",
        25 => "黑魔法师",
        27 => "召唤师",
        35 => "赤魔法师",
        42 => "绘灵法师",
        _ => $"Job {jobId}",
    };

    private static string MechanicName(ForsakenMechanic mechanic) => mechanic switch
    {
        ForsakenMechanic.Fan => "扇形",
        ForsakenMechanic.Steel => "钢铁",
        ForsakenMechanic.Share => "分摊",
        ForsakenMechanic.Idle => "待机",
        _ => "未知",
    };

    private static string MarkerDisplayName(PartyMarker marker) => marker switch
    {
        PartyMarker.Attack1 => "攻击1",
        PartyMarker.Attack2 => "攻击2",
        PartyMarker.Attack3 => "攻击3",
        PartyMarker.Attack4 => "攻击4",
        PartyMarker.Bind1 => "锁链1",
        PartyMarker.Bind2 => "锁链2",
        PartyMarker.Ignore1 => "禁止1",
        PartyMarker.Ignore2 => "禁止2",
        _ => "未知",
    };

    private static string PluginVersion() =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.2.9";

    private enum MarkerDiagnosticPhase
    {
        Idle,
        WaitingForMarker,
        WaitingForClear,
    }

    private readonly record struct LocalMarkerObservation(bool Available, PartyMarker? Marker);

    private sealed class MarkerDiagnosticResult(PartyMarker marker)
    {
        public PartyMarker Marker { get; } = marker;

        public bool? MarkerPassed { get; set; }

        public string MarkerObserved { get; set; } = "未检测";

        public bool? ClearPassed { get; set; }

        public string ClearObserved { get; set; } = "未检测";
    }

    private static void DrawSectionHeader(string title)
    {
        ImGui.Separator();
        ImGui.TextUnformatted(title);
        ImGui.Separator();
    }

    private void ToggleWindow() => showWindow = !showWindow;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private unsafe delegate void ReceiveActionEffectDelegate(
        uint casterEntityId,
        Character* caster,
        Vector3* targetPosition,
        ActionEffectHandler.Header* header,
        ActionEffectHandler.TargetEffects* effects,
        GameObjectId* targetEntityIds);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private unsafe delegate void ApplyMapEffectDelegate(
        ContentDirector* director,
        uint index,
        ushort state,
        ushort timelineIndex);
}
