using System;
using KartIHC.Kart;
using KartIHC.Network;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KartIHC.Gameplay
{
    public enum GameState
    {
        MainMenu,
        Tutorial,
        Playing,
        Paused
    }

    /// <summary>
    /// Owns the current high-level game state (menu/tutorial/playing/paused) and gates kart
    /// input accordingly. Listens to NetworkReceiver.OnStartRace and to VoicePowerUpInput so
    /// voice commands can drive race start, pause, resume, controls overlay and restart.
    ///
    /// Pause/Resume/Controls/Restart are only honored while in the matching state (see each
    /// handler), exactly as requested: those 3 commands only "do something" while paused.
    /// </summary>
    public class GameStateManager : MonoBehaviour
    {
        public event Action<GameState> OnStateChanged;
        public event Action<int> OnTutorialStarted;

        public GameState CurrentState { get; private set; } = GameState.MainMenu;
        public int CurrentTutorialId { get; private set; }

        /// <summary>True while the pause menu's extra controls panel should be shown.</summary>
        public bool ShowControlsOverlay { get; private set; }

        private KartController kartController;
        private NetworkReceiver networkReceiver;
        private VoicePowerUpInput voiceInput;
        private GameState stateBeforePause = GameState.Playing;

        private void Awake()
        {
            kartController = FindFirstObjectByType<KartController>();
            networkReceiver = FindFirstObjectByType<NetworkReceiver>();
            voiceInput = FindFirstObjectByType<VoicePowerUpInput>();

            if (networkReceiver != null)
            {
                networkReceiver.OnStartRace += HandleNetworkStartRace;
            }

            if (voiceInput != null)
            {
                voiceInput.StartRaceRequested += HandleVoiceStartRace;
                voiceInput.TutorialRequested += HandleVoiceTutorial;
                voiceInput.CancelTutorialRequested += HandleVoiceCancelTutorial;
                voiceInput.PauseRequested += HandleVoicePause;
                voiceInput.ResumeRequested += HandleVoiceResume;
                voiceInput.ControlsRequested += HandleVoiceControls;
                voiceInput.RestartRequested += HandleVoiceRestart;
            }
        }

        private void Start()
        {
            SetState(GameState.MainMenu);
        }

        private void OnDestroy()
        {
            if (networkReceiver != null)
            {
                networkReceiver.OnStartRace -= HandleNetworkStartRace;
            }

            if (voiceInput != null)
            {
                voiceInput.StartRaceRequested -= HandleVoiceStartRace;
                voiceInput.TutorialRequested -= HandleVoiceTutorial;
                voiceInput.CancelTutorialRequested -= HandleVoiceCancelTutorial;
                voiceInput.PauseRequested -= HandleVoicePause;
                voiceInput.ResumeRequested -= HandleVoiceResume;
                voiceInput.ControlsRequested -= HandleVoiceControls;
                voiceInput.RestartRequested -= HandleVoiceRestart;
            }

            // Por si se destruye el objeto mientras el juego estaba pausado.
            Time.timeScale = 1f;
        }

        public void StartTutorial()
        {
            BeginTutorial(1);
        }

        public void BeginPlaying()
        {
            CurrentTutorialId = 0;
            SetState(GameState.Playing);
        }

        public void BeginTutorial(int tutorialId)
        {
            CurrentTutorialId = Mathf.Clamp(tutorialId, 1, 4);
            Time.timeScale = 1f;
            ShowControlsOverlay = false;
            SetState(GameState.Tutorial);
            OnTutorialStarted?.Invoke(CurrentTutorialId);
        }

        public void ReturnToMainMenu()
        {
            CurrentTutorialId = 0;
            Time.timeScale = 1f;
            ShowControlsOverlay = false;
            SetState(GameState.MainMenu);
        }

        private void HandleNetworkStartRace()
        {
            BeginPlaying();
        }

        private void HandleVoiceTutorial(int tutorialId)
        {
            // Los comandos de tutorial solo se aceptan desde el inicio; asi no
            // interrumpen una carrera normal o un tutorial activo por accidente.
            if (CurrentState != GameState.MainMenu)
            {
                return;
            }

            BeginTutorial(tutorialId);
        }

        private void HandleVoiceStartRace()
        {
            // Comando exclusivo de la pantalla de inicio: no interrumpe una
            // carrera, tutorial o pausa ya activos.
            if (CurrentState != GameState.MainMenu)
            {
                return;
            }

            BeginPlaying();
        }

        private void HandleVoiceCancelTutorial()
        {
            if (CurrentState != GameState.Tutorial)
            {
                return;
            }

            if (CurrentTutorialId == 4)
            {
                EscapeModeNetworkBridge.Stop();
                SceneManager.LoadScene("ModeMenu", LoadSceneMode.Single);
                return;
            }

            ReturnToMainMenu();
        }

        private void HandleVoicePause()
        {
            // Solo se puede pausar mientras se esta jugando.
            if (CurrentState != GameState.Playing)
            {
                return;
            }

            stateBeforePause = CurrentState;
            Time.timeScale = 0f;
            SetState(GameState.Paused);
        }

        private void HandleVoiceResume()
        {
            // Solo tiene efecto si el juego esta pausado.
            if (CurrentState != GameState.Paused)
            {
                return;
            }

            Time.timeScale = 1f;
            ShowControlsOverlay = false;
            SetState(stateBeforePause);
        }

        private void HandleVoiceControls()
        {
            // Solo tiene efecto si el juego esta pausado.
            if (CurrentState != GameState.Paused)
            {
                return;
            }

            ShowControlsOverlay = !ShowControlsOverlay;
        }

        private void HandleVoiceRestart()
        {
            // En circuito normal reiniciar vuelve al inicio para poder elegir
            // circuito normal o cualquiera de los tutoriales otra vez.
            if (CurrentState != GameState.Playing && CurrentState != GameState.Paused)
            {
                return;
            }

            ReturnToMainMenu();
        }

        private void SetState(GameState newState)
        {
            CurrentState = newState;

            if (kartController != null)
            {
                kartController.SetInputEnabled(newState == GameState.Playing || (newState == GameState.Tutorial && CurrentTutorialId != 4));
            }

            OnStateChanged?.Invoke(newState);
        }
    }
}
