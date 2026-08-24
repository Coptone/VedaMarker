namespace VedaMarker.Core;

public sealed record ForsakenStatusObservation(
    RoleSlot Role,
    uint StatusId,
    uint Param);

public sealed record ForsakenAutomationUpdate(
    bool Changed,
    bool Completed,
    ValidatedMarkerAssignment? Assignment,
    string Message)
{
    public static ForsakenAutomationUpdate Waiting(string message) =>
        new(false, false, null, message);
}

public sealed class ForsakenAutomationEngine
{
    private sealed class RoleObservation
    {
        public uint? InventoryCount { get; set; }

        public ForsakenMechanic Mechanic { get; set; } = ForsakenMechanic.Unknown;
    }

    private readonly ForsakenStateMachine stateMachine = new();
    private readonly MarkerAssignmentResolver assignmentResolver = new();

    public ForsakenSnapshot Snapshot => stateMachine.Snapshot;

    public string LastStatus { get; private set; } = "等待遗弃末世开场八人点名";

    public ForsakenAutomationUpdate Observe(IEnumerable<ForsakenStatusObservation> observations)
    {
        var roles = Normalize(observations);
        if (stateMachine.Status == ForsakenEncounterStatus.Inactive)
        {
            return TryIdentifyOpening(roles);
        }

        var towerRoles = RolesInGroup(InitialGroup.InitialTower);
        var idleRoles = RolesInGroup(InitialGroup.InitialIdle);
        return stateMachine.CurrentWave switch
        {
            1 when TryReadGroup(roles, towerRoles, 3, out var mechanics) =>
                AdvanceWithMechanics(2, mechanics),
            2 when TryReadGroup(roles, towerRoles, 2, out var mechanics) =>
                AdvanceWithMechanics(3, mechanics),
            3 when TryReadGroup(roles, towerRoles, 1, out var mechanics) =>
                StorePendingAndBeginWave4(mechanics),
            4 when TryReadGroup(roles, idleRoles, 3, out var mechanics) =>
                AdvanceWithMechanics(5, mechanics),
            5 when TryReadGroup(roles, idleRoles, 2, out var mechanics) =>
                AdvanceWithMechanics(6, mechanics),
            6 when TryReadGroup(roles, idleRoles, 1, out var mechanics) =>
                AdvanceWithMechanics(7, mechanics),
            7 when GroupStatusesCleared(roles, idleRoles) => BeginWave8(),
            8 when GroupStatusesCleared(roles, towerRoles) => CompleteEncounter(),
            _ => ForsakenAutomationUpdate.Waiting(LastStatus),
        };
    }

    public void Reset()
    {
        stateMachine.Reset();
        LastStatus = "等待遗弃末世开场八人点名";
    }

    private ForsakenAutomationUpdate TryIdentifyOpening(
        IReadOnlyDictionary<RoleSlot, RoleObservation> roles)
    {
        if (!TryReadGroup(roles, Enum.GetValues<RoleSlot>(), 4, out var openingMechanics))
        {
            return ForsakenAutomationUpdate.Waiting(LastStatus);
        }

        stateMachine.IdentifyOpening(openingMechanics);
        stateMachine.BeginWave(1);
        return CreateAssignment("已识别开场八人点名，进入 Wave 1");
    }

    private ForsakenAutomationUpdate AdvanceWithMechanics(
        int nextWave,
        IReadOnlyDictionary<RoleSlot, ForsakenMechanic> mechanics)
    {
        stateMachine.ResolveWave(nextWave - 1);
        stateMachine.BeginWave(nextWave, mechanics);
        return CreateAssignment($"点名完整，进入 Wave {nextWave}");
    }

    private ForsakenAutomationUpdate StorePendingAndBeginWave4(
        IReadOnlyDictionary<RoleSlot, ForsakenMechanic> pendingMechanics)
    {
        stateMachine.ResolveWave(3);
        stateMachine.StorePendingForWave8(pendingMechanics);
        stateMachine.BeginWave(4);
        return CreateAssignment("Wave 3 新点名已保存至 Pending，进入 Wave 4");
    }

    private ForsakenAutomationUpdate BeginWave8()
    {
        stateMachine.ResolveWave(7);
        stateMachine.BeginWave(8);
        return CreateAssignment("Wave 7 已结算，恢复 Pending 点名并进入 Wave 8");
    }

    private ForsakenAutomationUpdate CompleteEncounter()
    {
        stateMachine.ResolveWave(8);
        LastStatus = "Wave 8 已结算，遗弃末世点名流程完成";
        return new ForsakenAutomationUpdate(true, true, null, LastStatus);
    }

    private ForsakenAutomationUpdate CreateAssignment(string message)
    {
        var assignment = assignmentResolver.Resolve(stateMachine.Snapshot);
        stateMachine.ApplyMarkers(assignment);
        LastStatus = message;
        return new ForsakenAutomationUpdate(true, false, assignment, message);
    }

    private IReadOnlyList<RoleSlot> RolesInGroup(InitialGroup group) =>
        stateMachine.Snapshot.Players.Values
            .Where(player => player.InitialGroup == group)
            .Select(player => player.Role)
            .OrderBy(role => role)
            .ToArray();

    private static bool TryReadGroup(
        IReadOnlyDictionary<RoleSlot, RoleObservation> roles,
        IEnumerable<RoleSlot> expectedRoles,
        uint expectedInventoryCount,
        out IReadOnlyDictionary<RoleSlot, ForsakenMechanic> mechanics)
    {
        var result = new Dictionary<RoleSlot, ForsakenMechanic>();
        foreach (var role in expectedRoles)
        {
            var observation = roles[role];
            if (observation.InventoryCount != expectedInventoryCount
                || observation.Mechanic == ForsakenMechanic.Unknown)
            {
                mechanics = new Dictionary<RoleSlot, ForsakenMechanic>();
                return false;
            }

            result[role] = observation.Mechanic;
        }

        mechanics = result;
        return true;
    }

    private static bool GroupStatusesCleared(
        IReadOnlyDictionary<RoleSlot, RoleObservation> roles,
        IEnumerable<RoleSlot> expectedRoles) =>
        expectedRoles.All(role =>
            roles[role].InventoryCount is null
            && roles[role].Mechanic == ForsakenMechanic.Unknown);

    private static IReadOnlyDictionary<RoleSlot, RoleObservation> Normalize(
        IEnumerable<ForsakenStatusObservation> observations)
    {
        var result = Enum.GetValues<RoleSlot>()
            .ToDictionary(role => role, _ => new RoleObservation());

        foreach (var observation in observations.Where(observation =>
                     ForsakenEncounterIds.IsMechanicStatus(observation.StatusId)))
        {
            var role = result[observation.Role];
            if (observation.StatusId == ForsakenEncounterIds.InventoryStatus)
            {
                if (role.InventoryCount is not null && role.InventoryCount != observation.Param)
                {
                    throw new ForsakenStateException(
                        $"{observation.Role} 同时出现多个遗弃末世计数状态。");
                }

                role.InventoryCount = observation.Param;
                continue;
            }

            var mechanic = ForsakenEncounterIds.MechanicFromStatus(observation.StatusId);
            if (role.Mechanic != ForsakenMechanic.Unknown && role.Mechanic != mechanic)
            {
                throw new ForsakenStateException(
                    $"{observation.Role} 同时出现多个遗弃末世点名状态。");
            }

            role.Mechanic = mechanic;
        }

        return result;
    }
}
