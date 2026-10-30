using System;
using UnityEngine;

/// <summary>
/// Steering input from a DualShock 4 / DualSense IMU through JoyShockLibrary.
/// The controller is used like a physical steering wheel: tilt left/right to steer.
/// </summary>
public class JoyShockGyroSteeringInput : MonoBehaviour
{
    [Header("Device")]
    [SerializeField] private bool preferDualShock4 = true;
    [SerializeField] private bool showDiagnostics = true;

    [Header("Steering")]
    [SerializeField] private float maxTiltDegrees = 35f;
    [SerializeField] private float sensitivity = 1.2f;
    [SerializeField] private float smoothing = 12f;
    [SerializeField] private float deadZoneDegrees = 2f;
    [SerializeField] private bool invert;

    private int deviceId = -1;
    private float centerTilt;
    private float currentSteering;
    private float currentTilt;
    private string status = "sin iniciar";

    public float Steering => currentSteering;
    public bool IsConnected => deviceId >= 0 && JoyShockLibraryNative.JslStillConnected(deviceId);
    public string Status => status;
    public float CurrentTiltDegrees => currentTilt;

    private void OnEnable()
    {
        Connect();
    }

    private void Start()
    {
        Calibrate();
    }

    private void Update()
    {
        if (!EnsureConnected())
        {
            currentSteering = Mathf.Lerp(currentSteering, 0f, Time.deltaTime * smoothing);
            return;
        }

        currentTilt = ReadTiltDegrees() - centerTilt;
        float rawSteering = ConvertTiltToSteering(currentTilt);
        currentSteering = Mathf.Lerp(currentSteering, rawSteering, Time.deltaTime * smoothing);
    }

    public void Calibrate()
    {
        centerTilt = IsConnected ? ReadTiltDegrees() : 0f;
        status = IsConnected ? $"calibrado device {deviceId}" : "no hay mando para calibrar";
    }

    private bool EnsureConnected()
    {
        if (deviceId >= 0 && JoyShockLibraryNative.JslStillConnected(deviceId))
        {
            return true;
        }

        return Connect();
    }

    private bool Connect()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        try
        {
            int count = JoyShockLibraryNative.JslConnectDevices();

            if (count <= 0)
            {
                deviceId = -1;
                status = "JoyShockLibrary no encontro mandos";
                return false;
            }

            int[] handles = new int[count];
            JoyShockLibraryNative.JslGetConnectedDeviceHandles(handles, handles.Length);
            deviceId = PickDevice(handles);

            if (deviceId < 0)
            {
                status = "hay mandos, pero no DS4/DS5 compatible";
                return false;
            }

            status = $"mando conectado: {ReadControllerName(deviceId)} ({deviceId})";
            centerTilt = ReadTiltDegrees();
            return true;
        }
        catch (DllNotFoundException)
        {
            deviceId = -1;
            status = "falta JoyShockLibrary.dll en Assets/Plugins/x86_64";
            return false;
        }
        catch (EntryPointNotFoundException exception)
        {
            deviceId = -1;
            status = $"DLL incompatible: {exception.Message}";
            return false;
        }
#else
        status = "JoyShockLibrary esta pensado para Windows en este proyecto";
        return false;
#endif
    }

    private int PickDevice(int[] handles)
    {
        int firstConnected = -1;

        for (int i = 0; i < handles.Length; i++)
        {
            int handle = handles[i];

            if (!JoyShockLibraryNative.JslStillConnected(handle))
            {
                continue;
            }

            if (firstConnected < 0)
            {
                firstConnected = handle;
            }

            int type = JoyShockLibraryNative.JslGetControllerType(handle);

            if (type == JoyShockLibraryNative.ControllerTypeDualShock4)
            {
                return handle;
            }

            if (!preferDualShock4 && type == JoyShockLibraryNative.ControllerTypeDualSense)
            {
                return handle;
            }
        }

        return firstConnected;
    }

    private float ReadTiltDegrees()
    {
        JoyShockLibraryNative.MotionState motion = JoyShockLibraryNative.JslGetMotionState(deviceId);

        if (Mathf.Abs(motion.gravX) > 0.0001f || Mathf.Abs(motion.gravY) > 0.0001f || Mathf.Abs(motion.gravZ) > 0.0001f)
        {
            return Mathf.Asin(Mathf.Clamp(motion.gravX, -1f, 1f)) * Mathf.Rad2Deg;
        }

        JoyShockLibraryNative.ImuState imu = JoyShockLibraryNative.JslGetIMUState(deviceId);
        return Mathf.Asin(Mathf.Clamp(imu.accelX, -1f, 1f)) * Mathf.Rad2Deg;
    }

    private float ConvertTiltToSteering(float tilt)
    {
        if (Mathf.Abs(tilt) < deadZoneDegrees)
        {
            return 0f;
        }

        tilt -= Mathf.Sign(tilt) * deadZoneDegrees;

        float usableRange = Mathf.Max(0.01f, maxTiltDegrees - deadZoneDegrees);
        float normalized = Mathf.Clamp(tilt / usableRange, -1f, 1f);

        if (invert)
        {
            normalized *= -1f;
        }

        return Mathf.Clamp(normalized * sensitivity, -1f, 1f);
    }

    private string ReadControllerName(int handle)
    {
        switch (JoyShockLibraryNative.JslGetControllerType(handle))
        {
            case JoyShockLibraryNative.ControllerTypeDualShock4:
                return "DualShock 4";
            case JoyShockLibraryNative.ControllerTypeDualSense:
                return "DualSense";
            default:
                return "control compatible";
        }
    }

    private void OnGUI()
    {
        if (!showDiagnostics)
        {
            return;
        }

        GUILayout.BeginArea(new Rect(20, 560, 620, 150), GUI.skin.box);
        GUILayout.Label("JoyShock / DS4 Gyro");
        GUILayout.Label($"Estado: {status}");
        GUILayout.Label($"Conectado: {IsConnected}");
        GUILayout.Label($"Inclinacion: {currentTilt:0.0} grados");
        GUILayout.Label($"Volante: {currentSteering:0.00}");

        if (GUILayout.Button("Calibrar mando"))
        {
            Calibrate();
        }

        GUILayout.EndArea();
    }

    private void OnDisable()
    {
        currentSteering = 0f;
    }

    private void OnDestroy()
    {
        try
        {
            JoyShockLibraryNative.JslDisconnectAndDisposeAll();
        }
        catch (DllNotFoundException)
        {
        }
    }
}
