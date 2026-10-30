using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Stopwatch = System.Diagnostics.Stopwatch;
using UnityEngine;

/// <summary>
/// Corre en el celular. Lee inclinacion con DeviceTiltReader (la misma clase
/// que usa GyroSteeringInput) para que el sensor funcione con el nuevo Input
/// System, y envia el valor por UDP a la PC.
/// </summary>
public class PhoneControllerSender : MonoBehaviour
{
    private enum TransportMode
    {
        TcpUsbAdb,
        UdpWifi
    }

    [SerializeField] private TransportMode transportMode = TransportMode.TcpUsbAdb;
    [SerializeField] private string computerIp = "192.168.1.100";
    [SerializeField] private string usbAdbHost = "127.0.0.1";
    [SerializeField] private int port = 47777;
    [SerializeField] private float maxTiltDegrees = 35f;
    [SerializeField] private float sensitivity = 1.2f;
    [SerializeField] private bool invert;
    [SerializeField] private bool lockLandscape = true;

    private UdpClient udpClient;
    private TcpClient tcpClient;
    private NetworkStream tcpStream;
    private StreamWriter tcpWriter;
    private float centerTilt;
    private float steering;
    private string status = "sin conectar";
    private string replyStatus = "sin respuesta de PC";
    private double lastReplyTime = -999d;
    private double nextTcpReconnectTime;

    private void Awake()
    {
        if (lockLandscape)
        {
            Screen.orientation = ScreenOrientation.LandscapeLeft;
        }

        udpClient = new UdpClient();
        DeviceTiltReader.EnableSensors();
        Calibrate();
    }

    private void Start()
    {
        // Igual que en GyroSteeringInput: los sensores tardan un frame en
        // entregar el primer valor real, asi que recalibramos aqui tambien.
        Calibrate();
    }

    private void Update()
    {
        steering = ReadSteering();
        Send($"STEER:{steering.ToString("0.000", CultureInfo.InvariantCulture)}");
        ReadReplies();
    }

    private void Calibrate()
    {
        centerTilt = DeviceTiltReader.ReadTiltDegrees(maxTiltDegrees);
    }

    private float ReadSteering()
    {
        float tilt = DeviceTiltReader.ReadTiltDegrees(maxTiltDegrees) - centerTilt;
        float normalized = Mathf.Clamp(tilt / maxTiltDegrees, -1f, 1f);

        if (invert)
        {
            normalized *= -1f;
        }

        return Mathf.Clamp(normalized * sensitivity, -1f, 1f);
    }

    private void Send(string message)
    {
        if (transportMode == TransportMode.TcpUsbAdb)
        {
            SendTcp(message);
            return;
        }

        SendUdp(message);
    }

    private void SendUdp(string message)
    {
        try
        {
            byte[] bytes = Encoding.UTF8.GetBytes(message);
            udpClient.Send(bytes, bytes.Length, computerIp, port);
            status = $"enviando a {computerIp}:{port} ({DeviceTiltReader.ActiveSourceName})";
        }
        catch (SocketException exception)
        {
            status = exception.Message;
        }
    }

    private void SendTcp(string message)
    {
        if (!EnsureTcpConnection())
        {
            return;
        }

        try
        {
            tcpWriter.WriteLine(message);
            status = $"USB ADB enviando a {usbAdbHost}:{port} ({DeviceTiltReader.ActiveSourceName})";
        }
        catch (IOException exception)
        {
            status = $"TCP cerrado: {exception.Message}";
            CloseTcpConnection();
        }
        catch (SocketException exception)
        {
            status = exception.Message;
            CloseTcpConnection();
        }
    }

    private bool EnsureTcpConnection()
    {
        if (tcpClient != null && tcpClient.Connected && tcpStream != null)
        {
            return true;
        }

        if (ReadNowSeconds() < nextTcpReconnectTime)
        {
            return false;
        }

        nextTcpReconnectTime = ReadNowSeconds() + 1d;
        CloseTcpConnection();

        try
        {
            tcpClient = new TcpClient();
            tcpClient.NoDelay = true;
            tcpClient.Connect(usbAdbHost, port);
            tcpStream = tcpClient.GetStream();
            tcpWriter = new StreamWriter(tcpStream, new UTF8Encoding(false)) { AutoFlush = true };
            status = $"USB ADB conectado a {usbAdbHost}:{port}";
            replyStatus = "esperando ACK por USB";
            return true;
        }
        catch (SocketException exception)
        {
            status = $"USB ADB sin conexion: {exception.Message}";
            CloseTcpConnection();
            return false;
        }
        catch (IOException exception)
        {
            status = $"USB ADB sin conexion: {exception.Message}";
            CloseTcpConnection();
            return false;
        }
    }

    private void ReadReplies()
    {
        if (transportMode == TransportMode.TcpUsbAdb)
        {
            ReadTcpReplies();
            return;
        }

        ReadUdpReplies();
    }

    private void ReadUdpReplies()
    {
        try
        {
            while (udpClient.Available > 0)
            {
                IPEndPoint sender = new IPEndPoint(IPAddress.Any, 0);
                byte[] bytes = udpClient.Receive(ref sender);
                string message = Encoding.UTF8.GetString(bytes);
                lastReplyTime = ReadNowSeconds();
                replyStatus = $"PC respondio {message} desde {sender.Address}";
            }

            if (ReadNowSeconds() - lastReplyTime > 2.5d)
            {
                replyStatus = "sin respuesta de PC";
            }
        }
        catch (SocketException exception)
        {
            replyStatus = exception.Message;
        }
    }

    private void ReadTcpReplies()
    {
        if (tcpStream == null)
        {
            if (ReadNowSeconds() - lastReplyTime > 2.5d)
            {
                replyStatus = "sin respuesta de PC por USB";
            }

            return;
        }

        try
        {
            byte[] buffer = new byte[128];

            while (tcpStream.DataAvailable)
            {
                int count = tcpStream.Read(buffer, 0, buffer.Length);

                if (count <= 0)
                {
                    CloseTcpConnection();
                    break;
                }

                string message = Encoding.UTF8.GetString(buffer, 0, count).Trim();
                lastReplyTime = ReadNowSeconds();
                replyStatus = $"PC respondio {message} por USB";
            }

            if (ReadNowSeconds() - lastReplyTime > 2.5d)
            {
                replyStatus = "sin respuesta de PC por USB";
            }
        }
        catch (IOException exception)
        {
            replyStatus = $"USB cerrado: {exception.Message}";
            CloseTcpConnection();
        }
        catch (SocketException exception)
        {
            replyStatus = exception.Message;
            CloseTcpConnection();
        }
    }

    private volatile bool isDiscovering;
    private string discoveryStatus = "";

    /// <summary>
    /// Manda un mensaje "DISCOVER" a toda la red local (broadcast) y espera
    /// a que la PC (que ya esta escuchando con DesktopPhoneControlReceiver)
    /// conteste "PONG". Cuando llega la respuesta, tomamos la IP de quien
    /// contesto y la ponemos automaticamente en computerIp.
    /// Requiere que el celular y la PC esten en la MISMA red (WiFi normal,
    /// hotspot o USB tethering) - el broadcast no cruza redes distintas.
    /// </summary>
    private void StartDiscovery()
    {
        if (isDiscovering)
        {
            return;
        }

        isDiscovering = true;
        discoveryStatus = "buscando PC en la red...";
        Thread discoveryThread = new Thread(DiscoveryWorker) { IsBackground = true };
        discoveryThread.Start();
    }

    private void DiscoveryWorker()
    {
        try
        {
            using (UdpClient discoveryClient = new UdpClient())
            {
                discoveryClient.EnableBroadcast = true;
                discoveryClient.Client.ReceiveTimeout = 2500;

                byte[] request = Encoding.UTF8.GetBytes("DISCOVER");
                discoveryClient.Send(request, request.Length, new IPEndPoint(IPAddress.Broadcast, port));

                IPEndPoint responder = new IPEndPoint(IPAddress.Any, 0);
                byte[] response = discoveryClient.Receive(ref responder);
                string text = Encoding.UTF8.GetString(response);

                if (string.Equals(text, "PONG", StringComparison.OrdinalIgnoreCase))
                {
                    computerIp = responder.Address.ToString();
                    discoveryStatus = $"PC encontrada: {computerIp}";
                }
                else
                {
                    discoveryStatus = "respuesta inesperada de la red";
                }
            }
        }
        catch (SocketException)
        {
            discoveryStatus = "no se encontro ninguna PC (revisa que esten en la misma red)";
        }
        finally
        {
            isDiscovering = false;
        }
    }

    private GUIStyle labelStyle;
    private GUIStyle fieldStyle;
    private GUIStyle buttonStyle;
    private bool stylesReady;

    private void BuildStyles()
    {
        // Escala el tamano de letra segun el DPI/resolucion real del telefono,
        // en vez de un numero fijo en pixeles que se ve chiquito en pantallas
        // grandes de alta densidad.
        float scale = Mathf.Max(1f, Screen.dpi / 160f);

        labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.RoundToInt(28 * scale),
            wordWrap = true
        };

        fieldStyle = new GUIStyle(GUI.skin.textField)
        {
            fontSize = Mathf.RoundToInt(28 * scale)
        };

        buttonStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = Mathf.RoundToInt(32 * scale)
        };

        stylesReady = true;
    }

    private void OnGUI()
    {
        if (!stylesReady)
        {
            BuildStyles();
        }

        float margin = Screen.width * 0.03f;
        float areaWidth = Screen.width - margin * 2f;
        float buttonHeight = Screen.height * 0.11f;

        GUILayout.BeginArea(new Rect(margin, margin, areaWidth, Screen.height - margin * 2f), GUI.skin.box);

        GUILayout.Label("Control remoto por celular", labelStyle);
        GUILayout.Label($"Modo: {ReadModeLabel()}", labelStyle);
        GUILayout.Label($"Estado: {status}", labelStyle);
        GUILayout.Label($"Conexion: {replyStatus}", labelStyle);
        GUILayout.Label($"Volante: {steering:0.00}", labelStyle);

        GUILayout.Space(10);
        GUILayout.Label("IP de la PC:", labelStyle);
        computerIp = GUILayout.TextField(computerIp, fieldStyle, GUILayout.Height(buttonHeight * 0.8f));

        GUILayout.BeginHorizontal();

        if (GUILayout.Button("USB ADB", buttonStyle, GUILayout.Height(buttonHeight)))
        {
            transportMode = TransportMode.TcpUsbAdb;
            computerIp = usbAdbHost;
            CloseTcpConnection();
            replyStatus = "usa adb reverse tcp:47777 tcp:47777";
        }

        if (GUILayout.Button("WiFi/Hotspot", buttonStyle, GUILayout.Height(buttonHeight)))
        {
            transportMode = TransportMode.UdpWifi;
            CloseTcpConnection();
            replyStatus = "modo UDP por red local";
        }

        GUILayout.EndHorizontal();

        if (!string.IsNullOrEmpty(discoveryStatus))
        {
            GUILayout.Label(discoveryStatus, labelStyle);
        }

        GUI.enabled = !isDiscovering;
        if (GUILayout.Button(isDiscovering ? "Buscando..." : "Buscar PC automaticamente", buttonStyle, GUILayout.Height(buttonHeight)))
        {
            StartDiscovery();
        }
        GUI.enabled = true;

        GUILayout.Space(20);

        if (GUILayout.Button("Calibrar volante", buttonStyle, GUILayout.Height(buttonHeight)))
        {
            Calibrate();
        }

        GUILayout.Space(10);
        GUILayout.BeginHorizontal();

        if (GUILayout.Button("Escudo", buttonStyle, GUILayout.Height(buttonHeight)))
        {
            Send("POWER:escudo");
        }

        if (GUILayout.Button("Turbo", buttonStyle, GUILayout.Height(buttonHeight)))
        {
            Send("POWER:turbo");
        }

        if (GUILayout.Button("Trampa", buttonStyle, GUILayout.Height(buttonHeight)))
        {
            Send("POWER:trampa");
        }

        GUILayout.EndHorizontal();
        GUILayout.EndArea();
    }

    private void OnDestroy()
    {
        udpClient?.Close();
        CloseTcpConnection();
    }

    private static double ReadNowSeconds()
    {
        return (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
    }

    private string ReadModeLabel()
    {
        return transportMode == TransportMode.TcpUsbAdb ? "USB ADB/TCP" : "WiFi/Hotspot UDP";
    }

    private void CloseTcpConnection()
    {
        tcpWriter?.Dispose();
        tcpStream?.Dispose();
        tcpClient?.Close();
        tcpWriter = null;
        tcpStream = null;
        tcpClient = null;
    }
}
