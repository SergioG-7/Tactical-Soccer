using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace TacticalSoccer.VFX
{
    // Efectos visuales de impacto, construidos con primitivas. Corren en tiempo no escalado para verse aunque el partido esté congelado.
    public class VFXManager : MonoBehaviour
    {
        [Header("Onda de impacto")]
        [Tooltip("Semi-transparent material for the shockwave. Written as a real " +
                 "asset by the scene generator; a runtime fallback is built if it " +
                 "is missing, so a scene generated before this existed still runs.")]
        [SerializeField] private Material impactMaterial;

        [SerializeField] private float impactStartScale = 1f;
        [SerializeField] private float impactEndScale = 5f;
        [SerializeField] private float impactDuration = 0.3f;

        private static readonly Color ImpactColor = new Color(1f, 0.93f, 0.35f, 0.75f);

        // Chispas blancas para un duelo normal, doradas para un crítico.
        private static readonly Color ClashSparkColor = new Color(1f, 0.97f, 0.8f, 1f);
        private static readonly Color CriticalBurstColor = new Color(1f, 0.78f, 0.15f, 1f);

        public static VFXManager Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        private void OnDisable()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        // Asigna el material de la onda de impacto.
        public void ConfigureImpactMaterial(Material material)
        {
            impactMaterial = material;
        }

        // Crea una esfera que se expande y se desvanece en el punto de contacto, y se autodestruye.
        public void PlayClashImpact(Vector3 position)
        {
            GameObject wave = GameObject.CreatePrimitive(PrimitiveType.Sphere);

            wave.name = "Clash Impact";
            wave.transform.position = position;
            wave.transform.localScale = Vector3.one * impactStartScale;

            // Es solo decoración, no debe tener collider.
            if (wave.TryGetComponent(out Collider collider))
            {
                Destroy(collider);
            }

            Renderer renderer = wave.GetComponent<Renderer>();

            // Se usa .material (instancia propia) porque cada onda desvanece su propio alfa.
            renderer.material = impactMaterial != null
                ? new Material(impactMaterial)
                : CreateFallbackMaterial();

            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            StartCoroutine(ExpandAndFade(wave, renderer.material));
        }

        // Lanza una pequeña ráfaga de chispas blancas para un duelo normal.
        public void PlayClashHit(Vector3 position)
        {
            SpawnBurst("Clash Sparks", position, ClashSparkColor,
                count: 24, speed: 6f, size: 0.18f, lifetime: 0.45f);
        }

        // Lanza una explosión dorada más grande para un golpe crítico.
        public void PlayCriticalBurst(Vector3 position)
        {
            SpawnBurst("Critical Burst", position, CriticalBurstColor,
                count: 120, speed: 14f, size: 0.5f, lifetime: 1.1f);
        }

        // Crea un sistema de partículas de una sola ráfaga y lo destruye solo al terminar.
        private void SpawnBurst(string name, Vector3 position, Color color,
            int count, float speed, float size, float lifetime)
        {
            GameObject holder = new GameObject(name);
            holder.transform.position = position;

            // Se crea inactivo para poder configurar el ParticleSystem antes de que empiece a reproducirse.
            holder.SetActive(false);

            ParticleSystem particles = holder.AddComponent<ParticleSystem>();

            ParticleSystem.MainModule main = particles.main;
            main.duration = lifetime;
            main.loop = false;
            main.startLifetime = lifetime;
            main.startSpeed = speed;
            main.startSize = size;
            main.startColor = color;
            main.gravityModifier = 0.6f;
            main.useUnscaledTime = true;
            main.playOnAwake = false;

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });

            // Radio 0: todo sale disparado desde el mismo punto de contacto.
            ParticleSystem.ShapeModule shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.01f;

            // Cada chispa se desvanece por su cuenta según su propia vida.
            ParticleSystem.ColorOverLifetimeModule fade = particles.colorOverLifetime;
            fade.enabled = true;
            fade.color = new ParticleSystem.MinMaxGradient(BuildFadeGradient(color));

            ParticleSystemRenderer renderer = holder.GetComponent<ParticleSystemRenderer>();
            renderer.material = CreateFallbackMaterial();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            holder.SetActive(true);
            particles.Play();

            // Se destruye con un temporizador en tiempo real, ya que Destroy con delay usa tiempo escalado.
            StartCoroutine(DestroyAfterRealtime(holder, lifetime * 2f));
        }

        // Destruye el objeto tras el tiempo indicado, en tiempo real (no escalado).
        private IEnumerator DestroyAfterRealtime(GameObject target, float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);

            if (target != null)
            {
                Destroy(target);
            }
        }

        // Construye el degradado de opacidad usado para desvanecer las chispas.
        private static Gradient BuildFadeGradient(Color color)
        {
            Gradient gradient = new Gradient();

            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(color, 0f),
                    new GradientColorKey(color, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, 0.55f),
                    new GradientAlphaKey(0f, 1f)
                });

            return gradient;
        }

        // Expande la onda de impacto y desvanece su alfa hasta destruirla.
        private IEnumerator ExpandAndFade(GameObject wave, Material material)
        {
            float elapsed = 0f;
            Color color = material.color;

            while (elapsed < impactDuration && wave != null)
            {
                elapsed += Time.unscaledDeltaTime;

                float progress = Mathf.Clamp01(elapsed / impactDuration);

                wave.transform.localScale =
                    Vector3.one * Mathf.Lerp(impactStartScale, impactEndScale, progress);

                color.a = Mathf.Lerp(ImpactColor.a, 0f, progress);
                material.color = color;

                yield return null;
            }

            if (wave != null)
            {
                Destroy(wave);
            }

            // Se destruye el material propio, ya que nadie más lo referencia.
            if (material != null)
            {
                Destroy(material);
            }
        }

        // Crea un material unlit de reserva cuando no se ha asignado ninguno.
        private static Material CreateFallbackMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");

            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            Material material = new Material(shader)
            {
                name = "ImpactWaveMaterial (runtime)",
                color = ImpactColor
            };

            // Hay que forzar el modo transparente, URP usa opaco por defecto.
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;

            return material;
        }
    }
}
