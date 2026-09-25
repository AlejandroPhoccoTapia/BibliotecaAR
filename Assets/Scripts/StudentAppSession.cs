using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

[Serializable]
public class StudentProfileData
{
    public int id;
    public string full_name;
    public string classroom;
    public string photo_url;
}

[Serializable]
public class StudentAuthData
{
    public string token;
    public string expires_at;
    public StudentProfileData student;
}

[Serializable]
public class StudentSessionData
{
    public StudentProfileData student;
}

[Serializable]
public class StudentBookData
{
    public int id;
    public string title;
    public string description;
    public string cover_url;
}

[Serializable]
public class StudentResumeData
{
    public int book_id;
    public string book_title;
    public int scene_id;
    public string scene_title;
    public bool is_completed;
}

[Serializable]
public class StudentLibraryData
{
    public StudentProfileData student;
    public StudentBookData[] assigned_books;
    public StudentBookData[] recent_books;
    public StudentResumeData resume;
}

[Serializable]
public class StudentChapterData
{
    public int id;
    public string title;
    public int order;
    public string text;
    public string audio_url;
    public bool is_completed;
    public string last_opened_at;
}

[Serializable]
public class StudentBookDetailData
{
    public StudentBookData book;
    public StudentChapterData[] chapters;
}

[Serializable]
public class StudentProgressData
{
    public int scene_id;
    public bool is_completed;
}

public static class StudentAppSession
{
    private const string TokenKey = "student_access_token";
    private const string NameKey = "student_name";
    private const string IdKey = "student_id";
    private static bool loaded;

    public static string ApiBaseUrl = "https://bibliotecaar-backend.onrender.com/api";
    public static bool OpenScannerOnLoad;
    public static string Token { get; private set; }
    public static string StudentName { get; private set; }
    public static int StudentId { get; private set; }
    public static bool HasToken => !string.IsNullOrEmpty(Token);

    public static void LoadRemembered()
    {
        if (loaded)
            return;
        loaded = true;
        Token = PlayerPrefs.GetString(TokenKey, string.Empty);
        StudentName = PlayerPrefs.GetString(NameKey, string.Empty);
        StudentId = PlayerPrefs.GetInt(IdKey, 0);
    }

    public static void Set(StudentAuthData auth, bool remember)
    {
        Token = auth.token;
        StudentName = auth.student.full_name;
        StudentId = auth.student.id;
        loaded = true;
        if (remember)
        {
            PlayerPrefs.SetString(TokenKey, Token);
            PlayerPrefs.SetString(NameKey, StudentName);
            PlayerPrefs.SetInt(IdKey, StudentId);
            PlayerPrefs.Save();
        }
        else
        {
            ForgetRemembered();
        }
    }

    public static void Clear()
    {
        Token = string.Empty;
        StudentName = string.Empty;
        StudentId = 0;
        OpenScannerOnLoad = false;
        ForgetRemembered();
    }

    private static void ForgetRemembered()
    {
        PlayerPrefs.DeleteKey(TokenKey);
        PlayerPrefs.DeleteKey(NameKey);
        PlayerPrefs.DeleteKey(IdKey);
        PlayerPrefs.Save();
    }
}

public static class StudentApi
{
    public static UnityWebRequest Get(string path)
    {
        UnityWebRequest request = UnityWebRequest.Get(Url(path));
        AddAuthorization(request);
        request.timeout = 30;
        return request;
    }

    public static UnityWebRequest Post(string path)
    {
        UnityWebRequest request = new UnityWebRequest(Url(path), "POST");
        request.uploadHandler = new UploadHandlerRaw(Array.Empty<byte>());
        request.downloadHandler = new DownloadHandlerBuffer();
        AddAuthorization(request);
        request.timeout = 30;
        return request;
    }

    public static UnityWebRequest CodeLogin(string code)
    {
        UnityWebRequest request = new UnityWebRequest(Url("student/code-login/"), "POST");
        request.uploadHandler = new UploadHandlerRaw(
            System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(new CodeLoginBody { code = code })));
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.timeout = 30;
        return request;
    }

    public static UnityWebRequest FaceLogin(byte[] jpeg)
    {
        var sections = new List<IMultipartFormSection>
        {
            new MultipartFormFileSection("image", jpeg, "student-face.jpg", "image/jpeg")
        };
        UnityWebRequest request = UnityWebRequest.Post(Url("student/face-login/"), sections);
        request.timeout = 30;
        return request;
    }

    public static string ErrorMessage(UnityWebRequest request)
    {
        if (request.responseCode == 401)
            return "Tu sesión terminó. Entra otra vez.";
        if (request.responseCode == 404)
            return "No se encontró el contenido o el rostro registrado.";
        if (request.responseCode == 429)
            return "Demasiados intentos. Espera un momento y vuelve a probar.";
        if (request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.DataProcessingError)
            return "No hay conexión con el servidor. Comprueba internet.";
        return "No se pudo completar la solicitud. Inténtalo de nuevo.";
    }

    private static string Url(string path)
    {
        return StudentAppSession.ApiBaseUrl.TrimEnd('/') + "/" + path.TrimStart('/');
    }

    private static void AddAuthorization(UnityWebRequest request)
    {
        if (StudentAppSession.HasToken)
            request.SetRequestHeader("Authorization", "Bearer " + StudentAppSession.Token);
    }

    [Serializable]
    private class CodeLoginBody
    {
        public string code;
    }
}
