using TacticalSoccer.Gameplay;
using UnityEngine;

namespace TacticalSoccer.Core
{
    /// <summary>
    /// Moving a player between lines, and the three things that have to agree
    /// with the move.
    ///
    /// Gathered here because two callers now need it and they must not drift
    /// apart: the editing panel, where a human changes somebody's position, and
    /// the squad restore, which replays those same changes at startup. The
    /// panel keeps the presentation — it is the one that puts the refusal on
    /// screen — and this keeps the rule.
    /// </summary>
    public static class SquadRoles
    {
        /// <summary>
        /// Moves a player to a line, swapping the gloves rather than duplicating
        /// or losing them.
        ///
        /// The goalkeeper is the one role that is not interchangeable, and both
        /// failure modes are silent:
        ///
        ///  - promote somebody while the side already has a keeper and there are
        ///    TWO, with every "find the keeper" search taking the first it meets
        ///    — so half the game defends the goal with one player and half with
        ///    the other;
        ///  - demote the only keeper and there are NONE, and a shot has nothing
        ///    to resolve against.
        ///
        /// So a promotion is a SWAP: the outgoing keeper takes the line the
        /// incoming one is leaving. A demotion of the last keeper is refused,
        /// because there is nobody to hand the gloves to and picking a
        /// replacement would be this code making a squad decision on the
        /// player's behalf.
        /// </summary>
        /// <returns>False when the change was refused, with the reason in <paramref name="refusal"/>.</returns>
        public static bool TrySetRole(TeamMember member, PlayerRole newRole, out string refusal)
        {
            refusal = string.Empty;

            if (member == null || member.role == newRole)
            {
                return true;
            }

            TeamMember currentKeeper = FindKeeper(member.team);

            if (newRole == PlayerRole.Goalkeeper)
            {
                if (currentKeeper != null && currentKeeper != member)
                {
                    Write(currentKeeper, member.role);
                }

                Write(member, PlayerRole.Goalkeeper);
                return true;
            }

            if (member.role == PlayerRole.Goalkeeper && currentKeeper == member)
            {
                refusal = LocalizationManager.GetText("edit.noKeeper");
                return false;
            }

            Write(member, newRole);
            return true;
        }

        /// <summary>
        /// Writes a role and the state that has to match it. The mechanic, with
        /// no policy in it — <see cref="TrySetRole"/> is what decides whether the
        /// move is allowed at all.
        ///
        /// isGoalkeeper is a separate field on TeamMember, because gameplay finds
        /// the keeper through it rather than reaching into the AI layer: a role
        /// written without it produces a player the formation treats as a keeper
        /// and the duel code does not. The GoalkeeperAI component follows for the
        /// same reason in reverse — left enabled on a midfielder it walks him
        /// back onto his own goal line — and the formation slot follows because
        /// otherwise he keeps the station of the line he no longer plays.
        /// </summary>
        public static void Write(TeamMember member, PlayerRole newRole)
        {
            if (member == null)
            {
                return;
            }

            member.role = newRole;
            member.isGoalkeeper = newRole == PlayerRole.Goalkeeper;

            if (member.TryGetComponent(out AI.GoalkeeperAI keeperAI))
            {
                keeperAI.enabled = member.isGoalkeeper;
            }

            if (member.TryGetComponent(out AI.TacticalPositioning positioning))
            {
                positioning.SetFormationSlot(MatchManager.ResolveFormationSlot(member));
            }

            RefreshTeamMateCaches(member.team);
        }

        /// <summary>
        /// Rebuilds every player's cached team-mate list for a side after a
        /// role change flips who is (and isn't) the goalkeeper.
        ///
        /// isGoalkeeper only changed on the two players who swapped, but
        /// TacticalPositioning.CacheTeamMates filters ITS OWN list by that flag
        /// at the moment it runs — so it is every OTHER player on the side
        /// whose cache is now wrong, not the two who swapped. Cheap enough to
        /// run unconditionally: one role edit, once, over one XI.
        /// </summary>
        private static void RefreshTeamMateCaches(TeamId team)
        {
            foreach (TeamMember other in Object.FindObjectsByType<TeamMember>())
            {
                if (other.team != team)
                {
                    continue;
                }

                if (other.TryGetComponent(out AI.TacticalPositioning positioning))
                {
                    positioning.CacheTeamMates();
                }
            }
        }

        /// <summary>The player currently wearing the gloves for a side, or null if nobody is.</summary>
        public static TeamMember FindKeeper(TeamId team)
        {
            foreach (TeamMember member in Object.FindObjectsByType<TeamMember>())
            {
                if (member.team == team && member.isGoalkeeper)
                {
                    return member;
                }
            }

            return null;
        }
    }
}
