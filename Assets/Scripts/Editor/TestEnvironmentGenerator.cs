using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.UI;
using TacticalSoccer.AI;
using TacticalSoccer.Audio;
using TacticalSoccer.CameraSystem;
using TacticalSoccer.Core;
using TacticalSoccer.Gameplay;
using TacticalSoccer.Input;
using TacticalSoccer.Player;
using TacticalSoccer.UI;
using TacticalSoccer.VFX;
using TacticalSoccer.Visuals;

namespace TacticalSoccer.Editor
{
    // Genera la escena de pruebas completa (pitch, porterías, equipos, balón, managers) usando la API de GameObjects.
    public static class TestEnvironmentGenerator
    {
        private const string GeneratedFolder = "Assets/Generated";

        // Carpeta con los audios grabados a mano.
        private const string AudioFolder = "Assets/Audio";

        private static readonly Vector3 PitchScale = new Vector3(3f, 1f, 5f);
        private const float PlayerSpawnHeight = 1f;

        private const float PixelsPerUnit = 100f;

        // Etiqueta flotante sobre cada jugador.
        private const float PlayerLabelHeight = 2.5f;
        private const float PlayerLabelScale = 0.02f;

        // Porcentaje del tanque de resistencia por debajo del cual un jugador cuenta como agotado.
        private const float ExhaustedTankShare = 0.2f;
        private static readonly Vector2 PlayerLabelCanvasSize = new Vector2(120f, 90f);
        private static readonly Vector2 PlayerLabelRoleSize = new Vector2(170f, 45f);
        private static readonly Vector2 PlayerLabelRoleOffset = new Vector2(0f, 22f);

        private const int PlayerLabelRoleFontSize = 30;
        private static readonly Vector2 PlayerLabelBarSize = new Vector2(80f, 14f);
        private static readonly Vector2 PlayerLabelBarOffset = new Vector2(0f, -14f);
        private static readonly Color PlayerLabelBarBackground = new Color(0f, 0f, 0f, 0.65f);

        // Formación por defecto: siete contra siete, portero más 2-2-2.
        private const FormationType DefaultFormation = FormationType.Balanced_2_2_2;

        private const float GoalkeeperZ = 21.5f;
        private const float GoalkeeperLateralRange = 3.5f;
        private static readonly Vector3 GoalkeeperWingspan = new Vector3(4f, 1.2f, 1f);

        private static readonly Vector3 GoalkeeperWingspanCenter = new Vector3(0f, -0.4f, 0f);

        private static readonly Vector3 BallScale = new Vector3(0.5f, 0.5f, 0.5f);
        private const float BallSpawnHeight = 0.25f;
        private const float BallBounciness = 0.6f;
        private const float BallLinearDamping = 0.4f;
        private const float BallAngularDamping = 0.8f;

        // Cámara principal del partido: en perspectiva, alta y por detrás del juego.
        private const float CameraHeight = 22f;
        private const float CameraDistanceBehind = 18f;
        private const float CameraPitchAngle = 55f;
        private const float CameraFieldOfView = 50f;
        private const float CameraSmoothTime = 0.3f;

        // Cuánto puede desplazarse la cámara de más al acercarse a una portería.
        private const float CameraExtraGoalPan = 10f;

        // Cámara del duelo: por encima del hombro del atacante, mirando hacia el defensor.
        private const float ClashCameraBackDistance = 5f;
        private const float ClashCameraHeight = 2.5f;
        private const float ClashCameraFieldOfView = 50f;

        // Cámara que persigue el balón golpeado, siguiendo su trayectoria desde atrás.
        private const float BallFlightCameraBackDistance = 6f;
        private const float BallFlightCameraHeight = 4f;
        private const float BallFlightCameraFieldOfView = 50f;
        private static readonly Vector3 ManagerParkingSpot = new Vector3(-25f, 0f, 0f);

        // Un Plane de Unity mide 10x10 a escala 1, así que el pitch acaba midiendo 30 x 50.
        private const float PitchHalfWidth = 15f;
        private const float PitchHalfLength = 25f;

        private const float GoalWidth = 7f;
        private const float GoalHeight = 2.5f;
        private const float GoalPostRadius = 0.1f;
        private const float GoalLineInset = 1f;
        private const float GoalTriggerDepth = 1f;

        private const int PitchTextureWidth = 512;

        private const TeamId HumanTeam = TeamId.Blue;

        // Estela del balón: corta y que se desvanece rápido.
        private const float BallTrailTime = 0.3f;
        private const float BallTrailStartWidth = 0.3f;

        // Nombres de todos los objetos raíz que crea esta herramienta, para poder borrarlos antes de regenerar.
        private static readonly string[] GeneratedRootNames =
        {
            "GameManager", "Pitch", "Surroundings", "Goal North", "Goal South",
            "Team Blue", "Team Red", "Ball", "InputManager",
            "UI Canvas", "EventSystem"
        };

        // Todo lo que rodea el terreno de juego fuera de las líneas.
        private static readonly Vector3 SurroundScale = new Vector3(9f, 1f, 11f);
        private const float SurroundY = -0.03f;

        // Igual que TacticalSoccer.Core.PitchBounds: marca las esquinas de las líneas pintadas, no del césped.
        private const float PitchBoundsSideX = 13.5f;
        private const float PitchBoundsGoalZ = 23.5f;

        // Banquillos justo fuera del área de juego, con las gradas escalonándose detrás.
        private const float DugoutX = PitchBoundsSideX + 2f;
        private const float DugoutLength = 8f;

        // Altura del techo del banquillo, con margen suficiente para que quepa un jugador de pie.
        private const float DugoutRoofY = 2.9f;
        private const float DugoutRoofThickness = 0.15f;
        private const float StandX = PitchBoundsSideX + 5f;
        private const int StandStepCount = 3;
        private const float StandStepRise = 0.7f;
        private const float StandStepDepth = 2.2f;
        private const float StandLength = 52f;

        // Banquillo: tres suplentes por equipo, colocados dentro de su propio banquillo.
        private const int SubstituteCount = 3;
        private const float SubstituteSpacing = 2.5f;

        // Numeración de camisetas: el 1 es el portero, del 2 al 7 titulares, del 8 al 10 suplentes.
        private const int GoalkeeperJerseyNumber = 1;
        private const int FirstStarterJerseyNumber = 2;

        // Público en las gradas: 3 filas x 29 por equipo.
        private const float SpectatorSpacing = 1.8f;
        private static readonly Vector3 SpectatorScale = new Vector3(0.45f, 0.45f, 0.45f);

        private const float CornerFlagHeight = 2f;
        private const float CornerFlagRadius = 0.1f;
        private const float CornerPennantSize = 1f;

        // Red de la portería.
        private const float GoalNetDepth = 1.2f;
        private const float GoalNetThickness = 0.08f;

        // Genera toda la escena de pruebas: pitch, equipos, balón, cámaras, UI y managers.
        [MenuItem("Tactical Soccer/Generar Escena de Pruebas")]
        private static void GenerateTestScene()
        {
            ClearPreviousGeneration();

            GameObject gameManager = CreateGameManager();
            CreatePitch();
            CreateSurroundings();
            CreateGoals();
            CreateTeam("Team Blue", "Player", Color.blue, TeamId.Blue, southSide: true);
            CreateTeam("Team Red", "Player", Color.red, TeamId.Red, southSide: false);

            GameObject ball = CreateBall();

            CreateInputManager();
            ConfigureMatchCamera(ball.transform);

            ClashUIController clashUI = CreateClashUI();
            gameManager.GetComponent<ClashManager>().uiController = clashUI;

            CreateScoreUI(uiHudArea, gameManager.GetComponent<ScoreManager>());
            CreateAnnouncerUI(uiHudArea);
            CreateTensionUI(uiHudArea);

            CreateHalftimeUI(uiCanvasRoot);
            CreateSubstitutionUI(uiCanvasRoot);
            CreatePenaltyUI(uiCanvasRoot);

            CreateMatchOverUI(uiCanvasRoot);

            FormationUIController formationUI = CreateFormationUI(uiCanvasRoot);
            MatchConfigUIController configUI = CreateMatchConfigUI(uiCanvasRoot, formationUI);

            CreateTitleScreenUI(uiCanvasRoot, configUI, formationUI);

            CreateDebugMenuUI(uiCanvasRoot);

            CreateAudioSettingsUI(uiCanvasRoot);

            CreatePlayerEditUI(uiCanvasRoot);

            CreateEventSystem();

            AssetDatabase.SaveAssets();
        }

        // Borra los objetos generados en la ejecución anterior antes de crear una nueva escena.
        private static void ClearPreviousGeneration()
        {
            foreach (GameObject root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (System.Array.IndexOf(GeneratedRootNames, root.name) >= 0)
                {
                    Undo.DestroyObjectImmediate(root);
                }
            }
        }

        // Crea el GameObject GameManager con todos los managers del partido.
        private static GameObject CreateGameManager()
        {
            GameObject gameManager = new GameObject("GameManager");
            Undo.RegisterCreatedObjectUndo(gameManager, "Create GameManager");

            gameManager.transform.position = ManagerParkingSpot + Vector3.left * 2f;
            gameManager.AddComponent<TimeController>();
            gameManager.AddComponent<ScoreManager>();
            gameManager.AddComponent<MatchManager>();
            gameManager.AddComponent<EnemyAIController>();
            gameManager.AddComponent<ClashManager>();

            gameManager.AddComponent<TensionManager>();

            VFXManager vfx = gameManager.AddComponent<VFXManager>();
            vfx.ConfigureImpactMaterial(GetOrCreateImpactMaterial());

            FloatingTextManager floatingText = gameManager.AddComponent<FloatingTextManager>();
            floatingText.ConfigureFont(GetUIFont());

            ConfigureAudio(gameManager.AddComponent<AudioManager>());

            gameManager.AddComponent<TournamentManager>();

            // Reaplica los cambios de plantilla guardados a los jugadores.
            gameManager.AddComponent<SquadPersistence>();

            return gameManager;
        }

        // Carga y asigna los clips de audio al AudioManager.
        private static void ConfigureAudio(AudioManager audio)
        {
            AudioClip shortWhistle = LoadAudioClip("whistle-short.aiff");
            AudioClip longWhistle = LoadAudioClip("whistle-long.wav");
            AudioClip fullTimeWhistle = LoadAudioClip("whistle-full-time.wav");
            AudioClip kick = LoadAudioClip("kick-ball.wav");
            AudioClip tension = LoadAudioClip("tension-max.wav");
            AudioClip crowd = LoadAudioClip("crowd-ambience.wav");
            AudioClip click = LoadAudioClip("click.mp3");

            // Varios sonidos reutilizan el mismo clip (el pitido de falta usa el silbato corto, etc.).
            audio.ConfigureClips(shortWhistle, longWhistle, fullTimeWhistle, kick,
                net: null, impact: kick, foul: shortWhistle,
                tension: tension, stadium: crowd, cheer: crowd, click: click);

            EditorUtility.SetDirty(audio);

            ReportMissingAudio(
                ("silbato corto", shortWhistle),
                ("silbato largo", longWhistle),
                ("silbato final", fullTimeWhistle),
                ("golpeo de balón", kick),
                ("zona de ardor", tension),
                ("ambiente de público", crowd),
                ("click de menú", click));

        }

        // Carga un clip de audio desde la carpeta de audios por nombre de archivo.
        private static AudioClip LoadAudioClip(string fileName)
        {
            return AssetDatabase.LoadAssetAtPath<AudioClip>($"{AudioFolder}/{fileName}");
        }

        // Avisa por consola de qué clips de audio no se encontraron.
        private static void ReportMissingAudio(params (string Label, AudioClip Clip)[] clips)
        {
            string missing = string.Empty;

            foreach ((string label, AudioClip clip) in clips)
            {
                if (clip != null)
                {
                    continue;
                }

                missing += missing.Length == 0 ? label : $", {label}";
            }

            if (missing.Length == 0)
            {
                return;
            }

            Debug.LogWarning($"[Audio] Sin sonido (falta el archivo en {AudioFolder}): {missing}. " +
                             "El partido funciona igual, esos momentos salen mudos.");
        }

        // Crea el terreno de juego con su textura y material.
        private static void CreatePitch()
        {
            GameObject pitch = GameObject.CreatePrimitive(PrimitiveType.Plane);
            Undo.RegisterCreatedObjectUndo(pitch, "Create Pitch");

            pitch.name = "Pitch";
            pitch.transform.localScale = PitchScale;

            Texture2D pitchTexture = GetOrRefreshPitchTexture();
            Material pitchMaterial = GetOrCreateMaterial("PitchMaterial.mat", Color.white, pitchTexture);
            ApplyMaterial(pitch, pitchMaterial);

            SetLayerByName(pitch, "Ground");
        }

        // Crea la decoración alrededor del terreno de juego: césped exterior, banquillos y banderines de córner.
        private static void CreateSurroundings()
        {
            GameObject root = new GameObject("Surroundings");
            Undo.RegisterCreatedObjectUndo(root, "Create Surroundings");

            GameObject apron = GameObject.CreatePrimitive(PrimitiveType.Plane);
            Undo.RegisterCreatedObjectUndo(apron, "Create Pitch Apron");

            apron.name = "Outer Grass";
            apron.transform.SetParent(root.transform, false);
            apron.transform.localScale = SurroundScale;

            // Ligeramente por debajo del terreno de juego para evitar z-fighting.
            apron.transform.position = new Vector3(0f, SurroundY, 0f);

            ApplyMaterial(apron, GetOrCreateMaterial("OuterGrassMaterial.mat", new Color(0.10f, 0.26f, 0.13f), null));
            StripCollider(apron);

            CreateStadium(root);

            Material poleMaterial = GetOrCreateMaterial("CornerPoleMaterial.mat", Color.white, null);
            Material pennantMaterial = GetOrCreateMaterial("CornerFlagMaterial.mat", new Color(0.95f, 0.85f, 0.15f), null);

            foreach (int xSign in new[] { -1, 1 })
            {
                foreach (int zSign in new[] { -1, 1 })
                {
                    CreateCornerFlag(root, $"Corner Flag {xSign} {zSign}",
                        new Vector3(xSign * PitchBoundsSideX, 0f, zSign * PitchBoundsGoalZ),
                        poleMaterial, pennantMaterial);
                }
            }
        }

        // Crea los banquillos y las gradas con público a ambos lados del campo.
        private static void CreateStadium(GameObject root)
        {
            Material shelterMaterial = GetOrCreateMaterial("DugoutMaterial.mat", new Color(0.30f, 0.31f, 0.34f), null);
            Material benchMaterial = GetOrCreateMaterial("BenchMaterial.mat", new Color(0.78f, 0.78f, 0.80f), null);
            Material blueMaterial = GetOrCreateMaterial("TeamBlueMaterial.mat", Color.blue, null);
            Material redMaterial = GetOrCreateMaterial("TeamRedMaterial.mat", Color.red, null);
            Material concreteMaterial = GetOrCreateMaterial("StandMaterial.mat", new Color(0.42f, 0.43f, 0.46f), null);

            CreateDugout(root, "Dugout Blue", new Vector3(-DugoutX, 0f, 0f), shelterMaterial, benchMaterial, blueMaterial);
            CreateDugout(root, "Dugout Red", new Vector3(DugoutX, 0f, 0f), shelterMaterial, benchMaterial, redMaterial);

            CreateStand(root, "Stand West", -1f, concreteMaterial, blueMaterial, redMaterial);
            CreateStand(root, "Stand East", 1f, concreteMaterial, blueMaterial, redMaterial);
        }

        // Crea un banquillo con pared trasera, techo y banco, con una franja del color del equipo.
        private static void CreateDugout(GameObject parent, string dugoutName, Vector3 position,
            Material shelterMaterial, Material benchMaterial, Material teamMaterial)
        {
            GameObject dugout = new GameObject(dugoutName);
            Undo.RegisterCreatedObjectUndo(dugout, $"Create {dugoutName}");

            dugout.transform.SetParent(parent.transform, false);
            dugout.transform.position = position;

            float outward = Mathf.Sign(position.x);

            CreateProp(dugout, "Back Wall", new Vector3(outward * 0.9f, DugoutRoofY * 0.5f, 0f),
                new Vector3(0.2f, DugoutRoofY, DugoutLength), shelterMaterial);

            CreateProp(dugout, "Roof", new Vector3(0f, DugoutRoofY + (DugoutRoofThickness * 0.5f), 0f),
                new Vector3(2f, DugoutRoofThickness, DugoutLength), shelterMaterial);

            CreateProp(dugout, "Roof Trim", new Vector3(-outward * 0.95f, DugoutRoofY + (DugoutRoofThickness * 0.5f), 0f),
                new Vector3(0.2f, 0.2f, DugoutLength), teamMaterial);

            CreateProp(dugout, "Bench", new Vector3(outward * 0.4f, 0.35f, 0f),
                new Vector3(0.8f, 0.7f, DugoutLength - 1f), benchMaterial);
        }

        // Crea una grada escalonada a lo largo de una banda, con espectadores en cada escalón.
        private static void CreateStand(GameObject parent, string standName, float side,
            Material concreteMaterial, Material blueMaterial, Material redMaterial)
        {
            GameObject stand = new GameObject(standName);
            Undo.RegisterCreatedObjectUndo(stand, $"Create {standName}");

            stand.transform.SetParent(parent.transform, false);

            int spectatorsPerRow = Mathf.FloorToInt(StandLength / SpectatorSpacing);
            float firstZ = -(spectatorsPerRow - 1) * SpectatorSpacing * 0.5f;

            for (int step = 0; step < StandStepCount; step++)
            {
                float height = StandStepRise * (step + 1);
                float x = side * (StandX + (step * StandStepDepth));

                CreateProp(stand, $"Step {step + 1}",
                    new Vector3(x, height * 0.5f, 0f),
                    new Vector3(StandStepDepth, height, StandLength),
                    concreteMaterial);

                for (int seat = 0; seat < spectatorsPerRow; seat++)
                {
                    CreateSpectator(stand,
                        new Vector3(x, height + (SpectatorScale.y * 1f), firstZ + (seat * SpectatorSpacing)),
                        ((seat + step) / 2) % 2 == 0 ? blueMaterial : redMaterial);
                }
            }
        }

        // Crea un espectador individual en la grada.
        private static void CreateSpectator(GameObject parent, Vector3 position, Material material)
        {
            GameObject spectator = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Undo.RegisterCreatedObjectUndo(spectator, "Create Spectator");

            spectator.name = "Spectator";
            spectator.transform.SetParent(parent.transform, false);
            spectator.transform.position = position;
            spectator.transform.localScale = SpectatorScale;

            ApplyMaterial(spectator, material);
            StripCollider(spectator);

            spectator.AddComponent<SpectatorAnimator>();
        }

        // Crea un banderín de córner: un poste con un banderín en la punta.
        private static void CreateCornerFlag(GameObject parent, string flagName, Vector3 position,
            Material poleMaterial, Material pennantMaterial)
        {
            GameObject flag = new GameObject(flagName);
            Undo.RegisterCreatedObjectUndo(flag, $"Create {flagName}");

            flag.transform.SetParent(parent.transform, false);
            flag.transform.position = position;

            GameObject pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Undo.RegisterCreatedObjectUndo(pole, $"Create {flagName} Pole");

            pole.name = "Pole";
            pole.transform.SetParent(flag.transform, false);
            pole.transform.localPosition = new Vector3(0f, CornerFlagHeight * 0.5f, 0f);
            pole.transform.localScale = new Vector3(CornerFlagRadius, CornerFlagHeight * 0.5f, CornerFlagRadius);

            ApplyMaterial(pole, poleMaterial);
            StripCollider(pole);

            GameObject pennant = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Undo.RegisterCreatedObjectUndo(pennant, $"Create {flagName} Pennant");

            pennant.name = "Pennant";
            pennant.transform.SetParent(flag.transform, false);
            pennant.transform.localPosition = new Vector3(0f, CornerFlagHeight, 0f);
            pennant.transform.localScale = Vector3.one * CornerPennantSize;
            pennant.transform.localRotation = Quaternion.Euler(90f, 45f, 0f);

            ApplyMaterial(pennant, pennantMaterial);
            StripCollider(pennant);
        }

        // Crea un cubo decorativo simple con la posición, escala y material dados.
        private static void CreateProp(GameObject parent, string propName, Vector3 localPosition,
            Vector3 scale, Material material)
        {
            GameObject prop = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Undo.RegisterCreatedObjectUndo(prop, $"Create {propName}");

            prop.name = propName;
            prop.transform.SetParent(parent.transform, false);
            prop.transform.localPosition = localPosition;
            prop.transform.localScale = scale;

            ApplyMaterial(prop, material);
            StripCollider(prop);
        }

        // Elimina el collider de un objeto puramente decorativo.
        private static void StripCollider(GameObject target)
        {
            if (target.TryGetComponent(out Collider collider))
            {
                Undo.DestroyObjectImmediate(collider);
            }
        }

        // Crea las dos porterías, una en cada extremo del campo.
        private static void CreateGoals()
        {
            float goalZ = PitchHalfLength - GoalLineInset;

            CreateGoal("Goal North", new Vector3(0f, 0f, goalZ), Quaternion.identity, ScoreManager.BlueTeamId);
            CreateGoal("Goal South", new Vector3(0f, 0f, -goalZ), Quaternion.Euler(0f, 180f, 0f), ScoreManager.RedTeamId);
        }

        // Crea una portería con dos postes, un larguero y un trigger invisible que detecta el gol.
        private static void CreateGoal(string goalName, Vector3 position, Quaternion rotation, int teamToScore)
        {
            GameObject goalRoot = new GameObject(goalName);
            Undo.RegisterCreatedObjectUndo(goalRoot, $"Create {goalName}");

            goalRoot.transform.SetPositionAndRotation(position, rotation);

            Material goalMaterial = GetOrCreateMaterial("GoalMaterial.mat", Color.white, null);
            float halfWidth = GoalWidth * 0.5f;

            CreateGoalPart(goalRoot, goalMaterial, "Post Left",
                new Vector3(-halfWidth, GoalHeight * 0.5f, 0f),
                Quaternion.identity,
                new Vector3(GoalPostRadius, GoalHeight * 0.5f, GoalPostRadius));

            CreateGoalPart(goalRoot, goalMaterial, "Post Right",
                new Vector3(halfWidth, GoalHeight * 0.5f, 0f),
                Quaternion.identity,
                new Vector3(GoalPostRadius, GoalHeight * 0.5f, GoalPostRadius));

            // El larguero se rota 90 grados porque el cilindro nace con el eje en Y.
            CreateGoalPart(goalRoot, goalMaterial, "Crossbar",
                new Vector3(0f, GoalHeight, 0f),
                Quaternion.Euler(0f, 0f, 90f),
                new Vector3(GoalPostRadius, halfWidth, GoalPostRadius));

            CreateGoalNet(goalRoot, halfWidth);
            CreateGoalTrigger(goalRoot, teamToScore);
        }

        // Crea la red de la portería con tres paneles (izquierdo, derecho y trasero) que sí colisionan con el balón.
        private static void CreateGoalNet(GameObject goalRoot, float halfWidth)
        {
            Material netMaterial = GetOrCreateNetMaterial();

            CreateNetPanel(goalRoot, netMaterial, "Net Left",
                new Vector3(-halfWidth, GoalHeight * 0.5f, GoalNetDepth * 0.5f),
                new Vector3(GoalNetThickness, GoalHeight, GoalNetDepth));

            CreateNetPanel(goalRoot, netMaterial, "Net Right",
                new Vector3(halfWidth, GoalHeight * 0.5f, GoalNetDepth * 0.5f),
                new Vector3(GoalNetThickness, GoalHeight, GoalNetDepth));

            CreateNetPanel(goalRoot, netMaterial, "Net Back",
                new Vector3(0f, GoalHeight * 0.5f, GoalNetDepth),
                new Vector3(halfWidth * 2f, GoalHeight, GoalNetThickness));
        }

        // Crea un panel de red individual.
        private static void CreateNetPanel(GameObject parent, Material material, string panelName,
            Vector3 localPosition, Vector3 localScale)
        {
            GameObject panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Undo.RegisterCreatedObjectUndo(panel, $"Create {panelName}");

            panel.name = panelName;
            panel.transform.SetParent(parent.transform, false);
            panel.transform.localPosition = localPosition;
            panel.transform.localScale = localScale;

            ApplyMaterial(panel, material);
        }

        // Crea una pieza cilíndrica del marco de la portería (poste o larguero) sin collider.
        private static void CreateGoalPart(GameObject parent, Material material, string partName,
            Vector3 localPosition, Quaternion localRotation, Vector3 localScale)
        {
            GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Undo.RegisterCreatedObjectUndo(part, $"Create {partName}");

            part.name = partName;
            part.transform.SetParent(parent.transform, false);
            part.transform.localPosition = localPosition;
            part.transform.localRotation = localRotation;
            part.transform.localScale = localScale;

            if (part.TryGetComponent(out Collider collider))
            {
                Undo.DestroyObjectImmediate(collider);
            }

            ApplyMaterial(part, material);
        }

        // Crea el trigger invisible que detecta cuando el balón entra en la portería.
        private static void CreateGoalTrigger(GameObject goalRoot, int teamToScore)
        {
            GameObject trigger = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Undo.RegisterCreatedObjectUndo(trigger, "Create Goal Trigger");

            trigger.name = "Goal Trigger";
            trigger.transform.SetParent(goalRoot.transform, false);
            trigger.transform.localPosition = new Vector3(0f, GoalHeight * 0.5f, 0f);
            trigger.transform.localScale = new Vector3(GoalWidth, GoalHeight, GoalTriggerDepth);

            if (trigger.TryGetComponent(out MeshRenderer meshRenderer))
            {
                Undo.DestroyObjectImmediate(meshRenderer);
            }

            if (trigger.TryGetComponent(out MeshFilter meshFilter))
            {
                Undo.DestroyObjectImmediate(meshFilter);
            }

            if (trigger.TryGetComponent(out BoxCollider boxCollider))
            {
                boxCollider.isTrigger = true;
            }

            SetTagByName(trigger, "Goal");
            SetLayerByName(trigger, "Goal");

            GoalDetector detector = trigger.AddComponent<GoalDetector>();
            detector.ConfigureTeam(teamToScore);
        }

        // Crea un equipo completo: titulares según la formación, portero y suplentes.
        private static void CreateTeam(string teamName, string layerName, Color color, TeamId teamId, bool southSide)
        {
            GameObject teamRoot = new GameObject(teamName);
            Undo.RegisterCreatedObjectUndo(teamRoot, $"Create {teamName}");

            float side = southSide ? -1f : 1f;
            Material teamMaterial = GetOrCreateMaterial($"{teamName.Replace(" ", string.Empty)}Material.mat", color, null);

            PlayerStatsSO strikerStats = GetOrCreateStats("StrikerStats.asset",
                dribble: 80, power: 70, shoot: 80, tackle: 20, block: 25, goalkeeping: 10);

            PlayerStatsSO midfielderStats = GetOrCreateStats("MidfielderStats.asset",
                dribble: 60, power: 55, shoot: 55, tackle: 55, block: 50, goalkeeping: 15);

            PlayerStatsSO defenderStats = GetOrCreateStats("DefenderStats.asset",
                dribble: 20, power: 45, shoot: 30, tackle: 80, block: 75, goalkeeping: 25);

            int roleCount = 0;
            PlayerRole previousRole = PlayerRole.Goalkeeper;
            int jerseyNumber = FirstStarterJerseyNumber;

            foreach (FormationSlot slot in Formations.Get(DefaultFormation))
            {
                roleCount = slot.Role == previousRole ? roleCount + 1 : 1;
                previousRole = slot.Role;

                PlayerStatsSO stats = ResolveStats(slot.Role, strikerStats, midfielderStats, defenderStats);

                CreatePlayer(teamRoot, $"{teamName} {slot.Role} {roleCount}",
                    new Vector3(slot.X, PlayerSpawnHeight, side * slot.OwnHalfZ),
                    layerName, teamId, slot.Role, teamMaterial, stats,
                    jerseyNumber, isStarter: true);

                jerseyNumber++;
            }

            PlayerStatsSO goalkeeperStats = GetOrCreateStats("GoalkeeperStats.asset",
                dribble: 10, power: 40, shoot: 10, tackle: 60, block: 70, goalkeeping: 85);

            CreateGoalkeeper(teamRoot, teamName, side, layerName, teamId, goalkeeperStats, teamMaterial);

            CreateSubstitutes(teamRoot, teamName, layerName, teamId, teamMaterial,
                jerseyNumber, strikerStats, midfielderStats, defenderStats);
        }

        // Crea los tres suplentes del equipo, uno por línea, sentados en su banquillo.
        private static void CreateSubstitutes(GameObject teamRoot, string teamName, string layerName,
            TeamId teamId, Material teamMaterial, int firstJerseyNumber,
            PlayerStatsSO strikerStats, PlayerStatsSO midfielderStats, PlayerStatsSO defenderStats)
        {
            float dugoutSide = teamId == TeamId.Blue ? -1f : 1f;

            PlayerRole[] benchRoles =
            {
                PlayerRole.Defender, PlayerRole.Midfielder, PlayerRole.Forward
            };

            float firstZ = -(SubstituteCount - 1) * SubstituteSpacing * 0.5f;

            for (int i = 0; i < SubstituteCount; i++)
            {
                PlayerRole role = benchRoles[i % benchRoles.Length];
                PlayerStatsSO stats = ResolveStats(role, strikerStats, midfielderStats, defenderStats);

                Vector3 seat = new Vector3(
                    dugoutSide * DugoutX,
                    PlayerSpawnHeight,
                    firstZ + (i * SubstituteSpacing));

                CreatePlayer(teamRoot, $"{teamName} Sub {role}", seat,
                    layerName, teamId, role, teamMaterial, stats,
                    firstJerseyNumber + i, isStarter: false);
            }
        }

        // Asigna un elemento a un jugador según su número de camiseta, desfasado entre los dos equipos.
        private static Element ResolveElement(TeamId teamId, int jerseyNumber)
        {
            int elementCount = System.Enum.GetValues(typeof(Element)).Length;
            int offset = teamId == TeamId.Blue ? 0 : 1;

            return (Element)(((jerseyNumber + offset) % elementCount + elementCount) % elementCount);
        }

        // Devuelve el bloque de stats correspondiente al rol del jugador.
        private static PlayerStatsSO ResolveStats(PlayerRole role,
            PlayerStatsSO striker, PlayerStatsSO midfielder, PlayerStatsSO defender)
        {
            switch (role)
            {
                case PlayerRole.Forward: return striker;
                case PlayerRole.Midfielder: return midfielder;
                default: return defender;
            }
        }

        // Crea un jugador con su cápsula, stats, aspecto y componentes de juego.
        private static GameObject CreatePlayer(GameObject teamRoot, string playerName, Vector3 position,
            string layerName, TeamId teamId, PlayerRole role, Material material, PlayerStatsSO stats,
            int jerseyNumber, bool isStarter)
        {
            GameObject player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Undo.RegisterCreatedObjectUndo(player, $"Create {playerName}");

            player.name = playerName;
            player.transform.SetParent(teamRoot.transform);
            player.transform.position = position;

            SetLayerByName(player, layerName);
            SetTagByName(player, "Player");
            ApplyMaterial(player, material);

            TeamMember teamMember = player.AddComponent<TeamMember>();
            teamMember.team = teamId;
            teamMember.stats = stats;
            teamMember.role = role;
            teamMember.jerseyNumber = jerseyNumber;
            teamMember.isStarter = isStarter;
            teamMember.element = ResolveElement(teamId, jerseyNumber);

            AttachPlayerSkin(player, teamMember);

            teamMember.currentStamina = teamMember.maxStamina;
            teamMember.exhaustedThreshold = teamMember.maxStamina * ExhaustedTankShare;

            player.AddComponent<PlayerRoute>();

            // Aura que marca a un jugador cuando su equipo está en la zona de ardor.
            TensionAura aura = player.AddComponent<TensionAura>();
            aura.ConfigureMaterial(GetOrCreateAuraMaterial());

            player.AddComponent<TacticalPositioning>();

            ApplyRouteLineMaterial(player);

            SetupBallInteraction(player);

            CreatePlayerLabel(player, teamMember);

            return player;
        }

        // Prefab del kit de personajes "Football Essentials 3D" usado para vestir a los jugadores.
        private const string CharacterKitPrefab =
            "Assets/Lightning Poly/Football Essentials 3D/Prefabs/Demo_Capsule.prefab";
        private const string CharacterKitTexture =
            "Assets/Lightning Poly/Football Essentials 3D/Material/Colors.png";

        private const float CharacterKitScale = 7.224f;
        private const float CharacterKitYaw = 180f;

        private static readonly string[] CharacterKitFeatureGroups = { "Eyes", "Mouths", "Hairs" };

        private const string CharacterKitGlasses = "Glasses";
        private const string CharacterKitDecorations = "Decorations";

        // Viste a un jugador con una cara aleatoria del kit de personajes, manteniendo la cápsula del juego.
        private static void AttachPlayerSkin(GameObject player, TeamMember member)
        {
            GameObject kit = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterKitPrefab);

            if (kit == null)
            {
                return;
            }

            Transform kitBody = kit.transform.childCount > 0 ? kit.transform.GetChild(0) : null;

            if (kitBody == null)
            {
                return;
            }

            GameObject skinRoot = new GameObject("Skin");
            skinRoot.transform.SetParent(player.transform, false);

            skinRoot.transform.localPosition = new Vector3(0f, -1f, 0f);
            skinRoot.transform.localRotation = Quaternion.Euler(0f, CharacterKitYaw, 0f);
            skinRoot.transform.localScale = Vector3.one * CharacterKitScale;

            GameObject kitInstance = (GameObject)PrefabUtility.InstantiatePrefab(kit);

            if (kitInstance == null)
            {
                Object.DestroyImmediate(skinRoot);
                return;
            }

            PrefabUtility.UnpackPrefabInstance(kitInstance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

            GameObject body = kitInstance.transform.GetChild(0).gameObject;
            body.transform.SetParent(skinRoot.transform, false);

            Object.DestroyImmediate(kitInstance);

            if (body.TryGetComponent(out MeshRenderer bodyRenderer))
            {
                bodyRenderer.enabled = false;
            }

            System.Random dice = new System.Random(((int)member.team * 1000) + member.jerseyNumber);

            foreach (string group in CharacterKitFeatureGroups)
            {
                KeepOneVariant(body.transform.Find(group), dice, alwaysWorn: true);
            }

            KeepOneVariant(body.transform.Find(CharacterKitGlasses), dice, alwaysWorn: false);
            KeepOneVariant(body.transform.Find(CharacterKitDecorations), dice, alwaysWorn: false);

            ApplyCharacterKitMaterial(skinRoot);

            foreach (Collider collider in skinRoot.GetComponentsInChildren<Collider>(true))
            {
                Object.DestroyImmediate(collider);
            }
        }

        // Deja activa una sola variante del grupo (o ninguna, si es opcional y así lo decide la tirada).
        private static void KeepOneVariant(Transform group, System.Random dice, bool alwaysWorn)
        {
            if (group == null || group.childCount == 0)
            {
                return;
            }

            int chosen = alwaysWorn || dice.Next(0, 3) == 0 ? dice.Next(0, group.childCount) : -1;

            for (int i = 0; i < group.childCount; i++)
            {
                group.GetChild(i).gameObject.SetActive(i == chosen);
            }
        }

        // Aplica un material URP con la paleta del kit a todas las partes del personaje.
        private static void ApplyCharacterKitMaterial(GameObject skinRoot)
        {
            Material material = GetOrCreateCharacterKitMaterial();

            if (material == null)
            {
                return;
            }

            foreach (Renderer renderer in skinRoot.GetComponentsInChildren<Renderer>(true))
            {
                Material[] slots = renderer.sharedMaterials;

                for (int i = 0; i < slots.Length; i++)
                {
                    slots[i] = material;
                }

                renderer.sharedMaterials = slots;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        // Crea o reutiliza el material URP con el atlas del kit de personajes.
        private static Material GetOrCreateCharacterKitMaterial()
        {
            Texture2D atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(CharacterKitTexture);

            if (atlas == null)
            {
                Debug.LogWarning($"[Skins] No se encuentra el atlas {CharacterKitTexture}: " +
                                 "las caras saldrían en magenta, así que se omiten.");
                return null;
            }

            return GetOrCreateMaterial("CharacterKitMaterial.mat", Color.white, atlas);
        }

        // Crea la etiqueta flotante sobre el jugador con el rol y la barra de resistencia.
        private static void CreatePlayerLabel(GameObject player, TeamMember member)
        {
            GameObject labelObject = new GameObject("Player Label", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(labelObject, $"Create {player.name} Label");

            labelObject.transform.SetParent(player.transform, false);

            RectTransform rect = (RectTransform)labelObject.transform;
            rect.sizeDelta = PlayerLabelCanvasSize;
            rect.localPosition = new Vector3(0f, PlayerLabelHeight, 0f);
            rect.localScale = Vector3.one * PlayerLabelScale;
            rect.localRotation = Quaternion.Euler(90f, 0f, 0f);

            Canvas canvas = labelObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            Text roleText = CreateLabelText(labelObject.transform);
            Image staminaFill = CreateStaminaBar(labelObject.transform);

            PlayerLabelUI label = labelObject.AddComponent<PlayerLabelUI>();
            label.roleText = roleText;
            label.staminaBar = staminaFill;
            label.Setup(member);
        }

        // Crea el texto con el rol y número de camiseta que aparece sobre el jugador.
        private static Text CreateLabelText(Transform parent)
        {
            GameObject textObject = new GameObject("Role Text", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(textObject, "Create Role Text");
            textObject.transform.SetParent(parent, false);

            RectTransform rect = (RectTransform)textObject.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = PlayerLabelRoleOffset;
            rect.sizeDelta = PlayerLabelRoleSize;

            Text text = textObject.AddComponent<Text>();
            text.font = GetPlayerTagFont();
            text.fontSize = PlayerLabelRoleFontSize;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.supportRichText = true;
            text.text = "--";

            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;

            Outline outline = textObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
            outline.effectDistance = new Vector2(2f, -2f);

            return text;
        }

        // Crea la barra de resistencia (fondo oscuro + relleno) y devuelve el relleno.
        private static Image CreateStaminaBar(Transform parent)
        {
            Sprite whiteSprite = GetOrCreateWhiteSprite();

            GameObject backgroundObject = new GameObject("Stamina Background", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(backgroundObject, "Create Stamina Background");
            backgroundObject.transform.SetParent(parent, false);

            RectTransform backgroundRect = (RectTransform)backgroundObject.transform;
            backgroundRect.anchorMin = new Vector2(0.5f, 0.5f);
            backgroundRect.anchorMax = new Vector2(0.5f, 0.5f);
            backgroundRect.pivot = new Vector2(0.5f, 0.5f);
            backgroundRect.anchoredPosition = PlayerLabelBarOffset;
            backgroundRect.sizeDelta = PlayerLabelBarSize;

            Image background = backgroundObject.AddComponent<Image>();
            background.sprite = whiteSprite;
            background.color = PlayerLabelBarBackground;

            GameObject fillObject = new GameObject("Stamina Fill", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(fillObject, "Create Stamina Fill");
            fillObject.transform.SetParent(backgroundObject.transform, false);

            RectTransform fillRect = (RectTransform)fillObject.transform;
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;

            Image fill = fillObject.AddComponent<Image>();
            fill.sprite = whiteSprite;
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;

            fill.fillOrigin = (int)Image.OriginHorizontal.Left;
            fill.fillAmount = 1f;

            return fill;
        }

        // Crea el portero: un jugador normal más la IA de portero y una zona de alcance más ancha.
        private static void CreateGoalkeeper(GameObject teamRoot, string teamName, float side,
            string layerName, TeamId teamId, PlayerStatsSO stats, Material teamMaterial)
        {
            GameObject keeper = CreatePlayer(teamRoot, $"{teamName} Goalkeeper",
                new Vector3(0f, PlayerSpawnHeight, side * GoalkeeperZ),
                layerName, teamId, PlayerRole.Goalkeeper, teamMaterial, stats,
                GoalkeeperJerseyNumber, isStarter: true);

            keeper.GetComponent<TeamMember>().isGoalkeeper = true;

            BoxCollider wingspan = keeper.AddComponent<BoxCollider>();
            wingspan.isTrigger = true;
            wingspan.center = GoalkeeperWingspanCenter;
            wingspan.size = GoalkeeperWingspan;

            GoalkeeperAI keeperAI = keeper.AddComponent<GoalkeeperAI>();
            keeperAI.maxLateralMovement = GoalkeeperLateralRange;
            keeperAI.autoClearance = teamId != HumanTeam;
        }

        // Configura el collider trigger, el socket del balón y el manejador de balón del jugador.
        private static void SetupBallInteraction(GameObject player)
        {
            if (player.TryGetComponent(out CapsuleCollider capsuleCollider))
            {
                capsuleCollider.isTrigger = true;
            }

            Rigidbody rb = player.AddComponent<Rigidbody>();
            rb.isKinematic = true;

            GameObject ballSocket = new GameObject("BallSocket");
            Undo.RegisterCreatedObjectUndo(ballSocket, $"Create {player.name} BallSocket");
            ballSocket.transform.SetParent(player.transform);
            ballSocket.transform.localPosition = new Vector3(0f, -0.75f, -0.55f);

            PlayerBallHandler ballHandler = player.AddComponent<PlayerBallHandler>();
            ballHandler.AssignBallSocket(ballSocket.transform);
        }

        // Crea el balón con su textura, material físico, rigidbody y controlador.
        private static GameObject CreateBall()
        {
            GameObject ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Undo.RegisterCreatedObjectUndo(ball, "Create Ball");

            ball.name = "Ball";
            ball.transform.position = new Vector3(0f, BallSpawnHeight, 0f);
            ball.transform.localScale = BallScale;

            Texture2D ballTexture = GetOrCreateAsset("BallTexture.asset", CreateBallTexture);
            Material ballMaterial = GetOrCreateMaterial("BallMaterial.mat", Color.white, ballTexture);
            ApplyMaterial(ball, ballMaterial);

            if (ball.TryGetComponent(out SphereCollider sphereCollider))
            {
                sphereCollider.sharedMaterial = GetOrCreateAsset("BallPhysics.asset", CreateBallPhysicsMaterial);
            }

            SetTagByName(ball, "Ball");

            Rigidbody rb = ball.AddComponent<Rigidbody>();
            rb.linearDamping = BallLinearDamping;
            rb.angularDamping = BallAngularDamping;

            BallController ballController = ball.AddComponent<BallController>();
            ballController.AssignTrail(CreateBallTrail(ball));
            ballController.ConfigureShadowMaterial(GetOrCreateBallShadowMaterial());

            return ball;
        }

        // Crea la estela del balón, apagada por defecto (BallController la activa según la velocidad).
        private static TrailRenderer CreateBallTrail(GameObject ball)
        {
            TrailRenderer trail = ball.AddComponent<TrailRenderer>();

            trail.time = BallTrailTime;
            trail.startWidth = BallTrailStartWidth;
            trail.endWidth = 0f;
            trail.emitting = false;

            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0.85f, 0f), new GradientAlphaKey(0f, 1f) });
            trail.colorGradient = gradient;

            trail.sharedMaterial = GetOrCreateTrailMaterial();

            return trail;
        }

        // Crea o reutiliza el material de la estela, con un shader que respeta el alpha del gradiente.
        private static Material GetOrCreateTrailMaterial()
        {
            return GetOrCreateAsset("BallTrailMaterial.mat", () =>
            {
                Shader shader = Shader.Find("Sprites/Default");

                if (shader == null)
                {
                    shader = Shader.Find("Universal Render Pipeline/Unlit");
                }

                return new Material(shader != null ? shader : GetDefaultShader())
                {
                    name = "BallTrailMaterial",
                    color = Color.white
                };
            });
        }

        // Crea el gestor de entrada y le asigna las capas de jugador, suelo y portería.
        private static void CreateInputManager()
        {
            GameObject inputManagerObject = new GameObject("InputManager");
            Undo.RegisterCreatedObjectUndo(inputManagerObject, "Create InputManager");

            inputManagerObject.transform.position = ManagerParkingSpot + Vector3.right * 2f;

            TacticalInputManager inputManager = inputManagerObject.AddComponent<TacticalInputManager>();
            inputManager.ConfigureLayers(
                LayerMask.GetMask("Player"),
                LayerMask.GetMask("Ground"),
                LayerMask.GetMask("Goal"));
        }

        // ---------------------------------------------------------------------
        // UI del duelo
        // ---------------------------------------------------------------------

        // Altura del banner del duelo, anclado abajo en vez de centrado.
        private const float UiClashBannerHeight = 0.35f;

        private static readonly Vector2 UiButtonSize = new Vector2(330f, 124f);
        private const int UiButtonFontSize = 26;

        // Separación horizontal de cada botón de acción respecto al centro del banner.
        private const float UiButtonSpacing = 200f;

        // Las tres zonas del banner del duelo: Azul a la izquierda, la elección en medio, Rojo a la derecha.
        private const float UiClashSideZoneWidth = 0.28f;
        private const float UiClashZonePadding = 44f;

        private static readonly Color UiClashBlueTint = new Color(0.55f, 0.78f, 1f, 1f);
        private static readonly Color UiClashRedTint = new Color(1f, 0.58f, 0.52f, 1f);

        private static readonly Vector2 UiScoreSize = new Vector2(400f, 90f);
        private const float UiScoreTopOffset = -24f;

        // El cronómetro va centrado bajo el marcador para que siempre quede sobre el terreno de juego.
        private static readonly Vector2 UiTimerSize = new Vector2(360f, 60f);
        private static readonly Vector2 UiTimerOffset = new Vector2(0f, -112f);

        // Insignia con la ronda del torneo, debajo del reloj.
        private static readonly Vector2 UiTournamentBadgeSize = new Vector2(360f, 40f);
        private static readonly Vector2 UiTournamentBadgeOffset = new Vector2(0f, -160f);
        private static readonly Color UiTournamentBadgeColor = new Color(1f, 0.85f, 0.35f, 1f);

        // Pantalla de fin de partido: resultado arriba, estadísticas comparativas y botones abajo.
        private static readonly Vector2 UiResultSize = new Vector2(1200f, 200f);
        private static readonly Vector2 UiResultOffset = new Vector2(0f, 320f);
        private static readonly Vector2 UiStatsSize = new Vector2(1000f, 340f);
        private static readonly Vector2 UiStatsOffset = new Vector2(0f, 40f);
        private static readonly Vector2 UiRestartButtonSize = new Vector2(460f, 120f);
        private const float UiMatchOverButtonSpacing = 260f;
        private const float UiMatchOverButtonY = -300f;

        // Banner del anunciador, centrado sobre el campo por encima de los jugadores.
        private static readonly Vector2 UiAnnouncerSize = new Vector2(1400f, 160f);
        private static readonly Vector2 UiAnnouncerOffset = new Vector2(0f, 180f);

        private static readonly Vector2 UiTitleSize = new Vector2(1600f, 200f);
        private static readonly Vector2 UiPlayButtonSize = new Vector2(420f, 120f);
        private static readonly Color UiTitleBackground = new Color(0.05f, 0.09f, 0.20f, 1f);

        // Barras de momentum a los lados del marcador: Azul a la izquierda, Roja a la derecha.
        private static readonly Vector2 UiTensionRootSize = new Vector2(1800f, 110f);
        private static readonly Vector2 UiTensionRootOffset = new Vector2(0f, UiScoreTopOffset);
        private static readonly Vector2 UiTensionBarSize = new Vector2(420f, 34f);
        private static readonly Vector2 UiTensionLabelSize = new Vector2(420f, 30f);

        private const float UiTensionBarSpacing = 440f;
        private const float UiTensionLabelY = 32f;
        private const float UiTensionBarInset = 4f;
        private static readonly Color UiTensionTroughColor = new Color(0f, 0f, 0f, 0.65f);

        // Icono pequeño y visible que abre el menú de desarrollador.
        private static readonly Vector2 UiDebugTriggerSize = new Vector2(56f, 56f);
        private static readonly Color UiDebugTriggerBackground = new Color(0.02f, 0.02f, 0.04f, 0.85f);
        private static readonly Color UiDebugGearColor = new Color32(210, 210, 218, 255);
        private static readonly Color UiDebugBackground = new Color(0.02f, 0.02f, 0.04f, 0.88f);
        private static readonly Vector2 UiDebugHeadingSize = new Vector2(1400f, 100f);
        private static readonly Vector2 UiDebugHeadingOffset = new Vector2(0f, 330f);
        private static readonly Vector2 UiDebugButtonSize = new Vector2(700f, 96f);
        private const float UiDebugFirstButtonY = 200f;
        private const float UiDebugButtonStep = 116f;

        // Panel flotante de opciones de audio.
        private static readonly Vector2 UiAudioPanelSize = new Vector2(1100f, 1040f);
        private static readonly Color UiAudioBackground = new Color(0.05f, 0.09f, 0.20f, 0.98f);
        private static readonly Vector2 UiAudioHeadingSize = new Vector2(1000f, 90f);
        private static readonly Vector2 UiAudioHeadingOffset = new Vector2(0f, 330f);
        private static readonly Vector2 UiAudioLabelSize = new Vector2(900f, 56f);
        private static readonly Vector2 UiAudioSliderSize = new Vector2(760f, 44f);
        private const float UiAudioMusicY = 0f;
        private const float UiAudioWhistleY = -130f;
        private const float UiAudioSfxY = -260f;
        private const float UiAudioLabelGap = 62f;
        private const float UiAudioHandleWidth = 44f;
        private static readonly Vector2 UiAudioReadoutOffset = new Vector2(0f, -335f);
        private static readonly Vector2 UiAudioCloseSize = new Vector2(420f, 100f);
        private static readonly Vector2 UiAudioCloseOffset = new Vector2(0f, -430f);

        // Fila de selección de idioma: un botón por idioma.
        private const float UiOptionsLanguageLabelY = 240f;
        private const float UiOptionsLanguageY = 150f;
        private static readonly Vector2 UiOptionsLanguageSize = new Vector2(260f, 92f);
        private const float UiOptionsLanguageSpacing = 300f;
        private static readonly Color UiAudioSliderTrough = new Color(0.12f, 0.16f, 0.28f, 1f);
        private static readonly Color UiAudioSliderFill = new Color(0.20f, 0.65f, 0.95f, 1f);

        private static readonly Vector2 UiTitleOptionsSize = new Vector2(420f, 96f);

        // Rango del zoom con gesto de pellizco de la cámara táctica.
        private const float CameraMinZoom = 0.65f;
        private const float CameraMaxZoom = 1.6f;

        private const float CameraZoomSensitivity = 0.0008f;

        // Cuánto se adelanta la cámara hacia la portería que ataca el jugador con el balón.
        private const float CameraLookAhead = 1.8f;
        private const float CameraMaxLookAhead = 2.2f;
        private const float CameraLookAheadSmoothing = 0.9f;

        // Botón de volver en las pantallas de configuración, anclado a la esquina superior izquierda.
        private static readonly Vector2 UiBackButtonSize = new Vector2(230f, 84f);
        private static readonly Vector2 UiBackButtonOffset = new Vector2(48f, -48f);
        private static readonly Color UiBackButtonColor = new Color(0.32f, 0.34f, 0.40f, 1f);

        // Editor de jugador: mismo fondo que el panel de plantilla.
        private static Color UiEditBackground => UiMenuBackground;

        private static readonly Vector2 UiEditContentSize = new Vector2(1600f, 1000f);
        private const float UiEditHeadingY = 455f;
        private const float UiEditExitY = -447f;

        private static readonly Vector2 UiEditLabelSize = new Vector2(900f, 44f);
        private static readonly Vector2 UiEditChoiceSize = new Vector2(230f, 70f);
        private const float UiEditChoiceSpacing = 250f;
        private static readonly Vector2 UiEditNudgeSize = new Vector2(78f, 62f);

        // Una fila de estadística: NOMBRE · − · VALOR · +.
        private static readonly Vector2 UiEditNameSize = new Vector2(290f, 52f);
        private const float UiEditNameX = -175f;
        private const float UiEditMinusX = 40f;
        private const float UiEditValueX = 160f;
        private const float UiEditPlusX = 280f;
        private static readonly Vector2 UiEditValueSize = new Vector2(140f, 52f);

        private const float UiEditFirstRowY = 80f;
        private const float UiEditRowStep = 72f;
        private static readonly Vector2 UiEditNoticeSize = new Vector2(1200f, 44f);
        private static readonly Vector2 UiEditNoticeOffset = new Vector2(0f, -405f);
        private static readonly Color UiEditNoticeColor = new Color(1f, 0.55f, 0.2f, 1f);
        private static readonly Vector2 UiEditExitSize = new Vector2(320f, 96f);

        private const float UiTitleGap = 30f;
        private static readonly Vector2 UiTitleHeadingOffset = new Vector2(0f, 190f);
        private static readonly Vector2 UiTitleHeadingSize = new Vector2(1600f, 160f);
        private static readonly Vector2 UiTournamentOutcomeSize = new Vector2(1400f, 70f);
        private static readonly Vector2 UiTournamentOutcomeOffset = new Vector2(0f, 55f);
        private const float UiTitlePlayY = -70f;
        private const float UiTitleTournamentY = -220f;
        private const float UiTitleOptionsY = -358f;

        private static readonly Vector2 UiDebugFeedbackSize = new Vector2(1400f, 60f);
        private static readonly Vector2 UiDebugFeedbackOffset = new Vector2(0f, -470f);

        // Menú de penalti: solo dos botones.
        private static readonly Color UiPenaltyBackground = new Color(0.10f, 0.04f, 0.06f, 0.96f);
        private static readonly Vector2 UiPenaltyHeadingSize = new Vector2(1600f, 200f);
        private static readonly Vector2 UiPenaltyHeadingOffset = new Vector2(0f, 300f);
        private static readonly Vector2 UiPenaltyButtonSize = new Vector2(620f, 280f);
        private const float UiPenaltyButtonSpacing = 360f;
        private const float UiPenaltyButtonY = 0f;
        private static readonly Vector2 UiPenaltyResultSize = new Vector2(1400f, 120f);
        private static readonly Vector2 UiPenaltyResultOffset = new Vector2(0f, -260f);

        // Pantalla de alineación: formaciones en fila, capitán y botón de saque inicial.
        private static readonly Vector2 UiFormationHeadingSize = new Vector2(1400f, 110f);
        private static readonly Vector2 UiFormationHeadingOffset = new Vector2(0f, 380f);
        private static readonly Vector2 UiFormationButtonSize = new Vector2(360f, 150f);
        private const float UiFormationButtonSpacing = 400f;
        private const float UiFormationButtonY = 230f;
        private static readonly Vector2 UiCaptainHeadingSize = new Vector2(1400f, 70f);
        private static readonly Vector2 UiCaptainHeadingOffset = new Vector2(0f, 95f);
        private static readonly Vector2 UiCaptainAreaSize = new Vector2(1500f, 110f);
        private static readonly Vector2 UiCaptainAreaOffset = new Vector2(0f, -30f);
        private static readonly Vector2 UiKickoffButtonSize = new Vector2(640f, 140f);
        private static readonly Vector2 UiKickoffButtonOffset = new Vector2(0f, -220f);
        private static readonly Color UiKickoffButtonColor = new Color(0.16f, 0.68f, 0.28f, 1f);

        // Pantalla de configuración previa al partido: dificultad, rival y duración.
        private static readonly Vector2 UiConfigHeadingSize = new Vector2(1600f, 110f);
        private static readonly Vector2 UiConfigHeadingOffset = new Vector2(0f, 480f);
        private static readonly Vector2 UiConfigLabelSize = new Vector2(900f, 60f);
        private static readonly Vector2 UiConfigChoiceSize = new Vector2(320f, 110f);
        private static readonly Vector2 UiConfigRivalChoiceSize = new Vector2(300f, 110f);
        private const float UiConfigDifficultyLabelY = 325f;
        private const float UiConfigDifficultyY = 230f;
        private const float UiConfigRivalLabelY = 120f;
        private const float UiConfigRivalY = 30f;
        private const float UiConfigDurationLabelY = -80f;
        private const float UiConfigDurationY = -170f;
        private const float UiConfigChoiceSpacing = 420f;
        private const float UiConfigRivalSpacing = 360f;
        // Fila de selección de equipación, con muestras de color.
        private const float UiConfigKitLabelY = -270f;
        private const float UiConfigKitY = -355f;
        private const float UiConfigKitSpacing = 340f;
        private static readonly Vector2 UiConfigKitSize = new Vector2(300f, 100f);

        // Resumen de todo lo elegido, como subtítulo bajo el encabezado.
        private static readonly Vector2 UiConfigSummarySize = new Vector2(1700f, 70f);
        private static readonly Vector2 UiConfigSummaryOffset = new Vector2(0f, 395f);
        private static readonly Vector2 UiConfigContinueSize = new Vector2(520f, 120f);
        private static readonly Vector2 UiConfigContinueOffset = new Vector2(0f, -470f);

        // Descanso: cambiar el equipo o volver al partido.
        private static readonly Vector2 UiHalftimeHeadingSize = new Vector2(1400f, 130f);
        private static readonly Vector2 UiHalftimeHeadingOffset = new Vector2(0f, 250f);
        private static readonly Vector2 UiHalftimeSummarySize = new Vector2(1400f, 280f);
        private static readonly Vector2 UiHalftimeSummaryOffset = new Vector2(0f, 30f);
        private static readonly Vector2 UiHalftimeButtonSize = new Vector2(500f, 130f);
        private static readonly Vector2 UiHalftimeSubsOffset = new Vector2(-280f, -220f);
        private static readonly Vector2 UiHalftimeResumeOffset = new Vector2(280f, -220f);

        // Pantalla de sustituciones: estadísticas a la izquierda, mini-campo con la formación a la derecha, banquillo abajo.
        private static readonly Vector2 UiSubsHeaderSize = new Vector2(1600f, 90f);
        private static readonly Vector2 UiSubsHeaderOffset = new Vector2(0f, 470f);
        private static readonly Vector2 UiSubsStatsPanelSize = new Vector2(620f, 780f);
        private static readonly Vector2 UiSubsStatsPanelOffset = new Vector2(-580f, -40f);
        private static readonly Vector2 UiSubsPitchSize = new Vector2(900f, 560f);
        private static readonly Vector2 UiSubsPitchOffset = new Vector2(280f, 75f);
        private static readonly Vector2 UiSubsBenchSize = new Vector2(900f, 160f);
        private static readonly Vector2 UiSubsBenchOffset = new Vector2(280f, -350f);
        private static readonly Vector2 UiSubsCloseSize = new Vector2(420f, 84f);
        private static readonly Vector2 UiSubsCloseOffset = new Vector2(280f, -490f);

        private static readonly Vector2 UiSubsEditOffset = new Vector2(-580f, -430f);

        private const float UiSubsCaptionClearance = 26f;
        private const float UiSubsStatsPadding = 34f;

        // Fondo compartido por todos los menús a pantalla completa.
        private static readonly Color UiMenuBackground = new Color(0.06f, 0.09f, 0.17f, 1f);

        private static Color UiSubsBackground => UiMenuBackground;
        private static readonly Color UiSubsStatsPanelColor = new Color(0.10f, 0.14f, 0.24f, 1f);
        private static readonly Color UiSubsPitchColor = new Color(0.10f, 0.30f, 0.16f, 1f);
        private static readonly Color UiSubsBenchColor = new Color(0.18f, 0.20f, 0.24f, 1f);

        // Contenedor al que se enganchan todos los paneles y elementos del HUD, ajustado al área segura de la pantalla.
        private static Transform uiHudArea;

        // El canvas raíz. Los menús a pantalla completa cuelgan directamente de él.
        private static Transform uiCanvasRoot;

        // Crea el área segura: un rect a pantalla completa ajustado a Screen.safeArea.
        private static Transform CreateSafeArea(Transform canvas)
        {
            GameObject area = new GameObject("HUD Safe Area", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(area, "Create Safe Area");
            area.transform.SetParent(canvas, false);

            RectTransform rect = (RectTransform)area.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            area.AddComponent<SafeAreaFitter>();

            return rect;
        }

        // Crea el canvas principal de la UI y el panel de duelo (elección de acción, estadísticas de cada equipo).
        private static ClashUIController CreateClashUI()
        {
            GameObject canvasObject = new GameObject("UI Canvas");
            Undo.RegisterCreatedObjectUndo(canvasObject, "Create UI Canvas");

            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;

            canvasObject.AddComponent<GraphicRaycaster>();

            uiCanvasRoot = canvasObject.transform;
            uiHudArea = CreateSafeArea(uiCanvasRoot);

            GameObject panel = CreateClashPanel(uiHudArea);
            Text clashText = CreateClashText(panel.transform);

            Text blueStats = CreateClashStatsText(panel.transform, "Blue Stats Text",
                new Vector2(0f, 0f), new Vector2(UiClashSideZoneWidth, 1f),
                TextAnchor.MiddleLeft, UiClashBlueTint,
                new Vector2(UiClashZonePadding, 0f), new Vector2(0f, 0f));

            Text redStats = CreateClashStatsText(panel.transform, "Red Stats Text",
                new Vector2(1f - UiClashSideZoneWidth, 0f), new Vector2(1f, 1f),
                TextAnchor.MiddleRight, UiClashRedTint,
                new Vector2(0f, 0f), new Vector2(-UiClashZonePadding, 0f));

            Button action1 = CreateActionButton(panel.transform, "Action1 Button", "Accion 1", -UiButtonSpacing);
            Button action2 = CreateActionButton(panel.transform, "Action2 Button", "Accion 2", UiButtonSpacing);

            ClashUIController controller = canvasObject.AddComponent<ClashUIController>();
            controller.uiPanel = panel;
            controller.clashText = clashText;
            controller.blueStatsText = blueStats;
            controller.redStatsText = redStats;
            controller.action1Button = action1;
            controller.action1Text = action1.GetComponentInChildren<Text>();
            controller.action2Button = action2;
            controller.action2Text = action2.GetComponentInChildren<Text>();

            panel.SetActive(false);

            return controller;
        }

        // Crea el panel del duelo: una franja en el tercio inferior de la pantalla.
        private static GameObject CreateClashPanel(Transform parent)
        {
            GameObject panel = new GameObject("Clash Panel", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(panel, "Create Clash Panel");
            panel.transform.SetParent(parent, false);

            RectTransform rect = (RectTransform)panel.transform;
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, UiClashBannerHeight);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            Image background = panel.AddComponent<Image>();
            background.color = new Color(0f, 0f, 0f, 0.82f);

            return panel;
        }

        // Crea el marcador y el cronómetro, anclados arriba en el centro.
        private static void CreateScoreUI(Transform canvas, ScoreManager scoreManager)
        {
            scoreManager.scoreText = CreateHudText(canvas, "Score Text", "0 - 0",
                new Vector2(0.5f, 1f), new Vector2(0f, UiScoreTopOffset),
                UiScoreSize, 56, TextAnchor.MiddleCenter);

            scoreManager.timerText = CreateHudText(canvas, "Timer Text", "Tiempo: 90",
                new Vector2(0.5f, 1f), UiTimerOffset,
                UiTimerSize, 40, TextAnchor.MiddleCenter);

            scoreManager.tournamentText = CreateHudText(canvas, "Tournament Badge", string.Empty,
                new Vector2(0.5f, 1f), UiTournamentBadgeOffset,
                UiTournamentBadgeSize, 26, TextAnchor.MiddleCenter);

            scoreManager.tournamentText.color = UiTournamentBadgeColor;
            scoreManager.tournamentText.gameObject.SetActive(false);
        }

        // Crea un texto de HUD anclado a un punto fijo de la pantalla.
        private static Text CreateHudText(Transform canvas, string objectName, string content,
            Vector2 anchor, Vector2 offset, Vector2 size, int fontSize, TextAnchor alignment)
        {
            GameObject textObject = new GameObject(objectName, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(textObject, $"Create {objectName}");
            textObject.transform.SetParent(canvas, false);

            RectTransform rect = (RectTransform)textObject.transform;
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.anchoredPosition = offset;
            rect.sizeDelta = size;

            Text text = textObject.AddComponent<Text>();
            text.font = GetUIFont();
            text.fontSize = fontSize;
            text.fontStyle = FontStyle.Bold;
            text.alignment = alignment;
            text.color = Color.white;
            text.text = content;

            Outline outline = textObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
            outline.effectDistance = new Vector2(2f, -2f);

            return text;
        }

        // Crea la pantalla de fin de partido, con el resultado, las estadísticas y los botones de salida.
        private static void CreateMatchOverUI(Transform canvas)
        {
            GameObject panel = new GameObject("Match Over Panel", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(panel, "Create Match Over Panel");
            panel.transform.SetParent(canvas, false);

            RectTransform rect = (RectTransform)panel.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            Image background = CreateFullScreenBackdrop(panel.transform, new Color(0f, 0f, 0f, 0.92f));

            Transform safeContent = CreateSafeAreaContent(panel.transform);

            Text resultText = CreateHudText(safeContent, "Result Text", "FIN",
                new Vector2(0.5f, 0.5f), UiResultOffset,
                UiResultSize, 110, TextAnchor.MiddleCenter);

            LocalizeDynamic(resultText);

            // Tabla comparativa de estadísticas, con fuente monoespaciada para que las columnas alineen.
            Text statsText = CreateHudText(safeContent, "Stats Text", string.Empty,
                new Vector2(0.5f, 0.5f), UiStatsOffset,
                UiStatsSize, 40, TextAnchor.UpperCenter);

            statsText.font = GetMonospaceFont();
            statsText.lineSpacing = 1.25f;

            Button restartButton = LocalizeButton(CreateUiButton(safeContent, "Restart Button",
                "JUGAR DE NUEVO",
                new Vector2(-UiMatchOverButtonSpacing, UiMatchOverButtonY), UiRestartButtonSize),
                "matchover.playAgain");

            Button menuButton = LocalizeButton(CreateUiButton(safeContent, "Menu Button",
                "MENÚ PRINCIPAL",
                new Vector2(UiMatchOverButtonSpacing, UiMatchOverButtonY), UiRestartButtonSize),
                "matchover.mainMenu");

            MatchOverUIController controller = canvas.gameObject.AddComponent<MatchOverUIController>();
            controller.uiPanel = panel;
            controller.resultText = resultText;
            controller.statsText = statsText;
            controller.restartButton = restartButton;
            controller.menuButton = menuButton;

            panel.SetActive(false);
        }

        // Crea un botón de menú centrado con una etiqueta.
        private static Button CreateUiButton(Transform parent, string objectName, string caption,
            Vector2 anchoredPosition, Vector2 size, bool playClickSound = true)
        {
            GameObject buttonObject = new GameObject(objectName, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(buttonObject, $"Create {objectName}");
            buttonObject.transform.SetParent(parent, false);

            RectTransform rect = (RectTransform)buttonObject.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;

            Image background = buttonObject.AddComponent<Image>();
            background.color = new Color(0.88f, 0.88f, 0.88f, 1f);

            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = background;

            if (playClickSound)
            {
                buttonObject.AddComponent<ButtonClickSound>();
            }

            GameObject labelObject = new GameObject("Label", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(labelObject, $"Create {objectName} Label");
            labelObject.transform.SetParent(buttonObject.transform, false);

            RectTransform labelRect = (RectTransform)labelObject.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            Text label = labelObject.AddComponent<Text>();
            label.font = GetUIFont();
            label.fontSize = 40;
            label.fontStyle = FontStyle.Bold;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.black;
            label.text = caption;

            return button;
        }

        // Crea el texto del anunciador, centrado sobre el campo.
        private static void CreateAnnouncerUI(Transform canvas)
        {
            Text announcerText = CreateHudText(canvas, "Announcer Text", string.Empty,
                new Vector2(0.5f, 0.5f), UiAnnouncerOffset,
                UiAnnouncerSize, 72, TextAnchor.MiddleCenter);

            Color transparent = announcerText.color;
            transparent.a = 0f;
            announcerText.color = transparent;

            AnnouncerUIController controller = announcerText.gameObject.AddComponent<AnnouncerUIController>();
            controller.announcerText = announcerText;
        }

        // Crea la pantalla de elección de formación, mostrada entre el título y el saque inicial.
        private static FormationUIController CreateFormationUI(Transform canvas)
        {
            GameObject panel = new GameObject("Formation Panel", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(panel, "Create Formation Panel");
            panel.transform.SetParent(canvas, false);

            RectTransform rect = (RectTransform)panel.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            Image background = CreateFullScreenBackdrop(panel.transform, UiTitleBackground);
            Transform safeContent = CreateSafeAreaContent(panel.transform);

            Localize(CreateHudText(safeContent, "Formation Heading", "ELIGE TU FORMACIÓN",
                new Vector2(0.5f, 0.5f), UiFormationHeadingOffset,
                UiFormationHeadingSize, 64, TextAnchor.MiddleCenter), "formation.heading");

            Button balanced = CreateUiButton(safeContent, "Formation 222 Button",
                $"{Formations.GetLabel(FormationType.Balanced_2_2_2)}\nEQUILIBRADA",
                new Vector2(-UiFormationButtonSpacing, UiFormationButtonY), UiFormationButtonSize);

            Button defensive = CreateUiButton(safeContent, "Formation 321 Button",
                $"{Formations.GetLabel(FormationType.Defensive_3_2_1)}\nDEFENSIVA",
                new Vector2(0f, UiFormationButtonY), UiFormationButtonSize);

            Button offensive = CreateUiButton(safeContent, "Formation 132 Button",
                $"{Formations.GetLabel(FormationType.Offensive_1_3_2)}\nOFENSIVA",
                new Vector2(UiFormationButtonSpacing, UiFormationButtonY), UiFormationButtonSize);

            Text captainHeading = CreateHudText(safeContent, "Captain Heading",
                "ELIGE TU CAPITÁN", new Vector2(0.5f, 0.5f), UiCaptainHeadingOffset,
                UiCaptainHeadingSize, 40, TextAnchor.MiddleCenter);

            RectTransform captainArea = CreateLayoutArea(safeContent, "Captain Area",
                UiCaptainAreaOffset, UiCaptainAreaSize);

            Button kickoff = LocalizeButton(CreateUiButton(safeContent, "Kickoff Button",
                "¡AL CÉSPED!", UiKickoffButtonOffset, UiKickoffButtonSize), "formation.kickoff");

            kickoff.targetGraphic.color = UiKickoffButtonColor;

            Text kickoffLabel = kickoff.GetComponentInChildren<Text>();
            kickoffLabel.color = Color.white;
            kickoffLabel.fontSize = 48;

            FormationUIController controller = canvas.gameObject.AddComponent<FormationUIController>();
            controller.uiPanel = panel;
            controller.btn222 = balanced;
            controller.btn321 = defensive;
            controller.btn132 = offensive;
            controller.btnStartMatch = kickoff;
            controller.backButton = CreateBackButton(safeContent, "Formation Back Button");

            controller.squadButton = LocalizeButton(CreateCornerButton(safeContent,
                "Formation Squad Button", "PLANTILLA ›", anchorRight: true), "formation.squad");
            controller.captainArea = captainArea;
            controller.captainHeading = captainHeading;

            panel.SetActive(false);

            return controller;
        }

        // Crea el fondo opaco a pantalla completa de un menú.
        private static Image CreateFullScreenBackdrop(Transform panel, Color color)
        {
            GameObject backdrop = new GameObject("Background Image", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(backdrop, "Create Background Image");
            backdrop.transform.SetParent(panel, false);

            RectTransform rect = (RectTransform)backdrop.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            Image image = backdrop.AddComponent<Image>();
            image.color = color;

            backdrop.transform.SetAsFirstSibling();

            return image;
        }

        // Crea el contenedor donde van los controles de una pantalla, ajustado al área segura.
        private static Transform CreateSafeAreaContent(Transform panel)
        {
            GameObject content = new GameObject("Safe Area Content", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(content, "Create Safe Area Content");
            content.transform.SetParent(panel, false);

            RectTransform rect = (RectTransform)content.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            content.AddComponent<SafeAreaFitter>();

            return rect;
        }

        // Crea una caja invisible donde un controlador coloca botones en tiempo de ejecución.
        private static RectTransform CreateLayoutArea(Transform parent, string objectName,
            Vector2 offset, Vector2 size)
        {
            GameObject area = new GameObject(objectName, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(area, $"Create {objectName}");
            area.transform.SetParent(parent, false);

            RectTransform rect = (RectTransform)area.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = offset;
            rect.sizeDelta = size;

            return rect;
        }

        // Crea la pantalla de configuración previa al partido, con las opciones de dificultad, rival y duración.
        private static MatchConfigUIController CreateMatchConfigUI(Transform canvas,
            FormationUIController formationUI)
        {
            GameObject panel = new GameObject("Match Config Panel", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(panel, "Create Match Config Panel");
            panel.transform.SetParent(canvas, false);

            RectTransform rect = (RectTransform)panel.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            Image background = CreateFullScreenBackdrop(panel.transform, UiTitleBackground);
            Transform safeContent = CreateSafeAreaContent(panel.transform);

            Localize(CreateHudText(safeContent, "Config Heading", "CONFIGURACIÓN DEL PARTIDO",
                new Vector2(0.5f, 0.5f), UiConfigHeadingOffset,
                UiConfigHeadingSize, 62, TextAnchor.MiddleCenter), "config.heading");

            Localize(CreateConfigLabel(safeContent, "Difficulty Label", "DIFICULTAD DEL RIVAL",
                UiConfigDifficultyLabelY), "config.difficulty");

            Button easy = LocalizeButton(CreateConfigChoice(safeContent, "Difficulty Easy",
                "FÁCIL", -UiConfigChoiceSpacing, UiConfigDifficultyY, UiConfigChoiceSize),
                "config.easy");
            Button normal = LocalizeButton(CreateConfigChoice(safeContent, "Difficulty Normal",
                "NORMAL", 0f, UiConfigDifficultyY, UiConfigChoiceSize), "config.normal");
            Button hard = LocalizeButton(CreateConfigChoice(safeContent, "Difficulty Hard",
                "DIFÍCIL", UiConfigChoiceSpacing, UiConfigDifficultyY, UiConfigChoiceSize),
                "config.hard");

            Localize(CreateConfigLabel(safeContent, "Rival Label", "FORMACIÓN RIVAL",
                UiConfigRivalLabelY), "config.rivalFormation");

            Button rivalRandom = LocalizeButton(CreateConfigChoice(safeContent, "Rival Random",
                "ALEATORIA", -1.5f * UiConfigRivalSpacing, UiConfigRivalY, UiConfigRivalChoiceSize),
                "config.random");
            Button rival222 = CreateConfigChoice(safeContent, "Rival 222",
                Formations.GetLabel(FormationType.Balanced_2_2_2),
                -0.5f * UiConfigRivalSpacing, UiConfigRivalY, UiConfigRivalChoiceSize);
            Button rival321 = CreateConfigChoice(safeContent, "Rival 321",
                Formations.GetLabel(FormationType.Defensive_3_2_1),
                0.5f * UiConfigRivalSpacing, UiConfigRivalY, UiConfigRivalChoiceSize);
            Button rival132 = CreateConfigChoice(safeContent, "Rival 132",
                Formations.GetLabel(FormationType.Offensive_1_3_2),
                1.5f * UiConfigRivalSpacing, UiConfigRivalY, UiConfigRivalChoiceSize);

            Localize(CreateConfigLabel(safeContent, "Duration Label", "DURACIÓN DE CADA PARTE",
                UiConfigDurationLabelY), "config.duration");

            Button short45 = CreateConfigChoice(safeContent, "Duration 45", "45 s",
                -UiConfigChoiceSpacing, UiConfigDurationY, UiConfigChoiceSize);
            Button medium60 = CreateConfigChoice(safeContent, "Duration 60", "60 s",
                0f, UiConfigDurationY, UiConfigChoiceSize);
            Button long90 = CreateConfigChoice(safeContent, "Duration 90", "90 s",
                UiConfigChoiceSpacing, UiConfigDurationY, UiConfigChoiceSize);

            Localize(CreateConfigLabel(safeContent, "Kit Label", "EQUIPACIÓN",
                UiConfigKitLabelY), "config.kit");

            Button kitBlue = CreateKitChoice(safeContent, "Kit Blue", TeamKit.Azul,
                -1.5f * UiConfigKitSpacing);
            Button kitGreen = CreateKitChoice(safeContent, "Kit Green", TeamKit.Verde,
                -0.5f * UiConfigKitSpacing);
            Button kitBlack = CreateKitChoice(safeContent, "Kit Black", TeamKit.Negro,
                0.5f * UiConfigKitSpacing);
            Button kitWhite = CreateKitChoice(safeContent, "Kit White", TeamKit.Blanco,
                1.5f * UiConfigKitSpacing);

            Text summary = CreateHudText(safeContent, "Config Summary", string.Empty,
                new Vector2(0.5f, 0.5f), UiConfigSummaryOffset,
                UiConfigSummarySize, 34, TextAnchor.MiddleCenter);

            Button continueButton = LocalizeButton(CreateUiButton(safeContent,
                "Config Continue Button", "CONTINUAR",
                UiConfigContinueOffset, UiConfigContinueSize), "config.continue");

            continueButton.targetGraphic.color = UiKickoffButtonColor;

            Text continueLabel = continueButton.GetComponentInChildren<Text>();
            continueLabel.color = Color.white;

            MatchConfigUIController controller = canvas.gameObject.AddComponent<MatchConfigUIController>();
            controller.uiPanel = panel;
            controller.easyButton = easy;
            controller.normalButton = normal;
            controller.hardButton = hard;
            controller.rivalRandomButton = rivalRandom;
            controller.rival222Button = rival222;
            controller.rival321Button = rival321;
            controller.rival132Button = rival132;
            controller.short45Button = short45;
            controller.medium60Button = medium60;
            controller.long90Button = long90;
            controller.kitBlueButton = kitBlue;
            controller.kitGreenButton = kitGreen;
            controller.kitBlackButton = kitBlack;
            controller.kitWhiteButton = kitWhite;
            controller.continueButton = continueButton;
            controller.backButton = CreateBackButton(safeContent, "Config Back Button");
            controller.summaryText = summary;
            controller.formationMenu = formationUI;

            panel.SetActive(false);

            return controller;
        }

        // Crea el botón de volver de una pantalla de configuración, en la esquina superior izquierda.
        private static Button CreateBackButton(Transform parent, string objectName)
        {
            return LocalizeButton(
                CreateCornerButton(parent, objectName, "‹ ATRÁS", anchorRight: false),
                "common.back");
        }

        // Crea un botón anclado a una esquina superior de la pantalla.
        private static Button CreateCornerButton(Transform parent, string objectName,
            string caption, bool anchorRight)
        {
            Button button = CreateUiButton(parent, objectName, caption, Vector2.zero, UiBackButtonSize);

            float x = anchorRight ? 1f : 0f;

            RectTransform rect = (RectTransform)button.transform;
            rect.anchorMin = new Vector2(x, 1f);
            rect.anchorMax = new Vector2(x, 1f);
            rect.pivot = new Vector2(x, 1f);
            rect.anchoredPosition = new Vector2(
                anchorRight ? -UiBackButtonOffset.x : UiBackButtonOffset.x,
                UiBackButtonOffset.y);

            button.targetGraphic.color = UiBackButtonColor;

            Text label = button.GetComponentInChildren<Text>();
            label.fontSize = 30;
            label.color = Color.white;

            return button;
        }

        // Crea una etiqueta de la pantalla de configuración.
        private static Text CreateConfigLabel(Transform parent, string objectName, string caption, float y)
        {
            return CreateHudText(parent, objectName, caption,
                new Vector2(0.5f, 0.5f), new Vector2(0f, y),
                UiConfigLabelSize, 32, TextAnchor.MiddleCenter);
        }

        // Crea un botón de opción de la pantalla de configuración.
        private static Button CreateConfigChoice(Transform parent, string objectName, string caption,
            float x, float y, Vector2 size)
        {
            Button button = CreateUiButton(parent, objectName, caption, new Vector2(x, y), size);

            Text label = button.GetComponentInChildren<Text>();
            label.fontSize = 32;

            return button;
        }

        // Clave de localización de la etiqueta de una equipación.
        private static string KitKey(TeamKit kit)
        {
            switch (kit)
            {
                case TeamKit.Verde: return "kit.green";
                case TeamKit.Negro: return "kit.black";
                case TeamKit.Blanco: return "kit.white";
                default: return "kit.blue";
            }
        }

        // Crea el botón de una equipación, coloreado con su color real.
        private static Button CreateKitChoice(Transform parent, string objectName, TeamKit kit, float x)
        {
            Color color = TeamKits.GetColor(kit);

            Button button = LocalizeButton(
                CreateUiButton(parent, objectName, TeamKits.GetLabel(kit).ToUpperInvariant(),
                    new Vector2(x, UiConfigKitY), UiConfigKitSize),
                KitKey(kit));

            button.targetGraphic.color = color;

            Text label = button.GetComponentInChildren<Text>();
            label.fontSize = 32;

            float luma = (0.299f * color.r) + (0.587f * color.g) + (0.114f * color.b);
            label.color = luma > 0.5f ? Color.black : Color.white;

            return button;
        }

        // Crea la pantalla de título, con los botones de partido rápido y torneo.
        private static void CreateTitleScreenUI(Transform canvas, MatchConfigUIController configUI,
            FormationUIController formationUI)
        {
            GameObject panel = new GameObject("Title Panel", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(panel, "Create Title Panel");
            panel.transform.SetParent(canvas, false);

            RectTransform rect = (RectTransform)panel.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            Image background = CreateFullScreenBackdrop(panel.transform, UiTitleBackground);
            Transform safeContent = CreateSafeAreaContent(panel.transform);

            Localize(CreateHudText(safeContent, "Title Text", "TACTICAL SOCCER",
                new Vector2(0.5f, 0.5f), UiTitleHeadingOffset,
                UiTitleHeadingSize, 110, TextAnchor.MiddleCenter), "title.heading");

            Button playButton = LocalizeButton(CreateUiButton(safeContent, "Play Button",
                "PARTIDO RÁPIDO", new Vector2(0f, UiTitlePlayY), UiPlayButtonSize),
                "title.quickMatch");

            // El controlador reasigna la etiqueta a la ronda actual del torneo cada vez que se muestra el título.
            Button tournamentButton = LocalizeButton(CreateUiButton(safeContent,
                "Tournament Button", "TORNEO",
                new Vector2(0f, UiTitleTournamentY), UiPlayButtonSize),
                "tournament.next.quarters");

            Text tournamentLabel = tournamentButton.GetComponentInChildren<Text>();

            Button optionsButton = LocalizeButton(CreateUiButton(safeContent, "Options Button",
                "OPCIONES", new Vector2(0f, UiTitleOptionsY), UiTitleOptionsSize),
                "title.options");

            Text outcomeText = CreateHudText(safeContent, "Tournament Outcome", string.Empty,
                new Vector2(0.5f, 0.5f), UiTournamentOutcomeOffset,
                UiTournamentOutcomeSize, 44, TextAnchor.MiddleCenter);

            TitleScreenUIController controller = canvas.gameObject.AddComponent<TitleScreenUIController>();
            controller.uiPanel = panel;
            controller.playButton = playButton;
            controller.tournamentButton = tournamentButton;
            controller.tournamentLabel = tournamentLabel;
            controller.tournamentOutcomeText = outcomeText;
            controller.optionsButton = optionsButton;

            controller.configMenu = configUI;
            controller.formationMenu = formationUI;

            panel.SetActive(false);
        }

        // Crea el menú de desarrollador y el icono de engranaje que lo abre.
        private static void CreateDebugMenuUI(Transform canvas)
        {
            GameObject trigger = new GameObject("Debug Trigger", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(trigger, "Create Debug Trigger");
            trigger.transform.SetParent(canvas, false);

            RectTransform triggerRect = (RectTransform)trigger.transform;
            triggerRect.anchorMin = new Vector2(0f, 1f);
            triggerRect.anchorMax = new Vector2(0f, 1f);
            triggerRect.pivot = new Vector2(0f, 1f);
            triggerRect.anchoredPosition = new Vector2(12f, -12f);
            triggerRect.sizeDelta = UiDebugTriggerSize;

            Image triggerImage = trigger.AddComponent<Image>();
            triggerImage.color = UiDebugTriggerBackground;

            Button triggerButton = trigger.AddComponent<Button>();
            triggerButton.targetGraphic = triggerImage;
            trigger.AddComponent<ButtonClickSound>();

            GameObject gearObject = new GameObject("Gear Icon", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(gearObject, "Create Debug Trigger Gear Icon");
            gearObject.transform.SetParent(trigger.transform, false);

            RectTransform gearRect = (RectTransform)gearObject.transform;
            gearRect.anchorMin = new Vector2(0.5f, 0.5f);
            gearRect.anchorMax = new Vector2(0.5f, 0.5f);
            gearRect.pivot = new Vector2(0.5f, 0.5f);
            gearRect.anchoredPosition = Vector2.zero;
            gearRect.sizeDelta = UiDebugTriggerSize * 0.7f;

            RawImage gearImage = gearObject.AddComponent<RawImage>();
            gearImage.texture = GetOrCreateAsset("DebugGearIcon.asset", CreateGearIconTexture);
            gearImage.raycastTarget = false;

            GameObject panel = new GameObject("Debug Panel", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(panel, "Create Debug Panel");
            panel.transform.SetParent(canvas, false);

            RectTransform rect = (RectTransform)panel.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            Image background = CreateFullScreenBackdrop(panel.transform, UiDebugBackground);
            Transform safeContent = CreateSafeAreaContent(panel.transform);

            Localize(CreateHudText(safeContent, "Debug Heading", "MENÚ DE DESARROLLO",
                new Vector2(0.5f, 0.5f), UiDebugHeadingOffset,
                UiDebugHeadingSize, 60, TextAnchor.MiddleCenter), "debug.heading");

            Button tension = LocalizeButton(CreateUiButton(safeContent, "Debug Tension Button",
                "MAX TENSIÓN (AZUL)", new Vector2(0f, UiDebugFirstButtonY), UiDebugButtonSize),
                "debug.tension");

            Button stamina = LocalizeButton(CreateUiButton(safeContent, "Debug Stamina Button",
                "CURAR ESTAMINA", new Vector2(0f, UiDebugFirstButtonY - UiDebugButtonStep), UiDebugButtonSize),
                "debug.stamina");

            Button endHalf = LocalizeButton(CreateUiButton(safeContent, "Debug End Half Button",
                "FORZAR FIN DE PARTE", new Vector2(0f, UiDebugFirstButtonY - (UiDebugButtonStep * 2f)), UiDebugButtonSize),
                "debug.endHalf");

            Button audioOptions = LocalizeButton(CreateUiButton(safeContent, "Debug Audio Button",
                "OPCIONES", new Vector2(0f, UiDebugFirstButtonY - (UiDebugButtonStep * 3f)), UiDebugButtonSize),
                "debug.options");

            Button resetSquad = LocalizeButton(CreateUiButton(safeContent, "Debug Reset Squad Button",
                "BORRAR EDICIONES DE PLANTILLA",
                new Vector2(0f, UiDebugFirstButtonY - (UiDebugButtonStep * 4f)), UiDebugButtonSize),
                "debug.resetSquad");

            resetSquad.GetComponentInChildren<Text>().fontSize = 34;

            Button close = LocalizeButton(CreateUiButton(safeContent, "Debug Close Button",
                "CERRAR MENÚ", new Vector2(0f, UiDebugFirstButtonY - (UiDebugButtonStep * 5f)), UiDebugButtonSize),
                "debug.close");

            close.targetGraphic.color = UiKickoffButtonColor;
            close.GetComponentInChildren<Text>().color = Color.white;

            Text feedback = CreateHudText(safeContent, "Debug Feedback", string.Empty,
                new Vector2(0.5f, 0.5f), UiDebugFeedbackOffset,
                UiDebugFeedbackSize, 30, TextAnchor.MiddleCenter);

            DebugMenuUIController controller = canvas.gameObject.AddComponent<DebugMenuUIController>();
            controller.uiPanel = panel;
            controller.openTrigger = triggerButton;
            controller.maxTensionButton = tension;
            controller.healStaminaButton = stamina;
            controller.endHalfButton = endHalf;
            controller.audioOptionsButton = audioOptions;
            controller.resetSquadButton = resetSquad;
            controller.closeButton = close;
            controller.feedbackText = feedback;

            panel.SetActive(false);
        }

        // Crea el panel de opciones de audio, con los deslizadores de volumen y el selector de idioma.
        private static void CreateAudioSettingsUI(Transform canvas)
        {
            GameObject panel = new GameObject("Audio Settings Panel", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(panel, "Create Audio Settings Panel");
            panel.transform.SetParent(canvas, false);

            RectTransform rect = (RectTransform)panel.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = UiAudioPanelSize;

            Image background = panel.AddComponent<Image>();
            background.color = UiAudioBackground;

            Localize(CreateHudText(panel.transform, "Options Heading", "OPCIONES",
                new Vector2(0.5f, 0.5f), UiAudioHeadingOffset,
                UiAudioHeadingSize, 54, TextAnchor.MiddleCenter), "options.heading");

            Localize(CreateHudText(panel.transform, "Language Label", "IDIOMA",
                new Vector2(0.5f, 0.5f), new Vector2(0f, UiOptionsLanguageLabelY),
                UiAudioLabelSize, 32, TextAnchor.MiddleCenter), "options.language");

            Button[] languageButtons = CreateLanguageButtons(panel.transform);

            Localize(CreateHudText(panel.transform, "Music Label", "PÚBLICO Y AMBIENTE",
                new Vector2(0.5f, 0.5f), new Vector2(0f, UiAudioMusicY + UiAudioLabelGap),
                UiAudioLabelSize, 32, TextAnchor.MiddleCenter), "options.music");

            Slider musicSlider = CreateVolumeSlider(panel.transform, "Music Slider", UiAudioMusicY);

            Localize(CreateHudText(panel.transform, "Whistle Label", "SILBATOS",
                new Vector2(0.5f, 0.5f), new Vector2(0f, UiAudioWhistleY + UiAudioLabelGap),
                UiAudioLabelSize, 32, TextAnchor.MiddleCenter), "options.whistle");

            Slider whistleSlider = CreateVolumeSlider(panel.transform, "Whistle Slider", UiAudioWhistleY);

            Localize(CreateHudText(panel.transform, "Sfx Label", "EFECTOS",
                new Vector2(0.5f, 0.5f), new Vector2(0f, UiAudioSfxY + UiAudioLabelGap),
                UiAudioLabelSize, 32, TextAnchor.MiddleCenter), "options.sfx");

            Slider sfxSlider = CreateVolumeSlider(panel.transform, "Sfx Slider", UiAudioSfxY);

            Text readout = CreateHudText(panel.transform, "Audio Readout", string.Empty,
                new Vector2(0.5f, 0.5f), UiAudioReadoutOffset,
                UiAudioLabelSize, 30, TextAnchor.MiddleCenter);

            Button close = CreateUiButton(panel.transform, "Audio Close Button", "VOLVER",
                UiAudioCloseOffset, UiAudioCloseSize);

            close.targetGraphic.color = UiKickoffButtonColor;
            close.GetComponentInChildren<Text>().color = Color.white;

            LocalizeButton(close, "options.close");

            AudioSettingsUI controller = canvas.gameObject.AddComponent<AudioSettingsUI>();
            controller.uiPanel = panel;
            controller.musicSlider = musicSlider;
            controller.whistleSlider = whistleSlider;
            controller.sfxSlider = sfxSlider;
            controller.closeButton = close;
            controller.languageButtons = languageButtons;
            controller.readoutText = readout;

            panel.SetActive(false);
        }

        // Crea un botón por idioma disponible, en una fila centrada. El controlador reescribe cada etiqueta
        // con el nombre real del idioma en tiempo de ejecución.
        private static Button[] CreateLanguageButtons(Transform parent)
        {
            string[] codes = LocalizationManager.AvailableLanguages;
            Button[] buttons = new Button[codes.Length];

            for (int i = 0; i < codes.Length; i++)
            {
                float x = (i - ((codes.Length - 1) * 0.5f)) * UiOptionsLanguageSpacing;

                Button button = CreateUiButton(parent,
                    $"Language {codes[i].ToUpperInvariant()} Button",
                    codes[i].ToUpperInvariant(),
                    new Vector2(x, UiOptionsLanguageY), UiOptionsLanguageSize);

                button.GetComponentInChildren<Text>().fontSize = 30;

                buttons[i] = button;
            }

            return buttons;
        }

        // Vincula un texto a una clave de localización para que siga el idioma seleccionado.
        private static Text Localize(Text text, string key)
        {
            if (text == null)
            {
                return null;
            }

            LocalizedText localized = text.gameObject.AddComponent<LocalizedText>();
            localized.key = key;

            return text;
        }

        // Igual que Localize, pero para la etiqueta dentro de un botón.
        private static Button LocalizeButton(Button button, string key)
        {
            if (button != null)
            {
                Localize(button.GetComponentInChildren<Text>(), key);
            }

            return button;
        }

        // Añade el componente de localización sin clave, para texto que el controlador rellena más tarde.
        private static void LocalizeDynamic(Text text)
        {
            Localize(text, string.Empty);
        }

        // Crea un deslizador de volumen de 0 a 1 con fondo, relleno y tirador.
        private static Slider CreateVolumeSlider(Transform parent, string objectName, float y)
        {
            GameObject sliderObject = new GameObject(objectName, typeof(RectTransform));
            sliderObject.transform.SetParent(parent, false);

            RectTransform rect = (RectTransform)sliderObject.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, y);
            rect.sizeDelta = UiAudioSliderSize;

            GameObject backgroundObject = CreateStretchedChild(sliderObject.transform, "Background");
            Image background = backgroundObject.AddComponent<Image>();
            background.color = UiAudioSliderTrough;

            GameObject fillArea = CreateStretchedChild(sliderObject.transform, "Fill Area");
            GameObject fillObject = CreateStretchedChild(fillArea.transform, "Fill");
            Image fill = fillObject.AddComponent<Image>();
            fill.color = UiAudioSliderFill;

            GameObject handleArea = CreateStretchedChild(sliderObject.transform, "Handle Slide Area");
            GameObject handleObject = CreateStretchedChild(handleArea.transform, "Handle");
            Image handle = handleObject.AddComponent<Image>();
            handle.color = Color.white;

            RectTransform handleRect = (RectTransform)handleObject.transform;
            handleRect.sizeDelta = new Vector2(UiAudioHandleWidth, 0f);

            Slider slider = sliderObject.AddComponent<Slider>();
            slider.fillRect = (RectTransform)fillObject.transform;
            slider.handleRect = handleRect;
            slider.targetGraphic = handle;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.wholeNumbers = false;
            slider.value = 1f;

            return slider;
        }

        // Crea el editor de jugador: posición, elemento, atributos y resistencia.
        private static void CreatePlayerEditUI(Transform canvas)
        {
            GameObject panel = new GameObject("Player Edit Panel", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(panel, "Create Player Edit Panel");
            panel.transform.SetParent(canvas, false);

            RectTransform rect = (RectTransform)panel.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            Image background = CreateFullScreenBackdrop(panel.transform, UiEditBackground);
            Transform safeContent = CreateSafeAreaContent(panel.transform);

            PlayerEditUIController controller = canvas.gameObject.AddComponent<PlayerEditUIController>();
            controller.uiPanel = panel;

            GameObject content = new GameObject("Edit Content", typeof(RectTransform));
            content.transform.SetParent(safeContent, false);

            RectTransform contentRect = (RectTransform)content.transform;
            contentRect.anchorMin = new Vector2(0.5f, 0.5f);
            contentRect.anchorMax = new Vector2(0.5f, 0.5f);
            contentRect.pivot = new Vector2(0.5f, 0.5f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = UiEditContentSize;

            Transform host = content.transform;

            controller.headingText = CreateHudText(host, "Edit Heading", "#0",
                new Vector2(0.5f, 0.5f), new Vector2(0f, UiEditHeadingY),
                new Vector2(1200f, 80f), 48, TextAnchor.MiddleCenter);

            Localize(CreateHudText(host, "Edit Role Label", "POSICIÓN",
                new Vector2(0.5f, 0.5f), new Vector2(0f, 360f),
                UiEditLabelSize, 26, TextAnchor.MiddleCenter), "edit.role");

            controller.roleGoalkeeperButton = CreateEditChoice(host, "Edit Role GK", "GK", -1.5f, 300f);
            controller.roleDefenderButton = CreateEditChoice(host, "Edit Role DF", "DF", -0.5f, 300f);
            controller.roleMidfielderButton = CreateEditChoice(host, "Edit Role MF", "MF", 0.5f, 300f);
            controller.roleForwardButton = CreateEditChoice(host, "Edit Role FW", "FW", 1.5f, 300f);

            Localize(CreateHudText(host, "Edit Element Label", "ELEMENTO",
                new Vector2(0.5f, 0.5f), new Vector2(0f, 235f),
                UiEditLabelSize, 26, TextAnchor.MiddleCenter), "edit.element");

            controller.elementFireButton = CreateEditChoice(host, "Edit Element Fire",
                $"{Elements.Glyph(Element.Fuego)} FUEGO", -1.5f, 175f);
            controller.elementForestButton = CreateEditChoice(host, "Edit Element Forest",
                $"{Elements.Glyph(Element.Bosque)} BOSQUE", -0.5f, 175f);
            controller.elementWindButton = CreateEditChoice(host, "Edit Element Wind",
                $"{Elements.Glyph(Element.Aire)} AIRE", 0.5f, 175f);
            controller.elementMountainButton = CreateEditChoice(host, "Edit Element Mountain",
                $"{Elements.Glyph(Element.Montaña)} MONTAÑA", 1.5f, 175f);

            string[] rows = { "REGATE", "FUERZA", "TIRO", "ENTRADA", "BLOQUEO", "PARADA", "ESTAMINA" };

            string[] rowKeys =
            {
                "stat.dribble", "stat.power", "stat.shoot", "stat.tackle",
                "stat.block", "stat.goalkeeping", "stat.stamina"
            };
            Button[] downs = new Button[rows.Length];
            Button[] ups = new Button[rows.Length];
            Text[] values = new Text[rows.Length];

            for (int i = 0; i < rows.Length; i++)
            {
                float y = UiEditFirstRowY - (i * UiEditRowStep);

                Localize(CreateHudText(host, $"Edit {rows[i]} Label", rows[i],
                    new Vector2(0.5f, 0.5f), new Vector2(UiEditNameX, y),
                    UiEditNameSize, 30, TextAnchor.MiddleLeft), rowKeys[i]);

                downs[i] = CreateEditNudge(host, $"Edit {rows[i]} Down", "−", UiEditMinusX, y);

                values[i] = CreateHudText(host, $"Edit {rows[i]} Value", "0",
                    new Vector2(0.5f, 0.5f), new Vector2(UiEditValueX, y),
                    UiEditValueSize, 34, TextAnchor.MiddleCenter);

                ups[i] = CreateEditNudge(host, $"Edit {rows[i]} Up", "+", UiEditPlusX, y);
            }

            controller.statValueTexts = values;

            controller.dribbleDownButton = downs[0]; controller.dribbleUpButton = ups[0];
            controller.powerDownButton = downs[1]; controller.powerUpButton = ups[1];
            controller.shootDownButton = downs[2]; controller.shootUpButton = ups[2];
            controller.tackleDownButton = downs[3]; controller.tackleUpButton = ups[3];
            controller.blockDownButton = downs[4]; controller.blockUpButton = ups[4];
            controller.goalkeepingDownButton = downs[5]; controller.goalkeepingUpButton = ups[5];
            controller.staminaDownButton = downs[6]; controller.staminaUpButton = ups[6];

            controller.noticeText = CreateHudText(host, "Edit Notice", string.Empty,
                new Vector2(0.5f, 0.5f), UiEditNoticeOffset,
                UiEditNoticeSize, 26, TextAnchor.MiddleCenter);

            controller.noticeText.color = UiEditNoticeColor;

            Button save = LocalizeButton(CreateUiButton(host, "Edit Save Button", "GUARDAR",
                new Vector2(-200f, UiEditExitY), UiEditExitSize), "edit.save");
            save.targetGraphic.color = UiKickoffButtonColor;
            save.GetComponentInChildren<Text>().color = Color.white;

            Button close = LocalizeButton(CreateUiButton(host, "Edit Close Button", "CERRAR",
                new Vector2(200f, UiEditExitY), UiEditExitSize), "edit.close");

            controller.saveButton = save;
            controller.closeButton = close;

            panel.SetActive(false);
        }

        // Crea uno de los botones de elección del editor de jugador (posición o elemento).
        private static Button CreateEditChoice(Transform parent, string objectName, string caption,
            float column, float y)
        {
            Button button = CreateUiButton(parent, objectName, caption,
                new Vector2(column * UiEditChoiceSpacing, y), UiEditChoiceSize);

            button.GetComponentInChildren<Text>().fontSize = 26;

            return button;
        }

        // Crea un botón pequeño de − o + para ajustar una estadística.
        private static Button CreateEditNudge(Transform parent, string objectName, string caption,
            float x, float y)
        {
            Button button = CreateUiButton(parent, objectName, caption, new Vector2(x, y), UiEditNudgeSize);

            button.GetComponentInChildren<Text>().fontSize = 38;

            return button;
        }

        // Crea un hijo estirado para llenar por completo a su padre.
        private static GameObject CreateStretchedChild(Transform parent, string objectName)
        {
            GameObject child = new GameObject(objectName, typeof(RectTransform));
            child.transform.SetParent(parent, false);

            RectTransform rect = (RectTransform)child.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            return child;
        }

        // Crea las dos barras de momentum, una por equipo, junto al marcador.
        private static void CreateTensionUI(Transform canvas)
        {
            GameObject root = new GameObject("Tension HUD", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(root, "Create Tension HUD");
            root.transform.SetParent(canvas, false);

            root.transform.SetAsFirstSibling();

            RectTransform rootRect = (RectTransform)root.transform;
            rootRect.anchorMin = new Vector2(0.5f, 1f);
            rootRect.anchorMax = new Vector2(0.5f, 1f);
            rootRect.pivot = new Vector2(0.5f, 1f);
            rootRect.anchoredPosition = UiTensionRootOffset;
            rootRect.sizeDelta = UiTensionRootSize;

            Image blueFill = CreateTensionBar(root.transform, "Blue Tension", -UiTensionBarSpacing);
            Image redFill = CreateTensionBar(root.transform, "Red Tension", UiTensionBarSpacing);

            Text blueLabel = CreateHudText(root.transform, "Blue Tension Label", string.Empty,
                new Vector2(0.5f, 0.5f), new Vector2(-UiTensionBarSpacing, UiTensionLabelY),
                UiTensionLabelSize, 24, TextAnchor.MiddleCenter);

            Text redLabel = CreateHudText(root.transform, "Red Tension Label", string.Empty,
                new Vector2(0.5f, 0.5f), new Vector2(UiTensionBarSpacing, UiTensionLabelY),
                UiTensionLabelSize, 24, TextAnchor.MiddleCenter);

            foreach (Graphic graphic in root.GetComponentsInChildren<Graphic>(true))
            {
                graphic.raycastTarget = false;
            }

            TensionUI ui = root.AddComponent<TensionUI>();
            ui.blueFill = blueFill;
            ui.redFill = redFill;
            ui.blueLabel = blueLabel;
            ui.redLabel = redLabel;
        }

        // Crea una barra de momentum: un fondo oscuro con un relleno horizontal.
        private static Image CreateTensionBar(Transform parent, string objectName, float x)
        {
            GameObject trough = new GameObject(objectName, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(trough, $"Create {objectName}");
            trough.transform.SetParent(parent, false);

            RectTransform troughRect = (RectTransform)trough.transform;
            troughRect.anchorMin = new Vector2(0.5f, 0.5f);
            troughRect.anchorMax = new Vector2(0.5f, 0.5f);
            troughRect.pivot = new Vector2(0.5f, 0.5f);
            troughRect.anchoredPosition = new Vector2(x, 0f);
            troughRect.sizeDelta = UiTensionBarSize;

            Image background = trough.AddComponent<Image>();
            background.color = UiTensionTroughColor;

            GameObject fillObject = new GameObject("Fill", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(fillObject, "Create Tension Fill");
            fillObject.transform.SetParent(trough.transform, false);

            RectTransform fillRect = (RectTransform)fillObject.transform;
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = new Vector2(UiTensionBarInset, UiTensionBarInset);
            fillRect.offsetMax = new Vector2(-UiTensionBarInset, -UiTensionBarInset);

            Image fill = fillObject.AddComponent<Image>();
            fill.sprite = GetOrCreateWhiteSprite();
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillAmount = 0f;

            return fill;
        }

        // Crea el menú de penalti: encabezado, dos botones de dirección y el resultado.
        private static void CreatePenaltyUI(Transform canvas)
        {
            GameObject panel = new GameObject("Penalty Panel", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(panel, "Create Penalty Panel");
            panel.transform.SetParent(canvas, false);

            RectTransform rect = (RectTransform)panel.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            Image background = CreateFullScreenBackdrop(panel.transform, UiPenaltyBackground);
            Transform safeContent = CreateSafeAreaContent(panel.transform);

            Text heading = CreateHudText(safeContent, "Penalty Heading", "PENALTI",
                new Vector2(0.5f, 0.5f), UiPenaltyHeadingOffset,
                UiPenaltyHeadingSize, 70, TextAnchor.MiddleCenter);

            Button left = LocalizeButton(CreateUiButton(safeContent, "Penalty Left Button",
                "IZQUIERDA", new Vector2(-UiPenaltyButtonSpacing, UiPenaltyButtonY), UiPenaltyButtonSize,
                playClickSound: false), "penalty.left");

            Button right = LocalizeButton(CreateUiButton(safeContent, "Penalty Right Button",
                "DERECHA", new Vector2(UiPenaltyButtonSpacing, UiPenaltyButtonY), UiPenaltyButtonSize,
                playClickSound: false), "penalty.right");

            Text result = CreateHudText(safeContent, "Penalty Result", string.Empty,
                new Vector2(0.5f, 0.5f), UiPenaltyResultOffset,
                UiPenaltyResultSize, 80, TextAnchor.MiddleCenter);

            PenaltyUIController controller = canvas.gameObject.AddComponent<PenaltyUIController>();
            controller.uiPanel = panel;
            controller.headingText = heading;
            controller.resultText = result;
            controller.leftButton = left;
            controller.rightButton = right;

            panel.SetActive(false);
        }

        // Crea la pantalla de descanso, con el resumen del primer tiempo y el botón de continuar.
        private static void CreateHalftimeUI(Transform canvas)
        {
            GameObject panel = new GameObject("Halftime Panel", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(panel, "Create Halftime Panel");
            panel.transform.SetParent(canvas, false);

            RectTransform rect = (RectTransform)panel.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            Image background = CreateFullScreenBackdrop(panel.transform, UiTitleBackground);
            Transform safeContent = CreateSafeAreaContent(panel.transform);

            Text heading = Localize(CreateHudText(safeContent, "Halftime Heading", "DESCANSO",
                new Vector2(0.5f, 0.5f), UiHalftimeHeadingOffset,
                UiHalftimeHeadingSize, 90, TextAnchor.MiddleCenter), "halftime.heading");

            Text summary = CreateHudText(safeContent, "Halftime Summary", string.Empty,
                new Vector2(0.5f, 0.5f), UiHalftimeSummaryOffset,
                UiHalftimeSummarySize, 38, TextAnchor.UpperCenter);

            Button substitutions = LocalizeButton(CreateUiButton(safeContent,
                "Halftime Subs Button", "HACER CAMBIOS",
                UiHalftimeSubsOffset, UiHalftimeButtonSize), "halftime.subs");

            Button resume = LocalizeButton(CreateUiButton(safeContent,
                "Halftime Resume Button", "SEGUNDA PARTE",
                UiHalftimeResumeOffset, UiHalftimeButtonSize), "halftime.resume");

            resume.targetGraphic.color = UiKickoffButtonColor;

            Text resumeLabel = resume.GetComponentInChildren<Text>();
            resumeLabel.color = Color.white;

            HalftimeUIController controller = canvas.gameObject.AddComponent<HalftimeUIController>();
            controller.uiPanel = panel;
            controller.headingText = heading;
            controller.summaryText = summary;
            controller.substitutionsButton = substitutions;
            controller.secondHalfButton = resume;

            panel.SetActive(false);
        }

        // Crea el marco de la pantalla de sustituciones (encabezado, estadísticas, zonas y botón de cerrar).
        // Los diez huecos de jugador los crea el controlador en tiempo de ejecución según la plantilla.
        private static void CreateSubstitutionUI(Transform canvas)
        {
            GameObject panel = new GameObject("Substitution Panel", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(panel, "Create Substitution Panel");
            panel.transform.SetParent(canvas, false);

            RectTransform rect = (RectTransform)panel.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            Image background = CreateFullScreenBackdrop(panel.transform, UiSubsBackground);
            Transform safeContent = CreateSafeAreaContent(panel.transform);

            Text header = Localize(CreateHudText(safeContent, "Substitution Header", "CAMBIOS",
                new Vector2(0.5f, 0.5f), UiSubsHeaderOffset,
                UiSubsHeaderSize, 58, TextAnchor.MiddleCenter), "subs.heading");

            Text stats = CreateSubstitutionStatsPanel(safeContent);


            RectTransform pitchArea = CreateSubstitutionZone(safeContent, "Pitch Area",
                UiSubsPitchOffset, UiSubsPitchSize, UiSubsPitchColor, "EN EL CAMPO", "subs.pitch");

            RectTransform benchArea = CreateSubstitutionZone(safeContent, "Bench Area",
                UiSubsBenchOffset, UiSubsBenchSize, UiSubsBenchColor, "BANQUILLO", "subs.bench");

            Button close = LocalizeButton(CreateUiButton(safeContent,
                "Substitution Close Button", "VOLVER",
                UiSubsCloseOffset, UiSubsCloseSize), "subs.close");

            Text closeLabel = close.GetComponentInChildren<Text>();
            closeLabel.fontSize = 32;

            Button edit = LocalizeButton(CreateUiButton(safeContent,
                "Substitution Edit Button", "EDITAR JUGADOR",
                UiSubsEditOffset, UiSubsCloseSize), "subs.edit");

            edit.GetComponentInChildren<Text>().fontSize = 30;

            SubstitutionUIController controller = canvas.gameObject.AddComponent<SubstitutionUIController>();
            controller.uiPanel = panel;
            controller.closeButton = close;
            controller.editButton = edit;
            controller.headerText = header;
            controller.statsText = stats;
            controller.pitchArea = pitchArea;
            controller.benchArea = benchArea;

            panel.SetActive(false);
        }

        // Crea la tarjeta de estadísticas del jugador seleccionado, a la izquierda del tablero.
        private static Text CreateSubstitutionStatsPanel(Transform parent)
        {
            GameObject card = new GameObject("Stats Card", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(card, "Create Stats Card");
            card.transform.SetParent(parent, false);

            RectTransform cardRect = (RectTransform)card.transform;
            cardRect.anchorMin = new Vector2(0.5f, 0.5f);
            cardRect.anchorMax = new Vector2(0.5f, 0.5f);
            cardRect.pivot = new Vector2(0.5f, 0.5f);
            cardRect.anchoredPosition = UiSubsStatsPanelOffset;
            cardRect.sizeDelta = UiSubsStatsPanelSize;

            Image cardImage = card.AddComponent<Image>();
            cardImage.color = UiSubsStatsPanelColor;

            GameObject textObject = new GameObject("Stats Text", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(textObject, "Create Stats Text");
            textObject.transform.SetParent(card.transform, false);

            RectTransform textRect = (RectTransform)textObject.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(UiSubsStatsPadding, UiSubsStatsPadding);
            textRect.offsetMax = new Vector2(-UiSubsStatsPadding, -UiSubsStatsPadding);

            Text text = textObject.AddComponent<Text>();
            text.font = GetUIFont();
            text.fontSize = 30;
            text.lineSpacing = 1.15f;
            text.alignment = TextAnchor.UpperLeft;
            text.color = Color.white;
            text.text = string.Empty;

            return text;
        }

        // Crea una de las dos zonas del tablero (campo o banquillo), con su caja y su título.
        private static RectTransform CreateSubstitutionZone(Transform parent, string objectName,
            Vector2 offset, Vector2 size, Color color, string caption, string captionKey)
        {
            GameObject zone = new GameObject(objectName, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(zone, $"Create {objectName}");
            zone.transform.SetParent(parent, false);

            RectTransform rect = (RectTransform)zone.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = offset;
            rect.sizeDelta = size;

            Image background = zone.AddComponent<Image>();
            background.color = color;

            Localize(CreateHudText(parent, $"{objectName} Caption", caption,
                new Vector2(0.5f, 0.5f),
                new Vector2(offset.x, offset.y + (size.y * 0.5f) + UiSubsCaptionClearance),
                new Vector2(size.x, 44f), 28, TextAnchor.MiddleCenter), captionKey);

            return rect;
        }

        // Crea el titular del duelo, en la zona central del panel.
        private static Text CreateClashText(Transform parent)
        {
            GameObject textObject = new GameObject("Clash Text", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(textObject, "Create Clash Text");
            textObject.transform.SetParent(parent, false);

            RectTransform rect = (RectTransform)textObject.transform;
            rect.anchorMin = new Vector2(UiClashSideZoneWidth, 0.62f);
            rect.anchorMax = new Vector2(1f - UiClashSideZoneWidth, 1f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = new Vector2(0f, -14f);

            Text text = textObject.AddComponent<Text>();
            text.font = GetUIFont();
            text.fontSize = 34;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.text = string.Empty;

            return text;
        }

        // Crea el texto de estadísticas de un equipo en el panel de duelo.
        private static Text CreateClashStatsText(Transform parent, string objectName,
            Vector2 anchorMin, Vector2 anchorMax, TextAnchor alignment, Color color,
            Vector2 offsetMin, Vector2 offsetMax)
        {
            GameObject textObject = new GameObject(objectName, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(textObject, $"Create {objectName}");
            textObject.transform.SetParent(parent, false);

            RectTransform rect = (RectTransform)textObject.transform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;

            Text text = textObject.AddComponent<Text>();
            text.font = GetUIFont();
            text.fontSize = 30;
            text.lineSpacing = 1.2f;
            text.alignment = alignment;
            text.color = color;
            text.text = string.Empty;

            return text;
        }

        // Crea uno de los botones de acción del duelo. El texto es solo un placeholder que el controlador reescribe.
        private static Button CreateActionButton(Transform parent, string objectName, string caption, float xOffset)
        {
            GameObject buttonObject = new GameObject(objectName, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(buttonObject, $"Create {objectName}");
            buttonObject.transform.SetParent(parent, false);

            RectTransform rect = (RectTransform)buttonObject.transform;
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(xOffset, 32f);
            rect.sizeDelta = UiButtonSize;

            Image background = buttonObject.AddComponent<Image>();
            background.color = new Color(0.88f, 0.88f, 0.88f, 1f);

            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = background;

            GameObject labelObject = new GameObject("Label", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(labelObject, $"Create {objectName} Label");
            labelObject.transform.SetParent(buttonObject.transform, false);

            RectTransform labelRect = (RectTransform)labelObject.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            Text label = labelObject.AddComponent<Text>();
            label.font = GetUIFont();
            label.fontSize = UiButtonFontSize;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.black;
            label.text = caption;

            return button;
        }

        // Devuelve la fuente de la UI: la fuente propia si existe, o una del sistema como reserva.
        private static Font GetUIFont()
        {
            Font customFont = Resources.Load<Font>("MainFont");
            if (customFont != null)
            {
                return customFont;
            }
            try
            {
                return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
            catch (System.ArgumentException)
            {
                return Resources.GetBuiltinResource<Font>("Arial.ttf");
            }
        }

        // Busca una fuente monoespaciada del sistema para la tabla de estadísticas de fin de partido.
        private static Font GetMonospaceFont()
        {
#if UNITY_WEBGL
            Debug.LogWarning("[Generador] WebGL no soporta fuentes del SO. Usando fuente UI para texto monoespaciado.");
            return GetUIFont();
#else
            string[] candidates = { "Consolas", "Courier New", "Lucida Console", "DejaVu Sans Mono" };
            Font font = FontResolver.TryResolveOSFont(candidates, 40);

            if (font != null)
            {
                return font;
            }

            Debug.LogWarning("[Generador] Sin fuente monoespaciada: la tabla de estadísticas " +
                             "no quedará alineada en columnas.");

            return GetUIFont();
#endif
        }

        // Busca una fuente del sistema capaz de dibujar el kanji del elemento en la etiqueta del jugador.
        private static Font GetPlayerTagFont()
        {
#if UNITY_WEBGL
            Debug.LogWarning("[Generador] WebGL no soporta fuentes del SO. Usando fuente UI de fallback.");
            return GetUIFont();
#else
            string[] candidates =
            {
                "Yu Gothic UI", "Yu Gothic", "MS Gothic", "Meiryo",
                "Microsoft YaHei", "SimSun", "Segoe UI"
            };

            Font font = FontResolver.TryResolveOSFont(candidates, PlayerLabelRoleFontSize);

            if (font != null)
            {
                return font;
            }

            Debug.LogWarning("[Generador] No se encontró ninguna fuente del sistema con kanji: " +
                             "las etiquetas de jugador saldrán sin el símbolo elemental.");

            return GetUIFont();
#endif
        }

        // Crea el EventSystem necesario para que la UI reciba clics, usando el módulo del Input System.
        private static void CreateEventSystem()
        {
            GameObject eventSystemObject = new GameObject("EventSystem");
            Undo.RegisterCreatedObjectUndo(eventSystemObject, "Create EventSystem");

            eventSystemObject.AddComponent<EventSystem>();
            eventSystemObject.AddComponent<InputSystemUIInputModule>();
        }

        // Configura la cámara principal del partido: posición, ángulo, seguimiento y zoom.
        private static void ConfigureMatchCamera(Transform followTarget)
        {
            GameObject cameraObject = GameObject.FindWithTag("MainCamera");
            if (cameraObject == null)
            {
                Debug.LogWarning("No se encontró ninguna cámara con el tag 'MainCamera'. Se omite la configuración de cámara.");
                return;
            }

            Vector3 rigOffset = new Vector3(0f, CameraHeight, -CameraDistanceBehind);
            Vector3 rigRotation = new Vector3(CameraPitchAngle, 0f, 0f);

            Undo.RecordObject(cameraObject.transform, "Configure Match Camera Transform");
            cameraObject.transform.position = rigOffset;
            cameraObject.transform.rotation = Quaternion.Euler(rigRotation);

            float aspect = 16f / 9f;

            if (cameraObject.TryGetComponent(out Camera camera))
            {
                Undo.RecordObject(camera, "Configure Match Camera");

                camera.orthographic = false;
                camera.fieldOfView = CameraFieldOfView;

                if (camera.aspect > 0f)
                {
                    aspect = camera.aspect;
                }
            }

            CameraController controller = cameraObject.GetComponent<CameraController>();
            if (controller == null)
            {
                controller = Undo.AddComponent<CameraController>(cameraObject);
            }

            CalculateCameraBounds(aspect, out Vector2 minBounds, out Vector2 maxBounds);

            Undo.RecordObject(controller, "Configure Camera Follow");

            controller.Configure(followTarget, rigOffset, minBounds, maxBounds);
            controller.ConfigureLookAhead(CameraLookAhead, CameraMaxLookAhead, CameraLookAheadSmoothing);

            EditorUtility.SetDirty(controller);

            TacticalCamera tacticalCamera = cameraObject.GetComponent<TacticalCamera>();
            if (tacticalCamera == null)
            {
                tacticalCamera = Undo.AddComponent<TacticalCamera>(cameraObject);
            }

            Undo.RecordObject(tacticalCamera, "Configure Tactical Camera");

            tacticalCamera.ConfigureOverhead(rigOffset, rigRotation);

            tacticalCamera.ConfigureZoom(CameraMinZoom, CameraMaxZoom, CameraZoomSensitivity);

            tacticalCamera.ConfigureClashFraming(
                ClashCameraBackDistance, ClashCameraHeight, ClashCameraFieldOfView);

            tacticalCamera.ConfigureBallFlightFraming(
                BallFlightCameraBackDistance, BallFlightCameraHeight, BallFlightCameraFieldOfView);

            EditorUtility.SetDirty(tacticalCamera);
        }

        // Calcula hasta dónde puede moverse la cámara sin que su campo de visión se salga del terreno de juego.
        private static void CalculateCameraBounds(float aspect, out Vector2 minBounds, out Vector2 maxBounds)
        {
            float halfFov = CameraFieldOfView * 0.5f;

            float topAngle = Mathf.Max(1f, CameraPitchAngle - halfFov);
            float bottomAngle = Mathf.Min(89f, CameraPitchAngle + halfFov);

            float nearForward = CameraHeight / Mathf.Tan(topAngle * Mathf.Deg2Rad);
            float farForward = CameraHeight / Mathf.Tan(bottomAngle * Mathf.Deg2Rad);

            float closest = Mathf.Min(nearForward, farForward);
            float furthest = Mathf.Max(nearForward, farForward);

            float reach = PitchHalfLength + CameraExtraGoalPan;

            float minZ = -reach - closest;
            float maxZ = reach - furthest;

            if (maxZ < minZ)
            {
                float locked = (minZ + maxZ) * 0.5f;
                minZ = locked;
                maxZ = locked;
            }

            float nearSlantRange = CameraHeight / Mathf.Sin(bottomAngle * Mathf.Deg2Rad);
            float visibleHalfWidth = nearSlantRange * Mathf.Tan(halfFov * Mathf.Deg2Rad) * aspect;

            float limitX = Mathf.Max(0f, PitchHalfWidth - visibleHalfWidth);

            minBounds = new Vector2(-limitX, minZ);
            maxBounds = new Vector2(limitX, maxZ);
        }

        // ---------------------------------------------------------------------
        // Generación procedural de texturas
        // ---------------------------------------------------------------------

        // Dibuja la textura del terreno de juego: franjas de césped, líneas, círculo central y áreas.
        private static Texture2D CreatePitchTexture()
        {
            int width = PitchTextureWidth;
            int height = Mathf.RoundToInt(PitchTextureWidth * (PitchScale.z / PitchScale.x));
            Color32[] pixels = new Color32[width * height];

            Color32 grassDark = new Color32(34, 122, 48, 255);
            Color32 grassLight = new Color32(44, 143, 58, 255);
            Color32 lineColor = new Color32(240, 240, 240, 255);

            int stripeHeight = Mathf.Max(1, height / 12);
            for (int y = 0; y < height; y++)
            {
                Color32 rowColor = (y / stripeHeight) % 2 == 0 ? grassDark : grassLight;
                int rowStart = y * width;
                for (int x = 0; x < width; x++)
                {
                    pixels[rowStart + x] = rowColor;
                }
            }

            int thickness = Mathf.Max(2, width / 128);
            int margin = Mathf.RoundToInt(width * 0.05f);

            TextureDrawing.DrawRectOutline(pixels, width, height, margin, margin, width - margin, height - margin, thickness, lineColor);
            TextureDrawing.FillRect(pixels, width, height, margin, (height - thickness) / 2, width - margin, (height + thickness) / 2, lineColor);
            TextureDrawing.DrawCircleOutline(pixels, width, height, width / 2, height / 2, width / 8, thickness, lineColor);

            DrawPenaltyAreas(pixels, width, height, thickness, lineColor);

            int spotRadius = Mathf.Max(2, width / 110);

            TextureDrawing.DrawCircleOutline(pixels, width, height, width / 2, height / 2,
                spotRadius, spotRadius, lineColor);

            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, true)
            {
                name = "PitchTexture",
                wrapMode = TextureWrapMode.Clamp
            };
            texture.SetPixels32(pixels);
            texture.Apply();

            return texture;
        }

        // Dibuja las dos áreas de penalti en la textura del terreno, usando las mismas medidas que las reglas de faltas.
        private static void DrawPenaltyAreas(Color32[] pixels, int width, int height,
            int thickness, Color32 lineColor)
        {
            float pixelsPerUnit = width / (PitchHalfWidth * 2f);

            System.Func<float, int> toPixelX = delegate (float worldX)
            {
                return Mathf.RoundToInt((worldX + PitchHalfWidth) * pixelsPerUnit);
            };

            System.Func<float, int> toPixelY = delegate (float worldZ)
            {
                return Mathf.RoundToInt((worldZ + PitchHalfLength) * pixelsPerUnit);
            };

            int left = toPixelX(-Core.PitchBounds.PenaltyAreaHalfWidth);
            int right = toPixelX(Core.PitchBounds.PenaltyAreaHalfWidth);

            float edge = Core.PitchBounds.GoalLineZ;
            float front = edge - Core.PitchBounds.PenaltyAreaDepth;

            TextureDrawing.DrawRectOutline(pixels, width, height,
                left, toPixelY(front), right, toPixelY(edge), thickness, lineColor);

            TextureDrawing.DrawRectOutline(pixels, width, height,
                left, toPixelY(-edge), right, toPixelY(-front), thickness, lineColor);
        }

        // Dibuja la textura del balón: un patrón de cuadros para que se note cuando rueda.
        private static Texture2D CreateBallTexture()
        {
            const int size = 64;
            const int cellSize = 8;

            Color32 light = new Color32(250, 250, 250, 255);
            Color32 dark = new Color32(30, 30, 35, 255);
            Color32[] pixels = new Color32[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool isDark = ((x / cellSize) + (y / cellSize)) % 2 == 0;
                    pixels[y * size + x] = isDark ? dark : light;
                }
            }

            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                name = "BallTexture",
                wrapMode = TextureWrapMode.Repeat
            };
            texture.SetPixels32(pixels);
            texture.Apply();

            return texture;
        }

        // Dibuja el icono de engranaje del menú de desarrollador a partir de formas simples.
        private static Texture2D CreateGearIconTexture()
        {
            const int size = 64;

            Color32 transparent = new Color32(0, 0, 0, 0);
            Color32 gear = UiDebugGearColor;

            Color32[] pixels = new Color32[size * size];

            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = transparent;
            }

            int center = size / 2;
            int outerRadius = (size / 2) - 6;
            int innerRadius = outerRadius / 2;
            int toothHalf = size / 12;

            TextureDrawing.FillCircle(pixels, size, size, center, center, outerRadius, gear);

            for (int i = 0; i < 8; i++)
            {
                float angle = i * Mathf.PI / 4f;
                int toothX = center + Mathf.RoundToInt(Mathf.Cos(angle) * outerRadius);
                int toothY = center + Mathf.RoundToInt(Mathf.Sin(angle) * outerRadius);

                TextureDrawing.FillRect(pixels, size, size,
                    toothX - toothHalf, toothY - toothHalf,
                    toothX + toothHalf, toothY + toothHalf, gear);
            }

            TextureDrawing.FillCircle(pixels, size, size, center, center, innerRadius, transparent);

            Texture2D gearTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "DebugGearIconTexture",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            gearTexture.SetPixels32(pixels);
            gearTexture.Apply();

            return gearTexture;
        }

        // Crea el material físico del balón, con combinación de rebote al máximo.
        private static PhysicsMaterial CreateBallPhysicsMaterial()
        {
            return new PhysicsMaterial("BallPhysics")
            {
                bounciness = BallBounciness,
                dynamicFriction = 0.4f,
                staticFriction = 0.4f,
                bounceCombine = PhysicsMaterialCombine.Maximum,
                frictionCombine = PhysicsMaterialCombine.Average
            };
        }

        // FillRect / DrawRectOutline / DrawCircleOutline están en TextureDrawing.cs.

        // ---------------------------------------------------------------------
        // Utilidades de assets
        // ---------------------------------------------------------------------

        // Devuelve el asset existente en la ruta generada, o lo crea con la factory y lo guarda.
        private static T GetOrCreateAsset<T>(string fileName, System.Func<T> factory) where T : UnityEngine.Object
        {
            EnsureGeneratedFolder();

            string path = $"{GeneratedFolder}/{fileName}";
            T existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null)
            {
                return existing;
            }

            T created = factory();
            AssetDatabase.CreateAsset(created, path);

            return created;
        }

        // Crea o reutiliza el material transparente del aura de un jugador en la zona.
        private static Material GetOrCreateAuraMaterial()
        {
            return GetOrCreateAsset("TensionAuraMaterial.mat", () =>
            {
                Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");

                Material material = new Material(unlit != null ? unlit : GetDefaultShader())
                {
                    name = "TensionAuraMaterial",
                    color = new Color(1f, 0.45f, 0.05f, 0.55f)
                };

                MakeTransparent(material);

                return material;
            });
        }

        // Repinta la textura del terreno de juego sobre el asset existente, en vez de reutilizarla tal cual.
        private static Texture2D GetOrRefreshPitchTexture()
        {
            EnsureGeneratedFolder();

            string path = $"{GeneratedFolder}/PitchTexture.asset";
            Texture2D existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            Texture2D fresh = CreatePitchTexture();

            if (existing == null)
            {
                AssetDatabase.CreateAsset(fresh, path);

                return fresh;
            }

            if (existing.width != fresh.width || existing.height != fresh.height)
            {
                existing.Reinitialize(fresh.width, fresh.height);
            }

            existing.SetPixels32(fresh.GetPixels32());
            existing.Apply();
            EditorUtility.SetDirty(existing);

            UnityEngine.Object.DestroyImmediate(fresh);

            return existing;
        }

        // Crea o reutiliza un material simple con un color y una textura opcional.
        private static Material GetOrCreateMaterial(string fileName, Color color, Texture2D mainTexture)
        {
            return GetOrCreateAsset(fileName, () =>
            {
                Material material = new Material(GetDefaultShader())
                {
                    name = System.IO.Path.GetFileNameWithoutExtension(fileName),
                    color = color
                };

                if (mainTexture != null)
                {
                    material.mainTexture = mainTexture;
                }

                return material;
            });
        }

        // Crea o reutiliza un PlayerStatsSO y reescribe sus valores en cada generación.
        private static PlayerStatsSO GetOrCreateStats(string fileName,
            int dribble, int power, int shoot, int tackle, int block, int goalkeeping)
        {
            PlayerStatsSO stats = GetOrCreateAsset(fileName, () =>
            {
                PlayerStatsSO created = ScriptableObject.CreateInstance<PlayerStatsSO>();
                created.name = System.IO.Path.GetFileNameWithoutExtension(fileName);

                return created;
            });

            Undo.RecordObject(stats, $"Configure {stats.name}");
            stats.dribble = dribble;
            stats.power = power;
            stats.shoot = shoot;
            stats.tackle = tackle;
            stats.block = block;
            stats.goalkeeping = goalkeeping;
            EditorUtility.SetDirty(stats);

            return stats;
        }

        // Crea o reutiliza el sprite blanco usado como relleno de las barras de progreso.
        private static Sprite GetOrCreateWhiteSprite()
        {
            Texture2D texture = GetOrCreateAsset("WhiteTexture.asset", CreateWhiteTexture);

            return GetOrCreateAsset("WhiteSprite.asset", () =>
            {
                Sprite sprite = Sprite.Create(
                    texture,
                    new Rect(0f, 0f, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f),
                    PixelsPerUnit);

                sprite.name = "WhiteSprite";

                return sprite;
            });
        }

        // Crea una textura blanca lisa de 4x4 píxeles.
        private static Texture2D CreateWhiteTexture()
        {
            const int size = 4;

            Color32[] pixels = new Color32[size * size];

            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Color32(255, 255, 255, 255);
            }

            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "WhiteTexture",
                wrapMode = TextureWrapMode.Clamp
            };

            texture.SetPixels32(pixels);
            texture.Apply();

            return texture;
        }

        // Crea o reutiliza el material transparente y emisivo de la onda de impacto.
        private static Material GetOrCreateImpactMaterial()
        {
            return GetOrCreateAsset("ImpactWaveMaterial.mat", () =>
            {
                Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");

                Material material = new Material(unlit != null ? unlit : GetDefaultShader())
                {
                    name = "ImpactWaveMaterial",
                    color = new Color(1f, 0.93f, 0.35f, 0.75f)
                };

                MakeTransparent(material);

                return material;
            });
        }

        // Crea o reutiliza el material de la sombra del balón, sin sombreado por la luz de la escena.
        private static Material GetOrCreateBallShadowMaterial()
        {
            return GetOrCreateAsset("BallShadowMaterial.mat", () =>
            {
                Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");

                Material material = new Material(unlit != null ? unlit : GetDefaultShader())
                {
                    name = "BallShadowMaterial",
                    color = new Color(0f, 0f, 0f, 0.5f)
                };

                MakeTransparent(material);

                return material;
            });
        }

        // Crea o reutiliza el material blanco y translúcido de la red de la portería.
        private static Material GetOrCreateNetMaterial()
        {
            return GetOrCreateAsset("GoalNetMaterial.mat", () =>
            {
                Material material = new Material(GetDefaultShader())
                {
                    name = "GoalNetMaterial",
                    color = new Color(1f, 1f, 1f, 0.3f)
                };

                MakeTransparent(material);

                return material;
            });
        }

        // Configura un material URP para que use alpha blending en vez de opaco.
        private static void MakeTransparent(Material material)
        {
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
        }

        // Asigna el material de la línea de ruta al LineRenderer del jugador.
        private static void ApplyRouteLineMaterial(GameObject player)
        {
            if (player.TryGetComponent(out LineRenderer lineRenderer))
            {
                lineRenderer.sharedMaterial = GetOrCreateLineMaterial();
            }
        }

        // Crea o reutiliza el material blanco y plano de las líneas de ruta.
        private static Material GetOrCreateLineMaterial()
        {
            return GetOrCreateAsset("RouteLineMaterial.mat", () =>
            {
                Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");

                return new Material(unlit != null ? unlit : GetDefaultShader())
                {
                    name = "RouteLineMaterial",
                    color = Color.white
                };
            });
        }

        // Devuelve el shader por defecto del pipeline de render activo.
        private static Shader GetDefaultShader()
        {
            RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;
            if (pipeline != null && pipeline.defaultShader != null)
            {
                return pipeline.defaultShader;
            }

            return Shader.Find("Standard");
        }

        // Crea la carpeta de assets generados si todavía no existe.
        private static void EnsureGeneratedFolder()
        {
            if (!AssetDatabase.IsValidFolder(GeneratedFolder))
            {
                AssetDatabase.CreateFolder("Assets", "Generated");
            }
        }

        // Asigna un material al renderer de un objeto, si tiene uno.
        private static void ApplyMaterial(GameObject target, Material material)
        {
            if (target.TryGetComponent(out Renderer renderer))
            {
                renderer.sharedMaterial = material;
            }
        }

        // Asigna una capa a un objeto por nombre, avisando si no existe.
        private static void SetLayerByName(GameObject target, string layerName)
        {
            int layer = LayerMask.NameToLayer(layerName);
            if (layer == -1)
            {
                Debug.LogWarning($"Layer '{layerName}' no existe. Se omite la asignación de capa para '{target.name}'.");
                return;
            }

            target.layer = layer;
        }

        // Asigna un tag a un objeto por nombre, avisando si no existe.
        private static void SetTagByName(GameObject target, string tagName)
        {
            if (System.Array.IndexOf(InternalEditorUtility.tags, tagName) < 0)
            {
                Debug.LogWarning($"Tag '{tagName}' no existe. Se omite la asignación de tag para '{target.name}'.");
                return;
            }

            target.tag = tagName;
        }
    }
}
