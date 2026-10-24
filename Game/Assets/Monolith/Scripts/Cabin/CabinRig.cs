using System;
using System.Collections.Generic;
using System.IO;
using Monolith.Core;
using Monolith.Hud;
using UnityEngine;

namespace Monolith.Cabin
{
    /// <summary>
    /// The cabin's body in the scene: the Blender model (Resources/Cabin/cabin.prefab) riding the player's ship at its interpolated pose, shown only while the pilot is up
    /// (seated, the view is open space with at most the canopy frame at its edges), with its lights, displays and the parts that move (they animate on their own: the pilot has no hands in the view): the breaker's lever,
    /// the repair panels' hatches and modules, the rack's modules. The pilot's pose is the <see cref="CabinSystem"/>'s (published on the board; the cockpit view puts the
    /// camera there). Before launch the sim is held, and the rig steps the cabin on its own clock so the pilot can walk and work.
    /// </summary>
    [DefaultExecutionOrder(40)]
    public sealed class CabinRig : MonoBehaviour
    {
        public const string PrefabPath = "Cabin/cabin";

        GameWorld _world;
        CabinSystem _cab;
        Transform _root;
        readonly Dictionary<string, Transform> _node = new Dictionary<string, Transform>();
        readonly List<Renderer> _interior = new List<Renderer>(), _frame = new List<Renderer>(), _glass = new List<Renderer>();
        CabinLights _lights;
        CabinDisplays _displays;
        float _manualNow;
        bool _built;
        string _lightState = "normal";

        // The emissive materials the rig drives by the cabin's lighting (the asset materials the prefab uses; the player build gets its own copies).
        readonly Dictionary<string, Material> _mat = new Dictionary<string, Material>();
        readonly MaterialPropertyBlock _knitProps = new MaterialPropertyBlock();
        float _instrument, _emergency;

        sealed class Panel { public string Id; public Transform Door, Module, Knit; public Vector3 ModulePos, DoorPos; public Quaternion DoorRot, ModuleRot; public float Open, Hold; public string Need; public Renderer KnitR; }
        readonly List<Panel> _panels = new List<Panel>();
        sealed class Bay { public string Id; public Transform Module; public Vector3 Pos; public Vector3 Scale; public string Shown; }
        readonly List<Bay> _bays = new List<Bay>();
        Transform _lever; Vector3 _leverRest;
        const float LeverTravel = 0.17f;

        /// <summary>The Sun's direction in the world (from the viewer toward it): the Sky's, given by whoever builds the scene.</summary>
        public Func<Vector3> SunDirection;

        // Diagnostics for the frame cost (-mnPerfSync runs): leave the displays or the shadows out and see what the frame loses.
        static readonly bool NoDisplays = CommandLine.Has("-mnCabinNoDisplays");

        public CabinSystem System => _cab;
        public Transform Root => _root;

        public static CabinRig Create(GameWorld world, CabinSystem cab, Camera cam, Transform parent)
        {
            var go = new GameObject("CabinRig");
            go.transform.SetParent(parent, false);
            var rig = go.AddComponent<CabinRig>();
            rig._world = world; rig._cab = cab;
            rig.Build(cam);
            return rig;
        }

        void Build(Camera cam)
        {
            var prefab = Resources.Load<GameObject>(PrefabPath) ?? throw new FileNotFoundException($"Resources/{PrefabPath}.prefab is missing (run the cabin model script, then Setup.Project)");
            var go = Instantiate(prefab, transform);
            go.name = "Cabin";
            _root = go.transform;
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) _node[t.name] = t;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                bool isFrame = r.name == "CABIN_FRAME" || r.transform.parent != null && r.transform.parent.name == "CABIN_FRAME";
                bool isGlass = r.name == "CABIN_GLASS";
                if (isGlass) _glass.Add(r); else if (isFrame) _frame.Add(r); else _interior.Add(r);
                foreach (var m in r.sharedMaterials) if (m != null && !_mat.ContainsKey(m.name)) _mat[m.name] = m;
            }
            _lights = new CabinLights(_root, _cab.Layout, _cab.Cfg);
            var shader = Resources.Load<Shader>(FlightHud.ShaderPath) ?? throw new FileNotFoundException($"Resources/{FlightHud.ShaderPath}.shader is missing");
            _displays = new CabinDisplays(_world, _cab, _root, shader);
            CabinOverlay.Create(_world, _cab, cam, transform);
            foreach (var id in new[] { "engine", "arms" })
            {
                var p = new Panel { Id = id, Door = Get($"PANEL_{id}_DOOR"), Module = Get($"PANEL_{id}_MODULE"), Knit = Get($"PANEL_{id}_KNIT") };
                if (p.Door != null) { p.DoorPos = p.Door.localPosition; p.DoorRot = p.Door.localRotation; }
                if (p.Module != null) { p.ModulePos = p.Module.localPosition; p.ModuleRot = p.Module.localRotation; }
                p.KnitR = p.Knit != null ? p.Knit.GetComponent<Renderer>() : null;
                _panels.Add(p);
            }
            foreach (var l in _cab.Layout.Interactables)
                if (l.Kind == "rack")
                {
                    var t = Get("RACK_MODULE_" + l.Bay);
                    if (t != null) _bays.Add(new Bay { Id = l.Bay, Module = t, Pos = t.localPosition, Scale = t.localScale });
                }
            _lever = Get("BREAKER_LEVER");
            if (_lever != null) _leverRest = _lever.localPosition;
            SetInteriorVisible(false);
            _built = true;
            MnLog.Info($"CABIN rig built: {_interior.Count} interior, {_frame.Count} frame, {_glass.Count} glass renderers, {_displays.Count} displays, {_cab.Layout.Lights.Count} lights");
        }

        Transform Get(string name) => _node.TryGetValue(name, out var t) ? t : null;

        void OnDestroy()
        {
            _displays?.Dispose();
        }

        // ------------------------------------------------------------------------------------------------ the frame

        void Update()
        {
            if (!_built) return;
            var b = _cab.Board;
            // Before launch the sim is held; the cabin runs on its own clock (a capture runs the sim, so this stays out of its way).
            if (b.Prelaunch && !_cab.Paused && _world.Sim.TimeScale <= 0f && !_world.Sim.OneTickPerFrame)
            {
                float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
                _manualNow += dt;
                _cab.Advance(dt, _manualNow);
                b.EyePrev = b.EyeNow; b.RotPrev = b.RotNow;
            }
        }

        void LateUpdate()
        {
            if (!_built) return;
            // The death sequence has its own cabin, torn to pieces (Juice/DeathFx): this one is not drawn while it plays.
            if (_world.Death.Active) { if (_root != null && _root.gameObject.activeSelf) _root.gameObject.SetActive(false); return; }
            var b = _cab.Board;
            var ship = _world.Ships.Get(_world.LocalPlayerShip);
            if (ship == null) { SetInteriorVisible(false); return; }
            float alpha = _world.Alpha;
            var rot = ship.RenderRotation(alpha);
            _root.SetPositionAndRotation(ship.RenderPosition(alpha) + rot * _cab.CabinOrigin, rot);
            bool show = b.Stand01 > 0.002f || b.State != CabinBoard.Phase.Seated;
            SetInteriorVisible(show);
            float now = Time.unscaledTime;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);

            // Lights.
            _lightState = b.Lights ?? "normal";
            float npu = b.NitsPerUnit;
            _lights.Update(dt, _lightState, b.Jolt, show, now, npu);
            Vector3 sun = SunDirection != null ? Quaternion.Inverse(rot) * SunDirection() : Vector3.zero;
            _lights.SetSun(sun, show ? 1f : 0f, npu);
            _lights.SetScan(b.ScanDirShip, show ? b.ScanLight : 0f, npu);
            DriveEmissives(dt);

            // Displays: dim when the lights are, off when dark.
            int lit = _lightState == "blackout" ? 0 : _lightState == "dark" ? 1 : 2;
            if (!NoDisplays) _displays.Update(now, lit, show || b.MapOpen);

            Animate(dt);
        }

        void SetInteriorVisible(bool show)
        {
            foreach (var r in _interior) if (r != null && r.enabled != show) r.enabled = show;
            bool glassOn = show && _cab.Board.Stand01 > 0.02f;
            foreach (var r in _glass) if (r != null && r.enabled != glassOn) r.enabled = glassOn;
            foreach (var r in _frame) if (r != null && !r.enabled) r.enabled = true;
        }

        void DriveEmissives(float dt)
        {
            string s = _lightState;
            float inst = s == "normal" ? 1f : s == "dark" ? 0.18f : s == "emergency" || s == "alert" ? 0.5f : 0f;
            float emer = s == "normal" ? 0f : s == "dark" ? 0.12f : s == "alert" ? (0.3f + 0.7f * Mathf.Clamp01(0.5f + 0.7f * Mathf.Sin(Time.unscaledTime * 6f))) : s == "emergency" ? 1f : 0.45f;
            _instrument = Mathf.MoveTowards(_instrument, inst, dt * 5f);
            _emergency = Mathf.MoveTowards(_emergency, emer, dt * 5f);
            var l = _cab.Cfg.light;
            float u = _cab.Board.NitsPerUnit;
            Emit("MN_CabTrim", Color.white, 0.5f * u * _instrument * (1f - 0.7f * _cab.Board.Jolt));
            Emit("MN_CabGlyph", Color.white, l.glyphUnits * u * Mathf.Max(_instrument, s == "blackout" ? 0.22f : 0f));
            Emit("MN_CabLampWhite", new Color(0.95f, 0.97f, 1f), l.lampUnits * u * _instrument);
            Emit("MN_CabLampAmber", new Color(1f, 0.55f, 0.14f), l.lampUnits * u * _instrument);
            Emit("MN_CabLampRed", new Color(1f, 0.08f, 0.04f), l.emergencyLampUnits * u * _emergency);
        }

        void Emit(string material, Color colour, float nits)
        {
            if (_mat.TryGetValue(material, out var m) && m != null) m.SetColor("_EmissiveColor", colour * Mathf.Max(0f, nits));
        }

        // ------------------------------------------------------------------------------------------------ the parts that move

        void Animate(float dt)
        {
            float knitBest = 0f; Vector3 knitPos = Vector3.zero;
            if (_lever != null) _lever.localPosition = _leverRest + Vector3.up * ((_cab.LeverPos - 0.5f) * 2f * LeverTravel);
            foreach (var p in _panels)
            {
                string need = _cab.PanelNeed(p.Id);
                float prog = need != null ? _cab.RepairProgress(need) : 1f;
                if (need != null) { p.Need = need; p.Hold = 1.4f; }
                else if (p.Hold > 0f) p.Hold -= dt;
                float wantOpen = need != null || p.Hold > 0f ? 1f : 0f;
                p.Open = Mathf.MoveTowards(p.Open, wantOpen, dt * 1.6f);
                float open = Mathf.SmoothStep(0f, 1f, p.Open);
                if (p.Door != null) p.Door.localRotation = p.DoorRot * Quaternion.AngleAxis(-100f * open, Vector3.right);
                if (p.Module != null)
                {
                    float seat = Mathf.SmoothStep(0f, 1f, prog);
                    // The module lies out of its socket, crooked, until it is pressed home.
                    p.Module.localPosition = p.ModulePos - Vector3.forward * (0.13f * (1f - seat)) + Vector3.up * (0.02f * (1f - seat));
                    p.Module.localRotation = p.ModuleRot * Quaternion.AngleAxis(16f * (1f - seat), Vector3.right) * Quaternion.AngleAxis(9f * (1f - seat), Vector3.forward);
                    p.Module.gameObject.SetActive(need != null || p.Hold > 0f);
                }
                if (p.KnitR != null)
                {
                    p.KnitR.enabled = need != null || p.Hold > 0f;
                    float glow = need != null ? Mathf.SmoothStep(0.05f, 1f, prog) : Mathf.Clamp01(p.Hold / 1.4f);
                    _knitProps.SetVector("_EmissiveColor", new Vector4(0.3f, 0.95f, 0.82f, 1f) * (_cab.Cfg.light.glyphUnits * 2.2f * glow * _cab.Board.NitsPerUnit));
                    p.KnitR.SetPropertyBlock(_knitProps);
                    if (glow > knitBest && p.Knit != null)
                    {
                        knitBest = glow;
                        Vector3 lp = _root.InverseTransformPoint(p.Knit.position);
                        lp.x += (lp.x > 0f ? -0.28f : 0.28f);          // out into the cabin, in front of the panel
                        knitPos = lp;
                    }
                }
            }
            _lights.SetKnit(knitPos, knitBest, _cab.Board.NitsPerUnit);
            foreach (var bay in _bays)
            {
                bool gun = bay.Id.StartsWith("gun", StringComparison.Ordinal);
                int slot = bay.Id.EndsWith("1", StringComparison.Ordinal) ? 1 : 0;
                string id = gun ? _cab.Loadout.Guns[slot] : _cab.Loadout.Missiles[slot];
                float out01 = _cab.RackOut01(bay.Id);
                bay.Module.localPosition = bay.Pos - Vector3.forward * (0.26f * Mathf.SmoothStep(0f, 1f, out01));
                if (bay.Shown != id)
                {
                    bay.Shown = id;
                    float th = 1f, len = 1f;
                    switch (id) { case "needle": th = 0.8f; len = 1.15f; break; case "hammer": th = 1.4f; len = 0.85f; break; case "missile_heat": th = 0.8f; len = 0.8f; break; case "missile_lock": th = 1f; len = 1f; break; case "torpedo_heavy": th = 1.35f; len = 1.3f; break; }
                    bay.Module.localScale = new Vector3(bay.Scale.x * th, bay.Scale.y * th, bay.Scale.z * len);
                }
            }
        }
    }
}
