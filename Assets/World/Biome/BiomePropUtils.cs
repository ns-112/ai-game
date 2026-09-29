using UnityEngine;

// Shared by TreeAiGenerator/RockAiGenerator/BuildingAiGenerator: adds a
// generated prop template to a biome's list without clobbering whatever
// other prop types have already been added to it.
public static class BiomePropUtils
{
    public static void AppendProp(BiomeSettings biome, GameObject prop)
    {
        GameObject[] existing = biome.props;
        var updated = new GameObject[(existing?.Length ?? 0) + 1];
        existing?.CopyTo(updated, 0);
        updated[updated.Length - 1] = prop;
        biome.props = updated;
    }
}
