using UnityEngine;

namespace Monolith.Core
{
    /// <summary>Feeds the real frame time to the <see cref="Sim"/> once per rendered frame, before everything else runs.</summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class SimDriver : MonoBehaviour
    {
        public Sim Sim;

        void Update()
        {
            if (Sim != null) Sim.Advance(Time.unscaledDeltaTime);
        }
    }
}
