using UnityEngine;
using TacticalSoccer.Core;

namespace TacticalSoccer.UI
{
    // Crea los textos flotantes de combate (roll, ventaja, agotado, etc.) sobre los jugadores.
    public class FloatingTextManager : MonoBehaviour
    {
        [Header("Colocación")]
        [Tooltip("Height above the player the first message appears at. Well " +
                 "clear of the role/stamina label, which sits at 2.5 and is " +
                 "about a unit tall.")]
        [SerializeField] private float baseHeight = 4.5f;

        [Tooltip("Vertical gap between stacked messages. Three can land on one " +
                 "player in the same duel — the roll, VENTAJA and AGOTADO — and " +
                 "the height offset is the only thing keeping them apart.")]
        [SerializeField] private float stackSpacing = 1f;

        [Tooltip("Sideways scatter, so two messages spawned on the same tick at " +
                 "the same height do not sit exactly on top of each other.")]
        [SerializeField] private float horizontalJitter = 0.5f;

        [Header("Tipografía")]
        [Tooltip("Assigned by the scene generator. Left null the manager falls " +
                 "back to the built-in runtime font.")]
        [SerializeField] private Font font;

        [Tooltip("Rendered glyph resolution. High with a small character size " +
                 "is what keeps a world-space TextMesh from going blurry the " +
                 "moment the duel camera gets close.")]
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
