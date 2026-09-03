using UnityEngine;
using TacticalSoccer.Gameplay;

namespace TacticalSoccer.Core
{
    // Define los límites del terreno de juego: líneas de banda, portería, área y zonas de peligro.
    public static class PitchBounds
    {
        // La línea pintada, no el borde del césped: margen de 5% (1.52 u) hacia dentro del plano 30x50.
        public const float SideLineX = 13.5f;
        public const float GoalLineZ = 23.5f;

        // Mitad del ancho de la portería.
        public const float GoalMouthHalfWidth = 3.5f;

        // Fondo de la red, más allá de esto el balón se considera fuera.
        public const float BehindGoalZ = 25.5f;

        // Altura mínima antes de considerar que el balón atravesó el suelo.
        public const float FallThroughFloorY = -5f;

        // Profundidad del área de penalti desde la línea de gol.
        public const float PenaltyAreaDepth = 12f;

        // Mitad del ancho del área de penalti.
        public const float PenaltyAreaHalfWidth = 8f;

        // Comprueba si un punto está dentro del área que defiende el equipo indicado.
        public static bool IsInsidePenaltyArea(Vector3 position, TeamId defendingTeam)
        {
            if (Mathf.Abs(position.x) > PenaltyAreaHalfWidth)
            {
                return false;
            }

            float depth = position.z * DefendedSide(defendingTeam);

            return depth >= GoalLineZ - PenaltyAreaDepth && depth <= BehindGoalZ;
        }

        // Un metro de margen fuera de la línea: los jugadores pueden salirse pero no del mundo.
        public const float PlayerLimitX = 14.5f;
        public const float PlayerLimitZ = 24.5f;

        // Dónde puede colocarse el portero a mano: a lo ancho de la boca de gol, cerca de su línea.
        private const float KeeperLineZ = 21.5f;
        private const float KeeperDepth = 2f;

        // Devuelve el signo en Z de la portería que defiende cada equipo. Azul defiende sur, Rojo defiende norte.
        public static float DefendedSide(TeamId team)
        {
            return team == TeamId.Blue ? -1f : 1f;
        }

        // True si el punto está en la portería que defiende el equipo (su propia portería).
        public static bool IsOwnGoal(Vector3 point, TeamId team)
        {
            return Mathf.Sign(point.z) == DefendedSide(team);
        }

        // Distancia desde la línea de gol que se considera peligro de autogol.
        public const float OwnGoalDangerDepth = 4f;

        // True si un punto está lo bastante cerca de la propia portería como para arriesgar un autogol.
        public static bool IsNearOwnGoal(Vector3 point, TeamId team)
        {
            if (Mathf.Abs(point.x) > GoalMouthHalfWidth + OwnGoalDangerDepth)
            {
                return false;
            }

            float depth = point.z * DefendedSide(team);

            return depth >= GoalLineZ - OwnGoalDangerDepth;
        }

        // Aleja un punto de la propia portería hasta una distancia segura, moviendo solo Z.
        public static Vector3 PushOutOfOwnGoal(Vector3 point, TeamId team)
        {
            if (!IsNearOwnGoal(point, team))
            {
                return point;
            }

            float side = DefendedSide(team);

            return new Vector3(point.x, point.y, side * (GoalLineZ - OwnGoalDangerDepth));
        }

        // Comprueba si el balón sigue dentro de los límites jugables del campo.
        public static bool IsBallInPlay(Vector3 position)
        {
            if (position.y <= FallThroughFloorY)
            {
                return false;
            }

            if (Mathf.Abs(position.x) > SideLineX)
            {
                return false;
            }

            if (Mathf.Abs(position.z) <= GoalLineZ)
            {
                return true;
            }

            // Más allá de la línea de gol: solo vale entre los postes y hasta el fondo de la red.
            return Mathf.Abs(position.x) <= GoalMouthHalfWidth
                && Mathf.Abs(position.z) <= BehindGoalZ;
        }

        // Mantiene a un jugador dentro de los límites del mapa, sin tocar la altura.
        public static Vector3 ClampPlayer(Vector3 position)
        {
            return new Vector3(
                Mathf.Clamp(position.x, -PlayerLimitX, PlayerLimitX),
                position.y,
                Mathf.Clamp(position.z, -PlayerLimitZ, PlayerLimitZ));
        }

        // Limita dónde puede colocarse un jugador en el saque inicial: solo en su propio campo, y el portero cerca de su portería.
        public static Vector3 ClampKickoffPlacement(Vector3 position, TeamId team, bool isGoalkeeper)
        {
            float ownSide = team == TeamId.Blue ? -1f : 1f;

            if (isGoalkeeper)
            {
                float line = ownSide * KeeperLineZ;

                return new Vector3(
                    Mathf.Clamp(position.x, -GoalMouthHalfWidth, GoalMouthHalfWidth),
                    position.y,
                    Mathf.Clamp(position.z,
                        Mathf.Min(line - KeeperDepth, line + KeeperDepth),
                        Mathf.Max(line - KeeperDepth, line + KeeperDepth)));
            }

            // Solo en su propio campo; la línea de medio campo marca el límite.
            float minZ = ownSide < 0f ? -PlayerLimitZ : 0f;
            float maxZ = ownSide < 0f ? 0f : PlayerLimitZ;

            return new Vector3(
                Mathf.Clamp(position.x, -PlayerLimitX, PlayerLimitX),
                position.y,
                Mathf.Clamp(position.z, minZ, maxZ));
        }
    }
}
