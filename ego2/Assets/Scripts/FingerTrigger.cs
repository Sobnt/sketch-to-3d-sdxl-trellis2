using UnityEngine;
using System.Collections.Generic;

public class FingerTrailTrigger : MonoBehaviour
{
    [Header("I Contenitori")]
    [Tooltip("Trascina qui l'oggetto vuoto posizionato sulla punta del controller DESTRO (RightHandAnchor)")]
    public Transform rightHandTip;
    [Tooltip("Trascina qui l'oggetto vuoto posizionato sulla punta del controller SINISTRO (LeftHandAnchor)")]
    public Transform leftHandTip;
    [Tooltip("L'anchor attualmente in uso. Verra' gestito in automatico se usi l'UI.")]
    public Transform liveControllerTip;
    public GameObject simulatedOVRBody;

    [Header("Configurazione Input")]
    [Tooltip("RTouch = Mano Destra, LTouch = Mano Sinistra")]
    public OVRInput.Controller controller = OVRInput.Controller.RTouch;

    [Header("Impostazioni Cambio Mano")]
    [Tooltip("Pulsante fisico opzionale per invertire la mano (es. Y o B)")]
    public OVRInput.RawButton switchHandButton = OVRInput.RawButton.Y;
    private bool isCurrentlyRightHanded = true; // Tiene traccia della mano attuale
    public bool IsCurrentlyRightHanded => isCurrentlyRightHanded;

    [Header("Ricerca Joint (Solo per Simulata)")]
    public string simulatedBoneName = "RightHandIndexTip";

    [Header("Impostazioni Linea")]
    [Tooltip("Distanza minima tra due punti (metri). 0.01 = 1cm.")]
    public float minVertexDistance = 0.01f;
    public Material lineMaterial;
    public float lineWidth = 0.01f;
    public Color lineColor = Color.white;

    [Header("Impostazioni Gomma")]
    [Tooltip("PrimaryHandTrigger = Grilletto del medio")]
    public OVRInput.Button eraserButton = OVRInput.Button.PrimaryHandTrigger;
    public float eraserRadius = 0.05f;

    [Header("Impostazioni Modalità Retta")]
    [Tooltip("Usa RawButton.X per controller sinistro, RawButton.A per controller destro")]
    public OVRInput.RawButton toggleModeButton = OVRInput.RawButton.X;

    private bool isStraightLineMode = false;
    private Transform cachedSimulatedBone;
    private List<Vector3> points = new List<Vector3>();
    private LineRenderer currentLine;
    private List<LineRenderer> allLines = new List<LineRenderer>();
    private bool wasDrawing = false;

    void Start()
    {
        if (lineMaterial == null)
        {
            Debug.LogWarning("[FingerTrailTool] Attenzione: Assegna un Materiale al campo Line Material nell'Inspector!");
        }

        // Imposta la mano destra come default all'avvio se liveControllerTip non è stato assegnato manualmente
        if (liveControllerTip == null && rightHandTip != null)
        {
            isCurrentlyRightHanded = true;
            SetHandedness(isCurrentlyRightHanded);
        }
    }

    void LateUpdate()
    {
        Vector3 currentPos = transform.position;
        bool hasValidData = false;

        // --- 1. LOGICA DI POSIZIONAMENTO ---
        if (liveControllerTip != null && liveControllerTip.gameObject.activeInHierarchy)
        {
            currentPos = liveControllerTip.position;
            if (currentPos != Vector3.zero) hasValidData = true;
        }
        else if (simulatedOVRBody != null && simulatedOVRBody.activeInHierarchy)
        {
            if (cachedSimulatedBone == null)
                cachedSimulatedBone = TrovaOsso(simulatedOVRBody.transform, simulatedBoneName);

            if (cachedSimulatedBone != null)
            {
                currentPos = cachedSimulatedBone.position;
                if (currentPos != Vector3.zero) hasValidData = true;
            }
        }
        transform.position = currentPos;

        // --- 2. CONTROLLO INPUT E TOGGLE ---

        // Controllo cambio mano al RILASCIO del pulsante fisico sul controller (opzionale)
        if (OVRInput.GetUp(switchHandButton))
        {
            ToggleHandedness();
        }

        // Controllo per la modalità retta/curva
        if (OVRInput.GetDown(toggleModeButton))
        {
            isStraightLineMode = !isStraightLineMode;
        }

        float triggerPressure = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, controller);
        bool isErasing = OVRInput.Get(eraserButton, controller);
        bool isDrawing = triggerPressure > 0.1f && !isErasing;

        if (hasValidData)
        {
            if (isErasing)
            {
                EraseLines(currentPos);
                if (wasDrawing) wasDrawing = false;
            }
            else
            {
                if (isDrawing && !wasDrawing)
                {
                    StartNewLine(currentPos);
                }
                else if (isDrawing && wasDrawing)
                {
                    UpdateLine(currentPos);
                }
            }
        }

        if (!isErasing)
        {
            wasDrawing = isDrawing;
        }
    }

    private void StartNewLine(Vector3 startPoint)
    {
        GameObject newLineObj = new GameObject("TrattoDisegno");
        currentLine = newLineObj.AddComponent<LineRenderer>();
        currentLine.useWorldSpace = true;
        currentLine.positionCount = 0;
        currentLine.material = lineMaterial;
        currentLine.startWidth = lineWidth;
        currentLine.endWidth = lineWidth;
        currentLine.startColor = lineColor;
        currentLine.endColor = lineColor;
        currentLine.numCapVertices = 5;
        currentLine.numCornerVertices = 5;

        points.Clear();
        allLines.Add(currentLine);
        UpdateLine(startPoint);
    }

    private void UpdateLine(Vector3 newPoint)
    {
        if (currentLine == null) return;

        if (isStraightLineMode)
        {
            // Modalità retta: crea solo il secondo punto e aggiorna continuamente la sua posizione
            if (points.Count < 2)
            {
                points.Add(newPoint);
                currentLine.positionCount = points.Count;
            }
            else
            {
                points[points.Count - 1] = newPoint; // Muove l'ultimo punto
            }
            currentLine.SetPosition(points.Count - 1, newPoint);
        }
        else
        {
            // Modalità curva: aggiunge un nuovo punto quando superi la distanza minima
            if (points.Count == 0 || Vector3.Distance(points[points.Count - 1], newPoint) > minVertexDistance)
            {
                points.Add(newPoint);
                currentLine.positionCount = points.Count;
                currentLine.SetPosition(points.Count - 1, newPoint);
            }
        }
    }

    private void EraseLines(Vector3 eraserPos)
    {
        for (int i = allLines.Count - 1; i >= 0; i--)
        {
            LineRenderer lineToCheck = allLines[i];
            if (lineToCheck == null)
            {
                allLines.RemoveAt(i);
                continue;
            }

            Vector3[] linePoints = new Vector3[lineToCheck.positionCount];
            lineToCheck.GetPositions(linePoints);
            bool lineErased = false;

            foreach (Vector3 pt in linePoints)
            {
                if (Vector3.Distance(pt, eraserPos) <= eraserRadius)
                {
                    Destroy(lineToCheck.gameObject);
                    allLines.RemoveAt(i);
                    lineErased = true;
                    break;
                }
            }

            if (lineErased && lineToCheck == currentLine)
            {
                currentLine = null;
                points.Clear();
            }
        }
    }

    private Transform TrovaOsso(Transform genitore, string nomeDaCercare)
    {
        Transform[] tuttiIFigli = genitore.GetComponentsInChildren<Transform>(true);
        foreach (Transform t in tuttiIFigli)
        {
            if (t.name.Contains(nomeDaCercare)) return t;
        }
        return null;
    }

    public void ClearDrawing()
    {
        foreach (LineRenderer l in allLines)
        {
            if (l != null) Destroy(l.gameObject);
        }
        allLines.Clear();
        points.Clear();
        currentLine = null;
    }

    /// <summary>
    /// Metodo da richiamare tramite UI (Unity Event del Poke Button) per invertire la mano.
    /// </summary>
    public void ToggleHandedness()
    {
        isCurrentlyRightHanded = !isCurrentlyRightHanded; // Inverte lo stato
        SetHandedness(isCurrentlyRightHanded); // Applica la modifica
    }

    /// <summary>
    /// Imposta direttamente la mano specifica.
    /// </summary>
    /// <param name="isRightHanded">Passare 'true' per destrorsi, 'false' per mancini.</param>
    public void SetHandedness(bool isRightHanded)
    {
        if (isRightHanded)
        {
            // Configurazione Destrorsa
            liveControllerTip = rightHandTip;
            controller = OVRInput.Controller.RTouch;
            simulatedBoneName = "RightHandIndexTip";
            toggleModeButton = OVRInput.RawButton.X; // Pulsante X del controller sinistro
        }
        else
        {
            // Configurazione Mancina
            liveControllerTip = leftHandTip;
            controller = OVRInput.Controller.LTouch;
            simulatedBoneName = "LeftHandIndexTip";
            toggleModeButton = OVRInput.RawButton.A; // Pulsante A del controller destro
        }

        isCurrentlyRightHanded = isRightHanded; // Sincronizza la variabile interna

        // Azzera la cache dell'osso per costringere lo script a cercare il nuovo dito (destro o sinistro)
        cachedSimulatedBone = null;

        Debug.Log("[FingerTrailTool] Mano aggiornata. Destrorso: " + isRightHanded);
    }
}