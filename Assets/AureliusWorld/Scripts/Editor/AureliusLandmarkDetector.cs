using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Aurelius.World.EditorTools
{
    /// <summary>
    /// Measures the city landmarks already placed in the scene (Castle, University, Colosseum) and
    /// turns them into terrain pads so the ground meets each model. The reserved city radius is set
    /// from the farthest landmark vertex plus a margin.
    /// </summary>
    public static class AureliusLandmarkDetector
    {
        public static readonly string[] LandmarkNames = { "Castle", "University", "Colloseum", "Colosseum" };

        public static void FillDefaults(AureliusWorldSettings s)
        {
            s.landmarkPads.Clear();
            if (!Detect(s, true))
            {
                // Measured from SampleScene when this tool was written.
                s.landmarkPads.Add(new LandmarkPad { name = "Castle Apron", polygon = Rect(-300, -300, 300, 300), innerHeight = 3.95f, edgeHeight = 3.95f, inset = 2f, blend = 2f });
                s.landmarkPads.Add(new LandmarkPad { name = "Colosseum", shape = LandmarkPadShape.Ellipse, center = new Vector2(-492.3f, 1f), radii = new Vector2(114f, 118f), innerHeight = 7.95f, edgeHeight = 7.95f, inset = 1f, blend = 45f });
            }
        }

        /// <summary>Returns false when no landmark was found in the open scene.</summary>
        public static bool Detect(AureliusWorldSettings s, bool setCityRadius)
        {
            var found = new List<GameObject>();
            foreach (var n in LandmarkNames)
            {
                var go = GameObject.Find(n);
                if (go != null && go.GetComponentInChildren<MeshFilter>() != null && !found.Contains(go)) found.Add(go);
            }
            if (found.Count == 0) return false;

            Undo.RecordObject(s, "Detect City Landmarks");
            float maxR = 0f;
            var log = new System.Text.StringBuilder("[Aurelius] City landmarks:\n");
            foreach (var go in found)
            {
                var pts = new List<Vector2>();
                var heights = new List<float>();
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
                {
                    var mesh = mf.sharedMesh;
                    if (mesh == null) continue;
                    var m = mf.transform.localToWorldMatrix;
                    var verts = mesh.vertices;
                    int step = Mathf.Max(1, verts.Length / 4000);
                    for (int i = 0; i < verts.Length; i += step)
                    {
                        var w = m.MultiplyPoint3x4(verts[i]);
                        pts.Add(new Vector2(w.x, w.z));
                        heights.Add(w.y);
                        maxR = Mathf.Max(maxR, Vector2.Distance(new Vector2(w.x, w.z), s.cityCenter));
                    }
                }
                if (pts.Count < 3) continue;
                heights.Sort();
                float baseH = heights[Mathf.Clamp(Mathf.RoundToInt(heights.Count * 0.008f), 0, heights.Count - 1)];
                float minH = heights[0];
                var hull = ConvexHull(pts);
                float dist = Vector2.Distance(Centroid(hull), s.cityCenter);

                LandmarkPad pad;
                var apron = go.GetComponentsInChildren<Renderer>(true).FirstOrDefault(r => r.name.Contains("Ground_Apron"));
                if (apron != null)
                {
                    // The apron defines the landmark's ground level. If it is rendered, sink the terrain under it;
                    // if it is only a collider (as on the castle), the terrain IS the ground there.
                    // Either way, the landmark's own water (moat / canals) is carved automatically.
                    var b = apron.bounds;
                    bool visible = apron.enabled && apron.gameObject.activeInHierarchy;
                    float top = b.max.y - 0.05f;
                    pad = FindOrAdd(s, go.name + " Apron", LandmarkPadShape.Polygon, top, top, 2f, 2f);
                    pad.polygon = Rect(b.min.x, b.min.z, b.max.x, b.max.z);
                    pad.innerHeight = visible ? top - 12f : top;
                    pad.edgeHeight = top;
                    pad.inset = visible ? 10f : 2f;
                }
                else
                {
                    bool isNew = s.landmarkPads.All(p => p.name != go.name);
                    pad = FindOrAdd(s, go.name, LandmarkPadShape.Polygon, baseH - 0.05f, baseH - 0.05f, 4f, 50f);
                    pad.polygon = Shrink(hull, 1f);
                    if (isNew) { pad.innerHeight = baseH - 0.05f; pad.edgeHeight = baseH - 0.05f; }
                }
                log.AppendLine($"  {go.name}: {dist:F0} m from castle, footprint reaches {MaxDist(hull, s.cityCenter):F0} m, base y {baseH:F1} (lowest {minH:F1})");
            }
            if (setCityRadius)
            {
                s.cityRadius = Mathf.Ceil((maxR + 28f) / 10f) * 10f;
                log.AppendLine($"  Reserved city radius = {s.cityRadius} m (farthest landmark vertex {maxR:F0} m + margin)");
            }
            Debug.Log(log.ToString());
            EditorUtility.SetDirty(s);
            return true;
        }

        static LandmarkPad FindOrAdd(AureliusWorldSettings s, string name, LandmarkPadShape shape, float inner, float edge, float inset, float blend)
        {
            var pad = s.landmarkPads.FirstOrDefault(p => p.name == name);
            if (pad != null) return pad;
            pad = new LandmarkPad { name = name, shape = shape, innerHeight = inner, edgeHeight = edge, inset = inset, blend = blend };
            s.landmarkPads.Add(pad);
            return pad;
        }

        static List<Vector2> Rect(float x0, float z0, float x1, float z1) =>
            new List<Vector2> { new Vector2(x0, z0), new Vector2(x1, z0), new Vector2(x1, z1), new Vector2(x0, z1) };

        static float MaxDist(List<Vector2> hull, Vector2 c) => hull.Max(p => Vector2.Distance(p, c));

        static Vector2 Centroid(List<Vector2> pts)
        {
            Vector2 sum = Vector2.zero;
            foreach (var p in pts) sum += p;
            return sum / pts.Count;
        }

        static List<Vector2> Shrink(List<Vector2> hull, float amount)
        {
            var c = Centroid(hull);
            return hull.Select(p => p + (c - p).normalized * amount).ToList();
        }

        /// <summary>Andrew's monotone chain.</summary>
        public static List<Vector2> ConvexHull(List<Vector2> points)
        {
            var pts = points.Distinct().OrderBy(p => p.x).ThenBy(p => p.y).ToList();
            if (pts.Count < 3) return pts;
            var hull = new List<Vector2>();
            float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
            foreach (var p in pts)
            {
                while (hull.Count >= 2 && Cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0) hull.RemoveAt(hull.Count - 1);
                hull.Add(p);
            }
            int lower = hull.Count + 1;
            for (int i = pts.Count - 2; i >= 0; i--)
            {
                var p = pts[i];
                while (hull.Count >= lower && Cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0) hull.RemoveAt(hull.Count - 1);
                hull.Add(p);
            }
            hull.RemoveAt(hull.Count - 1);
            return hull;
        }
    }
}
