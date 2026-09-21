using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(LineRenderer))]
public class FingerTrailTool : MonoBehaviour
{
    [Header("I Contenitori")]
    public GameObject liveOVRBody;
    public GameObject simulatedOVRBody;

    [Header("Ricerca per Simulazione (Gerarchia Transform)")]
    public string simulatedBoneName = "RightHandIndexTip";

    [Header("Ricerca per Hardware (API OVRPlugin)")]
    public int liveJointID = 0;

    [Header("Impostazioni Linea")]
    [Tooltip("Distanza minima tra due punti (metri). 0.01 = 1cm. Aumentalo per risparmiare memoria.")]
    public float minVertexDistance = 0.01f;

    private LineRenderer line;
    private Transform cachedSimulatedBone;
    private List<Vector3> points = new List<Vector3>();
    private bool isTrackingReady = false;

    void Start()
    {
        line = GetComponent<LineRenderer>();

        // Setup iniziale della linea
        line.useWorldSpace = true;
        line.positionCount = 0;

        if (line.material == null)
        {
            Debug.LogWarning("Ricordati di assegnare un Materiale al Line Renderer nell'Inspector!");
        }
    }

    void LateUpdate()
    {
        Vector3 currentPos = transform.position;
        bool hasValidData = false;

        // 1. Logica di posizionamento (Live)
        if (liveOVRBody != null && liveOVRBody.activeInHierarchy)
        {
            OVRPlugin.BodyState bodyState = new OVRPlugin.BodyState();
            if (OVRPlugin.GetBodyState(OVRPlugin.Step.Render, ref bodyState))
            {
                if (bodyState.JointLocations != null && liveJointID < bodyState.JointLocations.Length)
                {
                    var jointPose = bodyState.JointLocations[liveJointID].Pose;
                    Vector3 localPos = new Vector3(jointPose.Position.x, jointPose.Position.y, -jointPose.Position.z);
                    currentPos = liveOVRBody.transform.TransformPoint(localPos);

                    // Consideriamo i dati validi solo se la posizione non è esattamente zero
                    if (currentPos != Vector3.zero) hasValidData = true;
                }
            }
        }
        // 2. Logica di posizionamento (Simulata)
        else if (simulatedOVRBody != null && simulatedOVRBody.activeInHierarchy)
        {
            if (cachedSimulatedBone == null)
            {
                cachedSimulatedBone = TrovaOsso(simulatedOVRBody.transform, simulatedBoneName);
            }

            if (cachedSimulatedBone != null)
            {
                currentPos = cachedSimulatedBone.position;
                // Nella simulazione, se l'osso si è mosso dal centro, è pronto
                if (currentPos != Vector3.zero) hasValidData = true;
            }
        }

        // Applichiamo la posizione all'oggetto
        transform.position = currentPos;

        if (hasValidData)
        {
            UpdateLine(currentPos);
        }
    }

    private void UpdateLine(Vector3 newPoint)
    {
        if (!isTrackingReady)
        {
            isTrackingReady = true;
            points.Clear(); // Pulizia di sicurezza
        }

        if (points.Count == 0 || Vector3.Distance(points[points.Count - 1], newPoint) > minVertexDistance)
        {
            points.Add(newPoint);
            line.positionCount = points.Count;
            line.SetPosition(points.Count - 1, newPoint);
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

}