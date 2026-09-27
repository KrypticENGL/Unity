using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Aurelius.World.EditorTools
{
    /// <summary>
    /// Orchestrates generation. Each step can run alone and regenerates what depends on it:
    /// Lake -> Major corridors -> Rivers -> Secondary paths -> Terrain (heights) -> Biomes -> Water -> Vegetation.
    /// </summary>
    public static class AureliusWorldBuilder
    {
        public static readonly string[] HierarchyPaths =
        {
            "Terrain", "Water", "Water/Rivers", "Water/Ocean", "Vegetation", "Vegetation/Forest", "Vegetation/Grass",
            "Vegetation/Farms", "Rocks", "Paths", "Paths/Major", "Paths/Secondary", "Paths/BridgeSites", "Debug"
        };

        // ======================================================================================
        // Scene structure
        // ======================================================================================

        public static AureliusWorld FindOrCreateWorld()
        {
            var world = UnityEngine.Object.FindFirstObjectByType<AureliusWorld>();
            if (world != null)
            {
                if (world.settings == null) world.settings = AureliusAssetFactory.GetOrCreateSettings();
                return world;
            }
            var go = new GameObject("AureliusWorld");
            Undo.RegisterCreatedObjectUndo(go, "Create Aurelius World");
            world = go.AddComponent<AureliusWorld>();
            world.settings = AureliusAssetFactory.GetOrCreateSettings();
            EnsureHierarchy(world);
            return world;
        }

        public static void EnsureHierarchy(AureliusWorld world)
        {
            foreach (var p in HierarchyPaths) Child(world.transform, p);
            var terrainRoot = world.TerrainRoot;
            if (terrainRoot.GetComponent<AureliusTerrainChunkManager>() == null) terrainRoot.gameObject.AddComponent<AureliusTerrainChunkManager>();
        }

        public static Transform Child(Transform root, string path)
        {
            Transform cur = root;
            foreach (var part in path.Split('/'))
            {
                var next = cur.Find(part);
                if (next == null)
                {
                    next = new GameObject(part).transform;
                    next.SetParent(cur, false);
                }
                cur = next;
            }
            return cur;
        }

        public static void ClearChildren(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--) Undo.DestroyObjectImmediate(t.GetChild(i).gameObject);
        }

        public static AureliusWorldField BuildField(AureliusWorld world) =>
            new AureliusWorldField(world.settings, world.Lake, world.Rivers, world.Paths, CollectLandmarkWater());

        /// <summary>Water surfaces inside the city landmarks (castle moat / canals, fountains...).</summary>
        public static List<WaterCarve> CollectLandmarkWater()
        {
            var result = new List<WaterCarve>();
            foreach (var n in AureliusLandmarkDetector.LandmarkNames)
            {
                var root = GameObject.Find(n);
                if (root == null) continue;
                foreach (var r in root.GetComponentsInChildren<MeshRenderer>())
                {
                    bool water = false;
                    foreach (var m in r.sharedMaterials) if (m != null && m.name.Contains("Water")) water = true;
                    var mf = r.GetComponent<MeshFilter>();
                    if (!water || mf == null || mf.sharedMesh == null) continue;
                    var mesh = mf.sharedMesh;
                    var verts = mesh.vertices;
                    var tris = mesh.triangles;
                    var mtx = r.transform.localToWorldMatrix;
                    var tri2 = new Vector2[tris.Length];
                    for (int i = 0; i < tris.Length; i++)
                    {
                        var w = mtx.MultiplyPoint3x4(verts[tris[i]]);
                        tri2[i] = new Vector2(w.x, w.z);
                    }
                    result.Add(new WaterCarve { triangles = tri2, level = r.bounds.max.y, depth = 4.5f });
                }
            }
            return result;
        }

        // ======================================================================================
        // Top-level commands
        // ======================================================================================

        public static void GenerateAll(AureliusWorld world)
        {
            Run("Generate Aurelius World", () =>
            {
                EnsureHierarchy(world);
                Progress("Lake", 0.02f);
                AureliusWaterBuilder.GenerateLakeShape(world);
                Progress("Major corridors", 0.05f);
                AureliusPathGenerator.GenerateMajor(world);
                Progress("Rivers", 0.08f);
                AureliusRiverGenerator.Generate(world);
                Progress("Secondary paths", 0.16f);
                AureliusPathGenerator.GenerateSecondary(world);
                bool vegetationDone = GenerateTerrainInternal(world, 0.25f, 0.8f);
                Progress("Vegetation & rocks", 0.82f);
                if (!vegetationDone) AureliusVegetationGenerator.Generate(world);
            });
        }

        public static void GenerateTerrain(AureliusWorld world, int onlyTile = -1)
        {
            Run("Generate Terrain", () =>
            {
                EnsureHierarchy(world);
                if (world.Lake == null) AureliusWaterBuilder.GenerateLakeShape(world);
                if (!world.Paths.Exists(p => p.kind == PathKind.Major)) AureliusPathGenerator.GenerateMajor(world);
                GenerateTerrainInternal(world, 0.05f, 0.95f, onlyTile);
            });
        }

        public static void GenerateBiomes(AureliusWorld world)
        {
            Run("Generate Biomes", () =>
            {
                var field = BuildField(world);
                var tiles = Tiles(world);
                for (int i = 0; i < tiles.Count; i++)
                {
                    Progress($"Biomes {tiles[i].name}", i / (float)tiles.Count);
                    var t = tiles[i];
                    var td = t.Terrain.terrainData;
                    var ts = TileSamples.Compute(field, new Vector2(t.transform.position.x, t.transform.position.z), td.size.x, td.heightmapResolution);
                    // Use the terrain's current (possibly hand-sculpted) heights for slope and altitude.
                    OverrideHeights(ts, td, t.transform.position.y);
                    AureliusBiomePainter.Paint(world.settings, t.Terrain, t, ts);
                }
                RefreshTiles(world);
            });
        }

        public static void GenerateRivers(AureliusWorld world)
        {
            Run("Generate Rivers", () =>
            {
                EnsureHierarchy(world);
                if (world.Lake == null) AureliusWaterBuilder.GenerateLakeShape(world);
                if (!world.Paths.Exists(p => p.kind == PathKind.Major)) AureliusPathGenerator.GenerateMajor(world);
                AureliusRiverGenerator.Generate(world);
                if (Tiles(world).Count > 0) GenerateTerrainInternal(world, 0.2f, 0.95f);
            });
        }

        public static void GeneratePaths(AureliusWorld world)
        {
            Run("Generate Paths", () =>
            {
                EnsureHierarchy(world);
                if (world.Lake == null) AureliusWaterBuilder.GenerateLakeShape(world);
                AureliusPathGenerator.GenerateMajor(world);
                AureliusPathGenerator.GenerateSecondary(world);
                if (Tiles(world).Count > 0) GenerateTerrainInternal(world, 0.2f, 0.95f);
            });
        }

        public static void GenerateLake(AureliusWorld world)
        {
            Run("Generate Lake", () =>
            {
                EnsureHierarchy(world);
                AureliusWaterBuilder.GenerateLakeShape(world);
                if (Tiles(world).Count > 0) GenerateTerrainInternal(world, 0.1f, 0.95f);
            });
        }

        public static void GenerateVegetation(AureliusWorld world)
        {
            Run("Generate Vegetation", () => AureliusVegetationGenerator.Generate(world));
        }

        public static void ClearGenerated(AureliusWorld world, bool includeSplines, bool deleteAssets)
        {
            Undo.RegisterFullObjectHierarchyUndo(world.gameObject, "Clear Aurelius World");
            ClearChildren(world.TerrainRoot);
            Child(world.transform, "Water/Ocean");
            foreach (var t in new[] { "Vegetation/Forest", "Vegetation/Grass", "Vegetation/Farms", "Rocks", "Paths/BridgeSites", "Water/Ocean" })
                ClearChildren(Child(world.transform, t));
            foreach (var r in world.Rivers) { var s = r.transform.Find("Surface"); if (s) Undo.DestroyObjectImmediate(s.gameObject); }
            var lake = world.Lake;
            if (lake != null) { var s = lake.transform.Find("Surface"); if (s) Undo.DestroyObjectImmediate(s.gameObject); }
            if (includeSplines)
            {
                ClearChildren(Child(world.transform, "Paths/Major"));
                ClearChildren(Child(world.transform, "Paths/Secondary"));
                ClearChildren(Child(world.transform, "Water/Rivers"));
                if (lake != null) Undo.DestroyObjectImmediate(lake.gameObject);
            }
            if (deleteAssets)
            {
                AssetDatabase.DeleteAsset(AureliusAssetFactory.GeneratedDir);
                AureliusAssetFactory.EnsureFolders();
            }
            EditorSceneManager.MarkSceneDirty(world.gameObject.scene);
        }

        // ======================================================================================
        // Terrain tiles
        // ======================================================================================

        static bool GenerateTerrainInternal(AureliusWorld world, float p0, float p1, int onlyTile = -1)
        {
            var s = world.settings;
            AureliusAssetFactory.EnsureFolders();
            var material = AureliusAssetFactory.GetTerrainMaterial(s);
            var layers = AureliusAssetFactory.GetTerrainLayers();
            var field = BuildField(world);
            int n = s.tilesPerSide;
            var sw = Stopwatch.StartNew();

            RemoveStaleTiles(world, s);
            bool hadVegetation = Tiles(world).Exists(t => t.Terrain.terrainData != null && t.Terrain.terrainData.treeInstanceCount > 0);
            int total = n * n;
            for (int iz = 0; iz < n; iz++)
                for (int ix = 0; ix < n; ix++)
                {
                    int index = iz * n + ix;
                    if (onlyTile >= 0 && index != onlyTile) continue;
                    var tile = GetOrCreateTile(world, s, ix, iz, index, material, layers);
                    Progress($"Heights {tile.name}", Mathf.Lerp(p0, p1, index / (float)total));
                    var td = tile.Terrain.terrainData;
                    var tileMin = new Vector2(tile.transform.position.x, tile.transform.position.z);
                    var ts = TileSamples.Compute(field, tileMin, s.tileSize, s.heightmapResolution);
                    int res = s.heightmapResolution;
                    var hm = new float[res, res];
                    for (int y = 0; y < res; y++)
                        for (int x = 0; x < res; x++)
                            hm[y, x] = Mathf.Clamp01((ts.height[y * res + x] - s.terrainBaseY) / s.terrainHeight);
                    td.SetHeights(0, 0, hm);
                    Progress($"Biomes {tile.name}", Mathf.Lerp(p0, p1, (index + 0.6f) / total));
                    AureliusBiomePainter.Paint(s, tile.Terrain, tile, ts);
                    EditorUtility.SetDirty(td);
                }

            Progress("Water surfaces", p1);
            AureliusWaterBuilder.BuildSurfaces(world, field);
            AureliusPathGenerator.RebuildBridgeSites(world);
            RefreshTiles(world);
            // Heights changed under existing trees / grass: re-scatter so nothing floats or sits in rivers.
            if (hadVegetation)
            {
                Progress("Vegetation & rocks", p1);
                AureliusVegetationGenerator.Generate(world);
            }
            world.ApplyDebugGlobals();
            AssetDatabase.SaveAssets();
            Debug.Log($"[Aurelius] Terrain generated: {total} tiles of {s.tileSize} m ({s.WorldSize} m world) in {sw.Elapsed.TotalSeconds:F1}s.");
            return hadVegetation;
        }

        static void RemoveStaleTiles(AureliusWorld world, AureliusWorldSettings s)
        {
            var root = world.TerrainRoot;
            foreach (var t in root.GetComponentsInChildren<AureliusTerrainTile>(true))
            {
                var td = t.Terrain.terrainData;
                bool stale = td == null || t.coord.x >= s.tilesPerSide || t.coord.y >= s.tilesPerSide ||
                             Mathf.Abs(td.size.x - s.tileSize) > 0.01f || td.heightmapResolution != s.heightmapResolution;
                if (stale) Undo.DestroyObjectImmediate(t.gameObject);
            }
        }

        public static string TileName(AureliusWorldSettings s, int ix, int iz)
        {
            int n = s.tilesPerSide;
            var center = s.WorldMin + new Vector2((ix + 0.5f) * s.tileSize, (iz + 0.5f) * s.tileSize);
            var offset = center - s.cityCenter;
            if (offset.magnitude < s.tileSize * 0.5f) return "Terrain_CenterReserved";
            string[] names = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
            string dir = names[Mathf.RoundToInt(s.Bearing(offset) / 45f) % 8];
            return n == 3 ? $"Terrain_{dir}" : $"Terrain_{ix}_{iz}_{dir}";
        }

        static AureliusTerrainTile GetOrCreateTile(AureliusWorld world, AureliusWorldSettings s, int ix, int iz, int index, Material material, TerrainLayer[] layers)
        {
            var root = world.TerrainRoot;
            AureliusTerrainTile tile = null;
            foreach (var t in root.GetComponentsInChildren<AureliusTerrainTile>(true))
                if (t.coord == new Vector2Int(ix, iz)) tile = t;

            string name = TileName(s, ix, iz);
            string dataPath = $"{AureliusAssetFactory.TerrainDataDir}/TD_{name}.asset";
            TerrainData td;
            if (tile == null)
            {
                td = AssetDatabase.LoadAssetAtPath<TerrainData>(dataPath);
                if (td == null)
                {
                    td = new TerrainData();
                    AssetDatabase.CreateAsset(td, dataPath);
                }
                var go = Terrain.CreateTerrainGameObject(td);
                go.name = name;
                go.transform.SetParent(root, false);
                tile = go.AddComponent<AureliusTerrainTile>();
                Undo.RegisterCreatedObjectUndo(go, "Generate Terrain");
            }
            td = tile.Terrain.terrainData;
            tile.name = name;
            tile.coord = new Vector2Int(ix, iz);
            tile.index = index;

            td.heightmapResolution = s.heightmapResolution;
            td.size = new Vector3(s.tileSize, s.terrainHeight, s.tileSize);
            td.alphamapResolution = s.alphamapResolution;
            td.baseMapResolution = 256;
            td.SetDetailResolution(s.detailResolution, 32);
            td.terrainLayers = layers;

            var pos = s.WorldMin + new Vector2(ix * s.tileSize, iz * s.tileSize);
            tile.transform.position = new Vector3(pos.x, s.terrainBaseY, pos.y);

            var terrain = tile.Terrain;
            terrain.materialTemplate = material;
            terrain.drawInstanced = true;
            terrain.groupingID = 7171;
            terrain.allowAutoConnect = true;
            terrain.basemapDistance = 20000f;
            terrain.heightmapPixelError = 4f;
            terrain.treeDistance = s.treeDistance;
            terrain.treeBillboardDistance = s.treeDistance;
            terrain.treeMaximumFullLODCount = 60;
            terrain.detailObjectDistance = s.detailDistance;
            terrain.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            terrain.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            var col = tile.GetComponent<TerrainCollider>();
            if (col != null) col.terrainData = td; // tree colliders come from the prototype prefabs
            GameObjectUtility.SetStaticEditorFlags(tile.gameObject,
                StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic);
            return tile;
        }

        public static List<AureliusTerrainTile> Tiles(AureliusWorld world)
        {
            var l = new List<AureliusTerrainTile>();
            if (world.TerrainRoot != null) world.TerrainRoot.GetComponentsInChildren(true, l);
            return l;
        }

        static void RefreshTiles(AureliusWorld world)
        {
            var mgr = world.TerrainRoot.GetComponent<AureliusTerrainChunkManager>();
            if (mgr != null)
            {
                mgr.alwaysLoaded.Clear();
                foreach (var t in Tiles(world)) if (t.name == "Terrain_CenterReserved") mgr.alwaysLoaded.Add(t);
                mgr.Refresh();
            }
        }

        static void OverrideHeights(TileSamples ts, TerrainData td, float baseY)
        {
            int res = td.heightmapResolution;
            if (res != ts.res) return;
            var h = td.GetHeights(0, 0, res, res);
            float scale = td.size.y;
            for (int y = 0; y < res; y++)
                for (int x = 0; x < res; x++)
                    ts.height[y * res + x] = baseY + h[y, x] * scale;
        }

        // ======================================================================================

        static void Run(string title, Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                var world = UnityEngine.Object.FindFirstObjectByType<AureliusWorld>();
                if (world != null) EditorSceneManager.MarkSceneDirty(world.gameObject.scene);
            }
        }

        static void Progress(string info, float t) => EditorUtility.DisplayProgressBar("Aurelius World", info, Mathf.Clamp01(t));
    }
}
