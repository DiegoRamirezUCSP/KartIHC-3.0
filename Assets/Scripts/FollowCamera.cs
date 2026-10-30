using UnityEngine;

public class FollowCamera : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField] private Vector3 offset = new Vector3(0f, 3f, -6f);
    [SerializeField] private float positionSmoothSpeed = 8f;
    [SerializeField] private float rotationSmoothSpeed = 6f;

    private void LateUpdate()
    {
        if (target == null)
        {
            return;
        }

        // Antes se usaba target.TransformDirection(offset), que rota el offset
        // con la rotacion COMPLETA del auto (incluyendo inclinacion hacia
        // adelante/atras o de costado). Si el auto se inclinaba un poco al
        // chocar o acelerar fuerte, la camara salia disparada hacia arriba.
        // Aqui solo usamos el giro en Y (hacia donde apunta el auto en el
        // plano horizontal), ignorando cualquier inclinacion.
        float yaw = target.eulerAngles.y;
        Quaternion flatRotation = Quaternion.Euler(0f, yaw, 0f);

        Vector3 desiredPosition = target.position + flatRotation * offset;

        transform.position = Vector3.Lerp(transform.position, desiredPosition, Time.deltaTime * positionSmoothSpeed);

        Quaternion desiredRotation = Quaternion.LookRotation(
            (target.position + Vector3.up * 0.8f) - transform.position);
        transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, Time.deltaTime * rotationSmoothSpeed);
    }
}
