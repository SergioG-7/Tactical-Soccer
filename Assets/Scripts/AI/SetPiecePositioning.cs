using UnityEngine;
using TacticalSoccer.Core;
using TacticalSoccer.Gameplay;
using TacticalSoccer.Player;

namespace TacticalSoccer.AI
{
    // Cálculo de posiciones para los saques a balón parado: quién lo saca, quién se aparta y quién se ofrece.
    public static class SetPiecePositioning
    {
        // Margen para meter la marca del saque dentro de las líneas del campo.
        private const float RestartBallInset = 0.2f;

        // Aparta a los jugadores del equipo contrario que estén dentro del radio de exclusión del saque.
        public static void ClearExclusionZone(Vector3 ballSpot, PlayerBallHandler taker, float exclusionRadius)
        {
            TeamMember takerMember = taker != null ? taker.GetComponent<TeamMember>() : null;

            if (takerMember == null)
            {
                return;
            }

            int moved = 0;

            foreach (TeamMember member in Object.FindObjectsByType<TeamMember>())
            {
                if (member.team == takerMember.team || !member.isStarter || member.isGoalkeeper)
                {
                    continue;
                }

                Vector3 position = member.transform.position;

                // Distancia en el plano horizontal, ignorando la altura.
                Vector3 away = new Vector3(position.x - ballSpot.x, 0f, position.z - ballSpot.z);
                float distance = away.magnitude;

                if (distance >= exclusionRadius)
                {
                    continue;
                }

                // Si el jugador está justo sobre el balón, se retira hacia su propia portería.
                Vector3 direction = distance > 0.01f
                    ? away / distance
                    : new Vector3(0f, 0f, member.team == TeamId.Blue ? -1f : 1f);

                Vector3 pushed = PitchBounds.ClampPlayer(new Vector3(
                    ballSpot.x + (direction.x * exclusionRadius),
                    position.y,
                    ballSpot.z + (direction.z * exclusionRadius)));

                // Si el punto sigue dentro del radio (por ejemplo en una esquina), se retira hacia el centro del campo.
                if (Vector3.Distance(new Vector3(pushed.x, 0f, pushed.z),
                        new Vector3(ballSpot.x, 0f, ballSpot.z)) < exclusionRadius - 0.05f)
                {
                    Vector3 inward = new Vector3(-ballSpot.x, 0f, -ballSpot.z);

                    if (inward.sqrMagnitude < 0.01f)
                    {
                        inward = Vector3.forward;
                    }

                    inward.Normalize();

                    pushed = PitchBounds.ClampPlayer(new Vector3(
                        ballSpot.x + (inward.x * exclusionRadius),
                        position.y,
                        ballSpot.z + (inward.z * exclusionRadius)));
                }

                member.transform.position = pushed;

                if (member.TryGetComponent(out PlayerRoute route))
                {
                    route.CancelRoute();
                }

                moved++;
            }

            if (moved > 0)
            {
                Debug.Log($"[Saque] {moved} jugador(es) de {(takerMember.team == TeamId.Blue ? TeamId.Red : TeamId.Blue)} " +
                          $"retirados a {exclusionRadius:F1} u del balón.");
            }
        }

        // Acerca a los compañeros del sacador hacia el balón para ofrecer una opción de pase, según su línea.
        public static void OfferForRestart(PlayerBallHandler taker, Vector3 ballSpot, float supportClearance)
        {
            if (!taker.TryGetComponent(out TeamMember takerMember))
            {
                return;
            }

            foreach (TeamMember member in Object.FindObjectsByType<TeamMember>())
            {
                if (member.team != takerMember.team || member == takerMember
                    || !member.isStarter || member.role == PlayerRole.Goalkeeper)
                {
                    continue;
                }

                float pull = RestartSupportPull(member.role);

                if (pull <= 0f || !member.TryGetComponent(out TacticalPositioning positioning))
                {
                    continue;
                }

                // Interpola entre el puesto de formación y el balón, para que el saque siempre tenga la misma forma.
                Vector3 slot = positioning.FormationSlot;
                Vector3 target = Vector3.Lerp(slot, ballSpot, pull);

                // Evita que el compañero quede justo encima del balón.
                Vector3 away = target - ballSpot;
                away.y = 0f;

                if (away.magnitude < supportClearance)
                {
                    away = away.sqrMagnitude > 0.0001f ? away.normalized : Vector3.forward;
                    target = ballSpot + (away * supportClearance);
                }

                target.y = member.transform.position.y;

                if (member.TryGetComponent(out PlayerRoute route))
                {
                    route.CancelRoute();
                }

                member.transform.position = PitchBounds.ClampPlayer(target);
            }
        }

        // Cuánto se acerca cada línea de jugadores hacia el saque, entre 0 y 1.
        public static float RestartSupportPull(PlayerRole role)
        {
            switch (role)
            {
                case PlayerRole.Forward: return 0.75f;
                case PlayerRole.Midfielder: return 0.4f;
                default: return 0f;
            }
        }

        // Ajusta la marca del saque para que quede dentro de las líneas del campo.
        public static Vector3 ClampToRestartArea(Vector3 spot)
        {
            float maxX = PitchBounds.SideLineX - RestartBallInset;
            float maxZ = PitchBounds.GoalLineZ - RestartBallInset;

            return new Vector3(
                Mathf.Clamp(spot.x, -maxX, maxX),
                spot.y,
                Mathf.Clamp(spot.z, -maxZ, maxZ));
        }

        // Busca el compañero más cercano a una distancia mínima para recibir el saque. Excluye al portero.
        public static TeamMember FindRestartReceiver(PlayerBallHandler taker, float passMinDistance)
        {
            if (!taker.TryGetComponent(out TeamMember takerMember))
            {
                return null;
            }

            TeamMember best = null;
            float bestSqr = float.MaxValue;

            float minSqr = passMinDistance * passMinDistance;

            foreach (TeamMember member in Object.FindObjectsByType<TeamMember>())
            {
                if (member.team != takerMember.team || member == takerMember
                    || !member.isStarter || member.isGoalkeeper)
                {
                    continue;
                }

                float sqr = (member.transform.position - taker.transform.position).sqrMagnitude;

                if (sqr < minSqr || sqr >= bestSqr)
                {
                    continue;
                }

                bestSqr = sqr;
                best = member;
            }

            return best;
        }

        // Jugador de campo titular más cercano a un punto, excluyendo porteros y opcionalmente a un jugador.
        public static PlayerBallHandler FindNearestFieldPlayer(TeamId team, Vector3 point,
            PlayerBallHandler exclude = null)
        {
            return FindNearestFieldPlayer(team, point, exclude, null);
        }

        // Igual, pero restringido a un rol concreto si se indica.
        public static PlayerBallHandler FindNearestFieldPlayer(TeamId team, Vector3 point,
            PlayerBallHandler exclude, PlayerRole? onlyRole)
        {
            PlayerBallHandler closest = null;
            float closestSqrDistance = float.MaxValue;

            foreach (TeamMember member in Object.FindObjectsByType<TeamMember>())
            {
                if (member.team != team || member.isGoalkeeper || !member.isStarter)
                {
                    continue;
                }

                if (onlyRole.HasValue && member.role != onlyRole.Value)
                {
                    continue;
                }

                if (!member.TryGetComponent(out PlayerBallHandler handler) || handler == exclude)
                {
                    continue;
                }

                float sqrDistance = (member.transform.position - point).sqrMagnitude;

                if (sqrDistance < closestSqrDistance)
                {
                    closestSqrDistance = sqrDistance;
                    closest = handler;
                }
            }

            return closest;
        }

        // Elige quién saca: prioriza un centrocampista, luego un defensa, y solo si no hay otro, un delantero.
        public static PlayerBallHandler FindRestartTaker(TeamId team, Vector3 point)
        {
            PlayerBallHandler midfielder = FindNearestFieldPlayer(team, point, null, PlayerRole.Midfielder);

            if (midfielder != null)
            {
                return midfielder;
            }

            PlayerBallHandler defender = FindNearestFieldPlayer(team, point, null, PlayerRole.Defender);

            if (defender != null)
            {
                return defender;
            }

            // Último recurso: cualquier jugador de campo disponible.
            return FindNearestFieldPlayer(team, point);
        }
    }
}
