using KartIHC.Gameplay;
using UnityEngine;

namespace KartIHC.Kart
{
    /// <summary>
    /// Procedural kart audio, no external assets: a looping synthesized engine
    /// whose pitch follows the Rigidbody speed (louder while accelerating), plus
    /// short synthesized effects for turbo, shield, trap, checkpoint and finish
    /// line. Muted when the scene has no AudioListener.
    /// </summary>
    [RequireComponent(typeof(KartController))]
    public class KartAudio : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureOnKart()
        {
            KartController kart = FindFirstObjectByType<KartController>();
            if (kart != null && kart.GetComponent<KartAudio>() == null)
            {
                kart.gameObject.AddComponent<KartAudio>();
            }
        }

        private const int SampleRate = 44100;

        [Header("Volumen")]
        [Range(0f, 1f)] [SerializeField] private float masterVolume = 0.5f;
        [Range(0f, 1f)] [SerializeField] private float engineIdleVolume = 0.18f;
        [Range(0f, 1f)] [SerializeField] private float engineThrottleVolume = 0.35f;
        [Range(0f, 1f)] [SerializeField] private float effectsVolume = 0.7f;

        [Header("Motor")]
        [SerializeField] private float speedForMaxPitch = 25f;
        [SerializeField] private float minPitch = 0.8f;
        [SerializeField] private float maxPitch = 2.4f;
        [SerializeField] private float responsiveness = 6f;

        private KartController kart;
        private Rigidbody rb;
        private LapManager lapManager;
        private AudioSource engineSource;
        private AudioSource effectsSource;

        private AudioClip turboClip;
        private AudioClip shieldClip;
        private AudioClip trapClip;
        private AudioClip checkpointClip;
        private AudioClip finishClip;

        private float listenerCheckTimer;
        private bool hasListener;

        private void Awake()
        {
            kart = GetComponent<KartController>();
            rb = GetComponent<Rigidbody>();

            engineSource = gameObject.AddComponent<AudioSource>();
            engineSource.clip = CreateEngineClip();
            engineSource.loop = true;
            engineSource.playOnAwake = false;
            engineSource.spatialBlend = 0f;
            engineSource.volume = 0f;

            effectsSource = gameObject.AddComponent<AudioSource>();
            effectsSource.playOnAwake = false;
            effectsSource.spatialBlend = 0f;

            turboClip = CreateTurboClip();
            shieldClip = CreateShieldClip();
            trapClip = CreateTrapClip();
            checkpointClip = CreateCheckpointClip();
            finishClip = CreateFinishClip();
        }

        private void OnEnable()
        {
            kart.TurboActivated += PlayTurbo;
            kart.ShieldActivated += PlayShield;
            kart.TrapDropped += PlayTrap;
        }

        private void Start()
        {
            lapManager = FindFirstObjectByType<LapManager>();
            if (lapManager != null)
            {
                lapManager.CheckpointPassed += PlayCheckpoint;
                lapManager.LapCompleted += PlayFinish;
            }

            UpdateListenerState();
            engineSource.Play();
        }

        private void OnDisable()
        {
            kart.TurboActivated -= PlayTurbo;
            kart.ShieldActivated -= PlayShield;
            kart.TrapDropped -= PlayTrap;
        }

        private void OnDestroy()
        {
            if (lapManager != null)
            {
                lapManager.CheckpointPassed -= PlayCheckpoint;
                lapManager.LapCompleted -= PlayFinish;
            }
        }

        private void Update()
        {
            listenerCheckTimer -= Time.unscaledDeltaTime;
            if (listenerCheckTimer <= 0f)
            {
                UpdateListenerState();
            }

            float speed = rb.linearVelocity.magnitude;
            float speed01 = Mathf.Clamp01(speed / speedForMaxPitch);
            bool accelerating = kart.InputEnabled && kart.rearLeft != null && Mathf.Abs(kart.rearLeft.motorTorque) > 1f;

            float targetPitch = Mathf.Lerp(minPitch, maxPitch, speed01) + (accelerating ? 0.1f : 0f);
            float targetVolume = (accelerating ? engineThrottleVolume : engineIdleVolume) * masterVolume;
            if (Time.timeScale <= 0f)
            {
                targetVolume = 0f;
            }

            float blend = 1f - Mathf.Exp(-responsiveness * Time.unscaledDeltaTime);
            engineSource.pitch = Mathf.Lerp(engineSource.pitch, targetPitch, blend);
            engineSource.volume = Mathf.Lerp(engineSource.volume, targetVolume, blend);
        }

        private void UpdateListenerState()
        {
            listenerCheckTimer = 1f;
            hasListener = FindFirstObjectByType<AudioListener>() != null;
            engineSource.mute = !hasListener;
            effectsSource.mute = !hasListener;
        }

        private void PlayTurbo() => PlayEffect(turboClip);
        private void PlayShield() => PlayEffect(shieldClip);
        private void PlayTrap() => PlayEffect(trapClip);
        private void PlayCheckpoint(int index) => PlayEffect(checkpointClip);
        private void PlayFinish(int lap) => PlayEffect(finishClip);

        private void PlayEffect(AudioClip clip)
        {
            if (clip != null)
            {
                effectsSource.PlayOneShot(clip, effectsVolume * masterVolume);
            }
        }

        // ---------- Synthesis ----------

        private delegate float Generator(float t, float duration);

        private static AudioClip Synthesize(string clipName, float duration, Generator generator, bool envelope = true)
        {
            int length = Mathf.CeilToInt(SampleRate * duration);
            float[] data = new float[length];

            for (int i = 0; i < length; i++)
            {
                float t = (float)i / SampleRate;
                float sample = generator(t, duration);

                if (envelope)
                {
                    float attack = Mathf.Clamp01(t / 0.005f);
                    float release = Mathf.Clamp01((duration - t) / 0.04f);
                    sample *= attack * release;
                }

                data[i] = Mathf.Clamp(sample, -1f, 1f);
            }

            AudioClip clip = AudioClip.Create(clipName, length, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static float Sine(float phaseCycles) => Mathf.Sin(2f * Mathf.PI * phaseCycles);

        /// <summary>Band-limited-ish soft saw: a few harmonics with 1/k amplitude.</summary>
        private static float SoftSaw(float phaseCycles)
        {
            float value = 0f;
            for (int k = 1; k <= 6; k++)
            {
                value += Sine(phaseCycles * k) / k;
            }
            return value * 0.55f;
        }

        /// <summary>Phase (in cycles) of a linear frequency sweep f0 -> f1 over duration.</summary>
        private static float SweepPhase(float t, float duration, float f0, float f1)
        {
            return f0 * t + (f1 - f0) * t * t / (2f * duration);
        }

        private static AudioClip CreateEngineClip()
        {
            // 1 s loop; every frequency is a whole number of Hz so the loop is seamless.
            const float baseFrequency = 50f;
            return Synthesize("Engine", 1f, (t, d) =>
            {
                float saw = SoftSaw(baseFrequency * t);
                float square = (float)System.Math.Tanh(3f * Sine(baseFrequency * 0.5f * t)) * 0.35f;
                float putt = 0.8f + 0.2f * Sine(25f * t);
                return (saw + square) * putt * 0.5f;
            }, envelope: false);
        }

        private static AudioClip CreateTurboClip()
        {
            var random = new System.Random(1);
            return Synthesize("Turbo", 0.6f, (t, d) =>
            {
                float progress = t / d;
                float tone = SoftSaw(SweepPhase(t, d, 180f, 720f)) * 0.5f;
                float noise = ((float)random.NextDouble() * 2f - 1f) * 0.35f * (1f - progress);
                return (tone + noise) * (1f - progress * 0.6f);
            });
        }

        private static AudioClip CreateShieldClip()
        {
            return Synthesize("Shield", 0.7f, (t, d) =>
            {
                float progress = t / d;
                float shimmer = 1f + 0.3f * Sine(12f * t);
                float chord = Sine(SweepPhase(t, d, 440f, 660f)) + 0.6f * Sine(SweepPhase(t, d, 660f, 990f));
                return chord * shimmer * 0.35f * (1f - progress);
            });
        }

        private static AudioClip CreateTrapClip()
        {
            return Synthesize("Trap", 0.35f, (t, d) =>
            {
                float progress = t / d;
                float square = Mathf.Sign(Sine(SweepPhase(t, d, 520f, 140f)));
                return square * 0.25f * (1f - progress);
            });
        }

        private static AudioClip CreateCheckpointClip()
        {
            return Synthesize("Checkpoint", 0.22f, (t, d) =>
            {
                float frequency = t < 0.1f ? 880f : 1320f;
                float gate = (t < 0.09f || t > 0.11f) ? 1f : 0f;
                return Sine(frequency * t) * 0.4f * gate;
            });
        }

        private static AudioClip CreateFinishClip()
        {
            float[] notes = { 523.25f, 659.25f, 783.99f, 1046.5f };
            const float noteLength = 0.15f;
            return Synthesize("Finish", noteLength * notes.Length + 0.35f, (t, d) =>
            {
                int note = Mathf.Min((int)(t / noteLength), notes.Length - 1);
                float local = t - note * noteLength;
                float decay = Mathf.Exp(-local * (note == notes.Length - 1 ? 3f : 12f));
                return (Sine(notes[note] * t) + 0.3f * Sine(notes[note] * 2f * t)) * 0.35f * decay;
            });
        }
    }
}
