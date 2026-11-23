using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace KartIHC.EditorTools
{
    /// <summary>
    /// Construye una malla con varias submallas (una por material) a base de quads.
    /// Convención de Unity: un triángulo es frontal si se ve en sentido horario.
    /// </summary>
    public sealed class CityMeshBuilder
    {
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Vector3> normals = new List<Vector3>();
        private readonly List<Vector2> uvs = new List<Vector2>();
        private readonly List<int>[] triangles;

        public CityMeshBuilder(int submeshCount)
        {
            triangles = new List<int>[submeshCount];
            for (int i = 0; i < submeshCount; i++)
            {
                triangles[i] = new List<int>();
            }
        }

        public bool IsEmpty => vertices.Count == 0;
        public int TriangleCount
        {
            get
            {
                int count = 0;
                foreach (List<int> list in triangles) count += list.Count / 3;
                return count;
            }
        }

        /// <summary>Quad visto de frente: abajo-izquierda, abajo-derecha, arriba-derecha, arriba-izquierda.</summary>
        public void Quad(int submesh, Vector3 bl, Vector3 br, Vector3 tr, Vector3 tl,
            Vector2 uvBl, Vector2 uvBr, Vector2 uvTr, Vector2 uvTl)
        {
            Vector3 normal = Vector3.Cross(tl - bl, br - bl).normalized;
            int start = vertices.Count;
            vertices.Add(bl); vertices.Add(br); vertices.Add(tr); vertices.Add(tl);
            uvs.Add(uvBl); uvs.Add(uvBr); uvs.Add(uvTr); uvs.Add(uvTl);
            for (int i = 0; i < 4; i++) normals.Add(normal);
            List<int> list = triangles[submesh];
            list.Add(start); list.Add(start + 3); list.Add(start + 2);
            list.Add(start); list.Add(start + 2); list.Add(start + 1);
        }

        /// <summary>Pared vertical de 'from' a 'to' (normal a la derecha al caminar de from a to, vista desde fuera).</summary>
        public void Wall(int submesh, Vector3 from, Vector3 to, float height, Vector2 uvMin, Vector2 uvMax)
        {
            Vector3 up = Vector3.up * height;
            Quad(submesh, from, to, to + up, from + up,
                uvMin, new Vector2(uvMax.x, uvMin.y), uvMax, new Vector2(uvMin.x, uvMax.y));
        }

        /// <summary>Cara superior horizontal del rectángulo XZ a la altura y; UV en metros × uvScale.</summary>
        public void Top(int submesh, Rect rect, float y, float uvScale)
        {
            Quad(submesh,
                new Vector3(rect.xMin, y, rect.yMin), new Vector3(rect.xMax, y, rect.yMin),
                new Vector3(rect.xMax, y, rect.yMax), new Vector3(rect.xMin, y, rect.yMax),
                new Vector2(rect.xMin, rect.yMin) * uvScale, new Vector2(rect.xMax, rect.yMin) * uvScale,
                new Vector2(rect.xMax, rect.yMax) * uvScale, new Vector2(rect.xMin, rect.yMax) * uvScale);
        }

        /// <summary>Cara inferior (normal hacia abajo) del rectángulo XZ a la altura y, para aleros vistos desde abajo.</summary>
        public void Bottom(int submesh, Rect rect, float y, float uvScale)
        {
            // Mirando hacia arriba con +Z arriba en pantalla, la derecha es -X.
            Quad(submesh,
                new Vector3(rect.xMax, y, rect.yMin), new Vector3(rect.xMin, y, rect.yMin),
                new Vector3(rect.xMin, y, rect.yMax), new Vector3(rect.xMax, y, rect.yMax),
                new Vector2(rect.xMax, rect.yMin) * uvScale, new Vector2(rect.xMin, rect.yMin) * uvScale,
                new Vector2(rect.xMin, rect.yMax) * uvScale, new Vector2(rect.xMax, rect.yMax) * uvScale);
        }

        /// <summary>
        /// Las 4 paredes exteriores de un prisma rectangular entre y0 e y1. El UV horizontal recorre
        /// el perímetro de forma continua (las ventanas siguen en las esquinas):
        /// u = (perímetro recorrido) × uScale + uOffset, v = (y - y0) × vScale + vOffset.
        /// </summary>
        public void Walls(int submesh, Rect rect, float y0, float y1, float uScale, float vScale,
            float uOffset = 0f, float vOffset = 0f)
        {
            Vector3 a = new Vector3(rect.xMin, y0, rect.yMin); // SO
            Vector3 b = new Vector3(rect.xMax, y0, rect.yMin); // SE
            Vector3 c = new Vector3(rect.xMax, y0, rect.yMax); // NE
            Vector3 d = new Vector3(rect.xMin, y0, rect.yMax); // NO
            float h = y1 - y0;
            float v0 = vOffset, v1 = h * vScale + vOffset;
            float u = uOffset;
            // Recorrido con el exterior a la derecha: S (a→b), E (b→c), N (c→d), O (d→a).
            u = Side(submesh, a, b, h, u, uScale, v0, v1);
            u = Side(submesh, b, c, h, u, uScale, v0, v1);
            u = Side(submesh, c, d, h, u, uScale, v0, v1);
            Side(submesh, d, a, h, u, uScale, v0, v1);
        }

        private float Side(int submesh, Vector3 from, Vector3 to, float height, float u, float uScale, float v0, float v1)
        {
            float length = Vector3.Distance(from, to);
            float u1 = u + length * uScale;
            Wall(submesh, from, to, height, new Vector2(u, v0), new Vector2(u1, v1));
            return u1;
        }

        /// <summary>Prisma cerrado (4 paredes + tapa), UV en metros × uvScale.</summary>
        public void Box(int submesh, Rect rect, float y0, float y1, float uvScale)
        {
            Walls(submesh, rect, y0, y1, uvScale, uvScale, 0f, y0 * uvScale);
            Top(submesh, rect, y1, uvScale);
        }

        /// <summary>Disco horizontal (polígono regular) a la altura y; UV en metros × uvScale.</summary>
        public void Disc(int submesh, Vector3 center, float radius, int segments, float uvScale)
        {
            for (int i = 0; i < segments; i++)
            {
                float a0 = i * Mathf.PI * 2f / segments;
                float a1 = (i + 1) * Mathf.PI * 2f / segments;
                Vector3 p0 = center + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * radius;
                Vector3 p1 = center + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * radius;
                // Visto desde arriba, centro → p1 → p0 va en sentido horario.
                Triangle(submesh, center, p1, p0, Vector3.up, Uv(center, uvScale), Uv(p1, uvScale), Uv(p0, uvScale));
            }
        }

        /// <summary>Triángulo frontal (horario visto de frente) con normal explícita.</summary>
        public void Triangle(int submesh, Vector3 a, Vector3 b, Vector3 c, Vector3 normal, Vector2 uvA, Vector2 uvB, Vector2 uvC)
        {
            int start = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c);
            uvs.Add(uvA); uvs.Add(uvB); uvs.Add(uvC);
            normals.Add(normal); normals.Add(normal); normals.Add(normal);
            List<int> list = triangles[submesh];
            list.Add(start); list.Add(start + 1); list.Add(start + 2);
        }

        private static Vector2 Uv(Vector3 p, float scale) => new Vector2(p.x, p.z) * scale;

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name };
            if (vertices.Count > 65000)
            {
                mesh.indexFormat = IndexFormat.UInt32;
            }
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = triangles.Length;
            for (int i = 0; i < triangles.Length; i++)
            {
                mesh.SetTriangles(triangles[i], i, false);
            }
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }
    }
}
