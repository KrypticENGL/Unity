# Anime Fantasy Shader System (URP 17.3 / Unity 6000.3)

Stylized, texture-free rendering for the Aurelius castle, the University and future city pieces.
No meshes, colliders, transforms or hierarchies were changed. Materials are assigned through the
FBX importers' material remap (`castle_visual.fbx.meta`, `University_Visual.fbx.meta`), so every
scene that uses those models picks them up automatically.

## Files

| Path | What |
| --- | --- |
| `SG_AnimeFantasy_Master.shadergraph` | Master shader used by all opaque materials |
| `SG_AnimeFantasy_Water.shadergraph` | Transparent canal/fountain water |
| `SG_AnimeFantasy_Outline.shadergraph` | Optional thin inverted-hull outline (not applied by default) |
| `Include/AnimeLighting.hlsl` | Custom Function bodies, one per graph group |
| `Assets/Materials/Anime/*.mat` | Material instances |
| `Assets/Scenes/Anime/AnimeFantasy_ShaderTest.unity` | Test scene: real castle, swatch area, evaluation rig |
| `Assets/Scripts/Rendering/AnimeLightingEvaluator.cs` | Front/Side/Back light and Close/Medium/Far camera presets |

## How the master shader works

`Normal -> main light -> N.L -> toon bands -> colored shadow -> ambient -> rim -> specular -> final color`

The graph is organised into groups `01 INPUTS` ... `12 FINAL OUTPUT`, each with a sticky note.
It uses the **URP Lit target as a container**: Base Color and Specular are black and Smoothness is 0
(Specular workflow), so URP's PBR adds nothing. The stylized color is written to **Emission**. URP still
provides real-time shadows (all cascades and soft shadows), Forward+ additional lights, fog, depth,
depth-normals, shadow casting and the SRP Batcher.

* **Toon bands.** LIGHT / MID / SHADOW from N.L with `Toon Threshold`, `Toon Softness` (0 = crisp,
  1 = very soft) and `Mid Band Width`. Cast shadows join the SHADOW band.
* **Colored shadows.** The shadow band shifts the albedo *toward* `Shadow Color` (ivory -> lavender,
  purple roof -> deep violet-blue) instead of multiplying it, which would turn warm stone gray. It is
  darkened by `Shadow Strength` and never goes black.
* **Form Retention.** Blends a smooth half-Lambert back in so round towers and curved roofs keep
  their volume inside each band.
* **Ambient.** `Ambient Color` mixed with scene GI (`Ambient Scene Influence`), brighter from the sky
  and weighted to the shadow side.
* **Rim.** Fresnel x `Rim Strength` x a mask that is strongest when back-lit and on up-facing edges.
* **Specular.** Banded Blinn highlight (`Specular Strength / Size / Threshold`, softened by
  `Roughness`). `Metallic` tints it and switches on the stylized sky/ground metal response used by gold.
* **Fresnel.** Sky reflection for glass. **Emission** for stained glass.
* **Royal Highlight Strength.** Multiplies rim and emission by (1+R) and specular by (1+R/2).
  Castle materials use 0.35, University 0.12, ground and background 0.
* **Max Light Intensity** (default 1.05) caps the sun's brightest channel with hue preserved.
  The gameplay sun has intensity 2, which would otherwise clip ivory to white under the Neutral tonemapper.
* **Vertex Color Strength** (default 0). The University FBX has vertex colors, but they hold packed data,
  not tints, so they are ignored unless you raise this. Meshes without vertex colors work unchanged.

## Material assignment

| FBX material | Material |
| --- | --- |
| M_Stone_Main | MAT_Castle_Stone |
| M_Stone_Light | MAT_Castle_Stone_Trim |
| M_Stone_Floor | MAT_Castle_Stone_Floor |
| M_Stone_Dark | MAT_Architecture_Dark |
| M_Roof_Purple / M_Roof_Royal_Blue | MAT_Castle_Roof / MAT_Castle_Roof_Blue |
| M_Gold_Metal | MAT_Castle_Gold |
| M_Glass_Royal_Blue / M_Glass_Cyan / M_Glass_Violet | MAT_Castle_Glass / _Cyan / _Violet (violet = central keep, strongest glow) |
| M_Opening_Dark | MAT_Castle_Opening (deep indigo, not black) |
| M_Banner_Fabric | MAT_Castle_Banner |
| M_Water, MAT_University_Water | MAT_Castle_Water |
| M_Vegetation / M_Vegetation_Light / M_Bark | MAT_Vegetation / MAT_Vegetation_Light / MAT_Vegetation_Bark |
| M_City_Ground | MAT_City_Ground |
| MAT_University_* | MAT_University_Stone / _Stone_Weathered / _Paving / _Teal / _Roof / _Gold / _Glass / _Wood / _Rock, MAT_Grass, MAT_Vegetation |

To revert a model to its embedded materials, clear the remap in the model's Import Settings > Materials tab.

## Test scene

Open `Assets/Scenes/Anime/AnimeFantasy_ShaderTest.unity`. On **Anime Lighting Evaluator**, switch
*Light Preset* (Front / Side / Back) and *Camera Preset* (Close / Medium / Far / Swatch Area). This works
in edit mode. In Play mode the keys are 1/2/3 for light and Q/W/E/R for camera. The *Shader Swatch Area*
holds stone, trim, dark plinth, purple and blue roofs, gold, stained glass, doorway, banner, a water
basin with a bridge, and vegetation. The purple dome also carries the optional outline as a second material.

## Optional outline

`MAT_Anime_Outline` is thin, dark blue-purple and width-capped. It is not applied anywhere except the
swatch dome. To use it, add it as an extra material on a renderer, or use a *Render Objects* renderer
feature with it as the override material for a layer.

## Performance

Plain ALU only: no texture samples (the water adds one depth-texture read), no noise, no loops except
URP's additional-light loop. Only water is transparent. One shader variant family is shared by every
opaque material, so the SRP Batcher keeps the city cheap.
