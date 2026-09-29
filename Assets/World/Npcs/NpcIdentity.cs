using System;
using System.Collections.Generic;
using UnityEngine;

// Holds this NPC instance's stable id, rolled personality, and on-disk
// interaction memory. Attached to every NPC template by NpcAiGenerator,
// which calls Initialize() right after spawning a clone (before activating
// it, so NpcInteractable/NpcSocialize see populated data in their own
// Awake()).
public class NpcIdentity : MonoBehaviour
{
    // Every currently-spawned NPC — lets NpcQuestGenerator pick a real,
    // existing NPC as a Meet-quest target instead of an unfindable flavor
    // phrase with nobody actually behind it.
    public static readonly List<NpcIdentity> Active = new();

    public NpcPersonality Personality { get; private set; }
    public NpcMemory Memory { get; private set; }

    private void OnEnable() => Active.Add(this);
    private void OnDisable() => Active.Remove(this);

    public void Initialize(string npcId, string role, System.Random rng)
    {
        Personality = NpcPersonalityGenerator.Generate(npcId, role, rng);
        Memory = NpcMemoryStore.Load(npcId);
    }

    // Appends one exchange to this NPC's memory and immediately persists it
    // — interactions are infrequent (player conversations, occasional NPC-
    // NPC chats), so writing on every one is cheap and means memory is never
    // lost to an unclean exit.
    public void RecordInteraction(string otherName, bool otherIsPlayer, string summary)
    {
        Memory.entries.Add(new NpcMemoryEntry
        {
            otherName = otherName,
            otherIsPlayer = otherIsPlayer,
            summary = summary,
            timestamp = DateTime.UtcNow.ToString("o"),
        });

        // Cap accumulated history — only the most recent interactions matter
        // for flavoring future dialogue.
        const int maxEntries = 20;
        if (Memory.entries.Count > maxEntries)
            Memory.entries.RemoveRange(0, Memory.entries.Count - maxEntries);

        NpcMemoryStore.Save(Memory);
    }
}
