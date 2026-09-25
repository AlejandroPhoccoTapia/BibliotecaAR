using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using ZXing;
using ZXing.Common;

public class QRCodeScanner : MonoBehaviour
{
    [Header("UI")]
    public RawImage cameraPreview;
    public TMP_Text statusText;
    public TMP_Text instructionText;
    public Image scanFrame;
    public Button retryButton;
    public bool cameraPreviewCoversScreen = true;

    [Header("Student API")]
    public string studentApiBaseUrl = "https://bibliotecaar-backend.onrender.com/api";

    [Header("Scan")]
    public float scanInterval = 0.25f;
    public float cameraStartupTimeoutSeconds = 8f;
    public bool stopAfterFirstScan = true;

    [Header("Scene Flow")]
    public bool loadSceneAfterScan = true;
    public string arSceneName = "ARScene";
    public float loadSceneDelay = 0.75f;

    private WebCamTexture webCamTexture;
    private BarcodeReader barcodeReader;
    private string lastReadCode;
    private bool isScanning;
    private int lastPreviewWidth;
    private int lastPreviewHeight;
    private int lastPreviewRotation = -1;
    private Vector2 lastPreviewContainerSize;
    private RectTransform scannerSafeArea;
    private RectTransform instructionCard;
    private RectTransform statusCard;
    private Image[] reticleBackplates;
    private Image[] reticleSegments;
    private Coroutine reticlePulseCoroutine;
    private Vector2 lastScreenSize;
    private Rect lastScreenSafeArea;
    private static Sprite messageCardSprite;

    private static readonly Color InkColor = new Color(0.075f, 0.14f, 0.20f, 1f);
    private static readonly Color AccentColor = new Color(0.05f, 0.67f, 0.76f, 1f);

    private IEnumerator Start()
    {
        ConfigureCanvasForMobile();
        ConfigureCameraPreviewRect();
        ConfigureScannerOverlay();
        ConfigureScanReticle();
        ConfigureRetryButton();
        ApplyScannerLayout();
        SetScannerMessage("Solicitando permiso de cámara…", "La cámara se abrirá para leer el código del libro.", MessageTone.Neutral);

        if (!string.IsNullOrWhiteSpace(studentApiBaseUrl))
            StudentAppSession.ApiBaseUrl = studentApiBaseUrl.TrimEnd('/');
        StudentAppFlow studentFlow = gameObject.AddComponent<StudentAppFlow>();
        studentFlow.Initialize(this, statusText != null ? statusText.font : null);
        while (!studentFlow.ScanRequested)
            yield return null;

        if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
            yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);

        if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
        {
            SetScannerMessage("No se pudo acceder a la cámara", "Si ya denegaste el permiso, actívalo en Ajustes y vuelve a intentarlo.", MessageTone.Error);
            Debug.LogError("QRCodeScanner: permiso de camara denegado");
            yield break;
        }

        if (WebCamTexture.devices.Length == 0)
        {
            SetScannerMessage("No se encontró una cámara", "Comprueba que el teléfono tenga una cámara disponible y vuelve a intentar.", MessageTone.Error);
            Debug.LogError("QRCodeScanner: no hay camaras disponibles");
            yield break;
        }

        barcodeReader = new BarcodeReader
        {
            AutoRotate = true,
            Options = new DecodingOptions
            {
                TryHarder = true,
                PossibleFormats = new[] { BarcodeFormat.QR_CODE }
            }
        };

        WebCamDevice? backCamera = FindBackCamera();
        string cameraName = backCamera.HasValue ? backCamera.Value.name : null;

        webCamTexture = string.IsNullOrEmpty(cameraName)
            ? new WebCamTexture()
            : new WebCamTexture(cameraName);

        if (cameraPreview != null)
            cameraPreview.texture = webCamTexture;

        webCamTexture.Play();
        SetScannerMessage("Preparando cámara…", "Enfoca el código del libro dentro del marco.", MessageTone.Neutral);

        float startupDeadline = Time.realtimeSinceStartup + Mathf.Max(1f, cameraStartupTimeoutSeconds);
        while (webCamTexture != null && webCamTexture.isPlaying &&
               (webCamTexture.width <= 16 || webCamTexture.height <= 16) &&
               Time.realtimeSinceStartup < startupDeadline)
        {
            yield return null;
        }

        if (webCamTexture == null || !webCamTexture.isPlaying ||
            webCamTexture.width <= 16 || webCamTexture.height <= 16)
        {
            if (webCamTexture != null && webCamTexture.isPlaying)
                webCamTexture.Stop();
            SetScannerMessage("La cámara no respondió", "Cierra otras apps que usen la cámara y vuelve a abrir el escáner.", MessageTone.Error);
            Debug.LogError("QRCodeScanner: la camara no entrego imagen a tiempo");
            yield break;
        }

        isScanning = true;

        Debug.Log("QRCodeScanner: camara iniciada " + (cameraName ?? "default"));
        SetScannerMessage("Buscando código…", "Apunta al QR del libro y mantenlo dentro del marco.", MessageTone.Neutral);
        reticlePulseCoroutine = StartCoroutine(PulseScanReticle());

        StartCoroutine(UpdateCameraPreviewLayout());
        StartCoroutine(ScanLoop());
    }

    private void OnDestroy()
    {
        isScanning = false;

        if (reticlePulseCoroutine != null)
            StopCoroutine(reticlePulseCoroutine);

        if (webCamTexture != null && webCamTexture.isPlaying)
            webCamTexture.Stop();
    }

    private void Update()
    {
        if (scannerSafeArea != null &&
            (lastScreenSize != new Vector2(Screen.width, Screen.height) || lastScreenSafeArea != Screen.safeArea))
        {
            ApplyScannerLayout();
        }
    }

    private IEnumerator ScanLoop()
    {
        while (isScanning)
        {
            TryReadQRCode();
            yield return new WaitForSeconds(scanInterval);
        }
    }

    private IEnumerator UpdateCameraPreviewLayout()
    {
        while (webCamTexture != null && webCamTexture.isPlaying)
        {
            ApplyCameraPreviewTransform();
            yield return null;
        }
    }

    private void TryReadQRCode()
    {
        if (webCamTexture == null || !webCamTexture.isPlaying)
            return;

        if (webCamTexture.width <= 16 || webCamTexture.height <= 16)
            return;

        try
        {
            Color32[] pixels = webCamTexture.GetPixels32();
            Result result = barcodeReader.Decode(pixels, webCamTexture.width, webCamTexture.height);

            if (result == null || string.IsNullOrWhiteSpace(result.Text))
                return;

            if (result.Text == lastReadCode)
                return;

            lastReadCode = result.Text;
            ScannedQRData.LastCode = result.Text;

            Debug.Log("QR leido: " + result.Text);
            SetScannerMessage("¡Código encontrado!", "Cargando el contenido del libro…", MessageTone.Success);
            SetReticleColor(new Color(0.2f, 0.9f, 0.55f, 1f));
            if (reticlePulseCoroutine != null)
            {
                StopCoroutine(reticlePulseCoroutine);
                reticlePulseCoroutine = null;
            }

            if (stopAfterFirstScan || loadSceneAfterScan)
                isScanning = false;

            if (loadSceneAfterScan)
                StartCoroutine(LoadARSceneAfterDelay());
        }
        catch (System.Exception exception)
        {
            Debug.LogError("QRCodeScanner: error leyendo QR: " + exception.Message);
        }
    }

    private static WebCamDevice? FindBackCamera()
    {
        foreach (WebCamDevice device in WebCamTexture.devices)
        {
            if (!device.isFrontFacing)
                return device;
        }

        return WebCamTexture.devices.Length > 0 ? (WebCamDevice?)WebCamTexture.devices[0] : null;
    }

    private enum MessageTone
    {
        Neutral,
        Success,
        Error
    }

    private void SetScannerMessage(string status, string instruction, MessageTone tone)
    {
        if (statusText != null)
        {
            statusText.text = status;
            statusText.color = tone == MessageTone.Error
                ? new Color(0.74f, 0.18f, 0.18f, 1f)
                : tone == MessageTone.Success
                    ? new Color(0.03f, 0.48f, 0.32f, 1f)
                    : InkColor;
        }

        if (instructionText != null)
            instructionText.text = instruction;

        if (statusCard != null)
        {
            Image background = statusCard.GetComponent<Image>();
            if (background != null)
                background.color = tone == MessageTone.Error
                    ? new Color(1f, 0.95f, 0.94f, 0.97f)
                    : tone == MessageTone.Success
                        ? new Color(0.93f, 0.99f, 0.96f, 0.97f)
                        : new Color(0.985f, 0.991f, 0.991f, 0.96f);
        }

        if (tone == MessageTone.Error)
            SetReticleColor(new Color(1f, 0.4f, 0.35f, 1f));
        else if (tone == MessageTone.Neutral && !isScanning)
            SetReticleColor(new Color(0.35f, 0.9f, 1f, 0.9f));

        if (retryButton != null)
            retryButton.gameObject.SetActive(tone == MessageTone.Error);

        Debug.Log("QRCodeScanner: " + status);
    }

    public void RetryCameraSetup()
    {
        if (webCamTexture != null && webCamTexture.isPlaying)
            webCamTexture.Stop();

        StudentAppSession.OpenScannerOnLoad = true;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    private void ConfigureScannerOverlay()
    {
        Canvas canvas = cameraPreview != null ? cameraPreview.GetComponentInParent<Canvas>() : null;
        if (canvas == null)
            return;

        GameObject safeAreaObject = new GameObject("ScannerSafeArea", typeof(RectTransform));
        safeAreaObject.layer = canvas.gameObject.layer;
        safeAreaObject.transform.SetParent(canvas.transform, false);
        scannerSafeArea = safeAreaObject.GetComponent<RectTransform>();

        if (scanFrame != null)
            scanFrame.transform.SetParent(scannerSafeArea, false);

        instructionCard = CreateMessageCard("ScanInstructionCard");
        statusCard = CreateMessageCard("ScanStatusCard");

        if (instructionText != null)
        {
            instructionText.transform.SetParent(instructionCard, false);
            SetStretch(instructionText.rectTransform, Vector2.zero, Vector2.one);
            instructionText.fontSize = 44f;
            instructionText.enableAutoSizing = true;
            instructionText.fontSizeMin = 32f;
            instructionText.fontSizeMax = 46f;
            instructionText.margin = new Vector4(28f, 12f, 28f, 12f);
            instructionText.color = InkColor;
            instructionText.alignment = TextAlignmentOptions.Center;
            instructionText.raycastTarget = false;
        }

        if (statusText != null)
        {
            statusText.transform.SetParent(statusCard, false);
            SetStretch(statusText.rectTransform, Vector2.zero, Vector2.one);
            statusText.fontSize = 45f;
            statusText.enableAutoSizing = true;
            statusText.fontSizeMin = 32f;
            statusText.fontSizeMax = 48f;
            statusText.fontStyle = FontStyles.Bold;
            statusText.margin = new Vector4(26f, 10f, 26f, 10f);
            statusText.color = InkColor;
            statusText.alignment = TextAlignmentOptions.Center;
            statusText.raycastTarget = false;
        }
    }

    private RectTransform CreateMessageCard(string name)
    {
        GameObject cardObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        cardObject.layer = scannerSafeArea.gameObject.layer;
        cardObject.transform.SetParent(scannerSafeArea, false);
        Image background = cardObject.GetComponent<Image>();
        background.sprite = GetMessageCardSprite();
        background.type = Image.Type.Sliced;
        background.color = new Color(0.985f, 0.991f, 0.991f, 0.96f);
        background.raycastTarget = false;
        return cardObject.GetComponent<RectTransform>();
    }

    internal static Sprite GetMessageCardSprite()
    {
        if (messageCardSprite != null)
            return messageCardSprite;

        const int size = 64;
        const float radius = 18f;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "Scanner rounded card",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave
        };

        Color32[] pixels = new Color32[size * size];
        float half = size * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Abs(x + 0.5f - half) - (half - radius);
                float dy = Mathf.Abs(y + 0.5f - half) - (half - radius);
                float outerX = Mathf.Max(dx, 0f);
                float outerY = Mathf.Max(dy, 0f);
                float outer = Mathf.Sqrt(outerX * outerX + outerY * outerY);
                float distance = outer + Mathf.Min(Mathf.Max(dx, dy), 0f) - radius;
                byte alpha = (byte)Mathf.RoundToInt(255f * Mathf.Clamp01(0.5f - distance));
                pixels[y * size + x] = new Color32(255, 255, 255, alpha);
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, false);
        messageCardSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(20f, 20f, 20f, 20f));
        messageCardSprite.hideFlags = HideFlags.HideAndDontSave;
        return messageCardSprite;
    }

    private void ApplyScannerLayout()
    {
        if (scannerSafeArea == null || Screen.width <= 0 || Screen.height <= 0)
            return;

        Rect safe = Screen.safeArea;
        scannerSafeArea.anchorMin = new Vector2(safe.xMin / Screen.width, safe.yMin / Screen.height);
        scannerSafeArea.anchorMax = new Vector2(safe.xMax / Screen.width, safe.yMax / Screen.height);
        scannerSafeArea.offsetMin = Vector2.zero;
        scannerSafeArea.offsetMax = Vector2.zero;
        Canvas.ForceUpdateCanvases();

        float safeWidth = scannerSafeArea.rect.width;
        float safeHeight = scannerSafeArea.rect.height;
        if (safeWidth <= 0f || safeHeight <= 0f)
            return;

        bool landscape = safeWidth > safeHeight;
        if (instructionCard != null)
            SetStretch(instructionCard,
                landscape ? new Vector2(0.025f, 0.66f) : new Vector2(0.055f, 0.80f),
                landscape ? new Vector2(0.34f, 0.90f) : new Vector2(0.945f, 0.92f));
        if (statusCard != null)
            SetStretch(statusCard,
                landscape ? new Vector2(0.025f, 0.14f) : new Vector2(0.10f, 0.115f),
                landscape ? new Vector2(0.34f, 0.33f) : new Vector2(0.90f, 0.205f));

        if (scanFrame != null)
        {
            float frameSize = Mathf.Min(safeWidth * (landscape ? 0.37f : 0.66f),
                safeHeight * (landscape ? 0.55f : 0.34f));
            RectTransform frameRect = scanFrame.rectTransform;
            frameRect.anchorMin = new Vector2(0.5f, 0.5f);
            frameRect.anchorMax = new Vector2(0.5f, 0.5f);
            frameRect.pivot = new Vector2(0.5f, 0.5f);
            frameRect.anchoredPosition = landscape ? new Vector2(safeWidth * 0.14f, 0f) : Vector2.zero;
            frameRect.sizeDelta = new Vector2(frameSize, frameSize);
            UpdateReticleGeometry(frameSize);
        }

        if (retryButton != null)
            SetStretch(retryButton.transform as RectTransform,
                landscape ? new Vector2(0.035f, 0.025f) : new Vector2(0.22f, 0.025f),
                landscape ? new Vector2(0.33f, 0.125f) : new Vector2(0.78f, 0.10f));

        lastScreenSize = new Vector2(Screen.width, Screen.height);
        lastScreenSafeArea = safe;
    }

    private static void SetStretch(RectTransform rect, Vector2 min, Vector2 max)
    {
        if (rect == null)
            return;

        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = Vector2.zero;
    }

    private void ConfigureRetryButton()
    {
        if (retryButton == null)
        {
            Canvas canvas = cameraPreview != null ? cameraPreview.GetComponentInParent<Canvas>() : null;
            Transform parent = scannerSafeArea != null ? scannerSafeArea : canvas != null ? canvas.transform : null;

            if (parent == null)
                return;

            GameObject buttonObject = new GameObject("RetryCameraButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            buttonObject.layer = parent.gameObject.layer;
            buttonObject.transform.SetParent(parent, false);
            retryButton = buttonObject.GetComponent<Button>();

            RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(0.22f, 0.055f);
            buttonRect.anchorMax = new Vector2(0.78f, 0.12f);
            buttonRect.offsetMin = Vector2.zero;
            buttonRect.offsetMax = Vector2.zero;
            buttonRect.anchoredPosition = Vector2.zero;
            buttonRect.sizeDelta = Vector2.zero;

            Image image = buttonObject.GetComponent<Image>();
            image.sprite = GetMessageCardSprite();
            image.type = Image.Type.Sliced;
            image.color = new Color(0.02f, 0.43f, 0.49f, 1f);
            retryButton.targetGraphic = image;
            retryButton.colors = new ColorBlock
            {
                normalColor = Color.white,
                highlightedColor = new Color(0.88f, 0.97f, 0.97f, 1f),
                pressedColor = new Color(0.74f, 0.90f, 0.90f, 1f),
                selectedColor = Color.white,
                disabledColor = new Color(1f, 1f, 1f, 0.52f),
                colorMultiplier = 1f,
                fadeDuration = 0.12f
            };

            GameObject labelObject = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            labelObject.layer = parent.gameObject.layer;
            labelObject.transform.SetParent(buttonObject.transform, false);
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
            label.font = statusText != null ? statusText.font : null;
            label.text = "Reintentar cámara";
            label.fontSize = 38f;
            label.enableAutoSizing = true;
            label.fontSizeMin = 29f;
            label.fontSizeMax = 40f;
            label.fontStyle = FontStyles.Bold;
            label.color = Color.white;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
        }

        retryButton.onClick.RemoveListener(RetryCameraSetup);
        retryButton.onClick.AddListener(RetryCameraSetup);
        retryButton.gameObject.SetActive(false);
    }

    private void ConfigureScanReticle()
    {
        if (scanFrame == null)
            return;

        scanFrame.raycastTarget = false;
        reticleBackplates = new Image[8];
        reticleSegments = new Image[8];
        string[] corners =
        {
            "TopLeftHorizontal", "TopLeftVertical", "TopRightHorizontal", "TopRightVertical",
            "BottomLeftHorizontal", "BottomLeftVertical", "BottomRightHorizontal", "BottomRightVertical"
        };

        for (int i = 0; i < corners.Length; i++)
            reticleBackplates[i] = CreateReticleSegment(corners[i] + "Outline", new Color(0.04f, 0.17f, 0.21f, 0.9f));
        for (int i = 0; i < corners.Length; i++)
            reticleSegments[i] = CreateReticleSegment(corners[i], AccentColor);

        SetReticleColor(AccentColor);
    }

    private Image CreateReticleSegment(string segmentName, Color color)
    {
        GameObject segmentObject = new GameObject(segmentName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        segmentObject.layer = scanFrame.gameObject.layer;
        segmentObject.transform.SetParent(scanFrame.transform, false);

        RectTransform rect = segmentObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = Vector2.zero;

        Image segment = segmentObject.GetComponent<Image>();
        segment.color = color;
        segment.raycastTarget = false;
        return segment;
    }

    private void UpdateReticleGeometry(float frameSize)
    {
        if (reticleSegments == null || reticleBackplates == null)
            return;

        float cornerLength = Mathf.Clamp(frameSize * 0.19f, 48f, 100f);
        float thickness = Mathf.Clamp(frameSize * 0.015f, 8f, 13f);
        PositionReticleSegments(reticleBackplates, frameSize, cornerLength + 6f, thickness + 7f);
        PositionReticleSegments(reticleSegments, frameSize, cornerLength, thickness);
    }

    private static void PositionReticleSegments(Image[] segments, float frameSize, float cornerLength, float thickness)
    {
        float edge = frameSize * 0.5f - thickness * 0.5f;
        float inside = frameSize * 0.5f - cornerLength * 0.5f;
        SetReticleSegment(segments[0], new Vector2(-inside, edge), new Vector2(cornerLength, thickness));
        SetReticleSegment(segments[1], new Vector2(-edge, inside), new Vector2(thickness, cornerLength));
        SetReticleSegment(segments[2], new Vector2(inside, edge), new Vector2(cornerLength, thickness));
        SetReticleSegment(segments[3], new Vector2(edge, inside), new Vector2(thickness, cornerLength));
        SetReticleSegment(segments[4], new Vector2(-inside, -edge), new Vector2(cornerLength, thickness));
        SetReticleSegment(segments[5], new Vector2(-edge, -inside), new Vector2(thickness, cornerLength));
        SetReticleSegment(segments[6], new Vector2(inside, -edge), new Vector2(cornerLength, thickness));
        SetReticleSegment(segments[7], new Vector2(edge, -inside), new Vector2(thickness, cornerLength));
    }

    private static void SetReticleSegment(Image segment, Vector2 position, Vector2 size)
    {
        if (segment == null)
            return;

        RectTransform rect = segment.rectTransform;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private IEnumerator PulseScanReticle()
    {
        while (isScanning)
        {
            float alpha = Mathf.Lerp(0.55f, 1f, (Mathf.Sin(Time.unscaledTime * 2.5f) + 1f) * 0.5f);
            SetReticleColor(new Color(AccentColor.r, AccentColor.g, AccentColor.b, alpha));
            yield return null;
        }
    }

    private void SetReticleColor(Color color)
    {
        if (reticleSegments == null)
            return;

        foreach (Image segment in reticleSegments)
        {
            if (segment != null)
                segment.color = color;
        }
    }

    private void ConfigureCanvasForMobile()
    {
        if (cameraPreview == null)
            return;

        CanvasScaler canvasScaler = cameraPreview.GetComponentInParent<CanvasScaler>();
        if (canvasScaler == null)
            return;

        canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasScaler.referenceResolution = new Vector2(1080f, 1920f);
        canvasScaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        canvasScaler.matchWidthOrHeight = 0.5f;
    }

    private void ConfigureCameraPreviewRect()
    {
        if (cameraPreview == null)
            return;

        RectTransform rectTransform = cameraPreview.rectTransform;
        rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.anchoredPosition = Vector2.zero;
    }

    private void ApplyCameraPreviewTransform()
    {
        if (cameraPreview == null || webCamTexture == null)
            return;

        if (webCamTexture.width <= 16 || webCamTexture.height <= 16)
            return;

        int rotation = webCamTexture.videoRotationAngle;
        if (lastPreviewWidth == webCamTexture.width &&
            lastPreviewHeight == webCamTexture.height &&
            lastPreviewRotation == rotation &&
            lastPreviewContainerSize == GetPreviewContainerSize())
        {
            return;
        }

        lastPreviewWidth = webCamTexture.width;
        lastPreviewHeight = webCamTexture.height;
        lastPreviewRotation = rotation;
        lastPreviewContainerSize = GetPreviewContainerSize();

        cameraPreview.rectTransform.localEulerAngles = new Vector3(0f, 0f, -rotation);
        cameraPreview.uvRect = webCamTexture.videoVerticallyMirrored
            ? new Rect(0f, 1f, 1f, -1f)
            : new Rect(0f, 0f, 1f, 1f);

        ResizePreviewToScreen(rotation);

        Debug.Log(
            "QRCodeScanner: preview ajustado width=" + webCamTexture.width +
            ", height=" + webCamTexture.height +
            ", rotation=" + rotation +
            ", mirrored=" + webCamTexture.videoVerticallyMirrored
        );
    }

    private void ResizePreviewToScreen(int rotation)
    {
        if (cameraPreview == null)
            return;

        RectTransform previewRect = cameraPreview.rectTransform;
        Vector2 containerSize = GetPreviewContainerSize();
        float containerWidth = containerSize.x;
        float containerHeight = containerSize.y;

        if (containerWidth <= 0f || containerHeight <= 0f)
            return;

        bool isSideways = rotation == 90 || rotation == 270;
        float textureWidth = isSideways ? webCamTexture.height : webCamTexture.width;
        float textureHeight = isSideways ? webCamTexture.width : webCamTexture.height;
        float textureAspect = textureWidth / textureHeight;
        float containerAspect = containerWidth / containerHeight;

        float targetWidth;
        float targetHeight;

        if (cameraPreviewCoversScreen)
        {
            if (containerAspect > textureAspect)
            {
                targetWidth = containerWidth;
                targetHeight = targetWidth / textureAspect;
            }
            else
            {
                targetHeight = containerHeight;
                targetWidth = targetHeight * textureAspect;
            }
        }
        else
        {
            if (containerAspect > textureAspect)
            {
                targetHeight = containerHeight;
                targetWidth = targetHeight * textureAspect;
            }
            else
            {
                targetWidth = containerWidth;
                targetHeight = targetWidth / textureAspect;
            }
        }

        previewRect.sizeDelta = isSideways
            ? new Vector2(targetHeight, targetWidth)
            : new Vector2(targetWidth, targetHeight);
    }

    private Vector2 GetPreviewContainerSize()
    {
        if (cameraPreview == null)
            return new Vector2(Screen.width, Screen.height);

        Canvas canvas = cameraPreview.GetComponentInParent<Canvas>();
        if (canvas != null)
        {
            RectTransform canvasRect = canvas.transform as RectTransform;
            if (canvasRect != null && canvasRect.rect.width > 0f && canvasRect.rect.height > 0f)
                return canvasRect.rect.size;
        }

        RectTransform previewRect = cameraPreview.rectTransform;
        RectTransform parentRect = previewRect.parent as RectTransform;

        if (parentRect == null)
            return new Vector2(Screen.width, Screen.height);

        return parentRect.rect.size;
    }

    private IEnumerator LoadARSceneAfterDelay()
    {
        yield return new WaitForSeconds(loadSceneDelay);

        if (webCamTexture != null && webCamTexture.isPlaying)
            webCamTexture.Stop();

        Debug.Log("QRCodeScanner: cargando escena AR: " + arSceneName);
        SceneManager.LoadScene(arSceneName);
    }
}
