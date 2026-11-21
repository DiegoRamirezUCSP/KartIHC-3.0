using System.Reflection;
using UnityEngine;

namespace KartIHC.Gameplay.Hazards
{
    /// <summary>
    /// Bloqueo del dron para obstáculos y cables, sin modificar EscapeModeNetworkBridge.
    ///
    /// Si el bridge expone <c>public void ImmobilizeDrone(float seconds, string reason)</c>
    /// (pendiente de Diego), se usa ese método: reutiliza su bloqueo, su overlay
    /// "DRON BLOQUEADO" y también impide hackear puertas. Si no existe, se usa la
    /// alternativa propia: el dron queda fijo en su posición y EscapeHazardHud dibuja
    /// el overlay; en ese caso el hacker aún podría hackear puertas mientras dura.
    ///
    /// También permite "sostener" el dron quieto sin bloquearlo (durante el minijuego
    /// de obstáculos), igual que hace el bridge durante el minijuego de puertas.
    /// </summary>
    public sealed class EscapeDroneLock : MonoBehaviour
    {
        private static MethodInfo bridgeImmobilize;
        private static bool lookedUpBridgeMethod;

        private float fallbackLockedUntil;
        private bool fallbackWasLocked;
        private bool holdRequested;
        private bool pinned;
        private Vector3 pinPosition;
        private Quaternion pinRotation;

        public static bool BridgeSupportsImmobilize
        {
            get
            {
                if (!lookedUpBridgeMethod)
                {
                    lookedUpBridgeMethod = true;
                    bridgeImmobilize = typeof(EscapeModeNetworkBridge).GetMethod("ImmobilizeDrone",
                        BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(float), typeof(string) }, null);
                }
                return bridgeImmobilize != null;
            }
        }

        public float FallbackSecondsRemaining => Mathf.Max(0f, fallbackLockedUntil - Time.unscaledTime);
        public bool FallbackLocked => FallbackSecondsRemaining > 0f;

        /// <summary>Bloqueado por el bridge (fallo de puerta o ImmobilizeDrone) o por la alternativa propia.</summary>
        public bool IsDroneLocked(EscapeModeNetworkBridge bridge) => FallbackLocked || (bridge != null && bridge.DroneLocked);

        public float LockSecondsRemaining(EscapeModeNetworkBridge bridge) =>
            Mathf.Max(FallbackSecondsRemaining, bridge != null ? bridge.DroneLockSecondsRemaining : 0f);

        public void Lock(EscapeModeNetworkBridge bridge, float seconds, string reason)
        {
            if (bridge == null || !bridge.IsHacker || seconds <= 0f) return;
            if (BridgeSupportsImmobilize)
            {
                bridgeImmobilize.Invoke(bridge, new object[] { seconds, reason });
                return;
            }
            fallbackLockedUntil = Mathf.Max(fallbackLockedUntil, Time.unscaledTime + seconds);
            EscapeModeHudFeed.Publish(reason);
        }

        public void SetHold(bool hold) => holdRequested = hold;

        private void Update()
        {
            bool locked = FallbackLocked;
            if (fallbackWasLocked && !locked) EscapeModeHudFeed.Publish("Dron desbloqueado");
            fallbackWasLocked = locked;
        }

        private void LateUpdate()
        {
            EscapeModeNetworkBridge bridge = EscapeModeNetworkBridge.Instance;
            Transform drone = bridge != null && bridge.IsHacker ? bridge.ReconDroneTransform : null;
            if (drone == null || !(holdRequested || FallbackLocked))
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
