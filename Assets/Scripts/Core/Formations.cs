using UnityEngine;
using TacticalSoccer.Gameplay;

namespace TacticalSoccer.Core
{
    // Formaciones disponibles, con seis jugadores de campo cada una.
    public enum FormationType
    {
        Balanced_2_2_2,
        Defensive_3_2_1,
        Offensive_1_3_2
    }

    // Nivel de dificultad de la IA rival.
    public enum AIDifficulty
    {
        Facil,
        Normal,
        Dificil
    }

    // Un puesto de la formación inicial: rol, posición lateral y profundidad en el propio campo.
    public readonly struct FormationSlot
    {
        public readonly PlayerRole Role;

        // Posición lateral (X); ambos equipos usan el mismo valor en espejo.
        public readonly float X;

        // Distancia desde el medio campo hacia el propio campo, siempre positiva.
        public readonly float OwnHalfZ;

        public FormationSlot(PlayerRole role, float x, float ownHalfZ)
        {
            Role = role;
            X = x;
            OwnHalfZ = ownHalfZ;
        }
    }

    // Define las posiciones de cada formación disponible.
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

        // Número de jugadores de campo por formación (sin contar al portero).
        public const int OutfieldCount = 6;

        // Devuelve los puestos de la formación indicada.
        public static FormationSlot[] Get(FormationType formation)
        {
            switch (formation)
            {
                case FormationType.Defensive_3_2_1: return Defensive;
                case FormationType.Offensive_1_3_2: return Offensive;
                default: return Balanced;
            }
        }

        // Devuelve una formación al azar.
        public static FormationType Random()
        {
            FormationType[] all = (FormationType[])System.Enum.GetValues(typeof(FormationType));

            return all[UnityEngine.Random.Range(0, all.Length)];
        }

        // Devuelve la etiqueta de texto de la formación (ej. "2-2-2").
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
