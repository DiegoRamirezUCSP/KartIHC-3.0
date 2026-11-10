using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Cliente del servidor Python. Admite conexión directa y descubrimiento en la LAN.
/// </summary>
public class PythonRelayClient : MonoBehaviour
{
    // ---- formatos que devuelve el servidor (JsonUtility) ---------------------
    [Serializable] public class Header { public string t; }
    [Serializable] public class ServerAnnouncement { public string t, name; public int port, v; }
    [Serializable] public class DriverState { public bool connected; public float[] p, r, v; public string item; public float turbo, shield, stun; }
    [Serializable] public class HackerState { public bool connected; public float[] p, r; public bool frozen; public float freeze; public bool can_hack; public float lock_; }
    [Serializable] public class HackState { public bool active; public int obstacle; public float left; }
    [Serializable] public class TrapState { public int id; public float[] p; }
    [Serializable] public class Snapshot
    {
        public int tick; public float time; public string phase;
        public DriverState driver; public HackerState hacker; public HackState hack;
        public bool[] hacked; public bool[] doors; public TrapState[] traps;
    }

    public enum Role { Driver, Hacker }

    /// <summary>Ultima foto del mundo. Se actualiza en el hilo principal (Update).</summary>
    public Snapshot LastSnapshot { get; private set; }
    public bool Connected => client != null && client.Connected;
    /// <summary>Cada mensaje del servidor: tipo ("hack_result", "drone_frozen"...) y JSON crudo.</summary>
    public event Action<string, string> OnMessage;

    private TcpClient client;
    private NetworkStream stream;
    private Thread reader;
    private readonly ConcurrentQueue<string> inbox = new ConcurrentQueue<string>();
    private readonly object writeLock = new object();
    private volatile bool running;
    private int connectionGeneration;

    public async Task ConnectAsync(string host, int port, Role role, int timeoutMs = 4000, CancellationToken cancellationToken = default)
    {
        Disconnect();
        while (inbox.TryDequeue(out _)) { }
        int generation = Interlocked.Increment(ref connectionGeneration);
        var newClient = new TcpClient { NoDelay = true };
        try
        {
            Task connectTask = newClient.ConnectAsync(host, port);
            Task timeoutTask = Task.Delay(timeoutMs, cancellationToken);
            Task completed = await Task.WhenAny(connectTask, timeoutTask);
            if (cancellationToken.IsCancellationRequested)
            {
                connectTask.ContinueWith(task => { if (task.IsFaulted) _ = task.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                newClient.Close();
                cancellationToken.ThrowIfCancellationRequested();
            }
            if (completed != connectTask)
            {
                connectTask.ContinueWith(task => { if (task.IsFaulted) _ = task.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                throw new TimeoutException($"Tiempo agotado conectando a {host}:{port}.");
            }
            await connectTask;

            client = newClient;
            stream = client.GetStream();
            running = true;
            NetworkStream readStream = stream;
            reader = new Thread(() => ReadLoop(readStream, generation)) { IsBackground = true, Name = "PythonRelayClient-Reader" };
            reader.Start();
            SendRaw($"{{\"t\":\"hello\",\"role\":\"{(role == Role.Driver ? "driver" : "hacker")}\",\"v\":1}}");
        }
        catch
        {
            newClient.Close();
            throw;
        }
    }

    /// <summary>Encuentra el servidor Python por broadcast LAN y conecta con el rol pedido.</summary>
    public async Task<bool> DiscoverAndConnectAsync(Role role, int timeoutMs = 15000, int discoveryPort = 7778,
        CancellationToken cancellationToken = default)
    {
        int elapsed = 0;
        byte[] request = Encoding.UTF8.GetBytes("KARTIHC_DISCOVER");
        while (elapsed < timeoutMs && !cancellationToken.IsCancellationRequested)
        {
            using (var udp = new UdpClient(0))
            {
                udp.EnableBroadcast = true;
                var broadcast = new IPEndPoint(IPAddress.Broadcast, discoveryPort);
                await udp.SendAsync(request, request.Length, broadcast);
                Task<UdpReceiveResult> receive = udp.ReceiveAsync();
                int waitMs = Math.Min(1200, timeoutMs - elapsed);
                Task completed = await Task.WhenAny(receive, Task.Delay(waitMs, cancellationToken));
                elapsed += waitMs;
                if (cancellationToken.IsCancellationRequested)
                {
                    receive.ContinueWith(task => { if (task.IsFaulted) _ = task.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                    return false;
                }
                if (completed != receive)
                {
                    receive.ContinueWith(task => { if (task.IsFaulted) _ = task.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                    continue;
                }

                UdpReceiveResult reply = await receive;
                ServerAnnouncement announcement = JsonUtility.FromJson<ServerAnnouncement>(Encoding.UTF8.GetString(reply.Buffer));
                if (announcement == null || announcement.t != "server" || announcement.v != 1 || announcement.port <= 0)
                    continue;

                await ConnectAsync(reply.RemoteEndPoint.Address.ToString(), announcement.port, role, cancellationToken: cancellationToken);
                return true;
            }
        }
        return false;
    }

    public void Connect(string host, int port, Role role)
    {
        Disconnect();
        while (inbox.TryDequeue(out _)) { }
        int generation = Interlocked.Increment(ref connectionGeneration);
        client = new TcpClient { NoDelay = true };
        client.Connect(host, port);
        stream = client.GetStream();
        running = true;
        NetworkStream readStream = stream;
        reader = new Thread(() => ReadLoop(readStream, generation)) { IsBackground = true };
        reader.Start();
        SendRaw($"{{\"t\":\"hello\",\"role\":\"{(role == Role.Driver ? "driver" : "hacker")}\",\"v\":1}}");
    }

    public void Disconnect()
    {
        Interlocked.Increment(ref connectionGeneration);
        running = false;
        try { client?.Close(); } catch { }
        try { stream?.Dispose(); } catch { }
        client = null;
        stream = null;
    }

    private void OnDestroy() => Disconnect();

    private void ReadLoop(NetworkStream readStream, int generation)
    {
        try
        {
            using (var sr = new StreamReader(readStream, new UTF8Encoding(false)))
            {
                string line;
                while (running && generation == Volatile.Read(ref connectionGeneration) && (line = sr.ReadLine()) != null)
                    inbox.Enqueue(line);
            }
        }
        catch (Exception) { /* conexion cerrada */ }
        if (generation == Volatile.Read(ref connectionGeneration)) inbox.Enqueue("{\"t\":\"_disconnected\"}");
    }

    private void Update()
    {
        while (inbox.TryDequeue(out string json))
        {
            Header header;
            try { header = JsonUtility.FromJson<Header>(json); }
            catch (Exception) { continue; }
            if (header == null || string.IsNullOrEmpty(header.t)) continue;
            string type = header.t;
            if (type == "snap")
            {
                try { LastSnapshot = JsonUtility.FromJson<Snapshot>(json.Replace("\"lock\"", "\"lock_\"")); }
                catch (Exception) { continue; }
            }
            OnMessage?.Invoke(type, json);
        }
    }

    // ---- envio ---------------------------------------------------------------
    private void SendRaw(string json)
    {
        if (!Connected) return;
        byte[] data = Encoding.UTF8.GetBytes(json + "\n");
        try { lock (writeLock) stream.Write(data, 0, data.Length); }
        catch (Exception) { Disconnect(); }
    }

    private static string F(float x) => x.ToString("R", CultureInfo.InvariantCulture);
    private static string V3(Vector3 v) => $"[{F(v.x)},{F(v.y)},{F(v.z)}]";
    private static string Q(Quaternion q) => $"[{F(q.x)},{F(q.y)},{F(q.z)},{F(q.w)}]";

    /// <summary>Llamar ~30 veces por segundo con la posicion de tu kart (conductor) o dron (hacker).</summary>
    public void SendState(Vector3 p, Quaternion r, Vector3 v) =>
        SendRaw($"{{\"t\":\"state\",\"p\":{V3(p)},\"r\":{Q(r)},\"v\":{V3(v)}}}");

    public void Ping(int counter) => SendRaw($"{{\"t\":\"ping\",\"c\":{counter}}}");
    public void ToggleDoor(int doorId) => SendRaw($"{{\"t\":\"door_toggle\",\"id\":{doorId}}}");
    public void ItemPickup() => SendRaw("{\"t\":\"item_pickup\"}");                       // conductor
    public void UseItem(string item, Vector3 p) => SendRaw($"{{\"t\":\"use_item\",\"item\":\"{item}\",\"p\":{V3(p)}}}");
    public void TrapHit(int trapId) => SendRaw($"{{\"t\":\"trap_hit\",\"id\":{trapId}}}");
    public void Finish() => SendRaw("{\"t\":\"finish\"}");
    public void Fail() => SendRaw("{\"t\":\"fail\"}");
    public void CableHit() => SendRaw("{\"t\":\"cable_hit\"}");                           // hacker
    public void HackStart(int obstacle) => SendRaw($"{{\"t\":\"hack_start\",\"obstacle\":{obstacle}}}");
    public void HackResult(bool ok) => SendRaw($"{{\"t\":\"hack_result\",\"ok\":{(ok ? "true" : "false")}}}");
    public void ResetGame() => SendRaw("{\"t\":\"reset\"}");
}
