using System.Collections.Generic;
using UnityEngine;
using System.IO;

public class FullBodyDebugRecorder : MonoBehaviour
{
    [Header("Targets (I due omini)")]
    public GameObject liveOVRBody;       // L'oggetto con OVRBody (Hardware)
    public GameObject simulatedOVRBody;  // L'oggetto con OVRBodyFromPose (Simulazione)

    [Header("Registrazione")]
    public string outputFileName = "FullBodyRecording.json";
    public float recordInterval = 0.1f;

    private float recordTimer = 0f;
    private List<FrameData> recordedFrames = new List<FrameData>();

    // Riferimento all'omino che stiamo seguendo in questo momento
    private GameObject currentActiveTarget;
    private Dictionary<string, Transform> jointMap = new Dictionary<string, Transform>();

    void Update()
    {
        // 1. Determina quale omino è attivo
        GameObject target = GetActiveTarget();

        if (target == null) return;

        // 2. Se l'omino attivo è cambiato (es. hai appena tolto la spunta), rimappa i joint
        if (target != currentActiveTarget)
        {
            RemapJoints(target);
            currentActiveTarget = target;
        }

        // 3. Gestione del timer e registrazione
        recordTimer += Time.deltaTime;
        if (recordTimer >= recordInterval)
        {
            recordTimer = 0f;
            RecordCurrentFrame();
        }
    }

    private GameObject GetActiveTarget()
    {
        if (liveOVRBody != null && liveOVRBody.activeInHierarchy) return liveOVRBody;
        if (simulatedOVRBody != null && simulatedOVRBody.activeInHierarchy) return simulatedOVRBody;
        return null;
    }

    private void RemapJoints(GameObject target)
    {
        jointMap.Clear();
        foreach (Transform t in target.GetComponentsInChildren<Transform>(true))
        {
            // Usiamo il nome come chiave. 
            // Nota: Se ci sono nomi duplicati nella gerarchia, potresti aver bisogno di logica extra
            jointMap[t.name] = t;
        }
        Debug.Log($"[Recorder] Bersaglio cambiato: {target.name}. Mappati {jointMap.Count} joint.");
    }

    private void RecordCurrentFrame()
    {
        if (jointMap.Count == 0) return;

        List<JointEntry> frameJoints = new List<JointEntry>();
        foreach (var kv in jointMap)
        {
            frameJoints.Add(new JointEntry
            {
                name = kv.Key,
                position = kv.Value.localPosition,
                rotation = kv.Value.localRotation.eulerAngles
            });
        }
        recordedFrames.Add(new FrameData { time = Time.time, joints = frameJoints });
    }

    void OnApplicationQuit()
    {
        SaveToJson();
    }

    void SaveToJson()
    {
        if (recordedFrames.Count == 0) return;

        string json = JsonUtility.ToJson(new FrameList { frames = recordedFrames }, true);
        string path = Path.Combine(Application.persistentDataPath, outputFileName);
        File.WriteAllText(path, json);
        Debug.Log($"Registrazione completata: {path} ({recordedFrames.Count} frame)");
    }

    // Classi di supporto per JSON
    [System.Serializable]
    public class JointEntry { public string name; public Vector3 position; public Vector3 rotation; }
    [System.Serializable]
    public class FrameData { public float time; public List<JointEntry> joints; }
    [System.Serializable]
    public class FrameList { public List<FrameData> frames; }
}