using System;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

/// <summary>Small Unity bridge for the Android chapter narrator.</summary>
public sealed class AndroidNarrationVoice : IDisposable
{
#if UNITY_ANDROID && !UNITY_EDITOR
    private AndroidJavaObject nativeVoice;
#endif

    public bool IsReady { get; private set; }
    public bool IsSpeaking { get; private set; }
    public bool IsPaused { get; private set; }
    public string Error { get; private set; } = string.Empty;

    public AndroidNarrationVoice()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            AndroidJavaObject activity = AndroidApplication.currentActivity;
            if (activity == null)
            {
                Error = "No se pudo iniciar la voz en este teléfono.";
                return;
            }
            nativeVoice = new AndroidJavaObject("com.bibliotecaar.NarrationTts", activity);
            Poll();
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Narración automática: " + exception.Message);
            Error = "No se pudo iniciar la voz en este teléfono.";
        }
#else
        Error = "La voz automática está disponible en la app para Android.";
#endif
    }

    public void Poll()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (nativeVoice == null)
            return;
        try
        {
            IsReady = nativeVoice.Call<bool>("isReady");
            IsSpeaking = nativeVoice.Call<bool>("isSpeaking");
            IsPaused = nativeVoice.Call<bool>("isPaused");
            Error = nativeVoice.Call<string>("getError") ?? string.Empty;
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Narración automática: " + exception.Message);
            IsReady = false;
            IsSpeaking = false;
            IsPaused = false;
            Error = "La voz del teléfono dejó de responder.";
        }
#endif
    }

    public bool Play(string text)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (nativeVoice == null || !IsReady)
            return false;
        try
        {
            bool started = IsPaused
                ? nativeVoice.Call<bool>("resume")
                : nativeVoice.Call<bool>("play", text);
            Poll();
            return started;
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Narración automática: " + exception.Message);
            Error = "No se pudo reproducir la narración.";
        }
#endif
        return false;
    }

    public void Pause()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (nativeVoice == null)
            return;
        try
        {
            nativeVoice.Call("pause");
            Poll();
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Narración automática: " + exception.Message);
            Error = "No se pudo pausar la narración.";
        }
#endif
    }

    public void Stop()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (nativeVoice == null)
            return;
        try
        {
            nativeVoice.Call("stop");
            Poll();
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Narración automática: " + exception.Message);
        }
#endif
    }

    public void Dispose()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (nativeVoice == null)
            return;
        try
        {
            nativeVoice.Call("dispose");
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Narración automática: " + exception.Message);
        }
        nativeVoice.Dispose();
        nativeVoice = null;
#endif
    }
}
