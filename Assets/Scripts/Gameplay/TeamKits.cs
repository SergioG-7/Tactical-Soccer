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
    }
}
