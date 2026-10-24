using Monolith.Core;
using UnityEngine;

namespace Monolith.Juice
{
    /// <summary>
    /// What a weapon does where it lands: a ripple on a shield (a hex-patterned bubble flares and the ring spreads), sparks and
    /// debris and a burn mark on a hull, a near miss, a flak burst. Each also tells the feel (shake, rumble, edge flash, sound)
    /// when the player is the one hit.
    /// </summary>
    public sealed class HitEffects
    {
        readonly JuiceContext _c;
        readonly Emitters _fx;
        readonly Projectiles _projectiles;
        readonly Feel _feel;
        int _slot;

        public HitEffects(JuiceContext c, Emitters fx, Projectiles projectiles, Feel feel)
        {
            _c = c; _fx = fx; _projectiles = projectiles; _feel = feel;
        }

        public static readonly Vector3 ShieldBlue = new Vector3(0.42f, 0.78f, 1.0f);

        /// <summary>A radius that never falls below a fraction of the view at its distance, so a hit far away is still a clear flare.</summary>
        float Ap(Vector3 p, float r, float minFraction) => Mathf.Max(r, minFraction * _c.DistanceToCamera(p));

        // ------------------------------------------------------------------------------------------------ shields

        public void OnShieldHit(in GameEvent e)
        {
            var s = _c.Settings;
            _projectiles.Consume(e.Position, e.Entity);
            float m = Mathf.Clamp(0.65f + e.Value / 45f, 0.65f, 2.2f);
            bool toPlayer = _c.IsPlayer(e.Entity);
            var ship = _c.World.Ships.Get(e.Entity);
            Vector3 vel = ship != null ? ship.Velocity : Vector3.zero;
            Vector3 centre = ship != null ? ship.Position : e.Position;
            Vector3 outward = (e.Position - centre).sqrMagnitude > 1e-4f ? (e.Position - centre).normalized : -e.Direction.normalized;
            float dist = _c.DistanceToCamera(e.Position);

            if (s.effects.shieldRipples && !toPlayer) Ripple(e.Entity, e.Position, m);
            if (s.effects.shieldRipples)
            {
                // A bright flash where it lands, blue-white sparks thrown back off the bubble.
                float r = 2.6f * m * _c.Size;
                _fx.Flash(e.Position, Ap(e.Position, r, 0.02f), ShieldBlue * (40f * m), 0.14f, 0.8f, 2f, 1.5f, vel);
                _fx.Flash(e.Position, Ap(e.Position, r * 3f, 0.035f), ShieldBlue * (4.0f * m), 0.28f, 0.1f, 1.6f, 1.4f, vel);
                _fx.Flare(e.Position, Ap(e.Position, r * 3.5f, 0.05f), Ap(e.Position, r * 0.3f, 0.004f), ShieldBlue * (12f * m), 0.16f);
                _fx.Sparks(e.Position, vel, outward, 70f, _c.N(Mathf.RoundToInt(12 * m)), 25f, 90f * m, 0.25f, 0.65f, 0.09f, SparkStyle.Plasma, 0.5f);
                _fx.Ring(e.Position, r * 0.5f, r * 3.6f, 0.25f, ShieldBlue * (3.0f * m), 0.11f);
            }
            if (s.effects.lights) _c.Lights.Flash(e.Position + outward * 3f, new Color(0.45f, 0.72f, 1f), 2.2e7f * m * _c.Bright, 180f, 0.16f, 2f);

            _c.Audio.Play(toPlayer ? JuiceCue.HitPlayerShield : JuiceCue.HitShield, e.Position, Mathf.Clamp(0.55f * m, 0.4f, 1f), dist);
            if (toPlayer)
            {
                _c.Trauma.Add(0.22f * m);
                _c.Rumble.Pulse(0.30f * m, 0.55f, 0.16f);
                EdgeFlash(ShieldBlue * (0.9f * m), 0.24f);
                CockpitSparks(_c.Rng.Int(3, 7), true);
            }
            else _feel.Impact(e.Position, 0.05f * m, 140f, 0.04f, 0.10f, 0.08f);
        }

        /// <summary>Add a ripple to a ship's shield bubble (drawn by the effect pass).</summary>
        public void Ripple(int ship, Vector3 worldPoint, float strength)
        {
            var look = EnsureLook(ship);
            if (look == null || look.Shield == null) return;
            var sh = look.Shield;
            Vector3 local = Quaternion.Inverse(sh.Rotation) * (worldPoint - sh.Position);
            var unit = new Vector3(local.x / sh.Radii.x, local.y / sh.Radii.y, local.z / sh.Radii.z);
            if (unit.sqrMagnitude < 1e-6f) unit = Vector3.forward;
            unit.Normalize();
            // Reuse an unused slot, else the oldest.
            int pick = -1; float oldest = -1f;
            for (int i = 0; i < sh.Hits.Length; i++)
            {
                if (sh.Hits[i].w < 0f) { pick = i; break; }
                if (sh.Hits[i].w > oldest) { oldest = sh.Hits[i].w; pick = i; }
            }
            sh.Hits[pick] = new Vector4(unit.x, unit.y, unit.z, 0f);
            look.ShieldRim = Mathf.Min(1f, look.ShieldRim + 0.55f * strength);
        }

        // ------------------------------------------------------------------------------------------------ hulls

        readonly System.Collections.Generic.Dictionary<int, int> _hullHits = new System.Collections.Generic.Dictionary<int, int>();

        /// <summary>
        /// Damage builds up where you can see it (design: visible, progressive damage): every few hits a ship takes leave a vent or a fire
        /// burning on its hull for a while, attached to the hull so it travels with it.
        /// </summary>
        void Smoulder(in GameEvent e, ShipState ship, Vector3 normal, float big)
        {
            if (!_c.Settings.effects.hullHits) return;
            _hullHits.TryGetValue(e.Entity, out int n);
            _hullHits[e.Entity] = ++n;
            int every = big > 0.5f ? 4 : 3;
            if (n % every != 0) return;
            var look = _c.LookOf(e.Entity);
            Transform parent = look != null ? look.Visual : null;
            float r = (1.6f + 5f * big) * _c.Size;
            // A model may mark where its hull vents, sparks and burns (empties named FX_<kind>_<NN>): use the nearest one to the hit.
            Transform site = null; float best = 80f;
            if (look != null)
                foreach (var t in look.FxSites)
                {
                    float d = Vector3.Distance(t.position, e.Position);
                    if (d < best) { best = d; site = t; }
                }
            if (site != null)
            {
                string kind = site.name.Length > 3 ? site.name.Substring(3).ToLowerInvariant() : "";
                if (kind.StartsWith("spark")) _fx.Sparks(site.position, Vector3.zero, site.forward, 50f, _c.N(10), 14f, 70f, 0.3f, 0.9f, 0.12f, SparkStyle.Metal, 0.5f);
                else if (kind.StartsWith("fire")) _fx.Fireball(site.position + site.forward * r * 0.4f, Vector3.zero, r * 0.5f, r * 1.2f, 1.4f, 1.7f, 4f, 0f, 1.2f, 0.5f);
                else if (kind.StartsWith("smoke")) _fx.Gas(site.position, site.forward * 12f, r * 0.6f, r * 2.2f, 2.2f, new Vector3(0.5f, 0.55f, 0.65f), 0.3f, 2f);
                else _fx.Vent(site.position, site.forward, r, 14f + 10f * big, 22f + 30f * big, parent);
                return;
            }
            _fx.Vent(e.Position + normal * 0.5f, normal, r, 14f + 10f * big, 22f + 30f * big, parent);
            if (_c.Rng.Chance(0.6f)) _fx.Vent(e.Position + normal * 0.5f, (normal + _c.Rng.OnSphere() * 0.6f).normalized, r * 0.6f, 9f, 40f, parent, true);
        }

        public void OnHullHit(in GameEvent e)
        {
            var s = _c.Settings;
            _projectiles.Consume(e.Position, e.Entity);
            float m = Mathf.Clamp(0.7f + e.Value / 40f, 0.7f, 2.4f);
            bool toPlayer = _c.IsPlayer(e.Entity);
            var ship = _c.World.Ships.Get(e.Entity);
            Vector3 vel = ship != null ? ship.Velocity : Vector3.zero;
            Vector3 centre = ship != null ? ship.Position : e.Position;
            Vector3 n = e.Direction.sqrMagnitude > 0.5f ? e.Direction.normalized : (e.Position - centre).normalized;
            float dist = _c.DistanceToCamera(e.Position);
            float size = Mathf.Max(1f, ship != null ? ship.Radius : 10f);
            float big = Mathf.Clamp01(size / 200f);             // hits on capital ships are bigger splashes
            float R = (4.2f + 9f * big) * m * _c.Size;

            if (s.effects.hullHits && !toPlayer)
            {
                _fx.Flash(e.Position + n * 0.5f, Ap(e.Position, R * 1.0f, 0.014f), Emitters.WarmWhite * (34f * m), 0.09f, 0.75f, 2f, 1.3f, vel);
                _fx.Flash(e.Position + n * 0.5f, Ap(e.Position, R * 3.4f, 0.04f), Emitters.Orange * (5.5f * m), 0.24f, 0.08f, 1.8f, 1.4f, vel);
                _fx.Flare(e.Position + n * 0.5f, Ap(e.Position, R * 3.4f, 0.06f), Ap(e.Position, R * 0.22f, 0.004f), Emitters.WarmWhite * (15f * m), 0.13f);
                _fx.Fireball(e.Position + n * R * 0.3f, vel + n * 10f, R * 0.6f, R * 1.6f, 0.4f + 0.16f * big, 2.0f, 4.5f * m, 0f, 1.2f, 0.5f);
                _fx.Sparks(e.Position + n * 0.3f, vel, n, 78f, _c.N(Mathf.RoundToInt(26 * m * (1f + big))), 22f, 150f + 90f * big, 0.3f, 1.0f + 0.5f * big, 0.11f + 0.04f * big, SparkStyle.Hot, 0.4f);
                _fx.Sparks(e.Position + n * 0.3f, vel, n, 40f, _c.N(Mathf.RoundToInt(6 * m)), 80f, 260f, 0.12f, 0.3f, 0.1f, SparkStyle.Metal, 0.3f);
                if (s.effects.debris)
                    _fx.Embers(e.Position + n * 0.5f, vel, R * 0.3f, _c.N(Mathf.RoundToInt(3 + 3 * m)), 14f + 30f * big, 0.9f, 2.2f, 0.45f + 0.5f * big);
                var look = _c.LookOf(e.Entity);
                if (s.effects.debris && big > 0.3f && look != null && look.HullMat != null)
                    _c.Debris.Shards(e.Position + n * 0.5f, vel, 2 + Mathf.RoundToInt(2f * m), 0.3f, 1.4f, 25f, 90f, look.HullMat, 0.9f, 12f, n);
                if (look != null && look.Damage != null)
                {
                    // A modelled ship paints its own scorch, crater and streak decals on the plating, and opens its damage sites.
                    float sev = Mathf.Clamp01(0.3f + e.Value / 70f);
                    look.Damage.HullHit(e.Position, n, 1.2f + 1.6f * sev + 2f * big, sev);
                }
                else _fx.Scorch(e.Position + n * 0.15f, R * (0.75f + 0.25f * big), big < 0.3f ? 1.0f : 26f + 10f * big, 1f, vel);   // a small ship turns and flies away from its mark, so the mark is brief
                if (ship != null) Smoulder(e, ship, n, big);
            }
            if (s.effects.lights) _c.Lights.Flash(e.Position + n * (R + 2f), new Color(1f, 0.66f, 0.34f), 6e7f * m * (1f + 2f * big) * _c.Bright, 140f + 260f * big, 0.14f + 0.08f * big, 2f);

            _c.Audio.Play(toPlayer ? JuiceCue.HitPlayerHull : JuiceCue.HitHull, e.Position, Mathf.Clamp(0.6f * m, 0.45f, 1f), dist);
            if (toPlayer)
            {
                _c.Trauma.Add(0.40f * m);
                _c.Rumble.Pulse(0.75f * m, 0.35f, 0.22f);
                EdgeFlash(new Vector3(1f, 0.46f, 0.12f) * (1.0f * m), 0.28f);
                CockpitSparks(_c.Rng.Int(7, 15), false);
            }
            else _feel.Impact(e.Position, 0.07f * m * (1f + big), 220f, 0.08f, 0.12f, 0.1f);
        }

        // ------------------------------------------------------------------------------------------------ near miss

        public void OnNearMiss(in GameEvent e)
        {
            float miss = e.Value > 0f ? e.Value : 20f;
            float k = Mathf.Clamp01(1f - miss / 60f);
            if (k <= 0f) return;
            var look = WeaponLooks.Get(e.Subject, 1);
            if (_c.Settings.effects.bolts)
            {
                _fx.Flare(e.Position, 5f + 10f * k, 0.5f + 0.7f * k, look.Colour * (6f * k), 0.12f);
                _fx.Flash(e.Position, 3f + 6f * k, look.Colour * (4f * k), 0.10f, 0.4f, 1.5f, 1.3f);
            }
            _c.Audio.Play(JuiceCue.NearMiss, e.Position, 0.5f + 0.5f * k, miss);
            _c.Trauma.Add(0.10f + 0.10f * k);
            _c.Rumble.Pulse(0.15f * k, 0.50f * k + 0.2f, 0.14f);
        }

        // ------------------------------------------------------------------------------------------------ flak

        public void OnFlakBurst(in GameEvent e)
        {
            var s = _c.Settings;
            float R = (e.Value > 1f ? e.Value : 26f) * 1.35f * _c.Size;
            float dist = _c.DistanceToCamera(e.Position);
            _projectiles.Consume(e.Position, e.Entity);
            if (s.effects.flak)
            {
                _fx.Flash(e.Position, R * 0.8f, new Vector3(1f, 0.92f, 0.75f) * 7f, 0.09f, 0.95f, 2f, 1.3f);
                _fx.Flash(e.Position, R * 3.2f, Emitters.Orange * 1.3f, 0.26f, 0.0f, 1.8f, 1.4f);
                _fx.Flare(e.Position, R * 2.4f, R * 0.12f, Emitters.WarmWhite * 5f, 0.09f);
                _fx.Fireball(e.Position, Vector3.zero, R * 0.55f, R * 1.35f, 0.55f, 2.0f, 4.5f, 0f, 1.2f, 0.5f);
                _fx.Fireball(e.Position + _c.Rng.OnSphere() * R * 0.35f, Vector3.zero, R * 0.4f, R * 1.0f, 0.48f, 1.9f, 4.0f, 0f, 1.2f, 0.5f);
                // Shrapnel: a fast, bright, short-lived shell of streaks.
                _fx.SparkBurst(e.Position, Vector3.zero, _c.N(22), 70f, 260f, 0.16f, 0.5f, 0.1f, SparkStyle.Metal, 0.35f, 1.1f);
            }
            if (s.effects.lights) _c.Lights.Flash(e.Position, new Color(1f, 0.75f, 0.45f), 3e8f * _c.Bright, 420f, 0.2f, 2f);
            _c.Audio.Play(JuiceCue.FlakBurst, e.Position, 0.8f, dist);
            _feel.Impact(e.Position, 0.18f, 130f, 0.25f, 0.30f, 0.12f);
            if (dist < 70f) EdgeFlash(new Vector3(1f, 0.7f, 0.4f) * 0.9f, 0.14f);
        }

        // ------------------------------------------------------------------------------------------------ the player's own screen

        /// <summary>A flash round the edge of the picture (the middle stays clear: nothing hides the target or the HUD).</summary>
        public void EdgeFlash(Vector3 colour, float seconds)
        {
            if (!_c.Settings.effects.screenFlash) return;
            _edgeColour = colour; _edgeLife = seconds; _edgeTotal = seconds;
        }

        Vector3 _edgeColour; float _edgeLife, _edgeTotal;

        public void Update(float dt)
        {
            // Shield rims and ripple ages.
            foreach (var look in _c.Looks.Values)
            {
                var sh = look.Shield;
                if (sh == null) continue;
                look.ShieldRim = Mathf.Max(0f, look.ShieldRim - dt * 1.4f);
                sh.Rim = look.ShieldRim;
                for (int i = 0; i < sh.Hits.Length; i++)
                {
                    if (sh.Hits[i].w < 0f) continue;
                    sh.Hits[i].w += dt;
                    if (sh.Hits[i].w > 2.2f) sh.Hits[i].w = -1f;
                }
            }
            if (_edgeLife > 0f)
            {
                _edgeLife -= dt;
                float k = Mathf.Clamp01(_edgeLife / _edgeTotal);
                _c.Frame.EdgeColour = _edgeColour * (k * k * _c.Bright);
                _c.Frame.EdgeParams = new Vector4(0.70f, 1.05f, 0.45f, _c.Rng.F() * 10f);
            }
            else _c.Frame.EdgeColour = Vector3.zero;
        }

        /// <summary>Hot specks flying past the cockpit (a hit felt from the seat).</summary>
        void CockpitSparks(int count, bool plasma)
        {
            if (!_c.Settings.effects.hullHits && !_c.Settings.effects.shieldRipples) return;
            var cam = _c.Camera.transform;
            count = _c.N(count * 3);
            for (int i = 0; i < count; i++)
            {
                // Specks fly past at the edges of the view, thin and quick: the middle of the picture (target and HUD) stays clear.
                float side = _c.Rng.Chance(0.5f) ? -1f : 1f, vert = _c.Rng.Chance(0.5f) ? -1f : 1f;
                Vector3 p = cam.position + cam.forward * _c.Rng.Range(10f, 34f) + cam.right * (side * _c.Rng.Range(9f, 18f)) + cam.up * (vert * _c.Rng.Range(1f, 9f));
                Vector3 d = (cam.right * side * 0.7f + cam.up * vert * 0.3f - cam.forward * 0.4f + _c.Rng.OnSphere() * 0.35f).normalized;
                _fx.Sparks(p, Vector3.zero, d, 25f, 1, 25f, 85f, 0.25f, 0.6f, 0.035f, plasma ? SparkStyle.Plasma : SparkStyle.Metal, 0.9f, 0.8f);
            }
        }

        // ------------------------------------------------------------------------------------------------ ship looks

        /// <summary>The look of a ship, made from its published radius when nothing registered one.</summary>
        public ShipLook EnsureLook(int ship)
        {
            if (ship == GameWorld.NoShip) return null;
            if (_c.Looks.TryGetValue(ship, out var look)) return look;
            var st = _c.World.Ships.Get(ship);
            if (st == null) return null;
            look = new ShipLook { Id = ship, Radius = st.Radius, SizeClass = JuiceContext.SizeClassFromRadius(st.Radius) };
            look.HalfExtents = new Vector3(0.7f, 0.4f, 0.9f) * st.Radius;
            look.ShieldRadii = new Vector3(1.0f, 0.62f, 1.18f) * st.Radius;
            _c.Looks[ship] = look;
            AttachShield(look);
            return look;
        }

        public void AttachShield(ShipLook look)
        {
            if (look.ShieldRadii.sqrMagnitude < 1e-4f) return;
            var d = new ShieldDraw { Radii = look.ShieldRadii, Colour = ShieldBlue * 2.3f };
            for (int i = 0; i < d.Hits.Length; i++) d.Hits[i] = new Vector4(0, 0, 1, -1f);
            look.Shield = d;
        }

        /// <summary>Each frame: put every shield bubble on its ship and hand the drawable ones to the pass.</summary>
        public void Draw()
        {
            var shields = _c.Frame.Shields;
            shields.Clear();
            if (!_c.Settings.effects.shieldRipples) return;
            foreach (var look in _c.Looks.Values)
            {
                var sh = look.Shield;
                if (sh == null) continue;
                bool any = look.ShieldRim > 0.01f;
                for (int i = 0; i < sh.Hits.Length && !any; i++) any = sh.Hits[i].w >= 0f;
                if (!any) continue;
                if (!_c.Pose(look.Id, out var pos, out var rot)) continue;
                sh.Position = pos + rot * look.LocalCentre;
                sh.Rotation = rot;
                sh.Colour = ShieldBlue * (2.3f * _c.Bright);
                shields.Add(sh);
            }
        }
    }
}
