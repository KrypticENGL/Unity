using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Aurelius.World.EditorTools
{
    /// <summary>Per-tile samples of the world field at heightmap resolution.</summary>
    public sealed class TileSamples
    {
        public int res;
        public float spacing;
        public Vector2 min;
        public float[] height, mountain, forest, farm, rocky, grass, water, river, riverBank, path, majorPath, city, cityBlend, lakeDist;

        public TileSamples(int res)
        {
            this.res = res;
            int n = res * res;
            height = new float[n]; mountain = new float[n]; forest = new float[n]; farm = new float[n]; rocky = new float[n];
            grass = new float[n]; water = new float[n]; river = new float[n]; riverBank = new float[n]; path = new float[n];
            majorPath = new float[n]; city = new float[n]; cityBlend = new float[n]; lakeDist = new float[n];
        }

        public static TileSamples Compute(AureliusWorldField field, Vector2 tileMin, float tileSize, int res)
        {
            var ts = new TileSamples(res) { min = tileMin, spacing = tileSize / (res - 1) };
            Parallel.For(0, res, y =>
            {
                for (int x = 0; x < res; x++)
                {
                    var s = field.Sample(tileMin.x + x * ts.spacing, tileMin.y + y * ts.spacing);
                    int i = y * res + x;
                    ts.height[i] = s.height; ts.mountain[i] = s.mountain; ts.forest[i] = s.forest; ts.farm[i] = s.farm;
                    ts.rocky[i] = s.rocky; ts.grass[i] = s.grass; ts.water[i] = s.water; ts.river[i] = s.river;
                    ts.riverBank[i] = s.riverBank; ts.path[i] = s.path; ts.majorPath[i] = s.majorPath; ts.city[i] = s.city;
                    ts.cityBlend[i] = s.cityBlend; ts.lakeDist[i] = Mathf.Clamp(s.lakeDistance, -1e4f, 1e4f);
                }
            });
            return ts;
        }

        /// <summary>Bilinear sample of an array at tile-local metres.</summary>
        public float Sample(float[] a, float lx, float lz)
        {
            float fx = Mathf.Clamp(lx / spacing, 0, res - 1.001f), fz = Mathf.Clamp(lz / spacing, 0, res - 1.001f);
            int x = (int)fx, z = (int)fz;
            float tx = fx - x, tz = fz - z;
            int i = z * res + x;
            return Mathf.Lerp(Mathf.Lerp(a[i], a[i + 1], tx), Mathf.Lerp(a[i + res], a[i + res + 1], tx), tz);
        }

        /// <summary>Slope in degrees from the height array.</summary>
        public float Slope(float lx, float lz)
        {
            float d = spacing;
            float hx = Sample(height, lx + d, lz) - Sample(height, lx - d, lz);
            float hz = Sample(height, lx, lz + d) - Sample(height, lx, lz - d);
            return Mathf.Atan(Mathf.Sqrt(hx * hx + hz * hz) / (2f * d)) * Mathf.Rad2Deg;
        }
    }

    /// <summary>
    /// Paints the 8 biome splat layers from the world masks + slope + altitude, and exports the
    /// mask textures (MaskA = Mountain, Forest, Farm, Rocky / MaskB = Water, River, Path, City).
    /// Cliff rock, mountain rock and snow are added procedurally by the terrain shader.
    /// </summary>
    public static class AureliusBiomePainter
    {
        public static void Paint(AureliusWorldSettings s, Terrain terrain, AureliusTerrainTile tile, TileSamples ts)
        {
            var td = terrain.terrainData;
            int ares = td.alphamapResolution;
            int layers = td.alphamapLayers;
            float tileSize = td.size.x;
            var alpha = new float[ares, ares, layers];
            var noise = new AureliusNoise(s.seed * 19 + 7);
            int fieldSeed = AureliusAssetFactory.FieldSeed(s);
            Vector2 min = ts.min;

            Parallel.For(0, ares, y =>
            {
                var w = new float[8];
                for (int x = 0; x < ares; x++)
                {
                    float lx = (x + 0.5f) / ares * tileSize, lz = (y + 0.5f) / ares * tileSize;
                    float wx = min.x + lx, wz = min.y + lz;
                    float h = ts.Sample(ts.height, lx, lz);
                    float slope = ts.Slope(lx, lz);
                    float mountain = ts.Sample(ts.mountain, lx, lz), forest = ts.Sample(ts.forest, lx, lz);
                    float farm = ts.Sample(ts.farm, lx, lz), rocky = ts.Sample(ts.rocky, lx, lz);
                    float water = ts.Sample(ts.water, lx, lz), river = ts.Sample(ts.river, lx, lz);
                    float bank = ts.Sample(ts.riverBank, lx, lz), path = ts.Sample(ts.path, lx, lz);
                    float major = ts.Sample(ts.majorPath, lx, lz), city = ts.Sample(ts.city, lx, lz);
                    float lakeD = ts.Sample(ts.lakeDist, lx, lz);

                    float n1 = noise.Fbm(wx / 70f, wz / 70f, 3) * 0.5f + 0.5f;
                    float n2 = noise.Noise(wx / 23f + 50f, wz / 23f - 50f) * 0.5f + 0.5f;
                    float n3 = noise.Fbm(wx / 240f - 90f, wz / 240f + 30f, 2) * 0.5f + 0.5f;

                    for (int k = 0; k < 8; k++) w[k] = 0f;

                    // Base land cover
                    w[0] = 1f;                                                   // grass
                    w[1] = AureliusWorldField.SmoothStep(0.5f, 0.8f, n3) * 0.9f + forest * 0.5f + mountain * 0.3f; // dark grass
                    w[2] = AureliusWorldField.SmoothStep(0.2f, 0.55f, forest) * (0.8f + 0.4f * n1);      // forest ground
                    float farmW = AureliusWorldField.SmoothStep(0.3f, 0.55f, farm);
                    w[3] = farmW * 2.2f;                                         // farm parcels
                    float rockW = rocky * AureliusWorldField.SmoothStep(0.45f, 0.75f, n1 + rocky * 0.25f)
                                  + mountain * AureliusWorldField.SmoothStep(0.25f, 0.8f, mountain) * (0.4f + 0.6f * n2)
                                  + AureliusWorldField.SmoothStep(24f, 36f, slope + (n2 - 0.5f) * 8f) * 1.5f;
                    w[5] = rockW;                                                 // rocky ground
                    if (h > s.snowLine * 0.6f) w[5] += 0.8f;

                    // Farm tracks along some parcel borders ("natural dirt paths between fields").
                    if (farmW > 0.2f)
                    {
                        FieldEdge(wx, wz, s.fieldSize, s.fieldRotation, fieldSeed, out float edge, out float pairHash);
                        if (edge < 1.7f && pairHash < 0.38f) w[4] += farmW * 3f;
                    }

                    // Water margins
                    float normalized = 0f;
                    for (int k = 0; k < 8; k++) normalized += w[k];
                    for (int k = 0; k < 8; k++) w[k] /= normalized;

                    float shore = 0f;
                    if (lakeD < 1e3f)
                    {
                        float beach = 14f + n1 * 10f;
                        shore = 1f - AureliusWorldField.SmoothStep(beach * 0.6f, beach, lakeD);
                        shore *= 1f - AureliusWorldField.SmoothStep(s.lakeWaterLevel + 1.5f, s.lakeWaterLevel + 3.5f, h);
                        // Rocky shoreline sections
                        float rockyShore = AureliusWorldField.SmoothStep(0.62f, 0.72f, n3);
                        Blend(w, 5, shore * rockyShore);
                        Blend(w, 6, shore * (1f - rockyShore));
                        if (h < s.lakeWaterLevel - 2.5f) Blend(w, 7, 0.6f); // deeper lake bed
                    }
                    if (h < s.seaLevel + 2f) Blend(w, 6, 1f - AureliusWorldField.SmoothStep(s.seaLevel - 4f, s.seaLevel + 2f, h) * 0.3f);
                    Blend(w, 7, Mathf.Max(bank * 0.85f, river));
                    Blend(w, 6, river * 0.35f * n2);

                    // Paths (majors are wider & cleaner)
                    float pathW = Mathf.Max(path * (0.75f + 0.25f * n2), major);
                    Blend(w, 4, Mathf.Clamp01(pathW * 1.1f));

                    // City: clean grass with a soft ring marking the reserved boundary.
                    if (city > 0.5f)
                    {
                        float r = Vector2.Distance(new Vector2(wx, wz), s.cityCenter);
                        for (int k = 0; k < 8; k++) w[k] = 0f;
                        w[0] = 1f;
                        w[1] = AureliusWorldField.SmoothStep(0.55f, 0.85f, n3) * 0.35f;
                        float ring = 1f - AureliusWorldField.SmoothStep(2f, 5f, Mathf.Abs(r - (s.cityRadius - 3f)));
                        Blend(w, 4, ring * 0.7f);
                    }

                    float sum = 0f;
                    for (int k = 0; k < 8; k++) sum += w[k];
                    for (int k = 0; k < layers && k < 8; k++) alpha[y, x, k] = w[k] / sum;
                }
            });
            td.SetAlphamaps(0, 0, alpha);

            ExportMasks(s, tile, ts, td.size.x);
        }

        static void Blend(float[] w, int layer, float t)
        {
            t = Mathf.Clamp01(t);
            if (t <= 0f) return;
            for (int k = 0; k < 8; k++) w[k] *= 1f - t;
            w[layer] += t;
        }

        /// <summary>Same parcel Voronoi as the terrain shader (AureliusFieldCell).</summary>
        public static void FieldEdge(float wx, float wz, float size, float rotation, int seed, out float edgeMeters, out float pairHash)
        {
            const float stretch = 1.5f;
            float a = rotation * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
            float fx = (wx * c + wz * s) / (size * stretch), fy = (-wx * s + wz * c) / size;
            int bx = Mathf.FloorToInt(fx), by = Mathf.FloorToInt(fy);
            float f1 = 1e5f, f2 = 1e5f; int id1x = bx, id1y = by, id2x = bx, id2y = by;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int cx = bx + dx, cy = by + dy;
                    float jx = 0.12f + 0.76f * AureliusNoise.Hash01(cx, cy, seed, 0);
                    float jy = 0.12f + 0.76f * AureliusNoise.Hash01(cx, cy, seed, 1);
                    float ddx = fx - (cx + jx), ddy = fy - (cy + jy);
                    float d = Mathf.Sqrt(ddx * ddx + ddy * ddy);
                    if (d < f1) { f2 = f1; id2x = id1x; id2y = id1y; f1 = d; id1x = cx; id1y = cy; }
                    else if (d < f2) { f2 = d; id2x = cx; id2y = cy; }
                }
            edgeMeters = (f2 - f1) * 0.5f * size;
            int lo = id1x * 7919 + id1y < id2x * 7919 + id2y ? 1 : 0;
            pairHash = lo == 1 ? AureliusNoise.Hash01(id1x * 131 + id2x, id1y * 137 + id2y, seed + 5)
                               : AureliusNoise.Hash01(id2x * 131 + id1x, id2y * 137 + id1y, seed + 5);
        }

        static void ExportMasks(AureliusWorldSettings s, AureliusTerrainTile tile, TileSamples ts, float tileSize)
        {
            int res = s.maskResolution;
            var a = new Color32[res * res];
            var b = new Color32[res * res];
            Parallel.For(0, res, y =>
            {
                for (int x = 0; x < res; x++)
                {
                    float lx = (x + 0.5f) / res * tileSize, lz = (y + 0.5f) / res * tileSize;
                    a[y * res + x] = new Color32(B(ts.Sample(ts.mountain, lx, lz)), B(ts.Sample(ts.forest, lx, lz)), B(ts.Sample(ts.farm, lx, lz)), B(ts.Sample(ts.rocky, lx, lz)));
                    b[y * res + x] = new Color32(B(ts.Sample(ts.water, lx, lz)), B(ts.Sample(ts.river, lx, lz)), B(ts.Sample(ts.path, lx, lz)), B(ts.Sample(ts.city, lx, lz)));
                }
            });
            tile.maskA = SaveMask($"{AureliusAssetFactory.MasksDir}/Mask_{tile.name}_A.asset", res, a);
            tile.maskB = SaveMask($"{AureliusAssetFactory.MasksDir}/Mask_{tile.name}_B.asset", res, b);
            EditorUtility.SetDirty(tile);
        }

        static byte B(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);

        static Texture2D SaveMask(string path, int res, Color32[] px)
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex == null || tex.width != res)
            {
                if (tex != null) AssetDatabase.DeleteAsset(path);
                tex = new Texture2D(res, res, TextureFormat.RGBA32, true, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                tex.SetPixels32(px);
                tex.Apply(true, false);
                AssetDatabase.CreateAsset(tex, path);
                return tex;
            }
            tex.SetPixels32(px);
            tex.Apply(true, false);
            EditorUtility.SetDirty(tex);
            return tex;
        }
    }
}
