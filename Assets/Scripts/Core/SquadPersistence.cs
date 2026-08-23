using TacticalSoccer.Gameplay;
using UnityEngine;

namespace TacticalSoccer.Core
{
    // Guarda y restaura los cambios hechos a la plantilla entre sesiones.
    public class SquadPersistence : MonoBehaviour
    {
        // Se suscribe a los eventos de edición de jugadores.
        private void OnEnable()
        {
            UI.PlayerEditUIController.OnPlayerEdited += Capture;
        }

        private void OnDisable()
        {
            UI.PlayerEditUIController.OnPlayerEdited -= Capture;
        }

        // Restaura los cambios guardados sobre la plantilla actual.
        private void Start()
        {
            Restore();
        }

        // Aplica a cada jugador de la escena los cambios guardados que le correspondan.
        public static void Restore()
        {
            if (SaveManager.Data.squad.Count == 0)
            {
                return;
            }

            int restored = 0;
            int refused = 0;

            foreach (TeamMember member in FindObjectsByType<TeamMember>())
            {
                PlayerRecord record = SaveManager.FindPlayer((int)member.team, member.jerseyNumber);

                if (record == null)
                {
                    continue;
                }

                member.element = (Element)record.element;

                member.ApplyStatEdits(record.dribble, record.power, record.shoot,
                    record.tackle, record.block, record.goalkeeping, record.maxStamina);

                if (!SquadRoles.TrySetRole(member, (PlayerRole)record.role, out string refusal))
                {
                    Debug.LogWarning($"[Plantilla] #{member.jerseyNumber} ({member.team}) " +
                                     $"mantiene su posición: {refusal}");
                    refused++;
                }

                restored++;
            }

            Debug.Log($"[Plantilla] {restored} jugador(es) restaurado(s) desde " +
                      $"{SaveManager.FileName}" + (refused > 0 ? $", {refused} con la posición rechazada." : "."));
        }

        // Guarda de inmediato los datos editados de un jugador.
        private void Capture(TeamMember member)
        {
            if (member == null)
            {
                return;
            }

            PlayerRecord record = SaveManager.GetOrCreatePlayer((int)member.team, member.jerseyNumber);

            record.role = (int)member.role;
            record.element = (int)member.element;

            // Se guardan las estadísticas base, sin el bonus de capitán.
            record.dribble = member.BaseDribble;
            record.power = member.BasePower;
            record.shoot = member.BaseShoot;
            record.tackle = member.BaseTackle;
            record.block = member.BaseBlock;
            record.goalkeeping = member.BaseGoalkeeping;

            record.maxStamina = member.maxStamina;

            SaveManager.SaveNow();
        }
    }
}
