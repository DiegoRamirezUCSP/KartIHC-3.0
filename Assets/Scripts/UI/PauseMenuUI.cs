using KartIHC.Gameplay;
using UnityEngine;

/// <summary>
/// Dibuja el menu de pausa: aparece solo mientras GameStateManager.CurrentState
/// es Paused. Muestra como jugar y los 3 comandos de voz que solo funcionan
/// en pausa (reanudar / controles / reiniciar).
/// </summary>
public class PauseMenuUI : MonoBehaviour
{
    [SerializeField] private GameStateManager gameStateManager;

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
    }

    private void BuildStyles()
    {
        titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 48,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = Color.white }
        };

        textStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 26,
            alignment = TextAnchor.MiddleCenter,
            richText = true,
            wordWrap = true,
            normal = { textColor = Color.white }
        };

        boxStyle = new GUIStyle(GUI.skin.box);

        stylesReady = true;
    }

    private void OnGUI()
    {
        if (gameStateManager == null || gameStateManager.CurrentState != GameState.Paused)
        {
            return;
        }

        if (!stylesReady)
        {
            BuildStyles();
        }

        // Fondo oscuro semi-transparente que cubre toda la pantalla,
        // para dejar claro que el juego esta congelado.
        GUI.Box(new Rect(0, 0, Screen.width, Screen.height), GUIContent.none);

        float panelWidth = Mathf.Min(720f, Screen.width * 0.85f);
        float panelHeight = Mathf.Min(560f, Screen.height * 0.85f);
        Rect panel = new Rect((Screen.width - panelWidth) / 2f, (Screen.height - panelHeight) / 2f, panelWidth, panelHeight);

        GUILayout.BeginArea(panel, boxStyle);
        GUILayout.FlexibleSpace();

        GUILayout.Label("PAUSA", titleStyle);
        GUILayout.Space(20);

        if (!gameStateManager.ShowControlsOverlay)
        {
            GUILayout.Label(
                "Di <b>\"reanudar\"</b> para seguir jugando\n" +
                "Di <b>\"controles\"</b> para ver como se juega\n" +
                "Di <b>\"reiniciar\"</b> para volver al inicio",
                textStyle);
        }
        else
        {
            GUILayout.Label(
                "<b>COMO JUGAR</b>\n\n" +
                "Volante: inclina el mando PS4/DS4\n" +
                "Acelerar: levanta el pie izquierdo\n" +
                "Frenar / Reversa: levanta el pie derecho\n" +
                "Power-ups: di \"turbo\", \"escudo\" o \"trampa\"\n\n" +
                "Di <b>\"controles\"</b> de nuevo para ocultar esto",
                textStyle);
        }

        GUILayout.FlexibleSpace();
        GUILayout.EndArea();
    }
}
