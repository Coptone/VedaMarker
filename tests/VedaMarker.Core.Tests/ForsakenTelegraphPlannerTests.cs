using System.Numerics;
using VedaMarker.Core;

namespace VedaMarker.Core.Tests;

public sealed class ForsakenTelegraphPlannerTests
{
    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 4)]
    [InlineData(7, 2)]
    [InlineData(8, 4)]
    public void CreatesEightStationsAndExpectedOddEvenRanges(int wave, int expectedRanges)
    {
        var plan = ForsakenTelegraphPlanner.Create(wave, 0);

        Assert.Equal(8, plan.Stations.Count);
        Assert.Equal(expectedRanges, plan.Telegraphs.Count);
        Assert.All(plan.Telegraphs, telegraph => Assert.True(telegraph.Range > 0));
    }

    [Fact]
    public void DirectionFourRotatesEveryCoordinateByHalfTurn()
    {
        var basePlan = ForsakenTelegraphPlanner.Create(1, 0);
        var rotated = ForsakenTelegraphPlanner.Create(1, 4);

        for (var index = 0; index < basePlan.Stations.Count; index++)
        {
            AssertVectorNear(-basePlan.Stations[index].Position, rotated.Stations[index].Position);
        }

        for (var index = 0; index < basePlan.Telegraphs.Count; index++)
        {
            AssertVectorNear(-basePlan.Telegraphs[index].Origin, rotated.Telegraphs[index].Origin);
            AssertVectorNear(-basePlan.Telegraphs[index].Target, rotated.Telegraphs[index].Target);
        }
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(9, 0)]
    [InlineData(1, -1)]
    [InlineData(1, 8)]
    public void RejectsOutOfRangeWaveOrDirection(int wave, int direction8)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ForsakenTelegraphPlanner.Create(wave, direction8));
    }

    [Fact]
    public void RotationPreservesRangeAndDistanceFromCenter()
    {
        var basePlan = ForsakenTelegraphPlanner.Create(2, 0);
        var rotated = ForsakenTelegraphPlanner.Create(2, 3);

        Assert.Equal(
            basePlan.Stations.Select(station => station.Position.Length()),
            rotated.Stations.Select(station => station.Position.Length()),
            new FloatComparer());
        Assert.Equal(
            basePlan.Telegraphs.Select(telegraph => telegraph.Range),
            rotated.Telegraphs.Select(telegraph => telegraph.Range));
    }

    private static void AssertVectorNear(Vector2 expected, Vector2 actual)
    {
        Assert.InRange(MathF.Abs(expected.X - actual.X), 0f, 0.0001f);
        Assert.InRange(MathF.Abs(expected.Y - actual.Y), 0f, 0.0001f);
    }

    private sealed class FloatComparer : IEqualityComparer<float>
    {
        public bool Equals(float x, float y) => MathF.Abs(x - y) < 0.0001f;

        public int GetHashCode(float obj) => 0;
    }
}
