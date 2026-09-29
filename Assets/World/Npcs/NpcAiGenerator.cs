using System;
using System.Collections.Generic;
using UnityEngine;

// Generates a small set of billboard-sprite NPC "looks" (one or more per
// biome, via HuggingFaceImageClient — same service used for ground/bark/
// leaf textures elsewhere) and then spawns instances of them across the
// world as chunks stream in.
//
// Unlike trees/rocks/buildings/structures, NPC INSTANCES are deliberately
// NOT added to a biome's props array and NOT scattered by
// TerrainChunk.ScatterProps. That pipeline parents props to the chunk and
// destroys/regenerates them the moment the chunk's underlying GameObject
// gets recycled for a different coordinate (see TerrainChunkManager's
// pooling in UpdateChunks) — harmless for static scenery that respawns
// identically from a seed, but wrong for NPCs: they wander away from their
// spawn chunk, and getting silently destroyed whenever that original chunk
// object is recycled elsewhere would mean they don't actually persist.
// Instead this generator listens for TerrainChunkManager.OnChunkBuilt and
// spawns NPCs itself, parented to this object (never touched by chunk
// streaming), tracking which chunk coordinates have already been rolled so
// revisiting one doesn't duplicate its NPC.
public class NpcAiGenerator : MonoBehaviour, IGenerationStage
{
    public event Action OnFinished;

    public TerrainChunkManager terrainChunkManager;

    [Range(1, 3)]
    public int variantsPerBiome = 1;

    [Range(0f, 1f)]
    [Tooltip("Chance a newly streamed-in chunk spawns an NPC group (see groupMinSize/groupMaxSize below).")]
    public float spawnChancePerChunk = 0.15f;

    [Tooltip("How far a lone (non-grouped) NPC wanders from its spawn point.")]
    public float wanderRadius = 20f;

    [Header("Grouping (so NPCs actually meet each other)")]
    [Tooltip("A single lone NPC placed independently per chunk (chunks are 100 units apart) is far outside NpcSocialize's few-unit interaction range, so two independently-scattered NPCs essentially never crossed paths. Whenever a chunk rolls a spawn at all, it now spawns a small tight-knit group instead of just one — this is that group's size range (1 still happens sometimes, for variety).")]
    public int groupMinSize = 1;
    public int groupMaxSize = 3;
    [Tooltip("Wander radius used for grouped NPCs (kept small so members' wander circles keep overlapping instead of drifting apart) — lone spawns still use wanderRadius above.")]
    public float groupWanderRadius = 8f;

    [Header("Extra clusters near structures")]
    [Tooltip("On top of the general grouping above, every rare landmark structure that spawns rolls this chance to also get its own small NPC camp nearby — thematic flavor for landmarks, independent of spawnChancePerChunk.")]
    [Range(0f, 1f)]
    public float structureClusterChance = 0.6f;
    public int structureClusterMinSize = 2;
    public int structureClusterMaxSize = 4;

    private static readonly string[] RoleAdjectives =
    {
        "a wandering merchant", "a weathered traveler", "a local villager", "a wary scout",
        "a hooded stranger", "a cheerful farmer", "a retired adventurer", "a lost pilgrim",
    };

    // Indexed by biome index — each entry is that biome's rolled variant templates.
    private List<GameObject>[] templatesByBiome;
    private bool templatesReady;
    private int pendingTextures;
    private readonly HashSet<Vector2Int> spawnedChunks = new();

    public void GenerateFromBiomes(BiomeSettings[] biomes)
    {
        if (terrainChunkManager == null)
        {
            Debug.LogWarning("[NPCs] No TerrainChunkManager assigned — skipping.");
            OnFinished?.Invoke();
            return;
        }

        templatesByBiome = new List<GameObject>[biomes.Length];

        var defs = new (int biomeIndex, string name, string role, string prompt)[biomes.Length * variantsPerBiome];
        int count = 0;
        for (int b = 0; b < biomes.Length; b++)
        {
            templatesByBiome[b] = new List<GameObject>();
            // Seeded from the biome's NAME rather than the world seed
            // (unlike trees/rocks/buildings, which don't need cross-session
            // stability since they're never re-fetched from a network
            // cache). The world seed usually changes every session
            // (randomizeSeedOnStart), so seeding from it would roll a
            // different "role" — and therefore a different sprite prompt —
            // every time, meaning HuggingFaceImageClient's own on-disk
            // prompt cache (keyed by exact prompt text) could never hit
            // across sessions even though it's already there. A stable
            // biome-name hash means the same biome always rolls the same
            // role/prompt, so previously-generated NPC sprites actually get
            // reused instead of re-fetched every time you play.
            var rng = new System.Random(StableHash(biomes[b].name) + 37);
            string flavor = string.IsNullOrEmpty(biomes[b].description) ? biomes[b].name : biomes[b].description;

            for (int v = 0; v < variantsPerBiome; v++)
            {
                string role = RoleAdjectives[rng.Next(RoleAdjectives.Length)];
                string name = $"{biomes[b].name} {role} {v + 1}";
                string prompt = $"full-body fantasy RPG character concept art of {role}, fitting for a {flavor} setting, " +
                                 "standing neutral pose, front facing, plain solid magenta background, no shadow, simple flat colors";
                defs[count++] = (b, name, role, prompt);
            }
        }

        pendingTextures = defs.Length;
        foreach (var (biomeIndex, name, role, prompt) in defs)
        {
            // This whole call chain runs synchronously from inside
            // BiomeAiGenerator.FinishWithBiomes, right before it fires its
            // own OnFinished — WorldLoadingGate waits on that event to
            // release the player. An uncaught exception here (e.g. the
            // custom shader below failing to resolve) would abort
            // FinishWithBiomes before OnFinished ever fires, freezing the
            // player PERMANENTLY rather than just delaying them. Catching
            // per-variant, like TreeAiGenerator/StructureAiGenerator already
            // do around their own mesh building, means one bad variant can't
            // take the whole load-gate down with it.
            try { BuildAndAssign(biomeIndex, name, role, prompt); }
            catch (Exception e)
            {
                Debug.LogWarning($"[NPCs] Failed to build template \"{name}\" — {e.Message}");
                // No texture coroutine got started for this one, so release
                // its counted slot — but don't call FinishReady() here too:
                // the single check below (after the whole synchronous loop
                // finishes) is the only safe place to do that, since calling
                // it here as well could fire it twice if this happens to be
                // the failure that brings the count to zero.
                pendingTextures--;
            }
        }

        terrainChunkManager.OnChunkBuilt += OnChunkBuilt;
        TerrainChunk.OnStructureSpawned += OnStructureSpawned;

        if (pendingTextures == 0) FinishReady();
    }

    private void BuildAndAssign(int biomeIndex, string name, string role, string prompt)
    {
        // Fall back to a guaranteed-present shader if the custom one somehow
        // isn't found (e.g. an import hiccup) — sprites would render as
        // opaque billboards without background transparency, but that's far
        // better than new Material(null) throwing and taking down generation.
        Shader shader = Shader.Find("Custom/NpcSpriteUnlit");
        if (shader == null)
        {
            Debug.LogWarning("[NPCs] Custom/NpcSpriteUnlit shader not found — falling back to a standard shader (sprites will render opaque).");
            shader = Shader.Find("Universal Render Pipeline/Lit");
        }

        var material = new Material(shader) { color = new Color(0.75f, 0.75f, 0.8f) };

        GameObject template = BuildTemplate(name, role, material);
        templatesByBiome[biomeIndex].Add(template);

        StartCoroutine(HuggingFaceImageClient.GenerateTexture(prompt, texture =>
        {
            if (texture != null)
            {
                // Sample the actual corner color instead of assuming pure
                // magenta — the model is only ever asked for "plain solid
                // magenta background" via the prompt, it doesn't always
                // comply exactly, so keying against a fixed (255,0,255)
                // guess silently keys out nothing when it renders some other
                // (still roughly uniform) shade instead.
                Color32 keyColor = SampleBackgroundColor(texture);
                material.color = Color.white;
                material.mainTexture = ApplyChromaKey(texture, keyColor, 0.12f, 0.4f);
                Debug.Log($"[NPCs] Applied sprite for \"{name}\" (keyed background {keyColor}).");
            }
            else
            {
                Debug.LogWarning($"[NPCs] Sprite generation failed for \"{name}\" — keeping placeholder color.");
            }

            if (--pendingTextures <= 0) FinishReady();
        }));
    }

    private GameObject BuildTemplate(string name, string role, Material material)
    {
        var template = new GameObject($"Npc_{name}");
        template.transform.SetParent(transform);
        template.SetActive(false);

        var sprite = new GameObject("Sprite");
        sprite.transform.SetParent(template.transform);
        sprite.transform.localPosition = Vector3.zero;

        var filter = sprite.AddComponent<MeshFilter>();
        filter.sharedMesh = BuildSpriteMesh(1.2f, 2f);
        var renderer = sprite.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        template.AddComponent<NpcBillboard>();

        var wander = template.AddComponent<NpcWander>();
        wander.wanderRadius = wanderRadius;

        template.AddComponent<NpcIdentity>();
        template.AddComponent<NpcSocialize>();

        var sphere = template.AddComponent<SphereCollider>();
        sphere.isTrigger = true;
        sphere.radius = 2.5f;
        sphere.center = new Vector3(0f, 1f, 0f);

        template.AddComponent<NpcInteractable>();

        var marker = template.AddComponent<CompassMarker>();
        marker.kind = CompassMarker.MarkerKind.Npc;
        marker.color = CompassMarker.DefaultNpcColor;

        var info = template.AddComponent<NpcTemplateInfo>();
        info.role = role;

        return template;
    }

    // Bottom-anchored quad (0..height in Y, not centered) facing local +Z —
    // NpcBillboard rotates the whole NPC so +Z points at the camera/partner,
    // so this is guaranteed to be the visible side without depending on
    // which way Unity's built-in Quad primitive happens to face.
    private static Mesh BuildSpriteMesh(float width, float height)
    {
        float hw = width * 0.5f;
        var mesh = new Mesh { name = "NpcSprite" };
        mesh.vertices = new[]
        {
            new Vector3(-hw, 0f, 0f),
            new Vector3(hw, 0f, 0f),
            new Vector3(-hw, height, 0f),
            new Vector3(hw, height, 0f),
        };
        // U flipped (1 on the left, 0 on the right): the camera actually
        // ends up viewing this quad from behind the face this winding
        // intends as "front" — a flat single-layer quad viewed from its
        // back always shows a horizontally mirrored image of whatever's on
        // it (the same reason text on the back of a piece of paper reads
        // backwards), independent of shading/culling. Flipping the UV here
        // cancels that mirroring out instead of chasing winding/normals.
        mesh.uv = new[]
        {
            new Vector2(1f, 0f),
            new Vector2(0f, 0f),
            new Vector2(1f, 1f),
            new Vector2(0f, 1f),
        };
        mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // Samples many points along all four edges (not just the 4 corners —
    // a full-body pose can easily reach a corner in some generations,
    // which silently poisoned the old 4-pixel average into sampling
    // character color instead of background and inverted the whole
    // effect: keying out the character while leaving the true background
    // opaque, exactly as reported). Buckets samples by a coarsely
    // quantized color and returns the average color of the LARGEST
    // bucket (the mode) — since the background occupies far more of the
    // border than a character's extremities ever do, its bucket wins even
    // if a few edge pixels happen to be character, unlike a plain average
    // (which those same pixels could still skew) or a handful of corner
    // samples (which they can dominate outright).
    private static Color32 SampleBackgroundColor(Texture2D texture)
    {
        var buckets = new Dictionary<int, (int count, int r, int g, int b)>();

        void Sample(int x, int y)
        {
            Color32 p = texture.GetPixel(x, y);
            int key = ((p.r >> 4) << 8) | ((p.g >> 4) << 4) | (p.b >> 4); // quantize to 16 levels/channel
            buckets.TryGetValue(key, out var bucket);
            bucket.count++;
            bucket.r += p.r;
            bucket.g += p.g;
            bucket.b += p.b;
            buckets[key] = bucket;
        }

        int w = texture.width, h = texture.height;
        int stepX = Mathf.Max(1, w / 40);
        for (int x = 0; x < w; x += stepX)
        {
            Sample(x, 0);
            Sample(x, h - 1);
        }
        int stepY = Mathf.Max(1, h / 40);
        for (int y = 0; y < h; y += stepY)
        {
            Sample(0, y);
            Sample(w - 1, y);
        }

        int bestKey = -1, bestCount = 0;
        foreach (var kvp in buckets)
        {
            if (kvp.Value.count <= bestCount) continue;
            bestCount = kvp.Value.count;
            bestKey = kvp.Key;
        }

        var best = buckets[bestKey];
        return new Color32((byte)(best.r / best.count), (byte)(best.g / best.count), (byte)(best.b / best.count), 255);
    }

    // Turns the AI-generated image's solid-color background transparent —
    // the image-generation model has no alpha-channel output, so sprites are
    // prompted against a flat magenta backdrop (rare in character art), and
    // pixels close to it get keyed out here. Uses a soft ramp between two
    // thresholds rather than a single hard cutoff: a diffusion model doesn't
    // render a perfectly uniform "solid color" background (slight gradients/
    // noise are common), so a tight single threshold left a lot of the
    // background only partially keyed. The ramp is also more forgiving of
    // anti-aliased pixels right at the character's silhouette edge, which a
    // hard cutoff would leave as a jagged/fringed border.
    //
    // Builds a NEW texture rather than modifying the source in place:
    // Texture2D.LoadImage picks a pixel format based on what the source
    // image actually contains, and a generated image with no real
    // transparency in it (which these are, straight from the model) often
    // decodes to a format with no alpha channel at all — writing alpha via
    // SetPixels32 on a texture like that is a silent no-op, so no amount of
    // threshold tuning would ever have shown any effect. An explicit
    // RGBA32 destination guarantees an alpha channel actually exists.
    private static Texture2D ApplyChromaKey(Texture2D source, Color32 keyColor, float innerThreshold, float outerThreshold)
    {
        Color32[] pixels = source.GetPixels32();
        for (int i = 0; i < pixels.Length; i++)
        {
            Color32 p = pixels[i];
            float dr = (p.r - keyColor.r) / 255f;
            float dg = (p.g - keyColor.g) / 255f;
            float db = (p.b - keyColor.b) / 255f;
            float distance = Mathf.Sqrt(dr * dr + dg * dg + db * db);

            // Below innerThreshold: fully transparent (definitely background).
            // Above outerThreshold: fully opaque (definitely character).
            // Between: smooth ramp.
            float alpha01 = Mathf.Clamp01((distance - innerThreshold) / (outerThreshold - innerThreshold));
            pixels[i] = new Color32(p.r, p.g, p.b, (byte)(alpha01 * 255f));
        }

        var result = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
        };
        result.SetPixels32(pixels);
        result.Apply();
        return result;
    }

    // Deterministic string hash (FNV-1a) — unlike string.GetHashCode(),
    // .NET does not guarantee this is stable across processes/runs (it's
    // randomized by default for security), so it can't be used to seed
    // anything that needs to reproduce the same value across sessions.
    private static int StableHash(string s)
    {
        unchecked
        {
            int hash = (int)2166136261;
            foreach (char c in s)
                hash = (hash ^ c) * 16777619;
            return hash;
        }
    }

    private void FinishReady()
    {
        templatesReady = true;

        // Backfill chunks that streamed in (and got skipped — see
        // OnChunkBuilt) before sprite generation finished, most notably the
        // handful the player starts on top of.
        foreach (Vector2Int coord in terrainChunkManager.ActiveChunkCoords)
            TrySpawnForChunk(coord);

        OnFinished?.Invoke();
    }

    private void OnChunkBuilt(Vector2Int coord) => TrySpawnForChunk(coord);

    private void TrySpawnForChunk(Vector2Int coord)
    {
        if (!templatesReady) return; // will be retried from FinishReady's backfill pass
        if (!spawnedChunks.Add(coord)) return; // already rolled for this coordinate

        // Distinct primes/salt from ScatterProps/ScatterStructures in
        // TerrainChunk.cs so this roll doesn't correlate with theirs.
        var rng = new System.Random(coord.x * 15485863 ^ coord.y * 32452867 ^ (terrainChunkManager.terrainSettings.seed * 22801 + 19));
        if (rng.NextDouble() > spawnChancePerChunk) return;

        float chunkSize = terrainChunkManager.chunkSize;
        float worldOriginX = coord.x * chunkSize;
        float worldOriginZ = coord.y * chunkSize;
        float centerX = worldOriginX + (float)rng.NextDouble() * chunkSize;
        float centerZ = worldOriginZ + (float)rng.NextDouble() * chunkSize;

        int biomeIndex = terrainChunkManager.terrainSettings.DominantBiome(centerX, centerZ);
        List<GameObject> variants = templatesByBiome[biomeIndex];
        if (variants.Count == 0) return;

        // A whole chunk's single roll used to place exactly one NPC,
        // independently positioned per chunk (100 units apart on average) —
        // hopelessly outside NpcSocialize's few-unit interaction range, so
        // two separately-scattered NPCs essentially never crossed paths.
        // Spawning a small tight-knit group instead means their wander
        // circles actually overlap.
        int groupSize = groupMinSize + rng.Next(groupMaxSize - groupMinSize + 1);
        for (int i = 0; i < groupSize; i++)
        {
            float worldX = centerX, worldZ = centerZ;
            if (groupSize > 1)
            {
                float angle = (float)(rng.NextDouble() * Mathf.PI * 2.0);
                float dist = 2f + (float)rng.NextDouble() * 4f; // 2-6 units apart
                worldX += Mathf.Cos(angle) * dist;
                worldZ += Mathf.Sin(angle) * dist;
            }

            GameObject template = variants[rng.Next(variants.Count)];
            string role = template.GetComponent<NpcTemplateInfo>().role;

            GameObject instance = Instantiate(template, transform);
            Vector3 position = new Vector3(worldX, terrainChunkManager.GetGroundHeight(worldX, worldZ), worldZ);
            instance.transform.position = position;

            string npcId = $"npc_{coord.x}_{coord.y}_{i}";
            instance.GetComponent<NpcIdentity>().Initialize(npcId, role, rng);
            NpcWander wander = instance.GetComponent<NpcWander>();
            if (groupSize > 1) wander.wanderRadius = groupWanderRadius;
            wander.Initialize(terrainChunkManager, position);

            instance.SetActive(true); // Instantiate copies the source template's inactive state — must be forced on
        }

        Debug.Log($"[NPCs] Spawned a group of {groupSize} at chunk {coord}, center ({centerX:0}, {centerZ:0}).");
    }

    // A small camp of NPCs anchored near a rare landmark structure, close
    // enough together that their wander circles overlap and NpcSocialize's
    // proximity check can actually fire between them — the per-chunk
    // scatter above places NPCs a full chunk (100 units) or more apart on
    // average, far past NpcSocialize's few-unit trigger range, so two
    // independently-scattered NPCs essentially never crossed paths.
    private void OnStructureSpawned(Vector3 worldPosition)
    {
        if (!templatesReady) return; // structures are rare enough that skipping the odd early one isn't worth a backfill pass

        var rng = new System.Random();
        if (rng.NextDouble() > structureClusterChance) return;

        int biomeIndex = terrainChunkManager.terrainSettings.DominantBiome(worldPosition.x, worldPosition.z);
        List<GameObject> variants = templatesByBiome[biomeIndex];
        if (variants.Count == 0) return;

        int clusterSize = structureClusterMinSize + rng.Next(structureClusterMaxSize - structureClusterMinSize + 1);
        for (int i = 0; i < clusterSize; i++)
        {
            float angle = (float)(rng.NextDouble() * Mathf.PI * 2.0);
            float dist = 4f + (float)rng.NextDouble() * 8f; // 4-12 units from the structure itself
            float worldX = worldPosition.x + Mathf.Cos(angle) * dist;
            float worldZ = worldPosition.z + Mathf.Sin(angle) * dist;

            GameObject template = variants[rng.Next(variants.Count)];
            string role = template.GetComponent<NpcTemplateInfo>().role;

            GameObject instance = Instantiate(template, transform);
            Vector3 position = new Vector3(worldX, terrainChunkManager.GetGroundHeight(worldX, worldZ), worldZ);
            instance.transform.position = position;

            string npcId = $"npc_struct_{worldPosition.x:F0}_{worldPosition.z:F0}_{i}";
            instance.GetComponent<NpcIdentity>().Initialize(npcId, role, rng);
            instance.GetComponent<NpcWander>().wanderRadius = groupWanderRadius;
            instance.GetComponent<NpcWander>().Initialize(terrainChunkManager, position);

            instance.SetActive(true);
        }

        Debug.Log($"[NPCs] Spawned a cluster of {clusterSize} near a structure at {worldPosition}.");
    }
}
