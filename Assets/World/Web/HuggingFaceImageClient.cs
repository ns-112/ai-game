using System;
using System.Collections;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

// Minimal client for Hugging Face's Inference Providers text-to-image
// endpoint (hf-inference provider — free, no billing account required).
// The API token is read from a file OUTSIDE the Unity project's Assets
// folder (project root, sibling to Assets/) so it can never end up
// packaged into a build — never move huggingface.txt back under Assets/.
// huggingface.txt must contain a Hugging Face token (from
// huggingface.co/settings/tokens, "Make calls to Inference Providers"
// permission), not a Gemini key.
public static class HuggingFaceImageClient
{
    private const string DefaultModel = "stabilityai/stable-diffusion-3-medium-diffusers";
    private const int MaxAttempts = 4;
    private const float RetryBackoffSeconds = 5f;

    private static string cachedApiKey;

    public static string LoadApiKey()
    {
        if (!string.IsNullOrEmpty(cachedApiKey)) return cachedApiKey;

        // Application.dataPath is "<project>/Assets" in the Editor, so ".." is the project root.
        string path = Path.Combine(Application.dataPath, "..", "huggingface.txt");
        if (!File.Exists(path))
        {
            Debug.LogWarning($"HuggingFaceImageClient: no API key file found at {path}");
            return null;
        }

        cachedApiKey = File.ReadAllText(path).Trim();
        return cachedApiKey;
    }

    public static IEnumerator GenerateTexture(string prompt, Action<Texture2D> onComplete, string model = DefaultModel)
    {
        string cachePath = GetCachePath(prompt, model);
        string hitPath = AiCache.FindExisting(cachePath);
        if (hitPath != null)
        {
            yield return null; // see GeminiTextClient's cache-hit comment for why this matters
            Debug.Log($"[AI Texture] Cache hit for \"{prompt}\" -> {hitPath}");
            var cached = new Texture2D(2, 2);
            cached.LoadImage(File.ReadAllBytes(hitPath));
            onComplete(cached);
            yield break;
        }

        string apiKey = LoadApiKey();
        if (string.IsNullOrEmpty(apiKey))
        {
            onComplete(null);
            yield break;
        }

        string url = $"https://router.huggingface.co/hf-inference/models/{model}";
        string body = JsonUtility.ToJson(new RequestBody { inputs = prompt });

        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            Debug.Log($"[AI Texture] Requesting \"{prompt}\" from {model} (attempt {attempt}/{MaxAttempts})...");

            using var request = new UnityWebRequest(url, "POST");
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", $"Bearer {apiKey}");
            request.timeout = 120; // free-tier "cold" models can take a while to spin up

            float startTime = Time.realtimeSinceStartup;
            yield return request.SendWebRequest();
            float elapsed = Time.realtimeSinceStartup - startTime;

            if (request.result == UnityWebRequest.Result.Success)
            {
                byte[] imageBytes = request.downloadHandler.data; // the response body IS the raw image
                Debug.Log($"[AI Texture] Received {imageBytes.Length / 1024}KB for \"{prompt}\" in {elapsed:0.0}s");

                try
                {
                    File.WriteAllBytes(cachePath, imageBytes);
                    Debug.Log($"[AI Texture] Cached to {cachePath}");
                }
                catch (IOException e) { Debug.LogWarning($"HuggingFaceImageClient: failed to write cache — {e.Message}"); }

                var texture = new Texture2D(2, 2); // LoadImage resizes it to match the decoded image
                texture.LoadImage(imageBytes);
                onComplete(texture);
                yield break;
            }

            // HF's public inference commonly returns 503 while a "cold" model spins up,
            // and a cold start can also just outlast our timeout entirely (responseCode
            // 0 — no response received at all) — both are worth another attempt, since a
            // model that was cold on attempt 1 is often warm by attempt 2.
            bool retryable = request.responseCode == 503 || request.responseCode == 429 || request.responseCode == 0;
            Debug.LogWarning($"[AI Texture] Request failed after {elapsed:0.0}s (HTTP {request.responseCode}) — {request.error}\n{request.downloadHandler.text}");

            if (!retryable || attempt == MaxAttempts)
            {
                onComplete(null);
                yield break;
            }

            float delay = RetryBackoffSeconds * attempt;
            Debug.Log($"[AI Texture] Transient error (HTTP {request.responseCode}) — retrying in {delay:0}s...");
            yield return new WaitForSeconds(delay);
        }
    }

    // Cache key includes the model, so switching models doesn't collide with
    // old cached results. Lives under persistentDataPath, not Assets/, since
    // it's runtime-generated data that needs to work the same in the Editor
    // and in a build.
    private static string GetCachePath(string prompt, string model)
    {
        string dir = Path.Combine(Application.persistentDataPath, "AiTextureCache");
        Directory.CreateDirectory(dir);

        using var md5 = MD5.Create();
        byte[] hashBytes = md5.ComputeHash(Encoding.UTF8.GetBytes(model + "|" + prompt));
        var sb = new StringBuilder();
        foreach (byte b in hashBytes) sb.Append(b.ToString("x2"));

        return Path.Combine(dir, sb + ".png");
    }

    [Serializable] private class RequestBody { public string inputs; }
}
