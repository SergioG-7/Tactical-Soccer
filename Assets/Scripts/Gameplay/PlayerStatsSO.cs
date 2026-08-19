using UnityEngine;

namespace TacticalSoccer.Gameplay
{
    /// <summary>
    /// A player's RPG attributes, held as an asset rather than as inspector
    /// fields on the prefab: several players share one stat block, and tuning
    /// the block retunes every player wearing it without touching the scene.
    ///
    /// The stats pair off by duel. Tackle duels: dribble/power against
    /// tackle/block. Shot duels: shoot against goalkeeping.
    /// </summary>
    [CreateAssetMenu(fileName = "NewPlayerStats", menuName = "Tactical Soccer/Player Stats")]
    public class PlayerStatsSO : ScriptableObject
    {
        [Header("Ataque")]
        [Tooltip("Regate: esquivar al defensor conservando el balón.")]
        public int dribble = 50;

        [Tooltip("Fuerza: cargar contra el defensor por la vía directa.")]
        public int power = 50;

        [Tooltip("Tiro: potencia y colocación al rematar a puerta.")]
        public int shoot = 50;

        [Header("Defensa")]
        [Tooltip("Entrada: arrebatar el balón al portador.")]
        public int tackle = 50;

        [Tooltip("Bloqueo: plantarse y aguantar la embestida.")]
        public int block = 50;

        [Tooltip("Parada: detener un remate bajo palos.")]
        public int goalkeeping = 50;

        [Header("Movimiento")]
        public float moveSpeed = 5f;
    }
}
