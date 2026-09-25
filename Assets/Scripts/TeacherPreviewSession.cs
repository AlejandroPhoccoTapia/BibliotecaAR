using System;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

[Serializable]
public sealed class TeacherPreviewLoginResult
{
    public string token;
}

[Serializable]
public sealed class ChapterArPlacement
{
    public float ar_marker_width_cm = 6f;
    public float ar_model_size_cm = 8f;
    public float ar_offset_x_cm;
    public float ar_offset_y_cm = 0.5f;
    public float ar_offset_z_cm;
    public float ar_yaw_degrees;

    public ChapterArPlacement Copy()
    {
        return (ChapterArPlacement)MemberwiseClone();
    }
}

public static class TeacherPreviewSession
{
    [Serializable]
    private sealed class LoginBody
    {
        public string username;
        public string password;
    }

    // Deliberately held in memory only. Teachers must sign in again after restarting the app.
    public static string Token { get; private set; }
    public static bool IsActive => !string.IsNullOrEmpty(Token);

    public static void Set(string token)
    {
        Token = token;
    }

    public static void Clear()
    {
        Token = null;
    }

    public static UnityWebRequest Login(string username, string password)
    {
        UnityWebRequest request = new UnityWebRequest(Url("auth/mobile-login/"), "POST");
        request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(
            JsonUtility.ToJson(new LoginBody { username = username, password = password })));
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.timeout = 30;
        return request;
    }

    public static UnityWebRequest GetChapter(string qrCode)
    {
        UnityWebRequest request = UnityWebRequest.Get(SceneUrl(qrCode));
        Authorize(request);
        request.timeout = 30;
        return request;
    }

    public static UnityWebRequest SavePlacement(string qrCode, ChapterArPlacement placement)
    {
        UnityWebRequest request = new UnityWebRequest(SceneUrl(qrCode), "PATCH");
        request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(placement)));
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        Authorize(request);
        request.timeout = 30;
        return request;
    }

    private static void Authorize(UnityWebRequest request)
    {
        request.SetRequestHeader("Authorization", "TeacherPreview " + Token);
    }

    private static string SceneUrl(string qrCode)
    {
        return Url("teacher/mobile/scenes/" + UnityWebRequest.EscapeURL(qrCode) + "/");
    }

    private static string Url(string path)
    {
        return StudentAppSession.ApiBaseUrl.TrimEnd('/') + "/" + path;
    }
}
