using UnityEngine;

/// <summary>Police-deployed trap that immobilizes the kart for 30 seconds.</summary>
[RequireComponent(typeof(SphereCollider))]
public sealed class EscapePoliceTrap : MonoBehaviour
{
    private const float StunSeconds = 30f;
    private EscapePoliceStationGenerator station;
    private float expiresAt;

    public void Initialize(EscapePoliceStationGenerator owner, float lifetime)
    {
        station = owner;
        expiresAt = Time.time + lifetime;
        SphereCollider trigger = GetComponent<SphereCollider>();
        trigger.isTrigger = true;
        trigger.radius = 1.15f;
        Rigidbody body = GetComponent<Rigidbody>();
        if (body == null) body = gameObject.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
    }

    private void Update()
    {
        if (Time.time >= expiresAt) Destroy(gameObject);
    }

    private void OnTriggerEnter(Collider other)
    {
        KartIHC.Kart.KartController kart = other.GetComponentInParent<KartIHC.Kart.KartController>();
        if (kart == null) return;
        if (!kart.IsShielded)
        {
            kart.ApplyPoliceTrapEffect(StunSeconds);
            station?.NotifyKartTrapped(StunSeconds);
        }
        else
        {
            station?.PublishMessage("El escudo bloqueó una trampa policial.");
        }
        Destroy(gameObject);
    }
}
