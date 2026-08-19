using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace TacticalSoccer.VFX
{
    /// <summary>
    /// Throwaway impact effects, built from primitives because the project has
    /// no art assets yet. Nothing here holds gameplay state: every effect is a
    /// GameObject that expands, fades and deletes itself.
    ///
    /// Everything runs on unscaled time on purpose. The one effect that exists
    /// so far fires at the instant a duel freezes the match at timeScale 0, so
    /// a scaled animation would sit frozen at its first frame and never be seen.
    /// </summary>
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

        // White-hot sparks for an ordinary duel, and a saturated gold for a
        // natural 20. Kept clearly apart: the whole job of the critical burst is
        // to be unmistakable from the match camera at a glance.
        private static readonly Color ClashSparkColor = new Color(1f, 0.97f, 0.8f, 1f);
        private static readonly Color CriticalBurstColor = new Color(1f, 0.78f, 0.15f, 1f);

        public static VFXManager Instance { get; private set; }

        private void Awake()
        {
            Instance = this;
        }

        private void OnDisable()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>
        /// Assigned by the scene generator, which persists the material as an
        /// asset. A purely in-memory material would come back null (pink) after
        /// a domain reload.
        /// </summary>
        public void ConfigureImpactMaterial(Material material)
        {
            impactMaterial = material;
        }

        /// <summary>
        /// Expanding ring of light at the point of contact: a sphere that grows
        /// and fades out over <see cref="impactDuration"/>, then deletes itself.
        /// </summary>
        public void PlayClashImpact(Vector3 position)
        {
            GameObject wave = GameObject.CreatePrimitive(PrimitiveType.Sphere);

            wave.name = "Clash Impact";
            wave.transform.position = position;
            wave.transform.localScale = Vector3.one * impactStartScale;

            // Decoration only: a collider here would deflect the ball, trip a
            // player's trigger and catch the route-drawing raycast.
            if (wave.TryGetComponent(out Collider collider))
            {
                Destroy(collider);
            }

            Renderer renderer = wave.GetComponent<Renderer>();

            // .material, not .sharedMaterial: each wave fades its own alpha, and
            // writing that to the shared asset would fade every future one out
            // before it started.
            renderer.material = impactMaterial != null
                ? new Material(impactMaterial)
                : CreateFallbackMaterial();

            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            StartCoroutine(ExpandAndFade(wave, renderer.material));
        }

        /// <summary>
        /// Sparks at the point of contact for an ordinary duel: a short, tight
        /// spray of small white-hot points.
        ///
        /// Kept deliberately cheap and small. This fires on EVERY duel, and 7v7
        /// produces a lot of them — anything with real presence would stop being
        /// punctuation and start being noise.
        /// </summary>
        public void PlayClashHit(Vector3 position)
        {
            SpawnBurst("Clash Sparks", position, ClashSparkColor,
                count: 24, speed: 6f, size: 0.18f, lifetime: 0.45f);
        }

        /// <summary>
        /// The natural 20: a gold explosion several times the size of the
        /// ordinary one, thrown wider and held longer.
        ///
        /// This is now the whole of the critical's presentation together with
        /// the camera kick — it used to be carried by a 5.6 s audio fanfare that
        /// buried the duel and ran on into the next passage of play.
        /// </summary>
        public void PlayCriticalBurst(Vector3 position)
        {
            SpawnBurst("Critical Burst", position, CriticalBurstColor,
                count: 120, speed: 14f, size: 0.5f, lifetime: 1.1f);
        }

        /// <summary>
        /// Builds a one-shot particle burst from scratch and lets it delete
        /// itself. Built in code rather than from a prefab for the same reason
        /// as everything else here: the project has no art assets.
        ///
        /// Unscaled time throughout, and that is not a detail. Both callers fire
        /// while a duel has the match frozen at timeScale 0, so a system on
        /// scaled time would be emitted and then sit motionless at its first
        /// frame — present in the hierarchy and never seen. Same trap the impact
        /// wave already had to dodge.
        /// </summary>
        private void SpawnBurst(string name, Vector3 position, Color color,
            int count, float speed, float size, float lifetime)
        {
            GameObject holder = new GameObject(name);
            holder.transform.position = position;

            // Built INACTIVE, and this is not tidiness. A ParticleSystem added
            // to a live GameObject starts playing on the spot, and a playing
            // system refuses changes to its duration — while calling Stop() to
            // get around that leaves it in a stopped state that fires
            // stopAction on the next frame. That combination destroyed the
            // burst two frames in, before it had emitted a single particle.
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

            // A sphere with radius 0 throws everything outward from the single
            // point of contact, which is what a hit looks like; a volume would
            // read as a cloud that happened to be there already.
            ParticleSystem.ShapeModule shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.01f;

            // Fades each spark out on its own clock rather than dimming the
            // whole burst, so the spray thins instead of dipping.
            ParticleSystem.ColorOverLifetimeModule fade = particles.colorOverLifetime;
            fade.enabled = true;
            fade.color = new ParticleSystem.MinMaxGradient(BuildFadeGradient(color));

            ParticleSystemRenderer renderer = holder.GetComponent<ParticleSystemRenderer>();
            renderer.material = CreateFallbackMaterial();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            holder.SetActive(true);
            particles.Play();

            // Cleaned up on a REALTIME timer. Object.Destroy's delay runs on
            // scaled time, so the one-liner version would never come due while
            // a duel had the match frozen — which is the only moment either of
            // these is ever fired.
            StartCoroutine(DestroyAfterRealtime(holder, lifetime * 2f));
        }

        private IEnumerator DestroyAfterRealtime(GameObject target, float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);

            if (target != null)
            {
                Destroy(target);
            }
        }

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

            // The renderer took a private copy of the material; nothing else
            // will ever collect it once its GameObject is gone.
            if (material != null)
            {
                Destroy(material);
            }
        }

        /// <summary>
        /// Last resort when no material asset was handed over. Unlit rather than
        /// lit: a shockwave is emissive by nature, and shading it by the sun
        /// angle would leave the underside of the ring dark.
        /// </summary>
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

            // URP ships its shaders opaque by default; without flipping the
            // surface type the alpha above does nothing at all.
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
