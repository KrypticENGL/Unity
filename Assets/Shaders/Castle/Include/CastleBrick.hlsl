// CastleBrick.hlsl
// Procedural masonry functions for SG_Castle_WhiteBrick (used by Custom Function nodes).
//
// Everything is analytic: no textures, no loops, no raymarching. Brick size, mortar
// and bevel widths are all expressed in metres so the look stays consistent across
// meshes of different sizes when world-space mapping is used.

#ifndef CASTLE_BRICK_INCLUDED
#define CASTLE_BRICK_INCLUDED

#include "Assets/Shaders/Castle/Include/CastleNoise.hlsl"

// ---------------------------------------------------------------------------------
// 01 - UV / Brick Coordinates
// World mapping picks the dominant axis of the geometric normal, so rows stay
// horizontal on every wall orientation and courses line up around corners.
// UV mapping uses the mesh UVs and tangent frame instead.
// Coord is in metres; AxisU / AxisV are the world directions of +Coord.x / +Coord.y.
// ---------------------------------------------------------------------------------
void CastleBrickCoordinates_float(float3 PositionWS, float3 NormalWS, float3 TangentWS, float3 BitangentWS,
                                  float2 UV, float WorldMapping, float UVScale,
                                  out float2 Coord, out float3 AxisU, out float3 AxisV, out float FaceID)
{
    if (WorldMapping > 0.5)
    {
        float3 an = abs(NormalWS);
        if (an.y > an.x && an.y > an.z)
        {
            // Floors / wall tops.
            Coord = PositionWS.xz;
            AxisU = float3(1, 0, 0);
            AxisV = float3(0, 0, 1);
            FaceID = 2;
        }
        else if (an.x > an.z)
        {
            // Walls facing +/-X.
            Coord = PositionWS.zy;
            AxisU = float3(0, 0, 1);
            AxisV = float3(0, 1, 0);
            FaceID = 0;
        }
        else
        {
            // Walls facing +/-Z.
            Coord = PositionWS.xy;
            AxisU = float3(1, 0, 0);
            AxisV = float3(0, 1, 0);
            FaceID = 1;
        }
    }
    else
    {
        Coord = UV * UVScale;
        AxisU = TangentWS;
        AxisV = BitangentWS;
        FaceID = 3;
    }
}

// Horizontal jitter of the vertical joint at the left edge of brick column k in row r.
// Always in [0, amount) with amount < 1, so joint k never moves left of k.
float CastleBrickJoint(float k, float r, float face, float amount)
{
    return CastleHash31(float3(k, r, face) + 0.5) * amount;
}

// ---------------------------------------------------------------------------------
// 02 - Brick Pattern
// Staggered running bond. Every row is offset by RowOffset plus a small random
// amount, and every vertical joint is jittered so brick widths vary slightly.
// JointDist : distance (m) from the pixel to the nearest joint centre line.
// EdgeDir   : outward direction (brick space) toward that nearest joint.
// FaceCenter: 0 at the joint, 1 at the brick centre line.
// FilterWidth: screen-space footprint (m), used to anti-alias the thin mortar.
// ---------------------------------------------------------------------------------
void CastleBrickPattern_float(float2 Coord, float FaceID,
                              float BrickWidth, float BrickHeight, float AspectRatio,
                              float BrickScale, float BrickDensity, float RowOffset, float Irregularity,
                              out float2 CellID, out float JointDist, out float2 EdgeDir,
                              out float FaceCenter, out float FilterWidth)
{
    float2 size = max(float2(BrickWidth * AspectRatio, BrickHeight) * BrickScale / max(BrickDensity, 0.001), 0.001);
    float2 p = Coord / size;

    float irr = saturate(Irregularity);
    float row = floor(p.y);
    float rowJitter = (CastleHash31(float3(row, FaceID, 11.7)) - 0.5) * irr * 0.5;
    float x = p.x + row * RowOffset + rowJitter;

    // Jittered vertical joints. Joint k sits at k + J(k) with 0 <= J < 1, so a pixel
    // in [i, i+1) belongs either to column i (right of joint i) or to column i-1.
    float jitter = irr * 0.7;
    float i = floor(x);
    float jPrev = CastleBrickJoint(i - 1, row, FaceID, jitter);
    float jCur  = CastleBrickJoint(i,     row, FaceID, jitter);
    float jNext = CastleBrickJoint(i + 1, row, FaceID, jitter);
    float back = step(x, i + jCur);
    float left  = lerp(i + jCur,      i - 1 + jPrev, back);
    float right = lerp(i + 1 + jNext, i + jCur,      back);
    float column = i - back;

    float2 halfSize = float2((right - left) * 0.5 * size.x, 0.5 * size.y);
    float2 local = float2((x - (left + right) * 0.5) * size.x, (frac(p.y) - 0.5) * size.y);
    float2 d = halfSize - abs(local);

    JointDist = min(d.x, d.y);
    EdgeDir = (d.x < d.y) ? float2(sign(local.x), 0) : float2(0, sign(local.y));
    FaceCenter = saturate(JointDist / max(min(halfSize.x, halfSize.y), 0.0001));
    CellID = float2(column, row);
    FilterWidth = 0.75 * length(fwidth(Coord));
}

// ---------------------------------------------------------------------------------
// 04 - Per-Brick Variation
// One deterministic random set per brick (constant across the whole brick).
// Value: brightness pick, Tint: warm/cool multiplier, Tilt: face tilt in [-1, 1].
// ---------------------------------------------------------------------------------
void CastleBrickRandom_float(float2 CellID, float FaceID, float ColorVariation,
                             out float Value, out float3 Tint, out float2 Tilt)
{
    float3 c = float3(CellID, FaceID * 17.0);
    Value = CastleHash31(c + float3(0.37, 0.61, 0.13));
    float tone = CastleHash31(c + float3(41.3, 7.1, 3.9));
    Tint = 1.0 + (tone - 0.5) * saturate(ColorVariation) * float3(0.05, 0.005, -0.07);
    Tilt = float2(CastleHash31(c + float3(3.1, 19.7, 5.3)),
                  CastleHash31(c + float3(23.9, 2.3, 13.1))) * 2.0 - 1.0;
}

// ---------------------------------------------------------------------------------
// 05 - Brick Height / Normal
// Height profile: 0 in the mortar, smoothstep bevel over EdgeWidth, 1 on the face.
// The bevel normal is analytic (derivative of the smoothstep), so it is sharp and
// free of the 2x2 blockiness of ddx-based normals. Fine surface noise is added with
// a surface-gradient bump, and the bevel fades once it is smaller than a pixel to
// avoid shimmering at distance. Output normal is in world space.
// ---------------------------------------------------------------------------------
void CastleBrickNormal_float(float3 PositionWS, float3 NormalWS, float3 AxisU, float3 AxisV,
                             float JointDist, float2 EdgeDir, float FilterWidth,
                             float MortarHalfWidth, float EdgeWidth, float ReliefHeight,
                             float NormalStrength, float EdgeNormalStrength,
                             float2 Tilt, float FaceTilt,
                             float SurfaceNoise, float SurfaceNoiseScale, float SurfaceNormalStrength,
                             out float3 OutNormalWS, out float Height)
{
    float3 N = SafeNormalize(NormalWS);
    float3 U = SafeNormalize(AxisU - N * dot(AxisU, N));
    float3 V = SafeNormalize(AxisV - N * dot(AxisV, N));

    float bevel = max(EdgeWidth, 0.0001);
    float t = saturate((JointDist - MortarHalfWidth) / bevel);
    Height = t * t * (3.0 - 2.0 * t);

    float slope = ReliefHeight * 6.0 * t * (1.0 - t) / bevel * EdgeNormalStrength;
    slope *= saturate(bevel / max(3.0 * FilterWidth, 0.00001));

    float2 g = (EdgeDir * slope + Tilt * FaceTilt * Height) * NormalStrength;
    float3 n = N + g.x * U + g.y * V;

    // Surface-gradient bump from the fine stone noise (Mikkelsen 2020).
    float h = SurfaceNoise * SurfaceNormalStrength / max(SurfaceNoiseScale, 0.001);
    float3 dpdx = ddx(PositionWS);
    float3 dpdy = ddy(PositionWS);
    float3 r1 = cross(dpdy, N);
    float3 r2 = cross(N, dpdx);
    float det = dot(dpdx, r1);
    det = (abs(det) < 1e-10) ? 1e-10 : det;
    n -= (ddx(h) * r1 + ddy(h) * r2) / det;

    OutNormalWS = SafeNormalize(n);
}

// ---------------------------------------------------------------------------------
// 07 - Stylized Lighting
// Toon ramp of the main light, including its real-time shadows, evaluated with the
// brick normal. LightResponse scales how much the brick relief (vs. the flat
// geometric normal) drives the ramp, so bevels catch light as crisp bands.
// ---------------------------------------------------------------------------------
void CastleToonMainLight_float(float3 PositionWS, float3 NormalWS, float3 GeomNormalWS,
                               float LightResponse, float Threshold, float Softness,
                               out float Ramp)
{
    float3 L;
    float atten;
#if defined(SHADERGRAPH_PREVIEW)
    L = normalize(float3(0.5, 0.6, -0.45));
    atten = 1.0;
#elif defined(UNIVERSAL_LIGHTING_INCLUDED)
    Light light = GetMainLight(TransformWorldToShadowCoord(PositionWS));
    L = light.direction;
    atten = light.shadowAttenuation * light.distanceAttenuation;
#else
    L = _MainLightPosition.xyz;
    atten = 1.0;
#endif

    float3 N = SafeNormalize(lerp(GeomNormalWS, NormalWS, LightResponse));
    float soft = max(Softness, 0.001);
    float ndl = smoothstep(Threshold - soft, Threshold + soft, dot(N, L));
    float shadow = smoothstep(0.5 - soft, 0.5 + soft, atten);
    Ramp = ndl * shadow;
}

#endif // CASTLE_BRICK_INCLUDED
