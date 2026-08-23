using VedaMarker.Core;

namespace VedaMarker.Core.Tests;

public sealed class ForsakenStateMachineTests
{
    [Fact]
    public void IdentifyOpening_GroupsPairsContainingShare()
    {
        var machine = new ForsakenStateMachine();

        machine.IdentifyOpening(TestMechanics.Opening);

        var snapshot = machine.Snapshot;
        Assert.Equal(ForsakenEncounterStatus.OpeningIdentified, snapshot.Status);
        Assert.Equal(InitialGroup.InitialTower, snapshot.Players[RoleSlot.MT].InitialGroup);
        Assert.Equal(InitialGroup.InitialTower, snapshot.Players[RoleSlot.H1].InitialGroup);
        Assert.Equal(InitialGroup.InitialTower, snapshot.Players[RoleSlot.D1].InitialGroup);
        Assert.Equal(InitialGroup.InitialTower, snapshot.Players[RoleSlot.D3].InitialGroup);
        Assert.Equal(InitialGroup.InitialIdle, snapshot.Players[RoleSlot.ST].InitialGroup);
        Assert.Equal(InitialGroup.InitialIdle, snapshot.Players[RoleSlot.D4].InitialGroup);
    }

    [Fact]
    public void BeginWave1_UsesOpeningForTowerGroupAndIdleForOtherGroup()
    {
        var machine = StartedMachine();

        machine.BeginWave(1);

        var snapshot = machine.Snapshot;
        Assert.Equal(ForsakenMechanic.Fan, snapshot.Players[RoleSlot.MT].CurrentMechanic);
        Assert.Equal(ForsakenMechanic.Share, snapshot.Players[RoleSlot.H1].CurrentMechanic);
        Assert.Equal(ForsakenMechanic.Idle, snapshot.Players[RoleSlot.ST].CurrentMechanic);
        Assert.Equal(ForsakenMechanic.Idle, snapshot.Players[RoleSlot.D4].CurrentMechanic);
    }

    [Fact]
    public void PendingAfterWave3_IsPreservedUntilWave8()
    {
        var machine = StartedMachine();
        machine.BeginWave(1);
        machine.ResolveWave(1);
        machine.BeginWave(2, TestMechanics.TowerWave);
        machine.ResolveWave(2);
        machine.BeginWave(3, TestMechanics.TowerWave);
        machine.ResolveWave(3);

        machine.StorePendingForWave8(TestMechanics.PendingWave8);

        Assert.Equal(
            ForsakenMechanic.Steel,
            machine.Snapshot.Players[RoleSlot.MT].PendingMechanic);
        Assert.Equal(8, machine.Snapshot.Players[RoleSlot.MT].NextResolveWave);

        machine.BeginWave(4);
        Assert.Equal(
            TestMechanics.Opening[RoleSlot.ST],
            machine.Snapshot.Players[RoleSlot.ST].CurrentMechanic);
        Assert.Equal(ForsakenMechanic.Idle, machine.Snapshot.Players[RoleSlot.MT].CurrentMechanic);
        machine.ResolveWave(4);

        for (var wave = 5; wave <= 7; wave++)
        {
            machine.BeginWave(wave, TestMechanics.IdleGroupWave);
            machine.ResolveWave(wave);
        }

        machine.BeginWave(8);

        Assert.Equal(
            TestMechanics.PendingWave8[RoleSlot.MT],
            machine.Snapshot.Players[RoleSlot.MT].CurrentMechanic);
        Assert.Equal(ForsakenMechanic.Idle, machine.Snapshot.Players[RoleSlot.ST].CurrentMechanic);
    }

    [Fact]
    public void BeginWave4_RejectsMissingPendingMechanics()
    {
        var machine = StartedMachine();
        machine.BeginWave(1);
        machine.ResolveWave(1);
        machine.BeginWave(2, TestMechanics.TowerWave);
        machine.ResolveWave(2);
        machine.BeginWave(3, TestMechanics.TowerWave);
        machine.ResolveWave(3);

        var exception = Assert.Throws<ForsakenStateException>(() => machine.BeginWave(4));

        Assert.Contains("Pending", exception.Message);
    }

    private static ForsakenStateMachine StartedMachine()
    {
        var machine = new ForsakenStateMachine();
        machine.IdentifyOpening(TestMechanics.Opening);
        return machine;
    }
}

internal static class TestMechanics
{
    public static readonly IReadOnlyDictionary<RoleSlot, ForsakenMechanic> Opening =
        new Dictionary<RoleSlot, ForsakenMechanic>
        {
            [RoleSlot.MT] = ForsakenMechanic.Fan,
            [RoleSlot.ST] = ForsakenMechanic.Fan,
            [RoleSlot.H1] = ForsakenMechanic.Share,
            [RoleSlot.H2] = ForsakenMechanic.Steel,
            [RoleSlot.D1] = ForsakenMechanic.Steel,
            [RoleSlot.D2] = ForsakenMechanic.Fan,
            [RoleSlot.D3] = ForsakenMechanic.Share,
            [RoleSlot.D4] = ForsakenMechanic.Steel,
        };

    public static readonly IReadOnlyDictionary<RoleSlot, ForsakenMechanic> TowerWave =
        new Dictionary<RoleSlot, ForsakenMechanic>
        {
            [RoleSlot.MT] = ForsakenMechanic.Fan,
            [RoleSlot.H1] = ForsakenMechanic.Share,
            [RoleSlot.D1] = ForsakenMechanic.Steel,
            [RoleSlot.D3] = ForsakenMechanic.Share,
        };

    public static readonly IReadOnlyDictionary<RoleSlot, ForsakenMechanic> IdleGroupWave =
        new Dictionary<RoleSlot, ForsakenMechanic>
        {
            [RoleSlot.ST] = ForsakenMechanic.Fan,
            [RoleSlot.H2] = ForsakenMechanic.Share,
            [RoleSlot.D2] = ForsakenMechanic.Steel,
            [RoleSlot.D4] = ForsakenMechanic.Share,
        };

    public static readonly IReadOnlyDictionary<RoleSlot, ForsakenMechanic> PendingWave8 =
        new Dictionary<RoleSlot, ForsakenMechanic>
        {
            [RoleSlot.MT] = ForsakenMechanic.Steel,
            [RoleSlot.H1] = ForsakenMechanic.Share,
            [RoleSlot.D1] = ForsakenMechanic.Fan,
            [RoleSlot.D3] = ForsakenMechanic.Share,
        };
}
