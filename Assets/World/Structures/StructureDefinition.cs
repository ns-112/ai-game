using System;

// What Gemini invents for one large structure type: shape parameters only,
// never raw geometry — ProceduralStructureBuilder turns these into an
// actual walk-in mesh (hollow walls with a real doorway gap), so the result
// is always enterable and well-formed regardless of what numbers come back.
[Serializable]
public class StructureDefinition
{
    public string name = "Ruin";
    public float width = 10f;
    public float depth = 10f;
    public float wallHeight = 5f;
    public float wallThickness = 0.4f;
    public float doorWidth = 2.5f;
    public float doorHeight = 3f;
    public int pillarCount = 2; // 0-4 interior decorative pillars
    public string wallPrompt;
    public string roofPrompt;
}
