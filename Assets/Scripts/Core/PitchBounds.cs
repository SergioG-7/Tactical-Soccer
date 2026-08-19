using UnityEngine;
using TacticalSoccer.Gameplay;

namespace TacticalSoccer.Core
{
    /// <summary>
    /// The one place that knows where the pitch ends.
    ///
    /// Two different limits live here on purpose. The BALL goes out at the
    /// painted line, because that is the rule. A PLAYER is merely stopped a
    /// metre further out, because walking off the map is a bug, not a foul —
    /// and a player who steps over the line while carrying loses the ball
    /// through the ball's own check rather than through a wall.
    /// </summary>
    public static class PitchBounds
    {
        // The painted line, not the edge of the turf: the pitch plane is
        // 30 x 50 but the boundary is drawn a 5% texture margin inside it,
        // which is 1.52 units.
        public const float SideLineX = 13.5f;
        public const float GoalLineZ = 23.5f;

        /// <summary>Half the goal width: the only stretch of goal line a ball may cross.</summary>
        public const float GoalMouthHalfWidth = 3.5f;

        /// <summary>Back of the net. Past this even a shot is gone.</summary>
        public const float BehindGoalZ = 25.5f;

        /// <summary>Backstop for the ball tunnelling through the pitch at speed.</summary>
        public const float FallThroughFloorY = -5f;

        /// <summary>
        /// How far the penalty area reaches out from the goal line.
        /// </summary>
        public const float PenaltyAreaDepth = 12f;

        /// <summary>
        /// Half the width of the penalty area. Comfortably wider than the goal
        /// mouth (3.5) so that a foul out by the post is still a penalty, which
        /// is the whole reason the box is drawn wider than the goal in the real
        /// game.
        /// </summary>
        public const float PenaltyAreaHalfWidth = 8f;

        /// <summary>
        /// True when a point lies inside the box <paramref name="defendingTeam"/>
        /// defends — the one where a foul by them is a penalty.
        ///
        /// The team is a parameter rather than being worked out from the sign of
        /// Z because the question is always asked about a specific offender: the
        /// same spot is a penalty against one side and a free kick nowhere near
        /// goal for the other.
        /// </summary>
        public static bool IsInsidePenaltyArea(Vector3 position, TeamId defendingTeam)
        {
            if (Mathf.Abs(position.x) > PenaltyAreaHalfWidth)
            {
                return false;
            }

            // How deep into their own half the point is, measured towards the
            // goal this team defends.
            float depth = position.z * DefendedSide(defendingTeam);

            return depth >= GoalLineZ - PenaltyAreaDepth && depth <= BehindGoalZ;
        }

        // A metre of run-off outside the line. Players may overrun the
        // boundary — they just cannot leave the world.
        public const float PlayerLimitX = 14.5f;
        public const float PlayerLimitZ = 24.5f;

        // Where a keeper is allowed to stand when placed by hand: across the
        // goal mouth and within a stride of their own line.
        private const float KeeperLineZ = 21.5f;
        private const float KeeperDepth = 2f;

        /// <summary>
        /// Which end of the pitch a side defends, as a sign on Z. Blue defends
        /// south (negative Z); Red defends north.
        ///
        /// The convention itself was already written out by hand in four
        /// different places — the kickoff, the out-of-play ruling, the keeper
        /// clamp and the AI's shooting target — and one of them disagreeing with
        /// the others is the kind of bug that only shows up as a team attacking
        /// its own goal.
        /// </summary>
        public static float DefendedSide(TeamId team)
        {
            return team == TeamId.Blue ? -1f : 1f;
        }

        /// <summary>
        /// True when <paramref name="point"/> is at the goal <paramref name="team"/>
        /// is defending — i.e. the one they must not shoot into.
        ///
        /// Only the side of the halfway line is tested, because the callers ask
        /// this about a point that already hit a goal collider: there are two
        /// goals and the sign of Z names which one outright.
        /// </summary>
        public static bool IsOwnGoal(Vector3 point, TeamId team)
        {
            return Mathf.Sign(point.z) == DefendedSide(team);
        }

        /// <summary>
        /// How close to their own goal line a player may aim before the target
        /// is treated as "at my own net" rather than "back towards my own half".
        ///
        /// Measured from the goal line, so it covers the six-yard area and the
        /// net behind it — the whole stretch where a ball played that way ends up
        /// in the wrong side of the posts.
        /// </summary>
        public const float OwnGoalDangerDepth = 4f;

        /// <summary>
        /// True when a point is close enough to <paramref name="team"/>'s own
        /// goal that playing the ball there risks putting it in.
        ///
        /// Wider than a test against the goal collider: a tap that lands on the
        /// GRASS a metre in front of your own line is just as dangerous as one
        /// that lands in the net, and the ball does not care which collider the
        /// finger happened to hit.
        /// </summary>
        public static bool IsNearOwnGoal(Vector3 point, TeamId team)
        {
            if (Mathf.Abs(point.x) > GoalMouthHalfWidth + OwnGoalDangerDepth)
            {
                return false;
            }

            float depth = point.z * DefendedSide(team);

            return depth >= GoalLineZ - OwnGoalDangerDepth;
        }

        /// <summary>
        /// Pulls a target out of a team's own goal mouth, back to a safe distance
        /// in front of their line.
        ///
        /// Only Z is moved. The player asked for the ball to go in that
        /// direction and the sideways part of that is perfectly playable — it is
        /// only the last few metres towards their own net that must not be
        /// honoured.
        /// </summary>
        public static Vector3 PushOutOfOwnGoal(Vector3 point, TeamId team)
        {
            if (!IsNearOwnGoal(point, team))
            {
                return point;
            }

            float side = DefendedSide(team);

            return new Vector3(point.x, point.y, side * (GoalLineZ - OwnGoalDangerDepth));
        }

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

            // Past the goal line: legal only between the posts, and only as far
            // as the back of the net.
            return Mathf.Abs(position.x) <= GoalMouthHalfWidth
                && Mathf.Abs(position.z) <= BehindGoalZ;
        }

        /// <summary>
        /// Keeps a player on the map. Y is passed through untouched: height is
        /// the capsule's business, not the pitch's.
        /// </summary>
        public static Vector3 ClampPlayer(Vector3 position)
        {
            return new Vector3(
                Mathf.Clamp(position.x, -PlayerLimitX, PlayerLimitX),
                position.y,
                Mathf.Clamp(position.z, -PlayerLimitZ, PlayerLimitZ));
        }

        /// <summary>
        /// Where a player may be dropped during a kickoff. Tighter than
        /// <see cref="ClampPlayer"/>: you arrange your own half, not the whole
        /// pitch, and the keeper stays in his goal.
        /// </summary>
        public static Vector3 ClampKickoffPlacement(Vector3 position, TeamId team, bool isGoalkeeper)
        {
            // Blue defends south (negative Z); Red defends north.
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

            // Own half only. The halfway line is the far edge of it.
            float minZ = ownSide < 0f ? -PlayerLimitZ : 0f;
            float maxZ = ownSide < 0f ? 0f : PlayerLimitZ;

            return new Vector3(
                Mathf.Clamp(position.x, -PlayerLimitX, PlayerLimitX),
                position.y,
                Mathf.Clamp(position.z, minZ, maxZ));
        }
    }
}
