using System;
using System.Net;
using System.Net.Sockets;
using System.IO;
using System.Text;
using System.Threading;
using Stopwatch = System.Diagnostics.Stopwatch;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
    
/// <summary>
/// Receives leg-motion commands from the Python MediaPipe script.
/// Expected UDP messages: ACELERA, FRENA, NADA.
/// </summary>
public class LegMotionInputReceiver : MonoBehaviour
{
    [SerializeField] private int port = 5555;
    [SerializeField] private float timeoutSeconds = 1f;
    [SerializeField] private bool showDiagnostics = true;
    [SerializeField] private string pythonExecutable = "python";
    [SerializeField] private string scriptRelativePath = "Tools/LegCameraControl/leg_camera_control.py";
    [SerializeField] private int previewPort = 5556;
    [SerializeField] private int controlPort = 5557;
    [SerializeField] private VoicePowerUpInput voiceInput;
    [Header("Teclas de respaldo")]
    [SerializeField] private Key activateCameraKey = Key.F6;
    [SerializeField] private Key changeCameraKey = Key.F7;
    [SerializeField] private Key calibrateCameraKey = Key.F8;

    private UdpClient udpClient;
    private Thread receiveThread;
    private volatile bool isRunning;
    private readonly object stateLock = new object();

    private string lastCommand = "NADA";
    private string lastMessage = "sin datos";
    private double lastPacketTime;
    private float throttle;
    private float brake;
    private Process cameraProcess;
    private UdpClient previewClient;
    private Thread previewThread;
    private volatile bool previewRunning;
    private readonly object previewLock = new object();
    private Texture2D previewTexture;
    private byte[] latestJpeg;
    private bool cameraStartedByUs;
    private string cameraStatus = "Cámara apagada";
    private string calibrationStatus = "Sin calibrar";
    private string projectRoot;
    private int selectedCameraIndex = -1;

    public bool IsConnected => ReadElapsedSeconds(lastPacketTime) <= timeoutSeconds && !LastCommand.StartsWith("CALIBRACION");
    public float Throttle => IsConnected ? throttle : 0f;
    public float Brake => IsConnected ? brake : 0f;
    public string LastCommand => lastCommand;
    public string LastMessage => lastMessage;
    public string CameraStatus => cameraStatus;
    public string CalibrationStatus => calibrationStatus;
    public Texture2D PreviewTexture => previewTexture;
    public bool CameraRunning => cameraProcess != null && !cameraProcess.HasExited;

    private void OnEnable()
    {
        lastPacketTime = -timeoutSeconds;

        try
        {
            udpClient = new UdpClient(port);
        }
        catch (SocketException exception)
        {
            lastMessage = $"no se pudo abrir UDP {port}: {exception.Message}";
            Debug.LogError($"[LegMotionInputReceiver] {lastMessage}");
            return;
        }

        isRunning = true;
        receiveThread = new Thread(ReceiveLoop)
        {
            IsBackground = true,
            Name = "LegMotionInputReceiver-UDP"
        };
        receiveThread.Start();
        projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
        if (voiceInput == null) voiceInput = FindFirstObjectByType<VoicePowerUpInput>();
        if (voiceInput != null)
        {
            voiceInput.LegCameraActivateRequested += StartCamera;
            voiceInput.LegCameraChangeRequested += ChangeCamera;
            voiceInput.LegCameraCalibrateRequested += Calibrate;
        }
        previewTexture = new Texture2D(2, 2, TextureFormat.RGB24, false);
        StartPreviewReceiver();
        Debug.Log($"[LegMotionInputReceiver] Escuchando comandos de piernas en UDP {port}.");
    }

    public void StartCamera()
    {
        if (CameraRunning)
        {
            cameraStatus = "Cámara activa";
            return;
        }
        string scriptPath = Path.Combine(projectRoot ?? Directory.GetCurrentDirectory(), scriptRelativePath);
        if (!File.Exists(scriptPath))
        {
            cameraStatus = $"No se encontró: {scriptRelativePath}";
            Debug.LogError($"[LegMotionInputReceiver] {cameraStatus}");
            return;
        }

        try
        {
            var start = new ProcessStartInfo
            {
                FileName = pythonExecutable,
                Arguments = $"-u \"{scriptPath}\" --unity-control",
                WorkingDirectory = Path.GetDirectoryName(scriptPath),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = false
            };
            cameraProcess = Process.Start(start);
            cameraStartedByUs = cameraProcess != null;
            cameraStatus = cameraStartedByUs ? "Iniciando cámara..." : "No se pudo iniciar Python";
            if (cameraProcess != null)
            {
                cameraProcess.ErrorDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) UnityEngine.Debug.Log($"[LegCamera] {e.Data}"); };
                cameraProcess.BeginErrorReadLine();
            }
        }
        catch (Exception exception)
        {
            cameraStatus = $"Error Python: {exception.Message}";
            Debug.LogError($"[LegMotionInputReceiver] {cameraStatus}");
        }
    }

    public void ChangeCamera()
    {
        if (!CameraRunning) StartCamera();
        SendControl("CAMBIAR_CAMARA");
        cameraStatus = "Buscando otra cámara...";
    }

    public void StopCamera()
    {
        if (cameraProcess != null)
        {
            try { if (!cameraProcess.HasExited) cameraProcess.Kill(); }
            catch (Exception exception) { Debug.LogWarning($"[LegMotionInputReceiver] {exception.Message}"); }
            cameraProcess.Dispose();
            cameraProcess = null;
        }
        cameraStartedByUs = false;
        cameraStatus = "Cámara apagada";
        calibrationStatus = "Sin calibrar";
    }

    public void Calibrate()
    {
        if (!CameraRunning) StartCamera();
        SendControl("CALIBRAR");
        calibrationStatus = "Preparando calibración...";
    }

    private void SendControl(string command)
    {
        try
        {
            using (var sender = new UdpClient())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(command);
                sender.Send(bytes, bytes.Length, IPAddress.Loopback.ToString(), controlPort);
            }
        }
        catch (Exception exception) { Debug.LogWarning($"[LegMotionInputReceiver] No se pudo enviar control: {exception.Message}"); }
    }

    private void StartPreviewReceiver()
    {
        try
        {
            previewClient = new UdpClient(previewPort);
            previewRunning = true;
            previewThread = new Thread(() =>
            {
                IPEndPoint endpoint = new IPEndPoint(IPAddress.Any, previewPort);
                while (previewRunning)
                {
                    try
                    {
                        byte[] packet = previewClient.Receive(ref endpoint);
                        lock (previewLock) latestJpeg = packet;
                    }
                    catch (SocketException) { if (previewRunning) Thread.Sleep(30); }
                    catch (ObjectDisposedException) { return; }
                }
            }) { IsBackground = true, Name = "LegCameraPreview-UDP" };
            previewThread.Start();
        }
        catch (SocketException exception) { Debug.LogError($"[LegMotionInputReceiver] No se pudo abrir vista previa {previewPort}: {exception.Message}"); }
    }

    private void Update()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
        {
            if (Keyboard.current[activateCameraKey].wasPressedThisFrame) StartCamera();
            if (Keyboard.current[changeCameraKey].wasPressedThisFrame) ChangeCamera();
            if (Keyboard.current[calibrateCameraKey].wasPressedThisFrame) Calibrate();
        }
#elif ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(KeyCode.F6)) StartCamera();
        if (Input.GetKeyDown(KeyCode.F7)) ChangeCamera();
        if (Input.GetKeyDown(KeyCode.F8)) Calibrate();
#endif

        byte[] jpeg = null;
        lock (previewLock) { if (latestJpeg != null) { jpeg = latestJpeg; latestJpeg = null; } }
        if (jpeg != null && previewTexture != null)
        {
            previewTexture.LoadImage(jpeg);
            cameraStatus = "Cámara activa";
        }
        if (cameraProcess != null && cameraProcess.HasExited)
        {
            cameraStatus = $"Python finalizó ({cameraProcess.ExitCode})";
            cameraProcess.Dispose();
            cameraProcess = null;
            cameraStartedByUs = false;
        }
        if (LastCommand == "CAMARA_ACTIVA") cameraStatus = "Cámara activa";
        if (LastCommand.StartsWith("CAMARA ") && int.TryParse(LastCommand.Substring(7), out int cameraIndex))
        {
            selectedCameraIndex = cameraIndex;
            cameraStatus = $"Cámara {cameraIndex} activa";
        }
        if (LastCommand == "CALIBRANDO" || LastCommand == "CALIBRACION_LISTA" || LastCommand == "CALIBRACION_FALLIDA")
        {
            calibrationStatus = LastCommand.Replace('_', ' ');
        }
    }

    public string HudCameraStatus => $"{cameraStatus} | {calibrationStatus}\nF6 cámara  F7 cambiar  F8 calibrar";

    private void OnDestroy()
    {
        UnsubscribeVoice();
        StopCamera();
        previewRunning = false;
        previewClient?.Close();
        previewThread = null;
        if (previewTexture != null) Destroy(previewTexture);
    }

    private void UnsubscribeVoice()
    {
        if (voiceInput == null) return;
        voiceInput.LegCameraActivateRequested -= StartCamera;
        voiceInput.LegCameraChangeRequested -= ChangeCamera;
        voiceInput.LegCameraCalibrateRequested -= Calibrate;
    }

    private void ReceiveLoop()
    {
        IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, port);

        while (isRunning)
        {
            try
            {
                byte[] bytes = udpClient.Receive(ref remoteEndPoint);
                string command = Encoding.UTF8.GetString(bytes).Trim();
                ApplyCommand(command, remoteEndPoint);
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException exception)
            {
                if (isRunning)
                {
                    lastMessage = $"error UDP: {exception.Message}";
                }
            }
        }
    }

    private void ApplyCommand(string command, IPEndPoint sender)
    {
        string normalized = command.ToUpperInvariant();

        lock (stateLock)
        {
            lastPacketTime = ReadNowSeconds();
            lastCommand = normalized;
            lastMessage = $"{sender.Address}: {normalized}";

            if (normalized == "CALIBRANDO" || normalized == "CALIBRACION_LISTA" || normalized == "CALIBRACION_FALLIDA")
            {
                return;
            }

            switch (normalized)
            {
                case "ACELERA":
                case "ACELERAR":
                    throttle = 1f;
                    brake = 0f;
                    break;
                case "FRENA":
                case "FRENAR":
                    throttle = 0f;
                    brake = 1f;
                    break;
                default:
                    throttle = 0f;
                    brake = 0f;
                    break;
            }
        }
    }

    private static double ReadNowSeconds()
    {
        return (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
    }

    private static double ReadElapsedSeconds(double startTime)
    {
        return ReadNowSeconds() - startTime;
    }
}
