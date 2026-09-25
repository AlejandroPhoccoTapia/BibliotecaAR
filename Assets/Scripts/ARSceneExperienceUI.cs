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

    private static readonly Color PanelColor = new Color(0.985f, 0.991f, 0.991f, 0.97f);
    private static readonly Color InkColor = new Color(0.075f, 0.14f, 0.20f, 1f);
    private static readonly Color MutedColor = new Color(0.39f, 0.46f, 0.51f, 1f);
    private static readonly Color AccentColor = new Color(0.02f, 0.43f, 0.49f, 1f);
    private static Sprite roundedSprite;
    private static Sprite playSprite;
    private static Sprite pauseSprite;

    private TMP_Text titleText;
    private TMP_Text narrationText;
    private AudioSource audioSource;
    private Action playAudio;
    private Action pauseAudio;
    private Action retryContent;
    private Action markCompleted;

    private RectTransform safeArea;
    private RectTransform narrationPanel;
    private RectTransform panelShadow;
    private RectTransform statusPanel;
    private RectTransform narrationViewport;
    private TMP_Text statusText;
    private TMP_Text sectionLabel;
    private TMP_Text scrollHintText;
    private Button retryButton;
    private Button playButton;
    private Button pauseButton;
    private Button expandButton;
    private Button completeButton;
    private TMP_Text completeButtonText;
    private bool completionAvailable;
    private TMP_Text playButtonText;
    private TMP_Text pauseButtonText;
    private TMP_Text expandButtonText;
    private Image playIcon;
    private Image pauseIcon;
    private bool audioAvailable;
    private bool audioLoading;
    private bool isExpanded = true;
    private Rect lastSafeArea;
    private Vector2 lastScreenSize;
    private bool lastAudioPlaying;

    public void Initialize(
        TMP_Text title,
        TMP_Text narration,
        AudioSource source,
        Action onPlayAudio,
        Action onPauseAudio,
        Action onRetryContent,
        Action onMarkCompleted)
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
        markCompleted = onMarkCompleted;

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
        ConfigureNarrationPanel();
        ConfigureTitle();
        if (titleText != null)
            titleText.text = "Preparando lectura";
        if (narrationText != null)
            narrationText.text = "Un momento, estamos cargando el capítulo.";
        ConfigureRescanButton();
        BuildCardHeader();
        BuildStatusBanner();
        BuildNarrationScrollView();
        BuildAudioControls();
        BuildCompletionButton();
        BuildRetryButton();
        LayoutChrome();
        SetAudioAvailable(false);
    }

    private void Update()
    {
        if (safeArea != null && (lastSafeArea != Screen.safeArea || lastScreenSize != new Vector2(Screen.width, Screen.height)))
        {
            ConfigureSafeArea();
            ConfigureNarrationPanel();
            LayoutChrome();
            RefreshScrollHint();
        }

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
                MessageTone.Success => new Color(0.03f, 0.48f, 0.32f, 1f),
                MessageTone.Warning => new Color(0.62f, 0.35f, 0.03f, 1f),
                MessageTone.Error => new Color(0.73f, 0.18f, 0.17f, 1f),
                _ => InkColor
            };
        }

        if (statusPanel != null)
        {
            Image background = statusPanel.GetComponent<Image>();
            if (background != null)
            {
                background.color = tone == MessageTone.Error
                    ? new Color(1f, 0.95f, 0.94f, 0.98f)
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

    public void SetCompletionAvailable(bool available)
    {
        completionAvailable = available;
        if (completeButton != null)
            completeButton.gameObject.SetActive(available);
    }

    public void SetCompletionSaved(bool saved)
    {
        if (completeButtonText != null)
            completeButtonText.text = saved ? "✓ Leído" : "Terminé";
        if (completeButton != null)
            completeButton.interactable = !saved;
    }

    public void UpdateAudioPlaybackState()
    {
        RefreshAudioControls();
    }

    public void ScrollNarrationToTop()
    {
        if (narrationText == null)
            return;

        SetExpanded(true);
        Canvas.ForceUpdateCanvases();
        ScrollRect scrollRect = narrationText.GetComponentInParent<ScrollRect>();
        if (scrollRect != null)
            scrollRect.verticalNormalizedPosition = 1f;
        RefreshScrollHint();
    }

    public void HandleTrackingState(bool isTracking)
    {
        if (isTracking)
            SetStatus("QR detectado. Mantén el teléfono estable para ver el contenido.", MessageTone.Success, false);
        else
            SetStatus("Puedes seguir leyendo. Para recuperar el modelo, enfoca el QR con buena luz.", MessageTone.Info, false);
    }

    public void SetReadingExpanded(bool expanded)
    {
        SetExpanded(expanded);
    }

    public void SetTeacherAdjustmentOpen(bool open)
    {
        SetExpanded(false);
        if (narrationPanel != null)
            narrationPanel.gameObject.SetActive(!open);
        if (panelShadow != null)
            panelShadow.gameObject.SetActive(!open);
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

        if (narrationPanel != null)
        {
            titleText.transform.SetParent(narrationPanel, false);
            SetStretch(titleText.rectTransform, new Vector2(0.06f, 0.65f), new Vector2(0.73f, 0.81f));
        }

        titleText.fontSize = 56f;
        titleText.enableAutoSizing = true;
        titleText.fontSizeMin = 34f;
        titleText.fontSizeMax = 58f;
        titleText.fontStyle = FontStyles.Bold;
        titleText.color = InkColor;
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
        if (topBar != null)
        {
            Image oldTopBarImage = topBar.GetComponent<Image>();
            if (oldTopBarImage != null)
                oldTopBarImage.raycastTarget = false;
        }

        RectTransform rect = buttonTransform as RectTransform;
        if (rect != null)
            SetStretch(rect, new Vector2(0.64f, 0.925f), new Vector2(0.975f, 0.99f));

        Image background = buttonTransform.GetComponent<Image>();
        if (background != null)
            StyleRounded(background, new Color(1f, 1f, 1f, 0.96f));

        TMP_Text label = buttonTransform.GetComponentInChildren<TMP_Text>();
        if (label != null)
        {
            label.text = "Escanear otro QR";
            label.fontSize = 34f;
            label.enableAutoSizing = true;
            label.fontSizeMin = 26f;
            label.fontSizeMax = 36f;
            label.fontStyle = FontStyles.Bold;
            label.color = InkColor;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
        }
    }

    private void ConfigureNarrationPanel()
    {
        if (narrationPanel == null)
            return;

        bool landscape = Screen.width > Screen.height;
        Vector2 min = landscape
            ? isExpanded ? new Vector2(0.53f, 0.03f) : new Vector2(0.64f, 0.03f)
            : new Vector2(0.025f, 0.015f);
        Vector2 max = landscape
            ? isExpanded ? new Vector2(0.985f, 0.97f) : new Vector2(0.985f, 0.29f)
            : isExpanded ? new Vector2(0.975f, 0.40f) : new Vector2(0.975f, 0.14f);
        SetStretch(narrationPanel, min, max);

        Image background = narrationPanel.GetComponent<Image>();
        if (background != null)
        {
            StyleRounded(background, PanelColor);
            background.raycastTarget = false;
        }

        if (safeArea != null && panelShadow == null)
        {
            panelShadow = CreatePanel("ReadingSheetShadow", safeArea);
            panelShadow.GetComponent<Image>().color = new Color(0f, 0.07f, 0.1f, 0.20f);
            panelShadow.SetSiblingIndex(narrationPanel.GetSiblingIndex());
        }

        if (panelShadow != null)
        {
            SetStretch(panelShadow, min, max);
            panelShadow.anchoredPosition = new Vector2(0f, -12f);
        }
    }

    private void BuildCardHeader()
    {
        if (narrationPanel == null)
            return;

        RectTransform handle = CreatePanel("ReadingSheetHandle", narrationPanel);
        handle.GetComponent<Image>().color = new Color(0.76f, 0.81f, 0.83f, 1f);
        SetStretch(handle, new Vector2(0.43f, 0.955f), new Vector2(0.57f, 0.97f));

        sectionLabel = CreateText("ReadingSectionLabel", narrationPanel, "CAPÍTULO", 28f);
        sectionLabel.fontStyle = FontStyles.Bold;
        sectionLabel.characterSpacing = 3f;
        sectionLabel.color = AccentColor;
        sectionLabel.alignment = TextAlignmentOptions.Left;
        SetStretch(sectionLabel.rectTransform, new Vector2(0.06f, 0.82f), new Vector2(0.55f, 0.93f));

        expandButton = CreateButton("ToggleReadingButton", "Ocultar", narrationPanel,
            new Vector2(0.75f, 0.70f), new Vector2(0.94f, 0.88f), () => SetExpanded(!isExpanded));
        expandButton.GetComponent<Image>().color = new Color(0.88f, 0.95f, 0.95f, 1f);
        expandButtonText = expandButton.GetComponentInChildren<TMP_Text>();
        if (expandButtonText != null)
        {
            expandButtonText.color = AccentColor;
            expandButtonText.fontSize = 32f;
            expandButtonText.fontSizeMin = 25f;
            expandButtonText.fontSizeMax = 34f;
        }
    }

    private void BuildStatusBanner()
    {
        if (safeArea == null)
            return;

        statusPanel = CreatePanel("ARStatusPanel", safeArea);
        SetStretch(statusPanel, new Vector2(0.035f, 0.82f), new Vector2(0.965f, 0.91f));
        statusPanel.GetComponent<Image>().color = PanelColor;
        statusText = CreateText("ARStatusText", statusPanel, "Preparando la realidad aumentada…", 37f);
        SetStretch(statusText.rectTransform, Vector2.zero, Vector2.one);
        statusText.margin = new Vector4(36f, 14f, 36f, 14f);
        statusText.enableAutoSizing = true;
        statusText.fontSizeMin = 28f;
        statusText.fontSizeMax = 39f;
        statusText.color = InkColor;
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
        narrationViewport = viewportRect;
        SetStretch(viewportRect, new Vector2(0.06f, 0.315f), new Vector2(0.94f, 0.64f));

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
        narrationText.fontSize = 46f;
        narrationText.enableAutoSizing = false;
        narrationText.color = InkColor;
        narrationText.fontStyle = FontStyles.Normal;
        narrationText.enableWordWrapping = true;
        narrationText.overflowMode = TextOverflowModes.Overflow;
        narrationText.alignment = TextAlignmentOptions.TopLeft;
        narrationText.lineSpacing = 8f;
        narrationText.paragraphSpacing = 10f;
        narrationText.margin = new Vector4(4f, 4f, 4f, 12f);
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
        scrollRect.scrollSensitivity = 48f;

        scrollHintText = CreateText("NarrationScrollHint", narrationPanel, "Desliza para seguir leyendo", 27f);
        SetStretch(scrollHintText.rectTransform, new Vector2(0.12f, 0.255f), new Vector2(0.88f, 0.305f));
        scrollHintText.color = MutedColor;
        scrollHintText.enableAutoSizing = true;
        scrollHintText.fontSizeMin = 22f;
        scrollHintText.fontSizeMax = 29f;
        scrollHintText.alignment = TextAlignmentOptions.Center;
    }

    private void BuildAudioControls()
    {
        if (narrationPanel == null)
            return;

        playButton = CreateButton("PlayNarrationButton", "Escuchar audio", narrationPanel,
            new Vector2(0.055f, 0.06f), new Vector2(0.485f, 0.235f), () => playAudio?.Invoke());
        pauseButton = CreateButton("PauseNarrationButton", "Pausar audio", narrationPanel,
            new Vector2(0.515f, 0.06f), new Vector2(0.945f, 0.235f), () => pauseAudio?.Invoke());

        playButtonText = playButton.GetComponentInChildren<TMP_Text>();
        pauseButtonText = pauseButton.GetComponentInChildren<TMP_Text>();
        playIcon = CreateAudioIcon("PlayIcon", playButton.transform, GetAudioGlyph(true), Color.white);
        pauseIcon = CreateAudioIcon("PauseIcon", pauseButton.transform, GetAudioGlyph(false), AccentColor);
        if (playButtonText != null)
            SetStretch(playButtonText.rectTransform, new Vector2(0.20f, 0f), Vector2.one);
        if (pauseButtonText != null)
            SetStretch(pauseButtonText.rectTransform, new Vector2(0.20f, 0f), Vector2.one);
        pauseButton.GetComponent<Image>().color = new Color(0.88f, 0.95f, 0.95f, 1f);
        if (pauseButtonText != null)
            pauseButtonText.color = AccentColor;
        RefreshAudioControls();
    }

    private void BuildCompletionButton()
    {
        if (safeArea == null)
            return;

        completeButton = CreateButton("CompleteChapterButton", "Terminé", safeArea,
            new Vector2(0.035f, 0.925f), new Vector2(0.60f, 0.99f), () => markCompleted?.Invoke());
        completeButtonText = completeButton.GetComponentInChildren<TMP_Text>();
        if (completeButtonText != null)
        {
            completeButtonText.fontSize = 30f;
            completeButtonText.fontSizeMin = 23f;
            completeButtonText.fontSizeMax = 32f;
        }
        completeButton.gameObject.SetActive(false);
    }

    private void BuildRetryButton()
    {
        if (safeArea == null)
            return;

        retryButton = CreateButton("RetryContentButton", "Reintentar carga", safeArea,
            new Vector2(0.24f, 0.745f), new Vector2(0.76f, 0.81f), () => retryContent?.Invoke());
        retryButton.gameObject.SetActive(false);
    }

    private RectTransform CreatePanel(string objectName, Transform parent)
    {
        GameObject panelObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        panelObject.layer = parent.gameObject.layer;
        panelObject.transform.SetParent(parent, false);
        Image image = panelObject.GetComponent<Image>();
        StyleRounded(image, PanelColor);
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
        StyleRounded(image, AccentColor);

        Button button = buttonObject.GetComponent<Button>();
        button.targetGraphic = image;
        button.transition = Selectable.Transition.ColorTint;
        button.colors = new ColorBlock
        {
            normalColor = Color.white,
            highlightedColor = new Color(0.88f, 0.97f, 0.97f, 1f),
            pressedColor = new Color(0.74f, 0.90f, 0.90f, 1f),
            selectedColor = Color.white,
            disabledColor = new Color(1f, 1f, 1f, 0.52f),
            colorMultiplier = 1f,
            fadeDuration = 0.12f
        };
        button.onClick.AddListener(() => onClick?.Invoke());

        TMP_Text text = CreateText(objectName + "Label", buttonObject.transform, label, 35f);
        SetStretch(text.rectTransform, Vector2.zero, Vector2.one);
        text.margin = new Vector4(14f, 8f, 14f, 8f);
        text.enableAutoSizing = true;
        text.fontSizeMin = 26f;
        text.fontSizeMax = 38f;
        text.fontStyle = FontStyles.Bold;
        text.color = Color.white;
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
        text.color = InkColor;
        text.enableWordWrapping = true;
        text.raycastTarget = false;
        return text;
    }

    private void RefreshAudioControls()
    {
        bool hasClip = audioAvailable && audioSource != null && audioSource.clip != null;
        bool playing = hasClip && audioSource.isPlaying;
        bool showAudio = audioLoading || hasClip;
        lastAudioPlaying = playing;

        if (playButton != null)
        {
            playButton.gameObject.SetActive(isExpanded && showAudio);
            playButton.interactable = hasClip && !playing;
            SetStretch(playButton.transform as RectTransform,
                new Vector2(0.055f, 0.06f),
                hasClip ? new Vector2(0.485f, 0.235f) : new Vector2(0.945f, 0.235f));
            if (playButtonText != null)
            {
                bool paused = hasClip && !playing && audioSource.time > 0f && audioSource.time < audioSource.clip.length;
                playButtonText.text = audioLoading ? "Preparando audio…" : hasClip ? paused ? "Continuar audio" : "Escuchar audio" : "Audio no disponible";
                playButtonText.color = hasClip ? Color.white : InkColor;
            }
        }

        if (playIcon != null)
            playIcon.gameObject.SetActive(hasClip);

        if (pauseButton != null)
        {
            pauseButton.gameObject.SetActive(isExpanded && hasClip);
            pauseButton.interactable = playing;
        }
        if (pauseIcon != null)
            pauseIcon.color = playing ? AccentColor : MutedColor;

        if (narrationViewport != null)
            SetStretch(narrationViewport,
                new Vector2(0.06f, showAudio ? 0.315f : 0.12f),
                new Vector2(0.94f, 0.64f));
        if (scrollHintText != null)
            SetStretch(scrollHintText.rectTransform,
                new Vector2(0.12f, showAudio ? 0.255f : 0.06f),
                new Vector2(0.88f, showAudio ? 0.305f : 0.11f));
        RefreshScrollHint();
    }

    private void SetExpanded(bool expanded)
    {
        if (isExpanded == expanded)
            return;

        isExpanded = expanded;
        ConfigureNarrationPanel();

        if (titleText != null)
            SetStretch(titleText.rectTransform,
                expanded ? new Vector2(0.06f, 0.65f) : new Vector2(0.06f, 0.16f),
                expanded ? new Vector2(0.73f, 0.81f) : new Vector2(0.72f, 0.75f));

        if (sectionLabel != null)
            sectionLabel.gameObject.SetActive(expanded);
        if (narrationViewport != null)
            narrationViewport.gameObject.SetActive(expanded);

        if (expandButton != null)
            SetStretch(expandButton.transform as RectTransform,
                expanded ? new Vector2(0.75f, 0.70f) : new Vector2(0.75f, 0.21f),
                expanded ? new Vector2(0.94f, 0.88f) : new Vector2(0.94f, 0.78f));
        if (expandButtonText != null)
            expandButtonText.text = expanded ? "Ocultar" : "Leer";

        RefreshAudioControls();
    }

    private void LayoutChrome()
    {
        if (safeArea == null)
            return;

        bool landscape = Screen.width > Screen.height;
        RectTransform rescanRect = safeArea.Find("RescanButton") as RectTransform;
        if (rescanRect != null)
            SetStretch(rescanRect,
                landscape ? new Vector2(0.035f, 0.88f) : new Vector2(0.64f, 0.925f),
                landscape ? new Vector2(0.36f, 0.985f) : new Vector2(0.975f, 0.99f));
        if (completeButton != null)
            SetStretch(completeButton.transform as RectTransform,
                landscape ? new Vector2(0.38f, 0.88f) : new Vector2(0.035f, 0.925f),
                landscape ? new Vector2(0.51f, 0.985f) : new Vector2(0.60f, 0.99f));

        if (statusPanel != null)
            SetStretch(statusPanel,
                landscape ? new Vector2(0.035f, 0.70f) : new Vector2(0.035f, 0.82f),
                landscape ? new Vector2(0.49f, 0.86f) : new Vector2(0.965f, 0.91f));

        if (retryButton != null)
            SetStretch(retryButton.transform as RectTransform,
                landscape ? new Vector2(0.035f, 0.54f) : new Vector2(0.24f, 0.745f),
                landscape ? new Vector2(0.38f, 0.675f) : new Vector2(0.76f, 0.81f));
    }

    private void RefreshScrollHint()
    {
        if (scrollHintText == null)
            return;

        Canvas.ForceUpdateCanvases();
        bool hasOverflow = isExpanded && narrationText != null && narrationViewport != null &&
            narrationText.preferredHeight > narrationViewport.rect.height + 16f;
        scrollHintText.gameObject.SetActive(hasOverflow);
    }

    private Image CreateAudioIcon(string name, Transform parent, Sprite sprite, Color color)
    {
        GameObject iconObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        iconObject.layer = parent.gameObject.layer;
        iconObject.transform.SetParent(parent, false);
        RectTransform rect = iconObject.GetComponent<RectTransform>();
        SetStretch(rect, new Vector2(0.08f, 0.24f), new Vector2(0.19f, 0.76f));
        Image icon = iconObject.GetComponent<Image>();
        icon.sprite = sprite;
        icon.color = color;
        icon.preserveAspect = true;
        icon.raycastTarget = false;
        return icon;
    }

    private static Sprite GetAudioGlyph(bool play)
    {
        Sprite cached = play ? playSprite : pauseSprite;
        if (cached != null)
            return cached;

        const int size = 32;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.name = play ? "AR play icon" : "AR pause icon";
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;
        texture.hideFlags = HideFlags.HideAndDontSave;
        Color32[] pixels = new Color32[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool filled = play
                    ? x >= 8 && x <= 25 && Mathf.Abs(y - 16f) <= 11f * (25f - x) / 17f
                    : y >= 5 && y <= 27 && ((x >= 8 && x <= 13) || (x >= 19 && x <= 24));
                pixels[y * size + x] = filled
                    ? new Color32(255, 255, 255, 255)
                    : new Color32(255, 255, 255, 0);
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, false);
        Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f));
        sprite.name = texture.name;
        sprite.hideFlags = HideFlags.HideAndDontSave;
        if (play)
            playSprite = sprite;
        else
            pauseSprite = sprite;
        return sprite;
    }

    private static void StyleRounded(Image image, Color color)
    {
        image.sprite = GetRoundedSprite();
        image.type = Image.Type.Sliced;
        image.color = color;
    }

    private static Sprite GetRoundedSprite()
    {
        if (roundedSprite != null)
            return roundedSprite;

        const int size = 64;
        const float radius = 18f;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.name = "AR UI rounded corners";
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;
        texture.hideFlags = HideFlags.HideAndDontSave;
        Color32[] pixels = new Color32[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Abs(x - (size - 1) * 0.5f) - (size * 0.5f - radius);
                float dy = Mathf.Abs(y - (size - 1) * 0.5f) - (size * 0.5f - radius);
                float outside = Mathf.Sqrt(Mathf.Max(dx, 0f) * Mathf.Max(dx, 0f) + Mathf.Max(dy, 0f) * Mathf.Max(dy, 0f));
                float signedDistance = outside + Mathf.Min(Mathf.Max(dx, dy), 0f) - radius;
                byte alpha = (byte)Mathf.RoundToInt(255f * Mathf.Clamp01(0.5f - signedDistance));
                pixels[y * size + x] = new Color32(255, 255, 255, alpha);
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, false);
        roundedSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
            new Vector4(20f, 20f, 20f, 20f));
        roundedSprite.name = "AR UI rounded sprite";
        roundedSprite.hideFlags = HideFlags.HideAndDontSave;
        return roundedSprite;
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
