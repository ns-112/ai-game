using UnityEngine;

// Put this on a pickup object (with a trigger Collider) that the player
// walks into to satisfy a Fetch quest. targetId must match Quest.targetId.
public class QuestItem : MonoBehaviour
{
    public string targetId;

    [Tooltip("Set by whoever spawns this (e.g. QuestItemAiGenerator) — shown in the Hotbar once picked up.")]
    public string displayName = "Item";
    [Tooltip("Set by whoever spawns this once its AI texture (if any) finishes generating.")]
    public Texture2D icon;

    private void Awake()
    {
        var marker = gameObject.AddComponent<CompassMarker>();
        marker.kind = CompassMarker.MarkerKind.QuestItem;
        marker.color = Color.green;
        marker.label = "Item";
        // Only shown once a quest that actually wants this item has been
        // accepted — otherwise every QuestItem in the world would show up
        // on the compass regardless of whether the player has any reason
        // to look for it yet.
        marker.isVisible = () => QuestManager.Instance != null && QuestManager.Instance.IsTargetActive(targetId);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        Quest quest = QuestManager.Instance.FindActiveQuestByTargetId(targetId);
        QuestManager.Instance.ReportProgress(targetId);
        // A quest that has to be handed back keeps the item in the hotbar
        // until the giver takes it (NpcInteractable removes it on turn-in).
        // Otherwise the item has already served its purpose — shown for a
        // few seconds as pickup confirmation, then auto-cleared.
        bool carriedUntilTurnIn = quest != null && quest.requiresTurnIn;
        Hotbar.AddItem(displayName, icon, expiresIn: carriedUntilTurnIn ? 0f : 6f);
        Destroy(gameObject);
    }
}
