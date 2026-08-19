using UnityEngine;

namespace TacticalSoccer.Gameplay
{
    /// <summary>
    /// Momentum, as a bar each side fills by winning things.
    ///
    /// The bar is earned in duels and interceptions rather than by holding the
    /// ball or by time on the pitch: those reward the side already on top, and
    /// the point of momentum is that a side under pressure can build it by
    /// standing up to that pressure. Winning a tackle is worth more than the
    /// consolation a losing side gets, but the loser gets something — otherwise
    /// a team being overrun can never climb out.
    ///
    /// Once full it burns: <see cref="BurnDuration"/> seconds of a flat duel
    /// bonus and a real turn of pace, then the bar is spent and has to be
    /// rebuilt from nothing.
    ///
    /// Held per side in two plain fields rather than in a dictionary keyed by
    /// TeamId: there are exactly two teams, this is read every frame by the
    /// movement code, and an enum-keyed lookup would box the key on every call.
    /// </summary>
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

        /// <summary>Seconds a burn lasts. Read by the UI so the bar can drain in step.</summary>
        public float BurnDuration => burnDuration;

        public float MaxTension => maxTension;

        private void Awake()
        {
            Instance = this;

            // Statics survive a domain reload when fast enter-play mode is on,
            // which would otherwise open a match mid-burn.
            blueTension = 0f;
            redTension = 0f;
            blueBurnRemaining = 0f;
            redBurnRemaining = 0f;
        }

        // Deliberately NOT subscribed to OnMatchReset. That event fires on every
        // restart from the centre — which means every goal — and wiping the bars
        // there threw away momentum at the exact moment a side had earned the
        // most of it: score, and lose your zone as the reward. The bars now
        // survive goals, halves and substitutions, and are only cleared when a
        // whole new match begins, through ResetAll from MatchManager.RestartMatch.

        private void OnDisable()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>
        /// Drains an active burn, but only while the ball is actually in play.
        ///
        /// IsWaitingForSetPiece covers both cases that matter here: the states
        /// where timeScale is 0 (a duel, the interval, a penalty), where
        /// Time.deltaTime is zero anyway, AND the states where the match runs
        /// at full speed but nobody may act — a goal celebration, a corner
        /// being lined up, the wait before a kickoff. The zone lasts ten
        /// seconds, so two seconds of it burning away while the players stand
        /// still waiting for a restart is a fifth of the reward gone for
        /// nothing.
        /// </summary>
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

        /// <summary>
        /// Keeps the burn fanfare in step with the zone.
        ///
        /// Driven off "is ANYBODY burning" rather than off either side's own
        /// timer, because both can be in the zone at once and stopping the loop
        /// when the first one drops out would cut the second one's music off
        /// halfway through. Both calls are idempotent, so running this every
        /// frame costs a bool check and never restarts a loop mid-phrase.
        /// </summary>
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

        /// <summary>How full this side's bar is, 0..1. For anything drawing it.</summary>
        public float Fraction(TeamId team)
        {
            if (maxTension <= 0f)
            {
                return 0f;
            }

            // While burning the bar shows what is LEFT of the burn rather than
            // the charge that bought it: the charge is spent, and a bar sitting
            // full through the whole zone would give no sense of it running out.
            if (IsBurning(team))
            {
                return Mathf.Clamp01(Remaining(team) / burnDuration);
            }

            return Mathf.Clamp01(Current(team) / maxTension);
        }

        public float Current(TeamId team)
        {
            return team == TeamId.Blue ? blueTension : redTension;
        }

        public float Remaining(TeamId team)
        {
            return team == TeamId.Blue ? blueBurnRemaining : redBurnRemaining;
        }

        /// <summary>True while this side is in the zone.</summary>
        public bool IsBurning(TeamId team)
        {
            return Remaining(team) > 0f;
        }

        /// <summary>Duel stat bonus this side is carrying right now.</summary>
        public int DuelBonus(TeamId team)
        {
            return IsBurning(team) ? burnDuelBonus : 0;
        }

        /// <summary>Speed multiplier this side is carrying right now.</summary>
        public float SpeedMultiplier(TeamId team)
        {
            return IsBurning(team) ? burnSpeedMultiplier : 1f;
        }

        public void AddDuelWon(TeamId team)
        {
            Add(team, duelWonTension);
        }

        public void AddDuelLost(TeamId team)
        {
            Add(team, duelLostTension);
        }

        public void AddIntercept(TeamId team)
        {
            Add(team, interceptTension);
        }

        public void AddPassCompleted(TeamId team)
        {
            Add(team, passCompletedTension);
        }

        /// <summary>
        /// Charges a side's bar, and lights it if that fills it.
        ///
        /// A side already burning gains nothing. Letting it bank charge mid-burn
        /// would let a team that wins two duels in the zone come straight out of
        /// one burn and into another, which is not momentum, it is a lock.
        /// </summary>
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

        /// <summary>
        /// Spends a full bar and starts the burn. The charge is zeroed here, not
        /// when the burn ends: the bar is showing the burn's own countdown by
        /// then, and leaving it full would refill instantly on the next duel.
        /// </summary>
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

            // Started here rather than left to the next Update, so the fanfare
            // lands on the moment the bar fills instead of a frame later — and
            // so it still starts if the zone is lit during a set piece, which
            // Update stands down for.
            if (Audio.AudioManager.Instance != null)
            {
                Audio.AudioManager.Instance.StartTensionLoop();
            }

            Core.TacticalEvents.OnTensionIgnited?.Invoke(team);
        }

        /// <summary>
        /// Wipes both bars. Hooked to the match reset so a new match — or a new
        /// half — never opens with somebody mid-burn from the last one.
        /// </summary>
        public void ResetAll()
        {
            blueTension = 0f;
            redTension = 0f;
            blueBurnRemaining = 0f;
            redBurnRemaining = 0f;

            // Cut rather than left to the next Update: a new match can begin
            // from a menu, where Update has stood down, and the fanfare would
            // otherwise carry over from the last one.
            if (Audio.AudioManager.Instance != null)
            {
                Audio.AudioManager.Instance.StopTensionLoop();
            }
        }
    }
}
