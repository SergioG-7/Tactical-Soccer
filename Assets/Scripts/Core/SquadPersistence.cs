using TacticalSoccer.Gameplay;
using UnityEngine;

namespace TacticalSoccer.Core
{
    /// <summary>
    /// Makes the squad edits outlive the session that made them.
    ///
    /// The squad itself is NOT random and never was: the generator gives every
    /// player a shirt number, a line from the formation table, the stat asset of
    /// his role and a face seeded from that same number, all of it repeatable.
    /// The only part of a player that varies is what somebody typed into the
    /// editing panel — and that lived on the TeamMember in the scene, which
    /// meant it survived a match and a rematch but not closing Unity, and not
    /// regenerating the scene. That is what this restores.
    ///
    /// Only edited players are written. A file that listed all twenty would
    /// freeze today's stat assets into the save as well, so tuning
    /// MidfielderStats.asset afterwards would quietly do nothing for players
    /// nobody ever touched.
    ///
    /// A component rather than a static hook because restoring needs the players
    /// to exist: Start runs after every TeamMember's Awake, which is where the
    /// stamina and the initial-state snapshot are taken.
    /// </summary>
    public class SquadPersistence : MonoBehaviour
    {
        private void OnEnable()
        {
            // Subscribed here rather than in Start so no edit can slip through
            // between the two: OnEnable runs first.
            UI.PlayerEditUIController.OnPlayerEdited += Capture;
        }

        private void OnDisable()
        {
            UI.PlayerEditUIController.OnPlayerEdited -= Capture;
        }

        private void Start()
        {
            Restore();
        }

        /// <summary>
        /// Puts every saved edit back onto its player.
        ///
        /// Roles go through the same swap rule the editing panel used, and that
        /// is not tidiness: promoting somebody to keeper also DEMOTED the old
        /// one, and the old one never raised an edit event, so he has no record
        /// of his own. Replaying the promotion through the same rule demotes him
        /// again, exactly as it did the first time, and the side ends with the
        /// one keeper it must have.
        /// </summary>
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

        /// <summary>
        /// Writes one player into the save, the moment his edit is confirmed.
        ///
        /// Immediate rather than deferred: this is a deliberate change a player
        /// made on purpose, and the next thing they do is likely to be closing
        /// the game to see whether it stuck.
        /// </summary>
        private void Capture(TeamMember member)
        {
            if (member == null)
            {
                return;
            }

            PlayerRecord record = SaveManager.GetOrCreatePlayer((int)member.team, member.jerseyNumber);

            record.role = (int)member.role;
            record.element = (int)member.element;

            // The BASE numbers, not the ones the duel reads: those carry the
            // captain's passive on top, and saving them would bake a bonus that
            // belongs to an armband into the player himself — worth another ten
            // points every time the game was reopened.
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
