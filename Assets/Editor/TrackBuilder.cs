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
        private const int Segments = 32;
        private const float InnerRadius = 30f;
        private const float OuterRadius = 48f;
        private const float WallHeight = 2f;
        private const float WallThickness = 0.5f;
        private const float GroundMargin = 20f;
        private const float RockOffset = 3f;
        private const int RockEverySegments = 3;

        private static readonly Vector3 RockScale = new Vector3(0.8f, 0.6f, 0.7f);

        [MenuItem("KartIHC/Build Track")]
        public static void BuildTrack()
        {
            GameObject existingTrack = GameObject.Find("Track");
            if (existingTrack != null)
            {
                Object.DestroyImmediate(existingTrack);
            }

            GameObject track = new GameObject("Track");

            Material wallMatA = LoadMaterial("Mat_OffroadWallA");
            Material wallMatB = LoadMaterial("Mat_OffroadWallB");
            BuildWallRing(track.transform, InnerRadius, "InnerWall", wallMatA, wallMatB);
            BuildWallRing(track.transform, OuterRadius, "OuterWall", wallMatA, wallMatB);

            BuildRockDecoration(track.transform);

            ResizeAndPaintGround();

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

        private static void BuildWallRing(Transform parent, float radius, string namePrefix, Material matA, Material matB)
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

                Material material = (i % 2 == 0) ? matA : matB;
                if (material != null)
                {
                    segment.GetComponent<MeshRenderer>().sharedMaterial = material;
                }
            }
        }

        private static void ResizeAndPaintGround()
        {
            GameObject ground = GameObject.Find("Ground");
            if (ground == null)
            {
                return;
            }

            // Unity's default Plane primitive is 10x10 world units at scale 1.
            float diameterWithMargin = (OuterRadius * 2f) + GroundMargin;
            float scaleFactor = diameterWithMargin / 10f;
            ground.transform.localScale = new Vector3(scaleFactor, 1f, scaleFactor);

            ApplyMaterial(ground, "Mat_OffroadGround");
        }

        private static void BuildRockDecoration(Transform parent)
        {
            Material rockMaterial = LoadMaterial("Mat_Rock");
            Vector3[] points = ComputeCirclePoints(OuterRadius + RockOffset, Segments);

            for (int i = 0; i < Segments; i += RockEverySegments)
            {
                GameObject rock = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                rock.name = $"Rock_{i}";
                rock.transform.SetParent(parent, false);
                rock.transform.position = new Vector3(points[i].x, RockScale.y * 0.5f, points[i].z);
                rock.transform.localScale = RockScale;

                if (rockMaterial != null)
                {
                    rock.GetComponent<MeshRenderer>().sharedMaterial = rockMaterial;
                }

                SphereCollider rockCollider = rock.GetComponent<SphereCollider>();
                if (rockCollider != null)
                {
                    Object.DestroyImmediate(rockCollider);
                }
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
            ApplyMaterial(startLine, "Mat_StartLine");

            BoxCollider boxCollider = startLine.GetComponent<BoxCollider>();
            if (boxCollider != null)
            {
                boxCollider.isTrigger = true;
            }
        }

        private static Material LoadMaterial(string materialName)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>($"Assets/Materials/{materialName}.mat");
            if (material == null)
            {
                Debug.LogWarning($"[TrackBuilder] Material '{materialName}' not found in Assets/Materials. Run KartIHC/Setup Materials first.");
            }
            return material;
        }

        private static void ApplyMaterial(GameObject go, string materialName)
        {
            if (go == null)
            {
                return;
            }

            Material material = LoadMaterial(materialName);
            if (material == null)
            {
                return;
            }

            MeshRenderer renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = material;
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
