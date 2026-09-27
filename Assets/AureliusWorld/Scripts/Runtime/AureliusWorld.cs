using System.Collections.Generic;
using UnityEngine;

namespace Aurelius.World
{
    public enum AureliusDebugMode
    {
        Off = 0,
        Height = 1,
        BiomeMask = 2,
        Mountains = 3,
        Forest = 4,
        Farms = 5,
        Water = 6,
        Paths = 7,
        CityExclusion = 8,
        TerrainChunks = 9,
        LODLevels = 10,
        Rivers = 11,
    }

    /// <summary>
    /// Root of the generated Aurelius world. Holds the settings asset and the debug view mode.
    /// Use the inspector buttons (or the Aurelius menu) to generate / regenerate / clear.
    /// </summary>
    [ExecuteAlways]
    public class AureliusWorld : MonoBehaviour
    {
        public AureliusWorldSettings settings;

        [Header("Debug")]
        public AureliusDebugMode debugMode = AureliusDebugMode.Off;
        [Tooltip("Draw city radius, region sectors, landmark pads and tile grid gizmos.")]
        public bool drawGizmos = true;

        static readonly int DebugModeId = Shader.PropertyToID("_AureliusDebugMode");
        static readonly int HeightRangeId = Shader.PropertyToID("_AureliusHeightRange");
        static readonly int LodParamsId = Shader.PropertyToID("_AureliusLodParams");

        public Transform TerrainRoot => transform.Find("Terrain");
        public Transform WaterRoot => transform.Find("Water");
        public Transform PathsRoot => transform.Find("Paths");

        public AureliusLake Lake => GetComponentInChildren<AureliusLake>(true);
        public List<AureliusRiver> Rivers { get { var l = new List<AureliusRiver>(); GetComponentsInChildren(true, l); return l; } }
        public List<AureliusPath> Paths { get { var l = new List<AureliusPath>(); GetComponentsInChildren(true, l); return l; } }

        void OnEnable() => ApplyDebugGlobals();
        void OnValidate() => ApplyDebugGlobals();
        void OnDisable() => Shader.SetGlobalFloat(DebugModeId, 0f);

        public void ApplyDebugGlobals()
        {
            Shader.SetGlobalFloat(DebugModeId, (float)debugMode);
            if (settings != null)
            {
                Shader.SetGlobalVector(HeightRangeId, new Vector4(settings.seaLevel - 20f, settings.cityFloorHeight, settings.mountainHeight + settings.summitExtraHeight, settings.snowLine));
            }
            var mgr = GetComponentInChildren<AureliusTerrainChunkManager>();
            float treeDist = mgr != null ? mgr.treeDistance[(int)mgr.quality] : 1600f;
            float detailDist = mgr != null ? mgr.detailDistance[(int)mgr.quality] : 140f;
            Shader.SetGlobalVector(LodParamsId, new Vector4(detailDist, 350f, 800f, treeDist));
        }

        void OnDrawGizmos()
        {
            if (!drawGizmos || settings == null) return;
            var s = settings;
            Vector3 c = new Vector3(s.cityCenter.x, s.cityFloorHeight + 2f, s.cityCenter.y);

            Gizmos.color = Color.white;
            DrawCircle(c, s.cityRadius, 128);
            Gizmos.color = new Color(1f, 1f, 1f, 0.35f);
            DrawCircle(c, s.cityRadius + s.cityTransitionWidth, 128);
            Gizmos.color = new Color(0.3f, 0.6f, 1f, 0.6f);
            DrawCircle(c, s.worldRadius, 256);

            // Compass arrow
            Vector2 n = s.NorthDir;
            Gizmos.color = Color.red;
            Gizmos.DrawLine(c, c + new Vector3(n.x, 0, n.y) * (s.cityRadius * 0.5f));

            // Landmark pads
            Gizmos.color = new Color(1f, 0.85f, 0.2f, 1f);
            foreach (var pad in s.landmarkPads)
            {
                if (pad == null || !pad.enabled) continue;
                if (pad.shape == LandmarkPadShape.Polygon && pad.polygon.Count > 2)
                {
                    for (int i = 0; i < pad.polygon.Count; i++)
                    {
                        var a = pad.polygon[i]; var b = pad.polygon[(i + 1) % pad.polygon.Count];
                        Gizmos.DrawLine(new Vector3(a.x, pad.edgeHeight + 1f, a.y), new Vector3(b.x, pad.edgeHeight + 1f, b.y));
                    }
                }
                else if (pad.shape == LandmarkPadShape.Ellipse)
                {
                    DrawEllipse(new Vector3(pad.center.x, pad.edgeHeight + 1f, pad.center.y), pad.radii, 64);
                }
            }

            // Tile grid
            Gizmos.color = new Color(1f, 1f, 1f, 0.15f);
            Vector2 min = s.WorldMin;
            for (int i = 0; i <= s.tilesPerSide; i++)
            {
                float o = i * s.tileSize;
                Gizmos.DrawLine(new Vector3(min.x + o, 0, min.y), new Vector3(min.x + o, 0, min.y + s.WorldSize));
                Gizmos.DrawLine(new Vector3(min.x, 0, min.y + o), new Vector3(min.x + s.WorldSize, 0, min.y + o));
            }
        }

        static void DrawCircle(Vector3 c, float r, int steps) => DrawEllipse(c, new Vector2(r, r), steps);

        static void DrawEllipse(Vector3 c, Vector2 r, int steps)
        {
            Vector3 prev = c + new Vector3(r.x, 0, 0);
            for (int i = 1; i <= steps; i++)
            {
                float a = i / (float)steps * Mathf.PI * 2f;
                Vector3 p = c + new Vector3(Mathf.Cos(a) * r.x, 0, Mathf.Sin(a) * r.y);
                Gizmos.DrawLine(prev, p);
                prev = p;
            }
        }
    }
}
