using UnityEngine;

namespace TacticalSoccer.Audio
{
    /// <summary>
    /// Every sound the match makes, behind one door.
    ///
    /// Three sources, because the sounds have three different shapes:
    ///
    ///  - the crowd is a bed: one clip, looping, never interrupted, sitting low
    ///    in the mix, and audible only while a match is actually being played;
    ///  - the burn is a second bed, but a conditional one that has to start and
    ///    stop on a gameplay state rather than run all match;
    ///  - everything else is a punctuation mark that has to land on top of
    ///    whatever is already ringing, which is what PlayOneShot does and what a
    ///    second Play() on the same source would not — it would cut the first
    ///    clip dead.
    ///
    /// Nothing here is gameplay state. If the clips are missing the game runs
    /// silently and identically — every entry point checks, so a scene generated
    /// before the audio assets existed still plays.
    ///
    /// Scene singleton like <see cref="VFX.VFXManager"/>, deliberately without
    /// DontDestroyOnLoad: the project is one scene, and an object that survived
    /// it would come back doubled the next time the generator ran.
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        [Header("Silbatos")]
        [Tooltip("Short blast. Opens every restart from the centre and punctuates " +
                 "a goal.")]
        [SerializeField] private AudioClip whistleShort;

        [Tooltip("Long blast. Ends the first half.")]
        [SerializeField] private AudioClip whistleLong;

        [Tooltip("The referee's final whistle. Separate from the long blast " +
                 "because full time is the one whistle in a match that is meant " +
                 "to sound different from every other.")]
        [SerializeField] private AudioClip whistleFullTime;

        [Header("Balón")]
        [Tooltip("Struck ball. Fires from the ball itself, so it covers passes, " +
                 "shots, clearances and set pieces from one place.")]
        [SerializeField] private AudioClip kickBall;

        [Tooltip("The ball hitting the net.")]
        [SerializeField] private AudioClip netGoal;

        [Header("Duelos y faltas")]
        [Tooltip("Contact in a duel. Plays with the match frozen at timeScale 0 — " +
                 "audio is not governed by it, which is what makes the freeze " +
                 "land as a hit rather than as a pause.")]
        [SerializeField] private AudioClip clashImpact;

        [Tooltip("The whistle for a foul, blown as the duel is voided.")]
        [SerializeField] private AudioClip foulWhistle;

        [Header("Ardor")]
        [Tooltip("Looped for as long as ANY side is in the zone, rather than " +
                 "fired once when it lights: the sting is 5.6 s and the zone " +
                 "lasts 10, so a one-shot left the second half of it silent.")]
        [SerializeField] private AudioClip tensionMax;

        [Header("Público")]
        [Tooltip("Ambient crowd. Runs only while a half is actually being played " +
                 "— never under the title screen, the interval or the result.")]
        [SerializeField] private AudioClip stadiumLoop;

        [Tooltip("The roar for a goal.")]
        [SerializeField] private AudioClip crowdCheer;

        [Header("Mezcla")]
        [Tooltip("Live level of the crowd bed. Seeded in Awake from the save " +
                 "file — whose own default matches the value here — so editing " +
                 "it in the Inspector only changes what a player with no save " +
                 "hears. Well under the effects on purpose: the bed runs " +
                 "continuously, so at anything like full volume it stops being " +
                 "atmosphere and starts being the thing you hear.")]
        [Range(0f, 1f)]
        [SerializeField] private float musicVolume = 0.35f;

        [Range(0f, 1f)]
        [SerializeField] private float sfxVolume = 1f;

        private AudioSource musicSource;
        private AudioSource sfxSource;
        private AudioSource tensionSource;

        // How long a taste of the crowd lasts on the options screen. The clip
        // itself is ten seconds of stadium, which would bury a menu.
        private const float CrowdPreviewSeconds = 2f;

        private Coroutine crowdPreviewRoutine;

        public static AudioManager Instance { get; private set; }

        /// <summary>Current levels, 0..1. Read by the options panel to open on the right values.</summary>
        public float MusicVolume => musicVolume;

        public float SfxVolume => sfxVolume;

        /// <summary>
        /// All three sources are built here rather than serialised onto the
        /// object. They carry no tuning a human would ever want to open the
        /// Inspector for — the mix lives in the two volumes above — and building
        /// them keeps the generator from having to wire components whose only
        /// job is to exist.
        /// </summary>
        private void Awake()
        {
            Instance = this;

            // The player's own levels win over the serialised defaults, which
            // are only ever the starting point for a fresh install. They come
            // from the save file rather than from PlayerPrefs: one document
            // holds everything that outlives a session, and a level set here
            // reads back the same whether the game was closed properly or not.
            musicVolume = Core.SaveManager.Data.musicVolume;
            sfxVolume = Core.SaveManager.Data.sfxVolume;

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

            // Left at 1 on purpose: PlayOneShot's second argument SCALES the
            // source volume rather than replacing it, so setting both would
            // square sfxVolume and make 0.5 sound like a quarter.
            sfxSource.volume = 1f;

            // Flat 2D. Positional audio would need the clips placed in the world
            // and would then be mixed against a camera that swings from a wide
            // tactical view to a duel close-up in half a second — the same tackle
            // would be a different loudness depending on where the camera had got
            // to, which is worse than no spatialisation at all.
            musicSource.spatialBlend = 0f;
            tensionSource.spatialBlend = 0f;
            sfxSource.spatialBlend = 0f;
        }

        private void OnEnable()
        {
            // The one moment the audio can hear about without anybody having to
            // call it. The event already exists because something else needed
            // it, and it fires at exactly the instant the sound belongs to.
            //
            // The foul and the burn are deliberately NOT hooked here.
            // OnFoulCommitted is raised at the END of the dwell the foul is held
            // for, to hand the restart its spot, so a whistle on it would sound
            // a second and a half late. OnTensionIgnited would only cover the
            // start of the burn, and this one has to be stopped as well.
            Core.TacticalEvents.OnGoalScored += HandleGoalScored;
        }

        private void OnDisable()
        {
            Core.TacticalEvents.OnGoalScored -= HandleGoalScored;

            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>
        /// Assigned by the scene generator from the files in Assets/Audio. Any
        /// of them may be null — the project has more sounds specified than it
        /// has files — and every play path checks before touching one.
        /// </summary>
        public void ConfigureClips(AudioClip shortWhistle, AudioClip longWhistle,
            AudioClip fullTimeWhistle, AudioClip kick, AudioClip net,
            AudioClip impact, AudioClip foul, AudioClip tension,
            AudioClip stadium, AudioClip cheer)
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
        }

        /// <summary>
        /// Plays one effect over whatever else is already sounding. The single
        /// funnel: everything below is a name for a clip, and this is the only
        /// thing that actually makes noise.
        /// </summary>
        public void PlaySFX(AudioClip clip)
        {
            if (clip == null || sfxSource == null)
            {
                return;
            }

            sfxSource.PlayOneShot(clip, sfxVolume);
        }

        public void PlayWhistle(bool isLong)
        {
            PlaySFX(isLong ? whistleLong : whistleShort);
        }

        /// <summary>The final whistle, falling back to the long blast if there is no separate one.</summary>
        public void PlayFullTimeWhistle()
        {
            PlaySFX(whistleFullTime != null ? whistleFullTime : whistleLong);
        }

        public void PlayCrowdCheer()
        {
            PlaySFX(crowdCheer);
        }

        public void PlayKick()
        {
            PlaySFX(kickBall);
        }

        public void PlayClashImpact()
        {
            PlaySFX(clashImpact);
        }

        public void PlayFoulWhistle()
        {
            PlaySFX(foulWhistle);
        }

        /// <summary>
        /// Starts the crowd bed. Called when a half kicks off rather than on
        /// Awake: a stadium roaring behind the main menu is a stadium with
        /// nobody playing in it.
        ///
        /// Safe to call on every kickoff — restarting a loop that is already
        /// running would be an audible seam, so an already-playing bed is left
        /// exactly where it is.
        /// </summary>
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

        /// <summary>
        /// Holds the crowd where it is for the interval and the team sheet.
        ///
        /// Pause rather than Stop: the interval is a break in the same match,
        /// and a bed that restarted from sample zero every time the player
        /// opened a menu would announce the cut instead of hiding it.
        /// </summary>
        public void PauseCrowd()
        {
            if (musicSource != null && musicSource.isPlaying)
            {
                musicSource.Pause();
            }

            // The zone cannot survive an interval either — the burn timer is
            // frozen along with the match, so leaving the loop running would be
            // ten seconds of fanfare over a team talk.
            if (tensionSource != null && tensionSource.isPlaying)
            {
                tensionSource.Pause();
            }
        }

        /// <summary>Picks both beds up where they were left, on the way back out.</summary>
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

        /// <summary>
        /// Silences the crowd for good, for full time and the way back to the
        /// menu. Unlike the interval there is nothing to resume into.
        /// </summary>
        public void StopCrowd()
        {
            if (musicSource != null)
            {
                musicSource.Stop();
            }

            StopTensionLoop();
        }

        /// <summary>
        /// Runs the burn fanfare for as long as somebody is in the zone.
        ///
        /// A loop rather than a one-shot because the clip is 5.6 s and the zone
        /// lasts 10: fired once, the back half of the most dramatic state in the
        /// game was silent. Idempotent, so a second side lighting while the
        /// first is still burning does not restart it mid-phrase.
        /// </summary>
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

        /// <summary>Cuts the fanfare when the last side leaves the zone.</summary>
        public void StopTensionLoop()
        {
            if (tensionSource != null)
            {
                tensionSource.Stop();
            }
        }

        /// <summary>
        /// Plays a short taste of the crowd at the current level, for the
        /// options screen.
        ///
        /// Needed because the options are reachable from the title, where the
        /// stadium bed is deliberately silent — so without this the crowd slider
        /// would be the one control in the game that does nothing you can hear
        /// while you set it.
        ///
        /// Cut after <see cref="CrowdPreviewSeconds"/> rather than left to run:
        /// the clip is ten seconds of stadium and the menu would drown in it.
        /// The cut is a coroutine on unscaled time because every screen this
        /// opens from is holding the match at timeScale 0.
        ///
        /// If the bed is already playing — the options opened mid-match from the
        /// developer menu — this does nothing at all. The level is already
        /// audible, and starting a preview over it would only double it.
        /// </summary>
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

        private System.Collections.IEnumerator StopCrowdPreviewRoutine()
        {
            yield return new WaitForSecondsRealtime(CrowdPreviewSeconds);

            crowdPreviewRoutine = null;

            // Guarded, not unconditional: a match can have kicked off during the
            // preview — the options are reachable from the developer menu — and
            // this must never be what silences a live stadium.
            if (musicSource != null && !IsCrowdLive)
            {
                musicSource.Stop();
            }
        }

        /// <summary>
        /// True once a half is under way, which is the only state the crowd bed
        /// is meant to survive in on its own.
        /// </summary>
        private static bool IsCrowdLive => Core.MatchManager.Instance != null
            && Core.MatchManager.IsStarted
            && Core.MatchManager.IsPlayable
            && !Core.MatchManager.IsHalftime;

        /// <summary>
        /// Sets the crowd level and remembers it. Live: the bed is already
        /// playing while the player drags the slider, which is the only way to
        /// judge where to put it.
        /// </summary>
        public void SetMusicVolume(float value)
        {
            musicVolume = Mathf.Clamp01(value);

            if (musicSource != null)
            {
                musicSource.volume = musicVolume;
            }

            // Marked, not written. This is called on every frame of a drag —
            // forty times over one sweep of the handle — and the save manager
            // coalesces those into a single write once the handle settles.
            Core.SaveManager.Data.musicVolume = musicVolume;
            Core.SaveManager.Save();
        }

        /// <summary>
        /// Sets the effects level and remembers it.
        ///
        /// The one-shots read <see cref="sfxVolume"/> as they fire, so they need
        /// nothing here; the burn loop is already sounding, so it does.
        /// </summary>
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

        private void HandleGoalScored(int scoringTeamId)
        {
            PlaySFX(netGoal);
            PlayCrowdCheer();
            PlayWhistle(isLong: false);
        }
    }
}
