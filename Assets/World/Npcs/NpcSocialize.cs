using System.Collections.Generic;
using UnityEngine;

// Makes nearby wandering NPCs stop, turn to face each other (via
// NpcBillboard's facing override), and show one ambient line for a few
// seconds when they cross paths — flavor only, no quest data exchanged.
// Both participants log the exchange to their own persistent memory
// (NpcIdentity.RecordInteraction). Uses a static registry instead of
// physics triggers/layers so it works regardless of collider setup.
//
// Dialogue here always comes from NpcDialogueService's free procedural
// line, never the LLM — see that class's own comment for why the LLM
// budget is reserved for player conversations specifically.
[RequireComponent(typeof(NpcWander))]
public class NpcSocialize : MonoBehaviour
{
    private static readonly List<NpcSocialize> Active = new();

    public float socialRadius = 4f;
    public float chatDuration = 3f;
    public float checkInterval = 0.5f;
    public float cooldownAfterChat = 10f;

    private NpcWander wander;
    private NpcBillboard billboard;
    private NpcIdentity identity;
    private TextMesh bubble;
    private float checkTimer;
    private float chatTimer;
    private float cooldownTimer;
    private NpcSocialize partner;

    private void Awake()
    {
        wander = GetComponent<NpcWander>();
        billboard = GetComponent<NpcBillboard>();
        identity = GetComponent<NpcIdentity>();
        bubble = BuildBubble();
    }

    private TextMesh BuildBubble()
    {
        var bubbleObject = new GameObject("ChatBubble");
        bubbleObject.transform.SetParent(transform);
        bubbleObject.transform.localPosition = new Vector3(0f, 2.6f, 0f);

        var textMesh = bubbleObject.AddComponent<TextMesh>();
        textMesh.characterSize = 0.04f;
        textMesh.fontSize = 28;
        textMesh.anchor = TextAnchor.LowerCenter;
        textMesh.alignment = TextAlignment.Center;
        textMesh.color = new Color(0.9f, 0.9f, 1f);
        textMesh.text = "";

        bubbleObject.AddComponent<NpcBillboard>().flip180 = true;
        return textMesh;
    }

    private void OnEnable() => Active.Add(this);
    private void OnDisable() => Active.Remove(this);

    private void Update()
    {
        if (partner != null)
        {
            chatTimer -= Time.deltaTime;
            if (chatTimer <= 0f || !partner.gameObject.activeInHierarchy) EndChat();
            return;
        }

        if (cooldownTimer > 0f)
        {
            cooldownTimer -= Time.deltaTime;
            return;
        }

        checkTimer -= Time.deltaTime;
        if (checkTimer > 0f) return;
        checkTimer = checkInterval;

        FindNearbyPartner();
    }

    private void FindNearbyPartner()
    {
        foreach (NpcSocialize other in Active)
        {
            if (other == this || other.partner != null || other.cooldownTimer > 0f) continue;
            float sqrDist = (other.transform.position - transform.position).sqrMagnitude;
            if (sqrDist > socialRadius * socialRadius) continue;

            StartChat(other);
            other.StartChat(this);
            return;
        }
    }

    private void StartChat(NpcSocialize other)
    {
        partner = other;
        chatTimer = chatDuration;
        wander.SetExternallyPaused(true);
        if (billboard != null) billboard.SetFacingOverride(other.transform);

        if (identity != null)
        {
            string line = NpcDialogueService.GenerateAmbientLine(identity.Personality);
            bubble.text = line;

            string otherName = other.identity != null ? other.identity.Personality.displayName : "someone";
            identity.RecordInteraction(otherName, false, $"Chatted briefly with {otherName}. I said: \"{line}\"");
        }
    }

    private void EndChat()
    {
        partner = null;
        cooldownTimer = cooldownAfterChat;
        wander.SetExternallyPaused(false);
        if (billboard != null) billboard.ClearFacingOverride();
        bubble.text = "";
    }
}
