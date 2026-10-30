using System;
using System.Collections.Generic;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using UnityEngine.Windows.Speech;
#endif

public class VoicePowerUpInput : MonoBehaviour
{
    public const string VersionLabel = "VoicePowerUpInput v10 - practica hacker individual";

    // Nombres "oficiales" de cada comando. El resto del juego (puente de input,
    // inventario, tutorial) compara con estos textos, sin importar que palabra
    // dijo el jugador. No los cambies a menos que cambies tambien esos scripts.
    public const string StartId = "empezar";
    public const string CancelTutorialId = "cancelar";
    public const string ShieldId = "escudo";
    public const string BoostId = "turbo";
    public const string TrapId = "trampa";
    public const string PauseId = "pausa";
    public const string ResumeId = "reanudar";
    public const string ControlsId = "controles";
    public const string RestartId = "reiniciar";

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    [Header("Reconocimiento")]
    [Tooltip("Low acepta mas pronunciaciones pero da mas falsos positivos. High es mas estricto.")]
    [SerializeField] private ConfidenceLevel minimumConfidence = ConfidenceLevel.Medium;
    [Tooltip("Escribe en la consola cada palabra que oye el reconocedor, con su confianza, aunque se descarte. Util para ver que se confunde.")]
    [SerializeField] private bool logRecognizedPhrases = true;
#endif

    [Header("Pantalla de inicio (cada comando acepta varias palabras)")]
    [SerializeField] private string[] startWords = { "empezar", "iniciar", "comenzar", "iniciar carrera", "empezar carrera", "modo carrera" };
    [Tooltip("Cada tutorial tiene sus propias palabras. Solo funcionan en la pantalla de inicio.")]
    [SerializeField] private string[] tutorial1Words = { "tutorial uno" };
    [SerializeField] private string[] tutorial2Words = { "tutorial dos" };
    [SerializeField] private string[] tutorial3Words = { "tutorial tres" };
    [SerializeField] private string[] tutorial4Words = { "tutorial cuatro", "tutorial hacker" };
    [Tooltip("Sale del tutorial y vuelve a la pantalla de inicio. Solo funciona dentro de un tutorial.")]
    [SerializeField] private string[] cancelTutorialWords = { "cancelar", "salir" };

    [Header("Power-ups")]
    [SerializeField] private string[] shieldWords = { "escudo" };
    [SerializeField] private string[] boostWords = { "turbo", "nitro", "tubo" };
    [SerializeField] private string[] trapWords = { "trampa" };

    [Header("Pausa")]
    [SerializeField] private string[] pauseWords = { "pausa", "pausar" };
    [SerializeField] private string[] resumeWords = { "reanudar", "continuar" };
    [SerializeField] private string[] controlsWords = { "controles", "ayuda" };
    [SerializeField] private string[] restartWords = { "reiniciar", "reinicio" };

    [Header("Modo cooperativo Escape")]
    [SerializeField] private string[] backToModeMenuWords = { "volver menu", "regresar menu" };
    [SerializeField] private string[] openEscapeMenuWords = { "escape", "modo escape", "abrir escape" };
    [SerializeField] private string[] createEscapeModeWords = { "activar modo escape" };
    [SerializeField] private string[] findEscapeModeWords = { "buscar modo escape" };
    [SerializeField] private string[] leaveEscapeModeWords = { "salir modo escape" };

    [Header("Camara de piernas")]
    [SerializeField] private string[] activateLegCameraWords = { "activar camara", "encender la camara" };
    [SerializeField] private string[] changeLegCameraWords = { "cambiar camara" };
    [SerializeField] private string[] calibrateLegCameraWords = { "calibrar" };

    public event Action StartRaceRequested;
    public event Action<int> TutorialRequested;
    public event Action HackerPracticeRequested;
    public event Action CancelTutorialRequested;
    public event Action<string> PowerUpRequested;
    public event Action PauseRequested;
    public event Action ResumeRequested;
    public event Action ControlsRequested;
    public event Action RestartRequested;
    public event Action EscapeModeHostRequested;
    public event Action EscapeModeJoinRequested;
    public event Action EscapeModeMenuRequested;
    public event Action EscapeModeExitRequested;
    public event Action ModeMenuBackRequested;
    public event Action LegCameraActivateRequested;
    public event Action LegCameraChangeRequested;
    public event Action LegCameraCalibrateRequested;

    public string LastPowerUp { get; private set; } = "ninguno";
    public string LastCommand { get; private set; } = "ninguno";
    public int ActivationCount { get; private set; }
    public bool SupportsKeywordRecognizer
    {
        get
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            return true;
#else
            return false;
#endif
        }
    }

    public bool IsListening
    {
        get
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            return keywordRecognizer != null && keywordRecognizer.IsRunning;
#else
            return false;
#endif
        }
    }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    private KeywordRecognizer keywordRecognizer;
    private Dictionary<string, Action> commands;
#endif

    private void Start()
    {
        Debug.Log(VersionLabel);

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        commands = new Dictionary<string, Action>();
        RegisterCommand(startWords, RequestStartRace);
        RegisterCommand(tutorial1Words, () => RequestTutorial(1));
        RegisterCommand(tutorial2Words, () => RequestTutorial(2));
        RegisterCommand(tutorial3Words, () => RequestTutorial(3));
        RegisterCommand(tutorial4Words, () => RequestTutorial(4));
        RegisterCommand(new[] { "prueba hacker", "probar hacker" }, RequestHackerPractice);
        RegisterCommand(cancelTutorialWords, RequestCancelTutorial);
        RegisterCommand(shieldWords, () => RequestPowerUp(ShieldId));
        RegisterCommand(boostWords, () => RequestPowerUp(BoostId));
        RegisterCommand(trapWords, () => RequestPowerUp(TrapId));
        RegisterCommand(pauseWords, RequestPause);
        RegisterCommand(resumeWords, RequestResume);
        RegisterCommand(controlsWords, RequestControls);
        RegisterCommand(restartWords, RequestRestart);
        RegisterCommand(createEscapeModeWords, RequestEscapeModeHost);
        RegisterCommand(findEscapeModeWords, RequestEscapeModeJoin);
        RegisterCommand(openEscapeMenuWords, RequestEscapeModeMenu);
        RegisterCommand(backToModeMenuWords, RequestModeMenuBack);
        RegisterCommand(leaveEscapeModeWords, RequestEscapeModeExit);
        RegisterCommand(activateLegCameraWords, RequestLegCameraActivate);
        RegisterCommand(changeLegCameraWords, RequestLegCameraChange);
        RegisterCommand(calibrateLegCameraWords, RequestLegCameraCalibrate);

        if (commands.Count == 0)
        {
            Debug.LogWarning("VoicePowerUpInput: no hay palabras configuradas, la voz queda desactivada.");
            return;
        }

        string[] keywords = new string[commands.Keys.Count];
        commands.Keys.CopyTo(keywords, 0);

        // Se crea con el nivel mas permisivo y el filtro por confianza se aplica
        // en OnPhraseRecognized, asi podemos registrar tambien lo que se descarta.
        keywordRecognizer = new KeywordRecognizer(keywords, ConfidenceLevel.Rejected);
        keywordRecognizer.OnPhraseRecognized += OnPhraseRecognized;
        keywordRecognizer.Start();
#else
        Debug.LogWarning("VoicePowerUpInput necesita un plugin o puente nativo para reconocimiento de voz en Android/iOS.");
#endif
    }

    private void Update()
    {
        // Keyboard fallback for demos and testing without microphone.
        if (WasStartPressed()) RequestStartRace();
        if (WasPressed(PowerUpKey.Tutorial1)) RequestTutorial(1);
        if (WasPressed(PowerUpKey.Tutorial2)) RequestTutorial(2);
        if (WasPressed(PowerUpKey.Tutorial3)) RequestTutorial(3);
        if (WasPressed(PowerUpKey.CancelTutorial)) RequestCancelTutorial();
        if (WasPressed(PowerUpKey.Shield)) RequestPowerUp(ShieldId);
        if (WasPressed(PowerUpKey.Boost)) RequestPowerUp(BoostId);
        if (WasPressed(PowerUpKey.Trap)) RequestPowerUp(TrapId);
        if (WasPressed(PowerUpKey.Pause)) RequestPause();
        if (WasPressed(PowerUpKey.Resume)) RequestResume();
        if (WasPressed(PowerUpKey.Controls)) RequestControls();
        if (WasPressed(PowerUpKey.Restart)) RequestRestart();
    }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    /// <summary>Registra todas las palabras de un comando apuntando a la misma accion.</summary>
    private void RegisterCommand(string[] words, Action command)
    {
        if (words == null)
        {
            return;
        }

        foreach (string raw in words)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            string word = raw.Trim().ToLowerInvariant();

            if (commands.ContainsKey(word))
            {
                // La misma palabra en dos comandos distintos haria que solo valga uno.
                Debug.LogWarning($"VoicePowerUpInput: la palabra \"{word}\" esta repetida, se ignora la repeticion.");
                continue;
            }

            commands.Add(word, command);
        }
    }

    private void OnPhraseRecognized(PhraseRecognizedEventArgs args)
    {
        // ConfidenceLevel: High = 0, Medium = 1, Low = 2, Rejected = 3.
        // Mas bajo es mejor, asi que aceptamos si es igual o mejor que el minimo.
        bool accepted = (int)args.confidence <= (int)minimumConfidence;

        if (logRecognizedPhrases)
        {
            Debug.Log($"[Voz] \"{args.text}\" confianza={args.confidence} -> {(accepted ? "ACEPTADA" : "ignorada")}");
        }

        if (!accepted)
        {
            return;
        }

        if (commands.TryGetValue(args.text.ToLowerInvariant(), out Action command))
        {
            command.Invoke();
        }
    }
#endif

    private void RequestPowerUp(string powerUpId)
    {
        LastCommand = powerUpId;
        LastPowerUp = powerUpId;
        ActivationCount++;
        Debug.Log($"Power-up solicitado: {powerUpId}");
        PowerUpRequested?.Invoke(powerUpId);
    }

    private void RequestStartRace()
    {
        LastCommand = StartId;
        ActivationCount++;
        Debug.Log("Circuito normal solicitado por voz.");
        StartRaceRequested?.Invoke();
    }

    private void RequestTutorial(int tutorialId)
    {
        LastCommand = $"tutorial {tutorialId}";
        ActivationCount++;
        Debug.Log($"Tutorial {tutorialId} solicitado.");
        TutorialRequested?.Invoke(tutorialId);
    }

    private void RequestHackerPractice()
    {
        LastCommand = "prueba hacker";
        ActivationCount++;
        Debug.Log("Práctica Hacker individual solicitada por voz.");
        HackerPracticeRequested?.Invoke();
    }

    private void RequestCancelTutorial()
    {
        LastCommand = CancelTutorialId;
        ActivationCount++;
        Debug.Log("Cancelar tutorial solicitado.");
        CancelTutorialRequested?.Invoke();
    }

    private void RequestPause()
    {
        LastCommand = PauseId;
        ActivationCount++;
        Debug.Log("Pausa solicitada por voz.");
        PauseRequested?.Invoke();
    }

    private void RequestResume()
    {
        LastCommand = ResumeId;
        ActivationCount++;
        Debug.Log("Reanudar solicitado por voz.");
        ResumeRequested?.Invoke();
    }

    private void RequestControls()
    {
        LastCommand = ControlsId;
        ActivationCount++;
        Debug.Log("Controles solicitados por voz.");
        ControlsRequested?.Invoke();
    }

    private void RequestRestart()
    {
        LastCommand = RestartId;
        ActivationCount++;
        Debug.Log("Reinicio solicitado por voz.");
        RestartRequested?.Invoke();
    }

    private void RequestEscapeModeHost()
    {
        LastCommand = "activar modo escape";
        ActivationCount++;
        Debug.Log("Crear partida cooperativa Escape solicitado por voz.");
        EscapeModeHostRequested?.Invoke();
    }

    private void RequestEscapeModeMenu()
    {
        LastCommand = "modo escape";
        ActivationCount++;
        Debug.Log("Abrir selección del Modo Escape solicitado por voz.");
        EscapeModeMenuRequested?.Invoke();
    }

    private void RequestModeMenuBack()
    {
        LastCommand = "volver menu";
        ActivationCount++;
        Debug.Log("Volver al menú solicitado por voz.");
        ModeMenuBackRequested?.Invoke();
    }

    private void RequestEscapeModeJoin()
    {
        LastCommand = "buscar modo escape";
        ActivationCount++;
        Debug.Log("Buscar partida cooperativa Escape solicitado por voz.");
        EscapeModeJoinRequested?.Invoke();
    }

    private void RequestEscapeModeExit()
    {
        LastCommand = "salir modo escape";
        ActivationCount++;
        Debug.Log("Salir del Modo Escape solicitado por voz.");
        EscapeModeExitRequested?.Invoke();
    }

    private void RequestLegCameraActivate()
    {
        LastCommand = "activar camara";
        ActivationCount++;
        LegCameraActivateRequested?.Invoke();
    }

    private void RequestLegCameraChange()
    {
        LastCommand = "cambiar camara";
        ActivationCount++;
        LegCameraChangeRequested?.Invoke();
    }

    private void RequestLegCameraCalibrate()
    {
        LastCommand = "calibrar";
        ActivationCount++;
        LegCameraCalibrateRequested?.Invoke();
    }

    private bool WasStartPressed()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
        {
            return Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame;
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
#else
        return false;
#endif
    }

    private bool WasPressed(PowerUpKey key)
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
        {
            switch (key)
            {
                case PowerUpKey.Shield:
                    return Keyboard.current.zKey.wasPressedThisFrame;
                case PowerUpKey.Boost:
                    return Keyboard.current.xKey.wasPressedThisFrame;
                case PowerUpKey.Trap:
                    return Keyboard.current.cKey.wasPressedThisFrame;
                case PowerUpKey.Pause:
                    return Keyboard.current.pKey.wasPressedThisFrame;
                case PowerUpKey.Resume:
                    return Keyboard.current.oKey.wasPressedThisFrame;
                case PowerUpKey.Controls:
                    return Keyboard.current.iKey.wasPressedThisFrame;
                case PowerUpKey.Restart:
                    return Keyboard.current.backspaceKey.wasPressedThisFrame;
                case PowerUpKey.Tutorial1:
                    return Keyboard.current.digit1Key.wasPressedThisFrame;
                case PowerUpKey.Tutorial2:
                    return Keyboard.current.digit2Key.wasPressedThisFrame;
                case PowerUpKey.Tutorial3:
                    return Keyboard.current.digit3Key.wasPressedThisFrame;
                case PowerUpKey.CancelTutorial:
                    return Keyboard.current.escapeKey.wasPressedThisFrame;
            }
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        switch (key)
        {
            case PowerUpKey.Shield:
                return Input.GetKeyDown(KeyCode.Z);
            case PowerUpKey.Boost:
                return Input.GetKeyDown(KeyCode.X);
            case PowerUpKey.Trap:
                return Input.GetKeyDown(KeyCode.C);
            case PowerUpKey.Pause:
                return Input.GetKeyDown(KeyCode.P);
            case PowerUpKey.Resume:
                return Input.GetKeyDown(KeyCode.O);
            case PowerUpKey.Controls:
                return Input.GetKeyDown(KeyCode.I);
            case PowerUpKey.Restart:
                return Input.GetKeyDown(KeyCode.Backspace);
            case PowerUpKey.Tutorial1:
                return Input.GetKeyDown(KeyCode.Alpha1);
            case PowerUpKey.Tutorial2:
                return Input.GetKeyDown(KeyCode.Alpha2);
            case PowerUpKey.Tutorial3:
                return Input.GetKeyDown(KeyCode.Alpha3);
            case PowerUpKey.CancelTutorial:
                return Input.GetKeyDown(KeyCode.Escape);
        }
#endif

        return false;
    }

    private enum PowerUpKey
    {
        Shield,
        Boost,
        Trap,
        Pause,
        Resume,
        Controls,
        Restart,
        Tutorial1,
        Tutorial2,
        Tutorial3,
        CancelTutorial
    }

    private void OnDestroy()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (keywordRecognizer != null)
        {
            keywordRecognizer.OnPhraseRecognized -= OnPhraseRecognized;

            if (keywordRecognizer.IsRunning)
            {
                keywordRecognizer.Stop();
            }

            keywordRecognizer.Dispose();
        }
#endif
    }
}
