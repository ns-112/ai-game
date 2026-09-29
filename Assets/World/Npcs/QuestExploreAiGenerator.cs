using UnityEngine;

// Spawns a findable, walk-into location marker for an Explore-type quest the
// moment it's accepted — a small glowing beacon positioned a short walk from
// wherever the quest was given out. Same fix QuestItemAiGenerator applies to
// Fetch quests: without this, "go check out the old ruins" was pure flavor
// text with nothing anywhere the player could actually walk to.
//
// Uses Unity's own built-in sphere mesh (via a throwaway CreatePrimitive)
// rather than hand-building geometry — same reasoning as QuestItemAiGenerator's
// cube: this project has already been bitten twice by getting a custom
// mesh's winding/UV orientation wrong, and there's no need to risk that
// again for a plain beacon shape.
public static class QuestExploreAiGenerator
{
    // Matches QuestLocationMarker's compass marker color, so the color you
    // see on the compass is the same one you're looking for in the world.
    private static Color BeaconColor => QuestLocationMarker.MarkerColor;

    private static Mesh sharedSphereMesh;
    private static TerrainChunkManager terrainChunkManager;

    public static void SpawnFor(Quest quest, Vector3 nearPosition)
    {
        if (sharedSphereMesh == null)
        {
            var temp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sharedSphereMesh = temp.GetComponent<MeshFilter>().sharedMesh;
            Object.Destroy(temp);
        }
        if (terrainChunkManager == null) terrainChunkManager = Object.FindFirstObjectByType<TerrainChunkManager>();

        // Angle+distance rather than Random.insideUnitCircle (see
        // QuestItemAiGenerator's matching fix) — a uniform disk sample can
        // land within a unit of center, right back next to whoever gave the
        // quest. Further out than a Fetch item too — "go explore" should
        // mean an actual walk. Measured in chunks (1.5-2.5) rather than raw
        // units: the old 10-18 units was a few steps at this world's scale.
        var rng = new System.Random();
        float chunkSize = terrainChunkManager != null ? terrainChunkManager.chunkSize : 100f;
        float angle = (float)(rng.NextDouble() * Mathf.PI * 2.0);
        float distance = chunkSize * (1.5f + (float)rng.NextDouble());
        float worldX = nearPosition.x + Mathf.Cos(angle) * distance;
        float worldZ = nearPosition.z + Mathf.Sin(angle) * distance;
        // Ground height at the destination, not the giver's height — at this
        // distance the terrain can be far higher or lower than where it started.
        float worldY = terrainChunkManager != null ? terrainChunkManager.GetGroundHeight(worldX, worldZ) : nearPosition.y;
        Vector3 spawnPos = new Vector3(worldX, worldY + 1f, worldZ);

        var markerObject = new GameObject($"QuestLocation_{quest.targetId}");
        markerObject.transform.position = spawnPos;
        markerObject.transform.localScale = Vector3.one * 0.8f;

        var filter = markerObject.AddComponent<MeshFilter>();
        filter.sharedMesh = sharedSphereMesh;

        var renderer = markerObject.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = BeaconColor };

        // Bigger than the visual mesh so it's easy to actually walk into,
        // same reasoning as the NPC interact sphere/Fetch item collider.
        var collider = markerObject.AddComponent<SphereCollider>();
        collider.isTrigger = true;
        collider.radius = 2f;

        var locationMarker = markerObject.AddComponent<QuestLocationMarker>();
        locationMarker.targetId = quest.targetId;

        Debug.Log($"[Quest Locations] Spawned \"{quest.title}\" marker {distance:0}u from {nearPosition}.");
    }
}
