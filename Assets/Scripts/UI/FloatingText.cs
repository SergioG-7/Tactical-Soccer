using UnityEngine;

namespace TacticalSoccer.UI
{
    // Un texto flotante individual: sube, se desvanece y se autodestruye.
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

        // Guarda el color inicial del texto.
        private void Awake()
        {
            textMesh = GetComponent<TextMesh>();

            if (textMesh != null)
            {
                baseColor = textMesh.color;
            }
        }

        // Fija el mensaje, el color y la duración de vida del texto.
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

        // Sube el texto y lo desvanece hasta destruirlo al cumplir su duración.
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

            float fadeProgress = Mathf.InverseLerp(duration * holdFraction, duration, elapsed);

            Color color = baseColor;
            color.a = baseColor.a * (1f - fadeProgress);
            textMesh.color = color;
        }

        // Orienta el texto hacia la cámara actual.
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
