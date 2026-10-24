using System.Collections.Generic;
using System.Globalization;
using Monolith.Core;
using UnityEngine;

namespace Monolith.Mission
{
    /// <summary>
    /// The mission's radio channel: messages queue and go out one at a time as <c>radio_line</c> events (the HUD shows them), each holding the
    /// channel for a few seconds. High priority goes to the front, low priority is dropped when the channel is busy. The speaker is settled
    /// when the message is sent: a ship that is gone says nothing (its <c>fallbackFrom</c> speaks instead, if given); "&lt;any wingman&gt;" is
    /// a living wingman (else the player); "#Name" is someone off the battlefield. A message's voice clip goes out as <c>radio_voice</c>
    /// for the audio system to come.
    /// </summary>
    public sealed class MissionRadio
    {
        sealed class Line { public string Text, Voice; public int Speaker; public float Seconds; public bool High; }

        readonly MissionRuntime _rt;
        readonly List<Line> _queue = new List<Line>();
        float _busyUntil = -1f;

        /// <summary>Every line that went out, "SPEAKER: TEXT", in order.</summary>
        public readonly List<string> Said = new List<string>();
        public int Dropped { get; private set; }

        public MissionRadio(MissionRuntime rt) { _rt = rt; }

        public void Send(MissionFile.MessageDef m)
        {
            if (m == null) return;
            if (!Speaker(m.From, out int id, out string who) && !(m.FallbackFrom != null && Speaker(m.FallbackFrom, out id, out who)))
            {
                Dropped++;
                _rt.Note("radio", $"(dropped: {m.From} is not there) {m.Text}");
                return;
            }
            var line = new Line
            {
                Text = (who.Length > 0 ? who + ": " : "") + m.Text, Speaker = id, Voice = m.Voice, High = m.Priority == "high",
                Seconds = m.Seconds > 0f ? m.Seconds : Mathf.Clamp(1.2f + 0.05f * m.Text.Length, 2.5f, 6f),
            };
            if (m.Priority == "low" && (_queue.Count > 0 || _rt.Now < _busyUntil)) { Dropped++; _rt.Note("radio", "(dropped: channel busy) " + line.Text); return; }
            if (line.High)
            {
                int at = 0;
                while (at < _queue.Count && _queue[at].High) at++;
                _queue.Insert(at, line);
            }
            else _queue.Add(line);
        }

        bool Speaker(string from, out int id, out string who)
        {
            id = GameWorld.NoShip; who = "";
            if (string.IsNullOrEmpty(from)) return false;
            if (from[0] == '#') { who = from.Substring(1); return true; }
            if (from == MissionFile.AnyWingman)
            {
                var w = _rt.AnyWingman();
                if (w != null) { id = w.Id; who = w.Callsign; return true; }
                var p = _rt.World.Ships.Get(_rt.World.LocalPlayerShip);
                if (p != null) { id = p.Id; who = p.Callsign; return true; }
                return false;
            }
            var s = _rt.ShipOf(from);
            if (s == null) return false;
            id = s.Id; who = s.Callsign;
            return true;
        }

        public void Step(float now)
        {
            if (_queue.Count == 0 || now < _busyUntil) return;
            var line = _queue[0];
            _queue.RemoveAt(0);
            _busyUntil = now + line.Seconds;
            Said.Add(line.Text);
            GameEvents.Raise(CombatEvents.RadioLine, line.Text.ToUpperInvariant(), 0f, line.Speaker);
            if (!string.IsNullOrEmpty(line.Voice)) GameEvents.Raise(MissionEvents.RadioVoice, line.Voice, 0f, line.Speaker);
            _rt.Note("radio", line.Text);
            MnLog.Info("RADIO " + line.Text);
        }

        public void Clear() { _queue.Clear(); _busyUntil = -1f; }
        public int Waiting => _queue.Count;
    }

    /// <summary>
    /// The music state (FS2's music.tbl: ambience, three battle intensities, arrival stings, victory, failure, death). It follows the fight by
    /// itself (auto: battle 1, 2 or 3 by how many hostiles are close or on the player, back to ambient after a quiet spell) unless the mission
    /// forces a state; arrivals, objectives and the end play stings and states. It only publishes <c>music_state</c> / <c>music_sting</c> and
    /// <see cref="MissionBoard.Music"/>: the audio system that plays the tracks comes later.
    /// </summary>
    public sealed class MissionMusic
    {
        public static readonly string[] States = { "ambient", "battle1", "battle2", "battle3", "victory", "failure", "death" };
        public static readonly string[] Stings = { "enemy_arrival", "friendly_arrival", "goal_complete", "goal_failed" };
        public static bool IsState(string s) => System.Array.IndexOf(States, s) >= 0;
        public static bool IsSting(string s) => System.Array.IndexOf(Stings, s) >= 0;

        /// <summary>Seconds a lower intensity must hold before the music drops to it (it rises at once).</summary>
        public const float DropAfter = 8f;
        /// <summary>Hostiles within this range count as close (metres).</summary>
        public const float CloseRange = 3000f;

        readonly MissionRuntime _rt;
        readonly Dictionary<string, string> _tracks;
        bool _auto, _final;
        float _forcedUntil = -1f, _lowerSince = -1f;
        public string State { get; private set; }

        public MissionMusic(MissionRuntime rt, Dictionary<string, string> tracks) { _rt = rt; _tracks = tracks; }

        public void Start(string state, bool auto) { State = null; _auto = auto; _final = false; _forcedUntil = -1f; Set(state); }

        public void SetAuto(bool auto) { _auto = auto; _forcedUntil = -1f; _rt.Note("music", "auto " + (auto ? "on" : "off")); }

        /// <summary>Hold a state: for <paramref name="hold"/> seconds and then back to auto, or (0) until the mission says otherwise.</summary>
        public void Force(string state, float hold)
        {
            if (hold > 0f) _forcedUntil = _rt.Now + hold; else _auto = false;
            Set(state);
        }

        public void Sting(string sting)
        {
            if (_final) return;
            GameEvents.Raise(MissionEvents.MusicSting, sting);
            _rt.Note("music", "sting " + sting);
        }

        /// <summary>Once a second: what the fight says the music should be.</summary>
        public void Sample(int close, int onPlayer, bool capitalClose)
        {
            if (!_auto || _final || _rt.Now < _forcedUntil) return;
            int threat = Mathf.Max(close, onPlayer * 2);
            string want = threat == 0 && !capitalClose ? "ambient" : threat <= 3 && !capitalClose ? "battle1" : threat <= 7 ? "battle2" : "battle3";
            if (Rank(want) >= Rank(State)) { _lowerSince = -1f; Set(want); return; }
            if (_lowerSince < 0f) _lowerSince = _rt.Now;
            if (_rt.Now - _lowerSince >= DropAfter) { _lowerSince = -1f; Set(want); }
        }

        public void OnEnd(string result, bool playerDead)
        {
            Set(playerDead ? "death" : result == "victory" ? "victory" : result == "defeat" ? "failure" : State);
            _final = true;
        }

        static int Rank(string s) => s == "battle3" ? 3 : s == "battle2" ? 2 : s == "battle1" ? 1 : 0;

        void Set(string state)
        {
            if (state == null || state == State) return;
            State = state;
            _rt.Board.Music = state;
            _rt.Board.MusicTrack = _tracks != null && _tracks.TryGetValue(state, out var t) ? t : null;
            _rt.Board.Version++;
            GameEvents.Raise(MissionEvents.MusicState, state);
            _rt.Note("music", state + (_rt.Board.MusicTrack != null ? " (" + _rt.Board.MusicTrack + ")" : ""));
        }
    }
}
