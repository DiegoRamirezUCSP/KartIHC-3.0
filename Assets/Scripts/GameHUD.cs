using KartIHC.Kart;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;

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
    private GUIStyle hackOverlayTitleStyle;
    private GUIStyle hackOverlayInfoStyle;
    private bool stylesReady;
    private int communicationWheelSelection;
    private static readonly string[] CommunicationWheelLabels =
    {
        "ESTOY AQUÍ", "TENGO PROBLEMAS", "VEN AQUÍ", "AYÚDAME",
        "CUIDADO", "LISTO", "ESTOY HACKEANDO", "ESTOY AVANZANDO"
    };

    private void Update()
    {
        EscapeModeNetworkBridge bridge = EscapeModeNetworkBridge.Instance;
        if (bridge == null || SceneManager.GetActiveScene().name != "EscapeCourse" || !IsCommandWheelHeld())
            return;

        Gamepad pad = Gamepad.current;
        if (pad != null)
        {
            Vector2 stick = pad.leftStick.ReadValue();
            if (stick.sqrMagnitude > 0.36f)
            {
                float angle = Mathf.Atan2(stick.x, stick.y) * Mathf.Rad2Deg;
                if (angle < 0f) angle += 360f;
                communicationWheelSelection = Mathf.RoundToInt(angle / 45f) % CommunicationWheelLabels.Length;
            }

            if (pad.dpad.left.wasPressedThisFrame) communicationWheelSelection = 6;
            if (pad.dpad.right.wasPressedThisFrame) communicationWheelSelection = 2;
            if (pad.dpad.up.wasPressedThisFrame) communicationWheelSelection = 0;
            if (pad.dpad.down.wasPressedThisFrame) communicationWheelSelection = 4;
            if (pad.buttonSouth.wasPressedThisFrame) SendSelectedCommunication();
        }

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.leftArrowKey.wasPressedThisFrame) communicationWheelSelection = 6;
            if (keyboard.rightArrowKey.wasPressedThisFrame) communicationWheelSelection = 2;
            if (keyboard.upArrowKey.wasPressedThisFrame) communicationWheelSelection = 0;
            if (keyboard.downArrowKey.wasPressedThisFrame) communicationWheelSelection = 4;
            if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)
                SendSelectedCommunication();
        }
    }

    private static bool IsCommandWheelHeld()
    {
        return Keyboard.current != null && Keyboard.current.tabKey.isPressed;
    }

    private void SendSelectedCommunication()
    {
        if (voiceInput == null) voiceInput = FindFirstObjectByType<VoicePowerUpInput>();
        voiceInput?.RequestCommunicationFromWheel(communicationWheelSelection);
    }

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

        EscapeModeNetworkBridge activeBridge = EscapeModeNetworkBridge.Instance;
        bool hackerRole = activeBridge != null && activeBridge.IsHacker;
        float rowHeight = fontSize * 1.4f;
        float height = rowHeight * (hackerRole ? 3f : 4f) + 24f;

        Rect area = GetCornerRect(panelWidth, height);

        GUILayout.BeginArea(area, boxStyle);
        GUILayout.Space(6);

        if (hackerRole)
        {
            DrawRow("MANDO DRON", activeBridge.DroneGamepadConnected, null);
            DrawRow("GIROSCOPIO", activeBridge.DroneGyroscopeConnected, null);
            DrawMicRow();
        }
        else
        {
            DrawRow("MANDO", steeringInput != null && steeringInput.IsConnected, null);
            DrawThrottleBrakeRow();
            DrawMicRow();
            DrawPowerUpRow();
        }

        GUILayout.EndArea();

        if (showLegCameraPanel && legInput != null && !hackerRole)
        {
            DrawLegCameraPanel();
        }

        DrawEscapeEventHistory();
        DrawCommunicationChat(activeBridge);
        DrawHackMinigameOverlay(activeBridge);
        DrawCommunicationWheel(activeBridge);
    }

    private void DrawCommunicationWheel(EscapeModeNetworkBridge bridge)
    {
        if (bridge == null || SceneManager.GetActiveScene().name != "EscapeCourse" || !IsCommandWheelHeld())
            return;

        float radius = Mathf.Clamp(Mathf.Min(Screen.width, Screen.height) * 0.23f, 105f, 180f);
        float nodeWidth = Mathf.Clamp(Screen.width * 0.15f, 125f, 190f);
        float nodeHeight = 52f;
        Vector2 center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

        Color oldColor = GUI.color;
        GUI.color = new Color(0.02f, 0.04f, 0.07f, 0.9f);
        GUI.Box(new Rect(center.x - 148f, center.y - 35f, 296f, 70f), "RUEDA DE COMUNICACIÓN\nMueve la palanca y pulsa ✕ para enviar");
        GUI.color = oldColor;

        GUIStyle labelStyle = new GUIStyle(GUI.skin.box)
        {
            alignment = TextAnchor.MiddleCenter,
            wordWrap = true,
            fontSize = Mathf.Max(13, fontSize - 13),
            fontStyle = FontStyle.Bold
        };

        for (int i = 0; i < CommunicationWheelLabels.Length; i++)
        {
            float angle = i * Mathf.PI / 4f;
            Vector2 point = center + new Vector2(Mathf.Sin(angle), -Mathf.Cos(angle)) * radius;
            Rect node = new Rect(point.x - nodeWidth * 0.5f, point.y - nodeHeight * 0.5f, nodeWidth, nodeHeight);
            GUI.color = i == communicationWheelSelection
                ? new Color(0.1f, 0.55f, 0.85f, 0.98f)
                : new Color(0.05f, 0.08f, 0.12f, 0.92f);
            GUI.Box(node, CommunicationWheelLabels[i], labelStyle);
        }
        GUI.color = oldColor;

        GUI.Label(new Rect(center.x - 220f, center.y + radius + 35f, 440f, 30f),
            "Mantén TAB para mostrar · suelta TAB para cerrar · flechas/Enter como respaldo",
            GUI.skin.label);
    }

    private void DrawCommunicationChat(EscapeModeNetworkBridge bridge)
    {
        if (bridge == null || SceneManager.GetActiveScene().name != "EscapeCourse" ||
            bridge.CommunicationMessages.Count == 0) return;

        float width = Mathf.Min(410f, Screen.width - margin * 2f);
        float height = 34f + bridge.CommunicationMessages.Count * 25f;
        Rect area = new Rect(margin, Screen.height - height - margin, width, height);
        GUI.Box(area, "CHAT DE VOZ · ESCAPE");
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
        GUILayout.BeginArea(new Rect(area.x + 10f, area.y + 24f, area.width - 20f, area.height - 26f));
        foreach (string message in bridge.CommunicationMessages)
            GUILayout.Label(message, escapeTextStyle);
        GUILayout.EndArea();
    }

    private void DrawEscapeEventHistory()
    {
        EscapeModeSessionController session = EscapeModeSessionController.Instance;
        PythonEscapeSessionController pythonSession = PythonEscapeSessionController.Instance;
        EscapeModeNetworkBridge bridge = EscapeModeNetworkBridge.Instance;
        PythonEscapeModeBridge pythonBridge = PythonEscapeModeBridge.Instance;
        bool isEscapeScene = SceneManager.GetActiveScene().name == "EscapeCourse";
        bool sessionVisible = (session != null && session.ShouldShowStatus) ||
                              (pythonSession != null && pythonSession.ShouldShowStatus);
        if (!isEscapeScene && !sessionVisible) return;

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
        bool showDroneTargets = bridge != null && bridge.IsHacker;
        bool showNetworkTest = pythonBridge != null || (bridge != null && bridge.NetworkDiagnosticsEnabled);
        float height = 55f + EscapeModeHudFeed.Messages.Count * 24f +
                       (showDroneTargets ? 135f : 0f) +
                       (showNetworkTest ? 58f : 0f);
        Rect area = new Rect(Screen.width - width - margin, margin, width, height);
        GUI.Box(area, "MODO ESCAPE · ESTADO Y MENSAJES");
        GUILayout.BeginArea(new Rect(area.x + 10f, area.y + 24f, area.width - 20f, area.height - 28f));
        string status = pythonSession != null && pythonSession.ShouldShowStatus
            ? pythonSession.Status
            : bridge != null ? bridge.CurrentStatus : session != null ? session.Status : "Preparando Escape...";
        GUILayout.Label($"<b>{status}</b>", escapeTextStyle);
        foreach (string message in EscapeModeHudFeed.Messages)
            GUILayout.Label("• " + message, escapeTextStyle);
        if (showNetworkTest)
        {
            GUILayout.Space(5f);
            string networkStatus = pythonBridge != null ? pythonBridge.NetworkTestStatus : bridge.NetworkTestStatus;
            int sent = pythonBridge != null ? pythonBridge.PingsSent : bridge.LatencyProbesSent;
            int received = pythonBridge != null ? pythonBridge.PongsReceived : bridge.LatencyRepliesReceived;
            int lost = pythonBridge != null ? pythonBridge.PingsLost : bridge.LatencyProbesLost;
            GUILayout.Label("PRUEBA DE RED · " + networkStatus, escapeTextStyle);
            GUILayout.Label($"Sondas: {sent} enviadas · {received} respuestas · {lost} sin respuesta",
                escapeTextStyle);
        }
        if (showDroneTargets)
        {
            GUILayout.Space(6f);
            GUILayout.Label("DRON: inclina = vuelo · palanca = altura/giro · gatillos = zoom · mantén □ para hackear puerta cercana (7 m)", escapeTextStyle);
            foreach (string target in bridge.HackerTargetLabels)
                GUILayout.Label(target, escapeTextStyle);
        }
        GUILayout.EndArea();
    }

    private void DrawHackMinigameOverlay(EscapeModeNetworkBridge bridge)
    {
        if (bridge == null || !bridge.IsHacker || (!bridge.HackMinigameActive && !bridge.DroneLocked)) return;

        bool isHacking = bridge.HackMinigameActive;
        float panelWidth = Mathf.Min(700f, Screen.width - 32f);
        float panelHeight = isHacking ? 158f : 108f;
        Rect panel = new Rect((Screen.width - panelWidth) * 0.5f,
            Mathf.Max(8f, Screen.height - panelHeight - 20f), panelWidth, panelHeight);
        GUI.Box(panel, GUIContent.none, boxStyle);

        if (hackOverlayTitleStyle == null)
        {
            hackOverlayTitleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.Max(20, fontSize - 4),
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                richText = true
            };
            hackOverlayInfoStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.Max(16, fontSize - 10),
                alignment = TextAnchor.MiddleCenter,
                richText = true
            };
        }

        if (isHacking)
        {
            GUI.Label(new Rect(panel.x + 12f, panel.y + 6f, panel.width - 24f, 30f),
                $"HACKEO · PUERTA {bridge.HackMinigameDoorId + 1}", hackOverlayTitleStyle);

            float gap = Mathf.Clamp(panel.width * 0.035f, 8f, 24f);
            float arrowSize = Mathf.Min(44f, (panel.width - 48f - gap * 5f) / 6f);
            float rowWidth = arrowSize * 6f + gap * 5f;
            float firstCenterX = (Screen.width - rowWidth) * 0.5f + arrowSize * 0.5f;
            float centerY = panel.y + 83f;
            for (int i = 0; i < 6; i++)
            {
                Color arrowColor = i < bridge.HackMinigameProgress
                    ? new Color(0.45f, 1f, 0.5f)
                    : i == bridge.HackMinigameProgress
                        ? new Color(1f, 0.6f, 0.12f)
                        : Color.white;
                DrawDirectionArrow(new Vector2(firstCenterX + i * (arrowSize + gap), centerY),
                    arrowSize, bridge.HackMinigameDirectionAt(i), arrowColor);
            }

            GUI.Label(new Rect(panel.x + 12f, panel.y + 121f, panel.width - 24f, 28f),
                $"CRUCETA  ·  {bridge.HackMinigameProgress}/6  ·  {bridge.HackMinigameSecondsRemaining:0.0} s", hackOverlayInfoStyle);
        }
        else
        {
            GUI.Label(new Rect(panel.x + 12f, panel.y + 17f, panel.width - 24f, 38f),
                "DRON BLOQUEADO", hackOverlayTitleStyle);
            GUI.Label(new Rect(panel.x + 12f, panel.y + 57f, panel.width - 24f, 30f),
                $"Vuelo y hackeo disponibles en {bridge.DroneLockSecondsRemaining:0.0} s", hackOverlayInfoStyle);
        }
    }

    private static void DrawDirectionArrow(Vector2 center, float size, int direction, Color color)
    {
        if (direction < 0 || direction > 3) return;
        Vector2 forward = direction switch
        {
            0 => new Vector2(0f, -1f),
            1 => new Vector2(0f, 1f),
            2 => new Vector2(-1f, 0f),
            _ => new Vector2(1f, 0f)
        };
        Vector2 perpendicular = new Vector2(-forward.y, forward.x);
        Vector2 tip = center + forward * (size * 0.42f);
        Vector2 shaftStart = center - forward * (size * 0.30f);
        Vector2 shaftEnd = tip - forward * (size * 0.12f);
        Vector2 headLeft = tip - forward * (size * 0.24f) + perpendicular * (size * 0.20f);
        Vector2 headRight = tip - forward * (size * 0.24f) - perpendicular * (size * 0.20f);

        Color previousColor = GUI.color;
        GUI.color = color;
        DrawHudLine(shaftStart, shaftEnd, 7f);
        DrawHudLine(tip, headLeft, 7f);
        DrawHudLine(tip, headRight, 7f);
        GUI.color = previousColor;
    }

    private static void DrawHudLine(Vector2 start, Vector2 end, float thickness)
    {
        Vector2 delta = end - start;
        float length = delta.magnitude;
        Vector2 midpoint = (start + end) * 0.5f;
        Matrix4x4 previousMatrix = GUI.matrix;
        GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, midpoint);
        GUI.DrawTexture(new Rect(midpoint.x - length * 0.5f, midpoint.y - thickness * 0.5f,
            length, thickness), Texture2D.whiteTexture);
        GUI.matrix = previousMatrix;
    }

    private void DrawLegCameraPanel()
    {
        float width = cameraPanelWidth;
        float height = cameraPanelHeight;
        Rect area = new Rect(Screen.width - width - margin, Screen.height - height - margin, width, height);
        GUI.Box(area, "CÁMARA DE PIERNAS");
        Rect preview = new Rect(area.x + 8, area.y + 24, area.width - 16, area.height - 80);
        if (legInput.PreviewFrameReceived && legInput.PreviewTexture != null && legInput.PreviewTexture.width > 2)
        {
            GUI.DrawTexture(preview, legInput.PreviewTexture, ScaleMode.ScaleToFit);
        }
        else
        {
            string placeholder = legInput.PreviewStatus.StartsWith("No se pudo") || legInput.PreviewStatus.Contains("no es una imagen")
                ? legInput.PreviewStatus
                : legInput.CameraRunning
                    ? "Esperando imagen UDP de la cámara..."
                    : "Sin imagen. Di «activar cámara» o pulsa F6.";
            GUI.Label(preview, placeholder, GUI.skin.box);
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
            state = "<color=#FFD166>W/S + A/D respaldo</color>";
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
