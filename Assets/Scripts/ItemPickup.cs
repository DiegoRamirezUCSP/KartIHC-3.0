using System.Collections;
using UnityEngine;

/// <summary>
/// Caja de item en la pista. Al pasar el kart le da un power-up aleatorio (reemplazando
/// el que tenia), se oculta y reaparece tras unos segundos.
/// Requiere un Collider marcado "Is Trigger".
/// </summary>
[RequireComponent(typeof(Collider))]
public class ItemPickup : MonoBehaviour
{
    [SerializeField] private float respawnSeconds = 10f;
    [Tooltip("Giro decorativo para que se note que es recogible. 0 = sin giro.")]
    [SerializeField] private float spinDegreesPerSecond = 90f;

    private Collider pickupCollider;
    private Renderer[] renderers;
    private bool isAvailable = true;

    private void Awake()
    {
        pickupCollider = GetComponent<Collider>();
        pickupCollider.isTrigger = true;
        renderers = GetComponentsInChildren<Renderer>();
    }

    private void Update()
    {
        if (isAvailable && spinDegreesPerSecond != 0f)
        {
            transform.Rotate(0f, spinDegreesPerSecond * Time.deltaTime, 0f, Space.World);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!isAvailable)
        {
            return;
        }

        KartItemInventory inventory = other.GetComponentInParent<KartItemInventory>();
        if (inventory == null)
        {
            return;
        }

        string item = KartItemInventory.AllItems[Random.Range(0, KartItemInventory.AllItems.Length)];
        inventory.GiveItem(item);
        Debug.Log($"Item obtenido: {item}");

        StartCoroutine(RespawnRoutine());
    }

    private IEnumerator RespawnRoutine()
    {
        SetAvailable(false);
        yield return new WaitForSeconds(respawnSeconds);
        SetAvailable(true);
    }

    private void SetAvailable(bool available)
    {
        isAvailable = available;
        pickupCollider.enabled = available;

        foreach (Renderer r in renderers)
        {
            r.enabled = available;
        }
    }
}
