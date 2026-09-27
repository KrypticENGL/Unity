using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Aurelius.World.EditorTools
{
    /// <summary>Lake shape generation and the water surface meshes (lake, river ribbons, ocean).</summary>
    public static class AureliusWaterBuilder
    {
        public static AureliusLake GenerateLakeShape(AureliusWorld world)
        {
            var s = world.settings;
            var waterRoot = AureliusWorldBuilder.Child(world.transform, "Water");
            var lake = world.Lake;
            if (lake == null)
            {
                var go = new GameObject("Lake");
                go.transform.SetParent(waterRoot, false);
                lake = go.AddComponent<AureliusLake>();
                Undo.RegisterCreatedObjectUndo(go, "Generate Lake");
            }
            Undo.RecordObject(lake, "Generate Lake");
            Vector2 c = s.LakeCenter;
            lake.transform.position = new Vector3(c.x, s.lakeWaterLevel, c.y);
            lake.radius = s.lakeRadius;
            lake.depth = s.lakeDepth;

            var noise = new AureliusNoise(s.seed * 3 + 17);
            var rng = new System.Random(s.seed + 555);
            const int count = 28;
            lake.shape.Clear();
            for (int i = 0; i < count; i++)
            {
                float deg = i / (float)count * 360f;
                float m = 1f + s.lakeIrregularity * (0.65f * noise.Periodic(deg, 1.4f, 2, 1f) + 0.35f * noise.Periodic(deg, 3.6f, 1, 7f));
                lake.shape.Add(m);
            }
            // A few deliberate coves and headlands so it never reads as an ellipse.
            for (int k = 0; k < 3; k++)
            {
                int i = rng.Next(count);
                float f = k == 1 ? 1.28f : 0.7f;
                lake.shape[i] *= f;
                lake.shape[(i + 1) % count] *= Mathf.Lerp(1f, f, 0.5f);
            }

            lake.islands.Clear();
            for (int attempt = 0; attempt < 200 && lake.islands.Count < s.lakeIslandCount; attempt++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float rad = lake.ShoreRadius(a) * Mathf.Lerp(0.15f, 0.6f, (float)rng.NextDouble());
                var isl = new LakeIsland
                {
                    offset = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rad,
                    radius = Mathf.Lerp(28f, 95f, (float)rng.NextDouble()) * (lake.islands.Count == 0 ? 1.5f : 1f),
                    height = Mathf.Lerp(6f, 22f, (float)rng.NextDouble()),
                    rockiness = (float)rng.NextDouble()
                };
                bool ok = true;
                foreach (var o in lake.islands) if (Vector2.Distance(o.offset, isl.offset) < (o.radius + isl.radius) * 1.6f) ok = false;
                if (ok) lake.islands.Add(isl);
            }
            EditorUtility.SetDirty(lake);
            return lake;
        }

        public static void BuildSurfaces(AureliusWorld world, AureliusWorldField field)
        {
            var s = world.settings;
            var waterRoot = AureliusWorldBuilder.Child(world.transform, "Water");

            // Lake ---------------------------------------------------------------------------------
            var lake = world.Lake;
            if (lake != null)
            {
                var mesh = BuildLakeMesh(field, lake);
                SetSurface(lake.transform, "Surface", mesh, AureliusAssetFactory.LakeMaterial, new Vector3(0, 0, 0), true);
            }

            // Rivers -------------------------------------------------------------------------------
            var rivers = world.Rivers;
            for (int i = 0; i < rivers.Count && i < field.Rivers.Count; i++)
            {
                var mesh = BuildRiverMesh(field, field.Rivers[i], s, rivers[i].name);
                SetSurface(rivers[i].transform, "Surface", mesh, AureliusAssetFactory.RiverMaterial, Vector3.zero, true);
            }

            // Ocean --------------------------------------------------------------------------------
            var ocean = AureliusWorldBuilder.Child(waterRoot, "Ocean");
            var om = BuildOcean(s);
            SetSurface(ocean, "Surface", om, AureliusAssetFactory.OceanMaterial, Vector3.zero, true);
        }

        static void SetSurface(Transform parent, string name, Mesh mesh, Material mat, Vector3 localPos, bool worldSpaceMesh)
        {
            var t = parent.Find(name);
            if (t == null)
            {
                t = new GameObject(name).transform;
                t.SetParent(parent, false);
            }
            // Meshes are built in world space: keep the surface object at the world origin.
            t.position = Vector3.zero;
            t.rotation = Quaternion.identity;
            t.localScale = Vector3.one;
            var mf = t.GetComponent<MeshFilter>();
            if (mf == null) mf = t.gameObject.AddComponent<MeshFilter>();
            var mr = t.GetComponent<MeshRenderer>();
            if (mr == null) mr = t.gameObject.AddComponent<MeshRenderer>();
            mf.sharedMesh = SaveMesh(mesh);
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = true;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        static Mesh SaveMesh(Mesh m)
        {
            string path = $"{AureliusAssetFactory.MeshesDir}/{m.name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
            {
                existing.Clear();
                EditorUtility.CopySerialized(m, existing);
                Object.DestroyImmediate(m);
                return existing;
            }
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static Mesh BuildLakeMesh(AureliusWorldField field, AureliusLake lake)
        {
            const float cell = 8f;
            float extent = lake.radius * 1.75f;
            int dim = Mathf.CeilToInt(extent * 2f / cell) + 1;
            Vector2 min = lake.Center - Vector2.one * extent;
            float wl = lake.WaterLevel;
            var h = new float[dim * dim];
            Parallel.For(0, dim, y =>
            {
                for (int x = 0; x < dim; x++) h[y * dim + x] = field.Sample(min.x + x * cell, min.y + y * cell).height;
            });

            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            var index = new int[dim * dim];
            for (int i = 0; i < index.Length; i++) index[i] = -1;
            int V(int x, int y)
            {
                int i = y * dim + x;
                if (index[i] < 0)
                {
                    index[i] = verts.Count;
                    verts.Add(new Vector3(min.x + x * cell, wl, min.y + y * cell));
                    uvs.Add(new Vector2(x * cell, y * cell) * 0.05f);
                }
                return index[i];
            }
            for (int y = 0; y < dim - 1; y++)
                for (int x = 0; x < dim - 1; x++)
                {
                    float lo = Mathf.Min(Mathf.Min(h[y * dim + x], h[y * dim + x + 1]), Mathf.Min(h[(y + 1) * dim + x], h[(y + 1) * dim + x + 1]));
                    if (lo > wl + 0.2f) continue;
                    // Only inside (or just outside) the lake shape: never over the sea or low river beds.
                    if (field.LakeSignedDistance(min + new Vector2((x + 0.5f) * cell, (y + 0.5f) * cell)) > 30f) continue;
                    int a = V(x, y), b = V(x + 1, y), c = V(x + 1, y + 1), d = V(x, y + 1);
                    tris.Add(a); tris.Add(d); tris.Add(c);
                    tris.Add(a); tris.Add(c); tris.Add(b);
                }
            return MakeMesh("Water_Lake", verts, uvs, tris);
        }

        static Mesh BuildRiverMesh(AureliusWorldField field, FieldSpline r, AureliusWorldSettings s, string name)
        {
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            int prevL = -1, prevR = -1;
            for (int i = 0; i < r.p.Length; i += 2)
            {
                Vector2 p = r.p[i];
                // Stop where the river meets the lake or the sea: those surfaces take over.
                bool wet = (field.HasLake && field.LakeSignedDistance(p) < -8f) || r.level[i] <= s.seaLevel + 0.1f;
                if (wet) { prevL = prevR = -1; continue; }
                Vector2 a = r.p[Mathf.Max(0, i - 2)], b = r.p[Mathf.Min(r.p.Length - 1, i + 2)];
                Vector2 tan = (b - a).normalized;
                Vector2 perp = new Vector2(-tan.y, tan.x);
                float hw = r.width[i] * 0.5f + 1.2f;
                float y = r.level[i] + 0.03f;
                int l = verts.Count;
                verts.Add(new Vector3(p.x + perp.x * hw, y, p.y + perp.y * hw));
                verts.Add(new Vector3(p.x - perp.x * hw, y, p.y - perp.y * hw));
                uvs.Add(new Vector2(0, r.s[i] * 0.05f));
                uvs.Add(new Vector2(1, r.s[i] * 0.05f));
                if (prevL >= 0)
                {
                    tris.Add(prevL); tris.Add(l); tris.Add(l + 1);
                    tris.Add(prevL); tris.Add(l + 1); tris.Add(prevR);
                }
                prevL = l; prevR = l + 1;
            }
            return MakeMesh("Water_" + name, verts, uvs, tris);
        }

        static Mesh BuildOcean(AureliusWorldSettings s)
        {
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            float[] radii = { s.worldRadius * 0.86f, s.worldRadius * 1.1f, s.worldRadius * 1.5f, s.worldRadius * 2.5f, 16000f, 100000f };
            const int seg = 128;
            for (int ri = 0; ri < radii.Length; ri++)
                for (int i = 0; i < seg; i++)
                {
                    float a = i / (float)seg * Mathf.PI * 2f;
                    var p = s.cityCenter + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radii[ri];
                    verts.Add(new Vector3(p.x, s.seaLevel, p.y));
                    uvs.Add(p * 0.01f);
                }
            for (int ri = 0; ri < radii.Length - 1; ri++)
                for (int i = 0; i < seg; i++)
                {
                    int a = ri * seg + i, b = ri * seg + (i + 1) % seg, c = (ri + 1) * seg + (i + 1) % seg, d = (ri + 1) * seg + i;
                    tris.Add(a); tris.Add(b); tris.Add(c);
                    tris.Add(a); tris.Add(c); tris.Add(d);
                }
            return MakeMesh("Water_Ocean", verts, uvs, tris);
        }

        static Mesh MakeMesh(string name, List<Vector3> verts, List<Vector2> uvs, List<int> tris)
        {
            var m = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            m.SetVertices(verts);
            m.SetUVs(0, uvs);
            m.SetTriangles(tris, 0);
            var normals = new Vector3[verts.Count];
            for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.up;
            m.normals = normals;
            m.RecalculateBounds();
            m.RecalculateTangents();
            return m;
        }
    }
}
