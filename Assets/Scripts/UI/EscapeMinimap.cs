using System;
using System.Collections.Generic;
using KartIHC.Gameplay;
using KartIHC.Kart;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KartIHC.UI
{
    /// <summary>
    /// Minimapa cenital del Modo Escape (norte = +Z fijo arriba) en la esquina
    /// inferior izquierda, la única libre del HUD. Lo ven conductor y hacker:
    /// kart con orientación, dron, meta y puertas hackeables.
    ///
    /// Posición del dron en la máquina del conductor:
    /// - Relay/NGO: este componente envía su propio mensaje "KartIHC.Minimap.Drone"
    ///   (hacker → host, 10 Hz) sin tocar EscapeModeNetworkBridge.
    /// - Servidor Python: ya viaja en cada snapshot (snap.hacker.p/r); solo se lee.
    /// Si no llegan datos del dron en 2 s, su icono se atenúa y dice "sin señal".
    ///
    /// Se crea solo al cargar EscapeCourse; no requiere cambios en la escena.
    /// </summary>
    public sealed class EscapeMinimap : MonoBehaviour
    {
        public enum MarkerShape { Square, Dot, Checker }

        /// <summary>Marcador genérico para fuentes adicionales (trampas, patrullas...).</summary>
        public struct Marker
        {
            public Vector3 Position;
            public MarkerShape Shape;
            public Color Color;
            public float SizePixels;
        }

        private const string EscapeSceneName = "EscapeCourse";
        private const string DroneMessage = "KartIHC.Minimap.Drone";
        private const float DroneSendInterval = 0.1f;
        private const float DroneSignalTimeout = 2f;
        // Mismo alcance que EscapeModeNetworkBridge.DoorHackRangeMeters (privado allí).
        private const float DoorHackRangeMeters = 7f;
        private const int MapResolution = 256;
        private const float WorldPadding = 4f;
        // GameHUD (esquina superior izquierda, sin escalar): 16 px de margen +
        // 4 filas de fontSize 30 * 1.4 + 24 = 208 px. Se deja un poco de aire.
        private const float GameHudReservedHeight = 220f;

        private static readonly Color KartColor = KartUIStyle.Accent;
        private static readonly Color DroneColor = new Color(0.3f, 0.82f, 1f);
        private static readonly Color StaleColor = new Color(0.6f, 0.6f, 0.6f);
        private static readonly Color DoorClosedColor = new Color(1f, 0.32f, 0.3f);
        private static readonly Color DoorOpenColor = KartUIStyle.Good;
        private static readonly Color RoadColor = new Color(0.36f, 0.38f, 0.42f, 1f);
        private static readonly Color BarrierColor = new Color(0.86f, 0.86f, 0.84f, 0.95f);
        private static readonly Color CityColor = new Color(0.24f, 0.26f, 0.3f, 1f);

        /// <summary>
        /// Punto de extensión: cada fuente agrega sus marcadores en cada frame.
        /// Para trampas: PythonRelayClient.LastSnapshot.traps o EscapePoliceTrap;
        /// para patrullas: EscapePoliceCar. Registrar desde código propio.
        /// </summary>
        private static readonly List<Action<List<Marker>>> markerSources = new List<Action<List<Marker>>>();

        public static void RegisterMarkerSource(Action<List<Marker>> source)
        {
            if (source != null && !markerSources.Contains(source)) markerSources.Add(source);
        }

        public static void UnregisterMarkerSource(Action<List<Marker>> source) => markerSources.Remove(source);

        private readonly List<Marker> markers = new List<Marker>();
        private Texture2D mapTexture;
        private Texture2D triangleTexture;
        private Texture2D circleTexture;
        private Texture2D ringTexture;
        private Rect worldRect;
        private bool mapBuilt;
        private bool builtWithPolice;
        private EscapeModeNetworkBridge builtForBridge;

        private KartController kart;
        private GameStateManager gameStateManager;
        private GUIStyle labelStyle;
        private GUIStyle smallStyle;

        private CustomMessagingManager registeredMessaging;
        private float nextDroneSend;
        private PythonRelayClient pythonClient;

        private Vector3 remoteDronePosition;
        private float remoteDroneYaw;
        private bool hasRemoteDrone;
        private float remoteDroneReceivedAt = float.NegativeInfinity;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != EscapeSceneName || FindFirstObjectByType<EscapeMinimap>() != null) return;
            // Objeto de escena (no DontDestroyOnLoad): se destruye al salir de EscapeCourse.
            new GameObject("EscapeMinimap").AddComponent<EscapeMinimap>();
        }

        private void Awake()
        {
            triangleTexture = CreateTriangleTexture(64);
            circleTexture = CreateCircleTexture(64, 0f);
            ringTexture = CreateCircleTexture(128, 0.9f);
            RegisterMarkerSource(CollectFinishZones);
            RegisterMarkerSource(CollectDoors);
        }

        private void Update()
        {
            EscapeModeNetworkBridge bridge = EscapeModeNetworkBridge.Instance;
            UpdateNgoMessaging(bridge);
            UpdatePythonSubscription();
            if (bridge == null) return;

            if (kart == null) kart = FindFirstObjectByType<KartController>();
            if (gameStateManager == null) gameStateManager = FindFirstObjectByType<GameStateManager>();

            bool policePresent = EscapePoliceStationGenerator.Instance != null;
            if (!mapBuilt || builtForBridge != bridge || builtWithPolice != policePresent)
            {
                BuildMapTexture();
                builtForBridge = bridge;
                builtWithPolice = policePresent;
            }

            if (bridge.IsHacker) SendLocalDroneState(bridge);
        }

        // ---------------------------------------------------------------- red

        private void UpdateNgoMessaging(EscapeModeNetworkBridge bridge)
        {
            NetworkManager manager = NetworkManager.Singleton;
            CustomMessagingManager messaging = manager != null && manager.IsListening ? manager.CustomMessagingManager : null;
            if (messaging == registeredMessaging) return;

            UnregisterNgoHandler();
            if (messaging == null) return;
            messaging.RegisterNamedMessageHandler(DroneMessage, ReceiveDroneState);
            registeredMessaging = messaging;
        }

        private void UnregisterNgoHandler()
        {
            if (registeredMessaging == null) return;
            try { registeredMessaging.UnregisterNamedMessageHandler(DroneMessage); }
            catch (Exception) { /* el NetworkManager ya se apagó */ }
            registeredMessaging = null;
        }

        private void SendLocalDroneState(EscapeModeNetworkBridge bridge)
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (registeredMessaging == null || manager == null || !manager.IsConnectedClient || manager.IsServer) return;
            if (PythonEscapeModeBridge.Instance != null || Time.unscaledTime < nextDroneSend) return;
            Transform drone = bridge.ReconDroneTransform;
            if (drone == null) return;

            nextDroneSend = Time.unscaledTime + DroneSendInterval;
            using (var writer = new FastBufferWriter(32, Allocator.Temp))
            {
                writer.WriteValueSafe(drone.position);
                writer.WriteValueSafe(YawOf(drone.rotation));
                registeredMessaging.SendNamedMessage(DroneMessage, NetworkManager.ServerClientId, writer,
                    NetworkDelivery.UnreliableSequenced);
            }
        }

        private void ReceiveDroneState(ulong senderClientId, FastBufferReader reader)
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsServer || senderClientId == NetworkManager.ServerClientId) return;
            reader.ReadValueSafe(out Vector3 position);
            reader.ReadValueSafe(out float yaw);
            StoreRemoteDrone(position, yaw);
        }

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
            if (type != "snap" || pythonClient == null) return;
            PythonRelayClient.HackerState hacker = pythonClient.LastSnapshot?.hacker;
            if (hacker == null || !hacker.connected || hacker.p == null || hacker.p.Length < 3) return;

            var position = new Vector3(hacker.p[0], hacker.p[1], hacker.p[2]);
            Quaternion rotation = hacker.r != null && hacker.r.Length >= 4
                ? new Quaternion(hacker.r[0], hacker.r[1], hacker.r[2], hacker.r[3])
                : Quaternion.identity;
            // El servidor arranca al hacker en (0, 20, 0) sin rotación hasta que
            // llega su primer estado: eso aún no es una posición real del dron.
            if (!hasRemoteDrone && position == new Vector3(0f, 20f, 0f) && rotation == Quaternion.identity) return;
            StoreRemoteDrone(position, YawOf(rotation));
        }

        private void StoreRemoteDrone(Vector3 position, float yaw)
        {
            remoteDronePosition = position;
            remoteDroneYaw = yaw;
            hasRemoteDrone = true;
            remoteDroneReceivedAt = Time.unscaledTime;
        }

        // ---------------------------------------------------------- textura

        private void BuildMapTexture()
        {
            var sources = new List<(MeshRenderer renderer, int layer)>();
            bool hasBounds = false;
            Bounds bounds = default;
            foreach (MeshRenderer renderer in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (!renderer.gameObject.activeInHierarchy) continue;
                int layer = LayerFor(renderer.transform);
                if (layer < 0) continue;
                sources.Add((renderer, layer));
                if (hasBounds) bounds.Encapsulate(renderer.bounds);
                else { bounds = renderer.bounds; hasBounds = true; }
            }
            foreach (EscapeFinishZone finish in FindObjectsByType<EscapeFinishZone>(FindObjectsSortMode.None))
            {
                if (!hasBounds) { bounds = new Bounds(finish.transform.position, Vector3.one); hasBounds = true; }
                else bounds.Encapsulate(finish.transform.position);
            }
            if (!hasBounds) bounds = new Bounds(Vector3.zero, new Vector3(80f, 1f, 80f));

            float span = Mathf.Max(bounds.size.x, bounds.size.z) + WorldPadding * 2f;
            worldRect = new Rect(bounds.center.x - span * 0.5f, bounds.center.z - span * 0.5f, span, span);

            if (mapTexture == null)
            {
                mapTexture = new Texture2D(MapResolution, MapResolution, TextureFormat.RGBA32, false)
                {
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear
                };
            }
            var pixels = new Color32[MapResolution * MapResolution];
            float texel = span / MapResolution;
            // Ciudad primero, luego pista, luego barreras encima.
            for (int layer = 0; layer <= 2; layer++)
            {
                Color32 color = layer == 0 ? CityColor : layer == 1 ? RoadColor : BarrierColor;
                foreach ((MeshRenderer renderer, int sourceLayer) in sources)
                    if (sourceLayer == layer) RasterizeBox(renderer, pixels, color, texel);
            }
            mapTexture.SetPixels32(pixels);
            mapTexture.Apply();
            mapBuilt = true;
        }

        /// <summary>0 = área de la ciudad (modo policía), 1 = pista, 2 = barrera, -1 = ignorar.</summary>
        private static int LayerFor(Transform target)
        {
            string objectName = target.name;
            if (objectName.StartsWith("EscapeRoad_")) return 1;
            if (objectName.StartsWith("EscapeInnerBarrier_") || objectName.StartsWith("EscapeOuterBarrier_")) return 2;
            for (Transform parent = target.parent; parent != null; parent = parent.parent)
            {
                if (parent.name != "EscapePoliceStationArea" && parent.name != "EscapeSoloCityTestArea") continue;
                if (objectName.StartsWith("ExitRoadLine")) return -1;
                return objectName.Contains("Wall") ? 2 : objectName == "ExitRoad" ? 1 : 0;
            }
            return -1;
        }

        private void RasterizeBox(MeshRenderer renderer, Color32[] pixels, Color32 color, float texel)
        {
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            Bounds local = filter != null && filter.sharedMesh != null
                ? filter.sharedMesh.bounds
                : new Bounds(Vector3.zero, Vector3.one);
            Transform target = renderer.transform;
            Matrix4x4 worldToLocal = target.worldToLocalMatrix;
            Vector3 scale = target.lossyScale;
            // Medio texel de tolerancia para que las barreras finas no queden con huecos.
            float toleranceX = texel * 0.5f / Mathf.Max(0.0001f, Mathf.Abs(scale.x));
            float toleranceZ = texel * 0.5f / Mathf.Max(0.0001f, Mathf.Abs(scale.z));
            Bounds world = renderer.bounds;

            int minX = Mathf.Clamp(Mathf.FloorToInt((world.min.x - worldRect.x) / texel), 0, MapResolution - 1);
            int maxX = Mathf.Clamp(Mathf.CeilToInt((world.max.x - worldRect.x) / texel), 0, MapResolution - 1);
            int minY = Mathf.Clamp(Mathf.FloorToInt((world.min.z - worldRect.y) / texel), 0, MapResolution - 1);
            int maxY = Mathf.Clamp(Mathf.CeilToInt((world.max.z - worldRect.y) / texel), 0, MapResolution - 1);
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    var point = new Vector3(worldRect.x + (x + 0.5f) * texel, world.center.y, worldRect.y + (y + 0.5f) * texel);
                    Vector3 p = worldToLocal.MultiplyPoint3x4(point);
                    if (Mathf.Abs(p.x - local.center.x) <= local.extents.x + toleranceX &&
                        Mathf.Abs(p.z - local.center.z) <= local.extents.z + toleranceZ)
                        pixels[y * MapResolution + x] = color;
                }
            }
        }

        // ------------------------------------------------------------- dibujo

        private void OnGUI()
        {
            EscapeModeNetworkBridge bridge = EscapeModeNetworkBridge.Instance;
            if (bridge == null || !mapBuilt) return;
            if (gameStateManager != null &&
                (gameStateManager.CurrentState == GameState.MainMenu || gameStateManager.CurrentState == GameState.Paused))
                return;

            // Profundidad mayor = se dibuja debajo: el HUD existente queda siempre encima.
            GUI.depth = 10;
            KartUIStyle.Ensure();
            EnsureStyles();

            bool showKart = kart != null && !(bridge.IsHacker && bridge.IsOfflinePractice);
            bool expectDrone = bridge.IsHacker || !bridge.IsOfflinePractice;
            bool droneFresh = bridge.IsHacker
                ? bridge.ReconDroneTransform != null
                : hasRemoteDrone && Time.unscaledTime - remoteDroneReceivedAt <= DroneSignalTimeout;

            markers.Clear();
            foreach (Action<List<Marker>> source in markerSources.ToArray())
            {
                try { source(markers); }
                catch (Exception exception) { Debug.LogException(exception); }
            }
            bool anyDoor = false;
            foreach (EscapeDoor door in EscapeCourseBuilder.Doors)
                if (door != null && door.gameObject.activeInHierarchy) anyDoor = true;

            Rect panel = ComputePanelRect(bridge, out Rect map, out Rect legend);
            KartUIStyle.Panel(panel, accentBar: false);
            GUI.DrawTexture(map, mapTexture, ScaleMode.StretchToFill, true);
            DrawNorthLabel(map);

            GUI.BeginGroup(map);
            Rect local = new Rect(0f, 0f, map.width, map.height);
            foreach (Marker marker in markers) DrawMarker(local, marker);
            if (bridge.IsHacker) DrawHackableDoorHighlights(local, bridge);
            if (expectDrone) DrawDrone(local, bridge, droneFresh);
            if (showKart) DrawHeadingIcon(local, kart.transform.position, YawOf(kart.transform.rotation),
                KartUIStyle.S(18f), KartColor);
            GUI.EndGroup();

            if (expectDrone && !bridge.IsHacker && !hasRemoteDrone)
                GUI.Label(new Rect(map.x, map.y + map.height * 0.5f - KartUIStyle.S(12f), map.width, KartUIStyle.S(24f)),
                    "DRON · SIN SEÑAL", smallStyle);

            DrawLegend(legend, showKart, expectDrone, droneFresh, anyDoor);
        }

        private Rect ComputePanelRect(EscapeModeNetworkBridge bridge, out Rect map, out Rect legend)
        {
            float margin = KartUIStyle.S(16f);
            float padding = KartUIStyle.S(8f);
            float legendHeight = KartUIStyle.S(46f);
            float available = Screen.height - GameHudReservedHeight - margin;
            float mapSize = Mathf.Min(KartUIStyle.S(260f), Screen.width * 0.3f,
                available - legendHeight - padding * 3f);
            mapSize = Mathf.Max(KartUIStyle.S(110f), mapSize);
            float width = mapSize + padding * 2f;
            float height = mapSize + legendHeight + padding * 3f;
            var panel = new Rect(margin, Screen.height - height - margin, width, height);

            // Esquivar los paneles inferiores centrados de otros HUD.
            Rect? blocker = null;
            if (bridge.IsHacker && (bridge.HackMinigameActive || bridge.DroneLocked))
            {
                // Mismo cálculo que GameHUD.DrawHackMinigameOverlay.
                float overlayWidth = Mathf.Min(700f, Screen.width - 32f);
                float overlayHeight = bridge.HackMinigameActive ? 158f : 108f;
                blocker = new Rect((Screen.width - overlayWidth) * 0.5f,
                    Mathf.Max(8f, Screen.height - overlayHeight - 20f), overlayWidth, overlayHeight);
            }
            else if (!bridge.IsHacker)
            {
                // Caja "ITEM" de KartItemInventory.
                blocker = new Rect(Screen.width / 2f - 100f, Screen.height - 56f, 200f, 36f);
            }
            if (blocker.HasValue && panel.Overlaps(blocker.Value))
                panel.y = Mathf.Max(GameHudReservedHeight, blocker.Value.y - height - margin);

            map = new Rect(panel.x + padding, panel.y + padding, mapSize, mapSize);
            legend = new Rect(panel.x + padding, map.yMax + padding, mapSize, legendHeight);
            return panel;
        }

        private Vector2 WorldToMap(Rect map, Vector3 world)
        {
            float u = Mathf.Clamp01((world.x - worldRect.x) / worldRect.width);
            float v = Mathf.Clamp01((world.z - worldRect.y) / worldRect.height);
            return new Vector2(map.x + u * map.width, map.yMax - v * map.height);
        }

        private void DrawMarker(Rect map, Marker marker)
        {
            Vector2 center = WorldToMap(map, marker.Position);
            float size = marker.SizePixels;
            var rect = new Rect(center.x - size * 0.5f, center.y - size * 0.5f, size, size);
            switch (marker.Shape)
            {
                case MarkerShape.Checker:
                    DrawChecker(rect);
                    break;
                case MarkerShape.Dot:
                    DrawTinted(rect, circleTexture, marker.Color);
                    break;
                default:
                    KartUIStyle.Fill(rect, new Color(0f, 0f, 0f, 0.6f));
                    KartUIStyle.Fill(new Rect(rect.x + 1f, rect.y + 1f, rect.width - 2f, rect.height - 2f), marker.Color);
                    break;
            }
        }

        private void DrawHackableDoorHighlights(Rect map, EscapeModeNetworkBridge bridge)
        {
            Transform drone = bridge.ReconDroneTransform;
            if (drone == null) return;
            float pulse = 0.55f + Mathf.Sin(Time.unscaledTime * 5f) * 0.3f;
            float size = KartUIStyle.S(18f);
            foreach (EscapeDoor door in EscapeCourseBuilder.Doors)
            {
                if (door == null || !door.gameObject.activeInHierarchy) continue;
                if ((door.transform.position - drone.position).sqrMagnitude > DoorHackRangeMeters * DoorHackRangeMeters) continue;
                Vector2 center = WorldToMap(map, door.transform.position);
                DrawTinted(new Rect(center.x - size * 0.5f, center.y - size * 0.5f, size, size), ringTexture,
                    new Color(KartColor.r, KartColor.g, KartColor.b, pulse));
            }
        }

        private void DrawDrone(Rect map, EscapeModeNetworkBridge bridge, bool fresh)
        {
            Vector3 position;
            float yaw;
            if (bridge.IsHacker)
            {
                Transform drone = bridge.ReconDroneTransform;
                if (drone == null) return;
                position = drone.position;
                yaw = YawOf(drone.rotation);

                // Alcance de hackeo alrededor del dron, a escala.
                float rangePixels = DoorHackRangeMeters * 2f / worldRect.width * map.width;
                Vector2 c = WorldToMap(map, position);
                DrawTinted(new Rect(c.x - rangePixels * 0.5f, c.y - rangePixels * 0.5f, rangePixels, rangePixels),
                    ringTexture, new Color(DroneColor.r, DroneColor.g, DroneColor.b, bridge.DroneLocked ? 0.2f : 0.45f));
            }
            else
            {
                if (!hasRemoteDrone) return;
                position = remoteDronePosition;
                yaw = remoteDroneYaw;
            }

            Color color = fresh && !bridge.DroneLocked ? DroneColor : StaleColor;
            float alpha = fresh ? 1f : 0.4f;
            float size = KartUIStyle.S(14f);
            Vector2 center = WorldToMap(map, position);
            DrawTinted(new Rect(center.x - size * 0.5f, center.y - size * 0.5f, size, size), circleTexture,
                new Color(color.r, color.g, color.b, alpha));
            DrawHeadingIcon(map, position, yaw, size * 0.7f, new Color(0.05f, 0.06f, 0.08f, alpha));
            if (!fresh)
                GUI.Label(new Rect(center.x + size * 0.6f, center.y - KartUIStyle.S(10f), KartUIStyle.S(90f), KartUIStyle.S(20f)),
                    "sin señal", labelStyle);
        }

        private void DrawHeadingIcon(Rect map, Vector3 position, float yaw, float size, Color color)
        {
            Vector2 center = WorldToMap(map, position);
            Matrix4x4 previous = GUI.matrix;
            // IMGUI: ángulo positivo = horario en pantalla; yaw 0 = norte (+Z).
            GUIUtility.RotateAroundPivot(yaw, center);
            DrawTinted(new Rect(center.x - size * 0.5f, center.y - size * 0.5f, size, size), triangleTexture, color);
            GUI.matrix = previous;
        }

        private void DrawNorthLabel(Rect map)
        {
            GUI.Label(new Rect(map.x, map.y + KartUIStyle.S(2f), map.width, KartUIStyle.S(18f)), "N", smallStyle);
        }

        private void DrawLegend(Rect legend, bool showKart, bool showDrone, bool droneFresh, bool showDoors)
        {
            float row = legend.height * 0.5f;
            float column = legend.width * 0.5f;
            float icon = KartUIStyle.S(12f);
            int slot = 0;

            Rect NextSlot()
            {
                var cell = new Rect(legend.x + (slot % 2) * column, legend.y + (slot / 2) * row, column, row);
                slot++;
                return cell;
            }

            Rect IconRect(Rect cell) => new Rect(cell.x + KartUIStyle.S(2f), cell.y + (cell.height - icon) * 0.5f, icon, icon);
            Rect TextRect(Rect cell) => new Rect(cell.x + icon + KartUIStyle.S(8f), cell.y, cell.width - icon - KartUIStyle.S(8f), cell.height);

            if (showKart)
            {
                Rect cell = NextSlot();
                DrawTinted(IconRect(cell), triangleTexture, KartColor);
                GUI.Label(TextRect(cell), "Kart", labelStyle);
            }
            if (showDrone)
            {
                Rect cell = NextSlot();
                DrawTinted(IconRect(cell), circleTexture, droneFresh ? DroneColor : new Color(StaleColor.r, StaleColor.g, StaleColor.b, 0.5f));
                GUI.Label(TextRect(cell), droneFresh ? "Dron" : "Dron · sin señal", labelStyle);
            }
            {
                Rect cell = NextSlot();
                DrawChecker(IconRect(cell));
                GUI.Label(TextRect(cell), "Meta", labelStyle);
            }
            if (showDoors)
            {
                Rect cell = NextSlot();
                Rect iconRect = IconRect(cell);
                KartUIStyle.Fill(new Rect(iconRect.x, iconRect.y, iconRect.width * 0.5f, iconRect.height), DoorClosedColor);
                KartUIStyle.Fill(new Rect(iconRect.center.x, iconRect.y, iconRect.width * 0.5f, iconRect.height), DoorOpenColor);
                GUI.Label(TextRect(cell), "Puerta", labelStyle);
            }
        }

        private static void DrawChecker(Rect rect)
        {
            float half = rect.width * 0.5f;
            KartUIStyle.Fill(rect, Color.white);
            KartUIStyle.Fill(new Rect(rect.x, rect.y, half, rect.height * 0.5f), Color.black);
            KartUIStyle.Fill(new Rect(rect.x + half, rect.y + rect.height * 0.5f, half, rect.height * 0.5f), Color.black);
        }

        private static void DrawTinted(Rect rect, Texture texture, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, true);
            GUI.color = previous;
        }

        private void EnsureStyles()
        {
            int size = Mathf.RoundToInt(15f * KartUIStyle.Scale);
            if (labelStyle != null && labelStyle.fontSize == size) return;
            labelStyle = new GUIStyle(KartUIStyle.Body) { fontSize = size, alignment = TextAnchor.MiddleLeft };
            smallStyle = new GUIStyle(KartUIStyle.Caption) { alignment = TextAnchor.MiddleCenter };
        }

        // --------------------------------------------------------- marcadores

        private void CollectDoors(List<Marker> output)
        {
            float size = KartUIStyle.S(10f);
            foreach (EscapeDoor door in EscapeCourseBuilder.Doors)
            {
                // En Escape Solo el generador de policía desactiva las puertas.
                if (door == null || !door.gameObject.activeInHierarchy) continue;
                output.Add(new Marker
                {
                    Position = door.transform.position,
                    Shape = MarkerShape.Square,
                    Color = door.IsOpen ? DoorOpenColor : DoorClosedColor,
                    SizePixels = size
                });
            }
        }

        private void CollectFinishZones(List<Marker> output)
        {
            float size = KartUIStyle.S(14f);
            foreach (EscapeFinishZone finish in FindObjectsByType<EscapeFinishZone>(FindObjectsSortMode.None))
            {
                // En Escape Solo la meta circular queda deshabilitada y vale la salida norte.
                if (!finish.isActiveAndEnabled) continue;
                output.Add(new Marker { Position = finish.transform.position, Shape = MarkerShape.Checker, SizePixels = size });
            }
        }

        // ------------------------------------------------------------ utilidades

        private static float YawOf(Quaternion rotation)
        {
            Vector3 forward = rotation * Vector3.forward;
            forward.y = 0f;
            return forward.sqrMagnitude < 0.0001f ? 0f : Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
        }

        private static Texture2D CreateTriangleTexture(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            // Punta hacia arriba (fila alta = arriba en pantalla), base con muesca.
            Vector2 tip = new Vector2(size * 0.5f, size * 0.95f);
            Vector2 left = new Vector2(size * 0.1f, size * 0.05f);
            Vector2 right = new Vector2(size * 0.9f, size * 0.05f);
            Vector2 notch = new Vector2(size * 0.5f, size * 0.28f);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    bool inside = InTriangle(p, tip, left, notch) || InTriangle(p, tip, notch, right);
                    pixels[y * size + x] = inside ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        private static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
            float d2 = (p.x - c.x) * (b.y - c.y) - (b.x - c.x) * (p.y - c.y);
            float d3 = (p.x - a.x) * (c.y - a.y) - (c.x - a.x) * (p.y - a.y);
            bool negative = d1 < 0f || d2 < 0f || d3 < 0f;
            bool positive = d1 > 0f || d2 > 0f || d3 > 0f;
            return !(negative && positive);
        }

        /// <summary>Círculo lleno (innerRadius = 0) o anillo (innerRadius = fracción del radio).</summary>
        private static Texture2D CreateCircleTexture(int size, float innerRadius)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            float radius = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(radius, radius));
                    float outer = Mathf.Clamp01(radius - distance);
                    float inner = innerRadius > 0f ? Mathf.Clamp01(distance - radius * innerRadius) : 1f;
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(255f * outer * inner));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        private void OnDestroy()
        {
            UnregisterNgoHandler();
            if (pythonClient != null) pythonClient.OnMessage -= HandlePythonMessage;
            UnregisterMarkerSource(CollectDoors);
            UnregisterMarkerSource(CollectFinishZones);
            if (mapTexture != null) Destroy(mapTexture);
            if (triangleTexture != null) Destroy(triangleTexture);
            if (circleTexture != null) Destroy(circleTexture);
            if (ringTexture != null) Destroy(ringTexture);
        }
    }
}
