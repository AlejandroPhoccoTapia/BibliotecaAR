using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
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
        public GameObject prefab;
        public AudioClip audioClip;
    }

    [Header("Content")]
    public List<SceneContent> contents = new List<SceneContent>();
    public GameObject fallbackPrefab;

    [Header("Optional UI")]
    public TMP_Text titleText;
    public TMP_Text narrationText;

    [Header("Optional Audio")]
    public AudioSource audioSource;

    [Header("Scene Flow")]
    public string qrScanSceneName = "QRScanScene";

    private SceneContent selectedContent;
    private Dictionary<string, SceneContent> contentByCode;

    void Start()
    {
        BuildContentDictionary();

        string qrCode = ScannedQRData.LastCode;
        if (string.IsNullOrWhiteSpace(qrCode))
        {
            Debug.LogWarning("ARSceneController: no hay codigo QR recibido");
            ApplyContent(CreateUnknownContent("sin_codigo"));
            return;
        }

        Debug.Log("ARSceneController: codigo recibido en ARScene: " + qrCode);

        if (!contentByCode.TryGetValue(qrCode, out selectedContent))
        {
            Debug.LogWarning("ARSceneController: no existe contenido local para QR: " + qrCode);
            selectedContent = CreateUnknownContent(qrCode);
        }

        ApplyContent(selectedContent);
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

        foreach (SceneContent content in contents)
        {
            if (content == null || string.IsNullOrWhiteSpace(content.qrCode))
                continue;

            contentByCode[content.qrCode] = content;
        }
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
