using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Zorro.Core;

namespace PeakOverheadStats.MonoBehaviours;

internal sealed class MapBiomeInfo
{
    internal Biome.BiomeType BiomeType { get; }

    internal Transform OccurrenceRoot { get; }

    internal List<int> SegmentIndices { get; } = new();

    internal MapBiomeInfo(Biome.BiomeType biomeType, Transform occurrenceRoot)
    {
        BiomeType = biomeType;
        OccurrenceRoot = occurrenceRoot;
    }
}

internal sealed class MapBiomeCapture
{
    internal List<MapBiomeInfo> MapBiomes { get; } = new();

    internal Transform?[] SegmentRoots { get; }

    internal int[] SegmentBiomeIndices { get; }

    internal MapBiomeCapture(int segmentCount)
    {
        SegmentRoots = new Transform?[segmentCount];
        SegmentBiomeIndices = new int[segmentCount];
        Array.Fill(SegmentBiomeIndices, -1);
    }
}

internal static class MapBiomeCollector
{
    private static readonly HashSet<string> MesaKnownVariants = new()
    {
        "ScorpionsHell",
        "CactusHell",
        "CactusForest",
        "DynamiteHell",
        "TornadoHell",
        "TumblerHell",
    };

    private static readonly HashSet<string> RootsKnownVariants = new()
    {
        "CaveMania",
        "DeepWater",
        "BombBeetle",
        "DeepWoods",
        "Clearcut",
    };

    private static readonly HashSet<string> RootsStructuralNames = new()
    {
        "Redwoods",
        "Redwood",
        "Variant",
    };

    internal static MapBiomeCapture Capture(Scene scene)
    {
        if (!TryGetMapHandler(scene, out MapHandler mapHandler))
            return new MapBiomeCapture(0);

        MapBiomeCapture capture = new(mapHandler.segments.Length);
        Dictionary<int, int> occurrences = new();
        Peak.VoidBiome? voidBiome = Peak.VoidBiome.instance;

        for (int segmentIndex = 0; segmentIndex < mapHandler.segments.Length; segmentIndex++)
        {
            MapHandler.MapSegment segment = mapHandler.segments[segmentIndex];
            if (!TryGetOccurrence(
                    scene,
                    segment,
                    voidBiome,
                    out Transform segmentRoot,
                    out Transform occurrenceRoot))
            {
                continue;
            }

            capture.SegmentRoots[segmentIndex] = segmentRoot;
            int occurrenceId = occurrenceRoot.GetInstanceID();
            if (occurrences.TryGetValue(occurrenceId, out int occurrenceIndex))
            {
                capture.SegmentBiomeIndices[segmentIndex] = occurrenceIndex;
                capture.MapBiomes[occurrenceIndex].SegmentIndices.Add(segmentIndex);
                continue;
            }

            occurrenceIndex = capture.MapBiomes.Count;
            occurrences.Add(occurrenceId, occurrenceIndex);
            capture.SegmentBiomeIndices[segmentIndex] = occurrenceIndex;
            MapBiomeInfo mapBiome = new(segment.biome, occurrenceRoot);
            mapBiome.SegmentIndices.Add(segmentIndex);
            capture.MapBiomes.Add(mapBiome);
        }

        return capture;
    }

    internal static bool TryGetCurrentMapBiome(
        Scene scene,
        MapBiomeCapture capture,
        out MapBiomeInfo mapBiome,
        out int segmentIndex
    )
    {
        mapBiome = null!;
        segmentIndex = -1;
        if (!TryGetMapHandler(scene, out MapHandler mapHandler))
            return false;

        MapHandler.MapSegment currentSegment = MapHandler.CurrentMapSegment;
        segmentIndex = Array.IndexOf(mapHandler.segments, currentSegment);
        if (segmentIndex < 0 || segmentIndex >= capture.SegmentBiomeIndices.Length)
            return false;

        int occurrenceIndex = capture.SegmentBiomeIndices[segmentIndex];
        if (occurrenceIndex < 0 || occurrenceIndex >= capture.MapBiomes.Count)
            return false;

        mapBiome = capture.MapBiomes[occurrenceIndex];
        return true;
    }

    internal static bool TryGetCurrentVariant(
        MapBiomeInfo mapBiome,
        Transform? segmentRoot,
        out string variant
    )
    {
        return TryGetCurrentVariant(mapBiome.BiomeType, segmentRoot, mapBiome.OccurrenceRoot, out variant);
    }

    internal static bool TryGetCurrentVariant(Biome.BiomeType biomeType, Transform? root, out string variant)
    {
        return TryGetCurrentVariant(biomeType, root, root, out variant);
    }

    private static bool TryGetCurrentVariant(
        Biome.BiomeType biomeType,
        Transform? segmentRoot,
        Transform? occurrenceRoot,
        out string variant
    )
    {
        variant = "";
        if (!HasVariant(biomeType))
            return true;

        if (segmentRoot == null || !segmentRoot.gameObject.activeInHierarchy)
            return false;

        if (biomeType is Biome.BiomeType.Mesa or Biome.BiomeType.Roots)
        {
            List<string> candidates = CollectActiveVariants<VariantObject>(
                segmentRoot,
                occurrenceRoot ?? segmentRoot,
                biomeType);
            HashSet<string> knownVariants = biomeType == Biome.BiomeType.Mesa
                ? MesaKnownVariants
                : RootsKnownVariants;

            foreach (string candidate in candidates)
            {
                if (knownVariants.Contains(candidate))
                {
                    variant = candidate;
                    return true;
                }
            }

            foreach (string candidate in candidates)
            {
                if (candidate.Equals("Default", StringComparison.Ordinal))
                {
                    variant = candidate;
                    return true;
                }
            }

            if (candidates.Count == 0)
                return false;
            variant = candidates[0];
            return true;
        }

        List<string> biomeVariants = CollectActiveVariants<BiomeVariant>(
            segmentRoot,
            occurrenceRoot ?? segmentRoot,
            biomeType);
        if (biomeVariants.Count == 0)
            return false;

        variant = biomeVariants[0];
        return true;
    }

    internal static bool HasVariant(Biome.BiomeType biomeType)
    {
        return biomeType is Biome.BiomeType.Shore
            or Biome.BiomeType.Tropics
            or Biome.BiomeType.Alpine
            or Biome.BiomeType.Mesa
            or Biome.BiomeType.Roots;
    }

    internal static string CleanVariant(Biome.BiomeType biomeType, string variant)
    {
        if (biomeType == Biome.BiomeType.Mesa)
        {
            if (variant.Equals("NoVariant", StringComparison.Ordinal))
                return "Default";
            if (variant.Equals("CacusHell", StringComparison.Ordinal))
                return "CactusHell";
            return variant;
        }

        if (biomeType != Biome.BiomeType.Roots)
            return variant;

        string[] parts = variant.Split(' ', '-');
        if (parts.Length <= 1)
            return variant;

        for (int i = 0; i < parts.Length; i++)
        {
            string part = parts[i];
            if (part.Length > 0)
                part = char.ToUpperInvariant(part[0]) + part.Substring(1).ToLowerInvariant();
            if (RootsStructuralNames.Contains(part))
                part = "";
            parts[i] = part;
        }

        return string.Join("", parts);
    }

    private static List<string> CollectActiveVariants<T>(
        Transform segmentRoot,
        Transform occurrenceRoot,
        Biome.BiomeType biomeType
    ) where T : Component
    {
        List<string> variants = new();
        AddActiveVariants(segmentRoot, biomeType, variants);
        if (variants.Count == 0 && occurrenceRoot != segmentRoot)
            AddActiveVariants(occurrenceRoot, biomeType, variants);
        return variants;

        void AddActiveVariants(Transform root, Biome.BiomeType type, List<string> result)
        {
            foreach (T component in root.GetComponentsInChildren<T>(includeInactive: true))
            {
                if (!component.gameObject.activeInHierarchy)
                    continue;
                string name = CleanVariant(type, component.name);
                if (!string.IsNullOrWhiteSpace(name) && !result.Contains(name))
                    result.Add(name);
            }
        }
    }

    private static bool TryGetMapHandler(Scene scene, out MapHandler mapHandler)
    {
        mapHandler = null!;
        if (!MapHandler.ExistsAndInitialized)
            return false;

        mapHandler = Singleton<MapHandler>.Instance;
        return mapHandler && mapHandler.gameObject.scene == scene && mapHandler.segments != null;
    }

    private static bool TryGetOccurrence(
        Scene scene,
        MapHandler.MapSegment? segment,
        Peak.VoidBiome? voidBiome,
        out Transform segmentRoot,
        out Transform occurrenceRoot
    )
    {
        segmentRoot = null!;
        occurrenceRoot = null!;
        if (segment == null)
            return false;
        if (voidBiome != null && ReferenceEquals(segment, voidBiome.segment) && !voidBiome.isActive)
            return false;

        GameObject? segmentParent = segment.segmentParent;
        if (!segmentParent || segmentParent.scene != scene)
            return false;

        segmentRoot = segmentParent.transform;
        occurrenceRoot = segmentRoot.parent ?? segmentRoot;
        return true;
    }
}
