using UnityEngine;
using System.IO;
using System;
using TMPro;
using System.Collections;
using System.Collections.Generic;

public class QuestOpaqueInvertedScreenshot : MonoBehaviour
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

    [Header("Integrazione Server AI")]
    [Tooltip("Trascina qui l'oggetto che contiene lo script FlaskSenderOnly")]
    public FlaskSenderOnly flaskSender;

    [Header("Pulizia Screenshot")]
    [Tooltip("Nasconde il pannello AI durante lo screenshot, poi lo riattiva subito dopo.")]
    public bool hideRuntimeUIBeforeCapture = true;
    public string runtimeUIPanelName = "AI_Generation_UI";
    [Tooltip("Nasconde i modelli/icone dei controller durante lo screenshot.")]
    public bool hideControllerVisualsBeforeCapture = true;
    public string[] controllerObjectsToHide =
    {
        "RightControllerInHandAnchor",
        "LeftControllerInHandAnchor",
        "RightHandOnControllerAnchor",
        "LeftHandOnControllerAnchor"
    };

    [Header("Crop ControlNet")]
    [Tooltip("Ritaglia l'immagine attorno al tratto nero e lo centra su una canvas quadrata.")]
    public bool cropAndCenterSketch = true;
    [Range(256, 2048)]
    public int outputImageSize = 1024;
    [Range(0, 512)]
    public int cropPaddingPixels = 80;
    [Range(0f, 1f)]
    [Tooltip("Dopo l'inversione, i pixel piu' scuri di questa soglia vengono considerati parte dello sketch.")]
    public float inkBrightnessThreshold = 0.85f;

    private Coroutine textCoroutine;
    private bool isCapturing;

    private struct HiddenVisibilityState
    {
        public Component Component;
        public bool Enabled;
    }

    private void Start()
    {
        ResolveFingerTrailTrigger();

        if (feedbackText != null)
        {
            feedbackText.text = "";
        }
    }

    private void Update()
    {
        if (OVRInput.GetDown(GetActiveCaptureButton()))
        {
            StartCoroutine(TakeOpaqueInvertedScreenshotCoroutine());
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

    private IEnumerator TakeOpaqueInvertedScreenshotCoroutine()
    {
        if (isCapturing)
        {
            yield break;
        }

        isCapturing = true;
        List<HiddenVisibilityState> hiddenObjects = HideCaptureExclusions();

        yield return new WaitForEndOfFrame();

        Texture2D screenshot = null;
        Texture2D processedScreenshot = null;
        try
        {
            screenshot = ScreenCapture.CaptureScreenshotAsTexture();
            processedScreenshot = ProcessScreenshotForControlNet(screenshot);

            byte[] bytes = processedScreenshot.EncodeToPNG();

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string fileName = $"QuestShot_Invertito_Opaco_{timestamp}.png";
            string folderPath;

#if UNITY_EDITOR || UNITY_STANDALONE_WIN
            folderPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
#else
            folderPath = Application.persistentDataPath;
#endif

            string filePath = Path.Combine(folderPath, fileName);

            File.WriteAllBytes(filePath, bytes);
            Debug.Log($"[DEBUG] Screenshot invertito, pulito e centrato salvato con successo in: {filePath}");

            if (flaskSender != null)
            {
                flaskSender.InviaAFlask(filePath);
            }
            else
            {
                Debug.LogWarning("[Flask] Attenzione: FlaskSender non assegnato nell'Inspector.");
            }

            if (feedbackText != null)
            {
                if (textCoroutine != null)
                {
                    StopCoroutine(textCoroutine);
                }
                textCoroutine = StartCoroutine(MostraScritta());
            }
        }
        finally
        {
            RestoreCaptureExclusions(hiddenObjects);
            isCapturing = false;

            if (processedScreenshot != null && processedScreenshot != screenshot)
            {
                Destroy(processedScreenshot);
            }

            if (screenshot != null)
            {
                Destroy(screenshot);
            }
        }
    }

    private Texture2D ProcessScreenshotForControlNet(Texture2D screenshot)
    {
        Color[] pixels = screenshot.GetPixels();
        for (int i = 0; i < pixels.Length; i++)
        {
            float r = 1f - pixels[i].r;
            float g = 1f - pixels[i].g;
            float b = 1f - pixels[i].b;

            pixels[i] = new Color(r, g, b, 1.0f);
        }

        if (!cropAndCenterSketch || !TryFindInkBounds(pixels, screenshot.width, screenshot.height, out int minX, out int minY, out int maxX, out int maxY))
        {
            screenshot.SetPixels(pixels);
            screenshot.Apply();

            if (cropAndCenterSketch)
            {
                Debug.LogWarning("[Screenshot] Nessun tratto nero rilevato: invio l'immagine completa invertita.");
            }

            return screenshot;
        }

        return CreateCenteredCrop(pixels, screenshot.width, screenshot.height, minX, minY, maxX, maxY);
    }

    private bool TryFindInkBounds(Color[] pixels, int width, int height, out int minX, out int minY, out int maxX, out int maxY)
    {
        minX = width;
        minY = height;
        maxX = -1;
        maxY = -1;

        for (int y = 0; y < height; y++)
        {
            int rowOffset = y * width;
            for (int x = 0; x < width; x++)
            {
                Color pixel = pixels[rowOffset + x];
                float brightness = (pixel.r + pixel.g + pixel.b) / 3f;
                if (brightness > inkBrightnessThreshold)
                {
                    continue;
                }

                minX = Mathf.Min(minX, x);
                minY = Mathf.Min(minY, y);
                maxX = Mathf.Max(maxX, x);
                maxY = Mathf.Max(maxY, y);
            }
        }

        return maxX >= minX && maxY >= minY;
    }

    private Texture2D CreateCenteredCrop(Color[] pixels, int width, int height, int minX, int minY, int maxX, int maxY)
    {
        int objectWidth = maxX - minX + 1;
        int objectHeight = maxY - minY + 1;
        int side = Mathf.Max(objectWidth, objectHeight) + cropPaddingPixels * 2;
        side = Mathf.Clamp(side, 1, Mathf.Min(width, height));

        int centerX = Mathf.RoundToInt((minX + maxX) * 0.5f);
        int centerY = Mathf.RoundToInt((minY + maxY) * 0.5f);
        int cropX = Mathf.Clamp(centerX - side / 2, 0, Mathf.Max(0, width - side));
        int cropY = Mathf.Clamp(centerY - side / 2, 0, Mathf.Max(0, height - side));

        int size = Mathf.Max(1, outputImageSize);
        Texture2D cropped = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color[] centeredPixels = new Color[size * size];

        for (int y = 0; y < size; y++)
        {
            float sourceY01 = (y + 0.5f) / size;
            int sourceY = cropY + Mathf.Clamp(Mathf.FloorToInt(sourceY01 * side), 0, side - 1);
            int targetRow = y * size;

            for (int x = 0; x < size; x++)
            {
                float sourceX01 = (x + 0.5f) / size;
                int sourceX = cropX + Mathf.Clamp(Mathf.FloorToInt(sourceX01 * side), 0, side - 1);
                Color pixel = pixels[sourceY * width + sourceX];
                centeredPixels[targetRow + x] = new Color(pixel.r, pixel.g, pixel.b, 1f);
            }
        }

        cropped.SetPixels(centeredPixels);
        cropped.Apply();

        Debug.Log($"[Screenshot] Crop centrato: bounds=({minX},{minY})-({maxX},{maxY}), crop={side}px, output={size}x{size}.");
        return cropped;
    }

    private List<HiddenVisibilityState> HideCaptureExclusions()
    {
        List<HiddenVisibilityState> hiddenObjects = new List<HiddenVisibilityState>();

        if (hideRuntimeUIBeforeCapture && !string.IsNullOrWhiteSpace(runtimeUIPanelName))
        {
            HideSceneObjectByName(runtimeUIPanelName, hiddenObjects);
        }

        if (hideControllerVisualsBeforeCapture && controllerObjectsToHide != null)
        {
            for (int i = 0; i < controllerObjectsToHide.Length; i++)
            {
                HideSceneObjectByName(controllerObjectsToHide[i], hiddenObjects);
            }
        }

        return hiddenObjects;
    }

    private void HideSceneObjectByName(string objectName, List<HiddenVisibilityState> hiddenObjects)
    {
        if (string.IsNullOrWhiteSpace(objectName))
        {
            return;
        }

        Transform[] transforms = Resources.FindObjectsOfTypeAll<Transform>();
        for (int i = 0; i < transforms.Length; i++)
        {
            GameObject candidate = transforms[i].gameObject;
            if (candidate.name != objectName || !candidate.scene.IsValid() || IsAlreadyStored(candidate, hiddenObjects))
            {
                continue;
            }

            HideVisualComponents(candidate, hiddenObjects);
        }
    }

    private void HideVisualComponents(GameObject root, List<HiddenVisibilityState> hiddenObjects)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            StoreAndDisable(renderers[i], hiddenObjects);
        }

        Canvas[] canvases = root.GetComponentsInChildren<Canvas>(true);
        for (int i = 0; i < canvases.Length; i++)
        {
            StoreAndDisable(canvases[i], hiddenObjects);
        }

        UnityEngine.UI.Graphic[] graphics = root.GetComponentsInChildren<UnityEngine.UI.Graphic>(true);
        for (int i = 0; i < graphics.Length; i++)
        {
            StoreAndDisable(graphics[i], hiddenObjects);
        }
    }

    private void StoreAndDisable(Component component, List<HiddenVisibilityState> hiddenObjects)
    {
        if (component == null || IsAlreadyStored(component, hiddenObjects))
        {
            return;
        }

        hiddenObjects.Add(new HiddenVisibilityState
        {
            Component = component,
            Enabled = GetComponentEnabled(component)
        });
        SetComponentEnabled(component, false);
    }

    private bool IsAlreadyStored(GameObject gameObject, List<HiddenVisibilityState> hiddenObjects)
    {
        for (int i = 0; i < hiddenObjects.Count; i++)
        {
            if (hiddenObjects[i].Component != null && hiddenObjects[i].Component.gameObject == gameObject)
            {
                return true;
            }
        }

        return false;
    }

    private bool IsAlreadyStored(Component component, List<HiddenVisibilityState> hiddenObjects)
    {
        for (int i = 0; i < hiddenObjects.Count; i++)
        {
            if (hiddenObjects[i].Component == component)
            {
                return true;
            }
        }

        return false;
    }

    private bool GetComponentEnabled(Component component)
    {
        if (component is Renderer renderer)
        {
            return renderer.enabled;
        }

        if (component is Behaviour behaviour)
        {
            return behaviour.enabled;
        }

        return true;
    }

    private void SetComponentEnabled(Component component, bool enabled)
    {
        if (component is Renderer renderer)
        {
            renderer.enabled = enabled;
            return;
        }

        if (component is Behaviour behaviour)
        {
            behaviour.enabled = enabled;
        }
    }

    private void RestoreCaptureExclusions(List<HiddenVisibilityState> hiddenObjects)
    {
        for (int i = hiddenObjects.Count - 1; i >= 0; i--)
        {
            if (hiddenObjects[i].Component != null)
            {
                SetComponentEnabled(hiddenObjects[i].Component, hiddenObjects[i].Enabled);
            }
        }
    }

    private IEnumerator MostraScritta()
    {
        feedbackText.text = "Screenshot Invertito (Sfondo Opaco) Acquisito!";

        yield return new WaitForSeconds(tempoVisibilita);

        feedbackText.text = "";
        textCoroutine = null;
    }
}
