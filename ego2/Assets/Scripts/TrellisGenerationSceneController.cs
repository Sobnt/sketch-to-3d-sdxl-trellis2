using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Oculus.Interaction;
using Oculus.Interaction.Surfaces;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class TrellisGenerationSceneController : MonoBehaviour
{
    [Header("Trellis Bridge")]
    public string trellisServerUrl = "http://172.16.101.15:5055";
    public string generateEndpoint = "/generate-3d";
    public string jobStatusEndpoint = "/jobs/{0}";
    public int requestTimeout = 900;
    public float pollingInterval = 2.0f;
    public int maxPollingSeconds = 900;
    public bool autoStartGeneration = true;

    [Header("Model Preview")]
    public bool tryLoadGlbWithGltfFast = true;
    public float targetModelHeight = 1.0f;
    public Vector3 modelDisplayPosition = new Vector3(0f, 1.05f, 0.55f);
    public float modelDistanceFromViewer = 1.25f;
    public float modelHorizontalOffset = 0.25f;
    public float modelVerticalOffset = -0.25f;

    [Header("Mixed Reality")]
    public bool enablePassthrough = true;
    public float panelDistanceFromViewer = 0.9f;
    public float panelHorizontalOffset = -0.55f;
    public float panelVerticalOffset = -0.1f;

    [Header("Controller shortcuts")]
    public OVRInput.RawButton backControllerButton = OVRInput.RawButton.B;

    [Header("Navigation")]
    public string mainSceneName = "SampleScene";

    private TextMeshProUGUI statusText;
    private RawImage inputPreview;
    private Button startButton;
    private Transform modelRoot;
    private QuestGeneratedModelManipulator modelManipulator;
    private Texture2D previewTexture;
    private bool generationInProgress;
    private bool returningToDrawingScene;
    private string lastModelPath;

    private void Start()
    {
        RemoveDrawingSceneUI();
        EnsureSceneBasics();
        BuildUI();
        LoadInputPreview();

        if (autoStartGeneration && TrellisGenerationPayload.HasImage)
        {
            StartGeneration();
        }
        else if (!TrellisGenerationPayload.HasImage)
        {
            SetStatus("Nessuna immagine selezionata dalla scena precedente.");
        }
    }

    private void OnDestroy()
    {
        if (previewTexture != null)
        {
            Destroy(previewTexture);
            previewTexture = null;
        }
    }

    private void Update()
    {
        if (!returningToDrawingScene && OVRInput.GetDown(backControllerButton))
        {
            ReturnToDrawingScene();
        }
    }

    public void StartGeneration()
    {
        if (generationInProgress)
        {
            return;
        }

        byte[] imageBytes = TrellisGenerationPayload.GetImageBytes();
        if (imageBytes == null || imageBytes.Length == 0)
        {
            SetStatus("Nessuna immagine valida da inviare a Trellis.");
            return;
        }

        StartCoroutine(GenerateModelRoutine(imageBytes));
    }

    private IEnumerator GenerateModelRoutine(byte[] imageBytes)
    {
        generationInProgress = true;
        SetStartButtonEnabled(false);
        ClearLoadedModel();
        SetStatus("Invio immagine a Trellis...");

        TrellisGenerateRequest requestData = new TrellisGenerateRequest
        {
            image = Convert.ToBase64String(imageBytes),
            file_name = Path.GetFileName(TrellisGenerationPayload.ImagePath),
            preferred_format = "glb"
        };

        string jsonBody = JsonUtility.ToJson(requestData);
        UnityWebRequest request = new UnityWebRequest(BuildUrl(generateEndpoint), "POST");
        request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody));
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.timeout = Mathf.Max(30, requestTimeout);

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            SetStatus("Errore Trellis: " + request.error);
            Debug.LogError("[TrellisScene] Errore richiesta Trellis: " + request.error);
            request.Dispose();
            FinishGeneration();
            yield break;
        }

        if (TrySaveDirectModelResponse(request, out string directModelPath))
        {
            request.Dispose();
            yield return HandleModelReady(directModelPath);
            FinishGeneration();
            yield break;
        }

        string responseText = request.downloadHandler.text;
        request.Dispose();

        TrellisBridgeResponse response = ParseResponse(responseText);
        if (response == null)
        {
            SetStatus("Risposta Trellis non valida.");
            Debug.LogWarning("[TrellisScene] Risposta non valida: " + responseText);
            FinishGeneration();
            yield break;
        }

        yield return HandleBridgeResponse(response);
        FinishGeneration();
    }

    private IEnumerator HandleBridgeResponse(TrellisBridgeResponse response)
    {
        string status = string.IsNullOrWhiteSpace(response.status) ? "" : response.status.ToLowerInvariant();

        if ((status == "queued" || status == "processing" || status == "running") && !string.IsNullOrWhiteSpace(response.job_id))
        {
            yield return PollJobRoutine(response.job_id);
            yield break;
        }

        if (!string.IsNullOrWhiteSpace(response.model_base64))
        {
            string path = SaveModelBytes(Convert.FromBase64String(RemoveDataUrlPrefix(response.model_base64)), response.file_name);
            yield return HandleModelReady(path);
            yield break;
        }

        string downloadUrl = FirstNonEmpty(response.download_url, response.model_url);
        if (!string.IsNullOrWhiteSpace(downloadUrl))
        {
            yield return DownloadModelRoutine(downloadUrl, response.file_name);
            yield break;
        }

        if (status == "success" && !string.IsNullOrWhiteSpace(response.model_path))
        {
            SetStatus("Trellis ha completato, ma serve un download_url per scaricare il GLB.");
            Debug.Log("[TrellisScene] model_path remoto: " + response.model_path);
            yield break;
        }

        string message = string.IsNullOrWhiteSpace(response.message) ? "Risposta Trellis senza modello." : response.message;
        SetStatus(message);
    }

    private IEnumerator PollJobRoutine(string jobId)
    {
        SetStatus("Generazione 3D in corso...");
        float startTime = Time.realtimeSinceStartup;

        while (Time.realtimeSinceStartup - startTime < maxPollingSeconds)
        {
            string endpoint = string.Format(jobStatusEndpoint, jobId);
            UnityWebRequest request = UnityWebRequest.Get(BuildUrl(endpoint));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = Mathf.Max(5, requestTimeout);

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                SetStatus("Errore polling Trellis: " + request.error);
                Debug.LogWarning("[TrellisScene] Errore polling: " + request.error);
                request.Dispose();
                yield return new WaitForSeconds(pollingInterval);
                continue;
            }

            TrellisBridgeResponse response = ParseResponse(request.downloadHandler.text);
            request.Dispose();

            if (response == null)
            {
                yield return new WaitForSeconds(pollingInterval);
                continue;
            }

            string status = string.IsNullOrWhiteSpace(response.status) ? "" : response.status.ToLowerInvariant();
            if (status == "success" || status == "completed" || status == "done")
            {
                yield return HandleBridgeResponse(response);
                yield break;
            }

            if (status == "failed" || status == "error")
            {
                string message = string.IsNullOrWhiteSpace(response.message) ? "Generazione 3D fallita." : response.message;
                SetStatus(message);
                yield break;
            }

            if (!string.IsNullOrWhiteSpace(response.message))
            {
                SetStatus(response.message);
            }

            yield return new WaitForSeconds(pollingInterval);
        }

        SetStatus("Timeout Trellis: generazione 3D troppo lunga.");
    }

    private IEnumerator DownloadModelRoutine(string downloadUrl, string fileName)
    {
        string url = BuildDownloadUrl(downloadUrl);
        SetStatus("Download modello GLB...");

        UnityWebRequest request = UnityWebRequest.Get(url);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.timeout = Mathf.Max(30, requestTimeout);

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            SetStatus("Errore download modello: " + request.error);
            Debug.LogError("[TrellisScene] Errore download modello: " + request.error);
            request.Dispose();
            yield break;
        }

        string path = SaveModelBytes(request.downloadHandler.data, fileName);
        request.Dispose();
        yield return HandleModelReady(path);
    }

    private IEnumerator HandleModelReady(string modelPath)
    {
        lastModelPath = modelPath;
        SetStatus("Modello GLB scaricato: " + Path.GetFileName(modelPath));

        if (!tryLoadGlbWithGltfFast)
        {
            yield break;
        }

        yield return TryLoadModelWithGltfFast(modelPath);
    }

    private IEnumerator TryLoadModelWithGltfFast(string modelPath)
    {
        Type importType = FindType("GLTFast.GltfImport");
        if (importType == null)
        {
            SetStatus("GLB scaricato. Per visualizzarlo in runtime installa glTFast nel progetto Unity.");
            yield break;
        }

        object importer = CreateGltfImportInstance(importType);
        if (importer == null)
        {
            SetStatus("glTFast trovato, ma non riesco a creare GltfImport.");
            yield break;
        }

        MethodInfo loadMethod = FindFirstParameterMethod(importType, "Load", typeof(string));
        object firstArgument = new Uri(modelPath).AbsoluteUri;

        if (loadMethod == null)
        {
            loadMethod = FindFirstParameterMethod(importType, "Load", typeof(Uri));
            firstArgument = new Uri(modelPath);
        }

        if (loadMethod == null)
        {
            SetStatus("glTFast trovato, ma il metodo Load non e' compatibile.");
            yield break;
        }

        SetStatus("Caricamento GLB in scena...");
        Task loadTask = InvokeTask(importer, loadMethod, firstArgument);
        if (loadTask == null)
        {
            SetStatus("Impossibile avviare il caricamento glTFast.");
            yield break;
        }

        yield return WaitForTask(loadTask);

        if (loadTask.IsFaulted)
        {
            SetStatus("Errore glTFast durante il caricamento.");
            Debug.LogException(loadTask.Exception);
            yield break;
        }

        if (!GetTaskBoolResult(loadTask, true))
        {
            SetStatus("glTFast non e' riuscito a leggere il GLB.");
            yield break;
        }

        MethodInfo instantiateMethod = FindFirstParameterMethod(importType, "InstantiateMainSceneAsync", typeof(Transform));
        if (instantiateMethod == null)
        {
            instantiateMethod = FindFirstParameterMethod(importType, "InstantiateMainScene", typeof(Transform));
        }

        if (instantiateMethod == null)
        {
            SetStatus("GLB caricato, ma glTFast non espone InstantiateMainScene.");
            yield break;
        }

        Task instantiateTask = InvokeTask(importer, instantiateMethod, modelRoot);
        if (instantiateTask != null)
        {
            yield return WaitForTask(instantiateTask);
            if (instantiateTask.IsFaulted)
            {
                SetStatus("Errore glTFast durante l'istanza del modello.");
                Debug.LogException(instantiateTask.Exception);
                yield break;
            }
        }

        FrameLoadedModel();
        SetStatus("Modello 3D pronto in scena.");
    }

    private object CreateGltfImportInstance(Type importType)
    {
        ConstructorInfo emptyConstructor = importType.GetConstructor(Type.EmptyTypes);
        if (emptyConstructor != null)
        {
            return emptyConstructor.Invoke(null);
        }

        ConstructorInfo[] constructors = importType.GetConstructors(BindingFlags.Instance | BindingFlags.Public);
        for (int i = 0; i < constructors.Length; i++)
        {
            ParameterInfo[] parameters = constructors[i].GetParameters();
            object[] arguments = new object[parameters.Length];
            bool canUseConstructor = true;

            for (int j = 0; j < parameters.Length; j++)
            {
                if (parameters[j].HasDefaultValue)
                {
                    arguments[j] = parameters[j].DefaultValue;
                }
                else if (!parameters[j].ParameterType.IsValueType)
                {
                    arguments[j] = null;
                }
                else
                {
                    canUseConstructor = false;
                    break;
                }
            }

            if (!canUseConstructor)
            {
                continue;
            }

            try
            {
                return constructors[i].Invoke(arguments);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[TrellisScene] Costruttore GltfImport non utilizzabile: " + exception.Message);
            }
        }

        return null;
    }

    private Task InvokeTask(object instance, MethodInfo method, params object[] leadingArguments)
    {
        object result = InvokeWithDefaults(instance, method, leadingArguments);
        return result as Task;
    }

    private object InvokeWithDefaults(object instance, MethodInfo method, params object[] leadingArguments)
    {
        ParameterInfo[] parameters = method.GetParameters();
        object[] arguments = new object[parameters.Length];

        for (int i = 0; i < parameters.Length; i++)
        {
            if (i < leadingArguments.Length)
            {
                arguments[i] = leadingArguments[i];
                continue;
            }

            if (parameters[i].HasDefaultValue)
            {
                arguments[i] = parameters[i].DefaultValue;
                continue;
            }

            arguments[i] = parameters[i].ParameterType.IsValueType
                ? Activator.CreateInstance(parameters[i].ParameterType)
                : null;
        }

        return method.Invoke(instance, arguments);
    }

    private IEnumerator WaitForTask(Task task)
    {
        while (!task.IsCompleted)
        {
            yield return null;
        }
    }

    private bool GetTaskBoolResult(Task task, bool defaultValue)
    {
        PropertyInfo resultProperty = task.GetType().GetProperty("Result");
        if (resultProperty == null)
        {
            return defaultValue;
        }

        object value = resultProperty.GetValue(task);
        return value is bool boolValue ? boolValue : defaultValue;
    }

    private Type FindType(string typeName)
    {
        Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (int i = 0; i < assemblies.Length; i++)
        {
            Type type = assemblies[i].GetType(typeName);
            if (type != null)
            {
                return type;
            }
        }

        return null;
    }

    private MethodInfo FindFirstParameterMethod(Type type, string methodName, Type firstParameterType)
    {
        MethodInfo[] methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public);
        for (int i = 0; i < methods.Length; i++)
        {
            if (methods[i].Name != methodName)
            {
                continue;
            }

            ParameterInfo[] parameters = methods[i].GetParameters();
            if (parameters.Length > 0 && parameters[0].ParameterType == firstParameterType)
            {
                return methods[i];
            }
        }

        return null;
    }

    private bool TrySaveDirectModelResponse(UnityWebRequest request, out string modelPath)
    {
        modelPath = "";
        byte[] data = request.downloadHandler != null ? request.downloadHandler.data : null;
        if (data == null || data.Length < 4)
        {
            return false;
        }

        bool isGlb = data[0] == (byte)'g' && data[1] == (byte)'l' && data[2] == (byte)'T' && data[3] == (byte)'F';
        string contentType = request.GetResponseHeader("Content-Type");
        bool isModelContent = !string.IsNullOrWhiteSpace(contentType) &&
                              contentType.IndexOf("model", StringComparison.OrdinalIgnoreCase) >= 0;

        if (!isGlb && !isModelContent)
        {
            return false;
        }

        modelPath = SaveModelBytes(data, "trellis_model.glb");
        return true;
    }

    private string SaveModelBytes(byte[] data, string fileName)
    {
        string folder = Path.Combine(Application.persistentDataPath, "TrellisModels");
        Directory.CreateDirectory(folder);

        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = $"trellis_model_{DateTime.Now:yyyyMMdd_HHmmss}.glb";
        }

        if (!fileName.EndsWith(".glb", StringComparison.OrdinalIgnoreCase))
        {
            fileName = Path.ChangeExtension(fileName, ".glb");
        }

        string path = Path.Combine(folder, fileName);
        if (File.Exists(path))
        {
            path = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(fileName)}_{DateTime.Now:yyyyMMdd_HHmmss}.glb");
        }

        File.WriteAllBytes(path, data);
        Debug.Log("[TrellisScene] Modello salvato in: " + path);
        return path;
    }

    private TrellisBridgeResponse ParseResponse(string responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            return null;
        }

        try
        {
            return JsonUtility.FromJson<TrellisBridgeResponse>(responseText);
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[TrellisScene] JSON Trellis non parsabile: " + exception.Message);
            return null;
        }
    }

    private string RemoveDataUrlPrefix(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        int commaIndex = value.IndexOf(',');
        return commaIndex >= 0 ? value.Substring(commaIndex + 1) : value;
    }

    private string FirstNonEmpty(string first, string second)
    {
        return !string.IsNullOrWhiteSpace(first) ? first : second;
    }

    private string BuildUrl(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return trellisServerUrl.TrimEnd('/');
        }

        if (endpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            endpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return endpoint;
        }

        return trellisServerUrl.TrimEnd('/') + "/" + endpoint.TrimStart('/');
    }

    private string BuildDownloadUrl(string downloadUrl)
    {
        return BuildUrl(downloadUrl);
    }

    private void FinishGeneration()
    {
        generationInProgress = false;
        SetStartButtonEnabled(true);
    }

    private void EnsureSceneBasics()
    {
        OVRCameraRig cameraRig = EnsureXRRigAndPassthrough();
        Camera camera = Camera.main;
        if (camera == null)
        {
            GameObject cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0f, 1.55f, -3f);
            cameraObject.transform.rotation = Quaternion.identity;
            camera = cameraObject.GetComponent<Camera>();
        }

        if (enablePassthrough && camera != null)
        {
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
        }

        if (FindFirstObjectByType<Light>() == null)
        {
            GameObject lightObject = new GameObject("Directional Light", typeof(Light));
            Light light = lightObject.GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        EnsureUIEventSystem();

        GameObject modelRootObject = new GameObject("GeneratedModelRoot");
        modelRootObject.transform.position = GetModelDisplayPosition();
        modelRoot = modelRootObject.transform;
        modelManipulator = modelRootObject.AddComponent<QuestGeneratedModelManipulator>();
        if (cameraRig != null)
        {
            modelManipulator.Configure(cameraRig.leftControllerAnchor, cameraRig.rightControllerAnchor);
        }
        modelManipulator.SetInteractable(false);
    }

    private void RemoveDrawingSceneUI()
    {
        GenerationHistoryUI[] drawingPanels = FindObjectsByType<GenerationHistoryUI>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        for (int i = 0; i < drawingPanels.Length; i++)
        {
            GenerationHistoryUI drawingPanel = drawingPanels[i];
            if (drawingPanel == null)
            {
                continue;
            }

            Debug.Log("[TrellisScene] Rimuovo pannello UI della scena di disegno rimasto in scena.");
            Destroy(drawingPanel.gameObject);
        }

        GameObject oldGeneratedCanvas = GameObject.Find("GeneratedRuntimeCanvas");
        if (oldGeneratedCanvas != null)
        {
            Destroy(oldGeneratedCanvas);
        }

        GameObject oldGenerationUI = GameObject.Find("AI_Generation_UI");
        if (oldGenerationUI != null)
        {
            Destroy(oldGenerationUI);
        }

        GameObject oldParametersCanvas = GameObject.Find("Slider");
        if (oldParametersCanvas != null && oldParametersCanvas.GetComponent<Canvas>() != null)
        {
            Destroy(oldParametersCanvas);
        }
    }

    private void BuildUI()
    {
        Camera camera = Camera.main;
        GameObject canvasObject = new GameObject("TrellisGenerationCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = camera;
        canvas.sortingOrder = 100;
        GraphicRaycaster graphicRaycaster = canvasObject.GetComponent<GraphicRaycaster>();
        graphicRaycaster.ignoreReversedGraphics = false;

        RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(820f, 580f);
        canvasRect.anchorMin = new Vector2(0.5f, 0.5f);
        canvasRect.anchorMax = new Vector2(0.5f, 0.5f);
        canvasRect.pivot = new Vector2(0.5f, 0.5f);
        canvasRect.anchoredPosition = Vector2.zero;
        PositionPanelInFrontOfViewer(canvasObject.transform);
        canvasObject.transform.localScale = Vector3.one * 0.001f;
        CreatePokeCanvasInteraction(canvasObject.transform, canvas, canvasRect.sizeDelta);

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 20f;

        GameObject panel = CreateRect("Panel", canvasObject.transform, new Vector2(820f, 580f));
        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.anchoredPosition = Vector2.zero;
        AddImage(panel, new Color(0.08f, 0.09f, 0.1f, 0.92f), false);

        VerticalLayoutGroup layout = panel.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(22, 22, 22, 22);
        layout.spacing = 14;
        layout.childControlWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = true;

        AddText("Title", panel.transform, "Generazione modello 3D", 28, FontStyles.Bold, TextAlignmentOptions.Center, 44);
        statusText = AddText("Status", panel.transform, "Preparazione scena Trellis...", 18, FontStyles.Normal, TextAlignmentOptions.Left, 56);

        GameObject previewRow = CreateRect("PreviewRow", panel.transform, new Vector2(0f, 245f));
        HorizontalLayoutGroup previewLayout = previewRow.AddComponent<HorizontalLayoutGroup>();
        previewLayout.spacing = 16;
        previewLayout.childControlHeight = true;
        previewLayout.childControlWidth = true;
        previewLayout.childForceExpandWidth = true;

        GameObject imageFrame = CreateRect("InputPreviewFrame", previewRow.transform, new Vector2(245f, 245f));
        AddImage(imageFrame, new Color(0.15f, 0.16f, 0.18f, 0.96f), false);
        inputPreview = CreateRawImage("InputPreview", imageFrame.transform, new Vector2(220f, 220f));
        inputPreview.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        inputPreview.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        inputPreview.rectTransform.anchoredPosition = Vector2.zero;

        AddText("Info", previewRow.transform, "Immagine selezionata", 17, FontStyles.Normal, TextAlignmentOptions.Center, 230);

        GameObject buttonsRow = CreateRect("ButtonsRow", panel.transform, new Vector2(0f, 52f));
        HorizontalLayoutGroup buttonsLayout = buttonsRow.AddComponent<HorizontalLayoutGroup>();
        buttonsLayout.spacing = 12;
        buttonsLayout.childControlWidth = true;
        buttonsLayout.childControlHeight = true;
        buttonsLayout.childForceExpandWidth = true;

        startButton = CreateButton("StartButton", buttonsRow.transform, "Rigenera 3D", StartGeneration);
        CreateButton("ResetModelButton", buttonsRow.transform, "Centra modello", ResetModelPose);
        CreateButton("BackButton", buttonsRow.transform, "Torna al disegno", ReturnToDrawingScene);
    }

    private void CreatePokeCanvasInteraction(Transform canvasTransform, Canvas canvas, Vector2 canvasSize)
    {
        GameObject interactionObject = CreateRect("TrellisPokeCanvasInteraction", canvasTransform, canvasSize);
        RectTransform interactionRect = interactionObject.GetComponent<RectTransform>();
        interactionRect.anchorMin = Vector2.zero;
        interactionRect.anchorMax = Vector2.one;
        interactionRect.offsetMin = Vector2.zero;
        interactionRect.offsetMax = Vector2.zero;

        LayoutElement layoutElement = interactionObject.AddComponent<LayoutElement>();
        layoutElement.ignoreLayout = true;

        PointableCanvas pointableCanvas = interactionObject.AddComponent<PointableCanvas>();
        pointableCanvas.InjectAllPointableCanvas(canvas);

        GameObject surfaceObject = CreateRect("Surface", interactionObject.transform, canvasSize);
        RectTransform surfaceRect = surfaceObject.GetComponent<RectTransform>();
        surfaceRect.anchorMin = Vector2.zero;
        surfaceRect.anchorMax = Vector2.one;
        surfaceRect.offsetMin = Vector2.zero;
        surfaceRect.offsetMax = Vector2.zero;

        PlaneSurface planeSurface = surfaceObject.AddComponent<PlaneSurface>();
        planeSurface.InjectAllPlaneSurface(PlaneSurface.NormalFacing.Backward, true);

        BoundsClipper boundsClipper = surfaceObject.AddComponent<BoundsClipper>();
        boundsClipper.Position = Vector3.zero;
        boundsClipper.Size = new Vector3(canvasSize.x, canvasSize.y, 0.01f);

        ClippedPlaneSurface clippedSurface = surfaceObject.AddComponent<ClippedPlaneSurface>();
        clippedSurface.InjectAllClippedPlaneSurface(
            planeSurface,
            new IBoundsClipper[] { boundsClipper });

        PokeInteractable pokeInteractable = interactionObject.AddComponent<PokeInteractable>();
        pokeInteractable.InjectOptionalPointableElement(pointableCanvas);
        pokeInteractable.InjectAllPokeInteractable(clippedSurface);
    }

    private void LoadInputPreview()
    {
        previewTexture = TrellisGenerationPayload.CreatePreviewTexture();
        if (previewTexture != null && inputPreview != null)
        {
            inputPreview.texture = previewTexture;
            inputPreview.color = Color.white;
            SetStatus("Immagine selezionata pronta. Avvio Trellis...");
        }
        else
        {
            SetStatus("Preview non disponibile.");
        }
    }

    private void ClearLoadedModel()
    {
        if (modelRoot == null)
        {
            return;
        }

        for (int i = modelRoot.childCount - 1; i >= 0; i--)
        {
            Destroy(modelRoot.GetChild(i).gameObject);
        }

        modelRoot.SetPositionAndRotation(GetModelDisplayPosition(), Quaternion.identity);
        modelRoot.localScale = Vector3.one;
        if (modelManipulator != null)
        {
            modelManipulator.SetInteractable(false);
        }
    }

    private void FrameLoadedModel()
    {
        if (modelRoot == null)
        {
            return;
        }

        Renderer[] renderers = modelRoot.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            return;
        }

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }

        float maxSize = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        if (maxSize > 0.0001f)
        {
            float scale = targetModelHeight / maxSize;
            modelRoot.localScale *= scale;
        }

        renderers = modelRoot.GetComponentsInChildren<Renderer>();
        bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }

        modelRoot.position += GetModelDisplayPosition() - bounds.center;

        renderers = modelRoot.GetComponentsInChildren<Renderer>();
        bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }

        if (modelManipulator != null)
        {
            modelManipulator.SetModelBounds(bounds);
        }
    }

    private OVRCameraRig EnsureXRRigAndPassthrough()
    {
        OVRCameraRig cameraRig = FindFirstObjectByType<OVRCameraRig>();
        OVRManager manager = FindFirstObjectByType<OVRManager>();

        if (cameraRig == null)
        {
            GameObject rigObject = new GameObject("OVRCameraRig");
            if (manager == null)
            {
                manager = rigObject.AddComponent<OVRManager>();
            }

            cameraRig = rigObject.AddComponent<OVRCameraRig>();
            Debug.Log("[TrellisScene] Rig XR creato per l'avvio diretto della scena Trellis.");
        }

        if (manager == null)
        {
            manager = cameraRig.GetComponent<OVRManager>();
            if (manager == null)
            {
                manager = cameraRig.gameObject.AddComponent<OVRManager>();
            }
        }

        if (enablePassthrough)
        {
            manager.isInsightPassthroughEnabled = true;
            OVRPassthroughLayer passthroughLayer = cameraRig.GetComponentInParent<OVRPassthroughLayer>();
            if (passthroughLayer == null)
            {
                passthroughLayer = cameraRig.gameObject.AddComponent<OVRPassthroughLayer>();
            }

            passthroughLayer.hidden = false;
        }

        Camera xrCamera = cameraRig.centerEyeAnchor != null
            ? cameraRig.centerEyeAnchor.GetComponent<Camera>()
            : cameraRig.GetComponentInChildren<Camera>(true);
        if (xrCamera != null)
        {
            xrCamera.tag = "MainCamera";
        }

        return cameraRig;
    }

    private Vector3 GetModelDisplayPosition()
    {
        Camera camera = Camera.main;
        if (camera == null)
        {
            return modelDisplayPosition;
        }

        GetViewerAxes(camera.transform, out Vector3 forward, out Vector3 right);
        return camera.transform.position +
               forward * modelDistanceFromViewer +
               right * modelHorizontalOffset +
               Vector3.up * modelVerticalOffset;
    }

    private void PositionPanelInFrontOfViewer(Transform panelTransform)
    {
        Camera camera = Camera.main;
        if (camera == null)
        {
            panelTransform.position = new Vector3(0f, 1.65f, 1f);
            panelTransform.rotation = Quaternion.identity;
            return;
        }

        GetViewerAxes(camera.transform, out Vector3 forward, out Vector3 right);
        panelTransform.position = camera.transform.position +
                                  forward * panelDistanceFromViewer +
                                  right * panelHorizontalOffset +
                                  Vector3.up * panelVerticalOffset;
        // Keep the visual front of the panel facing the same direction as the viewer.
        // The GraphicRaycaster is configured to accept the XR pointer from the opposite side.
        panelTransform.rotation = Quaternion.LookRotation(forward, Vector3.up);
    }

    private void EnsureUIEventSystem()
    {
        EventSystem eventSystem = FindFirstObjectByType<EventSystem>();
        if (eventSystem == null)
        {
            GameObject eventSystemObject = new GameObject("EventSystem");
            eventSystem = eventSystemObject.AddComponent<EventSystem>();
        }

        eventSystem.enabled = true;

        InputSystemUIInputModule inputModule = eventSystem.GetComponent<InputSystemUIInputModule>();
        if (inputModule == null)
        {
            inputModule = eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
        }

        PointableCanvasModule pointableModule = eventSystem.GetComponent<PointableCanvasModule>();
        if (pointableModule == null)
        {
            pointableModule = eventSystem.gameObject.AddComponent<PointableCanvasModule>();
        }

        pointableModule.enabled = true;
        pointableModule.ExclusiveMode = true;
        Debug.Log("[TrellisScene] EventSystem XR pronto: PointableCanvasModule attivo.");
    }

    private static void GetViewerAxes(Transform viewer, out Vector3 forward, out Vector3 right)
    {
        forward = Vector3.ProjectOnPlane(viewer.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < 0.001f)
        {
            forward = Vector3.forward;
        }

        right = Vector3.Cross(Vector3.up, forward).normalized;
    }

    private void ResetModelPose()
    {
        if (modelManipulator == null)
        {
            return;
        }

        modelManipulator.ResetToHome();
        SetStatus("Modello ricentrato.");
    }

    private void ReturnToDrawingScene()
    {
        if (returningToDrawingScene)
        {
            return;
        }

        StartCoroutine(ReturnToDrawingSceneRoutine());
    }

    private IEnumerator ReturnToDrawingSceneRoutine()
    {
        returningToDrawingScene = true;
        SetStatus("Ritorno alla scena di disegno...");
        XRSceneRigPersistence.ReleasePersistentSceneObjects();
        yield return null;
        SceneManager.LoadScene(mainSceneName);
    }

    private GameObject CreateRect(string name, Transform parent, Vector2 size)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform));
        obj.transform.SetParent(parent, false);
        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.sizeDelta = size;
        rect.localScale = Vector3.one;
        return obj;
    }

    private Image AddImage(GameObject target, Color color, bool raycastTarget)
    {
        Image image = target.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = raycastTarget;
        return image;
    }

    private TextMeshProUGUI AddText(string name, Transform parent, string text, int size, FontStyles style, TextAlignmentOptions alignment, float height)
    {
        GameObject textObject = CreateRect(name, parent, new Vector2(0f, height));
        TextMeshProUGUI label = textObject.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = size;
        label.fontStyle = style;
        label.alignment = alignment;
        label.color = Color.white;
        label.textWrappingMode = TextWrappingModes.Normal;
        label.raycastTarget = false;
        return label;
    }

    private RawImage CreateRawImage(string name, Transform parent, Vector2 size)
    {
        GameObject imageObject = CreateRect(name, parent, size);
        RawImage rawImage = imageObject.AddComponent<RawImage>();
        rawImage.color = new Color(1f, 1f, 1f, 0f);
        rawImage.raycastTarget = false;
        return rawImage;
    }

    private Button CreateButton(string name, Transform parent, string label, UnityEngine.Events.UnityAction onClick)
    {
        GameObject buttonObject = CreateRect(name, parent, new Vector2(0f, 48f));
        Image image = AddImage(buttonObject, new Color(0.1f, 0.55f, 0.95f, 1f), true);
        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() =>
        {
            Debug.Log("[TrellisScene] Click UI: " + name);
            onClick?.Invoke();
        });

        TextMeshProUGUI buttonLabel = AddText("Label", buttonObject.transform, label, 17, FontStyles.Bold, TextAlignmentOptions.Center, 46);
        RectTransform labelRect = buttonLabel.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;
        return button;
    }

    private void SetStartButtonEnabled(bool enabled)
    {
        if (startButton != null)
        {
            startButton.interactable = enabled;
            if (startButton.targetGraphic != null)
            {
                startButton.targetGraphic.color = enabled
                    ? new Color(0.1f, 0.55f, 0.95f, 1f)
                    : new Color(0.22f, 0.22f, 0.24f, 1f);
            }
        }
    }

    private void SetStatus(string message)
    {
        Debug.Log("[TrellisScene] " + message);
        if (statusText != null)
        {
            statusText.text = message;
        }
    }

    [Serializable]
    private class TrellisGenerateRequest
    {
        public string image;
        public string file_name;
        public string preferred_format;
    }

    [Serializable]
    private class TrellisBridgeResponse
    {
        public string status = "";
        public string job_id = "";
        public string message = "";
        public string model_base64 = "";
        public string model_url = "";
        public string download_url = "";
        public string model_path = "";
        public string file_name = "";
    }
}
