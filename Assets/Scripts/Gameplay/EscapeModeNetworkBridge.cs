using System;
using System.Collections.Generic;
using System.Linq;
using KartIHC.Kart;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using Stopwatch = System.Diagnostics.Stopwatch;

/// <summary>
/// Relays authoritative kart, gate and finish state through the host's NGO session.
/// The hacker's reconnaissance drone is controlled and rendered locally.
/// </summary>
public sealed class EscapeModeNetworkBridge : MonoBehaviour
{
    private const string KartMessage = "KartIHC.Escape.Kart";
    private const string DoorRequestMessage = "KartIHC.Escape.DoorRequest";
    private const string DoorStateMessage = "KartIHC.Escape.DoorState";
    private const string FinishMessage = "KartIHC.Escape.Finish";
    private const string StateRequestMessage = "KartIHC.Escape.StateRequest";
    private const string LatencyProbeMessage = "KartIHC.Escape.LatencyProbe";
    private const string LatencyReplyMessage = "KartIHC.Escape.LatencyReply";
    private const float KartSendInterval = 1f / 20f;
    private const float DroneTiltDeadzone = 0.12f;
    private const float DoorHackRangeMeters = 7f;
    private const int HackPatternLength = 6;
    private const float HackTimeLimitSeconds = 10f;
    private const float HackFailureLockSeconds = 15f;
    private const double LatencyProbeIntervalSeconds = 1.0;
    private const double LatencyProbeTimeoutSeconds = 3.0;

    private static EscapeModeNetworkBridge instance;
    private readonly bool[] doorStates = new bool[3];
    private NetworkManager networkManager;
    private KartController kart;
    private Rigidbody kartBody;
    private bool isHost;
    private bool isHacker;
    private bool networked;
    private bool networkDiagnosticsEnabled;
    private bool handlersRegistered;
    private float nextKartSend;
    private long nextLatencyProbeTimestamp;
    private ulong nextLatencySequence;
    private ulong latencyPeerId;
    private bool hasLatencyPeer;
    private readonly Dictionary<ulong, long> pendingLatencyProbes = new Dictionary<ulong, long>();
    private int latencyProbesSent;
    private int latencyRepliesReceived;
    private int latencyProbesLost;
    private double averageRoundTripMs;
    private double lastRoundTripMs;
    private string latencyTestStatus = "Esperando conexión de red";
    private int gyroDeviceId = -1;
    private bool gyroConnected;
    private Vector3 neutralGravity;
    private bool hasNeutralGravity;
    private float droneAltitudeInput;
    private float droneHorizontalInput;
    private float droneForwardInput;
    private float droneYawInput;
    private float nextTargetRefresh;
    private readonly int[] hackPattern = new int[HackPatternLength];
    private int hackDoorId = -1;
    private int hackPatternProgress;
    private float hackDeadline;
    private float droneLockedUntil;
    private bool hackMinigameActive;
    private readonly int[] targetDoorIds = { -1, -1, -1, -1 };
    private readonly float[] targetButtonHoldTimes = new float[4];
    private readonly bool[] targetButtonHoldHandled = new bool[4];
    private readonly string[] hackerTargetLabels = { "Sin objetivo cercano", "Sin objetivo cercano", "Sin objetivo cercano", "Sin objetivo cercano" };
    private Vector3 remoteKartPosition;
    private Quaternion remoteKartRotation;
    private Vector3 remoteKartVelocity;
    private bool hasRemoteKartState;
    private Camera droneCamera;
    private CharacterController droneController;
    private Camera savedGameplayCamera;
    private GameObject hackIndicator;
    private Transform hackIndicatorVisual;
    private TextMesh hackIndicatorText;
    private Material hackIndicatorMaterial;
    private string status = "";

    public static EscapeModeNetworkBridge Instance => instance;
    public bool HasWon { get; private set; }
    public bool IsHacker => isHacker;
    public IReadOnlyList<string> HackerTargetLabels => hackerTargetLabels;
    public bool AnyDoorOpen { get { foreach (bool open in doorStates) if (open) return true; return false; } }
    public bool ZoomUsed { get; private set; }
    public bool DroneHasMoved { get; private set; }
    public bool DroneAltitudeUsed { get; private set; }
    public bool DoorHackUsed { get; private set; }
    public bool HackMinigameActive => hackMinigameActive;
    public int HackMinigameDoorId => hackDoorId;
    public int HackMinigameProgress => hackPatternProgress;
    public int HackMinigameDirectionAt(int index) => index >= 0 && index < hackPattern.Length ? hackPattern[index] : -1;
    public float HackMinigameSecondsRemaining => hackMinigameActive ? Mathf.Max(0f, hackDeadline - Time.unscaledTime) : 0f;
    public float DroneLockSecondsRemaining => Mathf.Max(0f, droneLockedUntil - Time.unscaledTime);
    public bool DroneLocked => DroneLockSecondsRemaining > 0f;
    public string CurrentStatus => status;
    public Transform ReconDroneTransform => droneController != null ? droneController.transform : null;
    public bool NetworkTestActive => hasLatencyPeer;
    public bool NetworkDiagnosticsEnabled => networkDiagnosticsEnabled;
    public bool DroneGamepadConnected => Gamepad.current != null;
    public bool DroneGyroscopeConnected => gyroConnected;
    public string NetworkTestStatus => latencyTestStatus;
    public double LastRoundTripMilliseconds => lastRoundTripMs;
    public double AverageRoundTripMilliseconds => averageRoundTripMs;
    public int LatencyProbesSent => latencyProbesSent;
    public int LatencyRepliesReceived => latencyRepliesReceived;
    public int LatencyProbesLost => latencyProbesLost;
    public float LatencyPacketLossPercent => latencyRepliesReceived + latencyProbesLost == 0
        ? 0f
        : latencyProbesLost * 100f / (latencyRepliesReceived + latencyProbesLost);

    public static EscapeModeNetworkBridge StartForRole(bool hostRole, bool offlinePractice = false)
    {
        if (instance == null)
        {
            var bridgeObject = new GameObject("EscapeModeNetworkBridge");
            instance = bridgeObject.AddComponent<EscapeModeNetworkBridge>();
        }

        instance.Configure(hostRole, offlinePractice);
        return instance;
    }

    public static void Stop()
    {
        if (instance != null) Destroy(instance.gameObject);
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
    }

    private void Configure(bool hostRole, bool practice)
    {
        DroneHasMoved = false;
        DroneAltitudeUsed = false;
        ZoomUsed = false;
        DoorHackUsed = false;
        hackMinigameActive = false;
        hackDoorId = -1;
        hackPatternProgress = 0;
        droneLockedUntil = 0f;
        isHost = hostRole;
        isHacker = !hostRole;
        networkDiagnosticsEnabled = !practice;
        networked = !practice && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        status = practice ? "Práctica Hacker individual" : hostRole ? "Eres el conductor" : "Eres el hacker";
        EscapeCourseBuilder.Build();
        networkManager = NetworkManager.Singleton;
        kart = FindFirstObjectByType<KartController>();
        kartBody = kart != null ? kart.GetComponent<Rigidbody>() : null;

        if (kart != null)
        {
            kart.SetInputEnabled(hostRole);
            if (!hostRole && kartBody != null) kartBody.isKinematic = true;
            if (practice)
            {
                kart.enabled = false;
                foreach (Renderer renderer in kart.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
            }
        }

        if (isHacker)
        {
            CreateReconDrone();
            FindGyroDevice();
            RefreshHackerTargets();
        }

        RegisterMessagesWhenReady();
        EscapeModeHudFeed.Publish(status);
    }

    private void Update()
    {
        if (networkManager == null) networkManager = NetworkManager.Singleton;
        if (!handlersRegistered) RegisterMessagesWhenReady();

        if (isHost && networkManager != null && networkManager.IsListening && Time.unscaledTime >= nextKartSend)
        {
            nextKartSend = Time.unscaledTime + KartSendInterval;
            SendKartState();
        }

        if (!isHost && hasRemoteKartState && kart != null)
        {
            float blend = 1f - Mathf.Exp(-18f * Time.unscaledDeltaTime);
            Vector3 position = Vector3.Lerp(kart.transform.position, remoteKartPosition, blend);
            Quaternion rotation = Quaternion.Slerp(kart.transform.rotation, remoteKartRotation, blend);
            kart.transform.SetPositionAndRotation(position, rotation);
            if (kartBody != null)
            {
                kartBody.position = position;
                kartBody.rotation = rotation;
                kartBody.linearVelocity = remoteKartVelocity;
            }
        }

        UpdateLatencyTest();
        if (isHacker) UpdateHackerControls();
    }

    private void RegisterMessagesWhenReady()
    {
        if (!networked || handlersRegistered || networkManager == null || !networkManager.IsListening || networkManager.CustomMessagingManager == null)
            return;

        networkManager.CustomMessagingManager.RegisterNamedMessageHandler(KartMessage, ReceiveKartState);
        networkManager.CustomMessagingManager.RegisterNamedMessageHandler(DoorRequestMessage, ReceiveDoorRequest);
        networkManager.CustomMessagingManager.RegisterNamedMessageHandler(DoorStateMessage, ReceiveDoorState);
        networkManager.CustomMessagingManager.RegisterNamedMessageHandler(FinishMessage, ReceiveFinish);
        networkManager.CustomMessagingManager.RegisterNamedMessageHandler(StateRequestMessage, ReceiveStateRequest);
        networkManager.CustomMessagingManager.RegisterNamedMessageHandler(LatencyProbeMessage, ReceiveLatencyProbe);
        networkManager.CustomMessagingManager.RegisterNamedMessageHandler(LatencyReplyMessage, ReceiveLatencyReply);
        networkManager.OnClientDisconnectCallback += OnClientDisconnected;
        handlersRegistered = true;

        if (!isHost)
        {
            using (var writer = new FastBufferWriter(1, Allocator.Temp))
                networkManager.CustomMessagingManager.SendNamedMessage(StateRequestMessage, NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableSequenced);
        }
    }

    private void UpdateLatencyTest()
    {
        if (networkManager == null || !networkManager.IsListening || !TryGetLatencyPeer(out ulong peerId))
        {
            if (hasLatencyPeer)
            {
                hasLatencyPeer = false;
                pendingLatencyProbes.Clear();
            }
            latencyTestStatus = "Esperando al otro jugador";
            return;
        }

        if (!hasLatencyPeer || latencyPeerId != peerId)
        {
            latencyPeerId = peerId;
            hasLatencyPeer = true;
            pendingLatencyProbes.Clear();
            latencyProbesSent = 0;
            latencyRepliesReceived = 0;
            latencyProbesLost = 0;
            averageRoundTripMs = 0d;
            lastRoundTripMs = 0d;
            nextLatencyProbeTimestamp = 0;
        }

        long now = Stopwatch.GetTimestamp();
        long timeoutTicks = (long)(Stopwatch.Frequency * LatencyProbeTimeoutSeconds);
        List<ulong> expired = null;
        foreach (KeyValuePair<ulong, long> probe in pendingLatencyProbes)
        {
            if (now - probe.Value < timeoutTicks) continue;
            if (expired == null) expired = new List<ulong>();
            expired.Add(probe.Key);
        }
        if (expired != null)
        {
            foreach (ulong sequence in expired)
            {
                pendingLatencyProbes.Remove(sequence);
                latencyProbesLost++;
            }
        }

        if (nextLatencyProbeTimestamp == 0 || now >= nextLatencyProbeTimestamp)
        {
            ulong sequence = ++nextLatencySequence;
            pendingLatencyProbes[sequence] = now;
            latencyProbesSent++;
            using (var writer = new FastBufferWriter(sizeof(ulong), Allocator.Temp))
            {
                writer.WriteValueSafe(sequence);
                networkManager.CustomMessagingManager.SendNamedMessage(
                    LatencyProbeMessage, peerId, writer, NetworkDelivery.UnreliableSequenced);
            }
            nextLatencyProbeTimestamp = now + (long)(Stopwatch.Frequency * LatencyProbeIntervalSeconds);
        }

        latencyTestStatus = latencyRepliesReceived == 0
            ? latencyProbesLost == 0
                ? "Conectado · midiendo RTT..."
                : $"Conectado · sin respuestas ({latencyProbesLost} sondas perdidas)"
            : $"Conectado · RTT {lastRoundTripMs:0} ms · promedio {averageRoundTripMs:0} ms · pérdida {LatencyPacketLossPercent:0.0}%";
    }

    private bool TryGetLatencyPeer(out ulong peerId)
    {
        peerId = 0;
        if (networkManager == null || !networkManager.IsListening) return false;

        if (networkManager.IsHost)
        {
            foreach (ulong clientId in networkManager.ConnectedClientsIds)
            {
                if (clientId == networkManager.LocalClientId) continue;
                peerId = clientId;
                return true;
            }
            return false;
        }

        if (networkManager.IsConnectedClient)
        {
            peerId = NetworkManager.ServerClientId;
            return true;
        }
        return false;
    }

    private void ReceiveLatencyProbe(ulong senderClientId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out ulong sequence);
        if (networkManager == null || !networkManager.IsListening) return;
        using (var writer = new FastBufferWriter(sizeof(ulong), Allocator.Temp))
        {
            writer.WriteValueSafe(sequence);
            networkManager.CustomMessagingManager.SendNamedMessage(
                LatencyReplyMessage, senderClientId, writer, NetworkDelivery.UnreliableSequenced);
        }
    }

    private void ReceiveLatencyReply(ulong senderClientId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out ulong sequence);
        if (!hasLatencyPeer || senderClientId != latencyPeerId ||
            !pendingLatencyProbes.TryGetValue(sequence, out long sentTimestamp)) return;

        pendingLatencyProbes.Remove(sequence);
        lastRoundTripMs = (Stopwatch.GetTimestamp() - sentTimestamp) * 1000d / Stopwatch.Frequency;
        latencyRepliesReceived++;
        averageRoundTripMs += (lastRoundTripMs - averageRoundTripMs) / latencyRepliesReceived;
    }

    private void SendKartState(ulong? targetClientId = null)
    {
        if (kart == null || networkManager.CustomMessagingManager == null) return;
        using (var writer = new FastBufferWriter(64, Allocator.Temp))
        {
            writer.WriteValueSafe(kart.transform.position);
            writer.WriteValueSafe(kart.transform.rotation);
            writer.WriteValueSafe(kartBody != null ? kartBody.linearVelocity : Vector3.zero);
            if (targetClientId.HasValue)
                networkManager.CustomMessagingManager.SendNamedMessage(KartMessage, targetClientId.Value, writer, NetworkDelivery.UnreliableSequenced);
            else
                networkManager.CustomMessagingManager.SendNamedMessageToAll(KartMessage, writer, NetworkDelivery.UnreliableSequenced);
        }
    }

    private void ReceiveKartState(ulong senderClientId, FastBufferReader reader)
    {
        if (isHost || senderClientId != NetworkManager.ServerClientId || kart == null) return;
        reader.ReadValueSafe(out Vector3 position);
        reader.ReadValueSafe(out Quaternion rotation);
        reader.ReadValueSafe(out Vector3 velocity);
        remoteKartPosition = position;
        remoteKartRotation = rotation;
        remoteKartVelocity = velocity;
        hasRemoteKartState = true;
    }

    private void UpdateHackerControls()
    {
        if (Time.unscaledTime >= nextTargetRefresh)
        {
            nextTargetRefresh = Time.unscaledTime + 0.2f;
            RefreshHackerTargets();
        }
        UpdateHackIndicator();

        if (hackMinigameActive && Time.unscaledTime >= hackDeadline)
        {
            FailHackMinigame("Se agotó el tiempo");
            return;
        }

        Gamepad pad = Gamepad.current;
        if (pad == null)
        {
            status = "Conecta el mando PS4 para controlar el dron";
            return;
        }
        if (DroneLocked)
        {
            UpdateDroneMotion(pad, allowMovement: false);
            return;
        }
        if (droneLockedUntil > 0f)
        {
            droneLockedUntil = 0f;
            status = "Dron desbloqueado";
            EscapeModeHudFeed.Publish(status);
        }
        if (hackMinigameActive)
        {
            UpdateHackMinigame(pad);
            return;
        }
        // Square (West on a DualShock layout) is reserved for door hacking.
        // The other face buttons remain available for future hacker actions.
        UpdateTargetButton(pad.buttonWest, 0);
        UpdateDroneMotion(pad);
    }

    private void UpdateTargetButton(ButtonControl button, int slot)
    {
        if (button.wasPressedThisFrame)
        {
            targetButtonHoldTimes[slot] = 0f;
            targetButtonHoldHandled[slot] = false;
        }

        if (button.isPressed)
        {
            targetButtonHoldTimes[slot] += Time.unscaledDeltaTime;
            if (!targetButtonHoldHandled[slot] && targetButtonHoldTimes[slot] >= 0.35f)
            {
                targetButtonHoldHandled[slot] = true;
                InteractWithTarget(slot);
            }
        }
        else
        {
            targetButtonHoldTimes[slot] = 0f;
            targetButtonHoldHandled[slot] = false;
        }
    }

    private void StartHackMinigame(int doorId)
    {
        if (DroneLocked || hackMinigameActive) return;
        hackDoorId = doorId;
        hackPatternProgress = 0;
        for (int i = 0; i < hackPattern.Length; i++)
            hackPattern[i] = UnityEngine.Random.Range(0, 4);
        hackDeadline = Time.unscaledTime + HackTimeLimitSeconds;
        hackMinigameActive = true;
        status = $"Hackeo de puerta {doorId + 1}: repite la secuencia con la cruceta";
        EscapeModeHudFeed.Publish(status);
    }

    private void UpdateHackMinigame(Gamepad pad)
    {
        if (Time.unscaledTime >= hackDeadline)
        {
            FailHackMinigame("Se agotó el tiempo");
            return;
        }

        int direction = -1;
        int pressedCount = 0;
        ReadDpadPress(pad.dpad.up, 0, ref direction, ref pressedCount);
        ReadDpadPress(pad.dpad.down, 1, ref direction, ref pressedCount);
        ReadDpadPress(pad.dpad.left, 2, ref direction, ref pressedCount);
        ReadDpadPress(pad.dpad.right, 3, ref direction, ref pressedCount);
        if (pressedCount == 0) return;
        if (pressedCount > 1 || direction != hackPattern[hackPatternProgress])
        {
            FailHackMinigame("Patrón incorrecto");
            return;
        }

        hackPatternProgress++;
        if (hackPatternProgress >= HackPatternLength)
            CompleteHackMinigame();
    }

    private static void ReadDpadPress(ButtonControl button, int direction, ref int selectedDirection, ref int pressedCount)
    {
        if (!button.wasPressedThisFrame) return;
        selectedDirection = direction;
        pressedCount++;
    }

    private void CompleteHackMinigame()
    {
        int doorId = hackDoorId;
        hackMinigameActive = false;
        hackDoorId = -1;
        hackPatternProgress = HackPatternLength;
        DoorHackUsed = true;

        if (PythonEscapeModeBridge.Instance != null)
        {
            PythonEscapeModeBridge.Instance.ToggleDoor(doorId);
        }
        else if (doorId >= 0 && doorId < doorStates.Length)
        {
            RequestDoorState(doorId, !doorStates[doorId]);
        }

        status = "Hackeo completado";
        EscapeModeHudFeed.Publish(status);
        RefreshHackerTargets();
    }

    private void FailHackMinigame(string reason)
    {
        hackMinigameActive = false;
        hackDoorId = -1;
        hackPatternProgress = 0;
        droneLockedUntil = Time.unscaledTime + HackFailureLockSeconds;
        status = $"{reason}: dron bloqueado por {HackFailureLockSeconds:0} s";
        EscapeModeHudFeed.Publish(status);
    }

    private void CreateReconDrone()
    {
        if (savedGameplayCamera == null) savedGameplayCamera = Camera.main;
        if (savedGameplayCamera != null) savedGameplayCamera.enabled = false;
        if (droneController != null)
        {
            Destroy(droneController.gameObject);
            droneController = null;
            droneCamera = null;
        }

        var drone = new GameObject("EscapeReconDrone");
        Transform spawn = GameObject.Find("EscapeDroneSpawn")?.transform;
        if (spawn != null)
            drone.transform.SetPositionAndRotation(spawn.position, spawn.rotation);
        else
        {
            drone.transform.position = new Vector3(0f, 20f, -27f);
            drone.transform.rotation = Quaternion.LookRotation(new Vector3(0f, -0.42f, 1f), Vector3.up);
        }
        droneController = drone.AddComponent<CharacterController>();
        droneController.height = 0.7f;
        droneController.radius = 0.35f;
        droneController.center = Vector3.zero;
        droneController.skinWidth = 0.04f;
        GameObject cameraObject = new GameObject("DroneCamera");
        cameraObject.transform.SetParent(drone.transform, false);
        droneCamera = cameraObject.AddComponent<Camera>();
        droneCamera.fieldOfView = 60f;
        droneCamera.nearClipPlane = 0.1f;
        droneCamera.enabled = true;
        status = "Dron de reconocimiento activo";
        EscapeModeHudFeed.Publish(status);
    }

    private void UpdateDroneMotion(Gamepad pad, bool allowMovement = true)
    {
        if (droneController == null || droneCamera == null) return;

        if (pad.startButton.wasPressedThisFrame)
            CalibrateDroneGyroscope();

        if (gyroDeviceId < 0) FindGyroDevice();
        Vector2 tilt = ReadDroneTilt(pad);
        droneHorizontalInput = Mathf.Lerp(droneHorizontalInput, tilt.x, Time.unscaledDeltaTime * 7f);
        droneForwardInput = Mathf.Lerp(droneForwardInput, tilt.y, Time.unscaledDeltaTime * 7f);
        droneAltitudeInput = pad.leftStick.y.ReadValue();
        if (Mathf.Abs(droneAltitudeInput) < 0.15f) droneAltitudeInput = 0f;
        droneYawInput = pad.leftStick.x.ReadValue();
        if (Mathf.Abs(droneYawInput) < 0.15f) droneYawInput = 0f;
        if (!allowMovement)
        {
            droneAltitudeInput = 0f;
            droneHorizontalInput = 0f;
            droneForwardInput = 0f;
            droneYawInput = 0f;
        }

        // Tilt controls translation relative to the drone's heading. Turning
        // the controller around its vertical axis cannot move the drone.
        Vector3 horizontalMovement = droneController.transform.right * droneHorizontalInput +
                                    droneController.transform.forward * droneForwardInput;
        Vector3 movement = horizontalMovement * 9f + Vector3.up * (droneAltitudeInput * 5f);
        droneController.Move(movement * Time.unscaledDeltaTime);
        droneController.transform.Rotate(Vector3.up, droneYawInput * 100f * Time.unscaledDeltaTime, Space.World);

        // Give the pilot helicopter-like flight feedback without tilting the
        // CharacterController or changing the movement axes.
        Quaternion cameraTilt = Quaternion.Euler(droneForwardInput * 14f, 0f, -droneHorizontalInput * 11f);
        droneCamera.transform.localRotation = Quaternion.Slerp(
            droneCamera.transform.localRotation, cameraTilt, Time.unscaledDeltaTime * 5f);

        if (horizontalMovement.sqrMagnitude > 0.025f) DroneHasMoved = true;
        if (Mathf.Abs(droneAltitudeInput) > 0.2f) DroneAltitudeUsed = true;

        float zoom = pad.rightTrigger.ReadValue() - pad.leftTrigger.ReadValue();
        if (Mathf.Abs(zoom) > 0.15f) ZoomUsed = true;
        droneCamera.fieldOfView = Mathf.Clamp(droneCamera.fieldOfView - zoom * 55f * Time.unscaledDeltaTime, 25f, 85f);
    }

    private void CalibrateDroneGyroscope()
    {
        if (gyroDeviceId < 0) FindGyroDevice();
        if (gyroDeviceId < 0)
        {
            status = "No se detectó giroscopio; la palanca derecha sigue controlando el vuelo";
            EscapeModeHudFeed.Publish(status);
            return;
        }

        try
        {
            if (!JoyShockLibraryNative.JslStillConnected(gyroDeviceId))
            {
                gyroDeviceId = -1;
                gyroConnected = false;
                hasNeutralGravity = false;
                status = "Mando desconectado; no se pudo recalibrar";
            }
            else
            {
                JoyShockLibraryNative.MotionState motion = JoyShockLibraryNative.JslGetMotionState(gyroDeviceId);
                neutralGravity = new Vector3(motion.gravX, motion.gravY, motion.gravZ);
                hasNeutralGravity = true;
                droneHorizontalInput = 0f;
                droneForwardInput = 0f;
                status = "Orientación neutra del dron recalibrada";
            }
        }
        catch (DllNotFoundException) { status = "Falta JoyShockLibrary; usa la palanca derecha"; }
        catch (EntryPointNotFoundException) { status = "JoyShockLibrary no compatible; usa la palanca derecha"; }
        EscapeModeHudFeed.Publish(status);
    }

    private Vector2 ReadDroneTilt(Gamepad pad)
    {
        // If JoyShockLibrary cannot read the controller IMU, keep drone flight
        // available through the right stick. The left stick remains altitude.
        if (gyroDeviceId < 0) return pad.rightStick.ReadValue();
        try
        {
            if (!JoyShockLibraryNative.JslStillConnected(gyroDeviceId))
            {
                gyroDeviceId = -1;
                gyroConnected = false;
                return pad.rightStick.ReadValue();
            }

            JoyShockLibraryNative.MotionState motion = JoyShockLibraryNative.JslGetMotionState(gyroDeviceId);
            if (!hasNeutralGravity)
            {
                neutralGravity = new Vector3(motion.gravX, motion.gravY, motion.gravZ);
                hasNeutralGravity = true;
            }

            float strafe = Mathf.Clamp((motion.gravX - neutralGravity.x) * 2.2f, -1f, 1f);
            // Local gravity rests mostly on the Y axis; pitch is therefore
            // read from Z so forward and backward produce opposite values.
            float forward = Mathf.Clamp((neutralGravity.z - motion.gravZ) * 2.2f, -1f, 1f);
            strafe = ApplyDroneTiltDeadzone(strafe);
            forward = ApplyDroneTiltDeadzone(forward);
            return new Vector2(strafe, forward);
        }
        catch (DllNotFoundException) { gyroConnected = false; status = "Sin JoyShockLibrary: usa la palanca derecha para volar"; }
        catch (EntryPointNotFoundException) { gyroConnected = false; status = "JoyShockLibrary no compatible: usa la palanca derecha"; }
        return pad.rightStick.ReadValue();
    }

    private static float ApplyDroneTiltDeadzone(float value)
    {
        float magnitude = Mathf.Abs(value);
        if (magnitude <= DroneTiltDeadzone) return 0f;
        return Mathf.Sign(value) * ((magnitude - DroneTiltDeadzone) / (1f - DroneTiltDeadzone));
    }

    private void FindGyroDevice()
    {
        gyroConnected = false;
        try
        {
            int count = JoyShockLibraryNative.JslConnectDevices();
            if (count <= 0) return;
            int[] handles = new int[count];
            JoyShockLibraryNative.JslGetConnectedDeviceHandles(handles, handles.Length);
            foreach (int handle in handles)
            {
                if (!JoyShockLibraryNative.JslStillConnected(handle)) continue;
                int controllerType = JoyShockLibraryNative.JslGetControllerType(handle);
                if (controllerType == JoyShockLibraryNative.ControllerTypeDualShock4 ||
                    controllerType == JoyShockLibraryNative.ControllerTypeDualSense)
                {
                    gyroDeviceId = handle;
                    gyroConnected = true;
                    JoyShockLibraryNative.MotionState motion = JoyShockLibraryNative.JslGetMotionState(handle);
                    neutralGravity = new Vector3(motion.gravX, motion.gravY, motion.gravZ);
                    hasNeutralGravity = true;
                    return;
                }
            }
        }
        catch (DllNotFoundException) { status = "Sin JoyShockLibrary: usa la palanca derecha para volar"; }
        catch (EntryPointNotFoundException) { status = "JoyShockLibrary no compatible: usa la palanca derecha"; }
    }

    private void RefreshHackerTargets()
    {
        foreach (int slot in Enumerable.Range(0, targetDoorIds.Length))
        {
            targetDoorIds[slot] = -1;
            hackerTargetLabels[slot] = slot == 0
                ? $"□ Puerta más cercana · rango {DoorHackRangeMeters:0} m"
                : new[] { "", "△ Acción futura", "○ Acción futura", "× Acción futura" }[slot];
        }

        if (droneController == null) return;
        EscapeDoor nearest = EscapeCourseBuilder.Doors
            .Where(door => (door.transform.position - droneController.transform.position).sqrMagnitude <=
                           DoorHackRangeMeters * DoorHackRangeMeters)
            .OrderBy(door => (door.transform.position - droneController.transform.position).sqrMagnitude)
            .FirstOrDefault();
        if (nearest == null)
        {
            hackerTargetLabels[0] = $"□ Sin puerta a alcance · acércate ({DoorHackRangeMeters:0} m)";
            return;
        }

        targetDoorIds[0] = nearest.Id;
        float distance = Vector3.Distance(nearest.transform.position, droneController.transform.position);
        string doorState = nearest.Id >= 0 && nearest.Id < doorStates.Length && doorStates[nearest.Id] ? "abierta" : "cerrada";
        hackerTargetLabels[0] = $"□ Puerta {nearest.Id + 1} · {distance:0.0} m · {doorState}";
    }

    private void UpdateHackIndicator()
    {
        bool canShow = droneController != null && droneCamera != null && !hackMinigameActive &&
                       !DroneLocked && targetDoorIds[0] >= 0;
        if (!canShow)
        {
            if (hackIndicator != null) hackIndicator.SetActive(false);
            return;
        }

        EscapeDoor door = EscapeCourseBuilder.Doors.FirstOrDefault(candidate => candidate.Id == targetDoorIds[0]);
        if (door == null)
        {
            if (hackIndicator != null) hackIndicator.SetActive(false);
            return;
        }

        EnsureHackIndicator();
        hackIndicator.SetActive(true);
        hackIndicator.transform.position = door.transform.position + Vector3.up * 2.2f;
        Vector3 towardCamera = hackIndicator.transform.position - droneCamera.transform.position;
        if (towardCamera.sqrMagnitude > 0.001f)
            hackIndicator.transform.rotation = Quaternion.LookRotation(towardCamera, Vector3.up);

        hackIndicatorText.text = $"HACK · {door.Id + 1}";
        float pulse = 1f + Mathf.Sin(Time.unscaledTime * 4f) * 0.08f;
        hackIndicatorVisual.localScale = Vector3.one * (0.28f * pulse);
    }

    private void EnsureHackIndicator()
    {
        if (hackIndicator != null) return;

        hackIndicator = new GameObject("HackableDoor3DIndicator");
        hackIndicator.SetActive(false);

        GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
        marker.name = "HackIndicatorMarker";
        marker.transform.SetParent(hackIndicator.transform, false);
        marker.transform.localPosition = new Vector3(0f, -0.45f, 0f);
        marker.transform.localRotation = Quaternion.Euler(45f, 45f, 0f);
        marker.transform.localScale = new Vector3(0.28f, 0.28f, 0.28f);
        Collider markerCollider = marker.GetComponent<Collider>();
        if (markerCollider != null) Destroy(markerCollider);
        Renderer markerRenderer = marker.GetComponent<Renderer>();
        if (markerRenderer != null)
        {
            hackIndicatorMaterial = markerRenderer.material;
            hackIndicatorMaterial.color = new Color(1f, 0.68f, 0.12f);
        }
        hackIndicatorVisual = marker.transform;

        GameObject label = new GameObject("HackIndicatorLabel");
        label.transform.SetParent(hackIndicator.transform, false);
        label.transform.localPosition = new Vector3(0f, 0.05f, 0f);
        hackIndicatorText = label.AddComponent<TextMesh>();
        hackIndicatorText.text = "HACK";
        hackIndicatorText.anchor = TextAnchor.MiddleCenter;
        hackIndicatorText.alignment = TextAlignment.Center;
        hackIndicatorText.fontStyle = FontStyle.Bold;
        hackIndicatorText.fontSize = 64;
        hackIndicatorText.characterSize = 0.12f;
        hackIndicatorText.color = new Color(1f, 0.84f, 0.35f);
    }

    private void InteractWithTarget(int slot)
    {
        if (slot != 0 || droneController == null || targetDoorIds[0] < 0)
        {
            status = $"No hay puerta a alcance para hackear (máx. {DoorHackRangeMeters:0} m)";
            EscapeModeHudFeed.Publish(status);
            return;
        }

        int doorId = targetDoorIds[0];
        EscapeDoor door = EscapeCourseBuilder.Doors.FirstOrDefault(candidate => candidate.Id == doorId);
        if (door == null || Vector3.Distance(door.transform.position, droneController.transform.position) > DoorHackRangeMeters)
        {
            RefreshHackerTargets();
            status = $"Acércate a la puerta; el hackeo requiere estar a {DoorHackRangeMeters:0} m o menos";
            EscapeModeHudFeed.Publish(status);
            return;
        }

        StartHackMinigame(doorId);
    }

    private void RequestDoorState(int doorId, bool open)
    {
        if (!networked)
        {
            ApplyDoorState(doorId, open, false);
            return;
        }
        if (isHost)
        {
            ApplyDoorState(doorId, open, true);
            return;
        }
        if (networkManager == null || !networkManager.IsListening) return;
        using (var writer = new FastBufferWriter(8, Allocator.Temp))
        {
            writer.WriteValueSafe(doorId);
            writer.WriteValueSafe(open);
            networkManager.CustomMessagingManager.SendNamedMessage(DoorRequestMessage, NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableSequenced);
        }
    }

    private void ReceiveDoorRequest(ulong senderClientId, FastBufferReader reader)
    {
        if (!isHost || senderClientId == NetworkManager.ServerClientId) return;
        reader.ReadValueSafe(out int doorId);
        reader.ReadValueSafe(out bool open);
        ApplyDoorState(doorId, open, true);
    }

    private void ApplyDoorState(int doorId, bool open, bool broadcast)
    {
        if (doorId < 0 || doorId >= doorStates.Length) return;
        bool changed = doorStates[doorId] != open;
        doorStates[doorId] = open;
        if (doorId < EscapeCourseBuilder.Doors.Count)
            EscapeCourseBuilder.Doors[doorId].SetOpen(open);
        if (changed) EscapeModeHudFeed.Publish($"Puerta {doorId + 1}: {(open ? "abierta" : "cerrada")}");

        if (broadcast && isHost && networkManager != null && networkManager.IsListening)
        {
            using (var writer = new FastBufferWriter(8, Allocator.Temp))
            {
                writer.WriteValueSafe(doorId);
                writer.WriteValueSafe(open);
                networkManager.CustomMessagingManager.SendNamedMessageToAll(DoorStateMessage, writer, NetworkDelivery.ReliableSequenced);
            }
        }
    }

    public void ApplyExternalDoorState(int doorId, bool open)
    {
        ApplyDoorState(doorId, open, false);
    }

    public void ApplyExternalFinish()
    {
        if (HasWon) return;
        HasWon = true;
        status = "¡Victoria compartida! Conductor y hacker ganan.";
        EscapeModeHudFeed.Publish(status);
    }

    private void ReceiveDoorState(ulong senderClientId, FastBufferReader reader)
    {
        if (senderClientId != NetworkManager.ServerClientId) return;
        reader.ReadValueSafe(out int doorId);
        reader.ReadValueSafe(out bool open);
        ApplyDoorState(doorId, open, false);
    }

    public void ReportDriverFinished()
    {
        if (!isHost || HasWon) return;
        if (PythonEscapeModeBridge.Instance != null)
        {
            PythonEscapeModeBridge.Instance.ReportFinished();
            ApplyExternalFinish();
            return;
        }
        HasWon = true;
        status = "¡Victoria compartida! Conductor y hacker ganan.";
        EscapeModeHudFeed.Publish(status);
        if (networkManager != null && networkManager.IsListening)
        {
            using (var writer = new FastBufferWriter(1, Allocator.Temp))
                networkManager.CustomMessagingManager.SendNamedMessageToAll(FinishMessage, writer, NetworkDelivery.ReliableSequenced);
        }
    }

    private void ReceiveFinish(ulong senderClientId, FastBufferReader reader)
    {
        if (senderClientId != NetworkManager.ServerClientId) return;
        HasWon = true;
        status = "¡Victoria compartida! Conductor y hacker ganan.";
        EscapeModeHudFeed.Publish(status);
    }

    private void ReceiveStateRequest(ulong senderClientId, FastBufferReader reader)
    {
        if (!isHost || senderClientId == NetworkManager.ServerClientId) return;
        SendKartState(senderClientId);
        for (int i = 0; i < doorStates.Length; i++)
            SendDoorState(senderClientId, i, doorStates[i]);
        if (HasWon)
        {
            using (var writer = new FastBufferWriter(1, Allocator.Temp))
                networkManager.CustomMessagingManager.SendNamedMessage(FinishMessage, senderClientId, writer, NetworkDelivery.ReliableSequenced);
        }
    }

    private void SendDoorState(ulong clientId, int doorId, bool open)
    {
        using (var writer = new FastBufferWriter(8, Allocator.Temp))
        {
            writer.WriteValueSafe(doorId);
            writer.WriteValueSafe(open);
            networkManager.CustomMessagingManager.SendNamedMessage(DoorStateMessage, clientId, writer, NetworkDelivery.ReliableSequenced);
        }
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (isHost || networkManager == null || clientId != networkManager.LocalClientId) return;
        status = "Se perdió la conexión. Buscando reconexión...";
        EscapeModeHudFeed.Publish(status);
        EscapeModeSessionController.Instance?.RetryHackerConnection();
    }

    private void OnDestroy()
    {
        if (droneCamera != null) droneCamera.enabled = false;
        if (isHacker && savedGameplayCamera != null) savedGameplayCamera.enabled = true;
        if (droneController != null) Destroy(droneController.gameObject);
        if (hackIndicator != null) Destroy(hackIndicator);
        if (hackIndicatorMaterial != null) Destroy(hackIndicatorMaterial);
        if (networkManager != null && handlersRegistered && networkManager.CustomMessagingManager != null)
        {
            networkManager.CustomMessagingManager.UnregisterNamedMessageHandler(KartMessage);
            networkManager.CustomMessagingManager.UnregisterNamedMessageHandler(DoorRequestMessage);
            networkManager.CustomMessagingManager.UnregisterNamedMessageHandler(DoorStateMessage);
            networkManager.CustomMessagingManager.UnregisterNamedMessageHandler(FinishMessage);
            networkManager.CustomMessagingManager.UnregisterNamedMessageHandler(StateRequestMessage);
            networkManager.OnClientDisconnectCallback -= OnClientDisconnected;
        }
        if (instance == this) instance = null;
    }
}
