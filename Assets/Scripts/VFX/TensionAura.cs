using UnityEngine;
using TacticalSoccer.Gameplay;

namespace TacticalSoccer.VFX
{
    // Aura brillante bajo los pies de un jugador cuyo equipo está en tensión ("en racha").
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

        // Ligeramente por encima del suelo para evitar z-fighting con el césped.
        private const float GroundY = 0.02f;

        private TeamMember member;
        private GameObject disc;
        private Material ownedMaterial;

        // Busca el TeamMember del jugador y crea el disco del aura.
        private void Awake()
        {
            member = GetComponent<TeamMember>();

            CreateDisc();
        }

        // Asigna el material del aura antes de que Awake cree el disco.
        public void ConfigureMaterial(Material material)
        {
            auraMaterial = material;
        }

        // Destruye el material creado en tiempo de ejecución, si lo hay.
        private void OnDestroy()
        {
            if (ownedMaterial != null)
            {
                Destroy(ownedMaterial);
                ownedMaterial = null;
            }
        }

        // Crea el disco plano del aura, hijo del jugador, sin collider.
        private void CreateDisc()
        {
            disc = GameObject.CreatePrimitive(PrimitiveType.Quad);
            disc.name = "Tension Aura";
            disc.transform.SetParent(transform, false);

            Collider quadCollider = disc.GetComponent<Collider>();

            if (quadCollider != null)
            {
                Destroy(quadCollider);
            }

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

        // Crea un material de reserva transparente cuando no hay ninguno asignado.
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

        // Muestra u oculta el aura según si el equipo del jugador está en tensión, y anima su pulso.
        private void LateUpdate()
        {
            if (disc == null || member == null)
            {
                return;
            }

            TensionManager tension = TensionManager.Instance;

            bool burning = tension != null && member.isStarter && tension.IsBurning(member.team);

            if (disc.activeSelf != burning)
            {
                disc.SetActive(burning);
            }

            if (!burning)
            {
                return;
            }

            float pulse = 1f + (pulseAmount * Mathf.Sin(Time.unscaledTime * pulseSpeed * Mathf.PI * 2f));
            float size = diameter * pulse;

            disc.transform.localScale = new Vector3(size, size, size);
        }
    }
}
