using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TacticalSoccer.Gameplay;
using TacticalSoccer.Player;

namespace TacticalSoccer.Core
{
    // FormationType, AIDifficulty, FormationSlot y Formations están en Formations.cs.

    // Controla el estado global del partido: tiempo, marcador, saques y reinicios de juego.
    public class MatchManager : MonoBehaviour
    {
        [Tooltip("Duración de cada parte del partido en segundos.")]
        public float matchDuration = 45f;

        // Segundos que quedan en la parte actual.
        public float currentTime { get; private set; }

        [Tooltip("Número de la parte en juego (1 para la primera, 2 para la segunda).")]
        public int currentHalf = 1;

        public bool isMatchOver = false;

        // True entre las dos partes, mientras dura el descanso.
        public bool isHalftime { get; private set; }

        // True desde que el jugador pulsa Jugar en la pantalla de título.
        public bool isMatchStarted { get; private set; }

        // True entre un reinicio y el primer pase o tiro.
        public bool isWaitingForKickoff { get; private set; }

        // True mientras se espera un saque de banda.
        public bool isWaitingForThrowIn { get; private set; }

        // True mientras se está preparando un córner.
        public bool isWaitingForCorner { get; private set; }

        // True mientras se está preparando un saque de puerta.
        public bool isWaitingForGoalKick { get; private set; }

        // True mientras se está preparando un tiro libre.
        public bool isWaitingForFreeKick { get; private set; }

        // True desde que se pita un penalti hasta que se lanza.
        public bool isWaitingForPenalty { get; private set; }

        // True mientras se celebra un gol antes de reanudar el juego.
        public bool IsCelebratingGoal { get; private set; }

        [Tooltip("Equipo asignado al jugador humano.")]
        [SerializeField] private TeamId humanTeam = TeamId.Blue;

        [Tooltip("Dificultad de la IA rival.")]
        public AIDifficulty aiDifficulty = AIDifficulty.Normal;

        [Tooltip("Formación táctica inicial del equipo rival.")]
        public FormationType rivalFormation = FormationType.Balanced_2_2_2;

        [Tooltip("Si es true, la formación rival se elige aleatoriamente al iniciar el partido.")]
        public bool randomiseRivalFormation = true;

        [Tooltip("Capitán del equipo azul. Su rol define la pasiva de todo el equipo.")]
        public TeamMember blueCaptain;

        [Tooltip("Capitán del equipo rojo.")]
        public TeamMember redCaptain;

        [Tooltip("Bonificación a estadísticas que otorga el capitán al equipo.")]
        [SerializeField] private int captainStatBonus = 10;

        [Tooltip("Multiplicador del gasto de energía cuando el capitán es centrocampista (ej. 0.8 = -20%).")]
        [SerializeField] private float captainStaminaDrainMultiplier = 0.8f;

        [Tooltip("Modificador de atributos aplicado en los duelos según el nivel de dificultad.")]
        [SerializeField] private int difficultyDuelModifier = 5;

        [Tooltip("Porcentaje de energía restante por debajo del cual la IA realiza cambios en el descanso.")]
        [Range(0f, 1f)]
        [SerializeField] private float tiredSubstitutionFraction = 0.8f;

        [Tooltip("Distancia detrás del punto central a la que se sitúa el jugador que saca de centro.")]
        [SerializeField] private float kickoffTakerOffset = 0.5f;

        [Tooltip("Tiempo de espera antes de que la IA ejecute un balón parado.")]
        [SerializeField] private float aiSetPieceDelay = 1.5f;

        [Tooltip("Duración de la pausa para celebrar un gol antes del reinicio.")]
        [SerializeField] private float goalCelebrationDelay = 2.5f;

        [Tooltip("Retraso tras el pitido final antes de mostrar la pantalla de resultados.")]
        [SerializeField] private float endOfHalfDelay = 2.5f;

        [Tooltip("Distancia desde la línea de meta donde se coloca el portero para el saque de puerta.")]
        [SerializeField] private float goalKickDepth = 3f;

        [Tooltip("Radio de exclusión para despejar a otros jugadores durante una falta.")]
        [SerializeField] private float restartClearanceRadius = 2.5f;

        [Tooltip("Distancia desde la línea de meta a la que se sitúa el punto de penalti.")]
        [SerializeField] private float penaltySpotDepth = 8f;

        [Tooltip("Distancia de pase para los saques de puerta.")]
        [SerializeField] private float goalKickDistance = 16f;

        [Tooltip("Distancia hacia el interior del campo para los saques de banda.")]
        [SerializeField] private float throwInDistance = 8f;

        [Tooltip("Distancia del pase corto en el saque de centro de la IA.")]
        [SerializeField] private float kickoffPassDistance = 7f;

        private const float MatchOverTimeScale = 0f;
        private const float NormalTimeScale = 1f;
        private const float FixedDeltaTimeAtNormalScale = 0.02f;

        private const int HalvesPerMatch = 2;

        // Parte del campo que cuenta como tercio de ataque.
        private const float AttackingThirdShare = 1f / 3f;

        public static MatchManager Instance { get; private set; }

        // True si el partido puede seguir corriendo (no ha terminado).
        public static bool IsPlayable => Instance == null || !Instance.isMatchOver;

        // The kick the camera takes on a goal: a soft 0.3 held for a long 0.5 s.
        private const float GoalShakeIntensity = 0.3f;
        private const float GoalShakeTime = 0.5f;

        // How far time is slowed the instant a goal goes in.
        private const float GoalSlowMotionScale = 0.3f;

        [Tooltip("Duración en segundos reales de la cámara lenta tras marcar un gol.")]
        [SerializeField] private float goalSlowMotionDuration = 1.2f;

        // True mientras el balón está parado, esperando cualquier tipo de reinicio.
        public bool IsWaitingForSetPiece =>
            !isMatchStarted || isHalftime || IsCelebratingGoal || isWaitingForKickoff
            || isWaitingForThrowIn || isWaitingForCorner || isWaitingForGoalKick
            || isWaitingForFreeKick || isWaitingForPenalty;

        // True mientras hay algo ocupando la pantalla: un duelo, un gol o un penalti pendiente.
        private bool IsPitchInterrupted =>
            Gameplay.ClashManager.IsClashActive || IsCelebratingGoal || isWaitingForPenalty;

        // True mientras el menú de penalti está abierto.
        public static bool IsPenaltyPending => Instance != null && Instance.isWaitingForPenalty;

        // True mientras se celebra un gol, para que el chequeo de fuera de juego del balón no salte.
        public static bool IsGoalBeingCelebrated => Instance != null && Instance.IsCelebratingGoal;

        // True si el partido ya ha empezado.
        public static bool IsStarted => Instance == null || Instance.isMatchStarted;

        // True durante el descanso entre partes.
        public static bool IsHalftime => Instance != null && Instance.isHalftime;

        // Equipo que controla el jugador.
        public TeamId HumanTeam => humanTeam;

        // Bonificación o penalización de estadística que da la dificultad elegida a un equipo en los duelos.
        public int DuelModifierFor(TeamId team)
        {
            if (team == humanTeam)
            {
                return 0;
            }

            switch (aiDifficulty)
            {
                case AIDifficulty.Facil: return -difficultyDuelModifier;
                case AIDifficulty.Dificil: return difficultyDuelModifier;
                default: return 0;
            }
        }

        // Multiplicador del tiempo de reacción de la IA según la dificultad.
        public float AiThinkIntervalScale
        {
            get
            {
                switch (aiDifficulty)
                {
                    case AIDifficulty.Facil: return 2f;
                    case AIDifficulty.Dificil: return 0.5f;
                    default: return 1f;
                }
            }
        }

        // Aplica la configuración elegida antes del partido: duración, dificultad, formación rival y equipación.
        public void ConfigureMatch(float halfDurationSeconds, AIDifficulty difficulty,
            bool randomRivalShape, FormationType rivalShape, TeamKit kit)
        {
            matchDuration = Mathf.Max(1f, halfDurationSeconds);
            currentTime = matchDuration;

            aiDifficulty = difficulty;
            randomiseRivalFormation = randomRivalShape;
            rivalFormation = rivalShape;
            humanKit = kit;

            rivalKitColor = Color.red;

            Debug.Log($"Configuración: {matchDuration:F0} s por parte, dificultad {aiDifficulty}, " +
                      $"rival {(randomiseRivalFormation ? "aleatorio" : Formations.GetLabel(rivalFormation))}, " +
                      $"equipación {TeamKits.GetLabel(humanKit)}.");
        }

        // Configura un partido de torneo con la duración, dificultad, formación y color rival dados.
        public void ConfigureTournamentMatch(float halfDurationSeconds, AIDifficulty difficulty,
            FormationType rivalShape, Color rivalColor)
        {
            matchDuration = Mathf.Max(1f, halfDurationSeconds);
            currentTime = matchDuration;

            aiDifficulty = difficulty;
            randomiseRivalFormation = false;
            rivalFormation = rivalShape;

            rivalKitColor = rivalColor;
        }

        [Tooltip("The strip the human side plays in. Chosen on the configuration " +
                 "screen and applied at the opening whistle.")]
        [SerializeField] private TeamKit humanKit = TeamKit.Azul;

        // Color de la equipación rival.
        private Color rivalKitColor = Color.red;

        // Devuelve el color que lleva puesto un equipo ahora mismo.
        public static Color GetTeamColor(TeamId team)
        {
            if (Instance == null)
            {
                return team == TeamId.Blue ? Color.blue : Color.red;
            }

            return team == Instance.humanTeam
                ? TeamKits.GetColor(Instance.humanKit)
                : Instance.rivalKitColor;
        }

        // Repinta ambos equipos con sus equipaciones elegidas, incluyendo suplentes.
        private void ApplyHumanKit()
        {
            int human = TeamKits.RepaintTeam(humanTeam, TeamKits.GetColor(humanKit));
            int rival = TeamKits.RepaintTeam(Opponent(humanTeam), rivalKitColor);

            Debug.Log($"Equipación {TeamKits.GetLabel(humanKit)} aplicada a {human} jugadores de " +
                      $"{humanTeam}; rival repintado en {rivalKitColor} ({rival} jugadores). " +
                      "Porteros incluidos.");
        }

        private Coroutine kickoffRoutine;
        private Coroutine aiSetPieceRoutine;

        [Tooltip("How far the side NOT taking a restart must stand off the ball. " +
                 "Roughly the ten yards of the real laws, scaled to this pitch: " +
                 "far enough that the taker gets a touch away before anybody " +
                 "reaches them, close enough that the defence is not handed a " +
                 "free pass every time.")]
        [SerializeField] private float restartExclusionRadius = 4f;

        // Estadísticas del partido por equipo, para el marcador final.
        private readonly int[] shots = new int[2];
        private readonly int[] fouls = new int[2];
        private readonly int[] passes = new int[2];

        public int ShotsFor(TeamId team) => shots[(int)team];
        public int FoulsFor(TeamId team) => fouls[(int)team];
        public int PassesFor(TeamId team) => passes[(int)team];

        // Suma un tiro a puerta al equipo.
        public void RecordShot(TeamId team)
        {
            shots[(int)team]++;
        }

        // Suma una falta cometida al equipo.
        public void RecordFoul(TeamId team)
        {
            fouls[(int)team]++;
        }

        // Suma un pase completado al equipo.
        public void RecordPass(TeamId team)
        {
            passes[(int)team]++;
        }

        // Pone a cero las estadísticas de ambos equipos.
        private void ResetStatistics()
        {
            for (int i = 0; i < shots.Length; i++)
            {
                shots[i] = 0;
                fouls[i] = 0;
                passes[i] = 0;
            }
        }

        // True cuando el reloj llega a cero pero un ataque sigue vivo (descuento).
        private bool isInStoppageTime;

        // True entre el pitido final y la pantalla que lo sigue.
        private bool isEndingHalf;

        // True mientras se está cerrando la parte, para que no se abran duelos nuevos.
        public static bool IsEndingHalf => Instance != null && Instance.isEndingHalf;

        // Equipo que saca de centro en el próximo reinicio.
        private TeamId kickoffTeam;

        // Inicializa el estado del partido al cargar la escena.
        private void Awake()
        {
            Instance = this;

            currentTime = matchDuration;
            currentHalf = 1;
            isMatchOver = false;
            isMatchStarted = false;
            isHalftime = false;
            isInStoppageTime = false;
            isEndingHalf = false;
            kickoffTeam = humanTeam;
            ClearSetPieceFlags();
        }

        // Se suscribe a los eventos de reinicio, gol y falta.
        private void OnEnable()
        {
            TacticalEvents.OnMatchReset += HandleMatchReset;
            TacticalEvents.OnGoalScored += HandleGoalScored;
            TacticalEvents.OnFoulCommitted += HandleFoul;
        }

        // Se desuscribe de los eventos al desactivarse.
        private void OnDisable()
        {
            TacticalEvents.OnMatchReset -= HandleMatchReset;
            TacticalEvents.OnGoalScored -= HandleGoalScored;
            TacticalEvents.OnFoulCommitted -= HandleFoul;

            if (Instance == this)
            {
                Instance = null;
            }
        }

        // Al marcar un gol, decide que el saque de centro es para el equipo que encajó.
        private void HandleGoalScored(int scoringTeamId)
        {
            TeamId scoringTeam = scoringTeamId == ScoreManager.RedTeamId ? TeamId.Red : TeamId.Blue;

            kickoffTeam = scoringTeam == TeamId.Blue ? TeamId.Red : TeamId.Blue;

            Debug.Log($"Gol de {scoringTeam}: el saque de centro es para {kickoffTeam}.");
        }

        // Muestra el gol en pantalla y luego reinicia desde el centro del campo.
        public void CelebrateGoal()
        {
            if (isMatchOver)
            {
                return;
            }

            if (IsCelebratingGoal)
            {
                return;
            }

            StartCoroutine(GoalCelebrationRoutine());
        }

        // Pone el partido a cámara lenta al marcar, muestra el anuncio y reinicia tras la espera.
        private IEnumerator GoalCelebrationRoutine()
        {
            IsCelebratingGoal = true;

            Time.timeScale = GoalSlowMotionScale;
            Time.fixedDeltaTime = FixedDeltaTimeAtNormalScale * GoalSlowMotionScale;

            if (CameraSystem.TacticalCamera.Instance != null)
            {
                CameraSystem.TacticalCamera.Instance.Shake(GoalShakeIntensity, GoalShakeTime);
            }

            Announce("announce.goal");

            float slowMotion = Mathf.Min(goalSlowMotionDuration, goalCelebrationDelay);

            yield return new WaitForSecondsRealtime(slowMotion);

            Time.timeScale = NormalTimeScale;
            Time.fixedDeltaTime = FixedDeltaTimeAtNormalScale;

            float remaining = goalCelebrationDelay - slowMotion;

            if (remaining > 0f)
            {
                yield return new WaitForSecondsRealtime(remaining);
            }

            IsCelebratingGoal = false;

            if (isMatchOver)
            {
                yield break;
            }

            BallController ball = BallController.Instance;

            if (ball != null)
            {
                ball.ResetToKickoff();
            }
            else
            {
                TacticalEvents.OnMatchReset?.Invoke();
            }
        }

        // Arranca el partido: mete al público, aplica las equipaciones y hace el saque inicial.
        public void StartInitialKickoff()
        {
            isMatchStarted = true;

            if (Audio.AudioManager.Instance != null)
            {
                Audio.AudioManager.Instance.PlayStadiumLoop();
                Audio.AudioManager.Instance.ResumeCrowd();
            }

            ApplyHumanKit();

            kickoffTeam = currentHalf >= HalvesPerMatch ? Opponent(humanTeam) : humanTeam;

            if (currentHalf < HalvesPerMatch)
            {
                SetUpRivalSide();
            }

            BeginKickoff();
        }

        // Coloca al rival en la formación elegida y le asigna un capitán.
        private void SetUpRivalSide()
        {
            TeamId rival = Opponent(humanTeam);

            FormationType shape = randomiseRivalFormation
                ? Formations.Random()
                : rivalFormation;

            ApplyFormation(rival, shape);

            if (redCaptain == null || redCaptain.team != rival || !redCaptain.isStarter)
            {
                redCaptain = PickRandomCaptain(rival);
            }

            SetCaptain(rival, redCaptain);
        }

        // Elige al azar un capitán entre los titulares de campo (sin contar al portero).
        private static TeamMember PickRandomCaptain(TeamId team)
        {
            List<TeamMember> candidates = new List<TeamMember>();

            foreach (TeamMember member in FindObjectsByType<TeamMember>())
            {
                if (member.team == team && member.isStarter && !member.isGoalkeeper)
                {
                    candidates.Add(member);
                }
            }

            return candidates.Count > 0 ? candidates[Random.Range(0, candidates.Count)] : null;
        }

        // Nombra capitán a un jugador y aplica su bonificación a todo el equipo.
        public void SetCaptain(TeamId team, TeamMember captain)
        {
            if (captain != null && captain.team != team)
            {
                Debug.LogWarning($"{captain.name} no juega en {team}: no puede ser su capitán.");
                return;
            }

            if (team == humanTeam)
            {
                blueCaptain = captain;
            }
            else
            {
                redCaptain = captain;
            }

            int attackBonus = 0;
            int defenceBonus = 0;
            float drainMultiplier = 1f;

            if (captain != null)
            {
                switch (captain.role)
                {
                    case PlayerRole.Forward:
                        attackBonus = captainStatBonus;
                        break;

                    case PlayerRole.Midfielder:
                        drainMultiplier = captainStaminaDrainMultiplier;
                        break;

                    default:
                        defenceBonus = captainStatBonus;
                        break;
                }
            }

            foreach (TeamMember member in FindObjectsByType<TeamMember>())
            {
                if (member.team != team)
                {
                    continue;
                }

                member.isCaptain = member == captain;
                member.ApplyCaptainBonuses(attackBonus, defenceBonus, drainMultiplier);
            }

            if (captain == null)
            {
                Debug.LogWarning($"El equipo {team} se queda sin capitán.");
                return;
            }

            Debug.Log($"Capitán de {team}: dorsal {captain.jerseyNumber} " +
                      $"({PlayerRoles.Describe(captain.role)}) — " +
                      $"ataque +{attackBonus}, defensa +{defenceBonus}, desgaste x{drainMultiplier:F2}.");
        }

        // Devuelve el equipo contrario.
        private static TeamId Opponent(TeamId team)
        {
            return team == TeamId.Blue ? TeamId.Red : TeamId.Blue;
        }

        // Coloca a un equipo en la formación dada: reasigna roles y mueve a cada jugador a su puesto.
        public void ApplyFormation(TeamId team, FormationType formation)
        {
            List<TeamMember> outfield = CollectOutfield(team);

            if (outfield.Count != Formations.OutfieldCount)
            {
                Debug.LogWarning($"El equipo {team} tiene {outfield.Count} jugadores de campo " +
                                 $"y la formación espera {Formations.OutfieldCount}. Se colocan los que haya.");
            }

            // Ordena de más atrás a más adelante para que los jugadores más retrasados formen la línea defensiva.
            float attackDirection = team == TeamId.Blue ? 1f : -1f;

            outfield.Sort((a, b) =>
            {
                float advanceA = a.transform.position.z * attackDirection;
                float advanceB = b.transform.position.z * attackDirection;

                int byDepth = advanceA.CompareTo(advanceB);

                return byDepth != 0 ? byDepth : a.transform.position.x.CompareTo(b.transform.position.x);
            });

            FormationSlot[] slots = Formations.Get(formation);
            float side = -attackDirection;

            int assigned = Mathf.Min(outfield.Count, slots.Length);

            for (int i = 0; i < assigned; i++)
            {
                PlaceInSlot(outfield[i], slots[i], side);
            }

            Debug.Log($"Formación {Formations.GetLabel(formation)} aplicada a {team}: " +
                      $"{assigned} jugadores de campo colocados.");
        }

        // Mueve a un jugador a su puesto en la formación y actualiza ruta e IA táctica.
        private static void PlaceInSlot(TeamMember member, FormationSlot slot, float side)
        {
            member.role = slot.Role;

            Vector3 position = new Vector3(slot.X, member.transform.position.y, side * slot.OwnHalfZ);

            if (member.TryGetComponent(out PlayerRoute route))
            {
                route.CancelRoute();
                route.SetFormationSlot(position);
            }

            if (member.TryGetComponent(out AI.TacticalPositioning positioning))
            {
                positioning.SetFormationSlot(position);
            }

            member.transform.position = position;
        }

        // Recopila a los titulares de campo (sin portero) de un equipo.
        private static List<TeamMember> CollectOutfield(TeamId team)
        {
            List<TeamMember> outfield = new List<TeamMember>();

            foreach (TeamMember member in FindObjectsByType<TeamMember>())
            {
                if (member.team != team || member.isGoalkeeper || !member.isStarter)
                {
                    continue;
                }

                outfield.Add(member);
            }

            return outfield;
        }

        // Corre el reloj del partido y decide cuándo termina la parte, respetando el descuento.
        private void Update()
        {
            if (isMatchOver || isHalftime)
            {
                return;
            }

            if (isEndingHalf)
            {
                return;
            }

            if (IsPitchInterrupted)
            {
                return;
            }

            if (IsWaitingForSetPiece)
            {
                if (isInStoppageTime)
                {
                    BeginEndOfHalf("La jugada acaba en balón parado");
                }

                return;
            }

            if (isInStoppageTime)
            {
                if (!IsPromisingAttack())
                {
                    BeginEndOfHalf("La jugada se apaga");
                }

                return;
            }

            currentTime -= Time.deltaTime;

            if (currentTime > 0f)
            {
                return;
            }

            currentTime = 0f;

            if (IsPromisingAttack())
            {
                isInStoppageTime = true;

                Announce("announce.stoppage");
                Debug.Log("TIEMPO CUMPLIDO, pero hay ataque en el último tercio: se juega el descuento.");

                return;
            }

            BeginEndOfHalf("Tiempo cumplido");
        }

        // True si algún jugador lleva el balón en el último tercio del campo que ataca.
        private bool IsPromisingAttack()
        {
            foreach (TeamMember member in FindObjectsByType<TeamMember>())
            {
                if (!member.isStarter)
                {
                    continue;
                }

                if (!member.TryGetComponent(out PlayerBallHandler handler) || !handler.HasBall)
                {
                    continue;
                }

                float advance = member.transform.position.z * -PitchBounds.DefendedSide(member.team);

                return advance >= PitchBounds.GoalLineZ * (1f - AttackingThirdShare);
            }

            return false;
        }

        // Termina la parte al instante, saltándose el descuento. Lo usa el menú de desarrollo.
        public void ForceEndOfHalf()
        {
            if (isMatchOver || isHalftime || isEndingHalf)
            {
                return;
            }

            currentTime = 0f;
            isInStoppageTime = false;

            BeginEndOfHalf("Forzado desde el menú de desarrollo");
        }

        // Pita el final de la parte y arranca la espera antes de mostrar la pantalla siguiente.
        private void BeginEndOfHalf(string reason)
        {
            if (isEndingHalf)
            {
                return;
            }

            isInStoppageTime = false;
            isEndingHalf = true;

            Debug.Log($"{reason}: fin de la {currentHalf}ª parte en {endOfHalfDelay:F1} s.");

            StartCoroutine(EndHalfRoutine());
        }

        // Anuncia el final de parte o de partido, espera un momento y pasa al descanso o al resultado.
        private IEnumerator EndHalfRoutine()
        {
            bool isFullTime = currentHalf >= HalvesPerMatch;

            Announce(isFullTime ? "announce.fullTime" : "announce.halfTime");

            if (Audio.AudioManager.Instance != null)
            {
                if (isFullTime)
                {
                    Audio.AudioManager.Instance.PlayFullTimeWhistle();
                }
                else
                {
                    Audio.AudioManager.Instance.PlayWhistle(isLong: true);
                }
            }

            yield return new WaitForSecondsRealtime(endOfHalfDelay);

            while (IsPitchInterrupted)
            {
                yield return null;
            }

            isEndingHalf = false;

            if (currentHalf < HalvesPerMatch)
            {
                BeginHalftime();
                yield break;
            }

            EndMatch();
        }

        // Congela el partido para el descanso: hace los cambios de la IA y muestra la pantalla de descanso.
        private void BeginHalftime()
        {
            ClearSetPieceFlags();

            PerformAISubstitutions();

            isHalftime = true;

            Time.timeScale = MatchOverTimeScale;

            if (Audio.AudioManager.Instance != null)
            {
                Audio.AudioManager.Instance.PauseCrowd();
            }

            Debug.Log($"DESCANSO. Fin de la {currentHalf}ª parte.");

            TacticalEvents.OnHalftime?.Invoke();
        }

        // Reanuda el partido para la segunda parte y coloca a los equipos en su formación.
        public void StartSecondHalf()
        {
            if (isMatchOver || !isHalftime)
            {
                return;
            }

            isHalftime = false;
            isInStoppageTime = false;
            isEndingHalf = false;
            currentHalf = HalvesPerMatch;
            currentTime = matchDuration;

            Time.timeScale = NormalTimeScale;
            Time.fixedDeltaTime = FixedDeltaTimeAtNormalScale;

            RestoreFormationPositions();

            Debug.Log("Comienza la 2ª parte.");

            StartInitialKickoff();
        }

        // Termina el partido: para el tiempo, silencia el público y reporta el resultado del torneo.
        private void EndMatch()
        {
            isMatchOver = true;
            isHalftime = false;
            ClearSetPieceFlags();

            Time.timeScale = MatchOverTimeScale;

            if (Audio.AudioManager.Instance != null)
            {
                Audio.AudioManager.Instance.StopCrowd();
            }

            ReportTournamentResult();

            Debug.Log("¡FINAL DEL PARTIDO!");

            TacticalEvents.OnMatchOver?.Invoke();
        }

        // Envía el resultado final al torneo, si el partido formaba parte de uno.
        private void ReportTournamentResult()
        {
            if (TournamentManager.Instance == null || ScoreManager.Instance == null)
            {
                return;
            }

            int blue = ScoreManager.Instance.BlueScore;
            int red = ScoreManager.Instance.RedScore;

            bool humanIsBlue = humanTeam == TeamId.Blue;

            TournamentManager.Instance.ReportResult(
                humanIsBlue ? blue : red,
                humanIsBlue ? red : blue);
        }

        // Cambia a los titulares cansados de la IA por suplentes frescos del mismo rol, en el descanso.
        public void PerformAISubstitutions()
        {
            TeamId aiTeam = Opponent(humanTeam);

            List<TeamMember> tired = new List<TeamMember>();
            List<TeamMember> bench = new List<TeamMember>();

            foreach (TeamMember member in FindObjectsByType<TeamMember>())
            {
                if (member.team != aiTeam || member.isGoalkeeper)
                {
                    continue;
                }

                if (member.isStarter)
                {
                    if (member.StaminaFraction < tiredSubstitutionFraction)
                    {
                        tired.Add(member);
                    }

                    continue;
                }

                if (member.StaminaFraction >= 1f)
                {
                    bench.Add(member);
                }
            }

            if (tired.Count == 0 || bench.Count == 0)
            {
                Debug.Log($"[IA] Sin cambios en el descanso: {tired.Count} cansados por debajo del " +
                          $"{tiredSubstitutionFraction:P0}, {bench.Count} suplentes al 100%.");
                return;
            }

            tired.Sort((a, b) => a.currentStamina.CompareTo(b.currentStamina));

            int changes = 0;
            int refused = 0;

            foreach (TeamMember outgoing in tired)
            {
                TeamMember incoming = null;

                foreach (TeamMember candidate in bench)
                {
                    if (candidate.role == outgoing.role)
                    {
                        incoming = candidate;
                        break;
                    }
                }

                if (incoming == null)
                {
                    refused++;
                    continue;
                }

                bench.Remove(incoming);
                SwapPlayers(outgoing, incoming);
                changes++;
            }

            Debug.Log($"[IA] {changes} cambio(s) en el descanso para {aiTeam}" +
                      (refused > 0
                          ? $"; {refused} sin relevo del mismo rol en el banquillo."
                          : "."));
        }

        // Pone el balón en juego: se llama cuando el que saca pasa o tira.
        public void EndKickoff()
        {
            ClearSetPieceFlags();
        }

        // Limpia todas las banderas de espera de reinicio.
        private void ClearSetPieceFlags()
        {
            isWaitingForKickoff = false;
            isWaitingForThrowIn = false;
            isWaitingForCorner = false;
            isWaitingForGoalKick = false;
            isWaitingForFreeKick = false;
            isWaitingForPenalty = false;
        }

        // Convierte una falta en el reinicio correcto: penalti si fue dentro del área propia, libre directo si no.
        private void HandleFoul(TeamMember offender)
        {
            if (offender == null || isMatchOver)
            {
                return;
            }

            RecordFoul(offender.team);

            Vector3 spot = offender.transform.position;

            TeamId attackingTeam = Opponent(offender.team);

            Debug.Log($"Falta de {offender.name} ({offender.team}) en " +
                      $"({spot.x:F1}, {spot.z:F1}) -> saque para {attackingTeam}.");

            ClearPossession();

            if (PitchBounds.IsInsidePenaltyArea(spot, offender.team))
            {
                StartPenaltyKick(attackingTeam);
                return;
            }

            StartFreeKick(spot, attackingTeam);
        }

        // Prepara un tiro libre desde el punto de la falta para el equipo atacante.
        public void StartFreeKick(Vector3 foulPosition, TeamId attackingTeam)
        {
            if (isMatchOver)
            {
                return;
            }

            PlayerBallHandler taker = FindRestartTaker(attackingTeam, foulPosition);

            if (taker == null)
            {
                Debug.LogWarning($"El equipo {attackingTeam} no tiene jugadores de campo para el libre directo.");
                return;
            }

            Vector3 spot = new Vector3(foulPosition.x, taker.transform.position.y, foulPosition.z);

            if (!PlaceTaker(taker, spot))
            {
                return;
            }

            // The offender is standing exactly where the ball has just been put,
            // because the foul mark IS where he was. Two capsules in the same
            // place jam against each other: the taker cannot walk the ball out,
            // and the opposition's AI keeps sending men at a ball that is
            // physically blocked, which reads as the whole match freezing.
            //
            // Measured from the MARK, not from the ball's live position. The ball
            // was handed to the taker a line ago, but it does not physically move
            // onto his socket until LateUpdate — so reading its transform here
            // gives wherever it was BEFORE the foul, and the players get pushed
            // away from the wrong point.
            SeparateFromRestart(ClampToRestartArea(spot), taker);

            isWaitingForFreeKick = true;

            CenterCameraOnPlay();

            Announce("announce.foul");

            Debug.Log($"FALTA para {attackingTeam}: saca {taker.name} desde " +
                      $"({spot.x:F1}, {spot.z:F1}).");

            float attackDirection = -PitchBounds.DefendedSide(attackingTeam);

            ScheduleAiRestart(attackingTeam, taker,
                new Vector3(spot.x, 0f, spot.z + (attackDirection * throwInDistance)));
        }

        // Quita el balón a quien lo lleve y cancela su ruta en curso.
        private static void ClearPossession()
        {
            BallController ball = BallController.Instance;

            if (ball == null || ball.Holder == null)
            {
                return;
            }

            if (ball.Holder.TryGetComponent(out PlayerBallHandler handler))
            {
                handler.ForceDropBall();
            }

            if (ball.Holder.TryGetComponent(out PlayerRoute route))
            {
                route.CancelRoute();
            }

            ball.Release();
        }

        // Aparta del balón a todos los jugadores que no sacan el reinicio.
        private void SeparateFromRestart(Vector3 ballSpot, PlayerBallHandler taker)
        {
            int moved = 0;

            foreach (TeamMember member in FindObjectsByType<TeamMember>())
            {
                if (!member.isStarter)
                {
                    continue;
                }

                if (taker != null && member.gameObject == taker.gameObject)
                {
                    continue;
                }

                Vector3 away = member.transform.position - ballSpot;
                away.y = 0f;

                float distance = away.magnitude;

                if (distance >= restartClearanceRadius)
                {
                    continue;
                }

                Vector3 direction = distance > 0.01f ? away / distance : Vector3.back;

                Vector3 target = ballSpot + (direction * restartClearanceRadius);
                target.y = member.transform.position.y;

                member.transform.position = PitchBounds.ClampPlayer(target);
                moved++;
            }

            if (moved > 0)
            {
                Debug.Log($"Falta: {moved} jugador(es) apartados {restartClearanceRadius:F1} u del balón.");
            }
        }

        // Concede un penalti al equipo atacante y abre el menú para lanzarlo.
        public void StartPenaltyKick(TeamId attackingTeam)
        {
            if (isMatchOver)
            {
                return;
            }

            isWaitingForPenalty = true;

            Announce("announce.penalty");

            Debug.Log($"PENALTI para {attackingTeam}.");

            StagePenalty(attackingTeam);

            if (UI.PenaltyUIController.Instance != null)
            {
                UI.PenaltyUIController.Instance.ShowPenalty(attackingTeam);
                return;
            }

            Debug.LogWarning("No hay PenaltyUIController: el penalti se anula y se reanuda desde el centro.");

            isWaitingForPenalty = false;

            BallController ball = BallController.Instance;

            if (ball != null)
            {
                ball.ResetToKickoff();
            }
        }

        // Punto donde se coloca el balón para el penalti.
        public Vector3 PenaltySpot { get; private set; }

        // Centro de la portería a la que se tira.
        public Vector3 PenaltyGoalCentre { get; private set; }

        private TeamMember penaltyTaker;
        private TeamMember penaltyKeeper;

        // Transform del portero colocado para el penalti, para animarlo durante el tiro.
        public Transform PenaltyKeeper => penaltyKeeper != null ? penaltyKeeper.transform : null;
        private Vector3 penaltyTakerOrigin;
        private Vector3 penaltyKeeperOrigin;

        // Coloca al lanzador en el punto de penalti y al portero en su línea.
        private void StagePenalty(TeamId attackingTeam)
        {
            TeamId defendingTeam = Opponent(attackingTeam);

            float attackDirection = -PitchBounds.DefendedSide(attackingTeam);

            PenaltyGoalCentre = new Vector3(0f, 0.5f, attackDirection * PitchBounds.GoalLineZ);
            PenaltySpot = new Vector3(0f, 0.5f, attackDirection * (PitchBounds.GoalLineZ - penaltySpotDepth));

            PlayerBallHandler takerHandler = FindNearestFieldPlayer(attackingTeam, PenaltySpot);
            PlayerBallHandler keeperHandler = FindGoalkeeper(defendingTeam);

            penaltyTaker = takerHandler != null ? takerHandler.GetComponent<TeamMember>() : null;
            penaltyKeeper = keeperHandler != null ? keeperHandler.GetComponent<TeamMember>() : null;

            if (penaltyTaker != null)
            {
                penaltyTakerOrigin = penaltyTaker.transform.position;

                if (penaltyTaker.TryGetComponent(out PlayerRoute takerRoute))
                {
                    takerRoute.CancelRoute();
                }

                // A stride behind the ball, facing the goal.
                penaltyTaker.transform.position = new Vector3(
                    PenaltySpot.x,
                    penaltyTaker.transform.position.y,
                    PenaltySpot.z - (attackDirection * 1.2f));

                takerHandler.ForceTakeBall(BallController.Instance);
            }

            if (penaltyKeeper != null)
            {
                penaltyKeeperOrigin = penaltyKeeper.transform.position;

                if (penaltyKeeper.TryGetComponent(out PlayerRoute keeperRoute))
                {
                    keeperRoute.CancelRoute();
                }

                penaltyKeeper.transform.position = new Vector3(
                    0f,
                    penaltyKeeper.transform.position.y,
                    attackDirection * (PitchBounds.GoalLineZ - 0.6f));
            }

            ClearPenaltyArea(defendingTeam);

            BallController ball = BallController.Instance;

            if (ball != null)
            {
                ball.Release();
                ball.transform.position = PenaltySpot;
            }

            if (takerHandler != null)
            {
                takerHandler.ForceDropBall();
            }

            CameraSystem.TacticalCamera.Instance?.ZoomToClash(penaltyTaker, penaltyKeeper);

            Debug.Log($"Penalti preparado: tira {(penaltyTaker != null ? penaltyTaker.name : "nadie")}, " +
                      $"para {(penaltyKeeper != null ? penaltyKeeper.name : "nadie")}, " +
                      $"balón en z={PenaltySpot.z:F1}.");
        }

        // Aparta del área a todos los jugadores excepto el lanzador y el portero.
        private void ClearPenaltyArea(TeamId defendingTeam)
        {
            int moved = 0;
            float defendedSide = PitchBounds.DefendedSide(defendingTeam);

            foreach (TeamMember member in FindObjectsByType<TeamMember>())
            {
                if (!member.isStarter || member == penaltyTaker || member == penaltyKeeper)
                {
                    continue;
                }

                if (!PitchBounds.IsInsidePenaltyArea(member.transform.position, defendingTeam))
                {
                    continue;
                }

                if (member.TryGetComponent(out PlayerRoute route))
                {
                    route.CancelRoute();
                }

                float lane = ((moved % 5) - 2) * 3f;

                Vector3 target = new Vector3(
                    lane,
                    member.transform.position.y,
                    defendedSide * (PitchBounds.GoalLineZ - PitchBounds.PenaltyAreaDepth - 3f));

                member.transform.position = PitchBounds.ClampPlayer(target);
                moved++;
            }

            if (moved > 0)
            {
                Debug.Log($"Penalti: {moved} jugador(es) desalojados del área.");
            }
        }

        // Devuelve al lanzador y al portero a su posición original y recupera la cámara.
        private void UnstagePenalty()
        {
            if (penaltyTaker != null)
            {
                penaltyTaker.transform.position = penaltyTakerOrigin;
                penaltyTaker = null;
            }

            if (penaltyKeeper != null)
            {
                penaltyKeeper.transform.position = penaltyKeeperOrigin;
                penaltyKeeper = null;
            }

            CameraSystem.TacticalCamera.Instance?.CenterCamera();
        }

        // Cierra el penalti: si falló, reanuda con saque de puerta para el defensor.
        public void EndPenalty(TeamId attackingTeam, bool scored)
        {
            isWaitingForPenalty = false;

            UnstagePenalty();

            if (scored)
            {
                return;
            }

            TeamId defendingTeam = Opponent(attackingTeam);

            StartGoalKick(defendingTeam,
                new Vector3(0f, 0f, PitchBounds.DefendedSide(defendingTeam) * PitchBounds.GoalLineZ));
        }

        // Prepara un saque de banda para el equipo que saca, en el punto por donde salió el balón.
        public void StartThrowIn(TeamId throwingTeam, Vector3 outOfBoundsPos)
        {
            if (isMatchOver)
            {
                return;
            }

            ClearDrawnRoutes();

            PlayerBallHandler thrower = FindRestartTaker(throwingTeam, outOfBoundsPos);
            if (thrower == null)
            {
                Debug.LogWarning($"El equipo {throwingTeam} no tiene jugadores de campo para sacar de banda.");
                return;
            }

            float sideline = Mathf.Sign(outOfBoundsPos.x) * PitchBounds.SideLineX;

            Vector3 spot = new Vector3(
                sideline,
                thrower.transform.position.y,
                Mathf.Clamp(outOfBoundsPos.z, -PitchBounds.GoalLineZ, PitchBounds.GoalLineZ));

            if (!PlaceTaker(thrower, spot))
            {
                return;
            }

            isWaitingForThrowIn = true;

            CenterCameraOnPlay();

            Announce("announce.throwIn");

            Debug.Log($"SAQUE DE BANDA para {throwingTeam}: saca {thrower.name} desde " +
                      $"x={sideline:F1}, z={spot.z:F1}.");

            ScheduleAiRestart(throwingTeam, thrower,
                new Vector3(sideline - (Mathf.Sign(sideline) * throwInDistance), 0f, spot.z));
        }

        // Prepara un saque de esquina para el equipo atacante.
        public void StartCorner(TeamId attackingTeam, Vector3 outPos)
        {
            if (isMatchOver)
            {
                return;
            }

            ClearDrawnRoutes();

            float cornerX = Mathf.Sign(outPos.x) * PitchBounds.SideLineX;
            float cornerZ = Mathf.Sign(outPos.z) * PitchBounds.GoalLineZ;

            PlayerBallHandler taker = FindRestartTaker(attackingTeam, new Vector3(cornerX, 0f, cornerZ));
            if (taker == null)
            {
                Debug.LogWarning($"El equipo {attackingTeam} no tiene jugadores de campo para sacar de esquina.");
                return;
            }

            Vector3 spot = new Vector3(cornerX, taker.transform.position.y, cornerZ);

            if (!PlaceTaker(taker, spot))
            {
                return;
            }

            isWaitingForCorner = true;

            CenterCameraOnPlay();

            Announce("announce.corner");

            Debug.Log($"CÓRNER para {attackingTeam}: saca {taker.name} desde ({cornerX:F1}, {cornerZ:F1}).");

            // Swung into the six-yard area in front of the goal being attacked.
            ScheduleAiRestart(attackingTeam, taker,
                new Vector3(0f, 0f, Mathf.Sign(cornerZ) * (PitchBounds.GoalLineZ - 4f)));
        }

        // Prepara un saque de puerta para el equipo defensor.
        public void StartGoalKick(TeamId defendingTeam, Vector3 outPos)
        {
            if (isMatchOver)
            {
                return;
            }

            ClearDrawnRoutes();

            PlayerBallHandler keeper = FindGoalkeeper(defendingTeam);
            if (keeper == null)
            {
                Debug.LogWarning($"El equipo {defendingTeam} no tiene portero para sacar de puerta.");
                return;
            }

            float side = Mathf.Sign(outPos.z);

            Vector3 spot = new Vector3(
                0f,
                keeper.transform.position.y,
                side * (PitchBounds.GoalLineZ - goalKickDepth));

            if (!PlaceTaker(keeper, spot))
            {
                return;
            }

            isWaitingForGoalKick = true;

            CenterCameraOnPlay();

            Announce("announce.goalKick");

            Debug.Log($"SAQUE DE PUERTA para {defendingTeam}: saca {keeper.name} desde z={spot.z:F1}.");

            ScheduleAiRestart(defendingTeam, keeper,
                new Vector3(0f, 0f, spot.z - (side * goalKickDistance)));
        }

        // Coloca a un jugador en el punto de reinicio y le entrega el balón.
        private bool PlaceTaker(PlayerBallHandler taker, Vector3 spot, bool offerSupport = true)
        {
            BallController ball = BallController.Instance;
            if (ball == null)
            {
                Debug.LogWarning("No hay balón: no se puede preparar el saque.");
                return false;
            }

            if (taker.TryGetComponent(out PlayerRoute route))
            {
                route.CancelRoute();
            }

            Vector3 ballSpot = ClampToRestartArea(spot);
            Vector3 offset = taker.BallOffset;

            taker.transform.position = new Vector3(
                ballSpot.x - offset.x,
                spot.y,
                ballSpot.z - offset.z);

            taker.ForceTakeBall(ball);

            ClearExclusionZone(ballSpot, taker);

            if (offerSupport)
            {
                OfferForRestart(taker, ballSpot);
            }

            return true;
        }

        // Cancela el gesto de dibujo activo y borra todas las rutas dibujadas al detenerse el juego.
        public static void ClearDrawnRoutes()
        {
            TacticalSoccer.Input.TacticalInputManager input =
                FindAnyObjectByType<TacticalSoccer.Input.TacticalInputManager>();

            if (input != null)
            {
                input.CancelActiveGesture();
            }

            int cleared = 0;

            foreach (PlayerRoute route in FindObjectsByType<PlayerRoute>())
            {
                bool wasActive = route.IsFollowingRoute;

                route.CancelRoute();

                if (wasActive)
                {
                    cleared++;
                }
            }

            if (cleared > 0)
            {
                Debug.Log($"[Balón parado] {cleared} ruta(s) cortada(s) al detenerse el juego.");
            }
        }

        // Aparta a los jugadores del equipo que no saca de la zona del balón antes del reinicio.
        private void ClearExclusionZone(Vector3 ballSpot, PlayerBallHandler taker)
        {
            AI.SetPiecePositioning.ClearExclusionZone(ballSpot, taker, restartExclusionRadius);
        }

        // Reubica a los compañeros del que saca para ofrecer opciones de pase en el reinicio.
        private void OfferForRestart(PlayerBallHandler taker, Vector3 ballSpot)
        {
            AI.SetPiecePositioning.OfferForRestart(taker, ballSpot, RestartSupportClearance);
        }

        [Tooltip("How far the nearest supporting player is kept from the restart " +
                 "mark. Close enough to be an easy pass, far enough not to stand " +
                 "on the taker or trip a duel the instant play resumes.")]
        [SerializeField] private float restartSupportClearance = 4f;

        private static float RestartSupportClearance =>
            Instance != null ? Instance.restartSupportClearance : 4f;

        // Ajusta un punto de reinicio para que quede dentro de las líneas del campo.
        private static Vector3 ClampToRestartArea(Vector3 spot)
        {
            return AI.SetPiecePositioning.ClampToRestartArea(spot);
        }

        // Vuelve a centrar la cámara sobre el balón para el reinicio.
        private static void CenterCameraOnPlay()
        {
            if (CameraSystem.TacticalCamera.Instance != null)
            {
                CameraSystem.TacticalCamera.Instance.CenterCamera();
            }
        }

        // Si el reinicio es de la IA, programa que lo saque ella sola tras una pequeña espera.
        private void ScheduleAiRestart(TeamId takingTeam, PlayerBallHandler taker, Vector3 target)
        {
            if (takingTeam == humanTeam)
            {
                return;
            }

            if (aiSetPieceRoutine != null)
            {
                StopCoroutine(aiSetPieceRoutine);
            }

            aiSetPieceRoutine = StartCoroutine(DelayedAISetPiece(taker, target));
        }

        // Espera un momento y hace que la IA saque el reinicio hacia un compañero.
        private IEnumerator DelayedAISetPiece(PlayerBallHandler taker, Vector3 target)
        {
            yield return new WaitForSecondsRealtime(aiSetPieceDelay);

            aiSetPieceRoutine = null;

            if (taker == null || !taker.HasBall)
            {
                EndKickoff();
                yield break;
            }

            TeamMember receiver = FindRestartReceiver(taker);

            Vector3 aim = receiver != null ? receiver.transform.position : target;

            Debug.Log(receiver != null
                ? $"[IA] {taker.name} saca hacia {receiver.name} ({receiver.role})."
                : $"[IA] {taker.name} no tiene a nadie: saca hacia {target}.");

            taker.PassTo(aim);
        }

        // Busca el compañero más cercano al que merece la pena pasarle en el reinicio.
        private TeamMember FindRestartReceiver(PlayerBallHandler taker)
        {
            return AI.SetPiecePositioning.FindRestartReceiver(taker, restartPassMinDistance);
        }

        [Tooltip("Shortest pass the AI will play from a restart. Anything under " +
                 "this is a pass that gains nothing and hands the ball back.")]
        [SerializeField] private float restartPassMinDistance = 6f;

        // Reinicia el partido desde cero: reloj, plantillas, estadísticas y tensión.
        public void RestartMatch()
        {
            currentTime = matchDuration;
            currentHalf = 1;
            isMatchOver = false;
            isHalftime = false;
            isInStoppageTime = false;
            isEndingHalf = false;
            kickoffTeam = humanTeam;
            ClearSetPieceFlags();

            Time.timeScale = NormalTimeScale;
            Time.fixedDeltaTime = FixedDeltaTimeAtNormalScale;

            RestoreInitialSquads();

            if (TensionManager.Instance != null)
            {
                TensionManager.Instance.ResetAll();
            }

            ResetStatistics();

            BallController ball = BallController.Instance;

            if (ball != null)
            {
                ball.ResetToKickoff();
            }
            else
            {
                TacticalEvents.OnMatchReset?.Invoke();
            }

            Debug.Log("Partido reiniciado.");
        }

        // Devuelve a cada titular a su puesto de formación fuera del balón.
        private static void RestoreFormationPositions()
        {
            int moved = 0;

            foreach (TeamMember member in FindObjectsByType<TeamMember>())
            {
                if (!member.isStarter || member.isGoalkeeper)
                {
                    continue;
                }

                Vector3 slot = ResolveFormationSlot(member);

                if (member.TryGetComponent(out PlayerRoute route))
                {
                    route.CancelRoute();
                }

                member.transform.position = new Vector3(slot.x, member.transform.position.y, slot.z);
                moved++;
            }

            Debug.Log($"Formaciones restablecidas para la 2ª parte: {moved} jugadores.");
        }

        // Vuelve a la pantalla de título: reinicia el partido y marca que aún no ha empezado.
        public void ReturnToTitle()
        {
            RestartMatch();

            isMatchStarted = false;
            ClearSetPieceFlags();

            Debug.Log("Vuelta a la pantalla de título.");
        }

        // Pone a ambos equipos como al empezar el partido: titulares, posiciones y energía completa.
        private static void RestoreInitialSquads()
        {
            foreach (TeamMember member in FindObjectsByType<TeamMember>())
            {
                member.RestoreInitialState();

                AssignSlot(member, member.InitialPosition);
            }
        }

        // Intercambia a dos jugadores entre el campo y el banquillo: posición, puesto y estado de titular.
        public void SwapPlayers(TeamMember p1, TeamMember p2)
        {
            if (p1 == null || p2 == null || p1 == p2)
            {
                return;
            }

            Vector3 position1 = p1.transform.position;
            Vector3 position2 = p2.transform.position;

            Vector3 slot1 = ResolveFormationSlot(p1);
            Vector3 slot2 = ResolveFormationSlot(p2);

            p1.transform.position = position2;
            p2.transform.position = position1;

            AssignSlot(p1, slot2);
            AssignSlot(p2, slot1);

            p1.isStarter = !p1.isStarter;
            p2.isStarter = !p2.isStarter;

            Debug.Log($"CAMBIO ({p1.team}): sale el {p1.jerseyNumber}, entra el {p2.jerseyNumber}.");
        }

        // Devuelve el puesto de formación de un jugador, o su posición actual si no tiene IA táctica.
        public static Vector3 ResolveFormationSlot(TeamMember member)
        {
            return member.TryGetComponent(out AI.TacticalPositioning positioning)
                ? positioning.FormationSlot
                : member.transform.position;
        }

        // Actualiza el puesto de un jugador en ruta e IA táctica.
        private static void AssignSlot(TeamMember member, Vector3 slot)
        {
            if (member.TryGetComponent(out PlayerRoute route))
            {
                route.CancelRoute();
                route.SetFormationSlot(slot);
            }

            if (member.TryGetComponent(out AI.TacticalPositioning positioning))
            {
                positioning.SetFormationSlot(slot);
            }
        }

        // Arranca el saque de centro tras un reinicio del partido.
        private void HandleMatchReset()
        {
            BeginKickoff();
        }

        // Prepara el saque de centro: para rutinas anteriores, pita y centra la cámara.
        private void BeginKickoff()
        {
            if (isMatchOver)
            {
                return;
            }

            if (kickoffRoutine != null)
            {
                StopCoroutine(kickoffRoutine);
            }

            if (aiSetPieceRoutine != null)
            {
                StopCoroutine(aiSetPieceRoutine);
                aiSetPieceRoutine = null;
            }

            ClearSetPieceFlags();
            isWaitingForKickoff = true;

            if (Audio.AudioManager.Instance != null)
            {
                Audio.AudioManager.Instance.PlayWhistle(isLong: false);
            }

            CenterCameraOnPlay();

            kickoffRoutine = StartCoroutine(SetupKickoffRoutine());
        }

        // Espera un frame a que el balón se reposicione y luego se lo entrega al que saca.
        private IEnumerator SetupKickoffRoutine()
        {
            yield return null;

            kickoffRoutine = null;

            PlayerBallHandler taker = FindNearestFieldPlayer(kickoffTeam, Vector3.zero);
            if (taker == null)
            {
                Debug.LogWarning($"Ningún jugador de campo del equipo {kickoffTeam} puede sacar. " +
                                 "El balón queda libre en el centro.");
                isWaitingForKickoff = false;
                yield break;
            }

            float ownSide = kickoffTeam == TeamId.Blue ? -1f : 1f;

            if (!PlaceTaker(taker, new Vector3(0f, taker.transform.position.y, ownSide * kickoffTakerOffset),
                offerSupport: false))
            {
                isWaitingForKickoff = false;
                yield break;
            }

            Debug.Log($"SAQUE DE CENTRO para {kickoffTeam}: saca {taker.name} desde el centro.");

            PlayerBallHandler receiver = FindNearestFieldPlayer(
                kickoffTeam, taker.transform.position, exclude: taker);

            Vector3 kickoffTarget = receiver != null
                ? receiver.transform.position
                : new Vector3(0f, 0f, -ownSide * kickoffPassDistance);

            if (receiver != null && kickoffTeam != humanTeam)
            {
                Debug.Log($"[IA] El saque de centro va hacia {receiver.name}.");
            }

            ScheduleAiRestart(kickoffTeam, taker, kickoffTarget);
        }

        // Jugador de campo más cercano a un punto, sin contar porteros ni suplentes.
        private PlayerBallHandler FindNearestFieldPlayer(TeamId team, Vector3 point,
            PlayerBallHandler exclude = null)
        {
            return AI.SetPiecePositioning.FindNearestFieldPlayer(team, point, exclude);
        }

        // Igual, pero limitado a un rol concreto.
        private PlayerBallHandler FindNearestFieldPlayer(TeamId team, Vector3 point,
            PlayerBallHandler exclude, PlayerRole? onlyRole)
        {
            return AI.SetPiecePositioning.FindNearestFieldPlayer(team, point, exclude, onlyRole);
        }

        // Elige quién saca un reinicio: prioriza a los centrocampistas, luego defensas y por último delanteros.
        private PlayerBallHandler FindRestartTaker(TeamId team, Vector3 point)
        {
            return AI.SetPiecePositioning.FindRestartTaker(team, point);
        }

        // Muestra un anuncio del locutor en pantalla, si el controlador existe.
        private static void Announce(string messageKey)
        {
            if (UI.AnnouncerUIController.Instance != null)
            {
                UI.AnnouncerUIController.Instance.ShowAnnouncement(
                    LocalizationManager.GetText(messageKey));
            }
        }

        // Busca al portero titular de un equipo.
        private PlayerBallHandler FindGoalkeeper(TeamId team)
        {
            foreach (TeamMember member in FindObjectsByType<TeamMember>())
            {
                if (member.team != team || !member.isGoalkeeper || !member.isStarter)
                {
                    continue;
                }

                if (member.TryGetComponent(out PlayerBallHandler handler))
                {
                    return handler;
                }
            }

            return null;
        }
    }
}
