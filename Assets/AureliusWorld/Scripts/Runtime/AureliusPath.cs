using UnityEngine;

namespace Aurelius.World
{
    public enum PathKind { Major, Secondary, Ring }

    /// <summary>
    /// A path / road corridor spline. The terrain is smoothed and slightly depressed along it.
    /// Major paths are the four radial corridors that will become roads.
    /// </summary>
    public class AureliusPath : AureliusSpline
    {
        public PathKind kind = PathKind.Secondary;
        public float width = 5f;

        public override Color GizmoColor => kind == PathKind.Major ? new Color(1f, 0.75f, 0.3f) : new Color(0.75f, 0.5f, 0.25f);
    }
}
