using System;

namespace PeakOverheadStats.Compatibility;

internal sealed class PllRouteProjection
{
    public string?[] SegmentTitles { get; } = Array.Empty<string?>();
}

internal static class PLLCompatibility
{
    public static bool Enabled => false;
    public static bool MapActive => false;

    public static bool TryGetMapSeed(out int seed)
    {
        seed = default;
        return false;
    }

    public static bool TryCreateRouteProjection(
        MapHandler.MapSegment[] segments,
        MountainProgressHandler.ProgressPoint[] progressPoints,
        out PllRouteProjection projection)
    {
        projection = new PllRouteProjection();
        return false;
    }
}