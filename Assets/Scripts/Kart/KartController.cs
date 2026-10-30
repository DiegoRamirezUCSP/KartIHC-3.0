using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace KartIHC.Kart
{
    /// <summary>
    /// Drives a kart using WheelColliders: rear wheels apply motor torque,
    /// front wheels steer. Exposes simple setters so external input sources
    /// (network, keyboard, custom controllers, etc.) can drive the kart.
    /// Also owns the 3 power-ups: turbo, escudo (shield) and trampa (trap).
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class KartController : MonoBehaviour
    {
        [Header("Wheel Colliders")]
        public WheelCollider frontLeft;
        public WheelCollider frontRight;
        public WheelCollider rearLeft;
        public WheelCollider rearRight;

        [Header("Wheel Meshes")]
        public Transform frontLeftMesh;
        public Transform frontRightMesh;
        public Transform rearLeftMesh;
        public Transform rearRightMesh;

        [Header("Kart Parameters")]
        public float motorTorque = 1500f;
        public float brakeTorque = 3000f;
        public float maxSteerAngle = 30f;

        [Header("Input Gate")]
        public bool InputEnabled = false;

        [Header("Power-up: Turbo")]
        [Tooltip("Multiplicador de motorTorque mientras el turbo esta activo.")]
        [SerializeField] private float turboTorqueMultiplier = 1.8f;
        [SerializeField] private float turboDuration = 1.5f;
        [Tooltip("Empujon instantaneo de velocidad (unidades/seg) hacia adelante al activar el turbo. Esto es lo que se SIENTE como el 'golpe' del turbo; el multiplicador de arriba solo ayuda a sostenerlo un poco mas.")]
        [SerializeField] private float turboImpulseSpeed = 8f;

        [Header("Power-up: Escudo")]
        [Tooltip("GameObject hijo del kart con la esfera visual del escudo (empieza desactivado).")]
        [SerializeField] private GameObject shieldVisual;
        [SerializeField] private float shieldDuration = 10f;

        [Header("Power-up: Trampa")]
        [Tooltip("Prefab con el componente TrapPickup que se suelta detras del kart.")]
        [SerializeField] private GameObject trapPrefab;
        [SerializeField] private float trapDropDistance = 3f;
        [Tooltip("Cuanto dura 'frenado en seco' un kart que pisa la trampa.")]
        [SerializeField] private float trapStunDuration = 1f;
        [Tooltip("Fuerza de frenado forzado mientras esta 'frenado en seco'.")]
        [SerializeField] private float trapStunBrakeTorque = 6000f;

        [Header("Frenado / Reversa")]
        [Tooltip("Velocidad hacia adelante (m/s) por debajo de la cual 'frenar' pasa a dar reversa en vez de solo frenar.")]
        [SerializeField] private float reverseSpeedThreshold = 1f;
        [Tooltip("Que tan fuerte empuja en reversa comparado con motorTorque (1 = igual de fuerte que para adelante).")]
        [SerializeField] private float reverseTorqueMultiplier = 0.6f;

        private Rigidbody rb;
        private Coroutine turboRoutine;
        private Coroutine shieldRoutine;
        private Coroutine stunRoutine;
        private Vector3 startPosition;
        private Quaternion startRotation;
        private readonly List<GameObject> droppedTraps = new List<GameObject>();

        /// <summary>True mientras el escudo esta activo (evita el efecto de la trampa).</summary>
        public bool IsShielded { get; private set; }

        /// <summary>True mientras el turbo esta activo.</summary>
        public bool IsTurboActive { get; private set; }

        /// <summary>True mientras esta "frenado en seco" por haber pisado una trampa.</summary>
        public bool IsStunned { get; private set; }

        /// <summary>Fired when a power-up is used (e.g. for sound effects).</summary>
        public event System.Action TurboActivated;
        public event System.Action ShieldActivated;
        public event System.Action TrapDropped;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            startPosition = transform.position;
            startRotation = transform.rotation;

            if (shieldVisual != null)
            {
                shieldVisual.SetActive(false);
            }
        }

        public void SetInputEnabled(bool isEnabled)
        {
            InputEnabled = isEnabled;
        }

        public void SetAccelerate(float value)
        {
            if (!InputEnabled)
            {
                return;
            }

            if (IsStunned)
            {
                // Ignora el acelerador mientras esta "frenado en seco".
                rearLeft.motorTorque = 0f;
                rearRight.motorTorque = 0f;
                return;
            }

            float effectiveTorque = IsTurboActive ? motorTorque * turboTorqueMultiplier : motorTorque;
            float torque = value * effectiveTorque;
            rearLeft.motorTorque = torque;
            rearRight.motorTorque = torque;
        }

        public void SetBrake(float value)
        {
            if (!InputEnabled)
            {
                return;
            }

            if (IsStunned)
            {
                // Fuerza freno maximo mientras dura el efecto de la trampa,
                // sin importar lo que mande el input externo.
                frontLeft.brakeTorque = trapStunBrakeTorque;
                frontRight.brakeTorque = trapStunBrakeTorque;
                rearLeft.brakeTorque = trapStunBrakeTorque;
                rearRight.brakeTorque = trapStunBrakeTorque;
                return;
            }

            float forwardSpeed = Vector3.Dot(rb.linearVelocity, transform.forward);

            if (value > 0f && forwardSpeed <= reverseSpeedThreshold)
            {
                // Ya casi detenido (o retrocediendo): soltar el freno y dar
                // reversa en vez de seguir "frenando" sobre algo quieto.
                frontLeft.brakeTorque = 0f;
                frontRight.brakeTorque = 0f;
                rearLeft.brakeTorque = 0f;
                rearRight.brakeTorque = 0f;

                float reverseTorque = -value * motorTorque * reverseTorqueMultiplier;
                rearLeft.motorTorque = reverseTorque;
                rearRight.motorTorque = reverseTorque;
                return;
            }

            // Todavia con velocidad hacia adelante: frenado normal.
            float torque = value * brakeTorque;
            frontLeft.brakeTorque = torque;
            frontRight.brakeTorque = torque;
            rearLeft.brakeTorque = torque;
            rearRight.brakeTorque = torque;
        }

        public void SetSteer(float value)
        {
            if (!InputEnabled)
            {
                return;
            }

            float angle = value * maxSteerAngle;
            frontLeft.steerAngle = angle;
            frontRight.steerAngle = angle;
        }

        /// <summary>Llamado desde el input bridge cuando se pide el power-up "turbo".</summary>
        public void ActivateTurbo()
        {
            if (turboRoutine != null)
            {
                StopCoroutine(turboRoutine);
            }
            turboRoutine = StartCoroutine(TurboRoutine());
            TurboActivated?.Invoke();
        }

        /// <summary>Llamado desde el input bridge cuando se pide el power-up "escudo".</summary>
        public void ActivateShield()
        {
            if (shieldRoutine != null)
            {
                StopCoroutine(shieldRoutine);
            }
            shieldRoutine = StartCoroutine(ShieldRoutine());
            ShieldActivated?.Invoke();
        }

        /// <summary>Llamado desde el input bridge cuando se pide el power-up "trampa".</summary>
        public void DropTrap()
        {
            if (trapPrefab == null)
            {
                Debug.LogWarning("KartController: no hay Trap Prefab asignado.");
                return;
            }

            Vector3 dropPosition = transform.position - transform.forward * trapDropDistance;
            GameObject trapInstance = Instantiate(trapPrefab, dropPosition, Quaternion.identity);
            droppedTraps.Add(trapInstance);

            TrapPickup trap = trapInstance.GetComponent<TrapPickup>();
            if (trap != null)
            {
                trap.Init(this);
            }

            TrapDropped?.Invoke();
        }

        /// <summary>
        /// Restores the kart to the position and clean state it had when the scene loaded.
        /// Used when returning to the main menu after restart/cancel/completed tutorial.
        /// </summary>
        public void ResetToStart()
        {
            StopActivePowerUps();
            ClearWheelInputs();
            ClearDroppedTraps();

            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.position = startPosition;
            rb.rotation = startRotation;
            transform.SetPositionAndRotation(startPosition, startRotation);

            UpdateWheelMesh(frontLeft, frontLeftMesh);
            UpdateWheelMesh(frontRight, frontRightMesh);
            UpdateWheelMesh(rearLeft, rearLeftMesh);
            UpdateWheelMesh(rearRight, rearRightMesh);
        }

        /// <summary>
        /// Llamado por TrapPickup cuando este kart pisa una trampa. Si el
        /// escudo esta activo, no pasa nada. Si no, frena en seco un momento.
        /// </summary>
        public void ApplyTrapEffect()
        {
            if (IsShielded)
            {
                Debug.Log("El escudo bloqueo el efecto de la trampa.");
                return;
            }

            if (stunRoutine != null)
            {
                StopCoroutine(stunRoutine);
            }
            stunRoutine = StartCoroutine(StunRoutine());
        }

        private IEnumerator TurboRoutine()
        {
            IsTurboActive = true;

            // El "golpe" real del turbo: un cambio de velocidad instantaneo
            // hacia adelante. ForceMode.VelocityChange ignora la masa, asi
            // que turboImpulseSpeed son directamente unidades/seg de mas.
            rb.AddForce(transform.forward * turboImpulseSpeed, ForceMode.VelocityChange);

            yield return new WaitForSeconds(turboDuration);
            IsTurboActive = false;
            turboRoutine = null;
        }

        private IEnumerator ShieldRoutine()
        {
            IsShielded = true;

            if (shieldVisual != null)
            {
                shieldVisual.SetActive(true);
            }

            yield return new WaitForSeconds(shieldDuration);

            IsShielded = false;

            if (shieldVisual != null)
            {
                shieldVisual.SetActive(false);
            }

            shieldRoutine = null;
        }

        private IEnumerator StunRoutine()
        {
            IsStunned = true;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;

            yield return new WaitForSeconds(trapStunDuration);

            IsStunned = false;
            stunRoutine = null;
        }

        private void StopActivePowerUps()
        {
            if (turboRoutine != null)
            {
                StopCoroutine(turboRoutine);
                turboRoutine = null;
            }

            if (shieldRoutine != null)
            {
                StopCoroutine(shieldRoutine);
                shieldRoutine = null;
            }

            if (stunRoutine != null)
            {
                StopCoroutine(stunRoutine);
                stunRoutine = null;
            }

            IsTurboActive = false;
            IsShielded = false;
            IsStunned = false;

            if (shieldVisual != null)
            {
                shieldVisual.SetActive(false);
            }
        }

        private void ClearWheelInputs()
        {
            ClearWheel(frontLeft);
            ClearWheel(frontRight);
            ClearWheel(rearLeft);
            ClearWheel(rearRight);
        }

        private void ClearWheel(WheelCollider wheel)
        {
            if (wheel == null)
            {
                return;
            }

            wheel.motorTorque = 0f;
            wheel.brakeTorque = 0f;
            wheel.steerAngle = 0f;
        }

        private void ClearDroppedTraps()
        {
            for (int i = droppedTraps.Count - 1; i >= 0; i--)
            {
                if (droppedTraps[i] != null)
                {
                    Destroy(droppedTraps[i]);
                }
            }

            droppedTraps.Clear();
        }

        private void Update()
        {
            UpdateWheelMesh(frontLeft, frontLeftMesh);
            UpdateWheelMesh(frontRight, frontRightMesh);
            UpdateWheelMesh(rearLeft, rearLeftMesh);
            UpdateWheelMesh(rearRight, rearRightMesh);
        }

        private void UpdateWheelMesh(WheelCollider collider, Transform mesh)
        {
            if (collider == null || mesh == null)
            {
                return;
            }

            collider.GetWorldPose(out Vector3 position, out Quaternion rotation);
            mesh.position = position;
            mesh.rotation = rotation;
        }
    }
}
