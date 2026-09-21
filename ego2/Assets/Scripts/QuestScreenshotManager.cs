using UnityEngine;
using System.IO;
using System;
using TMPro; // Necessario per TextMeshPro
using System.Collections; // Necessario per le Coroutine

public class QuestScreenshotManager : MonoBehaviour
{
    [Header("Impostazioni Input")]
    public OVRInput.RawButton captureButton = OVRInput.RawButton.LIndexTrigger;
    [Tooltip("Se attivo, lo screenshot usa sempre il controller opposto a quello usato per disegnare.")]
    public bool useOppositeHandFromDrawing = true;
    public OVRInput.RawButton rightHandedCaptureButton = OVRInput.RawButton.LIndexTrigger;
    public OVRInput.RawButton leftHandedCaptureButton = OVRInput.RawButton.RIndexTrigger;
    public FingerTrailTrigger fingerTrailTrigger;

    [Header("Feedback Visivo")]
    [Tooltip("Trascina qui il componente Text (TMP) dalla scena")]
    public TextMeshProUGUI feedbackText;
    [Tooltip("Quanti secondi la scritta rimane visibile")]
    public float tempoVisibilita = 2.0f;

    void Start()
    {
        ResolveFingerTrailTrigger();

        // Nascondi il testo all'avvio del gioco
        if (feedbackText != null)
        {
            feedbackText.text = "";
        }
    }

    void Update()
    {
        if (OVRInput.GetDown(GetActiveCaptureButton()))
        {
            TakeScreenshot();
        }
    }

    private void ResolveFingerTrailTrigger()
    {
        if (fingerTrailTrigger != null)
        {
            return;
        }

#pragma warning disable CS0618
        fingerTrailTrigger = FindObjectOfType<FingerTrailTrigger>(true);
#pragma warning restore CS0618
    }

    private OVRInput.RawButton GetActiveCaptureButton()
    {
        if (!useOppositeHandFromDrawing)
        {
            return captureButton;
        }

        ResolveFingerTrailTrigger();
        if (fingerTrailTrigger == null)
        {
            return captureButton;
        }

        return fingerTrailTrigger.IsCurrentlyRightHanded ? rightHandedCaptureButton : leftHandedCaptureButton;
    }

    private void TakeScreenshot()
    {
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string fileName = $"QuestShot_{timestamp}.png";
        string folderPath = "";

#if UNITY_EDITOR || UNITY_STANDALONE_WIN
        folderPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
#else
        folderPath = Application.persistentDataPath;
#endif

        string filePath = Path.Combine(folderPath, fileName);
        ScreenCapture.CaptureScreenshot(filePath);

        Debug.Log($"[DEBUG] Screenshot salvato con successo in: {filePath}");

        // Mostra il feedback testuale in VR
        if (feedbackText != null)
        {
            StopAllCoroutines(); // Ferma eventuali timer precedenti se clicchi velocemente 2 volte
            StartCoroutine(MostraScritta());
        }
    }

    // Coroutine che gestisce la comparsa e scomparsa del testo
    private IEnumerator MostraScritta()
    {
        feedbackText.text = "Screenshot acquisito!";

        // Aspetta i secondi impostati
        yield return new WaitForSeconds(tempoVisibilita);

        // Svuota il testo per farlo sparire
        feedbackText.text = "";
    }
}