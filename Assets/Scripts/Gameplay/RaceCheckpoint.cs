using UnityEngine;

namespace KartIHC.Gameplay
{
    /// <summary>
    /// Trigger placed along the track by KartIHC/Build Checkpoints. Reports to
    /// LapManager when the kart (tag "Player") drives through it. Its forward
    /// axis points in the racing direction, so it doubles as a respawn pose.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class RaceCheckpoint : MonoBehaviour
    {
        [Tooltip("Order along the track (0 = first after the finish line). Ignored for the finish line.")]
        public int index;

        [Tooltip("True for the finish/start line trigger.")]
        public bool isFinishLine;

        public LapManager lapManager;

        private void Awake()
        {
            GetComponent<BoxCollider>().isTrigger = true;

            if (lapManager == null)
            {
                lapManager = FindFirstObjectByType<LapManager>();
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (lapManager == null || !LapManager.IsPlayer(other))
            {
                return;
            }

            lapManager.HandleCheckpoint(this);
        }

        private void OnDrawGizmos()
        {
            BoxCollider box = GetComponent<BoxCollider>();
            if (box == null)
            {
                return;
            }

            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = isFinishLine ? new Color(1f, 1f, 1f, 0.35f) : new Color(1f, 0.8f, 0f, 0.25f);
            Gizmos.DrawCube(box.center, box.size);
            Gizmos.color = isFinishLine ? Color.white : Color.yellow;
            Gizmos.DrawWireCube(box.center, box.size);
        }
    }
}
