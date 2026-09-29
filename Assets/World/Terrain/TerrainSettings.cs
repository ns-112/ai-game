using System;
using UnityEngine;

// One terrain "look": how tall and how bumpy it gets, plus what gets
// scattered on it. This is one entry in a palette (TerrainSettings.biomes) —
// the palette is small and AI-generated, but individual cells across the
// (effectively infinite) world are assigned an entry from it algorithmically,
// so the same handful of looks get reused and interleaved everywhere instead
// of the world being limited to palette.Length regions total.
[Serializable]
public class BiomeSettings
{
    public string name = "Biome";
    public string description = "";
    public float maxHeight = 15f; // lower = flatter

    [Header("Scatter (trees/rocks/etc — assign your own prefabs)")]
    public GameObject[] props;
    public float propsPerSquareUnit = 0.002f;
    public float minScale = 0.85f;
    public float maxScale = 1.15f;

    [Header("Rare structures (procedural, per-instance — see RandomStructureGenerator)")]
    [Tooltip("Chance per loaded chunk that a large structure spawns while this biome is dominant there. Set by StructureAiGenerator at startup; tune its rarityWeight rather than this directly.")]
    public float structureRarity = 0.02f;

    // Runtime-generated ground texture (BiomeAiTextureGenerator fills this
    // in asynchronously). Not serialized: it's produced fresh each session,
    // never authored in the Inspector. Null until generation finishes —
    // TerrainChunk falls back to the shared material's placeholder texture
    // for that slot until then.
    [NonSerialized] public Texture2D groundTexture;
}

[Serializable]
public class TerrainSettings
{
    [Header("Shape noise")]
    public int seed = 0;
    public int octaves = 5;
    public float persistence = 0.5f;
    public float lacunarity = 2f;
    [Tooltip("Noise wavelength shared by every biome (bigger = broader, gentler hills). Kept global rather than per-biome: blending a DIFFERENT wavelength per biome at their shared edges is frequency modulation of the noise (shows up as wavy distortion), and resampling each biome independently to avoid that instead decorrelates the two fields at the seam (shows up as sudden walls). One shared wavelength sidesteps both — biomes still differ in height/props/texture, just not in bumpiness.")]
    public float baseScale = 250f;

    [Header("Biomes (infinite placement from this palette)")]
    [Tooltip("World size of one biome cell — bigger = larger, slower-changing regions.")]
    public float biomeScale = 600f;
    [Tooltip("1 = blend spans the whole cell boundary; higher = narrower transition band, more \"pure\" biome away from edges.")]
    public float biomeBlendSharpness = 1f;
    [Tooltip("How far (world units) terrain HEIGHT takes to ease from one biome's maxHeight to the next. Bigger = gentler slopes between flat and tall biomes. Capped at ~55% of biomeScale internally so the blend can never pop.")]
    public float heightBlendDistance = 200f;
    public BiomeSettings[] biomes =
    {
        new BiomeSettings { name = "Plains",    maxHeight = 8f   },
        new BiomeSettings { name = "Hills",     maxHeight = 45f  },
        new BiomeSettings { name = "Mountains", maxHeight = 140f },
    };

    // Which two biome CELLS a world position sits between, and how far along
    // (0 = fully cellA's biome, 1 = fully cellB's). The world is tiled into a
    // jittered grid of cells (one per biomeScale x biomeScale square); each
    // cell's biome is a palette entry picked by hashing its cell coordinates,
    // so the same cell always resolves to the same biome and there's no
    // limit to how far this extends in any direction — palette entries just
    // repeat and interleave across space. Shared by height, vertex-color,
    // and prop-scatter sampling so they all agree on where one biome ends
    // and the next begins.
    public void GetBiomeBlend(float worldX, float worldZ, out int i0, out int i1, out float t)
    {
        FindNearestCells(worldX, worldZ, out Vector2Int cellA, out float distA, out Vector2Int cellB, out float distB);

        i0 = ArchetypeForCell(cellA);
        i1 = ArchetypeForCell(cellB);

        // 0 at cellA's own point, ~0.5 at the boundary between the two
        // cells, 1 at cellB's point — a cheap stand-in for a proper Voronoi
        // edge distance that's smooth enough not to show seams.
        float totalDist = distA + distB;
        float raw = totalDist > 0f ? distA / totalDist : 0f;

        // Compress the transition around its midpoint: higher sharpness keeps
        // each biome "pure" for longer and squeezes the actual blending into
        // a narrower band, without changing how large the cells themselves are.
        t = Mathf.Clamp01((raw - 0.5f) * biomeBlendSharpness + 0.5f);
    }

    // Height at a world position, blended smoothly across whichever biomes
    // it sits between — no hard seam where one biome's terrain meets the next.
    //
    // Every biome shares the same noise wavelength (baseScale above), so
    // this is a SINGLE noise evaluation with only the amplitude (maxHeight)
    // varying by position — a plain scalar multiply on a continuous
    // function, which is smooth by construction. Two earlier approaches both
    // had artifacts: blending a different wavelength per biome before
    // sampling once is frequency modulation of the noise (wavy distortion at
    // the blend), and sampling each biome independently at ITS OWN
    // wavelength and blending the results decorrelates the two fields right
    // at the seam (sudden walls, since the two samples' peaks/troughs don't
    // line up). A shared wavelength sidesteps both.
    //
    // The amplitude itself comes from BlendedMaxHeight, NOT GetBiomeBlend's
    // two-cell lerp. That lerp only ever considers the nearest and second-
    // nearest cells, and near a spot where three cells meet the "second
    // nearest" flips from one cell to another while t is still well above
    // 0 — so maxHeight jumped straight from e.g. 5 to 55 between two
    // adjacent vertices. Those jumps were the near-vertical walls; the noise
    // on its own never exceeds ~10 degrees.
    public float SampleHeight(float worldX, float worldZ)
    {
        float shape = NoiseGenerator.Sample(worldX, worldZ, seed, octaves, baseScale, persistence, lacunarity);
        return shape * BlendedMaxHeight(worldX, worldZ);
    }

    // Weighted average of every nearby cell's maxHeight, each weighted by how
    // much farther it is than the nearest cell: the nearest gets full weight,
    // and a cell's weight eases to exactly zero once it's heightBlendDistance
    // farther than that. Every weight is a continuous function of position,
    // so the result is too — no cell can ever pop in or out of the blend.
    //
    // Searches a 5x5 ring (not 3x3 like FindNearestCells): a cell outside
    // that ring is at least 2*biomeScale away while the nearest is at most
    // ~1.41*biomeScale away, so capping the blend distance below the
    // difference (~0.59*biomeScale) guarantees any cell outside the search
    // already has zero weight and leaving the search window can't cause a jump.
    private float BlendedMaxHeight(float worldX, float worldZ)
    {
        int baseCx = Mathf.FloorToInt(worldX / biomeScale);
        int baseCz = Mathf.FloorToInt(worldZ / biomeScale);
        float blend = Mathf.Clamp(heightBlendDistance, 1f, biomeScale * 0.55f);

        const int Ring = 2, Side = Ring * 2 + 1;
        Span<float> dists = stackalloc float[Side * Side];
        Span<float> heights = stackalloc float[Side * Side];

        float nearest = float.MaxValue;
        int n = 0;
        for (int dz = -Ring; dz <= Ring; dz++)
        {
            for (int dx = -Ring; dx <= Ring; dx++)
            {
                var cell = new Vector2Int(baseCx + dx, baseCz + dz);
                Vector2 point = CellPoint(cell);
                float ddx = worldX - point.x;
                float ddz = worldZ - point.y;
                float dist = Mathf.Sqrt(ddx * ddx + ddz * ddz);
                dists[n] = dist;
                heights[n] = biomes[ArchetypeForCell(cell)].maxHeight;
                if (dist < nearest) nearest = dist;
                n++;
            }
        }

        float weightedSum = 0f, weightTotal = 0f;
        for (int i = 0; i < n; i++)
        {
            float s = Mathf.Clamp01(1f - (dists[i] - nearest) / blend);
            if (s <= 0f) continue;
            float w = s * s * (3f - 2f * s); // smoothstep — no kink where a cell's weight reaches zero
            weightedSum += w * heights[i];
            weightTotal += w;
        }
        return weightedSum / weightTotal; // nearest cell always has weight 1, so never divides by zero
    }

    // Whichever single biome most strongly owns this point — used to pick
    // which biome's props/texture dominates (as opposed to the smooth blend
    // SampleHeight uses).
    public int DominantBiome(float worldX, float worldZ)
    {
        GetBiomeBlend(worldX, worldZ, out int i0, out int i1, out float t);
        return t < 0.5f ? i0 : i1;
    }

    // Searches the 3x3 ring of cells around the point's own cell for the two
    // nearest cell centers. Each cell's center is jittered but always stays
    // within that cell (jitter is in [0,1) of biomeScale), so the true
    // nearest and second-nearest centers are always found in this ring.
    private void FindNearestCells(float worldX, float worldZ, out Vector2Int cellA, out float distA, out Vector2Int cellB, out float distB)
    {
        int baseCx = Mathf.FloorToInt(worldX / biomeScale);
        int baseCz = Mathf.FloorToInt(worldZ / biomeScale);

        cellA = new Vector2Int(baseCx, baseCz);
        cellB = cellA;
        distA = float.MaxValue;
        distB = float.MaxValue;

        for (int dz = -1; dz <= 1; dz++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                var cell = new Vector2Int(baseCx + dx, baseCz + dz);
                Vector2 point = CellPoint(cell);
                float dx2 = worldX - point.x;
                float dz2 = worldZ - point.y;
                float dist = Mathf.Sqrt(dx2 * dx2 + dz2 * dz2);

                if (dist < distA)
                {
                    cellB = cellA; distB = distA;
                    cellA = cell; distA = dist;
                }
                else if (dist < distB)
                {
                    cellB = cell; distB = dist;
                }
            }
        }
    }

    // Deterministic jittered center for a cell — same cell coordinates
    // always yield the same point.
    private Vector2 CellPoint(Vector2Int cell)
    {
        uint h = Hash(cell.x, cell.y, seed);
        float jx = (h & 0xFFFF) / 65536f;
        float jz = ((h >> 16) & 0xFFFF) / 65536f;
        return new Vector2((cell.x + jx) * biomeScale, (cell.y + jz) * biomeScale);
    }

    // Which palette entry a given cell uses — a different hash stream than
    // CellPoint's so jitter and biome choice don't correlate.
    private int ArchetypeForCell(Vector2Int cell)
    {
        uint h = Hash(cell.x * 2 + 1, cell.y * 2 + 1, seed + 40503);
        return (int)(h % (uint)biomes.Length);
    }

    // Cheap deterministic integer hash — good enough for spatial variety,
    // not cryptographic. `unchecked` because we want the wraparound, not an
    // OverflowException, and it stays fully deterministic either way.
    private static uint Hash(int x, int z, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + z * 668265263 + seed * 2147483647);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return h;
        }
    }
}
