using UnityEngine;

// Spawns a findable 3D pickup for a Fetch-type quest the moment it's
// accepted — a small procedurally-assembled shape with an AI-generated
// texture wrapped onto it (same HuggingFaceImageClient pipeline used for
// ground/bark/NPC-sprite textures elsewhere), positioned well away from
// wherever the quest was given out. Picking it up (QuestItem.OnTriggerEnter)
// reports quest progress and adds it to the player's Hotbar.
//
// Shapes are assembled from 1-3 of Unity's own built-in primitive meshes
// (cube/sphere/cylinder) rather than hand-built vertices/UVs/winding — this
// project has already been bitten twice by getting a custom mesh's winding/
// UV orientation wrong (the NPC sprite quad needed two separate fixes for
// exactly that). Combining a few guaranteed-correct primitives at randomized
// relative sizes/offsets gives real shape variety ("randomly generated")
// with zero orientation risk, unlike authoring new geometry by hand.
public static class QuestItemAiGenerator
{
    // Combined into a single prompt (material + detail + object), giving
    // 8*8*7 = 448 possible phrasings instead of picking from one flat list
    // of 6. HuggingFaceImageClient caches strictly by exact prompt text, so
    // a small pool meant frequent exact repeats — with only 6 options,
    // there's already better than even odds of two items colliding on the
    // same phrase (and therefore the same cached image) after just 3 spawns.
    private static readonly string[] Materials =
    {
        "weathered wood", "tarnished brass", "polished silver", "rough-hewn stone",
        "aged leather", "cracked ceramic", "dull pewter", "sun-bleached bone",
    };
    private static readonly string[] Details =
    {
        "rune-carved", "gem-encrusted", "moss-covered", "gold-inlaid",
        "battle-worn", "intricately patterned", "frost-covered", "faded and cracked",
    };
    private static readonly string[] ObjectTypes =
    {
        "box", "chest", "urn", "satchel", "lockbox", "shard container", "reliquary",
    };

    private enum ItemShape { Box, Gem, Wand, Urn }

    private static Mesh cubeMesh, sphereMesh, cylinderMesh;
    private static TerrainChunkManager terrainChunkManager;

    public static void SpawnFor(Quest quest, Vector3 nearPosition, MonoBehaviour coroutineRunner)
    {
        EnsurePrimitiveMeshes();
        if (terrainChunkManager == null) terrainChunkManager = Object.FindFirstObjectByType<TerrainChunkManager>();

        var rng = new System.Random();
        float size = 0.35f + (float)rng.NextDouble() * 0.35f; // 0.35-0.7 units, hand-sized

        // Angle + a genuinely large minimum distance rather than
        // Random.insideUnitCircle (which samples uniformly over the WHOLE
        // disk and kept landing within a couple of units of center, right
        // back on top of the player). Scaled to match the world's own
        // sense of distance — chunks are 100 units and NPCs wander up to 20
        // — even 20-40 units out still read as "a few steps away" at that
        // scale, so it's measured in chunks (1-2) instead.
        float chunkSize = terrainChunkManager != null ? terrainChunkManager.chunkSize : 100f;
        float angle = (float)(rng.NextDouble() * Mathf.PI * 2.0);
        float distance = chunkSize * (1f + (float)rng.NextDouble());
        float worldX = nearPosition.x + Mathf.Cos(angle) * distance;
        float worldZ = nearPosition.z + Mathf.Sin(angle) * distance;
        float worldY = terrainChunkManager != null
            ? terrainChunkManager.GetGroundHeight(worldX, worldZ)
            : nearPosition.y; // fallback if something is oddly missing the manager — better than crashing

        var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.8f, 0.75f, 0.6f) };

        GameObject itemObject = BuildRandomShape((ItemShape)rng.Next(4), rng, material);
        itemObject.name = $"QuestItem_{quest.targetId}";
        itemObject.transform.position = new Vector3(worldX, worldY + size * 0.5f, worldZ);
        itemObject.transform.localScale = Vector3.one * size;
        itemObject.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);

        // Bigger than the visual mesh so it's easy to actually walk into,
        // same reasoning as the NPC interact sphere being bigger than the sprite.
        var collider = itemObject.AddComponent<BoxCollider>();
        collider.isTrigger = true;
        collider.size = Vector3.one * 2.5f;

        var questItem = itemObject.AddComponent<QuestItem>();
        questItem.targetId = quest.targetId;
        questItem.displayName = quest.title;

        string materialName = Materials[rng.Next(Materials.Length)];
        string detail = Details[rng.Next(Details.Length)];
        string objectType = ObjectTypes[rng.Next(ObjectTypes.Length)];
        string prompt = $"seamless tileable fantasy game item texture, a {detail} {materialName} {objectType}, " +
                         "top-down flat lighting, no shadow, simple flat colors";

        coroutineRunner.StartCoroutine(HuggingFaceImageClient.GenerateTexture(prompt, texture =>
        {
            if (texture != null)
            {
                // All parts share this one Material instance, so this
                // updates every piece of the shape at once.
                material.mainTexture = texture;
                material.color = Color.white;
                questItem.icon = texture;
            }
            else
            {
                Debug.LogWarning($"[Quest Items] Texture generation failed for \"{quest.title}\" — keeping placeholder color.");
            }
        }));

        Debug.Log($"[Quest Items] Spawned \"{quest.title}\" pickup {distance:0}u from {nearPosition}.");
    }

    private static void EnsurePrimitiveMeshes()
    {
        if (cubeMesh != null) return;
        cubeMesh = ExtractMesh(PrimitiveType.Cube);
        sphereMesh = ExtractMesh(PrimitiveType.Sphere);
        cylinderMesh = ExtractMesh(PrimitiveType.Cylinder);
    }

    private static Mesh ExtractMesh(PrimitiveType type)
    {
        var temp = GameObject.CreatePrimitive(type);
        Mesh mesh = temp.GetComponent<MeshFilter>().sharedMesh;
        Object.Destroy(temp);
        return mesh;
    }

    // Assembles a small root + 1-3 child "part" GameObjects, each a scaled/
    // offset copy of a built-in primitive sharing the same material — no
    // custom mesh math anywhere, just composition.
    private static GameObject BuildRandomShape(ItemShape shape, System.Random rng, Material material)
    {
        var root = new GameObject("ItemShape");

        switch (shape)
        {
            case ItemShape.Box:
                AddPart(root, cubeMesh, material, Vector3.zero, Vector3.one, Quaternion.identity);
                AddPart(root, cubeMesh, material, new Vector3(0f, 0.55f, 0f), new Vector3(0.85f, 0.3f, 0.85f), Quaternion.identity); // lid
                break;

            case ItemShape.Gem:
                AddPart(root, sphereMesh, material, Vector3.zero, new Vector3(0.7f, 1f, 0.7f), Quaternion.identity); // core
                int shards = 2 + rng.Next(2);
                for (int i = 0; i < shards; i++)
                {
                    float yaw = (float)(rng.NextDouble() * 360.0);
                    Vector3 dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                    AddPart(root, sphereMesh, material, dir * 0.45f, Vector3.one * 0.4f, Quaternion.Euler((float)rng.NextDouble() * 30f, yaw, 0f));
                }
                break;

            case ItemShape.Wand:
                AddPart(root, cylinderMesh, material, Vector3.zero, new Vector3(0.2f, 0.9f, 0.2f), Quaternion.identity); // shaft
                AddPart(root, sphereMesh, material, new Vector3(0f, 1f, 0f), Vector3.one * 0.5f, Quaternion.identity); // orb
                break;

            default: // Urn
                AddPart(root, cylinderMesh, material, Vector3.zero, new Vector3(0.7f, 0.8f, 0.7f), Quaternion.identity); // body
                AddPart(root, sphereMesh, material, new Vector3(0f, 0.55f, 0f), Vector3.one * 0.4f, Quaternion.identity); // lid
                break;
        }

        return root;
    }

    private static void AddPart(GameObject root, Mesh mesh, Material material, Vector3 localPos, Vector3 localScale, Quaternion localRot)
    {
        var part = new GameObject("Part");
        part.transform.SetParent(root.transform, false);
        part.transform.localPosition = localPos;
        part.transform.localScale = localScale;
        part.transform.localRotation = localRot;

        var filter = part.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;
        var renderer = part.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
    }
}
