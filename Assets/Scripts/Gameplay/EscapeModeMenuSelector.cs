using KartIHC.Gameplay;
using KartIHC.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Replaces the initial menu with Carrera/Escape choices and Escape role selection.</summary>
public sealed class EscapeModeMenuSelector : MonoBehaviour
{
    private const string ModeMenuSceneName = "ModeMenu";
    private const string CareerSceneName = "SampleScene";
    private const string EscapeSceneName = "EscapeCourse";

    private enum MenuPage { Modes, Commands, EscapeRoles, Waiting }

    private static EscapeModeMenuSelector instance;
    private MenuPage page;
    private bool visible = true;
    private bool initialized;
    private GameState lastState = GameState.MainMenu;
    private GameStateManager stateManager;
    private UIController uiController;
    private EscapeModeSessionController sessionController;
    private PythonEscapeSessionController pythonSessionController;
    private VoicePowerUpInput voiceInput;
    private GUIStyle panelStyle;
    private GUIStyle titleStyle;
    private GUIStyle buttonStyle;
    private GUIStyle bodyStyle;
    private GUIStyle commandHeadingStyle;
    private GUIStyle commandBodyStyle;
    private Vector2 menuScroll;
    private bool startRaceAfterSceneLoad;
    private bool soloEscapeAfterSceneLoad;
    private int tutorialAfterSceneLoad;
    private bool hackerTutorialAfterSceneLoad;
    private bool hackerPracticeAfterSceneLoad;
    private string preparedSceneName = "";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void EnsureExists()
    {
        if (FindFirstObjectByType<EscapeModeMenuSelector>() != null) return;
        new GameObject("EscapeModeMenuSelector").AddComponent<EscapeModeMenuSelector>();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += HandleSceneLoaded;
        RefreshReferences();
    }

    private void Update()
    {
        RefreshReferences();
        PrepareSceneForModeMenu();
        if (SceneManager.GetActiveScene().name == ModeMenuSceneName)
        {
            visible = true;
            uiController?.SetMainMenuVisible(false);
            return;
        }
        if (stateManager == null) return;

        GameState state = stateManager.CurrentState;
        if (initialized && lastState != GameState.MainMenu && state == GameState.MainMenu)
        {
            page = MenuPage.Modes;
            visible = true;
        }
        if (state != GameState.MainMenu)
            visible = false;

        lastState = state;
        initialized = true;

        if (visible)
            uiController?.SetMainMenuVisible(false);
    }

    private void PrepareSceneForModeMenu()
    {
        string activeSceneName = SceneManager.GetActiveScene().name;
        if (preparedSceneName == activeSceneName) return;
        preparedSceneName = activeSceneName;
        if (activeSceneName != ModeMenuSceneName) return;

        // Keep the menu scene visually distinct; the full race track is loaded
        // only after Carrera is chosen.
        GameObject track = GameObject.Find("Track");
        if (track != null) track.SetActive(false);
        LapManager lapManager = FindFirstObjectByType<LapManager>();
        if (lapManager != null) lapManager.enabled = false;
        foreach (RaceCheckpoint checkpoint in FindObjectsByType<RaceCheckpoint>(FindObjectsSortMode.None))
            checkpoint.gameObject.SetActive(false);
    }

    private void RefreshReferences()
    {
        if (stateManager == null) stateManager = FindFirstObjectByType<GameStateManager>();
        if (uiController == null) uiController = FindFirstObjectByType<UIController>();
        if (sessionController == null) sessionController = EscapeModeSessionController.Instance;
        if (pythonSessionController == null) pythonSessionController = PythonEscapeSessionController.Instance;

        VoicePowerUpInput foundVoiceInput = FindFirstObjectByType<VoicePowerUpInput>();
        if (voiceInput == foundVoiceInput) return;
        if (voiceInput != null)
        {
            voiceInput.StartRaceRequested -= HandleStartRaceVoice;
            voiceInput.SoloEscapeRequested -= StartSoloEscape;
            voiceInput.TutorialRequested -= HandleTutorialVoice;
            voiceInput.CancelTutorialRequested -= HandleCancelTutorialVoice;
            voiceInput.RestartRequested -= HandleRestartVoice;
            voiceInput.ControlsRequested -= HandleCommandsVoice;
            voiceInput.ExitModeRequested -= HandleExitModeVoice;
            voiceInput.HackerPracticeRequested -= HandleHackerPracticeVoice;
            voiceInput.ModeMenuBackRequested -= HandleModeMenuBackVoice;
            voiceInput.EscapeModeMenuRequested -= OpenEscapeRoles;
            voiceInput.EscapeModeHostRequested -= ShowWaitingForEscape;
            voiceInput.EscapeModeJoinRequested -= ShowWaitingForEscape;
            voiceInput.LocalEscapeHostRequested -= ShowWaitingForPythonEscape;
            voiceInput.LocalEscapeJoinRequested -= ShowWaitingForPythonEscape;
        }

        voiceInput = foundVoiceInput;
        if (voiceInput != null)
        {
            voiceInput.StartRaceRequested += HandleStartRaceVoice;
            voiceInput.SoloEscapeRequested += StartSoloEscape;
            voiceInput.TutorialRequested += HandleTutorialVoice;
            voiceInput.CancelTutorialRequested += HandleCancelTutorialVoice;
            voiceInput.RestartRequested += HandleRestartVoice;
            voiceInput.ControlsRequested += HandleCommandsVoice;
            voiceInput.ExitModeRequested += HandleExitModeVoice;
            voiceInput.HackerPracticeRequested += HandleHackerPracticeVoice;
            voiceInput.ModeMenuBackRequested += HandleModeMenuBackVoice;
            voiceInput.EscapeModeMenuRequested += OpenEscapeRoles;
            voiceInput.EscapeModeHostRequested += ShowWaitingForEscape;
            voiceInput.EscapeModeJoinRequested += ShowWaitingForEscape;
            voiceInput.LocalEscapeHostRequested += ShowWaitingForPythonEscape;
            voiceInput.LocalEscapeJoinRequested += ShowWaitingForPythonEscape;
        }
    }

    private void HandleStartRaceVoice()
    {
        if (SceneManager.GetActiveScene().name == ModeMenuSceneName)
            StartCareerMode();
    }

    private void HandleCommandsVoice()
    {
        if (SceneManager.GetActiveScene().name != ModeMenuSceneName) return;
        page = MenuPage.Commands;
        menuScroll = Vector2.zero;
        visible = true;
    }

    private void StartSoloEscape()
    {
        if (soloEscapeAfterSceneLoad) return;
        page = MenuPage.Modes;
        visible = false;
        soloEscapeAfterSceneLoad = true;
        startRaceAfterSceneLoad = false;
        tutorialAfterSceneLoad = 0;
        SceneManager.LoadScene(EscapeSceneName, LoadSceneMode.Single);
    }

    private void HandleCancelTutorialVoice()
    {
        if (SceneManager.GetActiveScene().name == EscapeSceneName &&
            EscapePoliceStationGenerator.Instance != null)
        {
            ReturnToModeMenu();
            return;
        }

        EscapeModeNetworkBridge bridge = EscapeModeNetworkBridge.Instance;
        if (SceneManager.GetActiveScene().name == EscapeSceneName && bridge != null &&
            bridge.IsHacker && bridge.IsOfflinePractice)
            ReturnToModeMenu();
    }

    private void HandleRestartVoice()
    {
        if (SceneManager.GetActiveScene().name != EscapeSceneName) return;
        GameStateManager activeStateManager = FindFirstObjectByType<GameStateManager>();
        if (activeStateManager != null && activeStateManager.CurrentState == GameState.Tutorial) return;

        if (EscapePoliceStationGenerator.Instance != null)
        {
            EscapeModeNetworkBridge.Stop();
            soloEscapeAfterSceneLoad = true;
            hackerPracticeAfterSceneLoad = false;
            hackerTutorialAfterSceneLoad = false;
            SceneManager.LoadScene(EscapeSceneName, LoadSceneMode.Single);
            return;
        }

        EscapeModeNetworkBridge bridge = EscapeModeNetworkBridge.Instance;
        if (bridge != null && bridge.IsOfflinePractice)
        {
            EscapeModeNetworkBridge.Stop();
            hackerPracticeAfterSceneLoad = true;
            hackerTutorialAfterSceneLoad = false;
            soloEscapeAfterSceneLoad = false;
            SceneManager.LoadScene(EscapeSceneName, LoadSceneMode.Single);
            return;
        }

        if (PythonEscapeSessionController.Instance != null && PythonEscapeSessionController.Instance.IsActive)
        {
            PythonEscapeSessionController.Instance.RestartEscapeScene();
            return;
        }

        EscapeModeSessionController.Instance?.RestartEscapeMode();
    }

    private void HandleExitModeVoice()
    {
        if (SceneManager.GetActiveScene().name == ModeMenuSceneName)
        {
            HandleModeMenuBackVoice();
            return;
        }

        if (SceneManager.GetActiveScene().name == EscapeSceneName)
        {
            if (PythonEscapeSessionController.Instance != null && PythonEscapeSessionController.Instance.IsActive)
            {
                PythonEscapeSessionController.Instance.ExitLocalSession();
                return;
            }
            if (EscapeModeSessionController.Instance != null &&
                EscapeModeSessionController.Instance.HasActiveOrOngoingSession)
            {
                EscapeModeSessionController.Instance.ExitEscapeMode();
                return;
            }
            ReturnToModeMenu();
            return;
        }

        Time.timeScale = 1f;
        EscapeModeNetworkBridge.Stop();
        PythonEscapeModeBridge.Stop();
        EscapeCourseBuilder.RestoreStandardCourse();
        page = MenuPage.Modes;
        visible = true;
        SceneManager.LoadScene(ModeMenuSceneName, LoadSceneMode.Single);
    }

    public void ReturnToModeMenu()
    {
        soloEscapeAfterSceneLoad = false;
        hackerPracticeAfterSceneLoad = false;
        hackerTutorialAfterSceneLoad = false;
        startRaceAfterSceneLoad = false;
        tutorialAfterSceneLoad = 0;
        EscapePoliceStationGenerator.Stop();
        EscapeModeNetworkBridge.Stop();
        EscapeCourseBuilder.RestoreStandardCourse();
        page = MenuPage.Modes;
        visible = false;
        SceneManager.LoadScene(ModeMenuSceneName, LoadSceneMode.Single);
    }

    private void HandleTutorialVoice(int tutorialId)
    {
        if (tutorialId == 4)
        {
            StartHackerMode(tutorial: true);
            return;
        }
        if (SceneManager.GetActiveScene().name != ModeMenuSceneName) return;
        tutorialAfterSceneLoad = tutorialId;
        hackerTutorialAfterSceneLoad = false;
        startRaceAfterSceneLoad = false;
        visible = false;
        uiController?.SetMainMenuVisible(false);
        SceneManager.LoadScene(CareerSceneName, LoadSceneMode.Single);
    }

    private void HandleHackerPracticeVoice()
    {
        StartHackerMode(tutorial: false);
    }

    private void StartHackerMode(bool tutorial)
    {
        hackerPracticeAfterSceneLoad = !tutorial;
        hackerTutorialAfterSceneLoad = tutorial;
        tutorialAfterSceneLoad = 0;
        startRaceAfterSceneLoad = false;
        visible = false;
        uiController?.SetMainMenuVisible(false);

        if (SceneManager.GetActiveScene().name == EscapeSceneName)
            StartCoroutine(BeginSelectedCareerMode());
        else
            SceneManager.LoadScene(EscapeSceneName, LoadSceneMode.Single);
    }

    private void HandleModeMenuBackVoice()
    {
        if (SceneManager.GetActiveScene().name == "EscapeCourse")
        {
            if (EscapePoliceStationGenerator.Instance != null)
            {
                ReturnToModeMenu();
                return;
            }
            if (EscapeModeNetworkBridge.Instance != null && EscapeModeNetworkBridge.Instance.IsHacker)
            {
                if (EscapeModeNetworkBridge.Instance.IsOfflinePractice) ReturnToModeMenu();
                else sessionController?.ExitEscapeMode();
                return;
            }
            sessionController?.ExitEscapeMode();
            return;
        }

        if (SceneManager.GetActiveScene().name == ModeMenuSceneName)
        {
            if (page == MenuPage.Waiting)
            {
                pythonSessionController?.ExitLocalSession();
                sessionController?.ExitEscapeMode();
                page = MenuPage.Modes;
            }
            else if (page == MenuPage.EscapeRoles)
            {
                page = MenuPage.Modes;
            }
            else if (page == MenuPage.Commands)
            {
                page = MenuPage.Modes;
                menuScroll = Vector2.zero;
            }
            return;
        }

        HandleExitModeVoice();
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == ModeMenuSceneName)
        {
            page = MenuPage.Modes;
            visible = true;
            if (soloEscapeAfterSceneLoad)
                StartCoroutine(BeginSelectedCareerMode());
        }
        if ((scene.name == CareerSceneName && (startRaceAfterSceneLoad || (tutorialAfterSceneLoad > 0 && tutorialAfterSceneLoad < 4))) ||
            (scene.name == EscapeSceneName && soloEscapeAfterSceneLoad) ||
            (scene.name == EscapeSceneName && (hackerTutorialAfterSceneLoad || hackerPracticeAfterSceneLoad)))
            StartCoroutine(BeginSelectedCareerMode());
    }

    private System.Collections.IEnumerator BeginSelectedCareerMode()
    {
        // Wait for the newly loaded scene's GameStateManager.Start().
        yield return null;
        stateManager = FindFirstObjectByType<GameStateManager>();
        uiController = FindFirstObjectByType<UIController>();
        if (soloEscapeAfterSceneLoad)
        {
            EscapeModeHudFeed.Clear();
            EscapeCourseBuilder.Build();
            stateManager?.BeginPlaying();
            EscapeModeNetworkBridge.StartForRole(true, true);
            EscapeModeHudFeed.Publish("Escape Solo: cuatro patrullas activas. Llega a la zona de salida.");
        }
        else if (hackerPracticeAfterSceneLoad)
        {
            EscapeModeHudFeed.Clear();
            EscapeCourseBuilder.Build();
            stateManager?.BeginPlaying();
            EscapeModeNetworkBridge.StartForRole(false, true);
            EscapeModeHudFeed.Publish("Práctica Hacker iniciada sin conductor ni conexión de red.");
        }
        else if (hackerTutorialAfterSceneLoad)
        {
            EscapeCourseBuilder.Build();
            EscapeModeNetworkBridge.StartForRole(false, true);
            stateManager?.BeginTutorial(4);
        }
        else if (tutorialAfterSceneLoad > 0)
            stateManager?.BeginTutorial(tutorialAfterSceneLoad);
        else if (startRaceAfterSceneLoad)
            stateManager?.BeginPlaying();

        tutorialAfterSceneLoad = 0;
        hackerTutorialAfterSceneLoad = false;
        hackerPracticeAfterSceneLoad = false;
        startRaceAfterSceneLoad = false;
        soloEscapeAfterSceneLoad = false;
    }

    private void StartCareerMode()
    {
        page = MenuPage.Modes;
        visible = false;
        uiController?.SetMainMenuVisible(false);

        if (SceneManager.GetActiveScene().name == ModeMenuSceneName)
        {
            startRaceAfterSceneLoad = true;
            tutorialAfterSceneLoad = 0;
            SceneManager.LoadScene(CareerSceneName, LoadSceneMode.Single);
            return;
        }

        stateManager?.BeginPlaying();
    }

    private void OpenEscapeRoles()
    {
        if (stateManager != null && stateManager.CurrentState != GameState.MainMenu) return;
        page = MenuPage.EscapeRoles;
        visible = true;
        uiController?.SetMainMenuVisible(false);
    }

    private void ShowWaitingForEscape()
    {
        page = MenuPage.Waiting;
        visible = stateManager == null || stateManager.CurrentState == GameState.MainMenu;
        if (visible) uiController?.SetMainMenuVisible(false);
    }

    private void ShowWaitingForPythonEscape()
    {
        page = MenuPage.Waiting;
        visible = SceneManager.GetActiveScene().name == ModeMenuSceneName ||
                  stateManager == null || stateManager.CurrentState == GameState.MainMenu;
        if (visible) uiController?.SetMainMenuVisible(false);
    }

    public void ReturnToModesPage()
    {
        page = MenuPage.Modes;
        visible = true;
    }

    private void OnGUI()
    {
        bool inModeMenu = SceneManager.GetActiveScene().name == ModeMenuSceneName;
        if (!visible || (!inModeMenu && (stateManager == null || stateManager.CurrentState != GameState.MainMenu)))
            return;

        EnsureStyles();
        float width = Mathf.Min(620f, Screen.width - 32f);
        float height = Mathf.Min(Screen.height - 32f,
            page == MenuPage.Commands ? 760f : page == MenuPage.EscapeRoles ? 650f : page == MenuPage.Waiting ? 570f : 600f);
        Rect panel = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);
        GUILayout.BeginArea(panel, GUIContent.none, panelStyle);
        GUILayout.Space(24f);
        GUILayout.Label(page == MenuPage.Commands ? "COMANDOS DE VOZ" : "KART IHC", titleStyle);
        GUILayout.Space(8f);
        if (page == MenuPage.Modes && GUILayout.Button("GUÍA COMPLETA DE COMANDOS DE VOZ", buttonStyle, GUILayout.Height(50f)))
        {
            page = MenuPage.Commands;
            menuScroll = Vector2.zero;
        }
        float reservedHeight = page == MenuPage.Commands ? 166f : page == MenuPage.Modes ? 164f : 112f;
        menuScroll = GUILayout.BeginScrollView(menuScroll, GUILayout.Width(width - 56f), GUILayout.Height(height - reservedHeight));

        if (page == MenuPage.Modes)
        {
            GUILayout.Label("Di «iniciar carrera» para la pista normal. Di «ayuda» o «controles» para abrir la guía. En Escape puedes usar Internet (Relay) o la misma Wi-Fi (Python).", bodyStyle);
            GUILayout.Space(18f);
            if (GUILayout.Button("CARRERA", buttonStyle, GUILayout.Height(64f)))
                StartCareerMode();
            GUILayout.Space(12f);
            if (GUILayout.Button("MODO ESCAPE", buttonStyle, GUILayout.Height(64f)))
                page = MenuPage.EscapeRoles;
            GUILayout.Space(8f);
            if (GUILayout.Button("ESCAPE SOLO · PATRULLAS", buttonStyle, GUILayout.Height(56f)))
                StartSoloEscape();
            GUILayout.Space(10f);
            if (GUILayout.Button("PRUEBA HACKER", buttonStyle, GUILayout.Height(48f)))
                HandleHackerPracticeVoice();
            GUILayout.Space(8f);
            GUILayout.Label("TUTORIALES · di «tutorial uno», «tutorial dos», «tutorial tres» o «tutorial hacker»", bodyStyle);
            string[] tutorialLabels = { "PIERNAS", "MANDO", "COMANDOS", "HACKER" };
            GUILayout.BeginHorizontal();
            for (int i = 0; i < tutorialLabels.Length; i++)
                if (GUILayout.Button(tutorialLabels[i], buttonStyle, GUILayout.Height(42f))) HandleTutorialVoice(i + 1);
            GUILayout.EndHorizontal();
            GUILayout.Label("Di «escape solo» para probar patrullas y salida. Los controles de pantalla son respaldo.", bodyStyle);
            GUILayout.Space(12f);
            if (GUILayout.Button("SALIR DEL JUEGO", buttonStyle, GUILayout.Height(44f)))
                Application.Quit();
        }
        else if (page == MenuPage.Commands)
        {
            DrawVoiceCommandGuide();
        }
        else if (page == MenuPage.EscapeRoles)
        {
            GUILayout.Label("INTERNET · Conductor: «activar modo escape». Hacker: «buscar modo escape». La búsqueda no crea salas.", bodyStyle);
            GUILayout.Space(18f);
            if (GUILayout.Button("CREAR SALA · CONDUCTOR", buttonStyle, GUILayout.Height(58f)))
            {
                page = MenuPage.Waiting;
                sessionController?.CreateDriverRoom();
            }
            GUILayout.Space(10f);
            if (GUILayout.Button("BUSCAR PARTIDA · HACKER", buttonStyle, GUILayout.Height(58f)))
            {
                page = MenuPage.Waiting;
                sessionController?.SearchForDriverRoom();
            }
            GUILayout.Space(10f);
            GUILayout.Label("MISMA WI-FI · Conductor: «activar modo escape local». Hacker: «buscar modo escape local».", bodyStyle);
            if (GUILayout.Button("CREAR SERVIDOR LOCAL · CONDUCTOR", buttonStyle, GUILayout.Height(54f)))
            {
                page = MenuPage.Waiting;
                pythonSessionController?.StartLocalServer();
            }
            GUILayout.Space(8f);
            if (GUILayout.Button("BUSCAR SERVIDOR LOCAL · HACKER", buttonStyle, GUILayout.Height(54f)))
            {
                page = MenuPage.Waiting;
                pythonSessionController?.JoinLocalServer();
            }
            GUILayout.Space(8f);
            if (GUILayout.Button("VOLVER", buttonStyle, GUILayout.Height(44f)))
                page = MenuPage.Modes;
        }
        else
        {
            bool localSessionVisible = pythonSessionController != null && pythonSessionController.ShouldShowStatus;
            GUILayout.Label(localSessionVisible
                ? pythonSessionController.Status
                : sessionController != null ? sessionController.Status : "Preparando Modo Escape...", bodyStyle);
            if (localSessionVisible)
            {
                GUILayout.Space(12f);
                GUILayout.Label("Esta alternativa requiere que ambos equipos estén conectados a la misma Wi-Fi.", bodyStyle);
                if (pythonSessionController.SearchFailed && GUILayout.Button(
                    pythonSessionController.LastAttemptWasHost ? "REINTENTAR CREAR SERVIDOR LOCAL" : "REINTENTAR BÚSQUEDA LOCAL",
                    buttonStyle, GUILayout.Height(48f)))
                {
                    if (pythonSessionController.LastAttemptWasHost) pythonSessionController.StartLocalServer();
                    else pythonSessionController.JoinLocalServer();
                }
                if (GUILayout.Button("CERRAR ESCAPE LOCAL", buttonStyle, GUILayout.Height(48f)))
                {
                    pythonSessionController.ExitLocalSession();
                    page = MenuPage.Modes;
                }
            }
            if (!localSessionVisible && sessionController != null && sessionController.SearchFailed)
            {
                GUILayout.Space(12f);
                string failureText = sessionController.LastAttemptWasHost
                    ? "La sala no pudo crearse; puedes volver a intentarlo."
                    : sessionController.NoDriverFound
                        ? "Este equipo sigue como hacker; la búsqueda no crea salas."
                        : "No se pudo completar la búsqueda; el rol permanece como hacker.";
                GUILayout.Label(failureText, bodyStyle);
                string retryLabel = sessionController.LastAttemptWasHost ? "REINTENTAR CREAR SALA" : "REINTENTAR BÚSQUEDA";
                if (GUILayout.Button(retryLabel, buttonStyle, GUILayout.Height(50f)))
                {
                    sessionController.RetryFailedAttempt();
                    page = MenuPage.Waiting;
                }
            }
            else if (!localSessionVisible)
                GUILayout.Label("También puedes decir «salir modo escape» o «salir modo escape local» para cancelar.", bodyStyle);
            if (EscapeModeHudFeed.Messages.Count > 0)
            {
                GUILayout.Space(8f);
                foreach (string message in EscapeModeHudFeed.Messages)
                    GUILayout.Label("• " + message, bodyStyle);
            }
            GUILayout.Space(20f);
            if (GUILayout.Button("CANCELAR / VOLVER", buttonStyle, GUILayout.Height(50f)))
            {
                if (localSessionVisible) pythonSessionController.ExitLocalSession();
                else if (sessionController != null) sessionController.ExitEscapeMode();
                page = MenuPage.Modes;
            }
        }

        GUILayout.EndScrollView();
        if (page == MenuPage.Commands && GUILayout.Button("VOLVER A MODOS", buttonStyle, GUILayout.Height(46f)))
        {
            page = MenuPage.Modes;
            menuScroll = Vector2.zero;
        }
        GUILayout.EndArea();
    }

    private void DrawVoiceCommandGuide()
    {
        GUILayout.Label("Voz disponible en Windows con micrófono. Las frases entre comillas son alias que ejecutan la misma acción.", commandBodyStyle);
        GUILayout.Space(10f);
        CommandSection("MENÚ Y SELECCIÓN DE MODO",
            "«empezar», «iniciar», «comenzar», «iniciar carrera», «empezar carrera» o «modo carrera»: abre la carrera normal.\n" +
            "«escape», «modo escape» o «abrir escape»: abre la selección de rol y conexión.\n" +
            "«escape solo», «modo escape solo» o «iniciar escape solo»: inicia Escape Solo con patrullas.\n" +
            "«prueba hacker» o «probar hacker»: inicia la práctica individual del dron.");

        CommandSection("TUTORIALES",
            "«tutorial uno»: piernas. «tutorial dos»: mando del kart. «tutorial tres»: comandos y potenciadores.\n" +
            "«tutorial cuatro» o «tutorial hacker»: tutorial del dron.\n" +
            "«cancelar»: abandona el tutorial y regresa a ModeMenu.");

        CommandSection("ESCAPE POR INTERNET · RELAY",
            "Conductor: «activar modo escape» crea una sala. Hacker: «buscar modo escape» busca una sala existente.\n" +
            "«salir modo escape»: cierra la sesión Escape. Buscar no crea una sala.");

        CommandSection("ESCAPE POR RED LOCAL · PYTHON",
            "Conductor: «activar modo escape local», «activar escape local» o «crear escape local»: inicia el servidor local.\n" +
            "Hacker: «buscar modo escape local», «buscar escape local» o «unirse escape local»: busca el servidor.\n" +
            "«salir modo escape local» o «salir escape local»: cierra la sesión local.");

        CommandSection("CONDUCCIÓN Y ESTADO DEL JUEGO",
            "«escudo», «turbo»/«nitro»/«tubo» y «trampa»: usa el potenciador correspondiente si está disponible.\n" +
            "«pausa»/«pausar»: pausa. «reanudar»/«continuar»: reanuda.\n" +
            "«controles»/«ayuda»: abre esta guía en ModeMenu; durante una pausa muestra los controles.\n" +
            "«reiniciar»/«reinicio»: reinicia según el modo. «salir»: regresa a ModeMenu durante el juego.\n" +
            "«volver menu»/«regresar menu»: vuelve desde una pantalla secundaria o sale del modo activo al menú de modos.");

        CommandSection("CÁMARA DE PIERNAS",
            "«activar camara»/«encender la camara»: inicia la cámara. «cambiar camara»: busca otra cámara conectada.\n" +
            "«calibrar»: calibra el seguimiento de piernas.");

        CommandSection("CHAT DE VOZ EN ESCAPE",
            "«Estoy aquí» y «Tengo problemas»: envía el mensaje y marca la posición del emisor.\n" +
            "«Ven aquí» y «Ayúdame»: coloca una marca delante del emisor para que la vea el compañero.\n" +
            "«Cuidado», «Listo», «Estoy hackeando» y «Estoy avanzando»: envía el texto al chat compartido.");

        GUILayout.Space(8f);
        GUILayout.Label("Los comandos de Escape requieren estar en una partida Escape. El reconocimiento depende de Windows, el micrófono y el ruido ambiental.", commandBodyStyle);
    }

    private void CommandSection(string heading, string description)
    {
        GUILayout.Label(heading, commandHeadingStyle);
        GUILayout.Label(description, commandBodyStyle);
        GUILayout.Space(12f);
    }

    private void EnsureStyles()
    {
        if (panelStyle == null)
        {
            panelStyle = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(28, 28, 18, 22)
            };
            panelStyle.normal.background = MakeTexture(new Color(0.04f, 0.07f, 0.11f, 0.97f));
        }

        if (titleStyle == null)
        {
            titleStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 34,
                fontStyle = FontStyle.Bold
            };
            titleStyle.normal.textColor = new Color(1f, 0.68f, 0.18f);
        }

        if (buttonStyle == null)
        {
            buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold
            };
        }

        if (bodyStyle == null)
        {
            bodyStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 17,
                wordWrap = true
            };
            bodyStyle.normal.textColor = Color.white;
        }

        if (commandHeadingStyle == null)
        {
            commandHeadingStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = 19,
                fontStyle = FontStyle.Bold,
                wordWrap = true
            };
            commandHeadingStyle.normal.textColor = new Color(1f, 0.68f, 0.18f);
        }

        if (commandBodyStyle == null)
        {
            commandBodyStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = 16,
                wordWrap = true
            };
            commandBodyStyle.normal.textColor = Color.white;
        }
    }

    private static Texture2D MakeTexture(Color color)
    {
        var texture = new Texture2D(1, 1);
        texture.SetPixel(0, 0, color);
        texture.Apply();
        return texture;
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        if (voiceInput != null)
        {
            voiceInput.StartRaceRequested -= HandleStartRaceVoice;
            voiceInput.SoloEscapeRequested -= StartSoloEscape;
            voiceInput.TutorialRequested -= HandleTutorialVoice;
            voiceInput.CancelTutorialRequested -= HandleCancelTutorialVoice;
            voiceInput.RestartRequested -= HandleRestartVoice;
            voiceInput.ControlsRequested -= HandleCommandsVoice;
            voiceInput.ExitModeRequested -= HandleExitModeVoice;
            voiceInput.HackerPracticeRequested -= HandleHackerPracticeVoice;
            voiceInput.ModeMenuBackRequested -= HandleModeMenuBackVoice;
            voiceInput.EscapeModeMenuRequested -= OpenEscapeRoles;
            voiceInput.EscapeModeHostRequested -= ShowWaitingForEscape;
            voiceInput.EscapeModeJoinRequested -= ShowWaitingForEscape;
            voiceInput.LocalEscapeHostRequested -= ShowWaitingForPythonEscape;
            voiceInput.LocalEscapeJoinRequested -= ShowWaitingForPythonEscape;
        }
        if (instance == this) instance = null;
    }
}
