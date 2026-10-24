using UnityEngine;

namespace Monolith.Core
{
    /// <summary>Put on a ship's visual by whoever shows it (<c>ShipViews.Register</c>): which world ship it is. Lets a visual's own components (an engine's glow) read that ship's
    /// state without the view and ship art assemblies knowing each other.</summary>
    public sealed class ShipHandle : MonoBehaviour
    {
        public int Id = -1;
    }
}
