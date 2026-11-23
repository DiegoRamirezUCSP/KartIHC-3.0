using System.Collections.Generic;
using KartIHC.Gameplay.Hazards;
using KartIHC.Kart;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KartIHC.Gameplay
{
    /// <summary>
    /// Reaparición del kart en EscapeCourse en un punto fijo por sector.
    ///
    /// Sectores: el anillo se corta en cada puerta y obstáculo hackeable. El punto
    /// fijo de cada sector está unos metros después de su puerta/obstáculo, en el
    /// centro de la pista y mirando en el sentido de carrera; el primer sector usa
    /// EscapeKartSpawn. Se reaparece en el sector donde el kart estuvo en pista por
    /// última vez.
    ///
    /// Se activa (solo en la máquina del conductor) cuando:
    /// - el kart pasa 1,5 s fuera del anillo (y fuera del carril de salida de Escape Solo);
    /// - KartRecovery lo reaparece (volcado, caída o tecla T): KartRecovery lo deja
    ///   en la pose de escena del kart, fuera de la barrera exterior, así que aquí
    ///   se corrige al punto del sector. No modifica KartRecovery.
    /// La posición nueva llega al hacker por la sincronización normal del kart.
    /// </summary>
    public sealed class EscapeKartRespawn : MonoBehaviour
    {
        private const string EscapeSceneName = "EscapeCourse";
        private const float OffTrackSeconds = 1.5f;
        private const float OffTrackMarginMeters = 0.5f;
        private const float GateClearanceDegrees = 8f;
        private const float RespawnDropMeters = 0.05f;
        private const float DefaultSpawnAngle = 11.3f;
        // Ruta por la ciudad (EscapeRouteDefinition): el tramo inicial pisa el borde del anillo;
        // a partir de aquí cuenta como ruta aunque el radio también diga "anillo".
        private const float RouteOverRingMeters = 4f;
        private const float RouteStartRespawnMeters = 7f;
        private const float RouteGateClearanceMeters = 6f;

        private KartController kart;
        private Rigidbody body;
        private KartRecovery recovery;
        private float restHeight = 0.5f;
        private GameStateManager gameStateManager;
        private float offTrackTimer;
        private float lastTrackAngle = float.NaN;
        private bool lastOnRoute;
        private bool recoveryBlocked;
        private float lastRouteDistance;
        private Renderer exitRoad;
        private float nextExitRoadLookup;
        private readonly List<(float progress, Vector3 position, Quaternion rotation)> sectorPoints =
            new List<(float, Vector3, Quaternion)>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != EscapeSceneName || FindFirstObjectByType<EscapeKartRespawn>() != null) return;
            new GameObject("EscapeKartRespawn").AddComponent<EscapeKartRespawn>();
        }

        private static bool IsDriverSession(EscapeModeNetworkBridge bridge) =>
            bridge != null && !bridge.IsHacker && !bridge.HasWon && !bridge.HasLost;

        private void Update()
        {
            EscapeModeNetworkBridge bridge = EscapeModeNetworkBridge.Instance;
            BlockRecoveryAfterResult(bridge);
            if (!IsDriverSession(bridge) || !EnsureKart())
            {
                offTrackTimer = 0f;
                return;
            }
            if (gameStateManager == null) gameStateManager = FindFirstObjectByType<GameStateManager>();
            if (gameStateManager != null && gameStateManager.CurrentState != GameState.Playing)
            {
                offTrackTimer = 0f;
                return;
            }

            Vector3 position = kart.transform.position;
            if (TrackRoute(position))
            {
                offTrackTimer = 0f;
                return;
            }
            if (IsOnTrack(position))
            {
                lastOnRoute = false;
                lastTrackAngle = EscapeTrackGeometry.AngleOf(position);
                offTrackTimer = 0f;
                return;
            }

            offTrackTimer += Time.deltaTime;
            if (offTrackTimer >= OffTrackSeconds) RespawnAtSector("Kart fuera de la pista");
        }

        private bool EnsureKart()
        {
            if (kart != null) return true;
            kart = FindFirstObjectByType<KartController>();
            if (kart == null) return false;
            body = kart.GetComponent<Rigidbody>();
            restHeight = KartRecovery.ComputeRestHeight(kart.transform,
                new[] { kart.frontLeft, kart.frontRight, kart.rearLeft, kart.rearRight });
            recovery = kart.GetComponent<KartRecovery>();
            if (recovery != null) recovery.Respawned += OnRecoveryRespawned;
            return true;
        }

        /// <summary>
        /// Tras ganar o perder no hay reaparición (ni T ni automática) hasta reiniciar la partida:
        /// se desactiva KartRecovery (su Update atiende T, caída, vuelco y atasco). La vuelta al menú
        /// sigue funcionando porque KartResetOnMenu llama a ResetToStart directamente.
        /// </summary>
        private void BlockRecoveryAfterResult(EscapeModeNetworkBridge bridge)
        {
            bool finished = bridge != null && (bridge.HasWon || bridge.HasLost);
            if (finished == recoveryBlocked || !EnsureKart() || recovery == null) return;
            recovery.enabled = !finished;
            recoveryBlocked = finished;
        }

        private void OnRecoveryRespawned()
        {
            // KartResetOnMenu también usa KartRecovery al volver al menú: ahí no se toca.
            bool playing = gameStateManager == null || gameStateManager.CurrentState == GameState.Playing;
            if (playing && IsDriverSession(EscapeModeNetworkBridge.Instance)) RespawnAtSector("Reaparición");
        }

        /// <summary>True (y recuerda el progreso) si el kart está en el corredor de la ruta por la ciudad.</summary>
        private bool TrackRoute(Vector3 position)
        {
            EscapeRouteDefinition route = EscapeRouteDefinition.Active;
            if (route == null || !route.Contains(position, OffTrackMarginMeters, out float distance)) return false;
            bool onRing = EscapeTrackGeometry.RadiusOf(position) <= EscapeTrackGeometry.OuterRadius + OffTrackMarginMeters;
            if (onRing && distance < RouteOverRingMeters) return false; // todavía en el anillo
            lastOnRoute = true;
            lastRouteDistance = distance;
            return true;
        }

        private bool IsOnTrack(Vector3 position)
        {
            float radius = EscapeTrackGeometry.RadiusOf(position);
            if (radius >= EscapeTrackGeometry.InnerRadius - OffTrackMarginMeters &&
                radius <= EscapeTrackGeometry.OuterRadius + OffTrackMarginMeters)
                return true;

            // Escape Solo: el carril de salida norte (EscapePoliceStationGenerator) también es pista.
            if (EscapePoliceStationGenerator.Instance == null) return false;
            if (exitRoad == null && Time.unscaledTime >= nextExitRoadLookup)
            {
                nextExitRoadLookup = Time.unscaledTime + 1f;
                GameObject road = GameObject.Find("ExitRoad");
                exitRoad = road != null ? road.GetComponent<Renderer>() : null;
            }
            if (exitRoad == null) return false;
            Bounds bounds = exitRoad.bounds;
            bounds.Expand(new Vector3(1f, 20f, 1f));
            return bounds.Contains(position);
        }

        /// <summary>Reaparece en el punto fijo del sector donde el kart estuvo en pista por última vez.</summary>
        public void RespawnAtSector(string reason)
        {
            if (!EnsureKart() || body == null) return;
            EscapeRouteDefinition route = EscapeRouteDefinition.Active;
            if (lastOnRoute && route != null)
            {
                int routeSector = ChooseRouteSectorPoint(route, out Vector3 routePosition, out Quaternion routeRotation);
                Teleport(routePosition, routeRotation);
                offTrackTimer = 0f;
                EscapeModeHudFeed.Publish($"{reason}: el kart vuelve al tramo {routeSector + 1} de la ruta");
                return;
            }
            float angle = float.IsNaN(lastTrackAngle) ? EscapeTrackGeometry.AngleOf(kart.transform.position) : lastTrackAngle;
            int sector = ChooseSectorPoint(angle, out Vector3 position, out Quaternion rotation);
            Teleport(position, rotation);
            offTrackTimer = 0f;
            lastTrackAngle = EscapeTrackGeometry.AngleOf(position);
            EscapeModeHudFeed.Publish($"{reason}: el kart vuelve al sector {sector + 1}");
        }

        private int ChooseSectorPoint(float kartAngle, out Vector3 position, out Quaternion rotation)
        {
            Transform spawn = GameObject.Find("EscapeKartSpawn")?.transform;
            float spawnAngle = spawn != null ? EscapeTrackGeometry.AngleOf(spawn.position) : DefaultSpawnAngle;
            float y = spawn != null ? spawn.position.y : 1.1f;

            sectorPoints.Clear();
            sectorPoints.Add((0f,
                spawn != null ? spawn.position : EscapeTrackGeometry.PointAt(spawnAngle, EscapeTrackGeometry.RoadRadius, y),
                spawn != null ? spawn.rotation : EscapeTrackGeometry.RaceRotationAt(spawnAngle)));
            // Las puertas cuentan aunque Escape Solo las oculte: los sectores son fijos.
            // Las puertas y obstáculos que están en la ruta por la ciudad no cortan el anillo.
            EscapeRouteDefinition route = EscapeRouteDefinition.Active;
            foreach (EscapeDoor door in EscapeCourseBuilder.Doors)
                if (door != null && !OnRoute(route, door.transform.position))
                    AddGatePoint(spawnAngle, EscapeTrackGeometry.AngleOf(door.transform.position), y);
            EscapeHazardDirector hazards = EscapeHazardDirector.Instance;
            if (hazards != null)
                foreach (EscapeHackableObstacle obstacle in hazards.Obstacles)
                    if (obstacle != null && !OnRoute(route, obstacle.transform.position))
                        AddGatePoint(spawnAngle, obstacle.GateAngle, y);
            sectorPoints.Sort((a, b) => a.progress.CompareTo(b.progress));

            float kartProgress = EscapeTrackGeometry.ProgressFrom(spawnAngle, kartAngle);
            int chosen = 0;
            for (int i = 0; i < sectorPoints.Count; i++)
                if (sectorPoints[i].progress <= kartProgress) chosen = i;
            position = sectorPoints[chosen].position;
            rotation = sectorPoints[chosen].rotation;
            return chosen;
        }

        private static bool OnRoute(EscapeRouteDefinition route, Vector3 position) =>
            route != null && route.Contains(position, 1f, out float distance) && distance >= RouteOverRingMeters;

        /// <summary>
        /// Sectores de la ruta: el inicio, cada giro y cada puerta u obstáculo en la ruta. Un sector empieza
        /// en cuanto se cruza su giro/puerta/obstáculo y su punto de reaparición está 6 m después.
        /// </summary>
        private int ChooseRouteSectorPoint(EscapeRouteDefinition route, out Vector3 position, out Quaternion rotation)
        {
            Transform spawn = GameObject.Find("EscapeKartSpawn")?.transform;
            float y = spawn != null ? spawn.position.y : 1.1f;
            var gates = new List<float>();
            foreach (EscapeDoor door in EscapeCourseBuilder.Doors)
                if (door != null && route.Contains(door.transform.position, 1f, out float d) && d >= RouteOverRingMeters)
                    gates.Add(d);
            EscapeHazardDirector hazards = EscapeHazardDirector.Instance;
            if (hazards != null)
                foreach (EscapeHackableObstacle obstacle in hazards.Obstacles)
                    if (obstacle != null && route.Contains(obstacle.transform.position, 1f, out float d) && d >= RouteOverRingMeters)
                        gates.Add(d);
            // Cada giro de la ruta abre también un sector (reaparición 6 m después del giro).
            for (int i = 1; i < route.PointCount - 1; i++)
                gates.Add(route.DistanceAtPoint(i));
            gates.Sort();

            int chosen = 0;
            float at = RouteStartRespawnMeters;
            for (int i = 0; i < gates.Count; i++)
            {
                if (gates[i] > lastRouteDistance) break;
                chosen = i + 1;
                at = gates[i] + RouteGateClearanceMeters;
            }
            at = Mathf.Min(at, route.Length);
            position = route.PointAt(at) + Vector3.up * y;
            rotation = route.RotationAt(at);
            return chosen;
        }

        private void AddGatePoint(float spawnAngle, float gateAngle, float y)
        {
            // El sector empieza al cruzar la puerta; el punto de reaparición está unos grados después.
            float angle = gateAngle + GateClearanceDegrees;
            sectorPoints.Add((EscapeTrackGeometry.ProgressFrom(spawnAngle, gateAngle),
                EscapeTrackGeometry.PointAt(angle, EscapeTrackGeometry.RoadRadius, y),
                EscapeTrackGeometry.RaceRotationAt(angle)));
        }

        /// <summary>Mismo ajuste al suelo que KartRecovery.MoveTo (privado allí).</summary>
        private void Teleport(Vector3 position, Quaternion rotation)
        {
            float groundDistance = float.MaxValue;
            foreach (RaycastHit hit in Physics.RaycastAll(position + Vector3.up * 10f, Vector3.down, 30f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.rigidbody == body || hit.distance >= groundDistance) continue;
                groundDistance = hit.distance;
                position.y = hit.point.y;
            }
            position += Vector3.up * (restHeight + RespawnDropMeters);

            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.position = position;
            body.rotation = rotation;
            kart.transform.SetPositionAndRotation(position, rotation);
        }

        private void OnDestroy()
        {
            if (recovery != null && recoveryBlocked) recovery.enabled = true;
            if (recovery != null) recovery.Respawned -= OnRecoveryRespawned;
        }
    }
}
