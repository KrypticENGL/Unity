using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Aurelius.World.EditorTools
{
    /// <summary>
    /// Coarse world grid (base height + masks) with an A* / Dijkstra router. Rivers and paths are
    /// routed on it, then smoothed into splines. Deterministic: no randomness beyond the seed noise.
    /// </summary>
    public sealed class AureliusRouteGrid
    {
        public readonly int dim;
        public readonly float cell;
        public readonly Vector2 origin;
        public readonly float[] height;
        public readonly float[] mountain, lakeSd, edge, city, forest, farm;
        public readonly AureliusWorldSettings S;

        public AureliusRouteGrid(AureliusWorldField field, float cellSize)
        {
            S = field.S;
            cell = cellSize;
            origin = S.WorldMin;
            dim = Mathf.CeilToInt(S.WorldSize / cell);
            int n = dim * dim;
            height = new float[n]; mountain = new float[n]; lakeSd = new float[n]; edge = new float[n]; city = new float[n];
            forest = new float[n]; farm = new float[n];
            Parallel.For(0, dim, y =>
            {
                for (int x = 0; x < dim; x++)
                {
                    var p = CellCenter(x, y);
                    var s = field.Sample(p.x, p.y);
                    int i = y * dim + x;
                    height[i] = s.height;
                    mountain[i] = s.mountain;
                    lakeSd[i] = s.lakeDistance;
                    edge[i] = s.edgeDistance;
                    city[i] = s.city;
                    forest[i] = s.forest;
                    farm[i] = s.farm;
                }
            });
        }

        public Vector2 CellCenter(int x, int y) => origin + new Vector2((x + 0.5f) * cell, (y + 0.5f) * cell);
        public Vector2 CellCenter(int i) => CellCenter(i % dim, i / dim);

        public int CellOf(Vector2 p)
        {
            int x = Mathf.Clamp(Mathf.FloorToInt((p.x - origin.x) / cell), 0, dim - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt((p.y - origin.y) / cell), 0, dim - 1);
            return y * dim + x;
        }

        static readonly int[] DX = { 1, -1, 0, 0, 1, 1, -1, -1 };
        static readonly int[] DY = { 0, 0, 1, -1, 1, -1, 1, -1 };

        /// <summary>
        /// Least-cost route from <paramref name="start"/> until <paramref name="isGoal"/> is true.
        /// <paramref name="stepCost"/>(from, to, length) returns the cost or +inf if blocked.
        /// </summary>
        public List<int> Route(int start, Func<int, bool> isGoal, Func<int, int, float, float> stepCost, Func<int, float> heuristic, int maxExpanded = 400000)
        {
            int n = dim * dim;
            var g = new float[n];
            var parent = new int[n];
            var closed = new bool[n];
            for (int i = 0; i < n; i++) { g[i] = float.MaxValue; parent[i] = -1; }
            var open = new MinHeap(4096);
            g[start] = 0;
            open.Push(start, heuristic(start));
            int expanded = 0;
            while (open.Count > 0)
            {
                int cur = open.Pop();
                if (closed[cur]) continue;
                closed[cur] = true;
                if (isGoal(cur)) return Build(parent, cur);
                if (++expanded > maxExpanded) break;
                int cx = cur % dim, cy = cur / dim;
                for (int k = 0; k < 8; k++)
                {
                    int nx = cx + DX[k], ny = cy + DY[k];
                    if (nx < 0 || ny < 0 || nx >= dim || ny >= dim) continue;
                    int nb = ny * dim + nx;
                    if (closed[nb]) continue;
                    float len = (k < 4 ? 1f : 1.41421f) * cell;
                    float c = stepCost(cur, nb, len);
                    if (float.IsInfinity(c)) continue;
                    float ng = g[cur] + c;
                    if (ng < g[nb])
                    {
                        g[nb] = ng;
                        parent[nb] = cur;
                        open.Push(nb, ng + heuristic(nb));
                    }
                }
            }
            return null;
        }

        static List<int> Build(int[] parent, int end)
        {
            var path = new List<int>();
            for (int c = end; c >= 0; c = parent[c]) path.Add(c);
            path.Reverse();
            return path;
        }

        public List<Vector2> ToPoints(List<int> cells)
        {
            var l = new List<Vector2>(cells.Count);
            foreach (var c in cells) l.Add(CellCenter(c));
            return l;
        }

        sealed class MinHeap
        {
            int[] items; float[] keys; public int Count;
            public MinHeap(int cap) { items = new int[cap]; keys = new float[cap]; }
            public void Push(int item, float key)
            {
                if (Count == items.Length) { Array.Resize(ref items, Count * 2); Array.Resize(ref keys, Count * 2); }
                int i = Count++;
                while (i > 0)
                {
                    int p = (i - 1) >> 1;
                    if (keys[p] <= key) break;
                    items[i] = items[p]; keys[i] = keys[p]; i = p;
                }
                items[i] = item; keys[i] = key;
            }
            public int Pop()
            {
                int top = items[0];
                int last = items[--Count]; float lk = keys[Count];
                int i = 0;
                while (true)
                {
                    int l = i * 2 + 1;
                    if (l >= Count) break;
                    int r = l + 1;
                    int m = r < Count && keys[r] < keys[l] ? r : l;
                    if (keys[m] >= lk) break;
                    items[i] = items[m]; keys[i] = keys[m]; i = m;
                }
                items[i] = last; keys[i] = lk;
                return top;
            }
        }
    }

    public static class AureliusPolyline
    {
        /// <summary>Douglas-Peucker simplification.</summary>
        public static List<Vector2> Simplify(List<Vector2> pts, float tolerance)
        {
            if (pts.Count < 3) return new List<Vector2>(pts);
            var keep = new bool[pts.Count];
            keep[0] = keep[pts.Count - 1] = true;
            var stack = new Stack<(int, int)>();
            stack.Push((0, pts.Count - 1));
            while (stack.Count > 0)
            {
                var (a, b) = stack.Pop();
                float best = 0; int idx = -1;
                for (int i = a + 1; i < b; i++)
                {
                    float d = DistToSegment(pts[i], pts[a], pts[b]);
                    if (d > best) { best = d; idx = i; }
                }
                if (idx >= 0 && best > tolerance)
                {
                    keep[idx] = true;
                    stack.Push((a, idx));
                    stack.Push((idx, b));
                }
            }
            var result = new List<Vector2>();
            for (int i = 0; i < pts.Count; i++) if (keep[i]) result.Add(pts[i]);
            return result;
        }

        public static List<Vector2> Chaikin(List<Vector2> pts, int iterations, bool closed = false)
        {
            var cur = pts;
            for (int it = 0; it < iterations; it++)
            {
                var next = new List<Vector2>();
                int n = cur.Count;
                if (!closed) next.Add(cur[0]);
                int segs = closed ? n : n - 1;
                for (int i = 0; i < segs; i++)
                {
                    Vector2 a = cur[i], b = cur[(i + 1) % n];
                    next.Add(Vector2.Lerp(a, b, 0.25f));
                    next.Add(Vector2.Lerp(a, b, 0.75f));
                }
                if (!closed) next.Add(cur[n - 1]);
                cur = next;
            }
            return cur;
        }

        public static List<Vector2> Resample(List<Vector2> pts, float spacing)
        {
            var result = new List<Vector2>();
            if (pts.Count == 0) return result;
            result.Add(pts[0]);
            float carry = 0;
            for (int i = 1; i < pts.Count; i++)
            {
                Vector2 a = pts[i - 1], b = pts[i];
                float len = Vector2.Distance(a, b);
                float t = spacing - carry;
                while (t <= len)
                {
                    result.Add(Vector2.Lerp(a, b, t / len));
                    t += spacing;
                }
                carry = len - (t - spacing);
            }
            if (Vector2.Distance(result[result.Count - 1], pts[pts.Count - 1]) > spacing * 0.3f) result.Add(pts[pts.Count - 1]);
            return result;
        }

        public static float DistToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float l2 = ab.sqrMagnitude;
            if (l2 < 1e-6f) return Vector2.Distance(p, a);
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / l2);
            return Vector2.Distance(p, a + ab * t);
        }

        public static float Length(List<Vector2> pts)
        {
            float l = 0;
            for (int i = 1; i < pts.Count; i++) l += Vector2.Distance(pts[i - 1], pts[i]);
            return l;
        }

        /// <summary>Segment intersection (returns true and the point if the two segments cross).</summary>
        public static bool Intersect(Vector2 a, Vector2 b, Vector2 c, Vector2 d, out Vector2 hit)
        {
            hit = default;
            Vector2 r = b - a, s = d - c;
            float denom = r.x * s.y - r.y * s.x;
            if (Mathf.Abs(denom) < 1e-6f) return false;
            Vector2 ca = c - a;
            float t = (ca.x * s.y - ca.y * s.x) / denom;
            float u = (ca.x * r.y - ca.y * r.x) / denom;
            if (t < 0 || t > 1 || u < 0 || u > 1) return false;
            hit = a + r * t;
            return true;
        }
    }
}
