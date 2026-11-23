using UnityEngine;

namespace KartIHC.Gameplay.Hazards
{
    /// <summary>
    /// Barrera policial que corta todo el ancho del anillo. Cerrada: brazo a rayas
    /// bajado, luces rojo/azul parpadeando y una pared invisible que frena al kart.
    /// Hackeada: el brazo sube y la pared desaparece. Se construye en tiempo de
    /// ejecución; no hay que editar EscapeCourse.unity.
    /// Ejes locales: X = radial (de dentro hacia fuera del anillo), Z = sentido de carrera.
    /// </summary>
    public sealed class EscapePoliceBarrier : EscapeHackableObstacle
    {
        private const float OpenArmAngle = 82f;
        private const float ArmSpeedDegrees = 110f;
        private const float BlinkSeconds = 0.4f;

        private static readonly Color Concrete = new Color(0.62f, 0.62f, 0.6f);
        private static readonly Color StripeRed = new Color(0.86f, 0.1f, 0.08f);
        private static readonly Color StripeWhite = new Color(0.95f, 0.95f, 0.93f);
        private static readonly Color LightRedOn = new Color(1f, 0.12f, 0.08f);
        private static readonly Color LightBlueOn = new Color(0.15f, 0.35f, 1f);
        private static readonly Color LightOff = new Color(0.12f, 0.12f, 0.14f);

        private float span = 14.7f;
        private Transform armPivot;
        private BoxCollider blockWall;
        private Renderer redLight;
        private Renderer blueLight;
        private float armAngle;

        public override string DisplayName => "Barrera policial";
        public override string LegendLabel => "Barrera";
        public override float MinimapLengthMeters => span;
        public override string StateText(bool open) => open ? "abierta" : "cerrada";

        public static EscapePoliceBarrier Create(Transform parent, int id, float angleDegrees)
        {
            float inner = EscapeTrackGeometry.InnerRadius;
            float outer = EscapeTrackGeometry.OuterRadius;
            return CreateAt(parent, id,
                EscapeTrackGeometry.PointAt(angleDegrees, (inner + outer) * 0.5f, 0f),
                EscapeTrackGeometry.RaceRotationAt(angleDegrees), outer - inner);
        }

        /// <summary>Barrera en cualquier punto (p. ej. la ruta por la ciudad): +Z = sentido de carrera, ancho = span.</summary>
        public static EscapePoliceBarrier CreateAt(Transform parent, int id, Vector3 position, Quaternion raceRotation, float span)
        {
            var root = new GameObject($"EscapePoliceBarrier_{id + 1}");
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(position, raceRotation);
            var barrier = root.AddComponent<EscapePoliceBarrier>();
            barrier.span = span;
            barrier.Initialize(id);
            return barrier;
        }

        protected override void Build()
        {
            float postX = span * 0.5f - 0.9f;

            var wallObject = new GameObject("BarrierBlockWall");
            wallObject.transform.SetParent(transform, false);
            wallObject.transform.localPosition = new Vector3(0f, 1.3f, 0f);
            blockWall = wallObject.AddComponent<BoxCollider>();
            blockWall.size = new Vector3(span + 0.2f, 2.6f, 0.6f);

            CreateBox(transform, "BarrierPostInner", new Vector3(-postX, 0.7f, 0f), new Vector3(0.8f, 1.4f, 0.8f), Concrete);
            CreateBox(transform, "BarrierPostOuter", new Vector3(postX, 0.7f, 0f), new Vector3(0.8f, 1.4f, 0.8f), Concrete);
            redLight = CreateBox(transform, "BarrierLightRed", new Vector3(-postX, 1.52f, 0f), new Vector3(0.55f, 0.2f, 0.35f), LightRedOn);
            blueLight = CreateBox(transform, "BarrierLightBlue", new Vector3(postX, 1.52f, 0f), new Vector3(0.55f, 0.2f, 0.35f), LightBlueOn);

            armPivot = new GameObject("BarrierArmPivot").transform;
            armPivot.SetParent(transform, false);
            armPivot.localPosition = new Vector3(-postX, 1.15f, 0f);
            const int stripes = 8;
            float armLength = postX * 2f;
            float stripe = armLength / stripes;
            for (int i = 0; i < stripes; i++)
                CreateBox(armPivot, $"BarrierArmStripe_{i + 1}", new Vector3(stripe * (i + 0.5f), 0f, 0f),
                    new Vector3(stripe, 0.24f, 0.16f), i % 2 == 0 ? StripeRed : StripeWhite);

            // Cartel legible para el conductor que llega (mira hacia +Z local).
            var sign = new GameObject("BarrierSign");
            sign.transform.SetParent(transform, false);
            sign.transform.localPosition = new Vector3(0f, 2.2f, -0.1f);
            TextMesh text = sign.AddComponent<TextMesh>();
            text.text = "POLICÍA · ALTO";
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.fontStyle = FontStyle.Bold;
            text.fontSize = 64;
            text.characterSize = 0.09f;
            text.color = new Color(1f, 0.9f, 0.3f);
        }

        protected override void ApplyState(bool instant)
        {
            if (blockWall != null) blockWall.enabled = !IsOpen;
            if (instant)
            {
                armAngle = IsOpen ? OpenArmAngle : 0f;
                if (armPivot != null) armPivot.localRotation = Quaternion.Euler(0f, 0f, armAngle);
            }
        }

        private void Update()
        {
            float target = IsOpen ? OpenArmAngle : 0f;
            armAngle = Mathf.MoveTowards(armAngle, target, ArmSpeedDegrees * Time.deltaTime);
            if (armPivot != null) armPivot.localRotation = Quaternion.Euler(0f, 0f, armAngle);

            bool phase = Mathf.Repeat(Time.time, BlinkSeconds * 2f) < BlinkSeconds;
            if (redLight != null) redLight.material.color = !IsOpen && phase ? LightRedOn : LightOff;
            if (blueLight != null) blueLight.material.color = !IsOpen && !phase ? LightBlueOn : LightOff;
        }
    }
}
