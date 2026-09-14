using System;
using KartIHC.Kart;
using KartIHC.Network;
using UnityEngine;

namespace KartIHC.Gameplay
{
    public enum GameState
    {
        MainMenu,
        Tutorial,
        Playing
    }

    /// <summary>
    /// Owns the current high-level game state (menu/tutorial/playing) and gates kart input
    /// accordingly. Listens to NetworkReceiver.OnStartRace so the "start race" voice command
    /// can jump straight to Playing regardless of what screen is currently showing.
    /// </summary>
    public class GameStateManager : MonoBehaviour
    {
        public event Action<GameState> OnStateChanged;

        public GameState CurrentState { get; private set; } = GameState.MainMenu;

        private KartController kartController;
        private NetworkReceiver networkReceiver;

        private void Awake()
        {
            kartController = FindFirstObjectByType<KartController>();
            networkReceiver = FindFirstObjectByType<NetworkReceiver>();

            if (networkReceiver != null)
            {
                networkReceiver.OnStartRace += HandleNetworkStartRace;
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
        }

        public void StartTutorial()
        {
            SetState(GameState.Tutorial);
        }

        public void BeginPlaying()
        {
            SetState(GameState.Playing);
        }

        private void HandleNetworkStartRace()
        {
            BeginPlaying();
        }

        private void SetState(GameState newState)
        {
            CurrentState = newState;

            if (kartController != null)
            {
                kartController.SetInputEnabled(newState == GameState.Playing);
            }

            OnStateChanged?.Invoke(newState);
        }
    }
}
