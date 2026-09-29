using UnityEngine;

// Generates a simple grid-line texture at runtime, so you can see terrain
// shape/scale without needing to source or import an image asset.
public static class GridTexture
{
    public static Texture2D Create(int resolution = 256, int cellSize = 32, Color? lineColor = null, Color? fillColor = null)
    {
        Color line = lineColor ?? new Color(0.15f, 0.15f, 0.15f);
        Color fill = fillColor ?? new Color(0.85f, 0.85f, 0.85f);

        var tex = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Bilinear
        };

        var pixels = new Color[resolution * resolution];
        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                bool onLine = (x % cellSize == 0) || (y % cellSize == 0);
                pixels[y * resolution + x] = onLine ? line : fill;
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }
}
