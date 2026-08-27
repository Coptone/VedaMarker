namespace VedaMarker.Core;

public static class PartyMarkerCommandPlanner
{
    public static IReadOnlyList<string> BuildAssignmentCommands(
        ValidatedMarkerAssignment assignment,
        IReadOnlyCollection<RoleSlot> targetRoles,
        RoleSlot localRole,
        IReadOnlyDictionary<RoleSlot, int> partySlots)
    {
        ValidateCompleteAssignment(assignment);
        var targets = ValidateTargets(targetRoles, localRole, partySlots);
        var commands = new List<string>(targets.Length * 2);
        commands.AddRange(targets.Select(role => $"/mk clear {TargetReference(role, localRole, partySlots)}"));
        commands.AddRange(targets.Select(role =>
            $"/mk {CommandName(assignment.Markers[role])} {TargetReference(role, localRole, partySlots)}"));
        return commands;
    }

    public static IReadOnlyList<string> BuildClearCommands(
        IReadOnlyCollection<RoleSlot> targetRoles,
        RoleSlot localRole,
        IReadOnlyDictionary<RoleSlot, int> partySlots)
    {
        var targets = ValidateTargets(targetRoles, localRole, partySlots);
        return targets.Select(role => $"/mk clear {TargetReference(role, localRole, partySlots)}").ToArray();
    }

    public static string BuildSelfMarkerCommand(PartyMarker marker) =>
        $"/mk {CommandName(marker)} <me>";

    private static void ValidateCompleteAssignment(ValidatedMarkerAssignment assignment)
    {
        var roles = Enum.GetValues<RoleSlot>();
        if (assignment.Markers.Count != roles.Length
            || assignment.Markers.Values.Distinct().Count() != roles.Length
            || roles.Any(role => !assignment.Markers.ContainsKey(role)))
        {
            throw new MarkerAssignmentException("标点提交器拒绝不完整或重复的标点集合。");
        }
    }

    private static RoleSlot[] ValidateTargets(
        IReadOnlyCollection<RoleSlot> targetRoles,
        RoleSlot localRole,
        IReadOnlyDictionary<RoleSlot, int> partySlots)
    {
        var roles = Enum.GetValues<RoleSlot>();
        if (!Enum.IsDefined(localRole))
        {
            throw new MarkerAssignmentException("无法识别插件使用者本人的职责。");
        }

        var targets = targetRoles.Distinct().OrderBy(role => role).ToArray();
        if (targets.Length == 0 || targets.Any(role => !Enum.IsDefined(role)))
        {
            throw new MarkerAssignmentException("至少需要选择一个有效的标点目标职责。");
        }

        if (targets.Length == 1 && targets[0] == localRole)
        {
            return targets;
        }

        if (partySlots.Count != roles.Length
            || roles.Any(role => !partySlots.ContainsKey(role))
            || partySlots.Values.Any(slot => slot is < 1 or > 8)
            || partySlots.Values.Distinct().Count() != roles.Length)
        {
            throw new MarkerAssignmentException("标点提交器拒绝不完整、重复或越界的队伍序号。");
        }

        return targets;
    }

    private static string TargetReference(
        RoleSlot role,
        RoleSlot localRole,
        IReadOnlyDictionary<RoleSlot, int> partySlots) =>
        role == localRole ? "<me>" : $"<{partySlots[role]}>";

    private static string CommandName(PartyMarker marker) => marker switch
    {
        PartyMarker.Attack1 => "attack1",
        PartyMarker.Attack2 => "attack2",
        PartyMarker.Attack3 => "attack3",
        PartyMarker.Attack4 => "attack4",
        PartyMarker.Bind1 => "bind1",
        PartyMarker.Bind2 => "bind2",
        PartyMarker.Ignore1 => "ignore1",
        PartyMarker.Ignore2 => "ignore2",
        _ => throw new MarkerAssignmentException($"未知 Party Marker：{marker}"),
    };
}
