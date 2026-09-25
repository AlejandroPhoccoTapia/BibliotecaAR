using System.Collections;
using UnityEngine;

// Keeps a tracked model visible through short camera interruptions and eases it away on loss.
public sealed class ARTrackedVisibility : MonoBehaviour
{
    private Vector3 baseScale;
    private bool initialized;
    private bool? targetVisible;
    private Coroutine transition;

    private void Awake()
    {
        if (!initialized)
            SetBaseScale(transform.localScale);
    }

    public void SetBaseScale(Vector3 scale)
    {
        baseScale = scale;
        initialized = true;
        if (targetVisible != false)
            transform.localScale = scale;
    }

    public void SetTracked(bool visible)
    {
        if (targetVisible == visible)
            return;
        targetVisible = visible;
        if (transition != null)
            StopCoroutine(transition);
        if (visible)
        {
            gameObject.SetActive(true);
            transition = StartCoroutine(Show());
        }
        else
        {
            if (!gameObject.activeSelf)
            {
                transform.localScale = Vector3.zero;
                return;
            }
            transition = StartCoroutine(Hide());
        }
    }

    private IEnumerator Show()
    {
        Vector3 start = transform.localScale;
        float elapsed = 0f;
        while (elapsed < 0.2f)
        {
            elapsed += Time.deltaTime;
            transform.localScale = Vector3.Lerp(start, baseScale, Mathf.Clamp01(elapsed / 0.2f));
            yield return null;
        }
        transform.localScale = baseScale;
        transition = null;
    }

    private IEnumerator Hide()
    {
        yield return new WaitForSeconds(0.45f);
        Vector3 start = transform.localScale;
        float elapsed = 0f;
        while (elapsed < 0.22f)
        {
            elapsed += Time.deltaTime;
            transform.localScale = Vector3.Lerp(start, Vector3.zero, Mathf.Clamp01(elapsed / 0.22f));
            yield return null;
        }
        transform.localScale = Vector3.zero;
        transition = null;
        gameObject.SetActive(false);
    }
}
