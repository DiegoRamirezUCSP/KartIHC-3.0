using System.Collections.Generic;
using UnityEngine;

namespace KartIHC.Gameplay.Hazards
{
    /// <summary>
    /// Base de los obstáculos hackeables del Modo Escape (además de las puertas).
    /// Cerrado = bloquea la ruta del conductor; abierto = la habilita. El estado lo
    /// decide EscapeHazardDirector (local, host NGO o servidor Python); aquí solo
    /// se construye la geometría y se anima.
    /// Ids 0..2: coinciden con los obstáculos genéricos del servidor Python.
    /// </summary>
    public abstract class EscapeHackableObstacle : MonoBehaviour
    {
        private readonly List<Material> createdMaterials = new List<Material>();

        public int Id { get; private set; }
        public bool IsOpen { get; private set; }

        /// <summary>Nombre en avisos y HUD, p. ej. "Barrera policial".</summary>
        public abstract string DisplayName { get; }
        /// <summary>Texto corto para la leyenda del minimapa.</summary>
        public abstract string LegendLabel { get; }
        /// <summary>Largo del icono en el minimapa (eje X local), en metros.</summary>
        public abstract float MinimapLengthMeters { get; }
        public abstract string StateText(bool open);

        /// <summary>Punto contra el que se mide el alcance de hackeo del dron.</summary>
        public virtual Vector3 HackPoint => transform.position + Vector3.up * 1.2f;
        public float GateAngle => EscapeTrackGeometry.AngleOf(transform.position);
        /// <summary>Rumbo del eje largo (X local) para el minimapa: 0 = norte, sentido horario.</summary>
        public float LongAxisYaw
        {
            get
            {
                Vector3 right = transform.right;
                return Mathf.Atan2(right.x, right.z) * Mathf.Rad2Deg;
            }
        }

        public void Initialize(int id)
        {
            Id = id;
            Build();
            ApplyState(true);
        }

        public void SetOpen(bool open, bool instant = false)
        {
            if (IsOpen == open && !instant) return;
            IsOpen = open;
            ApplyState(instant);
        }

        protected abstract void Build();
        protected abstract void ApplyState(bool instant);

        /// <summary>Cubo visual sin collider (salvo que se pida), como en EscapePoliceStationGenerator.</summary>
        protected Renderer CreateBox(Transform parent, string objectName, Vector3 localPosition, Vector3 localScale,
            Color color, bool keepCollider = false)
        {
            GameObject item = GameObject.CreatePrimitive(PrimitiveType.Cube);
            item.name = objectName;
            item.transform.SetParent(parent, false);
            item.transform.localPosition = localPosition;
            item.transform.localScale = localScale;
            if (!keepCollider)
            {
                Collider itemCollider = item.GetComponent<Collider>();
                if (itemCollider != null) Destroy(itemCollider);
            }
            Renderer itemRenderer = item.GetComponent<Renderer>();
            if (itemRenderer != null)
            {
                Material material = itemRenderer.material;
                material.color = color;
                createdMaterials.Add(material);
            }
            return itemRenderer;
        }

        protected virtual void OnDestroy()
        {
            foreach (Material material in createdMaterials)
                if (material != null) Destroy(material);
            createdMaterials.Clear();
        }
    }
}
