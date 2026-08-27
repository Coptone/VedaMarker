using VedaMarker.Core;

namespace VedaMarker.Core.Tests;

public sealed class PartyMarkerCommandPlannerTests
{
    [Fact]
    public void SelfOnlyClearsThenMarksOnlyLocalUser()
    {
        var (assignment, slots) = CompleteAssignment();

        var commands = PartyMarkerCommandPlanner.BuildAssignmentCommands(
            assignment,
            [RoleSlot.H1],
            RoleSlot.H1,
            slots);

        Assert.Equal(["/mk off <me>", "/mk attack3 <me>"], commands);
    }

    [Fact]
    public void IgnoreMarkerUsesTheGamesIgnoreParameter()
    {
        var (assignment, slots) = CompleteAssignment();

        var commands = PartyMarkerCommandPlanner.BuildAssignmentCommands(
            assignment,
            [RoleSlot.D3],
            RoleSlot.D3,
            slots);

        Assert.Equal(["/mk off <me>", "/mk ignore1 <me>"], commands);
    }

    [Fact]
    public void CustomTargetsClearEverySelectionBeforeApplyingNewMarkers()
    {
        var (assignment, slots) = CompleteAssignment();

        var commands = PartyMarkerCommandPlanner.BuildAssignmentCommands(
            assignment,
            [RoleSlot.MT, RoleSlot.H1, RoleSlot.D4],
            RoleSlot.H1,
            slots);

        Assert.Equal(
        [
            "/mk off <1>",
            "/mk off <me>",
            "/mk off <8>",
            "/mk attack1 <1>",
            "/mk attack3 <me>",
            "/mk ignore2 <8>",
        ], commands);
    }

    [Fact]
    public void AllRolesBuildsEightClearsFollowedByEightMarkers()
    {
        var (assignment, slots) = CompleteAssignment();
        var roles = Enum.GetValues<RoleSlot>();

        var commands = PartyMarkerCommandPlanner.BuildAssignmentCommands(
            assignment,
            roles,
            RoleSlot.MT,
            slots);

        Assert.Equal(16, commands.Count);
        Assert.All(commands.Take(8), command => Assert.StartsWith("/mk off ", command));
        Assert.All(commands.Skip(8), command =>
            Assert.Matches("^/mk (attack[1-4]|bind[1-2]|ignore[1-2]) <(me|[1-8])>$", command));
    }

    [Fact]
    public void IncompleteAssignmentRejectsEntireSubmission()
    {
        var (assignment, slots) = CompleteAssignment();
        var incomplete = assignment with
        {
            Markers = assignment.Markers.Where(entry => entry.Key != RoleSlot.D4)
                .ToDictionary(entry => entry.Key, entry => entry.Value),
        };

        Assert.Throws<MarkerAssignmentException>(() =>
            PartyMarkerCommandPlanner.BuildAssignmentCommands(
                incomplete,
                [RoleSlot.MT],
                RoleSlot.MT,
                slots));
    }

    [Fact]
    public void EmptyTargetSelectionIsRejected()
    {
        var (assignment, slots) = CompleteAssignment();

        Assert.Throws<MarkerAssignmentException>(() =>
            PartyMarkerCommandPlanner.BuildAssignmentCommands(
                assignment,
                [],
                RoleSlot.MT,
                slots));
    }

    [Fact]
    public void CleanupTargetsOnlyConfiguredRoles()
    {
        var (_, slots) = CompleteAssignment();

        Assert.Equal(
            ["/mk off <me>", "/mk off <8>"],
            PartyMarkerCommandPlanner.BuildClearCommands(
                [RoleSlot.H1, RoleSlot.D4],
                RoleSlot.H1,
                slots));
    }

    private static (ValidatedMarkerAssignment Assignment, IReadOnlyDictionary<RoleSlot, int> Slots)
        CompleteAssignment()
    {
        var roles = Enum.GetValues<RoleSlot>();
        var markers = roles.Zip(Enum.GetValues<PartyMarker>())
            .ToDictionary(entry => entry.First, entry => entry.Second);
        var slots = roles.Select((role, index) => (role, slot: index + 1))
            .ToDictionary(entry => entry.role, entry => entry.slot);
        return (new ValidatedMarkerAssignment(1, markers), slots);
    }
}
