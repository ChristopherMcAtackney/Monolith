using System;
using System.Collections.Generic;
using Monolith.Core;
using UnityEngine;

namespace Monolith.Audio
{
    /// <summary>
    /// Plays the score in two layers. The BASE follows the fight (FS2's table: ambience, three battle intensities, arrival stings, victory, failure, death): a louder state comes in on
    /// the next beat, a quieter one waits for the bar and takes its time, the ending pieces play once, and every loop of the built-in score is the same length and the same harmony,
    /// so a change is a cross-fade that never loses its place. On top of it sit THEMES, tracks tied to a moment of a mission (the Lector arriving): a theme fades in over the base
    /// (the base keeps playing underneath, ducked, or silent when the theme replaces it), takes over, and fades out when its moment ends and the base is back; the higher priority
    /// wins when two are on at once. Stings play over everything.
    /// Every slot is the built-in synthesised track unless the player's music.json names a file for it (MP3, WAV or OGG) and says how it fades; a mission may also name its own track
    /// for a base state (the player's file in the Audio folder). A changed track with a known tempo (the built-in score's 112 BPM, or a bpm in the music.json) changes on the beat
    /// or the bar of that tempo; one without is a time-based cross-fade.
    /// </summary>
    public sealed class MusicConductor
    {
        readonly Mixer _mixer;
        readonly AudioLibrary _lib;
        readonly MonoBehaviour _host;

        public string State { get; private set; }
        public string Track { get; private set; }
        public int Changes { get; private set; }
        public int Stings { get; private set; }
        public readonly List<string> Log = new List<string>();
        /// <summary>The mission the game is in, so the player's music.json can name a slot for one mission ("escort/lector").</summary>
        public Func<string> MissionOf;
        string MissionId => MissionOf != null ? MissionOf() : null;

        readonly HashSet<string> _noted = new HashSet<string>();
        string _wantState, _wantTrack;
        float _wantSince, _lastNow;
        Clip _playing;
        bool _started;
        float _gridBpm = 112f;                  // the tempo the music clock is on: the built-in score's, a track of the player's that says its own, or 0 when it is not known

        public MusicConductor(Mixer mixer, AudioLibrary lib, MonoBehaviour host) { _mixer = mixer; _lib = lib; _host = host; }

        static int Rank(string s) => s == "ambient" ? 0 : s == "battle1" ? 1 : s == "battle2" ? 2 : s == "battle3" ? 3 : 9;
        public bool InBattle => Rank(State ?? "ambient") is >= 1 and <= 3;

        public void Stop(float fadeSeconds = 1.5f)
        {
            _mixer.MusicStop(fadeSeconds);
            _playing = null; State = null; Track = null; _wantState = null;
            _themes.Clear(); _current = null;
        }

        public void OnState(string state, string missionTrack, float now)
        {
            _lastNow = now;
            _wantState = state; _wantTrack = missionTrack; _wantSince = now;
            TryApply(now);
        }

        public void Update(float now)
        {
            _lastNow = now;
            if (_wantState != null) TryApply(now);
            if (_pendingSting != null) TrySting(now);
            if (_themes.Count > 0) RefreshThemes(now);
        }

        static bool NaN(float v) => float.IsNaN(v);

        /// <summary>Does a track of this tempo change on the music clock's beat or bar? (Both must be known and agree; the built-in score is 112 throughout.)</summary>
        bool OnGrid(float bpm) => _gridBpm > 0f && bpm > 0f && Math.Abs(bpm - _gridBpm) <= _gridBpm * 0.01f;

        // ------------------------------------------------------------------------------------------------ the base

        void TryApply(float now)
        {
            if (!_lib.Ready) return;
            MusicTrackInfo info = null;
            MusicChoice choice = null;
            string name = null;
            if (!string.IsNullOrEmpty(_wantTrack))
            {
                info = _lib.FindMusic(_wantTrack, _host, out bool pending);
                if (info != null) name = _wantTrack;
                else if (pending && now - _wantSince < 4f) return;                                  // being loaded: wait a moment for it
                else if (_noted.Add(_wantTrack)) { Log.Add($"the mission names track '{_wantTrack}' for {_wantState}: not there (music/{_wantTrack} in the Audio folder replaces it); the built-in {_wantState} plays"); MnLog.Info("MUSIC " + Log[Log.Count - 1]); }
            }
            if (info == null)
            {
                string section = Rank(_wantState) == 9 ? "endings" : "base";
                choice = _lib.ResolveMusic(section, MissionId, _wantState, _wantState, _host, out bool pending);
                if (choice == null && pending && now - _wantSince < 4f) return;                     // the player's file is loading
                if (choice != null) { info = choice.Info; name = choice.Name; }
            }
            if (info == null || info.Clip == null)
            {
                if (now - _wantSince > 5f) { Warn($"no music for state '{_wantState}'"); _wantState = null; }
                return;
            }
            var cfg = choice?.Cfg;
            string state = _wantState;
            _wantState = null;
            if (info.Clip == _playing && state == State) return;

            int from = Rank(State ?? "ambient"), to = Rank(state);
            bool loops = cfg?.Loop ?? info.Loop;
            bool oneShot = !loops || to == 9;
            Quant q; float fadeIn, fadeOut;
            if (!_started || State == null) { q = Quant.Now; fadeIn = state == "ambient" ? 2.5f : 0.4f; fadeOut = 0.5f; }
            else if (state == "death") { q = Quant.Now; fadeIn = 0.02f; fadeOut = 0.35f; }
            else if (oneShot) { q = Quant.Beat; fadeIn = 0.05f; fadeOut = state == "failure" ? 2.4f : 1.4f; }
            else if (to > from) { q = Quant.Beat; fadeIn = 0.85f; fadeOut = 0.85f; }
            else { q = Quant.Bar; fadeIn = 2.4f; fadeOut = 3.4f; }
            if (cfg != null) { if (!NaN(cfg.FadeIn)) fadeIn = cfg.FadeIn; if (!NaN(cfg.FadeOut)) fadeOut = cfg.FadeOut; }
            float levelDb = cfg != null ? cfg.VolumeDb : 0f;

            // the music clock: a track of the player's that gives its tempo puts the clock on it, so the next track of that tempo changes on its beat or bar; one that does not is a
            // time-based cross-fade; a built-in track puts the clock back on the score's 112
            bool user = choice != null && choice.User;
            bool idle = !_started || State == null || !_mixer.MusicActive;
            if (user)
            {
                if (info.Bpm > 0f)
                {
                    if (idle || Math.Abs(info.Bpm - _gridBpm) > 0.01f)
                    {
                        _gridBpm = info.Bpm;
                        _mixer.SetMusicTempo(info.Bpm, Math.Max(1, cfg.BeatsPerBar), !oneShot ? info.Seconds : 0f);
                        if (idle) q = Quant.Now;
                    }
                }
                else { _gridBpm = 0f; q = Quant.Now; }
            }
            else if (Math.Abs(_gridBpm - _lib.Bpm) > 0.01f)
            {
                _gridBpm = _lib.Bpm;
                _mixer.SetMusicTempo(_lib.Bpm, _lib.BeatsPerBar, _lib.LoopSeconds);
                q = Quant.Now;
            }
            _mixer.MusicPlay(info.Clip, q, fadeIn, fadeOut, loops && !oneShot, levelDb);
            _playing = info.Clip; _started = true; Changes++;
            if (to == 9) StopThemes(state == "death" ? 0.4f : 1.5f);                                  // the mission is over: every theme goes with it
            string line = $"{(State ?? "(start)")} -> {state} ({name}{(info.Source != "built in" ? ", " + info.Source : "")}) on the {(q == Quant.Now ? "spot" : q == Quant.Beat ? "beat" : "bar")}";
            State = state; Track = name;
            Log.Add(line);
            MnLog.Info("MUSIC " + line);
        }

        // ------------------------------------------------------------------------------------------------ stings

        string _pendingSting; float _pendingStingSince;

        public void OnSting(string sting)
        {
            _pendingSting = sting; _pendingStingSince = _lastNow;
            TrySting(_lastNow);
        }

        void TrySting(float now)
        {
            string sting = _pendingSting;
            string key, track;
            switch (sting)
            {
                case "enemy_arrival": key = InBattle ? "enemy_arrival_battle" : "enemy_arrival"; track = InBattle ? "sting_enemy_battle" : "sting_enemy"; break;
                case "friendly_arrival": key = InBattle ? "friendly_arrival_battle" : "friendly_arrival"; track = InBattle ? "sting_friendly_battle" : "sting_friendly"; break;
                case "goal_complete": key = "goal_complete"; track = "sting_goal"; break;
                case "goal_failed": key = "goal_failed"; track = "sting_fail"; break;
                default: Warn("unknown sting " + sting); _pendingSting = null; return;
            }
            var choice = _lib.ResolveMusic("stings", MissionId, key, track, _host, out bool pending);
            if (choice == null && pending && now - _pendingStingSince < 2f) return;           // the player's file is still loading: a moment's grace
            _pendingSting = null;
            if (choice == null || choice.Info == null || choice.Info.Clip == null) { Warn($"sting '{sting}' ({track}) is not loaded"); return; }
            var cfg = choice.Cfg;
            Quant q = Quant.Beat;
            if (choice.User && !OnGrid(choice.Info.Bpm)) q = Quant.Now;
            _mixer.MusicSting(choice.Info.Clip, q, cfg != null ? cfg.VolumeDb : 0f, -3.5f);
            Stings++;
            string line = $"sting {sting} ({choice.Name}{(choice.Info.Source != "built in" ? ", " + choice.Info.Source : "")})";
            Log.Add(line);
            MnLog.Info("MUSIC " + line);
        }

        // ------------------------------------------------------------------------------------------------ themes

        sealed class ActiveTheme
        {
            public ThemeEvent Ev; public string Name, Mission, Key;
            public MusicChoice Choice;
            public float Since, Priority, FadeIn, FadeOut, VolumeDb, DuckDb;
            public bool Replace;
            public int Slot = -1;
        }

        readonly List<ActiveTheme> _themes = new List<ActiveTheme>();
        ActiveTheme _current;
        int _slotCounter;
        public int ThemeStarts { get; private set; }
        /// <summary>The theme playing now (its name, e.g. "lector"), or null.</summary>
        public string ThemePlaying => _current != null ? _current.Name : null;
        /// <summary>Every theme that is on (playing, or waiting behind a higher priority).</summary>
        public int ThemesOn => _themes.Count;

        public void OnTheme(ThemeEvent ev, float now)
        {
            if (ev == null) return;
            _lastNow = now;
            if (ev.Name == ThemeEvent.StopAll) { StopThemes(1.0f); return; }
            if (string.IsNullOrEmpty(ev.Mission)) ev.Mission = MissionId;
            var cur = _themes.Find(t => t.Name == ev.Name && t.Mission == ev.Mission);
            if (!ev.On)
            {
                if (cur == null) { Log.Add($"theme {ev.Name} off: it was not on"); return; }
                _themes.Remove(cur);
                float fo = !NaN(ev.FadeOut) ? ev.FadeOut : cur.FadeOut;
                string l = $"theme {cur.Name} off (fades out over {fo:0.#} s)";
                Log.Add(l); MnLog.Info("MUSIC " + l);
                if (cur == _current) { StopCurrent(fo); RefreshThemes(now); }
                return;
            }
            if (cur == null) _themes.Add(cur = new ActiveTheme { Name = ev.Name, Mission = ev.Mission, Key = ev.Key });
            else if (cur == _current) { Log.Add($"theme {ev.Name} on: already playing"); return; }
            cur.Ev = ev; cur.Since = now; cur.Choice = null;
            RefreshThemes(now);
        }

        public void StopThemes(float fade)
        {
            if (_themes.Count == 0 && _current == null) return;
            _themes.Clear();
            _mixer.ThemeStop(-1, fade);
            if (_current != null) { string l = $"themes off (fade {fade:0.#} s)"; Log.Add(l); MnLog.Info("MUSIC " + l); }
            _current = null;
        }

        void StopCurrent(float fade)
        {
            if (_current == null) return;
            _mixer.ThemeStop(_current.Slot, fade);
            _current = null;
        }

        /// <summary>Resolve what is waiting for a sound and start whichever theme should be playing: the highest priority, the latest on a tie.</summary>
        void RefreshThemes(float now)
        {
            if (!_lib.Ready) return;
            for (int i = _themes.Count - 1; i >= 0; i--)
            {
                var t = _themes[i];
                if (t.Choice != null) continue;
                var c = _lib.FindTheme(t.Mission, t.Name, _host, out bool pending);
                if (c == null)
                {
                    if (pending && now - t.Since < 4f) continue;                                  // the player's file is loading
                    string l = $"WARN theme '{t.Key}' has no sound: no entry for it in music.json and no built-in theme of that name; nothing plays";
                    if (_noted.Add(t.Key)) { Log.Add(l); MnLog.Warn("MUSIC " + l); }
                    _themes.RemoveAt(i);
                    continue;
                }
                t.Choice = c;
                var cfg = c.Cfg; var def = c.Def; var ev = t.Ev;
                t.FadeIn = !NaN(ev.FadeIn) ? ev.FadeIn : cfg != null && !NaN(cfg.FadeIn) ? cfg.FadeIn : def != null ? def.FadeIn : 4f;
                t.FadeOut = !NaN(ev.FadeOut) ? ev.FadeOut : cfg != null && !NaN(cfg.FadeOut) ? cfg.FadeOut : def != null ? def.FadeOut : 6f;
                t.Priority = !NaN(ev.Priority) ? ev.Priority : cfg != null && !NaN(cfg.Priority) ? cfg.Priority : def != null ? def.Priority : 10f;
                t.VolumeDb = (!NaN(ev.VolumeDb) ? ev.VolumeDb : 0f) + (cfg != null ? cfg.VolumeDb : def != null ? def.VolumeDb : 0f);
                string mode = ev.Mode ?? (cfg != null ? cfg.Mode : null) ?? (def != null ? def.Mode : "duck");
                t.Replace = mode == "replace";
                t.DuckDb = !NaN(ev.DuckDb) ? ev.DuckDb : cfg != null && !NaN(cfg.DuckDb) ? cfg.DuckDb : def != null ? def.DuckDb : -10f;
            }
            ActiveTheme top = null;
            foreach (var t in _themes)
            {
                if (t.Choice == null) continue;
                if (top == null || t.Priority > top.Priority || (t.Priority == top.Priority && t.Since > top.Since)) top = t;
            }
            if (top == _current) return;
            if (_current != null) StopCurrent(_current.FadeOut);
            if (top == null) return;
            var info = top.Choice.Info;
            bool loop = top.Choice.Cfg?.Loop ?? info.Loop;
            // on the bar of the music clock when the theme and the base are the same tempo; otherwise now, with its fade-in
            Quant q = OnGrid(info.Bpm) ? Quant.Bar : Quant.Now;
            float duck = top.Replace ? 0f : Mathf.Pow(10f, top.DuckDb / 20f);
            top.Slot = _slotCounter++;
            _mixer.ThemePlay(top.Slot, info.Clip, q, top.FadeIn, top.FadeOut, top.VolumeDb, duck, loop);
            _current = top; ThemeStarts++;
            string line = $"theme {top.Name} on ({top.Choice.Name}{(info.Source != "built in" ? ", " + info.Source : ", built-in")}; in {top.FadeIn:0.#} s, out {top.FadeOut:0.#} s, priority {top.Priority:0.#}, {(top.Replace ? "replaces the base" : "base ducked " + top.DuckDb.ToString("0.#") + " dB")}, {(q == Quant.Bar ? "on the bar" : "now")})";
            Log.Add(line); MnLog.Info("MUSIC " + line);
        }

        void Warn(string m) { Log.Add("WARN " + m); MnLog.Warn("MUSIC " + m); }
    }
}
