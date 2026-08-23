namespace VedaMarker.Core;

public sealed class MarkerAssignmentResolver
{
    public ValidatedMarkerAssignment Resolve(ForsakenSnapshot snapshot)
    {
        if (snapshot.Status != ForsakenEncounterStatus.WaveActive)
        {
            throw new MarkerAssignmentException("只有活动轮次可以计算标点。");
        }

        if (snapshot.Players.Count != 8 || snapshot.Players.Values.Any(
                player => player.CurrentMechanic == ForsakenMechanic.Unknown))
        {
            throw new MarkerAssignmentException("八人当前职责未完整识别，不得生成部分标点。");
        }

        var markers = snapshot.CurrentWave % 2 == 1
            ? ResolveOdd(snapshot.Players)
            : ResolveEven(snapshot.Players);

        if (markers.Count != 8 || markers.Values.Distinct().Count() != 8)
        {
            throw new MarkerAssignmentException("标点结果不完整或存在重复 Marker，已拒绝提交。");
        }

        return new ValidatedMarkerAssignment(snapshot.CurrentWave, markers);
    }

    private static IReadOnlyDictionary<RoleSlot, PartyMarker> ResolveOdd(
        IReadOnlyDictionary<RoleSlot, PlayerMechanicSnapshot> players)
    {
        return players.ToDictionary(
            entry => entry.Key,
            entry => entry.Value.CurrentMechanic switch
            {
                ForsakenMechanic.Fan => PartyMarker.Ignore1,
                ForsakenMechanic.Steel => PartyMarker.Ignore2,
                ForsakenMechanic.Share when entry.Key.IsTankOrHealer() => PartyMarker.Bind1,
                ForsakenMechanic.Share => PartyMarker.Bind2,
                ForsakenMechanic.Idle when entry.Key.IsTank() => PartyMarker.Attack1,
                ForsakenMechanic.Idle when entry.Key is RoleSlot.D1 or RoleSlot.D2 => PartyMarker.Attack2,
                ForsakenMechanic.Idle when entry.Key.IsHealer() => PartyMarker.Attack3,
                ForsakenMechanic.Idle when entry.Key is RoleSlot.D3 or RoleSlot.D4 => PartyMarker.Attack4,
                _ => throw new MarkerAssignmentException($"{entry.Key} 的奇数轮机制无法映射标点。"),
            });
    }

    private static IReadOnlyDictionary<RoleSlot, PartyMarker> ResolveEven(
        IReadOnlyDictionary<RoleSlot, PlayerMechanicSnapshot> players)
    {
        var markers = new Dictionary<RoleSlot, PartyMarker>();
        foreach (var entry in players)
        {
            var role = entry.Key;
            var player = entry.Value;
            var pairMate = players.Values.Single(candidate =>
                candidate.Pair == player.Pair && candidate.Role != role);

            var left = role.IsTankOrHealer();
            if (pairMate.CurrentMechanic == player.CurrentMechanic && role.IsNear())
            {
                left = !left;
            }

            markers[role] = player.CurrentMechanic switch
            {
                ForsakenMechanic.Fan => left ? PartyMarker.Bind1 : PartyMarker.Bind2,
                ForsakenMechanic.Steel => left ? PartyMarker.Ignore1 : PartyMarker.Ignore2,
                ForsakenMechanic.Idle when role.IsNear() =>
                    left ? PartyMarker.Attack1 : PartyMarker.Attack2,
                ForsakenMechanic.Idle => left ? PartyMarker.Attack3 : PartyMarker.Attack4,
                _ => throw new MarkerAssignmentException($"{role} 的偶数轮机制无法映射标点。"),
            };
        }

        return markers;
    }
}
