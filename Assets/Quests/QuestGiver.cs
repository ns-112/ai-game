using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Put this on an NPC. Requires a trigger Collider on the NPC (or a child)
// sized as the "in range to talk" zone, and the player to be tagged "Player".
public class QuestGiver : MonoBehaviour
{
    public Quest quest;

    [Header("UI (assign in Inspector)")]
    public GameObject interactPrompt;   // small "Press E to talk" prompt
    public GameObject dialoguePanel;    // dialogue box
    public Text dialogueText;           // Text inside dialoguePanel

    private bool playerInRange;

    private void Awake()
    {
        var marker = gameObject.AddComponent<CompassMarker>();
        marker.kind = CompassMarker.MarkerKind.QuestGiver;
        marker.color = Color.yellow;
        marker.label = quest != null ? quest.title : "Quest";
        // Shown on the compass while the quest is still available or in
        // progress; hides once completed, since there's nothing left to
        // find this NPC for.
        marker.isVisible = () => quest != null && QuestManager.Instance != null && !QuestManager.Instance.IsQuestCompleted(quest.title);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        playerInRange = true;
        if (interactPrompt) interactPrompt.SetActive(true);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        playerInRange = false;
        if (interactPrompt) interactPrompt.SetActive(false);
        if (dialoguePanel) dialoguePanel.SetActive(false);
    }

    private void Update()
    {
        if (!playerInRange) return;
        if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
            Interact();
    }

    private void Interact()
    {
        if (!dialoguePanel || !dialogueText) return;

        dialoguePanel.SetActive(true);

        if (QuestManager.Instance.IsQuestCompleted(quest.title))
        {
            dialogueText.text = quest.completionDialogue;
        }
        else if (QuestManager.Instance.IsQuestActive(quest.title))
        {
            dialogueText.text = $"{quest.title} is still in progress.";
        }
        else
        {
            dialogueText.text = quest.giverDialogue;
            QuestManager.Instance.AcceptQuest(quest);
        }
    }
}
