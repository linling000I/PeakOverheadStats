using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace PeakOverheadStats.MonoBehaviours;

public class InventorySlotUI : MonoBehaviour
{
	private const float SelectedTargetScale = 1.1f;
	private const float SelectedEnvelopeScale = 1.38f;
	private const float SelectedElasticAmplitude = 1.70158f;
	private const float SelectedElasticPeriod = 0f;

	private RectTransform rectTransform = null!;

	private RawImage icon = null!;

	private GameObject? fuelBar;

	private Image? fuelBarFill;

	private Item? currentItem;

	private ItemInstanceData? currentData;

	private Vector2 startingSizeDelta;

	private float visualScale = 1f;

	private Tween? selectionTween;

	private bool selectionInitialized;

	private bool isSelected;

	private bool cookedStateInitialized;

	private bool hasCookedData;

	private int cookedValue;

	private bool fuelStateInitialized;

	private bool fuelBarVisible;

	private float fuelValue = 1f;

	public void Initialize()
	{
		rectTransform = GetComponent<RectTransform>();
		startingSizeDelta = rectTransform.sizeDelta;
		CreateFuelBar();
		icon = CreateIconImage();
		icon.color = new Color(1f, 1f, 1f, 0f);
		icon.raycastTarget = false;
		fuelBar?.transform.SetAsLastSibling();
	}

	public void SetItem(ItemSlot slot, bool selected)
	{
		bool isEmpty = slot.IsEmpty();
		SetItem(isEmpty ? null : slot.prefab, isEmpty ? null : slot.data, selected);
	}

	internal void SetItem(Item? item, ItemInstanceData? data, bool selected)
	{
		if (item?.UIData == null)
		{
			ClearItemVisuals();
			SetSelected(false);
			return;
		}

		bool itemChanged = !ReferenceEquals(currentItem, item) || !ReferenceEquals(currentData, data);
		currentItem = item;
		currentData = data;
		if (itemChanged || !icon.enabled)
		{
			icon.texture = item.UIData.GetIcon();
			icon.enabled = true;
			icon.color = Color.white;
			cookedStateInitialized = false;
			fuelStateInitialized = false;
		}

		UpdateCookedColor();
		UpdateFuelBar();
		SetSelected(selected, replayAnimation: itemChanged && selected);
	}

	public void Clear()
	{
		ClearItemVisuals();
		SetSelected(false);
	}

	private void SetSelected(bool selected, bool replayAnimation = false)
	{
		bool shouldHighlight = selected && currentItem != null;
		if (!replayAnimation && selectionInitialized && isSelected == shouldHighlight)
		{
			return;
		}

		selectionInitialized = true;
		isSelected = shouldHighlight;
		selectionTween?.Kill();
		if (shouldHighlight)
		{
			selectionTween = DOTween.To(
				() => visualScale,
				ApplyVisualScale,
				SelectedTargetScale,
				0.5f).SetEase(Ease.OutElastic, SelectedElasticAmplitude, SelectedElasticPeriod);
		}
		else if (currentItem != null)
		{
			selectionTween = DOTween.To(
				() => visualScale,
				ApplyVisualScale,
				1f,
				0.2f).SetEase(Ease.OutCubic);
		}
		else
		{
			ApplyVisualScale(1f);
		}
	}

	private void ApplyVisualScale(float value)
	{
		visualScale = Mathf.Clamp(value, 1f, SelectedEnvelopeScale);
		rectTransform.sizeDelta = startingSizeDelta * visualScale;
	}

	private void UpdateCookedColor()
	{
		ItemInstanceData? itemInstanceData = currentData;
		bool hasCookedValue = false;
		int nextCookedValue = 0;
		if (itemInstanceData != null &&
			itemInstanceData.TryGetDataEntry<IntItemData>(DataEntryKey.CookedAmount, out var cookedData))
		{
			hasCookedValue = true;
			nextCookedValue = cookedData.Value;
		}
		if (cookedStateInitialized && hasCookedData == hasCookedValue && cookedValue == nextCookedValue)
		{
			return;
		}

		cookedStateInitialized = true;
		hasCookedData = hasCookedValue;
		cookedValue = nextCookedValue;
		icon.color = hasCookedValue ? ItemCooking.GetCookColor(nextCookedValue) : Color.white;
	}

	private void UpdateFuelBar()
	{
		if (fuelBar == null)
		{
			return;
		}
		ItemInstanceData? itemInstanceData = currentData;
		bool shouldShowFuel = false;
		float nextFuelValue = 1f;
		if (itemInstanceData != null &&
			itemInstanceData.TryGetDataEntry<FloatItemData>(DataEntryKey.UseRemainingPercentage, out var fuelData))
		{
			shouldShowFuel = true;
			nextFuelValue = fuelData.Value;
		}
		if (!fuelStateInitialized || fuelBarVisible != shouldShowFuel)
		{
			fuelBar.SetActive(shouldShowFuel);
		}
		if (fuelBarFill != null &&
			(!fuelStateInitialized || fuelBarVisible != shouldShowFuel || !Mathf.Approximately(fuelValue, nextFuelValue)))
		{
			fuelBarFill.fillAmount = nextFuelValue;
		}

		fuelStateInitialized = true;
		fuelBarVisible = shouldShowFuel;
		fuelValue = nextFuelValue;
	}

	private void ClearItemVisuals()
	{
		if (currentItem == null && currentData == null && !icon.enabled &&
			(fuelBar == null || !fuelBar.activeSelf))
		{
			return;
		}

		icon.enabled = false;
		icon.texture = null;
		icon.color = new Color(1f, 1f, 1f, 0f);
		currentItem = null;
		currentData = null;
		cookedStateInitialized = false;
		hasCookedData = false;
		cookedValue = 0;
		fuelStateInitialized = false;
		fuelBarVisible = false;
		fuelValue = 1f;
		if (fuelBar != null)
		{
			fuelBar.SetActive(false);
		}
		if (fuelBarFill != null)
		{
			fuelBarFill.fillAmount = 1f;
		}
	}

	private RawImage CreateIconImage()
	{
		GameObject iconObject = new GameObject("Icon", typeof(RectTransform));
		iconObject.transform.SetParent(base.transform, worldPositionStays: false);
		RectTransform iconRect = iconObject.GetComponent<RectTransform>();
		iconRect.anchorMin = Vector2.zero;
		iconRect.anchorMax = Vector2.one;
		iconRect.sizeDelta = Vector2.zero;
		iconRect.anchoredPosition = Vector2.zero;
		return iconObject.AddComponent<RawImage>();
	}

	private void CreateFuelBar()
	{
		GameObject fuelGO = new GameObject("FuelBar", typeof(RectTransform));
		fuelGO.transform.SetParent(base.transform, worldPositionStays: false);
		fuelGO.transform.SetAsFirstSibling();
		RectTransform fuelRT = fuelGO.GetComponent<RectTransform>();
		fuelRT.anchorMin = new Vector2(0f, 0f);
		fuelRT.anchorMax = new Vector2(1f, 0f);
		fuelRT.sizeDelta = new Vector2(0f, 4f);
		fuelRT.anchoredPosition = new Vector2(0f, 2f);

		Image fuelBarBg = fuelGO.AddComponent<Image>();
		fuelBarBg.color = new Color(0f, 0f, 0f, 0.5f);

		Sprite whiteSprite = CreateWhiteSprite();
		fuelBarBg.sprite = whiteSprite;
		fuelBarBg.type = Image.Type.Sliced;

		GameObject fillGO = new GameObject("Fill", typeof(RectTransform));
		fillGO.transform.SetParent(fuelGO.transform, worldPositionStays: false);
		RectTransform fillRT = fillGO.GetComponent<RectTransform>();
		fillRT.anchorMin = Vector2.zero;
		fillRT.anchorMax = Vector2.one;
		fillRT.sizeDelta = Vector2.zero;
		fillRT.anchoredPosition = Vector2.zero;

		fuelBarFill = fillGO.AddComponent<Image>();
		fuelBarFill.color = Color.yellow;
		fuelBarFill.sprite = whiteSprite;
		fuelBarFill.type = Image.Type.Filled;
		fuelBarFill.fillMethod = Image.FillMethod.Horizontal;
		fuelBarFill.fillOrigin = (int)Image.OriginHorizontal.Left;

		fuelBar = fuelGO;
		fuelBar.SetActive(value: false);
	}
	private Sprite CreateWhiteSprite()
	{
		Texture2D tex = new Texture2D(1, 1);
		tex.SetPixel(0, 0, Color.white);
		tex.Apply();
		return Sprite.Create(tex, new Rect(0, 0, 1, 1), Vector2.one * 0.5f);
	}

	private void OnDisable()
	{
		selectionTween?.Kill();
		selectionTween = null;
		ApplyVisualScale(1f);
		selectionInitialized = false;
	}

}
