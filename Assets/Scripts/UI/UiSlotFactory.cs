using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace TacticalSoccer.UI
{
    // Crea slots de jugador clicables reutilizables: botón con fondo de color y etiqueta centrada.
    public static class UiSlotFactory
    {
        // Construye un slot con botón, fondo e etiqueta de texto, y engancha el click.
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

            button.onClick.AddListener(PlayClick);

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

        // Reproduce el sonido de click del botón.
        private static void PlayClick()
        {
            if (TacticalSoccer.Audio.AudioManager.Instance != null)
            {
                TacticalSoccer.Audio.AudioManager.Instance.PlayClick();
            }
        }
    }
}
