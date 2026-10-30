using KartIHC.Gameplay;
using UnityEngine;

/// <summary>
/// Guarda el item (power-up) que tiene el kart. Solo puede tener uno: recoger otro
/// reemplaza el anterior. Para usarlo hay que decir su nombre; si dices uno que no
/// tienes, muestra "ITEM NO DISPONIBLE".
/// Ponlo en el mismo GameObject que KartController.
/// </summary>
public class KartItemInventory : MonoBehaviour
{
    public static readonly string[] AllItems = { "escudo", "turbo", "trampa" };

    [SerializeField] private GameStateManager gameStateManager;
    [Tooltip("En el tutorial se pueden usar los 3 power-ups sin necesidad de recoger items.")]
    [SerializeField] private bool freeUseInTutorial = true;
    [SerializeField] private float messageSeconds = 1.5f;
    [SerializeField] private bool drawOnScreen = true;
    [Tooltip("Que tan rapido aparece y desaparece el cuadro del item.")]
    [SerializeField] private float fadeSpeed = 6f;

    private string message = "";
    private float messageUntil;
    private GUIStyle itemStyle;
    private GUIStyle messageStyle;
    private float boxAlpha;
    private string lastShownItem = "";

    /// <summary>Item que tiene ahora ("" si no tiene ninguno).</summary>
    public string HeldItem { get; private set; } = "";

    public bool HasItem => !string.IsNullOrEmpty(HeldItem);

    private void Awake()
    {
        if (gameStateManager == null)
        {
            gameStateManager = FindFirstObjectByType<GameStateManager>();
        }
    }

    private void Update()
    {
        // El cuadro solo se ve mientras tienes un item: aparece al recogerlo
        // y se desvanece al usarlo.
        if (HasItem)
        {
            lastShownItem = HeldItem;
        }

        float target = HasItem ? 1f : 0f;
        boxAlpha = Mathf.MoveTowards(boxAlpha, target, fadeSpeed * Time.unscaledDeltaTime);
    }

    /// <summary>Da un item. Si ya tenia uno, lo reemplaza.</summary>
    public void GiveItem(string itemId)
    {
        HeldItem = itemId;
    }

    /// <summary>
    /// Intenta usar el item pedido. Devuelve true si se puede activar (y lo consume).
    /// Si no lo tienes, muestra el mensaje y devuelve false.
    /// </summary>
    public bool TryUse(string itemId)
    {
        bool inTutorial = gameStateManager != null && gameStateManager.CurrentState == GameState.Tutorial;

        if (freeUseInTutorial && inTutorial)
        {
            return true;
        }

        if (HasItem && HeldItem == itemId)
        {
            HeldItem = "";
            return true;
        }

        message = "ITEM NO DISPONIBLE";
        messageUntil = Time.unscaledTime + messageSeconds;
        return false;
    }

    private void OnGUI()
    {
        if (!drawOnScreen)
        {
            return;
        }

        if (itemStyle == null)
        {
            itemStyle = new GUIStyle(GUI.skin.box) { fontSize = 20, alignment = TextAnchor.MiddleCenter };
            messageStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 44,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(1f, 0.35f, 0.35f) }
            };
        }

        if (boxAlpha > 0.01f)
        {
            Color previous = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, boxAlpha);
            GUI.Box(new Rect(Screen.width / 2f - 100f, Screen.height - 56f, 200f, 36f), $"ITEM: {lastShownItem.ToUpperInvariant()}", itemStyle);
            GUI.color = previous;
        }

        if (Time.unscaledTime < messageUntil)
        {
            GUI.Label(new Rect(0f, Screen.height * 0.2f, Screen.width, 70f), message, messageStyle);
        }
    }
}
