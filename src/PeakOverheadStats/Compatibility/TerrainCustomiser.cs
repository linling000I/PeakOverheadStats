namespace PeakOverheadStats.Compatibility;

internal static class TerrainCustomiserCompatibility
{
    public static bool Enabled => false;
    public static bool TryGetMapSeed(out int seed) { seed = default; return false; }
}