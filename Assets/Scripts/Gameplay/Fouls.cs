using UnityEngine;

namespace TacticalSoccer.Gameplay
{
    /// <summary>
    /// How a foul is named and coloured.
    ///
    /// One place rather than two, because the banner headline and the floating
    /// shout over the offender's head are announcing the same event: if they
    /// could disagree about the wording or the colour, sooner or later they
    /// would.
    /// </summary>
    public static class Fouls
    {
        /// <summary>
        /// Minimum brightness a shout is allowed to be printed at.
        ///
        /// The shirts include a near-black strip and a deep purple, and text in
        /// either of those on a dark banner is text nobody can read. The hue is
        /// what identifies the side, so it is kept and only the value is raised.
        /// </summary>
        private const float MinimumShoutValue = 0.85f;

        /// <summary>
        /// What the crowd would shout: the side's actual strip, not the name it
        /// was generated under.
        ///
        /// Reads the live kit rather than "AZUL"/"ROJO" hard-coded, because a
        /// tournament round puts the opposition in orange, purple or gold and a
        /// shout of "¡FALTA DE ROJO!" against a purple team names nobody on the
        /// pitch.
        /// </summary>
        public static string DescribeTeam(TeamId team)
        {
            return Core.MatchManager.Instance != null
                ? DescribeColor(Core.MatchManager.GetTeamColor(team)).ToUpperInvariant()
                : Core.LocalizationManager.GetText(team == TeamId.Blue ? "team.blue" : "team.red");
        }

        /// <summary>
        /// The colour a foul is announced in: the offending side's own, brightened
        /// enough to be legible.
        ///
        /// Naming the team and printing it in that team's colour is what makes
        /// the shout readable at a glance and what keeps it honest in a
        /// tournament, where the opposition is not red.
        /// </summary>
        public static Color AccusationColor(TeamId team)
        {
            Color kit = Core.MatchManager.Instance != null
                ? Core.MatchManager.GetTeamColor(team)
                : (team == TeamId.Blue ? Color.blue : Color.red);

            Color.RGBToHSV(kit, out float h, out float s, out float v);

            // Saturation is pulled down a little along with the lift: a fully
            // saturated pure blue is dark however high its "value" is, because
            // only one channel is carrying any light at all.
            return Color.HSVToRGB(h, Mathf.Min(s, 0.75f), Mathf.Max(v, MinimumShoutValue));
        }

        /// <summary>
        /// Names a colour the way a commentator would. Hue-based rather than a
        /// table of the exact kits, so a colour nobody has defined yet — a new
        /// tournament round, a strip added later — still gets called something
        /// rather than falling back to the team's original name.
        /// </summary>
        public static string DescribeColor(Color color)
        {
            Color.RGBToHSV(color, out float h, out float s, out float v);

            if (v < 0.25f)
            {
                return Name("color.black");
            }

            if (s < 0.18f)
            {
                return Name(v > 0.7f ? "color.white" : "color.grey");
            }

            float degrees = h * 360f;

            if (degrees < 20f || degrees >= 330f) return Name("color.red");
            if (degrees < 45f) return Name("color.orange");
            if (degrees < 70f) return Name(v > 0.75f ? "color.gold" : "color.yellow");
            if (degrees < 165f) return Name("color.green");
            if (degrees < 200f) return Name("color.cyan");
            if (degrees < 260f) return Name("color.blue");
            if (degrees < 290f) return Name("color.purple");

            return Name("color.pink");
        }

        /// <summary>
        /// A colour name in the player's language. Wrapped so the hue table above
        /// stays a table of hues rather than turning into a wall of lookups.
        /// </summary>
        private static string Name(string key)
        {
            return Core.LocalizationManager.GetText(key);
        }
    }
}
