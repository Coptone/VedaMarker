using VedaMarker.Core;

namespace VedaMarker.Core.Tests;

public sealed class PartyMarkerCommandPlannerTests
{
    [Fact]
    public void CompleteAssignmentClearsThenMarksOnlyLocalUser()
    {
        var roles = Enum.GetValues<RoleSlot>();
        var markers = roles.Zip(Enum.GetValues<PartyMarker>())
            .ToDictionary(entry => entry.First, entry => entry.Second);

        var commands = PartyMarkerCommandPlanner.BuildSelfAssignmentCommands(
            new ValidatedMarkerAssignment(1, markers),
            RoleSlot.H1);

        Assert.Equal(["/mk off <me>", "/mk attack3 <me>"], commands);
    }

    [Fact]
    public void EveryLocalRoleAlwaysClearsSelfBeforeApplyingOneSelfMarker()
    {
        var roles = Enum.GetValues<RoleSlot>();
        var markers = roles.Zip(Enum.GetValues<PartyMarker>())
            .ToDictionary(entry => entry.First, entry => entry.Second);
        var assignment = new ValidatedMarkerAssignment(1, markers);

        foreach (var role in roles)
        {
            var commands = PartyMarkerCommandPlanner.BuildSelfAssignmentCommands(assignment, role);

            Assert.Equal(2, commands.Count);
            Assert.Equal("/mk off <me>", commands[0]);
            Assert.Matches("^/mk (attack[1-4]|bind[1-2]|stop[1-2]) <me>$", commands[1]);
        }
    }

    [Fact]
    public void IncompleteAssignmentRejectsEntireSelfSubmission()
    {
        var roles = Enum.GetValues<RoleSlot>();
        var markers = roles.Take(7).Zip(Enum.GetValues<PartyMarker>())
            .ToDictionary(entry => entry.First, entry => entry.Second);

        Assert.Throws<MarkerAssignmentException>(() =>
            PartyMarkerCommandPlanner.BuildSelfAssignmentCommands(
                new ValidatedMarkerAssignment(1, markers),
                RoleSlot.MT));
    }

    [Fact]
    public void CleanupTargetsOnlyLocalUser()
    {
        Assert.Equal(["/mk off <me>"], PartyMarkerCommandPlanner.BuildSelfClearCommands());
    }
}
