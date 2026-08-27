using Dalamud.Configuration;

namespace VedaMarker;

public enum MarkerTargetMode
{
    SelfOnly,
    CustomRoles,
    AllRoles,
}

public sealed class PluginConfiguration : IPluginConfiguration
{
    public int Version { get; set; } = 3;

    public int CapturePollingIntervalMs { get; set; } = 100;

    public bool EnableExperimentalPartyMarkers { get; set; }

    public int MarkerCommandIntervalMs { get; set; } = 150;

    public MarkerTargetMode MarkerTargetMode { get; set; } = MarkerTargetMode.SelfOnly;

    public int CustomMarkerRoleMask { get; set; }
}
