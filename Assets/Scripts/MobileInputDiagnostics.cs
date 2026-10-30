using System.Collections;
using UnityEngine;

#if UNITY_ANDROID
using UnityEngine.Android;
#endif

public class MobileInputDiagnostics : MonoBehaviour
{
    private const string VersionLabel = "MobileInputDiagnostics v2 - Z/X/C";

    [SerializeField] private GyroSteeringInput steeringInput;
    [SerializeField] private VoicePowerUpInput voiceInput;
    [SerializeField] private bool showPanel = true;
    [SerializeField] private int sampleLength = 256;

    private AudioClip microphoneClip;
    private string microphoneName;
    private string microphoneStatus = "sin iniciar";
    private float microphoneLevel;

    private void Start()
    {
        StartCoroutine(StartMicrophoneCheck());
    }

    private void Update()
    {
        microphoneLevel = ReadMicrophoneLevel();
    }

    private IEnumerator StartMicrophoneCheck()
    {
#if UNITY_ANDROID
        if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
        {
            microphoneStatus = "pidiendo permiso";
            Permission.RequestUserPermission(Permission.Microphone);
            yield return new WaitForSeconds(1f);
        }
#endif

        if (!Application.HasUserAuthorization(UserAuthorization.Microphone))
        {
            microphoneStatus = "esperando permiso";
            yield return Application.RequestUserAuthorization(UserAuthorization.Microphone);
        }

        if (!Application.HasUserAuthorization(UserAuthorization.Microphone))
        {
            microphoneStatus = "permiso denegado";
            yield break;
        }

        if (Microphone.devices.Length == 0)
        {
            microphoneStatus = "no hay microfono";
            yield break;
        }

        microphoneName = Microphone.devices[0];
        microphoneClip = Microphone.Start(microphoneName, true, 1, 16000);
        microphoneStatus = microphoneClip != null ? "activo" : "fallo al iniciar";
    }

    private float ReadMicrophoneLevel()
    {
        if (microphoneClip == null || string.IsNullOrEmpty(microphoneName) || !Microphone.IsRecording(microphoneName))
        {
            return 0f;
        }

        int microphonePosition = Microphone.GetPosition(microphoneName);
        if (microphonePosition < sampleLength)
        {
            return 0f;
        }

        float[] samples = new float[sampleLength];
        microphoneClip.GetData(samples, microphonePosition - sampleLength);

        float sum = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            sum += samples[i] * samples[i];
        }

        return Mathf.Sqrt(sum / sampleLength);
    }

    private void OnGUI()
    {
        if (!showPanel)
        {
            return;
        }

        const int width = 520;
        const int height = 300;

        GUILayout.BeginArea(new Rect(20, 20, width, height), GUI.skin.box);
        GUILayout.Label("Diagnostico de controles moviles");
        GUILayout.Label(VersionLabel);
        GUILayout.Label($"Giroscopio disponible: {ReadGyroAvailable()}");
        GUILayout.Label($"Giroscopio activo: {ReadGyroEnabled()}");
        GUILayout.Label($"Inclinacion: {ReadTilt():0.0} grados");
        GUILayout.Label($"Volante: {ReadSteering():0.00}");
        GUILayout.Label($"Microfono: {microphoneStatus}");
        GUILayout.Label($"Entrada de audio: {microphoneLevel:0.000}");
        GUILayout.Label($"Voz por palabras clave aqui: {ReadVoiceSupport()}");
        GUILayout.Label($"Escuchando comandos: {ReadVoiceListening()}");
        GUILayout.Label($"Ultimo power-up: {ReadLastPowerUp()}");
        GUILayout.Label($"Activaciones: {ReadActivationCount()}");
        GUILayout.Label("Teclas: Z = escudo, X = turbo, C = trampa");

        if (GUILayout.Button("Calibrar volante"))
        {
            steeringInput?.Calibrate();
        }

        GUILayout.EndArea();
    }

    private bool ReadGyroAvailable()
    {
        return steeringInput != null ? steeringInput.GyroAvailable : SystemInfo.supportsGyroscope;
    }

    private bool ReadGyroEnabled()
    {
        return steeringInput != null && steeringInput.GyroEnabled;
    }

    private float ReadTilt()
    {
        return steeringInput != null ? steeringInput.CurrentTiltDegrees : 0f;
    }

    private float ReadSteering()
    {
        return steeringInput != null ? steeringInput.Steering : 0f;
    }

    private string ReadVoiceSupport()
    {
        return voiceInput != null && voiceInput.SupportsKeywordRecognizer ? "si" : "no en esta plataforma";
    }

    private string ReadVoiceListening()
    {
        return voiceInput != null && voiceInput.IsListening ? "si" : "no";
    }

    private string ReadLastPowerUp()
    {
        return voiceInput != null ? voiceInput.LastPowerUp : "sin VoicePowerUpInput";
    }

    private int ReadActivationCount()
    {
        return voiceInput != null ? voiceInput.ActivationCount : 0;
    }

    private void OnDestroy()
    {
        if (!string.IsNullOrEmpty(microphoneName) && Microphone.IsRecording(microphoneName))
        {
            Microphone.End(microphoneName);
        }
    }
}
