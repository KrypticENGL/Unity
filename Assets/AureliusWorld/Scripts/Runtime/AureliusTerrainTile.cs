using UnityEngine;

namespace Aurelius.World
{
    public enum AureliusMask { Mountain, Forest, Farm, Rocky, Water, River, Path, City, Grassland }

    /// <summary>
    /// One terrain chunk. Holds the exported biome masks for later systems (spawning, AI, quests,
    /// props) and binds the second splat control map + masks to the anime terrain shader.
    /// MaskA = (Mountain, Forest, Farm, Rocky), MaskB = (Water, River, Path, CityReserved).
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(Terrain))]
    public class AureliusTerrainTile : MonoBehaviour
    {
        public Vector2Int coord;
        public int index;
        public Texture2D maskA;
        public Texture2D maskB;

        Terrain terrain;
        MaterialPropertyBlock block;

        public Terrain Terrain => terrain != null ? terrain : (terrain = GetComponent<Terrain>());

        public Bounds WorldBounds
        {
            get
            {
                var t = Terrain;
                if (t == null || t.terrainData == null) return new Bounds(transform.position, Vector3.zero);
                var size = t.terrainData.size;
                return new Bounds(transform.position + size * 0.5f, size);
            }
        }

        void OnEnable() => Bind();

        void OnValidate() => Bind();

        /// <summary>Pushes per-tile shader data (8-layer control map, masks, tile index).</summary>
        public void Bind()
        {
            var t = Terrain;
            if (t == null || t.terrainData == null) return;
            block ??= new MaterialPropertyBlock();
            t.GetSplatMaterialPropertyBlock(block);
            var alpha = t.terrainData.alphamapTextures;
            if (alpha != null && alpha.Length > 1 && alpha[1] != null) block.SetTexture("_Control1", alpha[1]);
            if (maskA != null) block.SetTexture("_AureliusMaskA", maskA);
            if (maskB != null) block.SetTexture("_AureliusMaskB", maskB);
            block.SetVector("_AureliusTileInfo", new Vector4(coord.x, coord.y, index, 0));
            t.SetSplatMaterialPropertyBlock(block);
        }

        /// <summary>Samples an exported mask at a world position (0..1). Requires readable mask textures.</summary>
        public float SampleMask(Vector3 worldPosition, AureliusMask mask)
        {
            var t = Terrain;
            if (t == null || t.terrainData == null) return 0f;
            Vector3 local = worldPosition - transform.position;
            Vector3 size = t.terrainData.size;
            float u = Mathf.Clamp01(local.x / size.x), v = Mathf.Clamp01(local.z / size.z);
            if (mask == AureliusMask.Grassland)
            {
                float m = 0f;
                for (int i = 0; i < 4; i++) m = Mathf.Max(m, SampleMask(worldPosition, (AureliusMask)i));
                m = Mathf.Max(m, SampleMask(worldPosition, AureliusMask.Water));
                m = Mathf.Max(m, SampleMask(worldPosition, AureliusMask.City));
                return 1f - m;
            }
            int idx = (int)mask;
            var tex = idx < 4 ? maskA : maskB;
            if (tex == null || !tex.isReadable) return 0f;
            Color c = tex.GetPixelBilinear(u, v);
            return c[idx % 4];
        }
    }
}
