using System.Collections.Generic;
using UnityEngine;

namespace Aurelius.World
{
    /// <summary>
    /// Describes one scatter layer (forest, grass, farms, rocks). The instances themselves are
    /// stored in each tile's TerrainData (terrain trees / detail layers), which Unity renders with
    /// instancing and distance culling - there are no per-instance GameObjects.
    /// </summary>
    public class AureliusScatterInfo : MonoBehaviour
    {
        [TextArea] public string description;
        public List<GameObject> prototypes = new List<GameObject>();
        public int instanceCount;
    }
}
