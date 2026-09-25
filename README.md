# BibliotecaAR — Aplicación Android de realidad aumentada

Cliente Unity de un proyecto educativo de tesis: al escanear el QR de un capítulo de libro, consulta su contenido y muestra un modelo 3D sobre la imagen reconocida, junto con texto narrativo y audio opcional. Los docentes preparan los contenidos en un panel web independiente.

Documentación contrastada con código y escenas el **24 de septiembre de 2026**. Es un MVP. La presencia de scripts no garantiza que todos los flujos estén validados en un dispositivo; esta guía distingue funciones implementadas y pendientes.

## 1. Proyecto completo

| Repositorio | Responsabilidad |
| --- | --- |
| [BibliotecaAR](https://github.com/AlejandroPhoccoTapia/BibliotecaAR) | Este cliente: cámara, QR, seguimiento AR, modelos y audio. |
| [BibliotecaAR-backend](https://github.com/AlejandroPhoccoTapia/BibliotecaAR-backend) | Django: API, catálogo, estudiantes, sesiones docentes, QR, identificación facial experimental y archivos. |
| [BiblitoecaAR-fronted](https://github.com/AlejandroPhoccoTapia/BiblitoecaAR-fronted) | React: panel docente. El enlace conserva la grafía actual del repositorio. |

```text
Docente -> React -> Django -> base de datos / storage
                     ^
                     | GET /api/unity/scenes/<qr_code>/
Android: QRScanScene (acceso, biblioteca y escáner) -> ScannedQRData.LastCode -> ARScene
                                                   |
                                      texto + audio + modelo 3D
                                                   |
                                    seguimiento de la imagen QR
```

**Vocabulario:** el «capítulo» del panel es un registro `Scene` en Django. Las escenas Unity `QRScanScene` y `ARScene` son pantallas/entornos, no capítulos. El QR contiene un identificador, no una URL ni el modelo. `prefab_key` referencia contenido local; `glb_model_url` permite descargarlo.

El despliegue previsto utiliza Vercel para el panel, Render para Django y Supabase para PostgreSQL/Storage. En desarrollo, Django usa SQLite y archivos locales por defecto. Unity consume la API y las URLs de recursos, no la base de datos directamente.

## 2. Tecnologías y requisitos

| Elemento | Configuración del repositorio |
| --- | --- |
| Editor | Unity `6000.3.19f1`, según `ProjectSettings/ProjectVersion.txt`. |
| AR Foundation / ARCore | `6.3.5`, según `Packages/manifest.json`. |
| glTFast | `com.unity.cloud.gltfast` `6.7.1`; importación GLB en ejecución. |
| Input System | `1.19.0`. |
| QR | DLL ZXing en `Assets/Plugins/ZXing/`. |
| Interfaz | Unity UI y TextMesh Pro. |
| Plataforma preparada | Android con ARCore; no hay proveedor ARKit declarado para iOS. |
| Android mínimo | API 29; target SDK automático en los ajustes guardados. |
| Compilación Android | IL2CPP y ARM64 en la configuración guardada. |

Instalar esa versión mediante Unity Hub con Android Build Support, SDK/NDK/OpenJDK. Para validar AR, usar un teléfono compatible con ARCore y conceder permiso de cámara. Play Mode no sustituye la prueba móvil; la configuración Standalone guardada no tiene loaders activos.

Hay reglas Git LFS para imágenes, modelos, audio, fuentes y DLL. En una clonación nueva:

```powershell
git lfs install
git lfs pull
```

Si fallan importaciones, comprobar que los recursos sean archivos reales y no punteros LFS. Revisar primero el estado Git; no sobrescribir archivos modificados para resolverlo sin entender su contenido.

## 3. Estructura

```text
Assets/
  Scenes/
    QRScanScene.unity       Escáner y vista previa de cámara
    ARScene.unity           Entorno XR, contenido y controles
  Scripts/
    QRCodeScanner.cs        Permiso/cámara, ZXing y transición
    StudentAppFlow.cs       Acceso por código/rostro, biblioteca y lectura sin QR
    StudentAppSession.cs    Sesión, modelos JSON y peticiones a la API estudiantil
    ScannedQRData.cs        LastCode estático entre escenas
    ARSceneController.cs    API, selección, GLB, audio y QR dinámico
    ARSceneExperienceUI.cs Estados AR, texto desplazable y controles accesibles
    QRTrackedImagePlacer.cs Colocación/visibilidad sobre imagen
    ARTrackedVisibility.cs  Transición suave al perder/recuperar seguimiento
    ARStoryInteraction.cs   Toque del modelo y clips Idle/Walk
    ARPlacementEditorUI.cs  Ajuste docente de tamaño y posición en el teléfono
    TeacherPreviewSession.cs Token docente temporal y peticiones de ajuste
    ARRaycastPlaceObject.cs Alternativa mediante toque/raycast
    ARPlaneDebugLogger.cs   Diagnóstico XR y planos
    ZXingTest.cs            Log de disponibilidad de ZXing
  QRTargets/               QR demo y QRReferenceImageLibrary
  Prefabs/                 Cubos, hormiga y plano AR
  Models/                  Modelo local de hormiga y material
  Audio/                   Hormiga.mp3 de demostración
  Plugins/ZXing/           Biblioteca QR
  XR/                      Loaders y configuración ARCore/simulación
  TextMesh Pro/            Recursos de interfaz
Packages/                  Dependencias y lockfile
ProjectSettings/           Editor, Android, gráficos y escenas de build
```

## 4. Flujo de ejecución

### Escaneo

`QRCodeScanner.Start()` crea `StudentAppFlow` y espera a que el estudiante pulse «Escanear QR» antes de pedir permiso de cámara. La pantalla guía al usuario para centrar el QR en un marco con esquinas animadas. Su tamaño se calcula a partir del área útil de la pantalla (aproximadamente dos tercios del ancho en vertical) y se reajusta si cambia la orientación o el área segura; la cámara sigue ocupando toda la pantalla. Las instrucciones y el estado aparecen en tarjetas claras con texto oscuro para mantener el contraste sobre la imagen. Los estados distinguen permiso, preparación, búsqueda, lectura y errores de cámara. Si la cámara falla, puede volver a intentar desde la pantalla; si el permiso está bloqueado, la instrucción indica activarlo en Ajustes. Al leer el QR confirma el éxito sin mostrar el identificador técnico. ZXing intenta leer códigos cada `0.25` segundos por defecto. La vista previa ajusta rotación, espejo y proporción en móvil.

Al leer un código nuevo, guarda `ScannedQRData.LastCode`, detiene el escaneo según configuración y carga `ARScene` tras `0.75` segundos por defecto. La cámara del escáner se detiene antes de la transición. `LastCode` es memoria estática para el QR; la sesión y el progreso del estudiante se gestionan aparte.

### Acceso y biblioteca del estudiante

En `QRScanScene`, `StudentAppFlow` presenta acceso alternativo por código personal o rostro. El docente crea/restablece el código desde el panel. La app obtiene un token Bearer de `/api/student/code-login/` o `/api/student/face-login/`. La opción «Recordar en este dispositivo» está desactivada por defecto para equipos compartidos; si se activa, guarda el token en `PlayerPrefs` y lo valida con `/api/student/me/` al volver a abrir. «Cambiar estudiante» revoca la sesión actual y limpia el dispositivo. El reconocimiento facial del backend es experimental; el código siempre queda como alternativa.

`GET /api/student/library/` entrega «Mis libros» (asignados publicados), «Explorados» (publicados no asignados que abrió) y el último capítulo. `GET /api/student/books/<id>/` entrega capítulos con texto, audio y estado, sin modelo AR. Abrir un capítulo registra `/api/student/chapters/<id>/open/`; «Terminé» llama a `/complete/`. Así, «Retomar» abre la información del último capítulo incluso en otro teléfono. Desde esa lectura, «Ver en AR» abre el escáner: el modelo requiere leer el QR físico. Los borradores no se muestran.

`studentApiBaseUrl` de `QRCodeScanner` configura la API de acceso y progreso; por defecto usa `https://bibliotecaar-backend.onrender.com/api`. Para desarrollo local en un teléfono, cambiarlo a la IP LAN del backend y mantener coherente `apiBaseUrl` de `ARSceneController`. Ambas rutas necesitan internet o red local accesible.

### Vista docente y calibración física

Desde la pantalla de acceso o la biblioteca estudiantil, «Docente» permite entrar con una cuenta `is_staff=True` del backend. El token temporal se mantiene solo en memoria y dura como máximo 30 minutos. El docente escanea el QR impreso, incluso si el libro sigue en borrador, y pulsa «Ajustar modelo». Un panel compacto en la parte inferior muestra un ajuste por vez: las flechas cambian entre ancho del marcador, tamaño máximo del modelo, desplazamiento lateral/vertical/sobre la página y giro; «−» y «+» cambian el valor seleccionado. Durante el ajuste se oculta la tarjeta de lectura para dejar libre la vista de cámara; el modelo responde en directo y «Ver modelo» oculta el panel sin desplegar la lectura. «Guardar» envía los valores a Django; «Deshacer» recupera la última versión guardada. El ancho del marcador entra en vigor cuando se vuelve a escanear y se crea la referencia AR. Si ese QR ya viene dentro de la biblioteca estática de Unity, hay que corregir también su tamaño físico en el asset de referencia o quitar esa entrada: una referencia existente no se reemplaza dinámicamente.

El ancho del marcador debe medirse en el QR **impreso**, de lado a lado de la imagen que ARCore reconoce. `ar_model_size_cm` define la dimensión mayor de la geometría (alto, ancho o profundidad), no cada eje por separado. Unity mide los límites del modelo, centra su base sobre el marcador y calcula una escala para obtener esos centímetros; por ello modelos importados en unidades distintas terminan con una referencia física comparable. Los desplazamientos están en centímetros respecto al centro del QR; el giro está en grados alrededor del eje vertical. Cambiar el tamaño del marcador no cambia el tamaño configurado del modelo: son ajustes independientes. Probar con el teléfono sobre la página real antes de publicar.

### Contenido

`ARSceneController` construye diccionarios de contenido/bindings locales y consulta:

```http
GET <apiBaseUrl>/unity/scenes/<qr_code>/
```

Escapa el código para la URL. Convierte la respuesta en `SceneContent` y aplica título, texto, audio y prefab local disponible. Después intenta cargar GLB remoto y añadir la imagen QR a la biblioteca de seguimiento.

La pantalla indica búsqueda, preparación, errores de red, código inexistente/no publicado y fallos del modelo, con reintento para operaciones recuperables. Si falla la API y hay contenido local con el mismo código, lo muestra avisando que es una copia de demostración. La lectura aparece en una tarjeta clara de alto contraste que se puede plegar con «Ocultar» y volver a abrir con «Leer» para dejar más espacio a la cámara. El texto se desplaza y la indicación de deslizamiento solo aparece cuando hay contenido fuera de la vista. El audio muestra botones con iconos para reproducir y pausar cuando existe un clip; no empieza sin acción del usuario. La interfaz usa el área segura y adapta la distribución vertical u horizontal.

Al cargar un capítulo remoto con sesión estudiantil, AR registra `/api/student/qr/<qr_code>/open/`. El botón «Terminé» registra `/complete/` y confirma «Leído» al recibir respuesta. Las asignaciones organizan la biblioteca, pero un QR de otro libro publicado también funciona y queda en «Explorados». El endpoint Unity heredado sigue siendo público; no usar la asignación como control de acceso.

Si falla la API o el JSON y `fallbackToLocalContent` está activo, busca contenido local. Ante código desconocido muestra «Contenido no encontrado». El fallback no es una caché persistente: solo conoce los datos/prefabs locales incluidos.

### Seguimiento y modelo

`ARTrackedImageManager` reconoce referencias. `QRTrackedImagePlacer` compara su nombre con el código escaneado, instancia el recurso como hijo de la imagen y actualiza posición/rotación. Ante pérdida breve de seguimiento conserva el modelo 0,45 s y luego lo reduce suavemente; cuando vuelve el seguimiento lo muestra de nuevo. El texto y audio permanecen disponibles y la tarjeta de lectura se abre para continuar aunque el modelo o el seguimiento fallen.

El estado del QR seguido se comunica a la interfaz. Si no hay seguimiento, se guía al usuario para volver a encuadrar el impreso, moverse lentamente y mejorar la iluminación.

Leer el texto QR con ZXing y estimar su pose con ARCore son pasos diferentes. Un QR legible no garantiza una imagen aceptada o seguida por ARCore.

Para capítulos de Django, el controlador descarga `qr_image_url` y usa `ScheduleAddImageWithValidationJob` en una biblioteca mutable. Si la biblioteca no admite esa operación o se rechaza la imagen, los logs registran el problema y no se garantiza la colocación.

glTFast carga `glb_model_url`, instancia un objeto raíz y lo entrega al colocador. Los ajustes por capítulo anteriores se aplican tanto al GLB como al prefab local. Si falla la descarga, la alternativa local depende de que haya un prefab configurado; no toda clave recibida tiene necesariamente un binding.

Un toque sobre el modelo activa la interacción: con un clip GLB llamado `Walk`, `Walking` o `Caminar`, lo reproduce y desplaza el modelo 2,5 cm sobre la página. Al terminar intenta volver a `Idle`, `Quieto` o `Stand`. Un OBJ/prefab estático también se desplaza, pero sus patas no se animan: para una hormiga que camina de verdad hay que subir un GLB con rig/huesos y esos clips exportados. La API almacena el archivo, no crea animaciones automáticamente. Conviene que el clip de caminar sea un ciclo sobre el sitio, pues el desplazamiento lo hace Unity. La detección del toque usa un colisionador construido a partir de los límites visuales del modelo.

### Audio y navegación

El controlador escribe título/narración en TextMesh Pro. Reproduce el clip local o descarga audio mediante `UnityWebRequestMultimedia.GetAudioClip`. Expone `PlayAudio()`, `PauseAudio()` y `BackToScanner()` para los controles. Este último carga `QRScanScene`.

## 5. Estado de las escenas guardadas

Orden habilitado en `EditorBuildSettings.asset`:

1. `Assets/Scenes/QRScanScene.unity`.
2. `Assets/Scenes/ARScene.unity`.

En ARScene, `ARTrackedImageManager` y `QRTrackedImagePlacer` están habilitados. `ARPlaneManager`, `ARRaycastPlaceObject` y `ARPlaneDebugLogger` están deshabilitados. La escena está preparada para colocar contenido sobre imagen. Habilitar a la vez el modo de toque requiere revisar su coordinación.

La escena tiene referencias de texto/audio y tres entradas locales:

| Código demo | Título local |
| --- | --- |
| `libro_001_escena_001` | Amarillo |
| `libro_001_escena_002` | Verde |
| `libro_002_escena_002` | Rojo |

`EnsureDefaultContents()` tiene otros seis textos de ejemplo, pero solo los añade si `contents` está vacío. No confundirlos con las tres entradas serializadas.

Varios campos de API/GLB añadidos al script no aparecen serializados en el YAML revisado. Comprobar sus valores efectivos en el Inspector y guardar la escena al configurar. Cambiar un inicializador C# no garantiza sobrescribir valores guardados por Unity.

## 6. Configuración del Inspector

En el objeto con `ARSceneController`:

| Campo | Inicializador del script / significado |
| --- | --- |
| `loadContentFromApi` | `true`. |
| `apiBaseUrl` | `https://bibliotecaar-backend.onrender.com/api`; también queda guardada explícitamente en `ARScene`. |
| `apiTimeoutSeconds` | `75`; da margen al arranque del servicio gratuito de Render en peticiones API/audio/imagen. La espera de ARSession se limita a 15 segundos. No es timeout general de glTFast. |
| `fallbackToLocalContent` | `true`. |
| `addTrackingImageFromApi` | `true`. |
| `trackingImagePhysicalWidthMeters` | `0.06`; respaldo para APIs antiguas sin medida AR. Los capítulos actuales envían el ancho individual en centímetros. |
| `loadGlbModelFromApi` | `true`. |
| Ajustes físicos por capítulo | Llegan de la API (`ar_marker_width_cm`, `ar_model_size_cm`, desplazamientos y giro); no se calibran en el Inspector global. |
| `prefabBindings` | Mapeo de claves a prefabs locales. |
| `fallbackPrefab` | Alternativa local opcional. |

La app móvil consulta directamente la API de Render; Django lee los capítulos publicados desde la base de datos configurada en el servidor. La URL Vercel del panel no es la API.

Local: cambiar `apiBaseUrl` temporalmente a la IP LAN del ordenador, usar la misma red, Django en `0.0.0.0:8000` y puerto permitido. `localhost` en Android apunta al teléfono. Los ajustes guardados permiten HTTP inseguro para desarrollo; la configuración predeterminada usa HTTPS.

No hay archivo `.env` ni pantalla de ajustes que configure automáticamente esta URL. No incluir credenciales del servidor en el cliente.

## 7. Contrato JSON

Ejemplo ilustrativo con nombres usados en `UnitySceneApiResponse`:

```json
{
  "qr_code": "libro-demo-scene-a1b2c3d4e5",
  "book_title": "Libro demo",
  "title": "La hormiga",
  "order": 1,
  "text": "Texto narrativo.",
  "prefab_key": "Hormiga",
  "cover_url": null,
  "audio_url": "https://media.example/scenes/audio/hormiga.mp3",
  "glb_model_url": "https://media.example/scenes/models/hormiga.glb",
  "qr_image_url": "https://media.example/scenes/qr/libro-demo-scene-a1b2c3d4e5.png",
  "ar_marker_width_cm": 6,
  "ar_model_size_cm": 8,
  "ar_offset_x_cm": 0,
  "ar_offset_y_cm": 0.5,
  "ar_offset_z_cm": 0,
  "ar_yaw_degrees": 0
}
```

Endpoint público: solo libros publicados; código inexistente/libro borrador produce 404. Recursos ausentes son null. `cover_url` existe en el DTO, pero el controlador no implementa la carga de portada. La vista docente usa `GET/PATCH /api/teacher/mobile/scenes/<qr_code>/` con `Authorization: TeacherPreview <token>` y solo modifica los seis campos de ajuste físico.

Supabase puede servir archivos, pero Unity no necesita claves ni acceso directo a PostgreSQL. La API no convierte modelos ni sintetiza audio: se suben recursos preparados.

## 8. Inicio y prueba del sistema completo

1. Clonar los tres repositorios y leer sus README.
2. Instalar Django, aplicar migraciones, crear docente y arrancar API según el README backend.
3. En el panel ejecutar `npm ci`, `npm run dev` e iniciar sesión.
4. Crear libro publicado y capítulo con texto, un `prefab_key` local o un GLB válido, y audio opcional. Obtener su QR.
5. Comprobar JSON de `/api/unity/scenes/<qr_code>/` y acceso a cada archivo desde la red del teléfono.
6. Abrir con Unity 6000.3.19f1, restaurar paquetes y resolver cualquier error de importación/compilación.
7. Configurar API, bindings y UI/audio en el Inspector; medir y guardar el ancho del QR impreso por capítulo.
8. Seleccionar Android en Build Profiles. Revisar ARCore en XR Plug-in Management, IL2CPP, ARM64, SDK e identificador de aplicación. El proyecto conserva valores de identidad por defecto que deben revisarse antes de distribuir.
9. Incluir las dos escenas en orden, compilar e instalar en Android ARCore.
10. Iniciar en QRScanScene, dar permiso, escanear y comprobar texto, audio, modelo, escala, movimiento, pérdida/recuperación del tracking y vuelta al escáner.

Para probar contenido local, desactivar explícitamente `loadContentFromApi` en una configuración de prueba y usar QR demo. Para probar solo el flujo remoto, desactivar fallback y usar un QR de Django. Una demostración local no valida la API.

## 9. Diagnóstico

Consultar consola y Android Logcat; los logs usan prefijos `QRCodeScanner:`, `ARSceneController:` y `QRTrackedImagePlacer:`.

| Síntoma | Revisar |
| --- | --- |
| Sin cámara | Permiso, dispositivo, WebCamTexture y cameraPreview. |
| QR leído sin capítulo | LastCode, URL, red, timeout, JSON y publicación. |
| Capítulo sin modelo | URL GLB, logs glTFast, binding y tracking. |
| QR dinámico sin tracking | Validación de imagen, biblioteca mutable, nombre y ancho físico. |
| Tamaño/orientación incorrectos | Medir QR impreso; abrir vista docente en móvil y ajustar tamaño, posición y giro; revisar si el QR ya está en la biblioteca estática. |
| Materiales incorrectos en Android | Importación y shaders glTFast disponibles en el build; revisar GraphicsSettings. |
| Toque no coloca objetos | El script y los planos están deshabilitados en esta escena. |
| Fallo solo remoto | URL real, acceso público a recursos, HTTPS y respuesta del servidor. |

Abrir ARScene directamente sin escaneo previo no aporta un código válido. Verificar el flujo desde la primera escena antes de atribuir «Contenido no encontrado» a la API.

## 10. Límites y pendientes

- La comparación facial LBP es experimental y no comprueba presencia real; validar con fotos y teléfonos reales antes de usarla como identificación confiable.
- «Recordar en este dispositivo» persiste el token en `PlayerPrefs`, no en el almacén seguro del sistema. Reforzar ese almacenamiento antes de uso con datos personales reales.
- El endpoint Unity heredado es público; conocer un QR publicado permite consultar su escena. Las asignaciones no restringen ese acceso.
- El progreso lector se guarda en el backend; no hay evaluaciones, analítica educativa ni biblioteca descargada para uso offline completo.
- La normalización usa los límites del modelo en reposo; animaciones que extiendan mucho una extremidad pueden requerir ajuste manual en móvil.
- El modo docente usa un token temporal en memoria; el QR de una biblioteca estática conserva el ancho configurado en el asset de Unity.
- Validar en dispositivo tracking, formatos de audio, materiales y ciclo de carga/liberación de recursos.
- No hay pruebas automatizadas propias de estos flujos; `ZXingTest` solo escribe un log.
- El modo de raycast es alternativo: al activarlo, probar también qué ocurre si el GLB termina de descargarse después de colocar un objeto.
- El panel tiene problemas de listas multipart y valores vacíos; consultar su README antes de atribuir esos fallos a Unity.

Los scripts C# se compilaron con el compilador Roslyn y las referencias del proyecto Unity, sin errores. No se compiló ni ejecutó la aplicación Android al redactar esta documentación, ni se comprobó el servicio remoto. La validación integral requiere un dispositivo real y los tres componentes.

## 11. Guía para el siguiente asistente

1. Leer los tres README y revisar `git status` por repositorio; preservar cambios ajenos a la tarea.
2. Leer en orden StudentAppFlow, StudentAppSession, QRCodeScanner, ScannedQRData, ARSceneController y QRTrackedImagePlacer.
3. Examinar también valores serializados en .unity/.asset; los inicializadores C# no describen solos el comportamiento.
4. Preservar .meta y GUID. No actualizar editor/paquetes ni regenerar escenas sin necesidad.
5. Coordinar cambios de contrato con serializers Django y UnitySceneApiResponse; no reinterpretar QR como URLs.
6. No confundir la comparación facial experimental con presencia real ni el fallback local con progreso sincronizado.
7. Informar pruebas de editor, Android y API por separado y actualizar esta guía al completar funcionalidades.
