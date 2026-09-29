using UnityEngine;

// Shared by the AI prop generators (trees/rocks/buildings): adds a
// MeshCollider matching the prop's actual generated shape instead of an
// approximate primitive collider.
public static class ProceduralMeshUtils
{
    public static MeshCollider AddMeshCollider(GameObject target, Mesh mesh)
    {
        var collider = target.AddComponent<MeshCollider>();

        // These meshes duplicate a vertex per triangle corner for flat
        // shading, which is exactly the pattern that makes PhysX's
        // mesh-cleaning/vertex-welding cooking step choke and log "cleaning
        // the mesh failed" (same fix as TerrainChunk).
        collider.cookingOptions &= ~(MeshColliderCookingOptions.EnableMeshCleaning
                                    | MeshColliderCookingOptions.WeldColocatedVertices);
        collider.sharedMesh = mesh;
        return collider;
    }
}
