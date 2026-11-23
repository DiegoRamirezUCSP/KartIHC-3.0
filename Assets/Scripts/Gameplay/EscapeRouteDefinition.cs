using UnityEngine;

namespace KartIHC.Gameplay
{
    /// <summary>
    /// Ruta por la ciudad de EscapeCourse (KartIHC/City/Route On∕Off la crea o la quita).
    /// Si existe en la escena, el recorrido es: anillo desde EscapeKartSpawn → salida norte →
    /// calles → meta al final de la ruta. Si no existe, todo funciona como el anillo original.
    /// Polilínea en XZ (y se ignora) con ancho constante; tramos rectos.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EscapeRouteDefinition : MonoBehaviour
    {
        [SerializeField] private Vector3[] points = new Vector3[0];
        [SerializeField] private float width = 10f;
        [SerializeField, Tooltip("Distancia a lo largo de la ruta donde va la barrera policial.")]
        private float barrierDistance = 34f;
        [SerializeField, Tooltip("Distancia a lo largo de la ruta donde va la meta.")]
        private float finishDistance = 130f;
        [SerializeField, Tooltip("Distancia a lo largo de la ruta donde va la puerta movida (cruce).")]
        private float doorDistance = 82f;

        // Puerta del anillo movida a la ruta (pivote EscapeDoor_N), para restaurarla con Route Off.
        [SerializeField] private string movedDoorName = "";
        [SerializeField] private Vector3 originalDoorPosition;
        [SerializeField] private Quaternion originalDoorRotation = Quaternion.identity;
        [SerializeField] private Vector3 originalPanelLocalPosition;
        [SerializeField] private Quaternion originalPanelLocalRotation = Quaternion.identity;

        // Plaza final (x, z): cuenta como ruta aunque esté fuera del corredor.
        [SerializeField] private bool hasPlaza;
        [SerializeField] private Rect plaza;

        // Posición original de la meta del anillo, para restaurarla con Route Off.
        [SerializeField] private bool hasOriginalFinish;
        [SerializeField] private Vector3 originalFinishPosition;
        [SerializeField] private Quaternion originalFinishRotation = Quaternion.identity;

        private static EscapeRouteDefinition active;

        /// <summary>Ruta activa de la escena, o null (recorrido de anillo original).</summary>
        public static EscapeRouteDefinition Active
        {
            get
            {
                if (active == null || !active.isActiveAndEnabled)
                {
                    active = FindFirstObjectByType<EscapeRouteDefinition>();
                }
                return active != null && active.isActiveAndEnabled ? active : null;
            }
        }

        public float Width => width;
        public bool HasPlaza => hasPlaza;
        public Rect Plaza => plaza;
        public float HalfWidth => width * 0.5f;
        public int PointCount => points.Length;
        public Vector3 GetPoint(int index) => Flat(points[index]);
        public float BarrierDistance => barrierDistance;
        public float FinishDistance => finishDistance;
        public float DoorDistance => doorDistance;
        public string MovedDoorName => movedDoorName;
        public Vector3 OriginalDoorPosition => originalDoorPosition;
        public Quaternion OriginalDoorRotation => originalDoorRotation;
        public Vector3 OriginalPanelLocalPosition => originalPanelLocalPosition;
        public Quaternion OriginalPanelLocalRotation => originalPanelLocalRotation;
        public bool HasOriginalFinish => hasOriginalFinish;
        public Vector3 OriginalFinishPosition => originalFinishPosition;
        public Quaternion OriginalFinishRotation => originalFinishRotation;

        public float Length
        {
            get
            {
                float length = 0f;
                for (int i = 1; i < points.Length; i++) length += Vector3.Distance(Flat(points[i - 1]), Flat(points[i]));
                return length;
            }
        }

        private void OnEnable() => active = this;

        private void OnDisable()
        {
            if (active == this) active = null;
        }

        public void Configure(Vector3[] routePoints, float routeWidth, float barrierAt, float finishAt, float doorAt)
        {
            points = (Vector3[])routePoints.Clone();
            width = routeWidth;
            barrierDistance = barrierAt;
            finishDistance = finishAt;
            doorDistance = doorAt;
        }

        public void SetPlaza(Rect area)
        {
            hasPlaza = true;
            plaza = area;
        }

        /// <summary>Distancia recorrida hasta el vértice 'index' (los giros son 1..PointCount-2).</summary>
        public float DistanceAtPoint(int index)
        {
            float length = 0f;
            for (int i = 1; i <= index && i < points.Length; i++) length += Vector3.Distance(Flat(points[i - 1]), Flat(points[i]));
            return length;
        }

        public void RememberDoor(string doorName, Vector3 position, Quaternion rotation,
            Vector3 panelLocalPosition, Quaternion panelLocalRotation)
        {
            movedDoorName = doorName;
            originalDoorPosition = position;
            originalDoorRotation = rotation;
            originalPanelLocalPosition = panelLocalPosition;
            originalPanelLocalRotation = panelLocalRotation;
        }

        public void RememberFinish(Vector3 position, Quaternion rotation)
        {
            hasOriginalFinish = true;
            originalFinishPosition = position;
            originalFinishRotation = rotation;
        }

        /// <summary>Punto de la ruta (y = 0) a 'distance' metros del inicio.</summary>
        public Vector3 PointAt(float distance)
        {
            if (points.Length == 0) return Vector3.zero;
            float remaining = Mathf.Max(0f, distance);
            for (int i = 1; i < points.Length; i++)
            {
                Vector3 a = Flat(points[i - 1]), b = Flat(points[i]);
                float length = Vector3.Distance(a, b);
                if (remaining <= length || i == points.Length - 1)
                {
                    return length > 0.0001f ? Vector3.Lerp(a, b, Mathf.Clamp01(remaining / length)) : a;
                }
                remaining -= length;
            }
            return Flat(points[points.Length - 1]);
        }

        /// <summary>Dirección de avance (sentido de carrera) en 'distance'.</summary>
        public Vector3 DirectionAt(float distance)
        {
            if (points.Length < 2) return Vector3.forward;
            float remaining = Mathf.Max(0f, distance);
            for (int i = 1; i < points.Length; i++)
            {
                Vector3 a = Flat(points[i - 1]), b = Flat(points[i]);
                float length = Vector3.Distance(a, b);
                if (remaining <= length || i == points.Length - 1)
                {
                    return length > 0.0001f ? (b - a) / length : Vector3.forward;
                }
                remaining -= length;
            }
            return Vector3.forward;
        }

        /// <summary>Rotación "mirando en el sentido de carrera" en 'distance' (+X local = derecha).</summary>
        public Quaternion RotationAt(float distance) => Quaternion.LookRotation(DirectionAt(distance), Vector3.up);

        /// <summary>
        /// Proyecta una posición sobre la ruta: distancia recorrida hasta el punto más cercano y
        /// separación lateral a la línea central.
        /// </summary>
        public float Project(Vector3 position, out float lateral)
        {
            Vector3 p = Flat(position);
            float best = float.PositiveInfinity, bestDistance = 0f, walked = 0f;
            for (int i = 1; i < points.Length; i++)
            {
                Vector3 a = Flat(points[i - 1]), b = Flat(points[i]);
                Vector3 ab = b - a;
                float length = ab.magnitude;
                float t = length > 0.0001f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / (length * length)) : 0f;
                float sq = (a + ab * t - p).sqrMagnitude;
                if (sq < best)
                {
                    best = sq;
                    bestDistance = walked + t * length;
                }
                walked += length;
            }
            lateral = Mathf.Sqrt(best);
            return bestDistance;
        }

        /// <summary>True si la posición está dentro del corredor de la ruta (ancho/2 + margen) o en la plaza final.</summary>
        public bool Contains(Vector3 position, float margin, out float distance)
        {
            distance = Project(position, out float lateral);
            if (points.Length < 2) return false;
            return lateral <= HalfWidth + margin || InPlaza(position, margin);
        }

        public bool InPlaza(Vector3 position, float margin = 0f) =>
            hasPlaza && position.x >= plaza.xMin - margin && position.x <= plaza.xMax + margin &&
            position.z >= plaza.yMin - margin && position.z <= plaza.yMax + margin;

        public bool Contains(Vector3 position, float margin = 0f) => Contains(position, margin, out _);

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
