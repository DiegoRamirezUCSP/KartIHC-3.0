using System;
using System.Collections.Generic;
using KartIHC.Kart;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Relays authoritative kart, gate and finish state through the host's NGO session.
/// The hacker's security-camera selection stays local and only streams no video.
/// </summary>
public sealed class EscapeModeNetworkBridge : MonoBehaviour
{
    private const string KartMessage = "KartIHC.Escape.Kart";
    private const string DoorRequestMessage = "KartIHC.Escape.DoorRequest";
    private const string DoorStateMessage = "KartIHC.Escape.DoorState";
    private const string FinishMessage = "KartIHC.Escape.Finish";
    private const string StateRequestMessage = "KartIHC.Escape.StateRequest";
    private const float KartSendInterval = 1f / 20f;

    private static EscapeModeNetworkBridge instance;
    private readonly bool[] doorStates = new bool[3];
    private NetworkManager networkManager;
    private KartController kart;
    private Rigidbody kartBody;
    private bool isHost;
    private bool isHacker;
    private bool networked;
    private bool handlersRegistered;
    private float nextKartSend;
    private float circleHeldTime;
    private float squareHeldTime;
    private bool doorWasHeld;
    private bool squareHoldHandled;
    private int heldDoorId = -1;
    private int cameraIndex;
    private int gyroDeviceId = -1;
    private float cameraYaw;
    private float cameraPitch;
    private Vector3 remoteKartPosition;
    private Quaternion remoteKartRotation;
    private Vector3 remoteKartVelocity;
    private bool hasRemoteKartState;
    private Camera activeSecurityCamera;
    private Camera savedGameplayCamera;
    private Quaternion cameraBaseRotation;
    private string status = "";

    public static EscapeModeNetworkBridge Instance => instance;
    public bool HasWon { get; private set; }
    public int SelectedCameraIndex => cameraIndex;
    public bool AnyDoorOpen { get { foreach (bool open in doorStates) if (open) return true; return false; } }
    public bool ZoomUsed { get; private set; }
    public string CurrentStatus => status;

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
            cameraIndex = 0;
            SelectSecurityCamera(cameraIndex);
            FindGyroDevice();
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
        networkManager.OnClientDisconnectCallback += OnClientDisconnected;
        handlersRegistered = true;

        if (!isHost)
        {
            using (var writer = new FastBufferWriter(1, Allocator.Temp))
                networkManager.CustomMessagingManager.SendNamedMessage(StateRequestMessage, NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableSequenced);
        }
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
        Gamepad pad = Gamepad.current;
        if (pad == null) return;

        if (pad.buttonWest.wasPressedThisFrame)
        {
            squareHeldTime = 0f;
            squareHoldHandled = false;
        }

        if (pad.buttonWest.isPressed)
        {
            squareHeldTime += Time.unscaledDeltaTime;
            if (!squareHoldHandled && squareHeldTime >= 0.35f && EscapeCourseBuilder.Cameras.Count > 0)
            {
                squareHoldHandled = true;
                SelectSecurityCamera((cameraIndex + 1) % EscapeCourseBuilder.Cameras.Count);
            }
        }

        if (pad.buttonEast.wasPressedThisFrame) circleHeldTime = 0f;
        if (pad.buttonEast.isPressed) circleHeldTime += Time.unscaledDeltaTime;

        // Circle opens the gate paired with the active camera while held, then closes it on release.
        bool circlePressed = pad.buttonEast.isPressed && circleHeldTime >= 0.35f;
        if (circlePressed != doorWasHeld)
        {
            doorWasHeld = circlePressed;
            heldDoorId = circlePressed ? cameraIndex : heldDoorId;
            if (heldDoorId >= 0) RequestDoorState(heldDoorId, circlePressed);
            if (!circlePressed) heldDoorId = -1;
        }

        if (!pad.buttonEast.isPressed && doorWasHeld)
        {
            doorWasHeld = false;
            if (heldDoorId >= 0) RequestDoorState(heldDoorId, false);
            heldDoorId = -1;
        }

        UpdateCameraLookAndZoom(pad);
    }

    private void UpdateCameraLookAndZoom(Gamepad pad)
    {
        if (activeSecurityCamera == null) return;
        if (gyroDeviceId < 0) FindGyroDevice();

        try
        {
            if (gyroDeviceId >= 0 && JoyShockLibraryNative.JslStillConnected(gyroDeviceId))
            {
                JoyShockLibraryNative.ImuState imu = JoyShockLibraryNative.JslGetIMUState(gyroDeviceId);
                cameraYaw += imu.gyroY * Time.unscaledDeltaTime * 1.8f;
                cameraPitch -= imu.gyroX * Time.unscaledDeltaTime * 1.8f;
                cameraPitch = Mathf.Clamp(cameraPitch, -70f, 70f);
                activeSecurityCamera.transform.rotation = cameraBaseRotation * Quaternion.Euler(cameraPitch, cameraYaw, 0f);
            }
        }
        catch (DllNotFoundException) { status = "Falta JoyShockLibrary.dll para el giroscopio"; }
        catch (EntryPointNotFoundException) { status = "JoyShockLibrary no es compatible"; }

        float zoom = pad.rightTrigger.ReadValue() - pad.leftTrigger.ReadValue();
        if (Mathf.Abs(zoom) > 0.15f) ZoomUsed = true;
        activeSecurityCamera.fieldOfView = Mathf.Clamp(activeSecurityCamera.fieldOfView - zoom * 55f * Time.unscaledDeltaTime, 25f, 85f);
    }

    private void FindGyroDevice()
    {
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
                    return;
                }
            }
        }
        catch (DllNotFoundException) { status = "Falta JoyShockLibrary.dll para el giroscopio"; }
    }

    private void SelectSecurityCamera(int index)
    {
        var cameras = EscapeCourseBuilder.Cameras;
        if (cameras.Count == 0) return;
        cameraIndex = Mathf.Clamp(index, 0, cameras.Count - 1);
        if (activeSecurityCamera != null) activeSecurityCamera.enabled = false;

        if (savedGameplayCamera == null) savedGameplayCamera = Camera.main;
        if (isHacker && savedGameplayCamera != null) savedGameplayCamera.enabled = false;
        activeSecurityCamera = cameras[cameraIndex];
        activeSecurityCamera.enabled = isHacker;
        activeSecurityCamera.fieldOfView = 58f;
        cameraBaseRotation = activeSecurityCamera.transform.rotation;
        cameraYaw = 0f;
        cameraPitch = 0f;
        if (isHacker)
        {
            status = $"Cámara {cameraIndex + 1}/{cameras.Count} | □ cambiar · ○ puerta · gatillos zoom";
            EscapeModeHudFeed.Publish($"Vista cambiada a cámara {cameraIndex + 1}");
        }
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
        if (activeSecurityCamera != null) activeSecurityCamera.enabled = false;
        if (isHacker && savedGameplayCamera != null) savedGameplayCamera.enabled = true;
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
