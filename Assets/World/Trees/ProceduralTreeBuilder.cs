using System.Collections.Generic;
using UnityEngine;

// Builds a low-poly, flat-shaded tree mesh — a trunk (tapered cylinder),
// stacked cone canopy tiers on top, and optional angled branches (each a
// thin cylinder capped with its own small canopy cone) — from a
// TreeDefinition's parameters. Segment count is fixed (not LLM-controlled)
// so the topology is always well-formed regardless of what sizes/angles
// Gemini picks. Two submeshes: 0 = trunk + branch wood (bark material),
// 1 = canopy tiers + branch tips (leaf material).
public static class ProceduralTreeBuilder
{
    private const int Segments = 6;

    public static Mesh Build(TreeDefinition def)
    {
        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var trunkTriangles = new List<int>();
        var canopyTriangles = new List<int>();

        float trunkTopRadius = def.trunkRadius * 0.7f;
        AddCylinderSide(vertices, uvs, trunkTriangles, Vector3.zero, Vector3.up, def.trunkHeight, def.trunkRadius, trunkTopRadius);
        AddBottomCap(vertices, uvs, trunkTriangles, Vector3.zero, Vector3.up, def.trunkRadius);

        float y = def.trunkHeight;
        foreach (CanopyTier tier in def.canopyTiers)
        {
            y += tier.offset;
            Vector3 basePos = new Vector3(0f, y, 0f);
            AddCone(vertices, uvs, canopyTriangles, basePos, Vector3.up, tier.height, tier.radius);
            y += tier.height * 0.3f; // next tier starts partway up this one, so tiers overlap instead of leaving gaps
        }

        foreach (BranchDefinition branch in def.branches)
        {
            float heightFraction = Mathf.Clamp01(branch.heightFraction);
            Vector3 branchBase = new Vector3(0f, def.trunkHeight * heightFraction, 0f);

            // Direction: tilted away from vertical by angleFromVertical, then
            // rotated around the trunk by azimuth.
            Quaternion rotation = Quaternion.Euler(0f, branch.azimuth, 0f) * Quaternion.Euler(branch.angleFromVertical, 0f, 0f);
            Vector3 direction = rotation * Vector3.up;

            AddCylinderSide(vertices, uvs, trunkTriangles, branchBase, direction, branch.length, branch.radius, branch.radius * 0.6f);

            Vector3 tip = branchBase + direction * branch.length;
            AddCone(vertices, uvs, canopyTriangles, tip, direction, branch.canopyRadius * 1.2f, branch.canopyRadius);
        }

        var mesh = new Mesh { name = def.name };
        mesh.subMeshCount = 2;
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(trunkTriangles, 0);
        mesh.SetTriangles(canopyTriangles, 1);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // No implicit flip here — every call site below passes vertices in the
    // already-correct outward-facing order.
    private static void AddTriangle(List<Vector3> verts, List<Vector2> uvs, List<int> tris,
        Vector3 a, Vector3 b, Vector3 c, Vector2 uvA, Vector2 uvB, Vector2 uvC)
    {
        int start = verts.Count;
        verts.Add(a); verts.Add(b); verts.Add(c);
        uvs.Add(uvA); uvs.Add(uvB); uvs.Add(uvC);
        tris.Add(start); tris.Add(start + 1); tris.Add(start + 2);
    }

    // Two vectors perpendicular to direction, used to build a ring of points
    // around an arbitrary axis (not just straight up) — this is what lets
    // branches point off at any angle using the same code as the trunk.
    // For direction = Vector3.up (the trunk/canopy case), this must reduce to
    // tangent=(1,0,0), bitangent=(0,0,1) to exactly match the ring
    // parametrization the winding was originally verified against — get this
    // wrong and every ring built from it silently reverses (which is exactly
    // what happened here the first time around).
    private static void GetPerpendicularBasis(Vector3 direction, out Vector3 tangent, out Vector3 bitangent)
    {
        Vector3 reference = Mathf.Abs(Vector3.Dot(direction, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up;
        tangent = Vector3.Cross(direction, reference).normalized;
        bitangent = Vector3.Cross(tangent, direction);
    }

    private static void AddCylinderSide(List<Vector3> verts, List<Vector2> uvs, List<int> tris,
        Vector3 basePos, Vector3 direction, float length, float radiusBottom, float radiusTop)
    {
        GetPerpendicularBasis(direction, out Vector3 tangent, out Vector3 bitangent);
        Vector3 topPos = basePos + direction * length;

        for (int i = 0; i < Segments; i++)
        {
            float a0 = i / (float)Segments * Mathf.PI * 2f;
            float a1 = (i + 1) / (float)Segments * Mathf.PI * 2f;
            float u0 = i / (float)Segments;
            float u1 = (i + 1) / (float)Segments;

            Vector3 ring0 = tangent * Mathf.Cos(a0) + bitangent * Mathf.Sin(a0);
            Vector3 ring1 = tangent * Mathf.Cos(a1) + bitangent * Mathf.Sin(a1);

            Vector3 b0 = basePos + ring0 * radiusBottom;
            Vector3 b1 = basePos + ring1 * radiusBottom;
            Vector3 t0 = topPos + ring0 * radiusTop;
            Vector3 t1 = topPos + ring1 * radiusTop;

            Vector2 uvB0 = new Vector2(u0, 0f), uvB1 = new Vector2(u1, 0f);
            Vector2 uvT0 = new Vector2(u0, 1f), uvT1 = new Vector2(u1, 1f);

            AddTriangle(verts, uvs, tris, b0, t0, b1, uvB0, uvT0, uvB1);
            AddTriangle(verts, uvs, tris, b1, t0, t1, uvB1, uvT0, uvT1);
        }
    }

    private static void AddCone(List<Vector3> verts, List<Vector2> uvs, List<int> tris,
        Vector3 basePos, Vector3 direction, float height, float radius)
    {
        GetPerpendicularBasis(direction, out Vector3 tangent, out Vector3 bitangent);
        Vector3 apex = basePos + direction * height;

        for (int i = 0; i < Segments; i++)
        {
            float a0 = i / (float)Segments * Mathf.PI * 2f;
            float a1 = (i + 1) / (float)Segments * Mathf.PI * 2f;
            float u0 = i / (float)Segments;
            float u1 = (i + 1) / (float)Segments;

            Vector3 ring0 = tangent * Mathf.Cos(a0) + bitangent * Mathf.Sin(a0);
            Vector3 ring1 = tangent * Mathf.Cos(a1) + bitangent * Mathf.Sin(a1);
            Vector3 p0 = basePos + ring0 * radius;
            Vector3 p1 = basePos + ring1 * radius;

            AddTriangle(verts, uvs, tris, p0, apex, p1,
                new Vector2(u0, 0f), new Vector2((u0 + u1) * 0.5f, 1f), new Vector2(u1, 0f));
        }
        AddBottomCap(verts, uvs, tris, basePos, direction, radius);
    }

    private static void AddBottomCap(List<Vector3> verts, List<Vector2> uvs, List<int> tris,
        Vector3 basePos, Vector3 direction, float radius)
    {
        GetPerpendicularBasis(direction, out Vector3 tangent, out Vector3 bitangent);
        Vector2 uvCenter = new Vector2(0.5f, 0.5f);

        for (int i = 0; i < Segments; i++)
        {
            float a0 = i / (float)Segments * Mathf.PI * 2f;
            float a1 = (i + 1) / (float)Segments * Mathf.PI * 2f;
            Vector3 ring0 = tangent * Mathf.Cos(a0) + bitangent * Mathf.Sin(a0);
            Vector3 ring1 = tangent * Mathf.Cos(a1) + bitangent * Mathf.Sin(a1);
            Vector3 p0 = basePos + ring0 * radius;
            Vector3 p1 = basePos + ring1 * radius;
            Vector2 uv0 = new Vector2(Mathf.Cos(a0) * 0.5f + 0.5f, Mathf.Sin(a0) * 0.5f + 0.5f);
            Vector2 uv1 = new Vector2(Mathf.Cos(a1) * 0.5f + 0.5f, Mathf.Sin(a1) * 0.5f + 0.5f);

            AddTriangle(verts, uvs, tris, basePos, p0, p1, uvCenter, uv0, uv1); // faces "backward" along -direction
        }
    }
}
