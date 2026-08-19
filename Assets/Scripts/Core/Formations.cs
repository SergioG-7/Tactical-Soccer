using UnityEngine;
using TacticalSoccer.Gameplay;

namespace TacticalSoccer.Core
{
    /// <summary>The shapes a side may line up in. Six outfield players either way.</summary>
    public enum FormationType
    {
        Balanced_2_2_2,
        Defensive_3_2_1,
        Offensive_1_3_2
    }

    /// <summary>
    /// How hard the opposition plays. Two levers, both deliberately small: how
    /// often the AI re-decides, and a flat handicap on every duel it fights.
    ///
    /// Neither touches the human's side at any setting. A difficulty that made
    /// YOUR players worse would be indistinguishable from a bug from the other
    /// side of the screen.
    /// </summary>
    public enum AIDifficulty
    {
        Facil,
        Normal,
        Dificil
    }

    /// <summary>
    /// One outfield slot of a starting shape: where the player stands and which
    /// line they hold.
    /// </summary>
    public readonly struct FormationSlot
    {
        public readonly PlayerRole Role;

        /// <summary>Across the pitch. The two sides are mirror images, so one value serves both.</summary>
        public readonly float X;

        /// <summary>
        /// Distance from the halfway line into the team's OWN half, always
        /// positive. Callers multiply by the side's sign, so one table describes
        /// both teams.
        /// </summary>
        public readonly float OwnHalfZ;

        public FormationSlot(PlayerRole role, float x, float ownHalfZ)
        {
            Role = role;
            X = x;
            OwnHalfZ = ownHalfZ;
        }
    }

    /// <summary>
    /// The starting shapes, in one place. The scene generator spawns the squad
    /// from the same tables the formation menu later re-arranges them with, so
    /// picking the default shape in the menu puts everybody exactly back where
    /// they began rather than somewhere subtly different.
    ///
    /// Every slot sits in its own half: the three lines are pinned to the same
    /// depths whatever the shape, so a 3-2-1 reads as a deeper back line rather
    /// than as a different pitch.
    /// </summary>
    public static class Formations
    {
        private const float DefenderLineZ = 16f;
        private const float MidfieldLineZ = 9f;

        // Just outside the centre circle, whose painted radius is 3.75 units.
        private const float ForwardLineZ = 4.5f;

        private static readonly FormationSlot[] Balanced =
        {
            new FormationSlot(PlayerRole.Defender, -4.5f, DefenderLineZ),
            new FormationSlot(PlayerRole.Defender, 4.5f, DefenderLineZ),
            new FormationSlot(PlayerRole.Midfielder, -7.5f, MidfieldLineZ),
            new FormationSlot(PlayerRole.Midfielder, 7.5f, MidfieldLineZ),
            new FormationSlot(PlayerRole.Forward, -3.5f, ForwardLineZ),
            new FormationSlot(PlayerRole.Forward, 3.5f, ForwardLineZ)
        };

        private static readonly FormationSlot[] Defensive =
        {
            // The middle centre-back drops a metre deeper, so three across the
            // back reads as a covered line rather than a flat wall.
            new FormationSlot(PlayerRole.Defender, -7f, DefenderLineZ),
            new FormationSlot(PlayerRole.Defender, 0f, DefenderLineZ + 1f),
            new FormationSlot(PlayerRole.Defender, 7f, DefenderLineZ),
            new FormationSlot(PlayerRole.Midfielder, -5f, MidfieldLineZ),
            new FormationSlot(PlayerRole.Midfielder, 5f, MidfieldLineZ),
            new FormationSlot(PlayerRole.Forward, 0f, ForwardLineZ)
        };

        private static readonly FormationSlot[] Offensive =
        {
            new FormationSlot(PlayerRole.Defender, 0f, DefenderLineZ),
            new FormationSlot(PlayerRole.Midfielder, -8f, MidfieldLineZ + 1f),
            new FormationSlot(PlayerRole.Midfielder, 0f, MidfieldLineZ),
            new FormationSlot(PlayerRole.Midfielder, 8f, MidfieldLineZ + 1f),
            new FormationSlot(PlayerRole.Forward, -3.5f, ForwardLineZ),
            new FormationSlot(PlayerRole.Forward, 3.5f, ForwardLineZ)
        };

        /// <summary>How many outfield players a shape expects. The keeper is extra.</summary>
        public const int OutfieldCount = 6;

        public static FormationSlot[] Get(FormationType formation)
        {
            switch (formation)
            {
                case FormationType.Defensive_3_2_1: return Defensive;
                case FormationType.Offensive_1_3_2: return Offensive;
                default: return Balanced;
            }
        }

        /// <summary>One of the shapes, at random. Used by the "surprise me" rival setting.</summary>
        public static FormationType Random()
        {
            FormationType[] all = (FormationType[])System.Enum.GetValues(typeof(FormationType));

            return all[UnityEngine.Random.Range(0, all.Length)];
        }

        /// <summary>Label for the HUD, so the UI does not hardcode the numbers.</summary>
        public static string GetLabel(FormationType formation)
        {
            switch (formation)
            {
                case FormationType.Defensive_3_2_1: return "3-2-1";
                case FormationType.Offensive_1_3_2: return "1-3-2";
                default: return "2-2-2";
            }
        }
    }
}
