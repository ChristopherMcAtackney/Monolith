using System;
using System.Collections.Generic;
using Monolith.Core;
using UnityEngine;

namespace Monolith.Juice
{
    /// <summary>
    /// The player's death, first person throughout (user: "terrifying death sequences where the ship geometry rips apart and you are physically flung into
    /// space before dying"; design sections 6 and 8: never an external camera). It plays whenever the player's ship is destroyed, seated or standing, and runs
    /// about 5.9 s from the hit to the end:
    /// <list type="number">
    /// <item><b>The hit</b> (0 s): the canopy cracks in a web of white lines from the point of impact, a white flash, the cabin you never see from the seat
    /// appears and begins to tear: panels and ribs peel away (the real cabin model, cut into pieces), sparks, venting gas, red emergency light flickering and failing.</item>
    /// <item><b>The breach</b> (about 0.5 s): the canopy blows out. The air goes, loose pieces and fog stream past you toward the hole, and you are flung through it
    /// at about 30 m/s, tumbling. The pad peaks here and then fades.</item>
    /// <item><b>The tumble</b>: your own view, spinning in vacuum: the stars wheel, the battle goes on, and behind you your ship breaks up and burns in staged blasts
    /// with torn plates trailing fire.</item>
    /// <item><b>The dark</b> (from 3.6 s): your breathing, your heart, the view closing in on a red-edged iris, the sound collapsing, the last beat, then nothing.</item>
    /// </list>
    /// Then <see cref="DeathBoard.Finished"/> is set and the scene shows the ordinary failure flow over a black screen. A or Start skips it after
    /// <see cref="SkippableAt"/> seconds (<see cref="TrySkip"/>). The camera is <see cref="GameWorld.Death"/>'s (<c>CockpitView</c> reads it); the HUD and the
    /// cabin rig stand down while <see cref="DeathBoard.Active"/>. The named events the audio can hang on are in <see cref="DeathEvents"/>; the juice makes its
    /// own sounds through <see cref="IJuiceAudio"/>. By cause (gunfire, blast, beam, collision) the timing and force differ a little.
    /// </summary>
    public sealed class DeathFx
    {
        public const string CabinPrefabPath = "Cabin/cabin";
        public const float Length = 5.9f, SkippableAt = 1.4f;
        const float IrisStart = 3.0f, IrisEnd = 5.0f, BlackFrom = 4.6f, BlackDone = 5.4f;

        struct Kind { public string Name; public float Blow, Crack, OutSpeed, Tumble, Fire, Shove, Flash; }

        static Kind KindOf(string cause)
        {
            switch (cause)
            {
                case "collision": return new Kind { Name = cause, Blow = 0.12f, Crack = 0.10f, OutSpeed = 32f, Tumble = 3.4f, Fire = 0.9f, Shove = 11f, Flash = 1.1f };
                case "blast": return new Kind { Name = cause, Blow = 0.30f, Crack = 0.26f, OutSpeed = 36f, Tumble = 3.8f, Fire = 1.35f, Shove = 7f, Flash = 1.5f };
                case "beam": return new Kind { Name = cause, Blow = 0.70f, Crack = 0.66f, OutSpeed = 24f, Tumble = 2.6f, Fire = 1.2f, Shove = 2f, Flash = 1.7f };
                default: return new Kind { Name = "gunfire", Blow = 0.55f, Crack = 0.46f, OutSpeed = 28f, Tumble = 3.0f, Fire = 1.0f, Shove = 4f, Flash = 1.0f };
            }
        }

        sealed class Part
        {
            public DebrisPiece P;
            public Vector3 Home;
            public Quaternion HomeRot;
            public float Release, Radius;
            public bool Released, Glass;
        }

        readonly JuiceContext _c;
        readonly List<Part> _parts = new List<Part>();
        readonly List<IDisposable> _subs = new List<IDisposable>();
        bool _prepared;
        int _framesToPrepare = 90;
        Vector3 _seatEyeLocal = new Vector3(0f, 0.9f, 0.6f), _glassLocal = new Vector3(0f, 1.2f, 2.2f);
        Material _hullMat;

        /// <summary>Where the seated eye is in the ship's own frame (view.json camera.Eye): the cabin model is placed so its seat is there. Set by the scene.</summary>
        public Vector3 SeatedEyeShip = new Vector3(0f, 0.5f, 1.5f);

        // The last thing that hit the player, to tell the cause and where it came from.
        string _lastWeapon; float _lastHitAt = -99f, _lastCollisionAt = -99f; Vector3 _lastHitPoint, _lastHitDir;

        // The sequence.
        bool _active, _skip;
        float _t, _skipAt;
        Kind _k;
        Vector3 _rootPos, _eye0, _v0, _shipPos, _fwd, _dirOut, _impactLocal, _pos, _vel, _w;
        Quaternion _rootRot, _rot;
        Vector2 _impactUv;
        float _seed;
        bool _blown, _screamed, _breathed, _tumbleSaid, _stopped, _silenced, _w1, _w2, _w3, _duckedLate;
        float _nextBeat, _nextSpark, _nextFlicker, _fogLeft;
        int _beats;
        string _cause = "gunfire";

        /// <summary>Builds a fresh visual of the player's own ship (the scene knows how), placed at its pose: the death breaks it into burning pieces behind you. Null: only blasts and plates.</summary>
        public Func<Transform> WreckVisual;
        public bool Active => _active;
        public float Time => _t;
        public string Cause => _cause;
        public int Parts => _parts.Count;
        public bool Prepared => _prepared;
        /// <summary>How far the cabin's cutting has come (0..1), for the loading screen's bar.</summary>
        public float PrepareProgress => _prepared ? 1f : _prepRoot == null || _prepKids.Count == 0 ? 0f : Mathf.Clamp01(_prepAt / (float)_prepKids.Count);
        /// <summary>Start cutting the cabin on the next frame instead of after the usual wait (the loading screen does it, so the cutting is done before the mission is shown).</summary>
        public void PrepareSoon() { if (!_prepared) _framesToPrepare = Mathf.Min(_framesToPrepare, 1); }

        public DeathFx(JuiceContext c)
        {
            _c = c;
            _subs.Add(GameEvents.Subscribe(CombatEvents.HitHull, e => NoteHit(e)));
            _subs.Add(GameEvents.Subscribe(CombatEvents.HitShield, e => NoteHit(e)));
            _subs.Add(GameEvents.Subscribe(CombatEvents.Collision, e => { if (_c.IsPlayer(e.Entity) || _c.IsPlayer(e.Other)) _lastCollisionAt = _c.World.Time; }));
            c.PlayerDestroyed = Take;
        }

        public void Dispose() { foreach (var s in _subs) s.Dispose(); _subs.Clear(); }

        void NoteHit(in GameEvent e)
        {
            if (!_c.IsPlayer(e.Entity)) return;
            _lastWeapon = e.Subject; _lastHitAt = _c.World.Time;
            if (e.HasPosition) _lastHitPoint = e.Position;
            _lastHitDir = e.Direction.sqrMagnitude > 0.01f ? e.Direction.normalized : Vector3.zero;
        }

        // ------------------------------------------------------------------------------------------------ the cabin, cut in advance

        /// <summary>
        /// Cut the cabin model into pieces ahead of time, a part of the model a frame, starting a second or two after the scene begins (so the death does not pay
        /// for it: cutting it whole takes 150 ms). The pieces wait switched off at the model's own origin, and are put in place and launched when the ship dies.
        /// </summary>
        void BeginPrepare()
        {
            foreach (var p in _parts) if (p.P != null && !p.P.Dead) _c.Debris.Remove(p.P);
            _parts.Clear();
            _prepared = false;
            var prefab = Resources.Load<GameObject>(CabinPrefabPath);
            if (prefab == null) { MnLog.Warn($"DEATH the cabin model Resources/{CabinPrefabPath}.prefab is missing: the death has no interior to tear"); _prepared = true; return; }
            var go = UnityEngine.Object.Instantiate(prefab);
            go.name = "DeathCabin (cutting)";
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.enabled = false;          // each part is switched on just before it is cut
            foreach (var l in go.GetComponentsInChildren<Light>(true)) l.enabled = false;
            var eye = FindNode(go.transform, "ANCHOR_seat_eye");
            if (eye != null) _seatEyeLocal = go.transform.InverseTransformPoint(eye.position);
            var glass = FindNode(go.transform, "CABIN_GLASS");
            if (glass != null) { var gr = glass.GetComponentInChildren<Renderer>(true); if (gr != null) _glassLocal = go.transform.InverseTransformPoint(gr.bounds.center); }
            _hullMat = Debris.HullMaterial(go.transform);
            _prepKids.Clear(); _prepSeeds.Clear();
            var tris = new List<int>(); int total = 0;
            foreach (Transform ch in go.transform)
            {
                int n = 0;
                foreach (var mf in ch.GetComponentsInChildren<MeshFilter>(true))
                    if (mf.sharedMesh != null) for (int sm = 0; sm < mf.sharedMesh.subMeshCount; sm++) n += (int)(mf.sharedMesh.GetIndexCount(sm) / 3);
                if (n <= 0) continue;
                _prepKids.Add(ch); tris.Add(n); total += n;
            }
            for (int i = 0; i < _prepKids.Count; i++) _prepSeeds.Add(Mathf.Clamp(Mathf.RoundToInt(78f * tris[i] / Mathf.Max(1, total)), 1, 30));
            _prepAt = 0; _prepRoot = go.transform; _prepMs = 0f; _prepTris = 0;
        }

        void StepPrepare()
        {
            if (_prepRoot == null) return;
            if (_prepAt >= _prepKids.Count)
            {
                UnityEngine.Object.Destroy(_prepRoot.gameObject);
                _prepRoot = null; _prepared = true;
                MnLog.Info($"DEATH the cabin is cut into {_parts.Count} pieces ahead of time ({_prepTris} triangles in {_prepKids.Count} parts, {_prepMs:0} ms in all, the worst part {_prepWorst:0} ms), the seat eye at {_seatEyeLocal}, the canopy at {_glassLocal}");
                return;
            }
            var ch = _prepKids[_prepAt]; int count = _prepSeeds[_prepAt]; _prepAt++;
            foreach (var r in ch.GetComponentsInChildren<Renderer>(true)) r.enabled = true;
            var bounds = Debris.LocalBounds(ch);
            var seeds = _c.Debris.MakeSeeds(ch, bounds, count, false);
            var pieces = _c.Debris.Slice(ch, seeds, Vector3.zero, 9);
            _prepMs += Debris.LastSliceMs; _prepTris += Debris.LastSliceTris; _prepWorst = Mathf.Max(_prepWorst, Debris.LastSliceMs);
            foreach (var p in pieces)
            {
                bool glassy = false;
                foreach (var r in p.Rends) if (r != null) foreach (var m in r.sharedMaterials) if (m != null && m.name.IndexOf("glass", StringComparison.OrdinalIgnoreCase) >= 0) glassy = true;
                foreach (var r in p.Rends) if (r != null) r.renderingLayerMask = 3u;
                foreach (var m in p.Meshes) if (m != null) m.UploadMeshData(false);                // up to the GPU now, not in the middle of the death
                p.Life = 1e9f;
                p.Go.SetActive(false);
                _parts.Add(new Part { P = p, Home = p.T.position, HomeRot = p.T.rotation, Radius = p.Radius, Glass = glassy });
            }
        }

        Transform _prepRoot; int _prepAt, _prepTris; float _prepMs, _prepWorst;
        readonly List<Transform> _prepKids = new List<Transform>(); readonly List<int> _prepSeeds = new List<int>();

        /// <summary>The cabin is cut and the pieces are ready (finishes the cutting now if it is still going).</summary>
        void EnsurePrepared()
        {
            if (_prepared && PartsAlive()) return;
            if (_prepRoot == null) BeginPrepare();
            while (_prepRoot != null) StepPrepare();
        }

        static Transform FindNode(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        bool PartsAlive()
        {
            if (_parts.Count == 0) return false;
            foreach (var p in _parts) if (p.P == null || p.P.Dead || p.P.Go == null) return false;
            return true;
        }

        // ------------------------------------------------------------------------------------------------ start

        /// <summary>The juice's claim on the player's own ship_destroyed event (see <see cref="JuiceContext.PlayerDestroyed"/>).</summary>
        bool Take(GameEvent e)
        {
            if (!_c.Settings.death.enabled) return false;
            if (_active) return true;
            Begin(CauseNow(), e.HasPosition ? e.Position : (Vector3?)null);
            return true;
        }

        string CauseNow()
        {
            float now = _c.World.Time;
            if (now - _lastCollisionAt < 0.6f) return "collision";
            if (now - _lastHitAt < 0.8f && !string.IsNullOrEmpty(_lastWeapon))
            {
                string w = _lastWeapon;
                if (w.StartsWith("beam", StringComparison.Ordinal)) return "beam";
                if (w.StartsWith("missile", StringComparison.Ordinal) || w.StartsWith("torpedo", StringComparison.Ordinal) || w.StartsWith("flak", StringComparison.Ordinal)) return "blast";
            }
            return "gunfire";
        }

        /// <summary>Play the sequence now (the sandbox's button, <c>-mnDeathTest</c>): the player's ship is not touched.</summary>
        public void Trigger(string cause = null)
        {
            if (_active) return;
            Begin(string.IsNullOrEmpty(cause) ? "gunfire" : cause, null);
        }

        void Begin(string cause, Vector3? at)
        {
            var w = _c.World;
            var board = w.Death;
            EnsurePrepared();
            _cause = cause; _k = KindOf(cause);
            var ship = w.Ships.Get(w.LocalPlayerShip);
            float alpha = w.Alpha;
            var cam = _c.Camera.transform;
            _rootRot = ship != null ? ship.RenderRotation(alpha) : cam.rotation;
            _shipPos = ship != null ? ship.RenderPosition(alpha) : cam.position;
            _v0 = ship != null ? ship.Velocity : Vector3.zero;
            _fwd = _rootRot * Vector3.forward;
            _pos = cam.position; _rot = cam.rotation; _eye0 = _pos; _vel = _v0; _w = Vector3.zero;
            _rootPos = _shipPos + _rootRot * SeatedEyeShip - _rootRot * _seatEyeLocal;
            _seed = _c.Rng.F() * 50f;

            // Where the breach is and which way we are thrown: through the canopy, from wherever we are (seated, or at the table).
            Vector3 breach = _rootPos + _rootRot * _glassLocal;
            _dirOut = (breach - _pos);
            _dirOut = _dirOut.sqrMagnitude < 0.25f ? _fwd : _dirOut.normalized;
            _dirOut = (_dirOut + _fwd * 0.35f).normalized;

            // Where it hit (to start the cracks and the tearing there).
            bool recent = _c.World.Time - _lastHitAt < 0.8f;
            Vector3 hitPoint = recent ? _lastHitPoint : (at ?? (_pos + _fwd * 1.5f));
            Vector3 hitDir = recent && _lastHitDir.sqrMagnitude > 0.01f ? _lastHitDir : -_fwd;
            _impactLocal = Quaternion.Inverse(_rootRot) * (hitPoint - _rootPos);
            var vp = _c.Camera.WorldToViewportPoint(hitPoint);
            _impactUv = (vp.z > 0.5f && vp.x > 0.05f && vp.x < 0.95f && vp.y > 0.05f && vp.y < 0.95f) ? new Vector2(vp.x, 1f - vp.y) : new Vector2(0.5f + (_c.Rng.F() - 0.5f) * 0.5f, 0.42f + (_c.Rng.F() - 0.5f) * 0.25f);
            _shoveDir = _cause == "collision" || hitDir.sqrMagnitude < 0.01f ? _dirOut : hitDir.normalized;

            // Put the cabin's pieces in place, attached to the ship, lit by the scene's lights and by our own.
            foreach (var p in _parts)
            {
                var rel = p.Home - _impactLocal;
                float d01 = Mathf.Clamp01(rel.magnitude / 3.4f);
                p.Release = Mathf.Lerp(0.02f, _k.Blow * 0.92f, d01 * d01);
                if ((_rootRot * p.Home + _rootPos - _pos).sqrMagnitude < 0.45f * 0.45f) p.Release = 0f;   // not through the lens
                if (p.Glass) p.Release = _k.Blow;                                                           // the canopy goes with the breach
                p.Released = false;
                p.P.T.SetPositionAndRotation(_rootPos + _rootRot * p.Home, _rootRot * p.HomeRot);
                p.P.Vel = _v0; p.P.SpinRate = 0f; p.P.Heat = 0f; p.P.Age = 0f; p.P.Life = 1e9f;
                p.P.Go.SetActive(true);
            }

            _active = true; _skip = false; _t = 0f;
            _blown = _screamed = _breathed = _tumbleSaid = _stopped = _silenced = _w1 = _w2 = _w3 = _duckedLate = false;
            _beats = 0; _nextBeat = 1.95f; _nextSpark = 0f; _nextFlicker = 0f; _fogLeft = 0f;
            board.Active = true; board.CameraActive = true; board.Finished = false; board.Skippable = false;
            board.Position = _pos; board.Rotation = _rot; board.Velocity = _vel; board.Fov = _c.Camera.fieldOfView; board.Time = 0f; board.Phase = "hit"; board.Cause = _cause; board.Plays++;
            _fovBase = 55f;

            float s = _c.Settings.death.intensity;
            GameEvents.Raise(DeathEvents.Start, _cause, Length);
            GameEvents.Raise(DeathEvents.CanopyCrack, _cause, Mathf.Clamp01(0.6f + 0.4f * _k.Flash));
            _c.Audio.Play(JuiceCue.DeathCrack, _pos, Mathf.Clamp(s, 0.2f, 1.6f));
            _c.Trauma.Add(1.5f * s);
            _c.Rumble.Pulse(0.95f * s, 1f * s, 0.30f);
            _c.Hits.EdgeFlash(new Vector3(1f, 0.55f, 0.25f) * 1.2f * s, 0.5f);
            // The loudest light in the cabin, then the emergency red.
            _c.Lights.Flash(_pos, new Color(1f, 0.9f, 0.75f), 9e4f * s, 14f, 0.18f, 1.6f);
            // The first tearing: sparks from the wall where it hit, vents opening.
            CabinSparks(10, 1.2f);
            var rng = _c.Rng;
            for (int i = 0; i < 5; i++)
            {
                Vector3 local = _impactLocal + rng.OnSphere() * 1.4f;
                Vector3 at2 = _rootPos + _rootRot * local;
                _c.Fx.Vent(at2, (_pos - at2) + rng.OnSphere() * 0.8f, 0.18f, rng.Range(1.0f, 2.4f), rng.Range(18f, 34f));
            }
            MnLog.Info($"DEATH begins: {_cause}, {_parts.Count} cabin pieces, breach at {_k.Blow:0.00} s, out along {_dirOut} (frame {UnityEngine.Time.frameCount})");
        }

        Vector3 _shoveDir;
        float _fovBase;

        // ------------------------------------------------------------------------------------------------ the frame

        /// <summary>Once per rendered frame (the director's Update, before the pad's motors are written).</summary>
        public void Update(float real)
        {
            if (!_prepared && _c.Settings.death.enabled)
            {
                if (_prepRoot != null) StepPrepare();
                else if (--_framesToPrepare <= 0) BeginPrepare();
            }
            if (!_active) return;
            float dt = Mathf.Clamp(real, 0.001f, 0.1f);
            _t += dt;
            var board = _c.World.Death;
            board.Time = _t;
            board.Skippable = _t >= SkippableAt && !_skip;
            float s = _c.Settings.death.intensity;
            float tumbleScale = Mathf.Clamp(_c.Settings.death.tumble, 0f, 1.5f);
            float blow = _k.Blow;
            var rng = _c.Rng;

            if (_t >= 0.3f && _t - dt < 0.3f) MnLog.Info($"DEATH at 0.3 s: the cabin's exposure shift {(_c.World.Cabin != null ? _c.World.Cabin.ExposureShift : 0f):0.00} EV, ambient {(_c.World.Cabin != null ? _c.World.Cabin.AmbientScale : 1f):0.00}");
            // ---- the breach
            if (!_blown && _t >= blow)
            {
                _blown = true;
                board.Phase = "breach";
                GameEvents.Raise(DeathEvents.Decompression, _cause, 1f);
                _c.Audio.Play(JuiceCue.DeathDecompress, _pos, Mathf.Clamp(1.1f * Mathf.Max(0.4f, s), 0f, 1.7f));
                _c.Audio.Duck(0.55f, 1.4f);
                _c.Trauma.Add(1.2f * s);
                _c.Rumble.Pulse(1f * s, 1f * s, 0.75f);
                _c.Rumble.Pulse(0.8f * s, 0.55f * s, 2.6f);
                _c.Hits.EdgeFlash(new Vector3(1f, 0.8f, 0.55f) * 1.5f * s, 0.55f);
                _c.Lights.Flash(_pos + _dirOut * 1.5f, new Color(1f, 0.95f, 0.85f), 2e5f * s, 18f, 0.25f, 1.4f);
                // Fog and dust stream out for a moment; sparks go with the rush.
                _fogLeft = 0.9f;
                // The thrust: we go with the air, spinning (the axis is across the line of flight, with some roll).
                Vector3 across = Vector3.Cross(_dirOut, Vector3.up); if (across.sqrMagnitude < 0.01f) across = Vector3.right;
                across.Normalize();
                Vector3 up2 = Vector3.Cross(across, _dirOut);
                _w = (across * 1.0f + _dirOut * 0.55f + up2 * 0.3f).normalized * (_k.Tumble * tumbleScale);
                // The wreck, behind: the first blast now.
                BreakWreck();
                WreckBlast(_shipPos - _fwd * 1.0f, 13f, 1.2f, true);
                if (_hullMat != null) _c.Debris.Shards(_shipPos, _v0, 36, 0.5f, 4.2f, 10f, 46f * _k.Fire, _hullMat, 1.2f, 22f, null, 0.25f);
            }
            if (_blown)
            {
                var wr = _t - blow;
                if (!_tumbleSaid && wr > 0.2f) { _tumbleSaid = true; board.Phase = "tumble"; GameEvents.Raise(DeathEvents.Tumble, _cause, _w.magnitude); }
                if (!_w1 && wr > 0.30f) { _w1 = true; WreckBlast(_shipPos - _fwd * 2.5f, 17f, 1.1f, false); }
                if (!_w2 && wr > 0.80f) { _w2 = true; WreckBlast(_shipPos - _fwd * 5.5f, 21f, 1.0f, false); if (_hullMat != null) _c.Debris.Shards(_shipPos - _fwd * 4f, _v0, 24, 0.6f, 5.5f, 8f, 38f, _hullMat, 1.2f, 22f, null, 0.25f); }
                if (!_w3 && wr > 1.35f) { _w3 = true; WreckBlast(_shipPos - _fwd * 1.5f, 15f, 0.9f, false); }
            }

            // ---- the camera: pinned to the seat until the breach, then thrown out and tumbling in vacuum
            if (!_blown)
            {
                // The jolt of the hit: a shove along the line the blow came in, dying away (the shake does the rest).
                _pos += _shoveDir * (_k.Shove * Mathf.Exp(-_t / 0.12f) * 0.15f) * dt * 6f + _v0 * dt;
                _vel = _v0;
            }
            else
            {
                float ramp = 1f - Mathf.Exp(-(_t - blow) / 0.11f);
                _vel = _v0 + _dirOut * (_k.OutSpeed * ramp);
                _pos += _vel * dt;
                float wm = _w.magnitude;
                if (wm > 1e-4f)
                {
                    // A slow wander of the axis (nothing spins on one axis for long), and the spin dying away very slowly.
                    Vector3 wobble = new Vector3(Mathf.PerlinNoise(_t * 0.7f, 3.1f) - 0.5f, Mathf.PerlinNoise(_t * 0.6f, 9.2f) - 0.5f, Mathf.PerlinNoise(_t * 0.8f, 5.5f) - 0.5f) * (0.9f * wm * dt);
                    _w = (_w + wobble) * Mathf.Exp(-0.10f * dt);
                    float ang = _w.magnitude * dt * Mathf.Rad2Deg;
                    // When the view closes the spin slows: the body giving up.
                    if (_t > IrisStart) _w *= Mathf.Exp(-0.8f * dt);
                    _rot = Quaternion.AngleAxis(ang, _w.normalized) * _rot;
                }
            }
            board.Position = _pos; board.Rotation = _rot; board.Velocity = _vel;

            // ---- the field of view
            float fov = 55f + 16f * Mathf.Exp(-_t / 0.28f);
            if (_blown) fov += 24f * Mathf.Exp(-(_t - blow) / 0.55f) + 8f * Mathf.Clamp01((_t - blow) / 0.5f);
            fov -= 14f * Smooth01((_t - IrisStart) / 1.9f);
            board.Fov = fov;

            // ---- the shake: violent, then a tremor that eases
            if (_blown) _c.Trauma.Sustain(0.55f * s * (1f - Mathf.Clamp01((_t - blow) / 2.4f)));
            else _c.Trauma.Sustain(0.9f * s);

            // ---- the cabin tearing
            for (int i = 0; i < _parts.Count; i++)
            {
                var p = _parts[i];
                if (p.Released || _t < p.Release) continue;
                ReleasePart(p, _t >= blow);
            }
            if (!_blown && _t >= _nextSpark)
            {
                _nextSpark = _t + rng.Range(0.05f, 0.11f);
                CabinSparks(rng.Int(3, 7), 1f);
            }
            if (_fogLeft > 0f) { _fogLeft -= dt; Rush(dt); }
            if (_blown && _t - blow < 1.6f && _t >= _nextSpark) { _nextSpark = _t + 0.05f; Rush(0.05f); }

            // ---- the light failing: red flickers that thin out, then nothing but the fire behind
            if (_t < 3.4f && _t >= _nextFlicker)
            {
                float fail = Mathf.Clamp01(_t / 3.4f);
                _nextFlicker = _t + Mathf.Lerp(0.05f, 0.24f, fail) * rng.Range(0.5f, 1.5f);
                if (rng.Chance(1f - 0.7f * fail))
                {
                    Vector3 at = _pos + rng.OnSphere() * 1.6f;
                    _c.Lights.Flash(at, rng.Chance(0.25f) ? new Color(1f, 0.85f, 0.7f) : new Color(1f, 0.12f, 0.05f), (rng.Chance(0.25f) ? 2.2e5f : 1.1e5f) * (1f - 0.6f * fail) * s, 11f, rng.Range(0.05f, 0.14f), 1.4f);
                }
            }

            // ---- the screams, the breathing, the heart
            if (!_screamed && _t >= 0.2f) { _screamed = true; GameEvents.Raise(DeathEvents.HullScream, _cause, 3.2f); _c.Audio.Play(JuiceCue.DeathTear, _pos, Mathf.Clamp(0.9f * Mathf.Max(0.4f, s), 0f, 1.5f)); }
            if (!_breathed && _t >= 1.4f)
            {
                _breathed = true;
                GameEvents.Raise(DeathEvents.Breath, _cause, 3.4f);
                _c.Audio.Play(JuiceCue.DeathBreath, _pos, 0.9f);
                board.Phase = board.Phase == "tumble" ? "tumble" : board.Phase;
            }
            if (_t >= 2.2f && !_duckedLate)
            {
                _duckedLate = true;
                _c.Audio.Duck(0.94f, 4.6f);                     // the battle fades to a murmur through the hull, then to nothing
                board.Phase = "dying";
            }
            if (!_stopped && _t >= _nextBeat && _t < 4.95f)
            {
                float k = Mathf.Clamp01((_t - 1.95f) / 3.0f);
                float strength = Mathf.Lerp(1f, 0.35f, k);
                _beats++;
                GameEvents.Raise(DeathEvents.Heartbeat, _cause, strength);
                _c.Audio.Play(JuiceCue.DeathHeartbeat, _pos, 0.85f * Mathf.Lerp(1f, 0.5f, k));
                _c.Rumble.Pulse(0.42f * strength * s, 0.12f * strength * s, 0.16f);
                _c.Trauma.Add(0.07f * strength * s);
                _nextBeat = _t + Mathf.Lerp(0.62f, 1.10f, k);
            }
            if (!_stopped && _t >= 5.0f) { _stopped = true; GameEvents.Raise(DeathEvents.HeartbeatStop, _cause, 0f); }
            if (!_silenced && _t >= 5.25f) { _silenced = true; board.Phase = "dark"; GameEvents.Raise(DeathEvents.Silence, _cause, 0f); }

            // ---- the screen: cracks, flash, the iris, black
            var f = _c.Frame;
            f.DeathActive = true;
            float crackA = 0f, grow = 0f;
            if (!_blown) { crackA = Mathf.Clamp01(_t / 0.05f); grow = Mathf.Clamp01(_t / Mathf.Max(0.05f, _k.Crack)); }
            else if (_t - blow < 0.07f) { crackA = 1f - (_t - blow) / 0.07f; grow = 1f; }
            float flash = 1.0f * _k.Flash * Mathf.Exp(-_t / 0.07f) * s;
            if (_blown) flash += 1.0f * Mathf.Exp(-(_t - blow) / 0.10f) * s;
            if (_cause == "beam") flash += 0.9f * Mathf.Exp(-_t / 0.6f) * s;
            flash *= _c.Settings.effects.screenFlash ? 1f : 0.25f;                      // the flash switch (accessibility) turns the white-out down
            float irisR = 2f, soft = 0.3f, black = 0f, red = 0f;
            if (_t > IrisStart)
            {
                float k = Smooth01((_t - IrisStart) / (IrisEnd - IrisStart));
                irisR = Mathf.Lerp(1.25f, 0.015f, Mathf.Pow(k, 1.35f));
                soft = Mathf.Lerp(0.45f, 0.26f, k);
                red = Mathf.Lerp(0.2f, 1f, k);
                black = Smooth01((_t - BlackFrom) / (BlackDone - BlackFrom));
            }
            if (_skip)
            {
                float k = Mathf.Clamp01((_t - _skipAt) / 0.5f);
                black = Mathf.Max(black, Smooth01(k));
                irisR = Mathf.Min(irisR, Mathf.Lerp(1.4f, 0.1f, k));
                red = Mathf.Max(red, k);
            }
            f.DeathA = new Vector4(irisR, soft, black, crackA);
            board.Dark = Mathf.Max(black, 1f - Mathf.Clamp01(irisR / 0.9f));
            f.DeathB = new Vector4(_impactUv.x, _impactUv.y, grow, _seed);
            f.DeathC = new Vector4(flash, flash * 0.94f, flash * 0.82f, red);

            // ---- the end
            if (_t >= Length || (_skip && _t - _skipAt >= 0.55f)) End();
        }

        static float Smooth01(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

        /// <summary>The player asked to skip it (A or Start). False when it is too soon or already skipping.</summary>
        public bool TrySkip()
        {
            if (!_active || _skip || _t < SkippableAt) return false;
            _skip = true; _skipAt = _t;
            GameEvents.Raise(DeathEvents.Skip, _cause, 0f);
            return true;
        }

        void End()
        {
            _active = false;
            var board = _c.World.Death;
            board.Active = false; board.Finished = true; board.Skippable = false; board.Phase = "dark";
            var f = _c.Frame;
            f.DeathA = new Vector4(0f, 0.1f, 1f, 0f); f.DeathC = Vector4.zero;       // black, until the world is reset
            board.Dark = 1f;
            GameEvents.Raise(DeathEvents.End, _cause, _t);
            MnLog.Info($"DEATH ends after {_t:0.00} s{(_skip ? " (skipped)" : "")}: {_beats} heartbeats");
        }

        /// <summary>Back to the ordinary view (the next run, the sandbox's reset): the screen clears and the cabin pieces go.</summary>
        public void Reset()
        {
            var board = _c.World.Death;
            _active = false;
            board.Active = false; board.CameraActive = false; board.Finished = false; board.Skippable = false; board.Phase = ""; board.Time = 0f; board.Dark = 0f;
            var f = _c.Frame;
            f.DeathActive = false; f.DeathA = new Vector4(2f, 0.2f, 0f, 0f); f.DeathC = Vector4.zero;
            // Pieces we launched are the debris system's to age out; the ones still waiting are cut afresh next time.
            _prepared = false; _framesToPrepare = 5;
            if (_prepRoot != null) { UnityEngine.Object.Destroy(_prepRoot.gameObject); _prepRoot = null; }
            foreach (var p in _parts) if (p.P != null && !p.P.Dead && !p.Released) _c.Debris.Remove(p.P);
            _parts.Clear();
        }

        // ------------------------------------------------------------------------------------------------ pieces of the effect

        void ReleasePart(Part p, bool atBreach)
        {
            p.Released = true;
            var rng = _c.Rng;
            Vector3 at = p.P.T.position;
            Vector3 rel = at - _pos;
            float d = rel.magnitude;
            Vector3 dir = d < 0.4f ? rng.OnSphere() : rel / d;
            float ahead = Vector3.Dot(dir, _dirOut);
            float speed;
            if (!atBreach)
            {
                speed = rng.Range(2.5f, 9f);                      // peeling, before the breach: it lets go but it does not fly
                dir = (dir + _shoveDir * 0.4f + rng.OnSphere() * 0.35f).normalized;
            }
            else
            {
                dir = (dir * 0.6f + _dirOut * Mathf.Clamp01(0.45f + 0.4f * ahead) + rng.OnSphere() * 0.32f).normalized;
                speed = rng.Range(9f, 30f) * (1f + 0.2f * _k.Fire);
                float fwd = Vector3.Dot(dir, _dirOut);
                if (fwd > 0.2f) speed = Mathf.Max(speed, _k.OutSpeed + rng.Range(8f, 28f));   // what is ahead of us stays ahead of us
                else if (fwd < -0.3f) speed *= 0.6f;
                if (p.Glass) speed = _k.OutSpeed + rng.Range(26f, 48f);
            }
            _c.Debris.Launch(p.P, _v0 + dir * speed, rng.Range(0.8f, 5.2f) * (p.Radius > 1.1f ? 0.55f : 1f), atBreach ? rng.Range(0.5f, 0.9f) : rng.Range(0.2f, 0.5f), 1.6f, 15f);
            if (p.Radius > 0.7f && atBreach && rng.Chance(0.55f)) { p.P.TrailRate = 12f; p.P.TrailLeft = 1.2f; p.P.LocalCentre = Vector3.zero; }
            _c.Fx.SparkBurst(at, _v0, atBreach ? 4 : 6, 14f, 60f, 0.2f, 0.7f, 0.05f, SparkStyle.Hot, 0.6f, 0.9f);
        }

        void CabinSparks(int bursts, float strength)
        {
            var rng = _c.Rng;
            for (int i = 0; i < bursts; i++)
            {
                Vector3 local = _impactLocal * 0.5f + rng.OnSphere() * rng.Range(0.5f, 2.2f);
                Vector3 at = _eye0 + _rootRot * (local - _impactLocal * 0.5f) * 0.9f;
                at = _pos + rng.OnSphere() * rng.Range(0.4f, 2.2f);
                _c.Fx.SparkBurst(at, _v0, Mathf.RoundToInt(rng.Range(8f, 18f) * strength), 8f, 42f, 0.25f, 0.9f, 0.045f, rng.Chance(0.2f) ? SparkStyle.Plasma : SparkStyle.Hot, 0.8f, 1.1f);
            }
        }

        /// <summary>The air going out of the cabin: fog, dust and streaks racing past toward the hole.</summary>
        void Rush(float dt)
        {
            var rng = _c.Rng;
            int n = Mathf.Max(1, Mathf.RoundToInt(dt * 120f));
            for (int i = 0; i < n; i++)
            {
                Vector3 at = _pos + rng.OnSphere() * rng.Range(0.3f, 2.4f) - _dirOut * rng.Range(0f, 1.2f);
                _c.Fx.Sparks(at, _v0, _dirOut, 25f, 1, 18f, 52f, 0.25f, 0.6f, 0.012f, SparkStyle.Metal, 0.05f, 0.7f, new Vector3(0.75f, 0.82f, 0.95f));
                if (i % 7 == 0) _c.Fx.Gas(at, _v0 + _dirOut * rng.Range(14f, 34f), 0.35f, 1.5f, rng.Range(0.35f, 0.7f), new Vector3(0.85f, 0.9f, 1f) * 0.22f, 0.2f, 2f);
                if (i % 2 == 0) _c.Fx.Specks(at, _v0 + _dirOut * rng.Range(10f, 30f), 0.5f, 4, 2f, 9f, 0.4f, 0.9f, 0.05f);
            }
        }

        /// <summary>Your ship, cut into pieces that fly apart burning (the nose you were sitting in is what tore apart round you, so the pieces near the eye are not made).</summary>
        void BreakWreck()
        {
            if (WreckVisual == null || !_c.Settings.effects.debris) return;
            Transform v = null;
            try { v = WreckVisual(); } catch (Exception ex) { MnLog.Warn("DEATH could not build the ship's wreck: " + ex.Message); }
            if (v == null) return;
            var rng = _c.Rng;
            v.SetPositionAndRotation(_shipPos, _rootRot);
            var hold = _c.Debris.Adopt(v, _v0);
            var bounds = Debris.LocalBounds(hold.T);
            var seeds = _c.Debris.MakeSeeds(hold.T, bounds, 11, true);
            var parts = _c.Debris.Slice(hold.T, seeds, _v0, 0);
            Vector3 centre = _shipPos - _fwd * 1.0f;
            foreach (var p in parts)
            {
                if ((p.T.position - _pos).magnitude < 2.4f) { _c.Debris.Remove(p); continue; }
                Vector3 dir = (p.T.position - centre); dir = dir.sqrMagnitude < 0.25f ? rng.OnSphere() : dir.normalized;
                dir = (dir + rng.OnSphere() * 0.5f).normalized;
                _c.Debris.Launch(p, _v0 + dir * rng.Range(5f, 24f) * _k.Fire, rng.Range(0.6f, 3.6f), 1.2f, 2.6f, 24f);
                p.TrailRate = 24f; p.TrailLeft = 2.6f; p.LocalCentre = Vector3.zero;
                _c.Debris.Burn(p, p.T.position, 3f, 14f, Mathf.Max(0.7f, p.Radius * 0.35f));
            }
            _c.Debris.Remove(hold);
            if (v != null) UnityEngine.Object.Destroy(v.gameObject);
        }

        void WreckBlast(Vector3 at, float radius, float intensity, bool first)
        {
            var rng = _c.Rng;
            float r = radius * _k.Fire;
            _c.Fx.Blast(at, _v0, r, intensity, false, true, true);
            _c.Fx.SparkBurst(at, _v0, _c.N(first ? 30 : 22), 20f, 120f, 0.5f, 1.6f, 0.1f, SparkStyle.Metal, 0.5f);
            _c.Fx.Embers(at, _v0, r * 0.6f, _c.N(40), 40f, 0.8f, 2.4f, 0.12f);
            _c.Fx.Vent(at + rng.OnSphere() * 1.5f, rng.OnSphere(), 0.9f, 2.4f, 30f);
            _c.Lights.Flash(at, new Color(1f, 0.62f, 0.3f), 3e8f * intensity * _c.Bright, 160f, 0.55f, 1.8f);
        }
    }
}
