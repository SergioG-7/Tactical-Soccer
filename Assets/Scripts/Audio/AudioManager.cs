using UnityEngine;

namespace TacticalSoccer.Audio
{
    // Gestiona todo el sonido del partido: silbatos, balón, público, interfaz y ardor.
    public class AudioManager : MonoBehaviour
    {
        [Tooltip("Silbato corto para saques de centro y goles.")]
        [SerializeField] private AudioClip whistleShort;

        [Tooltip("Silbato largo para indicar el final del primer tiempo.")]
        [SerializeField] private AudioClip whistleLong;

        [Tooltip("Silbato de final de partido.")]
        [SerializeField] private AudioClip whistleFullTime;

        [Tooltip("Efecto de golpeo de balón (pases, tiros y despejes).")]
        [SerializeField] private AudioClip kickBall;

        [Tooltip("Efecto del balón impactando en la red.")]
        [SerializeField] private AudioClip netGoal;

        [Tooltip("Sonido de interacción para botones de menús y opciones.")]
        [SerializeField] private AudioClip clickSound;

        [Tooltip("Sonido de impacto físico al disputar un duelo.")]
        [SerializeField] private AudioClip clashImpact;

        [Tooltip("Silbato arbitral al señalar una falta.")]
        [SerializeField] private AudioClip foulWhistle;

        [Tooltip("Pista en bucle activada durante las zonas de máxima tensión.")]
        [SerializeField] private AudioClip tensionMax;

        [Tooltip("Pista de ambiente del estadio durante el partido.")]
        [SerializeField] private AudioClip stadiumLoop;

        [Tooltip("Reacción del público al marcar un gol.")]
        [SerializeField] private AudioClip crowdCheer;

        [Tooltip("Volumen base del ambiente del público.")]
        [Range(0f, 1f)]
        [SerializeField] private float musicVolume = 0.35f;

        [Range(0f, 1f)]
        [SerializeField] private float sfxVolume = 1f;

        [Tooltip("Control de volumen independiente para los silbatos arbitrales.")]
        [Range(0f, 1f)]
        [SerializeField] private float whistleVolume = 1f;

        private AudioSource musicSource;
        private AudioSource sfxSource;
        private AudioSource tensionSource;

        // Duración del avance del ambiente de público en la pantalla de opciones.
        private const float CrowdPreviewSeconds = 2f;

        private Coroutine crowdPreviewRoutine;

        public static AudioManager Instance { get; private set; }

        // Volumen actual del público (0..1), leído por el panel de opciones.
        public float MusicVolume => musicVolume;

        public float SfxVolume => sfxVolume;

        public float WhistleVolume => whistleVolume;

        // Crea las tres fuentes de audio y carga los volúmenes guardados.
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            musicVolume = Core.SaveManager.Data.musicVolume;
            sfxVolume = Core.SaveManager.Data.sfxVolume;
            whistleVolume = Core.SaveManager.Data.whistleVolume;

            musicSource = gameObject.AddComponent<AudioSource>();
            musicSource.loop = true;
            musicSource.playOnAwake = false;
            musicSource.volume = musicVolume;

            tensionSource = gameObject.AddComponent<AudioSource>();
            tensionSource.loop = true;
            tensionSource.playOnAwake = false;
            tensionSource.volume = sfxVolume;

            sfxSource = gameObject.AddComponent<AudioSource>();
            sfxSource.loop = false;
            sfxSource.playOnAwake = false;

            // Se deja a 1: PlayOneShot ya escala el volumen por su segundo parámetro.
            sfxSource.volume = 1f;

            musicSource.spatialBlend = 0f;
            tensionSource.spatialBlend = 0f;
            sfxSource.spatialBlend = 0f;
        }

        // Se suscribe al evento de gol para reproducir el sonido correspondiente.
        private void OnEnable()
        {
            Core.TacticalEvents.OnGoalScored += HandleGoalScored;
        }

        // Se desuscribe del evento de gol y limpia la instancia.
        private void OnDisable()
        {
            Core.TacticalEvents.OnGoalScored -= HandleGoalScored;

            if (Instance == this)
            {
                Instance = null;
            }
        }

        // Asigna los clips de audio generados por la escena.
        public void ConfigureClips(AudioClip shortWhistle, AudioClip longWhistle,
            AudioClip fullTimeWhistle, AudioClip kick, AudioClip net,
            AudioClip impact, AudioClip foul, AudioClip tension,
            AudioClip stadium, AudioClip cheer, AudioClip click)
        {
            whistleShort = shortWhistle;
            whistleLong = longWhistle;
            whistleFullTime = fullTimeWhistle;
            kickBall = kick;
            netGoal = net;
            clashImpact = impact;
            foulWhistle = foul;
            tensionMax = tension;
            stadiumLoop = stadium;
            crowdCheer = cheer;
            clickSound = click;
        }

        // Reproduce un efecto de sonido al volumen general de efectos.
        public void PlaySFX(AudioClip clip)
        {
            PlaySFX(clip, sfxVolume);
        }

        // Reproduce un efecto de sonido a un volumen concreto.
        private void PlaySFX(AudioClip clip, float volume)
        {
            if (clip == null || sfxSource == null)
            {
                return;
            }

            sfxSource.PlayOneShot(clip, volume);
        }

        // Reproduce el silbato corto o largo.
        public void PlayWhistle(bool isLong)
        {
            PlaySFX(isLong ? whistleLong : whistleShort, whistleVolume);
        }

        // Silbato de final de partido, usando el largo si no hay uno propio.
        public void PlayFullTimeWhistle()
        {
            PlaySFX(whistleFullTime != null ? whistleFullTime : whistleLong, whistleVolume);
        }

        // Reproduce la ovación del público.
        public void PlayCrowdCheer()
        {
            PlaySFX(crowdCheer);
        }

        // Reproduce el sonido de patada al balón.
        public void PlayKick()
        {
            PlaySFX(kickBall);
        }

        // Reproduce el impacto de un duelo.
        public void PlayClashImpact()
        {
            PlaySFX(clashImpact);
        }

        // Reproduce el silbato de falta.
        public void PlayFoulWhistle()
        {
            PlaySFX(foulWhistle);
        }

        // Reproduce el clic de la interfaz.
        public void PlayClick()
        {
            PlaySFX(clickSound);
        }

        // Arranca el ambiente de público, si no está ya sonando.
        public void PlayStadiumLoop()
        {
            if (stadiumLoop == null || musicSource == null || musicSource.isPlaying)
            {
                return;
            }

            musicSource.clip = stadiumLoop;
            musicSource.volume = musicVolume;
            musicSource.Play();
        }

        // Pausa el público y el ardor para el descanso o la alineación.
        public void PauseCrowd()
        {
            if (musicSource != null && musicSource.isPlaying)
            {
                musicSource.Pause();
            }

            if (tensionSource != null && tensionSource.isPlaying)
            {
                tensionSource.Pause();
            }
        }

        // Reanuda el público y el ardor donde se quedaron.
        public void ResumeCrowd()
        {
            if (musicSource != null && musicSource.clip != null && !musicSource.isPlaying)
            {
                musicSource.UnPause();
            }

            if (tensionSource != null && tensionSource.clip != null && !tensionSource.isPlaying)
            {
                tensionSource.UnPause();
            }
        }

        // Detiene el público y el ardor por completo.
        public void StopCrowd()
        {
            if (musicSource != null)
            {
                musicSource.Stop();
            }

            StopTensionLoop();
        }

        // Arranca en bucle la fanfarria de ardor mientras algún equipo esté en la zona.
        public void StartTensionLoop()
        {
            if (tensionMax == null || tensionSource == null || tensionSource.isPlaying)
            {
                return;
            }

            tensionSource.clip = tensionMax;
            tensionSource.volume = sfxVolume;
            tensionSource.Play();
        }

        // Corta la fanfarria de ardor.
        public void StopTensionLoop()
        {
            if (tensionSource != null)
            {
                tensionSource.Stop();
            }
        }

        // Reproduce un adelanto breve del público en el panel de opciones.
        public void PreviewCrowd()
        {
            if (stadiumLoop == null || musicSource == null || musicSource.isPlaying)
            {
                return;
            }

            if (crowdPreviewRoutine != null)
            {
                StopCoroutine(crowdPreviewRoutine);
            }

            musicSource.clip = stadiumLoop;
            musicSource.volume = musicVolume;
            musicSource.time = 0f;
            musicSource.Play();

            crowdPreviewRoutine = StartCoroutine(StopCrowdPreviewRoutine());
        }

        // Corta el adelanto del público tras un rato, salvo que el partido ya esté en marcha.
        private System.Collections.IEnumerator StopCrowdPreviewRoutine()
        {
            yield return new WaitForSecondsRealtime(CrowdPreviewSeconds);

            crowdPreviewRoutine = null;

            if (musicSource != null && !IsCrowdLive)
            {
                musicSource.Stop();
            }
        }

        // Cierto mientras una parte del partido está en juego.
        private static bool IsCrowdLive => Core.MatchManager.Instance != null
            && Core.MatchManager.IsStarted
            && Core.MatchManager.IsPlayable
            && !Core.MatchManager.IsHalftime;

        // Cambia el volumen del público en vivo y lo guarda.
        public void SetMusicVolume(float value)
        {
            musicVolume = Mathf.Clamp01(value);

            if (musicSource != null)
            {
                musicSource.volume = musicVolume;
            }

            Core.SaveManager.Data.musicVolume = musicVolume;
            Core.SaveManager.Save();
        }

        // Cambia el volumen general de efectos y lo guarda.
        public void SetSfxVolume(float value)
        {
            sfxVolume = Mathf.Clamp01(value);

            if (tensionSource != null)
            {
                tensionSource.volume = sfxVolume;
            }

            Core.SaveManager.Data.sfxVolume = sfxVolume;
            Core.SaveManager.Save();
        }

        // Cambia el volumen de los silbatos y lo guarda.
        public void SetWhistleVolume(float value)
        {
            whistleVolume = Mathf.Clamp01(value);

            Core.SaveManager.Data.whistleVolume = whistleVolume;
            Core.SaveManager.Save();
        }

        // Reproduce el sonido de gol: red, ovación y silbato.
        private void HandleGoalScored(int scoringTeamId)
        {
            PlaySFX(netGoal);
            PlayCrowdCheer();
            PlayWhistle(isLong: false);
        }
    }
}
