using System;
using System.Collections.Generic;
using UnityEngine;

public class QuestManager : MonoBehaviour
{
    public static QuestManager Instance { get; private set; }

    private readonly Dictionary<string, Quest> activeQuests = new();
    private readonly Dictionary<string, int> progress = new();
    private readonly HashSet<string> completedQuests = new();
    private readonly HashSet<string> awaitingTurnIn = new();

    public event Action<Quest> OnQuestAccepted;
    public event Action<Quest> OnQuestCompleted;
    public event Action<Quest, int> OnQuestProgress;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    public bool IsQuestActive(string title) => activeQuests.ContainsKey(title);
    public bool IsQuestCompleted(string title) => completedQuests.Contains(title);
    // Objective done (e.g. Fetch item picked up), but still needs handing in
    // to the giver — see Quest.requiresTurnIn.
    public bool IsAwaitingTurnIn(string title) => awaitingTurnIn.Contains(title);

    // Read-only views for QuestLogUi's always-on "in progress / completed" summary.
    public IEnumerable<Quest> ActiveQuests => activeQuests.Values;
    public int CompletedQuestCount => completedQuests.Count;

    // Whether any currently-active quest cares about this targetId — used by
    // CompassMarker on QuestItem so an item only shows on the compass once
    // its quest has actually been accepted, without QuestItem needing to
    // know which quest(s) reference it by title.
    public bool IsTargetActive(string targetId)
    {
        foreach (Quest quest in activeQuests.Values)
            if (quest.targetId == targetId) return true;
        return false;
    }

    // The active quest (if any) whose targetId matches — used by
    // NpcInteractable so that talking to a Meet quest's target NPC can show
    // THAT quest's own dialogue (giver or completion) instead of silently
    // reporting progress and then immediately overwriting the line with
    // this NPC's own, unrelated quest offer.
    public Quest FindActiveQuestByTargetId(string targetId)
    {
        foreach (Quest quest in activeQuests.Values)
            if (quest.targetId == targetId) return quest;
        return null;
    }

    public void AcceptQuest(Quest quest)
    {
        if (activeQuests.ContainsKey(quest.title) || completedQuests.Contains(quest.title))
            return;

        activeQuests[quest.title] = quest;
        progress[quest.title] = 0;
        OnQuestAccepted?.Invoke(quest);
    }

    // Call this when the player does something quest-relevant (pick up an item,
    // reach a location, talk to someone) — targetId matches Quest.targetId.
    public void ReportProgress(string targetId, int amount = 1)
    {
        foreach (var quest in new List<Quest>(activeQuests.Values))
        {
            if (quest.targetId != targetId) continue;
            if (awaitingTurnIn.Contains(quest.title)) continue;

            progress[quest.title] += amount;
            OnQuestProgress?.Invoke(quest, progress[quest.title]);

            if (progress[quest.title] >= quest.count)
            {
                if (quest.requiresTurnIn) awaitingTurnIn.Add(quest.title);
                else CompleteQuest(quest);
            }
        }
    }

    // Called by the giver when the player comes back with a finished
    // requiresTurnIn quest. Returns false if it wasn't actually ready.
    public bool TurnIn(string title)
    {
        if (!awaitingTurnIn.Remove(title)) return false;
        if (activeQuests.TryGetValue(title, out Quest quest)) CompleteQuest(quest);
        return true;
    }

    private void CompleteQuest(Quest quest)
    {
        activeQuests.Remove(quest.title);
        completedQuests.Add(quest.title);
        OnQuestCompleted?.Invoke(quest);
    }
}
