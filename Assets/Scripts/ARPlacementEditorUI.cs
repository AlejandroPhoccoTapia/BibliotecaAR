using System;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class ARPlacementEditorUI : MonoBehaviour
{
    private static readonly Color Ink = new Color(0.08f, 0.15f, 0.20f);
    private static readonly Color Teal = new Color(0.02f, 0.43f, 0.49f);
    private static readonly Color Slate = new Color(0.37f, 0.48f, 0.52f);
    private readonly float[] values = new float[6];
    private RectTransform panel;
    private Button toggleButton;
    private TMP_Text toggleLabel;
    private TMP_Text valuesLabel;
    private TMP_Text feedback;
    private TMP_InputField markerInput;
    private Action<int, float> adjust;
    private Action save;
    private Action reset;
    private Action<bool> openChanged;
    private TMP_FontAsset font;
    private bool open;
    private bool? lastLandscape;

    public static bool IsEditing { get; private set; }

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
            new Vector2(0.70f, 0.745f), new Vector2(0.97f, 0.81f), Teal, Toggle);
        toggleLabel = toggleButton.GetComponentInChildren<TMP_Text>();
        panel = MakePanel("TeacherPlacementPanel", parent,
            new Vector2(0.035f, 0.035f), new Vector2(0.965f, 0.255f));
        TMP_Text instructions = MakeText("PlacementInstructions", panel,
            "Arrastra: mover · pellizca: tamaño\nGira dos dedos: rotar · desliza ambos: altura",
            27f, new Vector2(0.05f, 0.76f), new Vector2(0.95f, 0.97f), TextAlignmentOptions.Center);
        instructions.fontStyle = FontStyles.Bold;
        valuesLabel = MakeText("PlacementValues", panel, string.Empty, 24f,
            new Vector2(0.05f, 0.54f), new Vector2(0.95f, 0.75f), TextAlignmentOptions.Center);
        MakeText("MarkerWidthLabel", panel, "Ancho del QR impreso", 25f,
            new Vector2(0.05f, 0.37f), new Vector2(0.54f, 0.53f), TextAlignmentOptions.Left);
        markerInput = MakeMarkerInput(panel);
        MakeText("MarkerWidthUnit", panel, "cm", 25f,
            new Vector2(0.84f, 0.37f), new Vector2(0.95f, 0.53f), TextAlignmentOptions.Left);
        MakeButton("SavePlacement", panel, "Guardar", new Vector2(0.05f, 0.17f),
            new Vector2(0.49f, 0.36f), Teal, Save);
        MakeButton("ResetPlacement", panel, "Deshacer", new Vector2(0.51f, 0.17f),
            new Vector2(0.95f, 0.36f), Slate, Reset);
        feedback = MakeText("PlacementFeedback", panel,
            "Ajusta sobre el QR y guarda cuando quede bien.", 22f,
            new Vector2(0.05f, 0.01f), new Vector2(0.95f, 0.16f), TextAlignmentOptions.Center);
        Layout();
        panel.gameObject.SetActive(false);
    }

    private void OnDisable()
    {
        IsEditing = false;
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
        values[0] = placement.ar_marker_width_cm;
        values[1] = placement.ar_model_size_cm;
        values[2] = placement.ar_offset_x_cm;
        values[3] = placement.ar_offset_y_cm;
        values[4] = placement.ar_offset_z_cm;
        values[5] = placement.ar_yaw_degrees;
        if (valuesLabel != null)
            valuesLabel.text = string.Format(CultureInfo.CurrentCulture,
                "Tamaño {0:0.#} cm · Altura {1:0.#} cm · Giro {2:0.#}°\nLados {3:0.#} cm · Página {4:0.#} cm",
                values[1], values[3], values[5], values[2], values[4]);
        if (markerInput != null && !markerInput.isFocused)
            markerInput.SetTextWithoutNotify(values[0].ToString("0.#", CultureInfo.CurrentCulture));
    }

    public void SetMessage(string message)
    {
        if (feedback != null)
            feedback.text = message;
    }

    private void Toggle()
    {
        open = !open;
        IsEditing = open;
        panel.gameObject.SetActive(open);
        toggleLabel.text = open ? "Ver modelo" : "Ajustar modelo";
        openChanged?.Invoke(open);
    }

    private void Save()
    {
        string input = markerInput.text.Trim().Replace(',', '.');
        if (!float.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out float width)
            || float.IsNaN(width) || float.IsInfinity(width) || width < 2f || width > 30f)
        {
            SetMessage("Escribe el ancho real del QR entre 2 y 30 cm.");
            return;
        }
        if (!Mathf.Approximately(values[0], width))
            adjust?.Invoke(0, width - values[0]);
        markerInput.SetTextWithoutNotify(width.ToString("0.#", CultureInfo.CurrentCulture));
        save?.Invoke();
    }

    private void Reset()
    {
        markerInput.DeactivateInputField();
        reset?.Invoke();
    }

    private TMP_InputField MakeMarkerInput(Transform parent)
    {
        GameObject go = new GameObject("MarkerWidthInput", typeof(RectTransform),
            typeof(CanvasRenderer), typeof(Image), typeof(TMP_InputField));
        go.transform.SetParent(parent, false);
        Stretch(go.GetComponent<RectTransform>(), new Vector2(0.56f, 0.38f), new Vector2(0.82f, 0.53f));
        Image image = go.GetComponent<Image>();
        image.color = new Color(0.88f, 0.95f, 0.95f);
        image.sprite = QRCodeScanner.GetMessageCardSprite();
        image.type = Image.Type.Sliced;
        TMP_Text text = MakeText("MarkerWidthText", go.transform, "6", 28f,
            new Vector2(0.08f, 0.05f), new Vector2(0.92f, 0.95f), TextAlignmentOptions.Center);
        text.raycastTarget = true;
        TMP_InputField input = go.GetComponent<TMP_InputField>();
        input.textViewport = go.GetComponent<RectTransform>();
        input.textComponent = (TextMeshProUGUI)text;
        input.contentType = TMP_InputField.ContentType.DecimalNumber;
        input.keyboardType = TouchScreenKeyboardType.DecimalPad;
        input.characterLimit = 5;
        return input;
    }

    private void Layout()
    {
        bool landscape = Screen.width > Screen.height;
        lastLandscape = landscape;
        Stretch(toggleButton.transform as RectTransform,
            landscape ? new Vector2(0.53f, 0.88f) : new Vector2(0.70f, 0.745f),
            landscape ? new Vector2(0.82f, 0.985f) : new Vector2(0.97f, 0.81f));
        Stretch(panel,
            landscape ? new Vector2(0.02f, 0.05f) : new Vector2(0.035f, 0.035f),
            landscape ? new Vector2(0.49f, 0.56f) : new Vector2(0.965f, 0.255f));
    }

    private RectTransform MakePanel(string name, Transform parent, Vector2 min, Vector2 max)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        RectTransform rect = go.GetComponent<RectTransform>();
        Stretch(rect, min, max);
        Image image = go.GetComponent<Image>();
        image.color = new Color(0.985f, 0.991f, 0.991f, 0.96f);
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
        label.fontSizeMin = 18f;
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
