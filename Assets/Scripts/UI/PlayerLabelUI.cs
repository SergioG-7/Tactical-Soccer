using UnityEngine;
using UnityEngine.UI;
using TacticalSoccer.Gameplay;

namespace TacticalSoccer.UI
{
    /// <summary>
    /// The tag floating over a player: what they play, and how much they have
    /// left. Lives on a world-space canvas parented to the player, so it tracks
    /// them for free and needs no per-frame position maths of its own.
    ///
    /// Reads its player rather than being pushed to. Stamina changes every
    /// frame and the role can change mid-match — the formation menu re-roles the
    /// whole side — so a label that were only written once at setup would be
    /// telling the player something that stopped being true.
    /// </summary>
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

        /// <summary>
        /// The role the label is currently showing. Compared against the live
        /// one each frame so re-roling a side actually relabels it, without
        /// rebuilding the text every frame for the 99% of frames it is unchanged.
        /// </summary>
        private PlayerRole shownRole;
        private bool hasShownRole;
        private bool shownCaptain;

        /// <summary>
        /// The element the label is currently showing. Tracked like the rest so
        /// the tag is only rebuilt when something on it actually changed —
        /// affinity does not change mid-match today, but the tag has no way of
        /// knowing that and a substitution swaps the whole player out.
        /// </summary>
        private Element shownElement;

        /// <summary>
        /// The number the label is currently showing. Tracked alongside the role
        /// for the same reason: a substitution swaps two players in and out, and
        /// the tag has to be able to notice that the man in this slot is now a
        /// different one.
        /// </summary>
        private int shownNumber;

        /// <summary>
        /// Wires the label to its player. Called by the scene generator, so the
        /// reference is serialized and the tag reads correctly in the editor as
        /// well as in play mode.
        /// </summary>
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
                // Parented under the player it describes, so it can find its own
                // subject if it was ever built by hand rather than generated.
                member = GetComponentInParent<TeamMember>();
            }
        }

        /// <summary>
        /// Late, not Update: the camera has already been moved for this frame by
        /// the follow rig and the duel camera, so billboarding here faces the
        /// pose actually being rendered instead of last frame's.
        /// </summary>
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

        private void ApplyRole()
        {
            if (roleText == null || member == null)
            {
                return;
            }

            string abbreviation = PlayerRoles.Abbreviate(member.role);

            // "10 - FW". The number comes first because it is the one thing that
            // identifies THIS player: three midfielders all read MF, and after a
            // substitution the shirt is how you tell who came on. A squad built
            // without numbers still reads correctly as the plain role.
            string label = member.jerseyNumber > 0
                ? $"{member.jerseyNumber} - {abbreviation}"
                : abbreviation;

            // The armband, on the tag rather than on a separate marker: it has
            // to be findable at a glance in a moving twelve-player pitch, and
            // the tag is the one thing already tracking each player.
            if (member.isCaptain)
            {
                label += " (C)";
            }

            // The element leads, in its own colour. It is the one thing on the
            // tag the rest of the label cannot carry: the role is already spelt
            // out and the number is already the identity, but affinity decides
            // duels and is invisible everywhere else on the pitch.
            //
            // Rich text rather than a second Text component: the colour has to
            // differ from the role colour on the SAME line, and a second object
            // would have to be laid out against a string whose width changes
            // with the shirt number.
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

        /// <summary>
        /// Turns the tag to face whatever is looking at it. The duel camera is
        /// the authority when it exists — it is the one that swings the view
        /// around during a clash — and the main camera is the fallback for a
        /// scene without one.
        /// </summary>
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
