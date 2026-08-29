using System.Numerics;

namespace VedaMarker.Core;

public enum ForsakenTelegraphKind
{
    Circle,
    Cone,
}

public sealed record ForsakenStation(string Label, Vector2 Position);

public sealed record ForsakenTelegraph(
    string Label,
    ForsakenTelegraphKind Kind,
    Vector2 Origin,
    Vector2 Target,
    float Range,
    float AngleDegrees);

public sealed record ForsakenTelegraphPlan(
    int Wave,
    int Direction8,
    IReadOnlyList<ForsakenStation> Stations,
    IReadOnlyList<ForsakenTelegraph> Telegraphs);

public static class ForsakenTelegraphPlanner
{
    private static readonly ForsakenStation[] OddStations =
    [
        new("分摊 A", new Vector2(-6.30f, 4.70f)),
        new("待机 A", new Vector2(-8.80f, 2.50f)),
        new("扇形", new Vector2(-5.66f, 9.16f)),
        new("扇形朝向", new Vector2(-5.66f, 11.66f)),
        new("钢铁", new Vector2(2.20f, 6.50f)),
        new("分摊 B", new Vector2(8.19f, 3.66f)),
        new("待机 C", new Vector2(8.70f, 2.34f)),
        new("待机 D", new Vector2(9.59f, 3.45f)),
    ];

    private static readonly ForsakenTelegraph[] OddTelegraphs =
    [
        new(
            "扇形 90° / 30m",
            ForsakenTelegraphKind.Cone,
            new Vector2(-5.66f, 9.16f),
            new Vector2(-5.66f, 11.66f),
            30f,
            90f),
        new(
            "钢铁 5m",
            ForsakenTelegraphKind.Circle,
            new Vector2(2.20f, 6.50f),
            new Vector2(2.20f, 6.50f),
            5f,
            360f),
    ];

    private static readonly ForsakenStation[] EvenStations =
    [
        new("扇形 A", new Vector2(-9.51f, 6.27f)),
        new("扇形 B", new Vector2(-8.30f, 8.52f)),
        new("钢铁 A", new Vector2(4.71f, 7.72f)),
        new("钢铁 B", new Vector2(8.69f, 3.22f)),
        new("待机 A", new Vector2(-1.90f, -3.50f)),
        new("待机 B", new Vector2(-3.20f, 2.40f)),
        new("待机 C", new Vector2(3.97f, -3.55f)),
        new("待机 D", new Vector2(3.20f, 2.40f)),
    ];

    private static readonly ForsakenTelegraph[] EvenTelegraphs =
    [
        new(
            "扇形 A 90° / 30m",
            ForsakenTelegraphKind.Cone,
            new Vector2(-9.51f, 6.27f),
            new Vector2(-8.30f, 8.52f),
            30f,
            90f),
        new(
            "扇形 B 90° / 30m",
            ForsakenTelegraphKind.Cone,
            new Vector2(-8.30f, 8.52f),
            new Vector2(-9.51f, 6.27f),
            30f,
            90f),
        new(
            "钢铁 A 5m",
            ForsakenTelegraphKind.Circle,
            new Vector2(4.71f, 7.72f),
            new Vector2(4.71f, 7.72f),
            5f,
            360f),
        new(
            "钢铁 B 5m",
            ForsakenTelegraphKind.Circle,
            new Vector2(8.69f, 3.22f),
            new Vector2(8.69f, 3.22f),
            5f,
            360f),
    ];

    public static ForsakenTelegraphPlan Create(int wave, int direction8)
    {
        if (wave is < 1 or > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(wave), "模拟轮次必须在 1 到 8 之间。");
        }

        if (direction8 is < 0 or > 7)
        {
            throw new ArgumentOutOfRangeException(nameof(direction8), "方向必须在 0 到 7 之间。");
        }

        var stations = wave % 2 == 1 ? OddStations : EvenStations;
        var telegraphs = wave % 2 == 1 ? OddTelegraphs : EvenTelegraphs;
        return new ForsakenTelegraphPlan(
            wave,
            direction8,
            stations.Select(station => station with
            {
                Position = Rotate(station.Position, direction8),
            }).ToArray(),
            telegraphs.Select(telegraph => telegraph with
            {
                Origin = Rotate(telegraph.Origin, direction8),
                Target = Rotate(telegraph.Target, direction8),
            }).ToArray());
    }

    private static Vector2 Rotate(Vector2 value, int direction8)
    {
        var radians = direction8 * MathF.PI / 4f;
        var cosine = MathF.Cos(radians);
        var sine = MathF.Sin(radians);
        return new Vector2(
            (value.X * cosine) - (value.Y * sine),
            (value.X * sine) + (value.Y * cosine));
    }
}
