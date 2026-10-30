using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Lee la inclinacion del telefono para usarlo como volante.
///
/// El proyecto tiene Active Input Handling en "Input System Package (New)", asi que
/// Input.gyro y SystemInfo.supportsGyroscope quedan fuera de la compilacion.
/// Esta clase lee los sensores del nuevo Input System (GravitySensor, Accelerometer,
/// AttitudeSensor) y deja el camino legacy solo como respaldo si alguien cambia el
/// proyecto a "Both".
///
/// Los sensores del nuevo Input System ya vienen compensados por orientacion de
/// pantalla, asi que el eje X siempre es "derecha en pantalla". Con el juego fijado
/// en landscape, girar el telefono como volante mueve gravity.x.
/// </summary>
public static class DeviceTiltReader
{
    public enum TiltSource
    {
        None,
        Keyboard,
        GravitySensor,
        Accelerometer,
        AttitudeSensor,
        LegacyGyro
    }

    private static bool sensorsRequested;

    /// <summary>Fuente que se uso en la ultima lectura.</summary>
    public static TiltSource ActiveSource { get; private set; } = TiltSource.None;

    /// <summary>True si el dispositivo expone algun sensor de inclinacion.</summary>
    public static bool SensorAvailable
    {
        get
        {
            EnableSensors();

#if ENABLE_INPUT_SYSTEM
            if (GravitySensor.current != null || Accelerometer.current != null || AttitudeSensor.current != null)
            {
                return true;
            }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
            return SystemInfo.supportsGyroscope;
#else
            return false;
#endif
        }
    }

    /// <summary>True si el sensor esta disponible y realmente encendido.</summary>
    public static bool SensorEnabled
    {
        get
        {
            EnableSensors();

#if ENABLE_INPUT_SYSTEM
            if (GravitySensor.current != null && GravitySensor.current.enabled)
            {
                return true;
            }

            if (Accelerometer.current != null && Accelerometer.current.enabled)
            {
                return true;
            }

            if (AttitudeSensor.current != null && AttitudeSensor.current.enabled)
            {
                return true;
            }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
            return SystemInfo.supportsGyroscope && Input.gyro.enabled;
#else
            return false;
#endif
        }
    }

    /// <summary>Nombre legible de la fuente activa, para el panel de diagnostico.</summary>
    public static string ActiveSourceName
    {
        get
        {
            switch (ActiveSource)
            {
                case TiltSource.GravitySensor:
                    return "sensor de gravedad";
                case TiltSource.Accelerometer:
                    return "acelerometro";
                case TiltSource.AttitudeSensor:
                    return "sensor de orientacion";
                case TiltSource.LegacyGyro:
                    return "giroscopio legacy";
                case TiltSource.Keyboard:
                    return "teclado (A/D)";
                default:
                    return "ninguna";
            }
        }
    }

    /// <summary>
    /// Los sensores del nuevo Input System llegan apagados para ahorrar bateria.
    /// Hay que pedirlos explicitamente con EnableDevice.
    /// </summary>
    public static void EnableSensors()
    {
        if (sensorsRequested)
        {
            return;
        }

        sensorsRequested = true;

#if ENABLE_INPUT_SYSTEM
        if (GravitySensor.current != null && !GravitySensor.current.enabled)
        {
            InputSystem.EnableDevice(GravitySensor.current);
        }

        if (Accelerometer.current != null && !Accelerometer.current.enabled)
        {
            InputSystem.EnableDevice(Accelerometer.current);
        }

        if (AttitudeSensor.current != null && !AttitudeSensor.current.enabled)
        {
            InputSystem.EnableDevice(AttitudeSensor.current);
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        if (SystemInfo.supportsGyroscope)
        {
            Input.gyro.enabled = true;
        }
#endif
    }

    /// <summary>
    /// Inclinacion lateral en grados. Positivo cuando el borde derecho baja.
    /// Cae al teclado cuando no hay sensor, para poder probar en Editor.
    /// </summary>
    /// <param name="keyboardFallbackDegrees">Grados que simula una tecla al fondo.</param>
    public static float ReadTiltDegrees(float keyboardFallbackDegrees)
    {
        EnableSensors();

#if ENABLE_INPUT_SYSTEM
        if (GravitySensor.current != null && GravitySensor.current.enabled)
        {
            Vector3 gravity = GravitySensor.current.gravity.ReadValue();

            if (gravity.sqrMagnitude > 0.0001f)
            {
                ActiveSource = TiltSource.GravitySensor;
                return TiltFromGravity(gravity);
            }
        }

        if (Accelerometer.current != null && Accelerometer.current.enabled)
        {
            // Con el telefono quieto la aceleracion es basicamente la gravedad.
            Vector3 acceleration = Accelerometer.current.acceleration.ReadValue();

            if (acceleration.sqrMagnitude > 0.0001f)
            {
                ActiveSource = TiltSource.Accelerometer;
                return TiltFromGravity(acceleration);
            }
        }

        if (AttitudeSensor.current != null && AttitudeSensor.current.enabled)
        {
            Quaternion attitude = AttitudeSensor.current.attitude.ReadValue();

            if (attitude.x != 0f || attitude.y != 0f || attitude.z != 0f || attitude.w != 0f)
            {
                // Llevamos "abajo" del mundo al espacio del dispositivo.
                Vector3 gravity = Quaternion.Inverse(attitude) * Vector3.down;
                ActiveSource = TiltSource.AttitudeSensor;
                return TiltFromGravity(gravity);
            }
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        if (SystemInfo.supportsGyroscope && Input.gyro.enabled)
        {
            ActiveSource = TiltSource.LegacyGyro;
            return TiltFromGravity(Input.gyro.gravity);
        }
#endif

        ActiveSource = TiltSource.Keyboard;
        return ReadKeyboardAxis() * keyboardFallbackDegrees;
    }

    /// <summary>
    /// Convierte un vector de gravedad en grados de inclinacion lateral.
    /// Normaliza primero para que da igual si el sensor entrega g o m/s2.
    /// </summary>
    private static float TiltFromGravity(Vector3 gravity)
    {
        Vector3 normalized = gravity.normalized;
        return Mathf.Asin(Mathf.Clamp(normalized.x, -1f, 1f)) * Mathf.Rad2Deg;
    }

    /// <summary>Eje horizontal de teclado, -1 a 1, para probar sin telefono.</summary>
    public static float ReadKeyboardAxis()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
        {
            float axis = 0f;

            if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed)
            {
                axis -= 1f;
            }

            if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed)
            {
                axis += 1f;
            }

            return axis;
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetAxisRaw("Horizontal");
#else
        return 0f;
#endif
    }
}
