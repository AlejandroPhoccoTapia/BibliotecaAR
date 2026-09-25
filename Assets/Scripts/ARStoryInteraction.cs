using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public sealed class ARStoryInteraction : MonoBehaviour
{
    private Transform visual;
    private Vector3 restingPosition;
    private Animation modelAnimation;
    private string idleClip;
    private string walkClip;
    private string tapAnimationName;
    private string tapClip;
    private Coroutine walkRoutine;
    private bool outward;

    public void Configure(string animationName)
    {
        tapAnimationName = animationName?.Trim();
    }

    private void Start()
    {
        visual = transform.Find("Visual");
        if (visual == null)
            return;
        restingPosition = visual.localPosition;
        modelAnimation = visual.GetComponentInChildren<Animation>(true);
        if (modelAnimation == null)
            return;
        modelAnimation.playAutomatically = false;
        modelAnimation.Stop();
        idleClip = FindClip("Idle", "Quieto", "Stand");
        walkClip = FindClip("Walk", "Caminar", "Walking");
        if (!string.IsNullOrEmpty(tapAnimationName))
        {
            foreach (AnimationState state in modelAnimation)
                if (string.Equals(state.name, tapAnimationName, StringComparison.Ordinal))
                {
                    tapClip = state.name;
                    break;
                }
            if (string.IsNullOrEmpty(tapClip))
                Debug.LogWarning("ARStoryInteraction: el GLB no contiene la animación configurada: " + tapAnimationName);
        }
        if (!string.IsNullOrEmpty(idleClip))
            modelAnimation.Play(idleClip);
    }

    private void Update()
    {
        Vector2 position;
        if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
            position = Touchscreen.current.primaryTouch.position.ReadValue();
        else if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            position = Mouse.current.position.ReadValue();
        else
            return;

        if (PointerHitsUi(position))
            return;

        // A chapter-selected clip plays from any free part of the camera view.
        if (!string.IsNullOrEmpty(tapClip))
        {
            if (walkRoutine != null)
                StopCoroutine(walkRoutine);
            walkRoutine = StartCoroutine(PlayTapAnimation());
            return;
        }

        if (Camera.main == null)
            return;
        Ray ray = Camera.main.ScreenPointToRay(position);
        foreach (RaycastHit hit in Physics.RaycastAll(ray, 20f))
        {
            if (hit.transform == transform || hit.transform.IsChildOf(transform))
            {
                if (walkRoutine != null)
                    StopCoroutine(walkRoutine);
                walkRoutine = StartCoroutine(PlayTapAnimation());
                break;
            }
        }
    }

    private bool PointerHitsUi(Vector2 position)
    {
        if (EventSystem.current == null)
            return false;
        PointerEventData pointer = new PointerEventData(EventSystem.current) { position = position };
        List<RaycastResult> hits = new List<RaycastResult>();
        EventSystem.current.RaycastAll(pointer, hits);
        foreach (RaycastResult hit in hits)
            if (hit.module is UnityEngine.UI.GraphicRaycaster)
                return true;
        return false;
    }

    private IEnumerator PlayTapAnimation()
    {
        if (visual == null)
            yield break;
        if (modelAnimation != null && !string.IsNullOrEmpty(tapClip))
        {
            modelAnimation.Stop();
            modelAnimation.Play(tapClip);
            yield return new WaitForSeconds(Mathf.Max(0.1f, modelAnimation[tapClip].length));
            modelAnimation.Stop();
            if (!string.IsNullOrEmpty(idleClip))
                modelAnimation.Play(idleClip);
            walkRoutine = null;
            yield break;
        }

        if (modelAnimation != null && !string.IsNullOrEmpty(walkClip))
            modelAnimation.Play(walkClip);

        // The root is normalized to a physical size; convert 2.5 cm to its local units.
        float localDistance = 0.025f / Mathf.Max(0.00001f, transform.localScale.x);
        Vector3 start = visual.localPosition;
        Vector3 target = restingPosition + (outward ? Vector3.zero : Vector3.forward * localDistance);
        outward = !outward;
        float duration = 1.35f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
            visual.localPosition = Vector3.Lerp(start, target, t);
            yield return null;
        }
        visual.localPosition = target;
        if (modelAnimation != null)
        {
            modelAnimation.Stop();
            if (!string.IsNullOrEmpty(idleClip))
                modelAnimation.Play(idleClip);
        }
        walkRoutine = null;
    }

    private string FindClip(params string[] candidates)
    {
        if (modelAnimation == null)
            return null;
        foreach (AnimationState state in modelAnimation)
            foreach (string candidate in candidates)
                if (state.name.IndexOf(candidate, StringComparison.OrdinalIgnoreCase) >= 0)
                    return state.name;
        return null;
    }
}
