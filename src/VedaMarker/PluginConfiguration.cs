using Dalamud.Configuration;

namespace VedaMarker;

public sealed class PluginConfiguration : IPluginConfiguration
{
    public int Version { get; set; } = 2;

    public int CapturePollingIntervalMs { get; set; } = 100;

    public bool EnableExperimentalPartyMarkers { get; set; }

    public int MarkerCommandIntervalMs { get; set; } = 150;
}
