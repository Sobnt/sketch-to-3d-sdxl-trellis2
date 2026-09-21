using System;
using System.IO;
using UnityEngine;

public static class TrellisGenerationPayload
{
    public static byte[] ImageBytes { get; private set; }
    public static string ImagePath { get; private set; }
    public static string SourcePrompt { get; private set; }
    public static string GeneratedAt { get; private set; }

    public static bool HasImage
    {
        get
        {
            return (ImageBytes != null && ImageBytes.Length > 0) ||
                   (!string.IsNullOrWhiteSpace(ImagePath) && File.Exists(ImagePath));
        }
    }

    public static string Capture(GeneratedImageResult result)
    {
        if (result == null)
        {
            throw new ArgumentNullException(nameof(result));
        }

        byte[] bytes = result.imageBytes;
        if ((bytes == null || bytes.Length == 0) && !string.IsNullOrWhiteSpace(result.imagePath) && File.Exists(result.imagePath))
        {
            bytes = File.ReadAllBytes(result.imagePath);
        }

        if (bytes == null || bytes.Length == 0)
        {
            throw new InvalidOperationException("L'immagine selezionata non contiene dati validi.");
        }

        string folder = Path.Combine(Application.persistentDataPath, "TrellisInput");
        Directory.CreateDirectory(folder);

        string fileName = $"trellis_input_{DateTime.Now:yyyyMMdd_HHmmss}.png";
        string path = Path.Combine(folder, fileName);
        File.WriteAllBytes(path, bytes);

        ImageBytes = bytes;
        ImagePath = path;
        SourcePrompt = string.IsNullOrWhiteSpace(result.promptUsato) ? result.prompt : result.promptUsato;
        GeneratedAt = result.generatedAt;

        Debug.Log("[TrellisPayload] Immagine selezionata pronta per Trellis: " + path);
        return path;
    }

    public static byte[] GetImageBytes()
    {
        if (ImageBytes != null && ImageBytes.Length > 0)
        {
            return ImageBytes;
        }

        if (!string.IsNullOrWhiteSpace(ImagePath) && File.Exists(ImagePath))
        {
            ImageBytes = File.ReadAllBytes(ImagePath);
            return ImageBytes;
        }

        return null;
    }

    public static Texture2D CreatePreviewTexture()
    {
        byte[] bytes = GetImageBytes();
        if (bytes == null || bytes.Length == 0)
        {
            return null;
        }

        Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!texture.LoadImage(bytes))
        {
            UnityEngine.Object.Destroy(texture);
            return null;
        }

        return texture;
    }

    public static void Clear()
    {
        ImageBytes = null;
        ImagePath = "";
        SourcePrompt = "";
        GeneratedAt = "";
    }
}
