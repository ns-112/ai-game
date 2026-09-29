using System;
using System.Collections.Generic;
using UnityEngine;

// One square tile of ground, built as a flat-shaded (low-poly) mesh: every
// triangle gets its own unshared vertices so RecalculateNormals produces
// hard, faceted faces instead of a smooth surface. Also scatters biome props
// (trees, rocks, ...) and writes per-vertex biome blend weights into vertex
// colors for a ground-texture-blending shader to read.
//
// The biome palette can be bigger than the shader's 3 texture slots (it's
// meant to be — see TerrainSettings), so a chunk can't just use a biome's
// palette index as its shader channel directly. Instead each chunk figures
// out which (up to 3) palette entries actually matter for ITS patch of
// ground, remaps just those to local slots 0/1/2, and binds their textures
// via a MaterialPropertyBlock — every chunk can point the same 3 shader
// slots at different textures without needing separate Material instances.
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
public class TerrainChunk : MonoBehaviour
{
    private static readonly int[] TextureIds =
    {
        Shader.PropertyToID("_Texture0"),
        Shader.PropertyToID("_Texture1"),
        Shader.PropertyToID("_Texture2"),
    };

    private Mesh mesh;
    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private MeshCollider meshCollider;
    private readonly List<GameObject> spawnedProps = new();
    private MaterialPropertyBlock propertyBlock;

    // This chunk's local slot -> palette index mapping from its last Build(),
    // so RefreshBiomeTextures can re-bind textures later (once more of them
    // finish generating) without rebuilding the mesh.
    private int[] slotArchetypes;

    private void Awake()
    {
        meshFilter = GetComponent<MeshFilter>();
        meshRenderer = GetComponent<MeshRenderer>();
        meshCollider = GetComponent<MeshCollider>();
        propertyBlock = new MaterialPropertyBlock();

        // This mesh deliberately duplicates a vertex per triangle corner
        // (for flat/faceted shading), which is exactly the pattern that
        // makes PhysX's mesh-cleaning/vertex-welding cooking step choke and
        // log "cleaning the mesh failed". Our geometry is already
        // well-formed, so skip cleaning instead of fighting it.
        meshCollider.cookingOptions &= ~(MeshColliderCookingOptions.EnableMeshCleaning
                                        | MeshColliderCookingOptions.WeldColocatedVertices);
    }

    public void Build(Vector2Int chunkCoord, int resolution, float chunkSize, TerrainSettings settings)
    {
        float worldOriginX = chunkCoord.x * chunkSize;
        float worldOriginZ = chunkCoord.y * chunkSize;

        int verticesPerSide = resolution + 1;
        int pointCount = verticesPerSide * verticesPerSide;
        Vector3[] gridPoints = new Vector3[pointCount];
        int[] gridI0 = new int[pointCount];
        int[] gridI1 = new int[pointCount];
        float[] gridT = new float[pointCount];

        // How much each palette entry matters across this whole chunk (summed
        // blend weight over every sample point) — used below to pick which
        // (up to 3) entries this chunk actually binds textures for.
        var archetypeWeight = new Dictionary<int, float>();

        for (int z = 0; z < verticesPerSide; z++)
        {
            for (int x = 0; x < verticesPerSide; x++)
            {
                float worldX = worldOriginX + (x / (float)resolution) * chunkSize;
                float worldZ = worldOriginZ + (z / (float)resolution) * chunkSize;
                float height = settings.SampleHeight(worldX, worldZ);
                settings.GetBiomeBlend(worldX, worldZ, out int i0, out int i1, out float t);

                int idx = z * verticesPerSide + x;
                gridPoints[idx] = new Vector3(worldX - worldOriginX, height, worldZ - worldOriginZ);
                gridI0[idx] = i0;
                gridI1[idx] = i1;
                gridT[idx] = t;

                Accumulate(archetypeWeight, i0, 1f - t);
                Accumulate(archetypeWeight, i1, t);
            }
        }

        slotArchetypes = PickDominantSlots(archetypeWeight);

        Color[] gridColors = new Color[pointCount];
        for (int i = 0; i < pointCount; i++)
            gridColors[i] = SlotColor(slotArchetypes, gridI0[i], gridI1[i], gridT[i]);

        int quadCount = resolution * resolution;
        Vector3[] vertices = new Vector3[quadCount * 6];
        Vector2[] uvs = new Vector2[quadCount * 6];
        Color[] colors = new Color[quadCount * 6];
        int[] triangles = new int[quadCount * 6];
        int vi = 0;

        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                int iA = z * verticesPerSide + x;
                int iB = z * verticesPerSide + x + 1;
                int iC = (z + 1) * verticesPerSide + x;
                int iD = (z + 1) * verticesPerSide + x + 1;

                Vector3 a = gridPoints[iA], b = gridPoints[iB], c = gridPoints[iC], d = gridPoints[iD];
                Color colA = gridColors[iA], colB = gridColors[iB], colC = gridColors[iC], colD = gridColors[iD];

                // UV is the point's fraction across the chunk (0..1) — only
                // used by the debug grid material's tiling now. The biome
                // ground-texture shader (BiomeBlend) derives its own UV from
                // world position instead, so its tiling stays continuous
                // across chunk borders rather than resetting to 0 at each one.
                Vector2 uvA = new Vector2(x / (float)resolution, z / (float)resolution);
                Vector2 uvB = new Vector2((x + 1) / (float)resolution, z / (float)resolution);
                Vector2 uvC = new Vector2(x / (float)resolution, (z + 1) / (float)resolution);
                Vector2 uvD = new Vector2((x + 1) / (float)resolution, (z + 1) / (float)resolution);

                // If the terrain looks inverted/dark from above, swap b and c
                // (and their uv/color) in both triangles below (winding got flipped).
                vertices[vi] = a; uvs[vi] = uvA; colors[vi] = colA; triangles[vi] = vi; vi++;
                vertices[vi] = c; uvs[vi] = uvC; colors[vi] = colC; triangles[vi] = vi; vi++;
                vertices[vi] = b; uvs[vi] = uvB; colors[vi] = colB; triangles[vi] = vi; vi++;

                vertices[vi] = b; uvs[vi] = uvB; colors[vi] = colB; triangles[vi] = vi; vi++;
                vertices[vi] = c; uvs[vi] = uvC; colors[vi] = colC; triangles[vi] = vi; vi++;
                vertices[vi] = d; uvs[vi] = uvD; colors[vi] = colD; triangles[vi] = vi; vi++;
            }
        }

        var newMesh = new Mesh { name = "Chunk" };
        newMesh.vertices = vertices;
        newMesh.uv = uvs;
        newMesh.colors = colors;
        newMesh.triangles = triangles;
        newMesh.RecalculateNormals();
        newMesh.RecalculateBounds();

        // Swap to a genuinely different Mesh object rather than mutating the
        // old one in place: MeshCollider only reliably re-cooks collision
        // when it sees a different sharedMesh reference, and reassigning the
        // SAME reference after clearing it needs a null step in between —
        // which leaves a frame with zero collision on this chunk. A fresh
        // object goes straight from "old valid mesh" to "new valid mesh"
        // with no gap for the player to fall through.
        Mesh oldMesh = mesh;
        mesh = newMesh;
        meshFilter.sharedMesh = newMesh;
        meshCollider.sharedMesh = newMesh;
        if (oldMesh != null) Destroy(oldMesh);

        transform.position = new Vector3(worldOriginX, 0f, worldOriginZ);

        ApplyBiomeTextures(settings);

        ClearProps();
        ScatterProps(worldOriginX, worldOriginZ, chunkCoord, chunkSize, settings);
        ScatterStructures(worldOriginX, worldOriginZ, chunkCoord, chunkSize, settings);
    }

    // Re-scatters props without touching the mesh or collider — see
    // TerrainChunkManager.RefreshAllProps for why this stays separate from
    // Build() (that one swaps the MeshCollider, which is only safe to do
    // for an actual shape change, not just a new prop template existing).
    public void RefreshProps(Vector2Int chunkCoord, float chunkSize, TerrainSettings settings)
    {
        float worldOriginX = chunkCoord.x * chunkSize;
        float worldOriginZ = chunkCoord.y * chunkSize;
        ClearProps();
        ScatterProps(worldOriginX, worldOriginZ, chunkCoord, chunkSize, settings);
    }

    // Re-binds this chunk's texture slots without touching the mesh —
    // called when a biome's ground texture finishes generating after this
    // chunk was already built, so newly-arrived textures (and any blending
    // between them) show up immediately instead of only on chunks built
    // afterward.
    public void RefreshBiomeTextures(TerrainSettings settings)
    {
        if (slotArchetypes == null) return;
        ApplyBiomeTextures(settings);
    }

    private void ApplyBiomeTextures(TerrainSettings settings)
    {
        propertyBlock.Clear();
        for (int slot = 0; slot < slotArchetypes.Length; slot++)
        {
            Texture2D tex = settings.biomes[slotArchetypes[slot]].groundTexture;
            if (tex != null) propertyBlock.SetTexture(TextureIds[slot], tex);
            // Leave unset otherwise — the shared material's placeholder
            // texture shows through until this biome's texture arrives.
        }
        meshRenderer.SetPropertyBlock(propertyBlock);
    }

    private static void Accumulate(Dictionary<int, float> weights, int key, float amount)
    {
        weights.TryGetValue(key, out float existing);
        weights[key] = existing + amount;
    }

    // Picks up to 3 palette entries that dominate this chunk's vertices, by
    // total blend weight — almost always 1-2 distinct entries for a chunk
    // much smaller than a biome cell, occasionally 3 near a cell corner. Any
    // 4th+ entry (rare, only when biomeScale is small relative to chunk
    // size) gets folded into its nearest chosen slot when writing vertex
    // colors rather than getting its own shader slot.
    private static int[] PickDominantSlots(Dictionary<int, float> weights)
    {
        var ordered = new List<int>(weights.Keys);
        ordered.Sort((a, b) => weights[b].CompareTo(weights[a]));

        var slots = new int[3];
        for (int i = 0; i < 3; i++)
            slots[i] = i < ordered.Count ? ordered[i] : ordered[0];
        return slots;
    }

    private static Color SlotColor(int[] slotArchetypes, int i0, int i1, float t)
    {
        Color color = Color.clear;
        AddToSlot(ref color, slotArchetypes, i0, 1f - t);
        AddToSlot(ref color, slotArchetypes, i1, t);
        return color;
    }

    private static void AddToSlot(ref Color color, int[] slotArchetypes, int archetype, float weight)
    {
        for (int s = 0; s < slotArchetypes.Length; s++)
        {
            if (slotArchetypes[s] != archetype) continue;
            color[s] += weight;
            return;
        }
        // Not one of this chunk's chosen slots (only possible when a chunk
        // touches more than 3 palette entries) — fold into slot 0 instead of
        // silently dropping the weight (which would show as unlit/black).
        color[0] += weight;
    }

    private void ClearProps()
    {
        foreach (GameObject prop in spawnedProps)
            if (prop != null) Destroy(prop);
        spawnedProps.Clear();
    }

    private void ScatterProps(float worldOriginX, float worldOriginZ, Vector2Int chunkCoord, float chunkSize, TerrainSettings settings)
    {
        float maxDensity = 0f;
        foreach (BiomeSettings biome in settings.biomes)
            maxDensity = Mathf.Max(maxDensity, biome.propsPerSquareUnit);
        if (maxDensity <= 0f) return;

        // Sample on a jittered grid fine enough for the densest biome; sparser
        // biomes just roll "no prop" more often per cell (see below).
        float cellSize = Mathf.Sqrt(1f / maxDensity);
        int cells = Mathf.Clamp(Mathf.FloorToInt(chunkSize / cellSize), 1, 200);
        float actualCellSize = chunkSize / cells;

        // Deterministic per-chunk seed so revisiting a chunk regenerates the
        // same prop placement instead of re-rolling every time it streams in.
        var rng = new System.Random(chunkCoord.x * 92821 ^ chunkCoord.y * 68917 ^ (settings.seed * 12007 + 1));

        for (int cz = 0; cz < cells; cz++)
        {
            for (int cx = 0; cx < cells; cx++)
            {
                float localX = (cx + (float)rng.NextDouble()) * actualCellSize;
                float localZ = (cz + (float)rng.NextDouble()) * actualCellSize;
                float worldX = worldOriginX + localX;
                float worldZ = worldOriginZ + localZ;

                BiomeSettings biome = settings.biomes[settings.DominantBiome(worldX, worldZ)];
                if (biome.props == null || biome.props.Length == 0) continue;

                float cellArea = actualCellSize * actualCellSize;
                if (rng.NextDouble() > Mathf.Clamp01(biome.propsPerSquareUnit * cellArea)) continue;

                GameObject prefab = PickWeighted(biome.props, rng);
                float height = settings.SampleHeight(worldX, worldZ);

                GameObject instance = Instantiate(prefab, transform);
                instance.SetActive(true); // Instantiate copies the source's active state — generated prop templates are kept inactive, so this must be forced on
                instance.transform.localPosition = new Vector3(localX, height, localZ);
                instance.transform.localRotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                instance.transform.localScale = Vector3.one * Mathf.Lerp(biome.minScale, biome.maxScale, (float)rng.NextDouble());

                spawnedProps.Add(instance);
            }
        }
    }

    // Fired right after a rare landmark structure finishes spawning, with
    // its final world position — lets an unrelated system (NpcAiGenerator)
    // cluster a few NPCs near it without TerrainChunk needing to know
    // anything about NPCs, and without NpcAiGenerator needing to duplicate
    // this method's own rarity roll/RNG stream to guess where structures
    // will land.
    public static event Action<Vector3> OnStructureSpawned;

    // Rare landmark structures don't use the shared-template pool above —
    // each one is built fresh, right here, from a randomly rolled
    // StructureDefinition (see RandomStructureGenerator), so two structures
    // never end up as identical clones the way two trees from the same
    // variant would. One roll per chunk (structures are large and rare, so a
    // dense per-cell grid like ScatterProps' would be wasted work) using a
    // separate RNG stream (different salt) so this roll doesn't correlate
    // with ScatterProps' "did a regular prop spawn" roll in the same chunk.
    private void ScatterStructures(float worldOriginX, float worldOriginZ, Vector2Int chunkCoord, float chunkSize, TerrainSettings settings)
    {
        var rng = new System.Random(chunkCoord.x * 50331653 ^ chunkCoord.y * 17627 ^ (settings.seed * 30011 + 7));

        float localX = (float)rng.NextDouble() * chunkSize;
        float localZ = (float)rng.NextDouble() * chunkSize;
        float worldX = worldOriginX + localX;
        float worldZ = worldOriginZ + localZ;

        BiomeSettings biome = settings.biomes[settings.DominantBiome(worldX, worldZ)];
        if (rng.NextDouble() > biome.structureRarity) return;

        StructureDefinition def = RandomStructureGenerator.RandomDefinition(rng, biome);
        float rotationY = (float)rng.NextDouble() * 360f;

        Mesh mesh;
        try { mesh = ProceduralStructureBuilder.Build(def); }
        catch (Exception e)
        {
            Debug.LogWarning($"[Structures] Failed to build \"{def.name}\" — {e.Message}");
            return;
        }

        var walls = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = RandomStoneColor(rng) };
        var roof = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = RandomStoneColor(rng) };

        var instance = new GameObject($"Structure_{def.name}");
        instance.layer = LayerMask.NameToLayer("World"); // so the player's GroundedCheck registers standing on top of it
        instance.transform.SetParent(transform);

        var filter = instance.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;

        var renderer = instance.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = new[] { walls, roof };

        ProceduralMeshUtils.AddMeshCollider(instance, mesh);

        // Structures can be up to 100 units wide/deep — comparable to or
        // bigger than the terrain's own noise wavelength (TerrainSettings.
        // baseScale) — so anchoring the flat-bottomed mesh to the height
        // sampled at just its center point left it floating over dips (or clipping
        // through rises) across the rest of its rotated footprint. Anchoring
        // to the LOWEST point actually under the footprint means any higher
        // ground nearby clips into the walls instead — reads as "built into
        // the hillside" rather than hovering in midair.
        float height = FootprintGroundHeight(worldX, worldZ, rotationY, def.width * 0.5f, def.depth * 0.5f, settings);
        instance.transform.localPosition = new Vector3(localX, height, localZ);
        instance.transform.localRotation = Quaternion.Euler(0f, rotationY, 0f);

        spawnedProps.Add(instance); // ClearProps() at the top of Build() cleans this up on rebuild/pooling same as any other prop

        OnStructureSpawned?.Invoke(instance.transform.position);

        Debug.Log($"[Structures] Built \"{def.name}\" for biome \"{biome.name}\".");
    }

    private static readonly Vector2[] FootprintCorners =
    {
        new(-1, -1), new(1, -1), new(-1, 1), new(1, 1), Vector2.zero,
    };

    // Lowest ground height under a rectangular footprint of half-extents
    // (hw, hd) centered at (worldX, worldZ) and rotated rotationYDeg around
    // Y — samples the corners plus center rather than just the center point.
    private static float FootprintGroundHeight(float worldX, float worldZ, float rotationYDeg, float hw, float hd, TerrainSettings settings)
    {
        Quaternion rot = Quaternion.Euler(0f, rotationYDeg, 0f);
        float minHeight = float.MaxValue;
        foreach (Vector2 corner in FootprintCorners)
        {
            Vector3 offset = rot * new Vector3(corner.x * hw, 0f, corner.y * hd);
            float h = settings.SampleHeight(worldX + offset.x, worldZ + offset.z);
            if (h < minHeight) minHeight = h;
        }
        return minHeight;
    }

    private static Color RandomStoneColor(System.Random rng)
    {
        float g = 0.35f + (float)rng.NextDouble() * 0.35f; // weathered stone greys/browns
        return new Color(g, g * 0.96f, g * 0.9f);
    }

    // Uniform if none of the candidates have a PropWeight (the old
    // behavior); otherwise favors higher-weight entries, so e.g. a rare
    // building can share a biome's prop list with common trees/rocks.
    private static GameObject PickWeighted(GameObject[] props, System.Random rng)
    {
        float total = 0f;
        foreach (GameObject prop in props)
            total += prop.TryGetComponent(out PropWeight pw) ? pw.weight : 1f;

        float roll = (float)rng.NextDouble() * total;
        float cumulative = 0f;
        foreach (GameObject prop in props)
        {
            cumulative += prop.TryGetComponent(out PropWeight pw) ? pw.weight : 1f;
            if (roll <= cumulative) return prop;
        }
        return props[props.Length - 1];
    }
}
