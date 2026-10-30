using KartIHC.Gameplay;
using UnityEngine;

namespace KartIHC.UI
{
    /// <summary>
    /// Switches between the MainMenu/Tutorial/Waiting UI panels and hides all of
    /// them once GameStateManager reports the game has moved into Playing.
    /// </summary>
    public class UIController : MonoBehaviour
    {
        [SerializeField]
        private GameObject mainMenuPanel;

        [SerializeField]
        private GameObject tutorialPanel;

        [SerializeField]
        private GameObject waitingPanel;

        private GameStateManager stateManager;

        private void Awake()
        {
            stateManager = FindFirstObjectByType<GameStateManager>();
            if (stateManager != null)
            {
                stateManager.OnStateChanged += HandleStateChanged;
            }
        }

        private void OnDestroy()
        {
            if (stateManager != null)
            {
                stateManager.OnStateChanged -= HandleStateChanged;
            }
        }

        public void ShowTutorial()
        {
            if (mainMenuPanel != null)
            {
                mainMenuPanel.SetActive(false);
            }
            if (tutorialPanel != null)
            {
                tutorialPanel.SetActive(true);
            }
        }

        public void ShowWaiting()
        {
            if (tutorialPanel != null)
            {
                tutorialPanel.SetActive(false);
            }
            if (waitingPanel != null)
            {
                waitingPanel.SetActive(true);
            }
        }

        /// <summary>Lets the mode selector temporarily replace the existing start menu.</summary>
        public void SetMainMenuVisible(bool visible)
        {
            if (mainMenuPanel != null)
            {
                mainMenuPanel.SetActive(visible);
            }
        }

        private void HandleStateChanged(GameState newState)
        {
            bool showMain = newState == GameState.MainMenu;
            bool hideGameplayPanels = newState == GameState.Playing || newState == GameState.Tutorial;

            if (mainMenuPanel != null)
            {
                mainMenuPanel.SetActive(showMain);
            }

            if (!hideGameplayPanels)
            {
                return;
            }

            if (tutorialPanel != null)
            {
                tutorialPanel.SetActive(false);
            }
            if (waitingPanel != null)
            {
                waitingPanel.SetActive(false);
            }
        }
    }
}
