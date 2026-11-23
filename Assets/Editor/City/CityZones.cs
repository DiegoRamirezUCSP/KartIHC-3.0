using System.Collections.Generic;
using KartIHC.Gameplay;
using UnityEngine;

namespace KartIHC.EditorTools
{
    /// <summary>
    /// Zonas libres de EscapeCourse que toda la ciudad debe respetar (plano XZ, y arriba;
    /// azimut 0 = +X, 90 = +Z, como EscapeTrackGeometry). Los valores salen de
    /// ModeSceneSetup (anillo), EscapePoliceStationGenerator (patrullas, carril norte y
    /// comisaría) y la barrera policial de EscapeHazardDirector.
    /// </summary>
    public static class CityZones
    {
        // Anillo: barrera interior 17.3–17.9, asfalto 18–32, barrera exterior 32.1–32.7.
        public const float InnerBarrierInnerFace = 17.3f;
        public const float OuterBarrierOuterFace = 32.7f;

        // Centro (dentro de la barrera interior): piezas bajas para no tapar al dron.
        public const float CenterFreeRadius = 15.3f;   // deja 2 m de acera junto a la barrera (P3)
        public const float CenterMaxHeight = 4.5f;

        // Patrullas exteriores en r = 36 (EscapePoliceStationGenerator.OutsidePatrolRadius),
        // coches de 2 m de ancho: ningún edificio más cerca de r 39.5.
        public const float PatrolRadius = 36f;
        public const float BuildingMinRadius = 39.5f;

        // Puertas (ModeSceneSetup.BuildDoors) y barrera policial (EscapeHazardDirector).
        public static readonly float[] DoorAngles = { 25f, 145f, 265f };
        public const float PoliceBarrierAngle = 85f;

        // Carril de salida norte de las patrullas y de Escape Solo (ExitRoad x ±6, z 28–60,
        // muros en x ±6.35) y comisaría (x ±11, z 59–72 en la versión de Diego).
        public static readonly Rect NorthCorridor = Rect.MinMaxRect(-8f, 25f, 8f, 58f);
        public static readonly Rect StationArea = Rect.MinMaxRect(-16f, 50f, 16f, 80f);

        /// <summary>Distancia mínima del rectángulo (x, z) al origen.</summary>
        public static float MinDistanceToOrigin(Rect rect)
        {
            float x = Mathf.Clamp(0f, rect.xMin, rect.xMax);
            float z = Mathf.Clamp(0f, rect.yMin, rect.yMax);
            return new Vector2(x, z).magnitude;
        }

        /// <summary>Distancia máxima del rectángulo (x, z) al origen.</summary>
        public static float MaxDistanceToOrigin(Rect rect)
        {
            float x = Mathf.Max(Mathf.Abs(rect.xMin), Mathf.Abs(rect.xMax));
            float z = Mathf.Max(Mathf.Abs(rect.yMin), Mathf.Abs(rect.yMax));
            return new Vector2(x, z).magnitude;
        }

        /// <summary>True si un solar (huella con su acera) puede llevar edificio.</summary>
        public static bool BuildingLotAllowed(Rect lot) =>
            MinDistanceToOrigin(lot) >= BuildingMinRadius &&
            !lot.Overlaps(NorthCorridor) &&
            !lot.Overlaps(StationArea) &&
            !OverlapsRoute(lot);

        // ---------- Ruta por la ciudad (KartIHC/City/Route On∕Off) ----------

        private static readonly List<Rect> routeCorridors = new List<Rect>();

        /// <summary>Rectángulos (x, z) de cada tramo de la ruta con su ancho; vacío si no hay ruta.</summary>
        public static IReadOnlyList<Rect> RouteCorridors => routeCorridors;

        /// <summary>Relee la ruta de la escena. Llamar al empezar cada generador.</summary>
        public static void RefreshRoute()
        {
            routeCorridors.Clear();
            EscapeRouteDefinition route = Object.FindFirstObjectByType<EscapeRouteDefinition>();
            if (route == null) return;
            float half = route.HalfWidth;
            for (int i = 1; i < route.PointCount; i++)
            {
                Vector3 a = route.GetPoint(i - 1), b = route.GetPoint(i);
                routeCorridors.Add(Rect.MinMaxRect(
                    Mathf.Min(a.x, b.x) - half, Mathf.Min(a.z, b.z) - half,
                    Mathf.Max(a.x, b.x) + half, Mathf.Max(a.z, b.z) + half));
            }
            if (route.HasPlaza) routeCorridors.Add(route.Plaza); // plaza final sin edificios
        }

        public static bool OverlapsRoute(Rect area)
        {
            foreach (Rect corridor in routeCorridors)
                if (area.Overlaps(corridor)) return true;
            return false;
        }

        /// <summary>True si una pieza del centro cabe en la plaza y no supera la altura máxima.</summary>
        public static bool CenterPieceAllowed(Rect footprint, float height) =>
            MaxDistanceToOrigin(footprint) <= CenterFreeRadius && height <= CenterMaxHeight;
    }
}
