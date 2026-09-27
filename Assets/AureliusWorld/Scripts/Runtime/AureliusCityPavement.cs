using System;
using System.Collections.Generic;
using UnityEngine;

namespace Aurelius.World
{
    public enum PaveLevel { Major = 1, Secondary = 2, Minor = 3 }

    [Serializable]
    public class PaveRoad
    {
        public string name = "Road";
        [Tooltip("Compass bearing (0 = North, 90 = East).")]
        public float bearing;
        public PaveLevel level = PaveLevel.Secondary;
        public float startRadius = 228f;
        public float endRadius = 670f;
        [Tooltip("0 = use the width of the level.")]
        public float widthOverride;
    }

    [Serializable]
    public class PaveRing
    {
        public string name = "Ring";
        public float radius = 330f;
        public PaveLevel level = PaveLevel.Secondary;
        [Tooltip("0 = Ring Width.")]
        public float widthOverride;
    }

    public enum PlazaShape { Rectangle, Ring }

    [Serializable]
    public class PavePlaza
    {
        public string name = "Plaza";
        public PlazaShape shape = PlazaShape.Rectangle;
        public Vector2 center;
        [Tooltip("Rectangle: full size (x along the rotation, y across).")]
        public Vector2 size = new Vector2(40, 40);
        [Tooltip("Rectangle rotation in world degrees (0 = world +X). Align with the building.")]
        public float rotation;
        [Tooltip("Ring: inner / outer radius around the centre (for round buildings).")]
        public Vector2 ringRadii = new Vector2(100, 130);
        public PaveLevel level = PaveLevel.Secondary;
    }

    /// <summary>Oriented rectangle where the pavement is cut out (a landmark's own ground piece).</summary>
    [Serializable]
    public class PaveExclusion
    {
        public string name = "Exclusion";
        public Vector2 center;
        public Vector2 size = new Vector2(10, 10);
        public float rotation;
    }

    /// <summary>
    /// Royal-city paving for the reserved Aurelius disc. The meshes are a plain draped disc (see the
    /// inspector's BUILD PAVEMENT MESH); every paving decision (which system, stone size, orientation,
    /// borders) is made in the Aurelius/City Pavement shader from the layout below, so layout edits are live.
    /// </summary>
    [ExecuteAlways]
    public class AureliusCityPavement : MonoBehaviour
    {
        [Header("Layout")]
        [Tooltip("City centre (world XZ). The castle is at the origin.")]
        public Vector2 center = Vector2.zero;
        [Tooltip("World direction of North in degrees from +Z (the castle uses 180: North = -Z).")]
        public float northYaw = 180f;
        [Tooltip("Pavement starts here (the castle moat's outer bank).")]
        public float innerRadius = 228.1f;
        [Tooltip("Outer edge of the royal fan-paved heart around the castle.")]
        public float centralPlazaRadius = 300f;
        [Tooltip("Aurelius Radius: pavement boundary (the reserved city radius).")]
        public float outerRadius = 670f;
        public float curbWidth = 2.4f;

        [Header("Road Widths (m)")]
        public float majorRoadWidth = 18f;
        public float secondaryRoadWidth = 10f;
        public float minorRoadWidth = 6.5f;
        public float ringWidth = 10f;

        [Header("Tiles")]
        [Tooltip("Base size of the general city-ground stones (length, width). Road / plaza stones scale from it.")]
        public Vector2 tileSize = new Vector2(0.9f, 0.6f);
        [Tooltip("Rotates the stone pattern of the central plaza, general ground and building plazas (degrees).")]
        public float tileRotation;
        public float borderWidth = 1.3f;
        [Tooltip("Spacing of the decorative rings in the central plaza (m).")]
        public float decorativeRingSpacing = 18f;
        [Range(4, 64)] public int centralSpokes = 16;

        [Header("Surface")]
        [Tooltip("Height of the pavement above the terrain (m). Rebuild the mesh after changing.")]
        public float pavementHeight = 0.12f;
        public Color pavementColor = new Color(0.96f, 0.91f, 0.81f);
        public Color secondaryColor = new Color(0.88f, 0.83f, 0.74f);
        public Color accentColor = new Color(0.62f, 0.66f, 0.74f);
        public Color borderColor = new Color(0.74f, 0.70f, 0.66f);
        [Range(0, 1)] public float variationAmount = 0.55f;
        public Material material;

        [Header("Landmark Ground Mask (baked)")]
        [Tooltip("Where landmarks bring their own ground (castle slabs, University lawns & paths) the pavement is cut out.")]
        public Texture2D landmarkMask;
        [Tooltip("World XZ rectangle covered by the mask: x, y = min corner, z, w = size.")]
        public Vector4 landmarkMaskRect;

        [Header("Systems")]
        public List<PaveRoad> roads = new List<PaveRoad>();
        public List<PaveRing> rings = new List<PaveRing>();
        public List<PavePlaza> plazas = new List<PavePlaza>();
        public List<PaveExclusion> exclusions = new List<PaveExclusion>();

        const int MaxRoads = 24, MaxRings = 8, MaxPlazas = 8, MaxExcl = 24;
        static readonly Vector4[] roadsA = new Vector4[MaxRoads], roadsB = new Vector4[MaxRoads];
        static readonly Vector4[] ringsA = new Vector4[MaxRings];
        static readonly Vector4[] plazasA = new Vector4[MaxPlazas], plazasB = new Vector4[MaxPlazas];
        static readonly Vector4[] exclA = new Vector4[MaxExcl], exclB = new Vector4[MaxExcl];

        void OnEnable() => Apply();
        void OnValidate() => Apply();

        public Vector2 NorthDir => new Vector2(Mathf.Sin(northYaw * Mathf.Deg2Rad), Mathf.Cos(northYaw * Mathf.Deg2Rad));

        public Vector2 BearingToDir(float bearing)
        {
            var n = NorthDir;
            var e = new Vector2(n.y, -n.x);
            float a = bearing * Mathf.Deg2Rad;
            return n * Mathf.Cos(a) + e * Mathf.Sin(a);
        }

        public float WidthOf(PaveLevel level) => level == PaveLevel.Major ? majorRoadWidth : level == PaveLevel.Secondary ? secondaryRoadWidth : minorRoadWidth;

        /// <summary>Pushes the layout to the shader (globals) and the look to the material.</summary>
        public void Apply()
        {
            Shader.SetGlobalVector("_PaveCenter", new Vector4(center.x, center.y, outerRadius, curbWidth));
            Shader.SetGlobalVector("_PaveCentral", new Vector4(innerRadius, centralPlazaRadius, decorativeRingSpacing, centralSpokes));

            // Majors first so they win at crossings.
            var sorted = new List<PaveRoad>(roads);
            sorted.Sort((a, b) => ((int)a.level).CompareTo((int)b.level));
            int nr = Mathf.Min(sorted.Count, MaxRoads);
            for (int i = 0; i < MaxRoads; i++)
            {
                if (i < nr)
                {
                    var r = sorted[i];
                    var d = BearingToDir(r.bearing);
                    float w = r.widthOverride > 0 ? r.widthOverride : WidthOf(r.level);
                    roadsA[i] = new Vector4(d.x, d.y, r.startRadius, r.endRadius);
                    roadsB[i] = new Vector4(w * 0.5f, (int)r.level, 0, 0);
                }
                else { roadsA[i] = Vector4.zero; roadsB[i] = Vector4.zero; }
            }
            int ng = Mathf.Min(rings.Count, MaxRings);
            for (int i = 0; i < MaxRings; i++)
            {
                if (i < ng)
                {
                    var g = rings[i];
                    float w = g.widthOverride > 0 ? g.widthOverride : ringWidth;
                    ringsA[i] = new Vector4(g.radius, w * 0.5f, Mathf.Min((int)g.level, 2), 0);
                }
                else ringsA[i] = Vector4.zero;
            }
            int np = Mathf.Min(plazas.Count, MaxPlazas);
            for (int i = 0; i < MaxPlazas; i++)
            {
                if (i < np)
                {
                    var p = plazas[i];
                    if (p.shape == PlazaShape.Rectangle)
                        plazasA[i] = new Vector4(p.center.x, p.center.y, p.size.x * 0.5f, p.size.y * 0.5f);
                    else
                        plazasA[i] = new Vector4(p.center.x, p.center.y, p.ringRadii.x, p.ringRadii.y);
                    plazasB[i] = new Vector4(p.shape == PlazaShape.Rectangle ? 0 : 1, p.rotation * Mathf.Deg2Rad, (int)p.level, 0);
                }
                else { plazasA[i] = Vector4.zero; plazasB[i] = Vector4.zero; }
            }
            int ne = Mathf.Min(exclusions.Count, MaxExcl);
            for (int i = 0; i < MaxExcl; i++)
            {
                if (i < ne)
                {
                    var e = exclusions[i];
                    float a = e.rotation * Mathf.Deg2Rad;
                    exclA[i] = new Vector4(e.center.x, e.center.y, e.size.x * 0.5f, e.size.y * 0.5f);
                    exclB[i] = new Vector4(Mathf.Cos(a), Mathf.Sin(a), 0, 0);
                }
                else { exclA[i] = Vector4.zero; exclB[i] = Vector4.zero; }
            }
            Shader.SetGlobalVector("_PaveCounts", new Vector4(nr, ng, np, ne));
            if (landmarkMask != null && landmarkMaskRect.z > 0)
            {
                Shader.SetGlobalTexture("_PaveLandmarkMask", landmarkMask);
                Shader.SetGlobalVector("_PaveMaskRect", new Vector4(landmarkMaskRect.x, landmarkMaskRect.y, 1f / landmarkMaskRect.z, 1f / landmarkMaskRect.w));
            }
            else
            {
                Shader.SetGlobalTexture("_PaveLandmarkMask", Texture2D.blackTexture);
                Shader.SetGlobalVector("_PaveMaskRect", Vector4.zero);
            }
            Shader.SetGlobalVectorArray("_PaveRoads", roadsA);
            Shader.SetGlobalVectorArray("_PaveRoadInfo", roadsB);
            Shader.SetGlobalVectorArray("_PaveRings", ringsA);
            Shader.SetGlobalVectorArray("_PavePlazas", plazasA);
            Shader.SetGlobalVectorArray("_PavePlazaInfo", plazasB);
            Shader.SetGlobalVectorArray("_PaveExcl", exclA);
            Shader.SetGlobalVectorArray("_PaveExclInfo", exclB);

            if (material != null)
            {
                material.SetColor("_StoneBase", pavementColor);
                material.SetColor("_StoneSecondary", secondaryColor);
                material.SetColor("_Accent", accentColor);
                material.SetColor("_BorderColor", borderColor);
                material.SetFloat("_Variation", variationAmount);
                material.SetFloat("_BorderWidth", borderWidth);
                material.SetFloat("_TileRotation", tileRotation);
                Vector2 t = tileSize;
                material.SetVector("_TileGeneral", new Vector4(t.x, t.y, 0, 0));
                material.SetVector("_TileSecondary", new Vector4(t.x * 1.6f, t.y * 1.2f, 0, 0));
                material.SetVector("_TilePlaza", new Vector4(t.x * 1.8f, t.y * 1.35f, 0, 0));
                material.SetVector("_TileMajor", new Vector4(t.x * 2.7f, t.y * 2f, 0, 0));
                material.SetVector("_TileCentral", new Vector4(t.x * 3.1f, t.y * 2.7f, 0, 0));
            }
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.9f, 0.6f);
            DrawCircle(innerRadius); DrawCircle(centralPlazaRadius); DrawCircle(outerRadius);
            Gizmos.color = new Color(0.6f, 0.8f, 1f);
            foreach (var g in rings) DrawCircle(g.radius);
            Gizmos.color = new Color(1f, 0.6f, 0.3f);
            foreach (var r in roads)
            {
                var d = BearingToDir(r.bearing);
                Gizmos.DrawLine(new Vector3(center.x + d.x * r.startRadius, 6, center.y + d.y * r.startRadius),
                                new Vector3(center.x + d.x * r.endRadius, 6, center.y + d.y * r.endRadius));
            }
        }

        void DrawCircle(float radius)
        {
            Vector3 prev = new Vector3(center.x + radius, 6, center.y);
            for (int i = 1; i <= 128; i++)
            {
                float a = i / 128f * Mathf.PI * 2f;
                var p = new Vector3(center.x + Mathf.Cos(a) * radius, 6, center.y + Mathf.Sin(a) * radius);
                Gizmos.DrawLine(prev, p);
                prev = p;
            }
        }
    }
}
