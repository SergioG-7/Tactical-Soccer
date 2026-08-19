using UnityEngine;
using TacticalSoccer.Gameplay;

namespace TacticalSoccer.VFX
{
    /// <summary>
    /// The glow under a player whose side is in the zone.
    ///
    /// One of these rides on every player and switches its own disc on and off.
    /// The alternative — a manager that finds every player of a side the moment
    /// the bar lights — would have to re-find them after every substitution and
    /// would go wrong quietly when it did not.
    ///
    /// The disc is a child of the player rather than an unparented object that
    /// follows: players never rotate in this game, so parenting cannot tip it on
    /// edge, and a child costs nothing per frame while a follower costs a
    /// position write for all fourteen of them.
    /// </summary>
    public class TensionAura : MonoBehaviour
    {
        [Tooltip("Assigned by the scene generator, which owns material assets. " +
                 "Left null the aura builds its own transparent material.")]
        [SerializeField] private Material auraMaterial;

        [Tooltip("Diameter of the disc. Wider than the capsule so it reads as a " +
                 "glow the player is standing in rather than as a hat.")]
        [SerializeField] private float diameter = 2.2f;

        [Tooltip("How fast the aura pulses, in cycles per second.")]
        [SerializeField] private float pulseSpeed = 2.5f;

        [Tooltip("How much the disc swells on each pulse, as a share of its size.")]
        [SerializeField] private float pulseAmount = 0.12f;

        // Just clear of the pitch plane at y=0, so the two never z-fight. The
        // player's transform sits at their CENTRE, a unit up, so the disc has to
        // be pushed back down by that much in local space.
        private const float GroundY = 0.02f;

        private TeamMember member;
        private GameObject disc;
        private Material ownedMaterial;

        private void Awake()
        {
            member = GetComponent<TeamMember>();

            CreateDisc();
        }

        /// <summary>Assigned by the scene generator, before Awake builds the disc.</summary>
        public void ConfigureMaterial(Material material)
        {
            auraMaterial = material;
        }

        private void OnDestroy()
        {
            // A material instanced at runtime is not collected with the object
            // that used it.
            if (ownedMaterial != null)
            {
                Destroy(ownedMaterial);
                ownedMaterial = null;
            }
        }

        private void CreateDisc()
        {
            disc = GameObject.CreatePrimitive(PrimitiveType.Quad);
            disc.name = "Tension Aura";
            disc.transform.SetParent(transform, false);

            // A collider here would be an invisible plate on the pitch: the ball
            // would bounce off the glow.
            Collider quadCollider = disc.GetComponent<Collider>();

            if (quadCollider != null)
            {
                Destroy(quadCollider);
            }

            // Flat on the grass, under the player's feet rather than at the
            // capsule's centre.
            disc.transform.localPosition = new Vector3(0f, GroundY - transform.position.y, 0f);
            disc.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            disc.transform.localScale = new Vector3(diameter, diameter, diameter);

            MeshRenderer renderer = disc.GetComponent<MeshRenderer>();

            if (renderer != null)
            {
                if (auraMaterial != null)
                {
                    renderer.sharedMaterial = auraMaterial;
                }
                else
                {
                    ownedMaterial = BuildFallbackMaterial();
                    renderer.sharedMaterial = ownedMaterial;
                }

                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            disc.SetActive(false);
        }

        /// <summary>
        /// Only used when the scene was built without an aura material asset.
        /// URP ships its shaders opaque, so the alpha below is ignored unless the
        /// material is explicitly flipped to alpha blending — without this the
        /// "glow" comes out as a solid orange plate.
        /// </summary>
        private static Material BuildFallbackMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");

            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            Material material = new Material(shader)
            {
                name = "TensionAuraMaterial (runtime)",
                color = new Color(1f, 0.45f, 0.05f, 0.55f)
            };

            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            return material;
        }

        /// <summary>
        /// Late, not Update: a substitution can change which side this player is
        /// on partway through a frame, and the aura should answer for the state
        /// the frame is actually being drawn in.
        /// </summary>
        private void LateUpdate()
        {
            if (disc == null || member == null)
            {
                return;
            }

            TensionManager tension = TensionManager.Instance;

            // A substitute in the dugout is not part of the surge, however well
            // his side is playing.
            bool burning = tension != null && member.isStarter && tension.IsBurning(member.team);

            if (disc.activeSelf != burning)
            {
                disc.SetActive(burning);
            }

            if (!burning)
            {
                return;
            }

            // Unscaled: a duel freezes the match, and an aura that froze with it
            // would read as the zone having ended at the exact moment the player
            // is deciding what to do with it.
            float pulse = 1f + (pulseAmount * Mathf.Sin(Time.unscaledTime * pulseSpeed * Mathf.PI * 2f));
            float size = diameter * pulse;

            disc.transform.localScale = new Vector3(size, size, size);
        }
    }
}
