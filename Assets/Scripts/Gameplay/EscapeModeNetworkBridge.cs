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
    private float nextTargetRefresh;
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
        if (handlersRegistered || networkManager == null || !networkManager.IsListening || networkManager.CustomMessagingManager == null)
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

        Gamepad pad = Gamepad.current;
        if (pad == null) return;
        UpdateTargetButton(pad.buttonWest, 0);
        UpdateTargetButton(pad.buttonNorth, 1);
        UpdateTargetButton(pad.buttonEast, 2);
        UpdateTargetButton(pad.buttonSouth, 3);
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

    private void CreateReconDrone()
    {
        if (savedGameplayCamera == null) savedGameplayCamera = Camera.main;
        if (savedGameplayCamera != null) savedGameplayCamera.enabled = false;

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
        droneCamera = drone.AddComponent<Camera>();
        droneCamera.fieldOfView = 60f;
        droneCamera.nearClipPlane = 0.1f;
        droneCamera.enabled = true;
        status = "Dron de reconocimiento activo";
        EscapeModeHudFeed.Publish(status);
    }

    private void UpdateDroneMotion(Gamepad pad)
    {
        if (droneController == null || droneCamera == null) return;

        if (gyroDeviceId < 0) FindGyroDevice();
        Vector2 tilt = ReadDroneTilt();
        droneHorizontalInput = Mathf.Lerp(droneHorizontalInput, tilt.x, Time.unscaledDeltaTime * 7f);
        droneForwardInput = Mathf.Lerp(droneForwardInput, tilt.y, Time.unscaledDeltaTime * 7f);
        droneAltitudeInput = pad.leftStick.y.ReadValue();
        if (Mathf.Abs(droneAltitudeInput) < 0.15f)
            droneAltitudeInput = pad.rightStick.y.ReadValue();

        Vector3 horizontalMovement = new Vector3(droneHorizontalInput, 0f, droneForwardInput);
        Vector3 movement = horizontalMovement * 9f + Vector3.up * (droneAltitudeInput * 5f);
        droneController.Move(movement * Time.unscaledDeltaTime);

        if (tilt.sqrMagnitude > 0.025f) DroneHasMoved = true;
        if (Mathf.Abs(droneAltitudeInput) > 0.2f) DroneAltitudeUsed = true;

        if (horizontalMovement.sqrMagnitude > 0.04f)
        {
            Vector3 viewDirection = horizontalMovement.normalized + Vector3.down * 0.28f;
            Quaternion targetRotation = Quaternion.LookRotation(viewDirection, Vector3.up);
            droneController.transform.rotation = Quaternion.Slerp(droneController.transform.rotation, targetRotation, Time.unscaledDeltaTime * 2f);
        }

        float zoom = pad.rightTrigger.ReadValue() - pad.leftTrigger.ReadValue();
        if (Mathf.Abs(zoom) > 0.15f) ZoomUsed = true;
        droneCamera.fieldOfView = Mathf.Clamp(droneCamera.fieldOfView - zoom * 55f * Time.unscaledDeltaTime, 25f, 85f);
    }

    private Vector2 ReadDroneTilt()
    {
        if (gyroDeviceId < 0) return Vector2.zero;
        try
        {
            if (!JoyShockLibraryNative.JslStillConnected(gyroDeviceId))
            {
                gyroDeviceId = -1;
                gyroConnected = false;
                return Vector2.zero;
            }

            JoyShockLibraryNative.MotionState motion = JoyShockLibraryNative.JslGetMotionState(gyroDeviceId);
            if (!hasNeutralGravity)
            {
                neutralGravity = new Vector3(motion.gravX, motion.gravY, motion.gravZ);
                hasNeutralGravity = true;
            }

            float strafe = Mathf.Clamp((motion.gravX - neutralGravity.x) * 2.2f, -1f, 1f);
            float forward = Mathf.Clamp((motion.gravY - neutralGravity.y) * 2.2f, -1f, 1f);
            if (Mathf.Abs(strafe) < 0.08f) strafe = 0f;
            if (Mathf.Abs(forward) < 0.08f) forward = 0f;
            return new Vector2(strafe, forward);
        }
        catch (DllNotFoundException) { gyroConnected = false; status = "Falta JoyShockLibrary.dll para el giroscopio"; }
        catch (EntryPointNotFoundException) { gyroConnected = false; status = "JoyShockLibrary no es compatible"; }
        return Vector2.zero;
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
                if (JoyShockLibraryNative.JslStillConnected(handle) && JoyShockLibraryNative.JslGetControllerType(handle) == JoyShockLibraryNative.ControllerTypeDualShock4)
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
        catch (DllNotFoundException) { status = "Falta JoyShockLibrary.dll para el giroscopio"; }
    }

    private void RefreshHackerTargets()
    {
        foreach (int slot in Enumerable.Range(0, targetDoorIds.Length))
        {
            targetDoorIds[slot] = -1;
            hackerTargetLabels[slot] = "Sin objetivo cercano";
        }

        if (droneController == null) return;
        string[] buttons = { "□", "△", "○", "×" };
        EscapeDoor[] nearest = EscapeCourseBuilder.Doors
            .OrderBy(door => (door.transform.position - droneController.transform.position).sqrMagnitude)
            .Take(targetDoorIds.Length)
            .ToArray();
        for (int slot = 0; slot < nearest.Length; slot++)
        {
            EscapeDoor door = nearest[slot];
            targetDoorIds[slot] = door.Id;
            float distance = Vector3.Distance(door.transform.position, droneController.transform.position);
            string doorState = door.Id >= 0 && door.Id < doorStates.Length && doorStates[door.Id] ? "abierta" : "cerrada";
            hackerTargetLabels[slot] = $"{buttons[slot]} Puerta {door.Id + 1} · {distance:0} m · {doorState}";
        }
    }

    private void InteractWithTarget(int slot)
    {
        if (slot < 0 || slot >= targetDoorIds.Length || targetDoorIds[slot] < 0)
        {
            status = "No hay un objetivo asignado a ese botón";
            EscapeModeHudFeed.Publish(status);
            return;
        }

        int doorId = targetDoorIds[slot];
        DoorHackUsed = true;
        if (PythonEscapeModeBridge.Instance != null)
        {
            PythonEscapeModeBridge.Instance.ToggleDoor(doorId);
            return;
        }
        bool open = !doorStates[doorId];
        RequestDoorState(doorId, open);
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
