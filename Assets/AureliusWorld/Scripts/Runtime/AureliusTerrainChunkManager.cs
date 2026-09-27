using System.Collections.Generic;
using UnityEngine;

namespace Aurelius.World
{
    public enum AureliusQuality { Low, Medium, High }

    /// <summary>
    /// Owns the terrain tiles: keeps neighbours connected, applies LOD / draw-distance settings per
    /// quality level and (in Play mode) streams tiles in and out around a target. Tiles are separate
    /// TerrainData assets, so they can later move into additive scenes / Addressables without changes
    /// here: register them with <see cref="Register"/> when they load.
    /// </summary>
    [ExecuteAlways]
    public class AureliusTerrainChunkManager : MonoBehaviour
    {
        [Header("Streaming (Play mode)")]
        public bool streaming = true;
        [Tooltip("Streams around this transform. Empty = main camera.")]
        public Transform target;
        [Tooltip("Tiles whose bounds are within this distance are enabled.")]
        public float loadDistance = 3200f;
        [Tooltip("Tiles further than this are disabled (hysteresis).")]
        public float unloadDistance = 3600f;
        [Tooltip("Tiles that are never streamed out (e.g. the city tile).")]
        public List<AureliusTerrainTile> alwaysLoaded = new List<AureliusTerrainTile>();

        [Header("LOD / Quality")]
        public AureliusQuality quality = AureliusQuality.High;
        [Tooltip("Terrain geometry error in pixels per quality level (higher = cheaper).")]
        public Vector3 pixelError = new Vector3(12f, 7f, 4f);
        [Tooltip("Tree & rock draw distance per quality level.")]
        public Vector3 treeDistance = new Vector3(700f, 1100f, 1600f);
        [Tooltip("Grass draw distance per quality level.")]
        public Vector3 detailDistance = new Vector3(60f, 100f, 140f);
        [Tooltip("Grass density per quality level.")]
        public Vector3 detailDensity = new Vector3(0.35f, 0.7f, 1f);

        readonly List<AureliusTerrainTile> tiles = new List<AureliusTerrainTile>();
        float nextCheck;

        public IReadOnlyList<AureliusTerrainTile> Tiles => tiles;

        void OnEnable()
        {
            Refresh();
        }

        void OnValidate()
        {
            if (isActiveAndEnabled) ApplyQuality();
        }

        /// <summary>Re-collects child tiles, reconnects neighbours and applies quality settings.</summary>
        [ContextMenu("Refresh Tiles")]
        public void Refresh()
        {
            tiles.Clear();
            GetComponentsInChildren(true, tiles);
            ConnectNeighbours();
            ApplyQuality();
            foreach (var t in tiles) t.Bind();
        }

        public void Register(AureliusTerrainTile tile)
        {
            if (tile == null || tiles.Contains(tile)) return;
            tiles.Add(tile);
            ConnectNeighbours();
            ApplyQuality(tile);
            tile.Bind();
        }

        public void Unregister(AureliusTerrainTile tile)
        {
            if (tiles.Remove(tile)) ConnectNeighbours();
        }

        public AureliusTerrainTile TileAt(Vector3 worldPosition)
        {
            foreach (var t in tiles)
            {
                var b = t.WorldBounds;
                if (worldPosition.x >= b.min.x && worldPosition.x < b.max.x && worldPosition.z >= b.min.z && worldPosition.z < b.max.z)
                    return t;
            }
            return null;
        }

        /// <summary>Samples an exported biome mask anywhere in the world.</summary>
        public float SampleMask(Vector3 worldPosition, AureliusMask mask)
        {
            var t = TileAt(worldPosition);
            return t != null ? t.SampleMask(worldPosition, mask) : 0f;
        }

        public void ConnectNeighbours()
        {
            var map = new Dictionary<Vector2Int, Terrain>();
            foreach (var t in tiles) if (t != null && t.Terrain != null) map[t.coord] = t.Terrain;
            foreach (var t in tiles)
            {
                if (t == null || t.Terrain == null) continue;
                map.TryGetValue(t.coord + Vector2Int.left, out var left);
                map.TryGetValue(t.coord + Vector2Int.up, out var top);
                map.TryGetValue(t.coord + Vector2Int.right, out var right);
                map.TryGetValue(t.coord + Vector2Int.down, out var bottom);
                t.Terrain.SetNeighbors(left, top, right, bottom);
            }
        }

        public void ApplyQuality()
        {
            foreach (var t in tiles) ApplyQuality(t);
        }

        void ApplyQuality(AureliusTerrainTile tile)
        {
            var terrain = tile != null ? tile.Terrain : null;
            if (terrain == null) return;
            int q = (int)quality;
            terrain.heightmapPixelError = pixelError[q];
            terrain.treeDistance = treeDistance[q];
            terrain.treeBillboardDistance = treeDistance[q];
            terrain.detailObjectDistance = detailDistance[q];
            terrain.detailObjectDensity = detailDensity[q];
        }

        void Update()
        {
            if (!Application.isPlaying || !streaming) return;
            if (Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + 0.5f;

            Transform focus = target != null ? target : (Camera.main != null ? Camera.main.transform : null);
            if (focus == null) return;
            Vector3 f = focus.position;
            foreach (var t in tiles)
            {
                if (t == null || alwaysLoaded.Contains(t)) continue;
                var b = t.WorldBounds;
                b.extents = new Vector3(b.extents.x, 1e5f, b.extents.z);
                float dist = Mathf.Sqrt(b.SqrDistance(f));
                bool active = t.gameObject.activeSelf;
                if (!active && dist < loadDistance) t.gameObject.SetActive(true);
                else if (active && dist > unloadDistance) t.gameObject.SetActive(false);
            }
        }
    }
}
