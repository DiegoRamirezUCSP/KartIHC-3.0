using KartIHC.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KartIHC.EditorTools
{
    /// <summary>
    /// Places RaceCheckpoint triggers evenly along the oval built by TrackBuilder
    /// (a circle around the origin through the StartLine), plus a tall finish
    /// trigger aligned with the existing StartLine, and a LapManager.
    /// The StartLine's own trigger is only 5 cm tall at ground level, so the
    /// kart's body never touches it; that's why a separate finish trigger is made.
    /// </summary>
    public static class CheckpointBuilder
    {
        private const string RootName = "RaceCheckpoints";
        private const int CheckpointCount = 8;
        private const float TriggerHeight = 6f;
        private const float TriggerDepth = 2f;
        private const float WidthMargin = 4f;

        [MenuItem("KartIHC/Build Checkpoints")]
        public static void BuildCheckpoints()
        {
            GameObject startLine = GameObject.Find("StartLine");
            if (startLine == null)
            {
                Debug.LogWarning("[CheckpointBuilder] No 'StartLine' found. Run KartIHC/Build Track first.");
                return;
            }

            GameObject existing = GameObject.Find(RootName);
            if (existing != null)
            {
                Undo.DestroyObjectImmediate(existing);
            }

            // TrackBuilder centers the oval on the origin and puts the StartLine on its mid radius,
            // stretched across the track width (localScale.x) and facing the racing direction.
            Vector3 start = startLine.transform.position;
            float radius = new Vector2(start.x, start.z).magnitude;
            float trackWidth = startLine.transform.localScale.x;
            float startAngle = Mathf.Atan2(start.z, start.x);
            Vector3 startForward = Vector3.ProjectOnPlane(startLine.transform.forward, Vector3.up).normalized;
            Vector3 counterClockwiseTangent = new Vector3(-Mathf.Sin(startAngle), 0f, Mathf.Cos(startAngle));
            float direction = Vector3.Dot(startForward, counterClockwiseTangent) >= 0f ? 1f : -1f;

            GameObject root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Build Checkpoints");
            LapManager lapManager = root.AddComponent<LapManager>();

            for (int i = 0; i < CheckpointCount; i++)
            {
                float t = (i + 0.5f) / CheckpointCount;
                float angle = startAngle + direction * t * Mathf.PI * 2f;
                Vector3 position = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                Vector3 tangent = direction * new Vector3(-Mathf.Sin(angle), 0f, Mathf.Cos(angle));

                CreateTrigger(root.transform, $"Checkpoint_{i}", position, Quaternion.LookRotation(tangent, Vector3.up),
                    trackWidth, i, false, lapManager);
            }

            CreateTrigger(root.transform, "FinishTrigger", new Vector3(start.x, 0f, start.z),
                Quaternion.LookRotation(startForward, Vector3.up), trackWidth, -1, true, lapManager);

            GameObject kartBody = GameObject.Find("KartBody");
            if (kartBody != null && !kartBody.CompareTag(LapManager.PlayerTag))
            {
                Undo.RecordObject(kartBody, "Tag KartBody");
                kartBody.tag = LapManager.PlayerTag;
            }

            EditorSceneManager.MarkSceneDirty(root.scene);
            Selection.activeGameObject = root;
            Debug.Log($"[CheckpointBuilder] Built {CheckpointCount} checkpoints + finish trigger (radius {radius:0.0}, width {trackWidth:0.0}).");
        }

        private static void CreateTrigger(Transform parent, string name, Vector3 groundPosition, Quaternion rotation,
            float trackWidth, int index, bool isFinish, LapManager lapManager)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(groundPosition, rotation);

            BoxCollider box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(trackWidth + WidthMargin, TriggerHeight, TriggerDepth);
            box.center = new Vector3(0f, TriggerHeight * 0.5f, 0f);

            RaceCheckpoint checkpoint = go.AddComponent<RaceCheckpoint>();
            checkpoint.index = index;
            checkpoint.isFinishLine = isFinish;
            checkpoint.lapManager = lapManager;
        }
    }
}
