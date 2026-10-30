using KartIHC.Kart;
using UnityEngine;

/// <summary>
/// Va en el prefab que KartController suelta detras del kart al usar el
/// power-up "trampa". Cuando otro kart (o el mismo, pasado un tiempo de
/// gracia) la pisa, le aplica un frenado en seco a traves de
/// KartController.ApplyTrapEffect(), que a su vez revisa si ese kart tiene
/// el escudo activo antes de hacer efecto.
///
/// Requisitos del prefab:
/// - Un Collider marcado "Is Trigger" (Sphere o Box, del tamano de la trampa).
/// - Este script.
/// - Cualquier modelo/visual que quieras que se vea en el suelo.
/// </summary>
[RequireComponent(typeof(Collider))]
public class TrapPickup : MonoBehaviour
{
    [Tooltip("Tag que deben tener los karts para activar la trampa.")]
    [SerializeField] private string vehicleTag = "Player";

    [Tooltip("Segundos donde se ignora al kart que solto la trampa, para que no se auto-active al soltarla.")]
    [SerializeField] private float ownerGracePeriod = 1.5f;

    private KartController owner;
    private float spawnTime;

    private void Awake()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    /// <summary>Llamado por KartController justo despues de instanciarla.</summary>
    public void Init(KartController droppedBy)
    {
        owner = droppedBy;
        spawnTime = Time.time;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!string.IsNullOrEmpty(vehicleTag) && !other.CompareTag(vehicleTag))
        {
            return;
        }

        KartController victim = other.GetComponentInParent<KartController>();
        if (victim == null)
        {
            return;
        }

        bool isOwnerInGracePeriod = victim == owner && Time.time - spawnTime < ownerGracePeriod;
        if (isOwnerInGracePeriod)
        {
            return;
        }

        victim.ApplyTrapEffect();

        // Se consume al primer kart que la activa, tenga o no escudo
        // (igual que una banana de Mario Kart: pisarla la gasta).
        Destroy(gameObject);
    }
}
