namespace VedaMarker.Core;

public static class JobCatalog
{
    public const uint Paladin = 19;
    public const uint Warrior = 21;
    public const uint DarkKnight = 32;
    public const uint Gunbreaker = 37;

    public const uint WhiteMage = 24;
    public const uint Scholar = 28;
    public const uint Astrologian = 33;
    public const uint Sage = 40;

    private static readonly HashSet<uint> Tanks = [1, 3, Paladin, Warrior, DarkKnight, Gunbreaker];
    private static readonly HashSet<uint> Healers = [6, WhiteMage, Scholar, Astrologian, Sage];
    private static readonly HashSet<uint> Melees = [2, 4, 20, 22, 29, 30, 34, 39, 41];
    private static readonly HashSet<uint> PhysicalRanged = [5, 23, 31, 38];
    private static readonly HashSet<uint> MagicalRanged = [7, 25, 26, 27, 35, 36, 42];

    public static CombatRoleCategory Category(uint jobId)
    {
        if (Tanks.Contains(jobId))
        {
            return CombatRoleCategory.Tank;
        }

        if (Healers.Contains(jobId))
        {
            return CombatRoleCategory.Healer;
        }

        if (Melees.Contains(jobId))
        {
            return CombatRoleCategory.Melee;
        }

        if (PhysicalRanged.Contains(jobId))
        {
            return CombatRoleCategory.PhysicalRanged;
        }

        return MagicalRanged.Contains(jobId)
            ? CombatRoleCategory.MagicalRanged
            : CombatRoleCategory.Unknown;
    }
}
