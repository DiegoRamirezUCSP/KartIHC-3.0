using System.Collections.Generic;
using KartIHC.Kart;
using KartIHC.UI;
using UnityEngine;

/// <summary>Builds the police station and four patrols for Escape. Gameplay effects are host-authoritative.</summary>
public sealed class EscapePoliceStationGenerator : MonoBehaviour
{
    private const int PatrolCount = 4;
    private const float ChaseDuration = 15f;
    private const float TrapInterval = 15f;
    private const float RespawnDelay = 20f;
    private const float PatrolSpeed = 7f;
    private const float ChaseSpeed = 8.5f;
    private const float OutsidePatrolRadius = 36f;
    private const float OuterBarrierRadius = 33.5f;
    private const float DoorRouteReachDistance = 1.8f;
    private static EscapePoliceStationGenerator instance;

    [SerializeField, Tooltip("Distancia máxima a la que una patrulla detecta el kart.")] private float visionRange = 38f;
    [SerializeField, Range(30f, 160f), Tooltip("Apertura total del cono frontal. Reducirla aumenta el punto ciego; aumentarla lo reduce.")]
    private float visionAngle = 100f;
    [SerializeField, Range(20f, 31f)] private float patrolRoadRadius = 25.5f;
    private readonly List<EscapePoliceCar> patrols = new List<EscapePoliceCar>();
    private readonly float[] respawnAt = new float[PatrolCount];
    private readonly int[] doorSearchCursor = new int[PatrolCount];
    private readonly int[] activeDoorIds = { -1, -1, -1, -1 };
    // 0 = no door route, 1 = inside approach, 2 = crossing outward,
    // 3 = outside approach, 4 = crossing inward.
    private readonly int[] doorTraversalPhase = new int[PatrolCount];
    private readonly float[] nextDoorSearchAt = new float[PatrolCount];
    private readonly RaycastHit[] obstructionHits = new RaycastHit[16];
    private readonly List<EscapePoliceTrap> traps = new List<EscapePoliceTrap>();
    private KartController kart;
    private EscapeModeNetworkBridge session;
    private bool authoritative;
    private bool soloRules;
    private Vector3[] stationSpawns;
    private readonly HashSet<GameObject> openedBarriers = new HashSet<GameObject>();
    private readonly List<EscapeFinishZone> openedFinishZones = new List<EscapeFinishZone>();
    private readonly List<EscapeDoor> hiddenDoors = new List<EscapeDoor>();

    public static EscapePoliceStationGenerator Instance => instance;
    public bool IsAuthoritative => authoritative;
    public bool IsSoloMode => soloRules;

    public static void StartForEscape(KartController targetKart, EscapeModeNetworkBridge bridge,
        bool hasAuthority, bool useSoloRules)
    {
        if (targetKart == null) return;
        Stop();
        GameObject root = new GameObject("EscapePoliceSystem");
        instance = root.AddComponent<EscapePoliceStationGenerator>();
        instance.Initialize(targetKart, bridge, hasAuthority, useSoloRules);
    }

    public static void Stop()
    {
        if (instance != null)
        {
            EscapePoliceStationGenerator old = instance;
            instance = null;
            old.RestoreSceneObjects();
            Destroy(old.gameObject);
        }
    }

    private void Initialize(KartController targetKart, EscapeModeNetworkBridge bridge,
        bool hasAuthority, bool useSoloRules)
    {
        kart = targetKart;
        session = bridge;
        authoritative = hasAuthority;
        soloRules = useSoloRules;
        if (soloRules) HideEscapeDoors();
        BuildExitSpurAndStation(soloRules);
        SpawnPatrols();
        OpenOuterBoundary();
        EscapeMinimap.RegisterMarkerSource(AddMinimapMarkers);
        string roleLabel = authoritative ? "autoridad del conductor" : "presentación local del hacker";
        Debug.Log($"[EscapePolice] Comisaría activa con {patrols.Count} patrullas ({roleLabel}).");
        EscapeModeHudFeed.Publish($"Comisaría activa · {patrols.Count} patrullas.");
    }

    private void Update()
    {
        for (int i = 0; i < patrols.Count; i++)
        {
            if (patrols[i] == null && Time.time >= respawnAt[i]) SpawnPolice(i);
        }
    }

    public void TickPolice(EscapePoliceCar police)
    {
        if (police == null || kart == null || session == null || session.HasLost || session.HasWon) return;

        bool visible = CanSeeKart(police);
        if (visible)
        {
            if (police.IsChasing || CountChasing() < 2)
                police.BeginOrRefreshChase(ChaseDuration);
            else
                police.StopChase();
        }
        else if (police.IsChasing && Time.time >= police.ChaseUntil)
        {
            police.StopChase();
        }

        police.SelectPatrolRing(IsOutsideOuterBarrier(police.transform.position));

        // Try an open gate first. If the approach is blocked or the gate is
        // closed, move to another gate; when none are available, keep circling.
        bool followingDoor = TryGetDoorRoute(police, out Vector3 destination);
        if (!followingDoor)
        {
            // A chase speeds up the patrol along its assigned lane. It never
            // steers directly at the kart, so police cannot cut across the track.
            Vector3[] route = police.Route;
            int waypoint = police.WaypointIndex;
            if (IsPathBlocked(police, route[waypoint]) &&
                TryFindClearRouteWaypoint(police, out int alternativeWaypoint))
            {
                police.SetWaypointIndex(alternativeWaypoint);
                waypoint = alternativeWaypoint;
            }
            destination = route[waypoint];
        }
        destination.y = police.transform.position.y;
        if (!followingDoor && Vector3.SqrMagnitude(destination - police.transform.position) < 1.6f * 1.6f)
            police.AdvanceWaypoint();
        if (!police.IsChasing && police.ShouldDropTrap) DropPoliceTrap(police);

        float speed = police.IsChasing ? ChaseSpeed : PatrolSpeed;
        Vector3 previous = police.transform.position;
        police.transform.position = Vector3.MoveTowards(previous, destination, speed * Time.deltaTime);
        Vector3 direction = police.transform.position - previous;
        direction.y = 0f;
        if (direction.sqrMagnitude > 0.01f)
            police.transform.rotation = Quaternion.Slerp(police.transform.rotation,
                Quaternion.LookRotation(direction.normalized, Vector3.up), Time.deltaTime * 5f);

        Rigidbody policeBody = police.GetComponent<Rigidbody>();
        policeBody.position = police.transform.position;
        policeBody.rotation = police.transform.rotation;

        if (authoritative && (kart.transform.position - police.transform.position).sqrMagnitude < 2.6f * 2.6f)
            HandlePoliceCollision(police, kart);
    }

    private bool CanSeeKart(EscapePoliceCar police)
    {
        Vector3 eye = police.transform.position + Vector3.up * 1.05f;
        Vector3 target = kart.transform.position + Vector3.up * 0.55f;
        Vector3 delta = target - eye;
        if (delta.sqrMagnitude > visionRange * visionRange) return false;
        Vector3 flatForward = police.transform.forward;
        Vector3 flatDelta = delta.normalized;
        flatForward.y = 0f;
        flatDelta.y = 0f;
        if (flatDelta.sqrMagnitude > 0.001f && Vector3.Angle(flatForward, flatDelta) > visionAngle * 0.5f)
            return false;
        if (Physics.Raycast(eye, delta.normalized, out RaycastHit hit, delta.magnitude, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore))
            return hit.collider.GetComponentInParent<KartController>() == kart;
        return true;
    }

    private int CountChasing()
    {
        int count = 0;
        foreach (EscapePoliceCar police in patrols)
            if (police != null && police.IsChasing) count++;
        return count;
    }

    private Vector3[] BuildPatrolRoute(Vector3 spawnPosition)
    {
        const int egressPoints = 4;
        const int ringSegments = 48;
        Vector3[] patrolRoute = new Vector3[egressPoints + ringSegments];
        float laneX = Mathf.Clamp(spawnPosition.x, -4.5f, 4.5f);
        patrolRoute[0] = new Vector3(laneX, 0.55f, spawnPosition.z - 3.5f);
        patrolRoute[1] = new Vector3(laneX, 0.55f, 40f);
        patrolRoute[2] = new Vector3(laneX, 0.55f, 30f);
        float entryAngle = Mathf.Asin(laneX / patrolRoadRadius);
        float entryZ = Mathf.Cos(entryAngle) * patrolRoadRadius;
        patrolRoute[3] = new Vector3(laneX, 0.55f, entryZ);

        for (int i = 0; i < ringSegments; i++)
        {
            float angle = entryAngle + Mathf.PI * 2f * i / ringSegments;
            patrolRoute[egressPoints + i] = new Vector3(
                Mathf.Sin(angle) * patrolRoadRadius, 0.55f, Mathf.Cos(angle) * patrolRoadRadius);
        }
        return patrolRoute;
    }

    private static Vector3[] BuildOuterPatrolRoute()
    {
        const int segments = 48;
        Vector3[] patrolRoute = new Vector3[segments];
        for (int i = 0; i < segments; i++)
        {
            float angle = Mathf.PI * 2f * i / segments;
            patrolRoute[i] = new Vector3(Mathf.Sin(angle) * OutsidePatrolRadius, 0.55f,
                Mathf.Cos(angle) * OutsidePatrolRadius);
        }
        return patrolRoute;
    }

    private bool TryGetDoorRoute(EscapePoliceCar police, out Vector3 destination)
    {
        destination = default;
        IReadOnlyList<EscapeDoor> doors = EscapeCourseBuilder.Doors;
        int patrolId = police.PatrolId;
        if (doors.Count == 0 || patrolId < 0 || patrolId >= PatrolCount) return false;

        int activeDoorIndex = FindDoorIndexById(doors, activeDoorIds[patrolId]);
        if (doorTraversalPhase[patrolId] != 0)
        {
            if (activeDoorIndex < 0 || !doors[activeDoorIndex].IsOpen)
            {
                AdvanceDoorSearch(police, doors, activeDoorIndex);
                return false;
            }

            EscapeDoor activeDoor = doors[activeDoorIndex];
            if (doorTraversalPhase[patrolId] == 3)
            {
                destination = GetOutsideDoorApproachWaypoint(police, DoorRoutePoint(activeDoor, 3), out bool reachedApproach);
                if (reachedApproach)
                {
                    doorTraversalPhase[patrolId] = 4;
                    destination = DoorRoutePoint(activeDoor, 4);
                    if (IsPathBlocked(police, destination))
                    {
                        AdvanceDoorSearch(police, doors, activeDoorIndex);
                        return false;
                    }
                }
                return true;
            }

            if (doorTraversalPhase[patrolId] == 4)
            {
                destination = DoorRoutePoint(activeDoor, 4);
                if (IsPathBlocked(police, destination))
                {
                    AdvanceDoorSearch(police, doors, activeDoorIndex);
                    return false;
                }
                if ((destination - police.transform.position).sqrMagnitude <= DoorRouteReachDistance * DoorRouteReachDistance)
                {
                    doorTraversalPhase[patrolId] = 0;
                    activeDoorIds[patrolId] = -1;
                    doorSearchCursor[patrolId] = (activeDoorIndex + 1) % doors.Count;
                    nextDoorSearchAt[patrolId] = Time.time + 0.5f;
                    return false;
                }
                return true;
            }

            destination = DoorRoutePoint(activeDoor, doorTraversalPhase[patrolId]);
            if (IsPathBlocked(police, destination))
            {
                AdvanceDoorSearch(police, doors, activeDoorIndex);
                return false;
            }

            if ((destination - police.transform.position).sqrMagnitude <= DoorRouteReachDistance * DoorRouteReachDistance)
            {
                if (doorTraversalPhase[patrolId] == 1)
                    doorTraversalPhase[patrolId] = 2;
                else
                {
                    doorTraversalPhase[patrolId] = 0;
                    activeDoorIds[patrolId] = -1;
                    doorSearchCursor[patrolId] = (activeDoorIndex + 1) % doors.Count;
                    nextDoorSearchAt[patrolId] = Time.time + 0.5f;
                    return false;
                }

                destination = DoorRoutePoint(activeDoor, doorTraversalPhase[patrolId]);
                if (IsPathBlocked(police, destination))
                {
                    AdvanceDoorSearch(police, doors, activeDoorIndex);
                    return false;
                }
            }
            return true;
        }

        if (Time.time < nextDoorSearchAt[patrolId]) return false;
        for (int offset = 0; offset < doors.Count; offset++)
        {
            int candidateIndex = (doorSearchCursor[patrolId] + offset) % doors.Count;
            EscapeDoor candidate = doors[candidateIndex];
            if (candidate == null || !candidate.IsOpen) continue;

            bool outside = IsOutsideOuterBarrier(police.transform.position);
            int phase = outside ? 3 : 1;
            Vector3 approach = DoorRoutePoint(candidate, phase);
            if (!outside && IsPathBlocked(police, approach)) continue;

            doorSearchCursor[patrolId] = candidateIndex;
            activeDoorIds[patrolId] = candidate.Id;
            doorTraversalPhase[patrolId] = phase;
            if (outside)
                destination = GetOutsideDoorApproachWaypoint(police, approach, out _);
            else
                destination = approach;
            return true;
        }

        // Keep patrolling while the gates are closed, then scan again shortly.
        nextDoorSearchAt[patrolId] = Time.time + 1f;
        return false;
    }

    private static int FindDoorIndexById(IReadOnlyList<EscapeDoor> doors, int doorId)
    {
        for (int i = 0; i < doors.Count; i++)
            if (doors[i] != null && doors[i].Id == doorId) return i;
        return -1;
    }

    private Vector3 GetOutsideDoorApproachWaypoint(EscapePoliceCar police, Vector3 doorPoint,
        out bool reachedApproach)
    {
        Vector3[] route = police.Route;
        int targetIndex = 0;
        float nearestTarget = float.PositiveInfinity;
        for (int i = 0; i < route.Length; i++)
        {
            Vector3 delta = route[i] - doorPoint;
            delta.y = 0f;
            if (delta.sqrMagnitude >= nearestTarget) continue;
            nearestTarget = delta.sqrMagnitude;
            targetIndex = i;
        }

        Vector3 toDoor = doorPoint - police.transform.position;
        toDoor.y = 0f;
        if (toDoor.sqrMagnitude <= 2.2f * 2.2f)
        {
            reachedApproach = true;
            return doorPoint;
        }

        reachedApproach = false;
        int current = police.WaypointIndex;
        int forwardDistance = (targetIndex - current + route.Length) % route.Length;
        int reverseDistance = (current - targetIndex + route.Length) % route.Length;
        int next = forwardDistance <= reverseDistance
            ? (current + 1) % route.Length
            : (current - 1 + route.Length) % route.Length;
        if (IsPathBlocked(police, route[next]) && TryFindClearRouteWaypoint(police, out int alternative))
            next = alternative;
        police.SetWaypointIndex(next);
        return route[next];
    }

    private static Vector3 DoorRoutePoint(EscapeDoor door, int phase)
    {
        Vector3 center = door.transform.position;
        Vector3 outward = new Vector3(center.x, 0f, center.z).normalized;
        Vector3 groundCenter = new Vector3(center.x, 0.55f, center.z);
        return phase switch
        {
            1 => groundCenter - outward * 5.5f,
            2 => groundCenter + outward * 4f,
            3 => groundCenter + outward * 4f,
            _ => groundCenter - outward * 5.5f
        };
    }

    private void AdvanceDoorSearch(EscapePoliceCar police, IReadOnlyList<EscapeDoor> doors, int currentDoorIndex)
    {
        int patrolId = police.PatrolId;
        doorTraversalPhase[patrolId] = 0;
        activeDoorIds[patrolId] = -1;
        doorSearchCursor[patrolId] = currentDoorIndex >= 0
            ? (currentDoorIndex + 1) % doors.Count
            : (doorSearchCursor[patrolId] + 1) % doors.Count;
        nextDoorSearchAt[patrolId] = Time.time + 0.35f;
    }

    private bool TryFindClearRouteWaypoint(EscapePoliceCar police, out int waypointIndex)
    {
        Vector3[] route = police.Route;
        int current = police.WaypointIndex;
        for (int offset = 1; offset < route.Length; offset++)
        {
            int forward = (current + offset) % route.Length;
            if (!IsPathBlocked(police, route[forward]))
            {
                waypointIndex = forward;
                return true;
            }

            int reverse = (current - offset + route.Length) % route.Length;
            if (!IsPathBlocked(police, route[reverse]))
            {
                waypointIndex = reverse;
                return true;
            }
        }

        waypointIndex = current;
        return false;
    }

    private bool IsPathBlocked(EscapePoliceCar police, Vector3 destination)
    {
        Vector3 origin = police.transform.position + Vector3.up * 0.65f;
        Vector3 target = destination + Vector3.up * 0.65f;
        Vector3 delta = target - origin;
        float distance = delta.magnitude;
        if (distance < 0.1f) return false;

        int hitCount = Physics.SphereCastNonAlloc(origin, 0.55f, delta / distance, obstructionHits,
            distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hitCount; i++)
        {
            Collider hitCollider = obstructionHits[i].collider;
            if (hitCollider == null || hitCollider.GetComponentInParent<EscapePoliceCar>() == police ||
                hitCollider.GetComponentInParent<KartController>() == kart)
                continue;
            return true;
        }
        return false;
    }

    private static bool IsOutsideOuterBarrier(Vector3 position)
    {
        return new Vector2(position.x, position.z).magnitude > OuterBarrierRadius;
    }

    private void SpawnPatrols()
    {
        stationSpawns = new Vector3[PatrolCount];
        patrols.Clear();
        for (int i = 0; i < PatrolCount; i++)
        {
            patrols.Add(null);
        }
        for (int i = 0; i < PatrolCount; i++)
        {
            float x = (i - 1.5f) * 2.4f;
            stationSpawns[i] = new Vector3(x, 0.55f, 56f);
            SpawnPolice(i);
        }
    }

    private void SpawnPolice(int id)
    {
        GameObject root = new GameObject($"PolicePatrol_{id + 1}");
        root.transform.SetParent(transform, true);
        root.transform.position = stationSpawns[id];
        BoxCollider bodyCollider = root.AddComponent<BoxCollider>();
        bodyCollider.isTrigger = true;
        bodyCollider.center = new Vector3(0f, 0.65f, 0f);
        bodyCollider.size = new Vector3(2.2f, 1.4f, 4f);
        Rigidbody body = root.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        EscapePoliceCar police = root.AddComponent<EscapePoliceCar>();
        CreatePoliceVisual(root.transform);
        police.Initialize(this, id, BuildPatrolRoute(stationSpawns[id]), BuildOuterPatrolRoute(),
            0, stationSpawns[id], true);
        doorSearchCursor[id] = id % Mathf.Max(1, EscapeCourseBuilder.Doors.Count);
        activeDoorIds[id] = -1;
        doorTraversalPhase[id] = 0;
        nextDoorSearchAt[id] = Time.time + id * 0.2f;
        patrols[id] = police;
        respawnAt[id] = float.PositiveInfinity;
    }

    private void CreatePoliceVisual(Transform parent)
    {
        CreateBox(parent, "PoliceBody", new Vector3(0f, 0.62f, 0f), new Vector3(2f, 0.62f, 3.7f),
            new Color(0.83f, 0.86f, 0.9f));
        CreateBox(parent, "PoliceHood", new Vector3(0f, 0.96f, 1.04f), new Vector3(1.65f, 0.12f, 0.9f),
            new Color(0.05f, 0.16f, 0.48f));
        CreateBox(parent, "PoliceRoof", new Vector3(0f, 1.05f, -0.08f), new Vector3(1.5f, 0.5f, 1.75f),
            new Color(0.08f, 0.18f, 0.48f));
        CreateBox(parent, "PoliceLightBar", new Vector3(0f, 1.36f, -0.08f), new Vector3(1.4f, 0.16f, 0.34f),
            new Color(0.9f, 0.08f, 0.06f));
        CreateBox(parent, "PoliceFrontBumper", new Vector3(0f, 0.36f, 1.87f), new Vector3(2.05f, 0.18f, 0.18f),
            new Color(0.1f, 0.1f, 0.12f));
    }

    private void BuildExitSpurAndStation(bool includeSoloExit)
    {
        GameObject city = new GameObject("EscapePoliceStationArea");
        city.transform.SetParent(transform, true);
        CreateBox(city.transform, "ExitRoad", new Vector3(0f, -0.12f, 44f), new Vector3(12f, 0.2f, 32f),
            new Color(0.17f, 0.18f, 0.2f));
        CreateBox(city.transform, "ExitRoadLineLeft", new Vector3(-2f, 0.0f, 44f), new Vector3(0.15f, 0.025f, 28f),
            new Color(0.9f, 0.83f, 0.4f));
        CreateBox(city.transform, "ExitRoadLineRight", new Vector3(2f, 0.0f, 44f), new Vector3(0.15f, 0.025f, 28f),
            new Color(0.9f, 0.83f, 0.4f));
        CreateBox(city.transform, "TestAreaWallLeft", new Vector3(-6.35f, 1.1f, 44f), new Vector3(0.5f, 2.2f, 28f),
            new Color(0.34f, 0.35f, 0.38f));
        CreateBox(city.transform, "TestAreaWallRight", new Vector3(6.35f, 1.1f, 44f), new Vector3(0.5f, 2.2f, 28f),
            new Color(0.34f, 0.35f, 0.38f));
        CreateBox(city.transform, "PoliceStationBuilding", new Vector3(0f, 3.4f, 66f), new Vector3(22f, 6.8f, 13f),
            new Color(0.66f, 0.69f, 0.73f));
        CreateBox(city.transform, "PoliceStationBlueFacade", new Vector3(0f, 2.0f, 59.35f), new Vector3(22f, 2.6f, 0.3f),
            new Color(0.04f, 0.16f, 0.43f));
        CreateBox(city.transform, "PoliceStationGarageLeft", new Vector3(-6.2f, 1.8f, 59.1f), new Vector3(7.2f, 3.6f, 0.35f),
            new Color(0.12f, 0.14f, 0.17f));
        CreateBox(city.transform, "PoliceStationGarageRight", new Vector3(6.2f, 1.8f, 59.1f), new Vector3(7.2f, 3.6f, 0.35f),
            new Color(0.12f, 0.14f, 0.17f));
        CreateText(city.transform, "POLICIA · GENERADOR", new Vector3(0f, 5.4f, 59.0f), 0.55f,
            new Color(0.96f, 0.97f, 1f));
        if (includeSoloExit)
        {
            CreateBox(city.transform, "ExitFinishStripe", new Vector3(0f, 0.02f, 48f), new Vector3(11.8f, 0.05f, 0.65f),
                new Color(1f, 0.7f, 0.06f));
            GameObject finish = new GameObject("EscapeSoloExitFinish");
            finish.transform.SetParent(city.transform, true);
            finish.transform.position = new Vector3(0f, 1f, 49f);
            BoxCollider trigger = finish.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(11.5f, 3f, 2f);
            finish.AddComponent<EscapeFinishZone>();

            // Solo uses this test lane; cooperative Escape keeps its authored finish.
            EscapeFinishZone[] authoredFinishZones = FindObjectsByType<EscapeFinishZone>(FindObjectsSortMode.None);
            foreach (EscapeFinishZone authoredFinish in authoredFinishZones)
            {
                if (authoredFinish.gameObject.name != "EscapeFinishZone") continue;
                authoredFinish.enabled = false;
                openedFinishZones.Add(authoredFinish);
            }
        }

    }

    private void OpenOuterBoundary()
    {
        // Open only the two barrier segments that cover the north exit.
        foreach (GameObject wall in FindObjectsByType<GameObject>(FindObjectsSortMode.None))
        {
            Vector3 position = wall.transform.position;
            if (!wall.name.Contains("OuterBarrier") || position.z < 25f) continue;
            float angle = Mathf.Atan2(position.z, position.x) * Mathf.Rad2Deg;
            if (angle < 0f) angle += 360f;
            if (angle > 55f && angle < 125f)
            {
                openedBarriers.Add(wall);
                wall.SetActive(false);
            }
        }
    }

    private void OnDestroy()
    {
        EscapeMinimap.UnregisterMarkerSource(AddMinimapMarkers);
        RestoreSceneObjects();
        if (instance == this) instance = null;
    }

    private void AddMinimapMarkers(List<EscapeMinimap.Marker> output)
    {
        foreach (EscapePoliceCar patrol in patrols)
        {
            if (patrol == null) continue;
            output.Add(new EscapeMinimap.Marker
            {
                Position = patrol.transform.position,
                Shape = EscapeMinimap.MarkerShape.Dot,
                Color = patrol.IsChasing ? new Color(1f, 0.12f, 0.08f) : new Color(0.9f, 0.55f, 0.12f),
                SizePixels = 9f
            });
        }

        foreach (EscapePoliceTrap trap in traps)
        {
            if (trap == null) continue;
            output.Add(new EscapeMinimap.Marker
            {
                Position = trap.transform.position,
                Shape = EscapeMinimap.MarkerShape.Checker,
                Color = new Color(1f, 0.2f, 0.08f),
                SizePixels = 7f
            });
        }
    }

    private void RestoreSceneObjects()
    {
        foreach (GameObject wall in openedBarriers)
            if (wall != null) wall.SetActive(true);
        foreach (EscapeFinishZone finish in openedFinishZones)
            if (finish != null) finish.enabled = true;
        foreach (EscapeDoor door in hiddenDoors)
            if (door != null) door.gameObject.SetActive(true);
        openedBarriers.Clear();
        openedFinishZones.Clear();
        hiddenDoors.Clear();
    }

    private void HideEscapeDoors()
    {
        foreach (EscapeDoor door in EscapeCourseBuilder.Doors)
        {
            if (door == null || !door.gameObject.activeSelf) continue;
            hiddenDoors.Add(door);
            door.gameObject.SetActive(false);
        }
    }

    private static void CreateBox(Transform parent, string objectName, Vector3 position, Vector3 scale, Color color)
    {
        GameObject item = GameObject.CreatePrimitive(PrimitiveType.Cube);
        item.name = objectName;
        item.transform.SetParent(parent, false);
        item.transform.localPosition = position;
        item.transform.localScale = scale;
        Renderer renderer = item.GetComponent<Renderer>();
        if (renderer != null) renderer.material.color = color;
        Collider collider = item.GetComponent<Collider>();
        if (collider != null) Destroy(collider);
    }

    private static void CreateText(Transform parent, string value, Vector3 position, float size, Color color)
    {
        GameObject item = new GameObject("PoliceStationSign");
        item.transform.SetParent(parent, false);
        item.transform.localPosition = position;
        TextMesh text = item.AddComponent<TextMesh>();
        text.text = value;
        text.anchor = TextAnchor.MiddleCenter;
        text.alignment = TextAlignment.Center;
        text.fontSize = 64;
        text.characterSize = size;
        text.color = color;
    }

    private void DropPoliceTrap(EscapePoliceCar police)
    {
        GameObject trapObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        trapObject.name = $"PoliceTrap_{police.PatrolId + 1}";
        trapObject.transform.position = police.transform.position - police.transform.forward * 2.6f + Vector3.up * 0.28f;
        trapObject.transform.localScale = Vector3.one * 0.65f;
        Renderer renderer = trapObject.GetComponent<Renderer>();
        if (renderer != null) renderer.material.color = new Color(0.95f, 0.18f, 0.06f);
        SphereCollider collider = trapObject.GetComponent<SphereCollider>();
        collider.isTrigger = true;
        Rigidbody body = trapObject.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        EscapePoliceTrap trap = trapObject.AddComponent<EscapePoliceTrap>();
        trap.Initialize(this, 45f);
        traps.Add(trap);
        if (traps.Count > 16)
        {
            EscapePoliceTrap oldest = traps[0];
            traps.RemoveAt(0);
            if (oldest != null) Destroy(oldest.gameObject);
        }
        police.TrapDropped(TrapInterval);
    }

    public void HandlePoliceCollision(EscapePoliceCar police, KartController collidedKart)
    {
        if (!authoritative || police == null || collidedKart == null || session == null || session.HasLost || session.HasWon) return;
        if (collidedKart.IsShielded)
        {
            EliminatePolice(police, "El escudo eliminó una patrulla.");
            return;
        }
        session.ReportDriverLost("El kart chocó con un auto policial.");
    }

    public void EliminatePolice(EscapePoliceCar police, string message)
    {
        if (!authoritative || police == null || police.PatrolId < 0 || police.PatrolId >= patrols.Count) return;
        int id = police.PatrolId;
        if (patrols[id] != police) return;
        patrols[id] = null;
        respawnAt[id] = Time.time + RespawnDelay;
        PublishMessage(message + " Reaparecerá en la comisaría en 20 s.");
        Destroy(police.gameObject);
    }

    public void NotifyKartTrapped(float seconds)
    {
        PublishMessage($"¡Trampa policial! Kart inmovilizado por {seconds:0} s.");
    }

    public void PublishMessage(string message) => EscapeModeHudFeed.Publish(message);
}
