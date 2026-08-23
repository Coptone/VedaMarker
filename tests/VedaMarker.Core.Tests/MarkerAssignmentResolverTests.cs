using VedaMarker.Core;

namespace VedaMarker.Core.Tests;

public sealed class MarkerAssignmentResolverTests
{
    private readonly MarkerAssignmentResolver resolver = new();

    [Fact]
    public void Resolve_OddWaveMapsAllEightMarkers()
    {
        var snapshot = Snapshot(
            1,
            new Dictionary<RoleSlot, ForsakenMechanic>
            {
                [RoleSlot.MT] = ForsakenMechanic.Fan,
                [RoleSlot.ST] = ForsakenMechanic.Idle,
                [RoleSlot.H1] = ForsakenMechanic.Share,
                [RoleSlot.H2] = ForsakenMechanic.Idle,
                [RoleSlot.D1] = ForsakenMechanic.Steel,
                [RoleSlot.D2] = ForsakenMechanic.Idle,
                [RoleSlot.D3] = ForsakenMechanic.Share,
                [RoleSlot.D4] = ForsakenMechanic.Idle,
            });

        var assignment = resolver.Resolve(snapshot);

        Assert.Equal(PartyMarker.Ignore1, assignment.Markers[RoleSlot.MT]);
        Assert.Equal(PartyMarker.Attack1, assignment.Markers[RoleSlot.ST]);
        Assert.Equal(PartyMarker.Bind1, assignment.Markers[RoleSlot.H1]);
        Assert.Equal(PartyMarker.Attack3, assignment.Markers[RoleSlot.H2]);
        Assert.Equal(PartyMarker.Ignore2, assignment.Markers[RoleSlot.D1]);
        Assert.Equal(PartyMarker.Attack2, assignment.Markers[RoleSlot.D2]);
        Assert.Equal(PartyMarker.Bind2, assignment.Markers[RoleSlot.D3]);
        Assert.Equal(PartyMarker.Attack4, assignment.Markers[RoleSlot.D4]);
    }

    [Fact]
    public void Resolve_EvenWaveAppliesNearSwapAndFarStay()
    {
        var snapshot = Snapshot(
            2,
            new Dictionary<RoleSlot, ForsakenMechanic>
            {
                [RoleSlot.MT] = ForsakenMechanic.Idle,
                [RoleSlot.H1] = ForsakenMechanic.Idle,
                [RoleSlot.ST] = ForsakenMechanic.Fan,
                [RoleSlot.H2] = ForsakenMechanic.Steel,
                [RoleSlot.D1] = ForsakenMechanic.Idle,
                [RoleSlot.D3] = ForsakenMechanic.Idle,
                [RoleSlot.D2] = ForsakenMechanic.Fan,
                [RoleSlot.D4] = ForsakenMechanic.Steel,
            });

        var assignment = resolver.Resolve(snapshot);

        Assert.Equal(PartyMarker.Attack2, assignment.Markers[RoleSlot.MT]);
        Assert.Equal(PartyMarker.Attack3, assignment.Markers[RoleSlot.H1]);
        Assert.Equal(PartyMarker.Bind1, assignment.Markers[RoleSlot.ST]);
        Assert.Equal(PartyMarker.Ignore1, assignment.Markers[RoleSlot.H2]);
        Assert.Equal(PartyMarker.Attack1, assignment.Markers[RoleSlot.D1]);
        Assert.Equal(PartyMarker.Attack4, assignment.Markers[RoleSlot.D3]);
        Assert.Equal(PartyMarker.Bind2, assignment.Markers[RoleSlot.D2]);
        Assert.Equal(PartyMarker.Ignore2, assignment.Markers[RoleSlot.D4]);
    }

    [Fact]
    public void Resolve_RejectsDuplicateMarkers()
    {
        var snapshot = Snapshot(
            1,
            Enum.GetValues<RoleSlot>().ToDictionary(role => role, _ => ForsakenMechanic.Idle));

        var exception = Assert.Throws<MarkerAssignmentException>(() => resolver.Resolve(snapshot));

        Assert.Contains("重复", exception.Message);
    }

    private static ForsakenSnapshot Snapshot(
        int wave,
        IReadOnlyDictionary<RoleSlot, ForsakenMechanic> mechanics)
    {
        var players = Enum.GetValues<RoleSlot>().ToDictionary(
            role => role,
            role => new PlayerMechanicSnapshot(
                role,
                role.Pair(),
                InitialGroup.Unknown,
                ForsakenMechanic.Unknown,
                mechanics[role],
                ForsakenMechanic.Unknown,
                null,
                null));
        return new ForsakenSnapshot(ForsakenEncounterStatus.WaveActive, wave, players);
    }
}
