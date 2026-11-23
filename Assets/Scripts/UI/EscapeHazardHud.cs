using System.Collections.Generic;
using KartIHC.Gameplay;
using KartIHC.Gameplay.Hazards;
using UnityEngine;

namespace KartIHC.UI
{
    /// <summary>
    /// Interfaz de los obstáculos hackeables (EscapeHazardDirector la agrega a su objeto):
    /// - Hacker: aviso "Mantén △ / G" con un obstáculo a alcance, overlay del
    ///   minijuego (mismo formato que el de puertas de GameHUD), espera de
    ///   confirmación del servidor Python y el overlay "DRON BLOQUEADO" cuando el
    ///   bloqueo es el alternativo propio (si es el del bridge, lo dibuja GameHUD).
    /// - Ambos roles: avisos breves arriba al centro (p. ej. "BARRERA POLICIAL: ABIERTA").
    /// </summary>
    public sealed class EscapeHazardHud : MonoBehaviour
    {
        private const float ToastSeconds = 3f;
        private const int MaxToasts = 3;

        private readonly List<(string text, float until)> toasts = new List<(string, float)>();
        private GUIStyle titleStyle;
        private GUIStyle infoStyle;
        private GUIStyle toastStyle;
        private GameStateManager gameStateManager;

        public void Toast(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            toasts.Add((message, Time.unscaledTime + ToastSeconds));
            if (toasts.Count > MaxToasts) toasts.RemoveAt(0);
        }

        private enum OverlayKind { None, Prompt, Waiting, Minigame }

        private static OverlayKind CurrentOverlay(EscapeModeNetworkBridge bridge, EscapeHazardDirector director)
        {
            if (bridge == null || director == null || !bridge.IsHacker) return OverlayKind.None;
            if (director.MinigameActive) return OverlayKind.Minigame;
            if (director.AwaitingServer) return OverlayKind.Waiting;
            if (director.Target != null && !bridge.HackMinigameActive && !bridge.DroneLocked) return OverlayKind.Prompt;
            return OverlayKind.None;
        }

        private static Rect OverlayRect(OverlayKind kind)
        {
            // Misma geometría que GameHUD.DrawHackMinigameOverlay, para que se vean igual.
            float width = Mathf.Min(kind == OverlayKind.Prompt ? 560f : 700f, Screen.width - 32f);
            float height = kind == OverlayKind.Minigame ? 158f : kind == OverlayKind.Prompt ? 46f : 108f;
            return new Rect((Screen.width - width) * 0.5f, Mathf.Max(8f, Screen.height - height - 20f), width, height);
        }

        /// <summary>Rectángulo inferior ocupado por esta interfaz (lo usa EscapeMinimap para no taparlo).</summary>
        public static bool TryGetBottomOverlay(out Rect rect)
        {
            OverlayKind kind = CurrentOverlay(EscapeModeNetworkBridge.Instance, EscapeHazardDirector.Instance);
            rect = kind == OverlayKind.None ? default : OverlayRect(kind);
            return kind != OverlayKind.None;
        }

        private void OnGUI()
        {
            EscapeModeNetworkBridge bridge = EscapeModeNetworkBridge.Instance;
            EscapeHazardDirector director = EscapeHazardDirector.Instance;
            if (bridge == null || director == null) return;
            if (gameStateManager == null) gameStateManager = FindFirstObjectByType<GameStateManager>();
            if (gameStateManager != null &&
                (gameStateManager.CurrentState == GameState.MainMenu || gameStateManager.CurrentState == GameState.Paused))
                return;

            GUI.depth = 5;
            EnsureStyles();
            DrawToasts();

            OverlayKind kind = CurrentOverlay(bridge, director);
            if (kind == OverlayKind.None) return;
            Rect panel = OverlayRect(kind);
            GUI.Box(panel, GUIContent.none);
            switch (kind)
            {
                case OverlayKind.Prompt:
                    GUI.Label(panel, $"Mantén <b>△</b> o <b>G</b> · hackear {director.Target.DisplayName.ToLowerInvariant()} · {director.TargetDistance:0.0} m",
                        infoStyle);
                    break;
                case OverlayKind.Waiting:
                    string name = director.AwaitingObstacle != null ? director.AwaitingObstacle.DisplayName.ToUpperInvariant() : "OBSTÁCULO";
                    GUI.Label(new Rect(panel.x + 12f, panel.y + 17f, panel.width - 24f, 38f), $"HACKEO · {name}", titleStyle);
                    GUI.Label(new Rect(panel.x + 12f, panel.y + 57f, panel.width - 24f, 30f), "Esperando confirmación del servidor...", infoStyle);
                    break;
                case OverlayKind.Minigame:
                    DrawMinigame(panel, director);
                    break;
            }
        }

        private void DrawMinigame(Rect panel, EscapeHazardDirector director)
        {
            GUI.Label(new Rect(panel.x + 12f, panel.y + 6f, panel.width - 24f, 30f),
                $"HACKEO · {director.MinigameObstacle.DisplayName.ToUpperInvariant()}", titleStyle);

            float gap = Mathf.Clamp(panel.width * 0.035f, 8f, 24f);
            float arrowSize = Mathf.Min(44f, (panel.width - 48f - gap * 5f) / 6f);
            float rowWidth = arrowSize * 6f + gap * 5f;
            float firstCenterX = (Screen.width - rowWidth) * 0.5f + arrowSize * 0.5f;
            float centerY = panel.y + 83f;
            for (int i = 0; i < EscapeHazardDirector.HackPatternLength; i++)
            {
                Color color = i < director.MinigameProgress
                    ? new Color(0.45f, 1f, 0.5f)
                    : i == director.MinigameProgress ? new Color(1f, 0.6f, 0.12f) : Color.white;
                DrawDirectionArrow(new Vector2(firstCenterX + i * (arrowSize + gap), centerY), arrowSize,
                    director.MinigameDirectionAt(i), color);
            }
            GUI.Label(new Rect(panel.x + 12f, panel.y + 121f, panel.width - 24f, 28f),
                $"CRUCETA O FLECHAS  ·  {director.MinigameProgress}/{EscapeHazardDirector.HackPatternLength}  ·  {director.MinigameSecondsRemaining:0.0} s",
                infoStyle);
        }

        private void DrawToasts()
        {
            toasts.RemoveAll(toast => toast.until <= Time.unscaledTime);
            if (toasts.Count == 0) return;
            KartUIStyle.Ensure();
            // Durante un tutorial su panel ocupa arriba al centro: los avisos bajan.
            bool tutorial = gameStateManager != null && gameStateManager.CurrentState == GameState.Tutorial;
            float y = tutorial ? Screen.height * 0.32f : KartUIStyle.S(24f);
            foreach ((string text, float _) in toasts)
            {
                Vector2 size = toastStyle.CalcSize(new GUIContent(text));
                float width = size.x + KartUIStyle.S(40f);
                float height = size.y + KartUIStyle.S(14f);
                var rect = new Rect((Screen.width - width) * 0.5f, y, width, height);
                KartUIStyle.Panel(rect);
                GUI.Label(rect, text, toastStyle);
                y += height + KartUIStyle.S(6f);
            }
        }

        private void EnsureStyles()
        {
            if (titleStyle != null) return;
            // Mismos tamaños que el overlay de puertas de GameHUD (fontSize 30 en EscapeCourse).
            titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 26,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                richText = true
            };
            infoStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                alignment = TextAnchor.MiddleCenter,
                richText = true
            };
            KartUIStyle.Ensure();
            toastStyle = new GUIStyle(KartUIStyle.Value) { alignment = TextAnchor.MiddleCenter };
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

            Color previous = GUI.color;
            GUI.color = color;
            DrawLine(shaftStart, shaftEnd, 7f);
            DrawLine(tip, headLeft, 7f);
            DrawLine(tip, headRight, 7f);
            GUI.color = previous;
        }

        private static void DrawLine(Vector2 start, Vector2 end, float thickness)
        {
            Vector2 delta = end - start;
            float length = delta.magnitude;
            Vector2 midpoint = (start + end) * 0.5f;
            Matrix4x4 previous = GUI.matrix;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, midpoint);
            GUI.DrawTexture(new Rect(midpoint.x - length * 0.5f, midpoint.y - thickness * 0.5f, length, thickness),
                Texture2D.whiteTexture);
            GUI.matrix = previous;
        }
    }
}
