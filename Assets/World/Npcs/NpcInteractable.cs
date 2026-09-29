using UnityEngine;
using UnityEngine.InputSystem;

// Generic "walk up, press E, get a quest" interaction for a plain NPC —
// same trigger + E-key pattern as Assets/Quests/QuestGiver.cs, but self-
// contained: it builds its own floating world-space prompt/dialogue text at
// runtime via TextMesh instead of a scene-authored Canvas/Text, since these
// NPCs are spawned procedurally across the world (by NpcAiGenerator) rather
// than hand-placed, so there's no scene prefab to wire UI references into.
//
// The quest itself comes from NpcQuestGenerator — procedural, not an LLM
// call, so talking to an NPC is instant rather than waiting on a network
// round-trip every time. One quest is rolled the first time this NPC is
// interacted with and then reused for the rest of its lifetime (talking
// again just re-shows its current giver/in-progress/completion line,
// exactly like QuestGiver.Interact() does).
[RequireComponent(typeof(SphereCollider))]
public class NpcInteractable : MonoBehaviour
{
    public float dialogueDisplaySeconds = 5f;

    private NpcIdentity identity;
    private NpcWander wander;
    private Quest quest;
    private bool playerInRange;
    private TextMesh label;
    private float dialogueHideTimer;

    // Every NPC whose trigger the player is currently inside. With two NPCs
    // in range, a single E press used to run Interact() on BOTH in the same
    // frame — so if one rolled a Meet quest targeting the other, the other's
    // Interact() completed it instantly and reset its gold marker before the
    // player ever saw it. Only the closest in-range NPC now handles a press.
    private static readonly System.Collections.Generic.HashSet<NpcInteractable> InRange = new();

    public static bool IsPlayerInRangeOf(NpcIdentity npc)
    {
        foreach (NpcInteractable interactable in InRange)
            if (interactable.identity == npc) return true;
        return false;
    }

    private void Awake()
    {
        identity = GetComponent<NpcIdentity>();
        wander = GetComponent<NpcWander>();
        GetComponent<SphereCollider>().isTrigger = true;
        label = BuildLabel();
    }

    private TextMesh BuildLabel()
    {
        var labelObject = new GameObject("InteractLabel");
        labelObject.transform.SetParent(transform);
        labelObject.transform.localPosition = new Vector3(0f, 2.3f, 0f);

        var textMesh = labelObject.AddComponent<TextMesh>();
        textMesh.characterSize = 0.05f;
        textMesh.fontSize = 32;
        textMesh.anchor = TextAnchor.LowerCenter;
        textMesh.alignment = TextAlignment.Center;
        textMesh.color = Color.white;
        textMesh.text = "";

        labelObject.AddComponent<NpcBillboard>().flip180 = true;
        return textMesh;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        playerInRange = true;
        InRange.Add(this);
        if (dialogueHideTimer <= 0f) label.text = "Press E to talk";
        // Otherwise the NPC's own wandering carries it out of trigger range
        // again within a second or two of the player arriving — the prompt
        // flashes on then immediately disappears since the trigger exits on
        // its own before the player can react, let alone press E.
        if (wander != null) wander.SetExternallyPaused(true);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        playerInRange = false;
        InRange.Remove(this);
        if (dialogueHideTimer <= 0f) label.text = "";
        if (wander != null) wander.SetExternallyPaused(false);
    }

    private void Update()
    {
        if (dialogueHideTimer > 0f)
        {
            dialogueHideTimer -= Time.deltaTime;
            if (dialogueHideTimer <= 0f) label.text = playerInRange ? "Press E to talk" : "";
        }

        UpdateReturnMarker();

        if (!playerInRange) return;
        if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame && IsClosestInRange())
            Interact();
    }

    // While the player is carrying this NPC's Fetch item back, recolor this
    // NPC's compass dot so they know who to return it to.
    private void UpdateReturnMarker()
    {
        if (quest == null || !quest.requiresTurnIn || QuestManager.Instance == null) return;
        if (!QuestManager.Instance.IsAwaitingTurnIn(quest.title)) return;

        CompassMarker marker = GetComponent<CompassMarker>();
        if (marker != null) marker.color = NpcQuestGenerator.ReturnToGiverColor;
    }

    private void OnDisable()
    {
        InRange.Remove(this);
        playerInRange = false;
    }

    private bool IsClosestInRange()
    {
        Camera cam = Camera.main;
        if (cam == null) return true;
        Vector3 playerPos = cam.transform.position;

        float myDistance = (transform.position - playerPos).sqrMagnitude;
        foreach (NpcInteractable other in InRange)
        {
            if (other == this || other == null) continue;
            float otherDistance = (other.transform.position - playerPos).sqrMagnitude;
            // Tie-break on instance id so exactly one NPC wins even at equal distance.
            if (otherDistance < myDistance || (otherDistance == myDistance && other.GetInstanceID() < GetInstanceID()))
                return false;
        }
        return true;
    }

    private void Interact()
    {
        if (identity == null || QuestManager.Instance == null) return;

        // If some OTHER active quest specifically wants the player to meet
        // THIS npc (see NpcQuestGenerator.BuildMeet), resolve and show THAT
        // quest's own line first, then stop — otherwise this NPC's own
        // unrelated quest got rolled and offered in the very same
        // interaction, silently completing the Meet quest in the background
        // while immediately burying it under a brand new quest offer with
        // no feedback that anything had just been accomplished.
        string meetTargetId = $"quest_meet_{identity.Personality.npcId}";
        Quest meetQuest = QuestManager.Instance.FindActiveQuestByTargetId(meetTargetId);
        if (meetQuest != null)
        {
            QuestManager.Instance.ReportProgress(meetTargetId);

            // meetQuest.giverDialogue/completionDialogue are written in the
            // ORIGINAL GIVER's voice ("go speak with Bram", "did you speak
            // with them?") — showing that text here, on the TARGET's own
            // dialogue box, reads as the wrong character speaking (reported
            // as "the person I was sent to meet responded as the first
            // character"). The target's own ambient-line generator gives a
            // greeting actually in THIS npc's voice instead.
            label.text = NpcDialogueService.GenerateAmbientLine(identity.Personality);
            dialogueHideTimer = dialogueDisplaySeconds;

            if (QuestManager.Instance.IsQuestCompleted(meetQuest.title))
            {
                // Nothing previously reset this after BuildMeet recolors it
                // gold — it stayed gold forever, even after the quest was
                // long done.
                CompassMarker targetMarker = GetComponent<CompassMarker>();
                if (targetMarker != null) targetMarker.color = CompassMarker.DefaultNpcColor;
            }

            return;
        }

        quest ??= NpcQuestGenerator.Generate(identity, new System.Random());

        string line;
        if (QuestManager.Instance.TurnIn(quest.title))
        {
            line = quest.completionDialogue;
            Hotbar.RemoveItem(quest.title);
            identity.RecordInteraction("Player", true, $"The player completed my quest: \"{quest.title}\".");

            // Back to plain blue — unless some other active quest still wants
            // the player to meet this NPC, in which case it stays gold.
            CompassMarker marker = GetComponent<CompassMarker>();
            if (marker != null)
            {
                bool stillMeetTarget = QuestManager.Instance.IsTargetActive($"quest_meet_{identity.Personality.npcId}");
                marker.color = stillMeetTarget ? NpcQuestGenerator.MeetTargetColor : CompassMarker.DefaultNpcColor;
            }
        }
        else if (QuestManager.Instance.IsQuestCompleted(quest.title))
        {
            line = quest.completionDialogue;
        }
        else if (QuestManager.Instance.IsQuestActive(quest.title))
        {
            line = $"{quest.title} is still in progress.";
        }
        else
        {
            line = quest.giverDialogue;
            QuestManager.Instance.AcceptQuest(quest);
            identity.RecordInteraction("Player", true, $"Gave the player a quest: \"{quest.title}\".");

            // Fetch quests need something findable in the world for
            // Quest.targetId to actually resolve to — without this, the
            // quest could be accepted and shown as "in progress" forever
            // with nothing anywhere the player could ever pick up to
            // complete it. Same reasoning for Explore quests — a location
            // marker rather than a pickup, but otherwise the same gap.
            if (quest.type == QuestType.Fetch)
                QuestItemAiGenerator.SpawnFor(quest, transform.position, this);
            else if (quest.type == QuestType.Explore)
                QuestExploreAiGenerator.SpawnFor(quest, transform.position);
        }

        label.text = line;
        dialogueHideTimer = dialogueDisplaySeconds;
    }
}
