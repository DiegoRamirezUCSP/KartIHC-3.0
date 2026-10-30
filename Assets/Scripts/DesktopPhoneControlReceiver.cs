using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Stopwatch = System.Diagnostics.Stopwatch;
using UnityEngine;

public class DesktopPhoneControlReceiver : MonoBehaviour
{
    [SerializeField] private int port = 47777;
    [SerializeField] private float timeoutSeconds = 1f;

    private readonly ConcurrentQueue<string> powerUps = new ConcurrentQueue<string>();
    private UdpClient udpClient;
    private TcpListener tcpListener;
    private Thread udpReceiveThread;
    private Thread tcpReceiveThread;
    private volatile bool isRunning;
    private float steering;
    private double lastPacketTime;
    private string lastMessage = "sin datos";
    private string localIps = "calculando...";
    private string receiverStatus = "sin iniciar";

    public event Action<string> PowerUpRequested;
    public float Steering => IsConnected ? steering : 0f;
    public string LastMessage => lastMessage;
    public bool IsConnected => ReadElapsedSeconds(lastPacketTime) <= timeoutSeconds;

    private void OnEnable()
    {
        lastPacketTime = -timeoutSeconds;
        localIps = ReadLocalIPv4Addresses();
        isRunning = true;

        try
        {
            udpClient = new UdpClient(port);
            udpReceiveThread = new Thread(ReceiveUdpLoop)
            {
                IsBackground = true
            };
            udpReceiveThread.Start();
        }
        catch (SocketException exception)
        {
            receiverStatus = $"UDP {port} no disponible: {exception.Message}";
            lastMessage = receiverStatus;
            Debug.LogError($"[DesktopPhoneControlReceiver] {receiverStatus}");
        }

        try
        {
            tcpListener = new TcpListener(IPAddress.Any, port);
            tcpListener.Start();
            tcpReceiveThread = new Thread(ReceiveTcpLoop)
            {
                IsBackground = true
            };
            tcpReceiveThread.Start();
        }
        catch (SocketException exception)
        {
            string tcpError = $"TCP {port} no disponible: {exception.Message}";
            receiverStatus = string.IsNullOrEmpty(receiverStatus) ? tcpError : $"{receiverStatus} | {tcpError}";
            lastMessage = receiverStatus;
            Debug.LogError($"[DesktopPhoneControlReceiver] {tcpError}");
        }

        if (udpClient != null || tcpListener != null)
        {
            receiverStatus = $"escuchando UDP/TCP {port}";
            Debug.Log($"Esperando control por celular en UDP/TCP puerto {port}");
        }
    }

    private void Update()
    {
        while (powerUps.TryDequeue(out string powerUpId))
        {
            PowerUpRequested?.Invoke(powerUpId);
        }
    }

    private void ReceiveUdpLoop()
    {
        IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, port);

        while (isRunning)
        {
            try
            {
                byte[] bytes = udpClient.Receive(ref remoteEndPoint);
                string message = Encoding.UTF8.GetString(bytes);
                lastMessage = $"UDP {remoteEndPoint.Address}: {message}";
                lastPacketTime = ReadNowSeconds();
                HandleMessage(message, remoteEndPoint);
                SendAck(remoteEndPoint);
            }
            catch (SocketException)
            {
                if (isRunning)
                {
                    lastMessage = "error de socket";
                }
            }
            catch (ObjectDisposedException)
            {
                return;
            }
        }
    }

    private void ReceiveTcpLoop()
    {
        while (isRunning)
        {
            try
            {
                using (TcpClient client = tcpListener.AcceptTcpClient())
                using (NetworkStream stream = client.GetStream())
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true })
                {
                    string remote = client.Client.RemoteEndPoint != null
                        ? client.Client.RemoteEndPoint.ToString()
                        : "cliente TCP";

                    while (isRunning && client.Connected)
                    {
                        string message = reader.ReadLine();

                        if (message == null)
                        {
                            break;
                        }

                        lastMessage = $"TCP {remote}: {message}";
                        lastPacketTime = ReadNowSeconds();
                        HandleMessage(message, null);
                        writer.WriteLine("ACK");
                    }
                }
            }
            catch (SocketException)
            {
                if (isRunning)
                {
                    lastMessage = "error de socket TCP";
                }
            }
            catch (IOException)
            {
                if (isRunning)
                {
                    lastMessage = "conexion TCP cerrada";
                }
            }
            catch (ObjectDisposedException)
            {
                return;
            }
        }
    }

    private void HandleMessage(string message, IPEndPoint sender)
    {
        if (message.StartsWith("STEER:", StringComparison.OrdinalIgnoreCase))
        {
            string valueText = message.Substring("STEER:".Length);

            if (float.TryParse(valueText, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
            {
                steering = Math.Max(-1f, Math.Min(1f, value));
            }
        }
        else if (message.StartsWith("POWER:", StringComparison.OrdinalIgnoreCase))
        {
            powerUps.Enqueue(message.Substring("POWER:".Length));
        }
        else if (string.Equals(message, "DISCOVER", StringComparison.OrdinalIgnoreCase))
        {
            if (sender == null || udpClient == null)
            {
                return;
            }

            // El celular esta preguntando "quien es la PC" a toda la red:
            // le contestamos directo a quien nos escribio (su IP ya viene
            // en 'sender'), asi el celular arma la IP solo, sin que el
            // usuario tenga que escribirla a mano.
            byte[] reply = Encoding.UTF8.GetBytes("PONG");
            udpClient.Send(reply, reply.Length, sender);
        }
    }

    private void SendAck(IPEndPoint receiver)
    {
        byte[] reply = Encoding.UTF8.GetBytes("ACK");
        udpClient.Send(reply, reply.Length, receiver);
    }

    private void OnGUI()
    {
        GUILayout.BeginArea(new Rect(20, 340, 760, 205), GUI.skin.box);
        GUILayout.Label("Receptor del celular");
        GUILayout.Label($"Estado: {receiverStatus}");
        GUILayout.Label($"Puerto UDP/TCP: {port}");
        GUILayout.Label($"IPs de esta PC: {localIps}");
        GUILayout.Label($"Conectado: {IsConnected}");
        GUILayout.Label($"Volante remoto: {Steering:0.00}");
        GUILayout.Label($"Ultimo mensaje: {lastMessage}");
        GUILayout.EndArea();
    }

    private void OnDisable()
    {
        isRunning = false;
        udpClient?.Close();
        tcpListener?.Stop();
        udpReceiveThread = null;
        tcpReceiveThread = null;
    }

    private static double ReadNowSeconds()
    {
        return (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
    }

    private static double ReadElapsedSeconds(double startTime)
    {
        return ReadNowSeconds() - startTime;
    }

    private static string ReadLocalIPv4Addresses()
    {
        try
        {
            string[] addresses = Dns.GetHostEntry(Dns.GetHostName())
                .AddressList
                .Where(address => address.AddressFamily == AddressFamily.InterNetwork)
                .Select(address => address.ToString())
                .ToArray();

            return addresses.Length > 0 ? string.Join(", ", addresses) : "sin IPv4";
        }
        catch (SocketException exception)
        {
            return $"no se pudo leer IP: {exception.Message}";
        }
    }
}
