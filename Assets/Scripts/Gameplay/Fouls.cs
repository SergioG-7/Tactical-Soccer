using UnityEngine;

namespace TacticalSoccer.Gameplay
{
    // Da nombre y color a las faltas para mostrarlas en la interfaz.
    public static class Fouls
    {
        // Brillo mínimo con el que se imprime un aviso de falta, para que se lea bien.
        private const float MinimumShoutValue = 0.85f;

        // Nombre del equipo según el color real de su camiseta.
        public static string DescribeTeam(TeamId team)
        {
            return Core.MatchManager.Instance != null
                ? DescribeColor(Core.MatchManager.GetTeamColor(team)).ToUpperInvariant()
                : Core.LocalizationManager.GetText(team == TeamId.Blue ? "team.blue" : "team.red");
        }

        // Color en el que se anuncia la falta: el del equipo infractor, aclarado para que se lea bien.
        public static Color AccusationColor(TeamId team)
        {
            Color kit = Core.MatchManager.Instance != null
                ? Core.MatchManager.GetTeamColor(team)
                : (team == TeamId.Blue ? Color.blue : Color.red);

            Color.RGBToHSV(kit, out float h, out float s, out float v);

            return Color.HSVToRGB(h, Mathf.Min(s, 0.75f), Mathf.Max(v, MinimumShoutValue));
        }

        // Convierte un color en su nombre (rojo, azul, verde...) según su tono.
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

        // Traduce la clave de color al idioma del jugador.
        private static string Name(string key)
        {
            return Core.LocalizationManager.GetText(key);
        }
    }
}
