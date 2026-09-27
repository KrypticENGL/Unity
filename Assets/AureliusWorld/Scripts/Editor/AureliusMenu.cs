using UnityEditor;
using UnityEngine;

namespace Aurelius.World.EditorTools
{
    public static class AureliusMenu
    {
        [MenuItem("Aurelius/World/Create or Select World Generator", priority = 0)]
        static void CreateWorld()
        {
            var world = AureliusWorldBuilder.FindOrCreateWorld();
            Selection.activeGameObject = world.gameObject;
        }

        [MenuItem("Aurelius/World/Generate All", priority = 20)]
        static void GenerateAll() => AureliusWorldBuilder.GenerateAll(AureliusWorldBuilder.FindOrCreateWorld());

        [MenuItem("Aurelius/World/Generate Terrain", priority = 21)]
        static void GenerateTerrain() => AureliusWorldBuilder.GenerateTerrain(AureliusWorldBuilder.FindOrCreateWorld());

        [MenuItem("Aurelius/World/Generate Vegetation", priority = 22)]
        static void GenerateVegetation() => AureliusWorldBuilder.GenerateVegetation(AureliusWorldBuilder.FindOrCreateWorld());

        [MenuItem("Aurelius/World/Rebuild Prefab Library", priority = 40)]
        static void RebuildLibrary() => AureliusAssetFactory.GetLibrary(true);

        static void SetDebug(AureliusDebugMode mode)
        {
            var world = Object.FindFirstObjectByType<AureliusWorld>();
            if (world == null) return;
            Undo.RecordObject(world, "Aurelius Debug View");
            world.debugMode = mode;
            world.ApplyDebugGlobals();
            EditorUtility.SetDirty(world);
            SceneView.RepaintAll();
        }

        [MenuItem("Aurelius/Debug/Off", priority = 100)] static void D0() => SetDebug(AureliusDebugMode.Off);
        [MenuItem("Aurelius/Debug/Height", priority = 101)] static void D1() => SetDebug(AureliusDebugMode.Height);
        [MenuItem("Aurelius/Debug/Biome Mask", priority = 102)] static void D2() => SetDebug(AureliusDebugMode.BiomeMask);
        [MenuItem("Aurelius/Debug/Mountains", priority = 103)] static void D3() => SetDebug(AureliusDebugMode.Mountains);
        [MenuItem("Aurelius/Debug/Forest", priority = 104)] static void D4() => SetDebug(AureliusDebugMode.Forest);
        [MenuItem("Aurelius/Debug/Farms", priority = 105)] static void D5() => SetDebug(AureliusDebugMode.Farms);
        [MenuItem("Aurelius/Debug/Water (Lake + Rivers)", priority = 106)] static void D6() => SetDebug(AureliusDebugMode.Water);
        [MenuItem("Aurelius/Debug/Rivers", priority = 107)] static void D11() => SetDebug(AureliusDebugMode.Rivers);
        [MenuItem("Aurelius/Debug/Paths", priority = 108)] static void D7() => SetDebug(AureliusDebugMode.Paths);
        [MenuItem("Aurelius/Debug/City Exclusion", priority = 109)] static void D8() => SetDebug(AureliusDebugMode.CityExclusion);
        [MenuItem("Aurelius/Debug/Terrain Chunks", priority = 110)] static void D9() => SetDebug(AureliusDebugMode.TerrainChunks);
        [MenuItem("Aurelius/Debug/LOD Levels", priority = 111)] static void D10() => SetDebug(AureliusDebugMode.LODLevels);
    }
}
