using UnityEngine;
using UnityEngine.SceneManagement;

namespace KartIHC.Gameplay
{
    /// <summary>
    /// Geometría del anillo de EscapeCourse: centrado en el origen y recorrido en
    /// sentido antihorario (la salida EscapeKartSpawn mira hacia +Z). Los radios
    /// se leen de las barreras y tramos de la escena una vez por escena.
    /// Ángulos en grados: 0 = +X, 90 = +Z.
    /// </summary>
    public static class EscapeTrackGeometry
    {
        private const float DefaultInnerRadius = 17.6f;
        private const float DefaultOuterRadius = 32.3f;
        private const float DefaultRoadRadius = 24.9f;

        private static int cachedScene = int.MinValue;
        private static float innerRadius = DefaultInnerRadius;
        private static float outerRadius = DefaultOuterRadius;
        private static float roadRadius = DefaultRoadRadius;

        public static float InnerRadius { get { Ensure(); return innerRadius; } }
        public static float OuterRadius { get { Ensure(); return outerRadius; } }
        public static float RoadRadius { get { Ensure(); return roadRadius; } }

        public static float AngleOf(Vector3 position)
        {
            float angle = Mathf.Atan2(position.z, position.x) * Mathf.Rad2Deg;
            return angle < 0f ? angle + 360f : angle;
        }

        public static float RadiusOf(Vector3 position) => new Vector2(position.x, position.z).magnitude;

        public static Vector3 PointAt(float angleDegrees, float radius, float y)
        {
            float radians = angleDegrees * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(radians) * radius, y, Mathf.Sin(radians) * radius);
        }

        /// <summary>Mirando en el sentido de carrera: +Z local = tangente, +X local = hacia afuera del anillo.</summary>
        public static Quaternion RaceRotationAt(float angleDegrees)
        {
            float radians = angleDegrees * Mathf.Deg2Rad;
            return Quaternion.LookRotation(new Vector3(-Mathf.Sin(radians), 0f, Mathf.Cos(radians)), Vector3.up);
        }

        /// <summary>Grados recorridos en sentido de carrera desde 'originDegrees' hasta 'angleDegrees', en [0, 360).</summary>
        public static float ProgressFrom(float originDegrees, float angleDegrees) => Mathf.Repeat(angleDegrees - originDegrees, 360f);

        private static void Ensure()
        {
            int scene = SceneManager.GetActiveScene().handle;
            if (scene == cachedScene) return;
            cachedScene = scene;

            float innerSum = 0f, outerSum = 0f, roadSum = 0f;
            int innerCount = 0, outerCount = 0, roadCount = 0;
            // Incluye inactivos: en Escape Solo se desactivan dos barreras exteriores.
            foreach (Transform item in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                string objectName = item.name;
                if (objectName.StartsWith("EscapeInnerBarrier_")) { innerSum += RadiusOf(item.position); innerCount++; }
                else if (objectName.StartsWith("EscapeOuterBarrier_")) { outerSum += RadiusOf(item.position); outerCount++; }
                else if (objectName.StartsWith("EscapeRoad_")) { roadSum += RadiusOf(item.position); roadCount++; }
            }
            innerRadius = innerCount > 0 ? innerSum / innerCount : DefaultInnerRadius;
            outerRadius = outerCount > 0 ? outerSum / outerCount : DefaultOuterRadius;
            roadRadius = roadCount > 0 ? roadSum / roadCount : DefaultRoadRadius;
        }
    }
}
