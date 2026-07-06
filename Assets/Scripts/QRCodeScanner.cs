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
    public bool cameraPreviewCoversScreen = true;

    [Header("Scan")]
    public float scanInterval = 0.25f;
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

    private IEnumerator Start()
    {
        ConfigureCanvasForMobile();
        ConfigureCameraPreviewRect();
        SetStatus("Solicitando permiso de camara...");

        if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
            yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);

        if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
        {
            SetStatus("Permiso de camara denegado");
            Debug.LogError("QRCodeScanner: permiso de camara denegado");
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
        isScanning = true;

        Debug.Log("QRCodeScanner: camara iniciada " + (cameraName ?? "default"));
        SetStatus("Esperando QR...");

        StartCoroutine(UpdateCameraPreviewLayout());
        StartCoroutine(ScanLoop());
    }

    private void OnDestroy()
    {
        isScanning = false;

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
            SetStatus("QR leido: " + result.Text);

            if (stopAfterFirstScan)
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

    private void SetStatus(string message)
    {
        if (statusText != null)
            statusText.text = message;

        Debug.Log("QRCodeScanner: " + message);
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
