// AureliusCityPavement.hlsl
// Procedural royal-city paving for the reserved Aurelius disc. The mesh is a plain draped disc; every
// pixel decides which paving system it belongs to and lays stones in THAT system's frame:
//   plazas (building-aligned rects / rings around round buildings)
//   major royal roads    - long slabs running along the road, curb borders, centre accent line
//   rings                - coursed stones that follow the curvature (one stone count per course)
//   secondary / minor roads
//   central royal plaza  - large fan-coursed stones, decorative rings, radial spokes
//   boundary curb        - curved curb stones on the city edge
//   general ground       - small concentric courses
// Lighting is the shared anime chain (AnimeLighting.hlsl). No textures.

#ifndef AURELIUS_CITY_PAVEMENT_INCLUDED
#define AURELIUS_CITY_PAVEMENT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Assets/Shaders/Anime/Include/AnimeLighting.hlsl"

CBUFFER_START(UnityPerMaterial)
    float4 _StoneBase, _StoneSecondary, _Accent, _BorderColor, _MortarColor, _RoyalTint;
    float4 _TileMajor, _TileSecondary, _TileGeneral, _TileCentral, _TilePlaza; // xy = length, width
    float _Variation, _AccentChance, _MortarWidth, _BevelWidth, _BorderWidth, _TileRotation;
    float _ToonThreshold, _ToonSoftness, _MidWidth, _ShadowTintStrength, _ShadowStrength;
    float _HighlightStrength, _FormRetention, _AmbientStrength, _AmbientSceneInfluence;
    float _RimStrength, _RimPower, _MaxLightIntensity, _Saturation, _Brightness;
    float4 _MidColor, _ShadowColor, _AmbientColor, _RimColor;
CBUFFER_END

// Layout globals (AureliusCityPavement.Apply)
float4 _PaveCenter;       // xy centre, z outer radius, w curb width
float4 _PaveCentral;      // x inner clear radius, y central plaza outer radius, z decorative ring spacing, w spoke count
float4 _PaveCounts;       // roads, rings, plazas, exclusions
float4 _PaveRoads[24];    // dir.xy, start radius, end radius
float4 _PaveRoadInfo[24]; // half width, level
float4 _PaveRings[8];     // radius, half width, level
float4 _PavePlazas[8];    // centre xy, a, b  (rect: half sizes / ring: inner, outer radius)
float4 _PavePlazaInfo[8]; // type (0 rect, 1 ring), angle (rad), level
float4 _PaveExcl[24];     // centre xy, half sizes
float4 _PaveExclInfo[24]; // cos, sin
TEXTURE2D(_PaveLandmarkMask); SAMPLER(sampler_PaveLandmarkMask);
float4 _PaveMaskRect;     // xy min corner, zw 1/size (0 = no mask)

#define TAU 6.2831853

struct Pave
{
    float2 id;      // stone id
    float edge;     // distance to the nearest joint (m)
    float border;   // 1 = border / curb stone
    float accent;   // 1 = decorative accent stone
    float level;    // 1 royal, 2 secondary, 3 general
    float sys;      // system id (decorrelates colours)
};

uint PaveHashU(int2 p, int s)
{
    uint h = (uint)p.x * 73856093u ^ (uint)p.y * 19349663u ^ (uint)s * 83492791u;
    h ^= h >> 13; h *= 0x5bd1e995u; h ^= h >> 15;
    return h;
}
float PaveHash(float2 id, float s) { return (PaveHashU((int2)id, (int)s) & 0xFFFF) / 65535.0; }

// Running-bond stones in a local frame: uv.x runs along the long side.
Pave LinearStones(float2 uv, float L, float W)
{
    Pave p = (Pave)0;
    float row = floor(uv.y / W);
    float odd = frac(row * 0.5) * 2.0;
    float u = uv.x / L + odd * 0.5;
    float fx = frac(u), fy = frac(uv.y / W);
    p.id = float2(floor(u), row);
    p.edge = min(min(fx, 1.0 - fx) * L, min(fy, 1.0 - fy) * W);
    return p;
}

// Concentric courses: each course has its own whole number of stones, so nothing stretches.
Pave PolarStones(float r, float theta, float L, float W, float rOffset)
{
    Pave p = (Pave)0;
    float rr = r - rOffset;
    float row = floor(rr / W);
    float rc = rOffset + (row + 0.5) * W;
    float circ = TAU * max(rc, 0.5);
    float n = max(6.0, round(circ / L));
    float u = theta / TAU * n + frac(row * 0.5);
    float cell = floor(u);
    cell -= n * floor(cell / n);
    float fx = frac(u), fy = frac(rr / W);
    p.id = float2(cell, row);
    p.edge = min(min(fx, 1.0 - fx) * (circ / n), min(fy, 1.0 - fy) * W);
    return p;
}

float2 Rotate(float2 v, float a) { float c = cos(a), s = sin(a); return float2(v.x * c + v.y * s, -v.x * s + v.y * c); }

bool Excluded(float2 p)
{
    // Landmarks' own ground surfaces (castle road slabs, University lawns & paths): baked mask.
    if (_PaveMaskRect.z > 0)
    {
        float2 muv = (p - _PaveMaskRect.xy) * _PaveMaskRect.zw;
        if (all(muv > 0) && all(muv < 1) && SAMPLE_TEXTURE2D_LOD(_PaveLandmarkMask, sampler_PaveLandmarkMask, muv, 0).r > 0.5) return true;
    }
    int n = (int)_PaveCounts.w;
    [loop] for (int i = 0; i < n; i++)
    {
        float2 d = p - _PaveExcl[i].xy;
        float c = _PaveExclInfo[i].x, s = _PaveExclInfo[i].y;
        float2 l = float2(d.x * c + d.y * s, -d.x * s + d.y * c);
        if (abs(l.x) < _PaveExcl[i].z && abs(l.y) < _PaveExcl[i].w) return true;
    }
    return false;
}

float4 TileFor(float level) { return level < 1.5 ? _TileMajor : _TileSecondary; }

bool RoadPave(float2 d, float levelWanted, inout Pave pv)
{
    int n = (int)_PaveCounts.x;
    [loop] for (int i = 0; i < n; i++)
    {
        float level = _PaveRoadInfo[i].y;
        if (abs(level - levelWanted) > 0.5) continue;
        float2 dir = _PaveRoads[i].xy;
        float along = dot(d, dir);
        float across = dot(d, float2(-dir.y, dir.x));
        float hw = _PaveRoadInfo[i].x;
        if (along < _PaveRoads[i].z || along > _PaveRoads[i].w || abs(across) > hw) continue;
        float4 t = TileFor(level);
        float bw = level < 1.5 ? _BorderWidth : _BorderWidth * 0.6;
        float side = abs(across);
        if (side > hw - bw)
        {
            // curb: one row of long stones following the road edge
            pv = LinearStones(float2(along, side - (hw - bw)), t.x * 1.5, bw);
            pv.border = 1;
        }
        else
        {
            pv = LinearStones(float2(along, across + t.y * 0.5), t.x, t.y);
            if (level < 1.5 && abs(pv.id.y) < 0.5) pv.accent = 0.55; // centre line of the royal road
        }
        pv.level = level;
        pv.sys = 10 + i;
        return true;
    }
    return false;
}

bool RingPave(float r, float theta, float levelWanted, inout Pave pv)
{
    int n = (int)_PaveCounts.y;
    [loop] for (int i = 0; i < n; i++)
    {
        float level = _PaveRings[i].z;
        if (abs(level - levelWanted) > 0.5) continue;
        float R = _PaveRings[i].x, hw = _PaveRings[i].y;
        if (abs(r - R) > hw) continue;
        float4 t = TileFor(level);
        float bw = level < 1.5 ? _BorderWidth : _BorderWidth * 0.6;
        float inner = R - hw;
        if (r - inner < bw || r - inner > 2.0 * hw - bw)
        {
            float o = r - inner < bw ? inner : R + hw - bw;
            pv = PolarStones(r, theta, t.x * 1.5, bw, o);
            pv.border = 1;
        }
        else pv = PolarStones(r, theta, t.x, t.y, inner + bw);
        pv.level = level;
        pv.sys = 40 + i;
        return true;
    }
    return false;
}

bool PlazaPave(float2 p, inout Pave pv)
{
    int n = (int)_PaveCounts.z;
    [loop] for (int i = 0; i < n; i++)
    {
        float2 c = _PavePlazas[i].xy;
        float type = _PavePlazaInfo[i].x;
        float level = _PavePlazaInfo[i].z;
        float bw = _BorderWidth * 0.8;
        if (type < 0.5)
        {
            float2 l = Rotate(p - c, _PavePlazaInfo[i].y + radians(_TileRotation));
            float2 h = _PavePlazas[i].zw;
            if (abs(l.x) > h.x || abs(l.y) > h.y) continue;
            float ex = h.x - abs(l.x), ey = h.y - abs(l.y);
            if (min(ex, ey) < bw)
            {
                // border stones run along the nearest plaza edge
                if (ex < ey) pv = LinearStones(float2(l.y, ex), _TilePlaza.x * 1.5, bw);
                else pv = LinearStones(float2(l.x, ey), _TilePlaza.x * 1.5, bw);
                pv.border = 1;
            }
            else pv = LinearStones(l, _TilePlaza.x, _TilePlaza.y);
        }
        else
        {
            float2 d = p - c;
            float r = length(d);
            if (r < _PavePlazas[i].z || r > _PavePlazas[i].w) continue;
            float theta = atan2(d.y, d.x);
            if (_PavePlazas[i].w - r < bw) { pv = PolarStones(r, theta, _TilePlaza.x * 1.5, bw, _PavePlazas[i].w - bw); pv.border = 1; }
            else pv = PolarStones(r, theta, _TilePlaza.x, _TilePlaza.y, _PavePlazas[i].z);
        }
        pv.level = level;
        pv.sys = 70 + i;
        return true;
    }
    return false;
}

Pave CentralPave(float r, float theta)
{
    float inner = _PaveCentral.x;
    float spacing = max(_PaveCentral.z, 4.0);
    float spokes = max(_PaveCentral.w, 1.0);
    float rot = radians(_TileRotation);
    // radial spokes (fan divisions): stones run along the spoke
    float seg = TAU / spokes;
    float a = theta + rot + seg * 0.5;
    a -= seg * floor(a / seg);
    float lateral = (a - seg * 0.5) * r;
    Pave pv;
    if (abs(lateral) < _TileCentral.y * 0.6)
    {
        pv = LinearStones(float2(r, lateral + _TileCentral.y * 0.6), _TileCentral.x, _TileCentral.y * 1.2);
        pv.accent = 1;
    }
    else
    {
        pv = PolarStones(r, theta + rot, _TileCentral.x, _TileCentral.y, inner);
        float band = frac((r - inner) / spacing) * spacing;
        if (band < _TileCentral.y) pv.border = 1;          // decorative ring every 'spacing' metres
        float outerBand = _PaveCentral.y - r;
        if (outerBand < _BorderWidth) pv.accent = 0.8;     // ring that closes the royal heart
    }
    pv.level = 1;
    pv.sys = 5;
    return pv;
}

float3 StoneColor(Pave pv, float px)
{
    float h1 = PaveHash(pv.id, pv.sys * 13 + 1);
    float h2 = PaveHash(pv.id, pv.sys * 13 + 2);
    float3 c = lerp(_StoneBase.rgb, _StoneSecondary.rgb, saturate(h1 * _Variation * 1.4));
    c *= 1.0 + (h2 - 0.5) * 0.08 * _Variation;
    if (h2 > 1.0 - _AccentChance && pv.border < 0.5) c = lerp(c, _Accent.rgb, 0.28);
    if (pv.level < 1.5) c *= _RoyalTint.rgb;
    else if (pv.level > 2.5) c *= 0.97;
    c = lerp(c, _BorderColor.rgb * lerp(0.95, 1.05, h1), pv.border);
    c = lerp(c, _Accent.rgb * lerp(0.94, 1.06, h1), saturate(pv.accent));

    // Subtle bevel inside each stone, then the joint. Joints fade out with distance (no moire).
    float fade = saturate(1.0 - (px - _MortarWidth * 0.5) / (_MortarWidth * 4.0 + 0.1)); // 1 near, 0 far
    float bevel = smoothstep(_MortarWidth, _MortarWidth + _BevelWidth, pv.edge);
    c *= lerp(1.0, lerp(0.93, 1.0, bevel), fade);
    float aa = max(px * 0.75, 0.005);
    float joint = 1.0 - smoothstep(_MortarWidth * 0.5, _MortarWidth * 0.5 + aa, pv.edge);
    c = lerp(c, _MortarColor.rgb, joint * fade);
    return c;
}

// ---------------------------------------------------------------------------------------------

struct PAttributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
struct PVaryings
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    half3 normalWS : TEXCOORD1;
    half fogFactor : TEXCOORD2;
    UNITY_VERTEX_OUTPUT_STEREO
};

PVaryings PaveVert(PAttributes v)
{
    PVaryings o = (PVaryings)0;
    UNITY_SETUP_INSTANCE_ID(v);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
    VertexPositionInputs vpi = GetVertexPositionInputs(v.positionOS.xyz);
    o.positionCS = vpi.positionCS;
    o.positionWS = vpi.positionWS;
    o.normalWS = TransformObjectToWorldNormal(v.normalOS);
    o.fogFactor = ComputeFogFactor(vpi.positionCS.z);
    return o;
}

Pave ResolvePave(float2 p)
{
    float2 d = p - _PaveCenter.xy;
    float r = length(d);
    float theta = atan2(d.y, d.x);
    // Priority: royal roads cross everything (they are the city's gates and axes); the boundary curb
    // and the royal heart are never overridden by plazas; plazas beat rings and lesser streets.
    Pave pv = (Pave)0;
    if (RoadPave(d, 1, pv)) return pv;
    if (r > _PaveCenter.z - _PaveCenter.w)
    {
        pv = PolarStones(r, theta, 2.4, _PaveCenter.w, _PaveCenter.z - _PaveCenter.w);
        pv.border = 1; pv.level = 2; pv.sys = 3;
        return pv;
    }
    if (r < _PaveCentral.y)
    {
        if (RoadPave(d, 2, pv)) return pv;
        return CentralPave(r, theta);
    }
    if (PlazaPave(p, pv)) return pv;
    if (RingPave(r, theta, 1, pv)) return pv;
    if (RingPave(r, theta, 2, pv)) return pv;
    if (RoadPave(d, 2, pv)) return pv;
    if (RoadPave(d, 3, pv)) return pv;
    pv = PolarStones(r, theta + radians(_TileRotation), _TileGeneral.x, _TileGeneral.y, _PaveCentral.y);
    pv.level = 3; pv.sys = 1;
    return pv;
}

half4 PaveFrag(PVaryings IN) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
    float3 pw = IN.positionWS;
    float2 p = pw.xz;
    float2 d = p - _PaveCenter.xy;
    float r = length(d);
    if (r > _PaveCenter.z || r < _PaveCentral.x || Excluded(p)) discard;

    float2 fw = fwidth(p);
    float px = max(fw.x, fw.y);
    Pave pv = ResolvePave(p);
    float3 albedo = StoneColor(pv, px);

    float3 normalWS = normalize(IN.normalWS);
    float3 viewDirWS = GetWorldSpaceNormalizeViewDir(pw);
    float3 lightDir, lightColor; float shadowAtten;
    AnimeMainLight_float(pw, _MaxLightIntensity, lightDir, lightColor, shadowAtten);
    float lightMask, midMask, ndotl, shadowMask;
    AnimeToonBands_float(normalWS, lightDir, shadowAtten, _ToonThreshold, _ToonSoftness, _MidWidth, lightMask, midMask, ndotl, shadowMask);
    float3 direct, shadowAlbedo;
    AnimeShadowBands_float(albedo, lightColor, lightMask, midMask, ndotl, shadowMask,
                           _ShadowColor.rgb, _ShadowTintStrength, _ShadowStrength, _MidColor.rgb,
                           _HighlightStrength, _FormRetention, direct, shadowAlbedo);
    float3 ambient;
    AnimeAmbient_float(normalWS, SampleSH(normalWS), shadowAlbedo, lightMask, _AmbientColor.rgb, _AmbientStrength, _AmbientSceneInfluence, ambient);
    float3 additional;
    AnimeAdditionalLights_float(pw, normalWS, float4(GetNormalizedScreenSpaceUV(IN.positionCS), 0, 0), albedo, _ToonThreshold, _ToonSoftness, additional);
    float rimMask;
    AnimeRimMask_float(normalWS, viewDirWS, lightDir, rimMask);
    float3 rim = _RimColor.rgb * pow(1.0 - saturate(dot(normalWS, viewDirWS)), _RimPower) * _RimStrength * rimMask * dot(lightColor, ANIME_LUMA);

    float3 color = direct + ambient + additional + rim;
    float luma = dot(color, ANIME_LUMA);
    color = lerp(luma.xxx, color, _Saturation) * _Brightness;
    color = MixFog(color, IN.fogFactor);
    return half4(color, 1);
}

// Depth-only / depth-normals: same clipping so the depth texture matches what is drawn.
struct PDepthVaryings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half3 normalWS : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };

PDepthVaryings PaveDepthVert(PAttributes v)
{
    PDepthVaryings o = (PDepthVaryings)0;
    UNITY_SETUP_INSTANCE_ID(v);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
    VertexPositionInputs vpi = GetVertexPositionInputs(v.positionOS.xyz);
    o.positionCS = vpi.positionCS;
    o.positionWS = vpi.positionWS;
    o.normalWS = TransformObjectToWorldNormal(v.normalOS);
    return o;
}

void PaveClip(float3 pw)
{
    float r = length(pw.xz - _PaveCenter.xy);
    if (r > _PaveCenter.z || r < _PaveCentral.x || Excluded(pw.xz)) discard;
}

half PaveDepthFrag(PDepthVaryings IN) : SV_Target { PaveClip(IN.positionWS); return IN.positionCS.z; }

half4 PaveDepthNormalsFrag(PDepthVaryings IN) : SV_Target
{
    PaveClip(IN.positionWS);
    return half4(NormalizeNormalPerPixel(IN.normalWS), 0.0);
}

#endif
