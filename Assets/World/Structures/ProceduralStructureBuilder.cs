using System.Collections.Generic;
using UnityEngine;

// Builds a large, weathered/ruined structure: perimeter walls with a
// per-segment randomized height and a chance of being missing entirely
// (collapsed sections), an optional tilted/missing roof, pillars snapped off
// at random heights, scattered rubble debris, and an occasional collapsed
// ramp/slab leaning against it — driven by a seed unique to the structure's
// name, so even two variants with identical width/height parameters end up
// visually distinct. The doorway opening itself is always guaranteed (never
// randomized away), so the structure is always enterable. Two submeshes:
// 0 = walls + pillars + rubble (wall material), 1 = roof (roof material).
public static class ProceduralStructureBuilder
{
    public static Mesh Build(StructureDefinition def)
    {
        var rng = new System.Random(def.name.GetHashCode());
        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var wallTriangles = new List<int>();
        var roofTriangles = new List<int>();

        float hw = def.width * 0.5f;
        float hd = def.depth * 0.5f;
        float wallH = Mathf.Max(1f, def.wallHeight);
        float t = Mathf.Clamp(def.wallThickness, 0.15f, 2f);
        float doorHalf = Mathf.Clamp(def.doorWidth * 0.5f, 0.5f, hw - 0.4f);
        float doorHeight = Mathf.Clamp(def.doorHeight, 1.5f, wallH - 0.3f);

        // Perimeter walls, each with its own random height/collapse chance.
        AddRuinedWall(vertices, uvs, wallTriangles, rng, new Vector3(-hw, 0, hd - t * 0.5f), new Vector3(hw, 0, hd - t * 0.5f), wallH, t, 0.9f);
        AddRuinedWall(vertices, uvs, wallTriangles, rng, new Vector3(-hw + t * 0.5f, 0, -hd), new Vector3(-hw + t * 0.5f, 0, hd), wallH, t, 0.85f);
        AddRuinedWall(vertices, uvs, wallTriangles, rng, new Vector3(hw - t * 0.5f, 0, -hd), new Vector3(hw - t * 0.5f, 0, hd), wallH, t, 0.85f);

        // Front wall: two segments flanking the doorway (the gap between
        // them is never filled, guaranteeing entry) plus a lintel that can
        // still be ruined away like anything else.
        float frontZ = -hd + t * 0.5f;
        float sideSegHalfWidth = (hw - doorHalf) * 0.5f;
        if (sideSegHalfWidth > 0.05f)
        {
            AddRuinedWall(vertices, uvs, wallTriangles, rng, new Vector3(-hw, 0, frontZ), new Vector3(-doorHalf, 0, frontZ), wallH, t, 0.75f);
            AddRuinedWall(vertices, uvs, wallTriangles, rng, new Vector3(doorHalf, 0, frontZ), new Vector3(hw, 0, frontZ), wallH, t, 0.75f);
        }

        float lintelHeight = wallH - doorHeight;
        if (rng.NextDouble() < 0.7)
            AddBox(vertices, uvs, wallTriangles, new Vector3(0, doorHeight + lintelHeight * 0.5f, frontZ), new Vector3(doorHalf, lintelHeight * 0.5f, t * 0.5f));

        // Roof: often missing entirely on a ruin; when present, sometimes
        // tilted as if partially caved in.
        if (rng.NextDouble() < 0.55)
        {
            float overhang = Mathf.Max(0.4f, Mathf.Min(hw, hd) * 0.06f);
            float roofThickness = Mathf.Max(0.15f, wallH * 0.03f);
            float tiltDeg = (float)(rng.NextDouble() * 10.0 - 5.0);
            Quaternion roofTilt = Quaternion.Euler(tiltDeg, 0f, tiltDeg * 0.6f);
            AddRotatedBox(vertices, uvs, roofTriangles, new Vector3(0, wallH + roofThickness * 0.5f, 0),
                new Vector3(hw + overhang, roofThickness * 0.5f, hd + overhang), roofTilt);
        }

        // Interior pillars, some snapped off partway up.
        int pillars = Mathf.Clamp(def.pillarCount, 0, 8);
        float pillarRadius = Mathf.Max(0.2f, Mathf.Min(hw, hd) * 0.04f);
        for (int i = 0; i < pillars; i++)
        {
            bool twoRows = pillars > 2;
            int perRow = twoRows ? Mathf.CeilToInt(pillars / 2f) : pillars;
            int row = twoRows ? i / perRow : 0;
            int indexInRow = twoRows ? i % perRow : i;

            float px = twoRows ? Mathf.Lerp(-hw * 0.3f, hw * 0.3f, row) : 0f;
            float pz = perRow == 1 ? 0f : Mathf.Lerp(-hd * 0.3f, hd * 0.5f, indexInRow / (float)(perRow - 1));

            float ph = wallH * Lerp(0.4f, 1f, (float)rng.NextDouble());
            AddBox(vertices, uvs, wallTriangles, new Vector3(px, ph * 0.5f, pz), new Vector3(pillarRadius, ph * 0.5f, pillarRadius));
        }

        // Scattered rubble/debris around the base — tilted, randomly sized
        // boxes, like fallen masonry or construction-site clutter.
        int rubbleCount = rng.Next(4, 10);
        for (int i = 0; i < rubbleCount; i++)
        {
            float rx = ((float)rng.NextDouble() * 2f - 1f) * (hw + Mathf.Min(hw, hd) * 0.2f);
            float rz = ((float)rng.NextDouble() * 2f - 1f) * (hd + Mathf.Min(hw, hd) * 0.2f);
            float size = Lerp(0.3f, 1.4f, (float)rng.NextDouble()) * Mathf.Max(1f, Mathf.Min(hw, hd) * 0.05f);
            Vector3 half = new Vector3(size, size * 0.5f, size * 0.7f);
            Quaternion rot = Quaternion.Euler(
                (float)(rng.NextDouble() * 40 - 20),
                (float)rng.NextDouble() * 360f,
                (float)(rng.NextDouble() * 40 - 20));
            AddRotatedBox(vertices, uvs, wallTriangles, new Vector3(rx, half.y, rz), half, rot);
        }

        // Occasional collapsed ramp/slab leaning against the structure.
        if (rng.NextDouble() < 0.6)
        {
            float rampLength = Mathf.Min(hw, hd) * Lerp(0.8f, 1.6f, (float)rng.NextDouble());
            float rampAngleDeg = Lerp(20f, 45f, (float)rng.NextDouble());
            float rampSide = rng.NextDouble() < 0.5 ? -1f : 1f;
            float rampAngleRad = rampAngleDeg * Mathf.Deg2Rad;
            Vector3 rampBase = new Vector3(
                rampSide * (hw + rampLength * 0.5f * Mathf.Cos(rampAngleRad)),
                rampLength * 0.5f * Mathf.Sin(rampAngleRad),
                Lerp(-hd * 0.5f, hd * 0.5f, (float)rng.NextDouble()));
            Quaternion rampRot = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, rampSide * rampAngleDeg);
            AddRotatedBox(vertices, uvs, wallTriangles, rampBase, new Vector3(rampLength * 0.5f, 0.15f, Mathf.Min(hw, hd) * 0.4f), rampRot);
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

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    // A straight axis-aligned wall segment from `from` to `to`, with a
    // randomized height fraction and a chance of being omitted entirely
    // (a collapsed section).
    private static void AddRuinedWall(List<Vector3> verts, List<Vector2> uvs, List<int> tris, System.Random rng,
        Vector3 from, Vector3 to, float nominalHeight, float thickness, float keepChance)
    {
        if (rng.NextDouble() > keepChance) return; // this section has collapsed entirely

        float height = nominalHeight * Lerp(0.35f, 1f, (float)rng.NextDouble());
        Vector3 dir = to - from;
        float length = dir.magnitude;
        if (length < 0.05f) return;

        bool alongX = Mathf.Abs(dir.x) > Mathf.Abs(dir.z);
        Vector3 halfExtents = alongX
            ? new Vector3(length * 0.5f, height * 0.5f, thickness * 0.5f)
            : new Vector3(thickness * 0.5f, height * 0.5f, length * 0.5f);

        Vector3 mid = (from + to) * 0.5f + new Vector3(0f, height * 0.5f, 0f);
        AddBox(verts, uvs, tris, mid, halfExtents);
    }

    // Axis-aligned box — walls/pillars/lintel never need rotation.
    private static void AddBox(List<Vector3> verts, List<Vector2> uvs, List<int> tris, Vector3 center, Vector3 halfExtents)
    {
        AddRotatedBox(verts, uvs, tris, center, halfExtents, Quaternion.identity);
    }

    // Rotated box — used for rubble, ramps, and tilted roofs. Corner layout
    // and winding copied from ProceduralRockBuilder's (confirmed correct) AddBox.
    private static void AddRotatedBox(List<Vector3> verts, List<Vector2> uvs, List<int> tris, Vector3 center, Vector3 halfExtents, Quaternion rotation)
    {
        Vector3[] corners =
        {
            new Vector3(-1, -1, -1), new Vector3(1, -1, -1), new Vector3(1, 1, -1), new Vector3(-1, 1, -1),
            new Vector3(-1, -1, 1), new Vector3(1, -1, 1), new Vector3(1, 1, 1), new Vector3(-1, 1, 1),
        };
        for (int i = 0; i < corners.Length; i++)
            corners[i] = center + rotation * Vector3.Scale(corners[i], halfExtents);

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
            AddTriangle(verts, uvs, tris, corners[face[0]], corners[face[1]], corners[face[2]], new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1));
            AddTriangle(verts, uvs, tris, corners[face[0]], corners[face[2]], corners[face[3]], new Vector2(0, 0), new Vector2(1, 1), new Vector2(0, 1));
        }
    }

    private static void AddTriangle(List<Vector3> verts, List<Vector2> uvs, List<int> tris,
        Vector3 a, Vector3 b, Vector3 c, Vector2 uvA, Vector2 uvB, Vector2 uvC)
    {
        int start = verts.Count;
        verts.Add(a); verts.Add(c); verts.Add(b); // matches ProceduralRockBuilder's confirmed-correct winding
        uvs.Add(uvA); uvs.Add(uvC); uvs.Add(uvB);
        tris.Add(start); tris.Add(start + 1); tris.Add(start + 2);
    }
}
