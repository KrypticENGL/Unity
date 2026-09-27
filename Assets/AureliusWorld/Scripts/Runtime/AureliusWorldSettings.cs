using System;
using System.Collections.Generic;
using UnityEngine;

namespace Aurelius.World
{
    public enum LandmarkPadShape { Polygon, Ellipse }

    /// <summary>
    /// A footprint inside the reserved city where the terrain is shaped to meet an existing landmark.
    /// Inside the footprint (minus <see cref="inset"/>) the ground sits at <see cref="innerHeight"/>;
    /// at the footprint edge it is <see cref="edgeHeight"/>; outside it eases to the city floor over
    /// <see cref="blend"/> metres. Use a low inner height to sink the terrain under a model's own ground.
    /// </summary>
    [Serializable]
    public class LandmarkPad
    {
        public string name = "Landmark";
        public bool enabled = true;
        public LandmarkPadShape shape = LandmarkPadShape.Polygon;
        [Tooltip("World XZ footprint (convex). Used when Shape = Polygon.")]
        public List<Vector2> polygon = new List<Vector2>();
        [Tooltip("World XZ centre. Used when Shape = Ellipse.")]
        public Vector2 center;
        [Tooltip("Ellipse radii in metres. Used when Shape = Ellipse.")]
        public Vector2 radii = new Vector2(100, 100);
        public float innerHeight = 4f;
        public float edgeHeight = 4f;
        [Min(0)] public float inset = 4f;
        [Min(1)] public float blend = 40f;
    }

    [Serializable]
    public class RegionSector
    {
        [Tooltip("Compass bearing in degrees (0 = North, 90 = East).")]
        public float bearing;
        [Tooltip("Total angular width in degrees.")]
        public float arc = 90f;
        [Tooltip("Soft edge in degrees.")]
        public float softness = 25f;
        [Tooltip("Distance from the city centre where the region begins (m).")]
        public float innerDistance;
        [Tooltip("Distance from the city centre where the region ends (m). 0 = world edge.")]
        public float outerDistance;
        [Range(0, 1)] public float strength = 1f;

        public RegionSector() { }
        public RegionSector(float bearing, float arc, float softness, float inner, float outer, float strength)
        {
            this.bearing = bearing; this.arc = arc; this.softness = softness;
            innerDistance = inner; outerDistance = outer; this.strength = strength;
        }
    }

    /// <summary>
    /// Every parameter of the Aurelius world. Deterministic: the same asset + seed always
    /// regenerates the same terrain. Scene-side editable features (lake shape, river and path
    /// splines) live on components under the AureliusWorld root so they can be hand-edited.
    /// </summary>
    [CreateAssetMenu(menuName = "Aurelius/World Settings", fileName = "AureliusWorldSettings")]
    public class AureliusWorldSettings : ScriptableObject
    {
        [Header("General")]
        public int seed = 1337;
        [Tooltip("World direction of NORTH, in degrees clockwise from +Z. The castle's gates put North at -Z, so the default is 180.")]
        public float northYaw = 180f;
        [Tooltip("World XZ of the city centre (the castle).")]
        public Vector2 cityCenter = Vector2.zero;

        [Header("Tiles")]
        [Tooltip("Tiles per side. 3 gives the named 3x3 layout (Terrain_NW ... Terrain_SE).")]
        [Range(1, 16)] public int tilesPerSide = 3;
        [Tooltip("Tile edge length in metres (1 unit = 1 m).")]
        public float tileSize = 2048f;
        [Tooltip("Heightmap samples per tile edge (power of two + 1). 1025 on 2048 m = 2 m per sample.")]
        public int heightmapResolution = 1025;
        [Tooltip("Splat (biome material) texels per tile edge.")]
        public int alphamapResolution = 1024;
        [Tooltip("Grass/detail cells per tile edge.")]
        public int detailResolution = 512;
        [Tooltip("Resolution of the exported mask textures per tile.")]
        public int maskResolution = 512;
        [Tooltip("World Y of the lowest representable terrain height.")]
        public float terrainBaseY = -80f;
        [Tooltip("Vertical range of the terrain (m).")]
        public float terrainHeight = 1400f;

        [Header("World Shape")]
        [Tooltip("Radius of the landmass around the city (m).")]
        public float worldRadius = 2850f;
        [Range(0, 0.2f)] public float coastIrregularity = 0.07f;
        public float seaLevel = -18f;
        public float coastCliffWidth = 80f;
        [Tooltip("Typical height of open land outside the city.")]
        public float landHeight = 7f;
        [Tooltip("Extra height the land gains toward the outer cliffs.")]
        public float outerPlateauHeight = 22f;
        public float rollingHillHeight = 14f;

        [Header("City (Reserved)")]
        [Tooltip("Reserved radius around the castle. The Colosseum wall reaches 605 m, the University 632 m.")]
        public float cityRadius = 660f;
        [Tooltip("Ground height of the city (castle apron = 4).")]
        public float cityFloorHeight = 3.95f;
        [Tooltip("Width of the soft ring where the city floor blends into natural terrain.")]
        public float cityTransitionWidth = 240f;
        public List<LandmarkPad> landmarkPads = new List<LandmarkPad>();

        [Header("Mountains")]
        public float mountainHeight = 620f;
        public RegionSector mountainRegion = new RegionSector(5f, 125f, 25f, 1050f, 0f, 1f);
        public RegionSector foothillRegion = new RegionSector(58f, 55f, 18f, 1350f, 0f, 0.55f);
        [Tooltip("Number of major ridge spines inside the mountain region.")]
        [Range(1, 8)] public int ridgeCount = 5;
        [Tooltip("Bearing of the tallest (snow) summit.")]
        public float summitBearing = 22f;
        public float summitExtraHeight = 260f;
        [Tooltip("Altitude above which snow appears (world Y).")]
        public float snowLine = 520f;
        [Tooltip("Half width of valley passes carved along major corridors through mountains.")]
        public float corridorValleyWidth = 90f;

        [Header("Forest")]
        public RegionSector forestRegion = new RegionSector(210f, 150f, 35f, 760f, 0f, 1f);
        public RegionSector southEastForest = new RegionSector(140f, 55f, 20f, 800f, 0f, 0.8f);
        public RegionSector westForest = new RegionSector(262f, 50f, 20f, 1500f, 0f, 0.8f);
        [Range(0, 2)] public float forestDensity = 1f;
        [Range(0, 1)] public float clearingAmount = 0.28f;

        [Header("Farms")]
        public RegionSector farmRegion = new RegionSector(302f, 85f, 22f, 720f, 2350f, 1f);
        public RegionSector westFarms = new RegionSector(265f, 35f, 15f, 720f, 1500f, 0.75f);
        [Tooltip("Typical farm field size (m).")]
        public float fieldSize = 85f;
        [Tooltip("Rotation of the field grid (degrees).")]
        public float fieldRotation = 18f;

        [Header("Rocky Hills")]
        public RegionSector rockyRegion = new RegionSector(135f, 55f, 20f, 900f, 0f, 1f);
        public RegionSector ruggedSouth = new RegionSector(182f, 80f, 25f, 2000f, 0f, 0.7f);
        public float rockyHillHeight = 42f;

        [Header("Lake")]
        [Tooltip("Compass bearing of the lake centre. East = 90; slightly south keeps it clear of the East corridor.")]
        public float lakeBearing = 116f;
        public float lakeDistance = 1720f;
        public float lakeRadius = 560f;
        [Range(0, 0.6f)] public float lakeIrregularity = 0.34f;
        public float lakeWaterLevel = 1f;
        public float lakeDepth = 16f;
        [Range(0, 8)] public int lakeIslandCount = 4;

        [Header("Rivers")]
        [Range(0, 6)] public int riverCount = 5;
        public float riverWidthStart = 3.5f;
        public float riverWidthEnd = 13f;
        public float riverDepth = 2.6f;
        [Tooltip("How much rivers wander across flat land (m).")]
        public float riverMeander = 26f;

        [Header("Paths")]
        [Tooltip("Width of the four radial corridors (future roads).")]
        public float majorPathWidth = 14f;
        public float secondaryPathWidth = 5f;
        [Tooltip("How far each corridor may wander from its straight line (m).")]
        public float corridorWander = 110f;
        [Tooltip("Adds a ring path hugging the city boundary.")]
        public bool ringPath = true;
        [Range(0, 60)] public int pathDestinations = 26;
        [Tooltip("How far paths sit below the surrounding ground (m).")]
        public float pathDepression = 0.35f;

        [Header("Vegetation")]
        [Tooltip("Spacing of the tree candidate grid (m). Lower = denser, more expensive.")]
        public float treeSpacing = 8.5f;
        [Range(0, 2)] public float grassDensity = 1f;
        [Range(0, 2)] public float rockDensity = 1f;
        [Tooltip("Tree density inside the city transition ring (controlled).")]
        [Range(0, 1)] public float nearCityVegetation = 0.25f;
        public float treeDistance = 1600f;
        public float detailDistance = 140f;

        // ------------------------------------------------------------------------------------

        public float WorldSize => tilesPerSide * tileSize;
        public Vector2 WorldMin => cityCenter - Vector2.one * (WorldSize * 0.5f);

        public Vector2 NorthDir => new Vector2(Mathf.Sin(northYaw * Mathf.Deg2Rad), Mathf.Cos(northYaw * Mathf.Deg2Rad));
        public Vector2 EastDir { get { var n = NorthDir; return new Vector2(n.y, -n.x); } }

        /// <summary>World XZ direction of a compass bearing (0 = N, 90 = E).</summary>
        public Vector2 BearingToDir(float bearing)
        {
            float a = bearing * Mathf.Deg2Rad;
            return NorthDir * Mathf.Cos(a) + EastDir * Mathf.Sin(a);
        }

        /// <summary>Compass bearing (0..360) of a world XZ offset from the city centre.</summary>
        public float Bearing(Vector2 offset)
        {
            float b = Mathf.Atan2(Vector2.Dot(offset, EastDir), Vector2.Dot(offset, NorthDir)) * Mathf.Rad2Deg;
            return b < 0 ? b + 360f : b;
        }

        public Vector2 LakeCenter => cityCenter + BearingToDir(lakeBearing) * lakeDistance;

        void OnValidate()
        {
            heightmapResolution = Mathf.ClosestPowerOfTwo(Mathf.Max(33, heightmapResolution - 1)) + 1;
            alphamapResolution = Mathf.ClosestPowerOfTwo(Mathf.Clamp(alphamapResolution, 16, 4096));
            detailResolution = Mathf.Clamp(detailResolution / 16 * 16, 16, 4048);
            maskResolution = Mathf.ClosestPowerOfTwo(Mathf.Clamp(maskResolution, 32, 2048));
            cityRadius = Mathf.Max(50f, cityRadius);
            worldRadius = Mathf.Clamp(worldRadius, cityRadius + 600f, WorldSize * 0.5f - 60f);
        }
    }
}
