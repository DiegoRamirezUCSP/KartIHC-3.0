using UnityEngine;

/// <summary>
/// Delimita el area jugable. Si el auto (o cualquier objeto con Rigidbody
/// con el tag indicado) sale de esta zona, lo teletransporta de vuelta a un
/// punto de reaparicion fijo que tu defines en el Inspector.
///
/// Por que un punto fijo y no "la ultima posicion valida": si se guarda la
/// ultima posicion mientras el objeto sigue dentro del trigger, esa "ultima
/// posicion" puede ser el instante justo antes de caer (todavia dentro del
/// cuadro en X/Z, pero ya cayendo en el aire fuera de la pista), asi que
/// reposicionar ahi vuelve a caer de inmediato: un bucle infinito. Un punto
/// fijo elegido a mano siempre es seguro.
///
/// Como usarlo:
/// 1. Crea un GameObject vacio en un punto seguro de tu pista (ej. la salida).
/// 2. Arrastralo al campo "Respawn Point" de este componente.
/// 3. Crea otro GameObject con BoxCollider (Is Trigger) que cubra toda la
///    pista con margen, y agrega este script ahi mismo.
/// </summary>
[RequireComponent(typeof(Collider))]
public class TrackBoundary : MonoBehaviour
{
    [Tooltip("Si esta vacio, cualquier objeto con Rigidbody que salga cuenta.")]
    [SerializeField] private string requiredTag = "Player";

    [Tooltip("Punto fijo donde reaparece el auto al salirse de la zona.")]
    [SerializeField] private Transform respawnPoint;

    private void Awake()
    {
        Collider col = GetComponent<Collider>();
        col.isTrigger = true;
    }

    private void OnTriggerExit(Collider other)
    {
        if (!string.IsNullOrEmpty(requiredTag) && !other.CompareTag(requiredTag))
        {
            return;
        }

        Rigidbody rb = other.attachedRigidbody;
        if (rb == null)
        {
            return;
        }

        if (respawnPoint == null)
        {
            Debug.LogWarning("TrackBoundary: no hay Respawn Point asignado, no se puede reposicionar.");
            return;
        }

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.position = respawnPoint.position;
        rb.rotation = respawnPoint.rotation;

        Debug.Log("El auto se salio de la zona jugable, se reposiciono en el punto de reaparicion.");
    }
}
