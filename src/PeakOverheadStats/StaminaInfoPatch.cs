using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using PeakOverheadStats.MonoBehaviours;
using TMPro;
using DG.Tweening;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PeakOverheadStats;

[HarmonyWrapSafe]
internal sealed class StaminaInfoPatch
{
    internal static GUIManager guiManager = null!;
    internal static Dictionary<string, TextMeshProUGUI> barTexts = [];
    internal static Dictionary<string, float> lastKnownData = [];
    internal static Dictionary<string, float> lastKnownAffData = [];
    internal static Dictionary<string, float> lastAfflictionUpdateTime = [];
    internal static Dictionary<int, float> LastUpdateTime { get; set; } = [];

    private const float LargeBarThreshold = 30f;
    private const float SmallBarThreshold = 15f;
    private const float BarUnitSize = 6f;
    private const float AfflictionRefreshInterval = 0.5f;
    private const float HungerIncrementThreshold = 0.025f;
    private const float CharacterExtraStaminaTextOffsetX = 6f;
    private const float CharacterExtraStaminaTextWidth = 80f;
    private const string HiddenHungerCountdownKey = "HiddenHungerCountdown";
    private const string YellowTextStart = "<color=#FFFF00>";
    private const string ColorTextEnd = "</color>";

    private static readonly Dictionary<int, float> hungerIncrementalStatuses = [];
    private static readonly Dictionary<int, float> lastTrackedHungerAddTimes = [];
    private static readonly Dictionary<int, float> lastObservedRealHungerIncrementalStatuses = [];
    private static readonly Dictionary<int, float> lastRealHungerProgressTimes = [];
    private static readonly Dictionary<int, bool> wasHungerBlockedByVanilla = [];
    private static readonly FieldInfo? currentIncrementalStatusesField =
        AccessTools.Field(typeof(CharacterAfflictions), "currentIncrementalStatuses");
    private static bool wasHiddenHungerCountdownShown;

    [HarmonyPatch(typeof(StaminaBar), "Update")]
    [HarmonyPostfix]
    private static void StaminaInfoStaminaBarUpdate(StaminaBar __instance)
    {
        try
        {
            if (guiManager == null)
            {
                barTexts = [];
                lastKnownData = [];
                lastKnownAffData = [];
                lastAfflictionUpdateTime = [];
                InitStaminaInfo(__instance);
            }

            if (!PluginConfig.ShowStaminaInfo.Value)
            {
                HideBarTexts(__instance);
                return;
            }

            ConstrainStaminaTextToFill(__instance.staminaBar.name, __instance.staminaBar);
            if (Character.observedCharacter != null)
            {
                UpdateBarTexts(__instance);
            }
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError(e.Message + e.StackTrace);
        }
    }

    [HarmonyPatch(typeof(CharacterStaminaBar), "Update")]
    [HarmonyPostfix]
    public static void Update(CharacterStaminaBar __instance)
    {
        try
        {
            var id = __instance.GetInstanceID();
            if (!LastUpdateTime.ContainsKey(id))
            {
                LastUpdateTime.Add(id, -1f);
            }

            if (!barTexts.ContainsKey(GetCharacterStaminaKey(id)))
            {
                InitCharacterStaminaInfo(__instance);
            }

            if (!PluginConfig.ShowStaminaInfo.Value)
            {
                HideCharacterBarTexts(__instance);
                return;
            }

            ConstrainStaminaTextToFill(
                GetCharacterStaminaKey(id),
                __instance.staminaBarRectTransform);
            if (Character.observedCharacter != null && Time.time - LastUpdateTime[id] > 1f)
            {
                UpdateCharacterBarTexts(__instance);
                LastUpdateTime[id] = Time.time;
            }
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError(e.Message + e.StackTrace);
        }
    }

    [HarmonyPatch(typeof(CharacterAfflictions), "AddStatus")]
    [HarmonyPostfix]
    private static void CharacterAfflictionsAddStatusPostfix(
        CharacterAfflictions __instance,
        CharacterAfflictions.STATUSTYPE statusType,
        float amount,
        bool __result)
    {
        if (statusType != CharacterAfflictions.STATUSTYPE.Hunger || amount <= 0f)
        {
            return;
        }

        if (__result)
        {
            if (TryReadCurrentIncrementalHunger(__instance, out float currentIncrementalStatus))
            {
                SetFallbackHungerIncrementalStatus(__instance, currentIncrementalStatus);
            }
            else
            {
                AddFallbackHungerIncrementalStatus(__instance, amount);
            }
            return;
        }

        if (!ShouldTrackHungerAdd(__instance))
        {
            return;
        }

        AddFallbackHungerIncrementalStatus(__instance, amount);
    }

    private static bool ShouldTrackHungerAdd(CharacterAfflictions afflictions)
    {
        Character? character = Character.observedCharacter;
        if (character?.refs?.afflictions != afflictions)
        {
            return false;
        }

        return character.data.fullyConscious &&
            afflictions.canGetHungry &&
            afflictions.hungerPerSecond * Ascents.hungerRateMultiplier > 0f;
    }

    private static void AddFallbackHungerIncrementalStatus(CharacterAfflictions afflictions, float amount)
    {
        int id = afflictions.GetInstanceID();
        hungerIncrementalStatuses.TryGetValue(id, out float currentIncrementalStatus);
        currentIncrementalStatus += amount;
        SetFallbackHungerIncrementalStatus(
            afflictions,
            currentIncrementalStatus >= HungerIncrementThreshold ? 0f : currentIncrementalStatus);
    }

    private static void SetFallbackHungerIncrementalStatus(CharacterAfflictions afflictions, float currentIncrementalStatus)
    {
        int id = afflictions.GetInstanceID();
        hungerIncrementalStatuses[id] = currentIncrementalStatus;
        lastTrackedHungerAddTimes[id] = Time.time;
    }

    private static void ClearFallbackHungerIncrementalStatus(CharacterAfflictions afflictions)
    {
        hungerIncrementalStatuses[afflictions.GetInstanceID()] = 0f;
    }

    private static float GetFallbackHungerIncrementalStatus(CharacterAfflictions afflictions)
    {
        int id = afflictions.GetInstanceID();
        hungerIncrementalStatuses.TryGetValue(id, out float currentIncrementalStatus);
        return currentIncrementalStatus;
    }

    [HarmonyPatch(typeof(CharacterAfflictions), "SubtractStatus")]
    [HarmonyPrefix]
    private static void CharacterAfflictionsSubtractStatusPrefix(
        CharacterAfflictions __instance,
        CharacterAfflictions.STATUSTYPE statusType,
        ref float __state)
    {
        __state = statusType == CharacterAfflictions.STATUSTYPE.Hunger
            ? __instance.GetCurrentStatus(CharacterAfflictions.STATUSTYPE.Hunger)
            : -1f;
    }

    [HarmonyPatch(typeof(CharacterAfflictions), "SubtractStatus")]
    [HarmonyPostfix]
    private static void CharacterAfflictionsSubtractStatusPostfix(
        CharacterAfflictions __instance,
        CharacterAfflictions.STATUSTYPE statusType,
        float __state)
    {
        if (statusType != CharacterAfflictions.STATUSTYPE.Hunger || __state <= 0f)
        {
            return;
        }

        float currentStatus = __instance.GetCurrentStatus(CharacterAfflictions.STATUSTYPE.Hunger);
        if (currentStatus < __state)
        {
            hungerIncrementalStatuses[__instance.GetInstanceID()] = 0f;
        }
    }

    [HarmonyPatch(typeof(CharacterAfflictions), "SetStatus")]
    [HarmonyPostfix]
    private static void CharacterAfflictionsSetStatusPostfix(
        CharacterAfflictions __instance,
        CharacterAfflictions.STATUSTYPE statusType)
    {
        if (statusType == CharacterAfflictions.STATUSTYPE.Hunger)
        {
            hungerIncrementalStatuses[__instance.GetInstanceID()] = 0f;
        }
    }

    private static void UpdateBarTexts(StaminaBar staminaBar)
    {
        string staminaSuffix = GetStaminaHungerCountdownSuffix(staminaBar, out bool forceStaminaUpdate);
        bool canShowStaminaSuffix = GameRef.GetDesiredStaminaSize(staminaBar) >= SmallBarThreshold;
        UpdateMaxStaminaHungerCountdown(staminaBar, canShowStaminaSuffix ? string.Empty : staminaSuffix, forceStaminaUpdate);

        UpdateBarText(
            staminaBar.staminaBar.name,
            GameRef.GetDesiredStaminaSize(staminaBar),
            PluginConfig.StaminaInfoRoundStaminaBars.Value,
            trimWholeDecimal: false,
            cache: lastKnownData,
            suffix: canShowStaminaSuffix ? staminaSuffix : string.Empty,
            forceUpdate: forceStaminaUpdate
        );

        if (forceStaminaUpdate)
        {
            lastAfflictionUpdateTime[HiddenHungerCountdownKey] = Time.time;
        }

        UpdateBarText(
            "ExtraStamina",
            GameRef.GetDesiredExtraStaminaSize(staminaBar),
            PluginConfig.StaminaInfoRoundStaminaBars.Value,
            trimWholeDecimal: false,
            cache: lastKnownData
        );

        foreach (BarAffliction affliction in staminaBar.afflictions)
        {
            string key = affliction.name;
            bool canShowCountdown = CanShowCountdown(affliction.afflictionType);
            bool shouldRefreshCountdown =
                canShowCountdown &&
                affliction.size > 0f &&
                (!lastAfflictionUpdateTime.ContainsKey(key) || Time.time - lastAfflictionUpdateTime[key] > AfflictionRefreshInterval);

            string suffix = string.Empty;
            if (canShowCountdown && IsCountdownEnabled(affliction.afflictionType))
            {
                float timeRemaining = GetReductionTimeRemaining(affliction.afflictionType);
                if (timeRemaining > 0f && !ShouldMoveHungerCountdownToStamina(affliction, timeRemaining))
                {
                    suffix = "(" + FormatTime(timeRemaining) + ")";
                }
            }

            UpdateBarText(
                key,
                affliction.size,
                PluginConfig.StaminaInfoRoundAfflictionBars.Value,
                trimWholeDecimal: true,
                cache: lastKnownAffData,
                suffix: suffix,
                forceUpdate: shouldRefreshCountdown
            );

            if (shouldRefreshCountdown)
            {
                lastAfflictionUpdateTime[key] = Time.time;
            }
        }
    }

    private static void UpdateMaxStaminaHungerCountdown(StaminaBar staminaBar, string suffix, bool forceUpdate)
    {
        if (!barTexts.TryGetValue(HiddenHungerCountdownKey, out TextMeshProUGUI text))
        {
            return;
        }

        if (string.IsNullOrEmpty(suffix))
        {
            text.gameObject.SetActive(false);
            return;
        }

        Transform maxStaminaTransform = staminaBar.transform.Find("LayoutGroup/MaxStamina");
        if (maxStaminaTransform is not RectTransform maxStamina)
        {
            text.gameObject.SetActive(false);
            return;
        }

        float preferredWidth = text.GetPreferredValues(suffix).x;
        if (preferredWidth > maxStamina.sizeDelta.x)
        {
            text.gameObject.SetActive(false);
            return;
        }

        if (!forceUpdate && text.gameObject.activeSelf)
        {
            return;
        }

        text.text = suffix;
        text.gameObject.SetActive(true);
    }

    private static string GetStaminaHungerCountdownSuffix(StaminaBar staminaBar, out bool forceUpdate)
    {
        forceUpdate = false;

        Character? character = Character.observedCharacter;
        CharacterAfflictions? afflictions = character?.refs?.afflictions;
        float timeRemaining = 0f;
        bool canShow =
            PluginConfig.StaminaInfoShowHungerCountdown.Value &&
            character != null &&
            afflictions != null;

        bool shouldShow = false;
        if (canShow)
        {
            timeRemaining = GetHungerIncreaseTimeRemaining(character!, afflictions!);
            BarAffliction? hungerBar = GetHungerBar(staminaBar);
            shouldShow = timeRemaining > 0f &&
                (hungerBar == null || !IsVisibleAfflictionBar(hungerBar) || ShouldMoveHungerCountdownToStamina(hungerBar, timeRemaining));
        }

        string suffix = string.Empty;
        if (shouldShow)
        {
            suffix = YellowTextStart + "(" + FormatTime(timeRemaining) + ")" + ColorTextEnd;
        }

        bool isShown = !string.IsNullOrEmpty(suffix);
        forceUpdate =
            wasHiddenHungerCountdownShown != isShown ||
            (isShown && (!lastAfflictionUpdateTime.ContainsKey(HiddenHungerCountdownKey) ||
                Time.time - lastAfflictionUpdateTime[HiddenHungerCountdownKey] > AfflictionRefreshInterval));
        wasHiddenHungerCountdownShown = isShown;
        return suffix;
    }

    private static BarAffliction? GetHungerBar(StaminaBar staminaBar)
    {
        foreach (BarAffliction affliction in staminaBar.afflictions)
        {
            if (affliction.afflictionType == CharacterAfflictions.STATUSTYPE.Hunger)
            {
                return affliction;
            }
        }

        return null;
    }

    private static bool IsVisibleAfflictionBar(BarAffliction affliction)
    {
        return affliction.gameObject.activeSelf && affliction.size >= SmallBarThreshold;
    }

    private static bool ShouldMoveHungerCountdownToStamina(BarAffliction affliction, float timeRemaining)
    {
        if (affliction.afflictionType != CharacterAfflictions.STATUSTYPE.Hunger || !IsVisibleAfflictionBar(affliction))
        {
            return false;
        }

        if (!barTexts.TryGetValue(affliction.name, out TextMeshProUGUI text))
        {
            return false;
        }

        string valueText = FormatBarValue(
            affliction.size,
            PluginConfig.StaminaInfoRoundAfflictionBars.Value,
            trimWholeDecimal: true);
        string countdownText = "(" + FormatTime(timeRemaining) + ")";
        float preferredWidth = text.GetPreferredValues(valueText + countdownText).x;
        return preferredWidth > affliction.size;
    }

    public static void UpdateCharacterBarTexts(CharacterStaminaBar characterStaminaBar)
    {
        if (characterStaminaBar.ObservedCharacter == null)
        {
            return;
        }

        var id = characterStaminaBar.GetInstanceID();

        float currentStaminaSize = Mathf.Max(
            0f,
            characterStaminaBar.ObservedCharacter.data.currentStamina * characterStaminaBar.fullBarRectTransform.sizeDelta.x);

        UpdateBarText(
            GetCharacterStaminaKey(id),
            currentStaminaSize,
            PluginConfig.StaminaInfoRoundStaminaBars.Value,
            trimWholeDecimal: false,
            cache: lastKnownData
        );

        float desiredExtraStaminaSize = Mathf.Max(
            0f,
            characterStaminaBar.ObservedCharacter.data.extraStamina * characterStaminaBar.fullBarRectTransform.sizeDelta.x);

        bool showExtraStaminaOutsideBar = PluginConfig.StaminaInfoShowTeammateExtraStaminaOutsideBar.Value;
        ApplyCharacterExtraStaminaTextLayout(characterStaminaBar, showExtraStaminaOutsideBar);

        UpdateBarText(
            GetCharacterExtraStaminaKey(id),
            desiredExtraStaminaSize,
            PluginConfig.StaminaInfoRoundStaminaBars.Value,
            trimWholeDecimal: false,
            cache: lastKnownData,
            prefix: showExtraStaminaOutsideBar ? "+" : string.Empty
        );

        foreach (CharacterBarAffliction affliction in characterStaminaBar.characterBarAfflictions)
        {
            affliction.FetchDesiredSize();

            UpdateBarText(
                GetCharacterAfflictionKey(id, affliction),
                affliction.size,
                PluginConfig.StaminaInfoRoundAfflictionBars.Value,
                trimWholeDecimal: true,
                cache: lastKnownAffData
            );
        }
    }

    private static void UpdateBarText(
        string key,
        float size,
        bool roundLargeValue,
        bool trimWholeDecimal,
        Dictionary<string, float> cache,
        string prefix = "",
        string suffix = "",
        bool forceUpdate = false)
    {
        if (!barTexts.TryGetValue(key, out TextMeshProUGUI text))
        {
            return;
        }

        bool needsUpdate =
            forceUpdate ||
            !cache.ContainsKey(key) ||
            !Mathf.Approximately(cache[key], size) ||
            !text.gameObject.activeSelf;

        if (size < SmallBarThreshold)
        {
            text.gameObject.SetActive(false);
            cache[key] = size;
            return;
        }

        string valueText = prefix + FormatBarValue(size, roundLargeValue, trimWholeDecimal);
        string desiredText = string.IsNullOrEmpty(suffix) ? valueText : valueText + suffix;

        needsUpdate = needsUpdate || text.text != desiredText;
        if (!needsUpdate)
        {
            return;
        }

        text.text = desiredText;
        text.gameObject.SetActive(true);
        cache[key] = size;
    }

    private static string FormatBarValue(float size, bool roundLargeValue, bool trimWholeDecimal)
    {
        if (size >= LargeBarThreshold)
        {
            float value = size / BarUnitSize;
            if (roundLargeValue)
            {
                return Mathf.Round(value).ToString();
            }

            string valueText = value.ToString("F1");
            return trimWholeDecimal ? valueText.Replace(".0", "") : valueText;
        }

        return Mathf.Round(size / BarUnitSize).ToString();
    }

    private static void HideBarTexts(StaminaBar staminaBar)
    {
        HideText(staminaBar.staminaBar.name);
        HideText("ExtraStamina");
        HideText(HiddenHungerCountdownKey);

        foreach (BarAffliction affliction in staminaBar.afflictions)
        {
            HideText(affliction.name);
        }
    }

    private static void HideCharacterBarTexts(CharacterStaminaBar characterStaminaBar)
    {
        var id = characterStaminaBar.GetInstanceID();

        HideText(GetCharacterStaminaKey(id));
        HideText(GetCharacterExtraStaminaKey(id));

        foreach (CharacterBarAffliction affliction in characterStaminaBar.characterBarAfflictions)
        {
            HideText(GetCharacterAfflictionKey(id, affliction));
        }
    }

    private static void HideText(string key)
    {
        if (barTexts.TryGetValue(key, out TextMeshProUGUI text))
        {
            text.gameObject.SetActive(false);
        }
    }

    private static bool CanShowCountdown(CharacterAfflictions.STATUSTYPE afflictionType)
    {
        return afflictionType != CharacterAfflictions.STATUSTYPE.Weight
            && afflictionType != CharacterAfflictions.STATUSTYPE.Injury
            && afflictionType != CharacterAfflictions.STATUSTYPE.Curse;
    }

    private static bool IsCountdownEnabled(CharacterAfflictions.STATUSTYPE afflictionType)
    {
        return afflictionType == CharacterAfflictions.STATUSTYPE.Hunger
            ? PluginConfig.StaminaInfoShowHungerCountdown.Value
            : PluginConfig.StaminaInfoShowAfflictionCountdown.Value;
    }

    private static string GetCharacterStaminaKey(int id) => id + "_Stamina";
    private static string GetCharacterExtraStaminaKey(int id) => id + "_ExtraStamina";
    private static string GetCharacterAfflictionKey(int id, CharacterBarAffliction affliction) => id + "_" + affliction.gameObject.name;

    private static void InitStaminaInfo(StaminaBar staminaBar)
    {
        GameObject guiManagerGameObj = GameObject.Find("GAME/GUIManager");
        guiManager = guiManagerGameObj.GetComponent<GUIManager>();
        AddTextObject(staminaBar.staminaBar.gameObject, staminaBar.staminaBar.name);
        AddMaxStaminaHungerCountdownTextObject(staminaBar);
        AddTextObject(staminaBar.extraBarStamina.gameObject, "ExtraStamina");
        foreach (BarAffliction affliction in staminaBar.afflictions)
        {
            AddTextObject(affliction.gameObject, affliction.gameObject.name);
        }
    }

    public static void InitCharacterStaminaInfo(CharacterStaminaBar characterStaminaBar)
    {
        var id = characterStaminaBar.GetInstanceID();
        AddTextObject(characterStaminaBar.staminaBarRectTransform.gameObject, GetCharacterStaminaKey(id));
        string extraStaminaKey = GetCharacterExtraStaminaKey(id);
        AddTextObject(characterStaminaBar.extraBar.gameObject, extraStaminaKey);
        ApplyCharacterExtraStaminaTextLayout(
            characterStaminaBar,
            PluginConfig.StaminaInfoShowTeammateExtraStaminaOutsideBar.Value);

        foreach (CharacterBarAffliction affliction in characterStaminaBar.characterBarAfflictions)
        {
            AddTextObject(affliction.gameObject, GetCharacterAfflictionKey(id, affliction));
        }
    }

    private static void ApplyCharacterExtraStaminaTextLayout(CharacterStaminaBar characterStaminaBar, bool showOutsideBar)
    {
        string extraStaminaKey = GetCharacterExtraStaminaKey(characterStaminaBar.GetInstanceID());
        if (!barTexts.TryGetValue(extraStaminaKey, out TextMeshProUGUI text))
        {
            return;
        }

        if (showOutsideBar)
        {
            if (text.transform.parent != characterStaminaBar.extraBar)
            {
                text.transform.SetParent(characterStaminaBar.extraBar, worldPositionStays: false);
            }

            text.color = Color.green;
            PositionCharacterExtraStaminaTextOutsideBar(text, characterStaminaBar);
            return;
        }

        Transform textParent =
            characterStaminaBar.extraBarStamina.Find("Stamina")
            ?? characterStaminaBar.extraBarStamina;
        if (text.transform.parent != textParent)
        {
            text.transform.SetParent(textParent, worldPositionStays: false);
        }

        PositionCharacterExtraStaminaTextInsideBar(text);
    }

    private static void PositionCharacterExtraStaminaTextInsideBar(TextMeshProUGUI text)
    {
        RectTransform rectTransform = text.rectTransform;
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.anchoredPosition = Vector2.zero;
        rectTransform.localRotation = Quaternion.identity;
        rectTransform.localScale = Vector3.one;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;
        text.color = Color.black;
        text.alignment = TextAlignmentOptions.Center;
    }

    private static void PositionCharacterExtraStaminaTextOutsideBar(TextMeshProUGUI text, CharacterStaminaBar characterStaminaBar)
    {
        RectTransform rectTransform = text.rectTransform;
        RectTransform outline = characterStaminaBar.extraBarOutline;
        float outlineRightEdge = outline.anchoredPosition.x + outline.sizeDelta.x * (1f - outline.pivot.x);

        rectTransform.anchorMin = new Vector2(0f, 0.5f);
        rectTransform.anchorMax = new Vector2(0f, 0.5f);
        rectTransform.pivot = new Vector2(0f, 0.5f);
        rectTransform.anchoredPosition = new Vector2(outlineRightEdge + CharacterExtraStaminaTextOffsetX, outline.anchoredPosition.y);
        rectTransform.sizeDelta = new Vector2(CharacterExtraStaminaTextWidth, Mathf.Max(outline.sizeDelta.y, 1f));
        rectTransform.localRotation = Quaternion.identity;
        rectTransform.localScale = Vector3.one;
        text.alignment = TextAlignmentOptions.Left;
    }

    private static void AddMaxStaminaHungerCountdownTextObject(StaminaBar staminaBar)
    {
        Transform maxStaminaTransform = staminaBar.transform.Find("LayoutGroup/MaxStamina");
        if (maxStaminaTransform == null)
        {
            return;
        }

        AddTextObject(maxStaminaTransform.gameObject, HiddenHungerCountdownKey);
    }

    internal static void AddTextObject(GameObject gameObj, string barName)
    {
        if (guiManager == null)
        {
            guiManager = GUIManager.instance;
        }

        if (guiManager == null)
        {
            return;
        }

        TMP_FontAsset font = guiManager.heroDayText.font;
        GameObject staminaInfo = new GameObject("StaminaInfo");
        staminaInfo.transform.SetParent(gameObj.transform);

        TextMeshProUGUI staminaInfoText = staminaInfo.AddComponent<TextMeshProUGUI>();
        RectTransform staminaInfoRect = staminaInfo.GetComponent<RectTransform>();
        gameObj.SetActive(true); // Necessary to update .fontSharedMaterial?

        staminaInfoText.font = font;
        staminaInfoText.fontSize = PluginConfig.StaminaInfoFontSize.Value;
        staminaInfoRect.anchorMin = Vector2.zero;
        staminaInfoRect.anchorMax = Vector2.one;
        staminaInfoRect.pivot = new Vector2(0.5f, 0.5f);
        staminaInfoRect.anchoredPosition = Vector2.zero;
        staminaInfoRect.localRotation = Quaternion.identity;
		staminaInfoRect.localScale = gameObj.TryGetComponent<CharacterBarAffliction>(out var characterAffliction)
			&& characterAffliction.isPetrify
			? new Vector3(
				1f / CharacterStaminaBar.ExtraStaminaBarWidthScale,
				1f / CharacterStaminaBar.PetrifyBarHeightScale,
				1f)
			: Vector3.one;
        staminaInfoRect.offsetMin = Vector2.zero;
        staminaInfoRect.offsetMax = Vector2.zero;
        staminaInfoText.alignment = TextAlignmentOptions.Center;
        staminaInfoText.verticalAlignment = VerticalAlignmentOptions.Capline;
        staminaInfoText.richText = true;
        staminaInfoText.textWrappingMode = TextWrappingModes.NoWrap;
        staminaInfoText.text = "";
        staminaInfoText.gameObject.SetActive(false);

        barTexts[barName] = staminaInfoText;
        lastKnownData[barName] = 0f;
        lastKnownAffData[barName] = 0f;
        lastAfflictionUpdateTime[barName] = -1f;

        try
        {
            staminaInfoText.outlineWidth = PluginConfig.StaminaInfoOutlineWidth.Value;
        }
        catch
        {
        }
    }

    private static void ConstrainStaminaTextToFill(string key, RectTransform fillRectTransform)
    {
        if (barTexts.TryGetValue(key, out TextMeshProUGUI text))
        {
            SetStaminaTextBounds(text, fillRectTransform);
        }
    }

    private static void SetStaminaTextBounds(TextMeshProUGUI text, RectTransform fillRectTransform)
    {
        float visibleWidth = Mathf.Max(0f, fillRectTransform.rect.width);
        if (fillRectTransform.parent is RectTransform visibleParent)
        {
            visibleWidth = Mathf.Min(visibleWidth, Mathf.Max(0f, visibleParent.rect.width));
        }

        RectTransform textRectTransform = text.rectTransform;
        textRectTransform.anchorMin = Vector2.zero;
        textRectTransform.anchorMax = new Vector2(0f, 1f);
        textRectTransform.pivot = new Vector2(0f, 0.5f);
        textRectTransform.anchoredPosition = Vector2.zero;
        textRectTransform.sizeDelta = new Vector2(visibleWidth, 0f);
    }

    private static float GetReductionTimeRemaining(CharacterAfflictions.STATUSTYPE statusType)
    {
        CharacterAfflictions? afflictions = Character.observedCharacter?.refs?.afflictions;
        if (afflictions == null)
        {
            return 0f;
        }
        if (statusType == CharacterAfflictions.STATUSTYPE.Hunger)
        {
            Character? character = Character.observedCharacter;
            return character == null ? 0f : GetHungerIncreaseTimeRemaining(character, afflictions);
        }
        if (statusType == CharacterAfflictions.STATUSTYPE.Thorns)
        {
            float time = 0f;
            int count = 0;
            foreach (ThornOnMe item in afflictions.physicalThorns)
            {
                if (item.stuckIn)
                {
                    count++;
                    time += Time.time - GameRef.GetPopOutTime(item);
                }
            }
            if (count == 0)
            {
                return 0f;
            }
            return Mathf.Abs(time) / count;
        }
        float currentStatus = afflictions.GetCurrentStatus(statusType);
        if (currentStatus <= 0f)
        {
            return 0f;
        }
        float reductionRate = GetReductionRate(afflictions, statusType);
        if (reductionRate <= 0f)
        {
            return 0f;
        }
        float cooldown = GetCooldown(afflictions, statusType);
        float lastAddedTime = afflictions.LastAddedStatus(statusType);
        float currentTime = Time.time;
        if (cooldown > 0f && currentTime - lastAddedTime < cooldown)
        {
            float cooldownRemaining = cooldown - (currentTime - lastAddedTime);
            float reductionTime = currentStatus / reductionRate;
            return cooldownRemaining + reductionTime;
        }
        return currentStatus / reductionRate;
    }

    private static float GetHungerIncreaseTimeRemaining(Character character, CharacterAfflictions afflictions)
    {
        if (character == null ||
            character != Character.localCharacter ||
            !character.data.fullyConscious ||
            GameRef.GetInAirport(afflictions))
        {
            return 0f;
        }

        float hungerRate = afflictions.hungerPerSecond * Ascents.hungerRateMultiplier;
        if (hungerRate <= 0f)
        {
            return 0f;
        }

        bool blockedByVanilla = GameRef.GetIsInvincible(character.data) || !afflictions.canGetHungry;
        if (blockedByVanilla)
        {
            ResetRealHungerProgressWhenBlockedStateStarts(afflictions);
        }
        else
        {
            wasHungerBlockedByVanilla[afflictions.GetInstanceID()] = false;
        }

        bool hasRealProgress = HasRecentlyObservedRealHungerProgress(afflictions);
        if (blockedByVanilla && !hasRealProgress)
        {
            ClearFallbackHungerIncrementalStatus(afflictions);
        }

        float currentIncrementalStatus = GetHungerIncrementalStatus(afflictions);
        float remainingIncrement = HungerIncrementThreshold - currentIncrementalStatus;
        if (remainingIncrement <= 0f)
        {
            remainingIncrement = HungerIncrementThreshold;
        }

        return remainingIncrement / hungerRate;
    }

    private static void ResetRealHungerProgressWhenBlockedStateStarts(CharacterAfflictions afflictions)
    {
        int id = afflictions.GetInstanceID();
        if (wasHungerBlockedByVanilla.TryGetValue(id, out bool wasBlocked) && wasBlocked)
        {
            return;
        }

        wasHungerBlockedByVanilla[id] = true;
        lastRealHungerProgressTimes.Remove(id);
        if (TryReadCurrentIncrementalHunger(afflictions, out float currentIncrementalStatus))
        {
            lastObservedRealHungerIncrementalStatuses[id] = currentIncrementalStatus;
        }
    }

    private static bool HasRecentlyObservedRealHungerProgress(CharacterAfflictions afflictions)
    {
        int id = afflictions.GetInstanceID();
        if (TryReadCurrentIncrementalHunger(afflictions, out float currentIncrementalStatus))
        {
            if (lastObservedRealHungerIncrementalStatuses.TryGetValue(id, out float lastObservedStatus) &&
                currentIncrementalStatus > lastObservedStatus)
            {
                lastRealHungerProgressTimes[id] = Time.time;
            }

            lastObservedRealHungerIncrementalStatuses[id] = currentIncrementalStatus;
        }

        return lastRealHungerProgressTimes.TryGetValue(id, out float lastProgressTime) &&
            Time.time - lastProgressTime <= AfflictionRefreshInterval * 2f;
    }

    private static float GetHungerIncrementalStatus(CharacterAfflictions afflictions)
    {
        float fallbackIncrementalStatus = GetFallbackHungerIncrementalStatus(afflictions);
        if (TryReadCurrentIncrementalHunger(afflictions, out float currentIncrementalStatus))
        {
            return Mathf.Max(currentIncrementalStatus, fallbackIncrementalStatus);
        }

        return fallbackIncrementalStatus;
    }

    private static bool TryReadCurrentIncrementalHunger(CharacterAfflictions afflictions, out float currentIncrementalStatus)
    {
        currentIncrementalStatus = 0f;
        if (currentIncrementalStatusesField?.GetValue(afflictions) is not float[] currentIncrementalStatuses)
        {
            return false;
        }

        int hungerIndex = (int)CharacterAfflictions.STATUSTYPE.Hunger;
        if (hungerIndex < 0 || hungerIndex >= currentIncrementalStatuses.Length)
        {
            return false;
        }

        currentIncrementalStatus = currentIncrementalStatuses[hungerIndex];
        return true;
    }

    private static float GetReductionRate(CharacterAfflictions afflictions, CharacterAfflictions.STATUSTYPE statusType)
    {
        return statusType switch
        {
            CharacterAfflictions.STATUSTYPE.Poison => afflictions.poisonReductionPerSecond,
            CharacterAfflictions.STATUSTYPE.Hot => afflictions.hotReductionPerSecond,
            CharacterAfflictions.STATUSTYPE.Spores => afflictions.sporesReductionPerSecond,
            CharacterAfflictions.STATUSTYPE.Drowsy => afflictions.drowsyReductionPerSecond,
            _ => 0f,
        };
    }

    private static float GetCooldown(CharacterAfflictions afflictions, CharacterAfflictions.STATUSTYPE statusType)
    {
        return statusType switch
        {
            CharacterAfflictions.STATUSTYPE.Poison => afflictions.poisonReductionCooldown,
            CharacterAfflictions.STATUSTYPE.Hot => afflictions.hotReductionCooldown,
            CharacterAfflictions.STATUSTYPE.Spores => afflictions.sporesReductionCooldown,
            CharacterAfflictions.STATUSTYPE.Drowsy => afflictions.drowsyReductionCooldown,
            _ => 0f,
        };
    }

    private static string FormatTime(float seconds)
    {
        if (seconds < 60f)
        {
            return Mathf.CeilToInt(seconds).ToString();
        }
        if (seconds < 3600f)
        {
            int minutes = Mathf.FloorToInt(seconds / 60f);
            int remainingSeconds = Mathf.CeilToInt(seconds % 60f);
            return $"{minutes}:{remainingSeconds:D2}";
        }
        int hours = Mathf.FloorToInt(seconds / 3600f);
        int minutes2 = Mathf.FloorToInt(seconds % 3600f / 60f);
        return $"{hours}:{minutes2:D2}";
    }
}
