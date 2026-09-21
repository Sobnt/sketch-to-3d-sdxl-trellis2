using UnityEngine;
using UnityEngine.EventSystems;

public sealed class PersistentXRSceneObject : MonoBehaviour
{
}

public static class XRSceneRigPersistence
{
    public static void PreserveForTrellisScene()
    {
        OVRCameraRig cameraRig = Object.FindFirstObjectByType<OVRCameraRig>();
        if (cameraRig != null)
        {
            PreserveRoot(cameraRig.transform.root.gameObject);
        }
        else
        {
            Debug.LogWarning("[XRSceneRigPersistence] OVRCameraRig non trovato: la scena Trellis creera' un rig di emergenza.");
        }

        EventSystem eventSystem = Object.FindFirstObjectByType<EventSystem>();
        if (eventSystem != null)
        {
            PreserveRoot(eventSystem.transform.root.gameObject);
        }

        // Do not preserve scene-1 canvas interaction objects here: in this project
        // they are children of the drawing UI, so keeping them would also keep the
        // old parameters panel visible inside the Trellis scene.
    }

    public static void ReleasePersistentSceneObjects()
    {
        PersistentXRSceneObject[] persistentObjects =
            Resources.FindObjectsOfTypeAll<PersistentXRSceneObject>();

        for (int i = 0; i < persistentObjects.Length; i++)
        {
            PersistentXRSceneObject marker = persistentObjects[i];
            if (marker == null || !marker.gameObject.scene.IsValid())
            {
                continue;
            }

            marker.gameObject.SetActive(false);
            Object.Destroy(marker.gameObject);
        }
    }

    private static void PreserveRoot(GameObject root)
    {
        if (root == null)
        {
            return;
        }

        if (root.GetComponent<PersistentXRSceneObject>() == null)
        {
            root.AddComponent<PersistentXRSceneObject>();
        }

        Object.DontDestroyOnLoad(root);
    }
}
