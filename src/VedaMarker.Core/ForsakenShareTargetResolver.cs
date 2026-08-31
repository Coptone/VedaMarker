namespace VedaMarker.Core;

public static class ForsakenShareTargetResolver
{
    public static IReadOnlyList<RoleSlot> Resolve(
        ForsakenSnapshot snapshot,
        IEnumerable<RoleSlot> selectedRoles)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(selectedRoles);

        var selected = selectedRoles.ToHashSet();
        return Enum.GetValues<RoleSlot>()
            .Where(role => selected.Contains(role)
                && snapshot.Players.TryGetValue(role, out var player)
                && player.CurrentMechanic == ForsakenMechanic.Share)
            .ToArray();
    }
}
