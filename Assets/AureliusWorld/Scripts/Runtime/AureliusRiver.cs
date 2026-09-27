using UnityEngine;

namespace Aurelius.World
{
    public enum RiverOutlet { Lake, Sea, River }

    /// <summary>
    /// A river spline. Control points run from source to mouth. Water level is derived from the
    /// terrain along the course (always flowing downhill) when the terrain is generated.
    /// </summary>
    public class AureliusRiver : AureliusSpline
    {
        public float widthStart = 3.5f;
        public float widthEnd = 13f;
        public float depth = 2.6f;
        [Tooltip("Where the river ends. Controls the water level it settles to at the mouth.")]
        public RiverOutlet outlet = RiverOutlet.Lake;
        [Tooltip("For Outlet = River: index of the river this one joins.")]
        public int joinsRiver = -1;

        public override Color GizmoColor => new Color(0.2f, 0.55f, 1f, 1f);
    }
}
