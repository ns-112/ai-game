using System;
using System.Collections.Generic;

// Plain data — one NPC's rolled personality (see NpcPersonalityGenerator)
// and its running interaction history (see NpcMemoryStore). Both are
// JsonUtility-serialized as-is, so keep every field a JsonUtility-friendly
// type (no properties, no interfaces, no Dictionary).
[Serializable]
public class NpcPersonality
{
    public string npcId;
    public string displayName;
    public string[] traits;
    public string backstory;
}

[Serializable]
public class NpcMemoryEntry
{
    public string otherName;
    public bool otherIsPlayer;
    public string summary;
    public string timestamp; // ISO 8601 (DateTime.ToString("o")) — informational only, nothing currently parses it back
}

[Serializable]
public class NpcMemory
{
    public string npcId;
    public List<NpcMemoryEntry> entries = new();
}
