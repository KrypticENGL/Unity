// CastleNoise.hlsl
// Shared procedural noise for the castle Shader Graphs (used by Custom Function nodes).
//
// World-space 3D value noise: it needs no UVs, stays seamless across mesh seams,
// and matches across LOD0-LOD3 because every LOD samples the same world position.

#ifndef CASTLE_NOISE_INCLUDED
#define CASTLE_NOISE_INCLUDED

// Hash without sine (Dave Hoskins), stable on large world coordinates.
float CastleHash31(float3 p)
{
    p = frac(p * 0.1031);
    p += dot(p, p.zyx + 31.32);
    return frac((p.x + p.y) * p.z);
}

// Smooth 3D value noise in [0, 1].
float CastleValueNoise3D(float3 p)
{
    float3 i = floor(p);
    float3 f = frac(p);
    float3 u = f * f * (3.0 - 2.0 * f);

    float n000 = CastleHash31(i + float3(0, 0, 0));
    float n100 = CastleHash31(i + float3(1, 0, 0));
    float n010 = CastleHash31(i + float3(0, 1, 0));
    float n110 = CastleHash31(i + float3(1, 1, 0));
    float n001 = CastleHash31(i + float3(0, 0, 1));
    float n101 = CastleHash31(i + float3(1, 0, 1));
    float n011 = CastleHash31(i + float3(0, 1, 1));
    float n111 = CastleHash31(i + float3(1, 1, 1));

    float nx00 = lerp(n000, n100, u.x);
    float nx10 = lerp(n010, n110, u.x);
    float nx01 = lerp(n001, n101, u.x);
    float nx11 = lerp(n011, n111, u.x);
    float nxy0 = lerp(nx00, nx10, u.y);
    float nxy1 = lerp(nx01, nx11, u.y);
    return lerp(nxy0, nxy1, u.z);
}

// Two-octave noise in [0, 1]. Position should already be multiplied by the desired scale.
void CastleNoise3D_float(float3 Position, out float Out)
{
    Out = CastleValueNoise3D(Position) * 0.65
        + CastleValueNoise3D(Position * 2.03 + 17.13) * 0.35;
}

void CastleNoise3D_half(half3 Position, out half Out)
{
    float n;
    CastleNoise3D_float((float3)Position, n);
    Out = (half)n;
}

#endif // CASTLE_NOISE_INCLUDED
