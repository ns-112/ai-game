using System.Collections.Generic;
using System.Text;

// A tiny, fully offline Markov-chain text generator — the classic pre-LLM
// technique, used here as the "smaller scale just text generator" fallback
// for NPC dialogue when no LLM (Gemini) call is available or it fails. No
// network call, no API key, no external dependency at all.
//
// Builds a word-transition table from a fixed corpus of sentences (order-2:
// each entry is keyed by the PREVIOUS TWO words) and randomly walks it to
// produce a new line. With a small corpus like NpcDialogueService's, many
// two-word contexts only ever have one possible next word, so it will often
// reproduce a corpus sentence closely — but wherever two sentences happen to
// share a two-word phrase, the walk can hop from one into the other,
// producing combinations that were never written verbatim. More corpus
// sentences (and more shared phrasing between them) means more variety.
public static class MarkovTextGenerator
{
    private const string Start = "\u0002"; // sentinel: beginning of sentence
    private const string End = "\u0003";   // sentinel: end of sentence

    public static string Generate(IReadOnlyList<string> corpus, System.Random rng, int maxWords = 16)
    {
        Dictionary<(string, string), List<string>> chain = BuildChain(corpus);
        return Walk(chain, rng, maxWords);
    }

    private static Dictionary<(string, string), List<string>> BuildChain(IReadOnlyList<string> corpus)
    {
        var chain = new Dictionary<(string, string), List<string>>();

        foreach (string sentence in corpus)
        {
            string w0 = Start, w1 = Start;
            foreach (string word in sentence.Split(' '))
            {
                AddTransition(chain, (w0, w1), word);
                w0 = w1;
                w1 = word;
            }
            AddTransition(chain, (w0, w1), End);
        }

        return chain;
    }

    private static void AddTransition(Dictionary<(string, string), List<string>> chain, (string, string) key, string next)
    {
        if (!chain.TryGetValue(key, out List<string> options))
        {
            options = new List<string>();
            chain[key] = options;
        }
        options.Add(next);
    }

    private static string Walk(Dictionary<(string, string), List<string>> chain, System.Random rng, int maxWords)
    {
        string w0 = Start, w1 = Start;
        var result = new StringBuilder();

        for (int i = 0; i < maxWords; i++)
        {
            if (!chain.TryGetValue((w0, w1), out List<string> options) || options.Count == 0)
                break;

            string next = options[rng.Next(options.Count)];
            if (next == End) break;

            if (result.Length > 0) result.Append(' ');
            result.Append(next);

            w0 = w1;
            w1 = next;
        }

        return result.Length > 0 ? result.ToString() : "...";
    }
}
