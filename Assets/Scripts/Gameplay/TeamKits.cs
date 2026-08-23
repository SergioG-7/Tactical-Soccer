using UnityEngine;

namespace TacticalSoccer.Gameplay
{
    // Equipación que puede llevar un equipo.
    public enum TeamKit
    {
        Azul,
        Verde,
        Negro,
        Blanco
    }

    // Colores y etiquetas de las cuatro equipaciones disponibles.
    public static class TeamKits
    {
        // Devuelve el color asociado a una equipación.
        public static Color GetColor(TeamKit kit)
        {
            switch (kit)
            {
                case TeamKit.Verde: return new Color(0.11f, 0.62f, 0.24f, 1f);
                case TeamKit.Negro: return new Color(0.12f, 0.12f, 0.15f, 1f);
                case TeamKit.Blanco: return new Color(0.90f, 0.90f, 0.93f, 1f);

                default: return Color.blue;
            }
        }

        // Devuelve el nombre localizado de una equipación.
        public static string GetLabel(TeamKit kit)
        {
            switch (kit)
            {
                case TeamKit.Verde: return Core.LocalizationManager.GetText("color.green");
                case TeamKit.Negro: return Core.LocalizationManager.GetText("color.black");
                case TeamKit.Blanco: return Core.LocalizationManager.GetText("color.white");
                default: return Core.LocalizationManager.GetText("color.blue");
            }
        }

        // Repinta a todos los jugadores de un equipo con el color dado y devuelve cuántos cambiaron.
        public static int RepaintTeam(TeamId team, Color color)
        {
            int repainted = 0;

            foreach (TeamMember member in Object.FindObjectsByType<TeamMember>())
            {
                if (member.team != team)
                {
                    continue;
                }

                if (!member.TryGetComponent(out MeshRenderer renderer))
                {
                    continue;
                }

                renderer.material.color = color;
                repainted++;

                // Actualiza también el color original que usa el parpadeo de aturdimiento.
                if (member.TryGetComponent(out Player.PlayerRoute route))
                {
                    route.RefreshOriginalColor(color);
                }
            }

            return repainted;
        }
    }
}
