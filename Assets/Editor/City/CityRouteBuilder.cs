using System.Collections.Generic;
using KartIHC.Gameplay;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Id = KartIHC.EditorTools.CityMaterials.Id;

namespace KartIHC.EditorTools
{
    /// <summary>
    /// KartIHC/City/Route On∕Off: alterna entre el anillo original y el recorrido por la ciudad.
    ///
    /// On: crea la raíz de escena "EscapeRoute" con
    ///  - EscapeRouteDefinition (polilínea y ancho; la leen la barrera, la reaparición, Solo y la ciudad),
    ///  - City_RouteGround: solo BoxColliders (sin malla), cara superior en y 0 como EscapeGround,
    ///  - City_RouteWalls: muros bajos (2.3 m como las barreras del anillo, por debajo del vuelo del dron) visibles, con
    ///    colliders en la capa Ignore Raycast para que las patrullas (SphereCast/visión) no cambien,
    ///    incluido el cierre de la apertura norte del anillo fuera del carril de la ruta,
    ///  - City_RouteFinishMark: damero de la meta (solo visual),
    /// mueve EscapeFinishZone al final de la ruta (guardando su posición) y regenera manzanas y calles.
    /// Off: restaura la meta, borra EscapeRoute y regenera la ciudad del anillo.
    ///
    /// Únicos colliders nuevos del proyecto de la ciudad: suelo y muros bajos de la ruta (aprobados).
    /// No toca ModeSceneSetup: no correr "Kart IHC/Configurar escenas de modos" con la ruta activa.
    /// </summary>
    public static class CityRouteBuilder
    {
        public const string RootName = "EscapeRoute";
        private const string MenuPath = CityGen.MenuRoot + "Route On∕Off";

        // ===== Recorrido (ajustable; vuelve a correr Off y On) =====
        // Salida norte del anillo (x 0) → giro a la derecha antes de la comisaría (z 44) →
        // calle x 34 hacia el norte → calle z 68 hacia el este → meta en el cruce x 102.
        private static readonly Vector3[] RoutePoints =
        {
            new Vector3(0f, 0f, 30f), new Vector3(0f, 0f, 44f), new Vector3(34f, 0f, 44f),
            new Vector3(34f, 0f, 68f), new Vector3(102f, 0f, 68f),
        };
        private const float RouteWidth = 10f;          // ancho de las calles de la cuadrícula
        private const float BarrierDistance = 34f;     // primera calle: (20, 44)
        private const float FinishBeforeEnd = 8f;      // meta 8 m antes del final
        private const string MovedDoorName = "EscapeDoor_2"; // puerta 2 (id 1): pasa al cruce x 34 / z 68
        private const float DoorDistance = 82f;        // 10 m después del cruce: (44, 68)
        private const float PaintY = 0.05f;           // sobre el suelo urbano (0.03) y las líneas de calle (0.04)
        private const float EdgeLineInset = 0.55f;     // desde el eje del muro hacia dentro
        private const float LineWidth = 0.15f;
        private const float ArrowSpacing = 16f;
        // Igual que las barreras del anillo (2.3 × 0.6 m) y con su carcasa roja/blanca de P3: la caja
        // de colisión del kart va de 0.52 a 2.57 m sobre el suelo y un muro de 1.1 m se podía montar.
        // Sigue muy por debajo de la altura de vuelo del dron (12–20 m).
        private const float WallHeight = 2.3f;
        private const float WallThickness = 0.6f;
        private const float GroundExtra = 1.5f;        // suelo más allá de los muros
        private const float GroundThickness = 0.5f;
        private const float RingExitRadius = 32.8f;    // los muros empiezan fuera de la barrera exterior
        private const float RingClosureRadius = 32.4f; // centro de la barrera exterior (32.1–32.7)
        private const float OpeningFrom = 52.5f, OpeningTo = 127.5f; // tramos que OpenOuterBoundary desactiva
        // Hueco por donde salen las patrullas de la comisaría (z 52 → 30): ahí el muro es invisible.
        private static readonly Rect PoliceLane = Rect.MinMaxRect(-5.01f, 44f, 5.01f, 60f);
        private const int IgnoreRaycastLayer = 2;

        [MenuItem(MenuPath, priority = 320)]
        public static void Toggle()
        {
            if (!CityGen.EnsureEscapeScene())
            {
                return;
            }
            if (FindRoute() != null) TurnOff();
            else TurnOn();
        }

        [MenuItem(MenuPath, true)]
        private static bool ToggleValidate()
        {
            Menu.SetChecked(MenuPath, FindRoute() != null);
            return true;
        }

        private static EscapeRouteDefinition FindRoute() => Object.FindFirstObjectByType<EscapeRouteDefinition>();

        // ---------- On / Off ----------

        private static void TurnOn()
        {
            EscapeFinishZone finish = FindAuthoredFinish();
            if (finish == null)
            {
                Report("No se encontró EscapeFinishZone en la escena.");
                return;
            }

            EnvGen.BeginUndo("Route On");
            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Create " + RootName);
            EscapeRouteDefinition route = root.AddComponent<EscapeRouteDefinition>();
            float length = PolylineLength(RoutePoints);
            route.Configure(RoutePoints, RouteWidth, BarrierDistance, length - FinishBeforeEnd, DoorDistance);
            route.RememberFinish(finish.transform.position, finish.transform.rotation);
            MoveDoorToRoute(route);

            CityMaterials.BuildAll();
            BuildGround(root.transform, route);
            BuildWalls(root.transform, route);
            BuildFinishMark(root.transform, route);
            BuildPaint(root.transform, route);

            Undo.RecordObject(finish.transform, "Move finish to route");
            float y = finish.transform.position.y;
            finish.transform.SetPositionAndRotation(route.PointAt(route.FinishDistance) + Vector3.up * y,
                route.RotationAt(route.FinishDistance));
            EnvGen.EndUndo();

            CityBlocksBuilder.Build();
            CityStreetsBuilder.Build();
            CityGen.MarkSceneDirty();
            Debug.Log($"[City] Ruta activada: {length:0} m, barrera a {BarrierDistance:0} m, meta a {route.FinishDistance:0} m. " +
                      "Guarda la escena. No corras 'Kart IHC/Configurar escenas de modos' con la ruta activa.");
        }

        private static void TurnOff()
        {
            EscapeRouteDefinition route = FindRoute();
            EnvGen.BeginUndo("Route Off");
            EscapeFinishZone finish = FindAuthoredFinish();
            if (finish != null && route.HasOriginalFinish)
            {
                Undo.RecordObject(finish.transform, "Restore finish");
                finish.transform.SetPositionAndRotation(route.OriginalFinishPosition, route.OriginalFinishRotation);
            }
            RestoreDoor(route);
            Undo.DestroyObjectImmediate(route.gameObject);
            EnvGen.EndUndo();

            CityBlocksBuilder.Build();
            CityStreetsBuilder.Build();
            CityGen.MarkSceneDirty();
            Debug.Log("[City] Ruta desactivada: anillo original restaurado (meta en su sitio). Guarda la escena.");
        }

        // ---------- Puerta 2 ----------

        /// <summary>
        /// Lleva EscapeDoor_2 a la ruta con el pivote en el centro de la calle y la hoja centrada en él
        /// (local (0, y, 0), sin giro): así el alcance de hackeo (7 m desde el pivote en el bridge),
        /// el indicador y el minimapa apuntan a la calle. En la escena la hoja tenía un ajuste local
        /// propio (a ~14.6 m del pivote); se guarda y Route Off lo restaura junto con el pivote.
        /// Abierta, la hoja gira 90° sobre su centro y queda paralela a la calle (~4.7 m libres a cada lado).
        /// </summary>
        private static void MoveDoorToRoute(EscapeRouteDefinition route)
        {
            Transform pivot = GameObject.Find(MovedDoorName)?.transform;
            Transform panel = pivot != null ? (pivot.Find("Door Panel") ?? (pivot.childCount > 0 ? pivot.GetChild(0) : null)) : null;
            if (panel == null)
            {
                Debug.LogWarning($"[City] No se encontró {MovedDoorName}/Door Panel: la puerta 2 se queda en el anillo.");
                return;
            }

            route.RememberDoor(MovedDoorName, pivot.position, pivot.rotation, panel.localPosition, panel.localRotation);
            float at = route.DoorDistance;
            Undo.RecordObject(pivot, "Move door to route");
            Undo.RecordObject(panel, "Move door to route");
            pivot.SetPositionAndRotation(route.PointAt(at) + Vector3.up * pivot.position.y, route.RotationAt(at));
            panel.localPosition = new Vector3(0f, panel.localPosition.y, 0f);
            panel.localRotation = Quaternion.identity; // eje largo (X local) cruzando la calle
        }

        private static void RestoreDoor(EscapeRouteDefinition route)
        {
            if (string.IsNullOrEmpty(route.MovedDoorName)) return;
            Transform pivot = GameObject.Find(route.MovedDoorName)?.transform;
            if (pivot == null) return;
            Transform panel = pivot.Find("Door Panel") ?? (pivot.childCount > 0 ? pivot.GetChild(0) : null);
            Undo.RecordObject(pivot, "Restore door");
            pivot.SetPositionAndRotation(route.OriginalDoorPosition, route.OriginalDoorRotation);
            if (panel != null)
            {
                Undo.RecordObject(panel, "Restore door");
                panel.localPosition = route.OriginalPanelLocalPosition;
                panel.localRotation = route.OriginalPanelLocalRotation;
            }
        }

        private static EscapeFinishZone FindAuthoredFinish()
        {
            foreach (EscapeFinishZone zone in Object.FindObjectsByType<EscapeFinishZone>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (zone.gameObject.name == "EscapeFinishZone") return zone;
            return null;
        }

        // ---------- Suelo ----------

        private static void BuildGround(Transform root, EscapeRouteDefinition route)
        {
            var ground = new GameObject("City_RouteGround");
            ground.transform.SetParent(root, false);
            float half = route.HalfWidth + WallThickness + GroundExtra;
            for (int i = 1; i < route.PointCount; i++)
            {
                Vector3 a = route.GetPoint(i - 1), b = route.GetPoint(i);
                Vector3 dir = (b - a).normalized;
                float length = Vector3.Distance(a, b) + half * 2f; // solapa en los giros
                var piece = new GameObject($"City_RouteGround_{i}");
                piece.transform.SetParent(ground.transform, false);
                piece.transform.SetPositionAndRotation((a + b) * 0.5f + Vector3.down * (GroundThickness * 0.5f),
                    Quaternion.LookRotation(dir, Vector3.up));
                BoxCollider box = piece.AddComponent<BoxCollider>();
                box.size = new Vector3(half * 2f, GroundThickness, length);
            }
        }

        // ---------- Muros ----------

        private struct WallPiece
        {
            public Vector3 From, To;
            public bool Visible;
        }

        private static void BuildWalls(Transform root, EscapeRouteDefinition route)
        {
            var pieces = new List<WallPiece>();
            List<Vector3> left = OffsetPolyline(route, +1f);
            List<Vector3> right = OffsetPolyline(route, -1f);

            // Se alargan media calle más allá del final y se cierra con un muro transversal.
            Vector3 endDir = route.DirectionAt(route.Length);
            left[left.Count - 1] += endDir * route.HalfWidth;
            right[right.Count - 1] += endDir * route.HalfWidth;
            // Empiezan fuera de la barrera exterior del anillo.
            left[0] = ExitPoint(left[0], left[1]);
            right[0] = ExitPoint(right[0], right[1]);

            AddPolyline(pieces, left);
            AddPolyline(pieces, right);
            pieces.Add(new WallPiece { From = left[left.Count - 1], To = right[right.Count - 1], Visible = true });

            // Cierre de la apertura norte del anillo (OpenOuterBoundary desactiva esos tramos en juego)
            // a ambos lados del carril de la ruta.
            float laneAngle = Mathf.Acos(Mathf.Clamp(route.HalfWidth / RingClosureRadius, -1f, 1f)) * Mathf.Rad2Deg;
            AddArc(pieces, OpeningFrom, laneAngle);
            AddArc(pieces, 180f - laneAngle, OpeningTo);

            var wallsRoot = new GameObject("City_RouteWalls");
            wallsRoot.transform.SetParent(root, false);
            var colliders = new GameObject("City_RouteWallColliders");
            colliders.transform.SetParent(wallsRoot.transform, false);
            var mesh = new CityMeshBuilder(1);
            int index = 0;
            foreach (WallPiece piece in pieces)
            {
                Vector3 delta = piece.To - piece.From;
                float length = delta.magnitude;
                if (length < 0.05f) continue;
                Vector3 dir = delta / length;
                Vector3 center = (piece.From + piece.To) * 0.5f;

                var go = new GameObject($"City_RouteWall_{++index:00}{(piece.Visible ? "" : "_PoliceLane")}");
                go.transform.SetParent(colliders.transform, false);
                go.transform.SetPositionAndRotation(center + Vector3.up * (WallHeight * 0.5f), Quaternion.LookRotation(dir, Vector3.up));
                go.layer = IgnoreRaycastLayer;
                BoxCollider box = go.AddComponent<BoxCollider>();
                box.size = new Vector3(WallThickness, WallHeight, length + WallThickness); // solapa en esquinas

                if (piece.Visible) AddWallMesh(mesh, center, dir, length + WallThickness);
            }

            Mesh wallMesh = CityGen.SaveMesh(mesh.ToMesh("City_RouteWalls"), "City_RouteWalls");
            GameObject visual = CityGen.CreateRenderer(wallsRoot.transform, "City_RouteWallsVisual", wallMesh,
                new[] { CityMaterials.Get(Id.Barrier) }, ShadowCastingMode.On);
            Undo.RegisterCreatedObjectUndo(visual, "Route walls");
        }

        private static void AddPolyline(List<WallPiece> pieces, List<Vector3> line)
        {
            for (int i = 1; i < line.Count; i++)
            {
                foreach (WallPiece piece in SplitAtPoliceLane(line[i - 1], line[i]))
                    pieces.Add(piece);
            }
        }

        /// <summary>Parte un muro donde cruza el carril de las patrullas: ese trozo es invisible.</summary>
        private static IEnumerable<WallPiece> SplitAtPoliceLane(Vector3 a, Vector3 b)
        {
            bool horizontal = Mathf.Abs(a.z - b.z) < 0.01f;
            if (!horizontal || a.z < PoliceLane.yMin || a.z > PoliceLane.yMax)
            {
                yield return new WallPiece { From = a, To = b, Visible = true };
                yield break;
            }

            float x0 = Mathf.Min(a.x, b.x), x1 = Mathf.Max(a.x, b.x), z = a.z;
            float laneMin = Mathf.Clamp(PoliceLane.xMin, x0, x1), laneMax = Mathf.Clamp(PoliceLane.xMax, x0, x1);
            if (laneMin > x0) yield return new WallPiece { From = new Vector3(x0, 0f, z), To = new Vector3(laneMin, 0f, z), Visible = true };
            if (laneMax > laneMin) yield return new WallPiece { From = new Vector3(laneMin, 0f, z), To = new Vector3(laneMax, 0f, z), Visible = false };
            if (x1 > laneMax) yield return new WallPiece { From = new Vector3(laneMax, 0f, z), To = new Vector3(x1, 0f, z), Visible = true };
        }

        private static void AddArc(List<WallPiece> pieces, float fromDegrees, float toDegrees)
        {
            const int steps = 5;
            for (int i = 0; i < steps; i++)
            {
                float a0 = Mathf.Lerp(fromDegrees, toDegrees, i / (float)steps) * Mathf.Deg2Rad;
                float a1 = Mathf.Lerp(fromDegrees, toDegrees, (i + 1) / (float)steps) * Mathf.Deg2Rad;
                pieces.Add(new WallPiece
                {
                    From = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * RingClosureRadius,
                    To = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * RingClosureRadius,
                    Visible = true,
                });
            }
        }

        /// <summary>Polilínea desplazada medio ancho (+1 izquierda, −1 derecha) con esquinas a inglete.</summary>
        private static List<Vector3> OffsetPolyline(EscapeRouteDefinition route, float side) =>
            OffsetPolyline(route, side, route.HalfWidth);

        private static List<Vector3> OffsetPolyline(EscapeRouteDefinition route, float side, float offset)
        {
            var result = new List<Vector3>();
            int count = route.PointCount;
            for (int i = 0; i < count; i++)
            {
                Vector3 p = route.GetPoint(i);
                Vector3 nPrev = i > 0 ? LeftNormal(route.GetPoint(i - 1), p) : LeftNormal(p, route.GetPoint(i + 1));
                Vector3 nNext = i < count - 1 ? LeftNormal(p, route.GetPoint(i + 1)) : nPrev;
                Vector3 miter = (nPrev + nNext).normalized;
                float scale = offset / Mathf.Max(0.2f, Vector3.Dot(miter, nPrev));
                result.Add(p + miter * (scale * side));
            }
            return result;
        }

        private static Vector3 LeftNormal(Vector3 a, Vector3 b)
        {
            Vector3 d = (b - a).normalized;
            return new Vector3(-d.z, 0f, d.x);
        }

        /// <summary>Avanza desde 'from' hacia 'to' hasta quedar fuera de la barrera exterior del anillo.</summary>
        private static Vector3 ExitPoint(Vector3 from, Vector3 to)
        {
            Vector3 dir = (to - from).normalized;
            float length = Vector3.Distance(from, to);
            for (float t = 0f; t <= length; t += 0.05f)
            {
                Vector3 p = from + dir * t;
                if (new Vector2(p.x, p.z).magnitude >= RingExitRadius) return p;
            }
            return from;
        }

        /// <summary>Muro como caja orientada: caras laterales, tapa y extremos; UV de la textura de barrera.</summary>
        private static void AddWallMesh(CityMeshBuilder mesh, Vector3 center, Vector3 dir, float length)
        {
            Vector3 right = new Vector3(dir.z, 0f, -dir.x); // derecha al mirar en 'dir'
            Vector3 up = Vector3.up;
            float hw = WallThickness * 0.5f, hh = WallHeight * 0.5f, hl = length * 0.5f;
            Vector3 c = center + up * hh;
            // Mismo mapeo que las carcasas de barrera de P3: u = metros / 2, v = altura / 2.3.
            float u = length / CityMaterials.BarrierTileLength;
            float v = WallHeight / CityMaterials.BarrierTileHeight;
            float uEnd = WallThickness / CityMaterials.BarrierTileLength;
            Face(mesh, c + right * hw, dir, hl, up, hh, Vector2.zero, new Vector2(u, v));
            Face(mesh, c - right * hw, -dir, hl, up, hh, Vector2.zero, new Vector2(u, v));
            Face(mesh, c + dir * hl, -right, hw, up, hh, Vector2.zero, new Vector2(uEnd, v));
            Face(mesh, c - dir * hl, right, hw, up, hh, Vector2.zero, new Vector2(uEnd, v));
            Face(mesh, c + up * hh, right, hw, dir, hl, new Vector2(0f, 0.4f), new Vector2(0.3f, 0.55f));
        }

        /// <summary>Cara rectangular centrada; normal = cross(upDir, rightDir).</summary>
        private static void Face(CityMeshBuilder b, Vector3 center, Vector3 rightDir, float halfRight, Vector3 upDir, float halfUp,
            Vector2 uvMin, Vector2 uvMax)
        {
            Vector3 r = rightDir * halfRight, u = upDir * halfUp;
            b.Quad(0, center - r - u, center + r - u, center + r + u, center - r + u,
                uvMin, new Vector2(uvMax.x, uvMin.y), uvMax, new Vector2(uvMin.x, uvMax.y));
        }

        // ---------- Meta ----------

        private static void BuildFinishMark(Transform root, EscapeRouteDefinition route)
        {
            const float square = 1f, y = 0.06f;
            float at = route.FinishDistance;
            Vector3 center = route.PointAt(at);
            Vector3 dir = route.DirectionAt(at);
            Vector3 right = new Vector3(dir.z, 0f, -dir.x);
            int columns = Mathf.FloorToInt(route.Width / square);
            var mesh = new CityMeshBuilder(1);
            for (int row = 0; row < 2; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    if ((row + column) % 2 != 0) continue;
                    Vector3 a = center + right * ((column - columns * 0.5f) * square) + dir * ((row - 1) * square) + Vector3.up * y;
                    Vector3 bl = a, br = a + right * square, tr = br + dir * square, tl = a + dir * square;
                    // Visto desde arriba con 'dir' hacia arriba en pantalla, 'right' queda a la derecha: normal +Y.
                    mesh.Quad(0, bl, br, tr, tl, Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
                }
            }
            Mesh finishMesh = CityGen.SaveMesh(mesh.ToMesh("City_RouteFinishMark"), "City_RouteFinishMark");
            CityGen.CreateRenderer(root, "City_RouteFinishMark", finishMesh,
                new[] { CityMaterials.Get(Id.PaintWhite) }, ShadowCastingMode.Off);
        }

        // ---------- Pintura ----------

        /// <summary>Líneas de borde, flechas de sentido y líneas de detención antes de la barrera y la puerta.</summary>
        private static void BuildPaint(Transform root, EscapeRouteDefinition route)
        {
            var mesh = new CityMeshBuilder(1);
            float inset = route.HalfWidth - EdgeLineInset;
            foreach (float side in new[] { 1f, -1f })
            {
                List<Vector3> line = OffsetPolyline(route, side, inset);
                line[0] = ExitPoint(line[0], line[1]);
                line[line.Count - 1] += route.DirectionAt(route.Length) * inset;
                for (int i = 1; i < line.Count; i++) PaintStrip(mesh, line[i - 1], line[i], LineWidth);
            }

            foreach (float gate in new[] { route.BarrierDistance, route.DoorDistance })
            {
                float at = gate - 3f;
                Vector3 c = route.PointAt(at), right = Right(route.DirectionAt(at));
                PaintStrip(mesh, c - right * inset, c + right * inset, 0.4f);
            }

            for (float at = 10f; at < route.FinishDistance - 8f; at += ArrowSpacing)
            {
                if (Mathf.Abs(at - route.BarrierDistance) < 8f || Mathf.Abs(at - route.DoorDistance) < 8f) continue;
                PaintArrow(mesh, route.PointAt(at), route.DirectionAt(at));
            }

            Mesh paint = CityGen.SaveMesh(mesh.ToMesh("City_RoutePaint"), "City_RoutePaint");
            CityGen.CreateRenderer(root, "City_RoutePaint", paint, new[] { CityMaterials.Get(Id.PaintWhite) }, ShadowCastingMode.Off);
        }

        private static void PaintStrip(CityMeshBuilder mesh, Vector3 from, Vector3 to, float width)
        {
            Vector3 dir = (to - from).normalized;
            Vector3 r = Right(dir) * (width * 0.5f);
            Vector3 up = Vector3.up * PaintY;
            FlatQuad(mesh, from - r + up, from + r + up, to + r + up, to - r + up);
        }

        private static void PaintArrow(CityMeshBuilder mesh, Vector3 center, Vector3 dir)
        {
            Vector3 r = Right(dir), up = Vector3.up * PaintY;
            Vector3 tail = center - dir * 1.5f, neck = center + dir * 0.5f, tip = center + dir * 1.7f;
            FlatQuad(mesh, tail - r * 0.15f + up, tail + r * 0.15f + up, neck + r * 0.15f + up, neck - r * 0.15f + up);
            FlatTriangle(mesh, neck - r * 0.6f + up, tip + up, neck + r * 0.6f + up);
        }

        /// <summary>Quad horizontal con normal hacia arriba sea cual sea el orden de los vértices.</summary>
        private static void FlatQuad(CityMeshBuilder mesh, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
        {
            if (Vector3.Cross(p3 - p0, p1 - p0).y < 0f) (p1, p3) = (p3, p1);
            mesh.Quad(0, p0, p1, p2, p3, Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
        }

        private static void FlatTriangle(CityMeshBuilder mesh, Vector3 a, Vector3 b, Vector3 c)
        {
            if (Vector3.Cross(b - a, c - a).y < 0f) (b, c) = (c, b);
            mesh.Triangle(0, a, b, c, Vector3.up, Vector2.zero, Vector2.right, Vector2.up);
        }

        private static Vector3 Right(Vector3 dir) => new Vector3(dir.z, 0f, -dir.x);

        // ---------- Utilidades ----------

        private static float PolylineLength(Vector3[] points)
        {
            float length = 0f;
            for (int i = 1; i < points.Length; i++) length += Vector3.Distance(points[i - 1], points[i]);
            return length;
        }

        private static void Report(string message)
        {
            Debug.LogError("[City] " + message);
            if (!Application.isBatchMode) EditorUtility.DisplayDialog("KartIHC City · Ruta", message, "OK");
        }
    }
}
