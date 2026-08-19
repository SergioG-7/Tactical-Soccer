using UnityEngine;

namespace TacticalSoccer.UI
{
    /// <summary>
    /// Builds the floating combat text. Nothing else in the game knows how a
    /// <see cref="TextMesh"/> is put together — callers just say what happened,
    /// where, and in what colour.
    ///
    /// A 3D TextMesh rather than a world-space Canvas on purpose: the built-in
    /// font material is drawn with ZTest Always, so the message is never
    /// swallowed by a player capsule, the netting or the stands it happens to
    /// be standing in front of.
    /// </summary>
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

        /// <summary>
        /// Self-healing: a scene generated before this system existed has no
        /// manager in it, and a missing one must not mean silently losing every
        /// duel readout. Creates its own host on first use instead.
        /// </summary>
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

        /// <summary>Assigned by the scene generator, which owns font choices.</summary>
        public void ConfigureFont(Font uiFont)
        {
            font = uiFont;
        }

        /// <summary>
        /// Drops a message above <paramref name="worldPos"/>, at the first
        /// stacking level.
        /// </summary>
        public void SpawnText(Vector3 worldPos, string message, Color color)
        {
            SpawnText(worldPos, message, color, 0);
        }

        /// <summary>
        /// Same, at stacking level <paramref name="stackLevel"/>. Level 0 sits
        /// at <see cref="baseHeight"/>, level 1 one <see cref="stackSpacing"/>
        /// above it, and so on — which is what lets a single duel put the roll,
        /// VENTAJA and AGOTADO on one player without them overprinting.
        /// </summary>
        public void SpawnText(Vector3 worldPos, string message, Color color, int stackLevel)
        {
            SpawnText(worldPos, message, color, stackLevel, 1f);
        }

        /// <summary>
        /// Same again, at <paramref name="scale"/> times the normal size. Exists
        /// for the shouts that have to carry from the match camera rather than
        /// from the duel one — a critical is a rare enough event that reading it
        /// should not depend on already being zoomed in on the player.
        /// </summary>
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
            // Character size rather than the transform's scale: a TextMesh built
            // at a bigger character size renders its glyphs at that size, while
            // scaling the object up just magnifies the atlas and goes soft.
            textMesh.characterSize = characterSize * Mathf.Max(0.01f, scale);
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.alignment = TextAlignment.Center;
            textMesh.text = message;
            textMesh.color = color;

            // A TextMesh created from script has no material: the font's own is
            // what carries the glyph atlas, and without it the text renders as a
            // magenta rectangle.
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

        private Font ResolveFont()
        {
            if (font != null)
            {
                return font;
            }

            // Arial.ttf stopped being a built-in in Unity 2022 and now throws;
            // LegacyRuntime.ttf replaced it and ships with the player.
            try
            {
                font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
            catch (System.ArgumentException)
            {
                font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            }

            return font;
        }
    }
}
