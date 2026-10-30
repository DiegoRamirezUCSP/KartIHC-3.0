using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using KartIHC.Gameplay;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Escape co-op session flow. The driver creates the room and remains its host;
/// the hacker only searches for an existing Escape room and never creates one.
/// </summary>
public sealed class EscapeModeSessionController : MonoBehaviour
{
    private const string SessionType = "kartihc.escape.v1";
    private const string MainSceneName = "ModeMenu";
    private const string CareerSceneName = "SampleScene";
    private const string EscapeSceneName = "EscapeCourse";
    private const string ModeProperty = "mode";
    private const string ModeValue = "escape";
    private const string SavedSessionKey = "kartihc.escape.session";
    private const string SavedRoleKey = "kartihc.escape.role";
    private const float DriverSearchDuration = 15f;

    private static EscapeModeSessionController instance;

    [SerializeField] private VoicePowerUpInput voiceInput;
    [SerializeField] private GameStateManager gameStateManager;

    private ISession currentSession;
    private bool operationRunning;
    private bool searching;
    private bool isExiting;
    private int requestGeneration;
    private bool hostRole;
    private bool searchFailed;
    private bool lastAttemptWasHost;
    private bool hudRunStarted;
    private string status = "Modo Escape: di activar modo escape o buscar modo escape";
    private string sessionCode = "";
    private NetworkManager networkManager;
    private bool createdNetworkManager;

    public static EscapeModeSessionController Instance => instance;
    public bool IsEscapeSessionActive => currentSession != null;
    public bool IsHostDriver => currentSession != null && hostRole;
    public string Status => status;
    public string SessionCode => sessionCode;
    public bool SearchFailed => searchFailed;
    public bool NoDriverFound => searchFailed && status.StartsWith("No se encontró");
    public bool LastAttemptWasHost => lastAttemptWasHost;
    public bool ShouldShowStatus => searching || searchFailed || currentSession != null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureExists()
    {
        if (FindFirstObjectByType<EscapeModeSessionController>() != null)
            return;

        var host = new GameObject("EscapeModeSessionController");
        host.AddComponent<EscapeModeSessionController>();
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
        if (voiceInput == null) voiceInput = FindFirstObjectByType<VoicePowerUpInput>();
        if (gameStateManager == null) gameStateManager = FindFirstObjectByType<GameStateManager>();
        EnsureNetworkManager();
    }

    private void OnEnable()
    {
        BindVoice();
    }

    private void Start()
    {
        if (voiceInput == null) voiceInput = FindFirstObjectByType<VoicePowerUpInput>();
        if (gameStateManager == null) gameStateManager = FindFirstObjectByType<GameStateManager>();
        BindVoice();
    }

    private void BindVoice()
    {
        if (voiceInput == null) return;
        voiceInput.EscapeModeHostRequested -= CreateDriverRoom;
        voiceInput.EscapeModeJoinRequested -= SearchForDriverRoom;
        voiceInput.EscapeModeExitRequested -= ExitEscapeMode;
        voiceInput.EscapeModeHostRequested += CreateDriverRoom;
        voiceInput.EscapeModeJoinRequested += SearchForDriverRoom;
        voiceInput.EscapeModeExitRequested += ExitEscapeMode;
    }

    private void EnsureNetworkManager()
    {
        networkManager = NetworkManager.Singleton;
        if (networkManager == null)
        {
            var networkObject = new GameObject("EscapeNetworkManager");
            DontDestroyOnLoad(networkObject);
            var transport = networkObject.AddComponent<UnityTransport>();
            networkManager = networkObject.AddComponent<NetworkManager>();
            createdNetworkManager = true;
            networkManager.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = transport,
                EnableSceneManagement = true
            };
        }
        else if (networkManager.NetworkConfig.NetworkTransport == null)
        {
            networkManager.NetworkConfig.NetworkTransport = networkManager.GetComponent<UnityTransport>();
        }

        if (!networkManager.IsListening)
            networkManager.NetworkConfig.EnableSceneManagement = true;
    }

    private void SetCurrentSession(ISession session)
    {
        if (currentSession != null) currentSession.StateChanged -= HandleSessionStateChanged;
        currentSession = session;
        if (currentSession != null) currentSession.StateChanged += HandleSessionStateChanged;
    }

    private void HandleSessionStateChanged(SessionState state)
    {
        if (state == SessionState.Disconnected && !hostRole && !isExiting)
            RetryHackerConnection();
    }

    public async void CreateDriverRoom()
    {
        if (isExiting || operationRunning || currentSession != null) return;
        lastAttemptWasHost = true;
        operationRunning = true;
        int requestId = ++requestGeneration;
        searching = false;
        hostRole = true;
        searchFailed = false;
        BeginHudRunIfNeeded();
        status = "Creando sala Escape como conductor...";
        EscapeModeHudFeed.Publish(status);

        try
        {
            await InitializeServicesAsync();
            EnsureNetworkManager();

            var properties = new Dictionary<string, SessionProperty>
            {
                { ModeProperty, new SessionProperty(ModeValue, VisibilityPropertyOptions.Public, PropertyIndex.String1) }
            };
            var options = new SessionOptions
            {
                Name = "KartIHC - Modo Escape",
                Type = SessionType,
                MaxPlayers = 2,
                IsPrivate = false,
                IsLocked = false,
                SessionProperties = properties,
                PlayerProperties = new Dictionary<string, PlayerProperty>
                {
                    { "role", new PlayerProperty("conductor") }
                }
            }.WithRelayNetwork();

            SetCurrentSession(await MultiplayerService.Instance.CreateSessionAsync(options));
            if (isExiting || requestId != requestGeneration)
            {
                await currentSession.LeaveAsync();
                SetCurrentSession(null);
                return;
            }
            sessionCode = currentSession.Code;
            PlayerPrefs.SetString(SavedSessionKey, currentSession.Id);
            PlayerPrefs.SetString(SavedRoleKey, "driver");
            PlayerPrefs.Save();
            status = "Sala creada. Esperando al hacker...";
            EscapeModeHudFeed.Publish(status);
            Debug.Log($"[EscapeMode] Sala de conductor creada. Código de respaldo: {sessionCode}");
            BeginEscapeGameplay();
        }
        catch (Exception exception)
        {
            status = $"No se pudo crear la sala: {exception.Message}";
            searchFailed = true;
            EscapeModeHudFeed.Publish(status);
            Debug.LogError($"[EscapeMode] {status}");
            hostRole = false;
        }
        finally
        {
            operationRunning = false;
        }
    }

    public async void SearchForDriverRoom()
    {
        if (isExiting || operationRunning || searching) return;
        if (currentSession != null)
        {
            status = hostRole ? "Esta sala ya está activa con el rol de conductor." : "Ya estás conectado como hacker.";
            EscapeModeHudFeed.Publish(status);
            return;
        }
        lastAttemptWasHost = false;
        operationRunning = true;
        int requestId = ++requestGeneration;
        searching = true;
        hostRole = false;
        searchFailed = false;
        BeginHudRunIfNeeded();
        sessionCode = "";
        status = "Buscando conductor para Modo Escape...";
        EscapeModeHudFeed.Publish(status);

        try
        {
            await InitializeServicesAsync();
            EnsureNetworkManager();

            bool previousRoleWasHacker = PlayerPrefs.GetString(SavedRoleKey, "") == "hacker";
            string previousSessionId = previousRoleWasHacker ? PlayerPrefs.GetString(SavedSessionKey, "") : "";
            if (!previousRoleWasHacker) PlayerPrefs.DeleteKey(SavedSessionKey);
            if (!string.IsNullOrEmpty(previousSessionId))
            {
                try
                {
                    SetCurrentSession(await MultiplayerService.Instance.ReconnectToSessionAsync(
                        previousSessionId,
                        new ReconnectSessionOptions { Type = SessionType }));
                }
                catch (Exception exception)
                {
                    Debug.Log($"[EscapeMode] No hay una sesión anterior para reanudar; comienzo la búsqueda: {exception.Message}");
                }

                if (currentSession != null)
                {
                    if (isExiting || requestId != requestGeneration)
                    {
                        await currentSession.LeaveAsync();
                        SetCurrentSession(null);
                        return;
                    }
                    status = "Reconectado como hacker.";
                    EscapeModeHudFeed.Publish(status);
                    BeginEscapeGameplay();
                    return;
                }
            }

            var joinOptions = new SessionOptions
            {
                Type = SessionType,
                MaxPlayers = 2,
                PlayerProperties = new Dictionary<string, PlayerProperty>
                {
                    { "role", new PlayerProperty("hacker") }
                }
            }.WithRelayNetwork();

            var filters = new List<FilterOption>
            {
                new FilterOption(FilterField.AvailableSlots, "1", FilterOperation.GreaterOrEqual),
                new FilterOption(FilterField.StringIndex1, ModeValue, FilterOperation.Equal)
            };

            // Quick Join retries the public session search. CreateSession=false is
            // intentional: a hacker must never become the conductor/host by timeout.
            float searchStartedAt = Time.realtimeSinceStartup;
            while (searching && requestId == requestGeneration && currentSession == null &&
                   Time.realtimeSinceStartup - searchStartedAt < DriverSearchDuration)
            {
                float secondsLeft = DriverSearchDuration - (Time.realtimeSinceStartup - searchStartedAt);
                try
                {
                    SetCurrentSession(await MultiplayerService.Instance.MatchmakeSessionAsync(
                        new QuickJoinOptions
                        {
                            Filters = filters,
                            Timeout = TimeSpan.FromSeconds(Math.Max(1f, Math.Min(8f, secondsLeft))),
                            CreateSession = false
                        },
                        joinOptions));
                }
                catch (Exception exception)
                {
                    status = "Buscando conductor...";
                    Debug.Log($"[EscapeMode] Quick Join reintentará: {exception.Message}");
                    secondsLeft = DriverSearchDuration - (Time.realtimeSinceStartup - searchStartedAt);
                    if (searching && secondsLeft > 0f)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(Math.Min(2f, secondsLeft)));
                        if (searching && requestId == requestGeneration)
                        {
                            secondsLeft = DriverSearchDuration - (Time.realtimeSinceStartup - searchStartedAt);
                            status = $"Buscando conductor... {Mathf.CeilToInt(Mathf.Max(0f, secondsLeft))} s";
                        }
                    }
                }
            }

            if (currentSession != null)
            {
                if (isExiting || requestId != requestGeneration)
                {
                    await currentSession.LeaveAsync();
                    SetCurrentSession(null);
                    return;
                }
                PlayerPrefs.SetString(SavedSessionKey, currentSession.Id);
                PlayerPrefs.SetString(SavedRoleKey, "hacker");
                PlayerPrefs.Save();
                status = "Conectado como hacker.";
                EscapeModeHudFeed.Publish(status);
                BeginEscapeGameplay();
            }
            else if (searching && requestId == requestGeneration)
            {
                searchFailed = true;
                searching = false;
                status = "No se encontró ningún conductor.";
                EscapeModeHudFeed.Publish(status);
                Debug.LogWarning($"[EscapeMode] {status}");
            }
        }
        catch (Exception exception)
        {
            status = $"Error al buscar sala: {exception.Message}";
            searchFailed = true;
            EscapeModeHudFeed.Publish(status);
            Debug.LogError($"[EscapeMode] {status}");
        }
        finally
        {
            searching = false;
            operationRunning = false;
        }
    }

    public void RetryFailedAttempt()
    {
        if (lastAttemptWasHost) CreateDriverRoom();
        else SearchForDriverRoom();
    }

    private void BeginHudRunIfNeeded()
    {
        if (hudRunStarted) return;
        EscapeModeHudFeed.Clear();
        hudRunStarted = true;
    }

    private static async Task InitializeServicesAsync()
    {
        if (UnityServices.State != ServicesInitializationState.Initialized)
            await UnityServices.InitializeAsync();

        if (!AuthenticationService.Instance.IsSignedIn)
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
    }

    private void BeginEscapeGameplay()
    {
        if (isExiting) return;
        status = hostRole ? "Conductor conectado; cargando pista Escape..." : "Hacker conectado; esperando la pista Escape...";
        EscapeModeHudFeed.Publish(status);
        Application.runInBackground = true;

        if (SceneManager.GetActiveScene().name == EscapeSceneName)
        {
            StartCoroutine(InitializeEscapeScene());
            return;
        }

        // The NGO host loads the dedicated scene; connected clients follow the
        // network scene event and initialize their local course in HandleSceneLoaded.
        if (!hostRole) return;
        StartCoroutine(LoadEscapeSceneWhenNetworkReady());
    }

    private IEnumerator LoadEscapeSceneWhenNetworkReady()
    {
        float timeout = 25f;
        while (timeout > 0f && (networkManager == null || !networkManager.IsListening))
        {
            EnsureNetworkManager();
            timeout -= Time.unscaledDeltaTime;
            yield return null;
        }

        if (networkManager == null || !networkManager.IsServer || networkManager.SceneManager == null)
        {
            status = "La sala se creó, pero la red de juego no inició. Revisa Relay y la consola.";
            Debug.LogError($"[EscapeMode] {status}");
            yield break;
        }

        var result = networkManager.SceneManager.LoadScene(EscapeSceneName, LoadSceneMode.Single);
        if (result != SceneEventProgressStatus.Started)
        {
            status = $"No se pudo cargar {EscapeSceneName}: {result}";
            Debug.LogError($"[EscapeMode] {status}");
        }
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == EscapeSceneName)
        {
            voiceInput = FindFirstObjectByType<VoicePowerUpInput>();
            gameStateManager = FindFirstObjectByType<GameStateManager>();
            BindVoice();
            if (currentSession != null) StartCoroutine(InitializeEscapeScene());
        }
        else if (scene.name == MainSceneName || scene.name == CareerSceneName)
        {
            voiceInput = FindFirstObjectByType<VoicePowerUpInput>();
            gameStateManager = FindFirstObjectByType<GameStateManager>();
            BindVoice();
        }
    }

    private IEnumerator InitializeEscapeScene()
    {
        // Let the new scene's GameStateManager finish its Start() initialization first.
        yield return null;
        if (isExiting || SceneManager.GetActiveScene().name != EscapeSceneName) yield break;

        voiceInput = FindFirstObjectByType<VoicePowerUpInput>();
        gameStateManager = FindFirstObjectByType<GameStateManager>();
        BindVoice();
        EscapeCourseBuilder.Build();
        gameStateManager?.BeginPlaying();
        EscapeModeNetworkBridge.StartForRole(hostRole);
        status = hostRole ? "Conductor en la pista Escape." : "Hacker conectado al conductor.";
        EscapeModeHudFeed.Publish(status);
    }

    public async void RetryHackerConnection()
    {
        if (isExiting || hostRole || operationRunning) return;
        operationRunning = true;
        searching = false;
        status = "Se perdió la conexión. Intentando volver a la sala...";
        EscapeModeHudFeed.Publish(status);
        string oldSessionId = currentSession != null
            ? currentSession.Id
            : PlayerPrefs.GetString(SavedSessionKey, "");
        SetCurrentSession(null);
        try
        {
            await InitializeServicesAsync();
            if (!string.IsNullOrEmpty(oldSessionId))
            {
                SetCurrentSession(await MultiplayerService.Instance.ReconnectToSessionAsync(
                    oldSessionId,
                    new ReconnectSessionOptions { Type = SessionType }));
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[EscapeMode] No se pudo reanudar la sesión anterior: {exception.Message}");
        }

        if (currentSession != null)
        {
            status = "Reconectado como hacker.";
            operationRunning = false;
            BeginEscapeGameplay();
            return;
        }

        status = "La sala anterior ya no acepta reconexión. Buscando conductor...";
        EscapeModeHudFeed.Publish(status);
        SetCurrentSession(null);
        sessionCode = "";
        operationRunning = false;
        SearchForDriverRoom();
    }

    public async void ExitEscapeMode()
    {
        if (isExiting) return;
        isExiting = true;
        requestGeneration++;
        searching = false;
        status = "Cerrando Modo Escape...";
        try
        {
            if (currentSession != null)
                await currentSession.LeaveAsync();
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[EscapeMode] No se pudo cerrar la sesión limpiamente: {exception.Message}");
        }
        finally
        {
            SetCurrentSession(null);
            sessionCode = "";
            hostRole = false;
            hudRunStarted = false;
            PlayerPrefs.DeleteKey(SavedSessionKey);
            PlayerPrefs.DeleteKey(SavedRoleKey);
            PlayerPrefs.Save();
            if (networkManager != null && networkManager.IsListening)
                networkManager.Shutdown();
            EscapeModeNetworkBridge.Stop();
            EscapeCourseBuilder.RestoreStandardCourse();
            if (createdNetworkManager && networkManager != null)
                Destroy(networkManager.gameObject);
            networkManager = null;
            createdNetworkManager = false;
            status = "Modo Escape cerrado.";
            if (SceneManager.GetActiveScene().name == EscapeSceneName)
                SceneManager.LoadScene(MainSceneName, LoadSceneMode.Single);
            else
            {
                if (gameStateManager == null) gameStateManager = FindFirstObjectByType<GameStateManager>();
                gameStateManager?.ReturnToMainMenu();
            }
            isExiting = false;
        }
    }

    private void OnDisable()
    {
        if (voiceInput != null)
        {
            voiceInput.EscapeModeHostRequested -= CreateDriverRoom;
            voiceInput.EscapeModeJoinRequested -= SearchForDriverRoom;
            voiceInput.EscapeModeExitRequested -= ExitEscapeMode;
        }
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        if (instance == this) instance = null;
    }
}
