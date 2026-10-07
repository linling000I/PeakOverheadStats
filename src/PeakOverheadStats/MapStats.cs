using System;
using PeakOverheadStats.Compatibility;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using Zorro.Core;

namespace PeakOverheadStats.MonoBehaviours;

internal sealed class MapStats : MonoBehaviour
{
    private const float RefreshInterval = 1f;
    private const int MaxVisibleRouteEntries = 6;
    private const string CurrentSegmentColorTag = "#FFD54F";

    internal const float MapTextRightMargin = 4f;

    private static readonly IReadOnlyDictionary<Biome.BiomeType, IReadOnlyDictionary<string, string>> SimplifiedChineseVariantNames =
        new Dictionary<Biome.BiomeType, IReadOnlyDictionary<string, string>>
        {
            [Biome.BiomeType.Shore] = new Dictionary<string, string>
            {
                ["Default"] = "默认",
                ["SnakeBeach"] = "蛇岩",
                ["RedBeach"] = "赤沙",
                ["BlueBeach"] = "蓝沙",
                ["JellyHell"] = "水母地狱",
                ["BlackSand"] = "黑沙",
            },
            [Biome.BiomeType.Tropics] = new Dictionary<string, string>
            {
                ["Default"] = "默认",
                ["Lava"] = "熔岩管",
                ["Pillars"] = "巨柱",
                ["Thorny"] = "巨棘藤",
                ["Bombs"] = "爆菇",
                ["Ivy"] = "紫毒藤",
                ["SkyJungle"] = "浮岛",
            },
            [Biome.BiomeType.Roots] = new Dictionary<string, string>
            {
                ["Default"] = "默认",
                ["CaveMania"] = "洞窟丛生",
                ["DeepWater"] = "赤水孢泽",
                ["BombBeetle"] = "爆菇甲虫",
                ["DeepWoods"] = "幽深密林",
                ["Clearcut"] = "皆伐菌原",
            },
            [Biome.BiomeType.Alpine] = new Dictionary<string, string>
            {
                ["Default"] = "默认",
                ["Lava"] = "熔岩管",
                ["Spiky"] = "冰刺",
                ["GeyserHell"] = "间歇泉地狱",
            },
            [Biome.BiomeType.Mesa] = new Dictionary<string, string>
            {
                ["Default"] = "默认",
                ["ScorpionsHell"] = "毒蝎地狱",
                ["CactusHell"] = "峭壁仙人掌地狱",
                ["CactusForest"] = "高原仙人掌林",
                ["DynamiteHell"] = "炸药地狱",
                ["TornadoHell"] = "龙卷风地狱",
                ["TumblerHell"] = "风滚草地狱",
            },
        };

    internal static string currentLevelName = "";
    internal static int currentLevelIndex = -1;
    private static int todayLevelIndex = -1;
    private static bool mapSeedConflictWarningLogged;
    private static bool ambiguousPeakWarningLogged;

    private int? currentMapSeed;
    private MapSeedSource mapSeedSource;
    private MapHandler? cachedSeedMapHandler;
    private Scene cachedSeedScene;
    private TMP_Text tmpText = null!;
    private TMP_Text routeText = null!;
    private TMP_Text variantText = null!;
    private RectTransform hudCanvasRect = null!;
    private MapBaker mapBaker = null!;
    private MapHandler mapHandler = null!;
    private NextLevelService nextLevelService = null!;
    private MapBiomeCapture? cachedCapture;
    private MapHandler.MapSegment[]? cachedSegments;
    private MapHandler.MapSegment[] cachedSegmentReferences = Array.Empty<MapHandler.MapSegment>();
    private Biome.BiomeType[] cachedSegmentBiomes = Array.Empty<Biome.BiomeType>();
    private GameObject?[] cachedSegmentParents = Array.Empty<GameObject?>();
    private MapHandler.MapSegment? cachedCurrentSegment;
    private MapBiomeInfo? cachedVariantOccurrence;
    private string cachedVariant = "";
    private bool variantResolved;
    private float cachedRouteWidth = -1f;
    private bool cachedIsInNadir;
    private MountainProgressHandler.ProgressPoint[]? cachedProgressPoints;
    private MountainProgressHandler.ProgressPoint[] cachedProgressPointReferences =
        Array.Empty<MountainProgressHandler.ProgressPoint>();
    private MountainProgressHandler.ProgressPoint? cachedPeakProgressPoint;
    private bool cachedPeakReached;
    private bool cachedPllMapActive;
    private string?[] cachedPllSegmentTitles = Array.Empty<string?>();    private bool cachedDisplayBiomes;
    private float nextRefreshTime;
    private bool routeNamesDirty = true;
    private readonly TrueFinalAscentCompatibility tfaCompatibility = new();
    private TfaRouteStatus cachedTfaStatus;
    private TfaRouteProjection? cachedTfaProjection;
    private int cachedTfaCurrentIndex = -1;
    private TfaRouteStage? cachedTfaVariantStage;

    private void Awake()
    {
        mapBaker = SingletonAsset<MapBaker>.Instance;
        mapHandler = Singleton<MapHandler>.Instance;
        nextLevelService = GameHandler.GetService<NextLevelService>();
        LocalizedText.OnLangugageChanged += OnLanguageChanged;
    }

    private void Start()
    {
        tmpText = GetComponent<TMP_Text>();
        ConfigureText(tmpText, autoSizeTextContainer: true);
    }

    internal void InitializeRouteText(TMP_Text text, TMP_Text currentVariantText, RectTransform canvasRect)
    {
        routeText = text;
        variantText = currentVariantText;
        hudCanvasRect = canvasRect;
        ConfigureRouteText(routeText, 20f);
        ConfigureRouteText(variantText, 16f);
        variantText.color = new Color32(255, 213, 79, byte.MaxValue);
        routeText.text = string.Empty;
        variantText.text = string.Empty;
    }

    private void Update()
    {
        if (Time.unscaledTime < nextRefreshTime)
        {
            return;
        }

        nextRefreshTime = Time.unscaledTime + RefreshInterval;
        RefreshRouteText();
        RefreshHeaderText();
    }

    private void RefreshHeaderText()
    {
        UpdateMapSeed();
        if (nextLevelService.Data.IsSome && todayLevelIndex == -1)
        {
            int levelIndexOrFallback = nextLevelService.NextLevelIndexOrFallback + NextLevelService.debugLevelIndexOffset;
            todayLevelIndex = levelIndexOrFallback % mapBaker.ScenePaths.Length;

            Plugin.Logger.LogInfo($"todayLevelIndex set to {todayLevelIndex}");
        }

        if (currentLevelIndex == -1)
        {
            int levelIndex = LevelNameToIndex(currentLevelName);
            if (levelIndex != -1)
            {
                currentLevelIndex = levelIndex;
            }
            else if (todayLevelIndex != -1)
            {
                currentLevelIndex = todayLevelIndex;
            }

            Plugin.Logger.LogInfo($"currentLevelIndex set to {currentLevelIndex}");
        }

        StringBuilder sb = new(64);
        if (PluginConfig.DisplayLevel.Value && currentLevelIndex != -1)
        {
            currentLevelName = GetLevelName(currentLevelIndex);
            sb.Append('(');
            sb.Append(currentLevelName);
            sb.Append(')');
        }

        if (PluginConfig.DisplayMapSeed.Value && currentMapSeed is int mapSeed)
        {
            sb.Append('<');
            sb.Append(mapSeed);
            sb.Append('>');
        }

        string headerText = sb.ToString();
        if (!string.Equals(tmpText.text, headerText, StringComparison.Ordinal))
        {
            tmpText.text = headerText;
        }
    }

    private void UpdateMapSeed()
    {
        if (!PluginConfig.DisplayMapSeed.Value)
        {
            return;
        }

        MapHandler? seedMapHandler = MapHandler.ExistsAndInitialized ? Singleton<MapHandler>.Instance : null;
        Scene scene = SceneManager.GetActiveScene();
        bool atTerminal = seedMapHandler != null &&
            (seedMapHandler.inNadir || GetUniquePeakProgressPoint(GetProgressPoints())?.Reached == true);
        TfaRouteStatus tfaStatus = tfaCompatibility.ReadSeedStatus(scene, seedMapHandler, atTerminal,
            out bool assemblyChanged);
        MapSeedSource source = tfaStatus switch
        {
            TfaRouteStatus.Ready => MapSeedSource.Tfa,
            TfaRouteStatus.NotApplicable => GetMapSeedSource(),
            _ => MapSeedSource.None,
        };
        if (source != mapSeedSource || assemblyChanged ||
            !ReferenceEquals(cachedSeedMapHandler, seedMapHandler) || cachedSeedScene != scene)
        {
            currentMapSeed = null;
        }
        mapSeedSource = source;
        cachedSeedMapHandler = seedMapHandler;
        cachedSeedScene = scene;

        if (tfaStatus is TfaRouteStatus.Pending or TfaRouteStatus.Unsupported)
        {
            currentMapSeed = null;
            return;
        }
        if (currentMapSeed.HasValue)
        {
            return;
        }

        int seed = default;
        bool seedAvailable = mapSeedSource switch
        {
            MapSeedSource.Tfa => tfaCompatibility.TryGetMapSeed(out seed),
            _ => false,
        };
        if (!seedAvailable)
        {
            return;
        }

        currentMapSeed = seed;
        Plugin.Logger.LogInfo($"Current map seed from {GetMapSeedSourceName(mapSeedSource)}: {seed}");
    }

    private static MapSeedSource GetMapSeedSource() { return MapSeedSource.None; }

    private static string GetMapSeedSourceName(MapSeedSource source)
    {
        return source switch
        {
            MapSeedSource.Tfa => "True Final Ascent",
            _ => "unknown source",
        };
    }

    private void RefreshRouteText()
    {
        if (!PluginConfig.DisplayBiomes.Value)
        {
            SetTextIfChanged(routeText, string.Empty);
            SetTextIfChanged(variantText, string.Empty);
            cachedDisplayBiomes = false;
            return;
        }

        float routeWidth = GetRouteWidth();
        if (!Mathf.Approximately(cachedRouteWidth, routeWidth))
        {
            SetRouteRectWidth(routeWidth);
        }

        if (mapHandler == null ||
            !MapHandler.ExistsAndInitialized ||
            mapHandler.segments == null ||
            mapHandler.segments.Length == 0)
        {
            tfaCompatibility.ResetRoute();
            ClearRouteCache(routeWidth);
            return;
        }

        MapHandler.MapSegment[] segments = mapHandler.segments;
        MapHandler.MapSegment currentSegment = MapHandler.CurrentMapSegment;
        bool isInNadir = mapHandler.inNadir;
        MountainProgressHandler.ProgressPoint[] progressPoints = GetProgressPoints();
        MountainProgressHandler.ProgressPoint? peakProgressPoint =
            GetUniquePeakProgressPoint(progressPoints);
        bool peakReached = peakProgressPoint?.Reached ?? false;
        TfaRouteStatus tfaStatus = tfaCompatibility.ReadRoute(
            SceneManager.GetActiveScene(), mapHandler, isInNadir || peakReached,
            out TfaRouteProjection? tfaProjection, out int tfaCurrentIndex);
        if (tfaStatus != cachedTfaStatus)
        {
            ClearRouteCache(routeWidth);
            cachedTfaStatus = tfaStatus;
        }
        if (tfaStatus == TfaRouteStatus.Pending)
        {
            ClearRouteCache(routeWidth);
            return;
        }
        if (tfaStatus == TfaRouteStatus.Ready)
        {
            RefreshTfaRouteText(tfaProjection!, tfaCurrentIndex, routeWidth,
                isInNadir, progressPoints, peakProgressPoint, peakReached);
            return;
        }

        bool pllMapActive = PLLCompatibility.MapActive;

        if (RouteSnapshotChanged(
                segments,
                currentSegment,
                isInNadir,
                progressPoints,
                peakProgressPoint,
                peakReached,
                pllMapActive,
                routeWidth))
        {
            CopyRouteSnapshot(
                segments,
                currentSegment,
                isInNadir,
                progressPoints,
                peakProgressPoint,
                peakReached,
                pllMapActive,
                routeWidth);
            cachedCapture = MapBiomeCollector.Capture(SceneManager.GetActiveScene());
            ResetVariantCache();
            if (!MapBiomeCollector.TryGetCurrentMapBiome(SceneManager.GetActiveScene(), cachedCapture, out _, out _))
            {
                SetTextIfChanged(routeText, string.Empty);
                SetTextIfChanged(variantText, string.Empty);
                routeNamesDirty = true;
                return;
            }

            cachedPllSegmentTitles = Array.Empty<string?>();
            if (pllMapActive)
            {
                if (!PLLCompatibility.TryCreateRouteProjection(
                        segments,
                        progressPoints,
                        out PllRouteProjection projection))
                {
                    SetTextIfChanged(routeText, string.Empty);
                    SetTextIfChanged(variantText, string.Empty);
                    routeNamesDirty = true;
                    return;
                }

                cachedPllSegmentTitles = projection.SegmentTitles;
            }

            string routeDisplay = BuildRouteDisplay(
                routeWidth,
                currentSegment,
                isInNadir,
                peakProgressPoint,
                peakReached,
                out float currentEntryRightFromRouteRight);
            SetTextIfChanged(routeText, routeDisplay);
            UpdateVariantLayout(currentEntryRightFromRouteRight);
        }

        RefreshVariantText();
    }

    private void RefreshTfaRouteText(
        TfaRouteProjection projection,
        int currentIndex,
        float width,
        bool isInNadir,
        MountainProgressHandler.ProgressPoint[] progressPoints,
        MountainProgressHandler.ProgressPoint? peakPoint,
        bool peakReached)
    {
        if (!cachedDisplayBiomes || routeNamesDirty ||
            !ReferenceEquals(cachedTfaProjection, projection) || cachedTfaCurrentIndex != currentIndex ||
            !Mathf.Approximately(cachedRouteWidth, width) || cachedIsInNadir != isInNadir ||
            !ReferenceEquals(cachedProgressPoints, progressPoints) ||
            !ReferenceEquals(cachedPeakProgressPoint, peakPoint) || cachedPeakReached != peakReached)
        {
            cachedTfaProjection = projection;
            cachedTfaCurrentIndex = currentIndex;
            cachedRouteWidth = width;
            cachedIsInNadir = isInNadir;
            cachedProgressPoints = progressPoints;
            cachedPeakProgressPoint = peakPoint;
            cachedPeakReached = peakReached;
            cachedDisplayBiomes = true;
            routeNamesDirty = false;
            ResetVariantCache();

            List<RouteEntry> entries = new();
            int peakIndex = -1;
            foreach (TfaRouteStage stage in projection.Stages)
            {
                if (stage.BiomeType == Biome.BiomeType.Peak)
                {
                    peakIndex = entries.Count;
                }
                string name = string.IsNullOrWhiteSpace(stage.TitleKey)
                    ? GetBiomeName(stage.BiomeType, 0)
                    : LocalizedText.GetText(stage.TitleKey);
                entries.Add(new RouteEntry(name));
            }
            if (peakIndex < 0 && peakPoint != null)
            {
                peakIndex = entries.Count;
                string name = peakPoint.localizedTitle;
                entries.Add(new RouteEntry(string.IsNullOrWhiteSpace(name) ? LocalizedText.GetText("PEAK") : name));
            }
            int displayIndex = currentIndex;
            if (isInNadir)
            {
                entries.Add(new RouteEntry(LocalizedText.GetText("AREA_VOID")));
                displayIndex = entries.Count - 1;
            }
            else if (peakReached && peakIndex >= 0)
            {
                displayIndex = peakIndex;
            }

            string display = BuildRouteDisplay(entries, displayIndex, width, out float currentEntryRightFromRouteRight);
            SetTextIfChanged(routeText, display);
            UpdateVariantLayout(currentEntryRightFromRouteRight);
        }

        TfaRouteStage? currentStage = !isInNadir && !peakReached &&
            currentIndex >= 0 && currentIndex < projection.Stages.Count
                ? projection.Stages[currentIndex]
                : null;
        RefreshTfaVariantText(currentStage);
    }

    private void RefreshTfaVariantText(TfaRouteStage? stage)
    {
        if (!ReferenceEquals(cachedTfaVariantStage, stage))
        {
            ResetVariantCache();
            cachedTfaVariantStage = stage;
        }
        if (stage == null || !MapBiomeCollector.HasVariant(stage.BiomeType) ||
            !stage.Terrain || !stage.Terrain.gameObject.activeInHierarchy)
        {
            variantResolved = false;
            SetTextIfChanged(variantText, string.Empty);
            return;
        }
        if (!variantResolved)
        {
            variantResolved = MapBiomeCollector.TryGetCurrentVariant(
                stage.BiomeType, stage.Terrain, out cachedVariant);
        }
        string display = variantResolved && !string.IsNullOrWhiteSpace(cachedVariant)
            ? $"({LocalizeVariant(stage.BiomeType, cachedVariant)})"
            : "";
        SetTextIfChanged(variantText, display);
    }

    private void RefreshVariantText()
    {
        if (cachedCapture == null ||
            !MapBiomeCollector.TryGetCurrentMapBiome(
                SceneManager.GetActiveScene(),
                cachedCapture,
                out MapBiomeInfo currentBiome,
                out int segmentIndex))
        {
            SetTextIfChanged(variantText, string.Empty);
            return;
        }

        if (!MapBiomeCollector.HasVariant(currentBiome.BiomeType))
        {
            SetTextIfChanged(variantText, string.Empty);
            return;
        }

        if (!ReferenceEquals(cachedVariantOccurrence, currentBiome))
        {
            cachedVariantOccurrence = currentBiome;
            cachedVariant = "";
            variantResolved = false;
        }

        if (!variantResolved)
        {
            Transform? segmentRoot = segmentIndex >= 0 && segmentIndex < cachedCapture.SegmentRoots.Length
                ? cachedCapture.SegmentRoots[segmentIndex]
                : null;
            variantResolved = MapBiomeCollector.TryGetCurrentVariant(
                currentBiome,
                segmentRoot,
                out cachedVariant);
        }
        string variantName = LocalizeVariant(currentBiome.BiomeType, cachedVariant);
        string display = variantResolved && !string.IsNullOrWhiteSpace(cachedVariant)
            ? $"({variantName})"
            : "";
        SetTextIfChanged(variantText, display);
    }

    private void ClearRouteCache(float routeWidth)
    {
        SetTextIfChanged(routeText, string.Empty);
        SetTextIfChanged(variantText, string.Empty);
        cachedCapture = null;
        cachedSegments = null;
        cachedSegmentReferences = Array.Empty<MapHandler.MapSegment>();
        cachedSegmentBiomes = Array.Empty<Biome.BiomeType>();
        cachedSegmentParents = Array.Empty<GameObject?>();
        cachedCurrentSegment = null;
        cachedProgressPoints = null;
        cachedProgressPointReferences = Array.Empty<MountainProgressHandler.ProgressPoint>();
        cachedPeakProgressPoint = null;
        cachedPeakReached = false;
        cachedPllMapActive = false;
        cachedPllSegmentTitles = Array.Empty<string?>();
        cachedTfaProjection = null;
        cachedTfaCurrentIndex = -1;
        cachedRouteWidth = routeWidth;
        cachedDisplayBiomes = true;
        routeNamesDirty = false;
        ResetVariantCache();
    }

    private bool RouteSnapshotChanged(
        MapHandler.MapSegment[] segments,
        MapHandler.MapSegment currentSegment,
        bool isInNadir,
        MountainProgressHandler.ProgressPoint[] progressPoints,
        MountainProgressHandler.ProgressPoint? peakPoint,
        bool peakReached,
        bool pllMapActive,
        float width)
    {
        if (!cachedDisplayBiomes ||
            routeNamesDirty ||
            !ReferenceEquals(cachedSegments, segments) ||
            !Mathf.Approximately(cachedRouteWidth, width) ||
            !ReferenceEquals(cachedCurrentSegment, currentSegment) ||
            cachedIsInNadir != isInNadir ||
            !ReferenceEquals(cachedProgressPoints, progressPoints) ||
            !ReferenceEquals(cachedPeakProgressPoint, peakPoint) ||
            cachedPeakReached != peakReached ||
            cachedPllMapActive != pllMapActive ||
            cachedSegmentReferences.Length != segments.Length ||
            cachedProgressPointReferences.Length != progressPoints.Length)
        {
            return true;
        }

        for (int i = 0; i < segments.Length; i++)
        {
            MapHandler.MapSegment segment = segments[i];
            if (!ReferenceEquals(cachedSegmentReferences[i], segment) ||
                cachedSegmentBiomes[i] != segment.biome ||
                !ReferenceEquals(cachedSegmentParents[i], segment.segmentParent))
            {
                return true;
            }
        }

        for (int i = 0; i < progressPoints.Length; i++)
        {
            if (!ReferenceEquals(cachedProgressPointReferences[i], progressPoints[i]))
            {
                return true;
            }
        }

        return false;
    }

    private void CopyRouteSnapshot(
        MapHandler.MapSegment[] segments,
        MapHandler.MapSegment currentSegment,
        bool isInNadir,
        MountainProgressHandler.ProgressPoint[] progressPoints,
        MountainProgressHandler.ProgressPoint? peakPoint,
        bool peakReached,
        bool pllMapActive,
        float width)
    {
        cachedSegmentReferences = new MapHandler.MapSegment[segments.Length];
        cachedSegments = segments;
        cachedSegmentBiomes = new Biome.BiomeType[segments.Length];
        cachedSegmentParents = new GameObject?[segments.Length];
        for (int i = 0; i < segments.Length; i++)
        {
            cachedSegmentReferences[i] = segments[i];
            cachedSegmentBiomes[i] = segments[i].biome;
            cachedSegmentParents[i] = segments[i].segmentParent;
        }
        cachedProgressPointReferences = new MountainProgressHandler.ProgressPoint[progressPoints.Length];
        Array.Copy(progressPoints, cachedProgressPointReferences, progressPoints.Length);
        cachedCurrentSegment = currentSegment;
        cachedIsInNadir = isInNadir;
        cachedProgressPoints = progressPoints;
        cachedPeakProgressPoint = peakPoint;
        cachedPeakReached = peakReached;
        cachedPllMapActive = pllMapActive;
        cachedRouteWidth = width;
        cachedDisplayBiomes = true;
        routeNamesDirty = false;
    }

    private string BuildRouteDisplay(
        float width,
        MapHandler.MapSegment currentSegment,
        bool isInNadir,
        MountainProgressHandler.ProgressPoint? peakPoint,
        bool peakReached,
        out float currentEntryRightFromRouteRight)
    {
        currentEntryRightFromRouteRight = 0f;
        if (cachedCapture == null)
        {
            return "";
        }

        List<RouteEntry> entries = new();
        int currentIndex = -1;
        bool hasPeak = false;
        int peakIndex = -1;
        int currentSegmentIndex = Array.IndexOf(cachedSegmentReferences, currentSegment);
        int currentOccurrenceIndex = currentSegmentIndex >= 0 &&
            currentSegmentIndex < cachedCapture.SegmentBiomeIndices.Length
                ? cachedCapture.SegmentBiomeIndices[currentSegmentIndex]
                : -1;
        for (int occurrenceIndex = 0; occurrenceIndex < cachedCapture.MapBiomes.Count; occurrenceIndex++)
        {
            MapBiomeInfo biome = cachedCapture.MapBiomes[occurrenceIndex];
            if (biome.BiomeType == Biome.BiomeType.Void)
            {
                continue;
            }

            if (biome.BiomeType == Biome.BiomeType.Peak)
            {
                hasPeak = true;
                peakIndex = entries.Count;
            }
            if (biome.BiomeType is Biome.BiomeType.Volcano or Biome.BiomeType.Swamp)
            {
                bool isCurrentOccurrence = occurrenceIndex == currentOccurrenceIndex;
                int selectedPart = isCurrentOccurrence ? GetOccurrenceSegmentOrdinal(biome, currentSegmentIndex) % 2 : 0;
                string first = GetOccurrenceName(biome, 0);
                string second = GetOccurrenceName(biome, 1);
                entries.Add(new RouteEntry(first));
                entries.Add(new RouteEntry(second));
                if (isCurrentOccurrence)
                {
                    currentIndex = entries.Count - (selectedPart == 1 ? 1 : 2);
                }

                continue;
            }

            entries.Add(new RouteEntry(GetOccurrenceName(biome, 0)));
            if (occurrenceIndex == currentOccurrenceIndex)
            {
                currentIndex = entries.Count - 1;
            }
        }

        if (!hasPeak && peakPoint != null)
        {
            peakIndex = entries.Count;
            string peakName = peakPoint.localizedTitle;
            entries.Add(new RouteEntry(
                string.IsNullOrWhiteSpace(peakName)
                    ? LocalizedText.GetText("PEAK")
                    : peakName));
        }
        if (isInNadir)
        {
            entries.Add(new RouteEntry(LocalizedText.GetText("AREA_VOID")));
            currentIndex = entries.Count - 1;
        }
        else if (peakReached && peakIndex >= 0)
        {
            currentIndex = peakIndex;
        }
        if (currentIndex < 0 || currentIndex >= entries.Count)
        {
            return "";
        }

        return BuildRouteDisplay(entries, currentIndex, width, out currentEntryRightFromRouteRight);
    }

    private string GetOccurrenceName(MapBiomeInfo biome, int ordinal)
    {
        if (ordinal >= 0 && ordinal < biome.SegmentIndices.Count)
        {
            int segmentIndex = biome.SegmentIndices[ordinal];
            if (segmentIndex >= 0 &&
                segmentIndex < cachedPllSegmentTitles.Length &&
                !string.IsNullOrWhiteSpace(cachedPllSegmentTitles[segmentIndex]))
            {
                return cachedPllSegmentTitles[segmentIndex]!;
            }
        }

        return GetBiomeName(biome.BiomeType, ordinal);
    }

    private static int GetOccurrenceSegmentOrdinal(MapBiomeInfo biome, int segmentIndex)
    {
        int ordinal = biome.SegmentIndices.IndexOf(segmentIndex);
        return ordinal < 0 ? 0 : ordinal;
    }

    private string BuildRouteDisplay(
        List<RouteEntry> entries,
        int currentIndex,
        float width,
        out float currentEntryRightFromRouteRight)
    {
        int start = currentIndex;
        int end = currentIndex;
        while (end - start + 1 < MaxVisibleRouteEntries)
        {
            bool added = false;
            if (end + 1 < entries.Count &&
                FitsRoute(entries, currentIndex, start, end + 1, width))
            {
                end++;
                added = true;
            }

            if (end - start + 1 < MaxVisibleRouteEntries &&
                start > 0 &&
                FitsRoute(entries, currentIndex, start - 1, end, width))
            {
                start--;
                added = true;
            }

            if (!added)
            {
                break;
            }
        }

        string result = FormatRouteRange(entries, currentIndex, start, end);
        if (MeasureText(result) > width)
        {
            result = FormatCurrentEntryToWidth(entries[currentIndex], width);
        }

        currentEntryRightFromRouteRight = GetCurrentEntryRightFromRouteRight(result);
        return result;
    }

    private bool FitsRoute(List<RouteEntry> entries, int currentIndex, int start, int end, float width)
    {
        return MeasureText(FormatRouteRange(entries, currentIndex, start, end)) <= width;
    }

    private string FormatRouteRange(List<RouteEntry> entries, int currentIndex, int start, int end)
    {
        StringBuilder sb = new(128);
        if (start > 0)
        {
            sb.Append("…(+").Append(start).Append(") · ");
        }

        for (int i = start; i <= end; i++)
        {
            if (i > start)
            {
                sb.Append(" · ");
            }

            sb.Append(FormatEntry(entries[i], i == currentIndex));
        }

        int hiddenAfter = entries.Count - end - 1;
        if (hiddenAfter > 0)
        {
            sb.Append(" · …(+").Append(hiddenAfter).Append(')');
        }

        return sb.ToString();
    }

    private string FormatCurrentEntryToWidth(RouteEntry entry, float width)
    {
        string full = FormatEntry(entry, true);
        if (MeasureText(full) <= width)
        {
            return full;
        }

        for (int length = entry.Name.Length - 1; length >= 0; length--)
        {
            string candidate = FormatEntry(entry, true, entry.Name[..length] + "…");
            if (MeasureText(candidate) <= width)
            {
                return candidate;
            }
        }

        return FormatEntry(entry, true, "…");
    }

    private static string FormatEntry(RouteEntry entry, bool current, string? nameOverride = null)
    {
        string name = nameOverride ?? entry.Name;
        return current
            ? $"<color={CurrentSegmentColorTag}>[{name}]</color>"
            : name;
    }

    private float GetCurrentEntryRightFromRouteRight(string routeDisplay)
    {
        string currentTag = $"<color={CurrentSegmentColorTag}>";
        int currentStart = routeDisplay.IndexOf(currentTag, StringComparison.Ordinal);
        if (currentStart < 0)
        {
            return 0f;
        }

        const string closingTag = "</color>";
        int closingTagStart = routeDisplay.IndexOf(closingTag, currentStart, StringComparison.Ordinal);
        if (closingTagStart < 0)
        {
            return 0f;
        }

        int currentEnd = closingTagStart + closingTag.Length;
        return currentEnd < routeDisplay.Length
            ? MeasureText(routeDisplay[currentEnd..])
            : 0f;
    }

    private float MeasureText(string text)
    {
        return routeText.GetPreferredValues(text).x;
    }

    private float GetRouteWidth()
    {
        return Mathf.Max(0f, hudCanvasRect.rect.width - MapTextRightMargin);
    }

    private void SetRouteRectWidth(float width)
    {
        RectTransform routeRect = routeText.rectTransform;
        Vector2 anchoredPosition = routeRect.anchoredPosition;
        anchoredPosition.x = -MapTextRightMargin;
        routeRect.anchoredPosition = anchoredPosition;
        routeRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        variantText.rectTransform.SetSizeWithCurrentAnchors(
            RectTransform.Axis.Horizontal,
            hudCanvasRect.rect.width);
    }

    private void UpdateVariantLayout(float currentEntryRightFromRouteRight)
    {
        RectTransform variantRect = variantText.rectTransform;
        Vector2 anchoredPosition = variantRect.anchoredPosition;
        anchoredPosition.x = -(MapTextRightMargin + currentEntryRightFromRouteRight);
        variantRect.anchoredPosition = anchoredPosition;
    }

    private void ResetVariantCache()
    {
        cachedVariantOccurrence = null;
        cachedTfaVariantStage = null;
        cachedVariant = "";
        variantResolved = false;
    }

    private static string LocalizeVariant(Biome.BiomeType biomeType, string variant)
    {
        if (LocalizedText.CURRENT_LANGUAGE == LocalizedText.Language.SimplifiedChinese &&
            SimplifiedChineseVariantNames.TryGetValue(
                biomeType,
                out IReadOnlyDictionary<string, string>? names) &&
            names.TryGetValue(variant, out string? localized))
        {
            return localized;
        }

        return variant;
    }

    private static MountainProgressHandler.ProgressPoint[] GetProgressPoints()
    {
        MountainProgressHandler? mountainProgressHandler = Singleton<MountainProgressHandler>.Instance;
        return mountainProgressHandler?.progressPoints ?? Array.Empty<MountainProgressHandler.ProgressPoint>();
    }

    private static MountainProgressHandler.ProgressPoint? GetUniquePeakProgressPoint(
        MountainProgressHandler.ProgressPoint[] progressPoints)
    {
        MountainProgressHandler.ProgressPoint? peakPoint = null;
        foreach (MountainProgressHandler.ProgressPoint point in progressPoints)
        {
            if (point == null || point.biome != Biome.BiomeType.Peak)
            {
                continue;
            }

            if (peakPoint != null)
            {
                if (!ambiguousPeakWarningLogged)
                {
                    ambiguousPeakWarningLogged = true;
                    Plugin.Logger.LogWarning(
                        "Multiple Peak progress points were found; the biome route will not append an ambiguous Peak entry.");
                }
                return null;
            }

            peakPoint = point;
        }

        return peakPoint;
    }

    private static string GetBiomeName(Biome.BiomeType biome, int part)
    {
        return biome switch
        {
            Biome.BiomeType.Volcano => LocalizedText.GetText(part == 0 ? "CALDERA" : "THE KILN"),
            Biome.BiomeType.Swamp => LocalizedText.GetText(part == 0 ? "GLOOM" : "THE CITADEL"),
            Biome.BiomeType.Void => LocalizedText.GetText("AREA_VOID"),
            _ => LocalizedText.GetText(biome.ToString()),
        };
    }

    private static void ConfigureText(TMP_Text text, bool autoSizeTextContainer)
    {
        text.autoSizeTextContainer = autoSizeTextContainer;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.alignment = TextAlignmentOptions.TopRight;
        text.lineSpacing = -20f;
        text.fontSize = 20f;
        text.outlineColor = new Color32(0, 0, 0, byte.MaxValue);
        text.outlineWidth = 0.055f;
    }

    private static void ConfigureRouteText(TMP_Text text, float fontSize)
    {
        ConfigureText(text, autoSizeTextContainer: false);
        text.richText = true;
        text.fontSize = fontSize;
        text.overflowMode = TextOverflowModes.Overflow;
        text.alignment = TextAlignmentOptions.TopRight;
    }

    private static void SetTextIfChanged(TMP_Text text, string value)
    {
        if (!string.Equals(text.text, value, StringComparison.Ordinal))
        {
            text.text = value;
        }
    }

    private void OnLanguageChanged()
    {
        routeNamesDirty = true;
    }

    private void OnDestroy()
    {
        LocalizedText.OnLangugageChanged -= OnLanguageChanged;
    }

    private string GetLevelName(int levelIndex)
    {
        levelIndex %= mapBaker.ScenePaths.Length;
        string result = System.IO.Path.GetFileNameWithoutExtension(mapBaker.ScenePaths[levelIndex]);
        return result;
    }

    private int LevelNameToIndex(string levelName)
    {
        for (int i = 0; i < mapBaker.ScenePaths.Length; i++)
        {
            string currentLevelName = GetLevelName(i);
            if (string.Equals(currentLevelName, levelName, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private sealed class RouteEntry
    {
        internal RouteEntry(string name)
        {
            Name = name;
        }

        internal string Name { get; }
    }

    private enum MapSeedSource { None, TerrainRandomiser, TerrainCustomiser, Pll, Tfa, Conflict }
}
