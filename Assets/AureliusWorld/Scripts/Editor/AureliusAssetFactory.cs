using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Aurelius.World.EditorTools
{
    /// <summary>
    /// Creates (once) and returns the shared assets the world uses: terrain material + layers,
    /// noise texture, water materials, and the modular tree / rock / grass prefab library.
    /// Everything is shared: one material per surface type, one mesh per prototype.
    /// </summary>
    public static class AureliusAssetFactory
    {
        public const string Root = "Assets/AureliusWorld";
        public const string MaterialsDir = Root + "/Materials";
        public const string TexturesDir = Root + "/Textures";
        public const string LayersDir = Root + "/TerrainLayers";
        public const string GeneratedDir = Root + "/Generated";
        public const string TerrainDataDir = GeneratedDir + "/TerrainData";
        public const string MasksDir = GeneratedDir + "/Masks";
        public const string MeshesDir = GeneratedDir + "/Meshes";
        public const string PrefabsDir = Root + "/Prefabs";
        public const string SettingsPath = Root + "/AureliusWorldSettings.asset";

        const string AnimeMaterials = "Assets/Materials/Anime/";

        // --------------------------------------------------------------------------------------
        // Folders / settings
        // --------------------------------------------------------------------------------------

        public static void EnsureFolders()
        {
            foreach (var dir in new[] { Root, MaterialsDir, TexturesDir, LayersDir, GeneratedDir, TerrainDataDir, MasksDir, MeshesDir, PrefabsDir })
                EnsureFolder(dir);
        }

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        public static AureliusWorldSettings GetOrCreateSettings()
        {
            EnsureFolders();
            var s = AssetDatabase.LoadAssetAtPath<AureliusWorldSettings>(SettingsPath);
            if (s != null) return s;
            s = ScriptableObject.CreateInstance<AureliusWorldSettings>();
            AureliusLandmarkDetector.FillDefaults(s);
            AssetDatabase.CreateAsset(s, SettingsPath);
            AssetDatabase.SaveAssets();
            return s;
        }

        // --------------------------------------------------------------------------------------
        // Terrain material, layers, noise
        // --------------------------------------------------------------------------------------

        public static readonly string[] LayerNames = { "Grass", "DarkGrass", "ForestGround", "FarmSoil", "DirtPath", "RockyGround", "Sand", "RiverBank" };
        static readonly Color[] LayerColors =
        {
            new Color(0.42f, 0.70f, 0.27f), new Color(0.25f, 0.52f, 0.22f), new Color(0.22f, 0.36f, 0.17f), new Color(0.56f, 0.40f, 0.25f),
            new Color(0.76f, 0.62f, 0.43f), new Color(0.55f, 0.52f, 0.46f), new Color(0.93f, 0.85f, 0.64f), new Color(0.45f, 0.40f, 0.29f)
        };

        public static Material GetTerrainMaterial(AureliusWorldSettings s)
        {
            string path = MaterialsDir + "/MAT_Aurelius_Terrain.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                var shader = Shader.Find("Aurelius/Anime Terrain");
                mat = new Material(shader) { name = "MAT_Aurelius_Terrain" };
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetTexture("_NoiseTex", GetNoiseTexture());
            mat.EnableKeyword("_TERRAIN_INSTANCED_PERPIXEL_NORMAL");
            mat.SetVector("_FieldParams", new Vector4(s.fieldSize, s.fieldRotation, FieldSeed(s), 1.5f));
            mat.SetVector("_WaterLevels", new Vector4(s.lakeWaterLevel, s.seaLevel, 0, 0));
            mat.SetVector("_MountainRockRange", new Vector4(s.snowLine * 0.42f, s.snowLine * 0.88f, 0, 0));
            EditorUtility.SetDirty(mat);
            return mat;
        }

        public static int FieldSeed(AureliusWorldSettings s) => (s.seed & 0x7FFF) + 911;

        public static TerrainLayer[] GetTerrainLayers()
        {
            var layers = new TerrainLayer[LayerNames.Length];
            for (int i = 0; i < layers.Length; i++)
            {
                string path = $"{LayersDir}/TL_{i}_{LayerNames[i]}.terrainlayer";
                var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
                if (layer == null)
                {
                    layer = new TerrainLayer
                    {
                        diffuseTexture = GetSwatch(LayerNames[i], LayerColors[i]),
                        tileSize = new Vector2(8, 8),
                        name = "TL_" + LayerNames[i]
                    };
                    AssetDatabase.CreateAsset(layer, path);
                }
                layers[i] = layer;
            }
            return layers;
        }

        static Texture2D GetSwatch(string name, Color c)
        {
            string path = $"{TexturesDir}/T_Swatch_{name}.png";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex != null) return tex;
            var t = new Texture2D(8, 8, TextureFormat.RGBA32, false);
            var px = new Color[64];
            for (int i = 0; i < px.Length; i++) px[i] = c;
            t.SetPixels(px);
            t.Apply();
            File.WriteAllBytes(path, t.EncodeToPNG());
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>256x256 tileable RGBA noise: R fine fBm, G blotches, B cell cracks, A strokes.</summary>
        public static Texture2D GetNoiseTexture()
        {
            string path = TexturesDir + "/T_Aurelius_TerrainNoise.png";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null) return existing;

            const int size = 256;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size, v = y / (float)size;
                    float r = PeriodicFbm(u, v, 8, 4, 11) * 0.5f + 0.5f;
                    float g = PeriodicFbm(u, v, 3, 3, 23) * 0.5f + 0.5f;
                    float b = PeriodicCells(u, v, 10, 37);
                    float a = PeriodicNoise(u * 6, v * 28, 6, 28, 51) * 0.5f + 0.5f;
                    px[y * size + x] = new Color32(ToByte(r), ToByte(g), ToByte(b), ToByte(a));
                }
            tex.SetPixels32(px);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.sRGBTexture = false;
            imp.wrapMode = TextureWrapMode.Repeat;
            imp.mipmapEnabled = true;
            imp.anisoLevel = 4;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static byte ToByte(float f) => (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(f) * 255f), 0, 255);

        static float PeriodicFbm(float u, float v, int basePeriod, int octaves, int seed)
        {
            float sum = 0, amp = 1, norm = 0; int p = basePeriod;
            for (int o = 0; o < octaves; o++)
            {
                sum += PeriodicNoise(u * p, v * p, p, p, seed + o) * amp;
                norm += amp; amp *= 0.5f; p *= 2;
            }
            return sum / norm * 1.5f;
        }

        static float PeriodicNoise(float x, float y, int px, int py, int seed)
        {
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
            float fx = x - ix, fy = y - iy;
            float Grad(int gx, int gy, float dx, float dy)
            {
                float a = AureliusNoise.Hash01(((gx % px) + px) % px, ((gy % py) + py) % py, seed) * Mathf.PI * 2f;
                return Mathf.Cos(a) * dx + Mathf.Sin(a) * dy;
            }
            float n00 = Grad(ix, iy, fx, fy), n10 = Grad(ix + 1, iy, fx - 1, fy);
            float n01 = Grad(ix, iy + 1, fx, fy - 1), n11 = Grad(ix + 1, iy + 1, fx - 1, fy - 1);
            float su = fx * fx * fx * (fx * (fx * 6 - 15) + 10), sv = fy * fy * fy * (fy * (fy * 6 - 15) + 10);
            return Mathf.Lerp(Mathf.Lerp(n00, n10, su), Mathf.Lerp(n01, n11, su), sv) * 1.4f;
        }

        static float PeriodicCells(float u, float v, int period, int seed)
        {
            float x = u * period, y = v * period;
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
            float f1 = 9, f2 = 9;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int cx = ix + dx, cy = iy + dy;
                    int wx = ((cx % period) + period) % period, wy = ((cy % period) + period) % period;
                    float jx = AureliusNoise.Hash01(wx, wy, seed, 0), jy = AureliusNoise.Hash01(wx, wy, seed, 1);
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(cx + jx, cy + jy));
                    if (d < f1) { f2 = f1; f1 = d; } else if (d < f2) f2 = d;
                }
            float edge = Mathf.Clamp01((f2 - f1) * 3f);
            return Mathf.Lerp(0.15f, 1f, edge) * Mathf.Lerp(0.8f, 1f, AureliusNoise.Hash01(ix, iy, seed + 5));
        }

        // --------------------------------------------------------------------------------------
        // Materials (copies of the existing anime materials, so they share the same shaders)
        // --------------------------------------------------------------------------------------

        static Material Variant(string name, string source, System.Action<Material> setup)
        {
            string path = $"{MaterialsDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;
            var src = AssetDatabase.LoadAssetAtPath<Material>(AnimeMaterials + source + ".mat");
            if (src == null)
            {
                Debug.LogWarning($"[Aurelius] Source material {source} not found; using URP Lit for {name}.");
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            }
            else mat = new Material(src);
            mat.name = name;
            mat.enableInstancing = true;
            setup?.Invoke(mat);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        public static Material LakeMaterial => Variant("MAT_Aurelius_Lake", "MAT_Castle_Water", m =>
        {
            m.SetColor("_ShallowColor", new Color(0.42f, 0.84f, 0.86f));
            m.SetColor("_DeepColor", new Color(0.06f, 0.30f, 0.72f));
            m.SetFloat("_DepthDistance", 5.5f);
            m.SetFloat("_RippleScale", 0.22f);
            m.SetFloat("_EdgeWidth", 0.5f);
        });

        public static Material RiverMaterial => Variant("MAT_Aurelius_River", "MAT_Castle_Water", m =>
        {
            m.SetColor("_ShallowColor", new Color(0.46f, 0.86f, 0.88f));
            m.SetColor("_DeepColor", new Color(0.10f, 0.42f, 0.78f));
            m.SetFloat("_DepthDistance", 2.2f);
            m.SetFloat("_RippleSpeed", 1.2f);
        });

        public static Material OceanMaterial => Variant("MAT_Aurelius_Ocean", "MAT_Castle_Water", m =>
        {
            m.SetColor("_ShallowColor", new Color(0.30f, 0.72f, 0.82f));
            m.SetColor("_DeepColor", new Color(0.03f, 0.16f, 0.45f));
            m.SetFloat("_DepthDistance", 14f);
            m.SetFloat("_RippleScale", 0.08f);
            m.SetFloat("_DeepAlpha", 0.97f);
        });

        public static Material Foliage => Variant("MAT_Aurelius_Foliage", "MAT_Vegetation", null);
        public static Material FoliageLight => Variant("MAT_Aurelius_FoliageLight", "MAT_Vegetation_Light", null);
        public static Material Conifer => Variant("MAT_Aurelius_Conifer", "MAT_Vegetation", m =>
        {
            m.SetColor("_BaseColor", new Color(0.16f, 0.44f, 0.30f));
            m.SetColor("_ShadowColor", new Color(0.08f, 0.22f, 0.34f));
        });
        public static Material Bark => Variant("MAT_Aurelius_Bark", "MAT_Vegetation_Bark", null);
        public static Material Rock => Variant("MAT_Aurelius_Rock", "MAT_University_Rock", m =>
        {
            m.SetColor("_BaseColor", new Color(0.62f, 0.58f, 0.52f));
        });
        public static Material MountainRock => Variant("MAT_Aurelius_MountainRock", "MAT_University_Rock", m =>
        {
            m.SetColor("_BaseColor", new Color(0.58f, 0.58f, 0.63f));
            m.SetColor("_ShadowColor", new Color(0.38f, 0.40f, 0.62f));
        });
        public static Material GrassDetail => Variant("MAT_Aurelius_GrassDetail", "MAT_Grass", m =>
        {
            m.SetColor("_BaseColor", new Color(0.47f, 0.74f, 0.29f));
        });
        public static Material TallGrassDetail => Variant("MAT_Aurelius_TallGrassDetail", "MAT_Grass", m =>
        {
            m.SetColor("_BaseColor", new Color(0.58f, 0.76f, 0.32f));
        });
        public static Material FlowerYellow => Variant("MAT_Aurelius_FlowerYellow", "MAT_Grass", m =>
        {
            m.SetColor("_BaseColor", new Color(1f, 0.86f, 0.35f));
            m.SetColor("_ShadowColor", new Color(0.7f, 0.45f, 0.4f));
        });
        public static Material FlowerPink => Variant("MAT_Aurelius_FlowerPink", "MAT_Grass", m =>
        {
            m.SetColor("_BaseColor", new Color(0.98f, 0.66f, 0.80f));
            m.SetColor("_ShadowColor", new Color(0.55f, 0.4f, 0.7f));
        });

        // --------------------------------------------------------------------------------------
        // Prefab library
        // --------------------------------------------------------------------------------------

        public sealed class Library
        {
            public GameObject[] forestTrees;   // conifers + broadleaf
            public GameObject[] groveTrees;    // broadleaf for grassland groves
            public GameObject[] mountainTrees; // conifers
            public GameObject bush;
            public GameObject smallRock, mediumRock, largeRock, cliffPiece, mountainRock, boulderCluster;
            public GameObject grass, tallGrass, flowerYellow, flowerPink;

            public List<GameObject> TreePrototypes()
            {
                var l = new List<GameObject>();
                void Add(GameObject g) { if (g != null && !l.Contains(g)) l.Add(g); }
                foreach (var g in forestTrees) Add(g);
                foreach (var g in groveTrees) Add(g);
                foreach (var g in mountainTrees) Add(g);
                Add(bush);
                Add(smallRock); Add(mediumRock); Add(largeRock); Add(cliffPiece); Add(mountainRock); Add(boulderCluster);
                return l;
            }
        }

        public static Library GetLibrary(bool rebuild = false)
        {
            EnsureFolders();
            var lib = new Library
            {
                forestTrees = new[]
                {
                    TreePrefab("Tree_Pine_Tall", rebuild, () => BuildConifer(1, 17f, 3.6f, 4)),
                    TreePrefab("Tree_Pine_Wide", rebuild, () => BuildConifer(2, 12f, 4.2f, 3)),
                    TreePrefab("Tree_Broadleaf_A", rebuild, () => BuildBroadleaf(3, 11f, 4.8f, false)),
                    TreePrefab("Tree_Broadleaf_B", rebuild, () => BuildBroadleaf(4, 9f, 4.2f, true)),
                },
            };
            lib.groveTrees = new[] { lib.forestTrees[2], lib.forestTrees[3] };
            lib.mountainTrees = new[] { lib.forestTrees[0], lib.forestTrees[1] };
            lib.bush = TreePrefab("Bush", rebuild, () => BuildBush(5));

            lib.smallRock = RockPrefab("SmallRock", rebuild, 11, new Vector3(1.2f, 0.8f, 1.1f), 0.35f, false, 1);
            lib.mediumRock = RockPrefab("MediumRock", rebuild, 12, new Vector3(2.8f, 2.0f, 2.5f), 0.35f, false, 1);
            lib.largeRock = RockPrefab("LargeRock", rebuild, 13, new Vector3(6.5f, 4.5f, 5.5f), 0.4f, false, 1);
            lib.cliffPiece = RockPrefab("CliffPiece", rebuild, 14, new Vector3(6f, 13f, 4.5f), 0.45f, true, 1);
            lib.mountainRock = RockPrefab("MountainRock", rebuild, 15, new Vector3(15f, 10f, 13f), 0.45f, false, 1);
            lib.boulderCluster = RockPrefab("BoulderCluster", rebuild, 16, new Vector3(3f, 2.2f, 2.8f), 0.35f, false, 3);

            lib.grass = DetailPrefab("Detail_Grass", rebuild, () => BuildTuft(21, 9, 0.55f, 0.07f), GrassDetail);
            lib.tallGrass = DetailPrefab("Detail_TallGrass", rebuild, () => BuildTuft(22, 7, 1.0f, 0.08f), TallGrassDetail);
            lib.flowerYellow = DetailPrefab("Detail_FlowerYellow", rebuild, () => BuildFlowers(23), FlowerYellow);
            lib.flowerPink = DetailPrefab("Detail_FlowerPink", rebuild, () => BuildFlowers(24), FlowerPink);
            AssetDatabase.SaveAssets();
            return lib;
        }

        static Mesh SaveMesh(Mesh m)
        {
            string path = $"{MeshesDir}/{m.name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) { EditorUtility.CopySerialized(m, existing); Object.DestroyImmediate(m); return existing; }
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        // Trees ---------------------------------------------------------------------------------

        struct TreeMeshes { public Mesh lod0, lod1; public Material[] mats; public float trunkHeight; }

        static GameObject TreePrefab(string name, bool rebuild, System.Func<TreeMeshes> build)
        {
            string path = $"{PrefabsDir}/{name}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null && !rebuild) return existing;

            var tm = build();
            tm.lod0.name = name + "_LOD0"; tm.lod1.name = name + "_LOD1";
            var m0 = SaveMesh(tm.lod0); var m1 = SaveMesh(tm.lod1);

            var go = new GameObject(name);
            var l0 = MeshChild(go, "LOD0", m0, tm.mats, ShadowCastingMode.On);
            var l1 = MeshChild(go, "LOD1", m1, tm.mats, ShadowCastingMode.On);
            var lod = go.AddComponent<LODGroup>();
            lod.SetLODs(new[] { new LOD(0.07f, new Renderer[] { l0 }), new LOD(0.009f, new Renderer[] { l1 }) });
            lod.fadeMode = LODFadeMode.None;
            if (tm.trunkHeight > 0f)
            {
                var cap = go.AddComponent<CapsuleCollider>();
                cap.radius = 0.45f;
                cap.height = tm.trunkHeight + 0.9f;
                cap.center = new Vector3(0, cap.height * 0.5f, 0);
            }
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab;
        }

        static MeshRenderer MeshChild(GameObject parent, string name, Mesh mesh, Material[] mats, ShadowCastingMode shadows)
        {
            var c = new GameObject(name);
            c.transform.SetParent(parent.transform, false);
            c.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = c.AddComponent<MeshRenderer>();
            r.sharedMaterials = mats;
            r.shadowCastingMode = shadows;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return r;
        }

        static TreeMeshes BuildConifer(int seed, float height, float radius, int tiers)
        {
            var rng = new System.Random(seed);
            var b0 = new AureliusMeshBuilder(2);
            float trunkH = height * 0.28f;
            b0.Cylinder(0, Vector3.zero, 0.42f, 0.25f, trunkH + 1f, 6);
            for (int i = 0; i < tiers; i++)
            {
                float f = i / (float)tiers;
                float baseY = trunkH * 0.7f + f * (height - trunkH) * 0.78f;
                float r = radius * Mathf.Lerp(1f, 0.42f, f);
                float h = (height - baseY) * Mathf.Lerp(0.62f, 1f, f);
                b0.Cone(1, new Vector3(0, baseY, 0), r, h, 9, 0.25f, rng);
            }
            var b1 = new AureliusMeshBuilder(2);
            b1.Cylinder(0, Vector3.zero, 0.4f, 0.3f, trunkH, 4);
            b1.Cone(1, new Vector3(0, trunkH * 0.7f, 0), radius * 0.95f, height - trunkH * 0.7f, 6, 0.1f, rng);
            return new TreeMeshes { lod0 = b0.ToMesh("c0"), lod1 = b1.ToMesh("c1"), mats = new[] { Bark, Conifer }, trunkHeight = trunkH };
        }

        static TreeMeshes BuildBroadleaf(int seed, float height, float radius, bool light)
        {
            var rng = new System.Random(seed);
            var b0 = new AureliusMeshBuilder(2);
            float trunkH = height * 0.45f;
            b0.Cylinder(0, Vector3.zero, 0.5f, 0.3f, trunkH + 1.5f, 7);
            b0.Blob(1, new Vector3(0, trunkH + radius * 0.55f, 0), new Vector3(radius, radius * 0.8f, radius), 1, 0.22f, seed * 13, false);
            for (int i = 0; i < 3; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                var off = new Vector3(Mathf.Cos(a) * radius * 0.55f, trunkH + radius * (0.3f + (float)rng.NextDouble() * 0.5f), Mathf.Sin(a) * radius * 0.55f);
                float r = radius * (0.5f + (float)rng.NextDouble() * 0.2f);
                b0.Blob(1, off, new Vector3(r, r * 0.85f, r), 1, 0.2f, seed * 17 + i, false);
            }
            var b1 = new AureliusMeshBuilder(2);
            b1.Cylinder(0, Vector3.zero, 0.5f, 0.35f, trunkH + 1f, 4);
            b1.Blob(1, new Vector3(0, trunkH + radius * 0.6f, 0), new Vector3(radius * 1.2f, radius, radius * 1.2f), 0, 0.15f, seed * 13, false);
            return new TreeMeshes { lod0 = b0.ToMesh("b0"), lod1 = b1.ToMesh("b1"), mats = new[] { Bark, light ? FoliageLight : Foliage }, trunkHeight = trunkH };
        }

        static TreeMeshes BuildBush(int seed)
        {
            var b0 = new AureliusMeshBuilder(1);
            b0.Blob(0, new Vector3(0, 0.9f, 0), new Vector3(1.8f, 1.2f, 1.6f), 1, 0.25f, seed, false, -0.7f);
            b0.Blob(0, new Vector3(0.9f, 0.6f, 0.4f), new Vector3(1.1f, 0.9f, 1.1f), 1, 0.25f, seed + 1, false, -0.6f);
            var b1 = new AureliusMeshBuilder(1);
            b1.Blob(0, new Vector3(0.3f, 0.8f, 0.1f), new Vector3(2.2f, 1.3f, 1.9f), 0, 0.2f, seed, false, -0.6f);
            return new TreeMeshes { lod0 = b0.ToMesh("bu0"), lod1 = b1.ToMesh("bu1"), mats = new[] { FoliageLight }, trunkHeight = 0f };
        }

        // Rocks ---------------------------------------------------------------------------------

        static GameObject RockPrefab(string name, bool rebuild, int seed, Vector3 size, float roughness, bool tall, int count)
        {
            string path = $"{PrefabsDir}/{name}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null && !rebuild) return existing;

            var rng = new System.Random(seed);
            var b0 = new AureliusMeshBuilder(1);
            var b1 = new AureliusMeshBuilder(1);
            for (int i = 0; i < count; i++)
            {
                float s = i == 0 ? 1f : 0.55f + (float)rng.NextDouble() * 0.25f;
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                var off = i == 0 ? Vector3.zero : new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * size.x * 0.9f;
                var sc = size * s * 0.5f;
                off.y = sc.y * 0.3f; // sits ~15% into the ground
                b0.Blob(0, off, sc, 2, roughness * (tall ? 1.2f : 1f), seed * 7 + i, true, -0.45f);
                b1.Blob(0, off, sc, 1, roughness * (tall ? 1.2f : 1f), seed * 7 + i, true, -0.45f);
            }
            var m0 = b0.ToMesh(name + "_LOD0"); var m1 = b1.ToMesh(name + "_LOD1");
            m0 = SaveMesh(m0); m1 = SaveMesh(m1);

            bool mountain = name.Contains("Mountain") || name.Contains("Cliff");
            var mats = new[] { mountain ? MountainRock : Rock };
            var go = new GameObject(name);
            var l0 = MeshChild(go, "LOD0", m0, mats, ShadowCastingMode.On);
            var l1 = MeshChild(go, "LOD1", m1, mats, ShadowCastingMode.On);
            var lod = go.AddComponent<LODGroup>();
            float cull = size.magnitude > 10f ? 0.004f : 0.01f;
            lod.SetLODs(new[] { new LOD(0.05f, new Renderer[] { l0 }), new LOD(cull, new Renderer[] { l1 }) });
            if (size.magnitude > 2.5f)
            {
                // Simplified collision: the low-poly LOD1 hull, never the render mesh.
                var mc = go.AddComponent<MeshCollider>();
                mc.sharedMesh = m1;
                mc.convex = true;
            }
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab;
        }

        // Grass details -------------------------------------------------------------------------

        static GameObject DetailPrefab(string name, bool rebuild, System.Func<Mesh> build, Material mat)
        {
            string path = $"{PrefabsDir}/{name}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null && !rebuild) return existing;
            var mesh = build(); mesh.name = name; mesh = SaveMesh(mesh);
            var go = new GameObject(name);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab;
        }

        /// <summary>Crossed blades, double-sided, normals pointing up so tufts shade like the ground.</summary>
        static Mesh BuildTuft(int seed, int blades, float height, float width)
        {
            var rng = new System.Random(seed);
            var b = new AureliusMeshBuilder(1);
            for (int i = 0; i < blades; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float r = (float)rng.NextDouble() * 0.28f;
                var root = new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
                float face = (float)rng.NextDouble() * Mathf.PI;
                var side = new Vector3(Mathf.Cos(face), 0, Mathf.Sin(face)) * width;
                float h = height * (0.6f + (float)rng.NextDouble() * 0.5f);
                var lean = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * h * 0.3f;
                int v0 = b.Add(root - side, Vector3.up, new Vector2(0, 0));
                int v1 = b.Add(root + side, Vector3.up, new Vector2(1, 0));
                int v2 = b.Add(root + lean + Vector3.up * h, Vector3.up, new Vector2(0.5f, 1));
                b.Tri(0, v0, v2, v1);
                b.Tri(0, v1, v2, v0);
            }
            return b.ToMesh("tuft");
        }

        static Mesh BuildFlowers(int seed)
        {
            var rng = new System.Random(seed);
            var b = new AureliusMeshBuilder(1);
            for (int i = 0; i < 6; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float r = (float)rng.NextDouble() * 0.35f;
                var c = new Vector3(Mathf.Cos(a) * r, 0.25f + (float)rng.NextDouble() * 0.2f, Mathf.Sin(a) * r);
                const float s = 0.09f;
                int n = b.Add(c + new Vector3(0, 0, s), Vector3.up, Vector2.zero);
                int e = b.Add(c + new Vector3(s, 0.02f, 0), Vector3.up, Vector2.right);
                int so = b.Add(c + new Vector3(0, 0, -s), Vector3.up, Vector2.one);
                int w = b.Add(c + new Vector3(-s, 0.02f, 0), Vector3.up, Vector2.up);
                b.Tri(0, n, e, so); b.Tri(0, n, so, w);
                b.Tri(0, so, e, n); b.Tri(0, w, so, n);
            }
            return b.ToMesh("flowers");
        }
    }
}
