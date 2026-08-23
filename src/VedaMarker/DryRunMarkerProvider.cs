using VedaMarker.Core;

namespace VedaMarker;

internal interface IMarkerProvider
{
    string Name { get; }

    bool IsNative { get; }

    void Submit(ValidatedMarkerAssignment assignment);

    void Clear();
}

internal sealed class DryRunMarkerProvider : IMarkerProvider
{
    public string Name => "Dry-run";

    public bool IsNative => false;

    public ValidatedMarkerAssignment? LastAssignment { get; private set; }

    public void Submit(ValidatedMarkerAssignment assignment)
    {
        if (assignment.Markers.Count != 8 || assignment.Markers.Values.Distinct().Count() != 8)
        {
            throw new MarkerAssignmentException("Dry-run provider 拒绝不完整或重复的标点集合。");
        }

        LastAssignment = assignment;
    }

    public void Clear()
    {
        LastAssignment = null;
    }
}
