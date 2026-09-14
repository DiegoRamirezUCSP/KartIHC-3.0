using KartIHC.Gameplay;
using KartIHC.UI;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace KartIHC.EditorTools
{
    /// <summary>
    /// Editor utility that builds the MainMenu/Tutorial/Waiting UI flow using the
    /// classic UnityEngine.UI system (Canvas + Text + Button — no TextMeshPro), and
    /// wires it to GameStateManager/UIController.
    /// </summary>
    public static class UIBuilder
    {
        private static readonly Color PanelBackground = new Color(0f, 0f, 0f, 0.75f);

        [MenuItem("KartIHC/Build UI")]
        public static void BuildUI()
        {
            GameObject existingCanvas = GameObject.Find("GameCanvas");
            if (existingCanvas != null)
            {
                Object.DestroyImmediate(existingCanvas);
            }

            EnsureEventSystem();

            EnsureGameStateManager();
            UIController uiController = EnsureUIController();

            GameObject canvasGO = BuildCanvas();

            GameObject mainMenuPanel = BuildMainMenuPanel(canvasGO.transform, uiController);
            GameObject tutorialPanel = BuildTutorialPanel(canvasGO.transform, uiController);
            GameObject waitingPanel = BuildWaitingPanel(canvasGO.transform);

            tutorialPanel.SetActive(false);
            waitingPanel.SetActive(false);

            AssignPanelReferences(uiController, mainMenuPanel, tutorialPanel, waitingPanel);

            Selection.activeGameObject = canvasGO;
        }

        private static GameObject BuildCanvas()
        {
            GameObject canvasGO = new GameObject("GameCanvas", typeof(RectTransform));
            Canvas canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            canvasGO.AddComponent<GraphicRaycaster>();

            return canvasGO;
        }

        private static void EnsureEventSystem()
        {
            if (Object.FindFirstObjectByType<EventSystem>() != null)
            {
                return;
            }

            GameObject eventSystemGO = new GameObject("EventSystem");
            eventSystemGO.AddComponent<EventSystem>();
            // This project runs with the new Input System package only (Active Input Handling
            // = Input System Package), so the UI needs InputSystemUIInputModule rather than
            // the legacy StandaloneInputModule.
            eventSystemGO.AddComponent<InputSystemUIInputModule>();
        }

        private static GameStateManager EnsureGameStateManager()
        {
            GameStateManager existing = Object.FindFirstObjectByType<GameStateManager>();
            if (existing != null)
            {
                return existing;
            }

            GameObject go = new GameObject("GameStateManager");
            return go.AddComponent<GameStateManager>();
        }

        private static UIController EnsureUIController()
        {
            UIController existing = Object.FindFirstObjectByType<UIController>();
            if (existing != null)
            {
                return existing;
            }

            GameObject go = new GameObject("UIController");
            return go.AddComponent<UIController>();
        }

        private static GameObject BuildMainMenuPanel(Transform parent, UIController uiController)
        {
            GameObject panel = CreatePanel("MainMenuPanel", parent);

            CreateText("Title", panel.transform, "KART IHC", 96, new Vector2(0f, 150f), new Vector2(1200f, 150f));
            CreateText("Subtitle", panel.transform, "Interacción Humano-Computador — Beta Test", 32, new Vector2(0f, 40f), new Vector2(1500f, 60f));

            Button howToPlayButton = CreateButton("HowToPlayButton", panel.transform, "¿Cómo jugar?", new Vector2(0f, -180f), new Vector2(420f, 80f));
            UnityEventTools.AddPersistentListener(howToPlayButton.onClick, uiController.ShowTutorial);

            return panel;
        }

        private static GameObject BuildTutorialPanel(Transform parent, UIController uiController)
        {
            GameObject panel = CreatePanel("TutorialPanel", parent);

            CreateText("Title", panel.transform, "Cómo jugar", 72, new Vector2(0f, 320f), new Vector2(1200f, 120f));
            CreateText("Block1", panel.transform, "🦶 PIES: pedalea para acelerar, detente para frenar", 30, new Vector2(0f, 120f), new Vector2(1500f, 80f));
            CreateText("Block2", panel.transform, "📱 CELULAR: gíralo como un volante para doblar", 30, new Vector2(0f, 20f), new Vector2(1500f, 80f));
            CreateText("Block3", panel.transform, "🎤 VOZ: di el comando para lanzar ítems, o dilo para empezar la carrera", 30, new Vector2(0f, -80f), new Vector2(1500f, 80f));

            Button readyButton = CreateButton("StartWaitingButton", panel.transform, "Listo, quiero empezar", new Vector2(0f, -280f), new Vector2(480f, 80f));
            UnityEventTools.AddPersistentListener(readyButton.onClick, uiController.ShowWaiting);

            return panel;
        }

        private static GameObject BuildWaitingPanel(Transform parent)
        {
            GameObject panel = CreatePanel("WaitingPanel", parent);

            CreateText("Message", panel.transform, "Esperando el comando de voz para arrancar...", 48, Vector2.zero, new Vector2(1500f, 200f));

            return panel;
        }

        private static void AssignPanelReferences(UIController uiController, GameObject mainMenuPanel, GameObject tutorialPanel, GameObject waitingPanel)
        {
            SerializedObject serializedController = new SerializedObject(uiController);
            serializedController.FindProperty("mainMenuPanel").objectReferenceValue = mainMenuPanel;
            serializedController.FindProperty("tutorialPanel").objectReferenceValue = tutorialPanel;
            serializedController.FindProperty("waitingPanel").objectReferenceValue = waitingPanel;
            serializedController.ApplyModifiedProperties();
        }

        private static GameObject CreatePanel(string name, Transform parent)
        {
            RectTransform rect = CreateUIObject(name, parent);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            Image image = rect.gameObject.AddComponent<Image>();
            image.color = PanelBackground;

            return rect.gameObject;
        }

        private static Text CreateText(string name, Transform parent, string content, int fontSize, Vector2 anchoredPosition, Vector2 sizeDelta)
        {
            RectTransform rect = CreateUIObject(name, parent);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = sizeDelta;

            Text text = rect.gameObject.AddComponent<Text>();
            text.text = content;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;

            return text;
        }

        private static Button CreateButton(string name, Transform parent, string label, Vector2 anchoredPosition, Vector2 sizeDelta)
        {
            RectTransform rect = CreateUIObject(name, parent);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = sizeDelta;

            Image image = rect.gameObject.AddComponent<Image>();
            image.color = new Color(1f, 1f, 1f, 0.9f);

            Button button = rect.gameObject.AddComponent<Button>();
            AddButtonLabel(button, label);

            return button;
        }

        private static void AddButtonLabel(Button button, string label)
        {
            RectTransform rect = CreateUIObject("Label", button.transform);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            Text text = rect.gameObject.AddComponent<Text>();
            text.text = label;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 28;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.black;
        }

        private static RectTransform CreateUIObject(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.GetComponent<RectTransform>();
        }
    }
}
