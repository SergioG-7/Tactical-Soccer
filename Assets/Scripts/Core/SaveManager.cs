using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace TacticalSoccer.Core
{
    // Datos guardados de un jugador editado, identificado por equipo y dorsal.
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

    // Todo lo que se guarda entre sesiones: ajustes y plantilla editada.
    [Serializable]
    public class GameData
    {
        public const float DefaultMusicVolume = 0.35f;
        public const float DefaultSfxVolume = 1f;
        public const float DefaultWhistleVolume = 1f;

        public string language = LocalizationManager.DefaultLanguage;

        public float musicVolume = DefaultMusicVolume;
        public float sfxVolume = DefaultSfxVolume;

        public float whistleVolume = DefaultWhistleVolume;

        public int tournamentStage;

        [Tooltip("Lista de jugadores modificados manualmente desde el panel de plantilla.")]
        public List<PlayerRecord> squad = new List<PlayerRecord>();
    }

    // Guarda y carga la partida en un archivo JSON. Los cambios frecuentes (sliders) se aplazan; los importantes se escriben al momento.
    public static class SaveManager
    {
        public const string FileName = "save_data.json";

        // Tiempo que espera un guardado aplazado antes de escribirse.
        private const float FlushDelaySeconds = 1f;

        // Claves antiguas de PlayerPrefs, solo para migrar datos de versiones previas.
        private const string LegacyMusicKey = "MusicVolume";
        private const string LegacySfxKey = "SFXVolume";
        private const string LegacyStageKey = "TournamentStage";

        private static GameData data;
        private static bool dirty;
        private static float flushDueAt;

        // Ruta del archivo de guardado en disco.
        public static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

        // Datos de la partida guardada. Se cargan del disco la primera vez que se piden; nunca es null.
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

        // Marca los datos como pendientes de guardar, sin escribir todavía.
        public static void Save()
        {
            dirty = true;
            flushDueAt = Time.unscaledTime + FlushDelaySeconds;
        }

        // Escribe el archivo de guardado inmediatamente.
        public static void SaveNow()
        {
            dirty = false;

            try
            {
                File.WriteAllText(FilePath, JsonUtility.ToJson(Data, true));
            }
            catch (Exception error)
            {
                Debug.LogWarning($"[Guardado] No se ha podido escribir {FilePath}: {error.Message}");
            }
        }

        // Escribe el archivo solo si hay cambios pendientes.
        public static void Flush()
        {
            if (dirty)
            {
                SaveNow();
            }
        }

        // Indica si toca escribir un guardado aplazado.
        internal static bool IsFlushDue => dirty && Time.unscaledTime >= flushDueAt;

        // Busca el registro guardado de un jugador, o null si nadie lo ha editado.
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

        // Devuelve el registro de un jugador, creándolo si es la primera edición.
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

        // Borra los jugadores editados, dejando el resto de ajustes intactos.
        public static void ClearSquad()
        {
            Data.squad.Clear();
            SaveNow();
        }

        // Carga los datos desde disco o los migra de PlayerPrefs si no hay archivo.
        private static void Load()
        {
            data = ReadFile() ?? MigrateFromPlayerPrefs();

            if (data.squad == null)
            {
                data.squad = new List<PlayerRecord>();
            }
        }

        // Lee y deserializa el archivo de guardado, o null si no existe o está corrupto.
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
                Debug.LogWarning($"[Guardado] {FileName} no se ha podido leer ({error.Message}). " +
                                 "Se empieza con la configuración por defecto.");

                return null;
            }
        }

        // Construye los datos iniciales a partir de las PlayerPrefs de una versión anterior.
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

        // Crea el GameObject que se encarga de volcar los guardados aplazados.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void InstallHost()
        {
            GameObject host = new GameObject("SaveManager")
            {
                hideFlags = HideFlags.HideAndDontSave
            };

            UnityEngine.Object.DontDestroyOnLoad(host);

            host.AddComponent<SaveFlushHost>();
        }
    }

    // Vuelca los guardados pendientes: por temporizador, al perder el foco, al pausarse o al cerrar la app.
    public class SaveFlushHost : MonoBehaviour
    {
        // Comprueba cada frame si toca escribir un guardado aplazado.
        private void Update()
        {
            if (SaveManager.IsFlushDue)
            {
                SaveManager.Flush();
            }
        }

        // Guarda al perder el foco la aplicación.
        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
            {
                SaveManager.Flush();
            }
        }

        // Guarda al pausarse la aplicación.
        private void OnApplicationPause(bool isPaused)
        {
            if (isPaused)
            {
                SaveManager.Flush();
            }
        }

        // Guarda al cerrar la aplicación.
        private void OnApplicationQuit()
        {
            SaveManager.Flush();
        }
    }
}
