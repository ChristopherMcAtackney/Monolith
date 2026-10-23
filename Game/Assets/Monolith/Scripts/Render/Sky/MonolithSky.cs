using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Monolith.Sky
{
    /// <summary>
    /// HDRP sky volume component for the Monolith sky: real star catalogue, Milky Way map and the Sun. It carries the numbers
    /// <see cref="MonolithSkyRenderer"/> needs; <see cref="SkyRig"/> fills it from <see cref="SkyOptions"/>.
    /// </summary>
    [Serializable]
    [VolumeComponentMenuForRenderPipeline("Sky/Monolith Sky", typeof(HDRenderPipeline))]
    [SkyUniqueID(UniqueId)]
    public sealed class MonolithSky : SkySettings
    {
        /// <summary>Visual Environment sky type for this sky (HDRP's own are 1 to 4).</summary>
        public const int UniqueId = 0x4D4E01;

        public Vector3Parameter sunDirection = new Vector3Parameter(Vector3.forward);
        public Vector3Parameter skyRotationEuler = new Vector3Parameter(Vector3.zero);
        public ColorParameter sunColor = new ColorParameter(Color.white, false, false, false);
        public Vector4Parameter sunCore = new Vector4Parameter(new Vector4(400f, 0.8f, 0f, 0f));            // energy, sigma px
        public Vector4Parameter sunHalo = new Vector4Parameter(new Vector4(1500f, 0.013f, 9000f, 0.2f));         // halo energy, halo size, veil energy, veil size (screen height fractions)
        public Vector4Parameter sunStreak = new Vector4Parameter(new Vector4(0.35f, 0.0035f, 0.09f, 0f));    // peak, thickness, reach
        public Vector4Parameter starParams = new Vector4Parameter(new Vector4(0.05f, 0.75f, 6f, 8.3f));      // gain, gamma, reference magnitude, limit magnitude
        public Vector4Parameter starShape = new Vector4Parameter(new Vector4(0.75f, 1.0f, 0.6f, 0f));        // core sigma px, saturation, halo fraction
        public Vector4Parameter starHalo = new Vector4Parameter(new Vector4(1.2f, 6f, 0f, 0f));              // halo size px: dim, bright
        public Vector4Parameter milkyWay = new Vector4Parameter(new Vector4(0.55f, 1.5f, 0.7f, 0f));           // gain, contrast, saturation
        public ColorParameter milkyWayTint = new ColorParameter(Color.white, false, false, false);
        public ColorParameter blackLift = new ColorParameter(Color.black, false, false, false);
        public ColorParameter ambientColor = new ColorParameter(Color.black, false, false, false);
        public FloatParameter ambientNits = new FloatParameter(0f);
        public Vector4Parameter ambientShape = new Vector4Parameter(new Vector4(0.7f, 1f, 0f, 0f));          // away-from-Sun gradient, Milky Way gain
        /// <summary>Nits of one display unit: 1.2 * 2^EV100 of the reference exposure.</summary>
        public FloatParameter nitsPerUnit = new FloatParameter(19660.8f);

        public override int GetHashCode()
        {
            // Only what the lighting cubemap (Milky Way and ambient fill) depends on; stars and the Sun are not baked.
            int hash = base.GetHashCode();
            unchecked
            {
                hash = hash * 23 + skyRotationEuler.GetHashCode();
                hash = hash * 23 + milkyWay.GetHashCode();
                hash = hash * 23 + milkyWayTint.GetHashCode();
                hash = hash * 23 + ambientColor.GetHashCode();
                hash = hash * 23 + ambientNits.GetHashCode();
                hash = hash * 23 + ambientShape.GetHashCode();
                hash = hash * 23 + sunDirection.GetHashCode();
                hash = hash * 23 + nitsPerUnit.GetHashCode();
            }
            return hash;
        }

        public override Type GetSkyRendererType() => typeof(MonolithSkyRenderer);
    }
}
