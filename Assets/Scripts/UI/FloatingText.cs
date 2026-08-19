using UnityEngine;

namespace TacticalSoccer.UI
{
    /// <summary>
    /// One piece of combat text: a 3D <see cref="TextMesh"/> that rises off the
    /// pitch, fades out and deletes itself. Spawned by
    /// <see cref="FloatingTextManager"/>, which is the only thing that knows how
    /// to build one.
    ///
    /// EVERYTHING here runs on unscaled time. A duel resolving is not the only
    /// thing that can be on screen while the clock is stopped: the whistle and a
    /// set piece both leave timeScale at 0 with these still in the air, and on
    /// scaled time the text would then neither rise, nor fade, nor ever reach
    /// its own lifetime — it would simply hang over the player's head until
    /// something else started the world again. Same trap the VFX shockwave
    /// already fell into.
    /// </summary>
    [RequireComponent(typeof(TextMesh))]
    public class FloatingText : MonoBehaviour
    {
        public float duration = 1.2f;
        public float floatSpeed = 2f;

        [Tooltip("Share of the lifetime held at full opacity before the fade " +
                 "starts. Fading from the very first frame makes a 1.2 s message " +
                 "read as already half gone by the time the eye finds it.")]
        [SerializeField] private float holdFraction = 0.35f;

        private TextMesh textMesh;
        private Color baseColor;
        private float elapsed;

        private void Awake()
        {
            textMesh = GetComponent<TextMesh>();

            if (textMesh != null)
            {
                baseColor = textMesh.color;
            }
        }

        /// <summary>
        /// Sets what this text says and how long it lives. Called by the manager
        /// straight after the component is added, so the colour captured in Awake
        /// is refreshed here rather than trusted.
        /// </summary>
        public void Configure(string message, Color color, float lifetime)
        {
            if (textMesh == null)
            {
                textMesh = GetComponent<TextMesh>();
            }

            if (textMesh != null)
            {
                textMesh.text = message;
                textMesh.color = color;
            }

            baseColor = color;
            duration = Mathf.Max(0.01f, lifetime);
            elapsed = 0f;
        }

        private void Update()
        {
            float delta = Time.unscaledDeltaTime;

            elapsed += delta;
            transform.position += Vector3.up * (floatSpeed * delta);

            if (elapsed >= duration)
            {
                Destroy(gameObject);
                return;
            }

            if (textMesh == null)
            {
                return;
            }

            // Alpha rides the vertex colours of the shared font material, so
            // every instance fades on its own without any material being copied.
            float fadeProgress = Mathf.InverseLerp(duration * holdFraction, duration, elapsed);

            Color color = baseColor;
            color.a = baseColor.a * (1f - fadeProgress);
            textMesh.color = color;
        }

        /// <summary>
        /// Late, not Update: the duel camera has already been moved for this
        /// frame, so the text faces the pose actually being rendered instead of
        /// the previous one — which, during the swoop into a clash, is a
        /// noticeably different direction.
        /// </summary>
        private void LateUpdate()
        {
            Transform viewpoint = CameraSystem.TacticalCamera.Instance != null
                ? CameraSystem.TacticalCamera.Instance.transform
                : (UnityEngine.Camera.main != null ? UnityEngine.Camera.main.transform : null);

            if (viewpoint == null)
            {
                return;
            }

            transform.rotation = viewpoint.rotation;
        }
    }
}
