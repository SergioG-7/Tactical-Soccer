using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace TacticalSoccer.Core
{
    /// <summary>
    /// One edited player, as saved.
    ///
    /// Identified by side and shirt number rather than by GameObject name: the
    /// number is the only stable name a player has — the name carries the role
    /// they were GENERATED with, and roles are reassigned by every formation
    /// change, so "Team Red Forward 1" is routinely a midfielder.
    ///
    /// Enums are stored as ints so a value renamed in code does not silently
    /// stop matching a file written by an older build. int is what the enum has
    /// always been; the name is just how it is spelled.
    /// </summary>
    [Serializable]
    public class PlayerRecord
    {
        public int team;
        public int jerseyNumber;
        public int role;
        public int element;

        public int dribble;
        public int power;
        public int shoot;
        public int tackle;
        public int block;
        public int goalkeeping;

        public float maxStamina;
    }

    /// <summary>
    /// Everything that outlives a session.
    ///
    /// Settings and squad, together in one file, because they are saved by the
    /// same event as often as not (the options screen writes a language, the
    /// squad board writes a player) and two files would mean two ways for the
    /// same save to half-happen.
    ///
    /// Every field has a default that is a working game: a fresh install with no
    /// file reads exactly like this object, and no caller has to distinguish
    /// "not saved yet" from "saved as zero".
    /// </summary>
    [Serializable]
    public class GameData
    {
        // Matching the values the audio manager was serialised with, so a fresh
        // install sounds the way it always did: the crowd bed sits under
        // everything else, the effects play at full.
        public const float DefaultMusicVolume = 0.35f;
        public const float DefaultSfxVolume = 1f;

        public string language = LocalizationManager.DefaultLanguage;

        public float musicVolume = DefaultMusicVolume;
        public float sfxVolume = DefaultSfxVolume;

        public int tournamentStage;

        [Tooltip("Players edited in the squad board. Absent from this list means " +
                 "'exactly as the generator made him' — the file only ever holds " +
                 "what somebody changed by hand.")]
        public List<PlayerRecord> squad = new List<PlayerRecord>();
    }

    /// <summary>
    /// The save file: one JSON document in the platform's own save folder.
    ///
    /// Static, and loaded on first read, for the same reason the localisation is:
    /// the language is needed before any menu exists, and a manager that has to
    /// be found in the scene first would be found too late. Nothing here needs a
    /// GameObject.
    ///
    /// Writing is DEFERRED by default. A volume slider calls in on every frame
    /// of a drag — forty writes over one sweep of the handle — so
    /// <see cref="Save"/> only marks the data dirty and a small runtime host
    /// flushes it a moment later, on losing focus, and on quit. Decisions the
    /// player makes once (a language, an edited player) use
    /// <see cref="SaveNow"/> and hit the disk immediately.
    ///
    /// This replaces the PlayerPrefs scattered across the audio manager and the
    /// tournament. Their values are migrated the first time this runs, so an
    /// existing player keeps their levels and their run.
    /// </summary>
    public static class SaveManager
    {
        public const string FileName = "save_data.json";

        // How long a deferred save waits before it is written. Long enough that
        // a slider drag coalesces into one write, short enough that a player who
        // closes the options and pulls the plug keeps their change.
        private const float FlushDelaySeconds = 1f;

        // The old PlayerPrefs keys, read once to migrate and never written
        // again. Named here rather than referenced from the two classes that
        // used to own them: this is a fact about last version's format, and it
        // should not keep those constants alive.
        private const string LegacyMusicKey = "MusicVolume";
        private const string LegacySfxKey = "SFXVolume";
        private const string LegacyStageKey = "TournamentStage";

        private static GameData data;
        private static bool dirty;
        private static float flushDueAt;

        /// <summary>Where the file lives. Useful in a log when somebody asks where their settings went.</summary>
        public static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

        /// <summary>
        /// The saved game. Loads from disk on first use and never returns null —
        /// a missing or corrupt file yields defaults rather than an exception, so
        /// no caller has to check.
        /// </summary>
        public static GameData Data
        {
            get
            {
                if (data == null)
                {
                    Load();
                }

                return data;
            }
        }

        /// <summary>
        /// Marks the data as needing a write, without doing one. For values that
        /// change continuously — the two volume sliders — where writing on every
        /// change would be a file write per frame.
        /// </summary>
        public static void Save()
        {
            dirty = true;
            flushDueAt = Time.unscaledTime + FlushDelaySeconds;
        }

        /// <summary>
        /// Writes immediately. For the changes a player would be annoyed to lose:
        /// the language, an edited player, the tournament advancing a round.
        /// </summary>
        public static void SaveNow()
        {
            dirty = false;

            try
            {
                File.WriteAllText(FilePath, JsonUtility.ToJson(Data, true));
            }
            catch (Exception error)
            {
                // A save that cannot be written is not worth taking the game
                // down for: the session carries on with the values in memory.
                Debug.LogWarning($"[Guardado] No se ha podido escribir {FilePath}: {error.Message}");
            }
        }

        /// <summary>
        /// Writes only if something is pending. Called by the host on its timer
        /// and at every exit point.
        /// </summary>
        public static void Flush()
        {
            if (dirty)
            {
                SaveNow();
            }
        }

        /// <summary>Whether a deferred save is due. Read by the host so the timer lives in one place.</summary>
        internal static bool IsFlushDue => dirty && Time.unscaledTime >= flushDueAt;

        /// <summary>
        /// The saved record for a player, or null if nobody has edited him.
        /// </summary>
        public static PlayerRecord FindPlayer(int team, int jerseyNumber)
        {
            foreach (PlayerRecord record in Data.squad)
            {
                if (record != null && record.team == team && record.jerseyNumber == jerseyNumber)
                {
                    return record;
                }
            }

            return null;
        }

        /// <summary>
        /// The record for a player, created and added if this is the first edit.
        /// </summary>
        public static PlayerRecord GetOrCreatePlayer(int team, int jerseyNumber)
        {
            PlayerRecord existing = FindPlayer(team, jerseyNumber);

            if (existing != null)
            {
                return existing;
            }

            PlayerRecord record = new PlayerRecord
            {
                team = team,
                jerseyNumber = jerseyNumber
            };

            Data.squad.Add(record);

            return record;
        }

        /// <summary>
        /// Throws the squad edits away, leaving the settings alone. The developer
        /// menu's way of getting back to the players the generator made.
        /// </summary>
        public static void ClearSquad()
        {
            Data.squad.Clear();
            SaveNow();
        }

        private static void Load()
        {
            data = ReadFile() ?? MigrateFromPlayerPrefs();

            // A file written by hand, or by an older build, can be missing the
            // list entirely — JsonUtility leaves it null rather than defaulting.
            if (data.squad == null)
            {
                data.squad = new List<PlayerRecord>();
            }
        }

        private static GameData ReadFile()
        {
            try
            {
                if (!File.Exists(FilePath))
                {
                    return null;
                }

                return JsonUtility.FromJson<GameData>(File.ReadAllText(FilePath));
            }
            catch (Exception error)
            {
                // Corrupt file: start clean rather than refuse to run. The file
                // is left where it is so it can still be looked at.
                Debug.LogWarning($"[Guardado] {FileName} no se ha podido leer ({error.Message}). " +
                                 "Se empieza con la configuración por defecto.");

                return null;
            }
        }

        /// <summary>
        /// First run of this system: builds the save from the PlayerPrefs the
        /// previous version wrote, so nobody's volume levels or tournament
        /// progress are reset by the upgrade.
        ///
        /// The old keys are left in place rather than deleted. They cost nothing,
        /// and deleting them would make a downgrade lose everything twice.
        /// </summary>
        private static GameData MigrateFromPlayerPrefs()
        {
            GameData fresh = new GameData();

            bool migrated = false;

            if (PlayerPrefs.HasKey(LegacyMusicKey))
            {
                fresh.musicVolume = PlayerPrefs.GetFloat(LegacyMusicKey, fresh.musicVolume);
                migrated = true;
            }

            if (PlayerPrefs.HasKey(LegacySfxKey))
            {
                fresh.sfxVolume = PlayerPrefs.GetFloat(LegacySfxKey, fresh.sfxVolume);
                migrated = true;
            }

            if (PlayerPrefs.HasKey(LegacyStageKey))
            {
                fresh.tournamentStage = PlayerPrefs.GetInt(LegacyStageKey, 0);
                migrated = true;
            }

            if (migrated)
            {
                Debug.Log("[Guardado] Preferencias antiguas (PlayerPrefs) migradas a " +
                          $"{FileName}.");
            }

            return fresh;
        }

        /// <summary>
        /// Installs the host that flushes deferred saves.
        ///
        /// Done from a runtime hook rather than from the scene generator: saving
        /// is not a thing the scene should have to be wired for, and a save that
        /// only worked in the generated scene would be a trap for any scene
        /// added later.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void InstallHost()
        {
            GameObject host = new GameObject("SaveManager")
            {
                // Not saved into any scene and not shown in the hierarchy: it is
                // plumbing, and a stray copy serialised into a scene would be a
                // second one flushing the same data.
                hideFlags = HideFlags.HideAndDontSave
            };

            UnityEngine.Object.DontDestroyOnLoad(host);

            host.AddComponent<SaveFlushHost>();
        }
    }

    /// <summary>
    /// Writes pending saves: on its timer, when the app loses focus, when it is
    /// paused, and when it quits.
    ///
    /// The last three are what matter on a phone, where quitting is not an event
    /// an app is guaranteed to see: a player who swipes the game away has already
    /// been paused, and that is the last moment anything can be written.
    /// </summary>
    public class SaveFlushHost : MonoBehaviour
    {
        private void Update()
        {
            if (SaveManager.IsFlushDue)
            {
                SaveManager.Flush();
            }
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
            {
                SaveManager.Flush();
            }
        }

        private void OnApplicationPause(bool isPaused)
        {
            if (isPaused)
            {
                SaveManager.Flush();
            }
        }

        private void OnApplicationQuit()
        {
            SaveManager.Flush();
        }
    }
}
