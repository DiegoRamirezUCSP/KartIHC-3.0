using UnityEngine;

namespace KartIHC.Gameplay.Hazards
{
    /// <summary>
    /// Bloqueo del dron para obstáculos y cables. Delega en
    /// <see cref="EscapeModeNetworkBridge.ImmobilizeDrone"/>: reutiliza su bloqueo, su
    /// overlay "DRON BLOQUEADO" y también impide hackear puertas mientras dura.
    ///
    /// También permite "sostener" el dron quieto sin bloquearlo (durante el minijuego
    /// de obstáculos), igual que hace el bridge durante el minijuego de puertas.
    /// </summary>
    public sealed class EscapeDroneLock : MonoBehaviour
    {
        private bool holdRequested;
        private bool pinned;
        private Vector3 pinPosition;
        private Quaternion pinRotation;

        /// <summary>Bloqueado por el bridge (fallo de puerta, de obstáculo o ImmobilizeDrone).</summary>
        public bool IsDroneLocked(EscapeModeNetworkBridge bridge) => bridge != null && bridge.DroneLocked;

        public float LockSecondsRemaining(EscapeModeNetworkBridge bridge) =>
            bridge != null ? bridge.DroneLockSecondsRemaining : 0f;

        public void Lock(EscapeModeNetworkBridge bridge, float seconds, string reason)
        {
            if (bridge == null || !bridge.IsHacker || seconds <= 0f) return;
            bridge.ImmobilizeDrone(seconds, reason);
        }

        public void SetHold(bool hold) => holdRequested = hold;

        private void LateUpdate()
        {
            EscapeModeNetworkBridge bridge = EscapeModeNetworkBridge.Instance;
            Transform drone = bridge != null && bridge.IsHacker ? bridge.ReconDroneTransform : null;
            if (drone == null || !holdRequested)
            {
                pinned = false;
                return;
            }

            if (!pinned)
            {
                pinPosition = drone.position;
                pinRotation = drone.rotation;
                pinned = true;
            }
            // El bridge mueve el dron con CharacterController.Move en su Update; se
            // devuelve a la posición fijada y se sincroniza la física para que el
            // siguiente Move parta de aquí.
            drone.SetPositionAndRotation(pinPosition, pinRotation);
            Physics.SyncTransforms();
        }
    }
}
