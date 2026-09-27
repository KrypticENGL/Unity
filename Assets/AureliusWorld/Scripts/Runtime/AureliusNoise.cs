using System;
using UnityEngine;

namespace Aurelius.World
{
    /// <summary>
    /// Deterministic, thread-safe 2D gradient noise with analytic derivatives, plus the fractal
    /// variants the world generator layers together (fBm, ridged multifractal, erosion-damped fBm).
    /// Same seed = same values on every machine; no UnityEngine.Random or Mathf.PerlinNoise.
    /// </summary>
    public sealed class AureliusNoise
    {
        readonly int[] perm = new int[512];
        readonly float[] gradX = new float[256];
        readonly float[] gradY = new float[256];

        public AureliusNoise(int seed)
        {
            var rng = new System.Random(seed);
            var p = new int[256];
            for (int i = 0; i < 256; i++) p[i] = i;
            for (int i = 255; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (p[i], p[j]) = (p[j], p[i]);
            }
            for (int i = 0; i < 512; i++) perm[i] = p[i & 255];
            for (int i = 0; i < 256; i++)
            {
                double a = rng.NextDouble() * Math.PI * 2.0;
                gradX[i] = (float)Math.Cos(a);
                gradY[i] = (float)Math.Sin(a);
            }
        }

        /// <summary>Gradient noise in roughly [-1, 1] with its partial derivatives.</summary>
        public float Noise(float x, float y, out float dx, out float dy)
        {
            int ix = FastFloor(x), iy = FastFloor(y);
            float fx = x - ix, fy = y - iy;
            int x0 = ix & 255, y0 = iy & 255, x1 = (x0 + 1) & 255, y1 = (y0 + 1) & 255;

            int h00 = perm[perm[x0] + y0], h10 = perm[perm[x1] + y0];
            int h01 = perm[perm[x0] + y1], h11 = perm[perm[x1] + y1];

            float ax = gradX[h00], ay = gradY[h00];
            float bx = gradX[h10], by = gradY[h10];
            float cx = gradX[h01], cy = gradY[h01];
            float ex = gradX[h11], ey = gradY[h11];

            float a = ax * fx + ay * fy;
            float b = bx * (fx - 1f) + by * fy;
            float c = cx * fx + cy * (fy - 1f);
            float d = ex * (fx - 1f) + ey * (fy - 1f);

            float u = fx * fx * fx * (fx * (fx * 6f - 15f) + 10f);
            float v = fy * fy * fy * (fy * (fy * 6f - 15f) + 10f);
            float du = 30f * fx * fx * (fx * (fx - 2f) + 1f);
            float dv = 30f * fy * fy * (fy * (fy - 2f) + 1f);

            float k = a - b - c + d;
            float n = a + u * (b - a) + v * (c - a) + u * v * k;
            dx = ax + u * (bx - ax) + v * (cx - ax) + u * v * (ax - bx - cx + ex) + du * ((b - a) + v * k);
            dy = ay + u * (by - ay) + v * (cy - ay) + u * v * (ay - by - cy + ey) + dv * ((c - a) + u * k);

            const float scale = 1.41f;
            dx *= scale; dy *= scale;
            return n * scale;
        }

        public float Noise(float x, float y) => Noise(x, y, out _, out _);

        /// <summary>Classic fBm in roughly [-1, 1].</summary>
        public float Fbm(float x, float y, int octaves, float lacunarity = 2.03f, float gain = 0.5f)
        {
            float sum = 0f, amp = 1f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += Noise(x, y) * amp;
                norm += amp;
                amp *= gain;
                // Rotate each octave slightly so lattice artifacts never line up.
                float nx = x * 1.6f - y * 1.2f;
                float ny = x * 1.2f + y * 1.6f;
                x = nx * (lacunarity / 2f) + 17.13f;
                y = ny * (lacunarity / 2f) - 9.71f;
            }
            return sum / norm;
        }

        /// <summary>
        /// Erosion-like fBm: octaves are damped where the accumulated slope is steep, so detail
        /// collects in valleys and flats while slopes stay clean (gullies instead of noise).
        /// Returns roughly [-1, 1].
        /// </summary>
        public float ErodedFbm(float x, float y, int octaves, float gain = 0.5f, float erosion = 1f)
        {
            float sum = 0f, amp = 1f, norm = 0f, sx = 0f, sy = 0f;
            for (int i = 0; i < octaves; i++)
            {
                float n = Noise(x, y, out float dx, out float dy);
                sx += dx * amp; sy += dy * amp;
                sum += amp * n / (1f + erosion * (sx * sx + sy * sy));
                norm += amp;
                amp *= gain;
                float nx = x * 1.6f - y * 1.2f;
                float ny = x * 1.2f + y * 1.6f;
                x = nx + 31.7f;
                y = ny - 11.3f;
            }
            return sum / norm;
        }

        /// <summary>Ridged multifractal in roughly [0, 1]: sharp crests, soft valleys.</summary>
        public float Ridged(float x, float y, int octaves, float gain = 0.5f, float sharpness = 2f)
        {
            float sum = 0f, amp = 0.5f, weight = 1f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                float n = 1f - Math.Abs(Noise(x, y));
                n = (float)Math.Pow(n, sharpness);
                n *= weight;
                weight = Clamp01(n * 2f);
                sum += n * amp;
                norm += amp;
                amp *= gain;
                float nx = x * 1.6f - y * 1.2f;
                float ny = x * 1.2f + y * 1.6f;
                x = nx - 5.3f;
                y = ny + 23.9f;
            }
            return sum / norm;
        }

        /// <summary>Noise sampled on a circle, seamless over 0..360 degrees. Roughly [-1, 1].</summary>
        public float Periodic(float degrees, float frequency, int octaves, float offset)
        {
            float a = degrees * Mathf.Deg2Rad;
            return Fbm(Mathf.Cos(a) * frequency + offset, Mathf.Sin(a) * frequency - offset, octaves);
        }

        static int FastFloor(float v) { int i = (int)v; return v < i ? i - 1 : i; }
        static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        // ------------------------------------------------------------------------------------
        // Integer hashing (shared bit-for-bit with the terrain shader for farm parcels).
        // ------------------------------------------------------------------------------------

        public static uint Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)x * 73856093u ^ (uint)y * 19349663u ^ (uint)seed * 83492791u;
                h ^= h >> 13;
                h *= 0x5bd1e995u;
                h ^= h >> 15;
                return h;
            }
        }

        public static float Hash01(int x, int y, int seed) => (Hash(x, y, seed) & 0xFFFFFF) / 16777216f;

        public static float Hash01(int x, int y, int seed, int channel) => Hash01(x, y, seed * 31 + channel * 7919);
    }
}
