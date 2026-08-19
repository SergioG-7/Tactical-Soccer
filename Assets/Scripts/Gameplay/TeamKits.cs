using UnityEngine;

namespace TacticalSoccer.Gameplay
{
    /// <summary>The shirt the human side plays in.</summary>
    public enum TeamKit
    {
        Azul,
        Verde,
        Negro,
        Blanco
    }

    /// <summary>
    /// The four strips, as colours and as labels.
    ///
    /// A lookup rather than a field on each player: the kit is a property of the
    /// SIDE, chosen once before kickoff, and storing it per player would let the
    /// two halves of a team disagree about what they are wearing.
    ///
    /// All four are kept clear of the opposition's red and of the white line
    /// paint — the pitch is read at a glance from a camera 22 units up, and a
    /// strip that needed a second look would cost more than it is worth.
    /// </summary>
    public static class TeamKits
    {
        public static Color GetColor(TeamKit kit)
        {
            switch (kit)
            {
                case TeamKit.Verde: return new Color(0.11f, 0.62f, 0.24f, 1f);

                // Not pure black. A capsule at 0,0,0 under a directional light
                // reads as a silhouette with no shading at all, so the number on
                // its back is the only thing telling two players apart.
                case TeamKit.Negro: return new Color(0.12f, 0.12f, 0.15f, 1f);

                // Likewise not pure white, so a player standing on the halfway
                // line is still distinguishable from the paint under them.
                case TeamKit.Blanco: return new Color(0.90f, 0.90f, 0.93f, 1f);

                default: return Color.blue;
            }
        }

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

        /// <summary>
        /// Paints one side's players and tells each of them what colour they
        /// are now, returning how many were changed.
        ///
        /// Written through <c>renderer.material</c>, never <c>sharedMaterial</c>:
        /// every blue player points at the same TeamBlueMaterial asset, so
        /// writing the shared one would repaint the opposition's keeper gloves
        /// on the way past and — worse — persist the change to disk, so the next
        /// match would open in whatever colour the last one chose.
        ///
        /// The goalkeeper is repainted with everybody else. He used to be
        /// exempt so he could be picked out of a crowded box, but that meant a
        /// fixed yellow — and a tournament round can put the OPPOSITION in
        /// orange or gold, at which point the keeper's "distinguishing" colour
        /// is the colour of the other team. Reading the eleven as one side is
        /// worth more.
        /// </summary>
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

                // The stun blink restores a colour it cached at Awake, off the
                // SHARED material — i.e. the shirt this player was born in. Left
                // alone, the first player stunned after a kit change would blink
                // back to blue and stay there.
                if (member.TryGetComponent(out Player.PlayerRoute route))
                {
                    route.RefreshOriginalColor(color);
                }
            }

            return repainted;
        }
    }
}
