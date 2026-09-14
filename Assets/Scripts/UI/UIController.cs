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

        private void HandleStateChanged(GameState newState)
        {
            if (newState != GameState.Playing)
            {
                return;
            }

            if (mainMenuPanel != null)
            {
                mainMenuPanel.SetActive(false);
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
