namespace VedaMarker.Core;

public sealed class AutomaticRoleResolver
{
    private static readonly uint[] TankPriority =
        [JobCatalog.Warrior, JobCatalog.Paladin, JobCatalog.Gunbreaker, JobCatalog.DarkKnight, 3, 1];

    private static readonly uint[] HealerPriority =
        [JobCatalog.WhiteMage, JobCatalog.Astrologian, JobCatalog.Sage, JobCatalog.Scholar, 6];

    public RoleResolutionResult Resolve(IEnumerable<PartyMemberCandidate> candidates)
    {
        var members = candidates.ToArray();
        if (members.Length != 8)
        {
            return RoleResolutionResult.Failure($"需要 8 名队员，当前读取到 {members.Length} 名。");
        }

        if (members.Select(member => member.EntityId).Distinct().Count() != members.Length)
        {
            return RoleResolutionResult.Failure("队伍成员实体 ID 不唯一，无法自动识别。");
        }

        var unknown = members.Where(member => JobCatalog.Category(member.JobId) == CombatRoleCategory.Unknown).ToArray();
        if (unknown.Length > 0)
        {
            return RoleResolutionResult.Failure(
                $"存在无法识别的职业 ID：{string.Join(", ", unknown.Select(member => member.JobId).Distinct())}。");
        }

        var tanks = members.Where(IsCategory(CombatRoleCategory.Tank)).ToArray();
        var healers = members.Where(IsCategory(CombatRoleCategory.Healer)).ToArray();
        var dps = members.Where(member => JobCatalog.Category(member.JobId) is
            CombatRoleCategory.Melee or CombatRoleCategory.PhysicalRanged or CombatRoleCategory.MagicalRanged).ToArray();

        if (tanks.Length != 2 || healers.Length != 2 || dps.Length != 4)
        {
            return RoleResolutionResult.Failure(
                $"自动识别只接受标准 2T/2H/4DPS，当前为 {tanks.Length}T/{healers.Length}H/{dps.Length}DPS。");
        }

        var assignments = new Dictionary<RoleSlot, PartyMemberCandidate>();
        var orderedTanks = OrderByPriority(tanks, TankPriority);
        assignments[RoleSlot.MT] = orderedTanks[0];
        assignments[RoleSlot.ST] = orderedTanks[1];

        var orderedHealers = OrderByPriority(healers, HealerPriority);
        assignments[RoleSlot.H1] = orderedHealers[0];
        assignments[RoleSlot.H2] = orderedHealers[1];

        AssignDps(assignments, dps);
        return new RoleResolutionResult(true, assignments, Array.Empty<string>());
    }

    private static Func<PartyMemberCandidate, bool> IsCategory(CombatRoleCategory category) =>
        member => JobCatalog.Category(member.JobId) == category;

    private static PartyMemberCandidate[] OrderByPriority(
        IEnumerable<PartyMemberCandidate> members,
        IReadOnlyList<uint> priority)
    {
        return members
            .OrderBy(member => PriorityOf(member.JobId, priority))
            .ThenBy(member => member.PartyIndex)
            .ThenBy(member => member.EntityId)
            .ToArray();
    }

    private static int PriorityOf(uint jobId, IReadOnlyList<uint> priority)
    {
        for (var index = 0; index < priority.Count; index++)
        {
            if (priority[index] == jobId)
            {
                return index;
            }
        }

        return int.MaxValue;
    }

    private static void AssignDps(
        IDictionary<RoleSlot, PartyMemberCandidate> assignments,
        IEnumerable<PartyMemberCandidate> dpsMembers)
    {
        var remaining = Stable(dpsMembers).ToList();

        // Reserve the conventional back-line slots first so a standard party always
        // lands physical ranged on D3 and caster on D4.
        var d3 = TakeFirst(remaining, CombatRoleCategory.PhysicalRanged);
        var d4 = TakeFirst(remaining, CombatRoleCategory.MagicalRanged);

        var front = remaining
            .OrderBy(member => FrontPriority(JobCatalog.Category(member.JobId)))
            .ThenBy(member => member.PartyIndex)
            .ThenBy(member => member.EntityId)
            .Take(2)
            .ToArray();

        assignments[RoleSlot.D1] = front[0];
        assignments[RoleSlot.D2] = front[1];
        foreach (var member in front)
        {
            remaining.Remove(member);
        }

        d3 ??= TakePreferred(
            remaining,
            CombatRoleCategory.PhysicalRanged,
            CombatRoleCategory.Melee,
            CombatRoleCategory.MagicalRanged);
        d4 ??= TakePreferred(
            remaining,
            CombatRoleCategory.MagicalRanged,
            CombatRoleCategory.PhysicalRanged,
            CombatRoleCategory.Melee);

        assignments[RoleSlot.D3] = d3
            ?? throw new InvalidOperationException("D3 automatic assignment failed unexpectedly.");
        assignments[RoleSlot.D4] = d4
            ?? throw new InvalidOperationException("D4 automatic assignment failed unexpectedly.");
    }

    private static PartyMemberCandidate? TakeFirst(
        ICollection<PartyMemberCandidate> remaining,
        CombatRoleCategory category)
    {
        var candidate = Stable(remaining).FirstOrDefault(IsCategory(category));
        if (candidate is not null)
        {
            remaining.Remove(candidate);
        }

        return candidate;
    }

    private static PartyMemberCandidate? TakePreferred(
        ICollection<PartyMemberCandidate> remaining,
        params CombatRoleCategory[] preferences)
    {
        foreach (var category in preferences)
        {
            var candidate = TakeFirst(remaining, category);
            if (candidate is not null)
            {
                return candidate;
            }
        }

        return null;
    }

    private static int FrontPriority(CombatRoleCategory category) => category switch
    {
        CombatRoleCategory.Melee => 0,
        CombatRoleCategory.MagicalRanged => 1,
        CombatRoleCategory.PhysicalRanged => 2,
        _ => int.MaxValue,
    };

    private static IOrderedEnumerable<PartyMemberCandidate> Stable(
        IEnumerable<PartyMemberCandidate> members) =>
        members.OrderBy(member => member.PartyIndex).ThenBy(member => member.EntityId);
}
