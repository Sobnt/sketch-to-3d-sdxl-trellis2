using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public class FlaskSenderOnly : MonoBehaviour
{
    [Header("Network Configuration")]
    public string serverUrl = "http://127.0.0.1:5001";

    [Header("Server Status")]
    public bool showServerStatusIndicator = false;
    public string statusEndpoint = "/health";
    public string expectedStatusResponse = "ok";
    public float statusCheckInterval = 2.0f;
    public int statusRequestTimeout = 2;
    public Vector2 statusIndicatorPosition = new Vector2(20f, 20f);
    public float statusIndicatorSize = 22f;
    public bool showServerStatusLabel = true;

    [Header("Generazione AI")]
    [TextArea(2, 4)]
    public string aiPrompt = "A modern wooden chair";

    [Header("Parametri SDXL")]
    [Range(0f, 1f)] public float strength = 0.85f;
    [Range(0f, 1f)] public float startPercent = 0.0f;
    [Range(0f, 1f)] public float endPercent = 0.9f;

    [Header("Qualità")]
    [Range(1f, 15f)] public float cfg = 7.0f;
    [Range(10, 50)] public int steps = 20;

    [Header("Magia Ollama")]
    public bool magicEnrich = true;

    [Header("Generazione 3D")]
    public string trellisEndpoint = "/generate_3d";
    public int trellisTimeout = 300;

    [Header("Runtime UI")]
    public bool autoCreateGenerationUI = true;

    public event Action GenerationStarted;
    public event Action<GeneratedImageResult> GenerationCompleted;
    public event Action<string> GenerationFailed;
    public event Action<string> TrellisCompleted;
    public event Action<string> TrellisFailed;

    private bool isProcessing = false;
    private bool isTrellisProcessing = false;
    private bool isServerOnline = false;
    private bool hasCheckedServer = false;
    private Texture2D statusDotTexture;
    private GUIStyle statusLabelStyle;

    private void Start()
    {
        if (autoCreateGenerationUI && GetComponent<GenerationHistoryUI>() == null)
        {
            gameObject.AddComponent<GenerationHistoryUI>();
        }
    }

    private void OnEnable()
    {
        StartCoroutine(ServerStatusRoutine());
    }

    private void OnDisable()
    {
        if (statusDotTexture != null)
        {
            Destroy(statusDotTexture);
            statusDotTexture = null;
        }
    }

    private IEnumerator ServerStatusRoutine()
    {
        WaitForSeconds wait = new WaitForSeconds(Mathf.Max(0.5f, statusCheckInterval));

        while (enabled)
        {
            yield return CheckServerStatus();
            yield return wait;
        }
    }

    private IEnumerator CheckServerStatus()
    {
        string endpoint = string.IsNullOrWhiteSpace(statusEndpoint) ? "" : statusEndpoint.Trim();
        if (!string.IsNullOrEmpty(endpoint) && !endpoint.StartsWith("/"))
        {
            endpoint = "/" + endpoint;
        }

        string url = $"{serverUrl.TrimEnd('/')}{endpoint}";
        UnityWebRequest request = UnityWebRequest.Get(url);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.timeout = Mathf.Max(1, statusRequestTimeout);

        yield return request.SendWebRequest();

        string responseText = request.downloadHandler != null ? request.downloadHandler.text : "";
        hasCheckedServer = true;
        isServerOnline = request.result == UnityWebRequest.Result.Success &&
                         request.responseCode >= 200 &&
                         request.responseCode < 300 &&
                         ResponseContainsExpectedStatus(responseText);

        request.Dispose();
    }

    private bool ResponseContainsExpectedStatus(string responseText)
    {
        if (string.IsNullOrWhiteSpace(expectedStatusResponse))
        {
            return !string.IsNullOrWhiteSpace(responseText);
        }

        return !string.IsNullOrWhiteSpace(responseText) &&
               responseText.IndexOf(expectedStatusResponse, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void OnGUI()
    {
        if (!showServerStatusIndicator)
        {
            return;
        }

        Color dotColor = hasCheckedServer && isServerOnline ? Color.green : Color.red;
        DrawStatusDot(new Rect(statusIndicatorPosition.x, statusIndicatorPosition.y, statusIndicatorSize, statusIndicatorSize), dotColor);

        if (showServerStatusLabel)
        {
            if (statusLabelStyle == null)
            {
                statusLabelStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 18,
                    fontStyle = FontStyle.Bold
                };
            }

            statusLabelStyle.normal.textColor = Color.white;
            string statusText = hasCheckedServer && isServerOnline ? "Server ON" : "Server OFF";
            Rect labelRect = new Rect(
                statusIndicatorPosition.x + statusIndicatorSize + 8f,
                statusIndicatorPosition.y - 2f,
                180f,
                statusIndicatorSize + 8f
            );
            GUI.Label(labelRect, statusText, statusLabelStyle);
        }
    }

    private void DrawStatusDot(Rect rect, Color color)
    {
        if (statusDotTexture == null)
        {
            statusDotTexture = CreateCircleTexture(64);
        }

        Color previousColor = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, statusDotTexture);
        GUI.color = previousColor;
    }

    private Texture2D CreateCircleTexture(int size)
    {
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.wrapMode = TextureWrapMode.Clamp;

        float radius = size * 0.5f;
        Vector2 center = new Vector2(radius - 0.5f, radius - 0.5f);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x, y), center);
                float alpha = Mathf.Clamp01(radius - distance);
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }

        texture.Apply();
        return texture;
    }

    public void SetStrength(float value)
    {
        strength = value;
        Debug.Log($"Strength: {strength}");
    }

    public void SetEndPercent(float value)
    {
        endPercent = value;
        Debug.Log($"EndPercent: {endPercent}");
    }

    public void SetCFG(float value)
    {
        cfg = value;
        Debug.Log($"CFG: {cfg}");
    }

    public void SetSteps(float value)
    {
        steps = Mathf.RoundToInt(value);
        Debug.Log($"Steps: {steps}");
    }

    // =========================
    // PROMPT VOICE
    // =========================

    public void SetPromptFromDictation(string transcribedText)
    {
        aiPrompt = transcribedText;
    }

    public void AppendToPromptFromDictation(string transcribedText)
    {
        if (!string.IsNullOrEmpty(aiPrompt) && !aiPrompt.EndsWith(" "))
            aiPrompt += " ";

        aiPrompt += transcribedText;
    }

    public void ClearPrompt()
    {
        aiPrompt = "";
    }

    // =========================
    // SEND TO SERVER
    // =========================

    public void InviaAFlask(string percorsoImmagineSalvata)
    {
        if (isProcessing) return;
        if (!File.Exists(percorsoImmagineSalvata))
        {
            Debug.LogError("File non trovato: " + percorsoImmagineSalvata);
            return;
        }

        StartCoroutine(UploadRoutine(percorsoImmagineSalvata));
    }

    private IEnumerator UploadRoutine(string path)
    {
        isProcessing = true;
        GenerationStarted?.Invoke();

        byte[] fileData = File.ReadAllBytes(path);
        string base64Image = Convert.ToBase64String(fileData);

        FlaskRequestData requestData = new FlaskRequestData
        {
            image = base64Image,
            prompt = aiPrompt,
            strength = strength,
            start_percent = startPercent,
            end_percent = endPercent,
            cfg = cfg,
            steps = steps,
            magic_enrich = magicEnrich
        };

        string jsonBody = JsonUtility.ToJson(requestData);
        string url = $"{serverUrl.TrimEnd('/')}/generate";

        UnityWebRequest request = new UnityWebRequest(url, "POST");
        request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody));
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.timeout = 120;

        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            FlaskResponse response = JsonUtility.FromJson<FlaskResponse>(request.downloadHandler.text);

            if (response != null && response.status == "success")
            {
                byte[] imageBytes = Convert.FromBase64String(RemoveDataUrlPrefix(response.image));

                string outputPath = Path.Combine(
                    Path.GetDirectoryName(path),
                    $"AI_RESULT_{DateTime.Now:yyyyMMdd_HHmmss}.png"
                );

                File.WriteAllBytes(outputPath, imageBytes);
                Debug.Log("Salvato: " + outputPath);

                Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (texture.LoadImage(imageBytes))
                {
                    GeneratedImageResult result = new GeneratedImageResult
                    {
                        texture = texture,
                        imageBytes = imageBytes,
                        imagePath = outputPath,
                        sourceSketchPath = path,
                        prompt = aiPrompt,
                        promptUsato = response.prompt_usato,
                        strength = strength,
                        startPercent = startPercent,
                        endPercent = endPercent,
                        cfg = cfg,
                        steps = steps,
                        magicEnrich = magicEnrich,
                        generatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                    };

                    GenerationCompleted?.Invoke(result);
                }
                else
                {
                    Destroy(texture);
                    string message = "Impossibile caricare la PNG restituita da Flask.";
                    Debug.LogError(message);
                    GenerationFailed?.Invoke(message);
                }
            }
            else
            {
                string message = response != null && !string.IsNullOrEmpty(response.message)
                    ? response.message
                    : "Flask non ha restituito una risposta di successo.";
                Debug.LogError(message);
                GenerationFailed?.Invoke(message);
            }
        }
        else
        {
            Debug.LogError(request.error);
            GenerationFailed?.Invoke(request.error);
        }

        isProcessing = false;
        request.Dispose();
    }

    public void SendSelectedToTrellis(GeneratedImageResult selectedResult)
    {
        if (selectedResult == null)
        {
            string message = "Nessuna immagine selezionata per Trellis.";
            Debug.LogWarning(message);
            TrellisFailed?.Invoke(message);
            return;
        }

        if (isTrellisProcessing)
        {
            return;
        }

        StartCoroutine(SendToTrellisRoutine(selectedResult));
    }

    private IEnumerator SendToTrellisRoutine(GeneratedImageResult selectedResult)
    {
        isTrellisProcessing = true;

        byte[] imageBytes = selectedResult.imageBytes;
        if ((imageBytes == null || imageBytes.Length == 0) && File.Exists(selectedResult.imagePath))
        {
            imageBytes = File.ReadAllBytes(selectedResult.imagePath);
        }

        if (imageBytes == null || imageBytes.Length == 0)
        {
            string message = "File immagine selezionato non disponibile.";
            Debug.LogError(message);
            TrellisFailed?.Invoke(message);
            isTrellisProcessing = false;
            yield break;
        }

        TrellisRequestData requestData = new TrellisRequestData
        {
            image = Convert.ToBase64String(imageBytes),
            source_image_path = selectedResult.imagePath,
            prompt = string.IsNullOrEmpty(selectedResult.promptUsato) ? selectedResult.prompt : selectedResult.promptUsato,
            strength = selectedResult.strength,
            start_percent = selectedResult.startPercent,
            end_percent = selectedResult.endPercent,
            cfg = selectedResult.cfg,
            steps = selectedResult.steps,
            magic_enrich = selectedResult.magicEnrich
        };

        string endpoint = string.IsNullOrWhiteSpace(trellisEndpoint) ? "/generate_3d" : trellisEndpoint.Trim();
        if (!endpoint.StartsWith("/"))
        {
            endpoint = "/" + endpoint;
        }

        string jsonBody = JsonUtility.ToJson(requestData);
        string url = $"{serverUrl.TrimEnd('/')}{endpoint}";

        UnityWebRequest request = new UnityWebRequest(url, "POST");
        request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody));
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.timeout = Mathf.Max(30, trellisTimeout);

        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            Debug.Log("Trellis response: " + request.downloadHandler.text);
            TrellisCompleted?.Invoke(request.downloadHandler.text);
        }
        else
        {
            Debug.LogError(request.error);
            TrellisFailed?.Invoke(request.error);
        }

        isTrellisProcessing = false;
        request.Dispose();
    }

    private string RemoveDataUrlPrefix(string base64)
    {
        if (string.IsNullOrEmpty(base64))
        {
            return "";
        }

        int commaIndex = base64.IndexOf(',');
        return commaIndex >= 0 ? base64.Substring(commaIndex + 1) : base64;
    }
}

[Serializable]
public class FlaskRequestData
{
    public string image;
    public string prompt;
    public float strength;
    public float start_percent;
    public float end_percent;
    public float cfg;
    public int steps;
    public bool magic_enrich;
}

[Serializable]
public class FlaskResponse
{
    public string status;
    public string image;
    public string message;
    public string prompt_usato;
}

[Serializable]
public class GeneratedImageResult
{
    public Texture2D texture;
    public byte[] imageBytes;
    public string imagePath;
    public string sourceSketchPath;
    public string prompt;
    public string promptUsato;
    public float strength;
    public float startPercent;
    public float endPercent;
    public float cfg;
    public int steps;
    public bool magicEnrich;
    public string generatedAt;
}

[Serializable]
public class TrellisRequestData
{
    public string image;
    public string source_image_path;
    public string prompt;
    public float strength;
    public float start_percent;
    public float end_percent;
    public float cfg;
    public int steps;
    public bool magic_enrich;
}
