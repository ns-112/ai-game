// Rolls a lightweight personality for one NPC instance from fixed word
// banks — no network call, since NPCs spawn continuously as the player
// explores an unbounded world and an LLM call per instance wouldn't scale
// (mirrors NpcAiGenerator's own reasoning for keeping per-instance sprite
// variants procedural rather than each spawn being uniquely AI-generated).
// NpcDialogueService spends its LLM budget on the actual spoken line to the
// player instead, which matters far more than the underlying trait words.
public static class NpcPersonalityGenerator
{
    private static readonly string[] FirstNames =
        { "Bram", "Elin", "Torvald", "Maren", "Osric", "Della", "Fenn", "Ysolde", "Cade", "Rowan", "Petra", "Halvard" };

    // Also read directly by NpcDialogueService.GenerateAmbientLine to pick a
    // matching stock line — keep this list and that method's switch in sync
    // if either changes.
    public static readonly string[] Traits =
        { "curious", "grumpy", "cheerful", "suspicious", "brave", "timid", "talkative", "stoic", "kind", "greedy", "proud", "forgetful" };

    private static readonly string[] Goals =
    {
        "finding a rare heirloom", "retiring somewhere quiet", "proving their worth",
        "reuniting with distant family", "forgetting a past mistake", "mapping the whole region",
        "settling an old debt", "learning a forgotten trade",
    };

    public static NpcPersonality Generate(string npcId, string role, System.Random rng)
    {
        string name = FirstNames[rng.Next(FirstNames.Length)];
        string traitA = Traits[rng.Next(Traits.Length)];
        string traitB = Traits[rng.Next(Traits.Length)];
        string goal = Goals[rng.Next(Goals.Length)];

        return new NpcPersonality
        {
            npcId = npcId,
            displayName = name,
            traits = new[] { traitA, traitB },
            backstory = $"{name} is {role}, known for being {traitA} and {traitB}, currently {goal}.",
        };
    }
}
