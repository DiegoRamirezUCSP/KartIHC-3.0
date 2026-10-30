using KartIHC.Gameplay;
using UnityEngine;

namespace KartIHC.Kart
{
    /// <summary>
    /// Listens to GameStateManager (no changes to it or to TutorialManager):
    /// - On entering MainMenu (tutorial completed/cancelled, restart): puts the
    ///   kart back on the start line facing the racing direction, stopped, with
    ///   motor torque 0 and brakes on, and snaps the camera back onto it.
    /// - During a tutorial: keeps the kart still (input off, brakes on, no
    ///   horizontal motion), since no tutorial needs the kart to move to tick
    ///   its checkboxes; they read the input devices directly.
    /// </summary>
    [RequireComponent(typeof(KartController))]
    public class KartResetOnMenu : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureOnKart()
        {
            KartController kart = FindFirstObjectByType<KartController>();
            if (kart != null && kart.GetComponent<KartResetOnMenu>() == null)
            {
                kart.gameObject.AddComponent<KartResetOnMenu>();
            }
        }

        [Tooltip("Keep the kart still while a tutorial is open.")]
        [SerializeField] private bool freezeDuringTutorial = true;

        private KartController kart;
        private KartRecovery recovery;
        private Rigidbody rb;
        private GameStateManager gameStateManager;

        private void Awake()
        {
            kart = GetComponent<KartController>();
            rb = GetComponent<Rigidbody>();
            recovery = GetComponent<KartRecovery>();
            if (recovery == null)
            {
                recovery = gameObject.AddComponent<KartRecovery>();
            }

            gameStateManager = FindFirstObjectByType<GameStateManager>();
            if (gameStateManager != null)
            {
                gameStateManager.OnStateChanged += HandleStateChanged;
            }
        }

        private void OnDestroy()
        {
            if (gameStateManager != null)
            {
                gameStateManager.OnStateChanged -= HandleStateChanged;
            }
        }

        private bool InTutorial => gameStateManager != null && gameStateManager.CurrentState == GameState.Tutorial;

        private void HandleStateChanged(GameState state)
        {
            // GameStateManager.SetState enables/disables input before raising this event,
            // so anything set here wins over it.
            if (state == GameState.MainMenu)
            {
                ResetKartToStart();
            }
            else if (state == GameState.Tutorial && freezeDuringTutorial)
            {
                HoldStill();
            }
        }

        private void FixedUpdate()
        {
            if (freezeDuringTutorial && InTutorial)
            {
                HoldStill();

                // Keep gravity so it settles on the ground, but cancel any push (e.g. turbo said in tutorial 3).
                rb.linearVelocity = new Vector3(0f, Mathf.Min(rb.linearVelocity.y, 0f), 0f);
                rb.angularVelocity = Vector3.zero;
            }
        }

        public void ResetKartToStart()
        {
            // Clears power-ups, dropped traps and wheel inputs.
            kart.ResetToStart();
            // Start line pose, facing the racing direction, velocities 0.
            recovery.ResetToStart();
            HoldStill();
            ReattachCamera();
        }

        private void HoldStill()
        {
            kart.InputEnabled = false;
            SetWheel(kart.frontLeft);
            SetWheel(kart.frontRight);
            SetWheel(kart.rearLeft);
            SetWheel(kart.rearRight);
        }

        private void SetWheel(WheelCollider wheel)
        {
            if (wheel == null)
            {
                return;
            }

            wheel.motorTorque = 0f;
            wheel.brakeTorque = kart.brakeTorque;
            wheel.steerAngle = 0f;
        }

        private void ReattachCamera()
        {
            Camera mainCamera = Camera.main;
            CameraFollow follow = mainCamera != null ? mainCamera.GetComponent<CameraFollow>() : FindFirstObjectByType<CameraFollow>();
            if (follow != null)
            {
                follow.SetTarget(transform, snap: true);
            }
        }
    }
}
