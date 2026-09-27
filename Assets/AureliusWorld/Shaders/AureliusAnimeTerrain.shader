// Aurelius anime terrain (URP 17 / Unity 6). Single forward pass for 8 terrain layers:
// the second control map is bound per tile by AureliusTerrainTile, so there is no add-pass.
// Shadow / depth / depth-normals / selection passes reuse URP's own terrain passes, which keeps
// terrain instancing, holes and shadow casting identical to the stock Terrain/Lit shader.
Shader "Aurelius/Anime Terrain"
{
    Properties
    {
        [Header(Biome Layers)]
        _GrassA ("Grass A", Color) = (0.42, 0.70, 0.27, 1)
        _GrassB ("Grass B", Color) = (0.55, 0.77, 0.30, 1)
        _DarkGrassA ("Dark Grass A", Color) = (0.25, 0.52, 0.22, 1)
        _DarkGrassB ("Dark Grass B", Color) = (0.32, 0.58, 0.24, 1)
        _ForestA ("Forest Ground A", Color) = (0.30, 0.43, 0.20, 1)
        _ForestB ("Forest Ground B", Color) = (0.44, 0.45, 0.23, 1)
        _SoilA ("Farm Soil A", Color) = (0.56, 0.40, 0.25, 1)
        _SoilB ("Farm Soil B", Color) = (0.44, 0.30, 0.19, 1)
        _PathA ("Dirt Path A", Color) = (0.76, 0.62, 0.43, 1)
        _PathB ("Dirt Path B", Color) = (0.66, 0.52, 0.36, 1)
        _RockyA ("Rocky Ground A", Color) = (0.55, 0.52, 0.46, 1)
        _RockyB ("Rocky Ground B", Color) = (0.46, 0.44, 0.41, 1)
        _SandA ("Sand / Lake Shore A", Color) = (0.93, 0.85, 0.64, 1)
        _SandB ("Sand / Lake Shore B", Color) = (0.86, 0.77, 0.57, 1)
        _MudA ("River Bank A", Color) = (0.45, 0.40, 0.29, 1)
        _MudB ("River Bank B", Color) = (0.38, 0.45, 0.28, 1)

        [Header(Procedural Layers)]
        _CliffA ("Cliff Rock A", Color) = (0.56, 0.53, 0.50, 1)
        _CliffB ("Cliff Rock B", Color) = (0.45, 0.43, 0.43, 1)
        _MountainRockA ("Mountain Rock A", Color) = (0.56, 0.56, 0.60, 1)
        _MountainRockB ("Mountain Rock B", Color) = (0.44, 0.45, 0.50, 1)
        _SnowColor ("Snow", Color) = (0.95, 0.97, 1.0, 1)
        _CliffSlope ("Cliff Slope (start, end)", Vector) = (0.28, 0.42, 0, 0)
        _MountainRockRange ("Mountain Rock Height (start, full)", Vector) = (180, 380, 0, 0)
        _SnowBlend ("Snow Blend", Float) = 45
        _WaterLevels ("Water Levels (lake, sea)", Vector) = (1, -18, 0, 0)

        [Header(Farm Parcels)]
        _CropWheat ("Wheat", Color) = (0.90, 0.76, 0.36, 1)
        _CropGreen ("Green Crop", Color) = (0.50, 0.72, 0.28, 1)
        _CropFallow ("Fallow", Color) = (0.66, 0.74, 0.40, 1)
        _FieldEdge ("Field Edge", Color) = (0.33, 0.56, 0.24, 1)
        _FieldParams ("Field (size, rotation, seed, stretch)", Vector) = (85, 18, 2248, 1.5)

        [Header(Detail)]
        [NoScaleOffset] _NoiseTex ("Noise (RGBA tileable)", 2D) = "gray" {}
        _MacroScale ("Macro Scale (1/m)", Float) = 0.0055
        _DetailScale ("Detail Scale (1/m)", Float) = 0.11
        _GrainScale ("Grain Scale (1/m)", Float) = 0.55
        _GrainStrength ("Grain Strength", Range(0, 0.3)) = 0.06
        _HeightTransition2 ("Height Blend", Range(0.01, 1)) = 0.35

        [Header(Anime Lighting)]
        _ToonThreshold ("Toon Threshold", Range(-1, 1)) = 0
        _ToonSoftness ("Toon Softness", Range(0, 1)) = 0.35
        _MidWidth ("Mid Band Width", Range(0, 1)) = 0.38
        _MidColor ("Mid Color", Color) = (0.84, 0.9, 0.82, 1)
        _ShadowColorGreen ("Shadow Color (vegetation)", Color) = (0.14, 0.36, 0.4, 1)
        _ShadowColorEarth ("Shadow Color (rock / soil)", Color) = (0.44, 0.42, 0.6, 1)
        _ShadowColorSnow ("Shadow Color (snow)", Color) = (0.55, 0.62, 0.9, 1)
        _ShadowTintStrength ("Shadow Tint Strength", Range(0, 1)) = 0.66
        _ShadowStrength ("Shadow Strength", Range(0, 1)) = 0.44
        _HighlightStrength ("Highlight Strength", Range(0, 1)) = 0.1
        _FormRetention ("Form Retention", Range(0, 1)) = 0.35
        _AmbientColor ("Ambient Color", Color) = (0.55, 0.7, 0.85, 1)
        _AmbientStrength ("Ambient Strength", Range(0, 1)) = 0.25
        _AmbientSceneInfluence ("Ambient Scene Influence", Range(0, 1)) = 0.3
        _RimColor ("Rim Color", Color) = (0.85, 0.9, 1, 1)
        _RimStrength ("Rim Strength", Range(0, 1)) = 0.03
        _RimPower ("Rim Power", Range(0.5, 8)) = 4
        _MaxLightIntensity ("Max Light Intensity", Float) = 1.05
        _Saturation ("Saturation", Range(0, 2)) = 1.05
        _Brightness ("Brightness", Range(0, 2)) = 1

        // set by the terrain engine / tiles
        [HideInInspector] _Control ("Control (RGBA)", 2D) = "red" {}
        [HideInInspector] _Control1 ("Control 1 (RGBA)", 2D) = "black" {}
        [HideInInspector] _AureliusMaskA ("Mask A", 2D) = "black" {}
        [HideInInspector] _AureliusMaskB ("Mask B", 2D) = "black" {}
        [HideInInspector] _Splat0 ("Layer 0 (R)", 2D) = "grey" {}
        [HideInInspector] _Splat1 ("Layer 1 (G)", 2D) = "grey" {}
        [HideInInspector] _Splat2 ("Layer 2 (B)", 2D) = "grey" {}
        [HideInInspector] _Splat3 ("Layer 3 (A)", 2D) = "grey" {}
        [HideInInspector] _MainTex ("BaseMap (RGB)", 2D) = "grey" {}
        [HideInInspector] _BaseColor ("Main Color", Color) = (1,1,1,1)
        [HideInInspector] _TerrainHolesTexture ("Holes Map (RGB)", 2D) = "white" {}
        [ToggleUI] _EnableInstancedPerPixelNormal ("Enable Instanced per-pixel normal", Float) = 1.0
    }

    HLSLINCLUDE
    #pragma multi_compile_fragment __ _ALPHATEST_ON
    ENDHLSL

    SubShader
    {
        Tags { "Queue" = "Geometry-100" "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "UniversalMaterialType" = "Lit" "IgnoreProjector" = "False" "TerrainCompatible" = "True" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex AureliusTerrainVert
            #pragma fragment AureliusTerrainFrag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _LIGHT_LAYERS
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap
            #pragma shader_feature_local _TERRAIN_INSTANCED_PERPIXEL_NORMAL

            #include "AureliusTerrainSurface.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/Shaders/Terrain/TerrainLitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/Terrain/TerrainLitPasses.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap
            #include "Packages/com.unity.render-pipelines.universal/Shaders/Terrain/TerrainLitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/Terrain/TerrainLitPasses.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthNormalOnlyVertex
            #pragma fragment DepthNormalOnlyFragment
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap
            #include "Packages/com.unity.render-pipelines.universal/Shaders/Terrain/TerrainLitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/Terrain/TerrainLitDepthNormalsPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "SceneSelectionPass"
            Tags { "LightMode" = "SceneSelectionPass" }

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap
            #define SCENESELECTIONPASS
            #include "Packages/com.unity.render-pipelines.universal/Shaders/Terrain/TerrainLitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/Terrain/TerrainLitPasses.hlsl"
            ENDHLSL
        }

        UsePass "Hidden/Nature/Terrain/Utilities/PICKING"
    }

    Fallback "Hidden/Universal Render Pipeline/FallbackError"
}
