using System;
using System.Collections.Generic;
using KartIHC.UI;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.SceneManagement;

namespace KartIHC.Gameplay.Hazards
{
    /// <summary>
    /// Obstáculos hackeables del Modo Escape (además de las 3 puertas del bridge).
    /// Se crea solo al cargar EscapeCourse y no modifica archivos existentes.
    ///
    /// Hacker: con el dron a 7 m o menos de un obstáculo cerrado, mantener △ (mando)
    /// o G (teclado) inicia un minijuego de 6 flechas, igual que las puertas. Al
    /// acertar, el obstáculo se abre para ambos; al fallar, el dron se bloquea.
    ///
    /// Autoridad del estado según el transporte:
    /// - Local (prácticas sin red): se aplica directamente.
    /// - Relay/NGO: el host (conductor). Mensajes propios:
    ///     KartIHC.Hazard.ObstacleHacked  hacker → host   (byte id)
    ///     KartIHC.Hazard.ObstacleState   host → todos    (byte máscara de abiertos)
    ///     KartIHC.Hazard.StateRequest    cliente → host  (se repite cada 1 s hasta recibir estado)
    /// - Servidor Python: el servidor, con sus obstáculos genéricos ya existentes
    ///   (hack_start / hack_started / hack_denied / hack_result y snap.hacked[]).
    ///   Allí el tiempo del minijuego y el bloqueo los fija el servidor (8 s / 10 s por defecto).
    ///
    /// Escape Solo (patrullas): los obstáculos se ocultan, igual que las puertas.
    /// </summary>
    public sealed class EscapeHazardDirector : MonoBehaviour
    {
        public enum Transport { Local, Ngo, Python }

        private const string EscapeSceneName = "EscapeCourse";
        private const string ObstacleHackedMessage = "KartIHC.Hazard.ObstacleHacked";
        private const string ObstacleStateMessage = "KartIHC.Hazard.ObstacleState";
        private const string StateRequestMessage = "KartIHC.Hazard.StateRequest";

        // Mismas reglas que las puertas (constantes privadas de EscapeModeNetworkBridge).
        public const float HackRangeMeters = 7f;
        public const int HackPatternLength = 6;
        private const float HackHoldSeconds = 0.35f;
        private const float HackTimeLimitSeconds = 10f;
        private const float HackFailureLockSeconds = 15f;
        private const float ServerReplyTimeoutSeconds = 3f;
        private const float StateRequestIntervalSeconds = 1f;

        // Posición en el anillo (grados, 0 = +X, antihorario): entre la puerta 1 (≈25°) y la 2 (≈145°).
        private const float PoliceBarrierAngle = 85f;
        private const int PoliceBarrierId = 0;

        private static readonly Color ObstacleClosedColor = new Color(1f, 0.32f, 0.3f);
        private static readonly Color ObstacleOpenColor = KartUIStyle.Good;

        [Serializable]
        private sealed class ServerHazardMessage
        {
            public string t;
            public int obstacle = -1;
            public float time;
            public string reason;
            public float left;
            public bool ok;
            public float @lock;
        }

        public static EscapeHazardDirector Instance { get; private set; }

        private readonly List<EscapeHackableObstacle> obstacles = new List<EscapeHackableObstacle>();
        private readonly List<string> legendKeys = new List<string>();
        private readonly int[] pattern = new int[HackPatternLength];
        private Transform obstacleRoot;
        private EscapeModeNetworkBridge boundBridge;
        private EscapeDroneLock droneLock;
        private EscapeHazardHud hud;
        private GameStateManager gameStateManager;
        private bool hidden;
        private bool debugReveal;

        private EscapeHackableObstacle minigameObstacle;
        private float minigameDeadline;
        private int awaitingObstacle = -1;
        private float awaitingSince;
        private float holdTime;
        private bool holdHandled;

        private CustomMessagingManager registeredMessaging;
        private bool hasHostState;
        private float nextStateRequest;
        private PythonRelayClient pythonClient;

        public IReadOnlyList<EscapeHackableObstacle> Obstacles => obstacles;
        public EscapeDroneLock DroneLock => droneLock;
        public EscapeHackableObstacle Target { get; private set; }
        public float TargetDistance { get; private set; }
        public bool MinigameActive => minigameObstacle != null;
        public EscapeHackableObstacle MinigameObstacle => minigameObstacle;
        public int MinigameProgress { get; private set; }
        public int MinigameDirectionAt(int index) => index >= 0 && index < pattern.Length ? pattern[index] : -1;
        public float MinigameSecondsRemaining => MinigameActive ? Mathf.Max(0f, minigameDeadline - Time.unscaledTime) : 0f;
        public bool AwaitingServer => awaitingObstacle >= 0;
        public EscapeHackableObstacle AwaitingObstacle => FindObstacle(awaitingObstacle);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != EscapeSceneName || FindFirstObjectByType<EscapeHazardDirector>() != null) return;
            new GameObject("EscapeHazardDirector").AddComponent<EscapeHazardDirector>();
        }

        private void Awake()
        {
            Instance = this;
            droneLock = gameObject.AddComponent<EscapeDroneLock>();
            hud = gameObject.AddComponent<EscapeHazardHud>();
            EscapeMinimap.RegisterMarkerSource(CollectMinimapMarkers);
        }

        private void Update()
        {
            EscapeModeNetworkBridge bridge = EscapeModeNetworkBridge.Instance;
            UpdateNgoMessaging();
            UpdatePythonSubscription();
            if (bridge == null) return;
            if (bridge != boundBridge) BindSession(bridge);
            if (gameStateManager == null) gameStateManager = FindFirstObjectByType<GameStateManager>();

            UpdateVisibility();
            if (CurrentTransport(bridge) == Transport.Ngo) UpdateHostStateRequest();
            if (bridge.IsHacker) UpdateHacker(bridge);
#if UNITY_EDITOR
            UpdateEditorDebugKey(bridge);
#endif
        }

        private Transport CurrentTransport(EscapeModeNetworkBridge bridge)
        {
            if (PythonEscapeModeBridge.Instance != null) return Transport.Python;
            NetworkManager manager = NetworkManager.Singleton;
            if (bridge != null && !bridge.IsOfflinePractice && manager != null && manager.IsListening) return Transport.Ngo;
            return Transport.Local;
        }

        // ------------------------------------------------------------ sesión

        private void BindSession(EscapeModeNetworkBridge bridge)
        {
            boundBridge = bridge;
            if (obstacleRoot == null) BuildObstacles();
            EndMinigame();
            awaitingObstacle = -1;
            hasHostState = false;
            nextStateRequest = 0f;
            debugReveal = false;
            foreach (EscapeHackableObstacle obstacle in obstacles) obstacle.SetOpen(false, instant: true);
        }

        private void BuildObstacles()
        {
            obstacleRoot = new GameObject("EscapeHazards").transform;
            obstacleRoot.SetParent(transform, false);
            AddObstacle(EscapePoliceBarrier.Create(obstacleRoot, PoliceBarrierId, PoliceBarrierAngle));
        }

        private void AddObstacle(EscapeHackableObstacle obstacle)
        {
            obstacles.Add(obstacle);
            string key = "hazard." + obstacle.LegendLabel;
            if (legendKeys.Contains(key)) return;
            legendKeys.Add(key);
            string label = obstacle.LegendLabel;
            EscapeMinimap.RegisterLegendEntry(key, new EscapeMinimap.LegendEntry
            {
                Shape = EscapeMinimap.MarkerShape.Bar,
                Color = ObstacleClosedColor,
                Label = label,
                IsVisible = () => !hidden && obstacles.Exists(o => o != null && o.LegendLabel == label)
            });
        }

        private void UpdateVisibility()
        {
            // Solo Escape Solo oculta los obstáculos (no hay hacker que los abra). La policía existe
            // también en cooperativo desde que Diego la activa para ambos roles.
            EscapePoliceStationGenerator police = EscapePoliceStationGenerator.Instance;
            hidden = police != null && police.IsSoloMode && !debugReveal;
            if (obstacleRoot == null || obstacleRoot.gameObject.activeSelf != hidden) return;
            obstacleRoot.gameObject.SetActive(!hidden);
            if (hidden) EndMinigame();
        }

        private EscapeHackableObstacle FindObstacle(int id)
        {
            foreach (EscapeHackableObstacle obstacle in obstacles)
                if (obstacle != null && obstacle.Id == id) return obstacle;
            return null;
        }

        private void ApplyObstacleState(int id, bool open, bool announce)
        {
            EscapeHackableObstacle obstacle = FindObstacle(id);
            if (obstacle == null || obstacle.IsOpen == open) return;
            obstacle.SetOpen(open);
            if (MinigameActive && minigameObstacle == obstacle && open) EndMinigame();
            if (!announce) return;
            string message = $"{obstacle.DisplayName}: {obstacle.StateText(open)}";
            EscapeModeHudFeed.Publish(message);
            hud.Toast(message.ToUpperInvariant());
        }

        // ------------------------------------------------------------ hacker

        private void UpdateHacker(EscapeModeNetworkBridge bridge)
        {
            Transform drone = bridge.ReconDroneTransform;
            bool playing = gameStateManager == null || gameStateManager.CurrentState == GameState.Playing ||
                           gameStateManager.CurrentState == GameState.Tutorial;
            if (drone == null || hidden || !playing || bridge.HasWon || bridge.HasLost)
            {
                if (MinigameActive) FailMinigame(bridge, "Hackeo interrumpido");
                Target = null;
                return;
            }

            RefreshTarget(drone);

            if (AwaitingServer && Time.unscaledTime - awaitingSince > ServerReplyTimeoutSeconds)
            {
                awaitingObstacle = -1;
                EscapeModeHudFeed.Publish("El servidor no respondió al hackeo; inténtalo de nuevo");
            }

            if (MinigameActive)
            {
                UpdateMinigame(bridge);
                return;
            }

            ReadHackButton(out bool held, out bool pressedThisFrame);
            if (pressedThisFrame)
            {
                holdTime = 0f;
                holdHandled = false;
            }
            if (!held)
            {
                holdTime = 0f;
                holdHandled = false;
                return;
            }

            bool blocked = droneLock.IsDroneLocked(bridge) || bridge.HackMinigameActive || AwaitingServer;
            if (blocked) return;
            holdTime += Time.unscaledDeltaTime;
            if (holdHandled || holdTime < HackHoldSeconds) return;
            holdHandled = true;
            TryStartHack(bridge);
        }

        private static void ReadHackButton(out bool held, out bool pressedThisFrame)
        {
            // △ (North) quedó reservado en el bridge para "acciones futuras"; G está libre en el teclado.
            Gamepad pad = Gamepad.current;
            Keyboard keyboard = Keyboard.current;
            held = (pad != null && pad.buttonNorth.isPressed) || (keyboard != null && keyboard.gKey.isPressed);
            pressedThisFrame = (pad != null && pad.buttonNorth.wasPressedThisFrame) ||
                               (keyboard != null && keyboard.gKey.wasPressedThisFrame);
        }

        private void RefreshTarget(Transform drone)
        {
            Target = null;
            TargetDistance = float.MaxValue;
            foreach (EscapeHackableObstacle obstacle in obstacles)
            {
                if (obstacle == null || obstacle.IsOpen || !obstacle.gameObject.activeInHierarchy) continue;
                float distance = Vector3.Distance(obstacle.HackPoint, drone.position);
                if (distance > HackRangeMeters || distance >= TargetDistance) continue;
                Target = obstacle;
                TargetDistance = distance;
            }
        }

        private void TryStartHack(EscapeModeNetworkBridge bridge)
        {
            if (Target == null)
            {
                EscapeModeHudFeed.Publish($"No hay obstáculo a alcance para hackear (máx. {HackRangeMeters:0} m)");
                return;
            }

            if (CurrentTransport(bridge) == Transport.Python)
            {
                if (pythonClient == null || !pythonClient.Connected) return;
                awaitingObstacle = Target.Id;
                awaitingSince = Time.unscaledTime;
                pythonClient.HackStart(Target.Id);
                return;
            }
            BeginMinigame(Target, HackTimeLimitSeconds);
        }

        private void BeginMinigame(EscapeHackableObstacle obstacle, float seconds)
        {
            minigameObstacle = obstacle;
            MinigameProgress = 0;
            for (int i = 0; i < pattern.Length; i++) pattern[i] = UnityEngine.Random.Range(0, 4);
            minigameDeadline = Time.unscaledTime + seconds;
            droneLock.SetHold(true);
            EscapeModeHudFeed.Publish($"Hackeo de {obstacle.DisplayName}: repite la secuencia con la cruceta o las flechas");
        }

        private void UpdateMinigame(EscapeModeNetworkBridge bridge)
        {
            // Si el bridge arranca su minijuego de puerta, las flechas irían a ambos.
            if (bridge.HackMinigameActive)
            {
                FailMinigame(bridge, "Hackeo interrumpido por otra puerta");
                return;
            }
            if (Time.unscaledTime >= minigameDeadline)
            {
                FailMinigame(bridge, "Se agotó el tiempo");
                return;
            }

            int direction = -1;
            int pressedCount = 0;
            Gamepad pad = Gamepad.current;
            Keyboard keyboard = Keyboard.current;
            if (pad != null)
            {
                ReadPress(pad.dpad.up, 0, ref direction, ref pressedCount);
                ReadPress(pad.dpad.down, 1, ref direction, ref pressedCount);
                ReadPress(pad.dpad.left, 2, ref direction, ref pressedCount);
                ReadPress(pad.dpad.right, 3, ref direction, ref pressedCount);
            }
            if (keyboard != null)
            {
                ReadPress(keyboard.upArrowKey, 0, ref direction, ref pressedCount);
                ReadPress(keyboard.downArrowKey, 1, ref direction, ref pressedCount);
                ReadPress(keyboard.leftArrowKey, 2, ref direction, ref pressedCount);
                ReadPress(keyboard.rightArrowKey, 3, ref direction, ref pressedCount);
            }
            if (pressedCount == 0) return;
            if (pressedCount > 1 || direction != pattern[MinigameProgress])
            {
                FailMinigame(bridge, "Patrón incorrecto");
                return;
            }

            MinigameProgress++;
            if (MinigameProgress >= HackPatternLength) CompleteMinigame(bridge);
        }

        private static void ReadPress(ButtonControl button, int value, ref int direction, ref int pressedCount)
        {
            if (!button.wasPressedThisFrame) return;
            direction = value;
            pressedCount++;
        }

        private void CompleteMinigame(EscapeModeNetworkBridge bridge)
        {
            EscapeHackableObstacle obstacle = minigameObstacle;
            EndMinigame();
            EscapeModeHudFeed.Publish($"Hackeo completado: {obstacle.DisplayName}");

            switch (CurrentTransport(bridge))
            {
                case Transport.Python:
                    pythonClient?.HackResult(true);
                    break;
                case Transport.Ngo:
                    SendObstacleHacked(obstacle.Id);
                    break;
                default:
                    ApplyObstacleState(obstacle.Id, true, announce: true);
                    break;
            }
        }

        private void FailMinigame(EscapeModeNetworkBridge bridge, string reason)
        {
            if (!MinigameActive) return;
            EndMinigame();
            if (CurrentTransport(bridge) == Transport.Python)
            {
                // El servidor decide el bloqueo y lo devuelve en hack_result.lock.
                pythonClient?.HackResult(false);
                EscapeModeHudFeed.Publish(reason);
                return;
            }
            droneLock.Lock(bridge, HackFailureLockSeconds, $"{reason}: dron bloqueado por {HackFailureLockSeconds:0} s");
        }

        private void EndMinigame()
        {
            minigameObstacle = null;
            MinigameProgress = 0;
            if (droneLock != null) droneLock.SetHold(false);
        }

        // ---------------------------------------------------------- red NGO

        private void UpdateNgoMessaging()
        {
            NetworkManager manager = NetworkManager.Singleton;
            CustomMessagingManager messaging = manager != null && manager.IsListening ? manager.CustomMessagingManager : null;
            if (messaging == registeredMessaging) return;

            UnregisterNgoHandlers();
            if (messaging == null) return;
            messaging.RegisterNamedMessageHandler(ObstacleHackedMessage, ReceiveObstacleHacked);
            messaging.RegisterNamedMessageHandler(ObstacleStateMessage, ReceiveObstacleState);
            messaging.RegisterNamedMessageHandler(StateRequestMessage, ReceiveStateRequest);
            registeredMessaging = messaging;
            hasHostState = false;
            nextStateRequest = 0f;
        }

        private void UnregisterNgoHandlers()
        {
            if (registeredMessaging == null) return;
            try
            {
                registeredMessaging.UnregisterNamedMessageHandler(ObstacleHackedMessage);
                registeredMessaging.UnregisterNamedMessageHandler(ObstacleStateMessage);
                registeredMessaging.UnregisterNamedMessageHandler(StateRequestMessage);
            }
            catch (Exception) { /* el NetworkManager ya se apagó */ }
            registeredMessaging = null;
        }

        private void UpdateHostStateRequest()
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (registeredMessaging == null || manager == null || manager.IsServer || !manager.IsConnectedClient) return;
            if (hasHostState || Time.unscaledTime < nextStateRequest) return;
            nextStateRequest = Time.unscaledTime + StateRequestIntervalSeconds;
            using (var writer = new FastBufferWriter(1, Allocator.Temp))
                registeredMessaging.SendNamedMessage(StateRequestMessage, NetworkManager.ServerClientId, writer,
                    NetworkDelivery.ReliableSequenced);
        }

        private void SendObstacleHacked(int id)
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (registeredMessaging == null || manager == null) return;
            if (manager.IsServer)
            {
                ApplyObstacleState(id, true, announce: true);
                BroadcastState();
                return;
            }
            using (var writer = new FastBufferWriter(1, Allocator.Temp))
            {
                writer.WriteValueSafe((byte)id);
                registeredMessaging.SendNamedMessage(ObstacleHackedMessage, NetworkManager.ServerClientId, writer,
                    NetworkDelivery.ReliableSequenced);
            }
        }

        private void ReceiveObstacleHacked(ulong senderClientId, FastBufferReader reader)
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsServer || senderClientId == NetworkManager.ServerClientId) return;
            reader.ReadValueSafe(out byte id);
            ApplyObstacleState(id, true, announce: true);
            BroadcastState();
        }

        private void ReceiveStateRequest(ulong senderClientId, FastBufferReader reader)
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsServer || senderClientId == NetworkManager.ServerClientId) return;
            SendState(senderClientId);
        }

        private void ReceiveObstacleState(ulong senderClientId, FastBufferReader reader)
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || manager.IsServer || senderClientId != NetworkManager.ServerClientId) return;
            reader.ReadValueSafe(out byte mask);
            hasHostState = true;
            foreach (EscapeHackableObstacle obstacle in obstacles)
                if (obstacle != null) ApplyObstacleState(obstacle.Id, (mask & (1 << obstacle.Id)) != 0, announce: true);
        }

        private byte OpenMask()
        {
            int mask = 0;
            foreach (EscapeHackableObstacle obstacle in obstacles)
                if (obstacle != null && obstacle.IsOpen) mask |= 1 << obstacle.Id;
            return (byte)mask;
        }

        private void BroadcastState()
        {
            if (registeredMessaging == null) return;
            using (var writer = new FastBufferWriter(1, Allocator.Temp))
            {
                writer.WriteValueSafe(OpenMask());
                registeredMessaging.SendNamedMessageToAll(ObstacleStateMessage, writer, NetworkDelivery.ReliableSequenced);
            }
        }

        private void SendState(ulong clientId)
        {
            using (var writer = new FastBufferWriter(1, Allocator.Temp))
            {
                writer.WriteValueSafe(OpenMask());
                registeredMessaging.SendNamedMessage(ObstacleStateMessage, clientId, writer, NetworkDelivery.ReliableSequenced);
            }
        }

        // ------------------------------------------------------- red Python

        private void UpdatePythonSubscription()
        {
            PythonRelayClient wanted = PythonEscapeModeBridge.Instance != null
                ? (pythonClient != null ? pythonClient : FindFirstObjectByType<PythonRelayClient>())
                : null;
            if (wanted == pythonClient) return;
            if (pythonClient != null) pythonClient.OnMessage -= HandlePythonMessage;
            pythonClient = wanted;
            if (pythonClient != null) pythonClient.OnMessage += HandlePythonMessage;
        }

        private void HandlePythonMessage(string type, string json)
        {
            if (type == "snap")
            {
                bool[] hacked = pythonClient != null ? pythonClient.LastSnapshot?.hacked : null;
                if (hacked == null) return;
                foreach (EscapeHackableObstacle obstacle in obstacles)
                    if (obstacle != null && obstacle.Id < hacked.Length)
                        ApplyObstacleState(obstacle.Id, hacked[obstacle.Id], announce: true);
                return;
            }
            if (type != "hack_started" && type != "hack_denied" && type != "hack_result") return;

            ServerHazardMessage message;
            try { message = JsonUtility.FromJson<ServerHazardMessage>(json); }
            catch (Exception) { return; }
            EscapeModeNetworkBridge bridge = EscapeModeNetworkBridge.Instance;
            if (message == null || bridge == null || !bridge.IsHacker) return;

            switch (type)
            {
                case "hack_started":
                    if (message.obstacle != awaitingObstacle) return;
                    awaitingObstacle = -1;
                    EscapeHackableObstacle obstacle = FindObstacle(message.obstacle);
                    if (obstacle != null && !obstacle.IsOpen)
                        BeginMinigame(obstacle, message.time > 0f ? message.time : HackTimeLimitSeconds);
                    break;
                case "hack_denied":
                    if (message.obstacle != awaitingObstacle) return;
                    awaitingObstacle = -1;
                    EscapeModeHudFeed.Publish(message.left > 0f
                        ? $"Hackeo rechazado: {message.reason} ({message.left:0.0} s)"
                        : $"Hackeo rechazado: {message.reason}");
                    break;
                case "hack_result":
                    // También llega si el servidor agotó el tiempo antes que el cliente.
                    if (MinigameActive && minigameObstacle.Id == message.obstacle) EndMinigame();
                    if (!message.ok && message.@lock > 0f)
                        droneLock.Lock(bridge, message.@lock, $"Hackeo fallido: dron bloqueado por {message.@lock:0} s");
                    break;
            }
        }

        // ---------------------------------------------------------- minimapa

        private void CollectMinimapMarkers(List<EscapeMinimap.Marker> output)
        {
            if (hidden) return;
            EscapeModeNetworkBridge bridge = EscapeModeNetworkBridge.Instance;
            bool hacker = bridge != null && bridge.IsHacker;
            foreach (EscapeHackableObstacle obstacle in obstacles)
            {
                if (obstacle == null || !obstacle.gameObject.activeInHierarchy) continue;
                output.Add(new EscapeMinimap.Marker
                {
                    Position = obstacle.transform.position,
                    Shape = EscapeMinimap.MarkerShape.Bar,
                    Color = obstacle.IsOpen ? ObstacleOpenColor : ObstacleClosedColor,
                    SizePixels = KartUIStyle.S(6f),
                    Yaw = obstacle.LongAxisYaw,
                    LengthMeters = obstacle.MinimapLengthMeters,
                    Highlight = hacker && obstacle == Target
                });
            }
        }

#if UNITY_EDITOR
        /// <summary>
        /// Solo en el editor y sin red: F9 muestra los obstáculos en Escape Solo y
        /// después alterna abrir/cerrar todos, para probar la física del conductor.
        /// </summary>
        private void UpdateEditorDebugKey(EscapeModeNetworkBridge bridge)
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.f9Key.wasPressedThisFrame || CurrentTransport(bridge) != Transport.Local) return;
            if (hidden)
            {
                debugReveal = true;
                EscapeModeHudFeed.Publish("[Editor] Obstáculos visibles (F9 de nuevo para abrir/cerrar)");
                return;
            }
            bool open = obstacles.Exists(o => o != null && !o.IsOpen);
            foreach (EscapeHackableObstacle obstacle in obstacles)
                if (obstacle != null) ApplyObstacleState(obstacle.Id, open, announce: true);
        }
#endif

        private void OnDestroy()
        {
            UnregisterNgoHandlers();
            if (pythonClient != null) pythonClient.OnMessage -= HandlePythonMessage;
            EscapeMinimap.UnregisterMarkerSource(CollectMinimapMarkers);
            foreach (string key in legendKeys) EscapeMinimap.UnregisterLegendEntry(key);
            if (Instance == this) Instance = null;
        }
    }
}
