using UnityEngine;

/// <summary>One generated police patrol vehicle for Escape.</summary>
public sealed class EscapePoliceCar : MonoBehaviour
{
    private EscapePoliceStationGenerator station;
    private int patrolId;
    private int waypointIndex;
    private bool chasing;
    private float chaseUntil;
    private float nextTrapAt;
    private Vector3[] route;
    private Vector3[] outerRoute;
    private bool usingOuterRoute;

    public int PatrolId => patrolId;
    public bool IsChasing => chasing;
    public float ChaseUntil => chaseUntil;
    public int WaypointIndex => waypointIndex;
    public Vector3[] Route => route;

    public void Initialize(EscapePoliceStationGenerator owner, int id, Vector3[] patrolRoute,
        Vector3[] outsidePatrolRoute, int firstWaypoint, Vector3 spawnPosition, bool atStation)
    {
        station = owner;
        patrolId = id;
        route = patrolRoute;
        outerRoute = outsidePatrolRoute;
        waypointIndex = firstWaypoint % route.Length;
        usingOuterRoute = false;
        chasing = false;
        chaseUntil = 0f;
        // Similar timing on both local patrol presentations in cooperative play.
        nextTrapAt = Time.time + 6f + id * 2.2f;
        transform.position = spawnPosition;
        if (atStation)
            transform.rotation = Quaternion.LookRotation(Vector3.back, Vector3.up);
        gameObject.SetActive(true);
    }

    private void Update()
    {
        if (station != null && gameObject.activeInHierarchy)
            station.TickPolice(this);
    }

    public void BeginOrRefreshChase(float duration)
    {
        chasing = true;
        chaseUntil = Time.time + duration;
    }

    public void StopChase()
    {
        chasing = false;
    }

    public void AdvanceWaypoint()
    {
        if (route == null || route.Length == 0) return;
        waypointIndex = (waypointIndex + 1) % route.Length;
    }

    public void SetWaypointIndex(int index)
    {
        if (route == null || route.Length == 0) return;
        waypointIndex = (index % route.Length + route.Length) % route.Length;
    }

    public void SelectPatrolRing(bool outside)
    {
        if (usingOuterRoute == outside || outerRoute == null || route == null) return;
        Vector3[] nextRoute = outside ? outerRoute : route;
        int nearest = 0;
        float nearestDistance = float.PositiveInfinity;
        for (int i = 0; i < nextRoute.Length; i++)
        {
            Vector3 delta = nextRoute[i] - transform.position;
            delta.y = 0f;
            float distance = delta.sqrMagnitude;
            if (distance >= nearestDistance) continue;
            nearestDistance = distance;
            nearest = i;
        }
        route = nextRoute;
        waypointIndex = nearest;
        usingOuterRoute = outside;
    }

    public bool ShouldDropTrap => !chasing && Time.time >= nextTrapAt;

    public void TrapDropped(float interval)
    {
        nextTrapAt = Time.time + interval;
    }

    public void EliminateByPlayerTrap()
    {
        station?.EliminatePolice(this, "Patrulla eliminada con la trampa del kart.");
    }

    private void OnTriggerEnter(Collider other)
    {
        KartIHC.Kart.KartController kart = other.GetComponentInParent<KartIHC.Kart.KartController>();
        if (kart != null) station?.HandlePoliceCollision(this, kart);
    }
}
