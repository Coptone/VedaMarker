namespace VedaMarker.Core;

public static class PartyMarkerCommandPlanner
{
    public static IReadOnlyList<string> BuildSelfAssignmentCommands(
        ValidatedMarkerAssignment assignment,
        RoleSlot localRole)
    {
        ValidateCompleteAssignment(assignment);
        if (!Enum.IsDefined(localRole) || !assignment.Markers.TryGetValue(localRole, out var marker))
        {
            throw new MarkerAssignmentException("无法从完整职责中识别插件使用者本人。");
        }

        return
        [
            "/mk off <me>",
            $"/mk {CommandName(marker)} <me>",
        ];
    }

    public static IReadOnlyList<string> BuildSelfClearCommands() => ["/mk off <me>"];

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
