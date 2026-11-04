using System;
using System.Collections.Generic;
using KartIHC.Kart;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

/// <summary>Adapts the Python LAN server snapshots to the local Escape scene.</summary>
public sealed class PythonEscapeModeBridge : MonoBehaviour
{
    [Serializable] private sealed class PongMessage { public int c; }

    private static PythonEscapeModeBridge instance;
    private readonly Dictionary<int, long> pendingPings = new Dictionary<int, long>();
    private PythonRelayClient client;
    private KartController kart;
    private Rigidbody kartBody;
    private bool driverRole;
    private float nextStateSend;
    private float nextPingSend;
    private int nextPingId;
    private int pingSent;
    private int pongReceived;
    private int pingLost;
    private double lastRttMs;
    private double averageRttMs;
    private string networkStatus = "Esperando respuesta del servidor";
    private Vector3 remoteKartPosition;
    private Quaternion remoteKartRotation;
    private Vector3 remoteKartVelocity;
    private bool hasRemoteKartState;

    public static PythonEscapeModeBridge Instance => instance;
    public bool IsDriver => driverRole;
    public bool NetworkTestActive => client != null && client.Connected;
    public string NetworkTestStatus => networkStatus;
    public int PingsSent => pingSent;
    public int PongsReceived => pongReceived;
    public int PingsLost => pingLost;
    public float PacketLossPercent => pongReceived + pingLost == 0 ? 0f : pingLost * 100f / (pongReceived + pingLost);

    public static PythonEscapeModeBridge StartForRole(PythonRelayClient pythonClient, bool hostRole)
    {
        if (instance != null) Destroy(instance.gameObject);
        var bridgeObject = new GameObject("PythonEscapeModeBridge");
        instance = bridgeObject.AddComponent<PythonEscapeModeBridge>();
        instance.Initialize(pythonClient, hostRole);
        return instance;
    }

    public static void Stop()
    {
        if (instance != null) Destroy(instance.gameObject);
    }

    private void Initialize(PythonRelayClient pythonClient, bool hostRole)
    {
        client = pythonClient;
        driverRole = hostRole;
        kart = FindFirstObjectByType<KartController>();
        kartBody = kart != null ? kart.GetComponent<Rigidbody>() : null;
        if (client != null) client.OnMessage += HandleMessage;
        networkStatus = "Conectado al servidor Python";
    }

    private void Update()
    {
        if (client == null) return;

        if (!client.Connected)
        {
            networkStatus = "Conexión al servidor perdida";
            return;
        }

        long now = Stopwatch.GetTimestamp();
        ExpirePings(now);
        if (Time.unscaledTime >= nextStateSend)
        {
            nextStateSend = Time.unscaledTime + 1f / 30f;
            SendLocalState();
        }
        if (Time.unscaledTime >= nextPingSend)
        {
            nextPingSend = Time.unscaledTime + 1f;
            int pingId = ++nextPingId;
            pendingPings[pingId] = now;
            pingSent++;
            client.Ping(pingId);
        }

        if (!driverRole && hasRemoteKartState && kart != null)
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
    }

    private void SendLocalState()
    {
        if (driverRole)
        {
            if (kart == null) return;
            client.SendState(kart.transform.position, kart.transform.rotation,
                kartBody != null ? kartBody.linearVelocity : Vector3.zero);
            return;
        }

        Transform drone = EscapeModeNetworkBridge.Instance != null
            ? EscapeModeNetworkBridge.Instance.ReconDroneTransform
            : null;
        if (drone != null) client.SendState(drone.position, drone.rotation, Vector3.zero);
    }

    private void HandleMessage(string type, string json)
    {
        if (type == "snap") ApplySnapshot(client.LastSnapshot);
        else if (type == "pong") HandlePong(json);
        else if (type == "phase")
        {
            if (json.Contains("finished")) EscapeModeNetworkBridge.Instance?.ApplyExternalFinish();
        }
        else if (type == "error")
        {
            networkStatus = "Servidor: " + json;
        }
    }

    private void ApplySnapshot(PythonRelayClient.Snapshot snapshot)
    {
        if (snapshot == null) return;
        if (!driverRole && snapshot.driver != null && snapshot.driver.p != null && snapshot.driver.r != null &&
            snapshot.driver.p.Length >= 3 && snapshot.driver.r.Length >= 4)
        {
            remoteKartPosition = new Vector3(snapshot.driver.p[0], snapshot.driver.p[1], snapshot.driver.p[2]);
            remoteKartRotation = new Quaternion(snapshot.driver.r[0], snapshot.driver.r[1], snapshot.driver.r[2], snapshot.driver.r[3]);
            if (snapshot.driver.v != null && snapshot.driver.v.Length >= 3)
                remoteKartVelocity = new Vector3(snapshot.driver.v[0], snapshot.driver.v[1], snapshot.driver.v[2]);
            hasRemoteKartState = true;
        }

        if (snapshot.doors != null)
        {
            for (int i = 0; i < snapshot.doors.Length; i++)
                EscapeModeNetworkBridge.Instance?.ApplyExternalDoorState(i, snapshot.doors[i]);
        }
        if (snapshot.phase == "finished") EscapeModeNetworkBridge.Instance?.ApplyExternalFinish();
    }

    private void HandlePong(string json)
    {
        PongMessage pong;
        try { pong = JsonUtility.FromJson<PongMessage>(json); }
        catch (Exception) { return; }
        if (pong == null || !pendingPings.TryGetValue(pong.c, out long sentAt)) return;
        pendingPings.Remove(pong.c);
        lastRttMs = (Stopwatch.GetTimestamp() - sentAt) * 1000d / Stopwatch.Frequency;
        pongReceived++;
        averageRttMs += (lastRttMs - averageRttMs) / pongReceived;
        networkStatus = $"Servidor local · RTT {lastRttMs:0} ms · promedio {averageRttMs:0} ms · sin respuesta {PacketLossPercent:0.0}%";
    }

    private void ExpirePings(long now)
    {
        long timeout = Stopwatch.Frequency * 3L;
        List<int> expired = null;
        foreach (KeyValuePair<int, long> ping in pendingPings)
        {
            if (now - ping.Value < timeout) continue;
            if (expired == null) expired = new List<int>();
            expired.Add(ping.Key);
        }
        if (expired == null) return;
        foreach (int pingId in expired)
        {
            pendingPings.Remove(pingId);
            pingLost++;
        }
        if (pongReceived == 0 && pingLost > 0)
            networkStatus = $"Servidor local · sin respuesta ({pingLost} sondas agotadas)";
    }

    public void ToggleDoor(int doorId)
    {
        if (!driverRole && client != null && client.Connected) client.ToggleDoor(doorId);
    }

    public void ReportFinished()
    {
        if (driverRole && client != null && client.Connected) client.Finish();
    }

    private void OnDestroy()
    {
        if (client != null) client.OnMessage -= HandleMessage;
        if (instance == this) instance = null;
    }
}
