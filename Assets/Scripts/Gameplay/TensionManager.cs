using UnityEngine;

namespace TacticalSoccer.Gameplay
{
    // Gestiona la barra de tensión de cada equipo y la zona de ardor que se activa al llenarla.
    public class TensionManager : MonoBehaviour
    {
        [Header("Carga")]
        [Tooltip("Full bar. The numbers below are all shares of this, so the " +
                 "scale can be re-tuned without touching what anything is worth.")]
        [SerializeField] private float maxTension = 100f;

        [Tooltip("Gained by the winner of a duel. Four clean wins fill the bar.")]
        [SerializeField] private float duelWonTension = 25f;

        [Tooltip("Gained by the loser of a duel. Small, but not nothing: a side " +
                 "being overrun still has to have a way back into the match.")]
        [SerializeField] private float duelLostTension = 5f;

        [Tooltip("Gained for cutting out a pass. Worth more than a duel win: an " +
                 "interception is read rather than rolled for, and it is the one " +
                 "defensive act the game has that is purely the player's doing.")]
        [SerializeField] private float interceptTension = 30f;

        [Tooltip("Gained for a pass that finds its man. Small by design — a " +
                 "quarter of a duel — because passing is the safe thing to do " +
                 "and it happens far more often. It is here so that keeping the " +
                 "ball builds momentum at all, rather than momentum belonging " +
                 "only to whoever wins collisions.")]
        [SerializeField] private float passCompletedTension = 8f;

        [Header("Zona de Ardor")]
        [Tooltip("How long the burn lasts once the bar fills, in seconds of " +
                 "match time. Scaled time on purpose: a duel freezes the match, " +
                 "and burning through a frozen screen would be pure theft.")]
        [SerializeField] private float burnDuration = 10f;

        [Tooltip("Flat bonus added to every duel stat while burning. Roughly a " +
                 "captain's passive and a bit — enough to turn a duel a side " +
                 "would narrowly lose, not enough to win one it has no business " +
                 "winning.")]
        [SerializeField] private int burnDuelBonus = 20;

        [Tooltip("Speed multiplier while burning. The visible half of the " +
                 "mechanic: the duel bonus is a number nobody sees, and this is " +
                 "what makes a burning side read as a side that has clicked.")]
        [SerializeField] private float burnSpeedMultiplier = 1.5f;

        private float blueTension;
        private float redTension;

        private float blueBurnRemaining;
        private float redBurnRemaining;

        public static TensionManager Instance { get; private set; }

        // Duración del ardor en segundos, la usa la UI para animar la barra.
        public float BurnDuration => burnDuration;

        public float MaxTension => maxTension;

        // Inicializa el singleton y pone las barras a cero.
        private void Awake()
        {
            Instance = this;

            blueTension = 0f;
            redTension = 0f;
            blueBurnRemaining = 0f;
            redBurnRemaining = 0f;
        }

        // Las barras no se resetean en cada reinicio de jugada, solo al empezar un partido nuevo (ver ResetAll).

        // Limpia la referencia al singleton al desactivarse.
        private void OnDisable()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        // Descuenta el tiempo de ardor de cada equipo mientras el balón está en juego.
        private void Update()
        {
            if (Core.MatchManager.Instance != null && Core.MatchManager.Instance.IsWaitingForSetPiece)
            {
                return;
            }

            if (blueBurnRemaining > 0f)
            {
                blueBurnRemaining -= Time.deltaTime;

                if (blueBurnRemaining <= 0f)
                {
                    blueBurnRemaining = 0f;
                    Debug.Log("[Tensión] Blue sale de la ZONA DE ARDOR.");
                }
            }

            if (redBurnRemaining > 0f)
            {
                redBurnRemaining -= Time.deltaTime;

                if (redBurnRemaining <= 0f)
                {
                    redBurnRemaining = 0f;
                    Debug.Log("[Tensión] Red sale de la ZONA DE ARDOR.");
                }
            }

            RefreshTensionAudio();
        }

        // Arranca o para el loop de audio de tensión según si algún equipo está ardiendo.
        private void RefreshTensionAudio()
        {
            Audio.AudioManager audio = Audio.AudioManager.Instance;

            if (audio == null)
            {
                return;
            }

            if (IsBurning(TeamId.Blue) || IsBurning(TeamId.Red))
            {
                audio.StartTensionLoop();
                return;
            }

            audio.StopTensionLoop();
        }

        // Devuelve cuánto tiene llena la barra este equipo, entre 0 y 1.
        public float Fraction(TeamId team)
        {
            if (maxTension <= 0f)
            {
                return 0f;
            }

            // Mientras arde, la barra muestra lo que queda de ardor en vez de la carga.
            if (IsBurning(team))
            {
                return Mathf.Clamp01(Remaining(team) / burnDuration);
            }

            return Mathf.Clamp01(Current(team) / maxTension);
        }

        // Tensión acumulada actual de este equipo.
        public float Current(TeamId team)
        {
            return team == TeamId.Blue ? blueTension : redTension;
        }

        // Segundos de ardor que le quedan a este equipo.
        public float Remaining(TeamId team)
        {
            return team == TeamId.Blue ? blueBurnRemaining : redBurnRemaining;
        }

        // Indica si este equipo está en la zona de ardor.
        public bool IsBurning(TeamId team)
        {
            return Remaining(team) > 0f;
        }

        // Bonus de duelo que tiene este equipo ahora mismo.
        public int DuelBonus(TeamId team)
        {
            return IsBurning(team) ? burnDuelBonus : 0;
        }

        // Multiplicador de velocidad que tiene este equipo ahora mismo.
        public float SpeedMultiplier(TeamId team)
        {
            return IsBurning(team) ? burnSpeedMultiplier : 1f;
        }

        // Suma tensión por ganar un duelo.
        public void AddDuelWon(TeamId team)
        {
            Add(team, duelWonTension);
        }

        // Suma tensión por perder un duelo.
        public void AddDuelLost(TeamId team)
        {
            Add(team, duelLostTension);
        }

        // Suma tensión por interceptar un pase.
        public void AddIntercept(TeamId team)
        {
            Add(team, interceptTension);
        }

        // Suma tensión por completar un pase.
        public void AddPassCompleted(TeamId team)
        {
            Add(team, passCompletedTension);
        }

        // Carga la barra de un equipo y activa el ardor si se llena. Si ya está ardiendo no gana nada.
        public void Add(TeamId team, float amount)
        {
            if (amount <= 0f || IsBurning(team))
            {
                return;
            }

            float value = Mathf.Clamp(Current(team) + amount, 0f, maxTension);

            if (team == TeamId.Blue)
            {
                blueTension = value;
            }
            else
            {
                redTension = value;
            }

            if (value < maxTension)
            {
                return;
            }

            Ignite(team);
        }

        // Gasta la barra llena y arranca el ardor del equipo.
        private void Ignite(TeamId team)
        {
            if (team == TeamId.Blue)
            {
                blueTension = 0f;
                blueBurnRemaining = burnDuration;
            }
            else
            {
                redTension = 0f;
                redBurnRemaining = burnDuration;
            }

            Debug.Log($"[Tensión] ¡{team} entra en ZONA DE ARDOR! " +
                      $"+{burnDuelBonus} en duelos y x{burnSpeedMultiplier:F2} de velocidad durante {burnDuration:F0} s.");

            if (Audio.AudioManager.Instance != null)
            {
                Audio.AudioManager.Instance.StartTensionLoop();
            }

            Core.TacticalEvents.OnTensionIgnited?.Invoke(team);
        }

        // Resetea las barras y el ardor de ambos equipos al empezar un partido nuevo.
        public void ResetAll()
        {
            blueTension = 0f;
            redTension = 0f;
            blueBurnRemaining = 0f;
            redBurnRemaining = 0f;

            if (Audio.AudioManager.Instance != null)
            {
                Audio.AudioManager.Instance.StopTensionLoop();
            }
        }
    }
}
