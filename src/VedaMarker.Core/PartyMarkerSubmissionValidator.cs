namespace VedaMarker.Core;

public static class PartyMarkerSubmissionValidator
{
    public static RoleSlot[] Validate(
        ValidatedMarkerAssignment assignment,
        IReadOnlyCollection<RoleSlot> targetRoles,
        RoleSlot localRole,
        IReadOnlyDictionary<RoleSlot, int> partySlots)
    {
        ValidateCompleteAssignment(assignment);
        return ValidateTargets(targetRoles, localRole, partySlots);
    }

    public static void ValidateCompleteAssignment(ValidatedMarkerAssignment assignment)
    {
        var roles = Enum.GetValues<RoleSlot>();
        if (assignment.Markers.Count != roles.Length
            || assignment.Markers.Values.Distinct().Count() != roles.Length
            || roles.Any(role => !assignment.Markers.ContainsKey(role)))
        {
            throw new MarkerAssignmentException("标点提交器拒绝不完整或重复的标点集合。");
        }
    }

    public static RoleSlot[] ValidateTargets(
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
}
