namespace VedaMarker.Core;

public static class PartyMarkerCommandPlanner
{
    public static IReadOnlyList<string> BuildAssignmentCommands(
        ValidatedMarkerAssignment assignment,
        IReadOnlyDictionary<RoleSlot, int> partySlots)
    {
        ValidateCompleteAssignment(assignment, partySlots);
        return assignment.Markers
            .OrderBy(entry => entry.Value)
            .Select(entry => $"/mk {CommandName(entry.Value)} <{partySlots[entry.Key]}>")
            .ToArray();
    }

    public static IReadOnlyList<string> BuildClearCommands() =>
        Enumerable.Range(1, 8).Select(slot => $"/mk off <{slot}>").ToArray();

    private static void ValidateCompleteAssignment(
        ValidatedMarkerAssignment assignment,
        IReadOnlyDictionary<RoleSlot, int> partySlots)
    {
        var roles = Enum.GetValues<RoleSlot>();
        if (assignment.Markers.Count != roles.Length
            || assignment.Markers.Values.Distinct().Count() != roles.Length
            || roles.Any(role => !assignment.Markers.ContainsKey(role)))
        {
            throw new MarkerAssignmentException("标点提交器拒绝不完整或重复的标点集合。");
        }

        if (partySlots.Count != roles.Length
            || roles.Any(role => !partySlots.ContainsKey(role))
            || partySlots.Values.Any(slot => slot is < 1 or > 8)
            || partySlots.Values.Distinct().Count() != roles.Length)
        {
            throw new MarkerAssignmentException("标点提交器拒绝不完整、重复或越界的队伍序号。");
        }
    }

    private static string CommandName(PartyMarker marker) => marker switch
    {
        PartyMarker.Attack1 => "attack1",
        PartyMarker.Attack2 => "attack2",
        PartyMarker.Attack3 => "attack3",
        PartyMarker.Attack4 => "attack4",
        PartyMarker.Bind1 => "bind1",
        PartyMarker.Bind2 => "bind2",
        PartyMarker.Ignore1 => "stop1",
        PartyMarker.Ignore2 => "stop2",
        _ => throw new MarkerAssignmentException($"未知 Party Marker：{marker}"),
    };
}
