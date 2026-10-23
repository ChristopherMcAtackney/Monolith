using UnityEngine;

namespace Monolith.Juice
{
    /// <summary>
    /// Screen shake as "trauma" (Eiserloh, GDC 2016): hits add to a 0..1 value that fades; the shake that is drawn grows with
    /// trauma squared and moves smoothly on noise, so a small hit is a tick and a big one is a roar. Pure numbers: the camera
    /// applies them (<see cref="CameraShake"/>), the pad rumble borrows the same value.
    /// </summary>
    public sealed class Trauma
    {
        /// <summary>How much trauma drains each second.</summary>
        public float DecayPerSecond = 0.62f;
        float _value;
        float _sustained;

        public float Value => Mathf.Clamp01(Mathf.Max(_value, _sustained));
        /// <summary>What the camera draws: trauma squared.</summary>
        public float Shake => Value * Value;

        /// <summary>Add trauma (hits, blasts). Several at once stack, up to 1.</summary>
        public void Add(float amount)
        {
            if (amount <= 0f) return;
            _value = Mathf.Min(1f, _value + amount);
        }

        /// <summary>A rumble that holds while something is happening (a beam roaring past); call every frame it lasts, it lets go by itself.</summary>
        public void Sustain(float level) { _sustained = Mathf.Max(_sustained, Mathf.Clamp01(level)); }

        public void Tick(float dt)
        {
            _value = Mathf.Max(0f, _value - DecayPerSecond * dt);
            _sustained = 0f;
        }

        public void Clear() { _value = 0f; _sustained = 0f; }
    }

    /// <summary>
    /// Applies <see cref="Trauma"/> to the player's camera after the view has put it on the ship (runs just after it): mostly
    /// rotation, as first-person shake should be, with a little positional jitter. Smooth Perlin noise, fixed seeds,
    /// effect-clock time, so a replayed run shakes the same way. Intensity and the off switch come from the settings.
    /// </summary>
    [DefaultExecutionOrder(60)]
    public sealed class CameraShake : MonoBehaviour
    {
        public const float MaxPitchDeg = 3.4f, MaxYawDeg = 3.4f, MaxRollDeg = 5.5f, MaxShiftM = 0.16f;
        public const float Frequency = 21f;

        JuiceContext _c;
        public Vector3 LastAnglesDeg { get; private set; }

        public static CameraShake Create(JuiceContext c)
        {
            var s = c.Camera.gameObject.AddComponent<CameraShake>();
            s._c = c;
            return s;
        }

        void LateUpdate()
        {
            LastAnglesDeg = Vector3.zero;
            if (_c == null) return;
            float scale = _c.Settings.ShakeScale;
            float shake = _c.Trauma.Shake * scale;
            if (shake <= 1e-4f) return;
            shake = Mathf.Min(shake, 1.6f);
            float t = _c.Time * Frequency;
            float pitch = Noise(t, 11.3f), yaw = Noise(t, 27.9f), roll = Noise(t * 0.8f, 43.1f);
            var angles = new Vector3(pitch * MaxPitchDeg, yaw * MaxYawDeg, roll * MaxRollDeg) * shake;
            var shift = new Vector3(Noise(t * 1.1f, 61.7f), Noise(t * 1.3f, 79.3f), Noise(t, 97.1f) * 0.35f) * (MaxShiftM * shake);
            var tr = transform;
            tr.rotation = tr.rotation * Quaternion.Euler(angles);
            tr.position += tr.rotation * shift;
            LastAnglesDeg = angles;
        }

        /// <summary>Smooth noise in -1..1: two octaves of Perlin.</summary>
        static float Noise(float t, float seed)
        {
            float a = Mathf.PerlinNoise(t, seed) * 2f - 1f;
            float b = Mathf.PerlinNoise(t * 2.3f + 5.1f, seed + 3.7f) * 2f - 1f;
            return a * 0.8f + b * 0.2f;
        }
    }
}
