

[System.Serializable] public enum QuestType
{
    Fetch,
    Explore,
    Meet
}

[System.Serializable] public class Quest {
    public string title, giverDialogue, completionDialogue;
    public QuestType type;
    public string targetId;
    public int count;
    public string rewardId;

    // When true, reaching `count` doesn't complete the quest — it waits in
    // QuestManager's "ready to turn in" state until the player goes back and
    // talks to the giver (giverName), who calls QuestManager.TurnIn().
    public bool requiresTurnIn;
    public string giverName;
}