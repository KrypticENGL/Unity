using System;
using System.Collections.Generic;
using UnityEngine;

namespace Aurelius.World
{
    [Serializable]
    public class LakeIsland
    {
        [Tooltip("Offset from the lake centre (world XZ, metres).")]
        public Vector2 offset;
        public float radius = 60f;
        [Tooltip("Height of the island above the water.")]
        public float height = 14f;
        [Tooltip("0 = grassy mound, 1 = rocky outcrop.")]
        [Range(0, 1)] public float rockiness = 0.3f;
    }

    /// <summary>
    /// Editable lake. The shoreline is the lake radius times a ring of multipliers (drag the handles
    /// in the Scene view), plus fine procedural coves. The transform position is the lake centre and
    /// its Y is the water level.
    /// </summary>
    public class AureliusLake : MonoBehaviour
    {
        public float radius = 560f;
        [Tooltip("Shoreline radius multipliers, evenly spaced around the centre starting at world +X.")]
        public List<float> shape = new List<float>();
        [Tooltip("Amplitude of small coves/points along the shore (m).")]
        public float shoreDetail = 22f;
        public float depth = 16f;
        [Tooltip("Width of the shallow shelf around the edge (m).")]
        public float shelfWidth = 45f;
        public float beachWidth = 16f;
        public List<LakeIsland> islands = new List<LakeIsland>();

        public float WaterLevel => transform.position.y;
        public Vector2 Center => new Vector2(transform.position.x, transform.position.z);

        /// <summary>Shore radius toward a world-space angle (radians, atan2(z, x)).</summary>
        public float ShoreRadius(float angle)
        {
            int n = shape.Count;
            if (n < 3) return radius;
            float t = Mathf.Repeat(angle / (Mathf.PI * 2f), 1f) * n;
            int i1 = Mathf.FloorToInt(t) % n;
            float f = t - Mathf.Floor(t);
            float p0 = shape[(i1 - 1 + n) % n], p1 = shape[i1], p2 = shape[(i1 + 1) % n], p3 = shape[(i1 + 2) % n];
            float f2 = f * f, f3 = f2 * f;
            float m = 0.5f * (2f * p1 + (-p0 + p2) * f + (2f * p0 - 5f * p1 + 4f * p2 - p3) * f2 + (-p0 + 3f * p1 - 3f * p2 + p3) * f3);
            return radius * Mathf.Max(0.15f, m);
        }

        public Vector3 ShapeHandlePosition(int i)
        {
            float a = i / (float)shape.Count * Mathf.PI * 2f;
            float r = radius * shape[i];
            return new Vector3(Center.x + Mathf.Cos(a) * r, WaterLevel, Center.y + Mathf.Sin(a) * r);
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.2f, 0.6f, 1f, 1f);
            const int steps = 96;
            Vector3 prev = Vector3.zero;
            for (int i = 0; i <= steps; i++)
            {
                float a = i / (float)steps * Mathf.PI * 2f;
                float r = ShoreRadius(a);
                var p = new Vector3(Center.x + Mathf.Cos(a) * r, WaterLevel + 1f, Center.y + Mathf.Sin(a) * r);
                if (i > 0) Gizmos.DrawLine(prev, p);
                prev = p;
            }
            Gizmos.color = new Color(0.4f, 0.8f, 0.4f, 1f);
            foreach (var isl in islands)
                Gizmos.DrawWireSphere(new Vector3(Center.x + isl.offset.x, WaterLevel + isl.height * 0.5f, Center.y + isl.offset.y), isl.radius);
        }
    }
}
