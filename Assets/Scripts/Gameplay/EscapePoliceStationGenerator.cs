using System.Collections.Generic;
using KartIHC.Kart;
using UnityEngine;

/// <summary>Builds a local police station, four patrol cars and an exit test spur for Escape Solo.</summary>
public sealed class EscapePoliceStationGenerator : MonoBehaviour
{
    private const int PatrolCount = 4;
    private const float ChaseDuration = 15f;
    private const float TrapInterval = 15f;
    private const float RespawnDelay = 20f;
    private const float PatrolSpeed = 7f;
    private const float ChaseSpeed = 10f;
    private static EscapePoliceStationGenerator instance;

    [SerializeField] private float visionRange = 38f;
    [SerializeField, Range(15f, 160f)] private float visionAngle = 100f;
    private readonly List<EscapePoliceCar> patrols = new List<EscapePoliceCar>();
    private readonly float[] respawnAt = new float[PatrolCount];
    private readonly List<EscapePoliceTrap> traps = new List<EscapePoliceTrap>();
    private KartController kart;
    private EscapeModeNetworkBridge session;
    private Vector3[] route;
    private Vector3[] stationSpawns;
    private readonly HashSet<GameObject> openedBarriers = new HashSet<GameObject>();
    private readonly List<EscapeFinishZone> openedFinishZones = new List<EscapeFinishZone>();
    private readonly List<EscapeDoor> hiddenDoors = new List<EscapeDoor>();

    public static EscapePoliceStationGenerator Instance => instance;

    public static void StartForSolo(KartController targetKart, EscapeModeNetworkBridge bridge)
    {
        if (targetKart == null) return;
        Stop();
        GameObject root = new GameObject("EscapeSoloPoliceSystem");
        instance = root.AddComponent<EscapePoliceStationGenerator>();
        instance.Initialize(targetKart, bridge);
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

    private void Initialize(KartController targetKart, EscapeModeNetworkBridge bridge)
    {
        kart = targetKart;
        session = bridge;
        HideEscapeDoors();
        BuildExitSpurAndStation();
        BuildPatrolRoute();
        SpawnPatrols();
        OpenOuterBoundary();
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

        Vector3 destination;
        if (police.IsChasing)
        {
            destination = kart.transform.position;
            destination.y = police.transform.position.y;
        }
        else
        {
            destination = route[police.WaypointIndex];
            destination.y = police.transform.position.y;
            if (Vector3.SqrMagnitude(destination - police.transform.position) < 2.2f * 2.2f)
                police.AdvanceWaypoint();
            if (police.ShouldDropTrap) DropPoliceTrap(police);
        }

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

        if ((kart.transform.position - police.transform.position).sqrMagnitude < 2.6f * 2.6f)
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

    private void BuildPatrolRoute()
    {
        const float radius = 24f;
        route = new Vector3[8];
        for (int i = 0; i < route.Length; i++)
        {
            float angle = Mathf.PI * 2f * i / route.Length;
            route[i] = new Vector3(Mathf.Sin(angle) * radius, 0.6f, Mathf.Cos(angle) * radius);
        }
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
            float x = (i - 1.5f) * 2.8f;
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
        police.Initialize(this, id, route, id * 2, stationSpawns[id], true);
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

    private void BuildExitSpurAndStation()
    {
        GameObject city = new GameObject("EscapeSoloCityTestArea");
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
        CreateBox(city.transform, "ExitFinishStripe", new Vector3(0f, 0.02f, 48f), new Vector3(11.8f, 0.05f, 0.65f),
            new Color(1f, 0.7f, 0.06f));
        GameObject finish = new GameObject("EscapeSoloExitFinish");
        finish.transform.SetParent(city.transform, true);
        finish.transform.position = new Vector3(0f, 1f, 49f);
        BoxCollider trigger = finish.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.size = new Vector3(11.5f, 3f, 2f);
        finish.AddComponent<EscapeFinishZone>();

        // The authored circular finish remains active for network Escape;
        // in Solo the player must reach this separate exit lane to win.
        EscapeFinishZone[] authoredFinishZones = FindObjectsByType<EscapeFinishZone>(FindObjectsSortMode.None);
        foreach (EscapeFinishZone authoredFinish in authoredFinishZones)
        {
            if (authoredFinish.gameObject.name != "EscapeFinishZone") continue;
            authoredFinish.enabled = false;
            openedFinishZones.Add(authoredFinish);
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
        RestoreSceneObjects();
        if (instance == this) instance = null;
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
        if (police == null || collidedKart == null || session == null || session.HasLost || session.HasWon) return;
        if (collidedKart.IsShielded)
        {
            EliminatePolice(police, "El escudo eliminó una patrulla.");
            return;
        }
        session.ReportDriverLost("El kart chocó con un auto policial.");
    }

    public void EliminatePolice(EscapePoliceCar police, string message)
    {
        if (police == null || police.PatrolId < 0 || police.PatrolId >= patrols.Count) return;
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
