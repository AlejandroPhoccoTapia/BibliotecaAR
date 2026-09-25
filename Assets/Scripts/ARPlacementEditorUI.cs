using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class ARPlacementEditorUI : MonoBehaviour
{
    private static readonly Color Ink = new Color(0.08f, 0.15f, 0.20f);
    private static readonly Color Teal = new Color(0.02f, 0.43f, 0.49f);
    private RectTransform panel;
    private Button toggleButton;
    private TMP_Text toggleLabel;
    private TMP_Text feedback;
    private readonly TMP_Text[] values = new TMP_Text[6];
    private bool open;
    private Action<int, float> adjust;
    private Action save;
    private Action reset;
    private Action<bool> openChanged;
    private TMP_FontAsset font;

    public void Initialize(TMP_FontAsset uiFont, Action<int, float> onAdjust, Action onSave,
        Action onReset, Action<bool> onOpenChanged)
    {
        font = uiFont != null ? uiFont : TMP_Settings.defaultFontAsset;
        adjust = onAdjust;
        save = onSave;
        reset = onReset;
        openChanged = onOpenChanged;

        GameObject safeObject = GameObject.Find("SafeArea");
        Transform parent = safeObject != null ? safeObject.transform : FindFirstObjectByType<Canvas>().transform;
        toggleButton = MakeButton("TeacherPlacementToggle", parent, "Ajustar modelo",
            new Vector2(0.77f, 0.745f), new Vector2(0.97f, 0.81f), Teal, Toggle);
        toggleLabel = toggleButton.GetComponentInChildren<TMP_Text>();

        panel = MakePanel("TeacherPlacementPanel", parent, new Vector2(0.035f, 0.155f),
            new Vector2(0.965f, 0.74f));
        MakeText("Heading", panel, "AJUSTE SOBRE LA PÁGINA", 31f,
            new Vector2(0.05f, 0.90f), new Vector2(0.95f, 0.99f), TextAlignmentOptions.Left);

        string[] labels = { "Marcador", "Tamaño", "Derecha / izquierda", "Altura", "Sobre la página", "Giro" };
        string[] units = { "cm", "cm", "cm", "cm", "cm", "°" };
        float[] steps = { 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 5f };
        for (int i = 0; i < labels.Length; i++)
        {
            int index = i;
            float top = 0.88f - i * 0.116f;
            float bottom = top - 0.108f;
            MakeText("Field" + i, panel, labels[i], 27f,
                new Vector2(0.055f, bottom), new Vector2(0.42f, top), TextAlignmentOptions.Left);
            MakeButton("Minus" + i, panel, "−", new Vector2(0.44f, bottom + 0.008f),
                new Vector2(0.55f, top - 0.008f), Teal, () => adjust?.Invoke(index, -steps[index]));
            values[i] = MakeText("Value" + i, panel, "0 " + units[i], 29f,
                new Vector2(0.56f, bottom), new Vector2(0.78f, top), TextAlignmentOptions.Center);
            MakeButton("Plus" + i, panel, "+", new Vector2(0.80f, bottom + 0.008f),
                new Vector2(0.92f, top - 0.008f), Teal, () => adjust?.Invoke(index, steps[index]));
        }

        MakeButton("SavePlacement", panel, "Guardar", new Vector2(0.05f, 0.105f),
            new Vector2(0.49f, 0.185f), Teal, () => save?.Invoke());
        MakeButton("ResetPlacement", panel, "Deshacer", new Vector2(0.51f, 0.105f),
            new Vector2(0.95f, 0.185f), new Color(0.37f, 0.48f, 0.52f), () => reset?.Invoke());
        feedback = MakeText("PlacementFeedback", panel,
            "El ancho del marcador se aplica al volver a escanear.", 23f,
            new Vector2(0.05f, 0.012f), new Vector2(0.95f, 0.099f), TextAlignmentOptions.Center);
        panel.gameObject.SetActive(false);
    }

    public void SetPlacement(ChapterArPlacement placement)
    {
        if (placement == null)
            return;
        float[] numbers = {
            placement.ar_marker_width_cm, placement.ar_model_size_cm, placement.ar_offset_x_cm,
            placement.ar_offset_y_cm, placement.ar_offset_z_cm, placement.ar_yaw_degrees,
        };
        for (int i = 0; i < values.Length; i++)
            values[i].text = numbers[i].ToString("0.#") + (i == 5 ? "°" : " cm");
    }

    public void SetMessage(string message)
    {
        if (feedback != null)
            feedback.text = message;
    }

    private void Toggle()
    {
        open = !open;
        panel.gameObject.SetActive(open);
        toggleLabel.text = open ? "Ver modelo" : "Ajustar modelo";
        openChanged?.Invoke(open);
    }

    private RectTransform MakePanel(string name, Transform parent, Vector2 min, Vector2 max)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        RectTransform rect = go.GetComponent<RectTransform>();
        Stretch(rect, min, max);
        Image image = go.GetComponent<Image>();
        image.color = new Color(0.985f, 0.991f, 0.991f, 0.98f);
        image.sprite = QRCodeScanner.GetMessageCardSprite();
        image.type = Image.Type.Sliced;
        return rect;
    }

    private Button MakeButton(string name, Transform parent, string caption, Vector2 min, Vector2 max,
        Color color, Action click)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        Stretch(go.GetComponent<RectTransform>(), min, max);
        Image image = go.GetComponent<Image>();
        image.color = color;
        image.sprite = QRCodeScanner.GetMessageCardSprite();
        image.type = Image.Type.Sliced;
        Button button = go.GetComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() => click?.Invoke());
        TMP_Text label = MakeText(name + "Label", go.transform, caption, 27f,
            Vector2.zero, Vector2.one, TextAlignmentOptions.Center);
        label.color = Color.white;
        label.fontStyle = FontStyles.Bold;
        return button;
    }

    private TMP_Text MakeText(string name, Transform parent, string value, float size,
        Vector2 min, Vector2 max, TextAlignmentOptions alignment)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        TMP_Text label = go.GetComponent<TMP_Text>();
        Stretch(label.rectTransform, min, max);
        label.font = font;
        label.text = value;
        label.fontSize = size;
        label.enableAutoSizing = true;
        label.fontSizeMin = 20f;
        label.fontSizeMax = size;
        label.alignment = alignment;
        label.color = Ink;
        label.raycastTarget = false;
        return label;
    }

    private static void Stretch(RectTransform rect, Vector2 min, Vector2 max)
    {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
