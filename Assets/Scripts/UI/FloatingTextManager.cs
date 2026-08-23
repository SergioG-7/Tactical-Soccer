using UnityEngine;
using TacticalSoccer.Core;

namespace TacticalSoccer.UI
{
    // Crea los textos flotantes de combate (roll, ventaja, agotado, etc.) sobre los jugadores.
    public class FloatingTextManager : MonoBehaviour
    {
        [Tooltip("Altura sobre el jugador a la que aparece el primer mensaje flotante.")]
        [SerializeField] private float baseHeight = 4.5f;

        [Tooltip("Separación vertical entre mensajes apilados simultáneos.")]
        [SerializeField] private float stackSpacing = 1f;

        [Tooltip("Variación horizontal aleatoria para evitar la superposición exacta de textos.")]
        [SerializeField] private float horizontalJitter = 0.5f;

        [Tooltip("Fuente tipográfica para el texto (si es nula, usa la predeterminada).")]
        [SerializeField] private Font font;

        [Tooltip("Resolución del renderizado de glifos para evitar textos borrosos.")]
        [SerializeField] private int fontResolution = 100;

        [SerializeField] private float characterSize = 0.08f;
        [SerializeField] private FontStyle fontStyle = FontStyle.Bold;

        [SerializeField] private float lifetime = 1.2f;

        private static FloatingTextManager instance;

        // Devuelve la instancia, creando una nueva si la escena no tiene ninguna.
        public static FloatingTextManager Instance
        {
            get
            {
                if (instance != null)
                {
                    return instance;
                }

                if (!Application.isPlaying)
                {
                    return null;
                }

                instance = FindAnyObjectByType<FloatingTextManager>();

                if (instance == null)
                {
                    GameObject host = new GameObject("FloatingTextManager");
                    instance = host.AddComponent<FloatingTextManager>();
                }

                return instance;
            }
        }

        private void Awake()
        {
            instance = this;
        }

        private void OnDisable()
        {
            if (instance == this)
            {
                instance = null;
            }
        }

        // Asigna la fuente a usar para los textos flotantes.
        public void ConfigureFont(Font uiFont)
        {
            font = uiFont;
        }

        // Muestra un mensaje flotante sobre la posición dada.
        public void SpawnText(Vector3 worldPos, string message, Color color)
        {
            SpawnText(worldPos, message, color, 0);
        }

        // Muestra un mensaje flotante en el nivel de apilado indicado, para no solapar varios mensajes en el mismo jugador.
        public void SpawnText(Vector3 worldPos, string message, Color color, int stackLevel)
        {
            SpawnText(worldPos, message, color, stackLevel, 1f);
        }

        // Muestra un mensaje flotante con un tamaño distinto del normal.
        public void SpawnText(Vector3 worldPos, string message, Color color, int stackLevel, float scale)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            Vector3 spawnPos = worldPos + new Vector3(
                Random.Range(-horizontalJitter, horizontalJitter),
                baseHeight + (stackSpacing * stackLevel),
                0f);

            GameObject textObject = new GameObject("FloatingText");
            textObject.transform.position = spawnPos;

            TextMesh textMesh = textObject.AddComponent<TextMesh>();
            textMesh.font = ResolveFont();
            textMesh.fontSize = fontResolution;
            textMesh.fontStyle = fontStyle;
            textMesh.characterSize = characterSize * Mathf.Max(0.01f, scale);
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.alignment = TextAlignment.Center;
            textMesh.text = message;
            textMesh.color = color;

            // Sin el material de la fuente el texto se vería como un rectángulo magenta.
            MeshRenderer meshRenderer = textObject.GetComponent<MeshRenderer>();

            if (meshRenderer != null && textMesh.font != null)
            {
                meshRenderer.sharedMaterial = textMesh.font.material;
                meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                meshRenderer.receiveShadows = false;
            }

            FloatingText floater = textObject.AddComponent<FloatingText>();
            floater.Configure(message, color, lifetime);
        }

        // Devuelve la fuente asignada, o la fuente por defecto del juego si no hay ninguna.
        private Font ResolveFont()
        {
            if (font != null)
            {
                return font;
            }

            font = LocalizationManager.BuiltInFont;
            return font;
        }
    }
}
