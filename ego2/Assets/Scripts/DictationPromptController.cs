using Meta.WitAi.Dictation;
using Meta.WitAi.Requests;
using Oculus.VoiceSDK.Utilities;
using UnityEngine;

public class DictationPromptController : MonoBehaviour
{
    public FlaskSenderOnly flaskSender;
    public DictationService dictationService;
    [Tooltip("Su Quest aspetta che visore e focus XR siano stabili prima di attivare la dictation.")]
    public bool waitForQuestFocusBeforeActivation = true;
    [Tooltip("Piccolo ritardo dopo focus/HMD mounted per dare tempo al microfono di inizializzarsi.")]
    public float activationDelayAfterFocus = 0.35f;
    [Tooltip("Tempo massimo di attesa degli eventi focus/HMD prima di provare comunque.")]
    public float focusWaitTimeout = 3.0f;

    public event System.Action<string> StatusChanged;
    public event System.Action<string> PromptChanged;

    private bool eventsWired;
    private bool activationPending;
    private bool receivedText;
    private bool reachedVoiceThreshold;
    private bool waitingForMicPermission;
    private Coroutine activationRoutine;
    private float maxMicLevelDuringRequest;
    private float lastMicLevelStatusTime;

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();
        WireEvents();
        OVRManager.HMDMounted += HandleQuestFocusChanged;
        OVRManager.InputFocusAcquired += HandleQuestFocusChanged;
        OVRManager.VrFocusAcquired += HandleQuestFocusChanged;
    }

    private void OnDisable()
    {
        OVRManager.HMDMounted -= HandleQuestFocusChanged;
        OVRManager.InputFocusAcquired -= HandleQuestFocusChanged;
        OVRManager.VrFocusAcquired -= HandleQuestFocusChanged;
        StopActivationRoutine();
        UnwireEvents();
    }

    public void ToggleDictation()
    {
        ResolveReferences();
        WireEvents();

        Debug.Log("[DictationPromptController] Bottone microfono premuto.");

        if (dictationService == null)
        {
            SetStatus("Dictation Meta non trovata.");
            return;
        }

        if (!HasMicrophonePermission())
        {
            RequestMicrophonePermission();
            return;
        }

        if (dictationService.MicActive)
        {
            StopActivationRoutine();
            dictationService.Deactivate();
            SetStatus("Microfono fermato.");
            return;
        }

        QueueActivation();
    }

    private void QueueActivation()
    {
        StopActivationRoutine();
        activationPending = true;
        activationRoutine = StartCoroutine(ActivateWhenQuestIsReady());
    }

    private System.Collections.IEnumerator ActivateWhenQuestIsReady()
    {
        if (waitForQuestFocusBeforeActivation)
        {
            float startTime = Time.time;
            while (!IsQuestReadyForMicrophone() && Time.time - startTime < focusWaitTimeout)
            {
                SetStatus("Attendo focus del visore per il microfono...");
                yield return new WaitForSeconds(0.25f);
            }

            yield return new WaitForSeconds(Mathf.Max(0f, activationDelayAfterFocus));
        }

        activationRoutine = null;
        activationPending = false;
        ActivateDictationNow();
    }

    private void ActivateDictationNow()
    {
        ResolveReferences();
        WireEvents();

        if (dictationService == null)
        {
            SetStatus("Dictation Meta non trovata.");
            return;
        }

        string activationError = dictationService.GetActivateAudioError();
        if (!string.IsNullOrEmpty(activationError))
        {
            SetStatus("Microfono non attivabile: " + activationError);
            Debug.LogWarning("[DictationPromptController] Activation error: " + activationError);
            return;
        }

        receivedText = false;
        reachedVoiceThreshold = false;
        maxMicLevelDuringRequest = 0f;
        lastMicLevelStatusTime = 0f;
        SetStatus("Avvio microfono...");

        try
        {
            dictationService.ActivateImmediately();
            SetStatus("Microfono pronto: parla ora.");
        }
        catch (System.Exception exception)
        {
            SetStatus("Errore microfono: " + exception.Message);
            Debug.LogException(exception);
        }
    }

    private bool IsQuestReadyForMicrophone()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (OVRManager.instance == null)
        {
            return true;
        }

        return OVRManager.instance.isUserPresent && OVRManager.hasInputFocus && OVRManager.hasVrFocus;
#else
        return true;
#endif
    }

    private void HandleQuestFocusChanged()
    {
        if (activationPending && activationRoutine == null)
        {
            activationRoutine = StartCoroutine(ActivateWhenQuestIsReady());
        }
    }

    private void StopActivationRoutine()
    {
        activationPending = false;
        if (activationRoutine != null)
        {
            StopCoroutine(activationRoutine);
            activationRoutine = null;
        }
    }

    private void ResolveReferences()
    {
        if (flaskSender == null)
        {
#pragma warning disable CS0618
            flaskSender = FindObjectOfType<FlaskSenderOnly>(true);
#pragma warning restore CS0618
        }

        if (dictationService == null)
        {
            dictationService = GetComponent<DictationService>();
        }

        if (dictationService == null)
        {
#pragma warning disable CS0618
            dictationService = FindObjectOfType<DictationService>(true);
#pragma warning restore CS0618
        }
    }

    private void WireEvents()
    {
        if (eventsWired || dictationService == null || dictationService.DictationEvents == null)
        {
            return;
        }

        dictationService.DictationEvents.OnStartListening.AddListener(HandleStartListening);
        dictationService.DictationEvents.OnStoppedListening.AddListener(HandleStoppedListening);
        dictationService.DictationEvents.OnStoppedListeningDueToInactivity.AddListener(HandleStoppedDueToInactivity);
        dictationService.DictationEvents.OnStoppedListeningDueToTimeout.AddListener(HandleStoppedDueToTimeout);
        dictationService.DictationEvents.OnMinimumWakeThresholdHit.AddListener(HandleWakeThresholdHit);
        dictationService.DictationEvents.OnMicLevelChanged.AddListener(HandleMicLevelChanged);
        dictationService.DictationEvents.OnPartialTranscription.AddListener(HandlePartialTranscription);
        dictationService.DictationEvents.OnFullTranscription.AddListener(HandleFullTranscription);
        dictationService.DictationEvents.OnUserPartialTranscription.AddListener(HandleUserPartialTranscription);
        dictationService.DictationEvents.OnUserFullTranscription.AddListener(HandleUserFullTranscription);
        dictationService.DictationEvents.OnError.AddListener(HandleError);
        dictationService.DictationEvents.OnComplete.AddListener(HandleComplete);
        eventsWired = true;
    }

    private void UnwireEvents()
    {
        if (!eventsWired || dictationService == null || dictationService.DictationEvents == null)
        {
            return;
        }

        dictationService.DictationEvents.OnStartListening.RemoveListener(HandleStartListening);
        dictationService.DictationEvents.OnStoppedListening.RemoveListener(HandleStoppedListening);
        dictationService.DictationEvents.OnStoppedListeningDueToInactivity.RemoveListener(HandleStoppedDueToInactivity);
        dictationService.DictationEvents.OnStoppedListeningDueToTimeout.RemoveListener(HandleStoppedDueToTimeout);
        dictationService.DictationEvents.OnMinimumWakeThresholdHit.RemoveListener(HandleWakeThresholdHit);
        dictationService.DictationEvents.OnMicLevelChanged.RemoveListener(HandleMicLevelChanged);
        dictationService.DictationEvents.OnPartialTranscription.RemoveListener(HandlePartialTranscription);
        dictationService.DictationEvents.OnFullTranscription.RemoveListener(HandleFullTranscription);
        dictationService.DictationEvents.OnUserPartialTranscription.RemoveListener(HandleUserPartialTranscription);
        dictationService.DictationEvents.OnUserFullTranscription.RemoveListener(HandleUserFullTranscription);
        dictationService.DictationEvents.OnError.RemoveListener(HandleError);
        dictationService.DictationEvents.OnComplete.RemoveListener(HandleComplete);
        eventsWired = false;
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
                SetStatus("Permesso microfono concesso. Riprova il pulsante Microfono.");
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
    private System.Collections.IEnumerator RequestEditorMicrophonePermission()
    {
        yield return Application.RequestUserAuthorization(UserAuthorization.Microphone);
        waitingForMicPermission = false;

        if (Application.HasUserAuthorization(UserAuthorization.Microphone))
        {
            SetStatus("Permesso microfono concesso. Riprova il pulsante Microfono.");
        }
        else
        {
            SetStatus("Permesso microfono negato sul computer.");
        }
    }
#endif

    private void HandleStartListening()
    {
        SetStatus("Microfono in ascolto...");
    }

    private void HandleStoppedListening()
    {
        if (!receivedText)
        {
            SetStatus(GetNoTranscriptionStatus("Ascolto terminato"));
        }
    }

    private void HandleStoppedDueToInactivity()
    {
        if (!receivedText)
        {
            SetStatus(GetNoTranscriptionStatus("Ascolto fermato per inattivita'"));
        }
    }

    private void HandleStoppedDueToTimeout()
    {
        if (!receivedText)
        {
            SetStatus(GetNoTranscriptionStatus("Ascolto fermato per timeout"));
        }
    }

    private void HandleWakeThresholdHit()
    {
        reachedVoiceThreshold = true;
        SetStatus("Voce rilevata, trascrizione in corso...");
    }

    private void HandleMicLevelChanged(float level)
    {
        if (receivedText)
        {
            return;
        }

        maxMicLevelDuringRequest = Mathf.Max(maxMicLevelDuringRequest, level);
        if (Time.time - lastMicLevelStatusTime < 0.4f)
        {
            return;
        }

        lastMicLevelStatusTime = Time.time;
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
        ApplyPrompt(text);
    }

    private void HandleUserPartialTranscription(string userId, string text)
    {
        HandlePartialTranscription(text);
    }

    private void HandleUserFullTranscription(string userId, string text)
    {
        ApplyPrompt(text);
    }

    private void HandleError(string errorType, string message)
    {
        SetStatus("Errore dictation: " + errorType + " - " + message);
        Debug.LogWarning("[DictationPromptController] " + errorType + " - " + message);
    }

    private void HandleComplete(VoiceServiceRequest request)
    {
        if (receivedText || request == null || request.Results == null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(request.Results.Transcription))
        {
            ApplyPrompt(request.Results.Transcription);
            return;
        }

        string[] finals = request.Results.FinalTranscriptions;
        if (finals != null)
        {
            for (int i = finals.Length - 1; i >= 0; i--)
            {
                if (!string.IsNullOrWhiteSpace(finals[i]))
                {
                    ApplyPrompt(finals[i]);
                    return;
                }
            }
        }

        SetStatus("Dictation completata senza testo.");
    }

    private void ApplyPrompt(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        receivedText = true;
        if (flaskSender != null)
        {
            flaskSender.SetPromptFromDictation(text);
        }

        PromptChanged?.Invoke(text);
        SetStatus("Prompt acquisito: " + text);
    }

    private void SetStatus(string message)
    {
        Debug.Log("[DictationPromptController] " + message);
        StatusChanged?.Invoke(message);
    }
}
