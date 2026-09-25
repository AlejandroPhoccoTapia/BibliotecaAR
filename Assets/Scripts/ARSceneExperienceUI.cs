using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class ARSceneExperienceUI : MonoBehaviour
{
    public enum MessageTone
    {
        Info,
        Success,
        Warning,
        Error
    }

    private static readonly Color PanelColor = new Color(0.025f, 0.055f, 0.09f, 0.9f);
    private static readonly Color AccentColor = new Color(0.12f, 0.72f, 0.88f, 1f);

    private TMP_Text titleText;
    private TMP_Text narrationText;
    private AudioSource audioSource;
    private Action playAudio;
    private Action pauseAudio;
    private Action retryContent;

    private RectTransform safeArea;
    private RectTransform narrationPanel;
    private RectTransform statusPanel;
    private TMP_Text statusText;
    private Button retryButton;
    private Button playButton;
    private Button pauseButton;
    private TMP_Text playButtonText;
    private bool audioAvailable;
    private bool audioLoading;
    private Rect lastSafeArea;
    private Vector2 lastScreenSize;
    private bool lastAudioPlaying;

    public void Initialize(
        TMP_Text title,
        TMP_Text narration,
        AudioSource source,
        Action onPlayAudio,
        Action onPauseAudio,
        Action onRetryContent)
    {
        titleText = title;
        narrationText = narration;
        audioSource = source;
        if (audioSource != null)
        {
            audioSource.playOnAwake = false;
            audioSource.Stop();
        }
        playAudio = onPlayAudio;
        pauseAudio = onPauseAudio;
        retryContent = onRetryContent;

        GameObject safeAreaObject = GameObject.Find("SafeArea");
        safeArea = safeAreaObject != null ? safeAreaObject.GetComponent<RectTransform>() : null;
        if (safeArea == null)
        {
            Canvas canvas = FindFirstObjectByType<Canvas>();
            safeArea = canvas != null ? canvas.transform as RectTransform : null;
        }

        CanvasScaler scaler = FindFirstObjectByType<CanvasScaler>();
        if (scaler != null)
        {
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
        }

        Transform panelTransform = safeArea != null ? safeArea.Find("BottomNarrationPanel") : null;
        narrationPanel = panelTransform as RectTransform;
        ConfigureSafeArea();
        ConfigureTitle();
        ConfigureRescanButton();
        ConfigureNarrationPanel();
        BuildStatusBanner();
        BuildNarrationScrollView();
        BuildAudioControls();
        BuildRetryButton();
        SetAudioAvailable(false);
    }

    private void Update()
    {
        if (safeArea != null && (lastSafeArea != Screen.safeArea || lastScreenSize != new Vector2(Screen.width, Screen.height)))
            ConfigureSafeArea();

        bool isPlaying = audioAvailable && audioSource != null && audioSource.isPlaying;
        if (isPlaying != lastAudioPlaying)
        {
            lastAudioPlaying = isPlaying;
            RefreshAudioControls();
        }
    }

    public void SetStatus(string message, MessageTone tone, bool canRetry)
    {
        if (statusText != null)
        {
            statusText.text = message;
            statusText.color = tone switch
            {
                MessageTone.Success => new Color(0.55f, 1f, 0.72f, 1f),
                MessageTone.Warning => new Color(1f, 0.82f, 0.42f, 1f),
                MessageTone.Error => new Color(1f, 0.55f, 0.5f, 1f),
                _ => Color.white
            };
        }

        if (statusPanel != null)
        {
            Image background = statusPanel.GetComponent<Image>();
            if (background != null)
            {
                background.color = tone == MessageTone.Error
                    ? new Color(0.18f, 0.045f, 0.05f, 0.94f)
                    : PanelColor;
            }
        }

        if (retryButton != null)
            retryButton.gameObject.SetActive(canRetry);
    }

    public void SetAudioLoading()
    {
        audioLoading = true;
        audioAvailable = false;
        RefreshAudioControls();
    }

    public void SetAudioAvailable(bool available)
    {
        audioLoading = false;
        audioAvailable = available;
        RefreshAudioControls();
    }

    public void UpdateAudioPlaybackState()
    {
        RefreshAudioControls();
    }

    public void ScrollNarrationToTop()
    {
        if (narrationText == null)
            return;

        Canvas.ForceUpdateCanvases();
        ScrollRect scrollRect = narrationText.GetComponentInParent<ScrollRect>();
        if (scrollRect != null)
            scrollRect.verticalNormalizedPosition = 1f;
    }

    public void HandleTrackingState(bool isTracking)
    {
        if (isTracking)
            SetStatus("QR detectado. Mantén el teléfono estable para ver el contenido.", MessageTone.Success, false);
        else
            SetStatus("Buscando el QR… Muévete despacio y mejora la iluminación.", MessageTone.Info, false);
    }

    private void ConfigureSafeArea()
    {
        if (safeArea == null || Screen.width <= 0 || Screen.height <= 0)
            return;

        Rect area = Screen.safeArea;
        safeArea.anchorMin = new Vector2(area.xMin / Screen.width, area.yMin / Screen.height);
        safeArea.anchorMax = new Vector2(area.xMax / Screen.width, area.yMax / Screen.height);
        safeArea.offsetMin = Vector2.zero;
        safeArea.offsetMax = Vector2.zero;
        lastSafeArea = area;
        lastScreenSize = new Vector2(Screen.width, Screen.height);
    }

    private void ConfigureTitle()
    {
        if (titleText == null)
            return;

        if (safeArea != null)
        {
            titleText.transform.SetParent(safeArea, false);
            SetStretch(titleText.rectTransform, new Vector2(0.04f, 0.92f), new Vector2(0.67f, 0.99f));
        }

        titleText.fontSize = 38f;
        titleText.enableAutoSizing = true;
        titleText.fontSizeMin = 24f;
        titleText.fontSizeMax = 42f;
        titleText.color = Color.white;
        titleText.enableWordWrapping = true;
        titleText.alignment = TextAlignmentOptions.Left;
        titleText.raycastTarget = false;
    }

    private void ConfigureRescanButton()
    {
        Transform topBar = safeArea != null ? safeArea.Find("TopBar") : null;
        Transform buttonTransform = topBar != null ? topBar.Find("RescanButton") : null;
        if (buttonTransform == null)
            return;

        if (safeArea != null)
            buttonTransform.SetParent(safeArea, false);

        RectTransform rect = buttonTransform as RectTransform;
        if (rect != null)
            SetStretch(rect, new Vector2(0.70f, 0.92f), new Vector2(0.96f, 0.99f));

        Image background = buttonTransform.GetComponent<Image>();
        if (background != null)
            background.color = new Color(0.06f, 0.15f, 0.21f, 0.96f);

        TMP_Text label = buttonTransform.GetComponentInChildren<TMP_Text>();
        if (label != null)
        {
            label.text = "Escanear otro QR";
            label.fontSize = 25f;
            label.enableAutoSizing = true;
            label.fontSizeMin = 19f;
            label.fontSizeMax = 26f;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
        }
    }

    private void ConfigureNarrationPanel()
    {
        if (narrationPanel == null)
            return;

        narrationPanel.anchorMin = new Vector2(0.035f, 0.015f);
        narrationPanel.anchorMax = new Vector2(0.965f, 0.31f);
        narrationPanel.pivot = new Vector2(0.5f, 0f);
        narrationPanel.anchoredPosition = Vector2.zero;
        narrationPanel.sizeDelta = Vector2.zero;

        Image background = narrationPanel.GetComponent<Image>();
        if (background != null)
        {
            background.color = PanelColor;
            background.raycastTarget = false;
        }
    }

    private void BuildStatusBanner()
    {
        if (safeArea == null)
            return;

        statusPanel = CreatePanel("ARStatusPanel", safeArea);
        SetStretch(statusPanel, new Vector2(0.04f, 0.79f), new Vector2(0.96f, 0.91f));
        statusPanel.GetComponent<Image>().color = PanelColor;
        statusText = CreateText("ARStatusText", statusPanel, "Preparando la realidad aumentada…", 31f);
        SetStretch(statusText.rectTransform, Vector2.zero, Vector2.one);
        statusText.margin = new Vector4(24f, 14f, 24f, 14f);
        statusText.enableAutoSizing = true;
        statusText.fontSizeMin = 22f;
        statusText.fontSizeMax = 34f;
        statusText.alignment = TextAlignmentOptions.Center;
    }

    private void BuildNarrationScrollView()
    {
        if (narrationPanel == null || narrationText == null)
            return;

        GameObject viewportObject = new GameObject("NarrationViewport", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Mask), typeof(ScrollRect));
        viewportObject.layer = narrationPanel.gameObject.layer;
        viewportObject.transform.SetParent(narrationPanel, false);

        RectTransform viewportRect = viewportObject.GetComponent<RectTransform>();
        SetStretch(viewportRect, new Vector2(0.055f, 0.31f), new Vector2(0.945f, 0.91f));

        Image viewportImage = viewportObject.GetComponent<Image>();
        viewportImage.color = new Color(1f, 1f, 1f, 0.025f);
        viewportImage.raycastTarget = true;
        Mask mask = viewportObject.GetComponent<Mask>();
        mask.showMaskGraphic = false;

        narrationText.transform.SetParent(viewportObject.transform, false);
        RectTransform textRect = narrationText.rectTransform;
        textRect.anchorMin = new Vector2(0f, 1f);
        textRect.anchorMax = new Vector2(1f, 1f);
        textRect.pivot = new Vector2(0.5f, 1f);
        textRect.anchoredPosition = Vector2.zero;
        textRect.sizeDelta = Vector2.zero;
        narrationText.fontSize = 31f;
        narrationText.enableAutoSizing = false;
        narrationText.color = new Color(0.94f, 0.96f, 0.98f, 1f);
        narrationText.enableWordWrapping = true;
        narrationText.overflowMode = TextOverflowModes.Overflow;
        narrationText.alignment = TextAlignmentOptions.TopLeft;
        narrationText.margin = new Vector4(8f, 8f, 8f, 18f);
        narrationText.raycastTarget = false;

        ContentSizeFitter contentFitter = narrationText.GetComponent<ContentSizeFitter>();
        if (contentFitter == null)
            contentFitter = narrationText.gameObject.AddComponent<ContentSizeFitter>();
        contentFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        ScrollRect scrollRect = viewportObject.GetComponent<ScrollRect>();
        scrollRect.viewport = viewportRect;
        scrollRect.content = textRect;
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.inertia = true;
        scrollRect.scrollSensitivity = 36f;

        TMP_Text scrollHintText = CreateText("NarrationScrollHint", narrationPanel, "Desliza para leer todo el texto", 20f);
        SetStretch(scrollHintText.rectTransform, new Vector2(0.12f, 0.265f), new Vector2(0.88f, 0.315f));
        scrollHintText.color = new Color(0.69f, 0.78f, 0.83f, 1f);
        scrollHintText.enableAutoSizing = true;
        scrollHintText.fontSizeMin = 15f;
        scrollHintText.fontSizeMax = 20f;
        scrollHintText.alignment = TextAlignmentOptions.Center;
    }

    private void BuildAudioControls()
    {
        if (narrationPanel == null)
            return;

        playButton = CreateButton("PlayNarrationButton", "Reproducir audio", narrationPanel,
            new Vector2(0.055f, 0.055f), new Vector2(0.485f, 0.25f), () => playAudio?.Invoke());
        pauseButton = CreateButton("PauseNarrationButton", "Pausar audio", narrationPanel,
            new Vector2(0.515f, 0.055f), new Vector2(0.945f, 0.25f), () => pauseAudio?.Invoke());

        playButtonText = playButton.GetComponentInChildren<TMP_Text>();
        RefreshAudioControls();
    }

    private void BuildRetryButton()
    {
        if (safeArea == null)
            return;

        retryButton = CreateButton("RetryContentButton", "Reintentar carga", safeArea,
            new Vector2(0.24f, 0.71f), new Vector2(0.76f, 0.77f), () => retryContent?.Invoke());
        retryButton.gameObject.SetActive(false);
    }

    private RectTransform CreatePanel(string objectName, Transform parent)
    {
        GameObject panelObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        panelObject.layer = parent.gameObject.layer;
        panelObject.transform.SetParent(parent, false);
        Image image = panelObject.GetComponent<Image>();
        image.color = PanelColor;
        image.raycastTarget = false;
        return panelObject.GetComponent<RectTransform>();
    }

    private Button CreateButton(string objectName, string label, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Action onClick)
    {
        GameObject buttonObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        buttonObject.layer = parent.gameObject.layer;
        buttonObject.transform.SetParent(parent, false);

        RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
        SetStretch(buttonRect, anchorMin, anchorMax);
        Image image = buttonObject.GetComponent<Image>();
        image.color = new Color(0.06f, 0.15f, 0.21f, 1f);

        Button button = buttonObject.GetComponent<Button>();
        button.targetGraphic = image;
        button.transition = Selectable.Transition.ColorTint;
        button.colors = new ColorBlock
        {
            normalColor = image.color,
            highlightedColor = new Color(0.09f, 0.25f, 0.32f, 1f),
            pressedColor = AccentColor,
            selectedColor = new Color(0.09f, 0.25f, 0.32f, 1f),
            disabledColor = new Color(0.13f, 0.16f, 0.18f, 0.8f),
            colorMultiplier = 1f,
            fadeDuration = 0.12f
        };
        button.onClick.AddListener(() => onClick?.Invoke());

        TMP_Text text = CreateText(objectName + "Label", buttonObject.transform, label, 27f);
        SetStretch(text.rectTransform, Vector2.zero, Vector2.one);
        text.margin = new Vector4(10f, 4f, 10f, 4f);
        text.enableAutoSizing = true;
        text.fontSizeMin = 19f;
        text.fontSizeMax = 29f;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        return button;
    }

    private TMP_Text CreateText(string objectName, Transform parent, string initialText, float fontSize)
    {
        GameObject textObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textObject.layer = parent.gameObject.layer;
        textObject.transform.SetParent(parent, false);

        TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
        TMP_FontAsset font = titleText != null ? titleText.font : null;
        if (font == null && narrationText != null)
            font = narrationText.font;
        text.font = font;
        text.text = initialText;
        text.fontSize = fontSize;
        text.color = Color.white;
        text.enableWordWrapping = true;
        text.raycastTarget = false;
        return text;
    }

    private void RefreshAudioControls()
    {
        bool hasClip = audioAvailable && audioSource != null && audioSource.clip != null;
        bool playing = hasClip && audioSource.isPlaying;
        lastAudioPlaying = playing;

        if (playButton != null)
        {
            playButton.interactable = hasClip && !playing;
            if (playButtonText != null)
            {
                bool paused = hasClip && !playing && audioSource.time > 0f && audioSource.time < audioSource.clip.length;
                playButtonText.text = audioLoading ? "Cargando audio…" : hasClip ? paused ? "Continuar audio" : "Reproducir audio" : "Audio no disponible";
            }
        }

        if (pauseButton != null)
            pauseButton.interactable = playing;
    }

    private static void SetStretch(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = Vector2.zero;
    }
}
