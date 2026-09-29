using System;
using UnityEngine;

// Generates several tree variants per biome PROCEDURALLY — shape parameters
// are rolled locally (System.Random, seeded from the world seed) within the
// same ranges an LLM would have picked from, so generation is instant and
// never depends on network availability or API quota. Trees are common,
// many-instanced props, so a modest pool of variants per biome (cloned by
// ScatterProps like any other prop) reads as plenty of visual variety
// without paying for every individual tree in the world to be unique — see
// StructureAiGenerator/RandomStructureGenerator for the rare-landmark case
// where per-instance uniqueness is worth that cost.
//
// ProceduralTreeBuilder turns each variant into a guaranteed-valid mesh, and
// HuggingFaceImageClient still generates a bark + leaf texture per variant —
// texture generation is a separate service from the one that was hitting
// quota limits, so it stays AI-driven. Each finished tree becomes an
// inactive template GameObject appended to that biome's Props array, then
// chunks are rebuilt so it starts showing up. If a texture call fails, that
// variant just keeps its flat placeholder color.
public class TreeAiGenerator : MonoBehaviour, IGenerationStage
{
    // Fires once every texture call this generator kicked off has come back
    // (success or failure) — shape generation is synchronous now, so this
    // only ever waits on textures. WorldLoadingGate can wait on this.
    public event Action OnFinished;

    public TerrainChunkManager terrainChunkManager;

    [Range(1, 5)]
    public int variantsPerBiome = 3;

    private static readonly string[] BarkAdjectives =
        { "smooth pale", "deeply furrowed", "peeling papery", "mossy dark", "scaly grey", "reddish stringy", "knotted weathered" };
    private static readonly string[] CanopyAdjectives =
        { "vibrant green", "autumnal orange", "dark evergreen", "silvery", "dense leafy", "sparse windswept", "blossoming" };
    private static readonly string[] NameAdjectives =
        { "Ancient", "Twisted", "Slender", "Gnarled", "Towering", "Weathered", "Young", "Sprawling" };

    private int pendingTextures;

    public void GenerateFromBiomes(BiomeSettings[] biomes)
    {
        if (terrainChunkManager == null)
        {
            Debug.LogWarning("[Trees] No TerrainChunkManager assigned — skipping.");
            OnFinished?.Invoke();
            return;
        }

        int seed = terrainChunkManager.terrainSettings.seed;

        var perBiomeVariants = new TreeDefinition[biomes.Length][];
        for (int b = 0; b < biomes.Length; b++)
        {
            // Distinct salt per generator category (11) so trees/rocks/buildings
            // don't end up drawing the same random sequence for a given biome.
            var rng = new System.Random(seed * 7919 + b * 104729 + 11);
            perBiomeVariants[b] = new TreeDefinition[variantsPerBiome];
            for (int v = 0; v < variantsPerBiome; v++)
                perBiomeVariants[b][v] = RandomDefinition(rng, biomes[b], v);
        }

        // Count expected texture calls up front so we know when everything's
        // truly done, before any of those coroutines can possibly complete.
        pendingTextures = 0;
        foreach (TreeDefinition[] variants in perBiomeVariants)
            foreach (TreeDefinition def in variants)
            {
                if (!string.IsNullOrEmpty(def.barkPrompt)) pendingTextures++;
                if (!string.IsNullOrEmpty(def.canopyPrompt)) pendingTextures++;
            }

        for (int b = 0; b < biomes.Length; b++)
            foreach (TreeDefinition def in perBiomeVariants[b])
                BuildAndAssign(def, biomes[b]);

        terrainChunkManager.RefreshAllProps();

        if (pendingTextures == 0) OnFinished?.Invoke();
    }

    private TreeDefinition RandomDefinition(System.Random rng, BiomeSettings biome, int index)
    {
        int tierCount = rng.Next(1, 4); // 1-3
        var tiers = new CanopyTier[tierCount];
        for (int i = 0; i < tierCount; i++)
            tiers[i] = new CanopyTier
            {
                radius = Range(rng, 0.5f, 6f),
                height = Range(rng, 0.8f, 6f),
                offset = Range(rng, -0.5f, 1.5f),
            };

        int branchCount = rng.Next(0, 6); // 0-5
        var branches = new BranchDefinition[branchCount];
        for (int i = 0; i < branchCount; i++)
            branches[i] = new BranchDefinition
            {
                heightFraction = Range(rng, 0.3f, 0.9f),
                angleFromVertical = Range(rng, 30f, 75f),
                azimuth = Range(rng, 0f, 360f),
                length = Range(rng, 0.8f, 4f),
                radius = Range(rng, 0.05f, 0.4f),
                canopyRadius = Range(rng, 0.4f, 2.5f),
            };

        return new TreeDefinition
        {
            name = $"{biome.name} {NameAdjectives[rng.Next(NameAdjectives.Length)]} Tree {index + 1}",
            trunkHeight = Range(rng, 2f, 15f),
            trunkRadius = Range(rng, 0.2f, 1.2f),
            canopyTiers = tiers,
            branches = branches,
            barkPrompt = $"top-down seamless tileable {BarkAdjectives[rng.Next(BarkAdjectives.Length)]} tree bark texture",
            canopyPrompt = $"top-down seamless tileable {CanopyAdjectives[rng.Next(CanopyAdjectives.Length)]} leaf foliage texture",
        };
    }

    private static float Range(System.Random rng, float min, float max) => min + (float)rng.NextDouble() * (max - min);

    private void BuildAndAssign(TreeDefinition def, BiomeSettings biome)
    {
        Mesh mesh;
        try { mesh = ProceduralTreeBuilder.Build(def); }
        catch (Exception e)
        {
            Debug.LogWarning($"[Trees] Failed to build mesh for \"{def.name}\" — {e.Message}");
            // This variant's bark/canopy prompts were already counted into
            // pendingTextures up front (before we knew the build would fail),
            // but the coroutines that would decrement them are never started
            // below — account for them here so pendingTextures still reaches
            // zero and OnFinished fires (via the check after the loop in
            // GenerateFromBiomes, which runs after every BuildAndAssign call
            // in that synchronous loop), instead of waiting forever on
            // textures nobody kicked off.
            if (!string.IsNullOrEmpty(def.barkPrompt)) pendingTextures--;
            if (!string.IsNullOrEmpty(def.canopyPrompt)) pendingTextures--;
            return;
        }

        var bark = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.35f, 0.25f, 0.15f) };
        var canopy = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.25f, 0.45f, 0.2f) };

        var template = new GameObject($"Tree_{def.name}");
        template.layer = LayerMask.NameToLayer("World"); // so the player's GroundedCheck registers standing on top of it
        template.transform.SetParent(transform);
        template.SetActive(false);

        var filter = template.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;

        var renderer = template.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = new[] { bark, canopy };

        ProceduralMeshUtils.AddMeshCollider(template, mesh);

        BiomePropUtils.AppendProp(biome, template);
        Debug.Log($"[Trees] Built \"{def.name}\" for biome \"{biome.name}\" and assigned it as a prop.");

        if (!string.IsNullOrEmpty(def.barkPrompt))
            StartCoroutine(HuggingFaceImageClient.GenerateTexture(def.barkPrompt, tex =>
            {
                if (tex != null)
                {
                    bark.color = Color.white; // clear the placeholder tint so the real texture isn't multiplied dark
                    bark.mainTexture = tex;
                    Debug.Log($"[Trees] Applied bark texture for \"{def.name}\".");
                }
                if (--pendingTextures <= 0) OnFinished?.Invoke();
            }));

        if (!string.IsNullOrEmpty(def.canopyPrompt))
            StartCoroutine(HuggingFaceImageClient.GenerateTexture(def.canopyPrompt, tex =>
            {
                if (tex != null)
                {
                    canopy.color = Color.white;
                    canopy.mainTexture = tex;
                    Debug.Log($"[Trees] Applied canopy texture for \"{def.name}\".");
                }
                if (--pendingTextures <= 0) OnFinished?.Invoke();
            }));
    }
}
