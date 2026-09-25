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
    private Image[] reticleSegments;
    private Coroutine reticlePulseCoroutine;

    private IEnumerator Start()
    {
        ConfigureCanvasForMobile();
        ConfigureCameraPreviewRect();
        ConfigureScanReticle();
        ConfigureRetryButton();
        SetScannerMessage("Solicitando permiso de cámara…", "La cámara se abrirá para leer el código del libro.", MessageTone.Neutral);

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
        SetScannerMessage("Buscando código…", "Centra el QR dentro del marco y mantén el teléfono quieto.", MessageTone.Neutral);
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

        return WebCamTexture.devices.Length > 0 ? WebCamTexture.devices[0] : null;
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
                ? new Color(1f, 0.45f, 0.4f, 1f)
                : tone == MessageTone.Success
                    ? new Color(0.45f, 1f, 0.7f, 1f)
                    : Color.white;
        }

        if (instructionText != null)
            instructionText.text = instruction;

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

        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    private void ConfigureRetryButton()
    {
        if (retryButton == null)
        {
            Canvas canvas = cameraPreview != null ? cameraPreview.GetComponentInParent<Canvas>() : null;
            Transform parent = canvas != null ? canvas.transform : null;
            Transform safeArea = parent != null ? parent.Find("SafeArea") : null;
            if (safeArea != null)
                parent = safeArea;

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
            image.color = new Color(0.06f, 0.15f, 0.21f, 0.96f);
            retryButton.targetGraphic = image;
            retryButton.colors = new ColorBlock
            {
                normalColor = image.color,
                highlightedColor = new Color(0.09f, 0.25f, 0.32f, 1f),
                pressedColor = new Color(0.12f, 0.72f, 0.88f, 1f),
                selectedColor = new Color(0.09f, 0.25f, 0.32f, 1f),
                disabledColor = new Color(0.13f, 0.16f, 0.18f, 0.8f),
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
            label.fontSize = 27f;
            label.enableAutoSizing = true;
            label.fontSizeMin = 21f;
            label.fontSizeMax = 30f;
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
        RectTransform frameRect = scanFrame.rectTransform;
        float width = frameRect.rect.width > 0f ? frameRect.rect.width : frameRect.sizeDelta.x;
        float height = frameRect.rect.height > 0f ? frameRect.rect.height : frameRect.sizeDelta.y;
        float cornerLength = Mathf.Clamp(Mathf.Min(width, height) * 0.16f, 48f, 100f);
        float thickness = Mathf.Clamp(Mathf.Min(width, height) * 0.018f, 10f, 16f);
        float x = Mathf.Max(0f, width * 0.5f - cornerLength * 0.5f);
        float y = Mathf.Max(0f, height * 0.5f - cornerLength * 0.5f);

        reticleSegments = new Image[8];
        reticleSegments[0] = CreateReticleSegment("TopLeftHorizontal", new Vector2(-x, y), new Vector2(cornerLength, thickness));
        reticleSegments[1] = CreateReticleSegment("TopLeftVertical", new Vector2(-width * 0.5f + thickness * 0.5f, y), new Vector2(thickness, cornerLength));
        reticleSegments[2] = CreateReticleSegment("TopRightHorizontal", new Vector2(x, y), new Vector2(cornerLength, thickness));
        reticleSegments[3] = CreateReticleSegment("TopRightVertical", new Vector2(width * 0.5f - thickness * 0.5f, y), new Vector2(thickness, cornerLength));
        reticleSegments[4] = CreateReticleSegment("BottomLeftHorizontal", new Vector2(-x, -y), new Vector2(cornerLength, thickness));
        reticleSegments[5] = CreateReticleSegment("BottomLeftVertical", new Vector2(-width * 0.5f + thickness * 0.5f, -y), new Vector2(thickness, cornerLength));
        reticleSegments[6] = CreateReticleSegment("BottomRightHorizontal", new Vector2(x, -y), new Vector2(cornerLength, thickness));
        reticleSegments[7] = CreateReticleSegment("BottomRightVertical", new Vector2(width * 0.5f - thickness * 0.5f, -y), new Vector2(thickness, cornerLength));
        SetReticleColor(new Color(0.35f, 0.9f, 1f, 0.9f));
    }

    private Image CreateReticleSegment(string segmentName, Vector2 position, Vector2 size)
    {
        GameObject segmentObject = new GameObject(segmentName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        segmentObject.transform.SetParent(scanFrame.transform, false);

        RectTransform rect = segmentObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        Image segment = segmentObject.GetComponent<Image>();
        segment.raycastTarget = false;
        return segment;
    }

    private IEnumerator PulseScanReticle()
    {
        while (isScanning)
        {
            float alpha = Mathf.Lerp(0.55f, 1f, (Mathf.Sin(Time.unscaledTime * 2.5f) + 1f) * 0.5f);
            SetReticleColor(new Color(0.35f, 0.9f, 1f, alpha));
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
