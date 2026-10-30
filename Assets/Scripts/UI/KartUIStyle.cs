using UnityEngine;

namespace KartIHC.UI
{
    /// <summary>
    /// Shared IMGUI look for our own HUDs (LapManager, TutorialHudLayout): dark
    /// translucent rounded panels, a clean system font, white text with an orange
    /// accent (same orange as the track cones/posts). Sizes scale with screen
    /// height (reference 1080p). Call only from OnGUI.
    /// </summary>
    public static class KartUIStyle
    {
        public static readonly Color Accent = new Color(1f, 0.6f, 0.12f);
        public static readonly Color Text = new Color(0.96f, 0.96f, 0.94f);
        public static readonly Color Muted = new Color(0.72f, 0.72f, 0.7f);
        public static readonly Color Good = new Color(0.45f, 1f, 0.5f);
        public const string AccentHex = "#FF9A1F";
        public const string MutedHex = "#B8B8B3";
        public const string GoodHex = "#73FF80";

        private static readonly string[] FontNames = { "Segoe UI", "Arial" };

        private static Font font;
        private static Texture2D panelTexture;
        private static Texture2D white;
        private static GUIStyle panel;
        private static float builtScale = -1f;

        public static GUIStyle Caption { get; private set; }
        public static GUIStyle Body { get; private set; }
        public static GUIStyle Value { get; private set; }
        public static GUIStyle Big { get; private set; }
        public static GUIStyle Title { get; private set; }
        public static GUIStyle Hint { get; private set; }

        public static float Scale => Mathf.Clamp(Screen.height / 1080f, 0.7f, 1.6f);

        /// <summary>Pixels at 1080p converted to the current resolution.</summary>
        public static float S(float pixels) => pixels * Scale;

        public static void Ensure()
        {
            if (panel != null && Mathf.Approximately(builtScale, Scale))
            {
                return;
            }

            builtScale = Scale;
            if (font == null)
            {
                font = Font.CreateDynamicFontFromOSFont(FontNames, 32);
            }
            if (panelTexture == null)
            {
                panelTexture = RoundedTexture(32, 10, new Color(0.05f, 0.06f, 0.08f, 0.78f));
                white = Texture2D.whiteTexture;
            }

            panel = new GUIStyle { normal = { background = panelTexture }, border = new RectOffset(10, 10, 10, 10) };
            Caption = Make(16, FontStyle.Bold, Muted, TextAnchor.MiddleLeft);
            Body = Make(20, FontStyle.Normal, Text, TextAnchor.MiddleLeft);
            Value = Make(24, FontStyle.Bold, Text, TextAnchor.MiddleRight);
            Big = Make(44, FontStyle.Bold, Accent, TextAnchor.MiddleRight);
            Title = Make(52, FontStyle.Bold, Accent, TextAnchor.MiddleCenter);
            Hint = Make(20, FontStyle.Normal, Muted, TextAnchor.MiddleCenter);
        }

        private static GUIStyle Make(int size, FontStyle style, Color color, TextAnchor anchor)
        {
            return new GUIStyle(GUI.skin.label)
            {
                font = font,
                fontSize = Mathf.RoundToInt(size * builtScale),
                fontStyle = style,
                alignment = anchor,
                richText = true,
                wordWrap = false,
                clipping = TextClipping.Overflow,
                normal = { textColor = color },
            };
        }

        public static void Panel(Rect rect, bool accentBar = true)
        {
            Ensure();
            GUI.Box(rect, GUIContent.none, panel);
            if (accentBar)
            {
                Fill(new Rect(rect.x, rect.y + S(10f), S(4f), rect.height - S(20f)), Accent);
            }
        }

        public static void Fill(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, white);
            GUI.color = previous;
        }

        /// <summary>Row of small squares, the first 'filled' ones in the accent color.</summary>
        public static void Pips(Rect rect, int filled, int total)
        {
            if (total <= 0)
            {
                return;
            }

            float gap = S(4f);
            float size = Mathf.Min(rect.height, (rect.width - gap * (total - 1)) / total);
            for (int i = 0; i < total; i++)
            {
                Fill(new Rect(rect.xMax - (total - i) * (size + gap) + gap, rect.y, size, size),
                    i < filled ? Accent : new Color(1f, 1f, 1f, 0.18f));
            }
        }

        private static Texture2D RoundedTexture(int size, int radius, Color color)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float cx = Mathf.Clamp(x + 0.5f, radius, size - radius);
                    float cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
                    float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                    float alpha = Mathf.Clamp01(radius - distance + 0.5f);
                    pixels[y * size + x] = new Color(color.r, color.g, color.b, color.a * alpha);
                }
            }
            texture.SetPixels(pixels);
            texture.Apply();
            texture.hideFlags = HideFlags.HideAndDontSave;
            return texture;
        }
    }
}
