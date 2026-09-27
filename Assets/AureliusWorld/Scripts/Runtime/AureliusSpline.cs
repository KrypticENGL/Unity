using System.Collections.Generic;
using UnityEngine;

namespace Aurelius.World
{
    /// <summary>
    /// Editable world-space spline (XZ control points, Catmull-Rom interpolated). Base for rivers and
    /// paths. Generators write the control points; you can move them with the scene handles and press
    /// GENERATE TERRAIN to re-carve. Only X and Z are used; heights are derived from the terrain.
    /// </summary>
    public abstract class AureliusSpline : MonoBehaviour
    {
        public List<Vector3> points = new List<Vector3>();
        public bool closed;

        public abstract Color GizmoColor { get; }

        /// <summary>Catmull-Rom densified XZ polyline with roughly <paramref name="step"/> metre spacing.</summary>
        public List<Vector2> Densify(float step)
        {
            var result = new List<Vector2>();
            int n = points.Count;
            if (n == 0) return result;
            if (n == 1) { result.Add(XZ(points[0])); return result; }

            int segments = closed ? n : n - 1;
            for (int i = 0; i < segments; i++)
            {
                Vector2 p0 = XZ(points[Wrap(i - 1, n)]);
                Vector2 p1 = XZ(points[i]);
                Vector2 p2 = XZ(points[Wrap(i + 1, n)]);
                Vector2 p3 = XZ(points[Wrap(i + 2, n)]);
                if (!closed)
                {
                    if (i == 0) p0 = p1 + (p1 - p2);
                    if (i + 2 >= n) p3 = p2 + (p2 - p1);
                }
                float len = Vector2.Distance(p1, p2);
                int sub = Mathf.Max(1, Mathf.CeilToInt(len / step));
                for (int s = 0; s < sub; s++)
                    result.Add(CatmullRom(p0, p1, p2, p3, s / (float)sub));
            }
            result.Add(closed ? XZ(points[0]) : XZ(points[n - 1]));
            return result;
        }

        static int Wrap(int i, int n) => ((i % n) + n) % n;
        static Vector2 XZ(Vector3 v) => new Vector2(v.x, v.z);

        static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            // Centripetal-ish tension via uniform CR; control points are dense enough to avoid loops.
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        protected virtual void OnDrawGizmos()
        {
            if (points.Count < 2) return;
            Gizmos.color = GizmoColor;
            var d = Densify(25f);
            for (int i = 1; i < d.Count; i++)
            {
                Vector3 a = new Vector3(d[i - 1].x, points[0].y, d[i - 1].y);
                Vector3 b = new Vector3(d[i].x, points[0].y, d[i].y);
                a.y = SampleY(i - 1, d.Count); b.y = SampleY(i, d.Count);
                Gizmos.DrawLine(a, b);
            }
        }

        float SampleY(int i, int count)
        {
            float t = count <= 1 ? 0 : i / (float)(count - 1) * (points.Count - 1);
            int a = Mathf.Clamp(Mathf.FloorToInt(t), 0, points.Count - 1);
            int b = Mathf.Min(a + 1, points.Count - 1);
            return Mathf.Lerp(points[a].y, points[b].y, t - a) + 2f;
        }
    }
}
