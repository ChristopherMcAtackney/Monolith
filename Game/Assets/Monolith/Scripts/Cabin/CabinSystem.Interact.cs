using System;
using System.Collections.Generic;
using Monolith.Core;
using Monolith.Mission;
using UnityEngine;

namespace Monolith.Cabin
{
    /// <summary>The cabin's four interactions: the main breaker, the hand repair, the plan table, the weapon rack. Short physical actions on one context button.</summary>
    public sealed partial class CabinSystem
    {
        public enum Act { None, Breaker, Repair, Rack, Table }

        Act _act;
        float _actT, _actDur, _releasedFor, _slip;
        bool _actFired;
        CabinLayout.Interactable _actItem, _aimItem;
        string _aimPrompt;
        readonly Dictionary<string, float> _repairP = new Dictionary<string, float>();
        float _poweredOffAt = -999f, _relightT = 99f;
        string _rackBay;

        public Act Action => _act;
        public MissionRuntime Runtime => _rt;
        /// <summary>The breaker lever: 1 up (the ship is powered), 0 down (dark); it moves while the pilot pulls it.</summary>
        public float LeverPos { get; private set; } = 1f;
        float _leverFrom, _leverTo = 1f;
        /// <summary>How far a rack bay's module is pulled out for a swap (0 seated, 1 clear of its bay), while the pilot works it.</summary>
        public float RackOut01(string bay)
        {
            if (_act != Act.Rack || _actItem == null || _actItem.Bay != bay) return 0f;
            float p = Mathf.Clamp01(_actT / Mathf.Max(0.01f, _actDur));
            return Mathf.Sin(p * Mathf.PI);
        }
        /// <summary>The part waiting for a hand repair at a panel ("engine" or "arms"), or null; and how far the repair has come.</summary>
        public string PanelNeed(string panel) => NeedFor("panel_" + panel)?.Name;
        public float RepairProgress(string partName) => partName != null && _repairP.TryGetValue(partName, out float p) ? p : 0f;
        public CabinLayout.Interactable ActiveItem => _actItem;
        /// <summary>The thing the pilot is looking at and could use right now, or null.</summary>
        public CabinLayout.Interactable AimedItem => _aimItem;
        public float ActionProgress => _act == Act.Repair ? RepairProgressOf(_actItem?.Id) : _actDur > 0f ? Mathf.Clamp01(_actT / _actDur) : 0f;
        public bool RackOpen => _prelaunch;
        public string Prompt => Board.Prompt;

        // The power state the mission asks about, and the lights the mission set.
        public bool Powered { get; private set; } = true;
        string _missionLights = "normal";
        public string Lights => _missionLights;

        // The plan table.
        public enum TableKind { Briefing, Question, Objectives, Map, Debrief }
        public struct TablePageInfo { public TableKind Kind; public int Index; public string Title; }
        public int TablePage { get; private set; }
        readonly List<TablePageInfo> _pages = new List<TablePageInfo>();
        int _tableMode = -1;
        public IReadOnlyList<TablePageInfo> TablePages => _pages;
        public TablePageInfo CurrentPage => _pages.Count > 0 ? _pages[Mathf.Clamp(TablePage, 0, _pages.Count - 1)] : new TablePageInfo { Kind = TableKind.Map, Title = "MAP" };

        void StepActions(float dt, in CabinInputFrame inp)
        {
            UpdateTableMode();
            if (Board.State != CabinBoard.Phase.Standing)
            {
                if (_act != Act.None && Board.State == CabinBoard.Phase.Rising) _act = Act.None;
                _aimItem = null;
                SetPrompt(SeatedPrompt(), null, -1f);
                DecayRepairs(dt, null);
                Board.Action = null; Board.ActionProgress = 0f;
                return;
            }
            if (_act == Act.None) PickAim();
            if (_act != Act.Breaker) LeverPos = Powered ? 1f : 0f;
            switch (_act)
            {
                case Act.None: StartAction(in inp); break;
                case Act.Breaker: StepBreaker(dt); break;
                case Act.Rack: StepRack(dt); break;
                case Act.Table: _actT += dt; if (_actT >= _actDur) _act = Act.None; break;
                case Act.Repair: StepRepair(dt, in inp); break;
            }
            if (_act == Act.None) DecayRepairs(dt, null);
            string prompt = null, key = null; float prog = -1f;
            if (_act == Act.None && _aimItem != null) PromptFor(_aimItem, out prompt, out key);
            else if (_act == Act.Repair) { PromptFor(_actItem, out prompt, out key); prog = RepairProgressOf(_actItem.Id); }
            else if (_act == Act.Breaker || _act == Act.Rack) { PromptFor(_actItem, out prompt, out key); prog = _actDur > 0f ? Mathf.Clamp01(_actT / _actDur) : -1f; }
            else if (_act == Act.None && AtSeat) { prompt = "SIT"; key = "HOLD " + Keys.Stand; }
            SetPrompt(prompt, key, prog);
            Board.Action = _act == Act.None ? null : _act.ToString().ToLowerInvariant();
            Board.ActionProgress = _act == Act.None ? 0f : ActionProgress;
            if (inp.StandHeld && AtSeat && _act == Act.None) Board.PromptProgress = Mathf.Clamp01(_standHold / Cfg.timing.standHoldSeconds);
        }

        string SeatedPrompt()
        {
            if (Board.State != CabinBoard.Phase.Seated) return null;
            if (_prelaunch) return "LAUNCH";
            if (_callOutOpen) return "STAND UP";
            return null;
        }

        void SetPrompt(string text, string key, float progress)
        {
            if (Board.State == CabinBoard.Phase.Seated)
            {
                if (_prelaunch) { text = "LAUNCH"; key = Keys.Confirm; progress = -1f; }
                else if (_callOutOpen) { text = "STAND UP"; key = "HOLD " + Keys.Stand; progress = _standHold > 0f ? Mathf.Clamp01(_standHold / Cfg.timing.standHoldSeconds) : -1f; }
            }
            if (Board.Prompt != text || Board.PromptKey != key) Board.Version++;
            Board.Prompt = text; Board.PromptKey = key; Board.PromptProgress = progress;
        }

        // ------------------------------------------------------------------------------------------------ what the pilot is looking at

        void PickAim()
        {
            _aimItem = null;
            Vector3 look = LookDir, eye = _eye;
            float best = 0.80f;
            foreach (var it in Layout.Interactables)
            {
                if (it.Kind == "seat") continue;
                float d = (_pos - it.Stand).magnitude;
                if (d > it.Radius) continue;
                Vector3 to = (it.Pos - eye);
                if (to.sqrMagnitude < 1e-6f) continue;
                float c = Vector3.Dot(look, to.normalized);
                if (c < best) continue;
                if (!Usable(it)) continue;
                best = c; _aimItem = it;
            }
        }

        /// <summary>Is there anything to do at this interactable right now (a sealed panel has nothing to say; a locked rack none either).</summary>
        bool Usable(CabinLayout.Interactable it)
        {
            switch (it.Kind)
            {
                case "panel": return NeedFor(it.Id) != null;
                case "rack": return true;                         // a locked rack still says so
                default: return true;
            }
        }

        void PromptFor(CabinLayout.Interactable it, out string prompt, out string key)
        {
            key = Keys.Context; prompt = null;
            switch (it.Kind)
            {
                case "breaker": prompt = Powered ? "PULL THE MAIN BREAKER" : "RESTORE POWER"; break;
                case "table": prompt = "PLAN TABLE  NEXT PAGE  (" + Keys.Back + " BACK)"; break;
                case "panel":
                {
                    var need = NeedFor(it.Id);
                    prompt = need != null ? "HOLD  RESEAT " + PartLabel(need.Name) : null;
                    break;
                }
                case "rack":
                    if (!RackOpen) { prompt = "RACK LOCKED"; key = null; }
                    else if (it.BayKind == "gun") prompt = "GUN " + (it.Bay.EndsWith("0") ? "1" : "2") + "  " + Loadout.Label(Loadout.Guns[Slot(it)]) + "  SWAP";
                    else prompt = "MISSILE " + (it.Bay.EndsWith("0") ? "1" : "2") + "  " + Loadout.Label(Loadout.Missiles[Slot(it)]) + "  SWAP";
                    break;
            }
        }

        static int Slot(CabinLayout.Interactable it) => it.Bay != null && it.Bay.EndsWith("1") ? 1 : 0;

        public static class Keys
        {
            public static bool Pad => UnityEngine.InputSystem.Gamepad.current != null;
            public static string Context => Pad ? "A" : "E";
            public static string Back => Pad ? "B" : "Q";
            public static string Stand => Pad ? "VIEW" : "Z";
            public static string Confirm => Pad ? "A" : "ENTER";
        }

        // ------------------------------------------------------------------------------------------------ starting an action

        void StartAction(in CabinInputFrame inp)
        {
            if (Frozen) return;
            var it = _aimItem;
            if (it == null) return;
            if (it.Kind == "table" && inp.Back) { TurnPage(-1); return; }
            switch (it.Kind)
            {
                case "breaker":
                    if (!inp.ContextPressed) return;
                    _leverFrom = Powered ? 1f : 0f; _leverTo = Powered ? 0f : 1f;
                    BeginAct(Act.Breaker, it, Cfg.timing.breakerSeconds);
                    break;
                case "table":
                    if (!inp.ContextPressed) return;
                    TurnPage(1);
                    BeginAct(Act.Table, it, Cfg.timing.tapSeconds);
                    break;
                case "rack":
                    if (!inp.ContextPressed || !RackOpen) return;
                    BeginAct(Act.Rack, it, Cfg.timing.rackSwapSeconds);
                    break;
                case "panel":
                    if (!inp.Context) return;
                    BeginAct(Act.Repair, it, 0f);
                    GameEvents.Raise(CabinEvents.Repair, "start", RepairProgressOf(it.Id));
                    break;
            }
        }

        void BeginAct(Act act, CabinLayout.Interactable it, float duration)
        {
            _act = act; _actItem = it; _actT = 0f; _actDur = duration; _actFired = false; _releasedFor = 0f;
        }

        // ------------------------------------------------------------------------------------------------ the main breaker

        void StepBreaker(float dt)
        {
            _actT += dt;
            float s = _actT / Mathf.Max(0.01f, _actDur);
            LeverPos = Mathf.Lerp(_leverFrom, _leverTo, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((s - 0.08f) / 0.54f)));
            if (!_actFired && s >= 0.62f)
            {
                _actFired = true;
                SetPower(!Powered);
            }
            if (s >= 1f) _act = Act.None;
        }

        /// <summary>Throw the main breaker (the clunk): the HUD dies and the cabin goes to its emergency glow, or the ship boots again.</summary>
        public void SetPower(bool on)
        {
            if (on == Powered) return;
            Powered = on;
            Board.Powered = on;
            if (!on) { _poweredOffAt = _now; _relightT = 0f; }
            else _relightT = 0f;
            GameEvents.Raise(CabinEvents.Power, on ? "on" : "off", on ? 1f : 0f);
            MnLog.Info($"CABIN breaker thrown: the ship is {(on ? "powered" : "dark")} at {_now:0.0} s");
            Board.Version++;
        }

        // ------------------------------------------------------------------------------------------------ the hand repair

        sealed class Need { public string Name; public int Index; public string Kind; public string Panel; }

        static string PanelOf(string kind) => kind == "engine" || kind == "reactor" ? "engine" : "arms";

        /// <summary>The part of the player's ship that needs a hand repair at this panel (the first destroyed one), or null.</summary>
        Need NeedFor(string interactableId)
        {
            string panel = interactableId.StartsWith("panel_", StringComparison.Ordinal) ? interactableId.Substring(6) : interactableId;
            var c = World.Combat.Get(World.LocalPlayerShip);
            if (c == null || !c.Alive) return null;
            foreach (var p in c.Parts)
                if (p.Destroyed && PanelOf(p.Kind) == panel) return new Need { Name = p.Name, Index = p.Index, Kind = p.Kind, Panel = panel };
            return null;
        }

        float RepairProgressOf(string interactableId)
        {
            if (interactableId == null) return 0f;
            var n = NeedFor(interactableId);
            return n != null && _repairP.TryGetValue(n.Name, out float p) ? p : 0f;
        }

        void StepRepair(float dt, in CabinInputFrame inp)
        {
            var need = NeedFor(_actItem.Id);
            bool aimed = _aimItem == _actItem || StillAimedAt(_actItem);
            if (need == null) { _act = Act.None; return; }
            _repairP.TryGetValue(need.Name, out float p);
            _actT += dt;                       // how long the hands have been in (the rig reaches them out over the first third of a second)
            if (Board.Jolt > 0.55f && _slip <= 0f && p > 0.02f)
            {
                _slip = Cfg.timing.repairSlipSeconds;
                p = Mathf.Max(0f, p - 0.10f);
                GameEvents.Raise(CabinEvents.Repair, "slip", p);
            }
            if (_slip > 0f) _slip -= dt;
            bool holding = inp.Context && aimed && (_pos - _actItem.Stand).magnitude <= _actItem.Radius;
            if (holding)
            {
                _releasedFor = 0f;
                if (_slip <= 0f) p += dt / Cfg.timing.repairSeconds;
            }
            else
            {
                _releasedFor += dt;
                if (_releasedFor > 0.2f) { _repairP[need.Name] = p; _act = Act.None; return; }
            }
            _repairP[need.Name] = Mathf.Clamp01(p);
            if (p >= 1f) CompleteRepair(need);
        }

        bool StillAimedAt(CabinLayout.Interactable it)
        {
            Vector3 to = it.Pos - _eye;
            return to.sqrMagnitude > 1e-6f && Vector3.Dot(LookDir, to.normalized) > 0.62f;
        }

        void CompleteRepair(Need need)
        {
            var c = World.Combat.Get(World.LocalPlayerShip);
            var part = c?.FindPart(need.Name);
            if (part != null)
            {
                if (_host != null) _host.RestorePart(World.LocalPlayerShip, part.Index);
                else part.Hp = Mathf.Max(part.Hp, part.MaxHp * 0.25f);
            }
            _repairP.Remove(need.Name);
            _act = Act.None;
            GameEvents.Raise(CabinEvents.Repair, "done", 1f);
            _rt?.Note("cabin", $"hand repair of {need.Name} done");
            MnLog.Info($"CABIN hand repair of {need.Name} done at {_now:0.0} s");
            Board.Version++;
        }

        void DecayRepairs(float dt, string except)
        {
            if (_repairP.Count == 0) return;
            List<string> drop = null;
            var keys = new List<string>(_repairP.Keys);
            foreach (var k in keys)
            {
                float p = _repairP[k] - Cfg.timing.repairDecayPerSecond * dt;
                if (p <= 0f) (drop ??= new List<string>()).Add(k); else _repairP[k] = p;
            }
            if (drop != null) foreach (var k in drop) _repairP.Remove(k);
        }

        // ------------------------------------------------------------------------------------------------ the weapon rack

        void StepRack(float dt)
        {
            _actT += dt;
            float s = _actT / Mathf.Max(0.01f, _actDur);
            if (!_actFired && s >= 0.5f)
            {
                _actFired = true;
                bool gun = _actItem.BayKind == "gun";
                int slot = Slot(_actItem);
                string id = Loadout.Next(gun, slot);
                var c = World.Combat.Get(World.LocalPlayerShip);
                Loadout.Apply(c);
                GameEvents.Raise(CabinEvents.Loadout, id, slot + (gun ? 0 : 2));
                _rt?.Note("cabin", $"rack: {_actItem.Bay} loaded with {id}");
                MnLog.Info($"CABIN rack {_actItem.Bay} -> {id}");
                Board.Version++;
            }
            if (s >= 1f) _act = Act.None;
        }

        // ------------------------------------------------------------------------------------------------ the plan table

        void UpdateTableMode()
        {
            int mode = _prelaunch ? 0 : (_rt != null && _rt.Ended) ? 2 : 1;
            if (mode == _tableMode && _pages.Count > 0) return;
            _tableMode = mode;
            BuildPages(mode);
            TablePage = 0;
            Board.Version++;
        }

        void BuildPages(int mode)
        {
            _pages.Clear();
            var f = File;
            if (mode == 2) _pages.Add(new TablePageInfo { Kind = TableKind.Debrief, Title = "DEBRIEF" });
            if (mode == 0 && f != null)
            {
                for (int i = 0; i < f.Briefing.Stages.Count; i++) _pages.Add(new TablePageInfo { Kind = TableKind.Briefing, Index = i, Title = (f.Briefing.Title ?? f.Name).ToUpperInvariant() });
                for (int i = 0; i < f.Briefing.Questions.Count; i++) _pages.Add(new TablePageInfo { Kind = TableKind.Question, Index = i, Title = "QUESTION" });
            }
            if (mode == 1) _pages.Add(new TablePageInfo { Kind = TableKind.Map, Title = "BATTLE MAP" });
            _pages.Add(new TablePageInfo { Kind = TableKind.Objectives, Title = "OBJECTIVES" });
            if (mode != 1) _pages.Add(new TablePageInfo { Kind = TableKind.Map, Title = "BATTLE MAP" });
        }

        void TurnPage(int d)
        {
            if (_pages.Count == 0) return;
            TablePage = (TablePage + d + _pages.Count) % _pages.Count;
            GameEvents.Raise(CabinEvents.Table, CurrentPage.Kind == TableKind.Map ? "map" : "page", TablePage);
            Board.Version++;
        }
    }
}
