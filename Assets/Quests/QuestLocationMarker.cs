using UnityEngine;

// Put on a location-marker object (with a trigger Collider) that the player
// walks into to satisfy an Explore quest. targetId must match Quest.targetId.
// Sibling to QuestItem, but doesn't touch the Hotbar — visiting a place
// isn't collecting anything.
public class QuestLocationMarker : MonoBehaviour
{
    public string targetId;

    // Violet — the old cyan (0.3, 0.85, 1) was nearly identical to the plain
    // NPC blue (CompassMarker.DefaultNpcColor), so explore targets were
    // indistinguishable from the crowd. Shared with QuestExploreAiGenerator's
    // in-world beacon so both match.
    public static readonly Color MarkerColor = new(0.75f, 0.35f, 1f);

    private void Awake()
    {
        var marker = gameObject.AddComponent<CompassMarker>();
        marker.kind = CompassMarker.MarkerKind.QuestLocation;
        marker.color = MarkerColor; // distinct from plain NPCs (blue), quest items (green) and Meet targets (gold)
        marker.label = "Explore";
        // Only shown once a quest that actually wants this location has
        // been accepted, same reasoning as QuestItem's marker.
        marker.isVisible = () => QuestManager.Instance != null && QuestManager.Instance.IsTargetActive(targetId);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        QuestManager.Instance.ReportProgress(targetId);
        Destroy(gameObject);
    }
}
