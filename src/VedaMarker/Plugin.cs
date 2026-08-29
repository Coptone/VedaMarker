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
using Dalamud.Interface.Textures;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using VedaMarker.Capture;
using VedaMarker.Core;
using LuminaAction = Lumina.Excel.Sheets.Action;

namespace VedaMarker;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/vedamarker";
    private const int MarkerDiagnosticDisplayDurationMs = 1500;
    private const int MarkerDiagnosticClearDurationMs = 400;
    private static readonly RoleSlot[] RoleOrder = Enum.GetValues<RoleSlot>();
    private static readonly PartyMarker[] MarkerDiagnosticOrder = Enum.GetValues<PartyMarker>();

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
    [PluginService] private static ITextureProvider TextureProvider { get; set; } = null!;
    [PluginService] private static IDataManager DataManager { get; set; } = null!;
    [PluginService] private static IPluginLog Log { get; set; } = null!;

    private readonly PluginConfiguration configuration;
    private readonly PartyRoleCoordinator roleCoordinator = new();
    private readonly ForsakenAutomationEngine automationEngine = new();
    private readonly ForsakenTowerDirectionTracker towerDirectionTracker = new();
    private readonly DryRunMarkerProvider dryRunMarkerProvider = new();
    private readonly LocalMarkerProvider localMarkerProvider;
    private readonly NativeOmenRenderer nativeOmenRenderer;
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
    private bool instanceControllerAuthorized;
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
    private string localAoeStatus = "游戏原生 AOE 测试尚未启动";
    private int activeAutomaticAoeWave;
    private int activeAutomaticAoeDirection8 = -1;

    public Plugin()
    {
        configuration = PluginInterface.GetPluginConfig() as PluginConfiguration ?? new PluginConfiguration();
        var configurationChanged = false;
        if (configuration.Version < 3 || !Enum.IsDefined(configuration.MarkerTargetMode))
        {
            configuration.MarkerTargetMode = MarkerTargetMode.SelfOnly;
            configuration.CustomMarkerRoleMask = 0;
            configurationChanged = true;
        }

        if (configuration.Version < 4)
        {
            configuration.EnableLocalMarkers = true;
            configuration.LocalMarkerScale = 1f;
            configurationChanged = true;
        }

        if (configuration.Version < 5)
        {
            configuration.EnableForsakenNativeTelegraphs = false;
            configurationChanged = true;
        }

        if (!float.IsFinite(configuration.LocalMarkerScale)
            || configuration.LocalMarkerScale is < 0.5f or > 1.5f)
        {
            configuration.LocalMarkerScale = 1f;
            configurationChanged = true;
        }

        localMarkerProvider = new LocalMarkerProvider(
            GameGui,
            ObjectTable,
            TextureProvider,
            () => configuration.LocalMarkerScale,
            () => ObjectTable.LocalPlayer?.EntityId,
            partySlot => currentParty.FirstOrDefault(member => member.PartyIndex + 1 == partySlot)?.EntityId);
        nativeOmenRenderer = new NativeOmenRenderer(SigScanner, Log);

        if (configurationChanged)
        {
            configuration.Version = 5;
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
        Log.Information("VedaMarker local marker provider initialized");
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
        nativeOmenRenderer.Dispose();
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
        var currentForsakenWave = automationEngine.Snapshot.CurrentWave;
        if (controllerArmed
            && ClientState.TerritoryType == ForsakenEncounterIds.Territory
            && towerDirectionTracker.ObserveMapEffect(
                index,
                state,
                currentForsakenWave,
                Environment.TickCount64))
        {
            Log.Information(
                "Forsaken tower direction captured: wave {Wave}, map effect {Index}/{State:X4}/{Timeline:X4}",
                currentForsakenWave,
                index,
                state,
                timelineIndex);
        }

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
            localMarkerProvider.Tick(now);
            AdvanceMarkerDiagnostic(now);
            RefreshAutomaticTelegraphs();
        }
        catch (Exception exception)
        {
            controllerArmed = false;
            instanceControllerAuthorized = false;
            simulationArmed = false;
            simulationWave = 0;
            simulationSoloMode = false;
            markerDiagnosticRunning = false;
            markerDiagnosticSummary = "测试中断：本地标点显示失败";
            automationEngine.Reset();
            towerDirectionTracker.Reset();
            currentAssignment = null;
            localMarkerProvider.Clear();
            nativeOmenRenderer.Clear();
            activeAutomaticAoeWave = 0;
            activeAutomaticAoeDirection8 = -1;
            status = "本地标点显示失败，主控已停止并完成清理";
            Log.Error(exception, "VedaMarker local marker operation failed");
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
                    if (controllerArmed
                        && battleChara.CastActionId == ForsakenEncounterIds.ForsakenAction
                        && !towerDirectionTracker.IsEncounterActive)
                    {
                        towerDirectionTracker.BeginEncounter();
                        Log.Information("Forsaken opening cast identified; tower direction tracking started");
                    }

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
            ClearTelegraphs();
            if (!towerDirectionTracker.IsEncounterActive)
            {
                towerDirectionTracker.BeginEncounter();
            }

            var localRole = ResolveLocalRole();
            var targetRoles = ResolveMarkerTargets(localRole);
            var partySlots = BuildPartySlots();
            activeMarkerProvider.Submit(update.Assignment, targetRoles, localRole, partySlots);
            currentAssignment = update.Assignment;
            var aoeStatus = configuration.EnableForsakenNativeTelegraphs
                ? "；原生 AOE 正在等待本轮双塔方向"
                : string.Empty;
            status = $"{update.Message}；完整八人逻辑已确认，已对 {string.Join('/', targetRoles)} 清除上一轮并显示本地新标{aoeStatus}";
        }

        if (update.Completed)
        {
            DisarmController(
                $"{update.Message}；本地标点和原生 AOE 已清理，本次副本自动恢复授权仍保留",
                preserveInstanceAuthorization: true);
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

    private LocalMarkerObservation ReadLocalMarker()
    {
        var localPlayer = ObjectTable.LocalPlayer;
        if (localPlayer is null)
        {
            return new LocalMarkerObservation(false, null);
        }

        return localMarkerProvider.TryGetMarker(localPlayer.EntityId, out var marker)
            ? new LocalMarkerObservation(true, marker)
            : new LocalMarkerObservation(true, null);
    }

    private string ReadLocalMarkerStatus() => FormatMarkerObservation(ReadLocalMarker());

    private void StartMarkerDiagnostic()
    {
        markerDiagnosticResults.Clear();
        markerDiagnosticResults.AddRange(MarkerDiagnosticOrder.Select(marker => new MarkerDiagnosticResult(marker)));
        markerDiagnosticRunning = true;
        markerDiagnosticIndex = 0;
        markerDiagnosticPhase = MarkerDiagnosticPhase.WaitingForMarker;
        markerDiagnosticObservationDeadline = Environment.TickCount64 + MarkerDiagnosticDisplayDurationMs;
        markerDiagnosticSummary = $"预览 1/{MarkerDiagnosticOrder.Length}：正在显示{MarkerDisplayName(MarkerDiagnosticOrder[0])}";
        localMarkerProvider.SubmitDiagnosticSelfMarker(MarkerDiagnosticOrder[0]);
    }

    private void AdvanceMarkerDiagnostic(long now)
    {
        if (!markerDiagnosticRunning || now < markerDiagnosticObservationDeadline)
        {
            return;
        }

        var expected = MarkerDiagnosticOrder[markerDiagnosticIndex];
        var result = markerDiagnosticResults[markerDiagnosticIndex];
        var observation = ReadLocalMarker();
        if (markerDiagnosticPhase == MarkerDiagnosticPhase.WaitingForMarker)
        {
            result.MarkerPassed = observation.Available && observation.Marker == expected;
            result.MarkerObserved = FormatMarkerObservation(observation);
            BeginMarkerDiagnosticClear(expected, now);
            return;
        }

        result.ClearPassed = observation.Available && observation.Marker is null;
        result.ClearObserved = FormatMarkerObservation(observation);
        ContinueMarkerDiagnostic(now);
    }

    private void BeginMarkerDiagnosticClear(PartyMarker marker, long now)
    {
        markerDiagnosticPhase = MarkerDiagnosticPhase.WaitingForClear;
        markerDiagnosticObservationDeadline = now + MarkerDiagnosticClearDurationMs;
        markerDiagnosticSummary =
            $"预览 {markerDiagnosticIndex + 1}/{MarkerDiagnosticOrder.Length}：正在清除{MarkerDisplayName(marker)}";
        localMarkerProvider.SubmitDiagnosticSelfClear();
    }

    private void ContinueMarkerDiagnostic(long now)
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
                ? "本地逻辑预览完成：8 种图标均已依次显示并清除；请以刚才画面为最终验收"
                : $"本地逻辑预览完成：{passed}/8 项状态切换成功；请查看下方失败项";
            status = markerDiagnosticSummary;
            return;
        }

        var marker = MarkerDiagnosticOrder[markerDiagnosticIndex];
        markerDiagnosticPhase = MarkerDiagnosticPhase.WaitingForMarker;
        markerDiagnosticObservationDeadline = now + MarkerDiagnosticDisplayDurationMs;
        markerDiagnosticSummary =
            $"预览 {markerDiagnosticIndex + 1}/{MarkerDiagnosticOrder.Length}：正在显示{MarkerDisplayName(marker)}";
        localMarkerProvider.SubmitDiagnosticSelfMarker(marker);
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
        localMarkerProvider.Clear(immediateCleanup);
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
        try
        {
            localMarkerProvider.Draw();
        }
        catch (Exception exception)
        {
            localMarkerProvider.Clear();
            markerDiagnosticRunning = false;
            markerDiagnosticSummary = $"本地标点绘制失败：{exception.Message}";
            status = markerDiagnosticSummary;
            Log.Error(exception, "VedaMarker local marker draw failed");
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
        var anyControllerArmed = controllerArmed || simulationArmed || instanceControllerAuthorized;
        var controllerStatus = simulationArmed
            ? $"{activeMarkerProvider.Name}模拟测试已手动启动（Wave {simulationWave}/8）"
            : controllerArmed
                ? $"{activeMarkerProvider.Name}主控已启动；本次副本团灭后会自动恢复"
                : instanceControllerAuthorized
                    ? "本次副本已授权；等待副本重开时自动恢复主控"
                : "主控未启动";
        ImGui.TextColored(
            anyControllerArmed ? new Vector4(1f, 0.75f, 0.25f, 1f) : new Vector4(0.55f, 0.9f, 0.55f, 1f),
            controllerStatus);
        ImGui.TextWrapped("首次核对职责并手动启动后，本次副本内团灭/重开会自动恢复；退本、队伍变化、完成副本或手动停止会撤销授权。");

        var safetyControlsLocked = anyControllerArmed || markerDiagnosticRunning;
        if (safetyControlsLocked)
        {
            ImGui.BeginDisabled();
        }
        var localMarkersEnabled = configuration.EnableLocalMarkers;
        if (ImGui.Checkbox("启用本地软标点（只有自己可见）", ref localMarkersEnabled))
        {
            configuration.EnableLocalMarkers = localMarkersEnabled;
            if (!localMarkersEnabled)
            {
                localMarkerProvider.Clear();
            }

            PluginInterface.SavePluginConfig(configuration);
            status = localMarkersEnabled
                ? "本地软标点已启用；正式主控仍需核对完整八人职责并手动启动"
                : "已切回 Dry-run 模式";
        }
        ImGui.TextWrapped("本地软标点使用游戏内攻击/锁链/禁止图标绘制在角色头顶，不调用 Party Marker；无论目标范围怎么选，队友都不会看到。");
        DrawMarkerTargetConfiguration();
        var markerScalePercent = configuration.LocalMarkerScale * 100f;
        if (ImGui.SliderFloat("本地标点大小", ref markerScalePercent, 50f, 150f, "%.0f%%"))
        {
            configuration.LocalMarkerScale = markerScalePercent / 100f;
            PluginInterface.SavePluginConfig(configuration);
        }
        if (safetyControlsLocked)
        {
            ImGui.EndDisabled();
        }

        if (configuration.EnableLocalMarkers)
        {
            ImGui.TextColored(new Vector4(0.55f, 0.9f, 0.55f, 1f),
                "默认只显示本人；选择自定义职责或全队时，你会在对应队员头顶看到本地图标，但不会改变任何人的游戏团队标点。 ");
            ImGui.TextWrapped("一键预览会在本人头顶依次显示 8 种图标，每种停留 1.5 秒并逐个清除：");
            if (anyControllerArmed || markerDiagnosticRunning)
            {
                ImGui.BeginDisabled();
            }

            if (ImGui.Button("一键预览全部8种本地标点"))
            {
                StartMarkerDiagnostic();
                status = "已开始在本人头顶依次预览全部8种本地标点";
            }

            ImGui.SameLine();
            if (ImGui.Button("清除本地测试标点"))
            {
                localMarkerProvider.SubmitDiagnosticSelfClear();
                status = "已清除本地测试标点";
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
                    StopMarkerDiagnostic("预览已由用户停止，本地标点已清除");
                    status = markerDiagnosticSummary;
                }
            }

            ImGui.TextUnformatted($"本地当前本人标记：{ReadLocalMarkerStatus()}");
            ImGui.TextWrapped(markerDiagnosticSummary);
            foreach (var result in markerDiagnosticResults.Where(result =>
                         result.MarkerPassed.HasValue || result.ClearPassed.HasValue))
            {
                var markerResult = result.MarkerPassed == true
                    ? "已显示"
                    : $"失败（状态：{result.MarkerObserved}）";
                var clearResult = result.ClearPassed switch
                {
                    true => "已清除",
                    false => $"失败（状态：{result.ClearObserved}）",
                    null => "等待中",
                };
                ImGui.TextUnformatted($"{MarkerDisplayName(result.Marker)}：{markerResult}；{clearResult}");
            }

            ImGui.TextWrapped($"最近操作：{localMarkerProvider.LastOperation}；当前显示 {localMarkerProvider.ActiveMarkerCount} 个本地标点");
        }

        var nativeTelegraphsEnabled = configuration.EnableForsakenNativeTelegraphs;
        if (ImGui.Checkbox("启用遗弃末世游戏原生 AOE 范围（实验）", ref nativeTelegraphsEnabled))
        {
            configuration.EnableForsakenNativeTelegraphs = nativeTelegraphsEnabled;
            if (!nativeTelegraphsEnabled)
            {
                ClearTelegraphs();
            }

            PluginInterface.SavePluginConfig(configuration);
            status = nativeTelegraphsEnabled
                ? "已启用原生 AOE：正式主控会按每轮双塔 MapEffect 自动识别方向并显示范围"
                : "已关闭并清理原生 AOE 范围";
        }
        ImGui.TextWrapped(nativeOmenRenderer.IsAvailable
            ? "原生 AOE 使用游戏自身 Omen 特效，仅本机可见；首次实机确认前保持显式勾选，不会替代游戏判定。"
            : nativeOmenRenderer.LastStatus);

        ImGui.TextWrapped($"Marker Provider：{activeMarkerProvider.Name}");

        var canArm = rolesConfirmed
            && roleCoordinator.Assignments.Count == 8
            && HasConfiguredMarkerTargets()
            && !markerDiagnosticRunning
            && !simulationArmed
            && !controllerArmed
            && !instanceControllerAuthorized;
        if (!canArm)
        {
            ImGui.BeginDisabled();
        }
        var armButton = configuration.EnableLocalMarkers
            ? "手动启动本地标点主控"
            : "手动启动 Dry-run 主控";
        if (ImGui.Button(armButton))
        {
            ArmEncounterController(resumedAfterWipe: false);
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
            "模拟测试不读取当前副本机制，只用正式奇偶轮分配器生成完整八人标点。每轮必须手动提交，并先清除上一轮本地标点再显示新一轮；不会执行移动或战斗操作，也不会让队友看到标点。");

        if (!simulationArmed)
        {
            RefreshSoloSimulationRoleRecommendation();
            var hasConfirmedParty = rolesConfirmed && roleCoordinator.Assignments.Count == 8;
            var soloMode = !hasConfirmedParty && configuration.MarkerTargetMode == MarkerTargetMode.SelfOnly;
            var canStart = (hasConfirmedParty || soloMode)
                && ObjectTable.LocalPlayer is not null
                && HasConfiguredMarkerTargets()
                && !controllerArmed
                && !instanceControllerAuthorized
                && !localAoeSimulationActive
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

            var label = configuration.EnableLocalMarkers
                ? soloMode
                    ? "手动启动单人模拟（本地标点）"
                    : "手动启动模拟测试（本地标点）"
                : soloMode
                    ? "手动启动单人模拟（Dry-run）"
                    : "手动启动模拟测试（Dry-run）";
            if (ImGui.Button(label))
            {
                var selectedSimulationRole = soloMode ? soloSimulationRole : ResolveLocalRole();
                ClearTelegraphs();
                automationEngine.Reset();
                towerDirectionTracker.Reset();
                currentAssignment = null;
                activeMarkerProvider = configuration.EnableLocalMarkers
                    ? localMarkerProvider
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

        var operationsPending = activeMarkerProvider.ProducesMarkers
            && activeMarkerProvider.PendingOperationCount != 0;
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
            ? $"模拟 Wave {simulationWave}/8：正在切换本地标点"
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

    private void RefreshAutomaticTelegraphs()
    {
        if (!controllerArmed
            || !configuration.EnableForsakenNativeTelegraphs
            || localAoeSimulationActive
            || currentAssignment is null
            || !nativeOmenRenderer.IsAvailable)
        {
            return;
        }

        var wave = automationEngine.Snapshot.CurrentWave;
        if (wave is < 1 or > 8
            || !towerDirectionTracker.TryGetDirection(wave, out var direction8)
            || (activeAutomaticAoeWave == wave && activeAutomaticAoeDirection8 == direction8))
        {
            return;
        }

        var localPlayer = ObjectTable.LocalPlayer;
        if (localPlayer is null)
        {
            return;
        }

        var arenaCenter = new Vector3(100f, localPlayer.Position.Y, 100f);
        if (nativeOmenRenderer.Replace(arenaCenter, ForsakenTelegraphPlanner.Create(wave, direction8)))
        {
            activeAutomaticAoeWave = wave;
            activeAutomaticAoeDirection8 = direction8;
            status =
                $"Wave {wave} / Direction {direction8}：本地标点已更新，游戏原生 AOE 范围已显示";
            return;
        }

        activeAutomaticAoeWave = 0;
        activeAutomaticAoeDirection8 = -1;
        status = nativeOmenRenderer.LastStatus;
    }

    private void ClearTelegraphs()
    {
        nativeOmenRenderer.Clear();
        activeAutomaticAoeWave = 0;
        activeAutomaticAoeDirection8 = -1;
    }

    private bool ReplaceManualTelegraphPreview()
    {
        var replaced = nativeOmenRenderer.Replace(
            localAoeCenter,
            ForsakenTelegraphPlanner.Create(localAoeWave, localAoeDirection8));
        localAoeStatus = nativeOmenRenderer.LastStatus;
        if (!replaced)
        {
            localAoeSimulationActive = false;
        }

        return replaced;
    }

    private void DrawLocalAoeSimulationPanel()
    {
        DrawSectionHeader("游戏原生 AOE 范围测试（任意副本可用）");
        ImGui.TextWrapped(
            "只在本机创建游戏自身 Omen，不会给队友显示，也不会操作角色。以本人位置为测试中心，显示 30m/90° 扇形和 5m 钢铁范围；方向 0-7 每档旋转 45°。");

        if (!localAoeSimulationActive)
        {
            var canStart = ObjectTable.LocalPlayer is not null
                && nativeOmenRenderer.IsAvailable
                && !controllerArmed
                && !simulationArmed
                && !instanceControllerAuthorized;
            if (!canStart)
            {
                ImGui.BeginDisabled();
            }

            if (ImGui.Button("以本人当前位置启动原生 AOE 测试"))
            {
                localAoeCenter = ObjectTable.LocalPlayer!.Position;
                localAoeWave = 1;
                localAoeDirection8 = 0;
                localAoeSimulationActive = true;
                ReplaceManualTelegraphPreview();
            }

            if (!canStart)
            {
                ImGui.EndDisabled();
            }

            ImGui.TextWrapped(nativeOmenRenderer.IsAvailable
                ? localAoeStatus
                : nativeOmenRenderer.LastStatus);
            return;
        }

        var waveChanged = ImGui.SliderInt("模拟轮次", ref localAoeWave, 1, 8);
        var directionChanged = ImGui.SliderInt("方向 Direction8", ref localAoeDirection8, 0, 7);
        if (waveChanged || directionChanged)
        {
            ReplaceManualTelegraphPreview();
        }

        if (ImGui.Button("把模拟中心移到本人当前位置") && ObjectTable.LocalPlayer is { } localPlayer)
        {
            localAoeCenter = localPlayer.Position;
            ReplaceManualTelegraphPreview();
        }

        ImGui.SameLine();
        if (ImGui.Button("停止原生 AOE 测试"))
        {
            localAoeSimulationActive = false;
            ClearTelegraphs();
            localAoeStatus = "游戏原生 AOE 测试已由用户停止并清理";
            return;
        }

        var plan = ForsakenTelegraphPlanner.Create(localAoeWave, localAoeDirection8);
        ImGui.TextWrapped(
            $"当前：Wave {localAoeWave} / Direction {localAoeDirection8}；{plan.Telegraphs.Count} 个游戏原生范围。关闭控制台后仍会继续显示；点击停止会立即清理。{localAoeStatus}");
    }

    private void SubmitNextSimulationWave()
    {
        try
        {
            if (!simulationArmed || simulationWave >= 8)
            {
                throw new MarkerAssignmentException("模拟测试尚未启动或八轮已经完成。");
            }

            if (activeMarkerProvider.ProducesMarkers && activeMarkerProvider.PendingOperationCount != 0)
            {
                throw new MarkerAssignmentException("上一轮本地标点操作仍在处理中。");
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
            status = $"模拟第 {wave} 轮已清除上一轮，并在本机显示 {string.Join('/', targetRoles)} 的新标点";
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
        if (configuration.EnableForsakenNativeTelegraphs && snapshot.CurrentWave is >= 1 and <= 8)
        {
            ImGui.TextWrapped(towerDirectionTracker.TryGetDirection(snapshot.CurrentWave, out var direction8)
                ? $"原生 AOE：本轮双塔方向已识别为 Direction {direction8}；{nativeOmenRenderer.LastStatus}"
                : "原生 AOE：等待本轮两条 state=0002 的塔 MapEffect 后显示");
        }

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

    private void ArmEncounterController(bool resumedAfterWipe)
    {
        localAoeSimulationActive = false;
        ClearTelegraphs();
        automationEngine.Reset();
        towerDirectionTracker.Reset();
        currentAssignment = null;
        activeMarkerProvider = configuration.EnableLocalMarkers
            ? localMarkerProvider
            : dryRunMarkerProvider;
        controllerArmed = true;
        instanceControllerAuthorized = true;
        lastCapturePollAt = 0;
        var providerStatus = configuration.EnableLocalMarkers ? "本地标点" : "Dry-run";
        var aoeStatus = configuration.EnableForsakenNativeTelegraphs
            ? "；原生 AOE 会在每轮双塔方向识别完成后显示"
            : string.Empty;
        status = resumedAfterWipe
            ? $"副本已重新开始：{providerStatus}主控已自动恢复，等待遗弃末世开场八人点名{aoeStatus}"
            : $"{providerStatus}主控已启动并授权本次副本自动恢复；等待遗弃末世开场八人点名{aoeStatus}";
    }

    private bool TryResumeEncounterController()
    {
        if (!instanceControllerAuthorized
            || !rolesConfirmed
            || roleCoordinator.Assignments.Count != 8
            || !HasConfiguredMarkerTargets())
        {
            return false;
        }

        ArmEncounterController(resumedAfterWipe: true);
        return true;
    }

    private void OnDutyWiped(IDutyStateEventArgs args)
    {
        captureRecorder.RecordLifecycle("duty_wiped");
        localAoeSimulationActive = false;
        localAoeStatus = "团灭，游戏原生 AOE 已清理";
        DisarmController(
            instanceControllerAuthorized
                ? "团灭：本地标点和原生 AOE 已清理；等待副本重开后自动恢复"
                : "团灭：主控已停止并完成清理",
            preserveInstanceAuthorization: instanceControllerAuthorized);
    }

    private void OnDutyRecommenced(IDutyStateEventArgs args)
    {
        captureRecorder.RecordLifecycle("duty_recommenced");
        var shouldResume = instanceControllerAuthorized;
        DisarmController(
            "副本重新开始：已清理上一次尝试的本地显示",
            preserveInstanceAuthorization: shouldResume);
        if (!TryResumeEncounterController())
        {
            status = rolesConfirmed
                ? "副本重新开始：职责确认已保留；此前未授权自动恢复，请手动启动主控"
                : "副本重新开始：请核对职责并手动启动主控";
        }
    }

    private void OnDutyCompleted(IDutyStateEventArgs args)
    {
        captureRecorder.RecordLifecycle("duty_completed");
        localAoeSimulationActive = false;
        localAoeStatus = "副本完成，游戏原生 AOE 已清理";
        DisarmController("副本完成：主控已停止，本地标点和原生 AOE 已清理");
    }

    private void DisarmController(
        string reason,
        bool immediateCleanup = false,
        bool preserveInstanceAuthorization = false)
    {
        StopMarkerDiagnostic(reason, immediateCleanup);
        controllerArmed = false;
        if (!preserveInstanceAuthorization)
        {
            instanceControllerAuthorized = false;
        }

        simulationArmed = false;
        simulationWave = 0;
        simulationSoloMode = false;
        currentAssignment = null;
        automationEngine.Reset();
        towerDirectionTracker.Reset();
        activeMarkerProvider.Clear(immediateCleanup);
        if (!ReferenceEquals(activeMarkerProvider, localMarkerProvider))
        {
            localMarkerProvider.Clear(immediateCleanup);
        }
        ClearTelegraphs();
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
        Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.3.1";

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
