using UnityEngine;
using TacticalSoccer.Core;
using TacticalSoccer.Gameplay;
using TacticalSoccer.Player;

namespace TacticalSoccer.AI
{
    /// <summary>
    /// The positioning math behind a dead-ball restart: who takes it, who is
    /// pushed clear of the mark, and who walks in to support.
    ///
    /// Used to live as private methods on MatchManager, which is the match
    /// clock and state machine, not a positioning system — this is pure
    /// geometry/roster search with no state of its own, so it moved out
    /// wholesale. MatchManager keeps thin wrapper methods with the original
    /// names and signatures (so every call site — kickoff, throw-in, corner,
    /// goal kick, free kick — is untouched) that just forward into here with
    /// its own tunables.
    /// </summary>
    public static class SetPiecePositioning
    {
        // Pulled a restart mark just inside the painted lines, so the ball
        // placed on it is unambiguously in play.
        private const float RestartBallInset = 0.2f;

        /// <summary>
        /// Walks every non-taking outfield player of the OTHER team back to
        /// the exclusion radius, so a restart is never taken with an opponent
        /// standing over the ball.
        /// </summary>
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

                // Flat distance: the ball's height at a restart is the socket's,
                // and nobody is closer for standing on lower ground.
                Vector3 away = new Vector3(position.x - ballSpot.x, 0f, position.z - ballSpot.z);
                float distance = away.magnitude;

                if (distance >= exclusionRadius)
                {
                    continue;
                }

                // Standing exactly on the ball leaves no line to push along, so
                // the retreat is towards the player's own goal — which is where
                // a defender backing off would go anyway.
                Vector3 direction = distance > 0.01f
                    ? away / distance
                    : new Vector3(0f, 0f, member.team == TeamId.Blue ? -1f : 1f);

                Vector3 pushed = PitchBounds.ClampPlayer(new Vector3(
                    ballSpot.x + (direction.x * exclusionRadius),
                    position.y,
                    ballSpot.z + (direction.z * exclusionRadius)));

                // The clamp can hand back a spot still inside the circle — a
                // corner is the obvious case, where "straight out" is straight
                // off the pitch. Then the retreat goes towards the middle
                // instead, which is always somewhere there is room.
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

        /// <summary>
        /// Walks the taker's team-mates into somewhere worth passing to.
        ///
        /// Without this a throw-in or a corner is taken into an empty half of
        /// the pitch: everybody else is standing on their formation slot, which
        /// is where the shape says they belong and not where a restart needs
        /// them. Each line offers itself by a different amount, for the same
        /// reason the off-the-ball drift does:
        ///
        ///  - forwards come to the ball, because they are the pass;
        ///  - midfielders come half way, because they are the outlet if the
        ///    first ball is not on;
        ///  - defenders stay where they are. A defender who followed the ball
        ///    into the corner is a defender who is not behind it when the
        ///    restart is lost, and losing a throw-in should not be the same
        ///    thing as conceding a counter-attack.
        ///
        /// Positions are written directly. The brief suggested NavMeshAgent.Warp
        /// but this project has no NavMesh at all — the players move by
        /// coroutine along drawn routes and by the drift, neither of which is
        /// agent-based — so there is no agent to warp.
        /// </summary>
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

                // Interpolated from the formation slot rather than from where the
                // player happens to be standing, so a restart always produces the
                // same shape instead of compounding wherever the last passage of
                // play left everybody.
                Vector3 slot = positioning.FormationSlot;
                Vector3 target = Vector3.Lerp(slot, ballSpot, pull);

                // Never on top of the ball: a team-mate standing on the mark
                // blocks the taker and, worse, can trip a duel on the restart.
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

        /// <summary>How far each line travels from its slot towards the restart, 0..1.</summary>
        public static float RestartSupportPull(PlayerRole role)
        {
            switch (role)
            {
                case PlayerRole.Forward: return 0.75f;
                case PlayerRole.Midfielder: return 0.4f;
                default: return 0f;
            }
        }

        /// <summary>
        /// Pulls a restart mark just inside the painted lines, so the ball
        /// placed on it is unambiguously in play.
        /// </summary>
        public static Vector3 ClampToRestartArea(Vector3 spot)
        {
            float maxX = PitchBounds.SideLineX - RestartBallInset;
            float maxZ = PitchBounds.GoalLineZ - RestartBallInset;

            return new Vector3(
                Mathf.Clamp(spot.x, -maxX, maxX),
                spot.y,
                Mathf.Clamp(spot.z, -maxZ, maxZ));
        }

        /// <summary>
        /// Who the AI restarts to: the nearest team-mate who is far enough away
        /// for the pass to be worth making.
        ///
        /// The minimum distance is the point. Without it the answer is whichever
        /// support player was pushed to the edge of the clearance radius, and a
        /// four-metre pass from a corner flag achieves nothing except giving the
        /// ball straight back to the defence.
        ///
        /// The keeper is excluded. He is often the closest available player at a
        /// goal kick, and passing to him restarts the same set piece.
        /// </summary>
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

        /// <summary>
        /// The nearest eligible outfield player to a point. Goalkeepers never
        /// come out to take a corner or an empty-net throw-in. So are
        /// substitutes, who would otherwise be walked out of the dugout.
        ///
        /// <paramref name="exclude"/> is for the kickoff, which has to find
        /// somebody to pass TO: the taker is standing on the ball and would win
        /// any nearest-player search against himself at zero distance.
        /// </summary>
        public static PlayerBallHandler FindNearestFieldPlayer(TeamId team, Vector3 point,
            PlayerBallHandler exclude = null)
        {
            return FindNearestFieldPlayer(team, point, exclude, null);
        }

        /// <summary>
        /// Same, restricted to one line when <paramref name="onlyRole"/> is set.
        /// The basis of the taker preference below.
        /// </summary>
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

        /// <summary>
        /// Who takes a restart: a midfielder if there is one, then a defender,
        /// and a forward only if nobody else can.
        ///
        /// By line rather than by distance, which is what it used to be. The
        /// nearest player to a corner flag is very often a forward — they are the
        /// ones camped in that third — and sending the forward to fetch the ball
        /// empties the box of the exact player the cross is meant to find. A
        /// midfielder walking twenty metres to take it is not a cost: the ball is
        /// dead and the clock is stopped.
        ///
        /// Within each line it is still the nearest, so the shortest walk of the
        /// right kind of player wins.
        /// </summary>
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

            // Last resort, and it still has to work: a side reduced to forwards
            // by substitutions must be able to take its own throw-in.
            return FindNearestFieldPlayer(team, point);
        }
    }
}
