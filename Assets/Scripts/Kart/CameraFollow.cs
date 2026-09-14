using UnityEngine;

namespace KartIHC.Kart
{
    /// <summary>
    /// Smoothly follows a target (typically the kart) from a fixed local offset,
    /// keeping the camera looking at the target at all times.
    /// </summary>
    public class CameraFollow : MonoBehaviour
    {
        [SerializeField]
        private Transform target;

        [SerializeField]
        private Vector3 offset = new Vector3(0f, 4f, -7f);

        [SerializeField]
        private float smoothSpeed = 5f;

        private void LateUpdate()
        {
            if (target == null)
            {
                return;
            }

            Vector3 desiredPosition = target.position + target.TransformDirection(offset);
            transform.position = Vector3.Lerp(transform.position, desiredPosition, smoothSpeed * Time.deltaTime);
            transform.LookAt(target);
        }
    }
}
