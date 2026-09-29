using UnityEngine;

// Enables distance fog and keeps its range in sync with the terrain's
// streaming radius, so chunks fade into fog right around where they'd
// otherwise pop in/out of existence instead of the world just ending.
//
// Also points the camera's background at the exact same color as the fog.
// Fog only recolors opaque geometry — Unity's skybox renders behind
// everything at "infinite" distance and is never touched by fog — so
// without this, terrain fades toward the fog color and then meets a
// differently-colored sky right at the edge, and that mismatch is a visible
// silhouette even though the terrain itself is heavily fogged. Matching the
// clear color to the fog color removes anything for the edge to contrast against.
public class FogController : MonoBehaviour
{
    public TerrainChunkManager terrainChunkManager;
    public Camera targetCamera; // defaults to Camera.main if left unassigned
    public Color fogColor = new(0.75f, 0.8f, 0.85f);
    [Range(0f, 1f)] public float fogStartFraction = 0.5f; // fog begins at this fraction of the view distance

    private void Start()
    {
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = fogColor;

        Camera cam = targetCamera != null ? targetCamera : Camera.main;
        if (cam != null)
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = fogColor;
        }

        if (terrainChunkManager == null) return;

        float viewDistance = terrainChunkManager.chunkSize * terrainChunkManager.viewDistanceInChunks;
        RenderSettings.fogStartDistance = viewDistance * fogStartFraction;
        RenderSettings.fogEndDistance = viewDistance;
    }
}
