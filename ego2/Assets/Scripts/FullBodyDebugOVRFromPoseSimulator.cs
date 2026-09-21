using System.Collections.Generic;
using UnityEngine;

public class FullBodyDebugOVRFromPoseSimulator : MonoBehaviour
{
    [Header("Visualizer")]
    public GameObject ovrBodyFromPose; // Prefab OVRBodyFromPose nella scena

    private Dictionary<string, Transform> jointMap;

    void Start()
    {
        if (ovrBodyFromPose == null)
        {
            Debug.LogError("Collega il prefab OVRBodyFromPose!");
            return;
        }

        // Mappa tutti i transform dei joint del prefab
        jointMap = new Dictionary<string, Transform>();
        foreach (Transform t in ovrBodyFromPose.GetComponentsInChildren<Transform>())
        {
            jointMap[t.name] = t;
        }
    }

    void Update()
    {
        if (jointMap == null) return;

        // Se questo script sta girando, significa che il Manager lo ha attivato.

        float time = Time.time;

        foreach (var kv in jointMap)
        {
            string jointName = kv.Key;
            Transform joint = kv.Value;

            // Simulazione con dati finti
            Vector3 posOffset = new Vector3(
                Mathf.Sin(time + jointName.Length),
                Mathf.Cos(time + jointName.Length * 0.5f) * 0.5f,
                Mathf.Sin(time * 0.5f + jointName.Length * 0.3f) * 0.05f
            );

            Quaternion rot = Quaternion.Euler(
                Mathf.Sin(time + jointName.Length * 10f) * 15f,
                Mathf.Cos(time + jointName.Length * 5f) * 15f,
                Mathf.Sin(time + jointName.Length * 7f) * 15f
            );

            joint.localPosition = posOffset;
            joint.localRotation = rot;
        }
    }
}