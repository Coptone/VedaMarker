using VedaMarker.Core;

namespace VedaMarker.Core.Tests;

public sealed class ForsakenAutomationEngineTests
{
    private static readonly RoleSlot[] TowerRoles =
        [RoleSlot.MT, RoleSlot.H1, RoleSlot.D1, RoleSlot.D3];

    private static readonly RoleSlot[] IdleRoles =
        [RoleSlot.ST, RoleSlot.H2, RoleSlot.D2, RoleSlot.D4];

    [Fact]
    public void Observe_ReplaysCompleteCaptureThroughAllEightWaves()
    {
        var engine = new ForsakenAutomationEngine();

        var opening = CompleteOpening();
        AssertWave(engine.Observe(ToStatuses(opening)), 1);
        Assert.Equal(InitialGroup.InitialTower, engine.Snapshot.Players[RoleSlot.MT].InitialGroup);
        Assert.Equal(InitialGroup.InitialIdle, engine.Snapshot.Players[RoleSlot.ST].InitialGroup);

        var wave2 = Update(opening, TowerRoles, 3, new Dictionary<RoleSlot, ForsakenMechanic>
        {
            [RoleSlot.MT] = ForsakenMechanic.Fan,
            [RoleSlot.H1] = ForsakenMechanic.Steel,
            [RoleSlot.D1] = ForsakenMechanic.Fan,
            [RoleSlot.D3] = ForsakenMechanic.Steel,
        });
        AssertWave(engine.Observe(ToStatuses(wave2)), 2);

        var wave3 = Update(wave2, TowerRoles, 2, new Dictionary<RoleSlot, ForsakenMechanic>
        {
            [RoleSlot.MT] = ForsakenMechanic.Steel,
            [RoleSlot.H1] = ForsakenMechanic.Share,
            [RoleSlot.D1] = ForsakenMechanic.Fan,
            [RoleSlot.D3] = ForsakenMechanic.Share,
        });
        AssertWave(engine.Observe(ToStatuses(wave3)), 3);

        var pendingAndWave4 = Update(wave3, TowerRoles, 1, new Dictionary<RoleSlot, ForsakenMechanic>
        {
            [RoleSlot.MT] = ForsakenMechanic.Fan,
            [RoleSlot.H1] = ForsakenMechanic.Fan,
            [RoleSlot.D1] = ForsakenMechanic.Steel,
            [RoleSlot.D3] = ForsakenMechanic.Steel,
        });
        AssertWave(engine.Observe(ToStatuses(pendingAndWave4)), 4);
        Assert.Equal(ForsakenMechanic.Steel, engine.Snapshot.Players[RoleSlot.D1].PendingMechanic);
        Assert.Equal(8, engine.Snapshot.Players[RoleSlot.D1].NextResolveWave);

        var wave5 = Update(pendingAndWave4, IdleRoles, 3, new Dictionary<RoleSlot, ForsakenMechanic>
        {
            [RoleSlot.ST] = ForsakenMechanic.Fan,
            [RoleSlot.H2] = ForsakenMechanic.Share,
            [RoleSlot.D2] = ForsakenMechanic.Steel,
            [RoleSlot.D4] = ForsakenMechanic.Share,
        });
        AssertWave(engine.Observe(ToStatuses(wave5)), 5);

        var wave6 = Update(wave5, IdleRoles, 2, new Dictionary<RoleSlot, ForsakenMechanic>
        {
            [RoleSlot.ST] = ForsakenMechanic.Steel,
            [RoleSlot.H2] = ForsakenMechanic.Fan,
            [RoleSlot.D2] = ForsakenMechanic.Fan,
            [RoleSlot.D4] = ForsakenMechanic.Steel,
        });
        AssertWave(engine.Observe(ToStatuses(wave6)), 6);

        var wave7 = Update(wave6, IdleRoles, 1, new Dictionary<RoleSlot, ForsakenMechanic>
        {
            [RoleSlot.ST] = ForsakenMechanic.Share,
            [RoleSlot.H2] = ForsakenMechanic.Steel,
            [RoleSlot.D2] = ForsakenMechanic.Share,
            [RoleSlot.D4] = ForsakenMechanic.Fan,
        });
        AssertWave(engine.Observe(ToStatuses(wave7)), 7);

        var wave8 = Clear(wave7, IdleRoles);
        AssertWave(engine.Observe(ToStatuses(wave8)), 8);
        Assert.Equal(ForsakenMechanic.Steel, engine.Snapshot.Players[RoleSlot.D1].CurrentMechanic);
        Assert.Equal(ForsakenMechanic.Fan, engine.Snapshot.Players[RoleSlot.MT].CurrentMechanic);

        var completed = engine.Observe(ToStatuses(Clear(wave8, TowerRoles)));
        Assert.True(completed.Changed);
        Assert.True(completed.Completed);
        Assert.Null(completed.Assignment);
        Assert.Equal(ForsakenEncounterStatus.WaveResolved, engine.Snapshot.Status);
        Assert.Equal(8, engine.Snapshot.CurrentWave);
    }

    [Fact]
    public void Observe_RepeatedSnapshotIsIdempotent()
    {
        var engine = new ForsakenAutomationEngine();
        var opening = ToStatuses(CompleteOpening());

        AssertWave(engine.Observe(opening), 1);
        var repeated = engine.Observe(opening);

        Assert.False(repeated.Changed);
        Assert.Null(repeated.Assignment);
        Assert.Equal(1, engine.Snapshot.CurrentWave);
    }

    [Fact]
    public void Observe_IncompleteOpeningDoesNotCreatePartialAssignment()
    {
        var engine = new ForsakenAutomationEngine();
        var opening = CompleteOpening();
        opening[RoleSlot.D4] = (4, ForsakenMechanic.Unknown);

        var update = engine.Observe(ToStatuses(opening));

        Assert.False(update.Changed);
        Assert.Null(update.Assignment);
        Assert.Equal(ForsakenEncounterStatus.Inactive, engine.Snapshot.Status);
    }

    [Fact]
    public void EncounterIds_MapCapturedStatusesToMechanics()
    {
        Assert.Equal(ForsakenMechanic.Share,
            ForsakenEncounterIds.MechanicFromStatus(ForsakenEncounterIds.ShareStatus));
        Assert.Equal(ForsakenMechanic.Steel,
            ForsakenEncounterIds.MechanicFromStatus(ForsakenEncounterIds.SteelStatus));
        Assert.Equal(ForsakenMechanic.Fan,
            ForsakenEncounterIds.MechanicFromStatus(ForsakenEncounterIds.FanStatus));
    }

    private static void AssertWave(ForsakenAutomationUpdate update, int wave)
    {
        Assert.True(update.Changed);
        Assert.False(update.Completed);
        Assert.NotNull(update.Assignment);
        Assert.Equal(wave, update.Assignment.Wave);
        Assert.Equal(8, update.Assignment.Markers.Count);
        Assert.Equal(8, update.Assignment.Markers.Values.Distinct().Count());
    }

    private static Dictionary<RoleSlot, (uint? Count, ForsakenMechanic Mechanic)> CompleteOpening() =>
        new()
        {
            [RoleSlot.MT] = (4, ForsakenMechanic.Fan),
            [RoleSlot.ST] = (4, ForsakenMechanic.Fan),
            [RoleSlot.H1] = (4, ForsakenMechanic.Share),
            [RoleSlot.H2] = (4, ForsakenMechanic.Fan),
            [RoleSlot.D1] = (4, ForsakenMechanic.Steel),
            [RoleSlot.D2] = (4, ForsakenMechanic.Steel),
            [RoleSlot.D3] = (4, ForsakenMechanic.Share),
            [RoleSlot.D4] = (4, ForsakenMechanic.Steel),
        };

    private static Dictionary<RoleSlot, (uint? Count, ForsakenMechanic Mechanic)> Update(
        IReadOnlyDictionary<RoleSlot, (uint? Count, ForsakenMechanic Mechanic)> source,
        IEnumerable<RoleSlot> roles,
        uint count,
        IReadOnlyDictionary<RoleSlot, ForsakenMechanic> mechanics)
    {
        var result = source.ToDictionary(entry => entry.Key, entry => entry.Value);
        foreach (var role in roles)
        {
            result[role] = (count, mechanics[role]);
        }

        return result;
    }

    private static Dictionary<RoleSlot, (uint? Count, ForsakenMechanic Mechanic)> Clear(
        IReadOnlyDictionary<RoleSlot, (uint? Count, ForsakenMechanic Mechanic)> source,
        IEnumerable<RoleSlot> roles)
    {
        var result = source.ToDictionary(entry => entry.Key, entry => entry.Value);
        foreach (var role in roles)
        {
            result[role] = (null, ForsakenMechanic.Unknown);
        }

        return result;
    }

    private static IReadOnlyList<ForsakenStatusObservation> ToStatuses(
        IReadOnlyDictionary<RoleSlot, (uint? Count, ForsakenMechanic Mechanic)> snapshot)
    {
        var statuses = new List<ForsakenStatusObservation>();
        foreach (var entry in snapshot)
        {
            if (entry.Value.Count is { } count)
            {
                statuses.Add(new ForsakenStatusObservation(
                    entry.Key,
                    ForsakenEncounterIds.InventoryStatus,
                    count));
            }

            var statusId = entry.Value.Mechanic switch
            {
                ForsakenMechanic.Share => ForsakenEncounterIds.ShareStatus,
                ForsakenMechanic.Steel => ForsakenEncounterIds.SteelStatus,
                ForsakenMechanic.Fan => ForsakenEncounterIds.FanStatus,
                _ => 0u,
            };
            if (statusId != 0)
            {
                statuses.Add(new ForsakenStatusObservation(entry.Key, statusId, 0));
            }
        }

        return statuses;
    }
}
