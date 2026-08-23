using VedaMarker.Core;

namespace VedaMarker;

internal sealed record RuntimePartyMember(
    int PartyIndex,
    uint EntityId,
    uint JobId,
    string DisplayName);

internal sealed class PartyRoleCoordinator
{
    private readonly AutomaticRoleResolver resolver = new();
    private readonly Dictionary<RoleSlot, uint> assignments = [];

    public IReadOnlyDictionary<RoleSlot, uint> Assignments => assignments;

    public string LastStatus { get; private set; } = "尚未读取队伍";

    public bool Refresh(IReadOnlyList<RuntimePartyMember> party)
    {
        var result = resolver.Resolve(party.Select(member =>
            new PartyMemberCandidate(member.EntityId, member.JobId, member.PartyIndex)));
        assignments.Clear();
        if (!result.Success)
        {
            LastStatus = string.Join(" ", result.Errors);
            return false;
        }

        foreach (var entry in result.Assignments)
        {
            assignments[entry.Key] = entry.Value.EntityId;
        }

        LastStatus = "自动识别完成，请人工核对后启动主控。";
        return true;
    }

    public bool Assign(RoleSlot role, uint entityId)
    {
        if (!assignments.TryGetValue(role, out var previousEntityId))
        {
            return false;
        }

        var otherRole = assignments
            .Where(entry => entry.Value == entityId)
            .Select(entry => (RoleSlot?)entry.Key)
            .SingleOrDefault();
        assignments[role] = entityId;
        if (otherRole.HasValue)
        {
            assignments[otherRole.Value] = previousEntityId;
        }

        LastStatus = "职责已手动调整，请重新核对。";
        return true;
    }

    public bool TryGetRole(uint entityId, out RoleSlot role)
    {
        foreach (var entry in assignments)
        {
            if (entry.Value == entityId)
            {
                role = entry.Key;
                return true;
            }
        }

        role = default;
        return false;
    }

    public void Clear(string reason)
    {
        assignments.Clear();
        LastStatus = reason;
    }
}
