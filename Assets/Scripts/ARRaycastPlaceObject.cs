using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

public class ARRaycastPlaceObject : MonoBehaviour
{
    public GameObject objectToPlace;

    private ARRaycastManager raycastManager;
    private ARPlaneManager planeManager;
    private GameObject spawnedObject;
    private float nextInputStatusLogTime;
    private bool enhancedTouchEnabled;

    private static List<ARRaycastHit> hits = new List<ARRaycastHit>();
    private static List<ARRaycastHit> debugHits = new List<ARRaycastHit>();

    void Awake()
    {
        Debug.Log("ARRaycastPlaceObject: Awake ejecutado");

        raycastManager = GetComponent<ARRaycastManager>();
        planeManager = GetComponent<ARPlaneManager>();

        if (raycastManager == null)
            Debug.LogError("No se encontro ARRaycastManager en este GameObject");
        else
            Debug.Log("ARRaycastManager encontrado correctamente");

        if (planeManager == null)
            Debug.LogError("No se encontro ARPlaneManager en este GameObject");
        else
            Debug.Log("ARPlaneManager encontrado correctamente");

        if (objectToPlace == null)
            Debug.LogError("objectToPlace esta vacio. Arrastra el prefab en el Inspector");
        else
            Debug.Log("Prefab asignado: " + objectToPlace.name);
    }

    void OnEnable()
    {
        EnhancedTouchSupport.Enable();
        enhancedTouchEnabled = true;
        Debug.Log("ARRaycastPlaceObject: EnhancedTouch habilitado");
    }

    void OnDisable()
    {
        EnhancedTouchSupport.Disable();
        enhancedTouchEnabled = false;
    }

    void Update()
    {
        LogInputStatus();

        Vector2 touchPosition;
        if (!TryGetTouchPosition(out touchPosition))
            return;

        Debug.Log("Toque detectado con New Input System");
        Debug.Log("Posicion del toque: " + touchPosition);
        Debug.Log("Estado ARSession: " + ARSession.state + " | razonNoTracking: " + ARSession.notTrackingReason);

        if (planeManager != null)
        {
            int planeCount = 0;
            foreach (ARPlane ignored in planeManager.trackables)
                planeCount++;

            Debug.Log(
                "ARPlaneManager enabled: " + planeManager.enabled +
                " | planos trackeados: " + planeCount +
                " | requestedDetection: " + planeManager.requestedDetectionMode +
                " | currentDetection: " + planeManager.currentDetectionMode
            );
        }

        if (raycastManager == null)
        {
            Debug.LogError("No se puede hacer Raycast porque raycastManager es null");
            return;
        }

        TrackableType detectedType;
        bool detectedPlane = TryGetPlacementHit(touchPosition, out Pose hitPose, out detectedType);

        Debug.Log("Resultado del Raycast: " + detectedPlane);
        Debug.Log("Cantidad de hits: " + hits.Count);

        if (!detectedPlane)
        {
            Debug.Log("No se detecto plano, plano estimado ni feature point en ese toque");
            return;
        }

        Debug.Log("Si detecta superficie con: " + detectedType);

        if (objectToPlace == null)
        {
            Debug.LogError("No se puede instanciar porque objectToPlace esta vacio");
            return;
        }

        if (spawnedObject == null)
        {
            Debug.Log("Creando objeto por primera vez");
            spawnedObject = Instantiate(objectToPlace, hitPose.position, hitPose.rotation);
        }
        else
        {
            Debug.Log("Moviendo objeto existente");
            spawnedObject.transform.position = hitPose.position;
            spawnedObject.transform.rotation = hitPose.rotation;
        }
    }

    private void LogInputStatus()
    {
        if (Time.time < nextInputStatusLogTime)
            return;

        nextInputStatusLogTime = Time.time + 2f;

        Debug.Log(
            "Input debug: Touchscreen.current=" + (Touchscreen.current != null) +
            " | enhancedTouchEnabled=" + enhancedTouchEnabled +
            " | activeTouches=" + Touch.activeTouches.Count
        );
    }

    private bool TryGetTouchPosition(out Vector2 touchPosition)
    {
        foreach (Touch touch in Touch.activeTouches)
        {
            if (touch.phase == UnityEngine.InputSystem.TouchPhase.Began)
            {
                touchPosition = touch.screenPosition;
                Debug.Log("Toque detectado con EnhancedTouch");
                return true;
            }
        }

        if (Touchscreen.current != null &&
            Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
        {
            touchPosition = Touchscreen.current.primaryTouch.position.ReadValue();
            Debug.Log("Toque detectado con Touchscreen.current");
            return true;
        }

        touchPosition = default;
        return false;
    }

    private bool TryGetPlacementHit(Vector2 touchPosition, out Pose hitPose, out TrackableType detectedType)
    {
        if (TryRaycast(touchPosition, TrackableType.PlaneWithinPolygon, out hitPose))
        {
            detectedType = TrackableType.PlaneWithinPolygon;
            return true;
        }

        if (TryRaycast(touchPosition, TrackableType.PlaneEstimated, out hitPose))
        {
            detectedType = TrackableType.PlaneEstimated;
            Debug.LogWarning("Colocando con PlaneEstimated. ARCore todavia no tiene un plano completo.");
            return true;
        }

        if (TryRaycast(touchPosition, TrackableType.FeaturePoint, out hitPose))
        {
            detectedType = TrackableType.FeaturePoint;
            Debug.LogWarning("Colocando con FeaturePoint. Hay tracking visual, pero no hay plano detectado.");
            return true;
        }

        detectedType = TrackableType.None;
        return false;
    }

    private bool TryRaycast(Vector2 touchPosition, TrackableType trackableType, out Pose hitPose)
    {
        bool detected = raycastManager.Raycast(touchPosition, hits, trackableType);
        Debug.Log("Raycast " + trackableType + ": " + detected + " | hits: " + hits.Count);

        if (detected && hits.Count > 0)
        {
            hitPose = hits[0].pose;
            return true;
        }

        hitPose = default;
        return false;
    }
}
