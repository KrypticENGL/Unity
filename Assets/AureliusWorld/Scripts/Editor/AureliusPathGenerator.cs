using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Aurelius.World.EditorTools
{
    /// <summary>
    /// Creates the four radial corridors (N, E, S, W - future roads), the ring path around the city,
    /// and secondary paths routed by least-cost search to farms, forest clearings, the lake shore,
    /// mountain valleys and open country. Also records bridge sites where paths cross rivers.
    /// </summary>
    public static class AureliusPathGenerator
    {
        public static readonly string[] MajorNames = { "Corridor_North", "Corridor_East", "Corridor_South", "Corridor_West" };

        public static void GenerateMajor(AureliusWorld world)
        {
            var s = world.settings;
            var root = AureliusWorldBuilder.Child(world.transform, "Paths/Major");
            AureliusWorldBuilder.ClearChildren(root);
            var noise = new AureliusNoise(s.seed * 5 + 3);
            float end = s.worldRadius * (1f + s.coastIrregularity) + 120f;

            for (int i = 0; i < 4; i++)
            {
                float bearing = i * 90f;
                Vector2 dir = s.BearingToDir(bearing);
                Vector2 perp = new Vector2(-dir.y, dir.x);
                var go = new GameObject(MajorNames[i]);
                go.transform.SetParent(root, false);
                var path = go.AddComponent<AureliusPath>();
                path.kind = PathKind.Major;
                path.width = s.majorPathWidth;
                // Starts exactly at the reserved city edge (a little inside so the join is seamless).
                for (float d = s.cityRadius - 30f; d <= end; d += 60f)
                {
                    float straight = AureliusWorldField.SmoothStep(s.cityRadius + 60f, s.cityRadius + 600f, d);
                    float off = noise.Fbm(d / 1100f + i * 17.3f, i * 5.1f, 2) * s.corridorWander * straight;
                    Vector2 p = s.cityCenter + dir * d + perp * off;
                    path.points.Add(new Vector3(p.x, s.cityFloorHeight, p.y));
                }
                Undo.RegisterCreatedObjectUndo(go, "Generate Paths");
            }
        }

        public static void GenerateSecondary(AureliusWorld world)
        {
            var s = world.settings;
            var root = AureliusWorldBuilder.Child(world.transform, "Paths/Secondary");
            AureliusWorldBuilder.ClearChildren(root);

            var majors = world.Paths.FindAll(p => p.kind == PathKind.Major);
            var field = new AureliusWorldField(s, world.Lake, world.Rivers, majors);
            var grid = new AureliusRouteGrid(field, 12f);
            int n = grid.dim * grid.dim;

            var riverNear = new bool[n];
            Parallel.For(0, grid.dim, y =>
            {
                for (int x = 0; x < grid.dim; x++)
                {
                    float d = field.DistanceToRivers(grid.CellCenter(x, y), out float hw);
                    riverNear[y * grid.dim + x] = d < hw + 5f;
                }
            });

            var network = new bool[n];
            foreach (var m in majors) Rasterize(network, grid, m.Densify(6f));

            // Ring path hugging the city boundary.
            if (s.ringPath)
            {
                var go = new GameObject("Path_Ring");
                go.transform.SetParent(root, false);
                var ring = go.AddComponent<AureliusPath>();
                ring.kind = PathKind.Ring;
                ring.closed = true;
                ring.width = s.secondaryPathWidth * 1.5f;
                var rn = new AureliusNoise(s.seed * 5 + 9);
                for (int i = 0; i < 48; i++)
                {
                    float b = i * 7.5f;
                    float r = s.cityRadius + 60f + rn.Periodic(b, 1.5f, 2, 3f) * 18f;
                    var p = s.cityCenter + s.BearingToDir(b) * r;
                    ring.points.Add(new Vector3(p.x, s.cityFloorHeight, p.y));
                }
                Rasterize(network, grid, ring.Densify(6f));
                Undo.RegisterCreatedObjectUndo(go, "Generate Paths");
            }

            var pois = PickDestinations(grid, s);
            pois.Sort((a, b) => Vector2.Distance(a.pos, s.cityCenter).CompareTo(Vector2.Distance(b.pos, s.cityCenter)));

            var wanderNoise = new AureliusNoise(s.seed * 5 + 21);
            float StepCost(int a, int b, float len)
            {
                if (grid.city[b] > 0f || grid.lakeSd[b] < 12f || grid.edge[b] < 45f) return float.PositiveInfinity;
                float grade = Mathf.Abs(grid.height[b] - grid.height[a]) / len;
                if (grade > 0.45f) return float.PositiveInfinity;
                // Low-frequency noise in the cost makes paths wind naturally instead of running straight.
                Vector2 pb = grid.CellCenter(b);
                float wander = wanderNoise.Fbm(pb.x / 320f, pb.y / 320f, 2) * 0.5f + 0.5f;
                float c = len * (1f + 1.6f * wander + 70f * grade * grade + 2.5f * grid.mountain[b] + 0.3f * grid.forest[b]);
                if (riverNear[b] && !riverNear[a]) c += 160f; // a bridge: cross rarely, and straight
                if (network[b]) c *= 0.3f;
                return c;
            }

            int count = 0;
            for (int i = 0; i < pois.Count; i++)
            {
                var poi = pois[i];
                int start = grid.CellOf(poi.pos);
                if (network[start]) continue;
                var route = grid.Route(start, c => network[c], StepCost, c => 0f, 250000);
                if (route == null) continue;
                AddPath(root, grid, route, s, $"Path_{count++:00}_{poi.kind}");
                foreach (var c in route) network[c] = true;

                // Every third destination also links to its nearest neighbour: loops, not just trees.
                if (i % 3 == 1)
                {
                    int best = -1; float bd = float.MaxValue;
                    for (int j = 0; j < pois.Count; j++)
                    {
                        if (j == i) continue;
                        float d = Vector2.Distance(pois[j].pos, poi.pos);
                        if (d < bd && d > 250f) { bd = d; best = j; }
                    }
                    if (best >= 0 && bd < 1400f)
                    {
                        int goal = grid.CellOf(pois[best].pos);
                        Vector2 gp = pois[best].pos;
                        var link = grid.Route(start, c => c == goal, StepCost, c => Vector2.Distance(grid.CellCenter(c), gp) * 0.3f, 250000);
                        if (link != null && link.Count > 6)
                        {
                            AddPath(root, grid, link, s, $"Path_{count++:00}_Link");
                            foreach (var c in link) network[c] = true;
                        }
                    }
                }
            }
        }

        struct Poi { public Vector2 pos; public string kind; }

        static List<Poi> PickDestinations(AureliusRouteGrid g, AureliusWorldSettings s)
        {
            var rng = new System.Random(s.seed + 303);
            var result = new List<Poi>();
            int total = s.pathDestinations;
            int farms = Mathf.RoundToInt(total * 0.24f), clearings = Mathf.RoundToInt(total * 0.2f);
            int shore = Mathf.RoundToInt(total * 0.15f), valleys = Mathf.RoundToInt(total * 0.15f);
            int open = Mathf.Max(0, total - farms - clearings - shore - valleys);

            bool Land(int c) => g.city[c] <= 0f && g.edge[c] > 160f && g.lakeSd[c] > 30f &&
                                Vector2.Distance(g.CellCenter(c), s.cityCenter) > s.cityRadius + 250f;
            bool Spaced(Vector2 p, float min)
            {
                foreach (var q in result) if (Vector2.Distance(p, q.pos) < min) return false;
                return true;
            }
            void Pick(int wanted, string kind, float spacing, System.Func<int, bool> ok)
            {
                int got = 0;
                for (int attempt = 0; attempt < 20000 && got < wanted; attempt++)
                {
                    int c = rng.Next(g.dim * g.dim);
                    if (!Land(c) || !ok(c)) continue;
                    var p = g.CellCenter(c);
                    if (!Spaced(p, spacing)) continue;
                    result.Add(new Poi { pos = p, kind = kind });
                    got++;
                }
            }

            Pick(farms, "Farm", 380f, c => g.farm[c] > 0.55f);
            Pick(clearings, "Clearing", 420f, c => g.forest[c] < 0.15f && NeighbourForest(g, c) > 0.4f);
            Pick(shore, "Shore", 450f, c => g.lakeSd[c] < 70f);
            Pick(valleys, "Valley", 450f, c => g.mountain[c] > 0.3f && g.mountain[c] < 0.75f && g.height[c] < 230f);
            Pick(open, "Open", 500f, c => true);
            return result;
        }

        static float NeighbourForest(AureliusRouteGrid g, int c)
        {
            int cx = c % g.dim, cy = c / g.dim; float sum = 0; int k = 0;
            for (int y = -5; y <= 5; y += 2)
                for (int x = -5; x <= 5; x += 2)
                {
                    int nx = cx + x, ny = cy + y;
                    if (nx < 0 || ny < 0 || nx >= g.dim || ny >= g.dim) continue;
                    sum += g.forest[ny * g.dim + nx]; k++;
                }
            return k > 0 ? sum / k : 0;
        }

        static void AddPath(Transform root, AureliusRouteGrid grid, List<int> route, AureliusWorldSettings s, string name)
        {
            var pts = grid.ToPoints(route);
            pts = AureliusPolyline.Simplify(pts, 7f);
            pts = AureliusPolyline.Chaikin(pts, 2);
            pts = AureliusPolyline.Resample(pts, 28f);
            if (pts.Count < 2) return;
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            var path = go.AddComponent<AureliusPath>();
            path.kind = PathKind.Secondary;
            path.width = s.secondaryPathWidth;
            foreach (var p in pts) path.points.Add(new Vector3(p.x, grid.height[grid.CellOf(p)], p.y));
            Undo.RegisterCreatedObjectUndo(go, "Generate Paths");
        }

        static void Rasterize(bool[] cells, AureliusRouteGrid g, List<Vector2> pts)
        {
            foreach (var p in pts) cells[g.CellOf(p)] = true;
        }

        /// <summary>Empty markers where a path crosses a river: future bridge locations.</summary>
        public static void RebuildBridgeSites(AureliusWorld world)
        {
            var root = AureliusWorldBuilder.Child(world.transform, "Paths/BridgeSites");
            AureliusWorldBuilder.ClearChildren(root);
            var s = world.settings;
            var field = new AureliusWorldField(s, world.Lake, world.Rivers, world.Paths);
            int count = 0;
            foreach (var path in world.Paths)
            {
                var pp = path.Densify(8f);
                foreach (var river in world.Rivers)
                {
                    var rp = river.Densify(8f);
                    for (int i = 1; i < pp.Count; i++)
                        for (int j = 1; j < rp.Count; j++)
                        {
                            if (!AureliusPolyline.Intersect(pp[i - 1], pp[i], rp[j - 1], rp[j], out var hit)) continue;
                            var sample = field.Sample(hit.x, hit.y);
                            var go = new GameObject($"BridgeSite_{count++:00}_{path.name}_{river.name}");
                            go.transform.SetParent(root, false);
                            Vector2 dir = (pp[i] - pp[i - 1]).normalized;
                            go.transform.position = new Vector3(hit.x, sample.height + 0.5f, hit.y);
                            go.transform.rotation = Quaternion.LookRotation(new Vector3(dir.x, 0, dir.y));
                            j = rp.Count; // one crossing per river segment run
                        }
                }
            }
        }
    }
}
