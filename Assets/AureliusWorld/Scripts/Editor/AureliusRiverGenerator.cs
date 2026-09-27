using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Aurelius.World.EditorTools
{
    /// <summary>
    /// Routes rivers from high ground to the lake / sea (or into an earlier river) across the base
    /// terrain, preferring downhill valleys, then smooths and meanders them into editable splines.
    /// </summary>
    public static class AureliusRiverGenerator
    {
        struct Source { public float bearing; public float distance; public RiverOutlet outlet; public float size; }

        public static List<AureliusRiver> Generate(AureliusWorld world)
        {
            var s = world.settings;
            var root = AureliusWorldBuilder.Child(world.transform, "Water/Rivers");
            AureliusWorldBuilder.ClearChildren(root);

            // Base terrain: majors + lake, no rivers, no secondary paths.
            var field = new AureliusWorldField(s, world.Lake, null, world.Paths.FindAll(p => p.kind == PathKind.Major));
            var grid = new AureliusRouteGrid(field, 16f);
            int n = grid.dim * grid.dim;
            var majorDist = new float[n];
            Parallel.For(0, grid.dim, y =>
            {
                for (int x = 0; x < grid.dim; x++) majorDist[y * grid.dim + x] = field.DistanceToMajors(grid.CellCenter(x, y));
            });
            var riverCell = new int[n];
            for (int i = 0; i < n; i++) riverCell[i] = -1;

            var noise = new AureliusNoise(s.seed * 11 + 5);
            var rng = new System.Random(s.seed + 707);
            float R = s.worldRadius;
            var sources = new List<Source>
            {
                new Source { bearing = 352f, distance = R - 430f, outlet = RiverOutlet.Lake, size = 1.2f },
                new Source { bearing = 30f, distance = R - 520f, outlet = RiverOutlet.Lake, size = 1f },
                new Source { bearing = 322f, distance = R - 560f, outlet = RiverOutlet.Sea, size = 1f },
                new Source { bearing = 226f, distance = s.cityRadius + 1000f, outlet = RiverOutlet.Sea, size = 0.8f },
                new Source { bearing = 170f, distance = s.cityRadius + 700f, outlet = RiverOutlet.Sea, size = 0.75f },
                new Source { bearing = 146f, distance = s.cityRadius + 420f, outlet = RiverOutlet.Lake, size = 0.6f },
            };

            var created = new List<AureliusRiver>();
            for (int si = 0; si < Mathf.Min(s.riverCount, sources.Count); si++)
            {
                var src = sources[si];
                float bearing = src.bearing + (float)(rng.NextDouble() - 0.5) * 12f;
                var nominal = s.cityCenter + s.BearingToDir(bearing) * src.distance;
                int start = HighestNear(grid, grid.CellOf(nominal), 10);
                var outlet = src.outlet == RiverOutlet.Lake && !field.HasLake ? RiverOutlet.Sea : src.outlet;
                Vector2 lakeC = field.LakeCenter;
                float lakeR = field.LakeRadius;
                int self = si;

                var route = grid.Route(start,
                    c => (outlet == RiverOutlet.Lake ? grid.lakeSd[c] < -25f : grid.edge[c] < -30f) || (riverCell[c] >= 0 && riverCell[c] != self),
                    (a, b, len) =>
                    {
                        Vector2 pb = grid.CellCenter(b);
                        float r = Vector2.Distance(pb, s.cityCenter);
                        if (r < s.cityRadius + 170f) return float.PositiveInfinity;
                        if (outlet == RiverOutlet.Lake && grid.edge[b] < 20f) return float.PositiveInfinity;
                        float up = Mathf.Max(0f, grid.height[b] - grid.height[a]);
                        float wobble = noise.Noise(pb.x / 260f, pb.y / 260f) * 0.5f + 0.5f;
                        float c = len * (1f + 1.4f * wobble) + up * 70f;
                        if (majorDist[b] < 45f) c += len * 5f;
                        if (outlet == RiverOutlet.Sea && grid.lakeSd[b] < 10f) return float.PositiveInfinity;
                        return c;
                    },
                    c =>
                    {
                        Vector2 p = grid.CellCenter(c);
                        if (outlet == RiverOutlet.Lake) return Mathf.Max(0f, Vector2.Distance(p, lakeC) - lakeR) * 0.9f;
                        return Mathf.Max(0f, R - Vector2.Distance(p, s.cityCenter)) * 0.9f;
                    });

                if (route == null || route.Count < 8)
                {
                    Debug.LogWarning($"[Aurelius] River {si} could not find a route from bearing {bearing:F0}.");
                    continue;
                }

                int end = route[route.Count - 1];
                var finalOutlet = outlet;
                int joins = -1;
                if (riverCell[end] >= 0 && riverCell[end] != self)
                {
                    finalOutlet = RiverOutlet.River;
                    joins = created.FindIndex(r => r.name == $"River_{riverCell[end]}");
                    if (joins < 0) finalOutlet = outlet;
                }

                var pts = grid.ToPoints(route);
                // Push the mouth well into the lake / sea so the channel opens cleanly.
                if (finalOutlet != RiverOutlet.River)
                {
                    Vector2 dir = (pts[pts.Count - 1] - pts[Mathf.Max(0, pts.Count - 6)]).normalized;
                    pts.Add(pts[pts.Count - 1] + dir * 60f);
                }
                pts = AureliusPolyline.Simplify(pts, 10f);
                pts = AureliusPolyline.Chaikin(pts, 2);
                pts = AureliusPolyline.Resample(pts, 18f);
                pts = Meander(pts, grid, noise, s, src.size);
                pts = AureliusPolyline.Resample(pts, 36f);

                var go = new GameObject($"River_{si}");
                go.transform.SetParent(root, false);
                var river = go.AddComponent<AureliusRiver>();
                river.outlet = finalOutlet;
                river.joinsRiver = joins;
                river.widthStart = s.riverWidthStart * src.size;
                river.widthEnd = s.riverWidthEnd * src.size;
                river.depth = s.riverDepth * Mathf.Lerp(0.8f, 1.2f, src.size);
                foreach (var p in pts) river.points.Add(new Vector3(p.x, grid.height[grid.CellOf(p)], p.y));
                created.Add(river);
                Undo.RegisterCreatedObjectUndo(go, "Generate Rivers");

                foreach (var c in route) MarkCells(riverCell, grid, c, si);
            }
            return created;
        }

        static int HighestNear(AureliusRouteGrid g, int c, int radius)
        {
            int cx = c % g.dim, cy = c / g.dim, best = c;
            float bh = float.MinValue;
            for (int y = -radius; y <= radius; y++)
                for (int x = -radius; x <= radius; x++)
                {
                    int nx = cx + x, ny = cy + y;
                    if (nx < 0 || ny < 0 || nx >= g.dim || ny >= g.dim || x * x + y * y > radius * radius) continue;
                    int i = ny * g.dim + nx;
                    if (g.height[i] > bh && g.edge[i] > 150f && g.city[i] <= 0f) { bh = g.height[i]; best = i; }
                }
            return best;
        }

        static void MarkCells(int[] cells, AureliusRouteGrid g, int c, int id)
        {
            int cx = c % g.dim, cy = c / g.dim;
            for (int y = -1; y <= 1; y++)
                for (int x = -1; x <= 1; x++)
                {
                    int nx = cx + x, ny = cy + y;
                    if (nx < 0 || ny < 0 || nx >= g.dim || ny >= g.dim) continue;
                    if (cells[ny * g.dim + nx] < 0) cells[ny * g.dim + nx] = id;
                }
        }

        /// <summary>Lateral sine-noise offset: strong on flat lowland, weak in mountains, zero at the ends.</summary>
        static List<Vector2> Meander(List<Vector2> pts, AureliusRouteGrid g, AureliusNoise noise, AureliusWorldSettings s, float size)
        {
            var result = new List<Vector2>(pts.Count);
            float total = AureliusPolyline.Length(pts), dist = 0f;
            for (int i = 0; i < pts.Count; i++)
            {
                if (i > 0) dist += Vector2.Distance(pts[i - 1], pts[i]);
                Vector2 prev = pts[Mathf.Max(0, i - 1)], next = pts[Mathf.Min(pts.Count - 1, i + 1)];
                Vector2 tan = (next - prev).normalized;
                Vector2 perp = new Vector2(-tan.y, tan.x);
                int c = g.CellOf(pts[i]);
                float flat = 1f - Mathf.Clamp01(g.mountain[c] * 1.4f);
                float ends = AureliusWorldField.SmoothStep(0f, 160f, dist) * AureliusWorldField.SmoothStep(0f, 160f, total - dist);
                float nearCity = AureliusWorldField.SmoothStep(s.cityRadius + 180f, s.cityRadius + 380f, Vector2.Distance(pts[i], s.cityCenter));
                float off = noise.Fbm(dist / 230f, size * 13.7f, 2) * s.riverMeander * (0.3f + 0.7f * flat) * ends * nearCity * 1.6f;
                result.Add(pts[i] + perp * off);
            }
            return result;
        }
    }
}
