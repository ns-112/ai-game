using System;
using UnityEngine;

// Rare landmark structures are generated PER INSTANCE, not as a pooled
// template like trees/rocks/small buildings — there are few enough of them
// in the world (that's the point) that each one being genuinely unique is
// worth paying for, unlike common scatter props where a modest shared pool
// reads as plenty of variety on its own. See RandomStructureGenerator (picks
// the random shape) and TerrainChunk.ScatterStructures (builds the mesh
// fresh, at the moment/place each one gets placed — never cloned from a
// template) for where the actual work happens.
//
// This component's only remaining job is handing the configured spawn
// rarity to every biome so TerrainChunk knows how often to roll for one —
// no AI call, no texture generation (structures use randomized stone colors
// instead — see RandomStructureGenerator), nothing to wait on.
public class StructureAiGenerator : MonoBehaviour, IGenerationStage
{
    public event Action OnFinished;

    [Range(0.001f, 0.2f)]
    [Tooltip("Chance, per loaded chunk, that a structure spawns while a biome is dominant there. Not a relative pool weight — structures no longer share a pick pool with trees/rocks/buildings, so this is an absolute per-chunk probability.")]
    public float rarityWeight = 0.02f;

    public void GenerateFromBiomes(BiomeSettings[] biomes)
    {
        foreach (BiomeSettings biome in biomes)
            biome.structureRarity = rarityWeight;

        OnFinished?.Invoke();
    }
}
