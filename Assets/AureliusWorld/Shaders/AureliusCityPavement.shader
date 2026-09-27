// Aurelius royal-city paving (URP 17). Layout comes from AureliusCityPavement (global arrays);
// this material holds the look. One material + one shader variant family for the whole city.
Shader "Aurelius/City Pavement"
{
    Properties
    {
        [Header(Stone)]
        _StoneBase ("Pavement Color", Color) = (0.96, 0.91, 0.81, 1)
        _StoneSecondary ("Secondary Stone", Color) = (0.88, 0.83, 0.74, 1)
        _Accent ("Accent (blue-grey)", Color) = (0.62, 0.66, 0.74, 1)
        _BorderColor ("Border / Curb", Color) = (0.74, 0.70, 0.66, 1)
        _MortarColor ("Joint", Color) = (0.66, 0.63, 0.63, 1)
        _RoyalTint ("Royal Road Tint", Color) = (1.04, 1.03, 1.0, 1)
        _Variation ("Variation Amount", Range(0, 1)) = 0.55
        _AccentChance ("Accent Stone Chance", Range(0, 0.3)) = 0.05
        _MortarWidth ("Joint Width (m)", Range(0.01, 0.3)) = 0.07
        _BevelWidth ("Bevel Width (m)", Range(0.01, 0.5)) = 0.12
        _BorderWidth ("Border Width (m)", Range(0.2, 4)) = 1.3
        _TileRotation ("Tile Rotation (deg)", Float) = 0

        [Header(Tile Sizes (length, width))]
        _TileMajor ("Major Road", Vector) = (2.4, 1.2, 0, 0)
        _TileSecondary ("Secondary / Ring", Vector) = (1.4, 0.7, 0, 0)
        _TileGeneral ("General Ground", Vector) = (0.9, 0.6, 0, 0)
        _TileCentral ("Central Plaza", Vector) = (2.8, 1.6, 0, 0)
        _TilePlaza ("Building Plaza", Vector) = (1.6, 0.8, 0, 0)

        [Header(Anime Lighting)]
        _ToonThreshold ("Toon Threshold", Range(-1, 1)) = 0
        _ToonSoftness ("Toon Softness", Range(0, 1)) = 0.35
        _MidWidth ("Mid Band Width", Range(0, 1)) = 0.38
        _MidColor ("Mid Color", Color) = (0.9, 0.88, 0.9, 1)
        _ShadowColor ("Shadow Color", Color) = (0.52, 0.52, 0.66, 1)
        _ShadowTintStrength ("Shadow Tint Strength", Range(0, 1)) = 0.6
        _ShadowStrength ("Shadow Strength", Range(0, 1)) = 0.4
        _HighlightStrength ("Highlight Strength", Range(0, 1)) = 0.08
        _FormRetention ("Form Retention", Range(0, 1)) = 0.3
        _AmbientColor ("Ambient Color", Color) = (0.62, 0.68, 0.9, 1)
        _AmbientStrength ("Ambient Strength", Range(0, 1)) = 0.25
        _AmbientSceneInfluence ("Ambient Scene Influence", Range(0, 1)) = 0.3
        _RimColor ("Rim Color", Color) = (0.85, 0.9, 1, 1)
        _RimStrength ("Rim Strength", Range(0, 1)) = 0.0
        _RimPower ("Rim Power", Range(0.5, 8)) = 4
        _MaxLightIntensity ("Max Light Intensity", Float) = 1.05
        _Saturation ("Saturation", Range(0, 2)) = 1.0
        _Brightness ("Brightness", Range(0, 2)) = 1.0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex PaveVert
            #pragma fragment PaveFrag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _LIGHT_LAYERS
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #pragma multi_compile_instancing
            #include "AureliusCityPavement.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex PaveDepthVert
            #pragma fragment PaveDepthFrag
            #pragma multi_compile_instancing
            #include "AureliusCityPavement.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex PaveDepthVert
            #pragma fragment PaveDepthNormalsFrag
            #pragma multi_compile_instancing
            #include "AureliusCityPavement.hlsl"
            ENDHLSL
        }
    }
    Fallback Off
}
