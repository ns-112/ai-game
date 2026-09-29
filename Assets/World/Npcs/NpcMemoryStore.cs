using System;
using System.IO;
using UnityEngine;

// On-disk JSON persistence for each NPC's interaction memory, one file per
// NPC keyed by its stable id (see NpcIdentity/NpcAiGenerator for how that id
// is derived). Lives under persistentDataPath like the AI response/texture
// caches in Assets/World/Web, since it's runtime data that must work the
// same in the Editor and in a build, not an authored asset.
//
// An NPC's id is derived from its spawn chunk coordinate and the world
// seed, so memory only reliably carries over between sessions that use the
// same seed (TerrainChunkManager.randomizeSeedOnStart off) — a fresh random
// seed means a fresh cast of NPCs with fresh (empty) memory files, since
// there's no guarantee the "same" NPC spawns in the same place again.
public static class NpcMemoryStore
{
    private static string DirPath => Path.Combine(Application.persistentDataPath, "NpcMemory");

    public static NpcMemory Load(string npcId)
    {
        string path = GetPath(npcId);
        if (File.Exists(path))
        {
            try { return JsonUtility.FromJson<NpcMemory>(File.ReadAllText(path)); }
            catch (Exception e) { Debug.LogWarning($"[NPC Memory] Failed to read \"{path}\" — {e.Message}"); }
        }
        return new NpcMemory { npcId = npcId };
    }

    public static void Save(NpcMemory memory)
    {
        try
        {
            Directory.CreateDirectory(DirPath);
            File.WriteAllText(GetPath(memory.npcId), JsonUtility.ToJson(memory, true));
        }
        catch (IOException e) { Debug.LogWarning($"[NPC Memory] Failed to save memory for \"{memory.npcId}\" — {e.Message}"); }
    }

    private static string GetPath(string npcId) => Path.Combine(DirPath, npcId + ".json");
}
