using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class ARPlacementEditorUI : MonoBehaviour
{
    private static readonly Color Ink = new Color(0.08f, 0.15f, 0.20f);
    private static readonly Color Teal = new Color(0.02f, 0.43f, 0.49f);
    private static readonly Color Slate = new Color(0.37f, 0.48f, 0.52f);
    private static readonly string[] FieldLabels = {
        "Marcador", "Tamaño del modelo", "Derecha / izquierda", "Altura", "Sobre la página", "Giro"
    };
    private static readonly float[] FieldSteps = { 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 5f };

    private RectTransform panel;
    private Button toggleButton;
    private TMP_Text toggleLabel;
    private TMP_Text fieldLabel;
    private TMP_Text valueLabel;
    private TMP_Text feedback;
    private readonly float[] values = new float[6];
    private int selectedField = 1;
    private bool open;
    private bool? lastLandscape;
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

        panel = MakePanel("TeacherPlacementPanel", parent, new Vector2(0.035f, 0.035f),
            new Vector2(0.965f, 0.205f));
        MakeButton("PreviousPlacementField", panel, "‹", new Vector2(0.04f, 0.75f),
            new Vector2(0.16f, 0.96f), Slate, () => SelectField(-1));
        fieldLabel = MakeText("PlacementField", panel, string.Empty, 31f,
            new Vector2(0.18f, 0.75f), new Vector2(0.82f, 0.96f), TextAlignmentOptions.Center);
        fieldLabel.fontStyle = FontStyles.Bold;
        MakeButton("NextPlacementField", panel, "›", new Vector2(0.84f, 0.75f),
            new Vector2(0.96f, 0.96f), Slate, () => SelectField(1));

        MakeButton("DecreasePlacement", panel, "−", new Vector2(0.04f, 0.44f),
            new Vector2(0.24f, 0.72f), Teal, () => adjust?.Invoke(selectedField, -FieldSteps[selectedField]));
        valueLabel = MakeText("PlacementValue", panel, string.Empty, 34f,
            new Vector2(0.26f, 0.44f), new Vector2(0.74f, 0.72f), TextAlignmentOptions.Center);
        valueLabel.fontStyle = FontStyles.Bold;
        MakeButton("IncreasePlacement", panel, "+", new Vector2(0.76f, 0.44f),
            new Vector2(0.96f, 0.72f), Teal, () => adjust?.Invoke(selectedField, FieldSteps[selectedField]));

        MakeButton("SavePlacement", panel, "Guardar", new Vector2(0.04f, 0.20f),
            new Vector2(0.49f, 0.41f), Teal, () => save?.Invoke());
        MakeButton("ResetPlacement", panel, "Deshacer", new Vector2(0.51f, 0.20f),
            new Vector2(0.96f, 0.41f), Slate, () => reset?.Invoke());
        feedback = MakeText("PlacementFeedback", panel, string.Empty, 23f,
            new Vector2(0.04f, 0.02f), new Vector2(0.96f, 0.18f), TextAlignmentOptions.Center);
        RefreshSelectedField();
        Layout();
        panel.gameObject.SetActive(false);
    }

    private void Update()
    {
        if (lastLandscape != (Screen.width > Screen.height))
            Layout();
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
            values[i] = numbers[i];
        RefreshSelectedField();
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

    private void SelectField(int direction)
    {
        selectedField = (selectedField + direction + FieldLabels.Length) % FieldLabels.Length;
        RefreshSelectedField();
    }

    private void RefreshSelectedField()
    {
        if (fieldLabel != null)
            fieldLabel.text = (selectedField + 1) + "/" + FieldLabels.Length + " · " + FieldLabels[selectedField];
        if (valueLabel != null)
            valueLabel.text = values[selectedField].ToString("0.#") + (selectedField == 5 ? "°" : " cm");
        if (feedback != null)
            feedback.text = selectedField == 0
                ? "El marcador se actualiza al guardar y volver a escanear."
                : "El modelo cambia en directo. Guarda cuando quede bien.";
    }

    private void Layout()
    {
        bool landscape = Screen.width > Screen.height;
        lastLandscape = landscape;
        Stretch(toggleButton.transform as RectTransform,
            landscape ? new Vector2(0.53f, 0.88f) : new Vector2(0.70f, 0.745f),
            landscape ? new Vector2(0.82f, 0.985f) : new Vector2(0.97f, 0.81f));
        Stretch(panel,
            landscape ? new Vector2(0.02f, 0.12f) : new Vector2(0.035f, 0.035f),
            landscape ? new Vector2(0.49f, 0.68f) : new Vector2(0.965f, 0.205f));
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
