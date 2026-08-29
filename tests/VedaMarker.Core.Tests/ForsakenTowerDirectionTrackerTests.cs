using VedaMarker.Core;

namespace VedaMarker.Core.Tests;

public sealed class ForsakenTowerDirectionTrackerTests
{
    [Theory]
    [InlineData(1, 0)]
    [InlineData(8, 1)]
    [InlineData(7, 2)]
    [InlineData(2, 7)]
    public void MapEffectIndexUsesActReferenceDirectionConvention(int index, int expected)
    {
        Assert.Equal(expected, ForsakenTowerDirectionTracker.DirectionFromMapEffectIndex(index));
    }

    [Fact]
    public void TwoTowerEventsProduceOneWaveDirection()
    {
        var tracker = new ForsakenTowerDirectionTracker();
        tracker.BeginEncounter();

        Assert.False(tracker.ObserveMapEffect(1, ForsakenTowerDirectionTracker.ActiveMapEffectState, 1, 1_000));
        Assert.True(tracker.ObserveMapEffect(7, ForsakenTowerDirectionTracker.ActiveMapEffectState, 1, 1_050));

        Assert.True(tracker.TryGetDirection(1, out var direction8));
        Assert.Equal(5, direction8);
    }

    [Fact]
    public void InvalidOrInactiveEventsAreIgnored()
    {
        var tracker = new ForsakenTowerDirectionTracker();

        Assert.False(tracker.ObserveMapEffect(1, ForsakenTowerDirectionTracker.ActiveMapEffectState, 1, 1_000));
        tracker.BeginEncounter();
        Assert.False(tracker.ObserveMapEffect(1, 1, 1, 1_000));
        Assert.False(tracker.ObserveMapEffect(9, ForsakenTowerDirectionTracker.ActiveMapEffectState, 1, 1_000));
        Assert.Equal(0, tracker.DirectionCount);
    }

    [Fact]
    public void AmbiguousPairDoesNotCreateDirection()
    {
        var tracker = new ForsakenTowerDirectionTracker();
        tracker.BeginEncounter();

        Assert.False(tracker.ObserveMapEffect(1, ForsakenTowerDirectionTracker.ActiveMapEffectState, 1, 1_000));
        Assert.False(tracker.ObserveMapEffect(8, ForsakenTowerDirectionTracker.ActiveMapEffectState, 1, 1_050));
        Assert.Equal(0, tracker.DirectionCount);
    }

    [Fact]
    public void EventsFromDifferentWavesOrBurstsAreNotPaired()
    {
        var tracker = new ForsakenTowerDirectionTracker();
        tracker.BeginEncounter();

        Assert.False(tracker.ObserveMapEffect(1, ForsakenTowerDirectionTracker.ActiveMapEffectState, 1, 1_000));
        Assert.False(tracker.ObserveMapEffect(7, ForsakenTowerDirectionTracker.ActiveMapEffectState, 2, 1_050));
        Assert.False(tracker.ObserveMapEffect(1, ForsakenTowerDirectionTracker.ActiveMapEffectState, 2, 4_000));
        Assert.Equal(0, tracker.DirectionCount);
    }

    [Fact]
    public void ResetClearsDirectionsAndRequiresNewEncounter()
    {
        var tracker = new ForsakenTowerDirectionTracker();
        tracker.BeginEncounter();
        tracker.ObserveMapEffect(1, ForsakenTowerDirectionTracker.ActiveMapEffectState, 1, 1_000);
        tracker.ObserveMapEffect(7, ForsakenTowerDirectionTracker.ActiveMapEffectState, 1, 1_050);

        tracker.Reset();

        Assert.False(tracker.IsEncounterActive);
        Assert.False(tracker.TryGetDirection(1, out _));
        Assert.False(tracker.ObserveMapEffect(1, ForsakenTowerDirectionTracker.ActiveMapEffectState, 1, 2_000));
    }
}
