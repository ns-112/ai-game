using System;
using UnityEngine;

// Generates the biome ground textures at runtime via HuggingFaceImageClient
// and stores each one on its matching TerrainSettings.biomes[] entry
// (BiomeSettings.groundTexture), then asks TerrainChunkManager to re-bind
// textures on every already-streamed-in chunk so the result shows up
// immediately instead of only affecting chunks built afterward.
//
// This never touches a shared Material directly (that was the old scheme,
// back when the palette was capped at 3 biomes matching the shader's 3
// texture slots one-to-one everywhere). With the palette no longer capped,
// there's no single global meaning for "slot 0/1/2" — each TerrainChunk
// picks its own locally-relevant biomes and binds their textures via its
// own MaterialPropertyBlock, so all this generator needs to do is make the
// texture available per biome and ask chunks to refresh.
//
// If a call fails (no key, no internet, API error), that biome just keeps
// showing the shared material's placeholder texture until a future
// successful generation.
public class BiomeAiTextureGenerator : MonoBehaviour, IGenerationStage
{
    // Fires once every texture call this generator started (success or
    // failure) has come back. WorldLoadingGate can wait on this.
    public event Action OnFinished;

    public TerrainChunkManager terrainChunkManager;

    [Tooltip("If a BiomeAiGenerator drives this one via GenerateFromBiomes, turn this on so Start() doesn't also generate from the fallback prompts below (which would waste a duplicate set of API calls).")]
    public bool waitForBiomeGenerator = false;

    [Tooltip("Fallback prompts used directly on Start() when nothing else drives this component (ignored if Wait For Biome Generator is on). Prompt i is applied to terrainChunkManager.terrainSettings.biomes[i] — keep the lengths matched.")]
    public string[] biomePrompts;

    private int pending;

    private void Start()
    {
        if (waitForBiomeGenerator) return;
        Generate(biomePrompts);
    }

    // Called by BiomeAiGenerator once it has real biome data, so ground
    // textures reflect whatever the LLM actually invented instead of the
    // generic fallback prompts above. `biomes` must be the same array (or at
    // least same order) as terrainChunkManager.terrainSettings.biomes, since
    // prompt index i is applied to biomes[i].
    public void GenerateFromBiomes(BiomeSettings[] biomes)
    {
        var prompts = new string[biomes.Length];
        for (int i = 0; i < biomes.Length; i++)
        {
            string flavor = string.IsNullOrEmpty(biomes[i].description) ? biomes[i].name : biomes[i].description;
            prompts[i] = $"top-down seamless photorealistic tileable ground texture depicting {flavor}, flat lighting, no shadows";
        }
        Generate(prompts);
    }

    private void Generate(string[] prompts)
    {
        if (terrainChunkManager == null)
        {
            Debug.LogWarning("[AI Texture] BiomeAiTextureGenerator has no TerrainChunkManager assigned — skipping.");
            OnFinished?.Invoke();
            return;
        }

        BiomeSettings[] biomes = terrainChunkManager.terrainSettings.biomes;
        pending = Mathf.Min(prompts.Length, biomes.Length);
        if (pending == 0)
        {
            OnFinished?.Invoke();
            return;
        }

        Debug.Log($"[AI Texture] Starting generation for {pending} biome texture(s)...");

        for (int i = 0; i < pending; i++)
        {
            int archetype = i; // capture for the coroutine callback — matches biomes[] index directly
            StartCoroutine(HuggingFaceImageClient.GenerateTexture(prompts[archetype], texture =>
            {
                if (texture == null)
                    Debug.LogWarning($"[AI Texture] \"{biomes[archetype].name}\" generation failed — keeping placeholder texture.");
                else
                {
                    biomes[archetype].groundTexture = texture;
                    terrainChunkManager.RefreshBiomeTextures();
                    Debug.Log($"[AI Texture] Applied generated texture for \"{biomes[archetype].name}\".");
                }

                if (--pending <= 0) OnFinished?.Invoke();
            }));
        }
    }
}
