using System.Collections.Generic;
using UnityEngine;

// A tiny in-game debug HUD: call GenerationDebugLog.Show("message") from
// anywhere to pop a line of text into the bottom-left corner of the screen
// that holds briefly then fades out — used to see which world-generation
// stage is currently running without needing the Console open.
//
// Drawn via OnGUI (IMGUI) rather than a Canvas/UI.Text — this project's
// Canvas rendering wasn't showing ANYTHING (confirmed the GameObjects were
// being created correctly, so it wasn't a font or execution problem, just
// Canvas content never appearing on screen for a reason not worth chasing
// further). OnGUI is a completely separate, much more primitive rendering
// path that needs no Canvas, RectTransform, or font resource lookup at all,
// so it sidesteps whatever that issue was entirely.
public class GenerationDebugLog : MonoBehaviour
{
    private static GenerationDebugLog instance;

    [Tooltip("How long a line stays fully visible before it starts fading.")]
    public float holdSeconds = 2f;
    [Tooltip("How long the fade-out itself takes.")]
    public float fadeSeconds = 1.5f;
    [Tooltip("Max lines shown at once — oldest is removed first once exceeded.")]
    public int maxLines = 6;

    private class Line
    {
        public string text;
        public float remaining;
    }

    private readonly List<Line> lines = new();
    private GUIStyle style;
    private GUIStyle shadowStyle;

    public static void Show(string message)
    {
        Debug.Log($"[Debug HUD] {message}");

        if (instance == null)
        {
            var go = new GameObject("GenerationDebugLog");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<GenerationDebugLog>();
        }
        instance.AddLine(message);
    }

    private void AddLine(string message)
    {
        lines.Add(new Line { text = message, remaining = holdSeconds + fadeSeconds });
        while (lines.Count > maxLines) lines.RemoveAt(0);
    }

    private void Update()
    {
        for (int i = lines.Count - 1; i >= 0; i--)
        {
            lines[i].remaining -= Time.deltaTime;
            if (lines[i].remaining <= 0f) lines.RemoveAt(i);
        }
    }

    private void OnGUI()
    {
        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
            shadowStyle = new GUIStyle(style);
        }

        float y = Screen.height - 30f;
        for (int i = lines.Count - 1; i >= 0; i--)
        {
            Line line = lines[i];
            float alpha = Mathf.Clamp01(line.remaining / fadeSeconds);

            var rect = new Rect(16f, y, 500f, 24f);
            shadowStyle.normal.textColor = new Color(0f, 0f, 0f, alpha * 0.8f);
            GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), line.text, shadowStyle);

            style.normal.textColor = new Color(1f, 1f, 1f, alpha);
            GUI.Label(rect, line.text, style);

            y -= 22f;
        }
    }
}
