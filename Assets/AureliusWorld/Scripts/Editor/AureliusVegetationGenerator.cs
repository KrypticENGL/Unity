using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Aurelius.World.EditorTools
{
    /// <summary>
    /// Scatters trees, bushes and rocks as Terrain tree instances (instanced, LOD'd, distance-culled,
    /// simple colliders, no GameObjects) and grass / flowers as Terrain detail layers. Reads the
    /// terrain's current heights and the exported masks, so hand-sculpted edits are respected.
    /// Placement uses a global jittered grid + integer hashes: deterministic and seamless across tiles.
    /// </summary>
    public static class AureliusVegetationGenerator
    {
        enum Kind { ForestTree, GroveTree, MountainTree, Bush, SmallRock, MediumRock, LargeRock, CliffPiece, MountainRock, BoulderCluster }

        public static void Generate(AureliusWorld world)
        {
            var s = world.settings;
            var lib = AureliusAssetFactory.GetLibrary();
            var protoObjects = lib.TreePrototypes();
            var protos = new TreePrototype[protoObjects.Count];
            for (int i = 0; i < protos.Length; i++) protos[i] = new TreePrototype { prefab = protoObjects[i], bendFactor = 0f };
            int Idx(GameObject g) => protoObjects.IndexOf(g);

            var details = new[]
            {
                Detail(lib.grass, 0.8f, 1.3f, 0.7f, 1.3f),
                Detail(lib.tallGrass, 0.8f, 1.2f, 0.8f, 1.3f),
                Detail(lib.flowerYellow, 0.8f, 1.2f, 0.8f, 1.2f),
                Detail(lib.flowerPink, 0.8f, 1.2f, 0.8f, 1.2f),
            };

            var tiles = AureliusWorldBuilder.Tiles(world);
            long treeTotal = 0, rockTotal = 0, bushTotal = 0;
            for (int ti = 0; ti < tiles.Count; ti++)
            {
                var tile = tiles[ti];
                EditorUtility.DisplayProgressBar("Aurelius World", $"Vegetation {tile.name}", ti / (float)tiles.Count);
                var td = tile.Terrain.terrainData;
                td.treePrototypes = protos;
                td.detailPrototypes = details;
                td.SetDetailScatterMode(DetailScatterMode.InstanceCountMode);
                td.RefreshPrototypes();

                var ctx = new TileContext(tile, s);
                var trees = new List<TreeInstance>();

                // --- Trees & bushes ------------------------------------------------------------------
                ScatterGrid(ctx, s.treeSpacing, 1, (wx, wz, h) =>
                {
                    var m = ctx.Masks(wx, wz);
                    float slope = ctx.Slope(wx, wz);
                    if (m.water > 0.05f || m.river > 0.05f || m.path > 0.08f || m.city > 0f || slope > 33f) return;
                    if (h < s.lakeWaterLevel + 1.4f || h < s.seaLevel + 3f || h > s.snowLine - 30f) return;
                    float nearCity = NearCity(s, wx, wz);
                    float roll = AureliusNoise.Hash01(ctx.GX(wx), ctx.GZ(wz), s.seed, 2);
                    float kindRoll = AureliusNoise.Hash01(ctx.GX(wx), ctx.GZ(wz), s.seed, 3);

                    if (m.farm > 0.45f)
                    {
                        // Farms stay open for future props: only hedgerow bushes on some field edges.
                        AureliusBiomePainter.FieldEdge(wx, wz, s.fieldSize, s.fieldRotation, AureliusAssetFactory.FieldSeed(s), out float edge, out _);
                        if (edge < 3f && roll < 0.22f * nearCity) Add(trees, ctx, wx, h, wz, Idx(lib.bush), 0.7f, 1.2f, s);
                        return;
                    }
                    if (roll < m.forest * nearCity)
                    {
                        bool high = m.mountain > 0.35f || h > 140f;
                        GameObject proto;
                        if (high) proto = lib.mountainTrees[kindRoll < 0.6f ? 0 : 1];
                        else if (m.grass > 0.6f && m.forestCore < 0.3f) proto = lib.groveTrees[kindRoll < 0.5f ? 0 : 1];
                        else proto = lib.forestTrees[Mathf.Min(3, (int)(kindRoll * 4f))];
                        Add(trees, ctx, wx, h, wz, Idx(proto), 0.75f, 1.3f, s);
                        return;
                    }
                    // Forest edges & open meadows: sparse bushes.
                    float bushP = (m.forest > 0.05f ? 0.12f : 0.015f * m.grass) * nearCity;
                    if (roll > 1f - bushP) Add(trees, ctx, wx, h, wz, Idx(lib.bush), 0.6f, 1.3f, s);
                });
                int treeCount = 0, bushCount = 0;
                foreach (var t in trees) { if (t.prototypeIndex == Idx(lib.bush)) bushCount++; else treeCount++; }

                // --- Rocks ---------------------------------------------------------------------------
                int beforeRocks = trees.Count;
                var rockNoise = new AureliusNoise(s.seed * 29 + 3);
                ScatterGrid(ctx, 15f, 2, (wx, wz, h) =>
                {
                    var m = ctx.Masks(wx, wz);
                    if (m.water > 0.05f || m.river > 0.3f || m.path > 0.05f || m.city > 0f) return;
                    if (h < s.lakeWaterLevel + 0.5f || h < s.seaLevel + 1f) return;
                    float slope = ctx.Slope(wx, wz);
                    float p = m.mountain * 0.2f + m.rocky * 0.28f + AureliusWorldField.SmoothStep(24f, 42f, slope) * 0.35f
                              + m.riverBank * 0.12f + m.grass * 0.012f + m.forest * 0.03f;
                    // Rocks gather in outcrops instead of an even sprinkle.
                    float clusterN = rockNoise.Fbm(wx / 160f, wz / 160f, 2) * 0.5f + 0.5f;
                    p *= s.rockDensity * NearCity(s, wx, wz) * AureliusWorldField.SmoothStep(0.4f, 0.75f, clusterN) * 1.6f;
                    float roll = AureliusNoise.Hash01(ctx.GX(wx), ctx.GZ(wz), s.seed, 12);
                    if (roll >= p) return;
                    float k = AureliusNoise.Hash01(ctx.GX(wx), ctx.GZ(wz), s.seed, 13);
                    GameObject proto;
                    if (m.mountain > 0.4f && h > 120f)
                        proto = k < 0.12f ? lib.mountainRock : k < 0.4f ? lib.cliffPiece : k < 0.7f ? lib.largeRock : lib.mediumRock;
                    else if (slope > 34f) proto = k < 0.45f ? lib.cliffPiece : lib.largeRock;
                    else if (m.rocky > 0.35f) proto = k < 0.2f ? lib.largeRock : k < 0.45f ? lib.boulderCluster : k < 0.75f ? lib.mediumRock : lib.smallRock;
                    else proto = k < 0.12f ? lib.boulderCluster : k < 0.45f ? lib.mediumRock : lib.smallRock;
                    Add(trees, ctx, wx, h, wz, Idx(proto), 0.6f, 1.5f, s);
                });
                int rockCount = trees.Count - beforeRocks;

                td.SetTreeInstances(trees.ToArray(), true);
                treeTotal += treeCount; bushTotal += bushCount; rockTotal += rockCount;

                // --- Grass & flowers (detail layers) ------------------------------------------------
                PaintDetails(ctx, td, s);
                EditorUtility.SetDirty(td);
            }

            UpdateInfo(world, "Vegetation/Forest", "Forest, grove and mountain trees (terrain tree instances, LODGroup prefabs, capsule trunk colliders).", treeTotal, lib.forestTrees);
            UpdateInfo(world, "Vegetation/Farms", "Hedgerow and meadow bushes. Farm fields are kept clear for future props.", bushTotal, new[] { lib.bush });
            UpdateInfo(world, "Vegetation/Grass", "Grass, tall grass and flowers (terrain detail layers, GPU instanced, distance culled).", 0, new[] { lib.grass, lib.tallGrass, lib.flowerYellow, lib.flowerPink });
            UpdateInfo(world, "Rocks", "Modular rocks: SmallRock, MediumRock, LargeRock, CliffPiece, MountainRock, BoulderCluster (terrain tree instances, LOD1 convex colliders).", rockTotal,
                new[] { lib.smallRock, lib.mediumRock, lib.largeRock, lib.cliffPiece, lib.mountainRock, lib.boulderCluster });
            EditorUtility.ClearProgressBar();
            Debug.Log($"[Aurelius] Vegetation: {treeTotal:N0} trees, {bushTotal:N0} bushes, {rockTotal:N0} rocks across {tiles.Count} tiles.");
        }

        static DetailPrototype Detail(GameObject prefab, float minW, float maxW, float minH, float maxH) => new DetailPrototype
        {
            prototype = prefab,
            usePrototypeMesh = true,
            renderMode = DetailRenderMode.VertexLit,
            useInstancing = true,
            minWidth = minW, maxWidth = maxW, minHeight = minH, maxHeight = maxH,
            noiseSpread = 0.4f,
            healthyColor = Color.white, dryColor = Color.white,
            alignToGround = 0.6f,
            positionJitter = 0.9f,
            density = 1f,
        };

        static float NearCity(AureliusWorldSettings s, float wx, float wz)
        {
            float r = Vector2.Distance(new Vector2(wx, wz), s.cityCenter);
            float t = AureliusWorldField.SmoothStep(s.cityRadius, s.cityRadius + s.cityTransitionWidth, r);
            return Mathf.Lerp(s.nearCityVegetation, 1f, t);
        }

        static void Add(List<TreeInstance> list, TileContext ctx, float wx, float h, float wz, int proto, float minScale, float maxScale, AureliusWorldSettings s)
        {
            if (proto < 0) return;
            int gx = ctx.GX(wx), gz = ctx.GZ(wz);
            float sc = Mathf.Lerp(minScale, maxScale, AureliusNoise.Hash01(gx, gz, s.seed, 5));
            float aspect = Mathf.Lerp(0.9f, 1.1f, AureliusNoise.Hash01(gx, gz, s.seed, 6));
            list.Add(new TreeInstance
            {
                position = new Vector3((wx - ctx.min.x) / ctx.size.x, 0f, (wz - ctx.min.y) / ctx.size.z),
                prototypeIndex = proto,
                widthScale = sc * aspect,
                heightScale = sc,
                rotation = AureliusNoise.Hash01(gx, gz, s.seed, 7) * Mathf.PI * 2f,
                color = Color.white,
                lightmapColor = Color.white,
            });
        }

        /// <summary>Global jittered grid: cell indices are world-based, so tiles never disagree.</summary>
        static void ScatterGrid(TileContext ctx, float spacing, int channel, System.Action<float, float, float> visit)
        {
            ctx.spacing = spacing;
            var origin = ctx.S.WorldMin;
            int gx0 = Mathf.FloorToInt((ctx.min.x - origin.x) / spacing), gx1 = Mathf.FloorToInt((ctx.min.x + ctx.size.x - origin.x) / spacing);
            int gz0 = Mathf.FloorToInt((ctx.min.y - origin.y) / spacing), gz1 = Mathf.FloorToInt((ctx.min.y + ctx.size.z - origin.y) / spacing);
            for (int gz = gz0; gz <= gz1; gz++)
                for (int gx = gx0; gx <= gx1; gx++)
                {
                    float jx = AureliusNoise.Hash01(gx, gz, ctx.S.seed + channel, 0);
                    float jz = AureliusNoise.Hash01(gx, gz, ctx.S.seed + channel, 1);
                    float wx = origin.x + (gx + jx) * spacing, wz = origin.y + (gz + jz) * spacing;
                    if (wx < ctx.min.x || wz < ctx.min.y || wx >= ctx.min.x + ctx.size.x || wz >= ctx.min.y + ctx.size.z) continue;
                    visit(wx, wz, ctx.Height(wx, wz));
                }
        }

        static void PaintDetails(TileContext ctx, TerrainData td, AureliusWorldSettings s)
        {
            int res = td.detailResolution;
            var grass = new int[res, res];
            var tall = new int[res, res];
            var yellow = new int[res, res];
            var pink = new int[res, res];
            var noise = new AureliusNoise(s.seed * 23 + 1);
            float cell = ctx.size.x / res;
            Parallel.For(0, res, y =>
            {
                for (int x = 0; x < res; x++)
                {
                    float wx = ctx.min.x + (x + 0.5f) * cell, wz = ctx.min.y + (y + 0.5f) * cell;
                    var m = ctx.Masks(wx, wz);
                    if (m.city > 0f || m.water > 0.02f || m.river > 0.1f) continue;
                    float h = ctx.Height(wx, wz);
                    if (h < s.lakeWaterLevel + 1.2f || h > s.snowLine * 0.75f) continue;
                    float slope = ctx.Slope(wx, wz);
                    float ok = (1f - AureliusWorldField.SmoothStep(26f, 38f, slope)) * (1f - Mathf.Clamp01(m.path * 1.5f)) * NearCity(s, wx, wz);
                    float n1 = noise.Fbm(wx / 55f, wz / 55f, 2) * 0.5f + 0.5f;
                    float n2 = noise.Noise(wx / 31f + 40f, wz / 31f) * 0.5f + 0.5f;
                    float n3 = noise.Noise(wx / 27f - 70f, wz / 27f + 11f) * 0.5f + 0.5f;

                    float cover = m.grass * 1f + m.forest * 0.25f + m.farm * 0.12f + m.mountain * 0.35f * (1f - AureliusWorldField.SmoothStep(120f, 260f, h)) + m.riverBank * 0.6f;
                    cover *= ok * s.grassDensity;
                    grass[y, x] = Mathf.RoundToInt(Mathf.Clamp01(cover) * 10f * Mathf.Lerp(0.55f, 1.15f, n1));
                    float meadow = m.grass * AureliusWorldField.SmoothStep(0.55f, 0.75f, n1) * ok * s.grassDensity;
                    tall[y, x] = Mathf.RoundToInt(meadow * 5f);
                    yellow[y, x] = Mathf.RoundToInt(m.grass * AureliusWorldField.SmoothStep(0.72f, 0.85f, n2) * ok * 3f);
                    pink[y, x] = Mathf.RoundToInt(m.grass * AureliusWorldField.SmoothStep(0.75f, 0.88f, n3) * ok * 3f);
                }
            });
            td.SetDetailLayer(0, 0, 0, grass);
            td.SetDetailLayer(0, 0, 1, tall);
            td.SetDetailLayer(0, 0, 2, yellow);
            td.SetDetailLayer(0, 0, 3, pink);
        }

        static void UpdateInfo(AureliusWorld world, string path, string description, long count, IEnumerable<GameObject> protos)
        {
            var t = AureliusWorldBuilder.Child(world.transform, path);
            var info = t.GetComponent<AureliusScatterInfo>();
            if (info == null) info = t.gameObject.AddComponent<AureliusScatterInfo>();
            info.description = description;
            info.instanceCount = (int)count;
            info.prototypes = new List<GameObject>(protos);
            EditorUtility.SetDirty(info);
        }

        struct MaskSample { public float mountain, forest, forestCore, farm, rocky, water, river, riverBank, path, city, grass; }

        /// <summary>Reads heights + masks of one tile into arrays (thread-safe sampling afterwards).</summary>
        sealed class TileContext
        {
            public readonly AureliusWorldSettings S;
            public readonly Vector2 min;
            public readonly Vector3 size;
            public float spacing;
            readonly float baseY;
            readonly float[,] heights;
            readonly int hres;
            readonly Color32[] a, b;
            readonly int mres;

            public TileContext(AureliusTerrainTile tile, AureliusWorldSettings s)
            {
                S = s;
                var td = tile.Terrain.terrainData;
                min = new Vector2(tile.transform.position.x, tile.transform.position.z);
                size = td.size;
                baseY = tile.transform.position.y;
                hres = td.heightmapResolution;
                heights = td.GetHeights(0, 0, hres, hres);
                if (tile.maskA != null && tile.maskB != null && tile.maskA.isReadable)
                {
                    a = tile.maskA.GetPixels32();
                    b = tile.maskB.GetPixels32();
                    mres = tile.maskA.width;
                }
            }

            public int GX(float wx) => Mathf.FloorToInt((wx - S.WorldMin.x) / Mathf.Max(spacing, 0.01f));
            public int GZ(float wz) => Mathf.FloorToInt((wz - S.WorldMin.y) / Mathf.Max(spacing, 0.01f));

            public float Height(float wx, float wz)
            {
                float fx = Mathf.Clamp((wx - min.x) / size.x * (hres - 1), 0, hres - 1.001f);
                float fz = Mathf.Clamp((wz - min.y) / size.z * (hres - 1), 0, hres - 1.001f);
                int x = (int)fx, z = (int)fz;
                float tx = fx - x, tz = fz - z;
                float h = Mathf.Lerp(Mathf.Lerp(heights[z, x], heights[z, x + 1], tx), Mathf.Lerp(heights[z + 1, x], heights[z + 1, x + 1], tx), tz);
                return baseY + h * size.y;
            }

            public float Slope(float wx, float wz)
            {
                float d = size.x / (hres - 1);
                float hx = Height(wx + d, wz) - Height(wx - d, wz);
                float hz = Height(wx, wz + d) - Height(wx, wz - d);
                return Mathf.Atan(Mathf.Sqrt(hx * hx + hz * hz) / (2f * d)) * Mathf.Rad2Deg;
            }

            public MaskSample Masks(float wx, float wz)
            {
                var m = new MaskSample();
                if (a == null) { m.grass = 1f; return m; }
                int x = Mathf.Clamp(Mathf.FloorToInt((wx - min.x) / size.x * mres), 0, mres - 1);
                int z = Mathf.Clamp(Mathf.FloorToInt((wz - min.y) / size.z * mres), 0, mres - 1);
                Color32 ca = a[z * mres + x], cb = b[z * mres + x];
                m.mountain = ca.r / 255f; m.forest = ca.g / 255f; m.farm = ca.b / 255f; m.rocky = ca.a / 255f;
                m.water = cb.r / 255f; m.river = cb.g / 255f; m.path = cb.b / 255f; m.city = cb.a / 255f;
                m.forestCore = m.forest;
                m.riverBank = 0f;
                // River banks: river mask nearby but not on the channel.
                if (m.river < 0.05f)
                {
                    int x2 = Mathf.Clamp(x + 2, 0, mres - 1), z2 = Mathf.Clamp(z + 2, 0, mres - 1);
                    int x0 = Mathf.Clamp(x - 2, 0, mres - 1), z0 = Mathf.Clamp(z - 2, 0, mres - 1);
                    float near = Mathf.Max(Mathf.Max(b[z * mres + x2].g, b[z * mres + x0].g), Mathf.Max(b[z2 * mres + x].g, b[z0 * mres + x].g)) / 255f;
                    m.riverBank = near;
                }
                m.grass = Mathf.Clamp01(1f - Mathf.Max(Mathf.Max(m.mountain, m.forest), Mathf.Max(m.farm, Mathf.Max(m.rocky, m.water))));
                return m;
            }
        }
    }
}
