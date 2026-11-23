using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Id = KartIHC.EditorTools.CityMaterials.Id;

namespace KartIHC.EditorTools
{
    /// <summary>
    /// Etapa 1 de la ciudad: calles (todo visual, sin colliders).
    /// - City_Ground: suelo urbano de asfalto a y 0.03, por encima de EscapeGround (y 0), con un
    ///   hueco circular para la plaza central. Tapa ExitRoad de Diego (y −0.02), que no tiene
    ///   collider; el kart sigue rodando sobre EscapeGround.
    /// - City_Streets: asfalto más oscuro sobre la pista (y 0.10, 1 cm sobre EscapeRoad), líneas de
    ///   borde, línea central discontinua, damero de meta, líneas de detención antes de las puertas
    ///   y de la barrera policial, aceras con bordillo a ambos lados del anillo, carcasa visual de
    ///   las barreras, líneas y pasos de cebra en la cuadrícula y líneas amarillas del carril norte.
    ///
    /// Nombres: nada contiene "OuterBarrier", "EscapeRoad_" ni "ExitRoad" (los buscan
    /// EscapeTrackGeometry, EscapeMinimap, EscapeKartRespawn y OpenOuterBoundary).
    /// </summary>
    public static class CityStreetsBuilder
    {
        public const string GroundSection = "City_Ground";
        public const string StreetsSection = "City_Streets";
        public const string BarrierShellsName = "City_BarrierShells"; // el validador de visión la ignora

        // ===== Valores para ajustar rápido (vuelve a correr "1 Streets") =====
        private const float GroundY = 0.03f;
        private const float GroundHalfSize = 300f;
        private const float GroundHoleRadius = CityZones.InnerBarrierInnerFace; // la plaza y su acera quedan dentro
        private const float RingRoadY = 0.10f;      // EscapeRoad tiene su cara superior en y 0.09
        private const float RingPaintY = 0.11f;
        private const float StreetPaintY = 0.04f;
        private const float SpurPaintY = 0.05f;     // por encima de la explanada de la comisaría (0.045)
        private const float CurbHeight = 0.15f;
        private const float RoadInner = 18f, RoadOuter = 32f;
        private const float InnerSidewalkInner = CityZones.CenterFreeRadius; // 15.3
        private const float OuterSidewalkOuter = 34f;  // patrullas en r 36 con coches de 2 m: libre desde 35
        private const float NorthGapHalfAngle = 15f;   // hueco de acera en el carril norte (x ±8 a r 33)
        private const float EdgeLineWidth = 0.2f;
        private const float EdgeLineInset = 0.45f;
        private const float CenterLineWidth = 0.18f;
        private const float DashLength = 3f;
        private const float StopLineDegreesBefore = 3.5f; // antes de la puerta en sentido de carrera (antihorario)
        private const float StopLineWidth = 0.5f;
        private const float CheckerSize = 0.7f;
        private const float BarrierShellMargin = 0.015f;
        private const int RingSegments = 96;

        private const float StreetPitch = 34f, StreetHalfWidth = 5f;
        private const int GridHalf = 4;

        private static readonly Id[] Materials = { Id.RoadRing, Id.Sidewalk, Id.PaintWhite, Id.PaintYellow, Id.Barrier };
        private const int SubRing = 0, SubSidewalk = 1, SubWhite = 2, SubYellow = 3, SubBarrier = 4;

        [MenuItem(CityGen.MenuRoot + "1 Streets", priority = 301)]
        public static void Build()
        {
            if (!CityGen.EnsureEscapeScene())
            {
                return;
            }

            EnvGen.BeginUndo("City Streets");
            int triangles = 0, shells = 0;
            try
            {
                CityMaterials.BuildAll();
                List<BarrierPiece> barriers = FindBarrierPieces();
                CityGen.ReplacePrefabSections(
                    (GroundSection, section => triangles += BuildGround(section)),
                    (StreetsSection, section => triangles += BuildStreets(section, barriers, out shells)));
                CityGen.EnsureSingleSceneInstance();
            }
            finally
            {
                EnvGen.EndUndo();
            }

            CityGen.MarkSceneDirty();
            AssetDatabase.SaveAssets();
            Debug.Log($"[City] Calles: {triangles} triángulos, {shells} carcasas de barrera. Guarda la escena.");
        }

        // ---------- Suelo ----------

        private static int BuildGround(Transform section)
        {
            const int segments = 64; // múltiplo de 8: las esquinas del cuadrado caen en bordes de segmento
            var builder = new CityMeshBuilder(1);
            float uv = 1f / CityMaterials.AsphaltTile;
            for (int i = 0; i < segments; i++)
            {
                float a0 = i * 360f / segments, a1 = (i + 1) * 360f / segments;
                Vector3 c0 = Polar(a0, GroundHoleRadius, GroundY), c1 = Polar(a1, GroundHoleRadius, GroundY);
                Vector3 s0 = SquareEdge(a0, GroundHalfSize, GroundY), s1 = SquareEdge(a1, GroundHalfSize, GroundY);
                builder.Quad(0, c0, s0, s1, c1, Uv(c0, uv), Uv(s0, uv), Uv(s1, uv), Uv(c1, uv));
            }
            Mesh mesh = CityGen.SaveMesh(builder.ToMesh("City_Ground"), "City_Ground");
            CityGen.CreateRenderer(section, "City_GroundPlane", mesh, new[] { CityMaterials.Get(Id.Asphalt) }, ShadowCastingMode.Off);
            return builder.TriangleCount;
        }

        // ---------- Calles ----------

        private static int BuildStreets(Transform section, List<BarrierPiece> barriers, out int shellCount)
        {
            var b = new CityMeshBuilder(Materials.Length);
            float asphaltUv = 1f / CityMaterials.AsphaltTile;
            float sidewalkUv = 1f / CityMaterials.SidewalkTile;

            // Pista: asfalto oscuro y pintura.
            Band(b, SubRing, RoadInner, RoadOuter, 0f, 360f, RingRoadY, asphaltUv);
            Band(b, SubWhite, RoadInner + EdgeLineInset, RoadInner + EdgeLineInset + EdgeLineWidth, 0f, 360f, RingPaintY, 1f);
            Band(b, SubWhite, RoadOuter - EdgeLineInset - EdgeLineWidth, RoadOuter - EdgeLineInset, 0f, 360f, RingPaintY, 1f);
            DashedArc(b, SubWhite, (RoadInner + RoadOuter) * 0.5f, CenterLineWidth, RingPaintY, skipAround: 0f, skipHalfAngle: 4f);
            Checker(b, 0f);
            foreach (float door in CityZones.DoorAngles) StopLine(b, door - StopLineDegreesBefore);
            StopLine(b, CityZones.PoliceBarrierAngle - StopLineDegreesBefore);

            // Aceras con bordillo: interior (junto a la plaza) y exterior (hueco en el carril norte).
            Band(b, SubSidewalk, InnerSidewalkInner, CityZones.InnerBarrierInnerFace, 0f, 360f, CurbHeight, sidewalkUv);
            CurbRing(b, InnerSidewalkInner, 0f, 360f, facingOut: false);
            float gapStart = 90f - NorthGapHalfAngle, gapEnd = 90f + NorthGapHalfAngle;
            Band(b, SubSidewalk, CityZones.OuterBarrierOuterFace, OuterSidewalkOuter, gapEnd, gapStart + 360f, CurbHeight, sidewalkUv);
            CurbRing(b, OuterSidewalkOuter, gapEnd, gapStart + 360f, facingOut: true);
            CurbEnd(b, gapStart, CityZones.OuterBarrierOuterFace, OuterSidewalkOuter, facingCcw: true);
            CurbEnd(b, gapEnd, CityZones.OuterBarrierOuterFace, OuterSidewalkOuter, facingCcw: false);

            // Cuadrícula y carril norte.
            GridStreets(b);
            SpurLines(b);

            // Carcasas de las barreras (malla aparte: el validador de visión las ignora porque
            // coinciden con las barreras reales).
            var shell = new CityMeshBuilder(1);
            shellCount = 0;
            foreach (BarrierPiece piece in barriers)
            {
                AddBarrierShell(shell, piece);
                shellCount++;
            }

            Mesh mesh = CityGen.SaveMesh(b.ToMesh("City_Streets"), "City_Streets");
            CityGen.CreateRenderer(section, "City_StreetSurface", mesh, CityMaterials.Get(Materials), ShadowCastingMode.Off);
            Mesh shellMesh = CityGen.SaveMesh(shell.ToMesh(BarrierShellsName), BarrierShellsName);
            CityGen.CreateRenderer(section, BarrierShellsName, shellMesh, new[] { CityMaterials.Get(Id.Barrier) }, ShadowCastingMode.Off);
            return b.TriangleCount + shell.TriangleCount;
        }

        /// <summary>Banda horizontal entre radios r0 y r1, de a0 a a1 grados (antihorario).</summary>
        private static void Band(CityMeshBuilder b, int submesh, float r0, float r1, float a0, float a1, float y, float uvScale)
        {
            int steps = Mathf.Max(1, Mathf.CeilToInt((a1 - a0) / 360f * RingSegments));
            for (int i = 0; i < steps; i++)
            {
                float s0 = Mathf.Lerp(a0, a1, i / (float)steps), s1 = Mathf.Lerp(a0, a1, (i + 1) / (float)steps);
                Vector3 in0 = Polar(s0, r0, y), out0 = Polar(s0, r1, y), out1 = Polar(s1, r1, y), in1 = Polar(s1, r0, y);
                b.Quad(submesh, in0, out0, out1, in1, Uv(in0, uvScale), Uv(out0, uvScale), Uv(out1, uvScale), Uv(in1, uvScale));
            }
        }

        private static void CurbRing(CityMeshBuilder b, float radius, float a0, float a1, bool facingOut)
        {
            int steps = Mathf.Max(1, Mathf.CeilToInt((a1 - a0) / 360f * RingSegments));
            float uv = 1f / CityMaterials.SidewalkTile;
            for (int i = 0; i < steps; i++)
            {
                float s0 = Mathf.Lerp(a0, a1, i / (float)steps), s1 = Mathf.Lerp(a0, a1, (i + 1) / (float)steps);
                Vector3 p0 = Polar(s0, radius, GroundY), p1 = Polar(s1, radius, GroundY);
                float u0 = s0 * Mathf.Deg2Rad * radius * uv, u1 = s1 * Mathf.Deg2Rad * radius * uv;
                float h = CurbHeight - GroundY;
                // De p0 a p1 en sentido antihorario la normal apunta hacia fuera.
                if (facingOut) b.Wall(SubSidewalk, p0, p1, h, new Vector2(u0, 0f), new Vector2(u1, h * uv));
                else b.Wall(SubSidewalk, p1, p0, h, new Vector2(u1, 0f), new Vector2(u0, h * uv));
            }
        }

        /// <summary>Cara radial del bordillo en el extremo de un tramo de acera.</summary>
        private static void CurbEnd(CityMeshBuilder b, float angle, float r0, float r1, bool facingCcw)
        {
            Vector3 inner = Polar(angle, r0, GroundY), outer = Polar(angle, r1, GroundY);
            float h = CurbHeight - GroundY;
            // Caminando de dentro hacia fuera, la normal queda a la derecha = sentido horario.
            if (facingCcw) b.Wall(SubSidewalk, outer, inner, h, Vector2.zero, new Vector2(r1 - r0, h) * 0.5f);
            else b.Wall(SubSidewalk, inner, outer, h, Vector2.zero, new Vector2(r1 - r0, h) * 0.5f);
        }

        private static void DashedArc(CityMeshBuilder b, int submesh, float radius, float width, float y,
            float skipAround, float skipHalfAngle)
        {
            float dashDegrees = DashLength / radius * Mathf.Rad2Deg;
            for (float a = 0f; a < 360f - 0.01f; a += dashDegrees * 2f)
            {
                if (Mathf.Abs(Mathf.DeltaAngle(a + dashDegrees * 0.5f, skipAround)) < skipHalfAngle) continue;
                Band(b, submesh, radius - width * 0.5f, radius + width * 0.5f, a, a + dashDegrees, y, 1f);
            }
        }

        /// <summary>Damero de dos filas en la meta (EscapeFinishZone está en (25, 0, 0) = 0°).</summary>
        private static void Checker(CityMeshBuilder b, float angle)
        {
            float mid = (RoadInner + RoadOuter) * 0.5f;
            float step = CheckerSize / mid * Mathf.Rad2Deg;
            int columns = Mathf.FloorToInt((RoadOuter - RoadInner) / CheckerSize);
            float r0 = mid - columns * CheckerSize * 0.5f;
            for (int row = 0; row < 2; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    if ((row + column) % 2 != 0) continue;
                    float a0 = angle - step + row * step;
                    float rr = r0 + column * CheckerSize;
                    Band(b, SubWhite, rr, rr + CheckerSize, a0, a0 + step, RingPaintY, 1f);
                }
            }
        }

        private static void StopLine(CityMeshBuilder b, float angle)
        {
            float mid = (RoadInner + RoadOuter) * 0.5f;
            float half = StopLineWidth * 0.5f / mid * Mathf.Rad2Deg;
            Band(b, SubWhite, RoadInner + EdgeLineInset, RoadOuter - EdgeLineInset, angle - half, angle + half, RingPaintY, 1f);
        }

        /// <summary>Líneas discontinuas en las calles de la cuadrícula y pasos de cebra en los cruces.</summary>
        private static void GridStreets(CityMeshBuilder b)
        {
            for (int k = -GridHalf; k <= GridHalf; k++)
            {
                float line = k * StreetPitch;
                for (int j = -GridHalf; j < GridHalf; j++)
                {
                    float from = j * StreetPitch + StreetHalfWidth, to = from + StreetPitch - 2f * StreetHalfWidth;
                    // Calle norte-sur (x = line) y este-oeste (z = line).
                    var northSouth = Rect.MinMaxRect(line - StreetHalfWidth, from, line + StreetHalfWidth, to);
                    var eastWest = Rect.MinMaxRect(from, line - StreetHalfWidth, to, line + StreetHalfWidth);
                    if (StreetAllowed(northSouth)) DashedStraight(b, new Vector3(line, StreetPaintY, from), Vector3.forward, to - from);
                    if (StreetAllowed(eastWest)) DashedStraight(b, new Vector3(from, StreetPaintY, line), Vector3.right, to - from);
                }

                for (int j = -GridHalf; j <= GridHalf; j++)
                {
                    Vector3 cross = new Vector3(line, StreetPaintY, j * StreetPitch);
                    Zebra(b, cross, Vector3.forward);
                    Zebra(b, cross, Vector3.back);
                    Zebra(b, cross, Vector3.right);
                    Zebra(b, cross, Vector3.left);
                }
            }
        }

        private static bool StreetAllowed(Rect street) =>
            CityZones.MinDistanceToOrigin(street) >= CityZones.BuildingMinRadius &&
            !street.Overlaps(CityZones.NorthCorridor) && !street.Overlaps(CityZones.StationArea) &&
            Mathf.Max(Mathf.Abs(street.xMin), Mathf.Abs(street.xMax), Mathf.Abs(street.yMin), Mathf.Abs(street.yMax)) <=
            GridHalf * StreetPitch;

        private static void DashedStraight(CityMeshBuilder b, Vector3 start, Vector3 direction, float length)
        {
            Vector3 side = new Vector3(direction.z, 0f, -direction.x) * (CenterLineWidth * 0.5f);
            int dashes = Mathf.FloorToInt((length + DashLength) / (DashLength * 2f));
            float used = dashes * DashLength * 2f - DashLength;
            float offset = (length - used) * 0.5f;
            for (int i = 0; i < dashes; i++)
            {
                Vector3 a = start + direction * (offset + i * DashLength * 2f);
                Vector3 c = a + direction * DashLength;
                FlatQuad(b, SubWhite, a - side, a + side, c + side, c - side);
            }
        }

        /// <summary>Paso de cebra en la entrada de un cruce, en la calle que sale hacia 'direction'.</summary>
        private static void Zebra(CityMeshBuilder b, Vector3 cross, Vector3 direction)
        {
            const float start = 5.6f, length = 3f, stripe = 0.5f, span = 4.2f;
            Vector3 side = new Vector3(direction.z, 0f, -direction.x);
            Vector3 a = cross + direction * start;
            Vector3 c = a + direction * length;
            Vector2 a2 = new Vector2(a.x, a.z), c2 = new Vector2(c.x, c.z);
            Vector2 s2 = new Vector2(side.x, side.z) * span;
            Rect area = Rect.MinMaxRect(
                Mathf.Min(a2.x - Mathf.Abs(s2.x), c2.x - Mathf.Abs(s2.x)), Mathf.Min(a2.y - Mathf.Abs(s2.y), c2.y - Mathf.Abs(s2.y)),
                Mathf.Max(a2.x + Mathf.Abs(s2.x), c2.x + Mathf.Abs(s2.x)), Mathf.Max(a2.y + Mathf.Abs(s2.y), c2.y + Mathf.Abs(s2.y)));
            if (!StreetAllowed(area)) return;

            for (float s = -span + stripe * 0.5f; s < span; s += stripe * 2f)
            {
                Vector3 o0 = side * (s - stripe * 0.5f), o1 = side * (s + stripe * 0.5f);
                FlatQuad(b, SubWhite, a + o0, a + o1, c + o1, c + o0);
            }
        }

        /// <summary>Líneas amarillas del carril norte (como las de Diego, que quedan bajo el suelo urbano).</summary>
        private static void SpurLines(CityMeshBuilder b)
        {
            const float width = 0.15f, zStart = 33.2f, zEnd = 59.4f;
            foreach (float x in new[] { -2f, 2f })
            {
                b.Top(SubYellow, Rect.MinMaxRect(x - width * 0.5f, zStart, x + width * 0.5f, zEnd), SpurPaintY, 1f);
            }
        }

        /// <summary>Quad horizontal con normal hacia arriba sea cual sea el orden de los vértices.</summary>
        private static void FlatQuad(CityMeshBuilder b, int submesh, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
        {
            if (Vector3.Cross(p3 - p0, p1 - p0).y < 0f)
            {
                (p1, p3) = (p3, p1);
            }
            b.Quad(submesh, p0, p1, p2, p3, Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
        }

        // ---------- Carcasas de barrera ----------

        private struct BarrierPiece
        {
            public Vector3 Center, Right, Forward;
            public Vector3 HalfSize; // x radial, y alto, z largo
            public float Angle;
        }

        /// <summary>
        /// Tramos de EscapeInnerBarrier_/EscapeOuterBarrier_ de la escena. Se omiten los exteriores
        /// que OpenOuterBoundary desactiva (z ≥ 25 y ángulo 55°–125°): en juego desaparecen y la
        /// carcasa quedaría como un muro fantasma.
        /// </summary>
        private static List<BarrierPiece> FindBarrierPieces()
        {
            var pieces = new List<BarrierPiece>();
            foreach (Transform item in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                string name = item.name;
                bool inner = name.StartsWith("EscapeInnerBarrier_", StringComparison.Ordinal);
                bool outer = name.StartsWith("EscapeOuterBarrier_", StringComparison.Ordinal);
                if (!inner && !outer) continue;

                Vector3 p = item.position;
                float angle = Mathf.Atan2(p.z, p.x) * Mathf.Rad2Deg;
                if (angle < 0f) angle += 360f;
                if (outer && p.z >= 25f && angle > 55f && angle < 125f) continue;

                pieces.Add(new BarrierPiece
                {
                    Center = p,
                    Right = item.right,
                    Forward = item.forward,
                    HalfSize = item.lossyScale * 0.5f,
                    Angle = angle,
                });
            }
            pieces.Sort((x, y) => x.Angle.CompareTo(y.Angle));
            return pieces;
        }

        private static void AddBarrierShell(CityMeshBuilder b, BarrierPiece piece)
        {
            Vector3 up = Vector3.up;
            float hw = piece.HalfSize.x + BarrierShellMargin;
            float hh = piece.HalfSize.y + BarrierShellMargin;
            float hl = piece.HalfSize.z;
            Vector3 c = piece.Center;
            float radius = new Vector2(c.x, c.z).magnitude;
            float uMid = piece.Angle * Mathf.Deg2Rad * radius / CityMaterials.BarrierTileLength;
            float uHalf = hl / CityMaterials.BarrierTileLength;
            float vTop = (2f * hh) / CityMaterials.BarrierTileHeight;

            // Caras laterales (fuera y dentro), extremos y tapa.
            Face(b, c + piece.Right * hw, piece.Forward, hl, up, hh, new Vector2(uMid - uHalf, 0f), new Vector2(uMid + uHalf, vTop));
            Face(b, c - piece.Right * hw, -piece.Forward, hl, up, hh, new Vector2(uMid - uHalf, 0f), new Vector2(uMid + uHalf, vTop));
            Face(b, c + piece.Forward * hl, -piece.Right, hw, up, hh, new Vector2(0f, 0f), new Vector2(hw, vTop));
            Face(b, c - piece.Forward * hl, piece.Right, hw, up, hh, new Vector2(0f, 0f), new Vector2(hw, vTop));
            Face(b, c + up * hh, piece.Right, hw, piece.Forward, hl, new Vector2(0f, 0.4f), new Vector2(0.3f, 0.55f));
        }

        /// <summary>Cara rectangular centrada; normal = cross(upDir, rightDir).</summary>
        private static void Face(CityMeshBuilder b, Vector3 center, Vector3 rightDir, float halfRight, Vector3 upDir, float halfUp,
            Vector2 uvMin, Vector2 uvMax)
        {
            Vector3 r = rightDir * halfRight, u = upDir * halfUp;
            b.Quad(0, center - r - u, center + r - u, center + r + u, center - r + u,
                uvMin, new Vector2(uvMax.x, uvMin.y), uvMax, new Vector2(uvMin.x, uvMax.y));
        }

        // ---------- Utilidades ----------

        private static Vector3 Polar(float degrees, float radius, float y)
        {
            float a = degrees * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(a) * radius, y, Mathf.Sin(a) * radius);
        }

        private static Vector3 SquareEdge(float degrees, float half, float y)
        {
            float a = degrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(a), sin = Mathf.Sin(a);
            float t = half / Mathf.Max(Mathf.Abs(cos), Mathf.Abs(sin));
            return new Vector3(cos * t, y, sin * t);
        }

        private static Vector2 Uv(Vector3 p, float scale) => new Vector2(p.x, p.z) * scale;
    }
}
