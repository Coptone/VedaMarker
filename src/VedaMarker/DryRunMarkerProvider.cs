using VedaMarker.Core;

namespace VedaMarker;

internal interface IMarkerProvider
{
    string Name { get; }

    bool ProducesGameMarkers { get; }

    int PendingCommandCount { get; }

    void Submit(
        ValidatedMarkerAssignment assignment,
        IReadOnlyDictionary<RoleSlot, int> partySlots);

    void Tick(long now);

    void Clear(bool immediate = false);
}

internal sealed class DryRunMarkerProvider : IMarkerProvider
{
    public string Name => "Dry-run";

    public bool ProducesGameMarkers => false;

    public int PendingCommandCount => 0;

    public ValidatedMarkerAssignment? LastAssignment { get; private set; }

    public void Submit(
        ValidatedMarkerAssignment assignment,
        IReadOnlyDictionary<RoleSlot, int> partySlots)
    {
        _ = PartyMarkerCommandPlanner.BuildAssignmentCommands(assignment, partySlots);
        LastAssignment = assignment;
    }

    public void Tick(long now)
    {
    }

    public void Clear(bool immediate = false)
    {
        LastAssignment = null;
    }
}
