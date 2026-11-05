using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public sealed class ARStoryInteraction : MonoBehaviour
{
    private Transform visual;
    private Animation modelAnimation;
    private string idleClip;
    private string tapAnimationName;
    private string tapClip;
    private Coroutine animationRoutine;
    private bool missingAnimationReported;

    public void Configure(string animationName)
    {
        tapAnimationName = animationName?.Trim();
    }

    private void Start()
    {
        visual = transform.Find("Visual");
        if (visual == null)
            return;
        modelAnimation = visual.GetComponentInChildren<Animation>(true);
        if (modelAnimation == null)
        {
            Debug.LogWarning("ARStoryInteraction: el modelo no contiene clips de animación reproducibles.");
            return;
        }
        modelAnimation.playAutomatically = false;
        modelAnimation.Stop();
        idleClip = FindClip("Idle", "Quieto", "Stand");
        if (!string.IsNullOrEmpty(tapAnimationName))
        {
            foreach (AnimationState state in modelAnimation)
                if (string.Equals(state.name, tapAnimationName, StringComparison.OrdinalIgnoreCase))
                {
                    tapClip = state.name;
                    break;
                }
            if (string.IsNullOrEmpty(tapClip))
                Debug.LogWarning("ARStoryInteraction: el GLB no contiene la animación configurada: " + tapAnimationName);
        }
        if (string.IsNullOrEmpty(tapClip))
            tapClip = FindClip("Walk", "Caminar", "Walking");
        if (string.IsNullOrEmpty(tapClip))
            foreach (AnimationState state in modelAnimation)
                if (!string.Equals(state.name, idleClip, StringComparison.Ordinal))
                {
                    tapClip = state.name;
                    break;
                }
        Debug.Log("ARStoryInteraction: clip al tocar = " + (tapClip ?? "ninguno")
            + "; configurado = " + (tapAnimationName ?? "automático"));
        if (!string.IsNullOrEmpty(idleClip))
            modelAnimation.Play(idleClip);
    }

    private void Update()
    {
        if (ARPlacementEditorUI.IsEditing)
            return;

        Vector2 position;
        if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
            position = Touchscreen.current.primaryTouch.position.ReadValue();
        else if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            position = Mouse.current.position.ReadValue();
        else
            return;

        if (PointerHitsUi(position))
            return;

        if (modelAnimation == null || string.IsNullOrEmpty(tapClip))
        {
            if (!missingAnimationReported)
            {
                missingAnimationReported = true;
                Debug.LogWarning("ARStoryInteraction: no hay una animación para reproducir al tocar.");
                FindAnyObjectByType<ARSceneExperienceUI>()?.SetStatus(
                    "Este modelo no incluye una animación para reproducir. Revisa el GLB en el panel docente.",
                    ARSceneExperienceUI.MessageTone.Warning, false);
            }
            return;
        }
        if (animationRoutine != null)
            StopCoroutine(animationRoutine);
        animationRoutine = StartCoroutine(PlayTapAnimation());
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
        if (modelAnimation == null || string.IsNullOrEmpty(tapClip))
            yield break;
        AnimationState state = modelAnimation[tapClip];
        if (state == null)
        {
            Debug.LogWarning("ARStoryInteraction: el clip desapareció del modelo: " + tapClip);
            animationRoutine = null;
            yield break;
        }
        modelAnimation.Stop();
        state.time = 0f;
        state.speed = 1f;
        state.wrapMode = WrapMode.Once;
        if (!modelAnimation.Play(tapClip, PlayMode.StopAll))
        {
            Debug.LogWarning("ARStoryInteraction: Unity no pudo reproducir el clip: " + tapClip);
            animationRoutine = null;
            yield break;
        }
        yield return new WaitForSeconds(Mathf.Max(0.1f, state.length));
        modelAnimation.Stop();
        if (!string.IsNullOrEmpty(idleClip))
            modelAnimation.Play(idleClip);
        animationRoutine = null;
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
