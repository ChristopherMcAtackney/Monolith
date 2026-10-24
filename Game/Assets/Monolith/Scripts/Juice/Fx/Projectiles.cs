using System.Collections.Generic;
using Monolith.Core;
using UnityEngine;

namespace Monolith.Juice
{
    public enum ProjectileKind { Bolt, Missile }

    /// <summary>How one weapon looks and sounds in flight. Numbers from the design appendix (speeds 450 to 750 m/s for fighter guns, 80 to 350 for turrets).</summary>
    public sealed class WeaponLook
    {
        public string Id;
        public ProjectileKind Kind = ProjectileKind.Bolt;
        /// <summary>Body colour in display units (1 = the brightest an 8-bit picture shows before tonemapping), and the white-hot core strength.</summary>
        public Vector3 Colour = new Vector3(0.22f, 1f, 0.3f) * 3.2f;
        public float Core = 9f;
        public float Speed = 620f, Life = 2.4f, Length = 24f, HalfWidth = 0.5f;
        /// <summary>How sharply the bolt brightens toward its head (0..), and the brightness of its tail (0..1).</summary>
        public float TailSharp = 0.15f, TailBright = 0.5f;
        /// <summary>Head glow radius in half widths, and its brightness.</summary>
        public float HeadGlow = 2.2f, HeadStrength = 0.8f;
        public float MuzzleSize = 2.6f;
        public JuiceCue Cue = JuiceCue.LaserLight;
        /// <summary>Missiles only: turn rate rad/s, acceleration time s.</summary>
        public float TurnRate = 1.7f, AccelTime = 0.9f;
    }

    /// <summary>The weapon table. Ids come from whatever raises <c>weapon_fired</c>; unknown ids fall back by prefix and team.</summary>
    public static class WeaponLooks
    {
        static readonly Dictionary<string, WeaponLook> Table = new Dictionary<string, WeaponLook>();

        static WeaponLooks()
        {
            var green = new Vector3(0.20f, 1.00f, 0.28f);
            var red = new Vector3(1.00f, 0.15f, 0.09f);
            var teal = new Vector3(0.10f, 1.00f, 0.60f);
            var hotRed = new Vector3(1.00f, 0.24f, 0.06f);
            var amber = new Vector3(1.00f, 0.58f, 0.18f);

            Add(new WeaponLook { Id = "laser_player", Colour = green * 3.4f, Core = 14f, Speed = 620f, Life = 2.6f, Length = 90f, HalfWidth = 0.9f, TailSharp = 1.0f, TailBright = 0.10f, HeadGlow = 3.2f, HeadStrength = 1.1f, MuzzleSize = 2.4f, Cue = JuiceCue.LaserLight });
            Add(new WeaponLook { Id = "laser_enemy", Colour = red * 3.8f, Core = 13f, Speed = 520f, Life = 3.0f, Length = 85f, HalfWidth = 0.9f, TailSharp = 1.0f, TailBright = 0.10f, HeadGlow = 3.2f, HeadStrength = 1.1f, MuzzleSize = 2.6f, Cue = JuiceCue.LaserEnemy });
            Add(new WeaponLook { Id = "laser_heavy", Colour = teal * 4.2f, Core = 16f, Speed = 340f, Life = 9f, Length = 230f, HalfWidth = 4.2f, TailSharp = 0.9f, TailBright = 0.12f, HeadGlow = 3.0f, HeadStrength = 1.1f, MuzzleSize = 11f, Cue = JuiceCue.LaserHeavy });
            Add(new WeaponLook { Id = "laser_heavy_enemy", Colour = hotRed * 4.6f, Core = 15f, Speed = 300f, Life = 9f, Length = 240f, HalfWidth = 4.4f, TailSharp = 0.9f, TailBright = 0.12f, HeadGlow = 3.0f, HeadStrength = 1.1f, MuzzleSize = 12f, Cue = JuiceCue.LaserHeavy });
            Add(new WeaponLook { Id = "hammer", Colour = green * 3.9f, Core = 12f, Speed = 650f, Life = 2.0f, Length = 62f, HalfWidth = 2.1f, TailBright = 0.55f, HeadGlow = 2.0f, MuzzleSize = 4.6f, Cue = JuiceCue.LaserHeavy });
            Add(new WeaponLook { Id = "hammer_enemy", Colour = red * 4.2f, Core = 11f, Speed = 600f, Life = 2.0f, Length = 62f, HalfWidth = 2.1f, TailBright = 0.55f, HeadGlow = 2.0f, MuzzleSize = 4.8f, Cue = JuiceCue.LaserHeavy });
            Add(new WeaponLook { Id = "flak_shell", Colour = amber * 3.4f, Core = 7f, Speed = 720f, Life = 3.2f, Length = 22f, HalfWidth = 0.9f, MuzzleSize = 3.5f, Cue = JuiceCue.FlakShell });
            Add(new WeaponLook { Id = "missile_heat", Kind = ProjectileKind.Missile, Colour = amber * 3.2f, Core = 8f, Speed = 190f, Life = 11f, Length = 9f, HalfWidth = 0.55f, MuzzleSize = 4f, Cue = JuiceCue.MissileLaunch, TurnRate = 1.5f, AccelTime = 0.7f });
            Add(new WeaponLook { Id = "missile_lock", Kind = ProjectileKind.Missile, Colour = new Vector3(0.45f, 0.75f, 1f) * 3.4f, Core = 8f, Speed = 250f, Life = 12f, Length = 9f, HalfWidth = 0.55f, MuzzleSize = 4f, Cue = JuiceCue.MissileLaunch, TurnRate = 2.2f, AccelTime = 0.5f });
            Add(new WeaponLook { Id = "torpedo", Kind = ProjectileKind.Missile, Colour = hotRed * 3.4f, Core = 9f, Speed = 80f, Life = 28f, Length = 14f, HalfWidth = 1.1f, MuzzleSize = 9f, Cue = JuiceCue.MissileLaunch, TurnRate = 0.45f, AccelTime = 2.2f });
        }

        static void Add(WeaponLook l) => Table[l.Id] = l;

        public static WeaponLook Get(string id, int team)
        {
            if (id == "hammer" && team != 0) return Table["hammer_enemy"];
            if (!string.IsNullOrEmpty(id) && Table.TryGetValue(id, out var l)) return l;
            id = id ?? "";
            if (id.StartsWith("hammer")) return team == 0 ? Table["hammer"] : Table["hammer_enemy"];
            if (id.StartsWith("flak")) return Table["flak_shell"];
            if (id.StartsWith("torpedo") || id.StartsWith("bomb")) return Table["torpedo"];
            if (id.StartsWith("missile")) return id.Contains("lock") ? Table["missile_lock"] : Table["missile_heat"];
            bool heavy = id.Contains("heavy") || id.Contains("turret");
            if (heavy) return team == 0 ? Table["laser_heavy"] : Table["laser_heavy_enemy"];
            return team == 0 ? Table["laser_player"] : Table["laser_enemy"];
        }
    }

    /// <summary>
    /// Laser bolts, flak shells and missiles in flight: visual only (a weapons system does the damage and the hit tests and raises
    /// hit events). A bolt is a bright streak with a hot head, placed analytically from where and when it was fired, so it
    /// is smooth at any frame rate; an impact event close to one consumes it. A missile steers on its target and leaves a glowing trail.
    /// </summary>
    public sealed class Projectiles
    {
        sealed class Proj
        {
            public WeaponLook Look;
            public Vector3 Origin, Dir;
            public float Speed, T0, Seed;
            public int Shooter, Target;
            public bool Player;
            // Missiles.
            public Vector3 Pos, Vel;
            public float Age;
            public bool Dead;
            // Missiles flown by the weapons system: it raises missile_track every tick with the true position and velocity.
            public bool Tracked;
            public int TrackId, LastTrackTick;
            public Vector3 TrackPos, TrackVel;
        }

        readonly JuiceContext _c;
        readonly List<Proj> _list = new List<Proj>(256);
        readonly Dictionary<int, Proj> _tracked = new Dictionary<int, Proj>();
        int _seq;

        public int Count => _list.Count;

        public Projectiles(JuiceContext c) { _c = c; }

        public void OnFired(in GameEvent e)
        {
            var s = _c.Settings;
            var shooter = _c.World.Ships.Get(e.Entity);
            int team = shooter != null ? shooter.Team : 0;
            var look = WeaponLooks.Get(e.Subject, team);
            Vector3 dir = e.Direction.sqrMagnitude > 1e-6f ? e.Direction.normalized : Vector3.forward;
            bool player = _c.IsPlayer(e.Entity);
            float dist = _c.DistanceToCamera(e.Position);

            if (s.effects.bolts && _list.Count < 700)
            {
                var p = new Proj
                {
                    Look = look, Origin = e.Position, Dir = dir, Speed = e.Value > 1f ? e.Value : look.Speed,
                    T0 = (e.Tick + 1) * Sim.Dt, Seed = (++_seq) * 0.173f % 1f, Shooter = e.Entity, Target = e.Other, Player = player,
                    Pos = e.Position, Vel = dir * (e.Value > 1f ? e.Value : look.Speed * 0.35f),
                };
                _list.Add(p);
            }

            LaunchEffects(e.Position, dir, look, player, dist);
        }

        /// <summary>The bang of a shot leaving: muzzle flash, launch sound, and the pad and shake when the player fired.</summary>
        void LaunchEffects(Vector3 position, Vector3 dir, WeaponLook look, bool player, float dist)
        {
            if (_c.Settings.effects.muzzleFlash) MuzzleFlash(position, dir, look, player);

            float gain = player ? 0.7f : Mathf.Lerp(0.6f, 0.12f, Mathf.Clamp01(dist / 2500f));
            _c.Audio.Play(look.Cue, position, gain, dist);
            if (player)
            {
                if (look.Kind == ProjectileKind.Bolt) _c.Rumble.Pulse(0.05f, 0.30f, 0.085f);
                else { _c.Rumble.Pulse(0.55f, 0.35f, 0.25f); _c.Trauma.Add(0.10f); }
            }
        }

        /// <summary>
        /// A missile the weapons system is flying reports where it is (every tick): the first report is its launch, the rest move it, and
        /// when the reports stop it is gone. A subject containing "decoy" is drawn as a slow hot streak without a launch bang.
        /// </summary>
        public void OnTrack(in GameEvent e)
        {
            int id = Mathf.RoundToInt(e.Value);
            if (!_tracked.TryGetValue(id, out var p) || p.Dead)
            {
                if (!_c.Settings.effects.bolts || _list.Count >= 700) return;
                var shooter = _c.World.Ships.Get(e.Entity);
                int team = shooter != null ? shooter.Team : 0;
                var look = WeaponLooks.Get(e.Subject, team);
                Vector3 v = e.Direction;
                Vector3 dir = v.sqrMagnitude > 1e-4f ? v.normalized : Vector3.forward;
                bool player = _c.IsPlayer(e.Entity);
                p = new Proj
                {
                    Look = look, Origin = e.Position, Dir = dir, Speed = v.magnitude, T0 = (e.Tick + 1) * Sim.Dt, Seed = (++_seq) * 0.173f % 1f,
                    Shooter = e.Entity, Target = e.Other, Player = player, Pos = e.Position, Vel = v, Tracked = true, TrackId = id,
                };
                _list.Add(p);
                _tracked[id] = p;
                bool decoy = e.Subject != null && e.Subject.Contains("decoy");
                if (!decoy) LaunchEffects(e.Position, dir, look, player, _c.DistanceToCamera(e.Position));
            }
            p.TrackPos = e.Position; p.TrackVel = e.Direction; p.LastTrackTick = e.Tick;
        }

        void MuzzleFlash(Vector3 pos, Vector3 dir, WeaponLook look, bool player)
        {
            float size = look.MuzzleSize * (player ? 0.42f : 1f) * _c.Size;
            float life = player ? 0.05f : 0.08f;
            var rgb = look.Colour * 2.2f;
            ref var g = ref _c.Particles.Add();
            g.Kind = FxKind.Glow; g.Pos = pos + dir * (size * 0.3f); g.Life = life; g.W0 = size * 0.55f; g.W1 = size * 0.8f; g.P1a = g.P1b = 0.75f;
            g.C0 = g.C1 = g.C2 = new Vector4(rgb.x, rgb.y, rgb.z, 0f) * 1.8f; g.FadeExp = 1.2f; g.NearStart = 4f; g.NearRange = 10f;
            ref var f = ref _c.Particles.Add();
            f.Kind = FxKind.Flare; f.Pos = pos + dir * (size * 0.4f); f.Life = life * 1.3f; f.W0 = size * 1.5f; f.W1 = size * 2.1f; f.H0 = f.H1 = size * 0.5f;
            f.C0 = f.C1 = f.C2 = new Vector4(rgb.x, rgb.y, rgb.z, 0f); f.FadeExp = 1.5f; f.NearStart = 4f; f.NearRange = 10f;
            // A short hot lance along the barrel direction.
            ref var l = ref _c.Particles.Add();
            l.Kind = FxKind.Streak; l.Pos = pos + dir * (size * 1.2f); l.Dir = dir; l.Life = life; l.MinLen = size * 2.2f; l.W0 = l.W1 = look.HalfWidth * 0.9f;
            l.C0 = l.C1 = l.C2 = new Vector4(look.Colour.x, look.Colour.y, look.Colour.z, look.Core * 0.8f); l.FadeExp = 1f; l.NearStart = 4f; l.NearRange = 10f;
            l.P1a = l.P1b = 0.5f; l.P2a = l.P2b = 0.3f;
        }

        /// <summary>An impact happened here: the bolt (or missile) that made it is done.</summary>
        public void Consume(Vector3 point, int victim, float speedHint = 0f)
        {
            int best = -1; float bestScore = float.MaxValue;
            float now = _c.RenderSimTime;
            for (int i = 0; i < _list.Count; i++)
            {
                var p = _list[i];
                if (p.Dead) continue;
                if (p.Look.Kind == ProjectileKind.Missile)
                {
                    float d = Vector3.Distance(point, p.Pos);
                    if (d < 45f && d < bestScore) { bestScore = d; best = i; }
                    continue;
                }
                Vector3 head = Head(p, now);
                Vector3 line = point - p.Origin;
                float along = Vector3.Dot(line, p.Dir);
                float perp = (line - p.Dir * along).magnitude;
                if (perp > 6f + p.Look.HalfWidth * 2f) continue;
                float headAlong = Vector3.Dot(head - p.Origin, p.Dir);
                float ahead = Mathf.Abs(along - headAlong);
                if (ahead > Mathf.Max(30f, p.Speed * 0.09f)) continue;
                float score = perp + ahead * 0.2f;
                if (score < bestScore) { bestScore = score; best = i; }
            }
            if (best >= 0)
            {
                var p = _list[best];
                if (p.Look.Kind == ProjectileKind.Missile) { p.Dead = true; }
                else { _list.RemoveAt(best); }
            }
        }

        Vector3 Head(Proj p, float now)
        {
            if (p.Look.Kind == ProjectileKind.Missile) return p.Pos;
            float age = Mathf.Max(0f, now - p.T0);
            return p.Origin + p.Dir * (p.Speed * age);
        }

        public void Update(float dt)
        {
            float now = _c.RenderSimTime;
            for (int i = _list.Count - 1; i >= 0; i--)
            {
                var p = _list[i];
                if (p.Look.Kind == ProjectileKind.Missile)
                {
                    StepMissile(p, dt);
                    if (p.Dead) { if (p.Tracked) _tracked.Remove(p.TrackId); _list.RemoveAt(i); }
                    continue;
                }
                if (now - p.T0 > p.Look.Life) _list.RemoveAt(i);
            }
        }

        void StepMissile(Proj p, float dt)
        {
            Vector3 trackedFrom = default;
            if (p.Tracked && !p.Dead)
            {
                // The weapons system flies it; draw it where the simulation puts it at this moment between ticks (as ships are drawn).
                if (_c.World.Tick - p.LastTrackTick > 3) { p.Dead = true; return; }
                trackedFrom = p.Pos;
                p.Pos = p.TrackPos - p.TrackVel * ((1f - _c.World.Alpha) * Sim.Dt);
                p.Vel = p.TrackVel;
                p.Age += dt;
            }
            if (!p.Dead)
            {
                if (!p.Tracked) p.Age += dt;
                // Accelerate along the nose, steer toward the target.
                var look = p.Look;
                Vector3 fwd = p.Vel.sqrMagnitude > 1f ? p.Vel.normalized : p.Dir;
                if (!p.Tracked)
                {
                    var target = _c.World.Ships.Get(p.Target);
                    if (target != null)
                    {
                        Vector3 to = target.Position - p.Pos;
                        float turn = look.TurnRate * dt;
                        fwd = Vector3.RotateTowards(fwd, to.normalized, turn, 0f);
                    }
                    float speed = Mathf.Lerp(p.Vel.magnitude, p.Speed, 1f - Mathf.Exp(-dt / Mathf.Max(0.05f, look.AccelTime)));
                    p.Vel = fwd * speed;
                    p.Pos += p.Vel * dt;
                    if (p.Age > look.Life) p.Dead = true;
                }

                // Exhaust: hot glows laid along the path flown this frame (no gaps at any speed) that drift back and cool, plus a few fire puffs.
                if (_c.Settings.effects.missileTrails)
                {
                    Vector3 from = p.Tracked ? trackedFrom : p.Pos - p.Vel * dt;
                    float stepLen = Mathf.Max(1.2f, look.HalfWidth * 2.2f);
                    int n = Mathf.Clamp(Mathf.CeilToInt(p.Vel.magnitude * dt / stepLen), 1, 12);
                    for (int k = 0; k < n; k++)
                    {
                        float t = (k + 0.5f) / n;
                        Vector3 at = Vector3.Lerp(from, p.Pos, t) - fwd * (look.Length * 0.45f);
                        ref var g = ref _c.Particles.Add();
                        g.Kind = FxKind.Glow; g.Pos = at; g.Vel = -fwd * _c.Rng.Range(4f, 14f) + _c.Rng.OnSphere() * 1.5f; g.Drag = 1.2f;
                        g.Life = _c.Rng.Range(0.9f, 1.5f); g.W0 = look.HalfWidth * 2.4f; g.W1 = look.HalfWidth * 6.5f; g.P1a = g.P1b = 0.35f;
                        var c0 = look.Colour * 2.1f;
                        g.C0 = new Vector4(c0.x, c0.y, c0.z, 0f); g.C1 = new Vector4(look.Colour.x, look.Colour.y * 0.7f, look.Colour.z * 0.5f, 0f); g.C2 = new Vector4(0.25f, 0.04f, 0f, 0f) * 0.4f;
                        g.FadeExp = 1.4f; g.NearStart = 6f; g.NearRange = 16f; g.Seed = _c.Rng.F();
                    }
                    if (_c.Rng.Chance(0.55f))
                    {
                        ref var b = ref _c.Particles.Add();
                        b.Kind = FxKind.Fire; b.Pos = p.Pos - fwd * (look.Length * 0.5f) + _c.Rng.OnSphere() * look.HalfWidth * 0.6f;
                        b.Vel = -fwd * _c.Rng.Range(10f, 30f) + _c.Rng.OnSphere() * 4f;
                        b.Life = _c.Rng.Range(0.35f, 0.6f); b.W0 = look.HalfWidth * 2.2f; b.W1 = look.HalfWidth * 4.2f; b.Seed = _c.Rng.F();
                        b.P1a = 1.0f; b.P1b = 1.0f; b.P2a = 0.25f; b.P2b = 0.25f; b.Palette = 0f;
                        var c = look.Colour * 1.2f;
                        b.C0 = b.C1 = b.C2 = new Vector4(c.x, c.y, c.z, 0f); b.FadeExp = 0.8f; b.Soft = 3f; b.NearStart = 6f; b.NearRange = 16f;
                    }
                }
            }
        }

        public void Draw()
        {
            var add = _c.Add;
            float now = _c.RenderSimTime;
            var cam = _c.CameraPosition;
            float bright = _c.Bright;
            float wScale = _c.Settings.levels.boltWidth;
            bool trails = _c.Settings.effects.missileTrails;

            for (int i = 0; i < _list.Count; i++)
            {
                var p = _list[i];
                var look = p.Look;
                if (look.Kind == ProjectileKind.Bolt)
                {
                    float age = Mathf.Max(0f, now - p.T0);
                    float travelled = p.Speed * age;
                    Vector3 head = p.Origin + p.Dir * travelled;
                    if ((head - cam).sqrMagnitude > 30000f * 30000f) continue;
                    float len = Mathf.Min(look.Length, travelled + look.Length * 0.12f);
                    float hw = look.HalfWidth * wScale;
                    var col = look.Colour * bright;
                    add.Quad(head - p.Dir * (len * 0.5f), new Vector4(col.x, col.y, col.z, look.Core * bright), FxKind.Streak, p.Seed,
                             new Vector4(p.Dir.x, p.Dir.y, p.Dir.z, len * 0.5f), hw, 0f, look.TailSharp, look.TailBright, 0f, 18f, 55f);
                    var hc = look.Colour * (bright * look.HeadStrength);
                    add.Quad(head, new Vector4(hc.x, hc.y, hc.z, 0f), FxKind.Glow, p.Seed, Vector4.zero, hw * look.HeadGlow, 0f, 0.55f, 0f, 0f, 18f, 55f);
                }
                else
                {
                    DrawMissile(p, trails, bright);
                }
            }
        }

        void DrawMissile(Proj p, bool trails, float bright)
        {
            var add = _c.Add;
            var look = p.Look;
            float hw = look.HalfWidth * _c.Settings.levels.boltWidth;
            if (!p.Dead)
            {
                var hc = look.Colour * (bright * 2.6f);
                add.Quad(p.Pos, new Vector4(hc.x, hc.y, hc.z, 0f), FxKind.Glow, 0.5f, Vector4.zero, hw * 7f, 0f, 0.7f, 0f);
                Vector3 dir = p.Vel.sqrMagnitude > 1f ? p.Vel.normalized : p.Dir;
                var cc = look.Colour * bright;
                add.Quad(p.Pos - dir * (look.Length * 0.5f), new Vector4(cc.x, cc.y, cc.z, look.Core * bright), FxKind.Streak, 0.5f,
                         new Vector4(dir.x, dir.y, dir.z, look.Length * 0.5f), hw, 0f, 1.5f, 0.2f);
                add.Quad(p.Pos - dir * look.Length * 0.8f, new Vector4(hc.x, hc.y, hc.z, 0f), FxKind.Flare, 0.5f, new Vector4(hw * 2f, 0, 0, 0), hw * 18f, 0f, 0f, 0f);
            }
        }
    }
}
