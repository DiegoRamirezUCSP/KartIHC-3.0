using UnityEngine;

/// <summary>
/// Volante por inclinacion del telefono.
/// La lectura del sensor vive en DeviceTiltReader para que este script y
/// PhoneControllerSender usen exactamente la misma matematica.
/// </summary>
public class GyroSteeringInput : MonoBehaviour
{
    [Header("Steering")]
    [SerializeField] private float maxTiltDegrees = 35f;
    [SerializeField] private float sensitivity = 1.2f;
    [SerializeField] private float smoothing = 12f;
    [SerializeField] private bool invert;

    [Header("Zona muerta")]
    [Tooltip("Grados de inclinacion que se ignoran alrededor del centro para evitar temblor.")]
    [SerializeField] private float deadZoneDegrees = 2f;

    [Header("Orientacion")]
    [Tooltip("Fija la pantalla en landscape. Si el telefono autorrota mientras inclinas, el volante se invierte.")]
    [SerializeField] private bool lockLandscape = true;

    private float centerTilt;
    private float currentSteering;

    /// <summary>Valor del volante, -1 a 1.</summary>
    public float Steering => currentSteering;

    /// <summary>True si el dispositivo tiene sensor de inclinacion.</summary>
    public bool GyroAvailable => DeviceTiltReader.SensorAvailable;

    /// <summary>True si el sensor esta encendido y entregando datos.</summary>
    public bool GyroEnabled => DeviceTiltReader.SensorEnabled;

    /// <summary>Inclinacion actual respecto al centro calibrado, en grados.</summary>
    public float CurrentTiltDegrees => DeviceTiltReader.ReadTiltDegrees(maxTiltDegrees) - centerTilt;

    /// <summary>Fuente que se esta usando: sensor de gravedad, acelerometro, teclado...</summary>
    public string TiltSourceName => DeviceTiltReader.ActiveSourceName;

    private void Awake()
    {
        if (lockLandscape)
        {
            // Los sensores del nuevo Input System vienen compensados por orientacion.
            // Si la pantalla rota mientras inclinas, el eje se voltea y el volante salta.
            Screen.orientation = ScreenOrientation.LandscapeLeft;
        }

        DeviceTiltReader.EnableSensors();
        Calibrate();
    }

    private void Start()
    {
        // Los sensores tardan un frame o dos en entregar el primer valor,
        // asi que recalibramos cuando ya hay dato real.
        Calibrate();
    }

    private void Update()
    {
        float rawSteering = ReadSteering();
        currentSteering = Mathf.Lerp(currentSteering, rawSteering, Time.deltaTime * smoothing);
    }

    /// <summary>Toma la inclinacion actual como el centro del volante.</summary>
    public void Calibrate()
    {
        centerTilt = DeviceTiltReader.ReadTiltDegrees(maxTiltDegrees);
    }

    private float ReadSteering()
    {
        float tilt = DeviceTiltReader.ReadTiltDegrees(maxTiltDegrees) - centerTilt;

        // La zona muerta solo aplica al sensor. Con teclado queremos respuesta inmediata.
        if (DeviceTiltReader.ActiveSource != DeviceTiltReader.TiltSource.Keyboard)
        {
            if (Mathf.Abs(tilt) < deadZoneDegrees)
            {
                return 0f;
            }

            tilt -= Mathf.Sign(tilt) * deadZoneDegrees;
        }

        float usableRange = Mathf.Max(0.01f, maxTiltDegrees - deadZoneDegrees);
        float normalized = Mathf.Clamp(tilt / usableRange, -1f, 1f);

        if (invert)
        {
            normalized *= -1f;
        }

        return Mathf.Clamp(normalized * sensitivity, -1f, 1f);
    }
}
