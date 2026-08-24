using VedaMarker.Core;

namespace VedaMarker.Core.Tests;

public sealed class PartyMarkerCommandPlannerTests
{
    [Fact]
    public void CompleteAssignmentBuildsEightWhitelistedCommands()
    {
        var roles = Enum.GetValues<RoleSlot>();
        var markers = roles.Zip(Enum.GetValues<PartyMarker>())
            .ToDictionary(entry => entry.First, entry => entry.Second);
        var slots = roles.Select((role, index) => (role, slot: index + 1))
            .ToDictionary(entry => entry.role, entry => entry.slot);

        var commands = PartyMarkerCommandPlanner.BuildAssignmentCommands(
            new ValidatedMarkerAssignment(1, markers),
            slots);

        Assert.Equal(8, commands.Count);
        Assert.Equal("/mk attack1 <1>", commands[0]);
        Assert.Equal("/mk stop2 <8>", commands[7]);
        Assert.All(commands, command => Assert.Matches("^/mk (attack[1-4]|bind[1-2]|stop[1-2]) <[1-8]>$", command));
    }

    [Fact]
    public void MissingPartySlotRejectsEntireSubmission()
    {
        var roles = Enum.GetValues<RoleSlot>();
        var markers = roles.Zip(Enum.GetValues<PartyMarker>())
            .ToDictionary(entry => entry.First, entry => entry.Second);
        var slots = roles.Take(7).Select((role, index) => (role, slot: index + 1))
            .ToDictionary(entry => entry.role, entry => entry.slot);

        Assert.Throws<MarkerAssignmentException>(() =>
            PartyMarkerCommandPlanner.BuildAssignmentCommands(
                new ValidatedMarkerAssignment(1, markers),
                slots));
    }

    [Fact]
    public void CleanupTargetsEveryPartySlot()
    {
        Assert.Equal(
            Enumerable.Range(1, 8).Select(slot => $"/mk off <{slot}>"),
            PartyMarkerCommandPlanner.BuildClearCommands());
    }
}
