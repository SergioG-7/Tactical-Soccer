using UnityEngine;

namespace TacticalSoccer.UI
{
    // Ajusta el RectTransform a la zona segura de la pantalla (evita el notch, la cámara y la barra de gestos).
    [RequireComponent(typeof(RectTransform))]
    public class SafeAreaFitter : MonoBehaviour
    {
        private RectTransform rect;

        private Rect appliedSafeArea;
        private int appliedWidth;
        private int appliedHeight;

        // Aplica la zona segura al arrancar.
        private void Awake()
        {
            rect = GetComponent<RectTransform>();

            Apply();
        }

        // Comprueba si la zona segura o el tamaño de pantalla han cambiado, y reaplica el ajuste si es así.
        private void Update()
        {
            if (Screen.safeArea == appliedSafeArea
                && Screen.width == appliedWidth
                && Screen.height == appliedHeight)
            {
                return;
            }

            Apply();
        }

        // Convierte la zona segura en fracciones de pantalla y las aplica como anclas del RectTransform.
        private void Apply()
        {
            if (rect == null)
            {
                return;
            }

            Rect safeArea = Screen.safeArea;

            int width = Screen.width;
            int height = Screen.height;

            if (width <= 0 || height <= 0)
            {
                return;
            }

            appliedSafeArea = safeArea;
            appliedWidth = width;
            appliedHeight = height;

            Vector2 min = safeArea.position;
            Vector2 max = safeArea.position + safeArea.size;

            min.x /= width;
            min.y /= height;
            max.x /= width;
            max.y /= height;

            rect.anchorMin = min;
            rect.anchorMax = max;

            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
