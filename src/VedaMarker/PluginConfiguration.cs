using Dalamud.Configuration;

namespace VedaMarker;

public sealed class PluginConfiguration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public int CapturePollingIntervalMs { get; set; } = 100;
}
