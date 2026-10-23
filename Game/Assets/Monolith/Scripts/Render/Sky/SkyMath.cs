using UnityEngine;

namespace Monolith.Sky
{
    /// <summary>
    /// The sky's frames of reference. The catalogue and the Milky Way map are in equatorial coordinates (ICRS: x to RA 0h,
    /// y to RA 6h, z to the north celestial pole). The game world is the ecliptic: world +Y is ecliptic north, so the
    /// planets (later) lie in the world's XZ plane. Unity is left-handed, so the mapping flips handedness, which is what
    /// keeps the sky from being mirrored: looking along world +Z with +Y up puts east on the left, as on the real sky.
    ///   world = (-ecl.y, ecl.z, ecl.x)      ecl = equatorial rotated by the obliquity of the ecliptic
    /// </summary>
    public static class SkyMath
    {
        public const float ObliquityDeg = 23.4392911f;

        /// <summary>Equatorial direction to world direction, with an optional extra rotation of the whole sky (Sun included).</summary>
        public static Matrix4x4 SkyToWorld(Vector3 extraEulerDeg)
        {
            float e = ObliquityDeg * Mathf.Deg2Rad, c = Mathf.Cos(e), s = Mathf.Sin(e);
            var m = Matrix4x4.identity;
            m.SetRow(0, new Vector4(0f, -c, -s, 0f));   // world.x = -(y cos e + z sin e)
            m.SetRow(1, new Vector4(0f, -s, c, 0f));    // world.y = -y sin e + z cos e
            m.SetRow(2, new Vector4(1f, 0f, 0f, 0f));   // world.z = x
            return extraEulerDeg == Vector3.zero ? m : Matrix4x4.Rotate(Quaternion.Euler(extraEulerDeg)) * m;
        }

        /// <summary>World direction of a point given by ecliptic longitude and latitude.</summary>
        public static Vector3 EclipticToWorld(float lonDeg, float latDeg, Vector3 extraEulerDeg)
        {
            float lon = lonDeg * Mathf.Deg2Rad, lat = latDeg * Mathf.Deg2Rad;
            var ecl = new Vector3(Mathf.Cos(lat) * Mathf.Cos(lon), Mathf.Cos(lat) * Mathf.Sin(lon), Mathf.Sin(lat));
            var world = new Vector3(-ecl.y, ecl.z, ecl.x);
            return (Quaternion.Euler(extraEulerDeg) * world).normalized;
        }

        /// <summary>World direction of an equatorial point (right ascension in hours, declination in degrees).</summary>
        public static Vector3 EquatorialToWorld(float raHours, float decDeg, Vector3 extraEulerDeg)
        {
            float ra = raHours * 15f * Mathf.Deg2Rad, dec = decDeg * Mathf.Deg2Rad;
            var eq = new Vector4(Mathf.Cos(dec) * Mathf.Cos(ra), Mathf.Cos(dec) * Mathf.Sin(ra), Mathf.Sin(dec), 0f);
            return ((Vector3)(SkyToWorld(extraEulerDeg) * eq)).normalized;
        }
    }
}
