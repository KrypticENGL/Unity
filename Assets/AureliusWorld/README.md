# Aurelius World Generator

Procedural, deterministic terrain around the reserved Aurelius city (URP 17 / Unity 6).
Select **AureliusWorld** in the scene (or menu **Aurelius > World > Create or Select World Generator**).

## Buttons (AureliusWorld inspector)

| Button | Does | Also regenerates |
| --- | --- | --- |
| GENERATE ALL | lake shape, corridors, rivers, paths, terrain, biomes, water, vegetation | everything |
| GENERATE TERRAIN | heights + biomes + water surfaces from the current lake / river / path components | vegetation (if present) |
| GENERATE BIOMES | repaints splat layers + masks, using the terrain's current (hand-sculpted) heights | - |
| GENERATE RIVERS / PATHS / LAKE | re-routes from the seed, overwriting those components | terrain |
| GENERATE VEGETATION | trees, bushes, rocks, grass from current heights + masks | - |
| Regenerate Tile | one tile only | - |
| Detect City Landmarks | measures Castle / University / Colosseum, sets pads + city radius | - |
| CLEAR GENERATED CONTENT | removes terrain, water surfaces, scatter; keeps splines | - |

Same settings + seed = same world. Edit `AureliusWorldSettings.asset` (inline in the inspector).

## Layout

- Compass follows the castle's gate names: **North = -Z, East = -X** (`northYaw = 180`).
- Reserved city: radius 670 m (the Colosseum wall reaches 605 m, the University 632 m). It is flat at y = 3.95 with
  **landmark pads** (Colosseum floor at y = 8, University plateau at y = 16) and **water carves** under the castle's
  moat and canals (any renderer with a *Water* material under a landmark).
- 3x3 tiles of 2048 m (`Terrain_NW ... Terrain_CenterReserved ... Terrain_SE`), 1025 heightmap (2 m), one TerrainData
  asset per tile in `Generated/TerrainData`. Change `tilesPerSide` / `tileSize` to extend the world.

## Editing

- **Rivers / Paths** (`Water/Rivers`, `Paths/*`): select one, drag the control points in the Scene view
  (Shift-drag inserts a point), then *Apply to Terrain*.
- **Lake** (`Water/Lake`): move the object (Y = water level), drag the shore handles and island handles.
- **Bridge sites** (`Paths/BridgeSites`): empty markers where paths cross rivers.

## Masks (for later systems)

Each tile's `AureliusTerrainTile` holds `maskA` (Mountain, Forest, Farm, Rocky) and `maskB` (Water, River, Path,
CityReserved). Query anywhere with `AureliusTerrainChunkManager.SampleMask(worldPos, AureliusMask.Forest)`.
`AureliusWorldField.Sample(x, z)` gives the same masks analytically in editor tools.

## Debug views

Menu **Aurelius > Debug**, or *Debug View* on AureliusWorld: Height, Biome Mask (red mountains, green forest,
yellow farms, orange rocky, blue water, brown paths, white city), individual masks, Terrain Chunks, LOD Levels.

## Rendering / performance

- `Shaders/AureliusAnimeTerrain.shader`: single-pass 8-layer terrain shader using `AnimeLighting.hlsl`
  (toon bands, colored shadows, rim) + URP's own terrain shadow / depth / instancing passes. Cliff rock, mountain rock,
  snow, wet shore and farm parcels are procedural (one 256² noise texture, no per-layer textures).
- Trees and rocks are Terrain tree instances of LODGroup prefabs (LOD0 / LOD1 / culled): no per-instance GameObjects.
  Trees use capsule trunk colliders; rocks use their low-poly LOD1 as a convex collider.
- Grass and flowers are instanced Terrain detail meshes, drawn to 140 m.
- `AureliusTerrainChunkManager` (on `Terrain`): neighbour stitching, quality presets (pixel error, tree / grass
  distance, grass density) and play-mode distance streaming of tiles.

## City pavement (inside the reserved circle)

Object **Aurelius_CityPavement** (menu **Aurelius > City > Create or Select City Pavement**): 16 draped mesh chunks,
one material (`MAT_Aurelius_CityPavement`, shader `Aurelius/City Pavement`), no colliders (the terrain collider is
0.12 m below). The shader picks the paving system per pixel and lays stones in that system's own frame:

| System | Orientation |
| --- | --- |
| Royal roads / avenues / streets | running bond along the road, curb borders, centre accent on royal roads |
| Rings, general ground, royal heart | concentric courses, each course with its own whole number of stones (no stretching) |
| Building plazas | rectangle aligned to the building, or a ring around a round building |

Layout, widths, colours and tile sizes update live from the component. **BUILD PAVEMENT MESH** is only needed
after changing the radii / Pavement Height / terrain. The landmark ground mask (baked by raycasts onto landmark
colliders) cuts the pavement out where the castle and University have their own visible ground surfaces.
