using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace TacticalSoccer.UI
{
    /// <summary>
    /// Builds one clickable player slot: a centered, fixed-size button with a
    /// coloured background and a bold centred label.
    ///
    /// Used to be built independently by SubstitutionUIController (the bench
    /// board) and FormationUIController (the captain picker) — same rect
    /// anchoring, same Image+Button+Text structure, differing only in size,
    /// colour, label and click handler. One factory now; each caller still
    /// owns its own colour/label rules and its own list of the GameObjects it
    /// hands back (re-deriving Image/Text from them later with
    /// TryGetComponent, same as before).
    /// </summary>
    public static class UiSlotFactory
    {
        public static GameObject CreateSlot(
            Transform parent,
            string name,
            Vector2 anchoredPosition,
            Vector2 size,
            Color backgroundColor,
            string labelText,
            Font font,
            int fontSize,
            UnityAction onClick)
        {
            GameObject slotObject = new GameObject(name, typeof(RectTransform));
            slotObject.transform.SetParent(parent, false);

            RectTransform rect = (RectTransform)slotObject.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;

            Image background = slotObject.AddComponent<Image>();
            background.color = backgroundColor;

            Button button = slotObject.AddComponent<Button>();
            button.targetGraphic = background;
            button.onClick.AddListener(onClick);

            GameObject labelObject = new GameObject("Label", typeof(RectTransform));
            labelObject.transform.SetParent(slotObject.transform, false);

            RectTransform labelRect = (RectTransform)labelObject.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            Text label = labelObject.AddComponent<Text>();
            label.font = font;
            label.fontSize = fontSize;
            label.fontStyle = FontStyle.Bold;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.black;
            label.text = labelText;

            return slotObject;
        }
    }
}
