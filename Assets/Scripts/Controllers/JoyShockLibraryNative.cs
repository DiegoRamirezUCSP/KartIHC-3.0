using System.Runtime.InteropServices;

/// <summary>
/// Minimal C# wrapper for JoyShockLibrary. Requires JoyShockLibrary.dll in
/// Assets/Plugins/x86_64 on Windows.
/// </summary>
public static class JoyShockLibraryNative
{
    public const int ControllerTypeDualShock4 = 4;
    public const int ControllerTypeDualSense = 5;

    private const string DllName = "JoyShockLibrary";

    [StructLayout(LayoutKind.Sequential)]
    public struct JoyShockState
    {
        public int buttons;
        public float lTrigger;
        public float rTrigger;
        public float stickLX;
        public float stickLY;
        public float stickRX;
        public float stickRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ImuState
    {
        public float accelX;
        public float accelY;
        public float accelZ;
        public float gyroX;
        public float gyroY;
        public float gyroZ;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MotionState
    {
        public float quatW;
        public float quatX;
        public float quatY;
        public float quatZ;
        public float accelX;
        public float accelY;
        public float accelZ;
        public float gravX;
        public float gravY;
        public float gravZ;
    }

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int JslConnectDevices();

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int JslGetConnectedDeviceHandles([Out] int[] deviceHandleArray, int size);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void JslDisconnectAndDisposeAll();

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool JslStillConnected(int deviceId);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern JoyShockState JslGetSimpleState(int deviceId);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern ImuState JslGetIMUState(int deviceId);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern MotionState JslGetMotionState(int deviceId);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int JslGetControllerType(int deviceId);
}
