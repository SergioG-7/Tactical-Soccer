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
    /// <summary>
    /// Editor-only utility that builds a disposable test scene (pitch, goals,
    /// teams, ball, managers) via the GameObject API rather than hand-editing
    /// scene YAML, so every created object stays Undo-safe.
    ///
    /// Textures, materials and physics materials are generated procedurally and
    /// written to <see cref="GeneratedFolder"/> as real assets. Keeping them on
    /// disk is what makes them survive a domain reload or a scene reopen; purely
    /// in-memory instances would come back as null (pink) references instead.
    /// </summary>
    public static class TestEnvironmentGenerator
    {
        private const string GeneratedFolder = "Assets/Generated";

        // Hand-authored, unlike everything in GeneratedFolder: sound is the one
        // thing here that cannot be built from primitives, so these are real
        // recordings dropped in by hand and merely looked up by name.
        private const string AudioFolder = "Assets/Audio";

        private static readonly Vector3 PitchScale = new Vector3(3f, 1f, 5f);
        private const float PlayerSpawnHeight = 1f;

        private const float PixelsPerUnit = 100f;

        // Floating tag over each player. The canvas is authored at UI scale and
        // shrunk into the world, so a 120 x 90 rect at 0.02 ends up 2.4 x 1.8
        // units — big enough to read over a 1-unit capsule from a camera that
        // sees 30 units of pitch, small enough that fourteen of them do not
        // paper over the match.
        private const float PlayerLabelHeight = 2.5f;
        private const float PlayerLabelScale = 0.02f;

        /// <summary>
        /// Share of a full tank at which a player counts as blown. Expressed as
        /// a share rather than as a flat number so it cannot drift out of step
        /// with maxStamina again — it was left at a flat 20 when the tank grew
        /// to 300, which quietly turned "exhausted" into a 6.7% edge case.
        /// </summary>
        private const float ExhaustedTankShare = 0.2f;
        private static readonly Vector2 PlayerLabelCanvasSize = new Vector2(120f, 90f);
        private static readonly Vector2 PlayerLabelRoleSize = new Vector2(170f, 45f);
        private static readonly Vector2 PlayerLabelRoleOffset = new Vector2(0f, 22f);

        // Down from 38: the tag now reads "10 - FW" rather than "FW", and seven
        // characters at the old size ran off both ends of the rect.
        private const int PlayerLabelRoleFontSize = 30;
        private static readonly Vector2 PlayerLabelBarSize = new Vector2(80f, 14f);
        private static readonly Vector2 PlayerLabelBarOffset = new Vector2(0f, -14f);
        private static readonly Color PlayerLabelBarBackground = new Color(0f, 0f, 0f, 0.65f);

        /// <summary>
        /// Seven a side: the keeper plus the six of the default shape. Read from
        /// the shared table rather than kept here, so choosing 2-2-2 in the
        /// formation menu puts the squad back exactly where it was spawned
        /// instead of somewhere almost identical.
        /// </summary>
        private const FormationType DefaultFormation = FormationType.Balanced_2_2_2;

        // Far enough off the goal line that the ball socket clears the goal
        // trigger entirely. At z=23 the socket landed on 23.5 — exactly the
        // trigger's near face — so the instant the keeper released a clearance
        // the ball turned free INSIDE the goal and scored an own goal.
        private const float GoalkeeperZ = 21.5f;
        private const float GoalkeeperLateralRange = 3.5f;
        private static readonly Vector3 GoalkeeperWingspan = new Vector3(4f, 1.2f, 1f);

        // The capsule's centre sits at y=1, so a 1.2-tall box centred on it would
        // float with its base at 0.4 and let ground shots roll underneath. Drop it
        // so the box rests on the turf and the gap is only above the keeper.
        private static readonly Vector3 GoalkeeperWingspanCenter = new Vector3(0f, -0.4f, 0f);

        private static readonly Vector3 BallScale = new Vector3(0.5f, 0.5f, 0.5f);
        private const float BallSpawnHeight = 0.25f;
        private const float BallBounciness = 0.6f;
        private const float BallLinearDamping = 0.4f;
        private const float BallAngularDamping = 0.8f;

        // The match rig: PERSPECTIVE, high, well behind the play and tilted
        // forward. Not the old orthographic bird's-eye — a top-down parallel
        // projection has no depth at all, so no amount of moving the camera ever
        // made anything look nearer, and every "zoom" had to be faked.
        private const float CameraHeight = 22f;
        private const float CameraDistanceBehind = 18f;
        private const float CameraPitchAngle = 55f;
        private const float CameraFieldOfView = 50f;
        private const float CameraSmoothTime = 0.3f;

        // Deliberate overshoot past the strict "never show the void" limit, so
        // the goals sit further from the screen edge when play reaches them.
        // Larger than it was: an angled camera cannot get behind the near goal
        // the way one hanging straight overhead could.
        private const float CameraExtraGoalPan = 10f;

        // Over the attacker's shoulder, looking down the line at the defender.
        // On a perspective rig the five metres ARE the zoom, so the lens is left
        // exactly where the match view has it — changing focal length on top of
        // a five-metre dolly reads as a lurch, not as drama.
        private const float ClashCameraBackDistance = 5f;
        private const float ClashCameraHeight = 2.5f;
        private const float ClashCameraFieldOfView = 50f;

        // Chasing a struck ball: behind it along its own line of flight, riding
        // above. Not a fixed world offset — that put the camera in FRONT of any
        // shot heading south, so half the goals were watched with the ball
        // flying into the lens.
        private const float BallFlightCameraBackDistance = 6f;
        private const float BallFlightCameraHeight = 4f;
        private const float BallFlightCameraFieldOfView = 50f;
        private static readonly Vector3 ManagerParkingSpot = new Vector3(-25f, 0f, 0f);

        // A Unity Plane is 10x10 units at scale 1, so the pitch spans 30 x 50.
        private const float PitchHalfWidth = 15f;
        private const float PitchHalfLength = 25f;

        private const float GoalWidth = 7f;
        private const float GoalHeight = 2.5f;
        private const float GoalPostRadius = 0.1f;
        private const float GoalLineInset = 1f;
        private const float GoalTriggerDepth = 1f;

        private const int PitchTextureWidth = 512;

        private const TeamId HumanTeam = TeamId.Blue;

        // Trail settings. Short and tapering, so it reads as pace rather than
        // painting a permanent line across the pitch.
        private const float BallTrailTime = 0.3f;
        private const float BallTrailStartWidth = 0.3f;

        /// <summary>
        /// Every root this tool creates. Wiping them first is what makes
        /// regenerating idempotent: without it a second run stacked a whole
        /// extra set of teams on top of the first, and the stale copies were
        /// indistinguishable from the new ones in the hierarchy.
        /// </summary>
        private static readonly string[] GeneratedRootNames =
        {
            "GameManager", "Pitch", "Surroundings", "Goal North", "Goal South",
            "Team Blue", "Team Red", "Ball", "InputManager",
            "UI Canvas", "EventSystem"
        };

        // Everything outside the touchline. The clash camera drops to head
        // height five metres behind the attacker, so a duel near a corner points
        // the lens straight off the pitch. This is what it finds there instead
        // of empty grey.
        private static readonly Vector3 SurroundScale = new Vector3(9f, 1f, 11f);
        private const float SurroundY = -0.03f;

        // Mirrors TacticalSoccer.Core.PitchBounds: the flags mark the corners of
        // the painted lines, not the corners of the turf.
        private const float PitchBoundsSideX = 13.5f;
        private const float PitchBoundsGoalZ = 23.5f;

        // Stadium. Dugouts just outside the run-off, stands stepping up behind
        // them. Both sit beyond the player limit (±14.5), so nothing in here can
        // ever be walked into.
        private const float DugoutX = PitchBoundsSideX + 2f;
        private const float DugoutLength = 8f;

        // Height of the underside of the dugout roof.
        //
        // A substitute is a capsule 2 units tall standing at y = 1, so its crown
        // is already at 2.0 — the old 1.775 clearance had their heads through the
        // roof before the character kit added hair and hats on top of that. Set
        // from the tallest thing that can stand under it plus room for a hat,
        // rather than from what looked right with nobody inside.
        // 2.6 cleared the tallest substitute measured by 0.096, which is not
        // clearance, it is luck: the hair is drawn at random per player and a
        // different roll is a different height. 2.9 leaves ~0.4.
        private const float DugoutRoofY = 2.9f;
        private const float DugoutRoofThickness = 0.15f;
        private const float StandX = PitchBoundsSideX + 5f;
        private const int StandStepCount = 3;
        private const float StandStepRise = 0.7f;
        private const float StandStepDepth = 2.2f;
        private const float StandLength = 52f;

        // The bench. Three substitutes a side, stood inside their own dugout
        // rather than parked off the map: they are visible from the touchline,
        // which is what makes bringing one on read as a substitution instead of
        // as a player appearing out of nowhere. Every squad system filters them
        // out through TeamMember.isStarter, so they take no part until swapped.
        private const int SubstituteCount = 3;
        private const float SubstituteSpacing = 2.5f;

        // Shirt numbers, in the order every football squad has used since they
        // were invented: 1 keeps goal, 2-7 start, 8-10 wait.
        private const int GoalkeeperJerseyNumber = 1;
        private const int FirstStarterJerseyNumber = 2;

        // Crowd. Spacing and rows are what decide the head count: 3 x 29 a side,
        // 174 in all. Every spectator is one Update and one small mesh, and they
        // share two materials so the batcher can fold them together.
        private const float SpectatorSpacing = 1.8f;
        private static readonly Vector3 SpectatorScale = new Vector3(0.45f, 0.45f, 0.45f);

        // A cylinder is 2 units tall at scale 1, so half the height is the scale.
        private const float CornerFlagHeight = 2f;
        private const float CornerFlagRadius = 0.1f;
        private const float CornerPennantSize = 1f;

        // Netting. The back panel stops short of PitchBounds.BehindGoalZ (25.5),
        // where the ball would count as gone: the whole point is that a goal
        // stays in the net instead of sailing out the back and restarting play.
        private const float GoalNetDepth = 1.2f;
        private const float GoalNetThickness = 0.08f;

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

            // Not optional wiring: without a controller there is no button to
            // dismiss the duel, and the match stays frozen at timeScale 0.
            ClashUIController clashUI = CreateClashUI();
            gameManager.GetComponent<ClashManager>().uiController = clashUI;

            // The scoreboard hangs off the same canvas, but outside the duel
            // panel so it stays on screen the whole match.
            CreateScoreUI(uiHudArea, gameManager.GetComponent<ScoreManager>());

            // Order here IS the draw order: within a canvas the last sibling
            // wins. The announcer must sit over the duel banner, full time over
            // the announcer, the team sheet over that, and the title screen over
            // the lot — it is the one the player sees first.
            CreateAnnouncerUI(uiHudArea);

            // The interval first, then the board it hands over to: within a
            // canvas the last sibling draws on top, and the substitutions screen
            // is opened FROM the team talk, so it has to cover it.
            // The momentum bars belong under the modal screens: they are HUD, and
            // a menu that opened underneath them would be read through two glowing
            // bars.
            CreateTensionUI(uiHudArea);

            CreateHalftimeUI(uiCanvasRoot);
            CreateSubstitutionUI(uiCanvasRoot);

            // Above the interval, below full time: a penalty can be given at any
            // moment of a live match, so it has to cover the HUD — but the final
            // whistle still covers it.
            CreatePenaltyUI(uiCanvasRoot);

            CreateMatchOverUI(uiCanvasRoot);

            FormationUIController formationUI = CreateFormationUI(uiCanvasRoot);
            MatchConfigUIController configUI = CreateMatchConfigUI(uiCanvasRoot, formationUI);

            CreateTitleScreenUI(uiCanvasRoot, configUI, formationUI);

            // Last of all, so it draws over every other screen: a developer menu
            // that opened underneath the title would be unreachable exactly when
            // somebody wanted it.
            CreateDebugMenuUI(uiCanvasRoot);

            // Dead last, so it draws over everything: it is opened from the
            // title AND from the developer menu, so it has to cover both — and
            // the developer menu is itself already on top of the title.
            CreateAudioSettingsUI(uiCanvasRoot);

            // Last of all, above even the audio options: it is opened from the
            // squad board, which is itself opened from the team sheet, so it has
            // to cover the deepest stack of menus in the game.
            CreatePlayerEditUI(uiCanvasRoot);

            CreateEventSystem();

            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Removes the previous run's objects. Only roots this tool owns by name
        /// are touched — the camera and the scene lighting are left alone, since
        /// the generator configures those rather than creating them. Destruction
        /// goes through Undo so the whole regeneration stays one Ctrl+Z.
        /// </summary>
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

            // Momentum. On the manager rather than on the HUD: the bar is read by
            // the duel maths and by the movement code, and neither of those
            // should stop working if somebody deletes the UI.
            gameManager.AddComponent<TensionManager>();

            // The impact material is written as a real asset rather than built
            // at runtime: an in-memory material would survive neither a domain
            // reload nor a scene reopen, and would come back pink.
            VFXManager vfx = gameManager.AddComponent<VFXManager>();
            vfx.ConfigureImpactMaterial(GetOrCreateImpactMaterial());

            // Duel readouts. Given the same font as the rest of the UI so the
            // numbers over a player's head and the numbers in the banner are
            // visibly the same system.
            FloatingTextManager floatingText = gameManager.AddComponent<FloatingTextManager>();
            floatingText.ConfigureFont(GetUIFont());

            ConfigureAudio(gameManager.AddComponent<AudioManager>());

            // The tournament's counter lives in PlayerPrefs, so this component
            // holds no state worth losing — but it is the one manager here that
            // marks itself DontDestroyOnLoad, because a run has to outlive a
            // match.
            gameManager.AddComponent<TournamentManager>();

            // Puts the squad edits back on the players. A component and not a
            // static hook because it has to run AFTER every TeamMember's Awake,
            // which is where stamina and the initial-state snapshot are taken —
            // Start is exactly that moment.
            gameManager.AddComponent<SquadPersistence>();

            return gameManager;
        }

        /// <summary>
        /// Hands the audio its clips from <see cref="AudioFolder"/>.
        ///
        /// Written explicitly on every pass rather than left to the Inspector,
        /// like every other serialised field the generator owns: a value already
        /// serialised onto a component in the scene survives a change to the
        /// code, so anything the generator does not rewrite silently keeps
        /// whatever the last generation put there.
        ///
        /// Missing files are not an error. The match specifies more sounds than
        /// the project has recordings for, every play path checks for null, and
        /// the summary below says exactly which ones are still silent so nobody
        /// has to guess whether a sound is missing or broken.
        /// </summary>
        private static void ConfigureAudio(AudioManager audio)
        {
            AudioClip shortWhistle = LoadAudioClip("whistle-short.aiff");
            AudioClip longWhistle = LoadAudioClip("whistle-long.wav");
            AudioClip fullTimeWhistle = LoadAudioClip("whistle-full-time.wav");
            AudioClip kick = LoadAudioClip("kick-ball.wav");
            AudioClip tension = LoadAudioClip("tension-max.wav");
            AudioClip crowd = LoadAudioClip("crowd-ambience.wav");
            AudioClip click = LoadAudioClip("click.mp3");

            // Three of the ten sounds share a recording with another, because the
            // project has six files for ten moments. Each reuse is a sound that
            // genuinely fits twice, not a placeholder:
            //
            //  - the foul whistle IS a short whistle, which is what a referee
            //    blows for one;
            //  - duel contact is a thud, and the only thud here is a struck
            //    ball;
            //  - the goal roar is the 10 s crowd bed fired a second time on top
            //    of itself, which swells rather than replaces — the crowd
            //    getting louder is exactly what a goal sounds like.
            //
            // Only the ball hitting the net has nothing to borrow: every other
            // clip here would read as a second kick a beat after the first.
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

        private static AudioClip LoadAudioClip(string fileName)
        {
            return AssetDatabase.LoadAssetAtPath<AudioClip>($"{AudioFolder}/{fileName}");
        }

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

        /// <summary>
        /// Dressing outside the touchline: a wider apron of darker grass, two
        /// dugouts and corner flags. Everything here is scenery — colliders are
        /// stripped and it sits on the default layer, so it can never catch a
        /// route raycast or deflect the ball.
        /// </summary>
        private static void CreateSurroundings()
        {
            GameObject root = new GameObject("Surroundings");
            Undo.RegisterCreatedObjectUndo(root, "Create Surroundings");

            GameObject apron = GameObject.CreatePrimitive(PrimitiveType.Plane);
            Undo.RegisterCreatedObjectUndo(apron, "Create Pitch Apron");

            apron.name = "Outer Grass";
            apron.transform.SetParent(root.transform, false);
            apron.transform.localScale = SurroundScale;

            // Just under the pitch, so the two planes never z-fight.
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

        /// <summary>
        /// Dugouts and stands down both touchlines, with a crowd in them.
        ///
        /// Parented under Surroundings rather than given a root of its own, so
        /// the existing wipe already clears it and regenerating stays a single
        /// Ctrl+Z. Everything is scenery: colliders stripped, default layer,
        /// beyond the player limit.
        /// </summary>
        private static void CreateStadium(GameObject root)
        {
            Material shelterMaterial = GetOrCreateMaterial("DugoutMaterial.mat", new Color(0.30f, 0.31f, 0.34f), null);
            Material benchMaterial = GetOrCreateMaterial("BenchMaterial.mat", new Color(0.78f, 0.78f, 0.80f), null);
            Material blueMaterial = GetOrCreateMaterial("TeamBlueMaterial.mat", Color.blue, null);
            Material redMaterial = GetOrCreateMaterial("TeamRedMaterial.mat", Color.red, null);
            Material concreteMaterial = GetOrCreateMaterial("StandMaterial.mat", new Color(0.42f, 0.43f, 0.46f), null);

            // One dugout per touchline, in the colours of the side it belongs to.
            // Real grounds put both on the same side; opposite sides is what the
            // brief asks for and it reads more clearly from directly overhead.
            CreateDugout(root, "Dugout Blue", new Vector3(-DugoutX, 0f, 0f), shelterMaterial, benchMaterial, blueMaterial);
            CreateDugout(root, "Dugout Red", new Vector3(DugoutX, 0f, 0f), shelterMaterial, benchMaterial, redMaterial);

            CreateStand(root, "Stand West", -1f, concreteMaterial, blueMaterial, redMaterial);
            CreateStand(root, "Stand East", 1f, concreteMaterial, blueMaterial, redMaterial);
        }

        /// <summary>
        /// A roofed shelter with a bench inside and a coloured stripe along the
        /// roof edge, so the two benches are told apart from the air.
        /// </summary>
        private static void CreateDugout(GameObject parent, string dugoutName, Vector3 position,
            Material shelterMaterial, Material benchMaterial, Material teamMaterial)
        {
            GameObject dugout = new GameObject(dugoutName);
            Undo.RegisterCreatedObjectUndo(dugout, $"Create {dugoutName}");

            dugout.transform.SetParent(parent.transform, false);
            dugout.transform.position = position;

            // Back wall, roof and bench rather than one solid block: from the
            // tactical camera a closed box reads as a crate, and the open front
            // is what makes it read as a dugout.
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

        /// <summary>
        /// Stepped terracing down one touchline, with a spectator every
        /// <see cref="SpectatorSpacing"/> units on each step.
        /// </summary>
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
                // Each step is taller and further out than the last, so the rows
                // behind can be seen over the rows in front.
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
                        // Alternating in irregular clumps rather than strictly
                        // one-and-one, so the crowd does not read as a barcode.
                        ((seat + step) / 2) % 2 == 0 ? blueMaterial : redMaterial);
                }
            }
        }

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

        /// <summary>
        /// A thin white pole with a pennant on top. The pennant is a Quad laid
        /// flat rather than left upright: this camera looks straight down, and a
        /// vertical flag is a one-pixel line from up there.
        /// </summary>
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

            // Half the height: a Unity cylinder is already 2 units tall at scale 1.
            pole.transform.localScale = new Vector3(CornerFlagRadius, CornerFlagHeight * 0.5f, CornerFlagRadius);

            ApplyMaterial(pole, poleMaterial);
            StripCollider(pole);

            GameObject pennant = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Undo.RegisterCreatedObjectUndo(pennant, $"Create {flagName} Pennant");

            pennant.name = "Pennant";
            pennant.transform.SetParent(flag.transform, false);
            pennant.transform.localPosition = new Vector3(0f, CornerFlagHeight, 0f);
            pennant.transform.localScale = Vector3.one * CornerPennantSize;

            // A Quad faces -Z, so a 90 degree turn about X points it at the sky.
            // The 45 degree yaw stops the four of them reading as squares aligned
            // with the touchlines.
            pennant.transform.localRotation = Quaternion.Euler(90f, 45f, 0f);

            ApplyMaterial(pennant, pennantMaterial);
            StripCollider(pennant);
        }

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

        private static void StripCollider(GameObject target)
        {
            if (target.TryGetComponent(out Collider collider))
            {
                Undo.DestroyObjectImmediate(collider);
            }
        }

        private static void CreateGoals()
        {
            float goalZ = PitchHalfLength - GoalLineInset;

            // Blue starts south and attacks north, so the north goal is the one
            // Blue scores in, and vice versa.
            CreateGoal("Goal North", new Vector3(0f, 0f, goalZ), Quaternion.identity, ScoreManager.BlueTeamId);
            CreateGoal("Goal South", new Vector3(0f, 0f, -goalZ), Quaternion.Euler(0f, 180f, 0f), ScoreManager.RedTeamId);
        }

        /// <summary>
        /// Builds a goal out of three cylinders (two posts plus a crossbar) and
        /// an invisible trigger box between them. The cylinders' colliders are
        /// destroyed on purpose: the frame is decorative for now and must not
        /// block player movement or deflect the ball before it reaches the
        /// trigger.
        /// </summary>
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

            // A cylinder's axis runs along Y, so the crossbar is rotated onto X.
            CreateGoalPart(goalRoot, goalMaterial, "Crossbar",
                new Vector3(0f, GoalHeight, 0f),
                Quaternion.Euler(0f, 0f, 90f),
                new Vector3(GoalPostRadius, halfWidth, GoalPostRadius));

            CreateGoalNet(goalRoot, halfWidth);
            CreateGoalTrigger(goalRoot, teamToScore);
        }

        /// <summary>
        /// Three flattened cubes closing the sides and back of the goal.
        ///
        /// Unlike the frame, these keep their colliders and stay on the default
        /// layer, so the ball — also on the default layer — actually hits them.
        /// That is the entire point: a shot used to cross the line and carry on
        /// into the void behind the goal, which read as the ball vanishing.
        ///
        /// The depth is what makes it work. Local z 1.2 puts the back panel at
        /// world 25.2, just inside PitchBounds.BehindGoalZ (25.5) where the ball
        /// would be called out of play — so the net catches it a hair before the
        /// rules would have taken it away.
        /// </summary>
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

            // Collider deliberately KEPT, and left on the default layer so it
            // meets the ball. It is never tagged "Goal" and never put on the Goal
            // layer, so neither the shooting tap nor the route raycast can catch
            // it — only the physics does.
        }

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

        private static void CreateGoalTrigger(GameObject goalRoot, int teamToScore)
        {
            GameObject trigger = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Undo.RegisterCreatedObjectUndo(trigger, "Create Goal Trigger");

            trigger.name = "Goal Trigger";
            trigger.transform.SetParent(goalRoot.transform, false);
            trigger.transform.localPosition = new Vector3(0f, GoalHeight * 0.5f, 0f);
            trigger.transform.localScale = new Vector3(GoalWidth, GoalHeight, GoalTriggerDepth);

            // Invisible: the trigger only needs its collider.
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

            // Tag identifies it to the tap handler; the dedicated layer keeps it
            // out of the ground raycast, which would otherwise snap drawn route
            // points onto the roof of this 2.5-unit-tall box.
            SetTagByName(trigger, "Goal");
            SetLayerByName(trigger, "Goal");

            GoalDetector detector = trigger.AddComponent<GoalDetector>();
            detector.ConfigureTeam(teamToScore);
        }

        private static void CreateTeam(string teamName, string layerName, Color color, TeamId teamId, bool southSide)
        {
            GameObject teamRoot = new GameObject(teamName);
            Undo.RegisterCreatedObjectUndo(teamRoot, $"Create {teamName}");

            // -1 for the side that starts (and defends) south, +1 for north.
            float side = southSide ? -1f : 1f;
            Material teamMaterial = GetOrCreateMaterial($"{teamName.Replace(" ", string.Empty)}Material.mat", color, null);

            // Deliberately lopsided: a striker beaten on Block still loses, and a
            // defender caught by Power still loses, so the action choice matters
            // more than the raw numbers. The midfielder sits between the two and
            // is good at nothing in particular, which is the point of the role.
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
                // Numbered within their own line, so the hierarchy reads as a
                // team sheet ("Team Blue Defender 2") rather than as six players
                // whose position you have to click to find out.
                roleCount = slot.Role == previousRole ? roleCount + 1 : 1;
                previousRole = slot.Role;

                PlayerStatsSO stats = ResolveStats(slot.Role, strikerStats, midfielderStats, defenderStats);

                CreatePlayer(teamRoot, $"{teamName} {slot.Role} {roleCount}",
                    new Vector3(slot.X, PlayerSpawnHeight, side * slot.OwnHalfZ),
                    layerName, teamId, slot.Role, teamMaterial, stats,
                    jerseyNumber, isStarter: true);

                jerseyNumber++;
            }

            // Keepers need their own block: shot duels read `goalkeeping`, and a
            // keeper wearing the outfield defender's 25 would be beaten by an
            // 80-shoot striker almost every time.
            PlayerStatsSO goalkeeperStats = GetOrCreateStats("GoalkeeperStats.asset",
                dribble: 10, power: 40, shoot: 10, tackle: 60, block: 70, goalkeeping: 85);

            CreateGoalkeeper(teamRoot, teamName, side, layerName, teamId, goalkeeperStats, teamMaterial);

            CreateSubstitutes(teamRoot, teamName, layerName, teamId, teamMaterial,
                jerseyNumber, strikerStats, midfielderStats, defenderStats);
        }

        /// <summary>
        /// The three on the bench, stood in their own dugout.
        ///
        /// One of each line, so the substitution board is a real choice rather
        /// than three interchangeable men: whoever is blown, there is somebody
        /// on the bench who plays where they do.
        ///
        /// They are built through exactly the same path as a starter — same
        /// components, same label, same ball socket — and differ only in
        /// isStarter. That flag is what every squad scan filters on, so a
        /// substitute needs no special case anywhere else in the game.
        /// </summary>
        private static void CreateSubstitutes(GameObject teamRoot, string teamName, string layerName,
            TeamId teamId, Material teamMaterial, int firstJerseyNumber,
            PlayerStatsSO strikerStats, PlayerStatsSO midfielderStats, PlayerStatsSO defenderStats)
        {
            // Same touchline as the dugout this side owns: CreateStadium puts
            // Blue's to the west and Red's to the east.
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

        /// <summary>
        /// Spreads the four elements across a squad by shirt number, with the
        /// two sides offset from each other.
        ///
        /// The offset is the point. Handing both teams the same element for the
        /// same shirt would mean the two players most likely to end up marking
        /// each other — the two number fours — could never have an affinity
        /// between them, and half the pitch would be elementally inert.
        /// </summary>
        private static Element ResolveElement(TeamId teamId, int jerseyNumber)
        {
            int elementCount = System.Enum.GetValues(typeof(Element)).Length;
            int offset = teamId == TeamId.Blue ? 0 : 1;

            return (Element)(((jerseyNumber + offset) % elementCount + elementCount) % elementCount);
        }

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

            // Written explicitly rather than left to the field default: the
            // component is serialized into the scene, so a zero here would show
            // an empty tank in the inspector and an empty bar before play.
            teamMember.currentStamina = teamMember.maxStamina;

            // Also written explicitly, and for a sharper reason: changing a
            // field's default in code does NOT touch a component already
            // serialized in the scene. The threshold moved from 20 to a fifth of
            // the tank, and without this line every player generated before that
            // change would quietly keep the old value.
            teamMember.exhaustedThreshold = teamMember.maxStamina * ExhaustedTankShare;

            player.AddComponent<PlayerRoute>();

            // The glow that marks a side in the zone. Given a real material
            // asset rather than one built at runtime: an in-memory material
            // survives neither a domain reload nor a scene reopen, and comes
            // back pink.
            TensionAura aura = player.AddComponent<TensionAura>();
            aura.ConfigureMaterial(GetOrCreateAuraMaterial());

            // Off-the-ball drift. Added to every player, including keepers: the
            // component switches itself off for those, since a keeper has his
            // own tracking loop along the goal line.
            player.AddComponent<TacticalPositioning>();

            // PlayerRoute pulls in a LineRenderer via RequireComponent, and a
            // LineRenderer with no material renders as broken-shader magenta.
            ApplyRouteLineMaterial(player);

            SetupBallInteraction(player);

            // Covers keepers too: CreateGoalkeeper builds its player through
            // here and only flips isGoalkeeper afterwards, and the label reads
            // the role — already Goalkeeper by then — rather than that flag.
            CreatePlayerLabel(player, teamMember);

            return player;
        }

        /// <summary>
        /// The tag floating over a player: role on top, stamina bar underneath.
        ///
        /// A world-space canvas parented to the player, so it follows them with
        /// no per-frame position maths. No GraphicRaycaster is added on purpose:
        /// these are read-only tags, and fourteen extra raycast targets would
        /// join every UI raycast the duel screen makes for nothing.
        /// </summary>
        // The character kit from "Football Essentials 3D". Despite the name it is
        // not a humanoid model: it is a CAPSULE with interchangeable faces, hair
        // and props — which is why it drops onto this game's players rather than
        // replacing them. See AttachPlayerSkin.
        private const string CharacterKitPrefab =
            "Assets/Lightning Poly/Football Essentials 3D/Prefabs/Demo_Capsule.prefab";
        private const string CharacterKitTexture =
            "Assets/Lightning Poly/Football Essentials 3D/Material/Colors.png";

        // Measured, not guessed: the kit's body is 0.2768 units tall as authored
        // and this game's players are Unity capsules 2 units tall.
        private const float CharacterKitScale = 7.224f;

        // The kit's face is modelled on +Z. This game's players never rotate and
        // the camera sits on -Z, so left alone every player would be facing away
        // from it — the same mistake that once hid the ball behind the capsule.
        private const float CharacterKitYaw = 180f;

        private static readonly string[] CharacterKitFeatureGroups = { "Eyes", "Mouths", "Hairs" };

        // Worn by some players and not others, so a side does not look uniformed.
        private const string CharacterKitGlasses = "Glasses";
        private const string CharacterKitDecorations = "Decorations";

        /// <summary>
        /// Dresses a player in a random face from the character kit.
        ///
        /// The kit's own body is hidden and this game's capsule kept, which is
        /// what preserves everything already built on it: the team colour, the
        /// kit chosen on the configuration screen, the keeper's own strip, the
        /// stun blink and the selection ring all read the capsule's renderer and
        /// none of them has to learn about any of this.
        ///
        /// The choice is seeded from the shirt number rather than left to
        /// Random, so regenerating the scene does not reshuffle everybody's face
        /// — and so the two number sevens do not come out identical.
        /// </summary>
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

            // The capsule's origin is its CENTRE, so the kit — which is modelled
            // standing on y = 0 — has to be dropped a full half-height.
            skinRoot.transform.localPosition = new Vector3(0f, -1f, 0f);
            skinRoot.transform.localRotation = Quaternion.Euler(0f, CharacterKitYaw, 0f);
            skinRoot.transform.localScale = Vector3.one * CharacterKitScale;

            // Instantiated from the prefab ROOT and then cut down, rather than
            // from the child directly: InstantiatePrefab only takes an asset
            // root, and handing it a child returns null.
            GameObject kitInstance = (GameObject)PrefabUtility.InstantiatePrefab(kit);

            if (kitInstance == null)
            {
                Object.DestroyImmediate(skinRoot);
                return;
            }

            // Unpacked so the variants can be switched off individually: a
            // prefab instance would keep reverting to the demo's "everything on
            // at once" state.
            PrefabUtility.UnpackPrefabInstance(kitInstance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

            GameObject body = kitInstance.transform.GetChild(0).gameObject;

            // worldPositionStays: false — the kit's body carries a local offset
            // and a 7.52 scale that are meaningful in its parent's space, and
            // preserving its world pose instead would flatten both.
            body.transform.SetParent(skinRoot.transform, false);

            // Everything else the demo prefab carried — its rigidbody, its own
            // controller script, its collider — goes with the husk.
            Object.DestroyImmediate(kitInstance);

            // The kit's own body is the one thing NOT wanted: this game's capsule
            // is already there, and it is the one carrying the team colour.
            if (body.TryGetComponent(out MeshRenderer bodyRenderer))
            {
                bodyRenderer.enabled = false;
            }

            System.Random dice = new System.Random(((int)member.team * 1000) + member.jerseyNumber);

            foreach (string group in CharacterKitFeatureGroups)
            {
                KeepOneVariant(body.transform.Find(group), dice, alwaysWorn: true);
            }

            // Roughly a third in glasses and a third carrying a prop, so a team
            // reads as eleven people rather than one person eleven times.
            KeepOneVariant(body.transform.Find(CharacterKitGlasses), dice, alwaysWorn: false);
            KeepOneVariant(body.transform.Find(CharacterKitDecorations), dice, alwaysWorn: false);

            ApplyCharacterKitMaterial(skinRoot);

            // Scenery. A collider here would deflect the ball and trip the duel
            // triggers, and the kit ships one on the body.
            foreach (Collider collider in skinRoot.GetComponentsInChildren<Collider>(true))
            {
                Object.DestroyImmediate(collider);
            }
        }

        /// <summary>
        /// Leaves exactly one child of <paramref name="group"/> enabled, or none
        /// at all when the group is optional and the roll says so.
        /// </summary>
        private static void KeepOneVariant(Transform group, System.Random dice, bool alwaysWorn)
        {
            if (group == null || group.childCount == 0)
            {
                return;
            }

            // The demo prefab ships with every variant switched on at once,
            // which is what it is for — so the default state has to be cleared
            // rather than assumed.
            int chosen = alwaysWorn || dice.Next(0, 3) == 0 ? dice.Next(0, group.childCount) : -1;

            for (int i = 0; i < group.childCount; i++)
            {
                group.GetChild(i).gameObject.SetActive(i == chosen);
            }
        }

        /// <summary>
        /// Repoints every part of the kit at a URP material carrying the same
        /// palette atlas.
        ///
        /// Not optional: the package ships its material on the built-in Standard
        /// shader, and URP cannot draw it — every face in the game would render
        /// magenta. The atlas itself is reused untouched, so the faces keep
        /// their authored colours.
        /// </summary>
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

        private static void CreatePlayerLabel(GameObject player, TeamMember member)
        {
            GameObject labelObject = new GameObject("Player Label", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(labelObject, $"Create {player.name} Label");

            labelObject.transform.SetParent(player.transform, false);

            RectTransform rect = (RectTransform)labelObject.transform;
            rect.sizeDelta = PlayerLabelCanvasSize;
            rect.localPosition = new Vector3(0f, PlayerLabelHeight, 0f);
            rect.localScale = Vector3.one * PlayerLabelScale;

            // Laid flat to face the overhead rig. PlayerLabelUI re-aims it every
            // frame in play mode; this is what makes it read correctly in the
            // Scene view, where LateUpdate never runs.
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

            // Not the plain UI font: this is the one tag that has to draw a
            // kanji, and the built-in one has no CJK glyphs to draw it with.
            text.font = GetPlayerTagFont();
            text.fontSize = PlayerLabelRoleFontSize;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.supportRichText = true;
            text.text = "--";

            // A floating tag has nothing to be clipped against, so overflowing
            // is harmless — and it is what stops a two-digit shirt number from
            // silently swallowing the role that follows it.
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;

            // The pitch behind it is light green and the players are saturated;
            // an outline keeps two letters legible over either.
            Outline outline = textObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
            outline.effectDistance = new Vector2(2f, -2f);

            return text;
        }

        /// <summary>
        /// A dark trough with a filled bar inside it. The fill is returned, not
        /// the trough: that is the one PlayerLabelUI drives.
        /// </summary>
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

            // Drains from the right, so a bar shrinking towards the left edge
            // reads the way every energy bar the player has ever seen does.
            fill.fillOrigin = (int)Image.OriginHorizontal.Left;
            fill.fillAmount = 1f;

            return fill;
        }

        /// <summary>
        /// Same rig as a field player plus the keeper brain, and a second, wider
        /// trigger box. The box is added rather than scaling the capsule so the
        /// keeper's own body stays player-sized while its reach covers the goal
        /// mouth.
        ///
        /// He wears the same shirt as the rest of his side. The yellow kit he
        /// used to get made him easy to pick out of a crowded box, but it was a
        /// FIXED yellow — and a tournament round can dress the opposition in
        /// orange or gold, at which point the keeper looks like one of them.
        /// </summary>
        private static void CreateGoalkeeper(GameObject teamRoot, string teamName, float side,
            string layerName, TeamId teamId, PlayerStatsSO stats, Material teamMaterial)
        {
            GameObject keeper = CreatePlayer(teamRoot, $"{teamName} Goalkeeper",
                new Vector3(0f, PlayerSpawnHeight, side * GoalkeeperZ),
                layerName, teamId, PlayerRole.Goalkeeper, teamMaterial, stats,
                GoalkeeperJerseyNumber, isStarter: true);

            // Flags the role on TeamMember itself so gameplay code can find a
            // keeper without reaching into the AI layer for GoalkeeperAI.
            keeper.GetComponent<TeamMember>().isGoalkeeper = true;

            BoxCollider wingspan = keeper.AddComponent<BoxCollider>();
            wingspan.isTrigger = true;
            wingspan.center = GoalkeeperWingspanCenter;
            wingspan.size = GoalkeeperWingspan;

            GoalkeeperAI keeperAI = keeper.AddComponent<GoalkeeperAI>();
            keeperAI.maxLateralMovement = GoalkeeperLateralRange;

            // Only the AI's keeper clears on its own. The human's keeper has a
            // human to choose the pass, and hoofing it blind up the middle just
            // fed the opposition every time it made a save.
            keeperAI.autoClearance = teamId != HumanTeam;
        }

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

            // On the CAMERA side of the player (-Z) and down at grass level.
            // Players never rotate, so this offset is fixed in world space: with
            // the ball on +Z, as it used to be, the angled camera looked at the
            // carrier's back and the ball spent every possession hidden behind
            // the capsule. Y puts it on the turf rather than floating at hip
            // height, which is also what lines its new drop shadow up under it.
            ballSocket.transform.localPosition = new Vector3(0f, -0.75f, -0.55f);

            PlayerBallHandler ballHandler = player.AddComponent<PlayerBallHandler>();
            ballHandler.AssignBallSocket(ballSocket.transform);
        }

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

            // Without damping a kicked ball keeps its speed indefinitely: it
            // crosses the whole pitch, rolls off the edge and falls forever.
            Rigidbody rb = ball.AddComponent<Rigidbody>();
            rb.linearDamping = BallLinearDamping;
            rb.angularDamping = BallAngularDamping;

            BallController ballController = ball.AddComponent<BallController>();
            ballController.AssignTrail(CreateBallTrail(ball));
            ballController.ConfigureShadowMaterial(GetOrCreateBallShadowMaterial());

            return ball;
        }

        /// <summary>
        /// Speed streak. Emission is driven by BallController from the ball's
        /// actual velocity, so it is off here — otherwise the ball would drag a
        /// permanent tail around, including while glued to a player's foot.
        /// </summary>
        private static TrailRenderer CreateBallTrail(GameObject ball)
        {
            TrailRenderer trail = ball.AddComponent<TrailRenderer>();

            trail.time = BallTrailTime;
            trail.startWidth = BallTrailStartWidth;
            trail.endWidth = 0f;
            trail.emitting = false;

            // Fade the tail out as well as taper it: width alone leaves a hard
            // edge where the trail ends.
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0.85f, 0f), new GradientAlphaKey(0f, 1f) });
            trail.colorGradient = gradient;

            trail.sharedMaterial = GetOrCreateTrailMaterial();

            return trail;
        }

        /// <summary>
        /// The trail needs a shader that honours vertex colour alpha, or the
        /// fade above does nothing. Sprites/Default does; the lit pipeline
        /// shader does not.
        /// </summary>
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
        // Clash UI
        // ---------------------------------------------------------------------

        // The duel panel is a bottom banner, not a centred box. It used to sit
        // dead centre, which meant the dramatic camera swooped in on the two
        // players and then the panel covered them up.
        private const float UiClashBannerHeight = 0.35f;

        // Taller and slightly narrower than they were: every caption is now two
        // lines, the move on top and the move it beats underneath.
        private static readonly Vector2 UiButtonSize = new Vector2(330f, 124f);
        private const int UiButtonFontSize = 26;

        // Horizontal offset of each action button from the banner centre. The
        // pair spans +-365 px of a 1920 reference, which is what leaves the side
        // zones below their room.
        private const float UiButtonSpacing = 200f;

        // The three zones of the duel banner, as fractions of its width. Blue
        // reads out on the left, Red on the right, and the choice sits between
        // them where the eye ends up after comparing the two.
        private const float UiClashSideZoneWidth = 0.28f;
        private const float UiClashZonePadding = 44f;

        private static readonly Color UiClashBlueTint = new Color(0.55f, 0.78f, 1f, 1f);
        private static readonly Color UiClashRedTint = new Color(1f, 0.58f, 0.52f, 1f);

        private static readonly Vector2 UiScoreSize = new Vector2(400f, 90f);
        private const float UiScoreTopOffset = -24f;

        // Directly under the score, NOT in a screen corner. Even at the NEAREST
        // row of the frustum the camera sees about +-18.5 units across while the
        // pitch is only +-15 wide, and it only widens with distance — so on a
        // wide window a corner-anchored label lands out past the touchline.
        // Centre column is the only strip that stays over the pitch at every
        // aspect ratio.
        private static readonly Vector2 UiTimerSize = new Vector2(360f, 60f);
        private static readonly Vector2 UiTimerOffset = new Vector2(0f, -112f);

        // The tournament round, tucked under the clock.
        private static readonly Vector2 UiTournamentBadgeSize = new Vector2(360f, 40f);
        private static readonly Vector2 UiTournamentBadgeOffset = new Vector2(0f, -160f);
        private static readonly Color UiTournamentBadgeColor = new Color(1f, 0.85f, 0.35f, 1f);

        // Full time: result on top, the comparison table under it, and the two
        // ways out along the bottom.
        private static readonly Vector2 UiResultSize = new Vector2(1200f, 200f);
        private static readonly Vector2 UiResultOffset = new Vector2(0f, 320f);
        private static readonly Vector2 UiStatsSize = new Vector2(1000f, 340f);
        private static readonly Vector2 UiStatsOffset = new Vector2(0f, 40f);
        private static readonly Vector2 UiRestartButtonSize = new Vector2(460f, 120f);
        private const float UiMatchOverButtonSpacing = 260f;
        private const float UiMatchOverButtonY = -300f;

        // Above the centre of the pitch: high enough to clear the players in a
        // duel, low enough to read as part of the action.
        private static readonly Vector2 UiAnnouncerSize = new Vector2(1400f, 160f);
        private static readonly Vector2 UiAnnouncerOffset = new Vector2(0f, 180f);

        private static readonly Vector2 UiTitleSize = new Vector2(1600f, 200f);
        private static readonly Vector2 UiPlayButtonSize = new Vector2(420f, 120f);
        private static readonly Color UiTitleBackground = new Color(0.05f, 0.09f, 0.20f, 1f);

        // Momentum bars, flanking the scoreboard along the top edge: Blue to the
        // left of the score, Red to the right. Reading them beside the score is
        // the point — the two numbers that say who is winning and who is on top
        // right now belong in one glance, and the top strip is the one place the
        // duel banner can never cover.
        private static readonly Vector2 UiTensionRootSize = new Vector2(1800f, 110f);
        private static readonly Vector2 UiTensionRootOffset = new Vector2(0f, UiScoreTopOffset);
        private static readonly Vector2 UiTensionBarSize = new Vector2(420f, 34f);
        private static readonly Vector2 UiTensionLabelSize = new Vector2(420f, 30f);

        // Half the scoreboard's width, plus a gap, plus half a bar: puts each
        // bar just clear of the score rather than overlapping it.
        private const float UiTensionBarSpacing = 440f;
        private const float UiTensionLabelY = 32f;
        private const float UiTensionBarInset = 4f;
        private static readonly Color UiTensionTroughColor = new Color(0f, 0f, 0f, 0.65f);

        // Developer menu. A small visible tool icon rather than the old
        // invisible corner (this is a portfolio piece — the point now is that
        // it CAN be found, not that it can't) — kept small on purpose so it
        // never competes with the scoreboard beside it.
        private static readonly Vector2 UiDebugTriggerSize = new Vector2(56f, 56f);
        private static readonly Color UiDebugTriggerBackground = new Color(0.02f, 0.02f, 0.04f, 0.85f);
        private static readonly Color UiDebugGearColor = new Color32(210, 210, 218, 255);
        private static readonly Color UiDebugBackground = new Color(0.02f, 0.02f, 0.04f, 0.88f);
        private static readonly Vector2 UiDebugHeadingSize = new Vector2(1400f, 100f);
        private static readonly Vector2 UiDebugHeadingOffset = new Vector2(0f, 330f);
        // Six actions now, so the rows are shorter and closer together than they
        // were: at the old height the last one fell off the bottom of a 1080
        // reference screen and the read-back landed on top of it. Spaced by
        // EDGES — 96 tall with a 20 px gap — rather than by centres.
        private static readonly Vector2 UiDebugButtonSize = new Vector2(700f, 96f);
        private const float UiDebugFirstButtonY = 200f;
        private const float UiDebugButtonStep = 116f;
        // The options. A floating card rather than a full-screen panel, so the
        // screen that opened it stays visible around the edges.
        //
        // Taller than it was twice over: the language row went in above the
        // volumes, and a third slider (whistles, split out of the classic
        // effects channel) went in between music and SFX. The block is laid out
        // from the top edge down, with a gap between the EDGES of consecutive
        // elements rather than between their centres — evenly spaced centres are
        // not evenly spaced boxes when the boxes are different heights.
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

        // The language row: one button per language, each captioned in its own
        // words. Wide enough for "ESPAÑOL" and for 日本語 at the same size, and
        // spaced so three of them span 860 of the panel's 1100.
        private const float UiOptionsLanguageLabelY = 240f;
        private const float UiOptionsLanguageY = 150f;
        private static readonly Vector2 UiOptionsLanguageSize = new Vector2(260f, 92f);
        private const float UiOptionsLanguageSpacing = 300f;
        private static readonly Color UiAudioSliderTrough = new Color(0.12f, 0.16f, 0.28f, 1f);
        private static readonly Color UiAudioSliderFill = new Color(0.20f, 0.65f, 0.95f, 1f);

        private static readonly Vector2 UiTitleOptionsSize = new Vector2(420f, 96f);

        // The title screen, laid out downwards with a constant gap between the
        // EDGES of each element rather than between their centres. Evenly spaced
        // centres are not evenly spaced boxes when the boxes are different
        // heights: the two 120-tall buttons ended up touching while the 96-tall
        // one below them had a gap.
        // Pinch zoom, as a multiple of the rig's designed offset.
        //
        // The range is deliberately narrow. This is a tactical view: the whole
        // pitch has to stay readable at either end, so the pinch is there to
        // lean in on a passage of play or take in a bit more of the field —
        // not to turn the game into a top-down map or a shoulder camera.
        private const float CameraMinZoom = 0.65f;
        private const float CameraMaxZoom = 1.6f;

        // Scale per pixel the fingers move apart. At 0.0015 a 230-pixel spread —
        // an ordinary flick — already hit the near limit, which made the zoom
        // feel like it was snapping rather than following the hand. At 0.0008 the
        // near half of the range takes about 440 pixels and the whole range about
        // 1200, which is a deliberate two-handed gesture.
        private const float CameraZoomSensitivity = 0.0008f;

        // How far the view leans towards the goal the carrier is attacking. Was
        // 5, which pushed a running player to the edge of the frame; the lean is
        // meant to show a little more of what is ahead, not to leave the ball
        // behind.
        private const float CameraLookAhead = 1.8f;
        private const float CameraMaxLookAhead = 2.2f;
        private const float CameraLookAheadSmoothing = 0.9f;

        // The back control on the setup screens. Anchored into the top-left
        // corner and offset inwards, clear of the notch/rounded corner every
        // phone has.
        private static readonly Vector2 UiBackButtonSize = new Vector2(230f, 84f);
        private static readonly Vector2 UiBackButtonOffset = new Vector2(48f, -48f);
        private static readonly Color UiBackButtonColor = new Color(0.32f, 0.34f, 0.40f, 1f);

        // The player editor. Left column is the controls, right column the
        // read-back of what is currently staged.
        // Shared with the squad board, which is the screen this one opens over:
        // a different navy between two screens one step apart reads as a glitch
        // rather than as a design.
        //
        // A property, not a field. Static field initialisers run in the order
        // they are WRITTEN, and this one is declared above UiMenuBackground — as
        // a field it copied a Color that was still all zeroes, and the panel came
        // out fully transparent. A property is evaluated when it is read.
        private static Color UiEditBackground => UiMenuBackground;
        // The content block, centred on the screen by its own container. The two
        // end offsets are chosen so the block is symmetric about zero: the
        // heading's top edge sits at +495 and the exit buttons' bottom edge at
        // −495, which is what actually centres it. Everything between them is
        // spaced from those.
        private static readonly Vector2 UiEditContentSize = new Vector2(1600f, 1000f);
        private const float UiEditHeadingY = 455f;
        private const float UiEditExitY = -447f;

        private static readonly Vector2 UiEditLabelSize = new Vector2(900f, 44f);
        private static readonly Vector2 UiEditChoiceSize = new Vector2(230f, 70f);
        private const float UiEditChoiceSpacing = 250f;
        private static readonly Vector2 UiEditNudgeSize = new Vector2(78f, 62f);

        // One stat row: NAME · − · VALUE · +.
        //
        // Centred on what is VISIBLE, not on the rects. The name is
        // right-aligned so the seven labels form a straight column, which means
        // the left half of an oversized name rect is empty space — and centring
        // the rects therefore pushed all the ink to the right of the screen
        // while the arithmetic insisted it was centred. The column is now only
        // as wide as the longest label ("ESTAMINA") needs, and the whole row is
        // offset so the ink, rather than the boxes, sits on the middle.
        // The label is LEFT-aligned in a wide column, not right-aligned in a
        // narrow one, and the row is laid out around that.
        //
        // Right-aligned meant the ink began wherever the word happened to end,
        // so the visible block moved every time the language changed the length
        // of "ESTAMINA" — centred in Spanish, adrift in English, adrift the
        // other way in Japanese. Left-aligned, the ink starts at the same x in
        // every language, which is what makes the group's centre a constant:
        // the label opens the row at -320 and the plus closes it at +319.
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

        // Penalty menu. Two buttons and nothing else to look at.
        private static readonly Color UiPenaltyBackground = new Color(0.10f, 0.04f, 0.06f, 0.96f);
        private static readonly Vector2 UiPenaltyHeadingSize = new Vector2(1600f, 200f);
        private static readonly Vector2 UiPenaltyHeadingOffset = new Vector2(0f, 300f);
        private static readonly Vector2 UiPenaltyButtonSize = new Vector2(620f, 280f);
        private const float UiPenaltyButtonSpacing = 360f;
        private const float UiPenaltyButtonY = 0f;
        private static readonly Vector2 UiPenaltyResultSize = new Vector2(1400f, 120f);
        private static readonly Vector2 UiPenaltyResultOffset = new Vector2(0f, -260f);

        // Team sheet. Shapes in a row, then the armband, then one wide kickoff
        // button — the choices above and the confirmation below never look alike.
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

        // Pre-match settings. Three rows of choices, each under its own label,
        // with the readout and the way out at the bottom.
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
        // The kit row: four swatches wearing the strips they stand for. Wider
        // spacing than the other rows because the buttons ARE the information
        // here — a green rectangle has to be big enough to read as a colour.
        private const float UiConfigKitLabelY = -270f;
        private const float UiConfigKitY = -355f;
        private const float UiConfigKitSpacing = 340f;
        private static readonly Vector2 UiConfigKitSize = new Vector2(300f, 100f);

        // Moved above the choices, as a subtitle under the heading, when the kit
        // row was added. It is a read-back of everything picked rather than a
        // step in the flow, so it works as well at the top — and the bottom of
        // this panel had run out of room between the last row and the button.
        private static readonly Vector2 UiConfigSummarySize = new Vector2(1700f, 70f);
        private static readonly Vector2 UiConfigSummaryOffset = new Vector2(0f, 395f);
        private static readonly Vector2 UiConfigContinueSize = new Vector2(520f, 120f);
        private static readonly Vector2 UiConfigContinueOffset = new Vector2(0f, -470f);

        // The interval. Two buttons and nothing else: change the team, or send
        // it back out. There is no HUD button into the substitutions board any
        // more — stamina does not come back during a match, so a change is a
        // decision for half time rather than something to reach for mid-move.
        private static readonly Vector2 UiHalftimeHeadingSize = new Vector2(1400f, 130f);
        private static readonly Vector2 UiHalftimeHeadingOffset = new Vector2(0f, 250f);
        private static readonly Vector2 UiHalftimeSummarySize = new Vector2(1400f, 280f);
        private static readonly Vector2 UiHalftimeSummaryOffset = new Vector2(0f, 30f);
        private static readonly Vector2 UiHalftimeButtonSize = new Vector2(500f, 130f);
        private static readonly Vector2 UiHalftimeSubsOffset = new Vector2(-280f, -220f);
        private static readonly Vector2 UiHalftimeResumeOffset = new Vector2(280f, -220f);

        // The board itself: stat block down the left, the shape on the right,
        // the bench in a row beneath it. Laid out in absolute reference pixels
        // (1920 x 1080) because the mini-pitch has to preserve the proportions
        // of the real one — a layout group would stretch the shape to fit and
        // the "formation preview" would stop previewing the formation.
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

        // Under the stats column on the left, which is the player it acts on.
        private static readonly Vector2 UiSubsEditOffset = new Vector2(-580f, -430f);

        // Each zone's caption clears the top of its own box by this much. Sat
        // outside the box rather than inside it, so a player slot mapped into
        // the top row of the shape can never be printed over.
        private const float UiSubsCaptionClearance = 26f;
        private const float UiSubsStatsPadding = 34f;
        // The ground every full-screen menu is painted on. One constant rather
        // than one per screen: the squad board and the player editor open into
        // each other, and two nearly-identical navies read as a bug.
        //
        // Alpha 1, and that is the point of it. At 0.97 the PITCH showed through
        // the squad board — three per cent of bright green over a dark ground is
        // plainly visible — which made a pre-match menu look like it was
        // hovering over a match that had already started.
        private static readonly Color UiMenuBackground = new Color(0.06f, 0.09f, 0.17f, 1f);

        // Property for the same reason as UiEditBackground above, even though
        // this one happens to be declared after UiMenuBackground: relying on
        // declaration order is what broke the other one.
        private static Color UiSubsBackground => UiMenuBackground;
        private static readonly Color UiSubsStatsPanelColor = new Color(0.10f, 0.14f, 0.24f, 1f);
        private static readonly Color UiSubsPitchColor = new Color(0.10f, 0.30f, 0.16f, 1f);
        private static readonly Color UiSubsBenchColor = new Color(0.18f, 0.20f, 0.24f, 1f);

        /// <summary>
        /// The container every panel and every HUD element is parented to.
        ///
        /// One level between the canvas and the screens, and it exists for one
        /// reason: it is inset to the phone's safe area, so a notch or a gesture
        /// bar can never sit on top of a button. Drawing order is unaffected —
        /// it is still sibling order, only one rung further down.
        /// </summary>
        private static Transform uiHudArea;

        /// <summary>The canvas itself. Full-screen menus hang straight off it.</summary>
        private static Transform uiCanvasRoot;

        /// <summary>
        /// Builds that container: a full-bleed rect with the fitter on it.
        ///
        /// The inset itself is worked out at runtime from Screen.safeArea, which
        /// in the editor is simply the whole Game view — so this changes nothing
        /// on a desktop and everything on a phone with a cutout.
        /// </summary>
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

        private static ClashUIController CreateClashUI()
        {
            GameObject canvasObject = new GameObject("UI Canvas");
            Undo.RegisterCreatedObjectUndo(canvasObject, "Create UI Canvas");

            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            // Above anything else that might be drawn later; the duel is modal.
            canvas.sortingOrder = 100;

            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            // The game is landscape only, so this is a fixed configuration
            // again rather than something that reacts to the screen.
            //
            // Matched to HEIGHT: a phone is far wider than 16:9 — 20:9 and 21:9
            // are ordinary — and matching height makes a wide screen simply GIVE
            // more horizontal room rather than shrinking the layout to fit. At
            // 20:9 the visible width works out at about 2400 reference units
            // instead of 1920, so nothing laid out inside +-960 can be cut.
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;

            canvasObject.AddComponent<GraphicRaycaster>();

            uiCanvasRoot = canvasObject.transform;

            // The HUD has no panel of its own to hang a safe area off, so it
            // gets one: the scoreboard, the clock and the momentum bars sit
            // against the screen edges and are exactly what a cutout eats.
            uiHudArea = CreateSafeArea(uiCanvasRoot);

            GameObject panel = CreateClashPanel(uiHudArea);
            Text clashText = CreateClashText(panel.transform);

            // Left and right zones. Fixed to a TEAM, never to a duel role: the
            // controller maps by TeamId, so blue is always read on the left.
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

            // Hidden from the start so the panel does not sit over the pitch in
            // the editor: Awake only runs in play mode.
            panel.SetActive(false);

            return controller;
        }

        private static GameObject CreateClashPanel(Transform parent)
        {
            GameObject panel = new GameObject("Clash Panel", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(panel, "Create Clash Panel");
            panel.transform.SetParent(parent, false);

            // Full-width strip across the bottom third. Everything above it
            // stays completely transparent, so the duel itself is visible.
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

        /// <summary>
        /// Scoreboard and match clock, pinned to the top centre and stacked.
        /// Both live on the same canvas as the duel panel but outside it, so
        /// hiding the duel never hides them.
        /// </summary>
        private static void CreateScoreUI(Transform canvas, ScoreManager scoreManager)
        {
            scoreManager.scoreText = CreateHudText(canvas, "Score Text", "0 - 0",
                new Vector2(0.5f, 1f), new Vector2(0f, UiScoreTopOffset),
                UiScoreSize, 56, TextAnchor.MiddleCenter);

            scoreManager.timerText = CreateHudText(canvas, "Timer Text", "Tiempo: 90",
                new Vector2(0.5f, 1f), UiTimerOffset,
                UiTimerSize, 40, TextAnchor.MiddleCenter);

            // Under the clock and deliberately small: it names the round, which
            // never changes during a match, so it is a reminder rather than
            // something to read. Hidden entirely outside a tournament.
            scoreManager.tournamentText = CreateHudText(canvas, "Tournament Badge", string.Empty,
                new Vector2(0.5f, 1f), UiTournamentBadgeOffset,
                UiTournamentBadgeSize, 26, TextAnchor.MiddleCenter);

            scoreManager.tournamentText.color = UiTournamentBadgeColor;
            scoreManager.tournamentText.gameObject.SetActive(false);
        }

        /// <summary>
        /// A HUD label pinned to one corner. Anchor and pivot are the same point,
        /// so the offset reads as a straight margin from that corner whatever the
        /// screen size.
        /// </summary>
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

            // The pitch behind it is light green; an outline keeps the digits
            // readable without needing a backing panel.
            Outline outline = textObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
            outline.effectDistance = new Vector2(2f, -2f);

            return text;
        }

        /// <summary>
        /// The full-time screen. Built last so it is the canvas's final sibling
        /// and therefore draws over the duel panel and the scoreboard alike.
        ///
        /// The controller goes on the canvas, not on the panel it owns: a
        /// component on a deactivated object never gets OnEnable, so parking it
        /// on its own hidden panel would stop it ever hearing the whistle.
        /// </summary>
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

            // Near-opaque: full time is modal, and a readable pitch behind the
            // result would invite taps that no longer do anything.
            Image background = CreateFullScreenBackdrop(panel.transform, new Color(0f, 0f, 0f, 0.92f));

            // Controls go inside this, never on the panel itself: the panel spans the
            // whole screen so its background can, and the fitter on this child is what
            // keeps the buttons clear of the cutout.
            Transform safeContent = CreateSafeAreaContent(panel.transform);

            Text resultText = CreateHudText(safeContent, "Result Text", "FIN",
                new Vector2(0.5f, 0.5f), UiResultOffset,
                UiResultSize, 110, TextAnchor.MiddleCenter);

            // Given the component but no key: which of victory, defeat and draw
            // this reads is not known until the whistle, and the controller sets
            // the key then. An empty key leaves the placeholder alone.
            LocalizeDynamic(resultText);

            // The comparison table. Given a monospaced OS font so the padded
            // columns actually line up — in a proportional face a "1" and a "8"
            // are different widths and every row would sit at its own margin.
            Text statsText = CreateHudText(safeContent, "Stats Text", string.Empty,
                new Vector2(0.5f, 0.5f), UiStatsOffset,
                UiStatsSize, 40, TextAnchor.UpperCenter);

            statsText.font = GetMonospaceFont();
            statsText.lineSpacing = 1.25f;

            // The friendly caption is the default; in a tournament the
            // controller repoints this button at "next round" or "finish".
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

            // Hidden from the start: Awake only runs in play mode, so without
            // this the panel would black out the whole editor Game view.
            panel.SetActive(false);
        }

        /// <summary>
        /// A centred menu button with a label. Shared by the full-time screen
        /// and the title screen, which want the same thing in the same place.
        /// </summary>
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

            // Opt-out, not opt-in: every menu button gets the click by default,
            // and only the handful that fire DURING live play (duel actions via
            // the separate CreateActionButton, the two penalty direction
            // buttons) ask for silence explicitly. A component rather than an
            // AddListener call here — this runs at generation time, outside
            // Play Mode, where a plain delegate listener is never serialized
            // into the saved scene (see ButtonClickSound's own doc comment).
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

        /// <summary>
        /// The restart announcer, sitting above the centre of the pitch where
        /// the eye already is. Created between the scoreboard and the modal
        /// panels: it must sit over the duel banner but under full time.
        ///
        /// The controller goes on the Text itself, which is never deactivated —
        /// visibility is the alpha's job, not the GameObject's — so its fade
        /// coroutine can always run.
        /// </summary>
        private static void CreateAnnouncerUI(Transform canvas)
        {
            Text announcerText = CreateHudText(canvas, "Announcer Text", string.Empty,
                new Vector2(0.5f, 0.5f), UiAnnouncerOffset,
                UiAnnouncerSize, 72, TextAnchor.MiddleCenter);

            // Starts invisible: Awake only runs in play mode, so without this the
            // placeholder would hang over the pitch in the editor.
            Color transparent = announcerText.color;
            transparent.a = 0f;
            announcerText.color = transparent;

            AnnouncerUIController controller = announcerText.gameObject.AddComponent<AnnouncerUIController>();
            controller.announcerText = announcerText;
        }

        /// <summary>
        /// The title screen. Built last of all so it is the canvas's final
        /// sibling and covers every other panel.
        ///
        /// The controller goes on the canvas, not on the panel: the panel is
        /// left deactivated in the editor, and a component on a deactivated
        /// object never gets Start — which is where the Play button is wired.
        /// </summary>
        /// <summary>
        /// The team sheet, shown between the title and the kickoff. Same shape
        /// as the other full-screen menus: an opaque panel the controller shows
        /// and hides, with the controller itself on the canvas — a component on
        /// a deactivated GameObject never gets Start, and Start is where its
        /// four buttons are wired.
        /// </summary>
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

            // Opaque like the title: this is still a menu, not a pause over a
            // pitch worth glimpsing.
            Image background = CreateFullScreenBackdrop(panel.transform, UiTitleBackground);

            // Controls go inside this, never on the panel itself: the panel spans the
            // whole screen so its background can, and the fitter on this child is what
            // keeps the buttons clear of the cutout.
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

            // The armband. Only the row's frame is built here — the seven
            // candidates are created by the controller, because which players
            // are available and what line each of them holds both change with
            // the shape chosen above.
            Text captainHeading = CreateHudText(safeContent, "Captain Heading",
                "ELIGE TU CAPITÁN", new Vector2(0.5f, 0.5f), UiCaptainHeadingOffset,
                UiCaptainHeadingSize, 40, TextAnchor.MiddleCenter);

            RectTransform captainArea = CreateLayoutArea(safeContent, "Captain Area",
                UiCaptainAreaOffset, UiCaptainAreaSize);

            Button kickoff = LocalizeButton(CreateUiButton(safeContent, "Kickoff Button",
                "¡AL CÉSPED!", UiKickoffButtonOffset, UiKickoffButtonSize), "formation.kickoff");

            // Green and wide: the one button that leaves this screen must not
            // look like a fourth thing to choose between.
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

            // Top-right, mirroring the back control on the left: both are ways
            // off this screen rather than choices on it.
            controller.squadButton = LocalizeButton(CreateCornerButton(safeContent,
                "Formation Squad Button", "PLANTILLA ›", anchorRight: true), "formation.squad");
            controller.captainArea = captainArea;
            controller.captainHeading = captainHeading;

            // Hidden in the editor; the title screen turns it on when the player
            // presses Play.
            panel.SetActive(false);

            return controller;
        }

        /// <summary>
        /// The opaque ground a full-screen menu is painted on: the panel's first
        /// child, stretched to the panel and therefore to the whole screen.
        ///
        /// It is deliberately OUTSIDE the safe-area container. A background is
        /// not content — its job is to hide the pitch — and inset by the notch
        /// margin it left a strip of live 3D football down each side of every
        /// menu. Its buttons still respect the cutout, because they live in the
        /// sibling below.
        ///
        /// First sibling, because within a canvas that is what "behind"
        /// means — and it is still what swallows taps meant for the pitch.
        /// </summary>
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

        /// <summary>
        /// The inset container a screen's controls live in: the panel's second
        /// child, in front of the background and carrying the safe-area fitter.
        ///
        /// One per screen rather than one for the whole canvas. A single
        /// container above everything would inset the backgrounds too, which is
        /// the very thing being fixed — and it is only three components either
        /// way, because a screen that is never shown never ticks.
        ///
        /// Starts stretched with no offsets; the fitter narrows it to whatever
        /// the device actually allows on the first frame it runs.
        /// </summary>
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

        /// <summary>
        /// An invisible box a controller lays runtime buttons out inside. No
        /// Image on purpose: a container that painted anything would also be a
        /// raycast target, and it sits directly over the buttons it holds.
        ///
        /// Anchored to the centre with an explicit size rather than stretched,
        /// so its rect is exactly the size asked for the instant it is created —
        /// which is what the layout maths reads.
        /// </summary>
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

        /// <summary>
        /// The pre-match settings screen, between the title and the team sheet.
        /// Three rows of mutually exclusive choices and one way out.
        ///
        /// Built AFTER the team sheet so it draws over it, and the controller
        /// goes on the canvas rather than on the panel — a component on a
        /// deactivated GameObject never receives Start, and Start is where every
        /// one of these buttons is wired.
        /// </summary>
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

            // Controls go inside this, never on the panel itself: the panel spans the
            // whole screen so its background can, and the fitter on this child is what
            // keeps the buttons clear of the cutout.
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

            // The three shape buttons beside it are NOT localized: "2-2-2" is a
            // formation, not a word, and it reads the same in every language.
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

            // Green, like the kickoff button on the next screen: the way out of
            // a menu always looks the same.
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

            // Hidden in the editor; the title screen turns it on when the player
            // presses Play.
            panel.SetActive(false);

            return controller;
        }

        /// <summary>
        /// The way out of a setup screen, anchored to the top-left corner.
        ///
        /// A corner rather than the row of choices, and deliberately smaller
        /// than the forward button: cancelling is not one of the options being
        /// weighed up, and putting it in with them invites a mis-tap that throws
        /// away everything the player just picked. Top-left is where a back
        /// control belongs on a touch screen.
        /// </summary>
        private static Button CreateBackButton(Transform parent, string objectName)
        {
            return LocalizeButton(
                CreateCornerButton(parent, objectName, "‹ ATRÁS", anchorRight: false),
                "common.back");
        }

        /// <summary>
        /// A control pinned into one of the top corners: a way OFF the screen
        /// rather than a choice on it.
        ///
        /// Anchor and pivot share a corner so the inset reads as a straight
        /// margin at any screen size, and the offset keeps it clear of the
        /// notch and rounded corner every phone has.
        /// </summary>
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

        private static Text CreateConfigLabel(Transform parent, string objectName, string caption, float y)
        {
            return CreateHudText(parent, objectName, caption,
                new Vector2(0.5f, 0.5f), new Vector2(0f, y),
                UiConfigLabelSize, 32, TextAnchor.MiddleCenter);
        }

        private static Button CreateConfigChoice(Transform parent, string objectName, string caption,
            float x, float y, Vector2 size)
        {
            Button button = CreateUiButton(parent, objectName, caption, new Vector2(x, y), size);

            Text label = button.GetComponentInChildren<Text>();
            label.fontSize = 32;

            return button;
        }

        /// <summary>
        /// A kit swatch: a button already wearing the strip it stands for, so
        /// the choice can be made by looking rather than by reading.
        ///
        /// The caption is written in whichever of black or white survives on top
        /// of that strip. A fixed colour would be illegible on one end of the
        /// range or the other — white lettering on the white kit, black on the
        /// black one — and this row is the only place in the UI where the button
        /// colour is not under the layout's control.
        /// </summary>
        /// <summary>The caption key for a strip. Capitals, to match the row it sits in.</summary>
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

        private static Button CreateKitChoice(Transform parent, string objectName, TeamKit kit, float x)
        {
            Color color = TeamKits.GetColor(kit);

            // Localised through the component rather than by calling GetLabel
            // here: this runs in the editor, and whatever language the editor
            // happened to be in would otherwise be baked into the saved scene.
            Button button = LocalizeButton(
                CreateUiButton(parent, objectName, TeamKits.GetLabel(kit).ToUpperInvariant(),
                    new Vector2(x, UiConfigKitY), UiConfigKitSize),
                KitKey(kit));

            button.targetGraphic.color = color;

            Text label = button.GetComponentInChildren<Text>();
            label.fontSize = 32;

            // Rec. 601 luma: closer to how the eye weights the three channels
            // than a flat average, which would call the green kit dark.
            float luma = (0.299f * color.r) + (0.587f * color.g) + (0.114f * color.b);
            label.color = luma > 0.5f ? Color.black : Color.white;

            return button;
        }

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

            // Fully opaque, unlike the full-time screen: there is nothing behind
            // this worth glimpsing, and a solid ground reads as a menu.
            Image background = CreateFullScreenBackdrop(panel.transform, UiTitleBackground);

            // Controls go inside this, never on the panel itself: the panel spans the
            // whole screen so its background can, and the fitter on this child is what
            // keeps the buttons clear of the cutout.
            Transform safeContent = CreateSafeAreaContent(panel.transform);

            Localize(CreateHudText(safeContent, "Title Text", "TACTICAL SOCCER",
                new Vector2(0.5f, 0.5f), UiTitleHeadingOffset,
                UiTitleHeadingSize, 110, TextAnchor.MiddleCenter), "title.heading");

            Button playButton = LocalizeButton(CreateUiButton(safeContent, "Play Button",
                "PARTIDO RÁPIDO", new Vector2(0f, UiTitlePlayY), UiPlayButtonSize),
                "title.quickMatch");

            // Structurally identical to the quick match button above — same
            // size, same colour, same type. The two are peers: one is not a
            // promoted mode and the other a fallback, they are simply the two
            // ways to start playing, and dressing one in gold said otherwise.
            // Localized with the FIRST round's key, which is also the caption
            // written here. The controller repoints it at the round actually
            // coming up every time the title is shown — through the same
            // component, so it follows the language as well as the round.
            Button tournamentButton = LocalizeButton(CreateUiButton(safeContent,
                "Tournament Button", "TORNEO",
                new Vector2(0f, UiTitleTournamentY), UiPlayButtonSize),
                "tournament.next.quarters");

            Text tournamentLabel = tournamentButton.GetComponentInChildren<Text>();

            // Smaller and below both ways in, because it is a detour rather than
            // a step: the options open OVER the title and hand it straight back.
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

            // Play no longer kicks off: it opens the match settings, which hand
            // over to the team sheet, which is what eventually does. The team
            // sheet is kept as a fallback for a scene built without settings.
            controller.configMenu = configUI;
            controller.formationMenu = formationUI;

            // Hidden in the editor; the controller turns it back on in Start.
            panel.SetActive(false);
        }

        /// <summary>
        /// The team talk. Same shape as the other full-screen menus: an opaque
        /// panel the controller shows and hides, with the controller itself on
        /// the canvas — a component on a deactivated GameObject never receives
        /// OnEnable, and OnEnable is where it subscribes to the half-time
        /// whistle it exists to answer.
        /// </summary>
        /// <summary>
        /// The developer menu, plus the small tool icon that opens it.
        ///
        /// A real, visible square with a procedurally-drawn gear on it — this is
        /// a portfolio piece, so the point is that a visitor CAN find the
        /// developer menu, not that they can't. Opens on a single click, kept
        /// small (56x56) so it never competes with the scoreboard beside it. It
        /// is parented to the CANVAS and not to the panel, because it has to be
        /// pressable while the panel is hidden — same reachability window as
        /// before (see DebugMenuUIController.IsReachable): only during a real
        /// passage of play, where it is shown; hidden the rest of the time
        /// instead of merely un-clickable, since an icon that looked live but
        /// did nothing would read as broken rather than as a portfolio flourish.
        /// </summary>
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

            // Controls go inside this, never on the panel itself: the panel spans the
            // whole screen so its background can, and the fitter on this child is what
            // keeps the buttons clear of the cutout.
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

            // Hidden from the start: Awake only runs in play mode, so without
            // this the menu would black out the whole editor Game view.
            panel.SetActive(false);
        }

        /// <summary>
        /// The audio options: two sliders and a way out.
        ///
        /// Narrower than the other menus and NOT full-screen, so whatever opened
        /// it — the title, the developer menu — stays visible around the edges.
        /// It is a detour from those screens rather than a replacement for them,
        /// and the player has to be able to see where closing it lands.
        /// </summary>
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

            // NOT localized through a component: the readout is a sentence with
            // two numbers in it, so the controller composes it from a key and a
            // pair of percentages and rewrites it on every change.
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

            // Hidden from the start: Awake only runs in play mode, so without
            // this the panel would sit over the editor Game view.
            panel.SetActive(false);
        }

        /// <summary>
        /// One button per available language, laid out in a centred row.
        ///
        /// The captions written here are only placeholders for the editor: the
        /// controller rewrites each one with the name that language gives
        /// ITSELF, in a font able to draw it. That cannot be done from the
        /// generator, because the font is resolved from the OS at runtime and
        /// serialising a dynamic font into the scene is not something to rely
        /// on.
        /// </summary>
        private static Button[] CreateLanguageButtons(Transform parent)
        {
            string[] codes = LocalizationManager.AvailableLanguages;
            Button[] buttons = new Button[codes.Length];

            for (int i = 0; i < codes.Length; i++)
            {
                // Centred on the panel: the row spans the same distance either
                // side of zero however many languages there are.
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

        /// <summary>
        /// Ties a caption to a localisation key, so it follows the language
        /// instead of being whatever Spanish was hard-coded here.
        ///
        /// The text passed in is left alone rather than translated now: the
        /// component rewrites it the moment its panel is shown in play mode, and
        /// leaving the generator's own string in place keeps the saved scene the
        /// same file whatever language the editor happens to be set to.
        /// </summary>
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

        /// <summary>As <see cref="Localize"/>, for the label inside a button.</summary>
        private static Button LocalizeButton(Button button, string key)
        {
            if (button != null)
            {
                Localize(button.GetComponentInChildren<Text>(), key);
            }

            return button;
        }

        /// <summary>
        /// Gives a caption the component but no key, for text whose key is only
        /// known once the match has been played — the full-time result being
        /// victory, defeat or a draw. Until the controller sets one, the
        /// placeholder written here is left exactly as it is.
        /// </summary>
        private static void LocalizeDynamic(Text text)
        {
            Localize(text, string.Empty);
        }

        /// <summary>
        /// A 0..1 slider built from the three parts Unity's own does: a
        /// background, a fill that follows the handle, and the handle itself.
        ///
        /// Assembled by hand rather than through the menu command because the
        /// generator has no access to the editor's GameObject menu — and because
        /// the default one arrives with a size and a palette belonging to no
        /// other screen in this game.
        /// </summary>
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

        /// <summary>
        /// The player editor: position, element, attributes and stamina.
        ///
        /// A floating card rather than a full-screen panel, so the squad board
        /// it opens over stays visible around the edges — the player is editing
        /// somebody FROM that squad and should not lose sight of it.
        /// </summary>
        private static void CreatePlayerEditUI(Transform canvas)
        {
            GameObject panel = new GameObject("Player Edit Panel", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(panel, "Create Player Edit Panel");
            panel.transform.SetParent(canvas, false);

            // Full screen and opaque, not a floating card. As a 1500-wide card on
            // a 1920 canvas it left the pitch showing down both sides, and a
            // menu you can see a match through does not read as a menu.
            RectTransform rect = (RectTransform)panel.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            Image background = CreateFullScreenBackdrop(panel.transform, UiEditBackground);

            // Controls go inside this, never on the panel itself: the panel spans the
            // whole screen so its background can, and the fitter on this child is what
            // keeps the buttons clear of the cutout.
            Transform safeContent = CreateSafeAreaContent(panel.transform);

            PlayerEditUIController controller = canvas.gameObject.AddComponent<PlayerEditUIController>();
            controller.uiPanel = panel;

            // Everything below hangs off a container pinned to the middle of the
            // screen, rather than off the full-screen ground directly. Two
            // reasons: the block can be re-centred by moving one object, and the
            // offsets inside it are read as "distance from the middle of the
            // content" instead of "distance from the middle of the screen",
            // which is what let the whole thing drift off centre in the first
            // place.
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

            // The four position buttons and the four element buttons are
            // captioned by the CONTROLLER, not here: each one pairs a word with
            // a glyph, which is a composition no single key can carry.
            controller.roleGoalkeeperButton = CreateEditChoice(host, "Edit Role GK", "GK", -1.5f, 300f);
            controller.roleDefenderButton = CreateEditChoice(host, "Edit Role DF", "DF", -0.5f, 300f);
            controller.roleMidfielderButton = CreateEditChoice(host, "Edit Role MF", "MF", 0.5f, 300f);
            controller.roleForwardButton = CreateEditChoice(host, "Edit Role FW", "FW", 1.5f, 300f);

            Localize(CreateHudText(host, "Edit Element Label", "ELEMENTO",
                new Vector2(0.5f, 0.5f), new Vector2(0f, 235f),
                UiEditLabelSize, 26, TextAnchor.MiddleCenter), "edit.element");

            // The kanji as well as the name, matching the player labels on the
            // pitch: that glyph is how an element is read during a match.
            controller.elementFireButton = CreateEditChoice(host, "Edit Element Fire",
                $"{Elements.Glyph(Element.Fuego)} FUEGO", -1.5f, 175f);
            controller.elementForestButton = CreateEditChoice(host, "Edit Element Forest",
                $"{Elements.Glyph(Element.Bosque)} BOSQUE", -0.5f, 175f);
            controller.elementWindButton = CreateEditChoice(host, "Edit Element Wind",
                $"{Elements.Glyph(Element.Aire)} AIRE", 0.5f, 175f);
            controller.elementMountainButton = CreateEditChoice(host, "Edit Element Mountain",
                $"{Elements.Glyph(Element.Montaña)} MONTAÑA", 1.5f, 175f);

            // Seven rows laid out from one table, so the stat list can be
            // reordered without touching seven blocks of code.
            //
            // Each row reads NAME · − · VALUE · + across the screen. The number
            // sits between its own two buttons because that is the thing they
            // move: with the name in the middle and no number anywhere, the
            // buttons appeared to do nothing at all.
            string[] rows = { "REGATE", "FUERZA", "TIRO", "ENTRADA", "BLOQUEO", "PARADA", "ESTAMINA" };

            // Parallel to the row names above, and in the same order the
            // controller writes the values back in.
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

            // Down at the bottom, out of the way of everything: it is empty
            // almost always, and it exists for the one case where an edit is
            // refused and has to say why.
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

            // Hidden in the editor; the squad board turns it on.
            panel.SetActive(false);
        }

        private static Button CreateEditChoice(Transform parent, string objectName, string caption,
            float column, float y)
        {
            Button button = CreateUiButton(parent, objectName, caption,
                new Vector2(column * UiEditChoiceSpacing, y), UiEditChoiceSize);

            button.GetComponentInChildren<Text>().fontSize = 26;

            return button;
        }

        private static Button CreateEditNudge(Transform parent, string objectName, string caption,
            float x, float y)
        {
            Button button = CreateUiButton(parent, objectName, caption, new Vector2(x, y), UiEditNudgeSize);

            button.GetComponentInChildren<Text>().fontSize = 38;

            return button;
        }

        /// <summary>A child stretched to fill its parent, which is what every layer of a slider is.</summary>
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

        /// <summary>
        /// The two momentum bars, one per side, along the bottom of the HUD.
        ///
        /// Anchored to the bottom CENTRE rather than to the screen corners. The
        /// camera sees about 27 units across while the pitch is only 15 wide, so
        /// a corner anchor puts the bar out on the grey apron where the eye never
        /// goes — the same mistake the match clock made before it was moved.
        /// </summary>
        private static void CreateTensionUI(Transform canvas)
        {
            GameObject root = new GameObject("Tension HUD", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(root, "Create Tension HUD");
            root.transform.SetParent(canvas, false);

            // Under the duel banner in the draw order. Created after it, so
            // without this it would be a later sibling and would paint over the
            // panel — and, being Images, would swallow the taps meant for the
            // buttons underneath.
            root.transform.SetAsFirstSibling();

            RectTransform rootRect = (RectTransform)root.transform;
            rootRect.anchorMin = new Vector2(0.5f, 1f);
            rootRect.anchorMax = new Vector2(0.5f, 1f);
            rootRect.pivot = new Vector2(0.5f, 1f);
            rootRect.anchoredPosition = UiTensionRootOffset;
            rootRect.sizeDelta = UiTensionRootSize;

            Image blueFill = CreateTensionBar(root.transform, "Blue Tension", -UiTensionBarSpacing);
            Image redFill = CreateTensionBar(root.transform, "Red Tension", UiTensionBarSpacing);

            // Empty rather than a placeholder in some language: the HUD writes
            // both of these every frame from the dictionary, and a baked caption
            // is only ever visible in the one moment the momentum manager is
            // missing — where the wrong language would be the least of it.
            Text blueLabel = CreateHudText(root.transform, "Blue Tension Label", string.Empty,
                new Vector2(0.5f, 0.5f), new Vector2(-UiTensionBarSpacing, UiTensionLabelY),
                UiTensionLabelSize, 24, TextAnchor.MiddleCenter);

            Text redLabel = CreateHudText(root.transform, "Red Tension Label", string.Empty,
                new Vector2(0.5f, 0.5f), new Vector2(UiTensionBarSpacing, UiTensionLabelY),
                UiTensionLabelSize, 24, TextAnchor.MiddleCenter);

            // Nothing here is ever pressed, so nothing here may absorb a press.
            // This is the guarantee that the duel buttons stay clickable: draw
            // order decides what is SEEN on top, but a raycast target decides
            // what is HIT, and the two are separate settings.
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

        /// <summary>
        /// One bar: a dark trough with a filled image inside it. The fill is
        /// Horizontal/Filled rather than a scaled rect so it empties from one
        /// end instead of shrinking towards its middle.
        /// </summary>
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

        /// <summary>
        /// The penalty menu: a heading, two enormous buttons and a line for the
        /// outcome.
        ///
        /// The buttons are deliberately the biggest in the game. It is the one
        /// moment where the whole match hangs on a single tap, and it is taken
        /// under time pressure on a phone.
        ///
        /// The controller goes on the canvas rather than on the panel, like every
        /// other modal screen here: the panel starts deactivated, and a component
        /// on a deactivated GameObject never receives Start — which is where its
        /// own buttons are wired.
        /// </summary>
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

            // Controls go inside this, never on the panel itself: the panel spans the
            // whole screen so its background can, and the fitter on this child is what
            // keeps the buttons clear of the cutout.
            Transform safeContent = CreateSafeAreaContent(panel.transform);

            Text heading = CreateHudText(safeContent, "Penalty Heading", "PENALTI",
                new Vector2(0.5f, 0.5f), UiPenaltyHeadingOffset,
                UiPenaltyHeadingSize, 70, TextAnchor.MiddleCenter);

            // No click sound: taking the penalty IS playing the match, not
            // navigating a menu.
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

            // Hidden from the start: Awake only runs in play mode, so without
            // this the menu would black out the whole editor Game view.
            panel.SetActive(false);
        }

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

            // Controls go inside this, never on the panel itself: the panel spans the
            // whole screen so its background can, and the fitter on this child is what
            // keeps the buttons clear of the cutout.
            Transform safeContent = CreateSafeAreaContent(panel.transform);

            Text heading = Localize(CreateHudText(safeContent, "Halftime Heading", "DESCANSO",
                new Vector2(0.5f, 0.5f), UiHalftimeHeadingOffset,
                UiHalftimeHeadingSize, 90, TextAnchor.MiddleCenter), "halftime.heading");

            // No component on the summary: it carries the score, so the
            // controller composes it from a key and two numbers and listens for
            // the language itself.
            Text summary = CreateHudText(safeContent, "Halftime Summary", string.Empty,
                new Vector2(0.5f, 0.5f), UiHalftimeSummaryOffset,
                UiHalftimeSummarySize, 38, TextAnchor.UpperCenter);

            Button substitutions = LocalizeButton(CreateUiButton(safeContent,
                "Halftime Subs Button", "HACER CAMBIOS",
                UiHalftimeSubsOffset, UiHalftimeButtonSize), "halftime.subs");

            Button resume = LocalizeButton(CreateUiButton(safeContent,
                "Halftime Resume Button", "SEGUNDA PARTE",
                UiHalftimeResumeOffset, UiHalftimeButtonSize), "halftime.resume");

            // Green and unmistakable, like the kickoff button on the team sheet:
            // the one that leaves the screen must not look like the one that
            // opens another.
            resume.targetGraphic.color = UiKickoffButtonColor;

            Text resumeLabel = resume.GetComponentInChildren<Text>();
            resumeLabel.color = Color.white;

            HalftimeUIController controller = canvas.gameObject.AddComponent<HalftimeUIController>();
            controller.uiPanel = panel;
            controller.headingText = heading;
            controller.summaryText = summary;
            controller.substitutionsButton = substitutions;
            controller.secondHalfButton = resume;

            // Hidden from the start: Awake only runs in play mode, so without
            // this the interval would black out the whole editor Game view.
            panel.SetActive(false);
        }

        /// <summary>
        /// The substitutions board. Only the frame is built here — the header,
        /// the stat block, the two zones and the close button. The ten player
        /// slots are built by the controller at runtime from the live squad,
        /// because which player stands in which slot is exactly what this screen
        /// changes.
        ///
        /// The controller goes on the canvas rather than on the panel: the panel
        /// is left deactivated in the editor, and a component on a deactivated
        /// GameObject never receives Start — which is where its own close button
        /// is wired. It is opened from the interval screen, which is the only
        /// place a substitution can be made.
        /// </summary>
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

            // Same shape as every other full screen: the ground reaches the
            // real edges, the board itself stays clear of the cutout.
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

            // Under the stats readout on the left, because that readout is what
            // it acts on: whichever player the board is currently describing.
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

            // Hidden from the start: Awake only runs in play mode, so without
            // this the board would black out the whole editor Game view.
            panel.SetActive(false);
        }

        /// <summary>
        /// The left-hand readout: a solid card with one text block inset in it.
        /// Top-aligned, since the block grows downwards as a player is selected
        /// and a centred one would jump about between selections.
        /// </summary>
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

        /// <summary>
        /// One of the two zones on the right: a tinted box with a caption above
        /// it. Returns the box itself, which is what the controller lays its
        /// player slots out inside.
        /// </summary>
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

            // Sat just above the box rather than inside it, so it can never be
            // covered by a player slot mapped into the top row.
            Localize(CreateHudText(parent, $"{objectName} Caption", caption,
                new Vector2(0.5f, 0.5f),
                new Vector2(offset.x, offset.y + (size.y * 0.5f) + UiSubsCaptionClearance),
                new Vector2(size.x, 44f), 28, TextAnchor.MiddleCenter), captionKey);

            return rect;
        }

        private static Text CreateClashText(Transform parent)
        {
            GameObject textObject = new GameObject("Clash Text", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(textObject, "Create Clash Text");
            textObject.transform.SetParent(parent, false);

            // Headline only, and only over the CENTRE zone: the two side panels
            // own the full height of their columns, so a full-width strip here
            // would print straight across the top of both stat readouts.
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

        /// <summary>
        /// One of the two team readouts flanking the buttons. Anchored to its
        /// own column and aligned outwards, so the two blocks read as opposite
        /// corners of the duel rather than as one centred paragraph.
        /// </summary>
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

        /// <summary>
        /// One of the two tactical choices. The caption is only a placeholder:
        /// ClashUIController relabels both buttons per duel, since the moves on
        /// offer depend on whether the human is attacking or defending.
        /// </summary>
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

        /// <summary>
        /// Arial.ttf was removed as a built-in font in Unity 2022 and now throws
        /// ArgumentException; LegacyRuntime.ttf replaced it. The fallback keeps
        /// this working if the project is ever opened in an older editor.
        /// </summary>
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

        /// <summary>
        /// A fixed-width font, for the full-time table.
        ///
        /// The columns there are lined up with string padding, which only works
        /// if every character is the same width — in the proportional UI font a
        /// row reading "11" and one reading "8" end at different places and the
        /// table visibly staggers. Falls back to the UI font, which leaves the
        /// numbers slightly ragged but still readable.
        /// </summary>
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

        /// <summary>
        /// A font that can actually draw the elemental kanji on the player tags.
        ///
        /// The built-in LegacyRuntime.ttf is Liberation Sans, which has no CJK
        /// glyphs at all: asking it for 火 does not fall back to anything, it
        /// draws nothing, and the tag would silently come out as " 10 - FW"
        /// with a gap where the element should be. So the tag is given an OS
        /// font instead, tried in order of how likely it is to be installed on
        /// a Windows machine.
        ///
        /// Falls back to the UI font if none of them resolve. The label is still
        /// readable that way — it just loses the badge — which is a better
        /// failure than a scene that will not generate.
        /// </summary>
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

        /// <summary>
        /// Without an EventSystem the resolve button never receives a click, and
        /// the duel — which now has no auto-resolution — would hang the match at
        /// timeScale 0. The module must be the Input System one: the project has
        /// the new backend enabled, where StandaloneInputModule throws.
        /// </summary>
        private static void CreateEventSystem()
        {
            GameObject eventSystemObject = new GameObject("EventSystem");
            Undo.RegisterCreatedObjectUndo(eventSystemObject, "Create EventSystem");

            eventSystemObject.AddComponent<EventSystem>();
            eventSystemObject.AddComponent<InputSystemUIInputModule>();
        }

        /// <summary>
        /// Builds the match rig: a perspective camera set well behind the play,
        /// high up and tilted forward, so the pitch runs away into the distance
        /// the way it does in a televised match instead of reading as a map.
        /// </summary>
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

                // Perspective is not cosmetic here: the duel staging works by
                // physically moving the camera to five metres behind a player,
                // and under an orthographic projection that changes the angle
                // but not the size of a single thing on screen.
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

            // Its resting pose is the rig above, not the component's own
            // defaults: those frame a smaller pitch, and on this 30 x 50 one
            // they would leave both goals permanently off screen.
            Undo.RecordObject(tacticalCamera, "Configure Tactical Camera");

            tacticalCamera.ConfigureOverhead(rigOffset, rigRotation);

            tacticalCamera.ConfigureZoom(CameraMinZoom, CameraMaxZoom, CameraZoomSensitivity);

            tacticalCamera.ConfigureClashFraming(
                ClashCameraBackDistance, ClashCameraHeight, ClashCameraFieldOfView);

            tacticalCamera.ConfigureBallFlightFraming(
                BallFlightCameraBackDistance, BallFlightCameraHeight, BallFlightCameraFieldOfView);

            EditorUtility.SetDirty(tacticalCamera);
        }

        /// <summary>
        /// Where the follow rig is allowed to put the camera before its frustum
        /// overruns the pitch.
        ///
        /// A tilted perspective camera does not see a rectangle centred on
        /// itself the way an orthographic one does — it sees a trapezoid opening
        /// away in front of it. So the budget is worked out from the two edges
        /// that matter: the bottom of the frame hits the ground
        /// <c>height / tan(pitch + halfFov)</c> in front of the camera, the top
        /// of the frame <c>height / tan(pitch - halfFov)</c>. Both have to stay
        /// inside the pitch plus the deliberate overshoot, which is what makes
        /// the Z limits asymmetric: the camera trails the play, so it can always
        /// drop further back than it can push forward.
        ///
        /// X comes out at zero on any normal window — the near edge of the frame
        /// is already ~18 units wide against a 15-unit half-pitch — which is
        /// exactly why manual panning exists.
        /// </summary>
        private static void CalculateCameraBounds(float aspect, out Vector2 minBounds, out Vector2 maxBounds)
        {
            float halfFov = CameraFieldOfView * 0.5f;

            // Clamped so a wide enough lens, whose top edge would rise above the
            // horizon, yields a large finite distance instead of a division by
            // something at or through zero.
            float topAngle = Mathf.Max(1f, CameraPitchAngle - halfFov);
            float bottomAngle = Mathf.Min(89f, CameraPitchAngle + halfFov);

            float nearForward = CameraHeight / Mathf.Tan(topAngle * Mathf.Deg2Rad);
            float farForward = CameraHeight / Mathf.Tan(bottomAngle * Mathf.Deg2Rad);

            // Ordered rather than assumed: "near" is whichever edge lands closer.
            float closest = Mathf.Min(nearForward, farForward);
            float furthest = Mathf.Max(nearForward, farForward);

            float reach = PitchHalfLength + CameraExtraGoalPan;

            float minZ = -reach - closest;
            float maxZ = reach - furthest;

            // A lens wide enough to swallow the whole pitch leaves no travel at
            // all; locking to the one position that frames it beats inverting.
            if (maxZ < minZ)
            {
                float locked = (minZ + maxZ) * 0.5f;
                minZ = locked;
                maxZ = locked;
            }

            // Half-width of the frame where it is narrowest: at the near edge,
            // whose slant range from the camera is height / sin(bottom angle).
            float nearSlantRange = CameraHeight / Mathf.Sin(bottomAngle * Mathf.Deg2Rad);
            float visibleHalfWidth = nearSlantRange * Mathf.Tan(halfFov * Mathf.Deg2Rad) * aspect;

            float limitX = Mathf.Max(0f, PitchHalfWidth - visibleHalfWidth);

            minBounds = new Vector2(-limitX, minZ);
            maxBounds = new Vector2(limitX, maxZ);
        }

        // ---------------------------------------------------------------------
        // Procedural texture generation
        // ---------------------------------------------------------------------

        /// <summary>
        /// Paints a football pitch: mown grass stripes plus the outer boundary,
        /// the halfway line and the centre circle. The texture keeps the pitch's
        /// own aspect ratio so the painted lines end up equally thick along both
        /// axes once stretched over the plane's 0-1 UVs.
        /// </summary>
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

            // The centre spot. Drawn as a filled disc by giving the outline a
            // thickness equal to its own radius, which is what closes the hole
            // in the middle — there is no fill-circle helper here and a spot
            // this small does not warrant one.
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

        /// <summary>
        /// Paints both penalty boxes onto the pitch texture, from the same
        /// numbers the foul rules are judged with.
        ///
        /// Painted rather than built out of objects because that is how every
        /// other line on this pitch already works — the boundary, the halfway
        /// line and the centre circle are all pixels — and a box made of meshes
        /// would be the one marking that could z-fight with the grass or catch a
        /// route raycast.
        ///
        /// The mapping is uniform: the pitch spans 30 x 50 units over a texture
        /// that is `width` by `width * 5/3`, so one pixel is 30/width units on
        /// both axes and a single scale converts either of them.
        /// </summary>
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

            // Red defends north (+Z), Blue defends south (-Z). One box each.
            TextureDrawing.DrawRectOutline(pixels, width, height,
                left, toPixelY(front), right, toPixelY(edge), thickness, lineColor);

            TextureDrawing.DrawRectOutline(pixels, width, height,
                left, toPixelY(-edge), right, toPixelY(-front), thickness, lineColor);
        }

        /// <summary>
        /// A simple two-tone checker so the ball's rotation stays readable while
        /// it rolls, which a flat colour would hide.
        /// </summary>
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

        /// <summary>
        /// A small gear, drawn from primitives rather than imported art: a
        /// filled disc, eight square teeth placed around its rim by angle, and a
        /// smaller disc punched back out of the middle in full transparency —
        /// which is what turns a solid circle into a recognisable gear/settings
        /// icon at a glance, the universal shorthand for "tools live here".
        /// </summary>
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

        /// <summary>
        /// Bounce needs <see cref="PhysicsMaterialCombine.Maximum"/>: the pitch
        /// has no physics material, so the default Average combine would halve
        /// the bounciness against its implicit zero and barely register.
        /// </summary>
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

        // FillRect / DrawRectOutline / DrawCircleOutline moved to
        // TextureDrawing.cs — generic pixel-buffer primitives with no
        // knowledge of the pitch.

        // ---------------------------------------------------------------------
        // Asset helpers
        // ---------------------------------------------------------------------

        /// <summary>
        /// Returns the asset already sitting at the generated path, or builds it
        /// with <paramref name="factory"/> and persists it. Reusing the existing
        /// asset keeps references stable across repeated generations.
        /// </summary>
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

        /// <summary>
        /// The fire-coloured disc under a player in the zone. Transparent, so it
        /// needs the full URP alpha-blend setup rather than just an alpha on the
        /// colour — URP ships its shaders opaque and would draw a solid plate.
        /// </summary>
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

        /// <summary>
        /// The pitch texture, REPAINTED rather than merely reused.
        ///
        /// Plain GetOrCreateAsset is wrong for this one. It keeps whatever asset
        /// is already on disk, which is right for a material or a sprite but not
        /// for a texture whose contents are computed from constants that change:
        /// the penalty boxes are drawn from PitchBounds, so a cached texture
        /// would keep showing the markings of whatever the numbers used to be,
        /// and no amount of regenerating the scene would ever update them.
        ///
        /// The existing asset is repainted in place instead of being replaced, so
        /// the material still points at the same object and nothing has to be
        /// re-linked.
        /// </summary>
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

            // The freshly built one was only ever a source of pixels; leaving it
            // alive would leak a texture per generation.
            UnityEngine.Object.DestroyImmediate(fresh);

            return existing;
        }

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

        /// <summary>
        /// Stat blocks are shared assets, so a squad points at two of them
        /// rather than carrying a private copy each.
        ///
        /// Values are rewritten on every run, not just on creation: these two
        /// assets ARE the archetype definition, and an asset left over from an
        /// older run would otherwise keep stale numbers — which is exactly what
        /// happened when power and block were added and the existing files
        /// stayed at the default 50. The cost is that hand-tuning these two in
        /// the inspector does not survive a regeneration.
        /// </summary>
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

        /// <summary>
        /// A plain white sprite, used as the fill of the stamina bars.
        ///
        /// Not optional dressing: an Image with no sprite falls back to drawing a
        /// simple quad and ignores its <c>type</c> entirely, so a Filled bar
        /// without one would render at full width for ever and fillAmount would
        /// do nothing at all.
        ///
        /// The texture is persisted as its own asset before the sprite is built
        /// from it — a sprite pointing at an in-memory texture comes back broken
        /// after a domain reload.
        /// </summary>
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

        /// <summary>
        /// The shockwave's material: unlit, so the ring is emissive rather than
        /// shaded dark on its underside, and genuinely transparent — URP ships
        /// its shaders opaque, and without flipping the surface type the alpha
        /// the effect animates would do nothing at all.
        /// </summary>
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

        /// <summary>
        /// The goal netting: white and barely there, so the ball stays visible
        /// through it. Lit rather than unlit, unlike the shockwave — this is a
        /// real object sitting in the world and it should take the same light
        /// the goal frame does.
        /// </summary>
        /// <summary>
        /// The blob under the ball. Unlit on purpose: a shadow that took the
        /// scene lighting would be brightest where the sun hits hardest, which
        /// is precisely backwards.
        /// </summary>
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

        /// <summary>
        /// Flips a material to alpha blending. URP ships its shaders opaque, so
        /// without this the alpha on the colour above is simply ignored and the
        /// net comes out a solid white wall.
        /// </summary>
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

        private static void ApplyRouteLineMaterial(GameObject player)
        {
            if (player.TryGetComponent(out LineRenderer lineRenderer))
            {
                lineRenderer.sharedMaterial = GetOrCreateLineMaterial();
            }
        }

        /// <summary>
        /// Route lines want a flat, unlit white: a lit shader would shade the
        /// strip by the sun angle and make the path read unevenly across the pitch.
        /// </summary>
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

        /// <summary>
        /// Resolves the shader the active render pipeline expects. Hardcoding a
        /// URP shader name would break the moment the project switches pipeline.
        /// </summary>
        private static Shader GetDefaultShader()
        {
            RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;
            if (pipeline != null && pipeline.defaultShader != null)
            {
                return pipeline.defaultShader;
            }

            return Shader.Find("Standard");
        }

        private static void EnsureGeneratedFolder()
        {
            if (!AssetDatabase.IsValidFolder(GeneratedFolder))
            {
                AssetDatabase.CreateFolder("Assets", "Generated");
            }
        }

        private static void ApplyMaterial(GameObject target, Material material)
        {
            if (target.TryGetComponent(out Renderer renderer))
            {
                renderer.sharedMaterial = material;
            }
        }

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
