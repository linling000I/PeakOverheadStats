using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Zorro.Core;


namespace PeakOverheadStats.MonoBehaviours;

internal sealed class CharacterStaminaBar : MonoBehaviour
{
	private const float SampleScaleLow = 0.6f;
	private const float DefaultTeammateScale = 0.72f;
	private const float SampleScaleHigh = 1f;
	private const float DefaultTeammateVisualYOffset = 10f;
	private const float ExtraBarExpandedSize = 28f;
	private const float TeammateExtraStaminaFillOffsetX = 5f;
	private const float InventorySlotsBaseX = 284f;
	private const float HeaderRowWidth = 600f;
	private const float HeaderVisualGap = -2f;
	private const float SelectedSlotEnvelopeScale = 1.38f;
	private const float LargestInventorySlotSize = 38f;
	private const float HeaderRowHeight = LargestInventorySlotSize * SelectedSlotEnvelopeScale;
	internal const float ExtraStaminaBarWidthScale = 0.7f;
	internal const float PetrifyBarHeightScale = 0.5f;

	private static readonly Color defaultBarColor = new Color(0.12f, 0.12f, 0.15f, 0.7f);

	private static readonly Color outOfStaminaColor = new Color(0.566f, 0.0089f, 0.0089f, 1f);

	private float minStaminaBarWidth = 20f;

	internal float minAfflictionWidth = 60f;

	internal RectTransform fullBarRectTransform = null!;

	internal RectTransform staminaBarRectTransform = null!;

	internal RectTransform maxStaminaBarRectTransform = null!;

	internal RectTransform staminaBarOutlineRectTransform = null!;

	internal RectTransform staminaBarOutlineOverflowBar = null!;

	internal Image barImage = null!;

	internal Image glowImage = null!;

	internal RectTransform extraBar = null!;

	internal RectTransform extraBarStamina = null!;

	internal RectTransform extraBarOutline = null!;

	internal GameObject shieldIcon = null!;

	internal GameObject campfireIcon = null!;

	private float staminaBarOffset;

	internal readonly List<CharacterBarAffliction> characterBarAfflictions = new List<CharacterBarAffliction>();

	private CharacterBarAffliction? petrifyAffliction;

	private Coroutine? animateDisableCoroutine;

	private Character? _observedCharacter;

	private bool hadObservedCharacter;

	private bool outOfStamina;

	private bool sequencingExtraBar;

	private bool isEnabled;

	private float sinTime;

	private readonly float TAU = MathF.PI * 2f;

	private float desiredExtraStaminaSize;

	private float extraOutlineMaxWidth;

	private float extraBarDefaultVisualGap;

	private RectTransform headerRowRectTransform = null!;

	private readonly Vector3[] outlineWorldCorners = new Vector3[4];

	private readonly List<InventorySlotUI> inventorySlotUIs = new List<InventorySlotUI>();

	private readonly List<InventorySlotUI> backpackSlotUIs = new List<InventorySlotUI>();

	private InventorySlotUI? heldItemSlot;

	private bool inventorySlotsCreated;

	private bool inventorySlotsVisible;

	private float appliedTeammateVisualYOffset;

	internal Character? ObservedCharacter
	{
		get
		{
			return _observedCharacter;
		}
		set
		{
			if (value != null)
			{
				hadObservedCharacter = true;
			}
			_observedCharacter = value;
			if (value != null)
			{
				ApplyTeammateVisualOffset();
			}
		}
	}

	internal void AnimateEnable()
	{
		if (!isEnabled && animateDisableCoroutine == null)
		{
			isEnabled = true;

			base.gameObject.SetActive(value: true);
			animateDisableCoroutine = StartCoroutine(EnableIEnumerator());
		}
	}

	internal void AnimateDisable()
	{
		if (isEnabled && animateDisableCoroutine == null)
		{
			isEnabled = false;
			animateDisableCoroutine = StartCoroutine(DisableIEnumerator());
		}
	}

	private void OnDisable()
	{
		base.transform.DOKill();
		StopAllCoroutines();
		animateDisableCoroutine = null;
		isEnabled = false;
		base.transform.localScale = Vector3.zero;
		if (base.gameObject.activeSelf)
		{
			base.gameObject.SetActive(value: false);
		}
	}

	internal void InitializeHeader(RectTransform playerNameRectTransform)
	{
		playerNameRectTransform.SetParent(headerRowRectTransform, worldPositionStays: false);
		playerNameRectTransform.anchorMin = new Vector2(0.5f, 0f);
		playerNameRectTransform.anchorMax = new Vector2(0.5f, 0f);
		playerNameRectTransform.pivot = new Vector2(0.5f, 0f);
		playerNameRectTransform.anchoredPosition = Vector2.zero;
		UpdateHeaderRowPosition();
	}

	private void Awake()
	{
		Plugin.Logger.LogDebug("[UpdateStaminaBar] CharacterStaminaBar Awake");
		if (TryGetComponent<StaminaBar>(out var original))
		{
			staminaBarOffset = original.staminaBarOffset;
			minStaminaBarWidth = original.minStaminaBarWidth;
			minAfflictionWidth = original.minAfflictionWidth;
			DisableMoraleBoostUi(original);
		}
		CacheStaminaBarReferences();
		CreateHeaderRow();
		if (original != null)
		{
			CreateExtraStaminaBar(original);
			if (original.extraBar != null)
			{
				original.extraBar.gameObject.SetActive(value: false);
			}
		}
	}

	private void Update()
	{
		if (!ObservedCharacter || !ObservedCharacter.gameObject.activeSelf)
		{
			if (hadObservedCharacter)
			{
				Destroy(base.gameObject);
			}
		}
		else
		{
			ApplyConfiguredScale();
			ApplyTeammateVisualOffset();
			ApplyExtraBarScaleCompensation();
			UpdateStaminaBar();
			UpdateExtraStaminaBar();
			if (PluginConfig.ShowInventorySlots.Value)
			{
				EnsureInventorySlotsCreated();
				inventorySlotsVisible = true;
				UpdateInventorySlots();
			}
			else if (inventorySlotsVisible)
			{
				ClearInventorySlots();
				inventorySlotsVisible = false;
			}
			UpdateHeaderRowPosition();
		}
	}

	private void CreateHeaderRow()
	{
		GameObject headerRowObject = new GameObject("HeaderRow", typeof(RectTransform));
		headerRowRectTransform = headerRowObject.GetComponent<RectTransform>();
		headerRowRectTransform.SetParent(transform, worldPositionStays: false);
		headerRowRectTransform.anchorMin = new Vector2(0.5f, 0.5f);
		headerRowRectTransform.anchorMax = new Vector2(0.5f, 0.5f);
		headerRowRectTransform.pivot = new Vector2(0.5f, 0f);
		headerRowRectTransform.sizeDelta = new Vector2(HeaderRowWidth, HeaderRowHeight);
		headerRowRectTransform.anchoredPosition = Vector2.zero;
	}

	private void UpdateHeaderRowPosition()
	{
		if (headerRowRectTransform == null || staminaBarOutlineRectTransform == null || animateDisableCoroutine != null)
		{
			return;
		}

		// Ensure header row is left-aligned with the stamina bar (pivot top-left)
		headerRowRectTransform.pivot = new Vector2(0f, 0f);

		staminaBarOutlineRectTransform.GetWorldCorners(outlineWorldCorners);
		float outlineTop = float.NegativeInfinity;
		float outlineLeft = float.PositiveInfinity;
		for (int i = 0; i < outlineWorldCorners.Length; i++)
		{
			Vector3 localPoint = transform.InverseTransformPoint(outlineWorldCorners[i]);
			outlineTop = Mathf.Max(outlineTop, localPoint.y);
			outlineLeft = Mathf.Min(outlineLeft, localPoint.x);
		}

		float targetY = outlineTop + HeaderVisualGap / GetConfiguredScale();
		Vector3 localPosition = headerRowRectTransform.localPosition;
		headerRowRectTransform.localPosition = new Vector3(outlineLeft, targetY, localPosition.z);
	}

	private void CacheStaminaBarReferences()
	{
		fullBarRectTransform = (RectTransform)base.transform.Find("FullBar");
		maxStaminaBarRectTransform = (RectTransform)base.transform.Find("LayoutGroup/MaxStamina");
		staminaBarOutlineRectTransform = (RectTransform)base.transform.Find("OutlineMask/Outline");
		staminaBarOutlineOverflowBar = (RectTransform)base.transform.Find("OutlineOverflowLine");
		barImage = maxStaminaBarRectTransform.Find("Back").GetComponent<Image>();
		barImage.color = defaultBarColor;
		staminaBarRectTransform = (RectTransform)barImage.transform.Find("Stamina");
		glowImage = staminaBarRectTransform.Find("Glow").GetComponent<Image>();
		campfireIcon = staminaBarOutlineRectTransform.Find("CampfireIcon").gameObject;
		shieldIcon = staminaBarOutlineRectTransform.Find("ShieldIcon").gameObject;
	}

	private static void DisableMoraleBoostUi(StaminaBar original)
	{
		if (original.moraleBoostAnimator != null)
		{
			original.moraleBoostAnimator.enabled = false;
		}

		if (original.moraleBoostText != null)
		{
			original.moraleBoostText.enabled = false;
			original.moraleBoostText.gameObject.SetActive(value: false);
		}
	}

	private void CreateExtraStaminaBar(StaminaBar original)
	{
		if (fullBarRectTransform == null)
		{
			Plugin.Logger.LogDebug("[CreateExtraStaminaBar] fullBar not found");
			return;
		}

		GameObject extraBarObject = new GameObject("FriendExtraBar");
		extraBarObject.transform.SetParent(base.transform, worldPositionStays: false);
		extraBar = extraBarObject.AddComponent<RectTransform>();
		extraBar.anchorMin = extraBar.anchorMax = new Vector2(0f, 0.5f);
		extraBar.pivot = new Vector2(0f, 0.5f);
		Vector2 fullBarPosition = fullBarRectTransform.anchoredPosition;
		extraBar.anchoredPosition = new Vector2(fullBarPosition.x, fullBarPosition.y - 31f);
		extraBarDefaultVisualGap =
			(fullBarRectTransform.anchoredPosition.y - extraBar.anchoredPosition.y) * DefaultTeammateScale;
		extraBar.sizeDelta = Vector2.one * ExtraBarExpandedSize;

		if (original.extraBarOutline != null)
		{
			GameObject outlineObject = Instantiate(
				original.extraBarOutline.gameObject,
				extraBarObject.transform,
				worldPositionStays: false);
			outlineObject.name = "FriendExtraBarOutline";
			extraBarOutline = outlineObject.GetComponent<RectTransform>();
			Vector2 outlinePosition = extraBarOutline.anchoredPosition;
			extraBarOutline.anchoredPosition = new Vector2(outlinePosition.x - 46f, outlinePosition.y - 1f);
			extraOutlineMaxWidth = fullBarRectTransform.sizeDelta.x * ExtraStaminaBarWidthScale + 12f;
			extraBarOutline.sizeDelta = new Vector2(extraOutlineMaxWidth, extraBarOutline.sizeDelta.y);
			RemoveAllScriptsExcept<Image, RectTransform>(outlineObject);
			RemoveCopiedStaminaInfo(outlineObject);
		}

		if (original.extraBarStamina != null)
		{
			GameObject staminaObject = Instantiate(
				original.extraBarStamina.gameObject,
				extraBarObject.transform,
				worldPositionStays: false);
			staminaObject.name = "FriendExtraBarStamina";
			extraBarStamina = staminaObject.GetComponent<RectTransform>();
			extraBarStamina.SetParent(extraBarOutline, worldPositionStays: false);
			extraBarStamina.anchorMin = extraBarStamina.anchorMax = new Vector2(0f, 0.5f);
			extraBarStamina.pivot = new Vector2(0f, 0.5f);
			extraBarStamina.anchoredPosition = Vector2.zero;
			extraBarStamina.localRotation = Quaternion.identity;
			extraBarStamina.localScale = Vector3.one;
			extraBarStamina.offsetMin = new Vector2(0f, extraBarStamina.offsetMin.y);

			if (staminaObject.transform.Find("Stamina") is RectTransform fillRectTransform)
			{
				fillRectTransform.anchoredPosition = new Vector2(
					fillRectTransform.anchoredPosition.x + TeammateExtraStaminaFillOffsetX,
					fillRectTransform.anchoredPosition.y);
				fillRectTransform.sizeDelta = new Vector2(
					fillRectTransform.sizeDelta.x,
					fillRectTransform.sizeDelta.y * 0.5f);
			}

			RemoveAllScriptsExcept<Image, RectTransform>(staminaObject);
			RemoveCopiedStaminaInfo(staminaObject);
		}

		CreateOrMovePetrifyAffliction();

		extraBarObject.SetActive(value: false);
	}

	private void CreateOrMovePetrifyAffliction()
	{
		if (petrifyAffliction == null && GUIManager.instance.bar.petrifyAffliction != null)
		{
			BarAffliction sourcePetrifyAffliction = GUIManager.instance.bar.petrifyAffliction;
			GameObject petrifyObject = Instantiate(sourcePetrifyAffliction.gameObject, extraBarOutline, worldPositionStays: false);
			petrifyObject.name = "FriendPetrifyAffliction";
			RemoveAllScriptsExcept<Image, RectTransform>(petrifyObject);

			petrifyAffliction = petrifyObject.AddComponent<CharacterBarAffliction>();
			petrifyAffliction.afflictionType = sourcePetrifyAffliction.afflictionType;
			petrifyAffliction.isPetrify = true;
			petrifyAffliction.FetchReferences();
		}

		if (petrifyAffliction != null)
		{
			RectTransform petrifyRectTransform = (RectTransform)petrifyAffliction.transform;
			petrifyRectTransform.SetParent(extraBarOutline, worldPositionStays: false);
			petrifyRectTransform.anchorMin = petrifyRectTransform.anchorMax = new Vector2(1f, 0.5f);
			petrifyRectTransform.pivot = new Vector2(1f, 0.5f);
			petrifyRectTransform.anchoredPosition = new Vector2(-8f, 0f);
			petrifyRectTransform.localRotation = Quaternion.identity;
			petrifyRectTransform.localScale = new Vector3(
				ExtraStaminaBarWidthScale,
				PetrifyBarHeightScale,
				1f);
			petrifyRectTransform.SetAsLastSibling();
		}
	}

	private void UpdateExtraStaminaBar()
	{
		if (extraBar == null || extraBarStamina == null || extraBarOutline == null || ObservedCharacter == null)
		{
			return;
		}
		float extra = ObservedCharacter.data.extraStamina;
		bool petrifyActive = petrifyAffliction != null && ObservedCharacter.data.petrifyAmount > 1f;
		bool shouldShowExtraBar = extra > 0f || petrifyActive;
		if (!extraBar.gameObject.activeSelf && shouldShowExtraBar)
		{
			extraBar.sizeDelta = Vector2.zero;
			extraBar.DOKill();
			extraBar.sizeDelta = Vector2.one * ExtraBarExpandedSize;
			extraBar.gameObject.SetActive(value: true);
		}
		if (extraBar.gameObject.activeSelf)
		{
			desiredExtraStaminaSize = Mathf.Max(0f, extra * fullBarRectTransform.sizeDelta.x * ExtraStaminaBarWidthScale);
			extraBarStamina.sizeDelta = new Vector2(Mathf.Lerp(extraBarStamina.sizeDelta.x, Mathf.Max(4f, desiredExtraStaminaSize), Time.deltaTime * 10f), extraBarStamina.sizeDelta.y);
			extraBarStamina.gameObject.SetActive(extraBarStamina.sizeDelta.x > 6.1f);
			extraBarOutline.sizeDelta = new Vector2(extraOutlineMaxWidth, extraBarOutline.sizeDelta.y);
			if (!shouldShowExtraBar && !sequencingExtraBar)
			{
				sequencingExtraBar = true;
				extraBar.sizeDelta = new Vector2(extraBar.sizeDelta.x, 0f); DisableExtraBar();
			}
		}
	}

	private void DisableExtraBar()
	{
		extraBar.gameObject.SetActive(value: false);
		sequencingExtraBar = false;
	}

	private static void RemoveAllScriptsExcept<T1, T2>(GameObject gameObject)
	{
		MonoBehaviour[] components = gameObject.GetComponents<MonoBehaviour>();
		foreach (MonoBehaviour component in components)
		{
			if (component.GetType() != typeof(T1) && component.GetType() != typeof(T2))
			{
				DestroyImmediate(component);
			}
		}
	}

	private static void RemoveCopiedStaminaInfo(GameObject gameObject)
	{
		Transform[] descendants = gameObject.GetComponentsInChildren<Transform>(includeInactive: true);
		foreach (Transform descendant in descendants)
		{
			if (descendant != gameObject.transform && descendant.name == "StaminaInfo")
			{
				DestroyImmediate(descendant.gameObject);
			}
		}
	}

	internal void AddCharacterBarAffliction(CharacterBarAffliction characterBarAffliction)
	{
		characterBarAfflictions.Add(characterBarAffliction);
		if (characterBarAffliction.isPetrify)
		{
			petrifyAffliction = characterBarAffliction;
		}
	}

	private void CreateInventorySlots()
	{
		float slotY = HeaderRowHeight * 0.5f;
		for (int i = 0; i < 3; i++)
		{
			InventorySlotUI slot = CreateInventorySlot($"InvSlot_{i}", new Vector2(32f, 32f), new Vector2(InventorySlotsBaseX + i * 38, slotY));
			inventorySlotUIs.Add(slot);
		}
		heldItemSlot = CreateInventorySlot("HoldingItemSlot", new Vector2(38f, 38f), new Vector2(InventorySlotsBaseX + 114f, slotY));

		for (int i = 0; i < 4; i++)
		{
			InventorySlotUI slot = CreateInventorySlot($"BackpackSlot_{i}", new Vector2(32f, 32f), new Vector2(InventorySlotsBaseX + 170f + i * 38, slotY));
			backpackSlotUIs.Add(slot);
		}
	}

	private void EnsureInventorySlotsCreated()
	{
		if (inventorySlotsCreated)
		{
			return;
		}

		CreateInventorySlots();
		inventorySlotsCreated = true;
	}

	private void UpdateInventorySlots()
	{
		if (ObservedCharacter?.player?.itemSlots == null)
		{
			ClearInventorySlots();
			return;
		}
		ItemSlot[] slots = ObservedCharacter.player.itemSlots;
		Optionable<byte> selectedSlot = ObservedCharacter.refs.items.currentSelectedSlot;
		for (int i = 0; i < inventorySlotUIs.Count && i < slots.Length; i++)
		{
			bool isSelected = selectedSlot.IsSome && selectedSlot.Value == i;
			inventorySlotUIs[i].SetItem(slots[i], isSelected);
		}
		Item? heldItem = ObservedCharacter.data.currentItem;
		if (heldItemSlot != null)
		{
			bool hasHeldItem = heldItem != null && heldItem.UIData != null;
			heldItemSlot.SetItem(heldItem, heldItem?.data, selected: hasHeldItem);
		}

		if (!ObservedCharacter.player.backpackSlot.IsEmpty())
		{
			ObservedCharacter.player.backpackSlot.data.TryGetDataEntry<BackpackData>(DataEntryKey.BackpackData, out var backpackData);
			ItemSlot[] backpackSlots = backpackData.itemSlots;

			for (int i = 0; i < backpackSlotUIs.Count && i < backpackSlots.Length; i++)
			{
				backpackSlotUIs[i].SetItem(backpackSlots[i], selected: false);
			}
		}
		else
		{
			foreach (InventorySlotUI slot in backpackSlotUIs)
			{
				slot.Clear();
			}
		}
	}

	private void ClearInventorySlots()
	{
		foreach (InventorySlotUI slot in inventorySlotUIs)
		{
			slot.Clear();
		}

		heldItemSlot?.Clear();
		foreach (InventorySlotUI slot in backpackSlotUIs)
		{
			slot.Clear();
		}
	}

	private void ApplyTeammateVisualOffset()
	{
		float desiredYOffset = GetTeammateVisualYOffset();
		float delta = desiredYOffset - appliedTeammateVisualYOffset;
		if (Mathf.Abs(delta) < 0.001f)
		{
			return;
		}

		appliedTeammateVisualYOffset = desiredYOffset;
		for (int i = 0; i < transform.childCount; i++)
		{
			if (transform.GetChild(i) is RectTransform child)
			{
				if (child == headerRowRectTransform)
				{
					continue;
				}

				child.anchoredPosition += new Vector2(0f, delta);
			}
		}
		UpdateHeaderRowPosition();
	}

	private static float GetTeammateVisualYOffset()
	{
		return DefaultTeammateVisualYOffset + InterpolateScaleSetting(10f, -1f, -21f);
	}

	private void ApplyConfiguredScale()
	{
		if (!isEnabled || animateDisableCoroutine != null)
		{
			return;
		}

		float scale = GetConfiguredScale();
		if (Mathf.Abs(transform.localScale.x - scale) < 0.001f)
		{
			return;
		}

		transform.DOKill();
		transform.localScale = Vector3.one * scale;
	}

	private void ApplyExtraBarScaleCompensation()
	{
		if (extraBar == null || fullBarRectTransform == null || extraBarDefaultVisualGap <= 0f)
		{
			return;
		}

		float visualGap = Mathf.Max(0f, extraBarDefaultVisualGap + InterpolateScaleSetting(-3f, 0f, 10f));
		float localGap = visualGap / Mathf.Max(0.001f, GetConfiguredScale());
		extraBar.anchoredPosition = new Vector2(
			extraBar.anchoredPosition.x,
			fullBarRectTransform.anchoredPosition.y - localGap);
	}

	internal static float GetConfiguredScale()
	{
		return Mathf.Clamp(PluginConfig.TeammateStaminaBarScale.Value, 0.2f, 1f);
	}

	internal static float GetGroupSpacing()
	{
		return Mathf.Max(0f, InterpolateScaleSetting(26.8f, 38f, 65.8f));
	}

	private static float InterpolateScaleSetting(float valueAtScaleLow, float valueAtDefaultScale, float valueAtScaleHigh)
	{
		float scale = GetConfiguredScale();
		float lowWeight =
			(scale - DefaultTeammateScale) *
			(scale - SampleScaleHigh) /
			((SampleScaleLow - DefaultTeammateScale) * (SampleScaleLow - SampleScaleHigh));
		float defaultWeight =
			(scale - SampleScaleLow) *
			(scale - SampleScaleHigh) /
			((DefaultTeammateScale - SampleScaleLow) * (DefaultTeammateScale - SampleScaleHigh));
		float highWeight =
			(scale - SampleScaleLow) *
			(scale - DefaultTeammateScale) /
			((SampleScaleHigh - SampleScaleLow) * (SampleScaleHigh - DefaultTeammateScale));

		return valueAtScaleLow * lowWeight + valueAtDefaultScale * defaultWeight + valueAtScaleHigh * highWeight;
	}

	private InventorySlotUI CreateInventorySlot(string name, Vector2 size, Vector2 position)
	{
		GameObject slotGO = new GameObject(name, typeof(RectTransform));
		slotGO.transform.SetParent(headerRowRectTransform, worldPositionStays: false);
		RectTransform rt = slotGO.GetComponent<RectTransform>();
		rt.sizeDelta = size;
		Vector2 anchorMin = rt.anchorMax = Vector2.zero;
		rt.anchorMin = anchorMin;
		rt.pivot = new Vector2(0f, 0.5f);
		rt.anchoredPosition = position;
		InventorySlotUI slotUI = slotGO.AddComponent<InventorySlotUI>();
		slotUI.Initialize();
		return slotUI;
	}

	private void UpdateStaminaBar()
	{
		if (ObservedCharacter == null)
		{
			Plugin.Logger.LogDebug("[UpdateStaminaBar] observedCharacter not found");
			return;
		}
		foreach (CharacterBarAffliction characterBarAffliction in characterBarAfflictions)
		{
			characterBarAffliction.FetchDesiredSize();
			characterBarAffliction.UpdateVisual();
		}
		float targetWidth = Mathf.Max(0f, ObservedCharacter.data.currentStamina * fullBarRectTransform.sizeDelta.x + staminaBarOffset);
		if (ObservedCharacter.data.currentStamina <= 0.005f && !outOfStamina)
		{
			outOfStamina = true;
			OutOfStaminaPulse();
		}
		else if (ObservedCharacter.data.currentStamina > 0.005f)
		{
			outOfStamina = false;
		}
		float lerpSpeed = Time.deltaTime * 10f;
		staminaBarRectTransform.sizeDelta = new Vector2(Mathf.Lerp(staminaBarRectTransform.sizeDelta.x, targetWidth, lerpSpeed), staminaBarRectTransform.sizeDelta.y);
		float glowAlpha = Mathf.Clamp01((staminaBarRectTransform.sizeDelta.x - targetWidth) * 0.5f);
		sinTime += lerpSpeed * glowAlpha;
		Color gCol = glowImage.color;
		gCol.a = glowAlpha * 0.4f - Mathf.Abs(Mathf.Sin(sinTime)) * 0.2f;
		glowImage.color = gCol;
		float maxWidth = Mathf.Max(0f, ObservedCharacter.GetMaxStamina() * fullBarRectTransform.sizeDelta.x + staminaBarOffset);
		maxStaminaBarRectTransform.sizeDelta = new Vector2(Mathf.Lerp(maxStaminaBarRectTransform.sizeDelta.x, maxWidth, lerpSpeed), maxStaminaBarRectTransform.sizeDelta.y);
		float statusSum = ObservedCharacter.refs.afflictions.statusSum;
		staminaBarOutlineRectTransform.sizeDelta = new Vector2(14f + Mathf.Max(1f, statusSum) * fullBarRectTransform.sizeDelta.x, staminaBarOutlineRectTransform.sizeDelta.y);
		staminaBarOutlineOverflowBar.gameObject.SetActive(statusSum > 1.005f);
		staminaBarRectTransform.gameObject.SetActive(staminaBarRectTransform.sizeDelta.x > minStaminaBarWidth);
		maxStaminaBarRectTransform.gameObject.SetActive(maxStaminaBarRectTransform.sizeDelta.x > minStaminaBarWidth);
		if (sinTime > TAU)
		{
			sinTime -= TAU;
		}
		shieldIcon.SetActive(GameRef.GetIsInvincible(ObservedCharacter.data));
		campfireIcon.SetActive(!ObservedCharacter.refs.afflictions.canGetHungry);
	}

	private void OutOfStaminaPulse()
	{
		barImage.color = outOfStaminaColor;
		barImage.color = defaultBarColor;
	}

	private IEnumerator EnableIEnumerator()
	{
		base.transform.localScale = Vector3.zero;
		base.transform.DOScale(GetConfiguredScale(), 0.5f).SetEase(Ease.OutElastic);
		yield return new WaitForSeconds(0.5f);
		animateDisableCoroutine = null;
	}

	private IEnumerator DisableIEnumerator()
	{
		base.transform.localScale = Vector3.one * GetConfiguredScale();
		base.transform.DOScale(0f, 0.5f).SetEase(Ease.InExpo);
		yield return new WaitForSeconds(0.5f);
		base.gameObject.SetActive(value: false);
		animateDisableCoroutine = null;
	}
}
