using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.UI;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

// Touches on the camera view edit the chapter placement; touches on the HUD never do.
public sealed class ARPlacementGestureController : MonoBehaviour
{
    private readonly List<Touch> touches = new List<Touch>(2);
    private readonly List<RaycastResult> uiHits = new List<RaycastResult>();
    private QRTrackedImagePlacer imagePlacer;
    private Func<ChapterArPlacement> getPlacement;
    private Action<ChapterArPlacement> setPlacement;
    private Action<string> setMessage;
    private int previousCount;
    private int previousFirstFinger = -1;
    private int previousSecondFinger = -1;
    private bool blocked;

    public void Initialize(QRTrackedImagePlacer placer, Func<ChapterArPlacement> onGetPlacement,
        Action<ChapterArPlacement> onSetPlacement, Action<string> onMessage)
    {
        imagePlacer = placer;
        getPlacement = onGetPlacement;
        setPlacement = onSetPlacement;
        setMessage = onMessage;
    }

    private void OnEnable()
    {
        EnhancedTouchSupport.Enable();
    }

    private void OnDisable()
    {
        EnhancedTouchSupport.Disable();
        ResetGesture();
    }

    private void Update()
    {
        if (!ARPlacementEditorUI.IsEditing || getPlacement == null || getPlacement() == null)
        {
            ResetGesture();
            return;
        }

        touches.Clear();
        foreach (Touch touch in Touch.activeTouches)
            if (touch.phase != UnityEngine.InputSystem.TouchPhase.Ended
                && touch.phase != UnityEngine.InputSystem.TouchPhase.Canceled)
                touches.Add(touch);

        int count = touches.Count;
        if (count == 0 || count > 2)
        {
            ResetGesture();
            return;
        }

        int firstFinger = touches[0].finger.index;
        int secondFinger = count == 2 ? touches[1].finger.index : -1;
        if (count != previousCount || firstFinger != previousFirstFinger
            || secondFinger != previousSecondFinger)
        {
            previousCount = count;
            previousFirstFinger = firstFinger;
            previousSecondFinger = secondFinger;
            blocked = PointerHitsUi(touches[0].screenPosition)
                || (count == 2 && PointerHitsUi(touches[1].screenPosition));
            return;
        }

        if (blocked || PointerHitsUi(touches[0].screenPosition)
            || (count == 2 && PointerHitsUi(touches[1].screenPosition)))
        {
            blocked = true;
            return;
        }

        if (imagePlacer == null || imagePlacer.objectToPlace == null
            || !imagePlacer.TryGetTrackedImageTransform(out Transform image))
        {
            setMessage?.Invoke("Enfoca el QR impreso para ajustar el modelo con gestos.");
            return;
        }

        ChapterArPlacement placement = getPlacement().Copy();
        if (count == 1)
        {
            if (!MoveOnPage(touches[0], image, placement))
                return;
        }
        else if (!PinchTurnAndLift(touches[0], touches[1], placement))
            return;
        setPlacement?.Invoke(placement);
    }

    private static bool MoveOnPage(Touch touch, Transform image, ChapterArPlacement placement)
    {
        if (touch.delta.sqrMagnitude < 0.01f || Camera.main == null)
            return false;

        Plane page = new Plane(image.up, image.position);
        Ray before = Camera.main.ScreenPointToRay(touch.screenPosition - touch.delta);
        Ray after = Camera.main.ScreenPointToRay(touch.screenPosition);
        if (!page.Raycast(before, out float beforeDistance)
            || !page.Raycast(after, out float afterDistance))
            return false;

        Vector3 delta = image.InverseTransformVector(
            after.GetPoint(afterDistance) - before.GetPoint(beforeDistance));
        // A nearly edge-on page can amplify a small screen motion into a large ray hit jump.
        if (delta.sqrMagnitude > 0.0025f)
            delta = delta.normalized * 0.05f;
        placement.ar_offset_x_cm = Mathf.Clamp(placement.ar_offset_x_cm + delta.x * 100f, -50f, 50f);
        placement.ar_offset_z_cm = Mathf.Clamp(placement.ar_offset_z_cm + delta.z * 100f, -50f, 50f);
        return true;
    }

    private static bool PinchTurnAndLift(Touch first, Touch second, ChapterArPlacement placement)
    {
        Vector2 now = second.screenPosition - first.screenPosition;
        Vector2 before = second.screenPosition - second.delta
            - first.screenPosition + first.delta;
        if (before.magnitude < 24f || now.magnitude < 24f)
            return false;

        float ratio = now.magnitude / before.magnitude;
        float rotation = Vector2.SignedAngle(before, now);
        float verticalPixels = (first.delta.y + second.delta.y) * 0.5f;
        if (Mathf.Abs(ratio - 1f) < 0.0001f
            && Mathf.Abs(rotation) < 0.01f && Mathf.Abs(verticalPixels) < 0.1f)
            return false;

        placement.ar_model_size_cm = Mathf.Clamp(placement.ar_model_size_cm * ratio, 1f, 50f);
        placement.ar_yaw_degrees = Mathf.Clamp(placement.ar_yaw_degrees + rotation, -180f, 180f);
        placement.ar_offset_y_cm = Mathf.Clamp(placement.ar_offset_y_cm
            + verticalPixels / Mathf.Max(Screen.height, 1) * 20f, -50f, 50f);
        return true;
    }

    private bool PointerHitsUi(Vector2 position)
    {
        if (EventSystem.current == null)
            return false;
        uiHits.Clear();
        PointerEventData pointer = new PointerEventData(EventSystem.current) { position = position };
        EventSystem.current.RaycastAll(pointer, uiHits);
        foreach (RaycastResult hit in uiHits)
            if (hit.module is GraphicRaycaster)
                return true;
        return false;
    }

    private void ResetGesture()
    {
        previousCount = 0;
        previousFirstFinger = -1;
        previousSecondFinger = -1;
        blocked = false;
    }
}
