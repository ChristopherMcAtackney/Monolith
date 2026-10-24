using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Monolith.Core;
using UnityEngine;

namespace Monolith.Audio
{
    /// <summary>
    /// Speaks the radio. A <c>radio_line</c> event ("SABLE 2: COPY. GOING IN.") is matched to its recorded clip through the voice index (the
    /// speaker's voice first, then any voice that has the same text); a mission message's own <c>voice</c> slot, or a file the player dropped
    /// in, wins over the index. A line with no clip gets a squelch in and out and is logged, never silence and never an error. Lines go out
    /// one at a time, as on a real channel: a second waits its turn, and a third waiting too long is dropped.
    /// </summary>
    public sealed class RadioDirector
    {
        sealed class Pending
        {
            public string Display, Speaker, Text, Norm, Slot, Voice;
            public int Entity;
            public float Born, Wait;
        }

        readonly Mixer _mixer;
        readonly AudioLibrary _lib;
        readonly MonoBehaviour _host;
        readonly List<Pending> _queue = new List<Pending>();
        Pending _fresh;
        float _busyUntil, _closeAt = -1f;
        public int Lines, Voiced, OtherVoice, Fallbacks, Dropped, FromSlot, FromUser;
        public readonly List<string> Missing = new List<string>();
        public readonly List<string> Log = new List<string>();
        public Func<string, string> CallsignOf;                // ship id -> callsign (the speaker when the line carries none)
        /// <summary>How hard the crew is working right now, 0 calm to 2 frantic (from the music state: ambient, battle 1, battles 2 and 3). A line made at several intensities (the wingmen's acknowledgements) plays the one nearest this.</summary>
        public Func<int> Mood = () => 0;
        public int FranticPlayed, TensePlayed, CalmPlayed;
        public Func<bool> Silent = () => false;

        public RadioDirector(Mixer mixer, AudioLibrary lib, MonoBehaviour host) { _mixer = mixer; _lib = lib; _host = host; }

        public static string Normalise(string s) => Regex.Replace((s ?? "").Trim().ToUpperInvariant(), @"\s+", " ");

        /// <summary>Split "SPEAKER: TEXT" into its parts (the speaker may be empty).</summary>
        public static void Split(string display, out string speaker, out string text)
        {
            int i = (display ?? "").IndexOf(": ", StringComparison.Ordinal);
            if (i > 0 && i < 24) { speaker = display.Substring(0, i); text = display.Substring(i + 2); }
            else { speaker = ""; text = display ?? ""; }
        }

        /// <summary>Which voice says this line, and which clip (null with no clip anywhere). For the self-test as much as for play.</summary>
        public VoiceClip Resolve(string display, out string voice, out bool otherVoice, int mood = -1)
        {
            if (mood < 0) mood = Mood();
            Split(display, out var speaker, out var text);
            voice = _lib.Cast.VoiceOf(speaker);
            if (voice == null && speaker.Length > 0)
            {
                // an unknown speaker keeps the same voice every time: a stable pick among the wingmen's
                var w = _lib.Cast.WingmanVoices;
                voice = w[(int)(Dsp.StableHash(speaker.ToLowerInvariant()) % (uint)w.Length)];
            }
            voice ??= _lib.Cast.Unknown;
            otherVoice = false;
            if (!_lib.VoicesByText.TryGetValue(Normalise(text), out var list)) return null;
            var best = Nearest(list, voice, mood);
            if (best != null) return best;
            best = Nearest(list, null, mood);
            if (best != null) otherVoice = true;
            return best;
        }

        /// <summary>The loaded clip of a voice (or of any voice when <paramref name="voice"/> is null) whose intensity is nearest the mood; the more urgent when two are equally near.</summary>
        static VoiceClip Nearest(List<VoiceClip> list, string voice, int mood)
        {
            VoiceClip best = null; int bestD = int.MaxValue;
            foreach (var c in list)
            {
                if (c.Clip == null || (voice != null && c.Voice != voice)) continue;
                int d = Math.Abs(c.Intensity - mood) * 2 - (c.Intensity > mood ? 1 : 0);
                if (d < bestD) { bestD = d; best = c; }
            }
            return best;
        }

        public void OnLine(GameEvent e)
        {
            if (_fresh != null) Commit(_fresh);
            Split(e.Subject, out var speaker, out var text);
            _fresh = new Pending { Display = e.Subject, Speaker = speaker, Text = text, Norm = Normalise(text), Entity = e.Entity, Born = Time.unscaledTime };
        }

        public void OnVoiceSlot(GameEvent e)
        {
            if (_fresh != null) _fresh.Slot = e.Subject;
        }

        void Commit(Pending p)
        {
            Lines++;
            if (_queue.Count >= 3) { var old = _queue[0]; _queue.RemoveAt(0); Dropped++; Log.Add("dropped (channel busy): " + old.Display); }
            p.Born = Time.unscaledTime;
            _queue.Add(p);
        }

        /// <summary>Once a tick: whatever line was raised this tick is complete now (its voice slot, if any, came right after it).</summary>
        public void Flush()
        {
            if (_fresh != null) { Commit(_fresh); _fresh = null; }
        }

        public void Update(float now)
        {
            if (_closeAt >= 0f && now >= _closeAt) { PlayUi("squelch_close"); _closeAt = -1f; }
            if (_queue.Count == 0 || now < _busyUntil || !_lib.Ready) return;
            var p = _queue[0];
            // a clip named by the mission, still loading: wait a moment for it
            Clip slotClip = null;
            if (!string.IsNullOrEmpty(p.Slot))
            {
                Resolve(p.Display, out var v0, out _);
                slotClip = _lib.FindVoiceSlot(p.Slot, _host, v0);
                if (slotClip == null && Time.unscaledTime - p.Born < 0.6f && SlotExists(p.Slot)) return;
            }
            _queue.RemoveAt(0);
            if (Silent()) return;
            if (Time.unscaledTime - p.Born > 12f) { Dropped++; Log.Add("dropped (stale): " + p.Display); return; }

            Clip clip = slotClip; string what = null; bool other = false, earth = false;
            if (clip != null) { FromSlot++; what = p.Slot; }
            else
            {
                var vc = Resolve(p.Display, out _, out other);
                if (vc != null)
                {
                    earth = vc.Voice != null && vc.Voice.StartsWith("earth", StringComparison.Ordinal);
                    clip = vc.Clip; what = vc.File + (vc.Source != "built in" ? " (yours)" : ""); if (vc.Source != "built in") FromUser++; if (other) OtherVoice++;
                    if (vc.Intensity == 2) FranticPlayed++; else if (vc.Intensity == 1) TensePlayed++; else CalmPlayed++;
                }
            }
            if (clip != null)
            {
                Voiced++;
                _mixer.Play(clip, new PlayParams { Gain = 1f, Pan = 0f, Pitch = 1f, Bus = Bus.Voice, Priority = 9f });
                // Earth's clips end in a long hall (3.4 s of reverb tail): the next line may start in it, or the crew would wait out the god's echo
                _busyUntil = now + (earth ? Mathf.Max(1.5f, clip.Seconds - 3.0f) : clip.Seconds + 0.18f);
                string line = $"{p.Display} -> {what} ({clip.Seconds:0.0} s){(other ? " [another voice]" : "")}";
                Log.Add(line);
                MnLog.Info("VOICE " + line);
            }
            else
            {
                Fallbacks++;
                Missing.Add(p.Display);
                Resolve(p.Display, out var voice, out _);
                float len = Mathf.Clamp(0.7f + 0.05f * p.Text.Length, 0.9f, 4.5f);
                PlayUi("squelch_open");
                _closeAt = now + len;
                _busyUntil = now + len + 0.25f;
                string line = $"no clip for '{p.Display}' (voice {voice}): squelch only";
                Log.Add("MISSING " + line);
                MnLog.Warn("VOICE " + line);
            }
        }

        bool SlotExists(string slot) => AudioPaths.FindUser(slot) != null || System.IO.File.Exists(System.IO.Path.Combine(AudioPaths.BuiltIn, slot));

        void PlayUi(string cue)
        {
            if (!_lib.Sfx.TryGetValue(cue, out var c) || !c.Ready) return;
            _mixer.Play(c.Near[0], new PlayParams { Gain = Db.ToLin(c.LevelDb) * 2.2f, Bus = Bus.Voice, Priority = 8f, Pitch = 1f });
        }

        public bool Busy(float now) => now < _busyUntil || _queue.Count > 0;
    }
}
