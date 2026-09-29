using System;

// What Gemini invents for one tree type: shape parameters only, never raw
// geometry — ProceduralTreeBuilder turns these into an actual mesh, so the
// result is always a valid, well-formed shape regardless of what numbers
// come back.
[Serializable]
public class TreeDefinition
{
    public string name = "Tree";
    public float trunkHeight = 3f;
    public float trunkRadius = 0.3f;
    public CanopyTier[] canopyTiers = { new CanopyTier { radius = 1.5f, height = 2f, offset = 0f } };
    public BranchDefinition[] branches = Array.Empty<BranchDefinition>();
    public string barkPrompt;
    public string canopyPrompt;
}

[Serializable]
public class CanopyTier
{
    public float radius = 1.5f;
    public float height = 2f;
    public float offset = 0f; // vertical gap from the previous tier's base (trunk top for the first tier)
}

[Serializable]
public class BranchDefinition
{
    public float heightFraction = 0.6f; // 0-1 up the trunk, where the branch emerges
    public float angleFromVertical = 45f; // degrees; 0 = straight up, 90 = horizontal
    public float azimuth = 0f; // degrees around the trunk (0-360), which direction the branch points
    public float length = 1.5f;
    public float radius = 0.1f;
    public float canopyRadius = 0.8f; // small canopy cone at the branch tip
}
