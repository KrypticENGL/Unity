// AnimeLighting.hlsl
// Custom Function bodies for SG_AnimeFantasy_Master / SG_AnimeFantasy_Water / SG_AnimeFantasy_Outline.
//
// Lighting chain (one function per Shader Graph group):
//   03 MAIN LIGHT     AnimeMainLight          light direction / color (intensity-capped) / real-time shadow
//                     AnimeAdditionalLights   toon-ramped point & spot lights (Forward, Forward+)
//   04 TOON LIGHTING  AnimeToonBands          N.L -> soft LIGHT / MID / SHADOW masks
//   05 SHADOW BANDS   AnimeShadowBands        masks -> colored band lighting (no black shadows)
//   06 AMBIENT        AnimeAmbient            artist ambient + scene GI, hemispherical, shadow-weighted
//   07 RIM LIGHT      AnimeRimMask            view/light-aware mask for the Fresnel rim
//   08 SPECULAR       AnimeSpecular           stylized banded Blinn highlight
//   11 MATERIAL       AnimeMetalAlbedo        stylized sky/ground "anime metal" response for gold
//   Water             AnimeWaterSurface       procedural ripples, depth tint, glints, shoreline
//   Outline           AnimeOutlineOffset      optional inverted-hull extrusion
//
// Everything is plain ALU: no textures, no noise lookups, no loops except the additional-light loop.
// The graphs use the URP Lit target with every PBR input zeroed (Specular workflow, black
// Base/Specular, Smoothness 0), so URP still provides shadows, Forward+, fog and all passes while the
// color below is written through the Emission block.

#ifndef ANIME_LIGHTING_INCLUDED
#define ANIME_LIGHTING_INCLUDED

#define ANIME_LUMA float3(0.2126, 0.7152, 0.0722)

// ---------------------------------------------------------------------------------------------
// 03 MAIN LIGHT
// ---------------------------------------------------------------------------------------------
// MaxIntensity caps the brightest channel of the sun color so a very bright directional light
// (the gameplay scene uses intensity 2) cannot blow ivory stone out to white. Hue is preserved.
void AnimeMainLight_float(float3 PositionWS, float MaxIntensity,
                          out float3 Direction, out float3 Color, out float ShadowAtten)
{
#if defined(SHADERGRAPH_PREVIEW)
    Direction = normalize(float3(0.45, 0.65, -0.6));
    Color = float3(1.0, 0.97, 0.92);
    ShadowAtten = 1.0;
#else
    float4 shadowCoord = TransformWorldToShadowCoord(PositionWS);
    Light light = GetMainLight(shadowCoord, PositionWS, half4(1, 1, 1, 1));
    Direction = light.direction;
    Color = light.color * light.distanceAttenuation;
    ShadowAtten = light.shadowAttenuation;
#endif
    float peak = max(Color.r, max(Color.g, Color.b));
    Color *= min(1.0, MaxIntensity / max(peak, 1e-4));
}

// Point / spot lights (torches, magic lights) get the same soft two-tone ramp so they read as
// part of the style. Directional additional lights are included on Forward+.
void AnimeAdditionalLights_float(float3 PositionWS, float3 NormalWS, float4 ScreenPosition,
                                 float3 Albedo, float Threshold, float Softness,
                                 out float3 Color)
{
    Color = 0;
#if !defined(SHADERGRAPH_PREVIEW) && defined(_ADDITIONAL_LIGHTS)
    float halfWidth = lerp(0.01, 0.45, Softness);
    InputData inputData = (InputData)0;
    inputData.positionWS = PositionWS;
    inputData.normalWS = NormalWS;
    inputData.normalizedScreenSpaceUV = ScreenPosition.xy;
    half4 shadowMask = half4(1, 1, 1, 1);

    #if USE_CLUSTER_LIGHT_LOOP
    [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
    {
        CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
        Light light = GetAdditionalLight(lightIndex, PositionWS, shadowMask);
        float ndl = dot(NormalWS, light.direction);
        float ramp = smoothstep(Threshold - halfWidth, Threshold + halfWidth, ndl);
        Color += Albedo * light.color * (0.25 + 0.75 * ramp) * light.distanceAttenuation * light.shadowAttenuation;
    }
    #endif

    uint pixelLightCount = GetAdditionalLightsCount();
    LIGHT_LOOP_BEGIN(pixelLightCount)
        Light light = GetAdditionalLight(lightIndex, PositionWS, shadowMask);
        float ndl = dot(NormalWS, light.direction);
        float ramp = smoothstep(Threshold - halfWidth, Threshold + halfWidth, ndl);
        // 25% wrap keeps the back of a torch-lit column warm instead of cutting to nothing.
        Color += Albedo * light.color * (0.25 + 0.75 * ramp) * light.distanceAttenuation * light.shadowAttenuation;
    LIGHT_LOOP_END
#endif
}

// ---------------------------------------------------------------------------------------------
// 04 TOON LIGHTING
// ---------------------------------------------------------------------------------------------
// Softness 0 = crisp anime terminator, 1 = very soft. MidWidth is how far (in N.L) the MID band
// extends below the LIGHT threshold. Cast shadows are pushed into the SHADOW band with a slightly
// sharpened edge so they match the cel look instead of the shadow-map blur.
void AnimeToonBands_float(float3 NormalWS, float3 LightDirection, float ShadowAtten,
                          float Threshold, float Softness, float MidWidth,
                          out float LightMask, out float MidMask, out float NdotL, out float ShadowMask)
{
    NdotL = dot(normalize(NormalWS), LightDirection);
    float halfWidth = lerp(0.01, 0.45, Softness);
    ShadowMask = lerp(smoothstep(0.3, 0.7, ShadowAtten), ShadowAtten, Softness);
    LightMask = smoothstep(Threshold - halfWidth, Threshold + halfWidth, NdotL) * ShadowMask;
    MidMask = smoothstep(Threshold - MidWidth - halfWidth, Threshold - MidWidth + halfWidth, NdotL) * ShadowMask;
}

// ---------------------------------------------------------------------------------------------
// 05 SHADOW BANDS
// ---------------------------------------------------------------------------------------------
// SHADOW band: the albedo is pulled toward ShadowColor (hue shift, e.g. ivory -> lavender) rather
//              than multiplied by it - multiplying a warm albedo by a cool tint gives mud-gray.
//              Then darkened by ShadowStrength and scaled by sun luminance. Never black.
// MID band   : albedo x MidColor x sun color
// LIGHT band : albedo x sun color, with an extra lift toward N.L = 1 (Highlight Strength) so lit
//              planes facing the sun separate from lit planes at a grazing angle.
// FormRetention blends a smooth half-Lambert back in so curved roofs and round towers keep their
// volume inside each band instead of going flat.
void AnimeShadowBands_float(float3 Albedo, float3 LightColor, float LightMask, float MidMask,
                            float NdotL, float ShadowMask,
                            float3 ShadowColor, float ShadowTintStrength, float ShadowStrength,
                            float3 MidColor, float HighlightStrength, float FormRetention,
                            out float3 Direct, out float3 ShadowAlbedo)
{
    float sunLuma = dot(LightColor, ANIME_LUMA);
    ShadowAlbedo = lerp(Albedo, ShadowColor, ShadowTintStrength);

    float3 shadowTone = ShadowAlbedo * ((1.0 - ShadowStrength) * sunLuma);
    float3 midTone = Albedo * MidColor * LightColor;
    float highlight = 1.0 + HighlightStrength * smoothstep(0.55, 1.0, NdotL) * LightMask;
    float3 lightTone = Albedo * LightColor * highlight;

    float3 band = lerp(shadowTone, midTone, MidMask);
    band = lerp(band, lightTone, LightMask);

    float halfLambert = saturate(NdotL * 0.5 + 0.5) * ShadowMask;
    float3 smoothTone = lerp(shadowTone, Albedo * LightColor, halfLambert * halfLambert);
    Direct = lerp(band, smoothTone, FormRetention);
}

// ---------------------------------------------------------------------------------------------
// 06 AMBIENT
// ---------------------------------------------------------------------------------------------
// Artist ambient color mixed with the scene's baked GI / sky probe, brighter on up-facing surfaces
// (sky dome) and darker facing the ground. It is weighted toward the shadow side so shaded
// architecture stays readable while lit faces keep their contrast. Albedo here is the shadow
// albedo from 05 so the fill light keeps the shadow hue.
void AnimeAmbient_float(float3 NormalWS, float3 BakedGI, float3 Albedo, float LightMask,
                        float3 AmbientColor, float AmbientStrength, float SceneInfluence,
                        out float3 Ambient)
{
    float up = saturate(normalize(NormalWS).y * 0.5 + 0.5);
    float hemi = lerp(0.55, 1.0, up);
    float3 ambientLight = lerp(AmbientColor, BakedGI, SceneInfluence);
    Ambient = Albedo * ambientLight * (hemi * AmbientStrength * (1.0 - 0.6 * LightMask));
}

// ---------------------------------------------------------------------------------------------
// 07 RIM LIGHT
// ---------------------------------------------------------------------------------------------
// Scales the Fresnel rim: always a little on, strongest when the sun is behind the silhouette
// (castle against a bright sky) and on up-facing edges (spires, roof ridges).
void AnimeRimMask_float(float3 NormalWS, float3 ViewDirectionWS, float3 LightDirection,
                        out float Mask)
{
    float3 v = normalize(ViewDirectionWS);
    float backLight = saturate(dot(-v, LightDirection) * 0.5 + 0.5);
    float up = saturate(normalize(NormalWS).y * 0.5 + 0.5);
    Mask = lerp(0.45, 1.0, backLight) * lerp(0.6, 1.0, up);
}

// ---------------------------------------------------------------------------------------------
// 08 SPECULAR
// ---------------------------------------------------------------------------------------------
// Size 0 = pin-point, 1 = broad sheen. Threshold picks where the highlight is cut; Roughness
// widens the cut into a soft falloff. Metallic tints the highlight with the albedo (gold).
void AnimeSpecular_float(float3 NormalWS, float3 ViewDirectionWS, float3 LightDirection,
                         float3 LightColor, float ShadowMask, float3 Albedo,
                         float Strength, float Size, float Threshold, float Roughness, float Metallic,
                         out float3 Specular)
{
    float3 n = normalize(NormalWS);
    float3 h = normalize(LightDirection + normalize(ViewDirectionWS));
    float ndh = saturate(dot(n, h));
    float exponent = exp2(lerp(11.0, 2.0, saturate(Size)));
    float blinn = pow(ndh, exponent);
    float halfWidth = lerp(0.02, 0.4, saturate(Roughness));
    float band = smoothstep(Threshold - halfWidth, Threshold + halfWidth, blinn);
    float3 metalTint = Albedo / max(max(Albedo.r, max(Albedo.g, Albedo.b)), 1e-3);
    float3 tint = lerp(float3(1, 1, 1), lerp(float3(1, 1, 1), metalTint, 0.65), saturate(Metallic));
    Specular = band * Strength * LightColor * tint * ShadowMask * saturate(dot(n, LightDirection) * 4.0);
}

// ---------------------------------------------------------------------------------------------
// 11 MATERIAL CONTROLS - stylized metal
// ---------------------------------------------------------------------------------------------
// Without reflection probes or textures, metal reads as "flat yellow". This fakes the anime
// gold look: the reflection vector picks a bright sky band above the horizon and a darker ground
// band below it, with a hot line near the horizon. Metallic 0 returns the albedo unchanged.
void AnimeMetalAlbedo_float(float3 Albedo, float3 NormalWS, float3 ViewDirectionWS, float Metallic,
                            out float3 Result)
{
    float3 r = reflect(-normalize(ViewDirectionWS), normalize(NormalWS));
    float sky = smoothstep(-0.2, 0.25, r.y);
    float horizon = exp(-abs(r.y - 0.08) * 9.0);
    float env = lerp(0.42, 1.12, sky) + 0.35 * horizon;
    Result = Albedo * lerp(1.0, env, saturate(Metallic));
}

// Optional vertex color influence (RGB tints the base color). Strength 0 ignores the vertex
// stream entirely, so meshes without vertex colors (or with packed data in them) are unaffected.
void AnimeVertexColor_float(float3 Color, float4 VertexColor, float Strength, out float3 Out)
{
    Out = Color * lerp(float3(1, 1, 1), VertexColor.rgb, saturate(Strength));
}

// ---------------------------------------------------------------------------------------------
// 12 FINAL OUTPUT
// ---------------------------------------------------------------------------------------------
void AnimeCombine_float(float3 Direct, float3 Ambient, float3 Additional, float3 Rim,
                        float3 Specular, float3 Fresnel, float3 Emission, out float3 Color)
{
    Color = Direct + Ambient + Additional + Rim + Specular + Fresnel + Emission;
}

// ---------------------------------------------------------------------------------------------
// WATER
// ---------------------------------------------------------------------------------------------
// Calm canal water: three slow procedural swells perturb the normal (no textures), depth fades
// shallow -> deep color using the camera depth texture, Fresnel mixes in a sky reflection color,
// sun glints are banded like the master specular, and a thin soft line brightens the shoreline.
void AnimeWaterSurface_float(float3 PositionWS, float3 NormalWS, float3 ViewDirectionWS,
                             float SceneEyeDepth, float4 ScreenPositionRaw, float Time,
                             float3 LightDirection, float3 LightColor, float ShadowAtten,
                             float3 ShallowColor, float3 DeepColor, float DepthDistance,
                             float3 ReflectionColor, float FresnelStrength, float FresnelPower,
                             float RippleScale, float RippleSpeed, float RippleStrength,
                             float SpecularStrength, float SpecularSize,
                             float3 ShadowColor, float ShadowStrength,
                             float3 EdgeColor, float EdgeWidth,
                             float ShallowAlpha, float DeepAlpha,
                             out float3 Color, out float Alpha)
{
    float2 p = PositionWS.xz * RippleScale;
    float t = Time * RippleSpeed;
    // Analytic derivatives of three crossing sine swells.
    float2 d1 = normalize(float2(1.0, 0.35));
    float2 d2 = normalize(float2(-0.45, 1.0));
    float2 d3 = normalize(float2(0.8, -0.9));
    float2 grad = d1 * cos(dot(p, d1) * 1.00 + t * 1.0) * 1.00
                + d2 * cos(dot(p, d2) * 1.73 + t * 1.3) * 0.60
                + d3 * cos(dot(p, d3) * 2.91 + t * 0.7) * 0.35;
    float3 n = normalize(normalize(NormalWS) + float3(-grad.x, 0.0, -grad.y) * RippleStrength);
    float3 v = normalize(ViewDirectionWS);

    // Depth tint. Raw screen position w is the surface's eye depth (perspective cameras).
    // SceneEyeDepth <= surface depth when the depth texture is missing -> treat as deep water.
    float thickness = SceneEyeDepth - ScreenPositionRaw.w;
    thickness = thickness > 0.0 ? thickness : DepthDistance * 4.0;
    float depth01 = 1.0 - exp(-thickness / max(DepthDistance, 1e-3));
    float3 body = lerp(ShallowColor, DeepColor, depth01);

    // Soft two-tone lighting of the body (water is mostly lit from the sky, not the sun).
    float ndl = dot(n, LightDirection);
    float lit = smoothstep(-0.1, 0.35, ndl) * lerp(smoothstep(0.3, 0.7, ShadowAtten), 1.0, 0.25);
    float sunLuma = dot(LightColor, ANIME_LUMA);
    float3 shadowTint = lerp(float3(1, 1, 1), ShadowColor, 0.6) * (1.0 - ShadowStrength);
    body *= lerp(shadowTint * sunLuma, LightColor, lit * 0.85 + 0.15);

    // Sky reflection.
    float fresnel = pow(1.0 - saturate(dot(n, v)), FresnelPower) * FresnelStrength;
    Color = lerp(body, ReflectionColor * max(sunLuma, 0.35), saturate(fresnel));

    // Banded sun glints.
    float3 h = normalize(LightDirection + v);
    float blinn = pow(saturate(dot(n, h)), exp2(lerp(11.0, 4.0, SpecularSize)));
    float glint = smoothstep(0.45, 0.55, blinn) * SpecularStrength * smoothstep(0.3, 0.7, ShadowAtten);
    Color += LightColor * glint;

    // Shoreline / wall contact line.
    float edge = 1.0 - smoothstep(0.0, max(EdgeWidth, 1e-3), thickness);
    Color = lerp(Color, EdgeColor * max(sunLuma, 0.5), edge * 0.6);

    Alpha = saturate(lerp(ShallowAlpha, DeepAlpha, depth01) + fresnel * 0.5 + edge * 0.3 + glint);
}

// ---------------------------------------------------------------------------------------------
// OUTLINE (optional)
// ---------------------------------------------------------------------------------------------
// Inverted-hull extrusion in object space. Width is roughly constant on screen up to MaxWidth
// (world units) so distant towers do not get fat outlines.
void AnimeOutlineOffset_float(float3 PositionOS, float3 NormalOS, float Width, float MaxWidth,
                              out float3 PositionOut)
{
    float3 positionWS = TransformObjectToWorld(PositionOS);
    float distanceToCamera = length(_WorldSpaceCameraPos - positionWS);
    float widthWS = min(Width * 0.001 * distanceToCamera, MaxWidth);
    float3 normalWS = TransformObjectToWorldDir(NormalOS, true);
    positionWS += normalWS * widthWS;
    PositionOut = TransformWorldToObject(positionWS);
}

#endif // ANIME_LIGHTING_INCLUDED
