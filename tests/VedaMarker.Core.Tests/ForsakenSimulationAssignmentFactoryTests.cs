using VedaMarker.Core;

namespace VedaMarker.Core.Tests;

public sealed class ForsakenSimulationAssignmentFactoryTests
{
    [Fact]
    public void EverySimulationWaveProducesAllEightUniqueMarkers()
    {
        foreach (var wave in Enumerable.Range(1, 8))
        {
            var assignment = ForsakenSimulationAssignmentFactory.Create(wave);

            Assert.Equal(wave, assignment.Wave);
            Assert.Equal(Enum.GetValues<RoleSlot>(), assignment.Markers.Keys.OrderBy(role => role));
            Assert.Equal(Enum.GetValues<PartyMarker>(), assignment.Markers.Values.OrderBy(marker => marker));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    public void RejectsOutOfRangeWave(int wave)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ForsakenSimulationAssignmentFactory.Create(wave));
    }
}
