// AureliusTerrainSurface.hlsl
// Forward pass of the Aurelius anime terrain. Biome colours come from 8 splat channels
// (_Control + _Control1) blended by height, with procedural cliff / mountain rock / snow / wet shore
// and farm parcels. Lighting is the shared anime chain from AnimeLighting.hlsl so the terrain
// matches the castle and University materials (toon bands, coloured shadows, ambient, rim).
//
// Splat layout
//   _Control  : R Grass       G Dark Grass    B Forest Ground   A Farm Soil (crop parcels)
//   _Control1 : R Dirt Path   G Rocky Ground  B Sand / Shore    A River Bank (mud)

#ifndef AURELIUS_TERRAIN_SURFACE_INCLUDED
#define AURELIUS_TERRAIN_SURFACE_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/Shaders/Terrain/TerrainLitInput.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Assets/Shaders/Anime/Include/AnimeLighting.hlsl"

// Per-tile (MaterialPropertyBlock from AureliusTerrainTile)
TEXTURE2D(_Control1);
TEXTURE2D(_AureliusMaskA);  SAMPLER(sampler_AureliusMaskA);
TEXTURE2D(_AureliusMaskB);
float4 _AureliusTileInfo;

// Globals (AureliusWorld)
float _AureliusDebugMode;
float4 _AureliusHeightRange;  // x sea floor, y city floor, z mountain top, w snow line
float4 _AureliusLodParams;    // x detail distance, y tree LOD1, z tree LOD2, w tree distance

TEXTURE2D(_NoiseTex); SAMPLER(sampler_NoiseTex);

// Material (plain uniforms: terrain is drawn with instancing, not the SRP Batcher)
float4 _GrassA, _GrassB, _DarkGrassA, _DarkGrassB, _ForestA, _ForestB, _SoilA, _SoilB;
float4 _PathA, _PathB, _RockyA, _RockyB, _SandA, _SandB, _MudA, _MudB;
float4 _CliffA, _CliffB, _MountainRockA, _MountainRockB, _SnowColor;
float4 _CropWheat, _CropGreen, _CropFallow, _FieldEdge;
float4 _FieldParams;          // x size, y rotation (deg), z seed, w stretch
float4 _CliffSlope;           // x start, y end (1 - normal.y)
float4 _MountainRockRange;    // x start height, y full height
float _SnowBlend;
float4 _WaterLevels;          // x lake, y sea
float _MacroScale, _DetailScale, _GrainScale, _HeightTransition2, _GrainStrength;

float _ToonThreshold, _ToonSoftness, _MidWidth, _ShadowTintStrength, _ShadowStrength;
float _HighlightStrength, _FormRetention, _AmbientStrength, _AmbientSceneInfluence;
float _RimStrength, _RimPower, _MaxLightIntensity, _Saturation, _Brightness;
float4 _MidColor, _ShadowColorGreen, _ShadowColorEarth, _ShadowColorSnow, _AmbientColor, _RimColor;

struct ATAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float2 texcoord : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct ATVaryings
{
    float4 clipPos : SV_POSITION;
    float2 uv : TEXCOORD0;
    float3 positionWS : TEXCOORD1;
    half3 normalWS : TEXCOORD2;
    half fogFactor : TEXCOORD3;
    UNITY_VERTEX_OUTPUT_STEREO
};

ATVaryings AureliusTerrainVert(ATAttributes v)
{
    ATVaryings o = (ATVaryings)0;
    UNITY_SETUP_INSTANCE_ID(v);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
    TerrainInstancing(v.positionOS, v.normalOS, v.texcoord);
    VertexPositionInputs vpi = GetVertexPositionInputs(v.positionOS.xyz);
    o.clipPos = vpi.positionCS;
    o.positionWS = vpi.positionWS;
    o.uv = v.texcoord;
    o.normalWS = TransformObjectToWorldNormal(v.normalOS);
    o.fogFactor = ComputeFogFactor(vpi.positionCS.z);
    return o;
}

// ---------------------------------------------------------------------------------------------
// Hash shared bit-for-bit with AureliusNoise.Hash (C#) so farm parcels match the painted paths.
// ---------------------------------------------------------------------------------------------
uint AureliusHash(int x, int y, int seed)
{
    uint h = (uint)x * 73856093u ^ (uint)y * 19349663u ^ (uint)seed * 83492791u;
    h ^= h >> 13;
    h *= 0x5bd1e995u;
    h ^= h >> 15;
    return h;
}

float AureliusHash01(int x, int y, int seed, int channel)
{
    return (AureliusHash(x, y, seed * 31 + channel * 7919) & 0xFFFFFF) / 16777216.0;
}

// Farm parcels: jittered Voronoi on a rotated, stretched grid. Returns edge distance (m) and cell id.
void AureliusFieldCell(float2 wp, out float edgeMeters, out int2 cellId, out float2 local)
{
    float size = max(_FieldParams.x, 1.0);
    float a = radians(_FieldParams.y);
    float c = cos(a), s = sin(a);
    int seed = (int)_FieldParams.z;
    float stretch = max(_FieldParams.w, 0.2);
    float2 fp = float2((wp.x * c + wp.y * s) / (size * stretch), (-wp.x * s + wp.y * c) / size);
    int2 base = (int2)floor(fp);
    float f1 = 1e5, f2 = 1e5;
    cellId = base;
    [unroll] for (int dy = -1; dy <= 1; dy++)
    [unroll] for (int dx = -1; dx <= 1; dx++)
    {
        int2 cc = base + int2(dx, dy);
        float2 j = float2(0.12 + 0.76 * AureliusHash01(cc.x, cc.y, seed, 0), 0.12 + 0.76 * AureliusHash01(cc.x, cc.y, seed, 1));
        float d = length(fp - (float2(cc) + j));
        if (d < f1) { f2 = f1; f1 = d; cellId = cc; }
        else if (d < f2) { f2 = d; }
    }
    edgeMeters = (f2 - f1) * 0.5 * size;
    local = float2(wp.x * c + wp.y * s, -wp.x * s + wp.y * c);
}

float3 AureliusCropColor(float2 wp, float4 det, out float edgeMask)
{
    float edge; int2 id; float2 local;
    AureliusFieldCell(wp, edge, id, local);
    int seed = (int)_FieldParams.z;
    float type = AureliusHash01(id.x, id.y, seed, 2);
    float dirSel = AureliusHash01(id.x, id.y, seed, 3);
    float rowCoord = dirSel < 0.5 ? local.x : local.y;
    float rows = sin(rowCoord * 6.2831853 / 3.2) * 0.5 + 0.5;
    rows = smoothstep(0.25, 0.75, rows);

    float3 col;
    if (type < 0.3)       col = lerp(_SoilA.rgb, _SoilB.rgb, rows * 0.8 + det.r * 0.2);
    else if (type < 0.58) col = _CropWheat.rgb * lerp(0.9, 1.06, rows) * lerp(0.95, 1.05, det.g);
    else if (type < 0.84) col = _CropGreen.rgb * lerp(0.86, 1.05, rows) * lerp(0.95, 1.05, det.g);
    else                  col = lerp(_CropFallow.rgb, _GrassA.rgb, det.g * 0.5);

    edgeMask = 1.0 - smoothstep(1.2, 2.6, edge + (det.r - 0.5) * 0.8);
    return lerp(col, _FieldEdge.rgb, edgeMask);
}

// ---------------------------------------------------------------------------------------------
// Debug views
// ---------------------------------------------------------------------------------------------
float3 AureliusHeatRamp(float t)
{
    t = saturate(t);
    float3 c0 = float3(0.05, 0.1, 0.35), c1 = float3(0.1, 0.55, 0.9), c2 = float3(0.25, 0.75, 0.3);
    float3 c3 = float3(0.95, 0.85, 0.3), c4 = float3(0.65, 0.35, 0.2), c5 = float3(1, 1, 1);
    if (t < 0.2) return lerp(c0, c1, t / 0.2);
    if (t < 0.4) return lerp(c1, c2, (t - 0.2) / 0.2);
    if (t < 0.6) return lerp(c2, c3, (t - 0.4) / 0.2);
    if (t < 0.8) return lerp(c3, c4, (t - 0.6) / 0.2);
    return lerp(c4, c5, (t - 0.8) / 0.2);
}

float3 AureliusDebugColor(int mode, float2 uv, float3 positionWS)
{
    float4 mA = SAMPLE_TEXTURE2D(_AureliusMaskA, sampler_AureliusMaskA, uv);
    float4 mB = SAMPLE_TEXTURE2D(_AureliusMaskB, sampler_AureliusMaskA, uv);
    float3 col = 0;
    if (mode == 1)
    {
        float h = positionWS.y;
        float t = (h - _AureliusHeightRange.x) / max(_AureliusHeightRange.z - _AureliusHeightRange.x, 1.0);
        col = AureliusHeatRamp(pow(saturate(t), 0.55));
        float contour = frac(h / 25.0);
        col *= lerp(0.7, 1.0, smoothstep(0.0, 0.06, contour));
    }
    else if (mode == 2)
    {
        float grass = saturate(1.0 - max(max(mA.r, mA.g), max(mA.b, mA.a)));
        col = float3(0.55, 0.72, 0.5) * 0.7;
        col = lerp(col, float3(1.0, 0.9, 0.1), mA.b);   // farms   YELLOW
        col = lerp(col, float3(0.1, 0.75, 0.15), mA.g); // forest  GREEN
        col = lerp(col, float3(0.95, 0.55, 0.2), mA.a * 0.8); // rocky ORANGE
        col = lerp(col, float3(0.9, 0.12, 0.1), mA.r);  // mountains RED
        col = lerp(col, float3(0.2, 0.55, 1.0), mB.g);  // rivers  BLUE
        col = lerp(col, float3(0.1, 0.3, 0.95), mB.r);  // water   BLUE
        col = lerp(col, float3(0.5, 0.3, 0.12), mB.b);  // paths   BROWN
        col = lerp(col, float3(1, 1, 1), mB.a);         // city    WHITE
        col = lerp(col, col * 1.0, grass);
    }
    else if (mode >= 3 && mode <= 8 || mode == 11)
    {
        float v = 0; float3 tint = 1;
        if (mode == 3) { v = mA.r; tint = float3(0.9, 0.12, 0.1); }
        if (mode == 4) { v = mA.g; tint = float3(0.1, 0.75, 0.15); }
        if (mode == 5) { v = mA.b; tint = float3(1.0, 0.9, 0.1); }
        if (mode == 6) { v = max(mB.r, mB.g); tint = float3(0.15, 0.45, 1.0); }
        if (mode == 7) { v = mB.b; tint = float3(0.6, 0.35, 0.12); }
        if (mode == 8) { v = mB.a; tint = float3(1, 1, 1); }
        if (mode == 11) { v = mB.g; tint = float3(0.2, 0.6, 1.0); }
        col = lerp(float3(0.08, 0.08, 0.1), tint, v);
    }
    else if (mode == 9)
    {
        float idx = _AureliusTileInfo.z;
        float3 palette = frac(float3(0.37, 0.61, 0.83) * (idx + 1.0) * 1.618);
        col = lerp(float3(0.25, 0.25, 0.3), palette, 0.8);
        float2 e = min(uv, 1.0 - uv);
        col = lerp(float3(1, 1, 1), col, smoothstep(0.0, 0.004, min(e.x, e.y)));
        float2 g = frac(uv * 16.0);
        col *= lerp(0.85, 1.0, step(0.02, min(g.x, g.y)));
    }
    else if (mode == 10)
    {
        float d = distance(_WorldSpaceCameraPos, positionWS);
        if (d < _AureliusLodParams.x) col = float3(0.2, 0.9, 0.3);       // grass visible, LOD0
        else if (d < _AureliusLodParams.y) col = float3(0.7, 0.95, 0.2); // trees LOD0
        else if (d < _AureliusLodParams.z) col = float3(1.0, 0.75, 0.2); // trees LOD1
        else if (d < _AureliusLodParams.w) col = float3(1.0, 0.4, 0.2);  // trees LOD1 far
        else col = float3(0.55, 0.3, 0.8);                                // terrain only
    }
    return col;
}

// ---------------------------------------------------------------------------------------------
// Fragment
// ---------------------------------------------------------------------------------------------
half4 AureliusTerrainFrag(ATVaryings IN) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
#ifdef _ALPHATEST_ON
    ClipHoles(IN.uv);
#endif
    float2 uv = IN.uv;
    float3 pw = IN.positionWS;
    float2 wp = pw.xz;

#if defined(ENABLE_TERRAIN_PERPIXEL_NORMAL)
    float2 sampleCoords = (uv / _TerrainHeightmapRecipSize.zw + 0.5f) * _TerrainHeightmapRecipSize.xy;
    float3 normalWS = TransformObjectToWorldNormal(normalize(SAMPLE_TEXTURE2D(_TerrainNormalmapTexture, sampler_TerrainNormalmapTexture, sampleCoords).rgb * 2 - 1));
#else
    float3 normalWS = normalize(IN.normalWS);
#endif
    float3 viewDirWS = GetWorldSpaceNormalizeViewDir(pw);

    // --- Noise (one tileable RGBA texture at four scales = anti-tiling) -----------------------
    float4 macro = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, wp * _MacroScale);
    float4 macro2 = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, wp * _MacroScale * 0.231 + 0.37);
    float4 det = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, wp * _DetailScale);
    float4 grain = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, wp * _GrainScale);

    // --- Splat weights with height blending ---------------------------------------------------
    half4 c0 = SAMPLE_TEXTURE2D(_Control, sampler_Control, uv);
    half4 c1 = SAMPLE_TEXTURE2D(_Control1, sampler_Control, uv);
    float w[8] = { c0.r, c0.g, c0.b, c0.a, c1.r, c1.g, c1.b, c1.a };
    float hgt[8] = {
        det.a * 0.6 + macro.g * 0.3,  // grass
        det.a * 0.8 + 0.1,            // dark grass
        det.g * 0.7 + 0.2,            // forest ground
        0.35,                         // farm
        0.25 + det.r * 0.25,          // path
        det.b * 0.9 + 0.25,           // rocky
        0.2 + det.r * 0.15,           // sand
        det.g * 0.5                   // mud
    };
    float ma = 0;
    [unroll] for (int i = 0; i < 8; i++) { w[i] = w[i] > 0.001 ? w[i] + hgt[i] * _HeightTransition2 : 0; ma = max(ma, w[i]); }
    float wsum = 0;
    [unroll] for (int k = 0; k < 8; k++) { w[k] = max(w[k] - (ma - _HeightTransition2 * 0.5 - 0.02), 0); wsum += w[k]; }
    wsum = max(wsum, 1e-4);

    // --- Layer colours (macro variation, no visible tiling) ----------------------------------
    float mv = saturate(macro.g * 0.75 + macro2.r * 0.6 - 0.2);
    float mountainT = smoothstep(_MountainRockRange.x, _MountainRockRange.y, pw.y + (macro.r - 0.5) * 60.0);
    float3 col0 = lerp(_GrassA.rgb, _GrassB.rgb, mv);
    float3 col1 = lerp(_DarkGrassA.rgb, _DarkGrassB.rgb, saturate(macro.r * 0.8 + det.a * 0.3));
    float3 col2 = lerp(_ForestA.rgb, _ForestB.rgb, det.g);
    float edgeMask = 0;
    float3 col3 = w[3] > 0 ? AureliusCropColor(wp, det, edgeMask) : _SoilA.rgb;
    float3 col4 = lerp(_PathA.rgb, _PathB.rgb, saturate(det.r * 0.8 + macro.r * 0.3));
    float3 col5 = lerp(lerp(_RockyA.rgb, _RockyB.rgb, det.b), lerp(_MountainRockA.rgb, _MountainRockB.rgb, det.b), mountainT);
    float3 col6 = lerp(_SandA.rgb, _SandB.rgb, macro.r);
    float3 col7 = lerp(_MudA.rgb, _MudB.rgb, det.g);

    float3 albedo = (col0 * w[0] + col1 * w[1] + col2 * w[2] + col3 * w[3] + col4 * w[4] + col5 * w[5] + col6 * w[6] + col7 * w[7]) / wsum;
    float green = (w[0] + w[1] + w[2] * 0.7 + w[3] * 0.5) / wsum;

    // --- Procedural cliff / mountain rock / snow / wet shore ---------------------------------
    float slope = 1.0 - normalWS.y;
    float cliffT = smoothstep(_CliffSlope.x, _CliffSlope.y, slope + (det.b - 0.5) * 0.1);
    float3 cliff = lerp(lerp(_CliffA.rgb, _CliffB.rgb, det.b), lerp(_MountainRockA.rgb, _MountainRockB.rgb, det.b), mountainT);
    cliff *= lerp(0.88, 1.08, macro2.g);
    albedo = lerp(albedo, cliff, cliffT);

    float snowT = smoothstep(_AureliusHeightRange.w - _SnowBlend, _AureliusHeightRange.w + _SnowBlend, pw.y + (macro.g - 0.5) * 90.0);
    snowT *= smoothstep(0.3, 0.55, normalWS.y + det.a * 0.1);
    albedo = lerp(albedo, _SnowColor.rgb * lerp(0.94, 1.04, det.r), snowT);

    float wetLake = 1.0 - smoothstep(0.0, 0.7, abs(pw.y - _WaterLevels.x - 0.2));
    float wetSea = 1.0 - smoothstep(0.0, 1.5, abs(pw.y - _WaterLevels.y - 0.5));
    albedo *= lerp(1.0, 0.8, saturate(wetLake + wetSea) * (1.0 - cliffT));

    albedo *= lerp(1.0 - _GrainStrength, 1.0 + _GrainStrength, grain.r);
    green *= (1.0 - cliffT) * (1.0 - snowT);

    // --- Anime lighting (shared with the castle / University materials) ----------------------
    float3 lightDir, lightColor; float shadowAtten;
    AnimeMainLight_float(pw, _MaxLightIntensity, lightDir, lightColor, shadowAtten);
    float lightMask, midMask, ndotl, shadowMask;
    AnimeToonBands_float(normalWS, lightDir, shadowAtten, _ToonThreshold, _ToonSoftness, _MidWidth, lightMask, midMask, ndotl, shadowMask);

    float3 shadowColor = lerp(_ShadowColorEarth.rgb, _ShadowColorGreen.rgb, saturate(green));
    shadowColor = lerp(shadowColor, _ShadowColorSnow.rgb, snowT);
    float3 direct, shadowAlbedo;
    AnimeShadowBands_float(albedo, lightColor, lightMask, midMask, ndotl, shadowMask,
                           shadowColor, _ShadowTintStrength, _ShadowStrength, _MidColor.rgb,
                           _HighlightStrength, _FormRetention, direct, shadowAlbedo);

    float3 ambient;
    AnimeAmbient_float(normalWS, SampleSH(normalWS), shadowAlbedo, lightMask,
                       _AmbientColor.rgb, _AmbientStrength, _AmbientSceneInfluence, ambient);

    float3 additional;
    AnimeAdditionalLights_float(pw, normalWS, float4(GetNormalizedScreenSpaceUV(IN.clipPos), 0, 0),
                                albedo, _ToonThreshold, _ToonSoftness, additional);

    float rimMask;
    AnimeRimMask_float(normalWS, viewDirWS, lightDir, rimMask);
    float fres = pow(1.0 - saturate(dot(normalWS, viewDirWS)), _RimPower);
    float3 rim = _RimColor.rgb * fres * _RimStrength * rimMask * dot(lightColor, ANIME_LUMA);

    float3 color = direct + ambient + additional + rim;
    float luma = dot(color, ANIME_LUMA);
    color = lerp(luma.xxx, color, _Saturation) * _Brightness;

    // --- Debug views (uniform branch) ---------------------------------------------------------
    int mode = (int)_AureliusDebugMode;
    if (mode > 0)
    {
        float relief = 0.6 + 0.4 * saturate(dot(normalWS, normalize(float3(0.4, 0.8, 0.3))));
        return half4(AureliusDebugColor(mode, uv, pw) * relief, 1);
    }

    color = MixFog(color, IN.fogFactor);
    return half4(color, 1);
}

#endif
