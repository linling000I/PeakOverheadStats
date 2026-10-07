using BepInEx;
using BepInEx.Logging;
using PeakOverheadStats.MonoBehaviours;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System.Text;
using System;

namespace PeakOverheadStats;

/// <summary>
/// PeakOverheadStats - Full PeakStatsEx HUD with overhead teammate display.
/// </summary>
[BepInPlugin("com.yls.peakoverheadstats", "PeakOverheadStats", "1.0.0")]
[BepInDependency("com.snosz.terrainrandomiser", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("com.snosz.terraincustomiser", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("af.pll.library", BepInDependency.DependencyFlags.SoftDependency)]
public partial class Plugin : BaseUnityPlugin
{
    internal static new ManualLogSource Logger { get; private set; } = null!;

    private void Awake()
    {
        Logger = base.Logger;
        Logger.LogInfo($"Plugin {Info.Metadata.Name} v{Info.Metadata.Version} loaded!");

        PluginConfig.Initialize(Config, Logger);

        var harmony = new Harmony("com.yls.peakoverheadstats");
        harmony.PatchAll();
    }
}

[HarmonyWrapSafe]
internal sealed class Patches
{
    [HarmonyPatch(typeof(GUIManager), "Start")]
    [HarmonyPostfix]
    private static void GUIManagerStartHook(GUIManager __instance)
    {
        // Overhead teammate bars work in all scenes (including Airport)
                if (SceneManager.GetActiveScene().name != "Airport")
        {
            CreateTimerAndHeightStats();
            CreateMapStats();
        }
        // Bottom-left ProximityStaminaManager replaced by overhead display
        // ProximityStaminaManager.InitializeBarGroup(__instance);

        void CreateTimerAndHeightStats()
        {
            Transform obj = __instance.GetComponentInChildren<AscentUI>().transform;
            RectTransform obj2 = (RectTransform)UnityEngine.Object.Instantiate(obj, obj.parent);
            obj2.name = "Timer & Height UI";
            UnityEngine.Object.Destroy(obj2.GetComponent<AscentUI>());
            obj2.gameObject.AddComponent<TimerHeightStats>();
            obj2.pivot = obj2.anchorMax = obj2.anchorMin = new Vector2(0.5f, 1f);
            obj2.anchoredPosition = Vector2.zero;
        }
        void CreateMapStats()
        {
            RectTransform sourceRect = (RectTransform)__instance.GetComponentInChildren<AscentUI>().transform;
            RectTransform mapStatsRect = UnityEngine.Object.Instantiate(sourceRect, sourceRect.parent);
            mapStatsRect.name = "Map & Level UI";
            UnityEngine.Object.Destroy(mapStatsRect.GetComponent<AscentUI>());
            MapStats mapStats = mapStatsRect.gameObject.AddComponent<MapStats>();
            mapStatsRect.pivot = mapStatsRect.anchorMax = mapStatsRect.anchorMin = new Vector2(1f, 1f);
            mapStatsRect.anchoredPosition = new Vector2(-MapStats.MapTextRightMargin, 5f);

            RectTransform routeRect = UnityEngine.Object.Instantiate(sourceRect, sourceRect.parent);
            routeRect.name = "Biome Route UI";
            UnityEngine.Object.Destroy(routeRect.GetComponent<AscentUI>());
            routeRect.pivot = routeRect.anchorMax = routeRect.anchorMin = new Vector2(1f, 1f);
            routeRect.anchoredPosition = new Vector2(0f, sourceRect.anchoredPosition.y - sourceRect.rect.height + 8f);

            TMP_Text routeText = routeRect.GetComponent<TMP_Text>();
            RectTransform variantRect = UnityEngine.Object.Instantiate(sourceRect, sourceRect.parent);
            variantRect.name = "Biome Variant UI";
            UnityEngine.Object.Destroy(variantRect.GetComponent<AscentUI>());
            variantRect.pivot = variantRect.anchorMax = variantRect.anchorMin = new Vector2(1f, 1f);
            variantRect.anchoredPosition = new Vector2(0f, routeRect.anchoredPosition.y - sourceRect.rect.height + 22f);

            TMP_Text variantText = variantRect.GetComponent<TMP_Text>();
            RectTransform hudCanvasRect = __instance.hudCanvas.GetComponent<RectTransform>();
            mapStats.InitializeRouteText(routeText, variantText, hudCanvasRect);
        }
    }

    // Overhead display replaces ProximityStaminaManager
    // [HarmonyPatch(typeof(PlayerHandler), "RegisterCharacter")]
    // [HarmonyPostfix]
    // private static void PlayerHandlerRegisterCharacterHook(Character character) { ... }
    [HarmonyPatch(typeof(UIPlayerNames), "UpdateName")]
    [HarmonyPostfix]
    private static void UIPlayerNamesUpdateNameHook(UIPlayerNames __instance, int index, bool visible)
    {
        if (__instance.playerNameText == null || index < 0 || index >= __instance.playerNameText.Length)
            return;

        PlayerName playerName = __instance.playerNameText[index];
        if (playerName == null) return;

        OverheadNameplate nameplate = playerName.GetComponent<OverheadNameplate>();
        if (nameplate == null)
            nameplate = playerName.gameObject.AddComponent<OverheadNameplate>();

        nameplate.Refresh(playerName, visible);
    }

    [HarmonyPatch(typeof(UIPlayerNames), "DisableName")]
    [HarmonyPrefix]
    private static void UIPlayerNamesDisableNameHook(UIPlayerNames __instance, int index)
    {
        if (__instance.playerNameText == null || index < 0 || index >= __instance.playerNameText.Length)
            return;

        PlayerName playerName = __instance.playerNameText[index];
        if (playerName != null && playerName.TryGetComponent(out OverheadNameplate nameplate))
            nameplate.Hide();
    }



    [HarmonyPatch(typeof(LoadingScreenHandler), "LoadSceneProcess")]
    [HarmonyPostfix]
    private static void LoadSceneProcessHook(string sceneName)
    {
        if (!sceneName.Contains("Level_"))
        {
            return;
        }
        MapStats.currentLevelName = sceneName;
        MapStats.currentLevelIndex = -1;
        Plugin.Logger.LogDebug($"After: LoadingScreenHandler.LoadSceneProcess called. sceneName: {sceneName}");
    }
}


internal static class StringExtensions
{
    public static string TrimToByteLength(this string input, int byteLength)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        int currentBytes = Encoding.UTF8.GetByteCount(input);
        if (currentBytes <= byteLength)
            return input;

        if (currentBytes == input.Length)
            return input[..byteLength];

        byte[] bytesArray = Encoding.UTF8.GetBytes(input);
        Array.Resize(ref bytesArray, byteLength);
        string result = Encoding.UTF8.GetString(bytesArray, 0, byteLength);
        return result.TrimEnd('�');
    }
}