using UnityEngine;

/// <summary>One generated police patrol vehicle for the local Escape Solo mode.</summary>
public sealed class EscapePoliceCar : MonoBehaviour
{
    private EscapePoliceStationGenerator station;
    private int patrolId;
    private int waypointIndex;
    private bool chasing;
    private float chaseUntil;
    private float nextTrapAt;
    private Vector3[] route;

    public int PatrolId => patrolId;
    public bool IsChasing => chasing;
    public float ChaseUntil => chaseUntil;
    public int WaypointIndex => waypointIndex;
    public Vector3[] Route => route;

    public void Initialize(EscapePoliceStationGenerator owner, int id, Vector3[] patrolRoute,
        int firstWaypoint, Vector3 spawnPosition, bool atStation)
    {
        station = owner;
        patrolId = id;
        route = patrolRoute;
        waypointIndex = firstWaypoint % route.Length;
        chasing = false;
        chaseUntil = 0f;
        nextTrapAt = Time.time + Random.Range(6f, 15f);
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
