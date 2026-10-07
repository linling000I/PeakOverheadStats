using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PeakOverheadStats.Compatibility;

internal enum TfaRouteStatus
{
    NotApplicable,
    Pending,
    Ready,
    Unsupported
}

internal sealed class TfaRouteStage
{
    internal object Source { get; }
    internal int SegmentIndex { get; }
    internal int ChainIndex { get; }
    internal Biome.BiomeType BiomeType { get; }
    internal string TitleKey { get; }
    internal Transform Terrain { get; }

    internal TfaRouteStage(object source, int segmentIndex, int chainIndex,
        Biome.BiomeType biomeType, string titleKey, Transform terrain)
    {
        Source = source;
        SegmentIndex = segmentIndex;
        ChainIndex = chainIndex;
        BiomeType = biomeType;
        TitleKey = titleKey;
        Terrain = terrain;
    }
}

internal sealed class TfaRouteProjection
{
    internal IReadOnlyList<TfaRouteStage> Stages { get; }

    internal TfaRouteProjection(List<TfaRouteStage> stages)
    {
        Stages = stages;
    }
}

internal sealed class TrueFinalAscentCompatibility
{
    private const string PluginGuid = "com.CptPICHU.peak.truefinalascent";
    private static readonly Dictionary<Assembly, StageContract> Contracts = new();
    private static readonly HashSet<Assembly> WarnedAssemblies = new();

    private Assembly? cachedAssembly;
    private Assembly? cachedSeedAssembly;
    private MapHandler? cachedMapHandler;
    private MapHandler.MapSegment[]? cachedSegments;
    private TfaRouteProjection? cachedProjection;
    private StageContract? currentContract;
    private MapHandler? contextMapHandler;
    private Scene contextScene;
    private string? failureReason;
    private bool seedReady;

    internal TfaRouteStatus ReadSeedStatus(Scene scene, MapHandler? mapHandler, bool atTerminal,
        out bool assemblyChanged)
    {
        TfaRouteStatus status = ReadRoute(scene, mapHandler, atTerminal, out _, out _);
        assemblyChanged = !ReferenceEquals(cachedSeedAssembly, cachedAssembly);
        cachedSeedAssembly = cachedAssembly;
        return status;
    }

    internal bool TryGetMapSeed(out int seed)
    {
        seed = default;
        if (!seedReady || currentContract?.Seed == null)
        {
            return false;
        }
        seed = (int)currentContract.Seed.GetValue(null)!;
        return true;
    }

    internal TfaRouteStatus ReadRoute(Scene scene, MapHandler? mapHandler, bool atTerminal,
        out TfaRouteProjection? projection, out int currentIndex)
    {
        projection = null;
        currentIndex = -1;
        seedReady = false;
        if (!BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue(PluginGuid, out BepInEx.PluginInfo plugin))
        {
            ClearProjection();
            cachedAssembly = null;
            currentContract = null;
            failureReason = null;
            return TfaRouteStatus.NotApplicable;
        }
        if (plugin.Instance == null)
        {
            ClearProjection();
            return TfaRouteStatus.Pending;
        }

        Assembly assembly = plugin.Instance.GetType().Assembly;
        if (!ReferenceEquals(cachedAssembly, assembly) || contextScene != scene ||
            !ReferenceEquals(contextMapHandler, mapHandler))
        {
            ClearProjection();
            cachedAssembly = assembly;
            contextScene = scene;
            contextMapHandler = mapHandler;
            failureReason = null;
        }
        if (!Contracts.TryGetValue(assembly, out StageContract? contract))
        {
            contract = new StageContract(assembly);
            Contracts.Add(assembly, contract);
        }
        currentContract = contract;

        if (contract.Active != null && !(bool)contract.Active.GetValue(null)!)
        {
            ClearProjection();
            return TfaRouteStatus.NotApplicable;
        }
        if (contract.FailureReason != null || failureReason != null)
        {
            return Unsupported(plugin, assembly, contract.FailureReason ?? failureReason!);
        }
        if (!(bool)contract.Done!.GetValue(null)! ||
            !(bool)contract.Ready!.GetValue(null)! ||
            (bool)contract.Building!.GetValue(null)! ||
            (bool)contract.Aborted!.GetValue(null)! ||
            mapHandler == null || !MapHandler.ExistsAndInitialized ||
            mapHandler.gameObject.scene != scene || mapHandler.segments == null)
        {
            ClearProjection();
            return TfaRouteStatus.Pending;
        }

        MapHandler.MapSegment[] segments = mapHandler.segments;
        if (cachedMapHandler != mapHandler || !ReferenceEquals(cachedSegments, segments))
        {
            ClearProjection();
            cachedMapHandler = mapHandler;
            cachedSegments = segments;
        }

        object? currentStage = contract.CurrentStage!.GetValue(null);
        currentIndex = FindCurrentStage(contract, currentStage);
        bool terrainChanged = currentIndex >= 0 &&
            (!cachedProjection!.Stages[currentIndex].Terrain ||
             contract.Terrain!.GetValue(currentStage) is not GameObject terrain ||
             terrain == null || terrain.scene != scene ||
             cachedProjection.Stages[currentIndex].Terrain != terrain.transform);
        if (cachedProjection == null || terrainChanged || (currentIndex < 0 && !atTerminal))
        {
            TfaRouteStatus captureStatus = Capture(scene, segments, contract, out string reason);
            if (captureStatus == TfaRouteStatus.Unsupported)
            {
                return Unsupported(plugin, assembly, reason);
            }
            if (captureStatus == TfaRouteStatus.Pending)
            {
                ClearProjection();
                return captureStatus;
            }
            currentIndex = FindCurrentStage(contract, currentStage);
        }
        if (currentIndex < 0 && !atTerminal)
        {
            return Unsupported(plugin, assembly, "CurrentStage does not match the completed stage chain.");
        }

        projection = cachedProjection;
        seedReady = true;
        return TfaRouteStatus.Ready;
    }

    private int FindCurrentStage(StageContract contract, object? currentStage)
    {
        if (cachedProjection == null || currentStage == null ||
            !contract.StageType!.IsInstanceOfType(currentStage))
        {
            return -1;
        }

        int segmentIndex = (int)contract.SegmentIndex!.GetValue(currentStage)!;
        int chainIndex = (int)contract.ChainIndex!.GetValue(currentStage)!;
        for (int i = 0; i < cachedProjection.Stages.Count; i++)
        {
            TfaRouteStage stage = cachedProjection.Stages[i];
            if (ReferenceEquals(stage.Source, currentStage) &&
                stage.SegmentIndex == segmentIndex && stage.ChainIndex == chainIndex)
            {
                return i;
            }
        }
        return -1;
    }

    private TfaRouteStatus Capture(Scene scene, MapHandler.MapSegment[] segments,
        StageContract contract, out string reason)
    {
        reason = "";
        if (contract.AllStages!.Invoke(null, null) is not IEnumerable sources)
        {
            reason = "AllStages returned no stage sequence.";
            return TfaRouteStatus.Unsupported;
        }

        List<TfaRouteStage> stages = new();
        HashSet<(int Segment, int Chain)> identities = new();
        Peak.VoidBiome? voidBiome = Peak.VoidBiome.instance;
        foreach (object? source in sources)
        {
            if (source == null || !contract.StageType!.IsInstanceOfType(source))
            {
                reason = "AllStages contains an invalid stage object.";
                return TfaRouteStatus.Unsupported;
            }
            int segmentIndex = (int)contract.SegmentIndex!.GetValue(source)!;
            int chainIndex = (int)contract.ChainIndex!.GetValue(source)!;
            Biome.BiomeType biomeType = (Biome.BiomeType)contract.BiomeType!.GetValue(source)!;
            if (segmentIndex < 0 || segmentIndex >= segments.Length || chainIndex < 0 ||
                !identities.Add((segmentIndex, chainIndex)))
            {
                reason = $"AllStages has an invalid or repeated identity ({segmentIndex}, {chainIndex}).";
                return TfaRouteStatus.Unsupported;
            }
            if (biomeType == Biome.BiomeType.Void ||
                (voidBiome != null && ReferenceEquals(segments[segmentIndex], voidBiome.segment)))
            {
                continue;
            }
            if (contract.Terrain!.GetValue(source) is not GameObject terrain || terrain == null)
            {
                reason = $"Stage ({segmentIndex}, {chainIndex}) has no terrain.";
                return TfaRouteStatus.Unsupported;
            }
            if (terrain.scene != scene)
            {
                return TfaRouteStatus.Pending;
            }

            string title = contract.Title!.GetValue(source) as string ?? "";
            // TFA's lone-segment registration calls TitleOf(Volcano) for Caldera too.
            if (segmentIndex == 3 && chainIndex == 0 &&
                biomeType == Biome.BiomeType.Volcano && title == "THE KILN")
            {
                title = "CALDERA";
            }
            stages.Add(new TfaRouteStage(source, segmentIndex, chainIndex, biomeType, title, terrain.transform));
        }
        if (stages.Count == 0)
        {
            reason = "The completed stage chain contains no visible route stages.";
            return TfaRouteStatus.Unsupported;
        }

        cachedProjection = new TfaRouteProjection(stages);
        return TfaRouteStatus.Ready;
    }

    private TfaRouteStatus Unsupported(BepInEx.PluginInfo plugin, Assembly assembly, string reason)
    {
        ClearProjection();
        failureReason = reason;
        seedReady = false;
        if (WarnedAssemblies.Add(assembly))
        {
            Plugin.Logger.LogWarning(
                $"True Final Ascent {plugin.Metadata.Version} compatibility failed: {reason} " +
                "Using the original biome collection instead. Map seed display is disabled.");
        }
        return TfaRouteStatus.Unsupported;
    }

    internal void ResetRoute()
    {
        ClearProjection();
        seedReady = false;
    }

    private void ClearProjection()
    {
        cachedMapHandler = null;
        cachedSegments = null;
        cachedProjection = null;
    }

    private sealed class StageContract
    {
        private const BindingFlags StaticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        internal string? FailureReason { get; private set; }
        internal Type? StageType { get; }
        internal PropertyInfo? Active { get; }
        internal PropertyInfo? Seed { get; }
        internal PropertyInfo? Done { get; }
        internal PropertyInfo? Ready { get; }
        internal PropertyInfo? Building { get; }
        internal PropertyInfo? Aborted { get; }
        internal PropertyInfo? CurrentStage { get; }
        internal MethodInfo? AllStages { get; }
        internal FieldInfo? SegmentIndex { get; }
        internal FieldInfo? ChainIndex { get; }
        internal FieldInfo? BiomeType { get; }
        internal FieldInfo? Title { get; }
        internal FieldInfo? Terrain { get; }

        internal StageContract(Assembly assembly)
        {
            Type? ascent = FindType(assembly, "FinalAscent");
            Active = GetProperty(ascent, "Active", typeof(bool));
            Seed = GetProperty(ascent, "Seed", typeof(int));
            Type? builder = FindType(assembly, "FinalAscentBuilder");
            Done = GetProperty(builder, "Done", typeof(bool));
            Ready = GetProperty(builder, "Ready", typeof(bool));
            Building = GetProperty(builder, "Building", typeof(bool));
            Aborted = GetProperty(builder, "Aborted", typeof(bool));
            StageType = FindType(assembly, "FinalAscentStage");
            Type? stageManager = FindType(assembly, "FinalAscentSegments");
            CurrentStage = GetProperty(stageManager, "CurrentStage", StageType);
            AllStages = stageManager?.GetMethod("AllStages", StaticFlags, null, Type.EmptyTypes, null);
            if (AllStages == null || StageType == null ||
                !typeof(IEnumerable<>).MakeGenericType(StageType).IsAssignableFrom(AllStages.ReturnType))
            {
                FailureReason ??= "FinalAscentSegments.AllStages has an unsupported signature.";
            }
            SegmentIndex = GetField("SegmentIndex", typeof(int));
            ChainIndex = GetField("ChainIndex", typeof(int));
            BiomeType = GetField("Type", typeof(Biome.BiomeType));
            Title = GetField("Title", typeof(string));
            Terrain = GetField("Terrain", typeof(GameObject));
        }

        private Type? FindType(Assembly assembly, string name)
        {
            Type? type = assembly.GetType("TrueFinalAscent." + name);
            if (type == null)
            {
                FailureReason ??= $"TrueFinalAscent.{name} is missing.";
            }
            return type;
        }

        private PropertyInfo? GetProperty(Type? type, string name, Type? expectedType)
        {
            PropertyInfo? property = type?.GetProperty(name, StaticFlags);
            if (property == null || property.PropertyType != expectedType ||
                property.GetGetMethod(true)?.IsStatic != true || property.GetIndexParameters().Length != 0)
            {
                FailureReason ??= $"{type?.Name}.{name} has an unsupported signature.";
                return null;
            }
            return property;
        }

        private FieldInfo? GetField(string name, Type expectedType)
        {
            FieldInfo? field = StageType?.GetField(name, InstanceFlags);
            if (field == null || field.FieldType != expectedType)
            {
                FailureReason ??= $"FinalAscentStage.{name} has an unsupported type.";
                return null;
            }
            return field;
        }
    }
}
