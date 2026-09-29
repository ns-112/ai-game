using System;
using UnityEngine;

// Same pattern as TreeAiGenerator: rock variants are generated PROCEDURALLY
// (no AI call for shape — see TreeAiGenerator's header comment for why),
// ProceduralRockBuilder turns each into a guaranteed-valid mesh, and
// HuggingFaceImageClient still generates a rock texture per variant. Appends
// the results to each biome's Props (doesn't replace trees/other props
// already there).
public class RockAiGenerator : MonoBehaviour, IGenerationStage
{
    // Fires once every texture call this generator kicked off has come back
    // (success or failure). WorldLoadingGate can wait on this.
    public event Action OnFinished;

    public TerrainChunkManager terrainChunkManager;

    [Range(1, 5)]
    public int variantsPerBiome = 3;

    private static readonly string[] TextureAdjectives =
        { "mossy jagged", "smooth river-worn", "cracked weathered", "snow-capped", "lichen-covered", "sharp volcanic", "sun-bleached" };
    private static readonly string[] NameAdjectives =
        { "Jagged", "Mossy", "Weathered", "Cracked", "Smooth", "Craggy", "Sunken" };

    private int pendingTextures;

    public void GenerateFromBiomes(BiomeSettings[] biomes)
    {
        if (terrainChunkManager == null)
        {
            Debug.LogWarning("[Rocks] No TerrainChunkManager assigned — skipping.");
            OnFinished?.Invoke();
            return;
        }

        int seed = terrainChunkManager.terrainSettings.seed;

        var perBiomeVariants = new RockDefinition[biomes.Length][];
        for (int b = 0; b < biomes.Length; b++)
        {
            var rng = new System.Random(seed * 7919 + b * 104729 + 23); // distinct salt from TreeAiGenerator's 11
            perBiomeVariants[b] = new RockDefinition[variantsPerBiome];
            for (int v = 0; v < variantsPerBiome; v++)
                perBiomeVariants[b][v] = RandomDefinition(rng, biomes[b], v);
        }

        pendingTextures = 0;
        foreach (RockDefinition[] variants in perBiomeVariants)
            foreach (RockDefinition def in variants)
                if (!string.IsNullOrEmpty(def.texturePrompt)) pendingTextures++;

        for (int b = 0; b < biomes.Length; b++)
            foreach (RockDefinition def in perBiomeVariants[b])
                BuildAndAssign(def, biomes[b]);

        terrainChunkManager.RefreshAllProps();

        if (pendingTextures == 0) OnFinished?.Invoke();
    }

    private RockDefinition RandomDefinition(System.Random rng, BiomeSettings biome, int index)
    {
        return new RockDefinition
        {
            name = $"{biome.name} {NameAdjectives[rng.Next(NameAdjectives.Length)]} Rock {index + 1}",
            baseRadius = Range(rng, 0.4f, 2f),
            clusterCount = rng.Next(1, 6), // 1-5
            texturePrompt = $"top-down seamless tileable {TextureAdjectives[rng.Next(TextureAdjectives.Length)]} rock surface texture",
        };
    }

    private static float Range(System.Random rng, float min, float max) => min + (float)rng.NextDouble() * (max - min);

    private void BuildAndAssign(RockDefinition def, BiomeSettings biome)
    {
        Mesh mesh;
        try { mesh = ProceduralRockBuilder.Build(def); }
        catch (Exception e)
        {
            Debug.LogWarning($"[Rocks] Failed to build mesh for \"{def.name}\" — {e.Message}");
            // This variant's texture prompt was already counted into
            // pendingTextures up front, but the coroutine that would
            // decrement it never starts below — account for it here so
            // pendingTextures still reaches zero (checked after this
            // synchronous loop in GenerateFromBiomes) instead of waiting forever.
            if (!string.IsNullOrEmpty(def.texturePrompt)) pendingTextures--;
            return;
        }

        var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.5f, 0.5f, 0.5f) };

        var template = new GameObject($"Rock_{def.name}");
        template.layer = LayerMask.NameToLayer("World"); // so the player's GroundedCheck registers standing on top of it
        template.transform.SetParent(transform);
        template.SetActive(false);

        var filter = template.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;

        var renderer = template.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;

        ProceduralMeshUtils.AddMeshCollider(template, mesh);

        BiomePropUtils.AppendProp(biome, template);
        Debug.Log($"[Rocks] Built \"{def.name}\" for biome \"{biome.name}\" and assigned it as a prop.");

        if (!string.IsNullOrEmpty(def.texturePrompt))
            StartCoroutine(HuggingFaceImageClient.GenerateTexture(def.texturePrompt, tex =>
            {
                if (tex != null)
                {
                    material.color = Color.white; // clear the placeholder tint so the real texture isn't multiplied dark
                    material.mainTexture = tex;
                    Debug.Log($"[Rocks] Applied texture for \"{def.name}\".");
                }
                if (--pendingTextures <= 0) OnFinished?.Invoke();
            }));
    }
}
