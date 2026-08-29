using VedaMarker.Core;

namespace VedaMarker.Core.Tests;

public sealed class PartyMarkerSubmissionValidatorTests
{
    [Fact]
    public void SelfOnlyReturnsLocalRoleWithoutPartySlots()
    {
        var (assignment, _) = CompleteAssignment();

        var targets = PartyMarkerSubmissionValidator.Validate(
            assignment,
            [RoleSlot.H1],
            RoleSlot.H1,
            new Dictionary<RoleSlot, int>());

        Assert.Equal([RoleSlot.H1], targets);
    }

    [Fact]
    public void NonLocalTargetStillRequiresCompletePartySlots()
    {
        var (assignment, _) = CompleteAssignment();

        Assert.Throws<MarkerAssignmentException>(() =>
            PartyMarkerSubmissionValidator.Validate(
                assignment,
                [RoleSlot.MT, RoleSlot.H1],
                RoleSlot.MT,
                new Dictionary<RoleSlot, int>()));
    }

    [Fact]
    public void CustomTargetsAreDeduplicatedAndOrdered()
    {
        var (assignment, slots) = CompleteAssignment();

        var targets = PartyMarkerSubmissionValidator.Validate(
            assignment,
            [RoleSlot.D4, RoleSlot.H1, RoleSlot.H1, RoleSlot.MT],
            RoleSlot.H1,
            slots);

        Assert.Equal([RoleSlot.MT, RoleSlot.H1, RoleSlot.D4], targets);
    }

    [Fact]
    public void AllRolesReturnsEightValidatedTargets()
    {
        var (assignment, slots) = CompleteAssignment();
        var roles = Enum.GetValues<RoleSlot>();

        var targets = PartyMarkerSubmissionValidator.Validate(
            assignment,
            roles,
            RoleSlot.MT,
            slots);

        Assert.Equal(roles, targets);
    }

    [Fact]
    public void IncompleteAssignmentIsRejected()
    {
        var (assignment, slots) = CompleteAssignment();
        var incomplete = assignment with
        {
            Markers = assignment.Markers.Where(entry => entry.Key != RoleSlot.D4)
                .ToDictionary(entry => entry.Key, entry => entry.Value),
        };

        Assert.Throws<MarkerAssignmentException>(() =>
            PartyMarkerSubmissionValidator.Validate(
                incomplete,
                [RoleSlot.MT],
                RoleSlot.MT,
                slots));
    }

    [Fact]
    public void DuplicateMarkerAssignmentIsRejected()
    {
        var (assignment, slots) = CompleteAssignment();
        var duplicate = assignment with
        {
            Markers = assignment.Markers.ToDictionary(
                entry => entry.Key,
                entry => entry.Key == RoleSlot.D4 ? PartyMarker.Attack1 : entry.Value),
        };

        Assert.Throws<MarkerAssignmentException>(() =>
            PartyMarkerSubmissionValidator.Validate(
                duplicate,
                [RoleSlot.MT],
                RoleSlot.MT,
                slots));
    }

    [Fact]
    public void EmptyTargetSelectionIsRejected()
    {
        var (assignment, slots) = CompleteAssignment();

        Assert.Throws<MarkerAssignmentException>(() =>
            PartyMarkerSubmissionValidator.Validate(
                assignment,
                [],
                RoleSlot.MT,
                slots));
    }

    [Fact]
    public void InvalidLocalRoleIsRejected()
    {
        var (assignment, slots) = CompleteAssignment();

        Assert.Throws<MarkerAssignmentException>(() =>
            PartyMarkerSubmissionValidator.Validate(
                assignment,
                [RoleSlot.MT],
                (RoleSlot)99,
                slots));
    }

    [Fact]
    public void DuplicatePartySlotsAreRejectedForMultipleTargets()
    {
        var (assignment, slots) = CompleteAssignment();
        var duplicateSlots = slots.ToDictionary(entry => entry.Key, entry => entry.Value);
        duplicateSlots[RoleSlot.D4] = duplicateSlots[RoleSlot.D3];

        Assert.Throws<MarkerAssignmentException>(() =>
            PartyMarkerSubmissionValidator.Validate(
                assignment,
                [RoleSlot.MT, RoleSlot.D4],
                RoleSlot.MT,
                duplicateSlots));
    }

    [Fact]
    public void OutOfRangePartySlotIsRejectedForMultipleTargets()
    {
        var (assignment, slots) = CompleteAssignment();
        var invalidSlots = slots.ToDictionary(entry => entry.Key, entry => entry.Value);
        invalidSlots[RoleSlot.D4] = 9;

        Assert.Throws<MarkerAssignmentException>(() =>
            PartyMarkerSubmissionValidator.Validate(
                assignment,
                [RoleSlot.MT, RoleSlot.D4],
                RoleSlot.MT,
                invalidSlots));
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
