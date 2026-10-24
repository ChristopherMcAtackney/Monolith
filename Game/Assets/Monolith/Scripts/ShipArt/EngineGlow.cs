using Monolith.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Monolith.ShipArt
{
    /// <summary>
    /// Makes an engine alive. The model's engine subsystem has a glowing <c>core</c> (the hot disc in the bell) and a <c>plume</c> (a short flare of nested additive
    /// shells); this flickers both with smooth noise, scales the flare's length with <see cref="Throttle"/> and dims everything as the throttle falls to 0. Nothing
    /// here knows game rules: the flight or effects system sets <see cref="Throttle"/> (0 off, 1 full burn). Added to every engine by ShipArtSetup.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EngineGlow : MonoBehaviour
    {
        static readonly int EmissiveId = Shader.PropertyToID("_EmissiveColor");

        [SerializeField] Renderer[] _core = new Renderer[0], _plume = new Renderer[0];
        /// <summary>Small point lights in front of the nozzle: the engine's light on the hull and the bell around it (no shadows).</summary>
        [SerializeField] HDAdditionalLightData[] _lights = new HDAdditionalLightData[0];
        [SerializeField] float[] _lightCandela = new float[0];
        ShipSubsystem _sub;
        /// <summary>0 = engine off (no flare, a dark bell), 1 = full burn.</summary>
        [Range(0f, 1f)] public float Throttle = 0.8f;
        /// <summary>True for small ships: the throttle and the afterburner of the world ship this visual shows (a <see cref="ShipHandle"/>) drive the glow, so a fighter's engines
        /// sit low when it coasts, burn when it flies and flare when it lights its afterburner.</summary>
        public bool FollowShip;
        /// <summary>0 to 1: the afterburner. It brightens the core and stretches the flare.</summary>
        [Range(0f, 1f)] public float Burn;
        ShipHandle _handle;
        /// <summary>How much the brightness and the flare's length waver (0 = steady).</summary>
        [Range(0f, 0.5f)] public float Flicker = 0.14f;

        Color[] _coreBase, _plumeBase;
        Vector3[] _plumeScale;
        float _seed;
        MaterialPropertyBlock _block;

        public void Configure(Renderer[] core, Renderer[] plume, HDAdditionalLightData[] lights = null, float[] candela = null)
        {
            _core = core; _plume = plume;
            _lights = lights ?? new HDAdditionalLightData[0];
            _lightCandela = candela ?? new float[0];
            _seed = Mathf.Abs(transform.GetSiblingIndex() * 7.31f + name.GetHashCode() % 97) + 3f;
        }

        void OnEnable() => Capture();

        void Capture()
        {
            _block = new MaterialPropertyBlock();
            _coreBase = Bases(_core);
            _plumeBase = Bases(_plume);
            _plumeScale = new Vector3[_plume.Length];
            for (int i = 0; i < _plume.Length; i++) _plumeScale[i] = _plume[i] != null ? _plume[i].transform.localScale : Vector3.one;
        }

        static Color[] Bases(Renderer[] rs)
        {
            var c = new Color[rs.Length];
            for (int i = 0; i < rs.Length; i++)
                c[i] = rs[i] != null && rs[i].sharedMaterial != null ? rs[i].sharedMaterial.GetColor(EmissiveId) : Color.black;
            return c;
        }

        void LateUpdate()
        {
            if (_block == null) Capture();
            float t = Time.time;
            // two noises at different speeds: a quick flutter and a slower surge
            float fast = Mathf.PerlinNoise(_seed, t * 17f) - 0.5f, slow = Mathf.PerlinNoise(_seed * 1.7f, t * 4.3f) - 0.5f;
            if (_sub == null) _sub = GetComponent<ShipSubsystem>();
            if (FollowShip)
            {
                if (_handle == null) _handle = GetComponentInParent<ShipHandle>();
                var w = GameWorld.Current;
                var ship = _handle != null && w != null ? w.Ships.Get(_handle.Id) : null;
                if (ship != null)
                {
                    float dt = Time.deltaTime;
                    Throttle = Mathf.MoveTowards(Throttle, ship.AfterburnerOn ? 1f : 0.4f + 0.6f * Mathf.Clamp01(ship.Throttle), dt * 2.5f);
                    Burn = Mathf.MoveTowards(Burn, ship.AfterburnerOn ? 1f : 0f, dt * 4f);
                }
            }
            bool dead = _sub != null && _sub.Destroyed;
            float k = dead || Throttle <= 0f ? 0f : Throttle * (1f + 0.5f * Burn) * (1f + Flicker * (fast * 1.4f + slow * 1.0f));
            for (int i = 0; i < _lights.Length; i++)
                if (_lights[i] != null) _lights[i].SetIntensity(_lightCandela[i] * Mathf.Clamp01(k) * (k > 0f ? 1f : 0f), LightUnit.Candela);
            for (int i = 0; i < _core.Length; i++) Apply(_core[i], _coreBase[i] * (k <= 0f ? 0.03f : 0.3f + 0.7f * k));
            float length = Mathf.Lerp(0.30f, 1f, Throttle) * (1f + 0.7f * Burn) * (1f + Flicker * (slow * 1.2f + fast * 0.5f));
            for (int i = 0; i < _plume.Length; i++)
            {
                var r = _plume[i];
                if (r == null) continue;
                r.enabled = k > 0.01f;
                Apply(r, _plumeBase[i] * k);
                var tr = r.transform;
                tr.localScale = new Vector3(_plumeScale[i].x, _plumeScale[i].y, _plumeScale[i].z * length);
            }
        }

        void Apply(Renderer r, Color emissive)
        {
            if (r == null) return;
            r.GetPropertyBlock(_block);
            _block.SetVector(EmissiveId, new Vector4(emissive.r, emissive.g, emissive.b, 0f));      // as a plain vector: SetColor would gamma-convert the nits
            r.SetPropertyBlock(_block);
        }
    }
}
