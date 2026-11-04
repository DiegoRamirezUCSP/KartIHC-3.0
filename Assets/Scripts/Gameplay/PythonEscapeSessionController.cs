using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;

/// <summary>Optional same-Wi-Fi Escape session using the included Python TCP server.</summary>
public sealed class PythonEscapeSessionController : MonoBehaviour
{
    private const string EscapeSceneName = "EscapeCourse";
    private const string MenuSceneName = "ModeMenu";
    private const int GamePort = 7777;
    private const int DiscoveryPort = 7778;
    private static PythonEscapeSessionController instance;

    private VoicePowerUpInput voiceInput;
    private PythonRelayClient client;
    private Process localServer;
    private bool busy;
    private bool pythonModeActive;
    private bool driverRole;
    private bool exiting;
    private bool retrying;
    private bool sceneInitialized;
    private bool searchFailed;
    private int operationGeneration;
    private CancellationTokenSource operationCancellation;
    private string status = "Escape local: di activar escape local o buscar escape local";

    public static PythonEscapeSessionController Instance => instance;
    public string Status => status;
    public bool ShouldShowStatus => busy || pythonModeActive || searchFailed;
    public bool IsActive => busy || pythonModeActive;
    public bool SearchFailed => searchFailed;
    public bool LastAttemptWasHost => driverRole;

    public void ClearFailure()
    {
        if (IsActive) return;
        searchFailed = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureExists()
    {
        if (FindFirstObjectByType<PythonEscapeSessionController>() != null) return;
        new GameObject("PythonEscapeSessionController").AddComponent<PythonEscapeSessionController>();
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
        EnsureClient();
    }

    private void Start() => BindVoice();

    private void OnEnable() => BindVoice();

    private void BindVoice()
    {
        if (voiceInput == null) voiceInput = FindFirstObjectByType<VoicePowerUpInput>();
        if (voiceInput == null) return;
        voiceInput.LocalEscapeHostRequested -= StartLocalServer;
        voiceInput.LocalEscapeJoinRequested -= JoinLocalServer;
        voiceInput.LocalEscapeExitRequested -= ExitLocalSession;
        voiceInput.LocalEscapeHostRequested += StartLocalServer;
        voiceInput.LocalEscapeJoinRequested += JoinLocalServer;
        voiceInput.LocalEscapeExitRequested += ExitLocalSession;
    }

    private void EnsureClient()
    {
        client = GetComponent<PythonRelayClient>();
        if (client == null) client = gameObject.AddComponent<PythonRelayClient>();
        client.OnMessage -= HandleClientMessage;
        client.OnMessage += HandleClientMessage;
    }

    public async void StartLocalServer()
    {
        if (busy || pythonModeActive) return;
        exiting = false;
        CancellationToken operationToken = BeginOperationCancellation();
        if (EscapeModeSessionController.Instance != null && EscapeModeSessionController.Instance.HasActiveOrOngoingSession)
        {
            searchFailed = true;
            status = "Cierra primero la sesión Relay antes de crear una sesión local.";
            PublishStatus();
            return;
        }
        busy = true;
        int operationId = ++operationGeneration;
        searchFailed = false;
        EscapeModeHudFeed.Clear();
        driverRole = true;
        status = "Iniciando servidor Escape local...";
        PublishStatus();
        try
        {
            EnsureClient();
            StartPythonServerProcess();
            bool connected = false;
            string lastError = "";
            for (int attempt = 0; attempt < 20 && !connected && operationId == operationGeneration && !exiting; attempt++)
            {
                if (localServer == null || localServer.HasExited)
                    throw new InvalidOperationException("El servidor Python se cerró al iniciar.");
                try
                {
                    await client.ConnectAsync("127.0.0.1", GamePort, PythonRelayClient.Role.Driver, 1500, operationToken);
                    if (operationId != operationGeneration || exiting)
                    {
                        client.Disconnect();
                        return;
                    }
                    connected = true;
                }
                catch (Exception exception)
                {
                    lastError = exception.Message;
                    await Task.Delay(350, operationToken);
                }
            }
            if (operationId != operationGeneration || exiting) return;
            if (!connected) throw new TimeoutException("No respondió el servidor local. " + lastError);

            pythonModeActive = true;
            status = "Servidor listo; esperando al hacker en la misma Wi-Fi.";
            PublishStatus();
            Application.runInBackground = true;
            LoadEscapeScene();
        }
        catch (Exception exception)
        {
            if (operationId != operationGeneration || exiting) return;
            searchFailed = true;
            status = "No se pudo iniciar Escape local: " + exception.Message;
            PublishStatus();
            client?.Disconnect();
            StopLocalServer();
        }
        finally { if (operationId == operationGeneration) busy = false; }
    }

    public async void JoinLocalServer()
    {
        if (busy || pythonModeActive) return;
        exiting = false;
        CancellationToken operationToken = BeginOperationCancellation();
        if (EscapeModeSessionController.Instance != null && EscapeModeSessionController.Instance.HasActiveOrOngoingSession)
        {
            searchFailed = true;
            status = "Cierra primero la sesión Relay antes de buscar una sesión local.";
            PublishStatus();
            return;
        }
        busy = true;
        int operationId = ++operationGeneration;
        searchFailed = false;
        EscapeModeHudFeed.Clear();
        driverRole = false;
        status = "Buscando servidor Escape en la red Wi-Fi...";
        PublishStatus();
        try
        {
            EnsureClient();
            bool connected = await client.DiscoverAndConnectAsync(PythonRelayClient.Role.Hacker, 15000, DiscoveryPort, operationToken);
            if (operationId != operationGeneration || exiting)
            {
                client.Disconnect();
                return;
            }
            if (!connected)
            {
                searchFailed = true;
                status = "No se encontró servidor Escape local en esta Wi-Fi.";
                PublishStatus();
                return;
            }

            pythonModeActive = true;
            status = "Conectado al servidor local como hacker.";
            PublishStatus();
            Application.runInBackground = true;
            LoadEscapeScene();
        }
        catch (Exception exception)
        {
            if (operationId != operationGeneration || exiting) return;
            searchFailed = true;
            status = "No se pudo conectar al servidor local: " + exception.Message;
            PublishStatus();
            client?.Disconnect();
        }
        finally { if (operationId == operationGeneration) busy = false; }
    }

    private void StartPythonServerProcess()
    {
        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
        string script = Path.Combine(projectRoot ?? Application.dataPath, "Tools", "KartIHCServer", "kartihc_server.py");
        if (!File.Exists(script)) throw new FileNotFoundException("No se encontró el servidor Python del proyecto.", script);

        var start = new ProcessStartInfo
        {
            FileName = "python",
            Arguments = $"-u \"{script}\" --host 0.0.0.0 --port {GamePort} --discovery-port {DiscoveryPort}",
            WorkingDirectory = Path.GetDirectoryName(script),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        localServer = Process.Start(start);
        if (localServer == null) throw new InvalidOperationException("Windows no pudo iniciar Python.");
        localServer.OutputDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data)) UnityEngine.Debug.Log("[Escape Python] " + args.Data);
        };
        localServer.ErrorDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data)) UnityEngine.Debug.Log("[Escape Python] " + args.Data);
        };
        localServer.BeginOutputReadLine();
        localServer.BeginErrorReadLine();
    }

    private void LoadEscapeScene()
    {
        if (SceneManager.GetActiveScene().name == EscapeSceneName)
        {
            StartCoroutine(InitializeEscapeScene());
            return;
        }
        SceneManager.LoadScene(EscapeSceneName, LoadSceneMode.Single);
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == EscapeSceneName && pythonModeActive)
            StartCoroutine(InitializeEscapeScene());
    }

    private System.Collections.IEnumerator InitializeEscapeScene()
    {
        if (sceneInitialized) yield break;
        yield return null;
        if (!pythonModeActive || SceneManager.GetActiveScene().name != EscapeSceneName) yield break;

        sceneInitialized = true;
        voiceInput = FindFirstObjectByType<VoicePowerUpInput>();
        BindVoice();
        EscapeCourseBuilder.Build();
        FindFirstObjectByType<KartIHC.Gameplay.GameStateManager>()?.BeginPlaying();
        EscapeModeNetworkBridge.Stop();
        EscapeModeNetworkBridge.StartForRole(driverRole);
        PythonEscapeModeBridge.StartForRole(client, driverRole);
        status = driverRole ? "Escape local: conductor conectado por Wi-Fi." : "Escape local: hacker conectado por Wi-Fi.";
        PublishStatus();
    }

    private void HandleClientMessage(string type, string json)
    {
        if (type == "phase")
        {
            if (json.Contains("playing"))
                status = "Servidor local: conductor y hacker conectados.";
            else if (json.Contains("waiting"))
                status = driverRole ? "Servidor listo; esperando al hacker." : "Conectado; esperando al conductor.";
            else if (json.Contains("finished"))
                status = "¡Victoria compartida en Escape local!";
            PublishStatus();
        }
        if (type == "_disconnected" && pythonModeActive && !exiting && !retrying)
            RetryConnection();
    }

    private async void RetryConnection()
    {
        retrying = true;
        int retryId = operationGeneration;
        CancellationToken retryToken = operationCancellation != null
            ? operationCancellation.Token
            : CancellationToken.None;
        status = "Se perdió la conexión; intentando reconectar por Wi-Fi...";
        PublishStatus();
        try
        {
            for (int attempt = 0; attempt < 15 && pythonModeActive && !exiting && retryId == operationGeneration; attempt++)
            {
                try
                {
                    if (driverRole)
                        await client.ConnectAsync("127.0.0.1", GamePort, PythonRelayClient.Role.Driver, 1500, retryToken);
                    else if (!await client.DiscoverAndConnectAsync(PythonRelayClient.Role.Hacker, 1000, DiscoveryPort, retryToken))
                        throw new TimeoutException("Servidor aún no visible.");

                    if (retryId != operationGeneration || exiting) return;
                    status = "Conexión local restablecida.";
                    PublishStatus();
                    return;
                }
                catch (Exception)
                {
                    if (retryToken.IsCancellationRequested) return;
                    await Task.Delay(800, retryToken);
                }
            }
            if (retryId == operationGeneration && pythonModeActive && !exiting)
            {
                status = "No se pudo restablecer la conexión local.";
                PublishStatus();
            }
        }
        finally { retrying = false; }
    }

    public void ExitLocalSession()
    {
        if (!pythonModeActive && !busy && !searchFailed) return;
        operationGeneration++;
        operationCancellation?.Cancel();
        operationCancellation?.Dispose();
        operationCancellation = null;
        busy = false;
        exiting = true;
        pythonModeActive = false;
        searchFailed = false;
        sceneInitialized = false;
        PythonEscapeModeBridge.Stop();
        EscapeModeNetworkBridge.Stop();
        EscapeCourseBuilder.RestoreStandardCourse();
        client?.Disconnect();
        StopLocalServer();
        status = "Escape local cerrado.";
        PublishStatus();
        if (SceneManager.GetActiveScene().name == EscapeSceneName)
            SceneManager.LoadScene(MenuSceneName, LoadSceneMode.Single);
        else
            FindFirstObjectByType<EscapeModeMenuSelector>()?.ReturnToModesPage();
    }

    private void StopLocalServer()
    {
        try
        {
            if (localServer != null && !localServer.HasExited) localServer.Kill();
        }
        catch (Exception exception) { UnityEngine.Debug.LogWarning("No se pudo cerrar el servidor Python: " + exception.Message); }
        finally
        {
            localServer?.Dispose();
            localServer = null;
        }
    }

    private void PublishStatus() => EscapeModeHudFeed.Publish(status);

    private CancellationToken BeginOperationCancellation()
    {
        operationCancellation?.Cancel();
        operationCancellation?.Dispose();
        operationCancellation = new CancellationTokenSource();
        return operationCancellation.Token;
    }

    private void OnApplicationQuit()
    {
        operationCancellation?.Cancel();
        StopLocalServer();
    }

    private void OnDisable()
    {
        if (voiceInput == null) return;
        voiceInput.LocalEscapeHostRequested -= StartLocalServer;
        voiceInput.LocalEscapeJoinRequested -= JoinLocalServer;
        voiceInput.LocalEscapeExitRequested -= ExitLocalSession;
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        if (client != null) client.OnMessage -= HandleClientMessage;
        operationCancellation?.Cancel();
        operationCancellation?.Dispose();
        StopLocalServer();
        if (instance == this) instance = null;
    }
}
