using UnityEngine;
using UnityEngine.UI;
using TacticalSoccer.Gameplay;

namespace TacticalSoccer.UI
{
    // Etiqueta flotante sobre un jugador: muestra su rol, dorsal y barra de estamina.
    public class PlayerLabelUI : MonoBehaviour
    {
        public Text roleText;
        public Image staminaBar;

        [Header("Colores de rol")]
        [SerializeField] private Color forwardColor = new Color(1f, 0.35f, 0.30f, 1f);
        [SerializeField] private Color midfielderColor = new Color(0.40f, 0.95f, 0.45f, 1f);
        [SerializeField] private Color defenderColor = new Color(0.45f, 0.70f, 1f, 1f);
        [SerializeField] private Color goalkeeperColor = new Color(1f, 0.90f, 0.30f, 1f);

        [Header("Colores de estamina")]
        [SerializeField] private Color staminaHealthyColor = new Color(0.30f, 0.85f, 0.35f, 1f);
        [SerializeField] private Color staminaTiredColor = new Color(0.95f, 0.80f, 0.20f, 1f);
        [SerializeField] private Color staminaExhaustedColor = new Color(0.90f, 0.25f, 0.20f, 1f);

        [Tooltip("Below this share of the tank the bar turns amber, as a warning " +
                 "before the player is actually blown.")]
        [SerializeField] private float tiredFraction = 0.5f;

        [SerializeField] private TeamMember member;

        // Últimos valores mostrados, para saber cuándo hay que reconstruir el texto.
        private PlayerRole shownRole;
        private bool hasShownRole;
        private bool shownCaptain;
        private Element shownElement;
        private int shownNumber;

        // Asocia la etiqueta a su jugador.
        public void Setup(TeamMember teamMember)
        {
            member = teamMember;

            ApplyRole();
            RefreshStamina();
        }

        private void Awake()
        {
            if (member == null)
            {
                member = GetComponentInParent<TeamMember>();
            }
        }

        // Actualiza el texto, la barra de estamina y la orientación de la etiqueta cada frame.
        private void LateUpdate()
        {
            if (member == null)
            {
                return;
            }

            if (!hasShownRole
                || shownRole != member.role
                || shownNumber != member.jerseyNumber
                || shownCaptain != member.isCaptain
                || shownElement != member.element)
            {
                ApplyRole();
            }

            RefreshStamina();
            FaceCamera();
        }

        // Reconstruye el texto de la etiqueta: dorsal, rol, capitán y afinidad.
        private void ApplyRole()
        {
            if (roleText == null || member == null)
            {
                return;
            }

            string abbreviation = PlayerRoles.Abbreviate(member.role);

            string label = member.jerseyNumber > 0
                ? $"{member.jerseyNumber} - {abbreviation}"
                : abbreviation;

            if (member.isCaptain)
            {
                label += " (C)";
            }

            roleText.supportRichText = true;

            roleText.text =
                $"<color=#{Elements.HexColor(member.element)}>{Elements.Glyph(member.element)}</color> {label}";

            roleText.color = GetRoleColor(member.role);

            shownRole = member.role;
            shownNumber = member.jerseyNumber;
            shownCaptain = member.isCaptain;
            shownElement = member.element;
            hasShownRole = true;
        }

        // Actualiza el color y el relleno de la barra de estamina.
        private void RefreshStamina()
        {
            if (staminaBar == null || member == null)
            {
                return;
            }

            float fraction = member.StaminaFraction;

            staminaBar.fillAmount = fraction;

            if (member.IsExhausted)
            {
                staminaBar.color = staminaExhaustedColor;
                return;
            }

            staminaBar.color = fraction < tiredFraction ? staminaTiredColor : staminaHealthyColor;
        }

        // Orienta la etiqueta para que mire siempre hacia la cámara.
        private void FaceCamera()
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

        // Devuelve el color asociado a cada rol.
        private Color GetRoleColor(PlayerRole role)
        {
            switch (role)
            {
                case PlayerRole.Forward: return forwardColor;
                case PlayerRole.Midfielder: return midfielderColor;
                case PlayerRole.Goalkeeper: return goalkeeperColor;
                default: return defenderColor;
            }
        }
    }
}
