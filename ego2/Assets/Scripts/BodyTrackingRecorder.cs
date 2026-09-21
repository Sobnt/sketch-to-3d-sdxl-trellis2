using UnityEngine;
using System.Collections.Generic;
using System.IO;

public class UnifiedBodyTrackingRecorder : MonoBehaviour
{
    [Header("Targets")]
    public GameObject liveOVRBody;
    public GameObject simulatedOVRBody;

    [Header("Configurazione Registrazione")]
    public string outputFileName = "FullBodyRecording.json";
    public float sampleRate = 30f;
    public bool recordOnStart = true;

    private float sampleInterval;
    private float timer;
    private bool isRecording = false;

    private GameObject currentActiveTarget;
    private Dictionary<string, Transform> jointMap = new Dictionary<string, Transform>();
    private List<FrameData> recordedFrames = new List<FrameData>();

    void Start()
    {
        sampleInterval = 1f / sampleRate;
        isRecording = recordOnStart;

        Debug.Log($"<color=cyan>[Recorder] Percorso di salvataggio base: {Application.persistentDataPath}</color>");
    }

    void Update()
    {
        if (!isRecording) return;

        GameObject target = GetActiveTarget();
        if (target == null) return;

        if (target != currentActiveTarget)
        {
            currentActiveTarget = target;
            if (currentActiveTarget == simulatedOVRBody) RemapJoints(target);
        }

        timer += Time.deltaTime;
        if (timer >= sampleInterval)
        {
            timer = 0f;
            CaptureFrame();
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
            jointMap[t.name] = t;
        }
    }

    private void CaptureFrame()
    {
        FrameData frame = new FrameData { time = Time.time, joints = new List<JointEntry>() };

        if (currentActiveTarget == liveOVRBody)
        {
            OVRPlugin.BodyState bodyState = new OVRPlugin.BodyState();
            if (OVRPlugin.GetBodyState(OVRPlugin.Step.Render, ref bodyState) && bodyState.JointLocations != null)
            {
                for (int i = 0; i < bodyState.JointLocations.Length; i++)
                {
                    var joint = bodyState.JointLocations[i];
                    frame.joints.Add(new JointEntry
                    {
                        jointIdentifier = ((OVRPlugin.BoneId)i).ToString(),
                        position = new Vector3(joint.Pose.Position.x, joint.Pose.Position.y, joint.Pose.Position.z),
                        rotation = new Quaternion(joint.Pose.Orientation.x, joint.Pose.Orientation.y, joint.Pose.Orientation.z, joint.Pose.Orientation.w)
                    });
                }
            }
        }
        else if (currentActiveTarget == simulatedOVRBody)
        {
            foreach (var kv in jointMap)
            {
                frame.joints.Add(new JointEntry
                {
                    jointIdentifier = kv.Key,
                    position = kv.Value.localPosition,
                    rotation = kv.Value.localRotation
                });
            }
        }

        if (frame.joints.Count > 0) recordedFrames.Add(frame);
    }

    [ContextMenu("Save Recording Now")]
    public void SaveToJson()
    {
        if (recordedFrames == null || recordedFrames.Count == 0)
        {
            Debug.LogWarning("[Recorder] Nessun dato da salvare.");
            return;
        }

        try
        {
            string json = JsonUtility.ToJson(new FrameList { frames = recordedFrames }, true);
            string path = Path.Combine(Application.persistentDataPath, outputFileName);

            File.WriteAllText(path, json);

            Debug.Log($"<color=green>[Recorder] SALVATO CON SUCCESSO!</color>\nFile: {path}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[Recorder] Errore: {e.Message}");
        }
    }

    void OnApplicationPause(bool pauseStatus) { if (pauseStatus) SaveToJson(); }
    void OnDisable() { SaveToJson(); }

    [System.Serializable] public class JointEntry { public string jointIdentifier; public Vector3 position; public Quaternion rotation; }
    [System.Serializable] public class FrameData { public float time; public List<JointEntry> joints; }
    [System.Serializable] public class FrameList { public List<FrameData> frames; }
}