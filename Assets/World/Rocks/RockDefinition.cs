using System;

[Serializable]
public class RockDefinition
{
    public string name = "Rock";
    public float baseRadius = 1f;
    public int clusterCount = 3; // number of overlapping boxes forming the rock, 1-6
    public string texturePrompt;
}
