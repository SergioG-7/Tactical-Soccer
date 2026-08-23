using UnityEngine;

namespace TacticalSoccer.Gameplay
{
    // Estadísticas de un jugador, guardadas como asset para poder compartirlas entre varios jugadores.
    [CreateAssetMenu(fileName = "NewPlayerStats", menuName = "Tactical Soccer/Player Stats")]
    public class PlayerStatsSO : ScriptableObject
    {
        [Tooltip("Regate: esquivar al defensor conservando el balón.")]
        public int dribble = 50;

        [Tooltip("Fuerza: cargar contra el defensor por la vía directa.")]
        public int power = 50;

        [Tooltip("Tiro: potencia y colocación al rematar a puerta.")]
        public int shoot = 50;

        [Tooltip("Entrada: arrebatar el balón al portador.")]
        public int tackle = 50;

        [Tooltip("Bloqueo: plantarse y aguantar la embestida.")]
        public int block = 50;

        [Tooltip("Parada: detener un remate bajo palos.")]
        public int goalkeeping = 50;

        public float moveSpeed = 5f;
    }
}
