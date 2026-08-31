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
    public int Version { get; set; } = 6;

    public int CapturePollingIntervalMs { get; set; } = 100;

    public bool EnableLocalMarkers { get; set; } = true;

    public float LocalMarkerScale { get; set; } = 1f;

    public bool EnableForsakenNativeTelegraphs { get; set; }

    public bool EnableForsakenNativeShareLockon { get; set; }

    public MarkerTargetMode MarkerTargetMode { get; set; } = MarkerTargetMode.SelfOnly;

    public int CustomMarkerRoleMask { get; set; }
}
