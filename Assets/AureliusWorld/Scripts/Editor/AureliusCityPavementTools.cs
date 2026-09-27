using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Aurelius.World.EditorTools
{
    /// <summary>
    /// Builds the city pavement: lays out the paving systems from the scene (castle roads, landmarks)
    /// and generates the draped disc mesh (a few chunks, no UVs, one shared material). Only adds the
    /// pavement object; terrain, buildings and everything else are left untouched.
    /// </summary>
    public static class AureliusCityPavementBuilder
    {
        const string MeshDir = AureliusAssetFactory.GeneratedDir + "/Pavement";
        const string MaterialPath = AureliusAssetFactory.MaterialsDir + "/MAT_Aurelius_CityPavement.mat";
        const int Sectors = 16;

        [MenuItem("Aurelius/City/Create or Select City Pavement", priority = 200)]
        public static void CreateOrSelect()
        {
            var pave = Object.FindFirstObjectByType<AureliusCityPavement>();
            if (pave == null)
            {
                var go = new GameObject("Aurelius_CityPavement");
                Undo.RegisterCreatedObjectUndo(go, "Create City Pavement");
                pave = go.AddComponent<AureliusCityPavement>();
                AutoLayout(pave);
                BuildMesh(pave);
            }
            Selection.activeGameObject = pave.gameObject;
        }

        public static Material GetMaterial()
        {
            AureliusAssetFactory.EnsureFolders();
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (mat != null) return mat;
            mat = new Material(Shader.Find("Aurelius/City Pavement")) { name = "MAT_Aurelius_CityPavement", enableInstancing = false };
            AssetDatabase.CreateAsset(mat, MaterialPath);
            return mat;
        }

        // ======================================================================================
        // Layout from the scene
        // ======================================================================================

        public static void AutoLayout(AureliusCityPavement p)
        {
            Undo.RecordObject(p, "Auto Layout Pavement");
            var settings = AssetDatabase.LoadAssetAtPath<AureliusWorldSettings>(AureliusAssetFactory.SettingsPath);
            if (settings != null)
            {
                p.center = settings.cityCenter;
                p.northYaw = settings.northYaw;
                p.outerRadius = settings.cityRadius;
            }
            p.material = GetMaterial();

            // Pavement starts at the castle moat's outer bank.
            var bank = GameObject.Find("Path_Ring_OuterBank");
            if (bank != null)
            {
                var b = bank.GetComponent<Renderer>().bounds;
                p.innerRadius = Mathf.Min(b.extents.x, b.extents.z) + 0.2f;
            }

            float R = p.outerRadius, inner = p.innerRadius;
            p.roads.Clear();
            // Royal roads: the castle's four main axes + the NW diagonal to the University gate.
            string[] card = { "North", "East", "South", "West" };
            for (int i = 0; i < 4; i++) p.roads.Add(new PaveRoad { name = "Royal_" + card[i], bearing = i * 90f, level = PaveLevel.Major, startRadius = inner, endRadius = R });
            p.roads.Add(new PaveRoad { name = "Royal_NW_University", bearing = 315f, level = PaveLevel.Major, startRadius = inner, endRadius = R });
            // Secondary diagonals from the castle's side gates.
            string[] diag = { "NE", "SE", "SW" };
            float[] diagB = { 45f, 135f, 225f };
            for (int i = 0; i < 3; i++) p.roads.Add(new PaveRoad { name = "Avenue_" + diag[i], bearing = diagB[i], level = PaveLevel.Secondary, startRadius = inner, endRadius = R });
            // Minor streets between them, from the inner ring outward.
            for (int i = 0; i < 8; i++)
                p.roads.Add(new PaveRoad { name = $"Street_{22.5f + i * 45f:0.#}", bearing = 22.5f + i * 45f, level = PaveLevel.Minor, startRadius = 330f, endRadius = R });

            p.rings.Clear();
            p.rings.Add(new PaveRing { name = "Inner_Ring", radius = 330f, level = PaveLevel.Secondary });
            p.rings.Add(new PaveRing { name = "Middle_Ring", radius = 470f, level = PaveLevel.Secondary });
            p.rings.Add(new PaveRing { name = "Outer_Promenade", radius = R - 34f, level = PaveLevel.Major, widthOverride = 14f });

            p.plazas.Clear();
            p.exclusions.Clear();
            var pads = settings != null ? settings.landmarkPads : new List<LandmarkPad>();

            // Colosseum: a ring plaza that follows the building + an entrance plaza facing the castle.
            var col = pads.FirstOrDefault(x => x.name.StartsWith("Col"));
            if (col != null && col.polygon.Count > 2)
            {
                Vector2 c = Centroid(col.polygon);
                float rad = col.polygon.Max(v => Vector2.Distance(v, c));
                p.plazas.Add(new PavePlaza { name = "Colosseum_Ring", shape = PlazaShape.Ring, center = c, ringRadii = new Vector2(rad - 3f, rad + 26f), level = PaveLevel.Secondary });
                Vector2 toCastle = (p.center - c).normalized;
                p.plazas.Add(new PavePlaza
                {
                    name = "Colosseum_Entrance", center = c + toCastle * (rad + 18f), size = new Vector2(44f, 64f),
                    rotation = Mathf.Atan2(toCastle.y, toCastle.x) * Mathf.Rad2Deg, level = PaveLevel.Major
                });
            }

            // University: forecourt around the campus aligned with it + gate plaza toward the castle.
            var uni = pads.FirstOrDefault(x => x.name.StartsWith("Uni"));
            if (uni != null && uni.polygon.Count > 2)
            {
                MinAreaRect(uni.polygon, out var c, out var size, out float angle);
                p.plazas.Add(new PavePlaza { name = "University_Forecourt", center = c, size = size + Vector2.one * 28f, rotation = angle, level = PaveLevel.Secondary });
                var gate = GameObject.Find("UNI_Gate_Main_South");
                Vector2 g = gate != null ? XZ(gate.GetComponent<Renderer>().bounds.center) : c;
                Vector2 toCastle = (p.center - g).normalized;
                p.plazas.Add(new PavePlaza
                {
                    name = "University_Gate", center = g + toCastle * 30f, size = new Vector2(54f, 60f),
                    rotation = Mathf.Atan2(toCastle.y, toCastle.x) * Mathf.Rad2Deg, level = PaveLevel.Major
                });
            }

            // Landmarks' own ground pieces are cut out by the baked landmark mask (BakeLandmarkMask);
            // 'exclusions' stays available for manual rectangular cut-outs.
            p.Apply();
            EditorUtility.SetDirty(p);
        }

        // ======================================================================================
        // Mesh
        // ======================================================================================

        public static void BuildMesh(AureliusCityPavement p)
        {
            AureliusAssetFactory.EnsureFolder(MeshDir);
            if (p.material == null) p.material = GetMaterial();

            // Landmark footprints (buildings stand here): skip quads fully inside them.
            var settings = AssetDatabase.LoadAssetAtPath<AureliusWorldSettings>(AureliusAssetFactory.SettingsPath);
            var footprints = new List<Vector2[]>();
            if (settings != null)
                foreach (var pad in settings.landmarkPads)
                {
                    if (!pad.enabled || pad.shape != LandmarkPadShape.Polygon || pad.polygon.Count < 3 || pad.name.Contains("Apron")) continue;
                    var landmark = GameObject.Find(pad.name);
                    if (landmark != null && landmark.GetComponentInChildren<Collider>() != null) continue; // handled by the mask
                    // Run the pavement well under the walls: the polar grid is coarse, so a tight
                    // footprint would leave stair-stepped grass slivers at the building's base.
                    footprints.Add(Shrink(pad.polygon, 12f).ToArray());
                }

            float inner = p.innerRadius, outer = p.outerRadius;
            const float radialStep = 3f;
            int rings = Mathf.CeilToInt((outer - inner) / radialStep);
            int segments = Mathf.CeilToInt(2f * Mathf.PI * outer / 3.2f / Sectors) * Sectors;
            int perSector = segments / Sectors;

            // Clear old chunks.
            for (int i = p.transform.childCount - 1; i >= 0; i--)
                Undo.DestroyObjectImmediate(p.transform.GetChild(i).gameObject);

            int totalVerts = 0, totalTris = 0;
            for (int s = 0; s < Sectors; s++)
            {
                var verts = new List<Vector3>();
                var normals = new List<Vector3>();
                var tris = new List<int>();
                var index = new Dictionary<int, int>();
                int cols = perSector + 1;

                int V(int ri, int si)
                {
                    int key = ri * cols + si;
                    if (index.TryGetValue(key, out int id)) return id;
                    float r = ri == rings ? outer : inner + ri * radialStep;
                    float a = (s * perSector + si) / (float)segments * Mathf.PI * 2f;
                    Vector2 xz = p.center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                    Vector3 n;
                    float h = SampleTerrain(xz, out n);
                    for (int k = 0; k < 4; k++)
                    {
                        Vector2 o = new Vector2(k == 0 ? 1.6f : k == 1 ? -1.6f : 0f, k == 2 ? 1.6f : k == 3 ? -1.6f : 0f);
                        h = Mathf.Max(h, SampleTerrain(xz + o, out _));
                    }
                    float slopeBonus = (1f - n.y) * 2.5f;
                    verts.Add(new Vector3(xz.x, h + p.pavementHeight + slopeBonus, xz.y));
                    normals.Add(n);
                    index[key] = verts.Count - 1;
                    return verts.Count - 1;
                }

                Vector2 P(int ri, int si)
                {
                    float r = ri == rings ? outer : inner + ri * radialStep;
                    float a = (s * perSector + si) / (float)segments * Mathf.PI * 2f;
                    return p.center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                }

                bool Covered(Vector2 q)
                {
                    foreach (var f in footprints) if (AureliusWorldField.PolygonSignedDistance(f, q) < 0f) return true;
                    return false;
                }

                for (int ri = 0; ri < rings; ri++)
                    for (int si = 0; si < perSector; si++)
                    {
                        // Skip only quads entirely under a building: no gaps at building edges.
                        if (Covered(P(ri, si)) && Covered(P(ri + 1, si)) && Covered(P(ri, si + 1)) && Covered(P(ri + 1, si + 1))) continue;
                        int a = V(ri, si), b = V(ri, si + 1), c = V(ri + 1, si + 1), d = V(ri + 1, si);
                        // (inner, next inner, next outer) is clockwise seen from above: faces up in Unity
                        tris.Add(a); tris.Add(b); tris.Add(c);
                        tris.Add(a); tris.Add(c); tris.Add(d);
                    }

                // Short skirt on the city boundary so the pavement edge never shows a gap to the grass.
                for (int si = 0; si < perSector; si++)
                {
                    int top0 = V(rings, si), top1 = V(rings, si + 1);
                    int b0 = verts.Count; verts.Add(verts[top0] + Vector3.down * 0.4f); normals.Add(normals[top0]);
                    int b1 = verts.Count; verts.Add(verts[top1] + Vector3.down * 0.4f); normals.Add(normals[top1]);
                    tris.Add(top0); tris.Add(top1); tris.Add(b1);
                    tris.Add(top0); tris.Add(b1); tris.Add(b0);
                }

                var mesh = new Mesh { name = $"CityPavement_{s:00}", indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(verts);
                mesh.SetNormals(normals);
                mesh.SetTriangles(tris, 0);
                mesh.RecalculateBounds();
                mesh = SaveMesh(mesh);
                totalVerts += verts.Count; totalTris += tris.Count / 3;

                var go = new GameObject(mesh.name);
                go.transform.SetParent(p.transform, false);
                go.transform.position = Vector3.zero;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = p.material;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = true;
                mr.lightProbeUsage = LightProbeUsage.Off;
                mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.OccludeeStatic | StaticEditorFlags.BatchingStatic);
                Undo.RegisterCreatedObjectUndo(go, "Build Pavement");
            }
            BakeLandmarkMask(p);
            p.Apply();
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(p.gameObject.scene);
            Debug.Log($"[Aurelius] City pavement: {Sectors} chunks, {totalVerts:N0} vertices, {totalTris:N0} triangles, 1 material.");
        }

        /// <summary>
        /// Raycasts down onto each landmark's colliders and marks texels where the landmark has its own
        /// ground-level surface (castle road slabs, University lawns and paths). The shader cuts the
        /// pavement out there, so nothing overlaps or z-fights and those surfaces keep their design.
        /// </summary>
        public static void BakeLandmarkMask(AureliusCityPavement p)
        {
            const int res = 3072;
            float half = p.outerRadius + 10f;
            Vector2 min = p.center - Vector2.one * half;
            float size = half * 2f, texel = size / res;
            var data = new byte[res * res];
            var hits = new RaycastHit[48];
            Physics.SyncTransforms();
            int marked = 0;

            foreach (var name in AureliusLandmarkDetector.LandmarkNames)
            {
                var root = GameObject.Find(name);
                if (root == null) continue;
                var cols = root.GetComponentsInChildren<Collider>();
                if (cols.Length == 0) continue;
                Bounds b = cols[0].bounds;
                foreach (var c in cols) b.Encapsulate(c.bounds);
                int x0 = Mathf.Clamp(Mathf.FloorToInt((b.min.x - min.x) / texel), 0, res - 1), x1 = Mathf.Clamp(Mathf.CeilToInt((b.max.x - min.x) / texel), 0, res - 1);
                int y0 = Mathf.Clamp(Mathf.FloorToInt((b.min.z - min.y) / texel), 0, res - 1), y1 = Mathf.Clamp(Mathf.CeilToInt((b.max.z - min.y) / texel), 0, res - 1);
                float top = b.max.y + 5f, len = b.size.y + 20f;
                for (int y = y0; y <= y1; y++)
                {
                    if (y % 64 == 0) EditorUtility.DisplayProgressBar("Aurelius City Pavement", $"Landmark mask: {name}", (y - y0) / (float)Mathf.Max(1, y1 - y0));
                    for (int x = x0; x <= x1; x++)
                    {
                        float wx = min.x + (x + 0.5f) * texel, wz = min.y + (y + 0.5f) * texel;
                        float r = Vector2.Distance(new Vector2(wx, wz), p.center);
                        if (r < p.innerRadius - 1f || r > p.outerRadius + 1f) continue;
                        int n = Physics.RaycastNonAlloc(new Vector3(wx, top, wz), Vector3.down, hits, len);
                        if (n == 0) continue;
                        float th = float.NaN;
                        for (int i = 0; i < n; i++)
                        {
                            var h = hits[i];
                            if (h.collider is TerrainCollider || h.normal.y < 0.7f || !h.collider.transform.IsChildOf(root.transform)) continue;
                            if (float.IsNaN(th)) th = SampleTerrain(new Vector2(wx, wz), out _);
                            // Only surfaces that are actually visible above the terrain (buried slabs get paved over).
                            if (h.point.y > th - 0.03f && h.point.y < th + p.pavementHeight + 0.45f) { data[y * res + x] = 255; marked++; break; }
                        }
                    }
                }
            }
            EditorUtility.ClearProgressBar();

            string path = MeshDir + "/T_PaveLandmarkMask.asset";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex == null || tex.width != res)
            {
                if (tex != null) AssetDatabase.DeleteAsset(path);
                tex = new Texture2D(res, res, TextureFormat.R8, false, true) { name = "T_PaveLandmarkMask", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                tex.SetPixelData(data, 0);
                tex.Apply(false, false);
                AssetDatabase.CreateAsset(tex, path);
            }
            else
            {
                tex.SetPixelData(data, 0);
                tex.Apply(false, false);
                EditorUtility.SetDirty(tex);
            }
            Undo.RecordObject(p, "Bake Landmark Mask");
            p.landmarkMask = tex;
            p.landmarkMaskRect = new Vector4(min.x, min.y, size, size);
            EditorUtility.SetDirty(p);
            Debug.Log($"[Aurelius] Landmark ground mask: {marked:N0} texels of {texel:F2} m.");
        }

        static float SampleTerrain(Vector2 xz, out Vector3 normal)
        {
            foreach (var t in Terrain.activeTerrains)
            {
                var pos = t.transform.position;
                var size = t.terrainData.size;
                if (xz.x < pos.x || xz.y < pos.z || xz.x > pos.x + size.x || xz.y > pos.z + size.z) continue;
                normal = t.terrainData.GetInterpolatedNormal((xz.x - pos.x) / size.x, (xz.y - pos.z) / size.z);
                return t.SampleHeight(new Vector3(xz.x, 0, xz.y)) + pos.y;
            }
            normal = Vector3.up;
            return 4f;
        }

        static Mesh SaveMesh(Mesh m)
        {
            string path = $"{MeshDir}/{m.name}.asset";
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

        // ======================================================================================

        static Vector2 XZ(Vector3 v) => new Vector2(v.x, v.z);

        static Vector2 Centroid(IList<Vector2> pts)
        {
            Vector2 s = Vector2.zero;
            foreach (var v in pts) s += v;
            return s / pts.Count;
        }

        static List<Vector2> Shrink(IList<Vector2> poly, float amount)
        {
            var c = Centroid(poly);
            return poly.Select(v => v + (c - v).normalized * amount).ToList();
        }

        /// <summary>Minimum-area oriented rectangle (rotation in world degrees from +X).</summary>
        public static void MinAreaRect(IList<Vector2> pts, out Vector2 center, out Vector2 size, out float angleDeg)
        {
            var hull = AureliusLandmarkDetector.ConvexHull(pts.ToList());
            float best = float.MaxValue; center = Vector2.zero; size = Vector2.zero; angleDeg = 0;
            for (float a = 0; a < 90f; a += 0.5f)
            {
                float c = Mathf.Cos(a * Mathf.Deg2Rad), s = Mathf.Sin(a * Mathf.Deg2Rad);
                float x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue, y1 = float.MinValue;
                foreach (var q in hull)
                {
                    float u = q.x * c + q.y * s, w = -q.x * s + q.y * c;
                    x0 = Mathf.Min(x0, u); x1 = Mathf.Max(x1, u); y0 = Mathf.Min(y0, w); y1 = Mathf.Max(y1, w);
                }
                float area = (x1 - x0) * (y1 - y0);
                if (area < best)
                {
                    best = area; angleDeg = a;
                    float cu = (x0 + x1) * 0.5f, cw = (y0 + y1) * 0.5f;
                    center = new Vector2(cu * c - cw * s, cu * s + cw * c);
                    size = new Vector2(x1 - x0, y1 - y0);
                }
            }
        }
    }

    [CustomEditor(typeof(AureliusCityPavement))]
    public class AureliusCityPavementEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            var p = (AureliusCityPavement)target;
            EditorGUILayout.HelpBox("Layout, colours and tile sizes update live (shader). Rebuild the mesh only after changing the radii, Pavement Height or the terrain.", MessageType.None);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("BUILD PAVEMENT MESH", GUILayout.Height(28))) AureliusCityPavementBuilder.BuildMesh(p);
                if (GUILayout.Button("AUTO LAYOUT FROM SCENE", GUILayout.Height(28)))
                {
                    AureliusCityPavementBuilder.AutoLayout(p);
                    AureliusCityPavementBuilder.BuildMesh(p);
                }
            }
            EditorGUI.BeginChangeCheck();
            DrawDefaultInspector();
            if (EditorGUI.EndChangeCheck()) { p.Apply(); SceneView.RepaintAll(); }
        }
    }
}

