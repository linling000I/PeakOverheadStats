using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using Peak;
using Photon.Pun;
using TMPro;
using UnityEngine;
using Zorro.Core;

namespace PeakOverheadStats.MonoBehaviours;

internal sealed class TimerHeightStats : MonoBehaviour
{
    private const float TargetLookupRetryInterval = 1f;
    private const float VanillaFogSpeed = 0.4f;

    private MapHandler.MapSegment? cachedTargetSegment;
    private GameObject? cachedCampfireRoot;
    private Campfire? cachedTargetCampfire;
    private float nextTargetLookupTime;
    private MountainProgressHandler.ProgressPoint? cachedNextProgressPoint;
    private int cachedNextProgressPointIndex = -1;
    private bool cachedIsInNadir;
    private PeakGatePortal? nadirPeakPortal;
    private int? targetHeight;

    private float lastFogUpdateTime = -1f;
    private int lastFogHeight;
    private float lastFogTime = -1f;
    private float lastFogRate;

    private float lastRisingFieldRate;
    private float lastRisingFieldMultiplier;
    private float lastRisingFieldHeight;
    private float lastRisingFieldTime;
    private float lastRisingFieldUpdateTime = -1f;
    private bool hasRisingFieldStats;

    private TMP_Text tmpText = null!;
    private DayNightManager _dayNightManager = null!;
    private MapHandler _mapHandler = null!;
    private OrbFogHandler _orbFogHandler = null!;
    private LavaRising? activeRisingField;

    private static string GetTimeString(float totalSeconds)
    {
        int num = Mathf.FloorToInt(totalSeconds);
        int num2 = num / 3600;
        int num3 = num % 3600 / 60;
        int num4 = num % 60;
        return $"{num2:00}:{num3:00}:{num4:00}";
    }

    private static string FormatTime(float seconds)
    {
        if (seconds < 60f) return Mathf.CeilToInt(seconds) + "s";
        if (seconds < 3600f)
        {
            int num = Mathf.FloorToInt(seconds / 60f);
            int num2 = Mathf.CeilToInt(seconds % 60f);
            return $"{num}:{num2:D2}";
        }
        int num3 = Mathf.FloorToInt(seconds / 3600f);
        int num4 = Mathf.FloorToInt(seconds % 3600f / 60f);
        return $"{num3}:{num4:D2}";
    }

    private void Awake()
    {
        _dayNightManager = FindFirstObjectByType<DayNightManager>();
        _mapHandler = Singleton<MapHandler>.Instance;
        _orbFogHandler = Singleton<OrbFogHandler>.Instance;
    }

    private void Start()
    {
        tmpText = GetComponent<TMP_Text>();
        tmpText.autoSizeTextContainer = true;
        tmpText.textWrappingMode = TextWrappingModes.NoWrap;
        tmpText.alignment = TextAlignmentOptions.Top;
        tmpText.lineSpacing = -40f;
        tmpText.fontSize = 26f;
        tmpText.outlineColor = new Color32(0, 0, 0, byte.MaxValue);
        tmpText.outlineWidth = 0.055f;
    }

    private void Update()
    {
        try
        {
            Character localCharacter = Character.localCharacter;
            Character observedCharacter = Character.observedCharacter;
            if (!tmpText || !localCharacter || !observedCharacter) return;

            CharacterStats stats = observedCharacter.refs.stats;
            StringBuilder sb = new(128);

            AppendTimerText(sb);
            AppendDayNightStatus(sb);
            AppendHeightInfo(sb, stats);
            AppendHazardStats(sb, stats);

            tmpText.text = sb.ToString();
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogInfo(ex.ToString());
        }
    }

    private void AppendTimerText(StringBuilder sb)
    {
        if (!PluginConfig.DisplayTimer.Value) return;
        string timeStr = GetTimeString(GameRef.GetTimeSinceRunStarted(RunManager.Instance));
        sb.Append(timeStr);
    }

    private void AppendDayNightStatus(StringBuilder sb)
    {
        if (!PluginConfig.DisplayDayNightCountdown.Value) return;
        if (sb.Length > 0 && PluginConfig.DisplayTimer.Value) sb.Append(" | ");

        (string status, float countdown) = CalculateDayNightStatus(
            _dayNightManager.timeOfDay, _dayNightManager.dayStart, _dayNightManager.dayEnd);
        sb.AppendFormat("({0}: {1:P0})", status, countdown);
    }

    private (string status, float countdown) CalculateDayNightStatus(float timeOfDay, float dayStart, float dayEnd)
    {
        float dayDuration = dayEnd - dayStart;
        float nightDuration = 24f - dayDuration;
        if (timeOfDay >= dayStart && timeOfDay < dayEnd)
            return ("Day", 1f - (timeOfDay - dayStart) / dayDuration);
        float adjustedTime = timeOfDay < dayStart ? timeOfDay + 24f : timeOfDay;
        return ("Night", 1f - (adjustedTime - dayEnd) / nightDuration);
    }

    private void AppendHeightInfo(StringBuilder sb, CharacterStats stats)
    {
        if (!PluginConfig.DisplayHeight.Value) return;
        UpdateCachedTargetHeight();
        if (sb.Length > 0 && (PluginConfig.DisplayTimer.Value || PluginConfig.DisplayDayNightCountdown.Value))
            sb.AppendLine();
        if (targetHeight == null)
            sb.AppendFormat("{0}m", stats.heightInMeters);
        else
            sb.AppendFormat("{0}m/{1}m", stats.heightInMeters, targetHeight.Value);
    }

    private void UpdateCachedTargetHeight()
    {
        MapHandler.MapSegment? segment = MapHandler.CurrentMapSegment;
        GameObject? campfireRoot = segment?.segmentCampfire;
        bool isInNadir = _mapHandler.inNadir;

        MountainProgressHandler? progressHandler = Singleton<MountainProgressHandler>.Instance;
        MountainProgressHandler.ProgressPoint? nextProgressPoint = null;
        int nextProgressPointIndex = -1;
        if (progressHandler?.progressPoints is { Length: > 0 } progressPoints)
        {
            int candidateIndex = progressHandler.maxProgressPointReached + 1;
            if (candidateIndex >= 0 && candidateIndex < progressPoints.Length)
            {
                nextProgressPointIndex = candidateIndex;
                nextProgressPoint = progressPoints[candidateIndex];
            }
        }

        bool mapContextChanged = !ReferenceEquals(cachedTargetSegment, segment) ||
            cachedCampfireRoot != campfireRoot || cachedIsInNadir != isInNadir;
        bool progressContextChanged = cachedNextProgressPointIndex != nextProgressPointIndex ||
            !ReferenceEquals(cachedNextProgressPoint, nextProgressPoint);
        bool targetContextChanged = mapContextChanged || progressContextChanged;

        if (mapContextChanged)
        {
            cachedTargetSegment = segment;
            cachedCampfireRoot = campfireRoot;
            cachedIsInNadir = isInNadir;
            cachedTargetCampfire = null;
            nextTargetLookupTime = 0f;
            nadirPeakPortal = null;
        }
        if (progressContextChanged)
        {
            cachedNextProgressPointIndex = nextProgressPointIndex;
            cachedNextProgressPoint = nextProgressPoint;
        }

        if (isInNadir)
        {
            if (targetContextChanged || nadirPeakPortal == null)
            {
                PeakGatePortal? peakGatePortal = GetNadirPeakPortal();
                targetHeight = peakGatePortal != null
                    ? ConvertTargetHeight(peakGatePortal.transform.position.y) : null;
            }
            return;
        }

        bool campfireWasDestroyed = cachedTargetCampfire == null && !ReferenceEquals(cachedTargetCampfire, null);
        bool targetLookupDue = Time.unscaledTime >= nextTargetLookupTime && cachedTargetCampfire == null;
        if (campfireWasDestroyed || targetLookupDue)
        {
            cachedTargetCampfire = campfireRoot?.GetComponentInChildren<Campfire>(includeInactive: true);
            nextTargetLookupTime = cachedTargetCampfire != null ? float.PositiveInfinity : Time.unscaledTime + TargetLookupRetryInterval;
        }

        if (!targetContextChanged && !campfireWasDestroyed && !targetLookupDue) return;

        float? campfireHeight = cachedTargetCampfire != null ? cachedTargetCampfire.transform.position.y : null;
        float? progressPointHeight = nextProgressPoint?.transform != null ? nextProgressPoint.transform.position.y : null;
        float? targetHeightInUnits = campfireHeight.HasValue && progressPointHeight.HasValue
            ? Mathf.Max(campfireHeight.Value, progressPointHeight.Value)
            : campfireHeight ?? progressPointHeight;
        targetHeight = targetHeightInUnits.HasValue ? ConvertTargetHeight(targetHeightInUnits.Value) : null;
    }

    private static int ConvertTargetHeight(float heightInUnits) =>
        Mathf.RoundToInt(CharacterStats.unitsToMeters * heightInUnits);

    private PeakGatePortal? GetNadirPeakPortal()
    {
        if (nadirPeakPortal != null) return nadirPeakPortal;
        nadirPeakPortal = cachedTargetSegment?.segmentParent.GetComponentInChildren<PeakGatePortal>(includeInactive: true);
        return nadirPeakPortal;
    }

    private void AppendHazardStats(StringBuilder sb, CharacterStats stats)
    {
        if (!PluginConfig.DisplayFog.Value && !PluginConfig.DisplayLava.Value) return;

        LavaRising? risingFieldRegion = FindRisingFieldForCurrentRegion();
        LavaRising? risingField = PluginConfig.DisplayLava.Value && risingFieldRegion != null &&
            risingFieldRegion.started && !risingFieldRegion.ended && risingFieldRegion.lava != null
            ? risingFieldRegion : null;
        SetActiveRisingField(risingField);

        bool showRisingField = risingField != null;
        bool showFog = PluginConfig.DisplayFog.Value && risingFieldRegion == null &&
            _orbFogHandler != null && _orbFogHandler.isMoving;
        if (!showRisingField && !showFog) return;

        string hazardText; string color; float speedMultiplier;

        if (showRisingField)
        {
            UpdateRisingFieldStats(risingField!, stats);
            if (!hasRisingFieldStats) return;
            color = GetRisingFieldColor(risingField!.risingFieldType);
            hazardText = lastRisingFieldTime > 0f
                ? $"(<#{color}>{Mathf.CeilToInt(lastRisingFieldHeight)}m/{FormatTime(Mathf.CeilToInt(lastRisingFieldTime))}</color>)"
                : $"(<#{color}>{Mathf.CeilToInt(lastRisingFieldHeight)}m</color>)";
            speedMultiplier = lastRisingFieldMultiplier;
        }
        else
        {
            object? sphere = GameRef.GetFogSphere(_orbFogHandler);
            if (sphere == null) return;
            UpdateFogStats(stats, sphere);
            if (Mathf.Approximately(lastFogRate, 0f)) return;
            color = "00BCFF";
            hazardText = lastFogTime > 0f
                ? $"(<#{color}>{lastFogHeight}m/{FormatTime(Mathf.CeilToInt(lastFogTime))}</color>)"
                : $"(<#{color}>{lastFogHeight}m</color>)";
            speedMultiplier = lastFogRate / VanillaFogSpeed;
        }

        bool shouldAddSeparator = sb.Length > 0 &&
            (PluginConfig.DisplayTimer.Value || PluginConfig.DisplayDayNightCountdown.Value || PluginConfig.DisplayHeight.Value);
        if (shouldAddSeparator) sb.Append(" | ");
        sb.Append(hazardText);
        sb.Append($" [<#{color}>{speedMultiplier:F2}x</color>]");
    }

    private void UpdateFogStats(CharacterStats stats, object sphere)
    {
        if (lastFogUpdateTime >= 0f && Time.time - lastFogUpdateTime <= 1f) return;
        (float fogRadius, float fogRate) = FogMovementTracker.GetDisplayState(_orbFogHandler);
        Vector3 fogPoint = GameRef.GetFogPoint(sphere);
        Character? character = GameRef.GetCharacter(stats);
        Vector3 center = character != null ? character.Center : Vector3.zero;
        Vector3 targetOffset = center - fogPoint;
        float distance = targetOffset.magnitude;
        float distanceDelta = distance - fogRadius;
        lastFogRate = fogRate;
        lastFogUpdateTime = Time.time;
        float fogHeightInUnits = distance > 0.0001f ? fogPoint.y + targetOffset.y / distance * fogRadius : fogPoint.y;
        lastFogHeight = Mathf.RoundToInt(fogHeightInUnits * CharacterStats.unitsToMeters);
        lastFogTime = distanceDelta < 0f && lastFogRate > 0f ? Mathf.Abs(distanceDelta / lastFogRate) : 0f;
    }

    private LavaRising? FindRisingFieldForCurrentRegion()
    {
        Segment currentMapSegment = _mapHandler.GetCurrentSegment();
        LavaRising.RisingFieldType expectedType = GetExpectedRisingFieldType(currentMapSegment);
        foreach (LavaRising risingField in LavaRising.ALL_LAVA)
        {
            if (!risingField || !risingField.isActiveAndEnabled ||
                risingField.requiredSegment != currentMapSegment ||
                risingField.risingFieldType != expectedType)
                continue;
            return risingField;
        }
        return null;
    }

    private LavaRising.RisingFieldType GetExpectedRisingFieldType(Segment currentMapSegment)
    {
        Biome.BiomeType currentBiome = _mapHandler.GetCurrentBiome();
        if (currentMapSegment == Segment.Void || currentBiome == Biome.BiomeType.Void)
            return LavaRising.RisingFieldType.VoidGhosts;
        return currentBiome == Biome.BiomeType.Swamp
            ? LavaRising.RisingFieldType.Gloom : LavaRising.RisingFieldType.Lava;
    }

    private void SetActiveRisingField(LavaRising? risingField)
    {
        if (activeRisingField == risingField) return;
        activeRisingField = risingField;
        lastRisingFieldRate = 0f; lastRisingFieldMultiplier = 0f;
        lastRisingFieldHeight = 0f; lastRisingFieldTime = 0f;
        lastRisingFieldUpdateTime = -1f; hasRisingFieldStats = false;
        if (risingField != null) RisingFieldMovementTracker.EnsureTracking(risingField);
    }

    private void UpdateRisingFieldStats(LavaRising risingField, CharacterStats stats)
    {
        if (lastRisingFieldUpdateTime >= 0f && Time.time - lastRisingFieldUpdateTime <= 1f) return;
        (float timeTraveled, float progressRate) = RisingFieldMovementTracker.GetDisplayState(risingField);
        float startHeight = GameRef.GetStartHeight(risingField) * CharacterStats.unitsToMeters;
        float topHeight = risingField.topTransform.position.y * CharacterStats.unitsToMeters;
        float baseRate = risingField.travelTime > 0.0001f ? (topHeight - startHeight) / risingField.travelTime : 0f;
        lastRisingFieldRate = baseRate * progressRate;
        lastRisingFieldMultiplier = progressRate;
        float normalizedProgress = risingField.travelTime > 0.0001f ? Mathf.Clamp01(timeTraveled / risingField.travelTime) : 0f;
        lastRisingFieldHeight = Mathf.Lerp(startHeight, topHeight, normalizedProgress);
        float heightDelta = stats.heightInMeters - lastRisingFieldHeight;
        lastRisingFieldTime = heightDelta > 0f && lastRisingFieldRate > 0.001f ? heightDelta / lastRisingFieldRate : 0f;
        lastRisingFieldUpdateTime = Time.time;
        hasRisingFieldStats = true;
    }

    private static string GetRisingFieldColor(LavaRising.RisingFieldType fieldType) => fieldType switch
    {
        LavaRising.RisingFieldType.Gloom => "B266FF",
        LavaRising.RisingFieldType.VoidGhosts => "D8D8FF",
        _ => "FF4500"
    };
}

[HarmonyWrapSafe]
internal static class FogMovementTracker
{
    private const float MinimumFogRadius = 30f;
    private const float RateSampleWindow = 0.5f;
    private const float MinimumSampleInterval = 0.0001f;

    private static OrbFogHandler? trackedHandler;
    private static bool hasSample;
    private static float previousSize;
    private static float previousSampleTime;
    private static float accumulatedSizeDelta;
    private static float accumulatedSampleTime;
    private static float measuredRate;
    private static bool hasMeasuredRate;
    private static bool hasSynchronizedSample;
    private static float previousSynchronizedSize;
    private static float previousSynchronizedTime;
    private static float synchronizedSize;
    private static float synchronizedTime;
    private static float synchronizedRate;
    private static bool hasSynchronizedRate;

    internal static (float radius, float rate) GetDisplayState(OrbFogHandler handler)
    {
        if (!PhotonNetwork.IsMasterClient && trackedHandler == handler && hasSynchronizedRate)
        {
            float elapsed = Mathf.Max(0f, Time.time - synchronizedTime);
            float radius = Mathf.Max(MinimumFogRadius, synchronizedSize - synchronizedRate * elapsed);
            return (radius, synchronizedRate);
        }
        float rate = trackedHandler == handler && hasMeasuredRate ? measuredRate : handler.speed;
        return (handler.currentSize, rate);
    }

    private static void Reset(OrbFogHandler handler)
    {
        trackedHandler = handler;
        ResetLocalSample(handler);
        hasSynchronizedSample = false;
        previousSynchronizedSize = 0f; previousSynchronizedTime = 0f;
        synchronizedSize = 0f; synchronizedTime = 0f; synchronizedRate = 0f;
        hasSynchronizedRate = false;
    }

    private static void ResetLocalSample(OrbFogHandler handler)
    {
        hasSample = true;
        previousSize = handler.currentSize;
        previousSampleTime = Time.time;
        accumulatedSizeDelta = 0f; accumulatedSampleTime = 0f;
        measuredRate = 0f; hasMeasuredRate = false;
    }

    private static void ObserveSynchronizedFog(OrbFogHandler handler, float size, bool isMoving)
    {
        if (trackedHandler != handler) Reset(handler);
        if (!isMoving) { Reset(handler); return; }
        float now = Time.time;
        if (hasSynchronizedSample)
        {
            float elapsed = now - previousSynchronizedTime;
            if (elapsed > MinimumSampleInterval)
            {
                synchronizedRate = (previousSynchronizedSize - size) / elapsed;
                hasSynchronizedRate = true;
            }
        }
        hasSynchronizedSample = true;
        previousSynchronizedSize = size; previousSynchronizedTime = now;
        synchronizedSize = size; synchronizedTime = now;
        ResetLocalSample(handler);
    }

    private static void Sample(OrbFogHandler handler)
    {
        if (trackedHandler != handler || !handler.isMoving) { Reset(handler); return; }
        if (!hasSample) { Reset(handler); return; }
        float now = Time.time;
        float elapsed = now - previousSampleTime;
        if (elapsed <= MinimumSampleInterval) return;
        accumulatedSizeDelta += previousSize - handler.currentSize;
        accumulatedSampleTime += elapsed;
        previousSize = handler.currentSize; previousSampleTime = now;
        if (accumulatedSampleTime < RateSampleWindow) return;
        measuredRate = accumulatedSizeDelta / accumulatedSampleTime;
        hasMeasuredRate = true;
        accumulatedSizeDelta = 0f; accumulatedSampleTime = 0f;
    }

    [HarmonyPatch(typeof(OrbFogHandler), "Update")]
    [HarmonyPostfix]
    private static void OrbFogHandlerUpdatePostfix(OrbFogHandler __instance) => Sample(__instance);

    [HarmonyPatch(typeof(OrbFogHandler), nameof(OrbFogHandler.RPCA_SyncFog))]
    [HarmonyPostfix]
    private static void OrbFogHandlerSyncFogPostfix(OrbFogHandler __instance) =>
        ObserveSynchronizedFog(__instance, __instance.currentSize, __instance.isMoving);

    [HarmonyPatch(typeof(OrbFogHandler), nameof(OrbFogHandler.RPC_InitFog))]
    [HarmonyPostfix]
    private static void OrbFogHandlerInitFogPostfix(OrbFogHandler __instance) => Reset(__instance);

    [HarmonyPatch(typeof(OrbFogHandler), nameof(OrbFogHandler.StartMovingRPC))]
    [HarmonyPostfix]
    private static void OrbFogHandlerStartMovingPostfix(OrbFogHandler __instance) => Reset(__instance);

    [HarmonyPatch(typeof(OrbFogHandler), nameof(OrbFogHandler.SetFogOrigin))]
    [HarmonyPostfix]
    private static void OrbFogHandlerSetFogOriginPostfix(OrbFogHandler __instance) => Reset(__instance);
}

[HarmonyWrapSafe]
internal static class RisingFieldMovementTracker
{
    private const float RateSampleWindow = 0.5f;
    private const float MinimumSampleInterval = 0.0001f;

    private sealed class SampleState
    {
        internal float PreviousTimeTraveled;
        internal float PreviousSampleTime;
        internal float AccumulatedTravelDelta;
        internal float AccumulatedSampleTime;
        internal float MeasuredRate;
        internal bool HasMeasuredRate;
        internal bool HasSynchronizedSample;
        internal float PreviousSynchronizedTimeTraveled;
        internal float PreviousSynchronizedTime;
        internal float SynchronizedTimeTraveled;
        internal float SynchronizedTime;
        internal float SynchronizedRate;
        internal bool HasSynchronizedRate;
    }

    private static readonly Dictionary<LavaRising, SampleState> sampleStates = new();

    internal static (float timeTraveled, float rate) GetDisplayState(LavaRising handler)
    {
        if (sampleStates.TryGetValue(handler, out SampleState? state))
        {
            if (!PhotonNetwork.IsMasterClient && state.HasSynchronizedRate)
            {
                float elapsed = Mathf.Max(0f, Time.time - state.SynchronizedTime);
                float predictedTime = Mathf.Clamp(state.SynchronizedTimeTraveled + state.SynchronizedRate * elapsed, 0f, Mathf.Max(0f, handler.travelTime));
                return (predictedTime, state.SynchronizedRate);
            }
            if (state.HasMeasuredRate) return (handler.timeTraveled, state.MeasuredRate);
        }
        return (handler.timeTraveled, handler.started && !handler.ended ? 1f : 0f);
    }

    internal static void Reset(LavaRising handler)
    {
        sampleStates[handler] = new SampleState
        {
            PreviousTimeTraveled = handler.timeTraveled,
            PreviousSampleTime = Time.time
        };
    }

    internal static void EnsureTracking(LavaRising handler)
    {
        if (!sampleStates.ContainsKey(handler)) Reset(handler);
    }

    private static void ObserveSynchronizedField(LavaRising handler)
    {
        if (!handler.started || handler.ended) { sampleStates.Remove(handler); return; }
        EnsureTracking(handler);
        SampleState state = sampleStates[handler];
        float now = Time.time;
        float timeTraveled = handler.timeTraveled;
        if (state.HasSynchronizedSample && timeTraveled < state.PreviousSynchronizedTimeTraveled)
        {
            state.HasSynchronizedSample = false;
            state.HasSynchronizedRate = false;
        }
        if (state.HasSynchronizedSample)
        {
            float elapsed = now - state.PreviousSynchronizedTime;
            if (elapsed > MinimumSampleInterval)
            {
                state.SynchronizedRate = (timeTraveled - state.PreviousSynchronizedTimeTraveled) / elapsed;
                state.HasSynchronizedRate = true;
            }
        }
        state.HasSynchronizedSample = true;
        state.PreviousSynchronizedTimeTraveled = timeTraveled;
        state.PreviousSynchronizedTime = now;
        state.SynchronizedTimeTraveled = timeTraveled;
        state.SynchronizedTime = now;
        state.PreviousTimeTraveled = timeTraveled;
        state.PreviousSampleTime = now;
        state.AccumulatedTravelDelta = 0f;
        state.AccumulatedSampleTime = 0f;
        state.MeasuredRate = 0f;
        state.HasMeasuredRate = false;
    }

    private static void Sample(LavaRising handler)
    {
        if (!handler.started || handler.ended) { sampleStates.Remove(handler); return; }
        if (!sampleStates.TryGetValue(handler, out SampleState? state)) { Reset(handler); return; }
        float now = Time.time;
        float elapsed = now - state.PreviousSampleTime;
        if (elapsed <= MinimumSampleInterval) return;
        state.AccumulatedTravelDelta += handler.timeTraveled - state.PreviousTimeTraveled;
        state.AccumulatedSampleTime += elapsed;
        state.PreviousTimeTraveled = handler.timeTraveled;
        state.PreviousSampleTime = now;
        if (state.AccumulatedSampleTime < RateSampleWindow) return;
        state.MeasuredRate = state.AccumulatedTravelDelta / state.AccumulatedSampleTime;
        state.HasMeasuredRate = true;
        state.AccumulatedTravelDelta = 0f;
        state.AccumulatedSampleTime = 0f;
    }

    [HarmonyPatch(typeof(LavaRising), "Update")]
    [HarmonyPostfix]
    private static void LavaRisingUpdatePostfix(LavaRising __instance) => Sample(__instance);

    [HarmonyPatch(typeof(LavaRising), nameof(LavaRising.RecieveLavaData))]
    [HarmonyPostfix]
    private static void LavaRisingReceiveDataPostfix(LavaRising __instance) => ObserveSynchronizedField(__instance);

    [HarmonyPatch(typeof(LavaRising), "OnDisable")]
    [HarmonyPostfix]
    private static void LavaRisingOnDisablePostfix(LavaRising __instance) => sampleStates.Remove(__instance);
}