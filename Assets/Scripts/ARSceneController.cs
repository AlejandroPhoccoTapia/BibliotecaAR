using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using GLTFast;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public class ARSceneController : MonoBehaviour
{
    [Serializable]
    public class SceneContent
    {
        public string qrCode;
        public string title;
        [TextArea(2, 5)]
        public string narration;
        public string prefabKey;
        public GameObject prefab;
        public AudioClip audioClip;
        public string audioUrl;
        public string modelUrl;
    }

    [Serializable]
    public class PrefabBinding
    {
        public string prefabKey;
        public GameObject prefab;
    }

    [Serializable]
    private class UnitySceneApiResponse
    {
        public string qr_code;
        public string book_title;
        public string title;
        public int order;
        public string text;
        public string prefab_key;
        public string cover_url;
        public string audio_url;
        public string glb_model_url;
        public string qr_image_url;
        public float ar_marker_width_cm;
        public float ar_model_size_cm;
        public float ar_offset_x_cm;
        public float ar_offset_y_cm;
        public float ar_offset_z_cm;
        public float ar_yaw_degrees;
    }

    [Header("Content")]
    public List<SceneContent> contents = new List<SceneContent>();
    public List<PrefabBinding> prefabBindings = new List<PrefabBinding>();
    public GameObject fallbackPrefab;

    [Header("API")]
    public bool loadContentFromApi = true;
    public string apiBaseUrl = "https://bibliotecaar-backend.onrender.com/api";
    public float apiTimeoutSeconds = 75f;
    public bool fallbackToLocalContent = true;
    public bool addTrackingImageFromApi = true;
    public float trackingImagePhysicalWidthMeters = 0.06f;
    public bool loadGlbModelFromApi = true;

    [Header("Optional UI")]
    public TMP_Text titleText;
    public TMP_Text narrationText;

    [Header("Optional Audio")]
    public AudioSource audioSource;

    [Header("Scene Flow")]
    public string qrScanSceneName = "QRScanScene";

    private SceneContent selectedContent;
    private Dictionary<string, SceneContent> contentByCode;
    private Dictionary<string, GameObject> prefabByKey;
    private ARSceneExperienceUI experienceUI;
    private QRTrackedImagePlacer trackedImagePlacer;
    private string scannedQrCode;
    private bool isLoadingContent;
    private bool trackingImageSetupFailed;
    private bool modelSetupFailed;
    private bool hasPlaceableModel;
    private ChapterArPlacement currentPlacement;
    private ChapterArPlacement savedPlacement;
    private ARPlacementEditorUI teacherEditor;
    private AndroidNarrationVoice narrationVoice;
    private string speechText;
    private bool useSpeech;
    private float nextVoicePollTime;
    private int contentVersion;
    private GameObject activePresentationRoot;
    private float activePresentationMaxDimension = 1f;
    private bool isSavingPlacement;

    IEnumerator Start()
    {
        BuildContentDictionary();

        experienceUI = gameObject.AddComponent<ARSceneExperienceUI>();
        experienceUI.Initialize(titleText, narrationText, audioSource, PlayAudio, PauseAudio, RetryContentLoad, MarkChapterCompleted);
        if (TeacherPreviewSession.IsActive)
        {
            teacherEditor = gameObject.AddComponent<ARPlacementEditorUI>();
            teacherEditor.Initialize(titleText != null ? titleText.font : null,
                AdjustTeacherPlacement, SaveTeacherPlacement, ResetTeacherPlacement,
                HandleTeacherAdjustmentOpen);
            experienceUI.SetReadingExpanded(false);
        }
        trackedImagePlacer = FindAnyObjectByType<QRTrackedImagePlacer>();
        if (trackedImagePlacer != null)
            trackedImagePlacer.TargetTrackingChanged += OnTargetTrackingChanged;

        scannedQrCode = ScannedQRData.LastCode;
        if (string.IsNullOrWhiteSpace(scannedQrCode))
        {
            Debug.LogWarning("ARSceneController: no hay codigo QR recibido");
            ShowUnavailableContent("No llegó ningún código QR. Vuelve al escáner e inténtalo otra vez.", false);
            yield break;
        }

        Debug.Log("ARSceneController: codigo recibido en ARScene: " + scannedQrCode);

        if (loadContentFromApi)
        {
            yield return LoadContentFromApi(scannedQrCode);
            yield break;
        }

        if (TryApplyLocalContent(scannedQrCode))
            SetTrackingHint();
        else
            ShowUnavailableContent(null, false);
    }

    private void OnDestroy()
    {
        narrationVoice?.Dispose();
        if (trackedImagePlacer != null)
            trackedImagePlacer.TargetTrackingChanged -= OnTargetTrackingChanged;
        if (activePresentationRoot != null)
            Destroy(activePresentationRoot);
    }

    private void Update()
    {
        if (!useSpeech || narrationVoice == null || Time.unscaledTime < nextVoicePollTime)
            return;
        nextVoicePollTime = Time.unscaledTime + 0.25f;
        RefreshVoiceState();
    }

    private void OnApplicationPause(bool paused)
    {
        if (!paused)
            return;
        if (audioSource != null && audioSource.isPlaying)
            audioSource.Pause();
        narrationVoice?.Pause();
        experienceUI?.UpdateAudioPlaybackState();
        RefreshVoiceState();
    }

    public void RetryContentLoad()
    {
        if (isLoadingContent || string.IsNullOrWhiteSpace(scannedQrCode))
            return;

        if (loadContentFromApi)
            StartCoroutine(LoadContentFromApi(scannedQrCode));
        else if (TryApplyLocalContent(scannedQrCode))
            SetTrackingHint();
    }

    public void PlayAudio()
    {
        if (audioSource != null && audioSource.clip != null)
        {
            if (audioSource.time > 0f && audioSource.time < audioSource.clip.length)
                audioSource.UnPause();
            else
                audioSource.Play();
            experienceUI?.UpdateAudioPlaybackState();
            return;
        }

        if (useSpeech && narrationVoice != null)
        {
            narrationVoice.Poll();
            narrationVoice.Play(speechText);
            RefreshVoiceState();
            return;
        }

        Debug.LogWarning("ARSceneController: no hay narración disponible para este QR");
    }

    public void PauseAudio()
    {
        if (audioSource != null && audioSource.clip != null)
            audioSource.Pause();
        else
            narrationVoice?.Pause();
        experienceUI?.UpdateAudioPlaybackState();
        RefreshVoiceState();
    }

    private void HandleTeacherAdjustmentOpen(bool open)
    {
        experienceUI?.SetTeacherAdjustmentOpen(open);
        if (open)
            PauseAudio();
    }

    private void ActivateSpeech(string text)
    {
        speechText = text?.Trim();
        useSpeech = !string.IsNullOrWhiteSpace(speechText);
        if (useSpeech && narrationVoice == null)
            narrationVoice = new AndroidNarrationVoice();
        RefreshVoiceState();
    }

    private void RefreshVoiceState()
    {
        if (useSpeech)
            narrationVoice?.Poll();
        experienceUI?.SetVoiceState(useSpeech, narrationVoice != null && narrationVoice.IsReady,
            narrationVoice != null && narrationVoice.IsSpeaking,
            narrationVoice != null && narrationVoice.IsPaused,
            narrationVoice != null ? narrationVoice.Error : string.Empty);
    }

    public void BackToScanner()
    {
        Debug.Log("ARSceneController: volviendo a escaner QR");
        StudentAppSession.OpenScannerOnLoad = true;
        SceneManager.LoadScene(qrScanSceneName);
    }

    public void MarkChapterCompleted()
    {
        if (StudentAppSession.HasToken && !string.IsNullOrWhiteSpace(ScannedQRData.LastCode))
            StartCoroutine(SaveQrProgress(ScannedQRData.LastCode, "complete"));
    }

    private IEnumerator SaveQrProgress(string qrCode, string action)
    {
        string path = "student/qr/" + UnityWebRequest.EscapeURL(qrCode) + "/" + action + "/";
        using (UnityWebRequest request = StudentApi.Post(path))
        {
            yield return request.SendWebRequest();
            if (request.result == UnityWebRequest.Result.Success)
            {
                if (action == "complete")
                    experienceUI?.SetCompletionSaved(true);
                else
                {
                    StudentProgressData progress = JsonUtility.FromJson<StudentProgressData>(request.downloadHandler.text);
                    experienceUI?.SetCompletionSaved(progress != null && progress.is_completed);
                }
            }
            else if (action == "complete")
            {
                experienceUI?.SetStatus("No se pudo guardar el capítulo. Inténtalo de nuevo.",
                    ARSceneExperienceUI.MessageTone.Warning, false);
            }
        }
    }

    private void ApplyContent(SceneContent content)
    {
        contentVersion++;
        narrationVoice?.Stop();
        speechText = null;
        useSpeech = false;
        experienceUI?.SetVoiceState(false, false, false, false, string.Empty);
        selectedContent = content;

        Debug.Log("ARSceneController: escena seleccionada: " + content.title);
        Debug.Log("ARSceneController: texto narrativo: " + content.narration);

        if (titleText != null)
            titleText.text = content.title;

        if (narrationText != null)
            narrationText.text = string.IsNullOrWhiteSpace(content.narration)
                ? "Este capítulo no tiene texto narrativo."
                : content.narration;
        experienceUI?.ScrollNarrationToTop();
        if (TeacherPreviewSession.IsActive)
            experienceUI?.SetReadingExpanded(false);

        if (audioSource != null)
        {
            audioSource.Stop();
            audioSource.clip = content.audioClip;
        }

        if (audioSource != null && content.audioClip != null)
            experienceUI?.SetAudioAvailable(true);
        else if (audioSource != null && !string.IsNullOrWhiteSpace(content.audioUrl))
        {
            experienceUI?.SetAudioLoading();
            StartCoroutine(LoadAudioFromUrl(content.audioUrl, contentVersion));
        }
        else
        {
            experienceUI?.SetAudioAvailable(false);
            ActivateSpeech(content.narration);
        }

        experienceUI?.SetStatus(
            "Contenido cargado. Apunta al QR impreso para colocar el modelo.",
            ARSceneExperienceUI.MessageTone.Info,
            false);

        GameObject prefabToPlace = content.prefab != null
            ? content.prefab
            : fallbackPrefab;

        ARRaycastPlaceObject placer = FindAnyObjectByType<ARRaycastPlaceObject>();
        if (prefabToPlace == null && placer != null)
            prefabToPlace = placer.objectToPlace;

        hasPlaceableModel = prefabToPlace != null || !string.IsNullOrWhiteSpace(content.modelUrl);

        if (prefabToPlace == null)
        {
            Debug.LogWarning("ARSceneController: no hay prefab para colocar en AR");
            if (string.IsNullOrWhiteSpace(content.modelUrl))
                experienceUI?.SetStatus("El texto está listo, pero este capítulo no tiene un modelo 3D.", ARSceneExperienceUI.MessageTone.Warning, false);
            return;
        }

        GameObject presentation = CreatePresentationRoot("LocalModel_" + SanitizeName(content.qrCode));
        Transform visual = presentation.transform.Find("Visual");
        GameObject localModel = Instantiate(prefabToPlace, visual);
        localModel.transform.localPosition = Vector3.zero;
        localModel.transform.localRotation = Quaternion.identity;
        FinishPresentationRoot(presentation);
        ReplacePresentation(content, presentation);
    }

    private void ApplyPrefabToPlacers(SceneContent content, GameObject prefabToPlace, bool clearExistingTrackedObjects)
    {
        ARRaycastPlaceObject placer = FindAnyObjectByType<ARRaycastPlaceObject>();
        if (placer != null)
        {
            placer.objectToPlace = prefabToPlace;
            Debug.Log("ARSceneController: prefab AR para raycast seleccionado: " + prefabToPlace.name);
        }
        else
        {
            Debug.Log("ARSceneController: no se encontro ARRaycastPlaceObject");
        }

        QRTrackedImagePlacer imagePlacer = FindAnyObjectByType<QRTrackedImagePlacer>();
        if (imagePlacer != null)
        {
            imagePlacer.SetObjectToPlace(prefabToPlace, clearExistingTrackedObjects);
            imagePlacer.targetQrCode = content.qrCode;
            imagePlacer.onlyShowScannedCode = true;
            Debug.Log("ARSceneController: prefab AR para QR seleccionado: " + prefabToPlace.name);
        }
        else
        {
            Debug.LogWarning("ARSceneController: no se encontro QRTrackedImagePlacer");
        }
    }

    private string GetTaskError(Task<bool> task)
    {
        if (task == null)
            return "task null";

        if (task.Exception != null)
            return task.Exception.GetBaseException().Message;

        return "resultado false";
    }

    private string SanitizeName(string rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
            return "sin_codigo";

        foreach (char invalidChar in System.IO.Path.GetInvalidFileNameChars())
            rawName = rawName.Replace(invalidChar, '_');

        return rawName;
    }

    private void BuildContentDictionary()
    {
        EnsureDefaultContents();
        contentByCode = new Dictionary<string, SceneContent>();
        prefabByKey = new Dictionary<string, GameObject>();

        foreach (SceneContent content in contents)
        {
            if (content == null || string.IsNullOrWhiteSpace(content.qrCode))
                continue;

            contentByCode[content.qrCode] = content;

            if (!string.IsNullOrWhiteSpace(content.prefabKey) && content.prefab != null)
                prefabByKey[content.prefabKey] = content.prefab;
        }

        foreach (PrefabBinding binding in prefabBindings)
        {
            if (binding == null || string.IsNullOrWhiteSpace(binding.prefabKey) || binding.prefab == null)
                continue;

            prefabByKey[binding.prefabKey] = binding.prefab;
        }
    }

    private IEnumerator LoadContentFromApi(string qrCode)
    {
        if (isLoadingContent)
            yield break;

        isLoadingContent = true;
        experienceUI?.SetCompletionAvailable(false);
        experienceUI?.SetCompletionSaved(false);
        trackingImageSetupFailed = false;
        modelSetupFailed = false;
        experienceUI?.SetStatus("Buscando el capítulo…", ARSceneExperienceUI.MessageTone.Info, false);
        bool teacherPreviewRequest = TeacherPreviewSession.IsActive;
        string url = teacherPreviewRequest
            ? "teacher/mobile/scenes/" + qrCode : BuildUnitySceneUrl(qrCode);
        Debug.Log("ARSceneController: consultando API: " + url);

        UnityWebRequest request = null;
        UnityWebRequestAsyncOperation operation = null;

        try
        {
            request = teacherPreviewRequest
                ? TeacherPreviewSession.GetChapter(qrCode) : UnityWebRequest.Get(url);
            request.timeout = Mathf.Max(1, Mathf.RoundToInt(apiTimeoutSeconds));
            operation = request.SendWebRequest();
        }
        catch (Exception exception)
        {
            Debug.LogError("ARSceneController: excepcion iniciando request API: " + exception);

            if (request != null)
                request.Dispose();

            isLoadingContent = false;
            if (teacherPreviewRequest || !TryShowLocalFallback(qrCode, "No se pudo conectar con el servidor."))
            {
                ShowUnavailableContent("No se pudo conectar con el servidor. Revisa tu conexión e inténtalo de nuevo.", true);
            }

            yield break;
        }

        yield return operation;

        try
        {
            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning(
                    "ARSceneController: error consultando API para QR " + qrCode +
                    " result=" + request.result +
                    " code=" + request.responseCode +
                    " error=" + request.error
                );

                long responseCode = request.responseCode;
                bool notFound = responseCode == 404;
                string message = teacherPreviewRequest && responseCode == 401
                    ? "La vista docente caducó. Vuelve al inicio y entra otra vez."
                    : notFound
                    ? "Este QR no existe o el libro todavía no está publicado."
                    : request.result == UnityWebRequest.Result.ConnectionError
                        ? "No hay conexión con el servidor. Revisa internet e inténtalo de nuevo."
                        : "No se pudo cargar el capítulo. Inténtalo de nuevo.";

                if (teacherPreviewRequest && responseCode == 401)
                    TeacherPreviewSession.Clear();
                if (teacherPreviewRequest || !TryShowLocalFallback(qrCode, message))
                    ShowUnavailableContent(message, !notFound);

                yield break;
            }

            UnitySceneApiResponse apiScene = null;
            try
            {
                apiScene = JsonUtility.FromJson<UnitySceneApiResponse>(request.downloadHandler.text);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("ARSceneController: respuesta JSON invalida: " + exception.Message);
            }

            if (apiScene == null || string.IsNullOrWhiteSpace(apiScene.qr_code))
            {
                Debug.LogWarning("ARSceneController: API no devolvio una escena valida");
                if (teacherPreviewRequest || !TryShowLocalFallback(qrCode, "El servidor devolvió una respuesta incompleta."))
                    ShowUnavailableContent("No se pudo interpretar el contenido. Puedes volver a intentarlo.", true);

                yield break;
            }

            currentPlacement = new ChapterArPlacement
            {
                ar_marker_width_cm = apiScene.ar_marker_width_cm > 0f ? apiScene.ar_marker_width_cm : 6f,
                ar_model_size_cm = apiScene.ar_model_size_cm > 0f ? apiScene.ar_model_size_cm : 8f,
                ar_offset_x_cm = apiScene.ar_offset_x_cm,
                ar_offset_y_cm = apiScene.ar_offset_y_cm,
                ar_offset_z_cm = apiScene.ar_offset_z_cm,
                ar_yaw_degrees = apiScene.ar_yaw_degrees,
            };
            savedPlacement = currentPlacement.Copy();
            teacherEditor?.SetPlacement(currentPlacement);
            SceneContent apiContent = CreateContentFromApi(apiScene);
            ApplyContent(apiContent);
            experienceUI?.SetCompletionAvailable(!teacherPreviewRequest && StudentAppSession.HasToken);
            if (!teacherPreviewRequest && StudentAppSession.HasToken)
                StartCoroutine(SaveQrProgress(apiScene.qr_code, "open"));

            if (loadGlbModelFromApi)
                yield return LoadGltfModelFromUrl(apiContent);

            if (addTrackingImageFromApi)
            {
                experienceUI?.SetStatus("Preparando el reconocimiento del QR…", ARSceneExperienceUI.MessageTone.Info, false);
                yield return AddTrackingImageFromApi(apiScene);
            }

            if (modelSetupFailed)
            {
                experienceUI?.SetStatus("El capítulo cargó, pero falló el modelo 3D. Revisa la conexión e inténtalo de nuevo.", ARSceneExperienceUI.MessageTone.Warning, true);
            }
            else if (!trackingImageSetupFailed)
            {
                if (hasPlaceableModel)
                    SetTrackingHint();
                else
                    experienceUI?.SetStatus("Capítulo listo para leer, pero no tiene un modelo 3D.", ARSceneExperienceUI.MessageTone.Warning, false);
            }
            if (!TeacherPreviewSession.IsActive && (modelSetupFailed || trackingImageSetupFailed || !hasPlaceableModel))
                experienceUI?.SetReadingExpanded(true);
        }
        finally
        {
            request.Dispose();
            isLoadingContent = false;
        }
    }

    private GameObject CreatePresentationRoot(string name)
    {
        GameObject root = new GameObject(name);
        root.SetActive(false);
        GameObject visual = new GameObject("Visual");
        visual.transform.SetParent(root.transform, false);
        return root;
    }

    private void FinishPresentationRoot(GameObject root)
    {
        Transform visual = root.transform.Find("Visual");
        Renderer[] renderers = visual.GetComponentsInChildren<Renderer>(true);
        Bounds combined = new Bounds();
        bool hasBounds = false;
        foreach (Renderer renderer in renderers)
        {
            Bounds meshBounds;
            if (renderer is SkinnedMeshRenderer skinned)
                meshBounds = skinned.localBounds;
            else
            {
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null)
                    continue;
                meshBounds = filter.sharedMesh.bounds;
            }

            Matrix4x4 matrix = root.transform.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
            Vector3 min = meshBounds.min;
            Vector3 max = meshBounds.max;
            for (int x = 0; x < 2; x++)
                for (int y = 0; y < 2; y++)
                    for (int z = 0; z < 2; z++)
                    {
                        Vector3 point = matrix.MultiplyPoint3x4(new Vector3(
                            x == 0 ? min.x : max.x, y == 0 ? min.y : max.y, z == 0 ? min.z : max.z));
                        if (!hasBounds)
                        {
                            combined = new Bounds(point, Vector3.zero);
                            hasBounds = true;
                        }
                        else
                            combined.Encapsulate(point);
                    }
        }

        if (!hasBounds)
        {
            activePresentationMaxDimension = 1f;
            Debug.LogWarning("ARSceneController: modelo sin geometría medible; se usará escala predeterminada.");
        }
        else
        {
            activePresentationMaxDimension = Mathf.Max(combined.size.x, combined.size.y, combined.size.z, 0.0001f);
            visual.localPosition = new Vector3(-combined.center.x, -combined.min.y, -combined.center.z);
            BoxCollider collider = root.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, combined.size.y * 0.5f, 0f);
            collider.size = new Vector3(Mathf.Max(combined.size.x, 0.01f),
                Mathf.Max(combined.size.y, 0.01f), Mathf.Max(combined.size.z, 0.01f));
        }
        root.AddComponent<ARStoryInteraction>();
        root.transform.localScale = Vector3.one * GetPresentationScale();
    }

    private float GetPresentationScale()
    {
        float desiredMeters = (currentPlacement != null && currentPlacement.ar_model_size_cm > 0f
            ? currentPlacement.ar_model_size_cm : 8f) * 0.01f;
        return desiredMeters / Mathf.Max(activePresentationMaxDimension, 0.0001f);
    }

    private void ReplacePresentation(SceneContent content, GameObject root)
    {
        GameObject previous = activePresentationRoot;
        activePresentationRoot = root;
        ApplyPrefabToPlacers(content, root, true);
        ApplyCurrentPlacement();
        if (previous != null)
            Destroy(previous);
    }

    private void ApplyCurrentPlacement()
    {
        if (currentPlacement == null || activePresentationRoot == null)
            return;
        float scale = GetPresentationScale();
        QRTrackedImagePlacer imagePlacer = FindAnyObjectByType<QRTrackedImagePlacer>();
        if (imagePlacer != null)
            imagePlacer.UpdatePlacement(
                new Vector3(currentPlacement.ar_offset_x_cm, currentPlacement.ar_offset_y_cm,
                    currentPlacement.ar_offset_z_cm) * 0.01f,
                new Vector3(0f, currentPlacement.ar_yaw_degrees, 0f), scale);
        else
            activePresentationRoot.transform.localScale = Vector3.one * scale;
    }

    private void AdjustTeacherPlacement(int field, float step)
    {
        if (currentPlacement == null)
            return;
        switch (field)
        {
            case 0:
                currentPlacement.ar_marker_width_cm = Mathf.Clamp(currentPlacement.ar_marker_width_cm + step, 2f, 30f);
                teacherEditor?.SetMessage("El ancho del marcador se aplica después de guardar y volver a escanear.");
                break;
            case 1:
                currentPlacement.ar_model_size_cm = Mathf.Clamp(currentPlacement.ar_model_size_cm + step, 1f, 50f);
                break;
            case 2:
                currentPlacement.ar_offset_x_cm = Mathf.Clamp(currentPlacement.ar_offset_x_cm + step, -50f, 50f);
                break;
            case 3:
                currentPlacement.ar_offset_y_cm = Mathf.Clamp(currentPlacement.ar_offset_y_cm + step, -50f, 50f);
                break;
            case 4:
                currentPlacement.ar_offset_z_cm = Mathf.Clamp(currentPlacement.ar_offset_z_cm + step, -50f, 50f);
                break;
            case 5:
                currentPlacement.ar_yaw_degrees = Mathf.Clamp(currentPlacement.ar_yaw_degrees + step, -180f, 180f);
                break;
        }
        ApplyCurrentPlacement();
        teacherEditor?.SetPlacement(currentPlacement);
    }

    private void ResetTeacherPlacement()
    {
        if (savedPlacement == null)
            return;
        currentPlacement = savedPlacement.Copy();
        ApplyCurrentPlacement();
        teacherEditor?.SetPlacement(currentPlacement);
        teacherEditor?.SetMessage("Cambios sin guardar descartados.");
    }

    private void SaveTeacherPlacement()
    {
        if (!isSavingPlacement && TeacherPreviewSession.IsActive && currentPlacement != null)
            StartCoroutine(SaveTeacherPlacementRequest());
    }

    private IEnumerator SaveTeacherPlacementRequest()
    {
        isSavingPlacement = true;
        teacherEditor?.SetMessage("Guardando ajuste del capítulo…");
        ChapterArPlacement submitted = currentPlacement.Copy();
        using (UnityWebRequest request = TeacherPreviewSession.SavePlacement(scannedQrCode, submitted))
        {
            yield return request.SendWebRequest();
            isSavingPlacement = false;
            if (request.result != UnityWebRequest.Result.Success)
            {
                if (request.responseCode == 401)
                {
                    TeacherPreviewSession.Clear();
                    teacherEditor?.SetMessage("Tu vista docente caducó. Vuelve al inicio para entrar otra vez.");
                }
                else
                    teacherEditor?.SetMessage("No se guardó. Comprueba internet y reintenta.");
                yield break;
            }
            savedPlacement = submitted;
            teacherEditor?.SetMessage("Guardado. Si cambiaste el marcador, escanea el QR de nuevo.");
        }
    }

    private SceneContent CreateContentFromApi(UnitySceneApiResponse apiScene)
    {
        GameObject prefab = null;
        if (!string.IsNullOrWhiteSpace(apiScene.prefab_key))
            prefabByKey.TryGetValue(apiScene.prefab_key, out prefab);

        string title = !string.IsNullOrWhiteSpace(apiScene.title)
            ? apiScene.title
            : apiScene.book_title + " - Escena " + apiScene.order;

        return new SceneContent
        {
            qrCode = apiScene.qr_code,
            title = title,
            narration = apiScene.text,
            prefabKey = apiScene.prefab_key,
            prefab = prefab,
            audioUrl = apiScene.audio_url,
            modelUrl = apiScene.glb_model_url
        };
    }

    private IEnumerator LoadGltfModelFromUrl(SceneContent content)
    {
        if (content == null || string.IsNullOrWhiteSpace(content.modelUrl))
        {
            Debug.Log("ARSceneController: API no envio glb_model_url, usando prefab local/fallback");
            yield break;
        }

        Debug.Log("ARSceneController: cargando GLB runtime: " + content.modelUrl);
        experienceUI?.SetStatus("Descargando el modelo 3D…", ARSceneExperienceUI.MessageTone.Info, false);

        GltfImport gltfImport = new GltfImport();
        Task<bool> loadTask = gltfImport.Load(content.modelUrl);
        while (!loadTask.IsCompleted)
            yield return null;

        if (loadTask.IsFaulted || !loadTask.Result)
        {
            Debug.LogWarning("ARSceneController: no se pudo cargar GLB runtime: " + GetTaskError(loadTask));
            modelSetupFailed = true;
            experienceUI?.SetStatus("El capítulo cargó, pero falló el modelo 3D. Revisa la conexión e inténtalo de nuevo.", ARSceneExperienceUI.MessageTone.Warning, true);
            yield break;
        }

        GameObject modelRoot = CreatePresentationRoot("RuntimeGLB_" + SanitizeName(content.qrCode));
        Transform visual = modelRoot.transform.Find("Visual");

        Task<bool> instantiateTask = gltfImport.InstantiateMainSceneAsync(visual);
        while (!instantiateTask.IsCompleted)
            yield return null;

        if (instantiateTask.IsFaulted || !instantiateTask.Result)
        {
            Debug.LogWarning("ARSceneController: no se pudo instanciar GLB runtime: " + GetTaskError(instantiateTask));
            Destroy(modelRoot);
            modelSetupFailed = true;
            experienceUI?.SetStatus("No se pudo preparar el modelo 3D. Puedes volver a intentarlo.", ARSceneExperienceUI.MessageTone.Warning, true);
            yield break;
        }

        FinishPresentationRoot(modelRoot);
        ReplacePresentation(content, modelRoot);
        Debug.Log("ARSceneController: GLB runtime listo para QR: " + content.qrCode);
    }

    private IEnumerator LoadAudioFromUrl(string audioUrl, int version)
    {
        Debug.Log("ARSceneController: descargando audio: " + audioUrl);

        using (UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip(audioUrl, AudioType.UNKNOWN))
        {
            request.timeout = Mathf.Max(1, Mathf.RoundToInt(apiTimeoutSeconds));
            yield return request.SendWebRequest();

            if (version != contentVersion)
                yield break;

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning("ARSceneController: no se pudo descargar audio: " + request.error);
                experienceUI?.SetAudioAvailable(false);
                ActivateSpeech(selectedContent?.narration);
                yield break;
            }

            AudioClip clip = DownloadHandlerAudioClip.GetContent(request);
            if (clip == null)
            {
                Debug.LogWarning("ARSceneController: audio descargado invalido");
                experienceUI?.SetAudioAvailable(false);
                ActivateSpeech(selectedContent?.narration);
                yield break;
            }

            audioSource.clip = clip;
            experienceUI?.SetAudioAvailable(true);
        }
    }

    private IEnumerator AddTrackingImageFromApi(UnitySceneApiResponse apiScene)
    {
        if (apiScene == null ||
            string.IsNullOrWhiteSpace(apiScene.qr_code) ||
            string.IsNullOrWhiteSpace(apiScene.qr_image_url))
        {
            Debug.Log("ARSceneController: API no envio qr_image_url para tracking runtime");
            ARTrackedImageManager existingImageManager = FindAnyObjectByType<ARTrackedImageManager>();
            bool referenceImageAvailable = existingImageManager != null && apiScene != null &&
                !string.IsNullOrWhiteSpace(apiScene.qr_code) &&
                ReferenceLibraryContainsName(existingImageManager.referenceLibrary, apiScene.qr_code);
            if (!referenceImageAvailable)
            {
                trackingImageSetupFailed = true;
                experienceUI?.SetStatus("No hay una imagen de este QR preparada para AR. Pide al docente que revise el capítulo.", ARSceneExperienceUI.MessageTone.Warning, false);
            }
            yield break;
        }

        ARTrackedImageManager imageManager = FindAnyObjectByType<ARTrackedImageManager>();
        if (imageManager == null)
        {
            Debug.LogWarning("ARSceneController: no se encontro ARTrackedImageManager para agregar QR runtime");
            trackingImageSetupFailed = true;
            experienceUI?.SetStatus("No se pudo iniciar el seguimiento AR. Vuelve al escáner e inténtalo otra vez.", ARSceneExperienceUI.MessageTone.Warning, false);
            yield break;
        }

        if (ReferenceLibraryContainsName(imageManager.referenceLibrary, apiScene.qr_code))
        {
            Debug.Log("ARSceneController: QR ya existe en reference library: " + apiScene.qr_code);
            yield break;
        }

        float waitStartedAt = Time.realtimeSinceStartup;
        while (ARSession.state < ARSessionState.Ready)
        {
            if (Time.realtimeSinceStartup - waitStartedAt > Mathf.Min(apiTimeoutSeconds, 15f))
            {
                Debug.LogWarning("ARSceneController: ARSession no llego a Ready para agregar QR runtime, state=" + ARSession.state);
                trackingImageSetupFailed = true;
                experienceUI?.SetStatus("La cámara AR todavía no está lista. Muévela despacio y vuelve a intentarlo.", ARSceneExperienceUI.MessageTone.Warning, true);
                yield break;
            }

            yield return null;
        }

        Debug.Log("ARSceneController: descargando imagen QR runtime: " + apiScene.qr_image_url);

        using (UnityWebRequest imageRequest = UnityWebRequestTexture.GetTexture(apiScene.qr_image_url))
        {
            imageRequest.timeout = Mathf.Max(1, Mathf.RoundToInt(apiTimeoutSeconds));
            yield return imageRequest.SendWebRequest();

            if (imageRequest.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning(
                    "ARSceneController: no se pudo descargar imagen QR runtime result=" +
                    imageRequest.result +
                    " code=" +
                    imageRequest.responseCode +
                    " error=" +
                    imageRequest.error
                );
                trackingImageSetupFailed = true;
                experienceUI?.SetStatus("No se pudo preparar el QR para AR. Revisa tu conexión e inténtalo de nuevo.", ARSceneExperienceUI.MessageTone.Warning, true);
                yield break;
            }

            Texture2D qrTexture = DownloadHandlerTexture.GetContent(imageRequest);
            if (qrTexture == null)
            {
                Debug.LogWarning("ARSceneController: imagen QR runtime descargada invalida");
                trackingImageSetupFailed = true;
                experienceUI?.SetStatus("La imagen del QR no es válida para el seguimiento AR.", ARSceneExperienceUI.MessageTone.Warning, false);
                yield break;
            }

            MutableRuntimeReferenceImageLibrary mutableLibrary = GetMutableRuntimeReferenceImageLibrary(imageManager);
            if (mutableLibrary == null)
            {
                Destroy(qrTexture);
                trackingImageSetupFailed = true;
                experienceUI?.SetStatus("Este dispositivo no pudo preparar el QR para seguimiento AR.", ARSceneExperienceUI.MessageTone.Warning, false);
                yield break;
            }

            AddReferenceImageJobState addImageJob;
            try
            {
                addImageJob = mutableLibrary.ScheduleAddImageWithValidationJob(
                    qrTexture,
                    apiScene.qr_code,
                    apiScene.ar_marker_width_cm > 0f
                        ? apiScene.ar_marker_width_cm * 0.01f : trackingImagePhysicalWidthMeters
                );
            }
            catch (Exception exception)
            {
                Debug.LogWarning("ARSceneController: no se pudo programar QR runtime: " + exception);
                Destroy(qrTexture);
                trackingImageSetupFailed = true;
                experienceUI?.SetStatus("No se pudo preparar el QR para AR. Puedes volver al escáner.", ARSceneExperienceUI.MessageTone.Warning, false);
                yield break;
            }

            while (addImageJob.status.IsPending())
                yield return null;

            Debug.Log(
                "ARSceneController: resultado agregar QR runtime codigo=" +
                apiScene.qr_code +
                " status=" +
                addImageJob.status
            );

            if (addImageJob.status != AddReferenceImageJobStatus.Success)
            {
                trackingImageSetupFailed = true;
                experienceUI?.SetStatus("ARCore no pudo reconocer la imagen del QR. Prueba con buena luz y una impresión nítida.", ARSceneExperienceUI.MessageTone.Warning, false);
            }

            Destroy(qrTexture);
        }
    }

    private MutableRuntimeReferenceImageLibrary GetMutableRuntimeReferenceImageLibrary(ARTrackedImageManager imageManager)
    {
        if (imageManager.referenceLibrary is MutableRuntimeReferenceImageLibrary mutableLibrary)
            return mutableLibrary;

        if (imageManager.referenceLibrary is XRReferenceImageLibrary serializedLibrary)
        {
            RuntimeReferenceImageLibrary runtimeLibrary = imageManager.CreateRuntimeLibrary(serializedLibrary);
            imageManager.referenceLibrary = runtimeLibrary;

            if (runtimeLibrary is MutableRuntimeReferenceImageLibrary createdMutableLibrary)
                return createdMutableLibrary;
        }

        Debug.LogWarning(
            "ARSceneController: reference library no es mutable en runtime: " +
            (imageManager.referenceLibrary == null ? "null" : imageManager.referenceLibrary.GetType().Name)
        );
        return null;
    }

    private bool ReferenceLibraryContainsName(IReferenceImageLibrary library, string imageName)
    {
        if (library == null || string.IsNullOrWhiteSpace(imageName))
            return false;

        int count = library.count;
        for (int i = 0; i < count; i++)
        {
            if (library[i].name == imageName)
                return true;
        }

        return false;
    }

    private bool TryApplyLocalContent(string qrCode)
    {
        if (!contentByCode.TryGetValue(qrCode, out selectedContent))
            return false;

        ApplyContent(selectedContent);
        return true;
    }

    private bool TryShowLocalFallback(string qrCode, string reason)
    {
        if (!fallbackToLocalContent || !TryApplyLocalContent(qrCode))
            return false;

        experienceUI?.SetStatus(reason + " Se muestra una copia de demostración incluida en la app.", ARSceneExperienceUI.MessageTone.Warning, true);
        return true;
    }

    private void ShowUnavailableContent(string message, bool canRetry)
    {
        ApplyContent(CreateUnknownContent(scannedQrCode));
        experienceUI?.SetStatus(message ?? "No encontramos este contenido. Comprueba que el libro esté publicado.", ARSceneExperienceUI.MessageTone.Error, canRetry);
        experienceUI?.SetReadingExpanded(true);
    }

    private void SetTrackingHint()
    {
        if (!hasPlaceableModel)
        {
            experienceUI?.SetStatus("Capítulo listo para leer. Este contenido no incluye modelo 3D.", ARSceneExperienceUI.MessageTone.Warning, false);
            return;
        }

        ARTrackedImageManager imageManager = FindAnyObjectByType<ARTrackedImageManager>();
        if (imageManager != null && !string.IsNullOrWhiteSpace(scannedQrCode))
        {
            foreach (ARTrackedImage trackedImage in imageManager.trackables)
            {
                if (trackedImage.referenceImage.name == scannedQrCode)
                {
                    experienceUI?.HandleTrackingState(trackedImage.trackingState == TrackingState.Tracking);
                    return;
                }
            }
        }

        experienceUI?.SetStatus(
            "Apunta al mismo QR impreso para ver el modelo. Muévete despacio y mejora la luz si no aparece.",
            ARSceneExperienceUI.MessageTone.Info,
            false);
    }

    private void OnTargetTrackingChanged(bool isTracking)
    {
        experienceUI?.HandleTrackingState(isTracking);
        if (!isTracking && !TeacherPreviewSession.IsActive)
            experienceUI?.SetReadingExpanded(true);
    }

    private string BuildUnitySceneUrl(string qrCode)
    {
        string baseUrl = string.IsNullOrWhiteSpace(apiBaseUrl)
            ? "https://bibliotecaar-backend.onrender.com/api"
            : apiBaseUrl.TrimEnd('/');

        return baseUrl + "/unity/scenes/" + UnityWebRequest.EscapeURL(qrCode) + "/";
    }

    private void EnsureDefaultContents()
    {
        if (contents.Count > 0)
            return;

        contents.Add(new SceneContent
        {
            qrCode = "libro_001_escena_001",
            title = "Bosque",
            narration = "Caperucita caminaba por el bosque mientras escuchaba ruidos extranos."
        });

        contents.Add(new SceneContent
        {
            qrCode = "libro_001_escena_002",
            title = "Castillo",
            narration = "A lo lejos aparecio un castillo antiguo entre la neblina."
        });

        contents.Add(new SceneContent
        {
            qrCode = "libro_001_escena_003",
            title = "Casa",
            narration = "La pequena casa brillaba entre los arboles al final del camino."
        });

        contents.Add(new SceneContent
        {
            qrCode = "libro_002_escena_001",
            title = "Dragon",
            narration = "El dragon desperto sobre la montana y extendio sus alas."
        });

        contents.Add(new SceneContent
        {
            qrCode = "libro_002_escena_002",
            title = "Tesoro",
            narration = "El cofre escondia un secreto dorado que nadie habia visto."
        });

        contents.Add(new SceneContent
        {
            qrCode = "libro_003_escena_001",
            title = "Nave",
            narration = "La nave aterrizo bajo un cielo extrano lleno de luces."
        });
    }

    private SceneContent CreateUnknownContent(string qrCode)
    {
        return new SceneContent
        {
            qrCode = qrCode,
            title = "Contenido no encontrado",
            narration = "Comprueba que el QR sea correcto y que el libro esté publicado. Puedes volver al escáner e intentarlo otra vez."
        };
    }
}
