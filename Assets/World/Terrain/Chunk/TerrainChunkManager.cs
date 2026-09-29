using System;
using System.Collections.Generic;
using UnityEngine;

// Streams TerrainChunk tiles in/out around the player, pooling them so
// moving through the world doesn't constantly Instantiate/Destroy.
public class TerrainChunkManager : MonoBehaviour
{
    [Header("References")]
    public Transform player;
    public GameObject chunkPrefab; // a prefab with TerrainChunk + a Material assigned on its MeshRenderer

    [Header("Chunking")]
    public float chunkSize = 100f;
    public int viewDistanceInChunks = 3;
    public int resolution = 20; // quads per side — lower = more visibly low-poly

    [Header("Debug grid texture (used until Biome Material is assigned)")]
    public bool useGridTexture = true;
    public float gridWorldTileSize = 10f; // world units per texture repeat

    [Header("Biome ground material (Custom/BiomeBlend shader — overrides grid texture)")]
    public Material biomeMaterial;

    [Header("Terrain shape / biomes")]
    public bool randomizeSeedOnStart = true;
    public TerrainSettings terrainSettings = new();

    [Header("Player spawn")]
    public float spawnHeightOffset = 1f; // extra clearance above the ground on spawn

    private readonly Dictionary<Vector2Int, TerrainChunk> activeChunks = new();
    private readonly Queue<TerrainChunk> pool = new();
    private Vector2Int currentPlayerChunk;
    private Material gridMaterial;

    // Fired right after a chunk at this coordinate is (re)built — used by
    // systems like NpcAiGenerator that need to spawn something once per
    // world location the first time it streams in, but must NOT be parented
    // under the chunk itself (unlike ScatterProps' props, which are fine
    // being destroyed/regenerated whenever the chunk's pooled GameObject
    // gets recycled for a different coordinate — see NpcAiGenerator's own
    // comment for why that destroy/regenerate behavior is wrong for NPCs).
    public event Action<Vector2Int> OnChunkBuilt;

    // Read-only view of which chunk coordinates are currently streamed in —
    // lets a late-arriving system (e.g. NPC sprites finishing generation
    // after nearby chunks already loaded) catch up on ones it missed via
    // OnChunkBuilt instead of only ever seeing future chunks.
    public IEnumerable<Vector2Int> ActiveChunkCoords => activeChunks.Keys;

    private void Start()
    {
        if (randomizeSeedOnStart)
            terrainSettings.seed = UnityEngine.Random.Range(0, 1000000); // NoiseGenerator now bounds this safely regardless, but no reason to hand it extreme values either

        if (useGridTexture)
        {
            gridMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"))
            {
                mainTexture = GridTexture.Create(),
                mainTextureScale = new Vector2(chunkSize / gridWorldTileSize, chunkSize / gridWorldTileSize)
            };
        }

        currentPlayerChunk = new Vector2Int(int.MinValue, int.MinValue); // force initial load
        UpdateChunks();
        SpawnPlayerOnGround();
    }

    // Terrain chunks live on this layer specifically so ground-height
    // queries can ignore trees/rocks/buildings/structures sitting on top of
    // the terrain — without this, a raycast could hit the roof of a large
    // generated structure instead of the actual ground beneath it. Computed
    // lazily (not as a static field initializer) because LayerMask.GetMask
    // isn't allowed to run before Unity's engine is initialized.
    private static int? terrainLayerMask;
    private static int TerrainLayerMask => terrainLayerMask ??= LayerMask.GetMask("Terrain");

    // The real ground height at any world (x,z) — for spawning items, NPCs,
    // props, etc. Prefers a physics raycast onto the actual collider (which
    // is a discretized mesh and can disagree with the smooth noise function
    // on steep terrain); falls back to the analytic value only if there's no
    // collider there yet (e.g. that chunk hasn't loaded).
    public float GetGroundHeight(float worldX, float worldZ)
    {
        float approxHeight = terrainSettings.SampleHeight(worldX, worldZ);
        Vector3 rayOrigin = new Vector3(worldX, approxHeight + 200f, worldZ);
        return Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, 1000f, TerrainLayerMask)
            ? hit.point.y
            : approxHeight;
    }

    // Convenience wrapper returning a full spawn-ready position.
    public Vector3 GetGroundPosition(float worldX, float worldZ, float heightOffset = 0f)
    {
        return new Vector3(worldX, GetGroundHeight(worldX, worldZ) + heightOffset, worldZ);
    }

    // Public so it can be called again later (e.g. by WorldLoadingGate once
    // AI-generated biomes have reshaped the terrain) — safe to call anytime,
    // not just on startup.
    public void SpawnPlayerOnGround()
    {
        if (player == null) return;

        // A CharacterController fights direct position changes while enabled,
        // so disable it for the teleport and restore it after.
        CharacterController controller = player.GetComponent<CharacterController>();
        bool wasEnabled = controller != null && controller.enabled;
        if (controller != null) controller.enabled = false;

        float groundHeight = GetGroundHeight(player.position.x, player.position.z);

        Vector3 pos = player.position;
        pos.y = groundHeight + spawnHeightOffset;
        player.position = pos;

        if (controller != null) controller.enabled = wasEnabled;
    }

    private void Update()
    {
        Vector2Int playerChunk = WorldToChunk(player.position);
        if (playerChunk != currentPlayerChunk)
        {
            currentPlayerChunk = playerChunk;
            UpdateChunks();
        }
    }

    private Vector2Int WorldToChunk(Vector3 worldPos)
    {
        return new Vector2Int(Mathf.FloorToInt(worldPos.x / chunkSize), Mathf.FloorToInt(worldPos.z / chunkSize));
    }

    // Rebuilds every chunk currently on screen against the current
    // terrainSettings — call this after changing terrainSettings.biomes (or
    // any other shape parameter) at runtime, e.g. once AI-generated biome
    // data arrives, so already-streamed-in chunks pick up the change instead
    // of only new ones.
    public void RegenerateAllChunks()
    {
        foreach (var kvp in activeChunks)
            kvp.Value.Build(kvp.Key, resolution, chunkSize, terrainSettings);
    }

    // Re-scatters props on every streamed-in chunk WITHOUT touching mesh or
    // collider — call this after a biome's prop list changes (a new AI
    // tree/rock/building/structure template got appended) so it starts
    // showing up on chunks that already exist, not just new ones.
    //
    // This deliberately does not go through Build()/RegenerateAllChunks():
    // that replaces each chunk's Mesh and MeshCollider.sharedMesh, which is
    // necessary when the terrain's actual SHAPE changes (new biome heights)
    // but is otherwise just an unnecessary collider swap — and doing that
    // repeatedly while the player is standing on the chunk (which happens
    // now that prop generation runs in the background after the player
    // already has control, not just while frozen) risks a frame where the
    // CharacterController loses contact with the old collider before the
    // new one is settled, i.e. falling through the floor.
    public void RefreshAllProps()
    {
        foreach (var kvp in activeChunks)
            kvp.Value.RefreshProps(kvp.Key, chunkSize, terrainSettings);
    }

    // Re-binds ground textures on every streamed-in chunk without rebuilding
    // their meshes — call this whenever a biome's texture finishes
    // generating (BiomeAiTextureGenerator does, per biome) so chunks that
    // already exist pick up the new texture (and any blending against it)
    // immediately instead of only chunks built from then on.
    public void RefreshBiomeTextures()
    {
        foreach (var kvp in activeChunks)
            kvp.Value.RefreshBiomeTextures(terrainSettings);
    }

    private void UpdateChunks()
    {
        var needed = new HashSet<Vector2Int>();
        for (int dz = -viewDistanceInChunks; dz <= viewDistanceInChunks; dz++)
            for (int dx = -viewDistanceInChunks; dx <= viewDistanceInChunks; dx++)
                needed.Add(currentPlayerChunk + new Vector2Int(dx, dz));

        var toRemove = new List<Vector2Int>();
        foreach (var coord in activeChunks.Keys)
            if (!needed.Contains(coord)) toRemove.Add(coord);

        foreach (var coord in toRemove)
        {
            TerrainChunk chunk = activeChunks[coord];
            activeChunks.Remove(coord);
            chunk.gameObject.SetActive(false);
            pool.Enqueue(chunk);
        }

        foreach (var coord in needed)
        {
            if (activeChunks.ContainsKey(coord)) continue;

            // An uncaught exception anywhere in here (Instantiate/Awake on
            // the prefab, Build, a subscriber of OnChunkBuilt) would
            // otherwise abort this ENTIRE foreach — every coordinate after
            // the failing one silently never gets built, which for the
            // very first call (from Start(), before any chunk exists) can
            // mean most or all of the initial world never appears at all,
            // and Start() never reaches SpawnPlayerOnGround() either. One
            // bad chunk should only cost that one chunk (retried whenever
            // it's next requested), never the whole world.
            try
            {
                bool isNew = pool.Count == 0;
                TerrainChunk chunk = isNew
                    ? Instantiate(chunkPrefab, transform).GetComponent<TerrainChunk>()
                    : pool.Dequeue();

                if (isNew)
                {
                    Material material = biomeMaterial != null ? biomeMaterial : gridMaterial;
                    if (material != null) chunk.GetComponent<MeshRenderer>().sharedMaterial = material;
                }

                chunk.gameObject.SetActive(true);
                chunk.Build(coord, resolution, chunkSize, terrainSettings);
                activeChunks[coord] = chunk;
                OnChunkBuilt?.Invoke(coord);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Terrain] Failed to build chunk at {coord} — {e.Message}\n{e.StackTrace}");
            }
        }
    }
}
