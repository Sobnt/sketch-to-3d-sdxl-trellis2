using System.Collections;
using System.Collections.Generic;
using Meta.WitAi;
using Meta.WitAi.Configuration;
using Meta.WitAi.Dictation;
using Meta.WitAi.Json;
using Meta.WitAi.Requests;
using Oculus.VoiceSDK.Utilities;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class GenerationHistoryUI : MonoBehaviour
{
    [Header("References")]
    public FlaskSenderOnly flaskSender;
    public Canvas targetCanvas;
    public FingerTrailTrigger fingerTrailTrigger;
    public MonoBehaviour dictationExperience;
    public DictationService dictationService;
    public DictationPromptController dictationPromptController;

    [Header("History")]
    public int maxHistory = 4;

    [Header("Layout")]
    public Vector2 panelSize = new Vector2(390f, 620f);
    public Vector2 panelPosition = Vector2.zero;
    public float worldCanvasPanelScale = 0.04f;

    [Header("Legacy UI")]
    public bool hideLegacyControls = true;

    [Header("Trellis Scene")]
    public bool openTrellisSceneOnGenerate = true;
    public string trellisSceneName = "TrellisGenerationScene";

    private readonly List<GeneratedImageResult> history = new List<GeneratedImageResult>();
    private readonly List<HistorySlot> historySlots = new List<HistorySlot>();

    private GameObject rootPanel;
    private GameObject parametersPage;
    private GameObject previewPage;
    private RawImage latestPreview;
    private TextMeshProUGUI promptValueText;
    private TextMeshProUGUI latestInfoText;
    private TextMeshProUGUI statusText;
    private Button generate3DButton;
    private Button parametersTabButton;
    private Button previewTabButton;
    private Button microphoneButton;
    private Button handednessButton;
    private GeneratedImageResult selectedResult;
    private bool dictationEventsWired;
    private bool dictationPromptControllerWired;
    private bool receivedTranscription;
    private bool reachedVoiceThreshold;
    private bool waitingForMicPermission;
    private float maxMicLevelDuringRequest;
    private float lastMicLevelStatusTime;

    private readonly Color panelColor = new Color(0.08f, 0.09f, 0.1f, 0.92f);
    private readonly Color surfaceColor = new Color(0.15f, 0.16f, 0.18f, 0.96f);
    private readonly Color selectedColor = new Color(0.1f, 0.55f, 0.95f, 1f);
    private readonly Color idleColor = new Color(0.28f, 0.29f, 0.31f, 1f);

    private void Awake()
    {
        if (flaskSender == null)
        {
            flaskSender = GetComponent<FlaskSenderOnly>();
        }

        ResolveSceneReferences();
    }

    private void OnEnable()
    {
        ResolveSceneReferences();
        WireDictationPromptController();

        if (flaskSender != null)
        {
            flaskSender.GenerationStarted += HandleGenerationStarted;
            flaskSender.GenerationCompleted += HandleGenerationCompleted;
            flaskSender.GenerationFailed += HandleGenerationFailed;
            flaskSender.TrellisCompleted += HandleTrellisCompleted;
            flaskSender.TrellisFailed += HandleTrellisFailed;
        }
    }

    private void OnDisable()
    {
        if (flaskSender != null)
        {
            flaskSender.GenerationStarted -= HandleGenerationStarted;
            flaskSender.GenerationCompleted -= HandleGenerationCompleted;
            flaskSender.GenerationFailed -= HandleGenerationFailed;
            flaskSender.TrellisCompleted -= HandleTrellisCompleted;
            flaskSender.TrellisFailed -= HandleTrellisFailed;
        }

        UnwireDictationEvents();
        UnwireDictationPromptController();
    }

    private void Start()
    {
        ResolveCanvas();
        HideLegacyControls();
        BuildUI();
        ShowPage(parametersPage);
        UpdateHistoryUI();
    }

    private void Update()
    {
        RefreshPromptText();
    }

    private void ResolveCanvas()
    {
        if (targetCanvas != null)
        {
            return;
        }

#pragma warning disable CS0618
        Canvas[] canvases = FindObjectsOfType<Canvas>(true);
#pragma warning restore CS0618
        foreach (Canvas canvas in canvases)
        {
            if (canvas.renderMode == RenderMode.WorldSpace)
            {
                targetCanvas = canvas;
                return;
            }
        }

        if (canvases.Length > 0)
        {
            targetCanvas = canvases[0];
            return;
        }

        GameObject canvasObject = new GameObject("GeneratedRuntimeCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        targetCanvas = canvasObject.GetComponent<Canvas>();
        targetCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
    }

    private void ResolveSceneReferences()
    {
        if (fingerTrailTrigger == null)
        {
#pragma warning disable CS0618
            fingerTrailTrigger = FindObjectOfType<FingerTrailTrigger>(true);
#pragma warning restore CS0618
        }

        if (dictationExperience == null)
        {
#pragma warning disable CS0618
            MonoBehaviour[] behaviours = FindObjectsOfType<MonoBehaviour>(true);
#pragma warning restore CS0618
            foreach (MonoBehaviour behaviour in behaviours)
            {
                if (behaviour != null && behaviour.GetType().FullName == "Oculus.Voice.Dictation.AppDictationExperience")
                {
                    dictationExperience = behaviour;
                    break;
                }
            }
        }

        if (dictationService == null)
        {
#pragma warning disable CS0618
            dictationService = FindObjectOfType<DictationService>(true);
#pragma warning restore CS0618
        }

        if (dictationPromptController == null)
        {
#pragma warning disable CS0618
            dictationPromptController = FindObjectOfType<DictationPromptController>(true);
#pragma warning restore CS0618
        }

        if (dictationPromptController == null && dictationService != null)
        {
            dictationPromptController = dictationService.GetComponent<DictationPromptController>();
            if (dictationPromptController == null)
            {
                dictationPromptController = dictationService.gameObject.AddComponent<DictationPromptController>();
            }
        }

        if (dictationPromptController != null)
        {
            if (dictationPromptController.flaskSender == null)
            {
                dictationPromptController.flaskSender = flaskSender;
            }

            if (dictationPromptController.dictationService == null)
            {
                dictationPromptController.dictationService = dictationService;
            }
        }

        WireDictationPromptController();
    }

    private void WireDictationPromptController()
    {
        if (dictationPromptControllerWired || dictationPromptController == null)
        {
            return;
        }

        dictationPromptController.StatusChanged += HandleDictationControllerStatus;
        dictationPromptController.PromptChanged += HandleDictationControllerPrompt;
        dictationPromptControllerWired = true;
    }

    private void UnwireDictationPromptController()
    {
        if (!dictationPromptControllerWired || dictationPromptController == null)
        {
            return;
        }

        dictationPromptController.StatusChanged -= HandleDictationControllerStatus;
        dictationPromptController.PromptChanged -= HandleDictationControllerPrompt;
        dictationPromptControllerWired = false;
    }

    private void HandleDictationControllerStatus(string message)
    {
        SetStatus(message);
    }

    private void HandleDictationControllerPrompt(string text)
    {
        RefreshPromptText();
    }

    private void WireDictationEvents()
    {
        if (dictationEventsWired || dictationService == null)
        {
            return;
        }

        dictationService.DictationEvents.OnStartListening.AddListener(HandleDictationStarted);
        dictationService.DictationEvents.OnStoppedListening.AddListener(HandleDictationStopped);
        dictationService.DictationEvents.OnStoppedListeningDueToInactivity.AddListener(HandleDictationStoppedDueToInactivity);
        dictationService.DictationEvents.OnStoppedListeningDueToTimeout.AddListener(HandleDictationStoppedDueToTimeout);
        dictationService.DictationEvents.OnMinimumWakeThresholdHit.AddListener(HandleWakeThresholdHit);
        dictationService.DictationEvents.OnMicLevelChanged.AddListener(HandleMicLevelChanged);
        dictationService.DictationEvents.OnPartialTranscription.AddListener(HandlePartialTranscription);
        dictationService.DictationEvents.OnFullTranscription.AddListener(HandleFullTranscription);
        dictationService.DictationEvents.OnUserPartialTranscription.AddListener(HandleUserPartialTranscription);
        dictationService.DictationEvents.OnUserFullTranscription.AddListener(HandleUserFullTranscription);
        dictationService.DictationEvents.OnError.AddListener(HandleDictationError);
        dictationService.DictationEvents.OnComplete.AddListener(HandleDictationComplete);
        dictationEventsWired = true;
    }

    private void UnwireDictationEvents()
    {
        if (!dictationEventsWired || dictationService == null)
        {
            return;
        }

        dictationService.DictationEvents.OnStartListening.RemoveListener(HandleDictationStarted);
        dictationService.DictationEvents.OnStoppedListening.RemoveListener(HandleDictationStopped);
        dictationService.DictationEvents.OnStoppedListeningDueToInactivity.RemoveListener(HandleDictationStoppedDueToInactivity);
        dictationService.DictationEvents.OnStoppedListeningDueToTimeout.RemoveListener(HandleDictationStoppedDueToTimeout);
        dictationService.DictationEvents.OnMinimumWakeThresholdHit.RemoveListener(HandleWakeThresholdHit);
        dictationService.DictationEvents.OnMicLevelChanged.RemoveListener(HandleMicLevelChanged);
        dictationService.DictationEvents.OnPartialTranscription.RemoveListener(HandlePartialTranscription);
        dictationService.DictationEvents.OnFullTranscription.RemoveListener(HandleFullTranscription);
        dictationService.DictationEvents.OnUserPartialTranscription.RemoveListener(HandleUserPartialTranscription);
        dictationService.DictationEvents.OnUserFullTranscription.RemoveListener(HandleUserFullTranscription);
        dictationService.DictationEvents.OnError.RemoveListener(HandleDictationError);
        dictationService.DictationEvents.OnComplete.RemoveListener(HandleDictationComplete);
        dictationEventsWired = false;
    }

    private void HideLegacyControls()
    {
        if (!hideLegacyControls)
        {
            return;
        }

        DisableObjectByName("PokeMano");
        DisableObjectByName("PokePrompt");
        DisableObjectByName("StrenghtSlider");
        DisableObjectByName("EndPercentSlider");
        DisableObjectByName("CFGSlider");
        DisableObjectByName("StepsSlider");
    }

    private void DisableObjectByName(string objectName)
    {
        Transform[] allTransforms = Resources.FindObjectsOfTypeAll<Transform>();
        foreach (Transform candidate in allTransforms)
        {
            if (candidate == null || candidate.name != objectName)
            {
                continue;
            }

            if (!candidate.gameObject.scene.IsValid())
            {
                continue;
            }

            candidate.gameObject.SetActive(false);
        }
    }

    private void BuildUI()
    {
        if (rootPanel != null || targetCanvas == null)
        {
            return;
        }

        rootPanel = CreateRect("AI_Generation_UI", targetCanvas.transform, panelSize);
        RectTransform rootRect = rootPanel.GetComponent<RectTransform>();
        rootRect.anchorMin = new Vector2(0.5f, 0.5f);
        rootRect.anchorMax = new Vector2(0.5f, 0.5f);
        rootRect.pivot = new Vector2(0.5f, 0.5f);
        rootRect.anchoredPosition = panelPosition;
        if (targetCanvas.renderMode == RenderMode.WorldSpace)
        {
            rootRect.localScale = Vector3.one * worldCanvasPanelScale;
        }
        AddImage(rootPanel, panelColor, false);

        VerticalLayoutGroup rootLayout = rootPanel.AddComponent<VerticalLayoutGroup>();
        rootLayout.padding = new RectOffset(14, 14, 14, 14);
        rootLayout.spacing = 10;
        rootLayout.childControlWidth = true;
        rootLayout.childControlHeight = false;
        rootLayout.childForceExpandWidth = true;
        rootLayout.childForceExpandHeight = false;

        AddText("Title", rootPanel.transform, "Generazione AI", 24, FontStyles.Bold, TextAlignmentOptions.Center, 34);

        GameObject tabs = CreateRect("Tabs", rootPanel.transform, new Vector2(0, 42));
        HorizontalLayoutGroup tabsLayout = tabs.AddComponent<HorizontalLayoutGroup>();
        tabsLayout.spacing = 8;
        tabsLayout.childControlWidth = true;
        tabsLayout.childControlHeight = true;
        tabsLayout.childForceExpandWidth = true;
        tabsLayout.childForceExpandHeight = true;

        parametersTabButton = CreateButton("ParametriTab", tabs.transform, "Parametri", () => ShowPage(parametersPage));
        previewTabButton = CreateButton("PreviewTab", tabs.transform, "Preview", () => ShowPage(previewPage));

        statusText = AddText("StatusText", rootPanel.transform, "Pronto.", 13, FontStyles.Normal, TextAlignmentOptions.Left, 42);

        parametersPage = CreatePage("ParametersPage");
        previewPage = CreatePage("PreviewPage");

        BuildParametersPage();
        BuildPreviewPage();
    }

    private GameObject CreatePage(string name)
    {
        GameObject page = CreateRect(name, rootPanel.transform, new Vector2(0, 440));
        VerticalLayoutGroup layout = page.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 10;
        layout.childControlWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        return page;
    }

    private void BuildParametersPage()
    {
        AddText("PromptLabel", parametersPage.transform, "Prompt corrente", 16, FontStyles.Bold, TextAlignmentOptions.Left, 24);
        string prompt = flaskSender != null ? flaskSender.aiPrompt : "";
        promptValueText = AddText("PromptValue", parametersPage.transform, string.IsNullOrEmpty(prompt) ? "-" : prompt, 14, FontStyles.Normal, TextAlignmentOptions.Left, 54);

        AddText("ActionsLabel", parametersPage.transform, "Comandi rapidi", 16, FontStyles.Bold, TextAlignmentOptions.Left, 24);

        GameObject actionsRow = CreateRect("ActionsRow", parametersPage.transform, new Vector2(0, 42));
        HorizontalLayoutGroup actionsLayout = actionsRow.AddComponent<HorizontalLayoutGroup>();
        actionsLayout.spacing = 8;
        actionsLayout.childControlWidth = true;
        actionsLayout.childControlHeight = true;
        actionsLayout.childForceExpandWidth = true;
        actionsLayout.childForceExpandHeight = true;

        handednessButton = CreateButton("HandednessButton", actionsRow.transform, "Cambia mano", ToggleHandedness);
        microphoneButton = CreateButton("MicrophoneButton", actionsRow.transform, "Microfono", StartPromptDictation);

        CreateSliderRow("Strength", parametersPage.transform, 0f, 1f, flaskSender != null ? flaskSender.strength : 0.85f, false, 0.05f, value =>
        {
            if (flaskSender != null)
            {
                flaskSender.SetStrength(value);
            }
        });

        CreateSliderRow("End percent", parametersPage.transform, 0f, 1f, flaskSender != null ? flaskSender.endPercent : 0.9f, false, 0.05f, value =>
        {
            if (flaskSender != null)
            {
                flaskSender.SetEndPercent(value);
            }
        });

        CreateSliderRow("CFG", parametersPage.transform, 1f, 15f, flaskSender != null ? flaskSender.cfg : 7f, false, 0.5f, value =>
        {
            if (flaskSender != null)
            {
                flaskSender.SetCFG(value);
            }
        });

        CreateSliderRow("Steps", parametersPage.transform, 10f, 50f, flaskSender != null ? flaskSender.steps : 20f, true, 1f, value =>
        {
            if (flaskSender != null)
            {
                flaskSender.SetSteps(value);
            }
        });

        Toggle magicToggle = CreateToggle("MagicEnrichToggle", parametersPage.transform, "Prompt augmentation", flaskSender != null && flaskSender.magicEnrich);
        magicToggle.onValueChanged.AddListener(value =>
        {
            if (flaskSender != null)
            {
                flaskSender.magicEnrich = value;
            }
        });

        AddText("Hint", parametersPage.transform, "Premi il pulsante del controller per inviare sketch, prompt e parametri.", 13, FontStyles.Normal, TextAlignmentOptions.Left, 48);
    }

    private void BuildPreviewPage()
    {
        AddText("PreviewLabel", previewPage.transform, "Preview ultima generazione", 16, FontStyles.Bold, TextAlignmentOptions.Left, 24);

        GameObject previewFrame = CreateRect("LatestPreviewFrame", previewPage.transform, new Vector2(0, 190));
        AddImage(previewFrame, surfaceColor, false);
        latestPreview = CreateRawImage("LatestPreview", previewFrame.transform, new Vector2(170, 170));
        latestPreview.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        latestPreview.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        latestPreview.rectTransform.anchoredPosition = Vector2.zero;
        latestPreview.color = new Color(1f, 1f, 1f, 0f);

        latestInfoText = AddText("LatestInfo", previewPage.transform, "Nessuna immagine generata.", 13, FontStyles.Normal, TextAlignmentOptions.Left, 48);

        AddText("HistoryLabel", previewPage.transform, "Cronologia ultime 4", 16, FontStyles.Bold, TextAlignmentOptions.Left, 24);

        GameObject historyGrid = CreateRect("HistoryGrid", previewPage.transform, new Vector2(0, 95));
        HorizontalLayoutGroup historyLayout = historyGrid.AddComponent<HorizontalLayoutGroup>();
        historyLayout.spacing = 8;
        historyLayout.childControlWidth = true;
        historyLayout.childControlHeight = true;
        historyLayout.childForceExpandWidth = true;
        historyLayout.childForceExpandHeight = true;

        for (int i = 0; i < maxHistory; i++)
        {
            historySlots.Add(CreateHistorySlot(historyGrid.transform, i));
        }

        generate3DButton = CreateButton("Generate3DButton", previewPage.transform, "Genera modello 3D", SendSelectedToTrellis);
        generate3DButton.interactable = false;
        SetButtonColor(generate3DButton, new Color(0.22f, 0.22f, 0.24f, 1f));

        SetStatus("Seleziona una preview per abilitarne l'invio a Trellis.");
    }

    private void HandleGenerationCompleted(GeneratedImageResult result)
    {
        history.Insert(0, result);
        while (history.Count > maxHistory)
        {
            GeneratedImageResult removed = history[history.Count - 1];
            history.RemoveAt(history.Count - 1);
            if (removed.texture != null && removed != selectedResult)
            {
                Destroy(removed.texture);
            }
        }

        SelectResult(result);
        ShowPage(previewPage);
        SetStatus("Nuova immagine aggiunta alla cronologia.");
    }

    private void HandleGenerationStarted()
    {
        ShowPage(previewPage);
        SetStatus("Generazione immagine in corso...");
    }

    private void HandleGenerationFailed(string message)
    {
        SetStatus("Errore generazione: " + message);
        ShowPage(previewPage);
    }

    private void HandleTrellisCompleted(string response)
    {
        SetStatus("Richiesta Trellis completata.");
    }

    private void HandleTrellisFailed(string message)
    {
        SetStatus("Errore Trellis: " + message);
    }

    private void SelectResult(GeneratedImageResult result)
    {
        selectedResult = result;
        UpdateHistoryUI();
    }

    private void SendSelectedToTrellis()
    {
        if (selectedResult == null)
        {
            SetStatus("Seleziona prima una immagine.");
            return;
        }

        try
        {
            TrellisGenerationPayload.Capture(selectedResult);
        }
        catch (System.Exception exception)
        {
            SetStatus("Impossibile preparare l'immagine per Trellis.");
            Debug.LogError("[GenerationHistoryUI] Errore payload Trellis: " + exception);
            return;
        }

        if (openTrellisSceneOnGenerate)
        {
            SetStatus("Apro la scena di generazione 3D...");
            try
            {
                XRSceneRigPersistence.PreserveForTrellisScene();
                SceneManager.LoadScene(trellisSceneName);
            }
            catch (System.Exception exception)
            {
                SetStatus("Scena Trellis non caricabile: " + trellisSceneName);
                Debug.LogError("[GenerationHistoryUI] Errore apertura scena Trellis: " + exception);
            }

            return;
        }

        if (flaskSender == null)
        {
            SetStatus("Client Flask non trovato.");
            return;
        }

        SetStatus("Invio immagine selezionata a Trellis...");
        flaskSender.SendSelectedToTrellis(selectedResult);
    }

    private void ToggleHandedness()
    {
        ResolveSceneReferences();

        if (fingerTrailTrigger == null)
        {
            SetStatus("Script cambio mano non trovato nella scena.");
            return;
        }

        fingerTrailTrigger.ToggleHandedness();
        SetStatus("Mano di disegno invertita.");
    }

    private void StartPromptDictation()
    {
        Debug.Log("[GenerationHistoryUI] Bottone microfono premuto.");
        ResolveSceneReferences();

        if (dictationPromptController == null)
        {
            SetStatus("Controller microfono non trovato nella scena.");
            return;
        }

        dictationPromptController.ToggleDictation();
    }

    private bool HasMicrophonePermission()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        return MicPermissionsManager.HasMicPermission();
#else
        return Application.HasUserAuthorization(UserAuthorization.Microphone);
#endif
    }

    private void RequestMicrophonePermission()
    {
        if (waitingForMicPermission)
        {
            SetStatus("In attesa del permesso microfono...");
            return;
        }

        waitingForMicPermission = true;
        SetStatus("Autorizza il microfono nel popup di sistema.");

#if UNITY_ANDROID && !UNITY_EDITOR
        MicPermissionsManager.RequestMicPermission(_ =>
        {
            waitingForMicPermission = false;
            if (MicPermissionsManager.HasMicPermission())
            {
                SetStatus("Permesso microfono concesso. Riavvio ascolto...");
                StartPromptDictation();
            }
            else
            {
                SetStatus("Permesso microfono negato nelle impostazioni del Quest.");
            }
        });
#else
        StartCoroutine(RequestEditorMicrophonePermission());
#endif
    }

#if !UNITY_ANDROID || UNITY_EDITOR
    private IEnumerator RequestEditorMicrophonePermission()
    {
        yield return Application.RequestUserAuthorization(UserAuthorization.Microphone);
        waitingForMicPermission = false;

        if (Application.HasUserAuthorization(UserAuthorization.Microphone))
        {
            SetStatus("Permesso microfono concesso. Riavvio ascolto...");
            StartPromptDictation();
        }
        else
        {
            SetStatus("Permesso microfono negato sul computer.");
        }
    }
#endif

    private void HandleDictationStarted()
    {
        SetStatus("Microfono in ascolto...");
    }

    private void HandleDictationStopped()
    {
        if (!receivedTranscription)
        {
            SetStatus(GetNoTranscriptionStatus("Ascolto terminato"));
        }
    }

    private void HandleDictationStoppedDueToInactivity()
    {
        if (!receivedTranscription)
        {
            SetStatus(GetNoTranscriptionStatus("Ascolto fermato per inattivita'"));
        }
    }

    private void HandleDictationStoppedDueToTimeout()
    {
        if (!receivedTranscription)
        {
            SetStatus("Ascolto fermato per timeout.");
        }
    }

    private void HandleWakeThresholdHit()
    {
        reachedVoiceThreshold = true;
        SetStatus("Voce rilevata, trascrizione in corso...");
    }

    private void HandleMicLevelChanged(float level)
    {
        if (receivedTranscription || Time.time - lastMicLevelStatusTime < 0.4f)
        {
            return;
        }

        lastMicLevelStatusTime = Time.time;
        maxMicLevelDuringRequest = Mathf.Max(maxMicLevelDuringRequest, level);
        if (level > 0.001f)
        {
            SetStatus($"Microfono riceve audio ({level:0.000}).");
        }
    }

    private string GetNoTranscriptionStatus(string prefix)
    {
        if (maxMicLevelDuringRequest <= 0.001f)
        {
            return $"{prefix}: nessun audio dal microfono.";
        }

        return reachedVoiceThreshold
            ? $"{prefix}: audio ricevuto, ma nessuna trascrizione."
            : $"{prefix}: audio troppo basso ({maxMicLevelDuringRequest:0.000}).";
    }

    private void HandlePartialTranscription(string text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            SetStatus("Sto ascoltando: " + text);
        }
    }

    private void HandleFullTranscription(string text)
    {
        ApplyTranscription(text);
    }

    private void HandleUserPartialTranscription(string userId, string text)
    {
        HandlePartialTranscription(text);
    }

    private void HandleUserFullTranscription(string userId, string text)
    {
        ApplyTranscription(text);
    }

    private void HandleDictationError(string errorType, string message)
    {
        SetStatus("Errore dictation: " + errorType + " - " + message);
        Debug.LogWarning("[GenerationHistoryUI] Dictation error: " + errorType + " - " + message);
    }

    private void HandleDictationRequestFailed(VoiceServiceRequest request)
    {
        string message = request != null && request.Results != null ? request.Results.Message : "";
        SetStatus("Richiesta dictation fallita: " + (string.IsNullOrWhiteSpace(message) ? "nessun dettaglio." : message));
    }

    private void HandleDictationFullResponse(WitResponseNode response)
    {
        string transcription = ExtractTranscription(response);
        if (!string.IsNullOrWhiteSpace(transcription))
        {
            ApplyTranscription(transcription);
            return;
        }

        SetStatus("Risposta Meta ricevuta, ma senza testo trascritto.");
        Debug.LogWarning("[GenerationHistoryUI] Dictation response senza trascrizione: " + (response != null ? response.ToString() : "null"));
    }

    private void HandleDictationComplete(VoiceServiceRequest request)
    {
        if (receivedTranscription || request == null)
        {
            return;
        }

        string transcription = ExtractTranscription(request);
        if (!string.IsNullOrWhiteSpace(transcription))
        {
            ApplyTranscription(transcription);
            return;
        }

        string message = request.Results != null ? request.Results.Message : "";
        string suffix = string.IsNullOrWhiteSpace(message) ? "" : " " + message;
        SetStatus($"Dictation completata senza testo. Stato: {request.State}.{suffix}");
    }

    private void ApplyTranscription(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            SetStatus("Trascrizione vuota.");
            return;
        }

        receivedTranscription = true;
        if (flaskSender != null)
        {
            flaskSender.SetPromptFromDictation(text);
        }

        RefreshPromptText();
        SetStatus("Prompt acquisito: " + text);
    }

    private string ExtractTranscription(VoiceServiceRequest request)
    {
        if (request == null)
        {
            return "";
        }

        if (request.Results != null)
        {
            if (!string.IsNullOrWhiteSpace(request.Results.Transcription))
            {
                return request.Results.Transcription;
            }

            string[] finalTranscriptions = request.Results.FinalTranscriptions;
            if (finalTranscriptions != null)
            {
                for (int i = finalTranscriptions.Length - 1; i >= 0; i--)
                {
                    if (!string.IsNullOrWhiteSpace(finalTranscriptions[i]))
                    {
                        return finalTranscriptions[i];
                    }
                }
            }
        }

        return ExtractTranscription(request.ResponseData);
    }

    private string ExtractTranscription(WitResponseNode response)
    {
        if (response == null)
        {
            return "";
        }

        string transcription = response.GetTranscription();
        if (!string.IsNullOrWhiteSpace(transcription))
        {
            return transcription;
        }

        string[] fallbackKeys = { "text", "_text", "transcription", "utterance" };
        for (int i = 0; i < fallbackKeys.Length; i++)
        {
            string value = GetResponseString(response, fallbackKeys[i]);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return "";
    }

    private string GetResponseString(WitResponseNode response, string key)
    {
        if (response == null || response.AsObject == null || !response.AsObject.HasChild(key))
        {
            return "";
        }

        return response[key].Value;
    }

    private void UpdateHistoryUI()
    {
        if (latestPreview != null)
        {
            latestPreview.texture = selectedResult != null ? selectedResult.texture : null;
            latestPreview.color = selectedResult != null ? Color.white : new Color(1f, 1f, 1f, 0f);
        }

        if (latestInfoText != null)
        {
            latestInfoText.text = selectedResult == null
                ? "Nessuna immagine generata."
                : $"Selezionata: {selectedResult.generatedAt}\nCFG {selectedResult.cfg:0.##} | Strength {selectedResult.strength:0.##} | End {selectedResult.endPercent:0.##} | Steps {selectedResult.steps}";
        }

        for (int i = 0; i < historySlots.Count; i++)
        {
            GeneratedImageResult result = i < history.Count ? history[i] : null;
            historySlots[i].SetResult(result, result != null && result == selectedResult, SelectResult);
        }

        if (generate3DButton != null)
        {
            generate3DButton.interactable = selectedResult != null;
            SetButtonColor(generate3DButton, selectedResult != null ? selectedColor : new Color(0.22f, 0.22f, 0.24f, 1f));
        }
    }

    private void ShowPage(GameObject page)
    {
        RefreshPromptText();

        if (parametersPage != null)
        {
            parametersPage.SetActive(page == parametersPage);
        }

        if (previewPage != null)
        {
            previewPage.SetActive(page == previewPage);
        }

        SetButtonColor(parametersTabButton, page == parametersPage ? selectedColor : idleColor);
        SetButtonColor(previewTabButton, page == previewPage ? selectedColor : idleColor);
    }

    private void RefreshPromptText()
    {
        if (promptValueText == null || flaskSender == null)
        {
            return;
        }

        promptValueText.text = string.IsNullOrEmpty(flaskSender.aiPrompt) ? "-" : flaskSender.aiPrompt;
    }

    private void SetStatus(string message)
    {
        Debug.Log("[GenerationHistoryUI] Stato: " + message);
        if (statusText != null)
        {
            statusText.text = message;
        }
    }

    private void CreateSliderRow(string label, Transform parent, float min, float max, float value, bool wholeNumbers, float stepSize, UnityEngine.Events.UnityAction<float> onChanged)
    {
        GameObject row = CreateRect(label + "Row", parent, new Vector2(0, 58));
        VerticalLayoutGroup rowLayout = row.AddComponent<VerticalLayoutGroup>();
        rowLayout.spacing = 4;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = false;
        rowLayout.childForceExpandWidth = true;

        float initialValue = SnapSliderValue(value, min, max, stepSize, wholeNumbers);
        TextMeshProUGUI valueText = AddText(label + "Value", row.transform, $"{label}: {initialValue:0.##}", 13, FontStyles.Normal, TextAlignmentOptions.Left, 20);
        Slider slider = CreateSlider(label + "Slider", row.transform, min, max, initialValue, wholeNumbers);
        slider.onValueChanged.AddListener(newValue =>
        {
            float snappedValue = SnapSliderValue(newValue, min, max, stepSize, wholeNumbers);
            if (!Mathf.Approximately(slider.value, snappedValue))
            {
                slider.SetValueWithoutNotify(snappedValue);
            }

            valueText.text = $"{label}: {snappedValue:0.##}";
            onChanged?.Invoke(snappedValue);
        });
    }

    private float SnapSliderValue(float value, float min, float max, float stepSize, bool wholeNumbers)
    {
        float effectiveStep = wholeNumbers ? 1f : Mathf.Max(0.0001f, stepSize);
        float snapped = min + Mathf.Round((value - min) / effectiveStep) * effectiveStep;
        snapped = Mathf.Clamp(snapped, min, max);
        return wholeNumbers ? Mathf.Round(snapped) : Mathf.Round(snapped * 100f) / 100f;
    }

    private Slider CreateSlider(string name, Transform parent, float min, float max, float value, bool wholeNumbers)
    {
        GameObject sliderObject = CreateRect(name, parent, new Vector2(0, 26));
        Slider slider = sliderObject.AddComponent<Slider>();
        slider.minValue = min;
        slider.maxValue = max;
        slider.value = value;
        slider.wholeNumbers = wholeNumbers;

        GameObject background = CreateRect("Background", sliderObject.transform, new Vector2(0, 10));
        RectTransform backgroundRect = background.GetComponent<RectTransform>();
        backgroundRect.anchorMin = new Vector2(0f, 0.5f);
        backgroundRect.anchorMax = new Vector2(1f, 0.5f);
        backgroundRect.offsetMin = new Vector2(0f, -5f);
        backgroundRect.offsetMax = new Vector2(0f, 5f);
        AddImage(background, new Color(0.3f, 0.31f, 0.34f, 1f), false);

        GameObject fillArea = CreateRect("Fill Area", sliderObject.transform, Vector2.zero);
        RectTransform fillAreaRect = fillArea.GetComponent<RectTransform>();
        fillAreaRect.anchorMin = new Vector2(0f, 0.5f);
        fillAreaRect.anchorMax = new Vector2(1f, 0.5f);
        fillAreaRect.offsetMin = new Vector2(0f, -5f);
        fillAreaRect.offsetMax = new Vector2(0f, 5f);

        GameObject fill = CreateRect("Fill", fillArea.transform, Vector2.zero);
        AddImage(fill, selectedColor, false);
        slider.fillRect = fill.GetComponent<RectTransform>();

        GameObject handle = CreateRect("Handle", sliderObject.transform, new Vector2(18, 18));
        Image handleImage = AddImage(handle, Color.white, true);
        slider.handleRect = handle.GetComponent<RectTransform>();
        slider.targetGraphic = handleImage;
        return slider;
    }

    private Toggle CreateToggle(string name, Transform parent, string label, bool value)
    {
        GameObject toggleObject = CreateRect(name, parent, new Vector2(0, 34));
        Toggle toggle = toggleObject.AddComponent<Toggle>();

        HorizontalLayoutGroup layout = toggleObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 8;
        layout.childControlWidth = false;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;

        GameObject checkmarkBox = CreateRect("Box", toggleObject.transform, new Vector2(24, 24));
        Image boxImage = AddImage(checkmarkBox, idleColor, true);

        GameObject checkmark = CreateRect("Checkmark", checkmarkBox.transform, new Vector2(16, 16));
        Image checkImage = AddImage(checkmark, selectedColor, false);

        AddText("Label", toggleObject.transform, label, 14, FontStyles.Normal, TextAlignmentOptions.Left, 28);

        toggle.targetGraphic = boxImage;
        toggle.graphic = checkImage;
        toggle.isOn = value;
        return toggle;
    }

    private HistorySlot CreateHistorySlot(Transform parent, int index)
    {
        GameObject slotObject = CreateRect("HistorySlot_" + index, parent, new Vector2(82, 82));
        Image background = AddImage(slotObject, idleColor, true);
        Button button = slotObject.AddComponent<Button>();
        button.targetGraphic = background;

        RawImage image = CreateRawImage("Image", slotObject.transform, new Vector2(72, 72));
        image.raycastTarget = false;
        return new HistorySlot(button, background, image, idleColor, selectedColor);
    }

    private Button CreateButton(string name, Transform parent, string label, UnityEngine.Events.UnityAction onClick)
    {
        GameObject buttonObject = CreateRect(name, parent, new Vector2(0, 40));
        Image image = AddImage(buttonObject, idleColor, true);
        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() =>
        {
            Debug.Log("[GenerationHistoryUI] Click UI: " + name);
            onClick?.Invoke();
        });
        TextMeshProUGUI buttonLabel = AddText("Label", buttonObject.transform, label, 15, FontStyles.Bold, TextAlignmentOptions.Center, 36);
        RectTransform labelRect = buttonLabel.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;
        return button;
    }

    private TextMeshProUGUI AddText(string name, Transform parent, string text, int size, FontStyles style, TextAlignmentOptions alignment, float height)
    {
        GameObject textObject = CreateRect(name, parent, new Vector2(320, height));
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
        rawImage.color = Color.white;
        rawImage.raycastTarget = false;
        return rawImage;
    }

    private Image AddImage(GameObject target, Color color, bool raycastTarget = false)
    {
        Image image = target.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = raycastTarget;
        return image;
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

    private void SetButtonColor(Button button, Color color)
    {
        if (button == null || button.targetGraphic == null)
        {
            return;
        }

        button.targetGraphic.color = color;
    }

    private class HistorySlot
    {
        private readonly Button button;
        private readonly Image background;
        private readonly RawImage image;
        private readonly Color idleColor;
        private readonly Color selectedColor;
        private GeneratedImageResult result;

        public HistorySlot(Button button, Image background, RawImage image, Color idleColor, Color selectedColor)
        {
            this.button = button;
            this.background = background;
            this.image = image;
            this.idleColor = idleColor;
            this.selectedColor = selectedColor;
        }

        public void SetResult(GeneratedImageResult newResult, bool selected, System.Action<GeneratedImageResult> onSelected)
        {
            result = newResult;
            image.texture = result != null ? result.texture : null;
            image.color = result != null ? Color.white : new Color(1f, 1f, 1f, 0f);
            background.color = selected ? selectedColor : idleColor;
            button.interactable = result != null;
            button.onClick.RemoveAllListeners();

            if (result != null)
            {
                button.onClick.AddListener(() => onSelected?.Invoke(result));
            }
        }
    }
}
