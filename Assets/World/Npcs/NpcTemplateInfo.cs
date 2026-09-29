using UnityEngine;

// Carries the flavor-text "role" (e.g. "a wandering merchant") an NPC
// template was generated with, from NpcAiGenerator's template-build step
// through to spawn time, where it's fed into NpcPersonalityGenerator so the
// rolled personality's backstory matches what the sprite was prompted as.
public class NpcTemplateInfo : MonoBehaviour
{
    public string role;
}
