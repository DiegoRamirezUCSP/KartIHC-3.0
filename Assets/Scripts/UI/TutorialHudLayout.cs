using KartIHC.Gameplay;
using KartIHC.Kart;
using UnityEngine;

namespace KartIHC.UI
{
    /// <summary>
    /// Mientras hay un tutorial abierto, apaga el panel grande de GameHUD (que se
    /// encima con el panel del tutorial) y dibuja en su lugar una franja compacta
    /// en la esquina inferior derecha. Al salir del tutorial vuelve a encender
    /// GameHUD tal cual estaba. No modifica GameHUD.
    /// Se crea solo al cargar la escena si existe un GameHUD.
    /// </summary>
    public class TutorialHudLayout : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureExists()
        {
            if (FindFirstObjectByType<TutorialHudLayout>() != null || FindFirstObjectByType<GameHUD>() == null)
            {
                return;
            }

            new GameObject("TutorialHudLayout").AddComponent<TutorialHudLayout>();
        }

        [SerializeField] private int compactFontSize = 16;
        [SerializeField] private float margin = 12f;

        private GameHUD gameHud;
        private GameStateManager gameStateManager;
        private JoyShockGyroSteeringInput steeringInput;
        private LegMotionInputReceiver legInput;
        private VoicePowerUpInput voiceInput;
        private KartController kartController;

        private GUIStyle labelStyle;
        private bool hudHiddenByUs;

        private bool TutorialOpen =>
            gameStateManager != null && gameStateManager.CurrentState == GameState.Tutorial;

        private void Awake()
        {
            gameHud = FindFirstObjectByType<GameHUD>();
            gameStateManager = FindFirstObjectByType<GameStateManager>();
            steeringInput = FindFirstObjectByType<JoyShockGyroSteeringInput>();
            legInput = FindFirstObjectByType<LegMotionInputReceiver>();
            voiceInput = FindFirstObjectByType<VoicePowerUpInput>();
            kartController = FindFirstObjectByType<KartController>();
        }

        private void Update()
        {
            if (gameHud == null)
            {
                return;
            }

            if (TutorialOpen && gameHud.enabled)
            {
                gameHud.enabled = false;
                hudHiddenByUs = true;
            }
            else if (!TutorialOpen && hudHiddenByUs)
            {
                gameHud.enabled = true;
                hudHiddenByUs = false;
            }
        }

        private void OnDisable()
        {
            if (gameHud != null && hudHiddenByUs)
            {
                gameHud.enabled = true;
                hudHiddenByUs = false;
            }
        }

        private void OnGUI()
        {
            if (!hudHiddenByUs)
            {
                return;
            }

            KartUIStyle.Ensure();
            if (labelStyle == null || labelStyle.fontSize != Mathf.RoundToInt(compactFontSize * KartUIStyle.Scale))
            {
                labelStyle = new GUIStyle(KartUIStyle.Body)
                {
                    fontSize = Mathf.RoundToInt(compactFontSize * KartUIStyle.Scale),
                    alignment = TextAnchor.MiddleCenter
                };
            }

            string line =
                $"{Caption("MANDO")} {Status(steeringInput != null && steeringInput.IsConnected)}    " +
                $"{Caption("PIERNAS")} {Status(legInput != null && legInput.IsConnected)}    " +
                $"{Caption("VOZ")} {Status(voiceInput != null && voiceInput.IsListening)}    " +
                $"{Caption("POWER")} {PowerText()}";

            Vector2 size = labelStyle.CalcSize(new GUIContent(line));
            float width = size.x + KartUIStyle.S(32f);
            float height = size.y + KartUIStyle.S(16f);
            float edge = KartUIStyle.S(margin);
            Rect area = new Rect(Screen.width - width - edge, Screen.height - height - edge, width, height);

            KartUIStyle.Panel(area);
            GUI.Label(area, line, labelStyle);
        }

        private static string Status(bool ok)
        {
            return ok ? $"<color={KartUIStyle.GoodHex}><b>OK</b></color>" : "<color=#777777>--</color>";
        }

        private static string Caption(string text)
        {
            return $"<color={KartUIStyle.MutedHex}>{text}</color>";
        }

        private string PowerText()
        {
            if (kartController == null)
            {
                return "<color=#888888>N/A</color>";
            }

            if (kartController.IsTurboActive) return "<color=#4CFF4C>TURBO</color>";
            if (kartController.IsShielded) return "<color=#4CC9FF>ESCUDO</color>";
            if (kartController.IsStunned) return "<color=#FF5C5C>TRAMPA</color>";
            return "<color=#888888>N/A</color>";
        }
    }
}
