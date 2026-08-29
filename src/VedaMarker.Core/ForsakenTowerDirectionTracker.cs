namespace VedaMarker.Core;

public sealed class ForsakenTowerDirectionTracker
{
    public const ushort ActiveMapEffectState = 2;
    public const long TowerPairWindowMs = 2_000;

    private readonly Dictionary<int, int> directionsByWave = [];
    private int? firstTowerDirection;
    private int firstTowerWave;
    private long firstTowerObservedAtMs;

    public bool IsEncounterActive { get; private set; }

    public int DirectionCount => directionsByWave.Count;

    public void BeginEncounter()
    {
        directionsByWave.Clear();
        firstTowerDirection = null;
        firstTowerWave = 0;
        firstTowerObservedAtMs = 0;
        IsEncounterActive = true;
    }

    public bool ObserveMapEffect(
        uint index,
        ushort state,
        int wave,
        long observedAtMs)
    {
        if (!IsEncounterActive
            || state != ActiveMapEffectState
            || index is < 1 or > 8
            || wave is < 1 or > 8
            || directionsByWave.ContainsKey(wave))
        {
            return false;
        }

        var direction = DirectionFromMapEffectIndex((int)index);
        if (firstTowerDirection is null
            || firstTowerWave != wave
            || observedAtMs - firstTowerObservedAtMs > TowerPairWindowMs)
        {
            firstTowerDirection = direction;
            firstTowerWave = wave;
            firstTowerObservedAtMs = observedAtMs;
            return false;
        }

        if (!TryCalculateRelativeDirection(firstTowerDirection.Value, direction, out var relativeDirection))
        {
            firstTowerDirection = direction;
            firstTowerObservedAtMs = observedAtMs;
            return false;
        }

        directionsByWave[wave] = relativeDirection;
        firstTowerDirection = null;
        firstTowerWave = 0;
        firstTowerObservedAtMs = 0;
        return true;
    }

    public bool TryGetDirection(int wave, out int direction8) =>
        directionsByWave.TryGetValue(wave, out direction8);

    public void Reset()
    {
        directionsByWave.Clear();
        firstTowerDirection = null;
        firstTowerWave = 0;
        firstTowerObservedAtMs = 0;
        IsEncounterActive = false;
    }

    public static int DirectionFromMapEffectIndex(int index)
    {
        if (index is < 1 or > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(index), "塔 MapEffect 索引必须在 1 到 8 之间。");
        }

        return (9 - index) % 8;
    }

    public static bool TryCalculateRelativeDirection(
        int firstDirection8,
        int secondDirection8,
        out int relativeDirection8)
    {
        if (firstDirection8 is < 0 or > 7)
        {
            throw new ArgumentOutOfRangeException(nameof(firstDirection8));
        }

        if (secondDirection8 is < 0 or > 7)
        {
            throw new ArgumentOutOfRangeException(nameof(secondDirection8));
        }

        var delta = ((secondDirection8 - firstDirection8 + 12) % 8) - 4;
        if (delta == -4 || Math.Abs(delta) % 2 != 0)
        {
            relativeDirection8 = 0;
            return false;
        }

        var midpoint = (firstDirection8 + (delta / 2) + 8) % 8;
        relativeDirection8 = (midpoint + 4) % 8;
        return true;
    }
}
