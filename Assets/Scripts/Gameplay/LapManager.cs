using System;
using System.Collections.Generic;
using KartIHC.Kart;
using KartIHC.UI;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace KartIHC.Gameplay
{
    /// <summary>
    /// Counts laps for the player kart. A lap only counts if every RaceCheckpoint
    /// was passed in order before crossing the finish line. Checkpoints only
    /// count while GameStateManager is in Playing. Shows lap/lap time/total time
    /// in the top-right corner, and a results panel at the end (Enter restarts).
    /// Created by KartIHC/Build Checkpoints.
    /// </summary>
    public class LapManager : MonoBehaviour
    {
        public const string PlayerTag = "Player";

        [SerializeField] private int totalLaps = 3;

        public event Action<int> CheckpointPassed;   // checkpoint index
        public event Action<int> LapCompleted;       // lap number just completed
        public event Action RaceFinished;

        public int CurrentLap { get; private set; } = 1;
        public bool IsFinished { get; private set; }
        public float TotalTime { get; private set; }
        public float CurrentLapTime { get; private set; }
        public float BestLapTime { get; private set; } = float.MaxValue;

        private GameStateManager gameStateManager;
        private KartController kartController;
        private readonly List<RaceCheckpoint> checkpoints = new List<RaceCheckpoint>();
        private RaceCheckpoint finishLine;
        private RaceCheckpoint lastPassed;
        private int nextCheckpoint;
        private GameState previousState = GameState.MainMenu;

        private readonly List<float> lapTimes = new List<float>();

        public static bool IsPlayer(Collider other)
        {
            if (other.CompareTag(PlayerTag))
            {
                return true;
            }

            Rigidbody body = other.attachedRigidbody;
            return body != null && body.CompareTag(PlayerTag);
        }

        private void Awake()
        {
            gameStateManager = FindFirstObjectByType<GameStateManager>();
            kartController = FindFirstObjectByType<KartController>();

            foreach (RaceCheckpoint checkpoint in FindObjectsByType<RaceCheckpoint>(FindObjectsSortMode.None))
            {
                checkpoint.lapManager = this;
                if (checkpoint.isFinishLine)
                {
                    finishLine = checkpoint;
                }
                else
                {
                    checkpoints.Add(checkpoint);
                }
            }

            checkpoints.Sort((a, b) => a.index.CompareTo(b.index));

            if (finishLine == null)
            {
                Debug.LogWarning("[LapManager] No finish line RaceCheckpoint found. Run KartIHC/Build Checkpoints.");
            }

            if (gameStateManager != null)
            {
                gameStateManager.OnStateChanged += HandleStateChanged;
            }
        }

        private void OnDestroy()
        {
            if (gameStateManager != null)
            {
                gameStateManager.OnStateChanged -= HandleStateChanged;
            }
        }

        private void HandleStateChanged(GameState state)
        {
            // A new race starts when entering Playing from anything but Paused.
            if ((state == GameState.Playing && previousState != GameState.Paused) || state == GameState.MainMenu)
            {
                ResetRace();
            }

            previousState = state;
        }

        private bool IsPlaying => gameStateManager != null && gameStateManager.CurrentState == GameState.Playing;

        private void Update()
        {
            if (IsFinished)
            {
                // GameStateManager re-enables input on pause/resume; keep the kart stopped.
                StopKart();

                if (WasEnterPressed())
                {
                    RestartRace();
                }

                return;
            }

            if (IsPlaying)
            {
                TotalTime += Time.deltaTime;
                CurrentLapTime += Time.deltaTime;
            }
        }

        public void HandleCheckpoint(RaceCheckpoint checkpoint)
        {
            if (!IsPlaying || IsFinished)
            {
                return;
            }

            if (checkpoint.isFinishLine)
            {
                if (nextCheckpoint >= checkpoints.Count)
                {
                    CompleteLap();
                }
                return;
            }

            if (nextCheckpoint < checkpoints.Count && checkpoints[nextCheckpoint] == checkpoint)
            {
                lastPassed = checkpoint;
                nextCheckpoint++;
                CheckpointPassed?.Invoke(checkpoint.index);
            }
        }

        /// <summary>
        /// Pose of the last checkpoint passed (or the finish line if none yet), facing the racing direction.
        /// </summary>
        public bool TryGetRespawnPose(out Vector3 position, out Quaternion rotation)
        {
            RaceCheckpoint anchor = lastPassed != null ? lastPassed : finishLine;
            if (anchor == null)
            {
                position = Vector3.zero;
                rotation = Quaternion.identity;
                return false;
            }

            Vector3 forward = Vector3.ProjectOnPlane(anchor.transform.forward, Vector3.up).normalized;
            rotation = Quaternion.LookRotation(forward, Vector3.up);
            position = anchor.transform.position;
            return true;
        }

        private void CompleteLap()
        {
            BestLapTime = Mathf.Min(BestLapTime, CurrentLapTime);
            lapTimes.Add(CurrentLapTime);
            int completedLap = CurrentLap;
            LapCompleted?.Invoke(completedLap);

            if (completedLap >= totalLaps)
            {
                FinishRace();
                return;
            }

            CurrentLap++;
            CurrentLapTime = 0f;
            nextCheckpoint = 0;
            lastPassed = finishLine;
        }

        private void FinishRace()
        {
            IsFinished = true;
            StopKart();
            RaceFinished?.Invoke();
        }

        private void StopKart()
        {
            if (kartController == null)
            {
                return;
            }

            kartController.InputEnabled = false;
            // With input disabled KartController keeps the last torque applied, so clear it here.
            SetWheel(kartController.frontLeft, 0f, kartController.brakeTorque);
            SetWheel(kartController.frontRight, 0f, kartController.brakeTorque);
            SetWheel(kartController.rearLeft, 0f, kartController.brakeTorque);
            SetWheel(kartController.rearRight, 0f, kartController.brakeTorque);
        }

        private static void SetWheel(WheelCollider wheel, float motor, float brake)
        {
            if (wheel == null)
            {
                return;
            }

            wheel.motorTorque = motor;
            wheel.brakeTorque = brake;
        }

        private void RestartRace()
        {
            if (kartController != null)
            {
                kartController.ResetToStart();
                kartController.SetInputEnabled(IsPlaying);
            }

            ResetRace();
        }

        private void ResetRace()
        {
            IsFinished = false;
            CurrentLap = 1;
            TotalTime = 0f;
            CurrentLapTime = 0f;
            BestLapTime = float.MaxValue;
            lapTimes.Clear();
            nextCheckpoint = 0;
            lastPassed = null;
        }

        private static bool WasEnterPressed()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null)
            {
                return Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame;
            }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
#else
            return false;
#endif
        }

        public static string FormatTime(float seconds)
        {
            int centiseconds = Mathf.FloorToInt(seconds * 100f);
            return $"{centiseconds / 6000:00}:{centiseconds / 100 % 60:00}.{centiseconds % 100:00}";
        }

        private void OnGUI()
        {
            bool inRace = gameStateManager == null ||
                          gameStateManager.CurrentState == GameState.Playing ||
                          gameStateManager.CurrentState == GameState.Paused;
            if (!inRace && !IsFinished)
            {
                return;
            }

            KartUIStyle.Ensure();
            DrawRaceHud();

            if (IsFinished)
            {
                DrawResults();
            }
        }

        /// <summary>Top-right panel: lap, lap time, total, best lap and checkpoint progress.</summary>
        private void DrawRaceHud()
        {
            float s = KartUIStyle.Scale;
            float width = 270f * s, row = 30f * s, pad = 16f * s;
            float height = pad * 2f + 54f * s + row * 3f + 14f * s;
            Rect panel = new Rect(Screen.width - width - 20f * s, 20f * s, width, height);
            KartUIStyle.Panel(panel);

            float x = panel.x + pad + 4f * s, w = panel.width - pad * 2f - 4f * s, y = panel.y + pad;
            GUI.Label(new Rect(x, y, w, 54f * s), "VUELTA", KartUIStyle.Caption);
            GUI.Label(new Rect(x, y, w, 54f * s), $"{Mathf.Min(CurrentLap, totalLaps)}<size={Mathf.RoundToInt(26 * s)}><color={KartUIStyle.MutedHex}>/{totalLaps}</color></size>", KartUIStyle.Big);
            y += 54f * s;

            Row(x, ref y, w, row, "Vuelta", FormatTime(CurrentLapTime));
            Row(x, ref y, w, row, "Total", FormatTime(TotalTime));
            Row(x, ref y, w, row, "Mejor", BestLapTime < float.MaxValue ? FormatTime(BestLapTime) : "--:--.--");

            KartUIStyle.Pips(new Rect(x, y + 4f * s, w, 8f * s), Mathf.Min(nextCheckpoint, checkpoints.Count), checkpoints.Count);
        }

        private static void Row(float x, ref float y, float width, float height, string caption, string value)
        {
            GUI.Label(new Rect(x, y, width, height), caption.ToUpperInvariant(), KartUIStyle.Caption);
            GUI.Label(new Rect(x, y, width, height), value, KartUIStyle.Value);
            y += height;
        }

        /// <summary>Centered results card: total time, per-lap times (best highlighted), restart hint.</summary>
        private void DrawResults()
        {
            float s = KartUIStyle.Scale;
            KartUIStyle.Fill(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.35f));

            float width = Mathf.Min(560f * s, Screen.width - 32f);
            float row = 32f * s;
            float height = 70f * s + 60f * s + 20f * s + row * (lapTimes.Count + 1) + 70f * s;
            Rect card = new Rect((Screen.width - width) / 2f, (Screen.height - height) / 2f, width, height);
            KartUIStyle.Panel(card);

            float pad = 28f * s, x = card.x + pad, w = card.width - pad * 2f, y = card.y + 18f * s;
            GUI.Label(new Rect(card.x, y, card.width, 64f * s), "¡LLEGASTE!", KartUIStyle.Title);
            y += 70f * s;

            GUI.Label(new Rect(x, y, w, 56f * s), "TIEMPO", KartUIStyle.Caption);
            GUI.Label(new Rect(x, y, w, 56f * s), FormatTime(TotalTime), KartUIStyle.Big);
            y += 60f * s;
            KartUIStyle.Fill(new Rect(x, y + 8f * s, w, 1f), new Color(1f, 1f, 1f, 0.15f));
            y += 20f * s;

            for (int i = 0; i < lapTimes.Count; i++)
            {
                bool best = Mathf.Approximately(lapTimes[i], BestLapTime);
                string value = best ? $"<color={KartUIStyle.GoodHex}>{FormatTime(lapTimes[i])}</color>" : FormatTime(lapTimes[i]);
                GUI.Label(new Rect(x, y, w, row), $"VUELTA {i + 1}", KartUIStyle.Caption);
                GUI.Label(new Rect(x, y, w, row), value, KartUIStyle.Value);
                y += row;
            }
            GUI.Label(new Rect(x, y, w, row), "MEJOR VUELTA", KartUIStyle.Caption);
            GUI.Label(new Rect(x, y, w, row), $"<color={KartUIStyle.GoodHex}>{FormatTime(BestLapTime)}</color>", KartUIStyle.Value);
            y += row + 18f * s;

            GUI.Label(new Rect(card.x, y, card.width, 30f * s), $"Pulsa <b><color={KartUIStyle.AccentHex}>Enter</color></b> para reiniciar la carrera", KartUIStyle.Hint);
        }
    }
}
