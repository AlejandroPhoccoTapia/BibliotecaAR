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
Android: QRScanScene -> ScannedQRData.LastCode -> ARScene
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
    ScannedQRData.cs        LastCode estático entre escenas
    ARSceneController.cs    API, selección, GLB, audio y QR dinámico
    QRTrackedImagePlacer.cs Colocación/visibilidad sobre imagen
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

`QRCodeScanner.Start()` pide permiso, elige preferentemente cámara trasera e inicia `WebCamTexture`. La pantalla guía al usuario para centrar el QR en un marco animado; los estados distinguen permiso, preparación, búsqueda, lectura y errores de cámara. Al leerlo confirma el éxito sin mostrar el identificador técnico. ZXing intenta leer QR cada `0.25` segundos por defecto. La vista previa ajusta rotación, espejo y proporción en móvil.

Al leer un código nuevo, guarda `ScannedQRData.LastCode`, detiene el escaneo según configuración y carga `ARScene` tras `0.75` segundos por defecto. La cámara del escáner se detiene antes de la transición. `LastCode` es memoria estática: no es sesión de estudiante ni persistencia entre reinicios.

### Contenido

`ARSceneController` construye diccionarios de contenido/bindings locales y consulta:

```http
GET <apiBaseUrl>/unity/scenes/<qr_code>/
```

Escapa el código para la URL. Convierte la respuesta en `SceneContent` y aplica título, texto, audio y prefab local disponible. Después intenta cargar GLB remoto y añadir la imagen QR a la biblioteca de seguimiento.

Si falla la API o el JSON y `fallbackToLocalContent` está activo, busca contenido local. Ante código desconocido muestra «Contenido no encontrado». El fallback no es una caché persistente: solo conoce los datos/prefabs locales incluidos.

### Seguimiento y modelo

`ARTrackedImageManager` reconoce referencias. `QRTrackedImagePlacer` compara su nombre con el código escaneado, instancia el recurso como hijo de la imagen y actualiza posición/rotación. Puede ocultarlo cuando deja de estar en estado Tracking.

Leer el texto QR con ZXing y estimar su pose con ARCore son pasos diferentes. Un QR legible no garantiza una imagen aceptada o seguida por ARCore.

Para capítulos de Django, el controlador descarga `qr_image_url` y usa `ScheduleAddImageWithValidationJob` en una biblioteca mutable. Si la biblioteca no admite esa operación o se rechaza la imagen, los logs registran el problema y no se garantiza la colocación.

glTFast carga `glb_model_url`, instancia un objeto raíz y lo entrega al colocador. Escala y orientación son parámetros globales del controlador, no metadatos por capítulo de la API. Si falla, la alternativa local depende de que haya un prefab configurado; no toda clave recibida tiene necesariamente un binding.

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
| `apiBaseUrl` | `http://192.168.1.48:8000/api`: adaptar a la instalación. |
| `apiTimeoutSeconds` | `10`; usado en peticiones API/audio/imagen y esperas concretas. No es timeout general de glTFast. |
| `fallbackToLocalContent` | `true`. |
| `addTrackingImageFromApi` | `true`. |
| `trackingImagePhysicalWidthMeters` | `0.06`; ajustarlo al ancho físico real de la imagen impresa. |
| `loadGlbModelFromApi` | `true`. |
| `runtimeGlbScale` | `0.02`. |
| `runtimeGlbLocalOffset` | `(0, 0.005, 0)`. |
| `runtimeGlbLocalEulerAngles` | `(0, 0, 0)`. |
| `prefabBindings` | Mapeo de claves a prefabs locales. |
| `fallbackPrefab` | Alternativa local opcional. |

Remoto: `https://<tu-backend>.onrender.com/api`. La URL Vercel del panel no es la API.

Local: misma red para teléfono/ordenador, IP LAN del ordenador, Django en `0.0.0.0:8000` y puerto permitido. `localhost` en Android apunta al teléfono. Los ajustes guardados permiten HTTP inseguro para desarrollo; el despliegue remoto previsto usa HTTPS.

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
  "qr_image_url": "https://media.example/scenes/qr/libro-demo-scene-a1b2c3d4e5.png"
}
```

Endpoint público: solo libros publicados; código inexistente/libro borrador produce 404. Recursos ausentes son null. `cover_url` existe en el DTO, pero el controlador no implementa la carga de portada. No hay transformaciones 3D por capítulo en este contrato.

Supabase puede servir archivos, pero Unity no necesita claves ni acceso directo a PostgreSQL. La API no convierte modelos ni sintetiza audio: se suben recursos preparados.

## 8. Inicio y prueba del sistema completo

1. Clonar los tres repositorios y leer sus README.
2. Instalar Django, aplicar migraciones, crear docente y arrancar API según el README backend.
3. En el panel ejecutar `npm ci`, `npm run dev` e iniciar sesión.
4. Crear libro publicado y capítulo con texto, `prefab_key`, GLB válido y audio opcional. Obtener su QR.
5. Comprobar JSON de `/api/unity/scenes/<qr_code>/` y acceso a cada archivo desde la red del teléfono.
6. Abrir con Unity 6000.3.19f1, restaurar paquetes y resolver cualquier error de importación/compilación.
7. Configurar API, bindings, UI/audio y ancho de QR en el Inspector.
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
| Tamaño/orientación incorrectos | Transformación original del modelo y parámetros runtimeGlb. |
| Materiales incorrectos en Android | Importación y shaders glTFast disponibles en el build; revisar GraphicsSettings. |
| Toque no coloca objetos | El script y los planos están deshabilitados en esta escena. |
| Fallo solo remoto | URL real, acceso público a recursos, HTTPS y respuesta del servidor. |

Abrir ARScene directamente sin escaneo previo no aporta un código válido. Verificar el flujo desde la primera escena antes de atribuir «Contenido no encontrado» a la API.

## 10. Límites y pendientes

- No hay identificación facial, sesión de estudiante ni consulta de libros asignados en Unity. El backend tiene comparación LBP experimental, todavía sin integración móvil.
- No existe autorización por estudiante. Conocer un QR publicado permite consultar su escena; las asignaciones del panel no restringen esa consulta.
- No hay progreso lector persistido, evaluaciones, analítica educativa ni biblioteca descargada para uso offline completo.
- La escala es global, sin editor de transformación por capítulo ni normalización automática de GLB.
- Validar en dispositivo tracking, formatos de audio, materiales y ciclo de carga/liberación de recursos.
- No hay pruebas automatizadas propias de estos flujos; `ZXingTest` solo escribe un log.
- El modo de raycast es alternativo: al activarlo, probar también qué ocurre si el GLB termina de descargarse después de colocar un objeto.
- El panel tiene problemas de listas multipart y valores vacíos; consultar su README antes de atribuir esos fallos a Unity.

No se compiló ni ejecutó la aplicación Android al redactar esta documentación, ni se comprobó el servicio remoto. La validación integral requiere un dispositivo real y los tres componentes.

## 11. Guía para el siguiente asistente

1. Leer los tres README y revisar `git status` por repositorio; preservar cambios ajenos a la tarea.
2. Leer en orden QRCodeScanner, ScannedQRData, ARSceneController y QRTrackedImagePlacer.
3. Examinar también valores serializados en .unity/.asset; los inicializadores C# no describen solos el comportamiento.
4. Preservar .meta y GUID. No actualizar editor/paquetes ni regenerar escenas sin necesidad.
5. Coordinar cambios de contrato con serializers Django y UnitySceneApiResponse; no reinterpretar QR como URLs.
6. No confundir fallback/demo o módulos faciales del backend con integración completa.
7. Informar pruebas de editor, Android y API por separado y actualizar esta guía al completar funcionalidades.
