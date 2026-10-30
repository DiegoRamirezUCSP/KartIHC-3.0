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

    private enum MenuPage { Modes, EscapeRoles, Waiting }

    private static EscapeModeMenuSelector instance;
    private MenuPage page;
    private bool visible = true;
    private bool initialized;
    private GameState lastState = GameState.MainMenu;
    private GameStateManager stateManager;
    private UIController uiController;
    private EscapeModeSessionController sessionController;
    private VoicePowerUpInput voiceInput;
    private GUIStyle panelStyle;
    private GUIStyle titleStyle;
    private GUIStyle buttonStyle;
    private GUIStyle bodyStyle;
    private bool startRaceAfterSceneLoad;
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

        VoicePowerUpInput foundVoiceInput = FindFirstObjectByType<VoicePowerUpInput>();
        if (voiceInput == foundVoiceInput) return;
        if (voiceInput != null)
        {
            voiceInput.StartRaceRequested -= HandleStartRaceVoice;
            voiceInput.TutorialRequested -= HandleTutorialVoice;
            voiceInput.HackerPracticeRequested -= HandleHackerPracticeVoice;
            voiceInput.ModeMenuBackRequested -= HandleModeMenuBackVoice;
            voiceInput.EscapeModeMenuRequested -= OpenEscapeRoles;
            voiceInput.EscapeModeHostRequested -= ShowWaitingForEscape;
            voiceInput.EscapeModeJoinRequested -= ShowWaitingForEscape;
        }

        voiceInput = foundVoiceInput;
        if (voiceInput != null)
        {
            voiceInput.StartRaceRequested += HandleStartRaceVoice;
            voiceInput.TutorialRequested += HandleTutorialVoice;
            voiceInput.HackerPracticeRequested += HandleHackerPracticeVoice;
            voiceInput.ModeMenuBackRequested += HandleModeMenuBackVoice;
            voiceInput.EscapeModeMenuRequested += OpenEscapeRoles;
            voiceInput.EscapeModeHostRequested += ShowWaitingForEscape;
            voiceInput.EscapeModeJoinRequested += ShowWaitingForEscape;
        }
    }

    private void HandleStartRaceVoice()
    {
        if (SceneManager.GetActiveScene().name == ModeMenuSceneName)
            StartCareerMode();
    }

    private void HandleTutorialVoice(int tutorialId)
    {
        if (SceneManager.GetActiveScene().name != ModeMenuSceneName) return;
        tutorialAfterSceneLoad = tutorialId;
        hackerTutorialAfterSceneLoad = tutorialId == 4;
        startRaceAfterSceneLoad = false;
        visible = false;
        uiController?.SetMainMenuVisible(false);
        SceneManager.LoadScene(tutorialId == 4 ? EscapeSceneName : CareerSceneName, LoadSceneMode.Single);
    }

    private void HandleHackerPracticeVoice()
    {
        if (SceneManager.GetActiveScene().name != ModeMenuSceneName) return;
        hackerPracticeAfterSceneLoad = true;
        hackerTutorialAfterSceneLoad = false;
        tutorialAfterSceneLoad = 0;
        startRaceAfterSceneLoad = false;
        visible = false;
        SceneManager.LoadScene(EscapeSceneName, LoadSceneMode.Single);
    }

    private void HandleModeMenuBackVoice()
    {
        if (SceneManager.GetActiveScene().name == "EscapeCourse")
        {
            sessionController?.ExitEscapeMode();
            return;
        }

        if (stateManager != null && stateManager.CurrentState == GameState.Tutorial)
        {
            stateManager.ReturnToMainMenu();
            return;
        }

        if (page == MenuPage.Waiting)
        {
            sessionController?.ExitEscapeMode();
            page = MenuPage.Modes;
        }
        else if (page == MenuPage.EscapeRoles)
        {
            page = MenuPage.Modes;
        }
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == ModeMenuSceneName)
        {
            page = MenuPage.Modes;
            visible = true;
        }
        if ((scene.name == CareerSceneName && (startRaceAfterSceneLoad || (tutorialAfterSceneLoad > 0 && tutorialAfterSceneLoad < 4))) ||
            (scene.name == EscapeSceneName && (hackerTutorialAfterSceneLoad || hackerPracticeAfterSceneLoad)))
            StartCoroutine(BeginSelectedCareerMode());
    }

    private System.Collections.IEnumerator BeginSelectedCareerMode()
    {
        // Wait for the newly loaded scene's GameStateManager.Start().
        yield return null;
        stateManager = FindFirstObjectByType<GameStateManager>();
        uiController = FindFirstObjectByType<UIController>();
        if (hackerPracticeAfterSceneLoad)
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
            EscapeModeNetworkBridge.StartForRole(false);
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

    private void OnGUI()
    {
        bool inModeMenu = SceneManager.GetActiveScene().name == ModeMenuSceneName;
        if (!visible || (!inModeMenu && (stateManager == null || stateManager.CurrentState != GameState.MainMenu)))
            return;

        EnsureStyles();
        const float width = 620f;
        const float height = 510f;
        Rect panel = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);
        GUILayout.BeginArea(panel, GUIContent.none, panelStyle);
        GUILayout.Space(24f);
        GUILayout.Label("KART IHC", titleStyle);
        GUILayout.Space(8f);

        if (page == MenuPage.Modes)
        {
            GUILayout.Label("Di «iniciar carrera» para la pista normal o «modo escape» para jugar en equipo.", bodyStyle);
            GUILayout.Space(18f);
            if (GUILayout.Button("CARRERA", buttonStyle, GUILayout.Height(64f)))
                StartCareerMode();
            GUILayout.Space(12f);
            if (GUILayout.Button("MODO ESCAPE", buttonStyle, GUILayout.Height(64f)))
                page = MenuPage.EscapeRoles;
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
            GUILayout.Label("Los controles en pantalla son respaldo; el juego se maneja por voz y dispositivos.", bodyStyle);
        }
        else if (page == MenuPage.EscapeRoles)
        {
            GUILayout.Label("Conductor: «activar modo escape». Hacker: «buscar modo escape». Buscar no crea salas ni cambia el rol hacker.", bodyStyle);
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
            if (GUILayout.Button("VOLVER", buttonStyle, GUILayout.Height(44f)))
                page = MenuPage.Modes;
        }
        else
        {
            GUILayout.Label(sessionController != null ? sessionController.Status : "Preparando Modo Escape...", bodyStyle);
            if (sessionController != null && sessionController.SearchFailed)
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
            else
                GUILayout.Label("También puedes decir «salir modo escape» para cancelar.", bodyStyle);
            if (EscapeModeHudFeed.Messages.Count > 0)
            {
                GUILayout.Space(8f);
                foreach (string message in EscapeModeHudFeed.Messages)
                    GUILayout.Label("• " + message, bodyStyle);
            }
            GUILayout.Space(20f);
            if (GUILayout.Button("CANCELAR / VOLVER", buttonStyle, GUILayout.Height(50f)))
            {
                if (sessionController != null) sessionController.ExitEscapeMode();
                page = MenuPage.Modes;
            }
        }

        GUILayout.EndArea();
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
            voiceInput.TutorialRequested -= HandleTutorialVoice;
            voiceInput.HackerPracticeRequested -= HandleHackerPracticeVoice;
            voiceInput.ModeMenuBackRequested -= HandleModeMenuBackVoice;
            voiceInput.EscapeModeMenuRequested -= OpenEscapeRoles;
            voiceInput.EscapeModeHostRequested -= ShowWaitingForEscape;
            voiceInput.EscapeModeJoinRequested -= ShowWaitingForEscape;
        }
        if (instance == this) instance = null;
    }
}
