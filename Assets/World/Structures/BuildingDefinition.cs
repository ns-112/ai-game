using System;

[Serializable]
public class BuildingDefinition
{
    public string name = "Building";
    public float width = 4f;
    public float depth = 4f;
    public float wallHeight = 3f;
    public float roofHeight = 1.5f;
    public string wallPrompt;
    public string roofPrompt;
}
