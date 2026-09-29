using System.Collections.Generic;
using UnityEngine;

// Builds a low-poly rock as a cluster of overlapping, randomly rotated/scaled
// boxes — simple and always well-formed regardless of what sizes come back
// from Gemini, and reads as "a jumble of rock" once flat-shaded. The seed is
// derived from the rock's name so the same definition always builds the same
// shape (matters since chunks get rebuilt/reused as the player roams).
public static class ProceduralRockBuilder
{
    private static readonly Vector2[] QuadUVs = { new(0, 0), new(1, 0), new(1, 1), new(0, 1) };

    public static Mesh Build(RockDefinition def)
    {
        var rng = new System.Random(def.name.GetHashCode());
        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();

        int count = Mathf.Clamp(def.clusterCount, 1, 6);
        for (int i = 0; i < count; i++)
        {
            float size = def.baseRadius * RandomRange(rng, 0.5f, 1f);
            Vector3 halfExtents = new Vector3(
                size * RandomRange(rng, 0.6f, 1f),
                size * RandomRange(rng, 0.6f, 1f),
                size * RandomRange(rng, 0.6f, 1f));

            Vector3 offset = new Vector3(
                RandomRange(rng, -def.baseRadius * 0.3f, def.baseRadius * 0.3f),
                halfExtents.y * 0.6f, // keep most of each box above y=0
                RandomRange(rng, -def.baseRadius * 0.3f, def.baseRadius * 0.3f));

            Quaternion rotation = Quaternion.Euler(
                RandomRange(rng, -15f, 15f),
                (float)rng.NextDouble() * 360f,
                RandomRange(rng, -15f, 15f));

            AddBox(vertices, uvs, triangles, halfExtents, offset, rotation);
        }

        var mesh = new Mesh { name = def.name };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static float RandomRange(System.Random rng, float min, float max) => min + (float)rng.NextDouble() * (max - min);

    private static void AddBox(List<Vector3> verts, List<Vector2> uvs, List<int> tris, Vector3 halfExtents, Vector3 offset, Quaternion rotation)
    {
        Vector3[] corners =
        {
            new Vector3(-1, -1, -1), new Vector3(1, -1, -1), new Vector3(1, 1, -1), new Vector3(-1, 1, -1),
            new Vector3(-1, -1, 1), new Vector3(1, -1, 1), new Vector3(1, 1, 1), new Vector3(-1, 1, 1),
        };
        for (int i = 0; i < corners.Length; i++)
            corners[i] = offset + rotation * Vector3.Scale(corners[i], halfExtents);

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
        {
            AddTriangle(verts, uvs, tris, corners[face[0]], corners[face[1]], corners[face[2]], QuadUVs[0], QuadUVs[1], QuadUVs[2]);
            AddTriangle(verts, uvs, tris, corners[face[0]], corners[face[2]], corners[face[3]], QuadUVs[0], QuadUVs[2], QuadUVs[3]);
        }
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
