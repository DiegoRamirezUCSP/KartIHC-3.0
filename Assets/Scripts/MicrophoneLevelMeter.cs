using UnityEngine;

/// <summary>
/// Mide el nivel de volumen del microfono en tiempo real, solo para dar
/// feedback visual (una barrita o un punto que reacciona al sonido).
/// No interfiere con VoicePowerUpInput: ese usa el reconocedor de voz de
/// Windows (que no expone volumen), este usa la API de Microphone de Unity
/// en paralelo, ambos pueden leer el mismo microfono a la vez sin problema.
/// </summary>
public class MicrophoneLevelMeter : MonoBehaviour
{
    [Tooltip("Nombre del dispositivo de microfono. Dejalo vacio para usar el predeterminado del sistema.")]
    [SerializeField] private string microphoneDevice = "";
    [Tooltip("Nivel minimo (0-1) para considerar que 'hay sonido' (evita que ruido de fondo muy bajo cuente).")]
    [SerializeField] private float noiseThreshold = 0.02f;
    [SerializeField] private int sampleWindow = 128;

    private AudioClip micClip;
    private bool isRecording;

    /// <summary>Nivel de volumen actual, 0 (silencio) a 1 (muy fuerte).</summary>
    public float Level { get; private set; }

    /// <summary>True mientras el nivel supera el umbral de ruido (esta "escuchando algo").</summary>
    public bool HasSignal => Level >= noiseThreshold;

    /// <summary>True si se pudo abrir el microfono.</summary>
    public bool IsAvailable => isRecording;

    private void OnEnable()
    {
        StartMicrophone();
    }

    private void StartMicrophone()
    {
        if (Microphone.devices.Length == 0)
        {
            isRecording = false;
            return;
        }

        string device = string.IsNullOrEmpty(microphoneDevice) ? Microphone.devices[0] : microphoneDevice;

        // Loop de 1 segundo a 16kHz es suficiente para un medidor de nivel,
        // no necesitamos calidad alta para esto.
        micClip = Microphone.Start(device, true, 1, 16000);
        isRecording = micClip != null;
    }

    private void Update()
    {
        if (!isRecording || micClip == null)
        {
            Level = 0f;
            return;
        }

        int micPosition = Microphone.GetPosition(string.IsNullOrEmpty(microphoneDevice) ? Microphone.devices[0] : microphoneDevice);
        int startPosition = micPosition - sampleWindow;

        if (startPosition < 0)
        {
            Level = 0f;
            return;
        }

        float[] samples = new float[sampleWindow];
        micClip.GetData(samples, startPosition);

        float sum = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            sum += samples[i] * samples[i];
        }

        float rms = Mathf.Sqrt(sum / samples.Length);
        Level = Mathf.Clamp01(rms * 8f); // 8x de ganancia para que se note en el medidor
    }

    private void OnDisable()
    {
        if (isRecording)
        {
            Microphone.End(string.IsNullOrEmpty(microphoneDevice) ? Microphone.devices[0] : microphoneDevice);
            isRecording = false;
        }
    }
}
