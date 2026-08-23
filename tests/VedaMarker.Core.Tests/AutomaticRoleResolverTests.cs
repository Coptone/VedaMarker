using VedaMarker.Core;

namespace VedaMarker.Core.Tests;

public sealed class AutomaticRoleResolverTests
{
    private readonly AutomaticRoleResolver resolver = new();

    [Fact]
    public void Resolve_UsesConfirmedTankHealerAndStandardDpsRules()
    {
        var party = new[]
        {
            Member(100, JobCatalog.Gunbreaker, 0),
            Member(101, JobCatalog.Warrior, 1),
            Member(102, JobCatalog.Scholar, 2),
            Member(103, JobCatalog.Astrologian, 3),
            Member(104, 41, 4), // Viper
            Member(105, 34, 5), // Samurai
            Member(106, 42, 6), // Pictomancer
            Member(107, 38, 7), // Dancer
        };

        var result = resolver.Resolve(party);

        Assert.True(result.Success);
        Assert.Equal(101u, result.Assignments[RoleSlot.MT].EntityId);
        Assert.Equal(100u, result.Assignments[RoleSlot.ST].EntityId);
        Assert.Equal(103u, result.Assignments[RoleSlot.H1].EntityId);
        Assert.Equal(102u, result.Assignments[RoleSlot.H2].EntityId);
        Assert.Equal(104u, result.Assignments[RoleSlot.D1].EntityId);
        Assert.Equal(105u, result.Assignments[RoleSlot.D2].EntityId);
        Assert.Equal(107u, result.Assignments[RoleSlot.D3].EntityId);
        Assert.Equal(106u, result.Assignments[RoleSlot.D4].EntityId);
    }

    [Fact]
    public void Resolve_ReservesPhysicalRangedForD3AndCasterForD4()
    {
        var party = StandardSupport().Concat(
        [
            Member(204, 34, 4), // Samurai
            Member(205, 38, 5), // Dancer
            Member(206, 25, 6), // Black Mage
            Member(207, 35, 7), // Red Mage
        ]);

        var result = resolver.Resolve(party);

        Assert.True(result.Success);
        Assert.Equal(204u, result.Assignments[RoleSlot.D1].EntityId);
        Assert.Equal(207u, result.Assignments[RoleSlot.D2].EntityId);
        Assert.Equal(205u, result.Assignments[RoleSlot.D3].EntityId);
        Assert.Equal(206u, result.Assignments[RoleSlot.D4].EntityId);
    }

    [Fact]
    public void Resolve_RejectsNonStandardComposition()
    {
        var party = new[]
        {
            Member(1, JobCatalog.Warrior, 0),
            Member(2, JobCatalog.Paladin, 1),
            Member(3, JobCatalog.DarkKnight, 2),
            Member(4, JobCatalog.WhiteMage, 3),
            Member(5, JobCatalog.Scholar, 4),
            Member(6, 34, 5),
            Member(7, 38, 6),
            Member(8, 42, 7),
        };

        var result = resolver.Resolve(party);

        Assert.False(result.Success);
        Assert.Contains("2T/2H/4DPS", result.Errors.Single());
    }

    private static IEnumerable<PartyMemberCandidate> StandardSupport() =>
    [
        Member(200, JobCatalog.Warrior, 0),
        Member(201, JobCatalog.Paladin, 1),
        Member(202, JobCatalog.WhiteMage, 2),
        Member(203, JobCatalog.Scholar, 3),
    ];

    private static PartyMemberCandidate Member(uint entityId, uint jobId, int partyIndex) =>
        new(entityId, jobId, partyIndex);
}
