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
/// Relays authoritative kart, police, gate and finish state through the host's NGO session.
/// The hacker's reconnaissance drone is controlled and rendered locally.
/// </summary>
public sealed class EscapeModeNetworkBridge : MonoBehaviour
{
    private const string KartMessage = "KartIHC.Escape.Kart";
    private const string DroneStateMessage = "KartIHC.Escape.Drone";
    private const string PoliceStateMessage = "KartIHC.Escape.Police";
    private const string DoorRequestMessage = "KartIHC.Escape.DoorRequest";
    private const string DoorStateMessage = "KartIHC.Escape.DoorState";
    private const string FinishMessage = "KartIHC.Escape.Finish";
    private const string LossMessage = "KartIHC.Escape.Loss";
    private const string StateRequestMessage = "KartIHC.Escape.StateRequest";
    private const string LatencyProbeMessage = "KartIHC.Escape.LatencyProbe";
    private const string LatencyReplyMessage = "KartIHC.Escape.LatencyReply";
    private const string CommunicationMessage = "KartIHC.Escape.Communication";
    private const float KartSendInterval = 1f / 20f;
    private const float PoliceSendInterval = 1f / 10f;
    private const float DroneTiltDeadzone = 0.12f;
    private const float DoorHackRangeMeters = 7f;
    private const int HackPatternLength = 6;
    private const float HackTimeLimitSeconds = 10f;
    private const float HackFailureLockSeconds = 15f;
    private const double LatencyProbeIntervalSeconds = 1.0;
    private const double LatencyProbeTimeoutSeconds = 3.0;

    private static EscapeModeNetworkBridge instance;
    private readonly bool[] doorStates = new bool[3];
    private readonly List<string> communicationMessages = new List<string>();
    private NetworkManager networkManager;
    private KartController kart;
    private Rigidbody kartBody;
    private bool isHost;
    private bool isHacker;
    private bool networked;
    private bool networkDiagnosticsEnabled;
    private bool handlersRegistered;
    private float nextKartSend;
    private float nextPoliceSend;
    private float nextDroneSend;
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
    private float droneLookPitch;
    private bool droneMouseLookActive;
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
    private Vector3 remoteDronePosition;
    private Quaternion remoteDroneRotation = Quaternion.identity;
    private bool hasRemoteDroneState;
    private GameObject remoteDroneVisual;
    private readonly List<Material> remoteDroneMaterials = new List<Material>();
    private Camera droneCamera;
    private CharacterController droneController;
    private Camera savedGameplayCamera;
    private GameObject hackIndicator;
    private Transform hackIndicatorVisual;
    private TextMesh hackIndicatorText;
    private Material hackIndicatorMaterial;
    private GameObject communicationMarker;
    private TextMesh communicationMarkerText;
    private float communicationMarkerExpiresAt;
    private VoicePowerUpInput voiceInput;
    private string status = "";

    public static EscapeModeNetworkBridge Instance => instance;
    public bool HasWon { get; private set; }
    public bool HasLost { get; private set; }
    public bool IsHacker => isHacker;
    public bool IsOfflinePractice => !networked && PythonEscapeModeBridge.Instance == null;
    public IReadOnlyList<string> HackerTargetLabels => hackerTargetLabels;
    public IReadOnlyList<string> CommunicationMessages => communicationMessages;
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
        if (instance != null)
        {
            EscapePoliceStationGenerator.Stop();
            Destroy(instance.gameObject);
        }
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
        communicationMessages.Clear();
        if (communicationMarker != null) Destroy(communicationMarker);
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
            if (practice && !hostRole)
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

        // Build the station and local patrol presentation for both roles in
        // cooperative Escape. Escape Solo additionally uses its own exit rules.
        if (kart != null && (!practice || isHost))
            EscapePoliceStationGenerator.StartForEscape(kart, this, isHost, practice && isHost);

        RegisterMessagesWhenReady();
        BindVoiceInput();
        EscapeModeHudFeed.Publish(status);
    }

    private void Update()
    {
        if (networkManager == null) networkManager = NetworkManager.Singleton;
        if (!handlersRegistered) RegisterMessagesWhenReady();
        if (voiceInput == null) BindVoiceInput();
        if (communicationMarker != null)
        {
            if (Time.unscaledTime >= communicationMarkerExpiresAt) Destroy(communicationMarker);
            else
            {
                Camera view = isHacker ? droneCamera : Camera.main;
                if (communicationMarkerText != null && view != null)
                {
                    Vector3 towardCamera = view.transform.position - communicationMarkerText.transform.position;
                    if (towardCamera.sqrMagnitude > 0.001f)
                        communicationMarkerText.transform.rotation = Quaternion.LookRotation(towardCamera, Vector3.up);
                }
            }
        }

        if (isHost && networkManager != null && networkManager.IsListening && Time.unscaledTime >= nextKartSend)
        {
            nextKartSend = Time.unscaledTime + KartSendInterval;
            SendKartState();
        }

        if (isHost && networkManager != null && networkManager.IsListening && Time.unscaledTime >= nextPoliceSend)
        {
            nextPoliceSend = Time.unscaledTime + PoliceSendInterval;
            SendPoliceState();
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

        if (isHacker && networked && droneController != null && networkManager != null &&
            networkManager.IsListening && Time.unscaledTime >= nextDroneSend)
        {
            nextDroneSend = Time.unscaledTime + KartSendInterval;
            SendDroneState();
        }
        UpdateRemoteDroneVisual();

        UpdateLatencyTest();
        if (isHacker) UpdateHackerControls();
    }

    private void RegisterMessagesWhenReady()
    {
        if (!networked || handlersRegistered || networkManager == null || !networkManager.IsListening || networkManager.CustomMessagingManager == null)
            return;

        networkManager.CustomMessagingManager.RegisterNamedMessageHandler(KartMessage, ReceiveKartState);
        networkManager.CustomMessagingManager.RegisterNamedMessageHandler(PoliceStateMessage, ReceivePoliceState);
        networkManager.CustomMessagingManager.RegisterNamedMessageHandler(DroneStateMessage, ReceiveDroneState);
        networkManager.CustomMessagingManager.RegisterNamedMessageHandler(DoorRequestMessage, ReceiveDoorRequest);
        networkManager.CustomMessagingManager.RegisterNamedMessageHandler(DoorStateMessage, ReceiveDoorState);
        networkManager.CustomMessagingManager.RegisterNamedMessageHandler(FinishMessage, ReceiveFinish);
        networkManager.CustomMessagingManager.RegisterNamedMessageHandler(LossMessage, ReceiveLoss);
        networkManager.CustomMessagingManager.RegisterNamedMessageHandler(StateRequestMessage, ReceiveStateRequest);
        networkManager.CustomMessagingManager.RegisterNamedMessageHandler(LatencyProbeMessage, ReceiveLatencyProbe);
        networkManager.CustomMessagingManager.RegisterNamedMessageHandler(LatencyReplyMessage, ReceiveLatencyReply);
        networkManager.CustomMessagingManager.RegisterNamedMessageHandler(CommunicationMessage, ReceiveCommunication);
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

    private void BindVoiceInput()
    {
        VoicePowerUpInput found = FindFirstObjectByType<VoicePowerUpInput>();
        if (found == voiceInput) return;
        if (voiceInput != null) voiceInput.CommunicationRequested -= HandleCommunicationRequested;
        voiceInput = found;
        if (voiceInput != null) voiceInput.CommunicationRequested += HandleCommunicationRequested;
    }

    private void HandleCommunicationRequested(string phrase)
    {
        int commandId = phrase switch
        {
            "Estoy aquí" => 0,
            "Tengo problemas" => 1,
            "Ven aquí" => 2,
            "Ayúdame" => 3,
            "Cuidado" => 4,
            "Listo" => 5,
            "Estoy hackeando" => 6,
            "Estoy avanzando" => 7,
            _ => -1
        };
        if (commandId < 0) return;

        Transform speaker = isHacker && droneController != null ? droneController.transform :
            kart != null ? kart.transform : transform;
        Vector3 position = speaker.position;
        Vector3 forward = speaker.forward;

        if (PythonEscapeModeBridge.Instance != null && PythonEscapeModeBridge.Instance.NetworkTestActive)
        {
            PythonEscapeModeBridge.Instance.SendCommunication(commandId, position, forward);
            return;
        }

        if (!networked || networkManager == null || !networkManager.IsListening ||
            networkManager.CustomMessagingManager == null)
        {
            ApplyRemoteCommunication(commandId, position, forward, !isHost);
            return;
        }

        if (isHost)
        {
            ApplyRemoteCommunication(commandId, position, forward, false);
            BroadcastCommunication(commandId, position, forward, false);
        }
        else
        {
            using (var writer = new FastBufferWriter(32, Allocator.Temp))
            {
                writer.WriteValueSafe(commandId);
                writer.WriteValueSafe(position);
                writer.WriteValueSafe(forward);
                networkManager.CustomMessagingManager.SendNamedMessage(
                    CommunicationMessage, NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableSequenced);
            }
        }
    }

    private void ReceiveCommunication(ulong senderClientId, FastBufferReader reader)
    {
        if (networkManager == null || !networkManager.IsListening) return;
        if (isHost)
        {
            if (senderClientId == NetworkManager.ServerClientId) return;
            reader.ReadValueSafe(out int commandId);
            reader.ReadValueSafe(out Vector3 position);
            reader.ReadValueSafe(out Vector3 forward);
            ApplyRemoteCommunication(commandId, position, forward, true);
            BroadcastCommunication(commandId, position, forward, true);
        }
        else
        {
            if (senderClientId != NetworkManager.ServerClientId) return;
            reader.ReadValueSafe(out int commandId);
            reader.ReadValueSafe(out Vector3 position);
            reader.ReadValueSafe(out Vector3 forward);
            reader.ReadValueSafe(out bool fromHacker);
            ApplyRemoteCommunication(commandId, position, forward, fromHacker);
        }
    }

    private void BroadcastCommunication(int commandId, Vector3 position, Vector3 forward, bool fromHacker)
    {
        using (var writer = new FastBufferWriter(33, Allocator.Temp))
        {
            writer.WriteValueSafe(commandId);
            writer.WriteValueSafe(position);
            writer.WriteValueSafe(forward);
            writer.WriteValueSafe(fromHacker);
            networkManager.CustomMessagingManager.SendNamedMessageToAll(
                CommunicationMessage, writer, NetworkDelivery.ReliableSequenced);
        }
    }

    public void ApplyRemoteCommunication(int commandId, Vector3 position, Vector3 forward, bool fromHacker)
    {
        string[] phrases = { "Estoy aquí", "Tengo problemas", "Ven aquí", "Ayúdame", "Cuidado", "Listo", "Estoy hackeando", "Estoy avanzando" };
        if (commandId < 0 || commandId >= phrases.Length) return;
        string role = fromHacker ? "HACKER" : "CONDUCTOR";
        communicationMessages.Add($"{DateTime.Now:HH:mm} · {role}: {phrases[commandId]}");
        while (communicationMessages.Count > 6) communicationMessages.RemoveAt(0);

        if (commandId <= 3)
        {
            Vector3 markerPosition = position;
            if (commandId == 2 || commandId == 3)
            {
                forward.y = 0f;
                if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
                markerPosition += forward.normalized * 8f;
            }
            ShowCommunicationMarker(markerPosition, phrases[commandId], commandId);
        }
    }

    private void ShowCommunicationMarker(Vector3 position, string label, int commandId)
    {
        if (communicationMarker != null) Destroy(communicationMarker);
        if (Physics.Raycast(position + Vector3.up * 50f, Vector3.down, out RaycastHit groundHit, 200f))
            position.y = groundHit.point.y;
        communicationMarker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        communicationMarker.name = "EscapeCommunicationMarker";
        communicationMarker.transform.position = position + Vector3.up * 0.8f;
        communicationMarker.transform.localScale = new Vector3(0.45f, 0.8f, 0.45f);
        Collider markerCollider = communicationMarker.GetComponent<Collider>();
        if (markerCollider != null) Destroy(markerCollider);
        Renderer markerRenderer = communicationMarker.GetComponent<Renderer>();
        if (markerRenderer != null)
        {
            markerRenderer.material.color = commandId == 1 ? new Color(1f, 0.18f, 0.12f) :
                commandId == 3 ? new Color(1f, 0.65f, 0.08f) : new Color(0.15f, 0.85f, 1f);
        }
        GameObject textObject = new GameObject("EscapeCommunicationMarkerLabel");
        textObject.transform.SetParent(communicationMarker.transform, false);
        textObject.transform.localPosition = Vector3.up * 1.35f;
        communicationMarkerText = textObject.AddComponent<TextMesh>();
        communicationMarkerText.text = label.ToUpperInvariant();
        communicationMarkerText.anchor = TextAnchor.MiddleCenter;
        communicationMarkerText.alignment = TextAlignment.Center;
        communicationMarkerText.fontStyle = FontStyle.Bold;
        communicationMarkerText.fontSize = 48;
        communicationMarkerText.characterSize = 0.09f;
        communicationMarkerText.color = markerRenderer != null ? markerRenderer.material.color : Color.cyan;
        communicationMarkerExpiresAt = Time.unscaledTime + 20f;
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

    private void SendPoliceState(ulong? targetClientId = null)
    {
        if (networkManager == null || networkManager.CustomMessagingManager == null) return;
        EscapePoliceStationGenerator generator = EscapePoliceStationGenerator.Instance;
        using (var writer = new FastBufferWriter(256, Allocator.Temp))
        {
            for (int id = 0; id < 4; id++)
            {
                bool active = false;
                bool chasing = false;
                Vector3 position = Vector3.zero;
                Quaternion rotation = Quaternion.identity;
                if (generator != null)
                    generator.TryGetPoliceNetworkState(id, out active, out position, out rotation, out chasing);
                writer.WriteValueSafe(active);
                writer.WriteValueSafe(position);
                writer.WriteValueSafe(rotation);
                writer.WriteValueSafe(chasing);
            }

            if (targetClientId.HasValue)
                networkManager.CustomMessagingManager.SendNamedMessage(PoliceStateMessage, targetClientId.Value,
                    writer, NetworkDelivery.UnreliableSequenced);
            else
                networkManager.CustomMessagingManager.SendNamedMessageToAll(PoliceStateMessage, writer,
                    NetworkDelivery.UnreliableSequenced);
        }
    }

    private void ReceivePoliceState(ulong senderClientId, FastBufferReader reader)
    {
        if (isHost || senderClientId != NetworkManager.ServerClientId) return;
        EscapePoliceStationGenerator generator = EscapePoliceStationGenerator.Instance;
        for (int id = 0; id < 4; id++)
        {
            reader.ReadValueSafe(out bool active);
            reader.ReadValueSafe(out Vector3 position);
            reader.ReadValueSafe(out Quaternion rotation);
            reader.ReadValueSafe(out bool chasing);
            generator?.ApplyRemotePoliceNetworkState(id, active, position, rotation, chasing);
        }
    }

    private void SendDroneState()
    {
        if (droneController == null || networkManager == null || !networkManager.IsListening ||
            networkManager.CustomMessagingManager == null) return;
        using (var writer = new FastBufferWriter(32, Allocator.Temp))
        {
            writer.WriteValueSafe(droneController.transform.position);
            writer.WriteValueSafe(droneController.transform.rotation);
            networkManager.CustomMessagingManager.SendNamedMessage(
                DroneStateMessage, NetworkManager.ServerClientId, writer, NetworkDelivery.UnreliableSequenced);
        }
    }

    private void ReceiveDroneState(ulong senderClientId, FastBufferReader reader)
    {
        if (!isHost || senderClientId == NetworkManager.ServerClientId) return;
        reader.ReadValueSafe(out remoteDronePosition);
        reader.ReadValueSafe(out remoteDroneRotation);
        hasRemoteDroneState = true;
    }

    public void ApplyRemoteDroneState(Vector3 position, Quaternion rotation)
    {
        if (!isHost) return;
        remoteDronePosition = position;
        remoteDroneRotation = rotation;
        hasRemoteDroneState = true;
    }

    public void ClearRemoteDroneState()
    {
        hasRemoteDroneState = false;
        if (remoteDroneVisual != null) remoteDroneVisual.SetActive(false);
    }

    private void UpdateRemoteDroneVisual()
    {
        if (!isHost || !hasRemoteDroneState || kart == null) return;
        if (remoteDroneVisual == null) CreateRemoteDroneVisual();

        bool nearby = (kart.transform.position - remoteDronePosition).sqrMagnitude <= 100f * 100f;
        if (remoteDroneVisual.activeSelf != nearby) remoteDroneVisual.SetActive(nearby);
        if (!nearby) return;

        float blend = 1f - Mathf.Exp(-20f * Time.unscaledDeltaTime);
        remoteDroneVisual.transform.position = Vector3.Lerp(
            remoteDroneVisual.transform.position, remoteDronePosition, blend);
        remoteDroneVisual.transform.rotation = Quaternion.Slerp(
            remoteDroneVisual.transform.rotation, remoteDroneRotation, blend);
    }

    private void CreateRemoteDroneVisual()
    {
        remoteDroneVisual = new GameObject("HackerDrone_RemoteVisual");
        remoteDroneVisual.transform.SetPositionAndRotation(remoteDronePosition, remoteDroneRotation);
        Color bodyColor = new Color(0.08f, 0.78f, 0.88f);
        Color frameColor = new Color(0.09f, 0.13f, 0.18f);
        Color accentColor = new Color(0.98f, 0.59f, 0.1f);

        AddRemoteDronePart("DroneCore", PrimitiveType.Cube, Vector3.zero,
            Quaternion.identity, new Vector3(0.56f, 0.2f, 0.72f), bodyColor);
        AddRemoteDronePart("DroneFrontCamera", PrimitiveType.Sphere, new Vector3(0f, -0.02f, 0.39f),
            Quaternion.identity, new Vector3(0.2f, 0.16f, 0.16f), accentColor);

        Vector3[] rotorPositions =
        {
            new Vector3(-0.58f, 0.04f, 0.52f), new Vector3(0.58f, 0.04f, 0.52f),
            new Vector3(-0.58f, 0.04f, -0.52f), new Vector3(0.58f, 0.04f, -0.52f)
        };
        Vector3[] armPositions =
        {
            new Vector3(-0.34f, 0.01f, 0.34f), new Vector3(0.34f, 0.01f, 0.34f),
            new Vector3(-0.34f, 0.01f, -0.34f), new Vector3(0.34f, 0.01f, -0.34f)
        };
        for (int i = 0; i < rotorPositions.Length; i++)
        {
            AddRemoteDronePart($"DroneArm_{i + 1}", PrimitiveType.Cube, armPositions[i],
                Quaternion.identity, new Vector3(0.72f, 0.08f, 0.1f), frameColor);
            AddRemoteDronePart($"DroneRotor_{i + 1}", PrimitiveType.Cylinder, rotorPositions[i],
                Quaternion.identity, new Vector3(0.25f, 0.025f, 0.25f), accentColor);
        }
    }

    private void AddRemoteDronePart(string partName, PrimitiveType primitive, Vector3 localPosition,
        Quaternion localRotation, Vector3 scale, Color color)
    {
        GameObject part = GameObject.CreatePrimitive(primitive);
        part.name = partName;
        part.transform.SetParent(remoteDroneVisual.transform, false);
        part.transform.localPosition = localPosition;
        part.transform.localRotation = localRotation;
        part.transform.localScale = scale;
        Collider collider = part.GetComponent<Collider>();
        if (collider != null) Destroy(collider);
        Renderer renderer = part.GetComponent<Renderer>();
        if (renderer != null)
        {
            Material material = renderer.material;
            material.color = color;
            remoteDroneMaterials.Add(material);
        }
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
        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame &&
            !networked && PythonEscapeModeBridge.Instance == null)
        {
            EscapeModeMenuSelector menu = FindFirstObjectByType<EscapeModeMenuSelector>();
            if (menu != null) menu.ReturnToModeMenu();
            return;
        }
        if (pad == null && keyboard == null && mouse == null)
        {
            status = "Conecta un mando o usa WASD y ratón para controlar el dron";
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
            UpdateHackMinigame(pad, keyboard);
            return;
        }
        // Square (West on a DualShock layout) is reserved for door hacking.
        // The other face buttons remain available for future hacker actions.
        bool hackHeld = pad != null && pad.buttonWest.isPressed;
        bool hackPressedThisFrame = pad != null && pad.buttonWest.wasPressedThisFrame;
        if (keyboard != null)
        {
            hackHeld |= keyboard.hKey.isPressed;
            hackPressedThisFrame |= keyboard.hKey.wasPressedThisFrame;
        }
        if (mouse != null)
        {
            hackHeld |= mouse.leftButton.isPressed;
            hackPressedThisFrame |= mouse.leftButton.wasPressedThisFrame;
        }
        UpdateTargetButton(hackHeld, hackPressedThisFrame, 0);
        UpdateDroneMotion(pad);
    }

    private void UpdateTargetButton(bool isPressed, bool wasPressedThisFrame, int slot)
    {
        if (wasPressedThisFrame)
        {
            targetButtonHoldTimes[slot] = 0f;
            targetButtonHoldHandled[slot] = false;
        }

        if (isPressed)
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

    private void UpdateHackMinigame(Gamepad pad, Keyboard keyboard)
    {
        if (Time.unscaledTime >= hackDeadline)
        {
            FailHackMinigame("Se agotó el tiempo");
            return;
        }

        int direction = -1;
        int pressedCount = 0;
        if (pad != null)
        {
            ReadDpadPress(pad.dpad.up, 0, ref direction, ref pressedCount);
            ReadDpadPress(pad.dpad.down, 1, ref direction, ref pressedCount);
            ReadDpadPress(pad.dpad.left, 2, ref direction, ref pressedCount);
            ReadDpadPress(pad.dpad.right, 3, ref direction, ref pressedCount);
        }
        if (keyboard != null)
        {
            ReadKeyPress(keyboard.upArrowKey, 0, ref direction, ref pressedCount);
            ReadKeyPress(keyboard.downArrowKey, 1, ref direction, ref pressedCount);
            ReadKeyPress(keyboard.leftArrowKey, 2, ref direction, ref pressedCount);
            ReadKeyPress(keyboard.rightArrowKey, 3, ref direction, ref pressedCount);
        }
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

    private static void ReadKeyPress(KeyControl key, int direction, ref int selectedDirection, ref int pressedCount)
    {
        if (!key.wasPressedThisFrame) return;
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

    /// <summary>
    /// Bloquea el dron del hacker 'seconds' segundos con el mismo bloqueo y overlay
    /// "DRON BLOQUEADO" que un hackeo fallido. Estado local del hacker: no envía mensajes.
    /// </summary>
    public void ImmobilizeDrone(float seconds, string reason)
    {
        if (!isHacker || seconds <= 0f) return;
        hackMinigameActive = false;
        hackDoorId = -1;
        hackPatternProgress = 0;
        droneLockedUntil = Mathf.Max(droneLockedUntil, Time.unscaledTime + seconds);
        status = string.IsNullOrEmpty(reason) ? $"Dron bloqueado por {seconds:0} s" : reason;
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
        UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(droneCamera).renderPostProcessing = true;
        status = "Dron activo · WASD mueve, Q/E altura, botón derecho del ratón mira, rueda acerca y mantén H o clic izquierdo para hackear";
        EscapeModeHudFeed.Publish(status);
    }

    private void UpdateDroneMotion(Gamepad pad, bool allowMovement = true)
    {
        if (droneController == null || droneCamera == null) return;

        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;
        if (pad != null && pad.startButton.wasPressedThisFrame)
            CalibrateDroneGyroscope();

        if (gyroDeviceId < 0) FindGyroDevice();
        Vector2 tilt = pad != null ? ReadDroneTilt(pad) : Vector2.zero;
        Vector2 keyboardMove = Vector2.zero;
        if (keyboard != null)
        {
            if (keyboard.aKey.isPressed) keyboardMove.x -= 1f;
            if (keyboard.dKey.isPressed) keyboardMove.x += 1f;
            if (keyboard.sKey.isPressed) keyboardMove.y -= 1f;
            if (keyboard.wKey.isPressed) keyboardMove.y += 1f;
            if (keyboard.rKey.wasPressedThisFrame) CalibrateDroneGyroscope();
        }
        tilt = Vector2.ClampMagnitude(tilt + keyboardMove, 1f);
        droneHorizontalInput = Mathf.Lerp(droneHorizontalInput, tilt.x, Time.unscaledDeltaTime * 7f);
        droneForwardInput = Mathf.Lerp(droneForwardInput, tilt.y, Time.unscaledDeltaTime * 7f);
        droneAltitudeInput = pad != null ? pad.leftStick.y.ReadValue() : 0f;
        if (keyboard != null)
        {
            if (keyboard.eKey.isPressed) droneAltitudeInput += 1f;
            if (keyboard.qKey.isPressed) droneAltitudeInput -= 1f;
        }
        droneAltitudeInput = Mathf.Clamp(droneAltitudeInput, -1f, 1f);
        if (Mathf.Abs(droneAltitudeInput) < 0.15f) droneAltitudeInput = 0f;
        droneYawInput = pad != null ? pad.leftStick.x.ReadValue() : 0f;
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

        if (mouse != null && mouse.rightButton.isPressed && allowMovement)
        {
            Vector2 look = mouse.delta.ReadValue();
            droneController.transform.Rotate(Vector3.up, look.x * 0.12f, Space.World);
            droneLookPitch = Mathf.Clamp(droneLookPitch - look.y * 0.12f, -75f, 75f);
            if (!droneMouseLookActive)
            {
                droneMouseLookActive = true;
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }
        else if (droneMouseLookActive)
        {
            droneMouseLookActive = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        // Give the pilot helicopter-like flight feedback without tilting the
        // CharacterController or changing the movement axes.
        Quaternion cameraTilt = Quaternion.Euler(droneLookPitch + droneForwardInput * 14f, 0f,
            -droneHorizontalInput * 11f);
        droneCamera.transform.localRotation = Quaternion.Slerp(
            droneCamera.transform.localRotation, cameraTilt, Time.unscaledDeltaTime * 5f);

        if (horizontalMovement.sqrMagnitude > 0.025f) DroneHasMoved = true;
        if (Mathf.Abs(droneAltitudeInput) > 0.2f) DroneAltitudeUsed = true;

        float zoom = pad != null ? pad.rightTrigger.ReadValue() - pad.leftTrigger.ReadValue() : 0f;
        if (keyboard != null)
        {
            if (keyboard.equalsKey.isPressed || keyboard.numpadPlusKey.isPressed) zoom += 1f;
            if (keyboard.minusKey.isPressed || keyboard.numpadMinusKey.isPressed) zoom -= 1f;
        }
        if (mouse != null) zoom += mouse.scroll.ReadValue().y * 0.01f;
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
        if (!isHost || HasWon || HasLost) return;
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

    public void ReportDriverLost(string reason)
    {
        if (HasLost || HasWon) return;
        HasLost = true;
        status = "¡Derrota! " + reason + " Conductor y hacker pierden.";
        EscapeModeHudFeed.Publish(status);
        kart?.StopKart();

        if (PythonEscapeModeBridge.Instance != null)
        {
            PythonEscapeModeBridge.Instance.ReportFailed();
            return;
        }

        if (isHost && networkManager != null && networkManager.IsListening)
        {
            using (var writer = new FastBufferWriter(1, Allocator.Temp))
                networkManager.CustomMessagingManager.SendNamedMessageToAll(LossMessage, writer, NetworkDelivery.ReliableSequenced);
        }
    }

    private void ReceiveLoss(ulong senderClientId, FastBufferReader reader)
    {
        if (senderClientId != NetworkManager.ServerClientId || HasLost) return;
        HasLost = true;
        status = "¡Derrota! El conductor chocó con una patrulla. Ambos pierden.";
        EscapeModeHudFeed.Publish(status);
        kart?.StopKart();
    }

    public void ApplyExternalFailure(string reason = "El conductor chocó con una patrulla.")
    {
        if (HasLost || HasWon) return;
        HasLost = true;
        status = "¡Derrota! " + reason + " Ambos pierden.";
        EscapeModeHudFeed.Publish(status);
        kart?.StopKart();
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
        SendPoliceState(senderClientId);
        for (int i = 0; i < doorStates.Length; i++)
            SendDoorState(senderClientId, i, doorStates[i]);
        if (HasWon)
        {
            using (var writer = new FastBufferWriter(1, Allocator.Temp))
                networkManager.CustomMessagingManager.SendNamedMessage(FinishMessage, senderClientId, writer, NetworkDelivery.ReliableSequenced);
        }
        else if (HasLost)
        {
            using (var writer = new FastBufferWriter(1, Allocator.Temp))
                networkManager.CustomMessagingManager.SendNamedMessage(LossMessage, senderClientId, writer, NetworkDelivery.ReliableSequenced);
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
        if (networkManager == null) return;
        if (isHost && clientId != networkManager.LocalClientId)
        {
            ClearRemoteDroneState();
            return;
        }
        if (isHost || clientId != networkManager.LocalClientId) return;
        status = "Se perdió la conexión. Buscando reconexión...";
        EscapeModeHudFeed.Publish(status);
        EscapeModeSessionController.Instance?.RetryHackerConnection();
    }

    private void OnDestroy()
    {
        if (droneMouseLookActive)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            droneMouseLookActive = false;
        }
        if (droneCamera != null) droneCamera.enabled = false;
        if (voiceInput != null) voiceInput.CommunicationRequested -= HandleCommunicationRequested;
        if (isHacker && savedGameplayCamera != null) savedGameplayCamera.enabled = true;
        if (droneController != null) Destroy(droneController.gameObject);
        if (remoteDroneVisual != null) Destroy(remoteDroneVisual);
        if (hackIndicator != null) Destroy(hackIndicator);
        if (communicationMarker != null) Destroy(communicationMarker);
        if (hackIndicatorMaterial != null) Destroy(hackIndicatorMaterial);
        foreach (Material material in remoteDroneMaterials)
            if (material != null) Destroy(material);
        if (networkManager != null && handlersRegistered && networkManager.CustomMessagingManager != null)
        {
            networkManager.CustomMessagingManager.UnregisterNamedMessageHandler(KartMessage);
            networkManager.CustomMessagingManager.UnregisterNamedMessageHandler(PoliceStateMessage);
            networkManager.CustomMessagingManager.UnregisterNamedMessageHandler(DroneStateMessage);
            networkManager.CustomMessagingManager.UnregisterNamedMessageHandler(DoorRequestMessage);
            networkManager.CustomMessagingManager.UnregisterNamedMessageHandler(DoorStateMessage);
            networkManager.CustomMessagingManager.UnregisterNamedMessageHandler(FinishMessage);
            networkManager.CustomMessagingManager.UnregisterNamedMessageHandler(LossMessage);
            networkManager.CustomMessagingManager.UnregisterNamedMessageHandler(StateRequestMessage);
            networkManager.CustomMessagingManager.UnregisterNamedMessageHandler(CommunicationMessage);
            networkManager.OnClientDisconnectCallback -= OnClientDisconnected;
        }
        if (instance == this) instance = null;
    }
}
