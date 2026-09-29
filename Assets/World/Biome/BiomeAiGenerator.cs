using System;
using UnityEngine;

// Generates a PALETTE of LLM biome definitions and replaces
// TerrainSettings.biomes with it shortly after Start (the terrain begins
// with the hardcoded defaults on TerrainSettings and swaps in the generated
// ones once the response arrives), then rebuilds whatever chunks are already
// streamed in so the change is visible immediately. The palette itself is
// small (biomeCount entries), but TerrainSettings places it across the
// (effectively infinite) world algorithmically — individual cells pick a
// palette entry by hashing their coordinates, so the same handful of AI
// looks get reused and interleaved everywhere rather than the world being
// limited to biomeCount regions total. If generation or parsing fails, the
// hardcoded defaults are left in place untouched — the world never ends up
// with zero biomes.
public class BiomeAiGenerator : MonoBehaviour, IGenerationStage
{
    // Fires once generation has settled either way (biomes applied, or the
    // defaults were kept after a failure) — terrain shape is final at that
    // point. WorldLoadingGate waits on this before releasing the player.
    public event Action OnFinished;

    public TerrainChunkManager terrainChunkManager;

    [Tooltip("Optional — if set, its ground textures are (re)generated from the AI biome names/descriptions once they're ready.")]
    public BiomeAiTextureGenerator textureGenerator;

    [Tooltip("Optional — if set, generates one procedural tree type per biome once biome data is ready.")]
    public TreeAiGenerator treeGenerator;

    [Tooltip("Optional — if set, generates one procedural rock type per biome once biome data is ready.")]
    public RockAiGenerator rockGenerator;

    [Tooltip("Optional — if set, generates one rare procedural building type per biome once biome data is ready.")]
    public BuildingAiGenerator buildingGenerator;

    [Tooltip("Optional — if set, generates a very rare, large, enterable structure per biome once biome data is ready.")]
    public StructureAiGenerator structureGenerator;

    [Tooltip("Optional — if set, generates billboard-sprite NPC variants per biome once biome data is ready, and starts spawning them as chunks stream in.")]
    public NpcAiGenerator npcGenerator;

    [Range(3, 12)]
    [Tooltip("Size of the biome palette. Cells across the world each pick one of these entries algorithmically (see TerrainSettings), so a bigger palette means more variety everywhere, not a bigger world — placement is already unbounded.")]
    public int biomeCount = 8;

    // Guards against the LLM ignoring the prompt's range — one runaway value
    // (e.g. 500) would turn every cell using that palette entry into cliffs.
    private const float MinBiomeHeight = 3f;
    private const float MaxBiomeHeight = 160f;

    [TextArea(4, 10)]
    public string prompt =
        "Invent {COUNT} fantasy game terrain biomes for a low-poly open-world game, ranging from calm/flat to " +
        "extreme/tall. Respond with ONLY a JSON array, no prose, no markdown fences, in this exact shape: " +
        "[{\"name\":string, \"description\":string (one sentence), \"maxHeight\":number}]. " +
        "maxHeight is world units controlling terrain height (bigger = taller, range 5-140).";

    private void Start()
    {
        if (terrainChunkManager == null)
        {
            Debug.LogWarning("[AI Biomes] No TerrainChunkManager assigned — skipping.");
            OnFinished?.Invoke();
            return;
        }

        GenerationDebugLog.Show("Generating biomes...");
        string resolvedPrompt = prompt.Replace("{COUNT}", biomeCount.ToString());
        StartCoroutine(GeminiTextClient.Generate(resolvedPrompt, OnResult));
    }

    private void OnResult(string json)
    {
        if (string.IsNullOrEmpty(json))
        {
            Debug.LogWarning("[AI Biomes] Generation failed — keeping default biomes.");
            FinishWithBiomes(terrainChunkManager.terrainSettings.biomes);
            return;
        }

        BiomeSettings[] generated = ParseBiomes(json);
        if (generated == null || generated.Length == 0)
        {
            Debug.LogWarning($"[AI Biomes] Could not parse response, keeping default biomes:\n{json}");
            FinishWithBiomes(terrainChunkManager.terrainSettings.biomes);
            return;
        }

        // Carry over scatter settings (props/density/scale) from the
        // matching default slot, since the LLM only invents shape + flavor text.
        BiomeSettings[] existing = terrainChunkManager.terrainSettings.biomes;
        foreach (BiomeSettings biome in generated)
            biome.maxHeight = Mathf.Clamp(biome.maxHeight, MinBiomeHeight, MaxBiomeHeight);
        for (int i = 0; i < generated.Length && i < existing.Length; i++)
        {
            generated[i].props = existing[i].props;
            generated[i].propsPerSquareUnit = existing[i].propsPerSquareUnit;
            generated[i].minScale = existing[i].minScale;
            generated[i].maxScale = existing[i].maxScale;
        }

        terrainChunkManager.terrainSettings.biomes = generated;
        terrainChunkManager.RegenerateAllChunks();

        foreach (BiomeSettings biome in generated)
            Debug.Log($"[AI Biomes] {biome.name}: {biome.description} (maxHeight={biome.maxHeight})");

        FinishWithBiomes(generated);
    }

    // Always drives the downstream generators (texture/tree/rock/building/structure),
    // even when biome text generation failed — otherwise the ones WorldLoadingGate
    // watches directly would never fire their own OnFinished (they only fire it from
    // inside GenerateFromBiomes), and the gate would wait forever with the player
    // frozen. Failing "closed" here would mean any Gemini hiccup permanently locks
    // the player out of control.
    private void FinishWithBiomes(BiomeSettings[] biomes)
    {
        GenerationDebugLog.Show("Biomes ready — generating world content...");

        if (textureGenerator != null)
            textureGenerator.GenerateFromBiomes(biomes);

        if (treeGenerator != null)
            treeGenerator.GenerateFromBiomes(biomes);

        if (rockGenerator != null)
            rockGenerator.GenerateFromBiomes(biomes);

        if (buildingGenerator != null)
            buildingGenerator.GenerateFromBiomes(biomes);

        if (structureGenerator != null)
            structureGenerator.GenerateFromBiomes(biomes);

        if (npcGenerator != null)
            npcGenerator.GenerateFromBiomes(biomes);

        OnFinished?.Invoke();
    }

    private static BiomeSettings[] ParseBiomes(string json)
    {
        // JsonUtility can't parse a top-level JSON array directly, so wrap it
        // in an object first.
        string wrapped = "{\"items\":" + json.Trim() + "}";
        try
        {
            BiomeListWrapper container = JsonUtility.FromJson<BiomeListWrapper>(wrapped);
            return container?.items;
        }
        catch (ArgumentException e)
        {
            Debug.LogWarning($"[AI Biomes] JSON parse error: {e.Message}");
            return null;
        }
    }

    [Serializable] private class BiomeListWrapper { public BiomeSettings[] items; }
}
