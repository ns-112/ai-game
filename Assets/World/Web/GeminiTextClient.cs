using System;
using System.Collections;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

// Minimal client for Gemini's text generation endpoint (gemini-2.5-flash).
// Uses a SEPARATE key file from HuggingFaceImageClient: gemini.txt at the
// project root (sibling to Assets/, never inside it — same reasoning as
// huggingface.txt: Unity can never bundle a file that isn't under Assets/ into a
// build). Gemini's free tier covers text generation generously — it was
// specifically the image model that required billing, which is why images
// went through Hugging Face instead (see HuggingFaceImageClient).
public static class GeminiTextClient
{
    private const string Endpoint = "https://generativelanguage.googleapis.com/v1beta/models/gemini-3.6-flash:generateContent";
    private const int MaxAttempts = 4;
    private const float RetryBackoffSeconds = 3f;

    private static string cachedApiKey;

    public static string LoadApiKey()
    {
        if (!string.IsNullOrEmpty(cachedApiKey)) return cachedApiKey;

        string path = Path.Combine(Application.dataPath, "..", "gemini.txt");
        if (!File.Exists(path))
        {
            Debug.LogWarning($"GeminiTextClient: no API key file found at {path}");
            return null;
        }

        cachedApiKey = File.ReadAllText(path).Trim();
        return cachedApiKey;
    }

    // Asks Gemini to respond with JSON only (responseMimeType), so callers
    // can feed the result straight to JsonUtility without stripping prose.
    public static IEnumerator Generate(string prompt, Action<string> onComplete)
    {
        string cachePath = GetCachePath(prompt);
        string hitPath = AiCache.FindExisting(cachePath);
        if (hitPath != null)
        {
            // Wait a frame even on a cache hit: callers (e.g. WorldLoadingGate)
            // may subscribe to completion events in their own Start(), and
            // Unity doesn't guarantee this component's Start() runs after
            // theirs — resolving instantly in the same frame can fire the
            // event before anyone's listening yet.
            yield return null;
            Debug.Log($"[AI Text] Cache hit -> {hitPath}");
            onComplete(File.ReadAllText(hitPath));
            yield break;
        }

        string apiKey = LoadApiKey();
        if (string.IsNullOrEmpty(apiKey))
        {
            onComplete(null);
            yield break;
        }

        string body = JsonUtility.ToJson(new GenerateRequest
        {
            contents = new[] { new RequestContent { parts = new[] { new RequestPart { text = prompt } } } },
            generationConfig = new GenerationConfig { responseMimeType = "application/json" }
        });

        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            Debug.Log($"[AI Text] Requesting generation (attempt {attempt}/{MaxAttempts})...");

            using var request = new UnityWebRequest(Endpoint, "POST");
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("x-goog-api-key", apiKey);
            request.timeout = 60;

            float startTime = Time.realtimeSinceStartup;
            yield return request.SendWebRequest();
            float elapsed = Time.realtimeSinceStartup - startTime;

            if (request.result == UnityWebRequest.Result.Success)
            {
                string text = ExtractText(request.downloadHandler.text);
                if (string.IsNullOrEmpty(text))
                {
                    Debug.LogWarning($"[AI Text] No text in response.\n{request.downloadHandler.text}");
                    onComplete(null);
                    yield break;
                }

                Debug.Log($"[AI Text] Received response in {elapsed:0.0}s ({text.Length} chars)");

                try { File.WriteAllText(cachePath, text); }
                catch (IOException e) { Debug.LogWarning($"GeminiTextClient: failed to write cache — {e.Message}"); }

                onComplete(text);
                yield break;
            }

            // A 429 can mean two very different things: a short-lived per-minute
            // rate limit (worth retrying) or the free tier's PER-DAY quota
            // (retrying is pointless — it cannot recover within our backoff
            // window no matter how many attempts we burn, and each attempt
            // still counts against tomorrow's quota too). Google's free-tier
            // error body names the quota id, so "PerDay" in the response is a
            // reliable way to tell them apart without parsing the full JSON.
            bool dailyQuotaExceeded = request.responseCode == 429 && request.downloadHandler.text.Contains("PerDay");

            // responseCode 0 = no response received at all (e.g. our own timeout) —
            // worth retrying same as a transient 503/429.
            bool retryable = !dailyQuotaExceeded && (request.responseCode == 503 || request.responseCode == 429 || request.responseCode == 0);
            Debug.LogWarning($"[AI Text] Request failed after {elapsed:0.0}s (HTTP {request.responseCode}) — {request.error}\n{request.downloadHandler.text}");

            if (dailyQuotaExceeded)
                Debug.LogWarning("[AI Text] This is the free tier's PER-DAY quota, not a transient rate limit — not retrying (it won't recover today). Falling back to defaults until quota resets.");

            if (!retryable || attempt == MaxAttempts)
            {
                onComplete(null);
                yield break;
            }

            float delay = RetryBackoffSeconds * attempt;
            Debug.Log($"[AI Text] Transient error (HTTP {request.responseCode}) — retrying in {delay:0}s...");
            yield return new WaitForSeconds(delay);
        }
    }

    private static string GetCachePath(string prompt)
    {
        string dir = Path.Combine(Application.persistentDataPath, "AiTextCache");
        Directory.CreateDirectory(dir);

        using var md5 = MD5.Create();
        byte[] hashBytes = md5.ComputeHash(Encoding.UTF8.GetBytes(prompt));
        var sb = new StringBuilder();
        foreach (byte b in hashBytes) sb.Append(b.ToString("x2"));

        return Path.Combine(dir, sb + ".json");
    }

    private static string ExtractText(string json)
    {
        GeminiResponse response = JsonUtility.FromJson<GeminiResponse>(json);
        Candidate[] candidates = response?.candidates;
        if (candidates == null) return null;

        foreach (Candidate candidate in candidates)
        {
            Part[] parts = candidate?.content?.parts;
            if (parts == null) continue;
            foreach (Part part in parts)
                if (!string.IsNullOrEmpty(part.text)) return part.text;
        }
        return null;
    }

    [Serializable] private class GenerateRequest { public RequestContent[] contents; public GenerationConfig generationConfig; }
    [Serializable] private class RequestContent { public RequestPart[] parts; }
    [Serializable] private class RequestPart { public string text; }
    [Serializable] private class GenerationConfig { public string responseMimeType; }

    [Serializable] private class GeminiResponse { public Candidate[] candidates; }
    [Serializable] private class Candidate { public Content content; }
    [Serializable] private class Content { public Part[] parts; }
    [Serializable] private class Part { public string text; }
}
