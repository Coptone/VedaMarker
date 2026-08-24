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
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using VedaMarker.Capture;
using VedaMarker.Core;

namespace VedaMarker;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/vedamarker";
    private static readonly RoleSlot[] RoleOrder = Enum.GetValues<RoleSlot>();

    [PluginService] private static IDalamudPluginInterface PluginInterface { get; set; } = null!;
    [PluginService] private static ICommandManager CommandManager { get; set; } = null!;
    [PluginService] private static IClientState ClientState { get; set; } = null!;
    [PluginService] private static ICondition Condition { get; set; } = null!;
    [PluginService] private static IDutyState DutyState { get; set; } = null!;
    [PluginService] private static IObjectTable ObjectTable { get; set; } = null!;
    [PluginService] private static IPartyList PartyList { get; set; } = null!;
    [PluginService] private static IFramework Framework { get; set; } = null!;
    [PluginService] private static IGameInteropProvider Interop { get; set; } = null!;
    [PluginService] private static IPluginLog Log { get; set; } = null!;

    private readonly PluginConfiguration configuration;
    private readonly PartyRoleCoordinator roleCoordinator = new();
    private readonly ForsakenAutomationEngine automationEngine = new();
    private readonly DryRunMarkerProvider dryRunMarkerProvider = new();
    private readonly ChatCommandMarkerProvider gameMarkerProvider;
    private readonly CaptureRecorder captureRecorder;
    private IMarkerProvider activeMarkerProvider;
    private Hook<ReceiveActionEffectDelegate>? actionEffectHook;
    private IReadOnlyList<RuntimePartyMember> currentParty = Array.Empty<RuntimePartyMember>();
    private ValidatedMarkerAssignment? currentAssignment;
    private string partySignature = string.Empty;
    private string status = "P0/P1：等待读取队伍";
    private bool showWindow;
    private bool rolesConfirmed;
    private bool controllerArmed;
    private long lastCapturePollAt;
    private uint lastTerritoryId;

    public Plugin()
    {
        configuration = PluginInterface.GetPluginConfig() as PluginConfiguration ?? new PluginConfiguration();
        if (configuration.Version < 2)
        {
            configuration.Version = 2;
            configuration.EnableExperimentalPartyMarkers = false;
            configuration.MarkerCommandIntervalMs = 150;
            PluginInterface.SavePluginConfig(configuration);
        }

        gameMarkerProvider = new ChatCommandMarkerProvider(() => configuration.MarkerCommandIntervalMs);
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
        InstallActionEffectHook();
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
        actionEffectHook!.Original(casterEntityId, caster, targetPosition, header, effects, targetEntityIds);
        if (header != null)
        {
            captureRecorder.RecordActionEffect(casterEntityId, header->ActionId);
        }
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        var now = Environment.TickCount64;
        if (lastTerritoryId != ClientState.TerritoryType)
        {
            lastTerritoryId = ClientState.TerritoryType;
            DisarmController("区域发生变化，主控已自动停止");
            rolesConfirmed = false;
        }

        RefreshPartyRoles(force: false);
        try
        {
            gameMarkerProvider.Tick(now);
        }
        catch (Exception exception)
        {
            controllerArmed = false;
            automationEngine.Reset();
            currentAssignment = null;
            gameMarkerProvider.Clear();
            status = "游戏标点命令提交失败，主控已停止；仍会继续尝试清理已提交标点";
            Log.Error(exception, "VedaMarker party marker command failed");
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

    private void PollGameState()
    {
        try
        {
            var entityRoles = roleCoordinator.Assignments.ToDictionary(
                entry => entry.Value,
                entry => entry.Key);
            var party = currentParty.Select(member => new CapturePartyMember(
                member.PartyIndex,
                member.EntityId,
                member.JobId,
                roleCoordinator.TryGetRole(member.EntityId, out var role) ? role : null)).ToArray();

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
                    casts.Add(new CaptureCastObservation(
                        battleChara.EntityId,
                        battleChara.CastActionId,
                        battleChara.CurrentCastTime));
                }
            }

            if (captureRecorder.IsActive)
            {
                captureRecorder.Observe(
                    ClientState.TerritoryType,
                    Condition[ConditionFlag.InCombat],
                    party,
                    statuses,
                    casts);
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
            var partySlots = BuildPartySlots();
            activeMarkerProvider.Submit(update.Assignment, partySlots);
            currentAssignment = update.Assignment;
            status = $"{update.Message}；已向 {activeMarkerProvider.Name} 提交完整八人标点";
        }

        if (update.Completed)
        {
            controllerArmed = false;
            currentAssignment = null;
            activeMarkerProvider.Clear();
            status = $"{update.Message}；标点清理已提交";
        }
    }

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
        DrawAutomationPanel();
        DrawCapturePanel();
        ImGui.End();
    }

    private void DrawSafetyPanel()
    {
        DrawSectionHeader("主控状态");
        ImGui.TextColored(
            controllerArmed ? new Vector4(1f, 0.75f, 0.25f, 1f) : new Vector4(0.55f, 0.9f, 0.55f, 1f),
            controllerArmed ? $"{activeMarkerProvider.Name}主控已手动启动" : "主控未启动");
        ImGui.TextWrapped("点名与八轮切换已接入完整实战数据；AoE 范围仍关闭，等待位置/方向采集验证。");

        if (controllerArmed)
        {
            ImGui.BeginDisabled();
        }
        var experimentalMarkers = configuration.EnableExperimentalPartyMarkers;
        if (ImGui.Checkbox("启用真实团队标点（实验，默认关闭）", ref experimentalMarkers))
        {
            configuration.EnableExperimentalPartyMarkers = experimentalMarkers;
            PluginInterface.SavePluginConfig(configuration);
            status = experimentalMarkers
                ? "真实团队标点已允许；仍需人工确认职责并手动启动"
                : "已切回 Dry-run 模式";
        }
        if (controllerArmed)
        {
            ImGui.EndDisabled();
        }

        if (configuration.EnableExperimentalPartyMarkers)
        {
            ImGui.TextColored(new Vector4(1f, 0.55f, 0.25f, 1f),
                "实验功能会实际改变全队可见的 Party Target Marker；请确保队内只有一个主控。 ");
        }

        ImGui.TextWrapped($"Marker Provider：{activeMarkerProvider.Name}；待处理命令：{gameMarkerProvider.PendingCommandCount}");

        var canArm = rolesConfirmed && roleCoordinator.Assignments.Count == 8 && !controllerArmed;
        if (!canArm)
        {
            ImGui.BeginDisabled();
        }
        var armButton = configuration.EnableExperimentalPartyMarkers
            ? "手动启动真实标点主控（实验）"
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
                ? "真实标点主控已启动；等待遗弃末世开场八人点名"
                : "Dry-run 主控已启动；等待遗弃末世开场八人点名";
        }
        if (!canArm)
        {
            ImGui.EndDisabled();
        }

        ImGui.SameLine();
        if (!controllerArmed)
        {
            ImGui.BeginDisabled();
        }
        if (ImGui.Button("停止并清理"))
        {
            DisarmController("用户手动停止主控");
        }
        if (!controllerArmed)
        {
            ImGui.EndDisabled();
        }

        ImGui.TextWrapped(status);
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
        ImGui.TextWrapped("建议在 P2 转场前开始，记录到团灭或通关。导出只包含会话别名、职业和机制事件 ID。");
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
        DisarmController("团灭：主控已停止并完成清理");
    }

    private void OnDutyRecommenced(IDutyStateEventArgs args)
    {
        captureRecorder.RecordLifecycle("duty_recommenced");
        DisarmController("副本重新开始：请重新核对并手动启动主控");
        rolesConfirmed = false;
    }

    private void OnDutyCompleted(IDutyStateEventArgs args)
    {
        captureRecorder.RecordLifecycle("duty_completed");
        DisarmController("副本完成：主控已停止并完成清理");
    }

    private void DisarmController(string reason, bool immediateCleanup = false)
    {
        controllerArmed = false;
        currentAssignment = null;
        automationEngine.Reset();
        activeMarkerProvider.Clear(immediateCleanup);
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

    private static string PluginVersion() =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.2.0";

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
}
