using System;
using System.Collections.Generic;
using UnityEngine;

namespace Aurelius.World
{
    /// <summary>
    /// A water surface that belongs to a city landmark (castle moat, canals...). The terrain is carved
    /// below it so the landmark's own water stays visible. Triangles are world XZ, 3 per triangle.
    /// </summary>
    public sealed class WaterCarve
    {
        public Vector2[] triangles;
        public float level;
        public float depth = 4.5f;
    }

    /// <summary>Everything the generator knows about one point of the world.</summary>
    public struct WorldSample
    {
        public float height;
        public float mountain;      // MountainMask
        public float forest;        // ForestMask (tree density, with clusters and clearings)
        public float forestRegion;  // raw forest biome weight
        public float farm;          // FarmMask
        public float rocky;         // rocky hills / formations
        public float grass;         // GrasslandMask (whatever is not another biome)
        public float water;         // LakeMask (lake + sea)
        public float river;         // RiverMask (channel)
        public float riverBank;
        public float path;          // PathMask
        public float majorPath;
        public float city;          // CityReservedMask
        public float cityBlend;     // 0 at the city edge -> 1 where natural terrain is fully restored
        public float lakeDistance;  // signed distance to the lake shore (m), negative = in the lake
        public float edgeDistance;  // distance inside the coastline (m), negative = sea
    }

    /// <summary>
    /// Deterministic sampler for the whole Aurelius world. Pure math over the settings and the
    /// scene's lake / river / path splines, so any point (and therefore any tile) can be generated
    /// independently and neighbouring tiles always match. Thread-safe after construction.
    /// </summary>
    public sealed class AureliusWorldField
    {
        public readonly AureliusWorldSettings S;

        readonly AureliusNoise nWarp, nLand, nHill, nMtn, nDetail, nForest, nEdge, nShore, nMisc;
        readonly Vector2 center;
        readonly float cityR;

        // Lake
        readonly bool hasLake;
        readonly Vector2 lakeC;
        readonly float lakeR, lakeLevel, lakeDepth, lakeShelf, lakeBeach, lakeShoreDetail;
        readonly float[] lakeShape;
        readonly LakeIsland[] islands;

        // Splines
        readonly SplineSet majors = new SplineSet();
        readonly SplineSet minors = new SplineSet();
        readonly SplineSet rivers = new SplineSet();

        // Mountain structure
        struct Spine { public float bearing, halfSpan, radius, width, wobbleSeed; }
        readonly Spine[] spines;
        readonly Vector2 summitPos;

        // Landmark pads
        struct Pad { public LandmarkPadShape shape; public Vector2[] poly; public Vector2 c, radii; public float inner, edge, inset, blend; public Rect bounds; }
        readonly Pad[] pads;

        // Landmark water carves (rasterised at 1 m)
        struct Carve { public Rect bounds; public int w, h; public bool[] mask; public float level, depth; }
        readonly Carve[] carves;

        [ThreadStatic] static Hit[] hitBuffer;

        public IReadOnlyList<FieldSpline> Rivers => rivers.splines;
        public IReadOnlyList<FieldSpline> MajorPaths => majors.splines;
        public IReadOnlyList<FieldSpline> MinorPaths => minors.splines;
        public bool HasLake => hasLake;
        public float LakeLevel => lakeLevel;
        public Vector2 LakeCenter => lakeC;
        public float LakeRadius => lakeR;

        public AureliusWorldField(AureliusWorldSettings settings, AureliusLake lake, IList<AureliusRiver> riverSplines, IList<AureliusPath> pathSplines,
                                  IList<WaterCarve> waterCarves = null)
        {
            S = settings;
            int seed = settings.seed;
            nWarp = new AureliusNoise(seed * 7 + 1);
            nLand = new AureliusNoise(seed * 7 + 2);
            nHill = new AureliusNoise(seed * 7 + 3);
            nMtn = new AureliusNoise(seed * 7 + 4);
            nDetail = new AureliusNoise(seed * 7 + 5);
            nForest = new AureliusNoise(seed * 7 + 6);
            nEdge = new AureliusNoise(seed * 7 + 8);
            nShore = new AureliusNoise(seed * 7 + 9);
            nMisc = new AureliusNoise(seed * 7 + 10);
            center = settings.cityCenter;
            cityR = settings.cityRadius;

            // --- Lake -------------------------------------------------------------------------
            if (lake != null && lake.isActiveAndEnabled && lake.shape.Count >= 3)
            {
                hasLake = true;
                lakeC = lake.Center;
                lakeR = lake.radius;
                lakeLevel = lake.WaterLevel;
                lakeDepth = lake.depth;
                lakeShelf = lake.shelfWidth;
                lakeBeach = lake.beachWidth;
                lakeShoreDetail = lake.shoreDetail;
                lakeShape = lake.shape.ToArray();
                islands = lake.islands.ToArray();
            }
            else
            {
                lakeLevel = settings.lakeWaterLevel;
                islands = Array.Empty<LakeIsland>();
                lakeShape = Array.Empty<float>();
            }

            // --- Mountain spines (deterministic) -----------------------------------------------
            var rng = new System.Random(seed + 101);
            var mr = settings.mountainRegion;
            spines = new Spine[settings.ridgeCount];
            for (int i = 0; i < spines.Length; i++)
            {
                float f = spines.Length == 1 ? 0.5f : i / (float)(spines.Length - 1);
                spines[i] = new Spine
                {
                    bearing = mr.bearing + (f - 0.5f) * mr.arc * 0.9f + (float)(rng.NextDouble() - 0.5) * 14f,
                    halfSpan = 16f + (float)rng.NextDouble() * 22f,
                    radius = Mathf.Lerp(mr.innerDistance + 450f, settings.worldRadius - 280f, (float)rng.NextDouble()),
                    width = 230f + (float)rng.NextDouble() * 220f,
                    wobbleSeed = (float)rng.NextDouble() * 100f
                };
            }
            summitPos = center + settings.BearingToDir(settings.summitBearing) * (settings.worldRadius - 640f);

            // --- Landmark pads ----------------------------------------------------------------
            var padList = new List<Pad>();
            foreach (var lp in settings.landmarkPads)
            {
                if (lp == null || !lp.enabled) continue;
                var pad = new Pad { shape = lp.shape, inner = lp.innerHeight, edge = lp.edgeHeight, inset = Mathf.Max(0.01f, lp.inset), blend = Mathf.Max(1f, lp.blend) };
                Rect b;
                if (lp.shape == LandmarkPadShape.Polygon)
                {
                    if (lp.polygon == null || lp.polygon.Count < 3) continue;
                    pad.poly = lp.polygon.ToArray();
                    Vector2 mn = pad.poly[0], mx = pad.poly[0];
                    foreach (var v in pad.poly) { mn = Vector2.Min(mn, v); mx = Vector2.Max(mx, v); }
                    b = Rect.MinMaxRect(mn.x, mn.y, mx.x, mx.y);
                }
                else
                {
                    pad.c = lp.center; pad.radii = new Vector2(Mathf.Max(1, lp.radii.x), Mathf.Max(1, lp.radii.y));
                    b = new Rect(lp.center - pad.radii, pad.radii * 2f);
                }
                float grow = pad.blend + 2f;
                pad.bounds = Rect.MinMaxRect(b.xMin - grow, b.yMin - grow, b.xMax + grow, b.yMax + grow);
                padList.Add(pad);
            }
            pads = padList.ToArray();
            carves = BuildCarves(waterCarves);

            Vector2 worldMin = settings.WorldMin;
            float worldSize = settings.WorldSize;

            // --- Major corridors first: they shape valleys and keep the lake away -------------
            if (pathSplines != null)
            {
                foreach (var p in pathSplines)
                {
                    if (p == null || !p.isActiveAndEnabled || p.kind != PathKind.Major || p.points.Count < 2) continue;
                    majors.splines.Add(FieldSpline.FromPoints(p.Densify(6f), p.width, p.name, (int)p.kind));
                }
            }
            foreach (var m in majors.splines) m.influence = Mathf.Max(S.corridorValleyWidth + 460f, m.maxWidth * 0.5f + 30f);
            majors.Build(worldMin, worldSize, 96f);

            // --- Rivers: water level follows the base terrain downhill ------------------------
            if (riverSplines != null)
            {
                for (int ri = 0; ri < riverSplines.Count; ri++)
                {
                    var r = riverSplines[ri];
                    if (r == null || !r.isActiveAndEnabled || r.points.Count < 2) continue;
                    var fs = FieldSpline.FromPoints(r.Densify(6f), 1f, r.name, 0);
                    PrepareRiver(fs, r, rivers.splines);
                    rivers.splines.Add(fs);
                }
            }
            rivers.Build(worldMin, worldSize, 96f);

            // --- Paths: smoothed target heights --------------------------------------------------
            foreach (var m in majors.splines) PreparePath(m, 90f);
            if (pathSplines != null)
            {
                foreach (var p in pathSplines)
                {
                    if (p == null || !p.isActiveAndEnabled || p.kind == PathKind.Major || p.points.Count < 2) continue;
                    var fs = FieldSpline.FromPoints(p.Densify(6f), p.width, p.name, (int)p.kind);
                    fs.closed = p.closed;
                    PreparePath(fs, 45f);
                    minors.splines.Add(fs);
                }
            }
            foreach (var m in minors.splines) m.influence = m.maxWidth * 0.5f + 14f;
            minors.Build(worldMin, worldSize, 96f);
        }

        // =====================================================================================
        // Public sampling
        // =====================================================================================

        /// <summary>Final terrain height (world Y) and all masks at a world XZ position.</summary>
        public WorldSample Sample(float x, float z)
        {
            var s = new WorldSample();
            float h = BaseHeight(x, z, ref s);
            var q = new Vector2(x, z);
            var buf = hitBuffer ?? (hitBuffer = new Hit[32]);

            // Paths: minor first so the major corridors win where they meet.
            ApplyPaths(minors, q, ref h, ref s, buf, 0.8f);
            ApplyPaths(majors, q, ref h, ref s, buf, 1.2f);

            // Rivers carve after paths so a crossing leaves a channel for a future bridge.
            int n = rivers.Query(q, buf);
            for (int i = 0; i < n; i++) CarveRiver(rivers.splines[buf[i].spline], buf[i], ref h, ref s);

            // City landmark pads.
            if (s.city > 0f || s.cityBlend < 1f)
            {
                foreach (var pad in pads)
                {
                    if (!pad.bounds.Contains(q)) continue;
                    float sd = PadDistance(pad, q);
                    if (sd <= -pad.inset) h = pad.inner;
                    else if (sd <= 0f) h = Mathf.Lerp(pad.inner, pad.edge, SmoothStep(-pad.inset, 0f, sd));
                    else h = Mathf.Lerp(pad.edge, h, SmoothStep(0f, pad.blend, sd));
                }
            }

            // Landmark water (moats, canals): terrain drops below the landmark's own water surface.
            foreach (var cv in carves)
            {
                if (!cv.bounds.Contains(q)) continue;
                if (CarveHit(cv, q)) h = Mathf.Min(h, cv.level - cv.depth);
            }

            // Water masks from the final surface.
            float lakeWater = hasLake && s.lakeDistance < 0f && h < lakeLevel - 0.05f ? SmoothStep(0f, 3f, lakeLevel - h) : 0f;
            float seaWater = h < S.seaLevel ? 1f : 0f;
            s.water = Mathf.Max(lakeWater, seaWater);

            // Forest density needs the final height (treeline).
            float cluster = nForest.Fbm(x / 300f, z / 300f, 3) * 0.5f + 0.5f;
            float clear = nForest.Fbm(x / 260f + 40f, z / 260f - 40f, 3) * 0.5f + 0.5f;
            float fd = s.forestRegion * SmoothStep(0.34f - 0.28f * s.forestRegion, 0.56f, cluster + s.forestRegion * 0.22f);
            fd *= 1f - Mathf.Clamp01(S.clearingAmount * 2.6f * SmoothStep(0.6f, 0.7f, clear));
            float groves = s.grass * SmoothStep(0.64f, 0.8f, cluster) * 0.6f;
            float mountainTrees = s.mountain * 0.5f * (1f - SmoothStep(160f, 320f, h)) * SmoothStep(0.35f, 0.6f, cluster);
            float forest = Mathf.Max(fd, Mathf.Max(groves, mountainTrees));
            forest *= Mathf.Lerp(S.nearCityVegetation, 1f, SmoothStep(0.25f, 0.9f, s.cityBlend));
            forest *= (1f - s.city) * (1f - s.water) * (1f - s.river) * (1f - s.path);
            s.forest = Mathf.Clamp01(forest * S.forestDensity);

            s.height = h;
            return s;
        }

        /// <summary>Height before paths and rivers (used to route them). Masks are filled in.</summary>
        public float BaseHeight(float x, float z)
        {
            var s = new WorldSample();
            return BaseHeight(x, z, ref s);
        }

        public float DistanceToRivers(Vector2 q, out float riverHalfWidth)
        {
            var buf = hitBuffer ?? (hitBuffer = new Hit[32]);
            int n = rivers.Query(q, buf);
            float best = float.MaxValue; riverHalfWidth = 0f;
            for (int i = 0; i < n; i++)
            {
                if (buf[i].dist < best)
                {
                    best = buf[i].dist;
                    riverHalfWidth = rivers.splines[buf[i].spline].Width(buf[i]) * 0.5f;
                }
            }
            return best;
        }

        public float DistanceToMajors(Vector2 q)
        {
            var buf = hitBuffer ?? (hitBuffer = new Hit[32]);
            int n = majors.Query(q, buf);
            float best = float.MaxValue;
            for (int i = 0; i < n; i++) best = Mathf.Min(best, buf[i].dist);
            return best;
        }

        /// <summary>Coastline radius (distance from the city centre) toward a compass bearing.</summary>
        public float CoastRadius(float bearing, float x, float z)
        {
            return S.worldRadius * (1f + S.coastIrregularity * nEdge.Periodic(bearing, 2.3f, 3, 0f)) + 20f * nEdge.Fbm(x / 85f, z / 85f, 3);
        }

        public float LakeSignedDistance(Vector2 q)
        {
            if (!hasLake) return float.MaxValue;
            Vector2 d = q - lakeC;
            float dist = d.magnitude;
            float ang = Mathf.Atan2(d.y, d.x);
            float rs = ShoreRadius(ang) + lakeShoreDetail * nShore.Fbm(q.x / 110f, q.y / 110f, 3);
            float sd = dist - rs;
            // Keep the major corridors dry: the shore bends away from them.
            float dm = DistanceToMajors(q);
            float clearance = S.majorPathWidth * 0.5f + 70f;
            return Mathf.Max(sd, clearance - dm);
        }

        // =====================================================================================
        // Base terrain
        // =====================================================================================

        float BaseHeight(float x, float z, ref WorldSample s)
        {
            var p = new Vector2(x, z);
            Vector2 d = p - center;
            float r = d.magnitude;
            float bearing = S.Bearing(d);

            // Domain warp: region borders and radial distances wander so nothing reads as a pie chart.
            float wx = nWarp.Fbm(x / 1400f, z / 1400f, 3);
            float wz = nWarp.Fbm(x / 1400f + 57.3f, z / 1400f - 31.9f, 3);
            float bw = bearing + wx * 24f;
            float rw = r * (1f + wz * 0.09f);

            float mountain = Mathf.Max(Sector(S.mountainRegion, bw, rw, 600f), Sector(S.foothillRegion, bw, rw, 700f));
            float farm = Mathf.Max(Sector(S.farmRegion, bw, rw, 200f), Sector(S.westFarms, bw, rw, 200f));
            float forest = Mathf.Max(Sector(S.forestRegion, bw, rw, 300f), Mathf.Max(Sector(S.southEastForest, bw, rw, 300f), Sector(S.westForest, bw, rw, 300f)));
            float rocky = Mathf.Max(Sector(S.rockyRegion, bw, rw, 300f), Sector(S.ruggedSouth, bw, rw, 500f));

            // Biomes give way to each other: mountains > rocky > forest > farms.
            forest *= 1f - mountain * 0.75f;
            farm *= (1f - mountain) * (1f - forest * 0.6f);
            rocky *= 1f - mountain * 0.5f;

            float grow = SmoothStep(cityR, cityR + 900f, r);
            float rEdge = CoastRadius(bearing, x, z);

            // --- Lowland --------------------------------------------------------------------------
            float cont = nLand.Fbm(x / 2600f, z / 2600f, 3) * 10f;
            float plateau = S.outerPlateauHeight * SmoothStep(S.worldRadius * 0.5f, S.worldRadius * 0.97f, r);
            float hillN = nHill.ErodedFbm(x / 560f, z / 560f, 5, 0.5f, 1.2f) * 0.5f + 0.5f;
            float hillAmp = S.rollingHillHeight * Mathf.Lerp(1f, 0.3f, farm) + 26f * forest + S.rockyHillHeight * rocky;
            float hills = hillN * hillN * hillAmp * 1.6f;
            float detail = nDetail.Fbm(x / 95f, z / 95f, 3) * (0.8f + 3.5f * rocky + 1.5f * forest);

            // Rocky knolls and scattered outcrops.
            float knollN = nDetail.Ridged(x / 260f, z / 260f, 3, 0.5f, 2.5f);
            float spots = SmoothStep(0.74f, 0.9f, nMisc.Noise(x / 420f, z / 420f) * 0.5f + 0.5f) * (1f - farm);
            float knolls = (rocky * 38f + spots * 26f) * knollN * knollN * knollN;
            rocky = Mathf.Max(rocky, spots * 0.8f);

            float land = S.landHeight + (cont + plateau + hills + detail + knolls) * grow;

            // --- Mountains ----------------------------------------------------------------------
            float valley = 1f;
            if (majors.splines.Count > 0)
            {
                float dm = DistanceToMajors(p);
                valley = Mathf.Lerp(0.04f, 1f, SmoothStep(S.corridorValleyWidth, S.corridorValleyWidth + 280f, dm));
            }
            if (mountain > 0.001f)
            {
                float growM = 0.3f + 0.7f * SmoothStep(S.mountainRegion.innerDistance, S.worldRadius * 0.93f, rw);
                float spine = SpineField(bearing, r);
                float env = mountain * growM * (0.35f + 0.85f * spine);
                float ridged = nMtn.Ridged(x / 1150f + wx * 0.35f, z / 1150f + wz * 0.35f, 6, 0.5f, 2.2f);
                float eroded = nMtn.ErodedFbm(x / 700f + 100f, z / 700f - 100f, 6, 0.5f, 1.4f) * 0.5f + 0.5f;
                float shape = Mathf.Pow(Mathf.Clamp01(ridged * 0.66f + eroded * 0.34f), 1.55f);
                // Broad uplift (a continuous massif) + ridged peaks on top.
                float mtn = S.mountainHeight * env * (0.2f + shape * 1.1f);
                // Summit: a sharp cone broken up by ridges, so it reads as a peak, not a dome.
                float ds = Vector2.Distance(p, summitPos) / 650f;
                float cone = Mathf.Exp(-ds * 2.2f) * (1f - SmoothStep(0.9f, 1.6f, ds));
                mtn += S.summitExtraHeight * 1.6f * cone * mountain * (0.35f + 0.65f * ridged * ridged);
                land += mtn * valley;
            }

            // --- Coast ----------------------------------------------------------------------------
            float coastT = SmoothStep(rEdge - S.coastCliffWidth, rEdge + 6f, r);
            float seaFloor = S.seaLevel - 22f - 30f * SmoothStep(rEdge, rEdge + 600f, r);
            float h = Mathf.Lerp(land, seaFloor, coastT);

            // --- Lake -------------------------------------------------------------------------------
            float sdL = float.MaxValue;
            if (hasLake)
            {
                sdL = LakeSignedDistance(p);
                if (sdL > 0f)
                {
                    float basin = 1f - SmoothStep(0f, 450f, sdL);
                    float lowered = Mathf.Min(h, lakeLevel + 2.2f + sdL * 0.04f);
                    h = Mathf.Lerp(h, lowered, basin);
                    float beachT = SmoothStep(0f, lakeBeach + 26f, sdL);
                    h = Mathf.Lerp(Mathf.Min(h, lakeLevel + 0.25f + sdL * 0.03f), h, beachT);
                }
                else
                {
                    float dIn = -sdL;
                    float bottom = lakeLevel - Mathf.Lerp(0.35f, 2.2f, SmoothStep(0f, lakeShelf, dIn))
                                   - lakeDepth * SmoothStep(lakeShelf * 0.6f, lakeShelf + 300f, dIn);
                    bottom += nDetail.Fbm(x / 140f, z / 140f, 2) * 1.5f * SmoothStep(lakeShelf, lakeShelf + 100f, dIn);
                    h = bottom;
                }
                foreach (var isl in islands)
                {
                    Vector2 ic = lakeC + isl.offset;
                    float di = Vector2.Distance(p, ic) / Mathf.Max(5f, isl.radius);
                    if (di > 4f) continue;
                    float ih;
                    if (di < 1f)
                    {
                        float dome = Mathf.Pow(1f - di * di, 0.8f);
                        float rock = nDetail.Ridged(x / 45f, z / 45f, 3) * isl.rockiness * 9f * dome;
                        ih = lakeLevel + isl.height * dome + rock;
                    }
                    else ih = lakeLevel - (di - 1f) * isl.radius * 0.12f - 0.2f;
                    h = Mathf.Max(h, ih);
                }
                s.lakeDistance = sdL;
            }
            else s.lakeDistance = float.MaxValue;

            // --- City -------------------------------------------------------------------------------
            float rc = r + nMisc.Noise(x / 300f, z / 300f) * 16f;
            float cityT = SmoothStep(cityR, cityR + S.cityTransitionWidth, rc);
            cityT = cityT * cityT * (3f - 2f * cityT);
            h = Mathf.Lerp(S.cityFloorHeight, h, cityT);
            float city = r <= cityR ? 1f : 0f;

            float natural = (1f - city);
            s.mountain = mountain * natural * (1f - coastT);
            s.farm = farm * natural * SmoothStep(0.1f, 0.6f, cityT) * (1f - coastT);
            s.forestRegion = forest * natural * (1f - coastT);
            s.rocky = rocky * natural * (1f - coastT);
            s.grass = Mathf.Clamp01(1f - Mathf.Max(Mathf.Max(s.mountain, s.forestRegion), Mathf.Max(s.farm, s.rocky))) * natural;
            s.city = city;
            s.cityBlend = cityT;
            s.edgeDistance = rEdge - r;
            return h;
        }

        float Sector(RegionSector reg, float bearing, float r, float radialSoftness)
        {
            if (reg.strength <= 0f) return 0f;
            float da = Mathf.Abs(Mathf.DeltaAngle(bearing, reg.bearing));
            float half = reg.arc * 0.5f;
            float ang = 1f - SmoothStep(half - reg.softness, half + reg.softness, da);
            if (ang <= 0f) return 0f;
            float rad = SmoothStep(reg.innerDistance - radialSoftness * 0.4f, reg.innerDistance + radialSoftness * 0.6f, r);
            if (reg.outerDistance > 0f) rad *= 1f - SmoothStep(reg.outerDistance - radialSoftness * 0.6f, reg.outerDistance + radialSoftness * 0.4f, r);
            return ang * rad * reg.strength;
        }

        float SpineField(float bearing, float r)
        {
            float best = 0f;
            foreach (var sp in spines)
            {
                float da = Mathf.Abs(Mathf.DeltaAngle(bearing, sp.bearing));
                float ang = 1f - SmoothStep(sp.halfSpan, sp.halfSpan + 14f, da);
                if (ang <= 0f) continue;
                float rr = sp.radius + 170f * nMisc.Noise(bearing * 0.045f + sp.wobbleSeed, sp.wobbleSeed);
                float t = (r - rr) / sp.width;
                best = Mathf.Max(best, Mathf.Exp(-t * t) * ang);
            }
            return best;
        }

        float ShoreRadius(float angle)
        {
            int n = lakeShape.Length;
            float t = Mathf.Repeat(angle / (Mathf.PI * 2f), 1f) * n;
            int i1 = Mathf.FloorToInt(t) % n;
            float f = t - Mathf.Floor(t);
            float p0 = lakeShape[(i1 - 1 + n) % n], p1 = lakeShape[i1], p2 = lakeShape[(i1 + 1) % n], p3 = lakeShape[(i1 + 2) % n];
            float f2 = f * f, f3 = f2 * f;
            float m = 0.5f * (2f * p1 + (-p0 + p2) * f + (2f * p0 - 5f * p1 + 4f * p2 - p3) * f2 + (-p0 + 3f * p1 - 3f * p2 + p3) * f3);
            return lakeR * Mathf.Max(0.15f, m);
        }

        // =====================================================================================
        // Paths and rivers
        // =====================================================================================

        void PreparePath(FieldSpline fs, float smoothWindow)
        {
            int n = fs.p.Length;
            var raw = new float[n];
            for (int i = 0; i < n; i++) raw[i] = BaseHeight(fs.p[i].x, fs.p[i].y);
            int half = Mathf.Max(1, Mathf.RoundToInt(smoothWindow / 6f / 2f));
            var sm = raw;
            for (int pass = 0; pass < 3; pass++) sm = BoxFilter(sm, half);
            for (int i = 0; i < n; i++) fs.level[i] = sm[i];
            fs.shoulder = fs.kind == (int)PathKind.Major ? 24f : 11f;
        }

        void PrepareRiver(FieldSpline fs, AureliusRiver r, List<FieldSpline> earlier)
        {
            int n = fs.p.Length;
            float outletLevel = r.outlet == RiverOutlet.Sea ? S.seaLevel - 0.5f : lakeLevel;
            if (r.outlet == RiverOutlet.River && r.joinsRiver >= 0 && r.joinsRiver < earlier.Count)
            {
                var parent = earlier[r.joinsRiver];
                outletLevel = parent.LevelNearest(fs.p[n - 1]);
            }
            else if (r.outlet == RiverOutlet.Lake && !hasLake) outletLevel = S.seaLevel - 0.5f;

            float wl = float.MaxValue;
            float len = fs.s[n - 1];
            for (int i = 0; i < n; i++)
            {
                float bh = BaseHeight(fs.p[i].x, fs.p[i].y);
                wl = Mathf.Min(wl, bh - (i == 0 ? 0.8f : 1.3f));
                fs.level[i] = Mathf.Max(wl, outletLevel);
                float t = len > 0 ? fs.s[i] / len : 0;
                float width = Mathf.Lerp(r.widthStart, r.widthEnd, Mathf.Pow(t, 0.6f));
                if (r.outlet == RiverOutlet.Lake) width *= 1f + 1.1f * SmoothStep(len - 160f, len, fs.s[i]);
                fs.width[i] = width;
                fs.depth[i] = r.depth * Mathf.Lerp(0.55f, 1.2f, t);
            }
            fs.maxWidth = 0f;
            foreach (var w in fs.width) fs.maxWidth = Mathf.Max(fs.maxWidth, w);
            fs.influence = fs.maxWidth * 0.5f + 4f + fs.maxWidth * 0.4f + 290f;
        }

        static float[] BoxFilter(float[] src, int half)
        {
            int n = src.Length;
            var dst = new float[n];
            for (int i = 0; i < n; i++)
            {
                float sum = 0; int c = 0;
                for (int k = -half; k <= half; k++)
                {
                    int j = Mathf.Clamp(i + k, 0, n - 1);
                    sum += src[j]; c++;
                }
                dst[i] = sum / c;
            }
            return dst;
        }

        void ApplyPaths(SplineSet set, Vector2 q, ref float h, ref WorldSample s, Hit[] buf, float falloff)
        {
            int n = set.Query(q, buf);
            for (int i = 0; i < n; i++)
            {
                var fs = set.splines[buf[i].spline];
                float w = fs.Width(buf[i]) * 0.5f;
                float d = buf[i].dist;
                float target = fs.Level(buf[i]);
                float k = 1f - SmoothStep(w, w + fs.shoulder * falloff, d);
                if (k <= 0f) continue;
                float core = 1f - SmoothStep(w * 0.6f, w, d);
                h = Mathf.Lerp(h, target - S.pathDepression * core, k);
                float m = 1f - SmoothStep(w * 0.75f, w + 1.5f, d);
                s.path = Mathf.Max(s.path, m);
                if (fs.kind == (int)PathKind.Major) s.majorPath = Mathf.Max(s.majorPath, m);
            }
        }

        void CarveRiver(FieldSpline fs, Hit hit, ref float h, ref WorldSample s)
        {
            float w = fs.Width(hit) * 0.5f;
            float wl = fs.Level(hit);
            float depth = fs.Depth(hit);
            float d = hit.dist;
            float bankW = 4f + w * 0.8f;
            float valleyW = Mathf.Lerp(12f, 240f, s.mountain) + 20f * s.rocky;

            float c;
            if (d < w)
            {
                float t = d / w;
                c = wl - depth * Mathf.Pow(1f - t * t, 0.6f);
            }
            else c = wl + 0.35f + (d - w) * 0.22f;

            float k = 1f - SmoothStep(w + bankW, w + bankW + valleyW + 30f, d);
            if (k > 0f) h = Mathf.Lerp(h, Mathf.Min(h, c), k);
            // Low levee so the water surface never sits above its banks on side slopes.
            if (d >= w && d < w + 3f) h = Mathf.Max(h, wl + 0.2f);

            s.river = Mathf.Max(s.river, 1f - SmoothStep(w - 1f, w + 0.5f, d));
            s.riverBank = Mathf.Max(s.riverBank, 1f - SmoothStep(w, w + bankW, d));
        }

        // =====================================================================================
        // Landmark pads & water carves
        // =====================================================================================

        static Carve[] BuildCarves(IList<WaterCarve> src)
        {
            if (src == null) return Array.Empty<Carve>();
            var list = new List<Carve>();
            foreach (var wc in src)
            {
                if (wc?.triangles == null || wc.triangles.Length < 3) continue;
                Vector2 mn = wc.triangles[0], mx = wc.triangles[0];
                foreach (var v in wc.triangles) { mn = Vector2.Min(mn, v); mx = Vector2.Max(mx, v); }
                mn -= Vector2.one * 3f; mx += Vector2.one * 3f;
                var cv = new Carve { bounds = Rect.MinMaxRect(mn.x, mn.y, mx.x, mx.y), level = wc.level, depth = wc.depth };
                cv.w = Mathf.CeilToInt(mx.x - mn.x) + 1; cv.h = Mathf.CeilToInt(mx.y - mn.y) + 1;
                cv.mask = new bool[cv.w * cv.h];
                for (int t = 0; t + 2 < wc.triangles.Length; t += 3)
                {
                    Vector2 a = wc.triangles[t], b = wc.triangles[t + 1], c = wc.triangles[t + 2];
                    int x0 = Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x)) - mn.x), x1 = Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x)) - mn.x);
                    int y0 = Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y)) - mn.y), y1 = Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y)) - mn.y);
                    for (int y = Mathf.Max(0, y0); y <= Mathf.Min(cv.h - 1, y1); y++)
                        for (int x = Mathf.Max(0, x0); x <= Mathf.Min(cv.w - 1, x1); x++)
                            if (InTriangle(new Vector2(mn.x + x, mn.y + y), a, b, c)) cv.mask[y * cv.w + x] = true;
                }
                list.Add(cv);
            }
            return list.ToArray();
        }

        static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
            float d2 = (p.x - c.x) * (b.y - c.y) - (b.x - c.x) * (p.y - c.y);
            float d3 = (p.x - a.x) * (c.y - a.y) - (c.x - a.x) * (p.y - a.y);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }

        /// <summary>True if any rasterised water cell lies within ~2 m (covers the 2 m heightmap spacing).</summary>
        static bool CarveHit(in Carve cv, Vector2 q)
        {
            int cx = Mathf.RoundToInt(q.x - cv.bounds.xMin), cy = Mathf.RoundToInt(q.y - cv.bounds.yMin);
            for (int y = -2; y <= 2; y++)
                for (int x = -2; x <= 2; x++)
                {
                    int px = cx + x, py = cy + y;
                    if (px < 0 || py < 0 || px >= cv.w || py >= cv.h) continue;
                    if (cv.mask[py * cv.w + px]) return true;
                }
            return false;
        }

        static float PadDistance(in Pad pad, Vector2 q)
        {
            if (pad.shape == LandmarkPadShape.Ellipse)
            {
                Vector2 k = (q - pad.c);
                Vector2 n = new Vector2(k.x / pad.radii.x, k.y / pad.radii.y);
                return (n.magnitude - 1f) * Mathf.Min(pad.radii.x, pad.radii.y);
            }
            return PolygonSignedDistance(pad.poly, q);
        }

        /// <summary>Signed distance to a simple polygon (negative inside).</summary>
        public static float PolygonSignedDistance(Vector2[] v, Vector2 p)
        {
            int n = v.Length;
            float d = Vector2.Dot(p - v[0], p - v[0]);
            float sign = 1f;
            for (int i = 0, j = n - 1; i < n; j = i, i++)
            {
                Vector2 e = v[j] - v[i];
                Vector2 w = p - v[i];
                Vector2 b = w - e * Mathf.Clamp01(Vector2.Dot(w, e) / Vector2.Dot(e, e));
                d = Mathf.Min(d, Vector2.Dot(b, b));
                bool c1 = p.y >= v[i].y, c2 = p.y < v[j].y, c3 = e.x * w.y > e.y * w.x;
                if ((c1 && c2 && c3) || (!c1 && !c2 && !c3)) sign = -sign;
            }
            return sign * Mathf.Sqrt(d);
        }

        public static float SmoothStep(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }
    }

    // =========================================================================================
    // Spline storage and spatial index
    // =========================================================================================

    public struct Hit
    {
        public int spline, segment;
        public float t, dist;
    }

    public sealed class FieldSpline
    {
        public string name;
        public int kind;
        public bool closed;
        public Vector2[] p;
        public float[] s, width, level, depth;
        public float maxWidth, influence, shoulder;

        public static FieldSpline FromPoints(List<Vector2> pts, float width, string name, int kind)
        {
            int n = pts.Count;
            var fs = new FieldSpline
            {
                name = name, kind = kind,
                p = pts.ToArray(), s = new float[n], width = new float[n], level = new float[n], depth = new float[n],
                maxWidth = width
            };
            for (int i = 0; i < n; i++)
            {
                fs.width[i] = width;
                if (i > 0) fs.s[i] = fs.s[i - 1] + Vector2.Distance(pts[i - 1], pts[i]);
            }
            return fs;
        }

        public float Length => s.Length > 0 ? s[s.Length - 1] : 0f;
        public float Width(in Hit h) => Mathf.Lerp(width[h.segment], width[h.segment + 1], h.t);
        public float Level(in Hit h) => Mathf.Lerp(level[h.segment], level[h.segment + 1], h.t);
        public float Depth(in Hit h) => Mathf.Lerp(depth[h.segment], depth[h.segment + 1], h.t);
        public float Distance(in Hit h) => Mathf.Lerp(s[h.segment], s[h.segment + 1], h.t);

        public float LevelNearest(Vector2 q)
        {
            float best = float.MaxValue; float lvl = level.Length > 0 ? level[level.Length - 1] : 0f;
            for (int i = 0; i < p.Length; i++)
            {
                float d = (p[i] - q).sqrMagnitude;
                if (d < best) { best = d; lvl = level[i]; }
            }
            return lvl;
        }

        public Vector2 PointAt(in Hit h) => Vector2.Lerp(p[h.segment], p[h.segment + 1], h.t);
    }

    public sealed class SplineSet
    {
        public readonly List<FieldSpline> splines = new List<FieldSpline>();
        List<int>[] grid;
        Vector2 origin;
        float cell;
        int dim;

        public void Build(Vector2 worldMin, float worldSize, float cellSize)
        {
            cell = cellSize;
            origin = worldMin;
            dim = Mathf.CeilToInt(worldSize / cellSize) + 1;
            grid = new List<int>[dim * dim];
            for (int si = 0; si < splines.Count; si++)
            {
                var sp = splines[si];
                for (int i = 0; i < sp.p.Length - 1; i++)
                {
                    Vector2 a = sp.p[i], b = sp.p[i + 1];
                    float r = sp.influence;
                    int x0 = Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(a.x, b.x) - r - origin.x) / cell), 0, dim - 1);
                    int x1 = Mathf.Clamp(Mathf.FloorToInt((Mathf.Max(a.x, b.x) + r - origin.x) / cell), 0, dim - 1);
                    int y0 = Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(a.y, b.y) - r - origin.y) / cell), 0, dim - 1);
                    int y1 = Mathf.Clamp(Mathf.FloorToInt((Mathf.Max(a.y, b.y) + r - origin.y) / cell), 0, dim - 1);
                    int code = (si << 20) | i;
                    for (int gy = y0; gy <= y1; gy++)
                        for (int gx = x0; gx <= x1; gx++)
                        {
                            ref var list = ref grid[gy * dim + gx];
                            if (list == null) list = new List<int>(8);
                            list.Add(code);
                        }
                }
            }
        }

        /// <summary>Nearest point per spline within that spline's influence radius.</summary>
        public int Query(Vector2 q, Hit[] buf)
        {
            if (grid == null || splines.Count == 0) return 0;
            int gx = Mathf.FloorToInt((q.x - origin.x) / cell), gy = Mathf.FloorToInt((q.y - origin.y) / cell);
            if (gx < 0 || gy < 0 || gx >= dim || gy >= dim) return 0;
            var list = grid[gy * dim + gx];
            if (list == null) return 0;
            int count = 0;
            for (int k = 0; k < list.Count; k++)
            {
                int code = list[k];
                int si = code >> 20, i = code & 0xFFFFF;
                var sp = splines[si];
                Vector2 a = sp.p[i], b = sp.p[i + 1];
                Vector2 ab = b - a;
                float len2 = Vector2.Dot(ab, ab);
                float t = len2 > 1e-6f ? Mathf.Clamp01(Vector2.Dot(q - a, ab) / len2) : 0f;
                float dist = Vector2.Distance(q, a + ab * t);
                if (dist > sp.influence) continue;
                int slot = -1;
                for (int j = 0; j < count; j++) if (buf[j].spline == si) { slot = j; break; }
                if (slot < 0)
                {
                    if (count >= buf.Length) continue;
                    slot = count++;
                    buf[slot] = new Hit { spline = si, segment = i, t = t, dist = dist };
                }
                else if (dist < buf[slot].dist) buf[slot] = new Hit { spline = si, segment = i, t = t, dist = dist };
            }
            return count;
        }
    }
}
