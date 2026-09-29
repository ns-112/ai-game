using System;
using System.Collections;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

// Minimal client for the Anthropic Messages API (claude-opus-5), used as a
// drop-in replacement for GeminiTextClient — same public surface
// (LoadApiKey/Generate), same on-disk response cache, same
// prompt-asks-for-JSON-only convention, so callers didn't need to change at
// all beyond swapping which client they call.
//
// Uses its own key file, claude.txt at the project root (sibling to Assets/,
// never inside it — same reasoning as huggingface.txt/gemini.txt: Unity can never
// bundle a file that isn't under Assets/ into a build). Put an Anthropic API
// key from console.anthropic.com in there (this is a billed API key, a
// different product from a claude.ai chat subscription — the subscription
// has no API access).
public static class ClaudeTextClient
{
    private const string Endpoint = "https://api.anthropic.com/v1/messages";
    private const string Model = "claude-opus-5";
    private const string AnthropicVersion = "2023-06-01";
    private const int MaxAttempts = 4;
    private const float RetryBackoffSeconds = 3f;

    private static string cachedApiKey;

    public static string LoadApiKey()
    {
        if (!string.IsNullOrEmpty(cachedApiKey)) return cachedApiKey;

        string path = Path.Combine(Application.dataPath, "..", "claude.txt");
        if (!File.Exists(path))
        {
            Debug.LogWarning($"ClaudeTextClient: no API key file found at {path}");
            return null;
        }

        cachedApiKey = File.ReadAllText(path).Trim();
        return cachedApiKey;
    }

    // Shares GeminiTextClient's cache directory — it's keyed purely by
    // prompt text, and both clients are asked to return the same kind of
    // thing (raw JSON text for the same prompt), so a response cached by
    // one is perfectly valid for the other.
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

        // Low effort: these are simple, structured JSON-generation calls, not
        // hard reasoning tasks — no need to spend on higher thinking depth.
        // Thinking itself is left on (the model default) rather than
        // disabled, since disabling it on this model can leak a tool-call-
        // shaped response into plain text; we aren't using tools here, but
        // there's no benefit to disabling it either.
        string body = JsonUtility.ToJson(new ClaudeRequest
        {
            model = Model,
            max_tokens = 16000, // headroom — currently only BiomeAiGenerator calls this client (trees/rocks/buildings/structures generate procedurally, no AI call)
            messages = new[] { new MessageParam { role = "user", content = prompt } },
            output_config = new OutputConfig { effort = "low" },
        });

        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            Debug.Log($"[AI Text] Requesting generation (attempt {attempt}/{MaxAttempts})...");

            using var request = new UnityWebRequest(Endpoint, "POST");
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("x-api-key", apiKey);
            request.SetRequestHeader("anthropic-version", AnthropicVersion);
            request.timeout = 120; // non-streaming call for a potentially large response — same reasoning as HuggingFaceImageClient's timeout

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
                catch (IOException e) { Debug.LogWarning($"ClaudeTextClient: failed to write cache — {e.Message}"); }

                onComplete(text);
                yield break;
            }

            // Unlike Gemini's free-tier daily quota, Anthropic's rate limits
            // (429) and transient server errors (500/502/503/529 "overloaded")
            // are per-minute/per-token-throughput and do recover within a
            // short backoff — no need to distinguish a "never retry this"
            // case the way GeminiTextClient does.
            // responseCode 0 = no response received at all (e.g. our own timeout).
            bool retryable = request.responseCode == 429 || request.responseCode == 500
                || request.responseCode == 502 || request.responseCode == 503
                || request.responseCode == 529 || request.responseCode == 0;
            Debug.LogWarning($"[AI Text] Request failed after {elapsed:0.0}s (HTTP {request.responseCode}) — {request.error}\n{request.downloadHandler.text}");

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

    // The response's content array can contain thinking blocks before the
    // text block (thinking is on by default on this model) — skip anything
    // that isn't type "text".
    private static string ExtractText(string json)
    {
        ClaudeResponse response = JsonUtility.FromJson<ClaudeResponse>(json);
        ContentBlock[] blocks = response?.content;
        if (blocks == null) return null;

        foreach (ContentBlock block in blocks)
            if (block.type == "text" && !string.IsNullOrEmpty(block.text)) return block.text;
        return null;
    }

    [Serializable] private class ClaudeRequest { public string model; public int max_tokens; public MessageParam[] messages; public OutputConfig output_config; }
    [Serializable] private class MessageParam { public string role; public string content; }
    [Serializable] private class OutputConfig { public string effort; }

    [Serializable] private class ClaudeResponse { public ContentBlock[] content; }
    [Serializable] private class ContentBlock { public string type; public string text; }
}
