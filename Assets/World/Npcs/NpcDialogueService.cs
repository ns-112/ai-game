// Generates the ambient line an NPC speaks during NpcSocialize's NPC-to-NPC
// chat beat. Fully offline (a small Markov chain, see MarkovTextGenerator) —
// player-facing interaction went through an LLM call here previously, but
// NpcInteractable now gives the player an instant, procedurally-generated
// quest instead (see NpcQuestGenerator), so nothing in the live NPC flow
// needs a network round-trip any more.
public static class NpcDialogueService
{
    // A modest fixed corpus for MarkovTextGenerator — generic NPC remarks
    // that don't reference any specific biome/structure name, so they read
    // fine regardless of which NPC says them. Bigger/more overlapping
    // corpora give the Markov chain more chances to blend two sentences
    // into something new rather than just replaying one verbatim.
    private static readonly string[] Corpus =
    {
        "the roads have been quiet since the old ruins collapsed",
        "I heard travelers speak of strange lights in the mountains",
        "trade has been slow but the harvest was good this year",
        "watch yourself near the old stones after dark",
        "my grandmother used to tell stories about this place",
        "the weather turns strange whenever the wind comes from the north",
        "I have not seen a friendly face in days",
        "some say the forest remembers every traveler who passes through",
        "keep your coin close and your wits closer",
        "the nights grow long this time of year",
        "I used to travel more before my knees gave out",
        "there is talk of a merchant caravan passing through soon",
        "the river has been rising since the storms began",
        "nobody comes this way without a good reason",
        "I keep meaning to leave but never quite manage it",
        "the old paths are safer than the new ones they say",
        "every season brings a new rumor to chase",
        "I trust few strangers but you seem harmless enough",
        "the animals have been restless lately, that usually means something",
        "there used to be more of us living out here",
        "you get used to the quiet after a while",
        "the last traveler through here left in quite a hurry",
        "I would offer you a seat but I only have the one",
        "something about the old ruins never sat right with me",
        "the stars have looked strange the past few nights",
        "I keep my door barred more than I used to",
        "you learn to read the weather living out here",
        "half the stories in this village are exaggerated but not all",
        "I have buried more friends than I care to count",
        "the world feels smaller than it did when I was young",
    };

    // Fully offline — a small Markov chain (MarkovTextGenerator) walked over
    // Corpus above, then flavored with a trait-based opener/closer. No
    // network call, no API key, so this always works.
    public static string GenerateAmbientLine(NpcPersonality speaker)
    {
        var rng = new System.Random();
        string sentence = MarkovTextGenerator.Generate(Corpus, rng);
        return Flavor(speaker, sentence);
    }

    private static string Flavor(NpcPersonality speaker, string sentence)
    {
        string trait = speaker.traits != null && speaker.traits.Length > 0 ? speaker.traits[0] : "curious";
        return trait switch
        {
            "grumpy" => $"...{sentence}, I suppose.",
            "cheerful" => $"{sentence}!",
            "suspicious" => $"{sentence}... or so I hear.",
            "timid" => $"Oh — {sentence}...",
            "talkative" => $"{sentence}, and that's not even the half of it.",
            "proud" => $"{sentence}, or so I've always said.",
            "greedy" => $"{sentence}. Speaking of which, got any coin?",
            "stoic" => $"{sentence}.",
            "brave" => $"{sentence}. Doesn't scare me none.",
            "forgetful" => $"{sentence}... or was it something else?",
            _ => $"{sentence}.",
        };
    }
}
