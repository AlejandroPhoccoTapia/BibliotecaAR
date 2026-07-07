using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

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
        public string qr_image_url;
    }

    [Header("Content")]
    public List<SceneContent> contents = new List<SceneContent>();
    public List<PrefabBinding> prefabBindings = new List<PrefabBinding>();
    public GameObject fallbackPrefab;

    [Header("API")]
    public bool loadContentFromApi = true;
    public string apiBaseUrl = "http://192.168.1.48:8000/api";
    public float apiTimeoutSeconds = 10f;
    public bool fallbackToLocalContent = true;

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

    IEnumerator Start()
    {
        BuildContentDictionary();

        string qrCode = ScannedQRData.LastCode;
        if (string.IsNullOrWhiteSpace(qrCode))
        {
            Debug.LogWarning("ARSceneController: no hay codigo QR recibido");
            ApplyContent(CreateUnknownContent("sin_codigo"));
            yield break;
        }

        Debug.Log("ARSceneController: codigo recibido en ARScene: " + qrCode);

        if (loadContentFromApi)
        {
            yield return LoadContentFromApi(qrCode);
            yield break;
        }

        ApplyLocalContent(qrCode);
    }

    public void PlayAudio()
    {
        if (audioSource == null)
        {
            Debug.LogWarning("ARSceneController: no hay AudioSource asignado");
            return;
        }

        if (audioSource.clip == null)
        {
            Debug.LogWarning("ARSceneController: no hay audio asignado para este QR");
            return;
        }

        audioSource.Play();
        Debug.Log("ARSceneController: reproduciendo audio");
    }

    public void PauseAudio()
    {
        if (audioSource == null)
            return;

        audioSource.Pause();
        Debug.Log("ARSceneController: audio pausado");
    }

    public void BackToScanner()
    {
        Debug.Log("ARSceneController: volviendo a escaner QR");
        SceneManager.LoadScene(qrScanSceneName);
    }

    private void ApplyContent(SceneContent content)
    {
        selectedContent = content;

        Debug.Log("ARSceneController: escena seleccionada: " + content.title);
        Debug.Log("ARSceneController: texto narrativo: " + content.narration);

        if (titleText != null)
            titleText.text = content.title;

        if (narrationText != null)
            narrationText.text = content.narration;

        if (audioSource != null)
        {
            audioSource.clip = content.audioClip;

            if (content.audioClip != null)
                audioSource.Play();
            else if (!string.IsNullOrWhiteSpace(content.audioUrl))
                StartCoroutine(LoadAudioFromUrl(content.audioUrl));
        }

        ARRaycastPlaceObject placer = FindAnyObjectByType<ARRaycastPlaceObject>();
        GameObject prefabToPlace = content.prefab != null
            ? content.prefab
            : fallbackPrefab;

        if (prefabToPlace == null && placer != null)
            prefabToPlace = placer.objectToPlace;

        if (prefabToPlace == null)
        {
            Debug.LogWarning("ARSceneController: no hay prefab para colocar en AR");
            return;
        }

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
            imagePlacer.objectToPlace = prefabToPlace;
            imagePlacer.targetQrCode = content.qrCode;
            imagePlacer.onlyShowScannedCode = true;
            Debug.Log("ARSceneController: prefab AR para QR seleccionado: " + prefabToPlace.name);
        }
        else
        {
            Debug.LogWarning("ARSceneController: no se encontro QRTrackedImagePlacer");
        }
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
        string url = BuildUnitySceneUrl(qrCode);
        Debug.Log("ARSceneController: consultando API: " + url);

        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            request.timeout = Mathf.Max(1, Mathf.RoundToInt(apiTimeoutSeconds));
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning(
                    "ARSceneController: error consultando API para QR " + qrCode +
                    " result=" + request.result +
                    " code=" + request.responseCode +
                    " error=" + request.error
                );

                if (fallbackToLocalContent)
                    ApplyLocalContent(qrCode);
                else
                    ApplyContent(CreateUnknownContent(qrCode));

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

                if (fallbackToLocalContent)
                    ApplyLocalContent(qrCode);
                else
                    ApplyContent(CreateUnknownContent(qrCode));

                yield break;
            }

            SceneContent apiContent = CreateContentFromApi(apiScene);
            ApplyContent(apiContent);
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
            audioUrl = apiScene.audio_url
        };
    }

    private IEnumerator LoadAudioFromUrl(string audioUrl)
    {
        Debug.Log("ARSceneController: descargando audio: " + audioUrl);

        using (UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip(audioUrl, AudioType.UNKNOWN))
        {
            request.timeout = Mathf.Max(1, Mathf.RoundToInt(apiTimeoutSeconds));
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning("ARSceneController: no se pudo descargar audio: " + request.error);
                yield break;
            }

            AudioClip clip = DownloadHandlerAudioClip.GetContent(request);
            if (clip == null)
            {
                Debug.LogWarning("ARSceneController: audio descargado invalido");
                yield break;
            }

            audioSource.clip = clip;
            audioSource.Play();
        }
    }

    private void ApplyLocalContent(string qrCode)
    {
        if (!contentByCode.TryGetValue(qrCode, out selectedContent))
        {
            Debug.LogWarning("ARSceneController: no existe contenido local para QR: " + qrCode);
            selectedContent = CreateUnknownContent(qrCode);
        }

        ApplyContent(selectedContent);
    }

    private string BuildUnitySceneUrl(string qrCode)
    {
        string baseUrl = string.IsNullOrWhiteSpace(apiBaseUrl)
            ? "http://192.168.1.48:8000/api"
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
            narration = "No hay datos locales para el codigo QR: " + qrCode
        };
    }
}
