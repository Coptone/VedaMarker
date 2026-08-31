using VedaMarker.Core;

namespace VedaMarker.Core.Tests;

public sealed class ForsakenShareTargetResolverTests
{
    [Fact]
    public void ReturnsOnlySelectedRolesWithCurrentShareMechanic()
    {
        var snapshot = CreateSnapshot(new Dictionary<RoleSlot, ForsakenMechanic>
        {
            [RoleSlot.H1] = ForsakenMechanic.Share,
            [RoleSlot.D3] = ForsakenMechanic.Share,
            [RoleSlot.MT] = ForsakenMechanic.Fan,
        });

        var result = ForsakenShareTargetResolver.Resolve(
            snapshot,
            [RoleSlot.MT, RoleSlot.H1, RoleSlot.D1]);

        Assert.Equal([RoleSlot.H1], result);
    }

    [Fact]
    public void ReturnsEmptyWhenSelectedRolesHaveNoShareMechanic()
    {
        var snapshot = CreateSnapshot(new Dictionary<RoleSlot, ForsakenMechanic>
        {
            [RoleSlot.H1] = ForsakenMechanic.Share,
            [RoleSlot.D3] = ForsakenMechanic.Share,
        });

        var result = ForsakenShareTargetResolver.Resolve(
            snapshot,
            [RoleSlot.MT, RoleSlot.ST, RoleSlot.D1, RoleSlot.D2]);

        Assert.Empty(result);
    }

    private static ForsakenSnapshot CreateSnapshot(
        IReadOnlyDictionary<RoleSlot, ForsakenMechanic> mechanics)
    {
        var players = Enum.GetValues<RoleSlot>().ToDictionary(
            role => role,
            role => new PlayerMechanicSnapshot(
                role,
                role.Pair(),
                InitialGroup.Unknown,
                ForsakenMechanic.Unknown,
                mechanics.GetValueOrDefault(role, ForsakenMechanic.Idle),
                ForsakenMechanic.Unknown,
                null,
                null));
        return new ForsakenSnapshot(ForsakenEncounterStatus.WaveActive, 1, players);
    }
}
