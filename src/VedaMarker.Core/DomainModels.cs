namespace VedaMarker.Core;

public enum RoleSlot
{
    MT,
    ST,
    H1,
    H2,
    D1,
    D2,
    D3,
    D4,
}

public enum PairId
{
    A,
    B,
    C,
    D,
}

public enum CombatRoleCategory
{
    Unknown,
    Tank,
    Healer,
    Melee,
    PhysicalRanged,
    MagicalRanged,
}

public enum ForsakenMechanic
{
    Unknown,
    Fan,
    Steel,
    Share,
    Idle,
}

public enum InitialGroup
{
    Unknown,
    InitialTower,
    InitialIdle,
}

public enum PartyMarker
{
    Attack1,
    Attack2,
    Attack3,
    Attack4,
    Bind1,
    Bind2,
    Ignore1,
    Ignore2,
}

public enum ForsakenEncounterStatus
{
    Inactive,
    OpeningIdentified,
    WaveActive,
    WaveResolved,
}

public sealed record PartyMemberCandidate(
    uint EntityId,
    uint JobId,
    int PartyIndex);

public sealed record RoleResolutionResult(
    bool Success,
    IReadOnlyDictionary<RoleSlot, PartyMemberCandidate> Assignments,
    IReadOnlyList<string> Errors)
{
    public static RoleResolutionResult Failure(params string[] errors) =>
        new(false, new Dictionary<RoleSlot, PartyMemberCandidate>(), errors);
}

public sealed record PlayerMechanicSnapshot(
    RoleSlot Role,
    PairId Pair,
    InitialGroup InitialGroup,
    ForsakenMechanic InitialMechanic,
    ForsakenMechanic CurrentMechanic,
    ForsakenMechanic PendingMechanic,
    int? NextResolveWave,
    PartyMarker? CurrentMarker);

public sealed record ForsakenSnapshot(
    ForsakenEncounterStatus Status,
    int CurrentWave,
    IReadOnlyDictionary<RoleSlot, PlayerMechanicSnapshot> Players);

public sealed record ValidatedMarkerAssignment(
    int Wave,
    IReadOnlyDictionary<RoleSlot, PartyMarker> Markers);

public sealed class ForsakenStateException(string message) : InvalidOperationException(message);

public sealed class MarkerAssignmentException(string message) : InvalidOperationException(message);

public static class RoleSlotRules
{
    private static readonly IReadOnlyDictionary<RoleSlot, PairId> Pairs =
        new Dictionary<RoleSlot, PairId>
        {
            [RoleSlot.MT] = PairId.A,
            [RoleSlot.H1] = PairId.A,
            [RoleSlot.ST] = PairId.B,
            [RoleSlot.H2] = PairId.B,
            [RoleSlot.D1] = PairId.C,
            [RoleSlot.D3] = PairId.C,
            [RoleSlot.D2] = PairId.D,
            [RoleSlot.D4] = PairId.D,
        };

    public static PairId Pair(this RoleSlot role) => Pairs[role];

    public static bool IsTank(this RoleSlot role) => role is RoleSlot.MT or RoleSlot.ST;

    public static bool IsHealer(this RoleSlot role) => role is RoleSlot.H1 or RoleSlot.H2;

    public static bool IsTankOrHealer(this RoleSlot role) => role.IsTank() || role.IsHealer();

    public static bool IsDps(this RoleSlot role) => !role.IsTankOrHealer();

    public static bool IsNear(this RoleSlot role) =>
        role is RoleSlot.MT or RoleSlot.ST or RoleSlot.D1 or RoleSlot.D2;

    public static bool IsFar(this RoleSlot role) => !role.IsNear();
}
