using UnityEngine;

// Procedurally builds a Quest for an NPC to offer the player — no LLM call,
// so it's instant instead of waiting on a network round-trip. Reuses the
// same offline word-bank technique as NpcPersonalityGenerator/
// NpcDialogueService's ambient line generator.
public static class NpcQuestGenerator
{
    private static readonly string[] FetchTargets =
        { "a lost heirloom", "a missing supply crate", "a stolen trinket", "a strayed pack animal", "a fallen banner" };
    private static readonly string[] ExploreTargets =
        { "the old ruins to the north", "the abandoned watchtower", "the cave past the ridge", "the forgotten shrine" };

    // Marks a Meet quest's target NPC on the compass — distinct from the
    // plain light-blue color every NPC gets by default, so there's actually
    // a way to tell which of the many wandering NPCs is the one to find.
    public static readonly Color MeetTargetColor = new(1f, 0.75f, 0.1f);

    // Marks a Fetch giver once the player is carrying their item back —
    // white stands apart from every other marker color (blue NPCs, gold
    // Meet targets, green items, violet explore spots, yellow quest givers).
    public static readonly Color ReturnToGiverColor = Color.white;

    // Quest.title is QuestManager's unique key (its activeQuests/completedQuests
    // are both keyed by title, not by any per-instance id), and NPC display
    // names repeat (only a dozen or so in the word bank) — folding the NPC's
    // own stable npcId into the title guarantees two different "Bram"s never
    // collide and silently drop one's quest.
    public static Quest Generate(NpcIdentity giver, System.Random rng)
    {
        // Meet needs another real, currently-spawned NPC to point at — if
        // none exists yet (e.g. very early in a session), fall back to
        // Fetch rather than offer a quest with no possible target at all.
        bool canOfferMeet = NpcIdentity.Active.Count > 1;
        var type = (QuestType)rng.Next(canOfferMeet ? 3 : 2);
        if (type == QuestType.Meet && !canOfferMeet) type = QuestType.Fetch;

        NpcPersonality speaker = giver.Personality;
        string title = $"{speaker.displayName}'s Favor ({speaker.npcId})";

        return type switch
        {
            QuestType.Fetch => BuildFetch(speaker, rng, title),
            QuestType.Explore => BuildExplore(speaker, rng, title),
            _ => BuildMeet(giver, rng, title),
        };
    }

    private static Quest BuildFetch(NpcPersonality speaker, System.Random rng, string title)
    {
        string item = FetchTargets[rng.Next(FetchTargets.Length)];
        return new Quest
        {
            title = title,
            giverDialogue = $"I've lost {item}. Bring it back to me and I'll make it worth your while.",
            completionDialogue = "You found it! Thank you, truly.",
            type = QuestType.Fetch,
            targetId = $"quest_item_{speaker.npcId}",
            count = 1,
            rewardId = "",
            requiresTurnIn = true,
            giverName = speaker.displayName,
        };
    }

    private static Quest BuildExplore(NpcPersonality speaker, System.Random rng, string title)
    {
        string place = ExploreTargets[rng.Next(ExploreTargets.Length)];
        return new Quest
        {
            title = title,
            giverDialogue = $"Nobody's been brave enough to check out {place}. Would you take a look?",
            completionDialogue = "You actually went? Good on you.",
            type = QuestType.Explore,
            targetId = $"quest_explore_{speaker.npcId}",
            count = 1,
            rewardId = "",
        };
    }

    // Picks an actual currently-spawned NPC (never the giver itself) as the
    // target, names them by their real personality name, and recolors their
    // compass marker so they're distinguishable from the crowd of ordinary
    // NPCs. NpcInteractable.Interact() reports progress against this
    // targetId on EVERY NPC interaction (not just the giver's), so walking
    // up to the named target and talking to them completes it.
    private static Quest BuildMeet(NpcIdentity giver, System.Random rng, string title)
    {
        // Prefer an NPC the player isn't already standing next to — otherwise
        // the "go find them" quest is trivially done by the NPC right beside
        // the giver. Falls back to anyone but the giver if that's all there is.
        var candidates = new System.Collections.Generic.List<NpcIdentity>();
        foreach (NpcIdentity npc in NpcIdentity.Active)
            if (npc != giver && !NpcInteractable.IsPlayerInRangeOf(npc)) candidates.Add(npc);
        if (candidates.Count == 0)
            foreach (NpcIdentity npc in NpcIdentity.Active)
                if (npc != giver) candidates.Add(npc);

        NpcIdentity target = candidates[rng.Next(candidates.Count)];

        CompassMarker targetMarker = target.GetComponent<CompassMarker>();
        if (targetMarker != null) targetMarker.color = MeetTargetColor;

        string targetName = target.Personality.displayName;
        return new Quest
        {
            title = title,
            giverDialogue = $"Do me a favor and go speak with {targetName} for me — look for the gold marker on your compass.",
            completionDialogue = "Did you speak with them? Good, that's a weight off my mind.",
            type = QuestType.Meet,
            targetId = $"quest_meet_{target.Personality.npcId}",
            count = 1,
            rewardId = "",
        };
    }
}
