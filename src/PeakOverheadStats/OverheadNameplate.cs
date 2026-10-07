using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PeakOverheadStats.MonoBehaviours;

/// <summary>
/// Attached to each PlayerName. Shows full PeakStatsEx CharacterStaminaBar above the name.
/// </summary>
internal sealed class OverheadNameplate : MonoBehaviour
{
    private const float NameTextClearance = 25f;

    private CharacterStaminaBar? staminaBar;

    internal void Refresh(PlayerName name, bool visible)
    {
        Character local = Character.localCharacter;
        Character? target = name.characterInteractable != null ? name.characterInteractable.character : null;

        if (local == null || target == null || target == local || !ShouldShow(local, target))
        {
            Hide();
            return;
        }

        if (name.text == null)
        {
            Hide();
            return;
        }

        EnsureStaminaBar(name.text.transform, target);

        if (staminaBar != null)
        {
            staminaBar.ObservedCharacter = target;
            staminaBar.AnimateEnable();
        }
    }

    internal void Hide()
    {
        if (staminaBar != null)
            staminaBar.AnimateDisable();
    }

    private static bool ShouldShow(Character local, Character target)
    {
        float maxDist = PluginConfig.TeammateStaminaBarProximity.Value;
        Character observer = Character.observedCharacter != null ? Character.observedCharacter : local;
        if (Vector3.Distance(observer.Center, target.Center) > maxDist)
            return false;

        Camera cam = MainCamera.instance != null ? MainCamera.instance.GetComponent<Camera>() : null;
        if (cam == null) return true;

        Vector3 vp = cam.WorldToViewportPoint(target.Center);
        return vp.z > 0f && vp.x >= -0.1f && vp.x <= 1.1f && vp.y >= -0.1f && vp.y <= 1.1f;
    }

    private void EnsureStaminaBar(Transform parent, Character observedCharacter)
    {
        if (staminaBar != null && staminaBar.gameObject != null && staminaBar.transform.parent == parent)
        {
            RemoveDuplicates(parent, staminaBar.gameObject);
            return;
        }

        if (staminaBar != null)
        {
            Destroy(staminaBar.gameObject);
            staminaBar = null;
        }

        if (GUIManager.instance == null || GUIManager.instance.bar == null)
            return;

        // Exact replica of ProximityStaminaManager.CreateStaminaBar
        Transform barTransform = GUIManager.instance.bar.transform;
        Transform barParentTransform = Instantiate(barTransform, parent);

        // Remove StaminaInfo
        foreach (Transform t in barParentTransform.GetComponentsInChildren<Transform>(includeInactive: true))
        {
            if (t.name == "StaminaInfo")
                Destroy(t.gameObject);
        }

        barParentTransform.SetAsFirstSibling();
        Destroy(barParentTransform.GetComponent<StaminaBar>());
        CharacterStaminaBar characterStaminaBar = barParentTransform.gameObject.AddComponent<CharacterStaminaBar>();

        // Replace BarAffliction with CharacterBarAffliction
        foreach (BarAffliction barAffliction in barParentTransform.GetComponentsInChildren<BarAffliction>(includeInactive: true))
        {
            CharacterBarAffliction characterBarAffliction = barAffliction.gameObject.AddComponent<CharacterBarAffliction>();
            characterBarAffliction.afflictionType = barAffliction.afflictionType;
            characterBarAffliction.isPetrify = barAffliction.isPetrify;
            characterBarAffliction.FetchReferences();
            characterStaminaBar.AddCharacterBarAffliction(characterBarAffliction);
            Destroy(barAffliction);
        }

        // Create player name text (EXACT same as ProximityStaminaManager)
        GameObject playerNameObject = new GameObject("CharacterName", typeof(RectTransform), typeof(TextMeshProUGUI));
        RectTransform nameRect = playerNameObject.GetComponent<RectTransform>();
        nameRect.SetParent(barParentTransform, worldPositionStays: false);

        TextMeshProUGUI playerNameText = playerNameObject.GetComponent<TextMeshProUGUI>();
        TextMeshProUGUI textTemplate = GUIManager.instance.heroDayText;
        playerNameText.font = textTemplate.font;
        playerNameText.fontSharedMaterial = textTemplate.fontSharedMaterial;

        string pName = Regex.Replace(observedCharacter.characterName, @"(<color=#\w{6}>|</color>)", "");
        playerNameText.text = pName.TrimToByteLength(18);
        nameRect.name = "CharacterName: " + playerNameText.text;

        playerNameText.textWrappingMode = TextWrappingModes.NoWrap;
        playerNameText.overflowMode = TextOverflowModes.Ellipsis;
        playerNameText.alignment = TextAlignmentOptions.Left;
        playerNameText.fontSize = 32f;
        playerNameText.autoSizeTextContainer = false;
        playerNameText.rectTransform.sizeDelta = new Vector2(260f, playerNameText.rectTransform.sizeDelta.y);
        playerNameText.color = observedCharacter.refs.customization.PlayerColor;
        playerNameText.outlineColor = new Color32(0, 0, 0, byte.MaxValue);
        playerNameText.outlineWidth = 0.055f;

        characterStaminaBar.InitializeHeader(nameRect);
        characterStaminaBar.ObservedCharacter = observedCharacter;

        // Center the whole bar horizontally above the character's name
        RectTransform barRect = (RectTransform)barParentTransform;
        barRect.pivot = new Vector2(0.5f, barRect.pivot.y);
        barRect.anchoredPosition = new Vector2(0f, NameTextClearance);

        staminaBar = characterStaminaBar;
        RemoveDuplicates(parent, staminaBar.gameObject);
        staminaBar.AnimateEnable();
    }

    private static void RemoveDuplicates(Transform parent, GameObject keep)
    {
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            Transform child = parent.GetChild(i);
            if (child.gameObject != keep && child.GetComponent<CharacterStaminaBar>() != null)
                Destroy(child.gameObject);
        }
    }
}