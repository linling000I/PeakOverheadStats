using UnityEngine;
using UnityEngine.UI;

namespace PeakOverheadStats.MonoBehaviours;

internal sealed class CharacterBarAffliction : MonoBehaviour
{
	internal CharacterAfflictions.STATUSTYPE afflictionType;

	internal bool isPetrify;

	private RectTransform rectTransform = null!;

	private CharacterStaminaBar characterStaminaBar = null!;
	private Image? barImage;

	internal float size;

	internal void FetchReferences()
	{
		rectTransform = (RectTransform)base.transform;
		barImage = base.GetComponent<Image>();
		Transform iconTransform = base.transform.Find("Icon");
		if (iconTransform != null)
		{
			iconTransform.gameObject.SetActive(false);
		}
		characterStaminaBar = GetComponentInParent<CharacterStaminaBar>();
		characterStaminaBar.AddCharacterBarAffliction(this);
		// Set distinct color for Weight (负重) bar - orange
		if (afflictionType == CharacterAfflictions.STATUSTYPE.Weight && barImage != null)
		{
			barImage.color = new Color(1f, 0.55f, 0f, 1f);
		}
	}

	internal void FetchDesiredSize()
	{
		CharacterStaminaBar characterStaminaBar = this.characterStaminaBar;
		Character? character = characterStaminaBar.ObservedCharacter;
		if (!character)
		{
			return;
		}
		float currentStatus = isPetrify
			? character.data.petrifyAmount / 100f
			: character.refs.afflictions.GetCurrentStatus(afflictionType);
		size = this.characterStaminaBar.fullBarRectTransform.sizeDelta.x * currentStatus;
		if (currentStatus > 0.01f)
		{
			if (size < characterStaminaBar.minAfflictionWidth)
			{
				size = characterStaminaBar.minAfflictionWidth;
			}
			base.gameObject.SetActive(value: true);
		}
		else
		{
			base.gameObject.SetActive(value: false);
		}
	}

	internal void UpdateVisual()
	{
		RectTransform rectTransform = this.rectTransform;
		Vector2 sizeDelta = this.rectTransform.sizeDelta;
		sizeDelta.x = Mathf.Lerp(this.rectTransform.sizeDelta.x, size, Mathf.Min(Time.deltaTime * 10f, 0.1f));
		rectTransform.sizeDelta = sizeDelta;
	}
}