using UnityEditor;
using UnityEngine;

namespace KartIHC.EditorTools
{
    /// <summary>
    /// Editor utility that generates a simple oval track (inner/outer walls +
    /// a start line) around the origin, so there's something for the kart to
    /// drive on before final track art exists.
    /// </summary>
    public static class TrackBuilder
    {
        private const int Segments = 16;
        private const float InnerRadius = 15f;
        private const float OuterRadius = 23f;
        private const float WallHeight = 2f;
        private const float WallThickness = 0.5f;

        [MenuItem("KartIHC/Build Track")]
        public static void BuildTrack()
        {
            GameObject existingTrack = GameObject.Find("Track");
            if (existingTrack != null)
            {
                Object.DestroyImmediate(existingTrack);
            }

            GameObject track = new GameObject("Track");

            BuildWallRing(track.transform, InnerRadius, "InnerWall");
            BuildWallRing(track.transform, OuterRadius, "OuterWall");

            float midRadius = (InnerRadius + OuterRadius) * 0.5f;
            Vector3[] midPoints = ComputeCirclePoints(midRadius, Segments);
            Vector3 startPosition = midPoints[0];
            Vector3 startDirection = (midPoints[1] - midPoints[0]).normalized;
            Quaternion startRotation = Quaternion.LookRotation(startDirection, Vector3.up);

            BuildStartLine(track.transform, startPosition, startRotation);

            GameObject kartBody = GameObject.Find("KartBody");
            if (kartBody != null)
            {
                kartBody.transform.position = startPosition + Vector3.up * 1f;
                kartBody.transform.rotation = startRotation;
            }

            Selection.activeGameObject = track;
        }

        private static void BuildWallRing(Transform parent, float radius, string namePrefix)
        {
            Vector3[] points = ComputeCirclePoints(radius, Segments);

            for (int i = 0; i < Segments; i++)
            {
                Vector3 current = points[i];
                Vector3 next = points[(i + 1) % Segments];
                Vector3 midpoint = (current + next) * 0.5f;
                Vector3 direction = (next - current).normalized;
                float length = Vector3.Distance(current, next);

                GameObject segment = GameObject.CreatePrimitive(PrimitiveType.Cube);
                segment.name = $"{namePrefix}_{i}";
                segment.transform.SetParent(parent, false);
                segment.transform.position = midpoint + Vector3.up * (WallHeight * 0.5f);
                segment.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
                segment.transform.localScale = new Vector3(WallThickness, WallHeight, length);
            }
        }

        private static void BuildStartLine(Transform parent, Vector3 position, Quaternion rotation)
        {
            GameObject startLine = GameObject.CreatePrimitive(PrimitiveType.Cube);
            startLine.name = "StartLine";
            startLine.transform.SetParent(parent, false);
            startLine.transform.position = new Vector3(position.x, 0.05f, position.z);
            startLine.transform.rotation = rotation;
            startLine.transform.localScale = new Vector3(OuterRadius - InnerRadius, 0.05f, 1f);

            BoxCollider boxCollider = startLine.GetComponent<BoxCollider>();
            if (boxCollider != null)
            {
                boxCollider.isTrigger = true;
            }
        }

        private static Vector3[] ComputeCirclePoints(float radius, int segments)
        {
            Vector3[] points = new Vector3[segments];
            for (int i = 0; i < segments; i++)
            {
                float angle = i * (2f * Mathf.PI / segments);
                points[i] = new Vector3(radius * Mathf.Cos(angle), 0f, radius * Mathf.Sin(angle));
            }
            return points;
        }
    }
}
