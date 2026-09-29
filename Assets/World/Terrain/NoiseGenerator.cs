using UnityEngine;

// Fractal Perlin noise sampled in world space (not chunk-local), so adjacent
// chunks always agree at shared edges with no seams — never introduce a
// per-chunk random offset here.
public static class NoiseGenerator
{
    public static float Sample(float worldX, float worldZ, int seed, int octaves, float baseScale, float persistence, float lacunarity)
    {
        // Bounded via modulo regardless of how large `seed` is — Mathf.PerlinNoise
        // hashes its input through Mathf.FloorToInt internally, which overflows
        // (undefined/garbage results) for inputs anywhere near int range. A raw
        // `seed * constant` offset can reach that territory if seed is a full
        // 32-bit random value (e.g. from Random.Range(int.MinValue, int.MaxValue)),
        // so keep the offset itself small no matter how large seed gets.
        float seedOffsetX = (seed % 10000) * 1.31f;
        float seedOffsetZ = (seed % 7919) * 1.31f; // different modulus so X/Z don't move in lockstep

        float x = worldX + seedOffsetX;
        float z = worldZ + seedOffsetZ;

        float amplitude = 1f;
        float frequency = 1f;
        float value = 0f;
        float maxPossible = 0f;

        for (int o = 0; o < octaves; o++)
        {
            // Rotate each octave's sampling axes by a fixed angle (the golden
            // angle, so no two octaves ever realign) before scaling by
            // frequency. Mathf.PerlinNoise has its own visible axis-aligned
            // grid, and stacking several octaves that all sample along the
            // same X/Z axes reinforces that grid into an obvious repeating
            // stripe pattern running along one axis. Rotating breaks that
            // reinforcement without affecting seamlessness — it's still a
            // fixed, position-independent transform per octave, so adjacent
            // chunks (which only ever call this with their own worldX/worldZ)
            // still agree at shared edges.
            float angle = o * 2.399963f; // golden angle, radians
            float cos = Mathf.Cos(angle);
            float sin = Mathf.Sin(angle);
            float rotatedX = x * cos - z * sin;
            float rotatedZ = x * sin + z * cos;

            float sampleX = rotatedX / baseScale * frequency;
            float sampleZ = rotatedZ / baseScale * frequency;

            value += Mathf.PerlinNoise(sampleX, sampleZ) * amplitude;
            maxPossible += amplitude;

            amplitude *= persistence;
            frequency *= lacunarity;
        }

        return Mathf.Clamp01(value / maxPossible); // normalized 0..1, clamped as a safety net
    }
}
