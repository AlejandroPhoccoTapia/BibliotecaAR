using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.Management;
using UnityEngine.XR.ARSubsystems;

public class ARPlaneDebugLogger : MonoBehaviour
{
    private ARPlaneManager planeManager;
    private ARRaycastManager raycastManager;
    private float nextStatusLogTime;

    void Awake()
    {
        planeManager = GetComponent<ARPlaneManager>();
        raycastManager = GetComponent<ARRaycastManager>();

        Debug.Log("ARPlaneDebugLogger: Awake ejecutado");
        Debug.Log("ARPlaneDebugLogger: ARPlaneManager " + (planeManager == null ? "NO encontrado" : "encontrado"));
        Debug.Log("ARPlaneDebugLogger: ARRaycastManager " + (raycastManager == null ? "NO encontrado" : "encontrado"));
    }

    void OnEnable()
    {
        ARSession.stateChanged += OnSessionStateChanged;

        if (planeManager != null)
            planeManager.trackablesChanged.AddListener(OnPlanesChanged);
    }

    void OnDisable()
    {
        ARSession.stateChanged -= OnSessionStateChanged;

        if (planeManager != null)
            planeManager.trackablesChanged.RemoveListener(OnPlanesChanged);
    }

    void Update()
    {
        if (Time.time < nextStatusLogTime)
            return;

        nextStatusLogTime = Time.time + 2f;

        int planeCount = 0;
        if (planeManager != null)
        {
            foreach (ARPlane ignored in planeManager.trackables)
                planeCount++;
        }

        Debug.Log(
            "ARPlaneDebugLogger: estado=" + ARSession.state +
            ", razonNoTracking=" + ARSession.notTrackingReason +
            GetXRLoaderStatus() +
            ", planos=" + planeCount +
            ", planeManagerEnabled=" + (planeManager != null && planeManager.enabled) +
            ", requestedDetection=" + (planeManager != null ? planeManager.requestedDetectionMode.ToString() : "null") +
            ", currentDetection=" + (planeManager != null ? planeManager.currentDetectionMode.ToString() : "null") +
            ", raycastManagerEnabled=" + (raycastManager != null && raycastManager.enabled)
        );
    }

    private static string GetXRLoaderStatus()
    {
        XRManagerSettings manager = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;

        if (manager == null)
            return ", xrManager=null, activeLoader=null, xrInicializado=False";

        string loaderName = manager.activeLoader != null ? manager.activeLoader.GetType().Name : "null";
        return ", xrManager=ok, activeLoader=" + loaderName + ", xrInicializado=" + manager.isInitializationComplete;
    }

    private void OnSessionStateChanged(ARSessionStateChangedEventArgs args)
    {
        Debug.Log(
            "ARPlaneDebugLogger: cambio estado ARSession -> " + args.state +
            ", razonNoTracking=" + ARSession.notTrackingReason +
            GetXRLoaderStatus()
        );
    }

    private void OnPlanesChanged(ARTrackablesChangedEventArgs<ARPlane> args)
    {
        Debug.Log(
            "ARPlaneDebugLogger: planesChanged agregados=" + args.added.Count +
            ", actualizados=" + args.updated.Count +
            ", removidos=" + args.removed.Count
        );

        foreach (ARPlane plane in args.added)
            LogPlane("agregado", plane);

        foreach (ARPlane plane in args.updated)
            LogPlane("actualizado", plane);
    }

    private static void LogPlane(string action, ARPlane plane)
    {
        Debug.Log(
            "ARPlaneDebugLogger: plano " + action +
            " id=" + plane.trackableId +
            ", alignment=" + plane.alignment +
            ", tracking=" + plane.trackingState +
            ", size=" + plane.size +
            ", center=" + plane.center
        );
    }
}
