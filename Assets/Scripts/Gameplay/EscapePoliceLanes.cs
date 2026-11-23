using System.Collections.Generic;
using KartIHC.Gameplay.Hazards;
using KartIHC.Kart;
using UnityEngine;

namespace KartIHC.Gameplay
{
    /// <summary>
    /// Persecución por carriles y patrullas en la ruta por la ciudad. EscapePoliceStationGenerator
    /// (solo en el conductor, que es la autoridad) le pregunta en cada TickPolice; si devuelve false,
    /// la patrulla sigue con su lógica de siempre (anillos, puertas, salida de la comisaría).
    ///
    /// Toma el control en tres casos:
    /// - Persecución en el anillo: avanza por el carril interior (r 25.5) en el sentido más corto hacia
    ///   el kart y, ya cerca, va directa a él. Nunca cruza el centro.
    /// - Servicio en la ruta: con el kart en la ruta, hasta MaxRoutePatrols patrullas (las que persiguen
    ///   primero) entran por la salida norte y avanzan por la ruta hacia el kart.
    /// - Regreso: al terminar el servicio vuelven por la ruta al anillo y retoman su recorrido en el
    ///   punto más cercano.
    /// Ante una barrera o puerta cerrada se detienen StopBeforeGate metros antes; solo una patrulla por
    /// obstáculo (las demás dejan el servicio). Se mueven sin física, como siempre: no hay red nueva.
    /// </summary>
    public static class EscapePoliceLanes
    {
        // ===== Valores ajustables =====
        /// <summary>Velocidad máxima del kart sin turbo (m/s). Estimada: 16–20 m/s (par 2×1500 N·m, radio 0.62 m,
        /// 800 kg, amortiguamiento 0.3). Mídela en juego: la consola muestra "[EscapePolice] Velocidad máxima…".</summary>
        public const float KartReferenceSpeed = 18f;
        public const float ChaseSpeedFactor = 0.85f;
        public const float ChaseSpeed = KartReferenceSpeed * ChaseSpeedFactor; // la usa también EscapePoliceStationGenerator
        public const float RoutePatrolSpeed = 7f;      // como PatrolSpeed de las patrullas normales
        public const int MaxRoutePatrols = 2;
        public const float InnerLaneRadius = 25.5f;    // carril interior de las patrullas (patrolRoadRadius)
        public const float OuterBarrierRadius = 33.5f; // como EscapePoliceStationGenerator
        public const float LaneStepDegrees = 8f;
        public const float DirectApproachDegrees = 10f; // en el anillo, más cerca que esto va directa al kart
        public const float DirectApproachMeters = 8f;   // en la ruta, ídem
        public const float RouteStepMeters = 5f;
        public const float StopBeforeGate = 4f;         // metros antes de barrera/puerta cerrada (ruta)
        public const float StopBeforeRingDoorDegrees = 8f;
        public const float RouteCorridorMargin = 2f;
        public const float KartOnRouteFrom = 4f;        // el kart cuenta como "en la ruta" desde aquí
        public const float RingEntryArriveMeters = 2.5f;
        public const float RouteFromRadius = 28f;       // desde aquí (salida norte) ya sigue la ruta
        private const float StaleSeconds = 1f;
        private const float MaxPlausibleSpeed = 60f;    // descarta teletransportes al medir

        private enum Mode { None, Ring, RouteDuty, Returning }

        private sealed class State
        {
            public Mode Mode;
            public float LastTick;
        }

        private static readonly Dictionary<EscapePoliceCar, State> states = new Dictionary<EscapePoliceCar, State>();
        private static readonly Dictionary<string, EscapePoliceCar> gateClaims = new Dictionary<string, EscapePoliceCar>();
        private static readonly List<EscapePoliceCar> scratch = new List<EscapePoliceCar>();

        /// <summary>
        /// Destino y velocidad para esta patrulla si la controlan los carriles; false = lógica original.
        /// </summary>
        public static bool TryGetDestination(EscapePoliceCar police, KartController kart, out Vector3 destination, out float speed)
        {
            destination = default;
            speed = 0f;
            if (police == null || kart == null) return false;
            MeasureKart(kart);
            Cleanup();

            State state = GetState(police);
            Mode previous = state.Mode;
            ReleaseClaims(police); // se vuelve a reservar en este mismo turno si sigue esperando
            Mode mode = Decide(police, kart, state, out destination, out speed);
            state.Mode = mode;
            if (mode == Mode.None)
            {
                // Al soltar el control, retomar el recorrido propio en el punto más cercano (sin cortar el centro).
                if (previous != Mode.None) ResyncWaypoint(police);
                return false;
            }
            return true;
        }

        private static Mode Decide(EscapePoliceCar police, KartController kart, State state, out Vector3 destination, out float speed)
        {
            destination = default;
            speed = police.IsChasing ? ChaseSpeed : RoutePatrolSpeed;
            EscapeRouteDefinition route = EscapeRouteDefinition.Active;
            Vector3 p = police.transform.position;
            Vector3 k = kart.transform.position;
            float policeRadius = Flat(p).magnitude;
            float dp = 0f, dk = 0f;
            bool policeOnRoute = route != null && route.Contains(p, RouteCorridorMargin, out dp);
            bool kartOnRoute = route != null && route.Contains(k, 1f, out dk) && dk >= KartOnRouteFrom;
            bool insideRing = policeRadius <= OuterBarrierRadius;

            // 1) Servicio en la ruta (desde el anillo interior, el carril de salida o la propia ruta).
            if (kartOnRoute && (insideRing || policeOnRoute) && WantsRouteDuty(police, state))
            {
                if (policeOnRoute && (dp > 0.5f || policeRadius >= RouteFromRadius))
                {
                    if (!AlongRoute(route, police, dp, dk, k, out destination)) return Mode.Returning;
                    return Mode.RouteDuty;
                }
                // Todavía en el anillo: por el carril interior hasta la salida norte (90°) y hacia la ruta.
                destination = TowardAngle(p, 90f, out bool arrived);
                if (arrived) destination = route.PointAt(2f);
                return Mode.RouteDuty;
            }

            // 2) Regreso desde la ruta al anillo.
            if (state.Mode == Mode.RouteDuty || state.Mode == Mode.Returning)
            {
                speed = RoutePatrolSpeed;
                if (route != null && policeOnRoute && dp > RingEntryArriveMeters)
                {
                    if (!AlongRoute(route, police, dp, 0f, null, out destination)) destination = p; // espera
                    return Mode.Returning;
                }
                Vector3 entry = PointOnRing(90f, InnerLaneRadius);
                if ((Flat(entry) - Flat(p)).magnitude > RingEntryArriveMeters && insideRing)
                {
                    destination = entry;
                    return Mode.Returning;
                }
                return Mode.None;
            }

            // 3) Persecución en el anillo (solo patrullas dentro del anillo y kart en el anillo).
            if (police.IsChasing && insideRing && !policeOnRoute && !kartOnRoute &&
                Flat(k).magnitude <= OuterBarrierRadius)
            {
                speed = ChaseSpeed;
                destination = ChaseOnRing(police, p, k);
                return Mode.Ring;
            }
            return Mode.None;
        }

        // ---------- Ruta ----------

        private static bool WantsRouteDuty(EscapePoliceCar police, State state)
        {
            if (state.Mode == Mode.RouteDuty) return true;
            if (state.Mode == Mode.Returning) return false;
            int onDuty = 0;
            foreach (KeyValuePair<EscapePoliceCar, State> pair in states)
                if (pair.Key != null && pair.Value.Mode == Mode.RouteDuty) onDuty++;
            return onDuty < MaxRoutePatrols;
        }

        /// <summary>
        /// Avanza por la ruta de dp hacia target. Se detiene antes de barreras/puertas cerradas; si otra
        /// patrulla ya espera en ese obstáculo devuelve false (esta deja el servicio).
        /// </summary>
        private static bool AlongRoute(EscapeRouteDefinition route, EscapePoliceCar police, float dp, float target,
            Vector3? kartPosition, out Vector3 destination)
        {
            float direction = Mathf.Sign(target - dp);
            float limit = target;
            foreach ((string key, float gate) in ClosedRouteGates(route))
            {
                bool between = direction > 0f ? gate > dp && gate <= target + 0.5f : gate < dp && gate >= target - 0.5f;
                if (!between) continue;
                float stop = gate - direction * StopBeforeGate;
                if (direction > 0f ? stop < limit : stop > limit)
                {
                    if (!Claim(key, police)) { destination = police.transform.position; return false; }
                    limit = stop;
                }
            }

            if (kartPosition.HasValue && Mathf.Abs(limit - target) < 0.01f && Mathf.Abs(target - dp) <= DirectApproachMeters)
            {
                destination = kartPosition.Value; // mismo tramo de calle: directa al kart
                return true;
            }
            float next = dp + direction * Mathf.Min(RouteStepMeters, Mathf.Abs(limit - dp));
            destination = route.PointAt(next);
            return true;
        }

        private static IEnumerable<(string key, float distance)> ClosedRouteGates(EscapeRouteDefinition route)
        {
            EscapeHazardDirector hazards = EscapeHazardDirector.Instance;
            if (hazards != null)
                foreach (EscapeHackableObstacle obstacle in hazards.Obstacles)
                    if (obstacle != null && obstacle.gameObject.activeInHierarchy && !obstacle.IsOpen &&
                        route.Contains(obstacle.transform.position, 1f, out float d) && d >= KartOnRouteFrom)
                        yield return ("obstacle:" + obstacle.Id, d);
            foreach (EscapeDoor door in EscapeCourseBuilder.Doors)
                if (door != null && door.gameObject.activeInHierarchy && !door.IsOpen &&
                    route.Contains(door.transform.position, 1f, out float d) && d >= KartOnRouteFrom)
                    yield return ("door:" + door.Id, d);
        }

        // ---------- Anillo ----------

        private static Vector3 ChaseOnRing(EscapePoliceCar police, Vector3 p, Vector3 k)
        {
            float ap = EscapeTrackGeometry.AngleOf(p);
            float ak = EscapeTrackGeometry.AngleOf(k);
            float delta = Mathf.DeltaAngle(ap, ak);
            float sign = Mathf.Sign(delta);
            float limitDelta = delta;

            // Puertas del anillo cerradas en el camino: esperar antes (una patrulla por puerta).
            foreach (EscapeDoor door in EscapeCourseBuilder.Doors)
            {
                if (door == null || !door.gameObject.activeInHierarchy || door.IsOpen) continue;
                if (Flat(door.transform.position).magnitude > OuterBarrierRadius) continue; // puerta de la ruta
                float gateDelta = Mathf.DeltaAngle(ap, EscapeTrackGeometry.AngleOf(door.transform.position));
                if (Mathf.Sign(gateDelta) != sign || Mathf.Abs(gateDelta) > Mathf.Abs(limitDelta)) continue;
                if (!Claim("door:" + door.Id, police)) return p; // otra ya espera: quieta
                limitDelta = gateDelta - sign * StopBeforeRingDoorDegrees;
                if (Mathf.Sign(limitDelta) != sign) return p;    // ya está pegada a la puerta
            }

            bool sameSector = Mathf.Abs(limitDelta - delta) < 0.01f && Mathf.Abs(delta) <= DirectApproachDegrees;
            if (sameSector) return k; // cerca y sin puertas cerradas: directa al kart (dentro de la pista)
            float step = sign * Mathf.Min(LaneStepDegrees, Mathf.Abs(limitDelta));
            return PointOnRing(ap + step, InnerLaneRadius);
        }

        private static Vector3 TowardAngle(Vector3 p, float angle, out bool arrived)
        {
            float ap = EscapeTrackGeometry.AngleOf(p);
            float delta = Mathf.DeltaAngle(ap, angle);
            arrived = Mathf.Abs(delta) <= 4f;
            float step = Mathf.Sign(delta) * Mathf.Min(LaneStepDegrees, Mathf.Abs(delta));
            return PointOnRing(ap + step, InnerLaneRadius);
        }

        /// <summary>Retoma el recorrido propio de la patrulla en el waypoint más cercano.</summary>
        private static void ResyncWaypoint(EscapePoliceCar police)
        {
            Vector3[] route = police.Route;
            if (route == null || route.Length == 0) return;
            int nearest = 0;
            float best = float.PositiveInfinity;
            for (int i = 0; i < route.Length; i++)
            {
                float sq = (Flat(route[i]) - Flat(police.transform.position)).sqrMagnitude;
                if (sq < best) { best = sq; nearest = i; }
            }
            police.SetWaypointIndex(nearest + 1);
        }

        // ---------- Reservas de obstáculos ----------

        private static bool Claim(string key, EscapePoliceCar police)
        {
            if (gateClaims.TryGetValue(key, out EscapePoliceCar owner) && owner != null && owner != police &&
                states.TryGetValue(owner, out State ownerState) && Time.time - ownerState.LastTick < StaleSeconds &&
                ownerState.Mode != Mode.None)
                return false;
            gateClaims[key] = police;
            return true;
        }

        private static readonly List<string> releasedKeys = new List<string>();

        private static void ReleaseClaims(EscapePoliceCar police)
        {
            releasedKeys.Clear();
            foreach (KeyValuePair<string, EscapePoliceCar> pair in gateClaims)
                if (pair.Value == police || pair.Value == null) releasedKeys.Add(pair.Key);
            foreach (string key in releasedKeys) gateClaims.Remove(key);
        }

        // ---------- Estado ----------

        private static State GetState(EscapePoliceCar police)
        {
            if (!states.TryGetValue(police, out State state))
            {
                state = new State();
                states[police] = state;
            }
            state.LastTick = Time.time;
            return state;
        }

        private static void Cleanup()
        {
            scratch.Clear();
            foreach (KeyValuePair<EscapePoliceCar, State> pair in states)
                if (pair.Key == null || Time.time - pair.Value.LastTick > StaleSeconds * 5f) scratch.Add(pair.Key);
            foreach (EscapePoliceCar dead in scratch) states.Remove(dead);
        }

        // ---------- Medición de la velocidad del kart ----------

        private static int measuredFrame = -1;
        private static Vector3 lastKartPosition;
        private static float lastKartTime = -1f;
        private static float maxKartSpeed;
        private static float loggedKartSpeed;

        private static void MeasureKart(KartController kart)
        {
            if (measuredFrame == Time.frameCount) return;
            measuredFrame = Time.frameCount;
            Vector3 position = kart.transform.position;
            float now = Time.time;
            if (lastKartTime >= 0f && now > lastKartTime && !kart.IsTurboActive)
            {
                float speed = (Flat(position) - Flat(lastKartPosition)).magnitude / (now - lastKartTime);
                if (speed < MaxPlausibleSpeed && speed > maxKartSpeed) maxKartSpeed = speed;
                if (maxKartSpeed >= loggedKartSpeed + 0.5f)
                {
                    loggedKartSpeed = maxKartSpeed;
                    Debug.Log($"[EscapePolice] Velocidad máxima medida del kart (sin turbo): {maxKartSpeed:0.0} m/s " +
                              $"({maxKartSpeed * 3.6f:0} km/h). Persecución actual: {ChaseSpeed:0.0} m/s " +
                              $"= {ChaseSpeedFactor:P0} de KartReferenceSpeed {KartReferenceSpeed:0.0}.");
                }
            }
            lastKartPosition = position;
            lastKartTime = now;
        }

        // ---------- Utilidades ----------

        private static Vector3 PointOnRing(float degrees, float radius) => EscapeTrackGeometry.PointAt(degrees, radius, 0.55f);

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
