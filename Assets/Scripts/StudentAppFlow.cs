using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class StudentAppFlow : MonoBehaviour
{
    private static readonly Color Ink = new Color(0.08f, 0.15f, 0.20f);
    private static readonly Color Muted = new Color(0.34f, 0.43f, 0.47f);
    private static readonly Color Accent = new Color(0.02f, 0.43f, 0.49f);
    private static readonly Color Surface = new Color(0.96f, 0.98f, 0.98f);

    private QRCodeScanner scanner;
    private TMP_FontAsset font;
    private RectTransform overlay;
    private RectTransform safeArea;
    private RectTransform scannerButtonSafeArea;
    private Button scannerHomeButton;
    private TMP_Text messageText;
    private TMP_InputField codeInput;
    private TMP_InputField teacherUserInput;
    private TMP_InputField teacherPasswordInput;
    private TMP_Text rememberLabel;
    private Button rememberButton;
    private RawImage facePreview;
    private WebCamTexture faceCamera;
    private AudioSource readingAudio;
    private StudentLibraryData library;
    private StudentBookDetailData bookDetail;
    private StudentChapterData currentChapter;
    private bool rememberDevice;
    private bool busy;
    private Rect lastSafeArea;
    private Vector2 lastScreenSize;

    public bool ScanRequested { get; private set; }

    public void Initialize(QRCodeScanner qrScanner, TMP_FontAsset uiFont)
    {
        scanner = qrScanner;
        font = uiFont;
        StudentAppSession.LoadRemembered();
        Canvas canvas = qrScanner.cameraPreview != null
            ? qrScanner.cameraPreview.GetComponentInParent<Canvas>() : null;
        if (canvas == null)
        {
            ScanRequested = true;
            return;
        }

        overlay = Panel("StudentAppOverlay", canvas.transform, Surface, Vector2.zero, Vector2.one);
        safeArea = Panel("StudentSafeArea", overlay, Color.clear, Vector2.zero, Vector2.one);
        safeArea.GetComponent<Image>().raycastTarget = false;
        scannerButtonSafeArea = Panel("ScannerButtonSafeArea", canvas.transform, Color.clear, Vector2.zero, Vector2.one);
        scannerButtonSafeArea.GetComponent<Image>().raycastTarget = false;
        UpdateSafeArea();
        scannerHomeButton = Button("LibraryFromScanner", scannerButtonSafeArea, "← Biblioteca",
            new Vector2(0.03f, 0.925f), new Vector2(0.39f, 0.995f), Accent, ReturnToLibrary);
        scannerHomeButton.gameObject.SetActive(false);
        readingAudio = gameObject.AddComponent<AudioSource>();
        readingAudio.playOnAwake = false;

        if (TeacherPreviewSession.IsActive)
        {
            if (StudentAppSession.OpenScannerOnLoad)
            {
                StudentAppSession.OpenScannerOnLoad = false;
                RequestScan();
            }
            else
                ShowTeacherReady();
        }
        else if (StudentAppSession.HasToken)
        {
            ShowLoading("Recuperando tu biblioteca…");
            StartCoroutine(ValidateSavedSession());
        }
        else
        {
            ShowLogin(false);
        }
    }

    private void Update()
    {
        if (safeArea != null &&
            (lastSafeArea != Screen.safeArea || lastScreenSize != new Vector2(Screen.width, Screen.height)))
            UpdateSafeArea();

        if (faceCamera != null && faceCamera.isPlaying && facePreview != null)
        {
            facePreview.rectTransform.localEulerAngles = new Vector3(0f, 0f, -faceCamera.videoRotationAngle);
            facePreview.uvRect = faceCamera.videoVerticallyMirrored
                ? new Rect(0f, 1f, 1f, -1f) : new Rect(0f, 0f, 1f, 1f);
        }
    }

    private void OnDestroy()
    {
        StopFaceCamera();
        if (readingAudio != null)
            readingAudio.Stop();
    }

    private void UpdateSafeArea()
    {
        Rect area = Screen.safeArea;
        safeArea.anchorMin = new Vector2(area.xMin / Screen.width, area.yMin / Screen.height);
        safeArea.anchorMax = new Vector2(area.xMax / Screen.width, area.yMax / Screen.height);
        safeArea.offsetMin = Vector2.zero;
        safeArea.offsetMax = Vector2.zero;
        if (scannerButtonSafeArea != null)
        {
            scannerButtonSafeArea.anchorMin = safeArea.anchorMin;
            scannerButtonSafeArea.anchorMax = safeArea.anchorMax;
            scannerButtonSafeArea.offsetMin = Vector2.zero;
            scannerButtonSafeArea.offsetMax = Vector2.zero;
        }
        lastSafeArea = area;
        lastScreenSize = new Vector2(Screen.width, Screen.height);
    }

    private IEnumerator ValidateSavedSession()
    {
        using (UnityWebRequest request = StudentApi.Get("student/me/"))
        {
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
            {
                if (request.responseCode == 401)
                {
                    StudentAppSession.Clear();
                    ShowLogin(false);
                    SetMessage("Tu sesión terminó. Entra otra vez.");
                }
                else
                {
                    ShowLoading("No hay conexión. Comprueba internet y vuelve a intentarlo.");
                    Button("RetrySession", safeArea, "Reintentar", new Vector2(0.20f, 0.26f),
                        new Vector2(0.80f, 0.34f), Accent, () => StartCoroutine(ValidateSavedSession()));
                    Button("ChangeStudentOffline", safeArea, "Cambiar estudiante", new Vector2(0.20f, 0.16f),
                        new Vector2(0.80f, 0.24f), Color.white, ChangeStudent);
                }
                yield break;
            }
        }

        if (StudentAppSession.OpenScannerOnLoad)
        {
            StudentAppSession.OpenScannerOnLoad = false;
            RequestScan();
        }
        else
        {
            yield return LoadLibrary();
        }
    }

    private void ShowLoading(string text)
    {
        ClearView();
        Text("Loading", safeArea, text, 45f, new Vector2(0.10f, 0.40f),
            new Vector2(0.90f, 0.61f), TextAlignmentOptions.Center, Ink);
    }

    private void ShowLogin(bool useFace)
    {
        ClearView();
        Text("WelcomeTitle", safeArea, "Tu biblioteca te espera", 58f,
            new Vector2(0.08f, 0.84f), new Vector2(0.92f, 0.96f), TextAlignmentOptions.Center, Ink);
        Text("WelcomeDescription", safeArea, "Entra con tu código o con tu rostro para guardar lo que lees.", 34f,
            new Vector2(0.10f, 0.76f), new Vector2(0.90f, 0.85f), TextAlignmentOptions.Center, Muted);

        Button("CodeMode", safeArea, "Usar código", new Vector2(0.08f, 0.67f),
            new Vector2(0.49f, 0.74f), useFace ? Color.white : Accent, () => ShowLogin(false));
        Button("FaceMode", safeArea, "Usar rostro", new Vector2(0.51f, 0.67f),
            new Vector2(0.92f, 0.74f), useFace ? Accent : Color.white, () => ShowLogin(true));

        if (useFace)
        {
            RectTransform previewFrame = Panel("FacePreviewFrame", safeArea,
                new Color(0.07f, 0.16f, 0.19f), new Vector2(0.12f, 0.33f), new Vector2(0.88f, 0.64f));
            previewFrame.GetComponent<Image>().sprite = QRCodeScanner.GetMessageCardSprite();
            previewFrame.GetComponent<Image>().type = Image.Type.Sliced;
            previewFrame.gameObject.AddComponent<RectMask2D>();
            GameObject previewObject = new GameObject("FacePreview", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            previewObject.transform.SetParent(previewFrame, false);
            facePreview = previewObject.GetComponent<RawImage>();
            Stretch(facePreview.rectTransform, new Vector2(0.04f, 0.04f), new Vector2(0.96f, 0.96f));
            facePreview.raycastTarget = false;
            rememberButton = Button("RememberDevice", safeArea, RememberCaption(),
                new Vector2(0.10f, 0.24f), new Vector2(0.90f, 0.31f), Color.white, ToggleRemember);
            Button("RecognizeFace", safeArea, "Reconocer mi rostro", new Vector2(0.13f, 0.15f),
                new Vector2(0.87f, 0.23f), Accent, CaptureFace);
            StartCoroutine(StartFaceCamera());
        }
        else
        {
            Text("CodeLabel", safeArea, "Código que te entregó tu docente", 32f,
                new Vector2(0.10f, 0.55f), new Vector2(0.90f, 0.62f), TextAlignmentOptions.Left, Ink);
            codeInput = Input("StudentCode", safeArea, "Escribe tu código",
                new Vector2(0.10f, 0.46f), new Vector2(0.90f, 0.55f));
            rememberButton = Button("RememberDevice", safeArea, RememberCaption(),
                new Vector2(0.10f, 0.35f), new Vector2(0.90f, 0.43f), Color.white, ToggleRemember);
            rememberLabel = Text("RememberHelp", safeArea, "Actívalo solo si este teléfono es tuyo.", 28f,
                new Vector2(0.11f, 0.29f), new Vector2(0.89f, 0.35f), TextAlignmentOptions.Left, Muted);
            Button("EnterCode", safeArea, "Entrar a mi biblioteca", new Vector2(0.13f, 0.17f),
                new Vector2(0.87f, 0.25f), Accent, LoginWithCode);
        }

        messageText = Text("LoginMessage", safeArea, string.Empty, 30f,
            new Vector2(0.10f, 0.015f), new Vector2(0.64f, 0.13f), TextAlignmentOptions.Center,
            new Color(0.72f, 0.16f, 0.16f));
        Button("TeacherMode", safeArea, "Docente", new Vector2(0.67f, 0.035f),
            new Vector2(0.94f, 0.105f), Color.white, ShowTeacherLogin);
    }

    private void ShowTeacherLogin()
    {
        ClearView();
        Text("TeacherTitle", safeArea, "Vista docente", 58f,
            new Vector2(0.09f, 0.80f), new Vector2(0.91f, 0.93f), TextAlignmentOptions.Center, Ink);
        Text("TeacherHelp", safeArea, "Entra para ajustar el modelo sobre la página. La sesión dura 30 minutos.", 32f,
            new Vector2(0.11f, 0.65f), new Vector2(0.89f, 0.79f), TextAlignmentOptions.Center, Muted);
        teacherUserInput = Input("TeacherUser", safeArea, "Usuario docente",
            new Vector2(0.10f, 0.52f), new Vector2(0.90f, 0.60f));
        teacherUserInput.characterLimit = 150;
        teacherUserInput.contentType = TMP_InputField.ContentType.Standard;
        teacherPasswordInput = Input("TeacherPassword", safeArea, "Contraseña",
            new Vector2(0.10f, 0.41f), new Vector2(0.90f, 0.49f));
        teacherPasswordInput.characterLimit = 128;
        teacherPasswordInput.contentType = TMP_InputField.ContentType.Password;
        Button("EnterTeacher", safeArea, "Entrar y escanear", new Vector2(0.13f, 0.27f),
            new Vector2(0.87f, 0.35f), Accent, LoginTeacher);
        Button("BackToStudent", safeArea, "Volver", new Vector2(0.20f, 0.17f),
            new Vector2(0.80f, 0.24f), Color.white,
            () => { if (StudentAppSession.HasToken && library != null) ShowHome(); else ShowLogin(false); });
        messageText = Text("TeacherMessage", safeArea, string.Empty, 30f,
            new Vector2(0.10f, 0.05f), new Vector2(0.90f, 0.15f), TextAlignmentOptions.Center,
            new Color(0.72f, 0.16f, 0.16f));
    }

    private void ShowTeacherReady()
    {
        ClearView();
        Text("TeacherReady", safeArea, "Vista docente activa", 56f,
            new Vector2(0.09f, 0.72f), new Vector2(0.91f, 0.90f), TextAlignmentOptions.Center, Ink);
        Text("TeacherReadyHelp", safeArea, "Escanea el QR del capítulo para ajustar el modelo sobre la página.", 34f,
            new Vector2(0.11f, 0.53f), new Vector2(0.89f, 0.69f), TextAlignmentOptions.Center, Muted);
        Button("TeacherScan", safeArea, "Escanear capítulo", new Vector2(0.13f, 0.32f),
            new Vector2(0.87f, 0.41f), Accent, RequestScan);
        Button("TeacherExit", safeArea, "Salir del modo docente", new Vector2(0.13f, 0.21f),
            new Vector2(0.87f, 0.29f), Color.white, ExitTeacherMode);
    }

    private void LoginTeacher()
    {
        if (busy || teacherUserInput == null || teacherPasswordInput == null)
            return;
        string username = teacherUserInput.text.Trim();
        string password = teacherPasswordInput.text;
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            SetMessage("Escribe tu usuario y contraseña.");
            return;
        }
        StartCoroutine(SubmitTeacherLogin(username, password));
    }

    private IEnumerator SubmitTeacherLogin(string username, string password)
    {
        busy = true;
        SetMessage("Comprobando acceso…");
        using (UnityWebRequest request = TeacherPreviewSession.Login(username, password))
        {
            yield return request.SendWebRequest();
            busy = false;
            if (request.result != UnityWebRequest.Result.Success)
            {
                SetMessage(request.responseCode == 403
                    ? "Datos incorrectos o cuenta sin permiso docente."
                    : StudentApi.ErrorMessage(request));
                yield break;
            }
            TeacherPreviewLoginResult result = JsonUtility.FromJson<TeacherPreviewLoginResult>(request.downloadHandler.text);
            if (result == null || string.IsNullOrEmpty(result.token))
            {
                SetMessage("No se pudo abrir la vista docente.");
                yield break;
            }
            TeacherPreviewSession.Set(result.token);
        }
        RequestScan();
    }

    private void ExitTeacherMode()
    {
        TeacherPreviewSession.Clear();
        if (StudentAppSession.HasToken && library != null)
            ShowHome();
        else if (StudentAppSession.HasToken)
        {
            ShowLoading("Recuperando tu biblioteca…");
            StartCoroutine(ValidateSavedSession());
        }
        else
            ShowLogin(false);
    }

    private void ToggleRemember()
    {
        rememberDevice = !rememberDevice;
        if (rememberButton != null)
        {
            TMP_Text caption = rememberButton.GetComponentInChildren<TMP_Text>();
            if (caption != null)
                caption.text = RememberCaption();
        }
        if (rememberLabel != null)
            rememberLabel.text = rememberDevice
                ? "Tu sesión seguirá abierta en este teléfono."
                : "Actívalo solo si este teléfono es tuyo.";
    }

    private string RememberCaption()
    {
        return rememberDevice ? "☑ Recordar en este dispositivo" : "☐ Recordar en este dispositivo";
    }

    private void LoginWithCode()
    {
        if (busy || codeInput == null)
            return;
        string code = codeInput.text.Trim();
        if (string.IsNullOrEmpty(code))
        {
            SetMessage("Escribe el código que te dio tu docente.");
            return;
        }
        StartCoroutine(SubmitCode(code));
    }

    private IEnumerator SubmitCode(string code)
    {
        busy = true;
        SetMessage("Entrando…");
        using (UnityWebRequest request = StudentApi.CodeLogin(code))
        {
            yield return request.SendWebRequest();
            busy = false;
            if (request.result != UnityWebRequest.Result.Success)
            {
                SetMessage(request.responseCode == 403 ? "Código incorrecto. Pídele ayuda a tu docente." : StudentApi.ErrorMessage(request));
                yield break;
            }
            StudentAuthData auth = JsonUtility.FromJson<StudentAuthData>(request.downloadHandler.text);
            if (auth == null || auth.student == null || string.IsNullOrEmpty(auth.token))
            {
                SetMessage("No se pudo abrir tu biblioteca. Vuelve a intentarlo.");
                yield break;
            }
            StudentAppSession.Set(auth, rememberDevice);
        }
        yield return LoadLibrary();
    }

    private IEnumerator StartFaceCamera()
    {
        if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
            yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);
        if (facePreview == null)
            yield break;
        if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
        {
            SetMessage("Activa el permiso de cámara en Ajustes o entra con tu código.");
            yield break;
        }

        WebCamDevice[] devices = WebCamTexture.devices;
        if (devices.Length == 0)
        {
            SetMessage("No encontramos una cámara. Entra con tu código.");
            yield break;
        }
        WebCamDevice chosen = devices[0];
        foreach (WebCamDevice device in devices)
        {
            if (device.isFrontFacing)
            {
                chosen = device;
                break;
            }
        }
        faceCamera = new WebCamTexture(chosen.name);
        if (facePreview != null)
            facePreview.texture = faceCamera;
        faceCamera.Play();
    }

    private void CaptureFace()
    {
        if (busy || faceCamera == null || !faceCamera.isPlaying || faceCamera.width <= 16)
        {
            SetMessage("Espera a que la cámara esté lista.");
            return;
        }

        Color32[] pixels = OrientFacePixels(faceCamera.GetPixels32(), faceCamera.width, faceCamera.height,
            faceCamera.videoRotationAngle, faceCamera.videoVerticallyMirrored, out int width, out int height);
        Texture2D still = new Texture2D(width, height, TextureFormat.RGB24, false);
        still.SetPixels32(pixels);
        still.Apply();
        byte[] jpeg = still.EncodeToJPG(85);
        Destroy(still);
        StopFaceCamera();
        StartCoroutine(SubmitFace(jpeg));
    }

    private static Color32[] OrientFacePixels(Color32[] pixels, int width, int height, int rotation,
        bool verticallyMirrored, out int outputWidth, out int outputHeight)
    {
        rotation = ((rotation % 360) + 360) % 360;
        bool sideways = rotation == 90 || rotation == 270;
        outputWidth = sideways ? height : width;
        outputHeight = sideways ? width : height;
        Color32[] oriented = new Color32[pixels.Length];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int sourceY = verticallyMirrored ? height - 1 - y : y;
                int targetX = x;
                int targetY = y;
                if (rotation == 90)
                {
                    targetX = y;
                    targetY = width - 1 - x;
                }
                else if (rotation == 180)
                {
                    targetX = width - 1 - x;
                    targetY = height - 1 - y;
                }
                else if (rotation == 270)
                {
                    targetX = height - 1 - y;
                    targetY = x;
                }
                oriented[targetY * outputWidth + targetX] = pixels[sourceY * width + x];
            }
        }
        return oriented;
    }

    private IEnumerator SubmitFace(byte[] jpeg)
    {
        busy = true;
        SetMessage("Buscando tu perfil…");
        using (UnityWebRequest request = StudentApi.FaceLogin(jpeg))
        {
            yield return request.SendWebRequest();
            busy = false;
            if (request.result != UnityWebRequest.Result.Success)
            {
                SetMessage(request.responseCode == 404
                    ? "No reconocimos tu rostro. Prueba con más luz o entra con tu código."
                    : StudentApi.ErrorMessage(request));
                StartCoroutine(StartFaceCamera());
                yield break;
            }
            StudentAuthData auth = JsonUtility.FromJson<StudentAuthData>(request.downloadHandler.text);
            if (auth == null || auth.student == null || string.IsNullOrEmpty(auth.token))
            {
                SetMessage("No se pudo abrir tu biblioteca. Entra con tu código.");
                yield break;
            }
            StudentAppSession.Set(auth, rememberDevice);
        }
        yield return LoadLibrary();
    }

    private IEnumerator LoadLibrary()
    {
        ShowLoading("Cargando tu biblioteca…");
        using (UnityWebRequest request = StudentApi.Get("student/library/"))
        {
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
            {
                if (request.responseCode == 401)
                {
                    StudentAppSession.Clear();
                    ShowLogin(false);
                    SetMessage("Tu sesión terminó. Entra otra vez.");
                }
                else
                {
                    ShowLoading(StudentApi.ErrorMessage(request));
                    Button("RetryLibrary", safeArea, "Reintentar", new Vector2(0.20f, 0.25f),
                        new Vector2(0.80f, 0.34f), Accent, () => StartCoroutine(LoadLibrary()));
                }
                yield break;
            }
            library = JsonUtility.FromJson<StudentLibraryData>(request.downloadHandler.text);
        }
        if (library == null)
        {
            ShowLoading("No se pudo leer la biblioteca.");
            yield break;
        }
        ShowHome();
    }

    private void ShowHome()
    {
        StopReadingAudio();
        currentChapter = null;
        ClearView();
        Header("Hola, " + StudentAppSession.StudentName, null, ChangeStudent);
        RectTransform content = ScrollContent(new Vector2(0.05f, 0.13f), new Vector2(0.95f, 0.76f));
        if (library.resume != null)
        {
            Label(content, "CONTINUAR LEYENDO", 28f, Accent);
            ListButton(content, library.resume.book_title + "\n" + library.resume.scene_title +
                (library.resume.is_completed ? " · Leído" : " · En progreso"),
                () => StartCoroutine(OpenBook(library.resume.book_id, library.resume.scene_id)));
        }

        Label(content, "MIS LIBROS", 28f, Accent);
        AddBooks(content, library.assigned_books, "Tu docente aún no te asignó libros. Puedes explorar con un QR.");
        if (library.recent_books != null && library.recent_books.Length > 0)
        {
            Label(content, "EXPLORADOS", 28f, Accent);
            AddBooks(content, library.recent_books, string.Empty);
        }
        Label(content, "PARA DOCENTES", 28f, Accent);
        ListButton(content, "Ajustar modelos sobre la página", ShowTeacherLogin);
        Button("ScanFromLibrary", safeArea, "Escanear QR", new Vector2(0.12f, 0.02f),
            new Vector2(0.88f, 0.105f), Accent, RequestScan);
    }

    private void AddBooks(RectTransform parent, StudentBookData[] books, string emptyMessage)
    {
        if (books == null || books.Length == 0)
        {
            Label(parent, emptyMessage, 32f, Muted);
            return;
        }
        foreach (StudentBookData book in books)
        {
            int bookId = book.id;
            ListButton(parent, book.title + (string.IsNullOrEmpty(book.description) ? string.Empty : "\n" + book.description),
                () => StartCoroutine(OpenBook(bookId, 0)));
        }
    }

    private IEnumerator OpenBook(int bookId, int resumeChapterId)
    {
        ShowLoading("Cargando capítulos…");
        using (UnityWebRequest request = StudentApi.Get("student/books/" + bookId + "/"))
        {
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
            {
                if (request.responseCode == 401)
                {
                    StudentAppSession.Clear();
                    ShowLogin(false);
                    SetMessage("Tu sesión terminó. Entra otra vez.");
                    yield break;
                }
                ShowLoading(StudentApi.ErrorMessage(request));
                Button("BackFromBookError", safeArea, "Volver", new Vector2(0.20f, 0.24f),
                    new Vector2(0.80f, 0.33f), Accent, ShowHome);
                yield break;
            }
            bookDetail = JsonUtility.FromJson<StudentBookDetailData>(request.downloadHandler.text);
        }
        if (bookDetail == null || bookDetail.book == null)
        {
            ShowHome();
            yield break;
        }
        if (resumeChapterId != 0)
        {
            foreach (StudentChapterData chapter in bookDetail.chapters)
            {
                if (chapter.id == resumeChapterId)
                {
                    OpenChapter(chapter);
                    yield break;
                }
            }
        }
        ShowBook();
    }

    private void ShowBook()
    {
        StopReadingAudio();
        currentChapter = null;
        ClearView();
        Header(bookDetail.book.title, () => StartCoroutine(LoadLibrary()), null);
        RectTransform content = ScrollContent(new Vector2(0.05f, 0.04f), new Vector2(0.95f, 0.76f));
        if (!string.IsNullOrEmpty(bookDetail.book.description))
            Label(content, bookDetail.book.description, 34f, Muted);
        Label(content, "CAPÍTULOS", 28f, Accent);
        if (bookDetail.chapters == null || bookDetail.chapters.Length == 0)
            Label(content, "Este libro todavía no tiene capítulos.", 32f, Muted);
        else
        {
            foreach (StudentChapterData chapter in bookDetail.chapters)
            {
                StudentChapterData selected = chapter;
                ListButton(content, "Capítulo " + chapter.order + " · " + chapter.title +
                    (chapter.is_completed ? "\n✓ Leído" : "\nToca para leer"), () => OpenChapter(selected));
            }
        }
    }

    private void OpenChapter(StudentChapterData chapter)
    {
        currentChapter = chapter;
        StopReadingAudio();
        ShowChapter();
        StartCoroutine(SaveChapterProgress(chapter.id, "open"));
    }

    private void ShowChapter()
    {
        ClearView();
        Header(currentChapter.title, ShowBook, null);
        RectTransform content = ScrollContent(new Vector2(0.05f, 0.23f), new Vector2(0.95f, 0.76f));
        Label(content, "CAPÍTULO " + currentChapter.order, 28f, Accent);
        Label(content, currentChapter.text, 40f, Ink);
        if (!string.IsNullOrEmpty(currentChapter.audio_url))
        {
            Button("ReadAudio", safeArea, "Escuchar audio", new Vector2(0.07f, 0.14f),
                new Vector2(0.48f, 0.21f), Color.white, PlayReadingAudio);
            Button("PauseReadAudio", safeArea, "Pausar", new Vector2(0.52f, 0.14f),
                new Vector2(0.93f, 0.21f), Color.white, PauseReadingAudio);
        }
        Button("CompletedChapter", safeArea, currentChapter.is_completed ? "✓ Leído" : "Terminé",
            new Vector2(0.07f, 0.03f), new Vector2(0.48f, 0.11f), Accent, CompleteChapter);
        Button("OpenARScanner", safeArea, "Ver en AR · Escanear", new Vector2(0.52f, 0.03f),
            new Vector2(0.93f, 0.11f), Accent, RequestScan);
        messageText = Text("ChapterFeedback", safeArea, string.Empty, 28f,
            new Vector2(0.08f, 0.105f), new Vector2(0.92f, 0.14f), TextAlignmentOptions.Center, Muted);
    }

    private IEnumerator SaveChapterProgress(int chapterId, string action)
    {
        using (UnityWebRequest request = StudentApi.Post("student/chapters/" + chapterId + "/" + action + "/"))
        {
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success && currentChapter != null && currentChapter.id == chapterId)
            {
                if (request.responseCode == 401)
                {
                    StudentAppSession.Clear();
                    ShowLogin(false);
                    SetMessage("Tu sesión terminó. Entra otra vez.");
                }
                else
                    SetMessage("No se pudo guardar el avance. Revisa internet.");
            }
            else if (action == "complete" && currentChapter != null && currentChapter.id == chapterId)
            {
                currentChapter.is_completed = true;
                if (bookDetail != null && bookDetail.chapters != null)
                {
                    foreach (StudentChapterData chapter in bookDetail.chapters)
                        if (chapter.id == chapterId) chapter.is_completed = true;
                }
                SetMessage("¡Capítulo guardado como leído!");
                Transform button = safeArea.Find("CompletedChapter");
                if (button != null)
                {
                    TMP_Text label = button.GetComponentInChildren<TMP_Text>();
                    if (label != null) label.text = "✓ Leído";
                }
            }
        }
    }

    private void CompleteChapter()
    {
        if (currentChapter == null || currentChapter.is_completed)
            return;
        StartCoroutine(SaveChapterProgress(currentChapter.id, "complete"));
    }

    private void PlayReadingAudio()
    {
        if (currentChapter == null || string.IsNullOrEmpty(currentChapter.audio_url))
            return;
        if (readingAudio.clip != null)
        {
            readingAudio.UnPause();
            return;
        }
        StartCoroutine(LoadReadingAudio(currentChapter.audio_url));
    }

    private IEnumerator LoadReadingAudio(string url)
    {
        SetMessage("Cargando audio…");
        using (UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip(url, AudioType.UNKNOWN))
        {
            request.timeout = 45;
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
            {
                SetMessage("No se pudo cargar el audio. Inténtalo de nuevo.");
                yield break;
            }
            if (currentChapter == null || currentChapter.audio_url != url)
            {
                Destroy(DownloadHandlerAudioClip.GetContent(request));
                yield break;
            }
            readingAudio.clip = DownloadHandlerAudioClip.GetContent(request);
            readingAudio.Play();
            SetMessage(string.Empty);
        }
    }

    private void PauseReadingAudio()
    {
        if (readingAudio != null && readingAudio.isPlaying)
            readingAudio.Pause();
    }

    private void StopReadingAudio()
    {
        if (readingAudio == null)
            return;
        readingAudio.Stop();
        if (readingAudio.clip != null)
            Destroy(readingAudio.clip);
        readingAudio.clip = null;
    }

    private void RequestScan()
    {
        StopFaceCamera();
        StopReadingAudio();
        ScanRequested = true;
        overlay.gameObject.SetActive(false);
        TMP_Text scannerCaption = scannerHomeButton.GetComponentInChildren<TMP_Text>();
        if (scannerCaption != null)
            scannerCaption.text = TeacherPreviewSession.IsActive ? "Salir vista docente" : "← Biblioteca";
        scannerHomeButton.gameObject.SetActive(true);
    }

    private void ReturnToLibrary()
    {
        if (TeacherPreviewSession.IsActive)
            TeacherPreviewSession.Clear();
        StudentAppSession.OpenScannerOnLoad = false;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    private void ChangeStudent()
    {
        if (busy)
            return;
        StartCoroutine(Logout());
    }

    private IEnumerator Logout()
    {
        busy = true;
        StopFaceCamera();
        StopReadingAudio();
        if (StudentAppSession.HasToken)
        {
            using (UnityWebRequest request = StudentApi.Post("student/logout/"))
            {
                request.timeout = 5;
                yield return request.SendWebRequest();
            }
        }
        StudentAppSession.Clear();
        busy = false;
        ShowLogin(false);
    }

    private void SetMessage(string message)
    {
        if (messageText != null)
            messageText.text = message;
    }

    private void StopFaceCamera()
    {
        if (faceCamera != null)
        {
            if (faceCamera.isPlaying)
                faceCamera.Stop();
            faceCamera = null;
        }
        facePreview = null;
    }

    private void ClearView()
    {
        StopFaceCamera();
        messageText = null;
        codeInput = null;
        teacherUserInput = null;
        teacherPasswordInput = null;
        rememberLabel = null;
        rememberButton = null;
        for (int i = safeArea.childCount - 1; i >= 0; i--)
        {
            safeArea.GetChild(i).gameObject.SetActive(false);
            Destroy(safeArea.GetChild(i).gameObject);
        }
    }

    private void Header(string title, Action onBack, Action onChange)
    {
        if (onBack != null)
            Button("BackButton", safeArea, "← Volver", new Vector2(0.04f, 0.89f),
                new Vector2(0.31f, 0.96f), Color.white, onBack);
        if (onChange != null)
            Button("ChangeStudent", safeArea, "Cambiar estudiante", new Vector2(0.55f, 0.89f),
                new Vector2(0.96f, 0.96f), Color.white, onChange);
        Text("ScreenTitle", safeArea, title, 48f, new Vector2(0.06f, 0.78f),
            new Vector2(0.94f, 0.88f), TextAlignmentOptions.Left, Ink);
    }

    private RectTransform ScrollContent(Vector2 min, Vector2 max)
    {
        GameObject viewportObject = new GameObject("ScrollViewport", typeof(RectTransform), typeof(CanvasRenderer),
            typeof(Image), typeof(Mask), typeof(ScrollRect));
        viewportObject.transform.SetParent(safeArea, false);
        RectTransform viewport = viewportObject.GetComponent<RectTransform>();
        Stretch(viewport, min, max);
        viewportObject.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.01f);
        viewportObject.GetComponent<Mask>().showMaskGraphic = false;

        GameObject contentObject = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup),
            typeof(ContentSizeFitter));
        contentObject.transform.SetParent(viewport, false);
        RectTransform content = contentObject.GetComponent<RectTransform>();
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = Vector2.one;
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = Vector2.zero;
        VerticalLayoutGroup layout = contentObject.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(8, 8, 12, 24);
        layout.spacing = 18f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        contentObject.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        ScrollRect scroll = viewportObject.GetComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        return content;
    }

    private void Label(Transform parent, string value, float size, Color color)
    {
        GameObject item = new GameObject("ListText", typeof(RectTransform), typeof(CanvasRenderer),
            typeof(TextMeshProUGUI));
        item.transform.SetParent(parent, false);
        TextMeshProUGUI label = item.GetComponent<TextMeshProUGUI>();
        label.font = font;
        label.text = value;
        label.fontSize = size;
        label.color = color;
        label.enableWordWrapping = true;
        label.raycastTarget = false;
        label.margin = new Vector4(8f, 8f, 8f, 8f);
    }

    private void ListButton(Transform parent, string value, Action action)
    {
        Button button = Button("BookOrChapter", parent, value, Vector2.zero, Vector2.one, Color.white, action);
        button.GetComponent<RectTransform>().sizeDelta = Vector2.zero;
        LayoutElement element = button.gameObject.AddComponent<LayoutElement>();
        element.minHeight = 130f;
        element.preferredHeight = value.Length > 80 ? 180f : 150f;
        TMP_Text label = button.GetComponentInChildren<TMP_Text>();
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.margin = new Vector4(30f, 14f, 30f, 14f);
        label.color = Ink;
    }

    private static RectTransform Panel(string name, Transform parent, Color color, Vector2 min, Vector2 max)
    {
        GameObject item = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        item.transform.SetParent(parent, false);
        RectTransform rect = item.GetComponent<RectTransform>();
        Stretch(rect, min, max);
        item.GetComponent<Image>().color = color;
        return rect;
    }

    private TMP_Text Text(string name, Transform parent, string value, float size, Vector2 min, Vector2 max,
        TextAlignmentOptions alignment, Color color)
    {
        GameObject item = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        item.transform.SetParent(parent, false);
        TextMeshProUGUI label = item.GetComponent<TextMeshProUGUI>();
        Stretch(label.rectTransform, min, max);
        label.font = font;
        label.text = value;
        label.fontSize = size;
        label.enableAutoSizing = true;
        label.fontSizeMin = Mathf.Max(24f, size * 0.65f);
        label.fontSizeMax = size;
        label.alignment = alignment;
        label.color = color;
        label.enableWordWrapping = true;
        label.raycastTarget = false;
        return label;
    }

    private Button Button(string name, Transform parent, string label, Vector2 min, Vector2 max,
        Color color, Action action)
    {
        GameObject item = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        item.transform.SetParent(parent, false);
        RectTransform rect = item.GetComponent<RectTransform>();
        Stretch(rect, min, max);
        Image background = item.GetComponent<Image>();
        background.sprite = QRCodeScanner.GetMessageCardSprite();
        background.type = Image.Type.Sliced;
        background.color = color;
        Button button = item.GetComponent<Button>();
        button.targetGraphic = background;
        button.onClick.AddListener(() => action?.Invoke());
        TMP_Text caption = Text("Label", item.transform, label, 34f,
            new Vector2(0.04f, 0.04f), new Vector2(0.96f, 0.96f), TextAlignmentOptions.Center,
            color == Color.white ? Accent : Color.white);
        caption.fontStyle = FontStyles.Bold;
        return button;
    }

    private TMP_InputField Input(string name, Transform parent, string placeholder, Vector2 min, Vector2 max)
    {
        GameObject item = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(TMP_InputField));
        item.transform.SetParent(parent, false);
        Stretch(item.GetComponent<RectTransform>(), min, max);
        Image background = item.GetComponent<Image>();
        background.sprite = QRCodeScanner.GetMessageCardSprite();
        background.type = Image.Type.Sliced;
        background.color = Color.white;
        TMP_Text inputText = Text("InputText", item.transform, string.Empty, 40f,
            new Vector2(0.05f, 0.04f), new Vector2(0.95f, 0.96f), TextAlignmentOptions.Left, Ink);
        TMP_Text hint = Text("Placeholder", item.transform, placeholder, 35f,
            new Vector2(0.05f, 0.04f), new Vector2(0.95f, 0.96f), TextAlignmentOptions.Left, Muted);
        TMP_InputField field = item.GetComponent<TMP_InputField>();
        field.textComponent = inputText;
        field.textViewport = inputText.rectTransform;
        field.placeholder = hint;
        field.characterLimit = 10;
        field.contentType = TMP_InputField.ContentType.Alphanumeric;
        field.lineType = TMP_InputField.LineType.SingleLine;
        return field;
    }

    private static void Stretch(RectTransform rect, Vector2 min, Vector2 max)
    {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
