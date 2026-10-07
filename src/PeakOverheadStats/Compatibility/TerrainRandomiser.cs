namespace PeakOverheadStats.Compatibility;

internal static class TerrainRandomiserCompatibility
{
    public static bool Enabled => false;
    public static bool TryGetMapSeed(out int seed) { seed = default; return false; }
}