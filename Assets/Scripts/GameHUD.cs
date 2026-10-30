using KartIHC.Kart;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// HUD compacto de diagnostico: muestra si cada dispositivo esta conectado,
/// si el kart esta acelerando/frenando, y si el microfono capta sonido.
/// Diseñado para ser NO intrusivo: una franja chica en una esquina, con
/// texto corto y letras grandes para leerlo de un vistazo sin taparte
/// la pista.
/// </summary>
public class GameHUD : MonoBehaviour
{
    private enum Corner { TopLeft, TopRight, BottomLeft, BottomRight }
    private const float EscapePanelWidth = 440f;

    [Header("Referencias")]
    [SerializeField] private JoyShockGyroSteeringInput steeringInput;
    [SerializeField] private LegMotionInputReceiver legInput;
    [SerializeField] private VoicePowerUpInput voiceInput;
    [SerializeField] private MicrophoneLevelMeter micMeter;
    [SerializeField] private KartController kartController;

    [Header("Layout")]
    [SerializeField] private Corner corner = Corner.TopLeft;
    [SerializeField] private int fontSize = 30;
    [SerializeField] private float panelWidth = 260f;
    [SerializeField] private float margin = 16f;
    [SerializeField] private bool showLegCameraPanel = true;
    [SerializeField] private float cameraPanelWidth = 260f;
    [SerializeField] private float cameraPanelHeight = 190f;

    private GUIStyle rowStyle;
    private GUIStyle boxStyle;
    private GUIStyle escapeTextStyle;
    private bool stylesReady;

    private void Awake()
    {
        if (steeringInput == null)
        {
            steeringInput = FindFirstObjectByType<JoyShockGyroSteeringInput>();
        }

        if (legInput == null)
        {
            legInput = FindFirstObjectByType<LegMotionInputReceiver>();
        }

        if (voiceInput == null)
        {
            voiceInput = FindFirstObjectByType<VoicePowerUpInput>();
        }

        if (micMeter == null)
        {
            micMeter = FindFirstObjectByType<MicrophoneLevelMeter>();
        }

        if (kartController == null)
        {
            kartController = FindFirstObjectByType<KartController>();
        }
    }

    private void BuildStyles()
    {
        rowStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = fontSize,
            richText = true,
            wordWrap = false
        };

        boxStyle = new GUIStyle(GUI.skin.box);

        stylesReady = true;
    }

    private void OnGUI()
    {
        if (!stylesReady)
        {
            BuildStyles();
        }

        float rowHeight = fontSize * 1.4f;
        float height = rowHeight * 4f + 24f;

        Rect area = GetCornerRect(panelWidth, height);

        GUILayout.BeginArea(area, boxStyle);
        GUILayout.Space(6);

        DrawRow("MANDO", steeringInput != null && steeringInput.IsConnected, null);
        DrawThrottleBrakeRow();
        DrawMicRow();
        DrawPowerUpRow();

        GUILayout.EndArea();

        if (showLegCameraPanel && legInput != null && SceneManager.GetActiveScene().name != "EscapeCourse")
        {
            DrawLegCameraPanel();
        }

        DrawEscapeEventHistory();
    }

    private void DrawEscapeEventHistory()
    {
        EscapeModeSessionController session = EscapeModeSessionController.Instance;
        EscapeModeNetworkBridge bridge = EscapeModeNetworkBridge.Instance;
        bool isEscapeScene = SceneManager.GetActiveScene().name == "EscapeCourse";
        if (!isEscapeScene && (session == null || !session.ShouldShowStatus)) return;

        if (escapeTextStyle == null)
        {
            escapeTextStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.Max(15, fontSize - 9),
                richText = true,
                wordWrap = true
            };
            escapeTextStyle.normal.textColor = Color.white;
        }

        float width = Mathf.Min(EscapePanelWidth, Screen.width - margin * 2f);
        float height = 55f + EscapeModeHudFeed.Messages.Count * 24f;
        Rect area = new Rect(Screen.width - width - margin, margin, width, height);
        GUI.Box(area, "MODO ESCAPE · ESTADO Y MENSAJES");
        GUILayout.BeginArea(new Rect(area.x + 10f, area.y + 24f, area.width - 20f, area.height - 28f));
        string status = bridge != null ? bridge.CurrentStatus : session != null ? session.Status : "Preparando Escape...";
        GUILayout.Label($"<b>{status}</b>", escapeTextStyle);
        foreach (string message in EscapeModeHudFeed.Messages)
            GUILayout.Label("• " + message, escapeTextStyle);
        GUILayout.EndArea();
    }

    private void DrawLegCameraPanel()
    {
        float width = cameraPanelWidth;
        float height = cameraPanelHeight;
        Rect area = new Rect(Screen.width - width - margin, Screen.height - height - margin, width, height);
        GUI.Box(area, "CÁMARA DE PIERNAS");
        Rect preview = new Rect(area.x + 8, area.y + 24, area.width - 16, area.height - 80);
        if (legInput.PreviewTexture != null && legInput.PreviewTexture.width > 2)
        {
            GUI.DrawTexture(preview, legInput.PreviewTexture, ScaleMode.ScaleToFit);
        }
        GUI.Label(new Rect(area.x + 8, area.yMax - 46, area.width - 16, 40), legInput.HudCameraStatus, GUI.skin.label);
    }

    private Rect GetCornerRect(float width, float height)
    {
        switch (corner)
        {
            case Corner.TopRight:
                return new Rect(Screen.width - width - margin, margin, width, height);
            case Corner.BottomLeft:
                return new Rect(margin, Screen.height - height - margin, width, height);
            case Corner.BottomRight:
                return new Rect(Screen.width - width - margin, Screen.height - height - margin, width, height);
            default:
                return new Rect(margin, margin, width, height);
        }
    }

    private void DrawRow(string label, bool ok, string extra)
    {
        string statusText = ok ? "<color=#4CFF4C>OK</color>" : "<color=#888888>--</color>";
        string line = string.IsNullOrEmpty(extra)
            ? $"{label}  {statusText}"
            : $"{label}  {statusText}  {extra}";

        GUILayout.Label(line, rowStyle);
    }

    private void DrawThrottleBrakeRow()
    {
        bool connected = legInput != null && legInput.IsConnected;
        string state;

        if (!connected)
        {
            state = "";
        }
        else if (legInput.Brake > 0.1f)
        {
            state = "<color=#FF5C5C>FRENA</color>";
        }
        else if (legInput.Throttle > 0.1f)
        {
            state = "<color=#4CFF4C>ACELERA</color>";
        }
        else
        {
            state = "<color=#888888>quieto</color>";
        }

        DrawRow("PIERNAS", connected, state);
    }

    private void DrawMicRow()
    {
        bool listening = voiceInput != null && voiceInput.IsListening;
        string noiseIndicator;

        if (!listening)
        {
            noiseIndicator = "";
        }
        else if (micMeter != null && micMeter.IsAvailable)
        {
            noiseIndicator = micMeter.HasSignal
                ? "<color=#4CFF4C>\u2022 sonido</color>"
                : "<color=#555555>\u2022</color>";
        }
        else
        {
            noiseIndicator = "";
        }

        DrawRow("VOZ", listening, noiseIndicator);
    }

    private void DrawPowerUpRow()
    {
        string activeText;
        bool anyActive;

        if (kartController != null && kartController.IsTurboActive)
        {
            activeText = "<color=#4CFF4C>TURBO</color>";
            anyActive = true;
        }
        else if (kartController != null && kartController.IsShielded)
        {
            activeText = "<color=#4CC9FF>ESCUDO</color>";
            anyActive = true;
        }
        else if (kartController != null && kartController.IsStunned)
        {
            activeText = "<color=#FF5C5C>TRAMPA</color>";
            anyActive = true;
        }
        else
        {
            activeText = "<color=#888888>N/A</color>";
            anyActive = false;
        }

        DrawRow("POWER", anyActive, activeText);
    }
}
