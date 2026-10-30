using System.Collections;
using KartIHC.Kart;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KartIHC.Gameplay
{
    /// <summary>
    /// Runs the three voice-selected tutorials:
    /// 1) legs: accelerate and brake,
    /// 2) DualShock gyro steering,
    /// 3) voice commands.
    /// </summary>
    public class TutorialManager : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureTutorialManagerExists()
        {
            if (FindFirstObjectByType<TutorialManager>() != null || FindFirstObjectByType<GameStateManager>() == null)
            {
                return;
            }

            GameObject tutorialObject = new GameObject("TutorialManager");
            tutorialObject.AddComponent<TutorialManager>();
        }

        [Header("References")]
        [SerializeField] private GameStateManager gameStateManager;
        [SerializeField] private LegMotionInputReceiver legInput;
        [SerializeField] private JoyShockGyroSteeringInput steeringInput;
        [SerializeField] private VoicePowerUpInput voiceInput;
        [SerializeField] private KartController kartController;

        [Header("Thresholds")]
        [SerializeField] private float steeringThreshold = 0.45f;
        [SerializeField] private float completionDelay = 2f;

        private bool active;
        private int tutorialId;
        private string title = "";
        private string instruction = "";
        private string progress = "";
        private bool completed;
        private Coroutine completionRoutine;

        private bool didAccelerate;
        private bool didBrake;
        private bool didSteerLeft;
        private bool didSteerRight;
        private bool didTurbo;
        private bool didShield;
        private bool didTrap;
        private bool didPause;
        private bool didResume;
        private bool didControls;
        private bool didRestart;
        private bool didChangeCamera;
        private bool didOpenDoor;
        private bool didZoom;

        private GUIStyle titleStyle;
        private GUIStyle textStyle;
        private GUIStyle boxStyle;
        private bool stylesReady;

        private void Awake()
        {
            if (gameStateManager == null)
            {
                gameStateManager = FindFirstObjectByType<GameStateManager>();
            }

            if (legInput == null)
            {
                legInput = FindFirstObjectByType<LegMotionInputReceiver>();
            }

            if (steeringInput == null)
            {
                steeringInput = FindFirstObjectByType<JoyShockGyroSteeringInput>();
            }

            if (voiceInput == null)
            {
                voiceInput = FindFirstObjectByType<VoicePowerUpInput>();
            }

            if (kartController == null)
            {
                kartController = FindFirstObjectByType<KartController>();
            }

            if (gameStateManager != null)
            {
                gameStateManager.OnTutorialStarted += StartTutorial;
                gameStateManager.OnStateChanged += HandleStateChanged;
            }

            if (voiceInput != null)
            {
                voiceInput.PowerUpRequested += HandlePowerUpRequested;
                voiceInput.PauseRequested += HandlePauseRequested;
                voiceInput.ResumeRequested += HandleResumeRequested;
                voiceInput.ControlsRequested += HandleControlsRequested;
                voiceInput.RestartRequested += HandleRestartRequested;
                voiceInput.CancelTutorialRequested += HandleCancelTutorialRequested;
            }
        }

        private void OnDestroy()
        {
            if (gameStateManager != null)
            {
                gameStateManager.OnTutorialStarted -= StartTutorial;
                gameStateManager.OnStateChanged -= HandleStateChanged;
            }

            if (voiceInput != null)
            {
                voiceInput.PowerUpRequested -= HandlePowerUpRequested;
                voiceInput.PauseRequested -= HandlePauseRequested;
                voiceInput.ResumeRequested -= HandleResumeRequested;
                voiceInput.ControlsRequested -= HandleControlsRequested;
                voiceInput.RestartRequested -= HandleRestartRequested;
                voiceInput.CancelTutorialRequested -= HandleCancelTutorialRequested;
            }
        }

        private void Update()
        {
            if (!active || completed)
            {
                return;
            }

            switch (tutorialId)
            {
                case 1:
                    UpdateLegTutorial();
                    break;
                case 2:
                    UpdateSteeringTutorial();
                    break;
                case 3:
                    UpdateVoiceTutorial();
                    break;
                case 4:
                    UpdateHackerTutorial();
                    break;
            }
        }

        private void StartTutorial(int id)
        {
            ResetFlags();

            active = true;
            completed = false;
            tutorialId = id;

            if (completionRoutine != null)
            {
                StopCoroutine(completionRoutine);
                completionRoutine = null;
            }

            switch (tutorialId)
            {
                case 1:
                    title = "TUTORIAL 1: ACELERAR Y FRENAR";
                    instruction = "Pierna izquierda: ACELERAR. Pierna derecha: FRENAR.";
                    break;
                case 2:
                    title = "TUTORIAL 2: GIRAR EL MANDO";
                    instruction = "Inclina el mando a la izquierda y a la derecha.\nSin mando: usa A/D.";
                    steeringInput?.Calibrate();
                    break;
                default:
                    if (tutorialId == 4)
                    {
                        title = "TUTORIAL 4: HACKER";
                        instruction = "Conecta un mando PS4. Mantén □ para cambiar de cámara; mueve el mando para mirar con giroscopio; usa L2/R2 para zoom y mantén ○ para abrir una puerta. Di \"cancelar tutorial\" para salir.";
                    }
                    else
                    {
                        title = "TUTORIAL 3: COMANDOS DE VOZ";
                        instruction = "Di cada comando de la lista. Di \"cancelar tutorial\" para salir.";
                    }
                    break;
            }

            progress = "Esperando accion...";
        }

        private void UpdateHackerTutorial()
        {
            EscapeModeNetworkBridge bridge = EscapeModeNetworkBridge.Instance;
            if (bridge != null)
            {
                didChangeCamera |= bridge.SelectedCameraIndex > 0;
                didOpenDoor |= bridge.AnyDoorOpen;
                didZoom |= bridge.ZoomUsed;
            }
            progress = BuildChecklistLine("Mantener □ para cambiar cámara", didChangeCamera) + "\n" +
                       BuildChecklistLine("Usar L2 o R2 para zoom", didZoom) + "\n" +
                       BuildChecklistLine("Mantener ○ para abrir puerta", didOpenDoor);
            if (didChangeCamera && didZoom && didOpenDoor) CompleteTutorial();
        }

        private void HandleStateChanged(GameState state)
        {
            if (state == GameState.MainMenu)
            {
                active = false;
                completed = false;
            }
        }

        private void UpdateLegTutorial()
        {
            if (legInput != null && legInput.IsConnected)
            {
                if (legInput.Throttle > 0.1f)
                {
                    didAccelerate = true;
                }

                if (legInput.Brake > 0.1f)
                {
                    didBrake = true;
                }
            }

            progress =
                BuildChecklistLine("Desliza la pierna izquierda  para Acelerar ", didAccelerate) + "\n" +
                BuildChecklistLine("Levanta la pierna derecha para Frenar y Retroceder ", didBrake);

            if (didAccelerate && didBrake)
            {
                CompleteTutorial();
            }
        }

        private void UpdateSteeringTutorial()
        {
            float steering = steeringInput != null && steeringInput.IsConnected
                ? steeringInput.Steering
                : DeviceTiltReader.ReadKeyboardAxis();

            if (steering < -steeringThreshold)
            {
                didSteerLeft = true;
            }

            if (steering > steeringThreshold)
            {
                didSteerRight = true;
            }

            progress =
                BuildChecklistLine("Girar mando a la izquierda", didSteerLeft) + "\n" +
                BuildChecklistLine("Girar mando a la derecha", didSteerRight);

            if (didSteerLeft && didSteerRight)
            {
                CompleteTutorial();
            }
        }

        private void UpdateVoiceTutorial()
        {
            progress =
                BuildChecklistLine("Decir turbo", didTurbo) + "\n" +
                BuildChecklistLine("Decir escudo", didShield) + "\n" +
                BuildChecklistLine("Decir trampa", didTrap) + "\n" +
                BuildChecklistLine("Decir pausa", didPause) + "\n" +
                BuildChecklistLine("Decir reanudar", didResume) + "\n" +
                BuildChecklistLine("Decir controles", didControls) + "\n" +
                BuildChecklistLine("Decir reiniciar", didRestart);

            if (didTurbo && didShield && didTrap && didPause && didResume && didControls && didRestart)
            {
                CompleteTutorial();
            }
        }

        private void HandlePowerUpRequested(string powerUpId)
        {
            if (!active || tutorialId != 3)
            {
                return;
            }

            switch (powerUpId)
            {
                case "turbo":
                    didTurbo = true;
                    break;
                case "escudo":
                    didShield = true;
                    break;
                case "trampa":
                    didTrap = true;
                    break;
            }
        }

        private void HandlePauseRequested()
        {
            if (active && tutorialId == 3)
            {
                didPause = true;
            }
        }

        private void HandleResumeRequested()
        {
            if (active && tutorialId == 3)
            {
                didResume = true;
            }
        }

        private void HandleControlsRequested()
        {
            if (active && tutorialId == 3)
            {
                didControls = true;
            }
        }

        private void HandleRestartRequested()
        {
            if (active && tutorialId == 3)
            {
                didRestart = true;
            }
        }

        private void HandleCancelTutorialRequested()
        {
            if (!active) return;
            if (tutorialId == 4)
            {
                // GameStateManager handles tutorial 4's voice cancel by loading ModeMenu.
                // It owns the same voice event and runs before this listener.
                return;
            }
            else gameStateManager?.ReturnToMainMenu();
        }

        private void CompleteTutorial()
        {
            if (completed)
            {
                return;
            }

            completed = true;
            progress = "Tutorial completado. Volviendo al inicio...";
            completionRoutine = StartCoroutine(ReturnToMenuAfterDelay());
        }

        private IEnumerator ReturnToMenuAfterDelay()
        {
            yield return new WaitForSeconds(completionDelay);
            if (tutorialId == 4)
            {
                EscapeModeNetworkBridge.Stop();
                SceneManager.LoadScene("ModeMenu", LoadSceneMode.Single);
                yield break;
            }
            gameStateManager?.ReturnToMainMenu();
        }

        private void ResetFlags()
        {
            didAccelerate = false;
            didBrake = false;
            didSteerLeft = false;
            didSteerRight = false;
            didTurbo = false;
            didShield = false;
            didTrap = false;
            didPause = false;
            didResume = false;
            didControls = false;
            didRestart = false;
        }

        private string BuildChecklistLine(string label, bool done)
        {
            return $"{(done ? "[OK]" : "[  ]")} {label}";
        }

        private void BuildStyles()
        {
            titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 34,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };

            textStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 24,
                alignment = TextAnchor.MiddleLeft,
                wordWrap = true,
                normal = { textColor = Color.white }
            };

            boxStyle = new GUIStyle(GUI.skin.box);
            stylesReady = true;
        }

        private void OnGUI()
        {
            if (!active)
            {
                DrawStartCommands();
                return;
            }

            if (!stylesReady)
            {
                BuildStyles();
            }

            float width = Mathf.Min(760f, Screen.width * 0.85f);
            float contentWidth = width - 24f;
            float height = 70f + titleStyle.CalcHeight(new GUIContent(title), contentWidth) + textStyle.CalcHeight(new GUIContent(instruction), contentWidth) + textStyle.CalcHeight(new GUIContent(progress), contentWidth);
            Rect panel = new Rect((Screen.width - width) / 2f, 24f, width, height);

            GUILayout.BeginArea(panel, boxStyle);
            GUILayout.Space(10);
            GUILayout.Label(title, titleStyle);
            GUILayout.Space(12);
            GUILayout.Label(instruction, textStyle);
            GUILayout.Space(16);
            GUILayout.Label(progress, textStyle);
            GUILayout.EndArea();
        }

        private void DrawStartCommands()
        {
            if (gameStateManager == null || gameStateManager.CurrentState != GameState.MainMenu)
            {
                return;
            }

            if (!stylesReady)
            {
                BuildStyles();
            }

            Rect panel = new Rect(24f, Screen.height - 235f, Mathf.Min(760f, Screen.width - 48f), 210f);
            GUILayout.BeginArea(panel, boxStyle);
            GUILayout.Label("COMANDOS DE INICIO", titleStyle);
            GUILayout.Label(
                "Di \"INICIAR\" para jugar\n" +
                "Di \"tutorial uno\" para acelerar/frenar\n" +
                "Di \"tutorial dos\" para girar el mando\n" +
                "Di \"tutorial tres\" para practicar comandos de voz\n" +
                "Teclas: Enter = circuito normal, F1/F2/F3 = tutoriales, Esc = cancelar tutorial",
                textStyle);
            GUILayout.EndArea();
        }
    }
}
