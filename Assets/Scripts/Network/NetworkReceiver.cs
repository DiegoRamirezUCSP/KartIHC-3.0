using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using KartIHC.Kart;
using UnityEngine;

namespace KartIHC.Network
{
    /// <summary>
    /// Listens for UDP JSON commands (e.g. {"command":"accelerate","value":1.0}) from an
    /// external input source (custom controller, mobile app, etc.) and raises the
    /// corresponding gameplay events on Unity's main thread.
    /// </summary>
    public class NetworkReceiver : MonoBehaviour
    {
        [SerializeField]
        private int port = 9000;

        [SerializeField]
        private KartController kartController;

        public event Action<float> OnAccelerate;
        public event Action<float> OnBrake;
        public event Action<float> OnSteer;
        public event Action OnUseItem;
        public event Action OnStartRace;

        private UdpClient udpClient;
        private Thread listenThread;
        private volatile bool isRunning;

        // Actions queued by the network thread, drained and invoked on the main thread in Update().
        private readonly ConcurrentQueue<Action> mainThreadActions = new ConcurrentQueue<Action>();

        [Serializable]
        private struct KartCommand
        {
            public string command;
            public float value;
        }

        private void Start()
        {
            StartListening();
        }

        private void StartListening()
        {
            if (isRunning)
            {
                return;
            }

            try
            {
                udpClient = new UdpClient(port);
            }
            catch (Exception e)
            {
                Debug.LogError($"[NetworkReceiver] Failed to open UDP socket on port {port}: {e.Message}");
                return;
            }

            isRunning = true;
            listenThread = new Thread(ListenLoop)
            {
                IsBackground = true,
                Name = "NetworkReceiver-UDP"
            };
            listenThread.Start();

            Debug.Log($"[NetworkReceiver] Listening for UDP commands on port {port}.");
        }

        private void ListenLoop()
        {
            IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);

            while (isRunning)
            {
                byte[] data;
                try
                {
                    data = udpClient.Receive(ref remoteEndPoint);
                }
                catch (ObjectDisposedException)
                {
                    // Socket was closed as part of shutdown; exit quietly.
                    break;
                }
                catch (SocketException e)
                {
                    if (!isRunning)
                    {
                        break;
                    }

                    Debug.LogWarning($"[NetworkReceiver] Socket error: {e.Message}");
                    continue;
                }

                string json = Encoding.UTF8.GetString(data);
                ProcessMessage(json);
            }
        }

        private void ProcessMessage(string json)
        {
            KartCommand command;
            try
            {
                command = JsonUtility.FromJson<KartCommand>(json);
                if (string.IsNullOrEmpty(command.command))
                {
                    throw new FormatException("Missing 'command' field.");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[NetworkReceiver] Malformed JSON received ('{json}'): {e.Message}");
                return;
            }

            switch (command.command.ToLowerInvariant())
            {
                case "accelerate":
                    Enqueue(() =>
                    {
                        Debug.Log($"[NetworkReceiver] OnAccelerate received, value={command.value}");
                        kartController?.SetAccelerate(command.value);
                        OnAccelerate?.Invoke(command.value);
                    });
                    break;
                case "brake":
                    Enqueue(() =>
                    {
                        Debug.Log($"[NetworkReceiver] OnBrake received, value={command.value}");
                        kartController?.SetBrake(command.value);
                        OnBrake?.Invoke(command.value);
                    });
                    break;
                case "steer":
                    Enqueue(() =>
                    {
                        Debug.Log($"[NetworkReceiver] OnSteer received, value={command.value}");
                        kartController?.SetSteer(command.value);
                        OnSteer?.Invoke(command.value);
                    });
                    break;
                case "use_item":
                case "useitem":
                    Enqueue(() =>
                    {
                        Debug.Log("[NetworkReceiver] OnUseItem received");
                        OnUseItem?.Invoke();
                    });
                    break;
                case "start_race":
                case "startrace":
                    Enqueue(() =>
                    {
                        Debug.Log("[NetworkReceiver] OnStartRace received");
                        OnStartRace?.Invoke();
                    });
                    break;
                default:
                    Debug.LogWarning($"[NetworkReceiver] Unknown command received: '{command.command}'");
                    break;
            }
        }

        private void Enqueue(Action action)
        {
            mainThreadActions.Enqueue(action);
        }

        private void Update()
        {
            while (mainThreadActions.TryDequeue(out Action action))
            {
                action.Invoke();
            }
        }

        private void OnDestroy()
        {
            StopListening();
        }

        private void StopListening()
        {
            isRunning = false;

            udpClient?.Close();
            udpClient = null;

            if (listenThread != null && listenThread.IsAlive)
            {
                listenThread.Join(500);
            }
            listenThread = null;
        }
    }
}
