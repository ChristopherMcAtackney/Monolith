using System;
using System.Collections.Generic;
using Monolith.Core;
using UnityEngine;

namespace Monolith.Audio
{
    /// <summary>
    /// Plays the score from the mission's music states and stings (FS2's table: ambience, three battle intensities, arrival stings,
    /// victory, failure, death). A louder state comes in on the next beat; a quieter one waits for the bar and takes its time; the ending
    /// pieces play once. Every loop of the score is the same length and the same harmony, so a change is a cross-fade that never loses its
    /// place. A mission may name its own track for a state (the player's MP3 in the Audio folder, say); if it is not there the built-in one plays.
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

        readonly HashSet<string> _noted = new HashSet<string>();
        string _wantState, _wantTrack;
        float _wantSince;
        Clip _playing;
        bool _started;

        public MusicConductor(Mixer mixer, AudioLibrary lib, MonoBehaviour host) { _mixer = mixer; _lib = lib; _host = host; }

        static int Rank(string s) => s == "ambient" ? 0 : s == "battle1" ? 1 : s == "battle2" ? 2 : s == "battle3" ? 3 : 9;
        public bool InBattle => Rank(State ?? "ambient") is >= 1 and <= 3;

        public void Stop(float fadeSeconds = 1.5f)
        {
            _mixer.MusicStop(fadeSeconds);
            _playing = null; State = null; Track = null; _wantState = null;
        }

        public void OnState(string state, string missionTrack, float now)
        {
            _wantState = state; _wantTrack = missionTrack; _wantSince = now;
            TryApply(now);
        }

        public void Update(float now)
        {
            if (_wantState != null) TryApply(now);
        }

        void TryApply(float now)
        {
            if (!_lib.Ready) return;
            MusicTrackInfo info = null;
            string name = null;
            if (!string.IsNullOrEmpty(_wantTrack))
            {
                info = _lib.FindMusic(_wantTrack, _host, out bool pending);
                if (info != null) name = _wantTrack;
                else if (pending && now - _wantSince < 4f) return;                                  // being loaded: wait a moment for it
                else if (_noted.Add(_wantTrack)) { Log.Add($"the mission names track '{_wantTrack}' for {_wantState}: not there (music/{_wantTrack} in the Audio folder replaces it); the built-in {_wantState} plays"); MnLog.Info("MUSIC " + Log[Log.Count - 1]); }
            }
            if (info == null) { name = _wantState; info = _lib.FindMusic(_wantState, _host); }
            if (info == null || info.Clip == null)
            {
                if (now - _wantSince > 5f) { Warn($"no music for state '{_wantState}'"); _wantState = null; }
                return;
            }
            string state = _wantState;
            _wantState = null;
            if (info.Clip == _playing && state == State) return;

            int from = Rank(State ?? "ambient"), to = Rank(state);
            bool oneShot = !info.Loop || to == 9;
            Quant q; float fadeIn, fadeOut;
            if (!_started || State == null) { q = Quant.Now; fadeIn = state == "ambient" ? 2.5f : 0.4f; fadeOut = 0.5f; }
            else if (state == "death") { q = Quant.Now; fadeIn = 0.02f; fadeOut = 0.35f; }
            else if (oneShot) { q = Quant.Beat; fadeIn = 0.05f; fadeOut = state == "failure" ? 2.4f : 1.4f; }
            else if (to > from) { q = Quant.Beat; fadeIn = 0.85f; fadeOut = 0.85f; }
            else { q = Quant.Bar; fadeIn = 2.4f; fadeOut = 3.4f; }
            _mixer.MusicPlay(info.Clip, q, fadeIn, fadeOut, info.Loop && !oneShot);
            _playing = info.Clip; _started = true; Changes++;
            string line = $"{(State ?? "(start)")} -> {state} ({name}{(info.Source != "built in" ? ", " + info.Source : "")}) on the {(q == Quant.Now ? "spot" : q == Quant.Beat ? "beat" : "bar")}";
            State = state; Track = name;
            Log.Add(line);
            MnLog.Info("MUSIC " + line);
        }

        public void OnSting(string sting)
        {
            string name;
            switch (sting)
            {
                case "enemy_arrival": name = InBattle ? "sting_enemy_battle" : "sting_enemy"; break;
                case "friendly_arrival": name = InBattle ? "sting_friendly_battle" : "sting_friendly"; break;
                case "goal_complete": name = "sting_goal"; break;
                case "goal_failed": name = "sting_fail"; break;
                default: Warn("unknown sting " + sting); return;
            }
            var info = _lib.FindMusic(name, _host);
            if (info == null || info.Clip == null) { Warn($"sting '{sting}' ({name}) is not loaded"); return; }
            _mixer.MusicSting(info.Clip, Quant.Beat, 0f, -3.5f);
            Stings++;
            string line = $"sting {sting} ({name})";
            Log.Add(line);
            MnLog.Info("MUSIC " + line);
        }

        void Warn(string m) { Log.Add("WARN " + m); MnLog.Warn("MUSIC " + m); }
    }
}
