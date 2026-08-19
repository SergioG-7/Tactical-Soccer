using UnityEngine;
using UnityEngine.UI;
using TacticalSoccer.Gameplay;

namespace TacticalSoccer.UI
{
    /// <summary>
    /// The momentum bars, one per side.
    ///
    /// Polled every frame rather than pushed to. The bar changes continuously
    /// while a burn drains, so an event-driven version would need a tick event
    /// anyway — and the one thing that genuinely happens once, the bar filling,
    /// already has its own event for the shout.
    ///
    /// Both sides are shown, not just the human's. Knowing the opposition is one
    /// duel away from the zone is exactly the information that should change how
    /// you play the next duel.
    /// </summary>
    public class TensionUI : MonoBehaviour
    {
        [Header("Barras")]
        public Image blueFill;
        public Image redFill;

        [Header("Etiquetas")]
        public Text blueLabel;
        public Text redLabel;

        [Header("Colores")]
        [Tooltip("Fill while the bar is still charging.")]
        [SerializeField] private Color blueChargingColor = new Color(0.25f, 0.55f, 1f, 1f);

        [SerializeField] private Color redChargingColor = new Color(1f, 0.35f, 0.30f, 1f);

        [Tooltip("Fill while the side is in the zone. Deliberately the same for " +
                 "both teams: burning is a state, not a team colour, and it has " +
                 "to be unmistakable at a glance from either bar.")]
        [SerializeField] private Color burningColor = new Color(1f, 0.85f, 0.15f, 1f);

        [Tooltip("How fast the burning bar pulses, in cycles per second.")]
        [SerializeField] private float burnPulseSpeed = 3f;

        private void Update()
        {
            TensionManager tension = TensionManager.Instance;

            if (tension == null)
            {
                return;
            }

            Paint(tension, TeamId.Blue, blueFill, blueLabel, blueChargingColor);
            Paint(tension, TeamId.Red, redFill, redLabel, redChargingColor);
        }

        private void Paint(TensionManager tension, TeamId team, Image fill, Text label, Color chargingColor)
        {
            if (fill == null)
            {
                return;
            }

            bool burning = tension.IsBurning(team);

            fill.fillAmount = tension.Fraction(team);

            if (burning)
            {
                // Unscaled: a duel freezes the match, and a bar that stopped
                // pulsing behind the duel panel would read as the zone having
                // ended just when the player is deciding what to do with it.
                float pulse = 0.75f + (0.25f * Mathf.Sin(Time.unscaledTime * burnPulseSpeed * Mathf.PI * 2f));

                fill.color = burningColor * pulse;
            }
            else
            {
                fill.color = chargingColor;
            }

            if (label == null)
            {
                return;
            }

            // The colour the side is WEARING, not the slot it occupies: a
            // player who picked the green kit was still being called AZUL, and
            // in a tournament the opposition is orange or gold rather than red.
            // Fouls.DescribeTeam resolves the live kit through the same colour
            // names the foul shout uses.
            string side = Fouls.DescribeTeam(team);

            if (burning)
            {
                Core.LocalizationManager.WriteFormatted(label, "hud.tensionBurning", side);
                return;
            }

            Core.LocalizationManager.WriteFormatted(label, "hud.tensionPercent",
                side, Mathf.RoundToInt(tension.Fraction(team) * 100f));
        }
    }
}
