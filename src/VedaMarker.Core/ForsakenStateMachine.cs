namespace VedaMarker.Core;

public sealed class ForsakenStateMachine
{
    private sealed class PlayerState(RoleSlot role)
    {
        public RoleSlot Role { get; } = role;
        public PairId Pair { get; } = role.Pair();
        public InitialGroup InitialGroup { get; set; }
        public ForsakenMechanic InitialMechanic { get; set; }
        public ForsakenMechanic CurrentMechanic { get; set; }
        public ForsakenMechanic PendingMechanic { get; set; }
        public int? NextResolveWave { get; set; }
        public PartyMarker? CurrentMarker { get; set; }

        public PlayerMechanicSnapshot Snapshot() => new(
            Role,
            Pair,
            InitialGroup,
            InitialMechanic,
            CurrentMechanic,
            PendingMechanic,
            NextResolveWave,
            CurrentMarker);
    }

    private readonly Dictionary<RoleSlot, PlayerState> players = [];

    public ForsakenEncounterStatus Status { get; private set; } = ForsakenEncounterStatus.Inactive;

    public int CurrentWave { get; private set; }

    public ForsakenSnapshot Snapshot => new(
        Status,
        CurrentWave,
        players.ToDictionary(entry => entry.Key, entry => entry.Value.Snapshot()));

    public void IdentifyOpening(IReadOnlyDictionary<RoleSlot, ForsakenMechanic> openingMechanics)
    {
        if (Status != ForsakenEncounterStatus.Inactive)
        {
            throw new ForsakenStateException("开场识别只能从 Inactive 状态开始。");
        }

        ValidateRoles(openingMechanics, Enum.GetValues<RoleSlot>(), "开场点名");
        if (openingMechanics.Values.Any(mechanic => mechanic is ForsakenMechanic.Unknown or ForsakenMechanic.Idle))
        {
            throw new ForsakenStateException("开场八人必须全部具有可识别的真实点名。");
        }

        var towerPairs = Enum.GetValues<PairId>()
            .Where(pair => openingMechanics.Any(entry => entry.Key.Pair() == pair && entry.Value == ForsakenMechanic.Share))
            .ToHashSet();
        if (towerPairs.Count != 2)
        {
            throw new ForsakenStateException($"开场必须识别到两个含分摊的 Pair，当前为 {towerPairs.Count} 个。");
        }

        players.Clear();
        foreach (var role in Enum.GetValues<RoleSlot>())
        {
            players[role] = new PlayerState(role)
            {
                InitialGroup = towerPairs.Contains(role.Pair())
                    ? InitialGroup.InitialTower
                    : InitialGroup.InitialIdle,
                InitialMechanic = openingMechanics[role],
            };
        }

        CurrentWave = 0;
        Status = ForsakenEncounterStatus.OpeningIdentified;
    }

    public void BeginWave(
        int wave,
        IReadOnlyDictionary<RoleSlot, ForsakenMechanic>? observedMechanics = null)
    {
        if (wave is < 1 or > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(wave), "Forsaken wave must be between 1 and 8.");
        }

        if (Status is not (ForsakenEncounterStatus.OpeningIdentified or ForsakenEncounterStatus.WaveResolved))
        {
            throw new ForsakenStateException("只有开场识别完成或上一轮结算后才能开始下一轮。");
        }

        if (wave != CurrentWave + 1)
        {
            throw new ForsakenStateException($"轮次必须连续推进：当前 {CurrentWave}，请求 {wave}。");
        }

        var activeGroup = ActiveGroupFor(wave);
        var activeRoles = players.Values
            .Where(player => player.InitialGroup == activeGroup)
            .Select(player => player.Role)
            .OrderBy(role => role)
            .ToArray();

        if (wave is 2 or 3 or 5 or 6 or 7)
        {
            if (observedMechanics is null)
            {
                throw new ForsakenStateException($"Wave {wave} 需要当前处理组的新点名。");
            }

            ValidateRoles(observedMechanics, activeRoles, $"Wave {wave} 新点名");
            if (observedMechanics.Values.Any(mechanic => mechanic is ForsakenMechanic.Unknown or ForsakenMechanic.Idle))
            {
                throw new ForsakenStateException($"Wave {wave} 当前处理组存在未识别点名。");
            }
        }
        else if (observedMechanics is { Count: > 0 })
        {
            throw new ForsakenStateException($"Wave {wave} 应复用已保存点名，不接受新的点名集合。");
        }

        if (wave == 4 && players.Values
                .Where(player => player.InitialGroup == InitialGroup.InitialTower)
                .Any(player => player.PendingMechanic == ForsakenMechanic.Unknown || player.NextResolveWave != 8))
        {
            throw new ForsakenStateException("进入 Wave 4 前必须完整保存初始踩塔组的 Wave 8 Pending 点名。");
        }

        foreach (var player in players.Values)
        {
            player.CurrentMechanic = ForsakenMechanic.Idle;
            player.CurrentMarker = null;
        }

        foreach (var role in activeRoles)
        {
            players[role].CurrentMechanic = wave switch
            {
                1 or 4 => players[role].InitialMechanic,
                8 => PendingForWave8(players[role]),
                _ => observedMechanics![role],
            };
        }

        CurrentWave = wave;
        Status = ForsakenEncounterStatus.WaveActive;
    }

    public void ApplyMarkers(ValidatedMarkerAssignment assignment)
    {
        if (Status != ForsakenEncounterStatus.WaveActive || assignment.Wave != CurrentWave)
        {
            throw new ForsakenStateException("只能为当前活动轮次应用完整标点。");
        }

        ValidateRoles(assignment.Markers, Enum.GetValues<RoleSlot>(), "标点分配");
        foreach (var entry in assignment.Markers)
        {
            players[entry.Key].CurrentMarker = entry.Value;
        }
    }

    public void ResolveWave(int wave)
    {
        if (Status != ForsakenEncounterStatus.WaveActive || wave != CurrentWave)
        {
            throw new ForsakenStateException($"无法结算非活动轮次 Wave {wave}。");
        }

        foreach (var player in players.Values)
        {
            player.CurrentMechanic = ForsakenMechanic.Unknown;
            player.CurrentMarker = null;
            if (wave == 8 && player.InitialGroup == InitialGroup.InitialTower)
            {
                player.PendingMechanic = ForsakenMechanic.Unknown;
                player.NextResolveWave = null;
            }
        }

        Status = ForsakenEncounterStatus.WaveResolved;
    }

    public void StorePendingForWave8(IReadOnlyDictionary<RoleSlot, ForsakenMechanic> pendingMechanics)
    {
        if (Status != ForsakenEncounterStatus.WaveResolved || CurrentWave != 3)
        {
            throw new ForsakenStateException("只有 Wave 3 结算后才能保存 Wave 8 Pending 点名。");
        }

        var towerRoles = players.Values
            .Where(player => player.InitialGroup == InitialGroup.InitialTower)
            .Select(player => player.Role)
            .OrderBy(role => role)
            .ToArray();
        ValidateRoles(pendingMechanics, towerRoles, "Wave 8 Pending 点名");
        if (pendingMechanics.Values.Any(mechanic => mechanic is ForsakenMechanic.Unknown or ForsakenMechanic.Idle))
        {
            throw new ForsakenStateException("Wave 8 Pending 点名必须完整可识别。");
        }

        foreach (var role in towerRoles)
        {
            players[role].PendingMechanic = pendingMechanics[role];
            players[role].NextResolveWave = 8;
        }
    }

    public void Reset()
    {
        players.Clear();
        CurrentWave = 0;
        Status = ForsakenEncounterStatus.Inactive;
    }

    private static InitialGroup ActiveGroupFor(int wave) => wave is 1 or 2 or 3 or 8
        ? InitialGroup.InitialTower
        : InitialGroup.InitialIdle;

    private static ForsakenMechanic PendingForWave8(PlayerState player)
    {
        if (player.PendingMechanic == ForsakenMechanic.Unknown || player.NextResolveWave != 8)
        {
            throw new ForsakenStateException($"{player.Role} 缺少 Wave 8 Pending 点名。");
        }

        return player.PendingMechanic;
    }

    private static void ValidateRoles<T>(
        IReadOnlyDictionary<RoleSlot, T> values,
        IEnumerable<RoleSlot> expectedRoles,
        string label)
    {
        var expected = expectedRoles.ToHashSet();
        var actual = values.Keys.ToHashSet();
        if (!actual.SetEquals(expected))
        {
            var missing = string.Join(", ", expected.Except(actual));
            var extra = string.Join(", ", actual.Except(expected));
            throw new ForsakenStateException($"{label}角色集合不完整；缺少 [{missing}]，多出 [{extra}]。");
        }
    }
}
