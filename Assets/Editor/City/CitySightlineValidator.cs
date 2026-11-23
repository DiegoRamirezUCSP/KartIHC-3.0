using System.Collections.Generic;
using System.Text;
using KartIHC.Gameplay;
using UnityEditor;
using UnityEngine;

namespace KartIHC.EditorTools
{
    /// <summary>
    /// KartIHC/City/Validate Drone Sightlines: comprueba que la ciudad no tapa la visión del
    /// dron hacia las tres puertas y la barrera policial. Lanza segmentos desde puntos de vuelo
    /// típicos (EscapeDroneSpawn, sobre el anillo a 12 m y 20 m, sobre el centro) y los prueba
    /// contra los triángulos reales de la instancia EscapeCity (sin física: la ciudad no tiene
    /// colliders). También verifica que la instancia no tenga ningún Collider.
    /// </summary>
    public static class CitySightlineValidator
    {
        private const int RingViewpoints = 12;
        private static readonly float[] RingHeights = { 12f, 20f };
        private const float RingViewpointRadius = 25f;
        private const float TargetHeight = 1.2f;   // mitad de la puerta (2.4 m)
        private const float DoorSampleOffset = 4.5f; // además del centro, ±4.5 m a lo largo de la puerta

        [MenuItem(CityGen.MenuRoot + "Validate Drone Sightlines", priority = 350)]
        public static void ValidateMenu()
        {
            if (!CityGen.EnsureEscapeScene())
            {
                return;
            }
            Validate(out string report);
            if (!Application.isBatchMode)
            {
                EditorUtility.DisplayDialog("KartIHC City · Líneas de vista del dron", report, "OK");
            }
        }

        /// <summary>Devuelve true si todas las líneas están libres y no hay colliders.</summary>
        public static bool Validate(out string report)
        {
            List<GameObject> instances = CityGen.FindSceneInstances();
            if (instances.Count == 0)
            {
                report = "No hay instancia de EscapeCity en la escena: nada que validar.";
                Debug.LogWarning("[City] " + report);
                return true;
            }

            var triangles = CollectTriangles(instances, out int colliderCount);
            List<(string name, Vector3 point)> viewpoints = Viewpoints();
            List<(string name, Vector3 point)> targets = Targets();

            int total = 0, blocked = 0;
            var blockedByTarget = new Dictionary<string, int>();
            var examples = new List<string>();
            foreach ((string viewName, Vector3 from) in viewpoints)
            {
                foreach ((string targetName, Vector3 to) in targets)
                {
                    total++;
                    if (!SegmentBlocked(from, to, triangles, out string hitName))
                    {
                        continue;
                    }
                    blocked++;
                    string key = targetName.Split('·')[0].Trim();
                    blockedByTarget[key] = blockedByTarget.TryGetValue(key, out int count) ? count + 1 : 1;
                    if (examples.Count < 12)
                    {
                        examples.Add($"  {viewName} → {targetName}: tapa {hitName}");
                    }
                }
            }

            var text = new StringBuilder();
            text.AppendLine($"Líneas libres: {total - blocked} de {total} " +
                            $"({viewpoints.Count} puntos de vuelo × {targets.Count} objetivos).");
            text.AppendLine(colliderCount == 0
                ? "Colliders en EscapeCity: 0 ✔"
                : $"Colliders en EscapeCity: {colliderCount} ✘ (la ciudad no debe tener ninguno)");
            if (blocked > 0)
            {
                text.AppendLine("Bloqueadas por objetivo:");
                foreach (KeyValuePair<string, int> pair in blockedByTarget)
                {
                    text.AppendLine($"  {pair.Key}: {pair.Value}");
                }
                text.AppendLine("Ejemplos:");
                foreach (string example in examples)
                {
                    text.AppendLine(example);
                }
            }

            report = text.ToString();
            bool ok = blocked == 0 && colliderCount == 0;
            if (ok) Debug.Log("[City] " + report);
            else Debug.LogWarning("[City] " + report);
            return ok;
        }

        // ---------- Puntos ----------

        private static List<(string, Vector3)> Viewpoints()
        {
            var points = new List<(string, Vector3)>();
            GameObject spawn = GameObject.Find("EscapeDroneSpawn");
            points.Add(("EscapeDroneSpawn", spawn != null ? spawn.transform.position : new Vector3(0f, 20f, -27f)));
            foreach (float height in RingHeights)
            {
                for (int i = 0; i < RingViewpoints; i++)
                {
                    float angle = i * 360f / RingViewpoints;
                    points.Add(($"anillo {angle:0}° a {height:0} m",
                        EscapeTrackGeometry.PointAt(angle, RingViewpointRadius, height)));
                }
            }
            points.Add(("centro a 20 m", new Vector3(0f, 20f, 0f)));
            points.Add(("centro a 12 m", new Vector3(0f, 12f, 0f)));
            return points;
        }

        private static List<(string, Vector3)> Targets()
        {
            var points = new List<(string, Vector3)>();
            for (int i = 1; i <= 3; i++)
            {
                GameObject door = GameObject.Find($"EscapeDoor_{i}");
                Transform panel = door != null ? door.transform.Find("Door Panel") : null;
                if (panel == null)
                {
                    float angle = CityZones.DoorAngles[i - 1];
                    AddLineTargets(points, $"Puerta {i}",
                        EscapeTrackGeometry.PointAt(angle, 25f, TargetHeight),
                        EscapeTrackGeometry.PointAt(angle, 1f, 0f).normalized);
                    continue;
                }
                AddLineTargets(points, $"Puerta {i}", panel.position, panel.right);
            }

            // La barrera policial se crea en tiempo de ejecución: se usa su ángulo fijo.
            float barrier = CityZones.PoliceBarrierAngle;
            AddLineTargets(points, "Barrera policial",
                EscapeTrackGeometry.PointAt(barrier, EscapeTrackGeometry.RoadRadius, TargetHeight),
                EscapeTrackGeometry.PointAt(barrier, 1f, 0f).normalized);
            return points;
        }

        private static void AddLineTargets(List<(string, Vector3)> points, string name, Vector3 center, Vector3 along)
        {
            along.y = 0f;
            along.Normalize();
            center.y = TargetHeight;
            points.Add(($"{name} · centro", center));
            points.Add(($"{name} · interior", center - along * DoorSampleOffset));
            points.Add(($"{name} · exterior", center + along * DoorSampleOffset));
        }

        // ---------- Triángulos ----------

        private struct Tri
        {
            public Vector3 A, B, C;
            public string Owner;
        }

        private struct MeshTris
        {
            public Bounds Bounds;
            public List<Tri> Tris;
        }

        private static List<MeshTris> CollectTriangles(List<GameObject> instances, out int colliderCount)
        {
            var result = new List<MeshTris>();
            colliderCount = 0;
            foreach (GameObject instance in instances)
            {
                colliderCount += instance.GetComponentsInChildren<Collider>(true).Length;
                foreach (MeshFilter filter in instance.GetComponentsInChildren<MeshFilter>())
                {
                    MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
                    Mesh mesh = filter.sharedMesh;
                    if (renderer == null || !renderer.enabled || mesh == null) continue;

                    Matrix4x4 matrix = filter.transform.localToWorldMatrix;
                    Vector3[] vertices = mesh.vertices;
                    int[] indices = mesh.triangles;
                    var tris = new List<Tri>(indices.Length / 3);
                    for (int i = 0; i < indices.Length; i += 3)
                    {
                        tris.Add(new Tri
                        {
                            A = matrix.MultiplyPoint3x4(vertices[indices[i]]),
                            B = matrix.MultiplyPoint3x4(vertices[indices[i + 1]]),
                            C = matrix.MultiplyPoint3x4(vertices[indices[i + 2]]),
                            Owner = filter.gameObject.name,
                        });
                    }
                    result.Add(new MeshTris { Bounds = renderer.bounds, Tris = tris });
                }
            }
            return result;
        }

        private static bool SegmentBlocked(Vector3 from, Vector3 to, List<MeshTris> meshes, out string hitName)
        {
            Vector3 delta = to - from;
            float length = delta.magnitude;
            var ray = new Ray(from, delta / length);
            foreach (MeshTris mesh in meshes)
            {
                if (!mesh.Bounds.IntersectRay(ray, out float enter) || enter > length) continue;
                foreach (Tri tri in mesh.Tris)
                {
                    if (RayTriangle(from, delta, tri.A, tri.B, tri.C, out float t) && t > 1e-4f && t < 1f - 1e-4f)
                    {
                        hitName = tri.Owner;
                        return true;
                    }
                }
            }
            hitName = null;
            return false;
        }

        /// <summary>Möller–Trumbore para el segmento origin + t·delta, t en [0, 1], ambas caras.</summary>
        private static bool RayTriangle(Vector3 origin, Vector3 delta, Vector3 a, Vector3 b, Vector3 c, out float t)
        {
            t = 0f;
            Vector3 edge1 = b - a, edge2 = c - a;
            Vector3 p = Vector3.Cross(delta, edge2);
            float det = Vector3.Dot(edge1, p);
            if (Mathf.Abs(det) < 1e-9f) return false;
            float inv = 1f / det;
            Vector3 s = origin - a;
            float u = Vector3.Dot(s, p) * inv;
            if (u < 0f || u > 1f) return false;
            Vector3 q = Vector3.Cross(s, edge1);
            float v = Vector3.Dot(delta, q) * inv;
            if (v < 0f || u + v > 1f) return false;
            t = Vector3.Dot(edge2, q) * inv;
            return true;
        }
    }
}
