using TacticalSoccer.Gameplay;
using UnityEngine;

namespace TacticalSoccer.Core
{
    // Cambia el rol de un jugador y mantiene sincronizado todo lo que depende de él (IA, formación, portero).
    public static class SquadRoles
    {
        // Intenta cambiar el rol de un jugador. Si el nuevo rol es portero, hace un intercambio con el portero actual; si se intenta quitar al único portero, se rechaza.
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

        // Asigna el rol a un jugador y actualiza su IA de portero y su slot de formación en consecuencia.
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

        // Reconstruye la caché de compañeros de equipo de todos los jugadores tras un cambio de portero.
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

        // Devuelve el portero actual de un equipo, o null si no tiene.
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
