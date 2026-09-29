using System.Collections.Generic;
using UnityEngine;

// Builds a simple low-poly building: a box (walls, including a floor so the
// mesh is closed) topped with a randomized roof, flat-shaded. The roof style
// (gable/hip/flat-with-parapet) and optional chimney/porch are all driven by
// a seed unique to the building's name — same approach as
// ProceduralStructureBuilder — so two variants with identical width/depth/
// height still end up looking like different buildings instead of the same
// box resized. Two submeshes: 0 = walls + chimney + porch posts (wall
// material), 1 = roof + porch overhang (roof material).
public static class ProceduralBuildingBuilder
{
    public static Mesh Build(BuildingDefinition def)
    {
        var rng = new System.Random(def.name.GetHashCode());
        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var wallTriangles = new List<int>();
        var roofTriangles = new List<int>();

        float hw = def.width * 0.5f;
        float hd = def.depth * 0.5f;
        float wallH = def.wallHeight;

        Vector3[] baseCorners =
        {
            new Vector3(-hw, 0, -hd), new Vector3(hw, 0, -hd), new Vector3(hw, 0, hd), new Vector3(-hw, 0, hd),
        };
        Vector3[] topCorners =
        {
            new Vector3(-hw, wallH, -hd), new Vector3(hw, wallH, -hd), new Vector3(hw, wallH, hd), new Vector3(-hw, wallH, hd),
        };

        for (int i = 0; i < 4; i++)
        {
            int j = (i + 1) % 4;
            AddQuad(vertices, uvs, wallTriangles, baseCorners[i], baseCorners[j], topCorners[j], topCorners[i]);
        }
        AddQuad(vertices, uvs, wallTriangles, baseCorners[3], baseCorners[2], baseCorners[1], baseCorners[0]); // floor

        AddRoof(vertices, uvs, wallTriangles, roofTriangles, rng, topCorners, hw, hd, wallH, def.roofHeight);

        // Optional chimney, poking up through the roof near a back corner.
        if (rng.NextDouble() < 0.4)
        {
            float chimneySize = Mathf.Max(0.15f, Mathf.Min(def.width, def.depth) * 0.08f);
            float cx = Lerp(hw * 0.3f, hw * 0.7f, (float)rng.NextDouble()) * (rng.NextDouble() < 0.5 ? -1f : 1f);
            float cz = Lerp(hd * 0.3f, hd * 0.7f, (float)rng.NextDouble());
            float chimneyTop = wallH + def.roofHeight + chimneySize * 1.5f;
            AddBox(vertices, uvs, wallTriangles, new Vector3(cx, chimneyTop * 0.5f, cz),
                new Vector3(chimneySize, chimneyTop * 0.5f, chimneySize));
        }

        // Optional small porch over the front door, with two support posts.
        if (rng.NextDouble() < 0.45 && hw > 1f)
        {
            float porchWidth = Mathf.Min(hw * 1.2f, hw + 0.8f);
            float porchDepth = Lerp(0.6f, 1.2f, (float)rng.NextDouble());
            float porchHeight = wallH * Lerp(0.55f, 0.8f, (float)rng.NextDouble());
            float postRadius = 0.08f;
            float roofThickness = 0.08f;
            float frontZ = -hd;

            AddBox(vertices, uvs, roofTriangles,
                new Vector3(0f, porchHeight + roofThickness * 0.5f, frontZ - porchDepth * 0.5f),
                new Vector3(porchWidth * 0.5f, roofThickness * 0.5f, porchDepth * 0.5f));

            float postX = porchWidth * 0.5f - postRadius;
            float postZ = frontZ - porchDepth + postRadius;
            AddBox(vertices, uvs, wallTriangles, new Vector3(-postX, porchHeight * 0.5f, postZ), new Vector3(postRadius, porchHeight * 0.5f, postRadius));
            AddBox(vertices, uvs, wallTriangles, new Vector3(postX, porchHeight * 0.5f, postZ), new Vector3(postRadius, porchHeight * 0.5f, postRadius));
        }

        var mesh = new Mesh { name = def.name };
        mesh.subMeshCount = 2;
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(wallTriangles, 0);
        mesh.SetTriangles(roofTriangles, 1);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // Picks one of three roof styles per building, seeded so it's consistent
    // for a given variant name: gable (ridge spans the full depth), hip
    // (ridge inset from both ends, so all four sides slope), or flat with a
    // low parapet wall around the edge.
    private static void AddRoof(List<Vector3> verts, List<Vector2> uvs, List<int> wallTris, List<int> roofTris,
        System.Random rng, Vector3[] topCorners, float hw, float hd, float wallH, float roofHeight)
    {
        double style = rng.NextDouble();

        if (style < 0.55) // gable
        {
            AddGableOrHipRoof(verts, uvs, roofTris, topCorners, hw, hd, wallH, roofHeight, 0f);
        }
        else if (style < 0.85) // hip
        {
            float hipInset = Mathf.Min(hd, hw) * Lerp(0.25f, 0.5f, (float)rng.NextDouble());
            AddGableOrHipRoof(verts, uvs, roofTris, topCorners, hw, hd, wallH, roofHeight, hipInset);
        }
        else // flat with parapet
        {
            float slabThickness = Mathf.Max(0.1f, roofHeight * 0.3f);
            AddBox(verts, uvs, roofTris, new Vector3(0f, wallH + slabThickness * 0.5f, 0f), new Vector3(hw, slabThickness * 0.5f, hd));

            float parapetHeight = Mathf.Max(0.2f, roofHeight * 0.5f);
            float t = 0.08f;
            float py = wallH + slabThickness + parapetHeight * 0.5f;
            AddBox(verts, uvs, wallTris, new Vector3(0f, py, -hd + t * 0.5f), new Vector3(hw, parapetHeight * 0.5f, t * 0.5f));
            AddBox(verts, uvs, wallTris, new Vector3(0f, py, hd - t * 0.5f), new Vector3(hw, parapetHeight * 0.5f, t * 0.5f));
            AddBox(verts, uvs, wallTris, new Vector3(-hw + t * 0.5f, py, 0f), new Vector3(t * 0.5f, parapetHeight * 0.5f, hd));
            AddBox(verts, uvs, wallTris, new Vector3(hw - t * 0.5f, py, 0f), new Vector3(t * 0.5f, parapetHeight * 0.5f, hd));
        }
    }

    // hipInset of 0 gives the original full-depth gable ridge; a positive
    // inset pulls both ridge ends in from the eaves, turning the front/back
    // gable faces into hip slopes and the side faces into trapezoids.
    private static void AddGableOrHipRoof(List<Vector3> verts, List<Vector2> uvs, List<int> roofTris,
        Vector3[] topCorners, float hw, float hd, float wallH, float roofHeight, float hipInset)
    {
        Vector3 ridgeFront = new Vector3(0f, wallH + roofHeight, -hd + hipInset);
        Vector3 ridgeBack = new Vector3(0f, wallH + roofHeight, hd - hipInset);

        AddTriangle(verts, uvs, roofTris, topCorners[0], topCorners[1], ridgeFront,
            new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 1)); // front slope
        AddTriangle(verts, uvs, roofTris, topCorners[3], ridgeBack, topCorners[2],
            new Vector2(0, 0), new Vector2(0.5f, 1), new Vector2(1, 0)); // back slope
        AddQuad(verts, uvs, roofTris, topCorners[1], topCorners[2], ridgeBack, ridgeFront); // right slope
        AddQuad(verts, uvs, roofTris, topCorners[3], topCorners[0], ridgeFront, ridgeBack); // left slope
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    // Axis-aligned box, e.g. chimney/porch posts/parapet — corner layout and
    // winding matches ProceduralStructureBuilder's (confirmed correct) AddBox.
    private static void AddBox(List<Vector3> verts, List<Vector2> uvs, List<int> tris, Vector3 center, Vector3 half)
    {
        Vector3[] corners =
        {
            center + new Vector3(-half.x, -half.y, -half.z), center + new Vector3(half.x, -half.y, -half.z),
            center + new Vector3(half.x, half.y, -half.z), center + new Vector3(-half.x, half.y, -half.z),
            center + new Vector3(-half.x, -half.y, half.z), center + new Vector3(half.x, -half.y, half.z),
            center + new Vector3(half.x, half.y, half.z), center + new Vector3(-half.x, half.y, half.z),
        };

        int[][] faces =
        {
            new[] { 0, 1, 2, 3 }, // back
            new[] { 5, 4, 7, 6 }, // front
            new[] { 4, 0, 3, 7 }, // left
            new[] { 1, 5, 6, 2 }, // right
            new[] { 3, 2, 6, 7 }, // top
            new[] { 4, 5, 1, 0 }, // bottom
        };

        foreach (int[] face in faces)
            AddQuad(verts, uvs, tris, corners[face[0]], corners[face[1]], corners[face[2]], corners[face[3]]);
    }

    private static void AddQuad(List<Vector3> verts, List<Vector2> uvs, List<int> tris, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        AddTriangle(verts, uvs, tris, a, b, c, new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1));
        AddTriangle(verts, uvs, tris, a, c, d, new Vector2(0, 0), new Vector2(1, 1), new Vector2(0, 1));
    }

    private static void AddTriangle(List<Vector3> verts, List<Vector2> uvs, List<int> tris,
        Vector3 a, Vector3 b, Vector3 c, Vector2 uvA, Vector2 uvB, Vector2 uvC)
    {
        int start = verts.Count;
        verts.Add(a); verts.Add(c); verts.Add(b); // swapped: flips winding so faces render outward
        uvs.Add(uvA); uvs.Add(uvC); uvs.Add(uvB);
        tris.Add(start); tris.Add(start + 1); tris.Add(start + 2);
    }
}
