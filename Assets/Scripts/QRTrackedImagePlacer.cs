using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public class QRTrackedImagePlacer : MonoBehaviour
{
    public GameObject objectToPlace;
    public Vector3 localOffset = new Vector3(0f, 0.05f, 0f);
    public Vector3 localEulerAngles = Vector3.zero;
    public bool hideWhenNotTracking = true;
    public bool onlyShowScannedCode = true;
    public string targetQrCode;

    private ARTrackedImageManager trackedImageManager;
    private readonly Dictionary<TrackableId, GameObject> spawnedObjects = new Dictionary<TrackableId, GameObject>();
    private readonly Dictionary<string, GameObject> spawnedObjectsByCode = new Dictionary<string, GameObject>();

    void Awake()
    {
        trackedImageManager = GetComponent<ARTrackedImageManager>();

        if (trackedImageManager == null)
            Debug.LogError("QRTrackedImagePlacer: No se encontro ARTrackedImageManager en este GameObject");
        else
            Debug.Log("QRTrackedImagePlacer: ARTrackedImageManager encontrado");

        if (objectToPlace == null)
            Debug.LogWarning("QRTrackedImagePlacer: objectToPlace esta vacio");
    }

    void OnEnable()
    {
        if (trackedImageManager != null)
            trackedImageManager.trackablesChanged.AddListener(OnTrackedImagesChanged);
    }

    void OnDisable()
    {
        if (trackedImageManager != null)
            trackedImageManager.trackablesChanged.RemoveListener(OnTrackedImagesChanged);
    }

    private void OnTrackedImagesChanged(ARTrackablesChangedEventArgs<ARTrackedImage> args)
    {
        foreach (ARTrackedImage trackedImage in args.added)
            UpdateTrackedImage(trackedImage, "agregado");

        foreach (ARTrackedImage trackedImage in args.updated)
            UpdateTrackedImage(trackedImage, "actualizado");

        foreach (KeyValuePair<TrackableId, ARTrackedImage> removed in args.removed)
        {
            if (spawnedObjects.TryGetValue(removed.Key, out GameObject spawnedObject))
            {
                string qrCode = removed.Value != null ? removed.Value.referenceImage.name : null;
                if (!string.IsNullOrWhiteSpace(qrCode) &&
                    spawnedObjectsByCode.TryGetValue(qrCode, out GameObject objectByCode) &&
                    objectByCode == spawnedObject)
                {
                    spawnedObjectsByCode.Remove(qrCode);
                }

                Destroy(spawnedObject);
                spawnedObjects.Remove(removed.Key);
            }

            Debug.Log("QRTrackedImagePlacer: QR removido id=" + removed.Key);
        }
    }

    private void UpdateTrackedImage(ARTrackedImage trackedImage, string action)
    {
        string qrCode = trackedImage.referenceImage.name;
        string requiredCode = GetRequiredCode();

        Debug.Log(
            "QRTrackedImagePlacer: QR " + action +
            " codigo=" + qrCode +
            ", requerido=" + (string.IsNullOrWhiteSpace(requiredCode) ? "cualquiera" : requiredCode) +
            ", tracking=" + trackedImage.trackingState +
            ", size=" + trackedImage.size
        );

        if (!string.IsNullOrWhiteSpace(requiredCode) && qrCode != requiredCode)
        {
            HideExistingObject(trackedImage.trackableId, qrCode);
            Debug.Log("QRTrackedImagePlacer: ignorando QR porque no coincide con el escaneado");
            return;
        }

        if (objectToPlace == null)
            return;

        if (spawnedObjectsByCode.TryGetValue(qrCode, out GameObject existingObject))
        {
            spawnedObjects[trackedImage.trackableId] = existingObject;
            UpdateObjectTransform(existingObject, trackedImage);
            return;
        }

        if (!spawnedObjects.TryGetValue(trackedImage.trackableId, out GameObject spawnedObject))
        {
            spawnedObject = Instantiate(objectToPlace, trackedImage.transform);
            spawnedObject.name = "ARContent_" + qrCode;
            spawnedObjects[trackedImage.trackableId] = spawnedObject;
            spawnedObjectsByCode[qrCode] = spawnedObject;
        }

        UpdateObjectTransform(spawnedObject, trackedImage);
    }

    private void UpdateObjectTransform(GameObject spawnedObject, ARTrackedImage trackedImage)
    {
        spawnedObject.transform.SetParent(trackedImage.transform, false);
        spawnedObject.transform.localPosition = localOffset;
        spawnedObject.transform.localRotation = Quaternion.Euler(localEulerAngles);
        
        bool shouldShow = !hideWhenNotTracking || trackedImage.trackingState == TrackingState.Tracking;
        spawnedObject.SetActive(shouldShow);
    }

    private string GetRequiredCode()
    {
        if (!onlyShowScannedCode)
            return null;

        if (!string.IsNullOrWhiteSpace(targetQrCode))
            return targetQrCode;

        return ScannedQRData.LastCode;
    }

    private void HideExistingObject(TrackableId trackableId, string qrCode)
    {
        if (spawnedObjects.TryGetValue(trackableId, out GameObject objectById))
            objectById.SetActive(false);

        if (!string.IsNullOrWhiteSpace(qrCode) &&
            spawnedObjectsByCode.TryGetValue(qrCode, out GameObject objectByCode))
        {
            objectByCode.SetActive(false);
        }
    }
}
