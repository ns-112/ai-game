using UnityEngine;

// Optional — attach to a prop prefab/template to make it rarer or more
// common than other props in the same biome's list. Absent = weight 1 (the
// old uniform-random behavior), so manually assigned prefabs are unaffected.
public class PropWeight : MonoBehaviour
{
    public float weight = 1f;
}
