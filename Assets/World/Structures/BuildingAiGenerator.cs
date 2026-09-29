using System;
using UnityEngine;

// Same pattern as TreeAiGenerator/RockAiGenerator: small building variants
// are generated PROCEDURALLY (no AI call for shape), ProceduralBuildingBuilder
// turns each into a guaranteed-valid mesh (with its own seeded random roof
// style/chimney/porch — see ProceduralBuildingBuilder), and
// HuggingFaceImageClient still generates wall + roof textures per variant.
// Appends the results to each biome's Props with a low PropWeight so
// buildings stay rare compared to trees/rocks instead of appearing just as
// often. Small/common buildings stay in this pooled-template model (a
// handful of variants cloned around the world) — see StructureAiGenerator
// for the large/rare landmark case, which is generated fresh per instance
// instead since there are few enough of them that per-instance uniqueness is
// worth paying for.
public class BuildingAiGenerator : MonoBehaviour, IGenerationStage
{
    // Fires once every texture call this generator kicked off has come back
    // (success or failure). WorldLoadingGate can wait on this.
    public event Action OnFinished;

    public TerrainChunkManager terrainChunkManager;

    [Range(1, 5)]
    public int variantsPerBiome = 2;

    [Range(0.01f, 1f)]
    public float rarityWeight = 0.1f;

    private static readonly string[] WallAdjectives =
        { "rustic timber", "whitewashed stone", "weathered wooden", "mossy stone", "sun-bleached clay", "snow-dusted log" };
    private static readonly string[] RoofAdjectives =
        { "mossy thatched", "weathered wood-shingle", "red clay-tile", "slate", "snow-capped shingle" };
    private static readonly string[] NameAdjectives =
        { "Cozy", "Sturdy", "Quaint", "Timeworn", "Humble", "Rustic" };
    private static readonly string[] NameNouns =
        { "Hut", "Cabin", "Lodge", "Cottage", "Shack", "House" };

    private int pendingTextures;

    public void GenerateFromBiomes(BiomeSettings[] biomes)
    {
        if (terrainChunkManager == null)
        {
            Debug.LogWarning("[Buildings] No TerrainChunkManager assigned — skipping.");
            OnFinished?.Invoke();
            return;
        }

        int seed = terrainChunkManager.terrainSettings.seed;

        var perBiomeVariants = new BuildingDefinition[biomes.Length][];
        for (int b = 0; b < biomes.Length; b++)
        {
            var rng = new System.Random(seed * 7919 + b * 104729 + 37); // distinct salt from trees (11) / rocks (23)
            perBiomeVariants[b] = new BuildingDefinition[variantsPerBiome];
            for (int v = 0; v < variantsPerBiome; v++)
                perBiomeVariants[b][v] = RandomDefinition(rng, biomes[b], v);
        }

        pendingTextures = 0;
        foreach (BuildingDefinition[] variants in perBiomeVariants)
            foreach (BuildingDefinition def in variants)
            {
                if (!string.IsNullOrEmpty(def.wallPrompt)) pendingTextures++;
                if (!string.IsNullOrEmpty(def.roofPrompt)) pendingTextures++;
            }

        for (int b = 0; b < biomes.Length; b++)
            foreach (BuildingDefinition def in perBiomeVariants[b])
                BuildAndAssign(def, biomes[b]);

        terrainChunkManager.RefreshAllProps();

        if (pendingTextures == 0) OnFinished?.Invoke();
    }

    private BuildingDefinition RandomDefinition(System.Random rng, BiomeSettings biome, int index)
    {
        return new BuildingDefinition
        {
            name = $"{biome.name} {NameAdjectives[rng.Next(NameAdjectives.Length)]} {NameNouns[rng.Next(NameNouns.Length)]} {index + 1}",
            width = Range(rng, 3f, 8f),
            depth = Range(rng, 3f, 8f),
            wallHeight = Range(rng, 2f, 4f),
            roofHeight = Range(rng, 1f, 3f),
            wallPrompt = $"seamless tileable {WallAdjectives[rng.Next(WallAdjectives.Length)]} wall texture",
            roofPrompt = $"seamless tileable {RoofAdjectives[rng.Next(RoofAdjectives.Length)]} roof texture",
        };
    }

    private static float Range(System.Random rng, float min, float max) => min + (float)rng.NextDouble() * (max - min);

    private void BuildAndAssign(BuildingDefinition def, BiomeSettings biome)
    {
        Mesh mesh;
        try { mesh = ProceduralBuildingBuilder.Build(def); }
        catch (Exception e)
        {
            Debug.LogWarning($"[Buildings] Failed to build mesh for \"{def.name}\" — {e.Message}");
            // This variant's wall/roof prompts were already counted into
            // pendingTextures up front, but the coroutines that would
            // decrement them never start below — account for them here so
            // pendingTextures still reaches zero (checked after this
            // synchronous loop in GenerateFromBiomes) instead of waiting forever.
            if (!string.IsNullOrEmpty(def.wallPrompt)) pendingTextures--;
            if (!string.IsNullOrEmpty(def.roofPrompt)) pendingTextures--;
            return;
        }

        var walls = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.7f, 0.65f, 0.55f) };
        var roof = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.4f, 0.2f, 0.15f) };

        var template = new GameObject($"Building_{def.name}");
        template.layer = LayerMask.NameToLayer("World"); // so the player's GroundedCheck registers standing on top of it
        template.transform.SetParent(transform);
        template.SetActive(false);

        var filter = template.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;

        var renderer = template.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = new[] { walls, roof };

        ProceduralMeshUtils.AddMeshCollider(template, mesh);

        template.AddComponent<PropWeight>().weight = rarityWeight;

        BiomePropUtils.AppendProp(biome, template);
        Debug.Log($"[Buildings] Built \"{def.name}\" for biome \"{biome.name}\" and assigned it as a prop (weight {rarityWeight}).");

        if (!string.IsNullOrEmpty(def.wallPrompt))
            StartCoroutine(HuggingFaceImageClient.GenerateTexture(def.wallPrompt, tex =>
            {
                if (tex != null)
                {
                    walls.color = Color.white; // clear the placeholder tint so the real texture isn't multiplied dark
                    walls.mainTexture = tex;
                    Debug.Log($"[Buildings] Applied wall texture for \"{def.name}\".");
                }
                if (--pendingTextures <= 0) OnFinished?.Invoke();
            }));

        if (!string.IsNullOrEmpty(def.roofPrompt))
            StartCoroutine(HuggingFaceImageClient.GenerateTexture(def.roofPrompt, tex =>
            {
                if (tex != null)
                {
                    roof.color = Color.white;
                    roof.mainTexture = tex;
                    Debug.Log($"[Buildings] Applied roof texture for \"{def.name}\".");
                }
                if (--pendingTextures <= 0) OnFinished?.Invoke();
            }));
    }
}
