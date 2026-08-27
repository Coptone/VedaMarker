namespace VedaMarker.Core;

public static class ForsakenSimulationAssignmentFactory
{
    private static readonly IReadOnlyDictionary<RoleSlot, ForsakenMechanic> OddWaveMechanics =
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
        };

    private static readonly IReadOnlyDictionary<RoleSlot, ForsakenMechanic> EvenWaveMechanics =
        new Dictionary<RoleSlot, ForsakenMechanic>
        {
            [RoleSlot.MT] = ForsakenMechanic.Idle,
            [RoleSlot.ST] = ForsakenMechanic.Fan,
            [RoleSlot.H1] = ForsakenMechanic.Idle,
            [RoleSlot.H2] = ForsakenMechanic.Steel,
            [RoleSlot.D1] = ForsakenMechanic.Idle,
            [RoleSlot.D2] = ForsakenMechanic.Fan,
            [RoleSlot.D3] = ForsakenMechanic.Idle,
            [RoleSlot.D4] = ForsakenMechanic.Steel,
        };

    public static ValidatedMarkerAssignment Create(int wave)
    {
        if (wave is < 1 or > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(wave), "模拟轮次必须在 1 到 8 之间。");
        }

        var mechanics = wave % 2 == 1 ? OddWaveMechanics : EvenWaveMechanics;
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
        var snapshot = new ForsakenSnapshot(ForsakenEncounterStatus.WaveActive, wave, players);
        return new MarkerAssignmentResolver().Resolve(snapshot);
    }
}
