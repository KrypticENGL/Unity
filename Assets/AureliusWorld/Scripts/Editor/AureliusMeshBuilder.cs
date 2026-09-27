using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Aurelius.World.EditorTools
{
    /// <summary>Tiny procedural mesh helper for the stylized vegetation / rock library.</summary>
    public sealed class AureliusMeshBuilder
    {
        public readonly List<Vector3> v = new List<Vector3>();
        public readonly List<Vector3> n = new List<Vector3>();
        public readonly List<Vector2> uv = new List<Vector2>();
        readonly List<List<int>> subs = new List<List<int>>();

        public AureliusMeshBuilder(int submeshes = 1)
        {
            for (int i = 0; i < submeshes; i++) subs.Add(new List<int>());
        }

        public int Add(Vector3 pos, Vector3 normal, Vector2 texcoord)
        {
            v.Add(pos); n.Add(normal); uv.Add(texcoord);
            return v.Count - 1;
        }

        public void Tri(int sub, int a, int b, int c) { var l = subs[sub]; l.Add(a); l.Add(b); l.Add(c); }

        public Mesh ToMesh(string name)
        {
            var m = new Mesh { name = name };
            if (v.Count > 65000) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(v);
            m.SetNormals(n);
            m.SetUVs(0, uv);
            m.subMeshCount = subs.Count;
            for (int i = 0; i < subs.Count; i++) m.SetTriangles(subs[i], i);
            m.RecalculateBounds();
            m.RecalculateTangents();
            return m;
        }

        // --------------------------------------------------------------------------------------

        public void Cylinder(int sub, Vector3 baseCenter, float r0, float r1, float height, int sides)
        {
            int start = v.Count;
            for (int i = 0; i <= sides; i++)
            {
                float a = i / (float)sides * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                Add(baseCenter + dir * r0, dir, new Vector2(i / (float)sides, 0));
                Add(baseCenter + dir * r1 + Vector3.up * height, dir, new Vector2(i / (float)sides, 1));
            }
            for (int i = 0; i < sides; i++)
            {
                int a = start + i * 2;
                Tri(sub, a, a + 1, a + 3);
                Tri(sub, a, a + 3, a + 2);
            }
        }

        /// <summary>Cone with soft "spherized" normals (anime foliage shading) and an underside.</summary>
        public void Cone(int sub, Vector3 baseCenter, float radius, float height, int sides, float jitter, System.Random rng)
        {
            int apex = Add(baseCenter + Vector3.up * height, Vector3.up, new Vector2(0.5f, 1));
            int ring = v.Count;
            for (int i = 0; i < sides; i++)
            {
                float a = (i + (float)rng.NextDouble() * jitter) / sides * Mathf.PI * 2f;
                float r = radius * (1f + ((float)rng.NextDouble() - 0.5f) * jitter);
                var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                var p = baseCenter + dir * r + Vector3.up * (((float)rng.NextDouble() - 0.5f) * jitter * height * 0.15f);
                Add(p, (dir + Vector3.up * 0.75f).normalized, new Vector2(i / (float)sides, 0));
            }
            for (int i = 0; i < sides; i++) Tri(sub, apex, ring + (i + 1) % sides, ring + i);
            // underside, normals pointing down/out so it reads as shade from below
            int c = Add(baseCenter + Vector3.up * height * 0.12f, Vector3.down, new Vector2(0.5f, 0.5f));
            int ring2 = v.Count;
            for (int i = 0; i < sides; i++) Add(v[ring + i], (Vector3.down * 0.6f + (v[ring + i] - baseCenter).normalized).normalized, uv[ring + i]);
            for (int i = 0; i < sides; i++) Tri(sub, c, ring2 + i, ring2 + (i + 1) % sides);
        }

        /// <summary>Displaced icosphere blob. flat = faceted (rocks); otherwise spherized normals (foliage).</summary>
        public void Blob(int sub, Vector3 center, Vector3 scale, int subdiv, float displacement, int seed, bool flat, float flattenBottom = -2f)
        {
            Icosphere(subdiv, out var pos, out var tris);
            var noise = new AureliusNoise(seed);
            var disp = new Vector3[pos.Count];
            for (int i = 0; i < pos.Count; i++)
            {
                Vector3 d = pos[i];
                float k = 1f + displacement * (noise.Noise(d.x * 1.3f + d.z * 0.7f + 3.1f, d.y * 1.3f - d.x * 0.4f) * 0.7f
                                              + noise.Noise(d.z * 3.1f + 11f, d.y * 3.1f + d.x * 2f) * 0.3f);
                Vector3 p = d * k;
                if (p.y < flattenBottom) p.y = flattenBottom + (p.y - flattenBottom) * 0.15f;
                disp[i] = Vector3.Scale(p, scale);
            }
            if (flat)
            {
                for (int t = 0; t < tris.Count; t += 3)
                {
                    Vector3 a = disp[tris[t]], b = disp[tris[t + 1]], c = disp[tris[t + 2]];
                    Vector3 nn = Vector3.Cross(b - a, c - a).normalized;
                    int ia = Add(center + a, nn, new Vector2(a.x, a.z));
                    int ib = Add(center + b, nn, new Vector2(b.x, b.z));
                    int ic = Add(center + c, nn, new Vector2(c.x, c.z));
                    Tri(sub, ia, ib, ic);
                }
            }
            else
            {
                int start = v.Count;
                for (int i = 0; i < disp.Length; i++)
                {
                    Vector3 sn = (pos[i] + Vector3.up * 0.25f).normalized;
                    Add(center + disp[i], sn, new Vector2(pos[i].x * 0.5f + 0.5f, pos[i].y * 0.5f + 0.5f));
                }
                for (int t = 0; t < tris.Count; t += 3) Tri(sub, start + tris[t], start + tris[t + 1], start + tris[t + 2]);
            }
        }

        public void Append(Mesh mesh, Matrix4x4 m, int sub)
        {
            var mv = mesh.vertices; var mn = mesh.normals; var mu = mesh.uv;
            int start = v.Count;
            for (int i = 0; i < mv.Length; i++) Add(m.MultiplyPoint3x4(mv[i]), m.MultiplyVector(mn[i]).normalized, mu.Length > i ? mu[i] : Vector2.zero);
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                var t = mesh.GetTriangles(s);
                for (int i = 0; i < t.Length; i += 3) Tri(sub, start + t[i], start + t[i + 1], start + t[i + 2]);
            }
        }

        static void Icosphere(int subdiv, out List<Vector3> pos, out List<int> tris)
        {
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            pos = new List<Vector3>
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1)
            };
            for (int i = 0; i < pos.Count; i++) pos[i] = pos[i].normalized;
            tris = new List<int>
            {
                0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
                3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1
            };
            for (int s = 0; s < subdiv; s++)
            {
                var cache = new Dictionary<long, int>();
                var nt = new List<int>();
                var p = pos;
                int Mid(int a, int b)
                {
                    long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                    if (cache.TryGetValue(key, out int idx)) return idx;
                    p.Add(((p[a] + p[b]) * 0.5f).normalized);
                    cache[key] = p.Count - 1;
                    return p.Count - 1;
                }
                for (int i = 0; i < tris.Count; i += 3)
                {
                    int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                    int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                    nt.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
                }
                tris = nt;
            }
        }
    }
}
