using UnityEngine;

// An always-on quest summary in the top-right corner: each active quest's
// title plus a running completed count. Drawn via OnGUI, same reasoning as
// GenerationDebugLog/Hotbar — no scene wiring needed, and this project's
// earlier "Canvas doesn't render" scare turned out to be an Editor
// Game-view zoom/crop red herring rather than a real limitation, but OnGUI
// is still the least code for a small fixed block of text.
public class QuestLogUi : MonoBehaviour
{
    private static QuestLogUi instance;

    public float width = 260f;
    public float margin = 16f;

    private GUIStyle headerStyle;
    private GUIStyle lineStyle;

    public static void EnsureExists()
    {
        if (instance != null) return;
        var go = new GameObject("QuestLogUi");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<QuestLogUi>();
    }

    private void OnGUI()
    {
        if (QuestManager.Instance == null) return;

        headerStyle ??= new GUIStyle(GUI.skin.label)
        {
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.UpperRight,
        };
        headerStyle.normal.textColor = Color.white;

        lineStyle ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperRight };
        lineStyle.normal.textColor = new Color(0.85f, 0.85f, 0.85f);

        float x = Screen.width - width - margin;
        float y = margin;

        GUI.Label(new Rect(x, y, width, 22f), $"Quests — {QuestManager.Instance.CompletedQuestCount} completed", headerStyle);
        y += 20f;

        foreach (Quest quest in QuestManager.Instance.ActiveQuests)
        {
            if (QuestManager.Instance.IsAwaitingTurnIn(quest.title))
            {
                GUI.Label(new Rect(x, y, width, 20f), $"• {quest.title}", lineStyle);
                y += 18f;
                string giver = string.IsNullOrEmpty(quest.giverName) ? "the giver" : quest.giverName;
                GUI.Label(new Rect(x, y, width, 20f), $"Return to {giver} (white marker)", lineStyle);
            }
            else
            {
                GUI.Label(new Rect(x, y, width, 20f), $"• {quest.title} (in progress)", lineStyle);
            }
            y += 18f;
        }
    }
}
