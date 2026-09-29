// Produces a randomized StructureDefinition directly — no AI call, no shared
// template. Unlike the pooled trees/rocks/small-buildings generators,
// structures are rare landmarks meant to each feel unique, so
// TerrainChunk.ScatterStructures calls this fresh at the moment/place a
// structure is about to be placed, instead of building N variants once and
// cloning whichever one gets picked. ProceduralStructureBuilder already
// randomizes the geometry per structure NAME (collapsed wall sections,
// rubble, tilted roof, pillars) via a hash of def.name — this just picks the
// outer dimensions and an always-distinct name (so two structures never hash
// to the same ruin pattern even if they land on the same biome/theme),
// using the same value ranges the old AI prompt specified.
public static class RandomStructureGenerator
{
    private static readonly string[] Themes =
    {
        "Ruined Temple", "Collapsed Watchtower", "Crumbling Keep", "Abandoned Sanctum",
        "Forgotten Hall", "Shattered Bastion", "Lost Monastery", "Derelict Citadel",
    };

    public static StructureDefinition RandomDefinition(System.Random rng, BiomeSettings biome)
    {
        return new StructureDefinition
        {
            // The trailing number is what guarantees a distinct hash per
            // instance for ProceduralStructureBuilder's internal seeding —
            // without it, every structure that rolls the same biome+theme
            // combination (inevitable with only 8 themes) would come out as
            // an identical ruin pattern despite differing dimensions.
            name = $"{biome.name} {Themes[rng.Next(Themes.Length)]} {rng.Next(1000, 9999)}",
            width = Range(rng, 15f, 100f),
            depth = Range(rng, 15f, 100f),
            wallHeight = Range(rng, 8f, 45f),
            wallThickness = Range(rng, 0.4f, 2f),
            doorWidth = Range(rng, 2f, 8f),
            doorHeight = Range(rng, 2.5f, 10f),
            pillarCount = rng.Next(0, 9), // 0-8
        };
    }

    private static float Range(System.Random rng, float min, float max) => min + (float)rng.NextDouble() * (max - min);
}
