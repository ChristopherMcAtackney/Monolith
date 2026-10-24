using System;
using System.Collections.Generic;
using Monolith.Core;
using Monolith.Hud;
using UnityEngine;

namespace Monolith.HudUi
{
    /// <summary>
    /// The player's HUD: one component that reads the game into a <see cref="HudFrame"/> every frame (<see cref="HudReader"/>), draws it in the chosen <see cref="HudStyle"/>
    /// (<see cref="HudRenderer"/>) and puts it on the finished frame after post-processing (<see cref="HudPass"/>), so captures show it as the player sees it. It owns what is the
    /// HUD's own: the notices and the radio feed (from the game's events), the overlay (the pause, briefing and debrief screens), the banner, the info lines, the footer, the damage
    /// flash, and the lock and missile-warning stand-in tones. It replaces the placeholder flight and combat HUDs.
    /// </summary>
    [DefaultExecutionOrder(110)]
    public sealed class PilotHud : MonoBehaviour
    {
        GameWorld _world;
        Camera _cam;
        HudLines _lines;
        HudRenderer _renderer;
        HudFrame _frame;
        HudMissiles _missiles;
        CombatAudio _audio;
        readonly List<IDisposable> _subs = new List<IDisposable>();

        struct Feed { public string Speaker, Text; public float Born, Life; public bool Alert; }
        readonly Feed[] _notices = new Feed[6];
        int _noticeNext;
        readonly Feed[] _radio = new Feed[3];
        int _radioNext;
        float _damageFlash;
        string _lastOrder;

        /// <summary>Lines at the top left under the class name (the range's score, the mission's clock and hulls, hints).</summary>
        public string[] Info;
        /// <summary>Shown big in the middle (ship lost, the mission's result). Null for none.</summary>
        public string Banner, BannerSub;
        /// <summary>The pause, briefing and debrief screen: lines of text on a plate in the middle (see <see cref="HudRenderer"/>: a tab splits a key from what it does, a blank line is a gap, "#" starts a section). Null or empty for none.</summary>
        public string[] Overlay;
        /// <summary>A short line at the bottom left (device warnings).</summary>
        public string Footer;
        /// <summary>The HUD's own switch to the battle map, for scenes with no cabin (the cabin's map switch is read from <see cref="GameWorld.Cabin"/>).</summary>
        public bool MapSwitch;
        /// <summary>Show the pad's buttons in the quick menu (false: the keyboard's keys).</summary>
        public bool UsePadGlyphs = true;
        /// <summary>The 1080p-relative scale of everything, for the player's taste.</summary>
        public float Scale = 1f;

        public HudFrame Frame => _frame;
        public HudRenderer Renderer => _renderer;
        public HudLines Lines => _lines;

        public static PilotHud Create(GameWorld world, Camera camera, Transform parent, bool audio = true)
        {
            var go = new GameObject("PilotHud");
            go.transform.SetParent(parent, false);
            var hud = go.AddComponent<PilotHud>();
            hud._world = world;
            hud._cam = camera;
            hud._lines = new HudLines();
            hud._renderer = new HudRenderer();
            hud._frame = new HudFrame();
            hud._missiles = new HudMissiles(world);
            HudPass.Attach(go, camera, hud._lines, "MN HUD", 0f);
            if (audio) hud._audio = CombatAudio.Create(go.transform);
            hud._subs.Add(GameEvents.SubscribeAll(hud.OnEvent));
            return hud;
        }

        void OnDestroy()
        {
            foreach (var s in _subs) s.Dispose();
            _subs.Clear();
            _missiles?.Dispose();
        }

        // ------------------------------------------------------------------------------------------------ the feeds

        void Say(string text, bool alert = false)
        {
            _notices[_noticeNext % _notices.Length] = new Feed { Text = text, Born = Time.unscaledTime, Life = 5f, Alert = alert };
            _noticeNext++;
        }

        void OnEvent(GameEvent e)
        {
            int me = _world.LocalPlayerShip;
            if (me == GameWorld.NoShip) return;
            switch (e.Id)
            {
                case CombatEvents.SubsystemDestroyed:
                    if (e.Entity == me) Say(HudReader.PartLabel(e.Subject) + " DESTROYED", true);
                    else if (e.Other == me) Say("TARGET " + HudReader.PartLabel(e.Subject) + " DESTROYED");
                    break;
                case CombatEvents.PartDamaged:
                    if (e.Entity == me) Say(HudReader.PartLabel(e.Subject) + " DAMAGED", true);
                    break;
                case CombatEvents.ShieldDown:
                    if (e.Entity == me) { Say("SHIELD DOWN", true); _damageFlash = 1f; }
                    break;
                case CombatEvents.HitHull:
                    if (e.Entity == me) _damageFlash = Mathf.Max(_damageFlash, 0.6f);
                    break;
                case CombatEvents.ShipDestroyed:
                    if (e.Other == me && e.Entity != me) Say("DESTROYED " + (_world.Ships.Get(e.Entity)?.Callsign ?? "TARGET").ToUpperInvariant());
                    break;
                case CombatEvents.LockAcquired:
                    if (e.Entity == me) Say("LOCK ACQUIRED");
                    break;
                case CombatEvents.LockLost:
                    if (e.Entity == me) Say("LOCK LOST");
                    break;
                case CombatEvents.MissileWarning:
                    if (e.Entity == me) Say("MISSILE INBOUND", true);
                    break;
                case CombatEvents.DecoySuccess:
                    if (e.Entity == me) Say("DECOY HIT");
                    break;
                case CombatEvents.ShotDown:
                    if (e.Other == me) Say("TORPEDO SHOT DOWN");
                    break;
                case CombatEvents.PowerChanged:
                    if (e.Entity == me) Say("POWER " + (e.Subject ?? "").ToUpperInvariant());
                    break;
                case CombatEvents.Collision:
                    if (e.Entity == me || e.Other == me) Say("COLLISION", true);
                    break;
                case CombatEvents.RadioLine:
                {
                    SplitSpeaker(e.Subject, out string who, out string text);
                    _radio[_radioNext % _radio.Length] = new Feed { Speaker = who, Text = text, Born = Time.unscaledTime, Life = e.Value > 0f && e.Value < 20f ? Mathf.Max(e.Value, 4f) : 7f, Alert = false };
                    _radioNext++;
                    break;
                }
                case CombatEvents.WingOrder:
                    if (e.Entity == me) _lastOrder = (e.Subject ?? "").ToUpperInvariant();
                    break;
            }
        }

        /// <summary>"SABLE 2: COPY." is the speaker and the line; a line with no speaker in front of its first colon has none.</summary>
        public static void SplitSpeaker(string line, out string speaker, out string text)
        {
            speaker = null; text = line ?? "";
            int c = text.IndexOf(": ", StringComparison.Ordinal);
            if (c > 0 && c <= 26 && text.IndexOf('.', 0, c) < 0) { speaker = text.Substring(0, c); text = text.Substring(c + 2); }
        }

        // the feed lines are reused every frame
        readonly HudFrame.FeedLine[] _noticeLines = MakeLines(6), _radioLines = MakeLines(4);
        static HudFrame.FeedLine[] MakeLines(int n) { var a = new HudFrame.FeedLine[n]; for (int i = 0; i < n; i++) a[i] = new HudFrame.FeedLine(); return a; }

        void FillFeeds(HudFrame f)
        {
            float now = Time.unscaledTime;
            f.Notices.Clear();
            for (int k = 0; k < _notices.Length; k++)
            {
                int idx = _noticeNext - 1 - k;
                if (idx < 0) break;
                var n = _notices[idx % _notices.Length];
                float age = now - n.Born;
                if (n.Text == null || age > n.Life) continue;
                float a = Mathf.Clamp01(n.Life - age) * Mathf.Clamp01(age * 8f + 0.3f);
                var line = _noticeLines[f.Notices.Count]; line.Speaker = null; line.Text = n.Text; line.Alert = n.Alert; line.Alpha = a; f.Notices.Add(line);
            }
            f.Radio.Clear();
            for (int k = _radio.Length - 1; k >= 0; k--)
            {
                int idx = _radioNext - 1 - k;
                if (idx < 0) continue;
                var n = _radio[idx % _radio.Length];
                float age = now - n.Born;
                if (n.Text == null || age > n.Life) continue;
                float a = Mathf.Clamp01((n.Life - age) * 0.8f) * Mathf.Clamp01(age * 8f + 0.3f);
                var line = _radioLines[f.Radio.Count]; line.Speaker = n.Speaker; line.Text = n.Text; line.Alert = n.Alert; line.Alpha = a; f.Radio.Add(line);
            }
            // only the two newest lines show at once
            while (f.Radio.Count > 2) f.Radio.RemoveAt(0);
        }

        // ------------------------------------------------------------------------------------------------ the frame

        // -mnHudCost: the HUD's own CPU time per frame (read the world, build the mesh, upload it), logged every 600 frames
        static readonly bool Cost = CommandLine.Has("-mnHudCost");
        long _costTicks, _tRead, _tBuild, _tCommit; int _costFrames;

        void LateUpdate()
        {
            if (_cam == null) return;
            long t0 = Cost ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
            Frame_();
            if (Cost)
            {
                _costTicks += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
                if (++_costFrames == 600) { MnLog.Info($"HUD COST {_costTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency / _costFrames:0.000} ms of CPU per frame over {_costFrames} frames ({_lines.VertexCount} vertices): read {_tRead * 1000.0 / System.Diagnostics.Stopwatch.Frequency / _costFrames:0.000}, build {_tBuild * 1000.0 / System.Diagnostics.Stopwatch.Frequency / _costFrames:0.000}, upload {_tCommit * 1000.0 / System.Diagnostics.Stopwatch.Frequency / _costFrames:0.000}"); _costTicks = 0; _tRead = _tBuild = _tCommit = 0; _costFrames = 0; }
            }
        }

        // The HUD is rebuilt at most 120 times a second: past that nobody can see it, and at 240 frames a second it halves the cost. A change of screen size or style rebuilds at once.
        const float MinBuildInterval = 1f / 120f;
        float _lastBuild = -1f;
        string[] _lastOverlay;
        int _lastW, _lastH;
        string _lastStyle;

        void Frame_()
        {
            float now = Time.unscaledTime;
            string styleName = HudStyles.Current.Name;
            bool force = _cam.pixelWidth != _lastW || _cam.pixelHeight != _lastH || styleName != _lastStyle || now < _lastBuild || Overlay != _lastOverlay;
            if (!force && now - _lastBuild < MinBuildInterval - 0.0008f) return;
            float dt = _lastBuild < 0f ? 0f : Mathf.Min(now - _lastBuild, 0.25f);
            _lastBuild = now; _lastW = _cam.pixelWidth; _lastH = _cam.pixelHeight; _lastStyle = styleName; _lastOverlay = Overlay;
            _damageFlash = Mathf.MoveTowards(_damageFlash, 0f, dt * 2.2f);
            var f = _frame;
            long a0 = Cost ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
            HudReader.Read(_world, _cam, f, Time.unscaledTime);
            f.MapOpen |= MapSwitch && f.HasShip && f.Alive;
            f.UsePadGlyphs = UsePadGlyphs;
            f.LastOrder = _lastOrder;
            f.Info = Info; f.Banner = Banner; f.BannerSub = BannerSub; f.Overlay = Overlay; f.Footer = Footer;
            f.DamageFlash = _damageFlash;
            FillFeeds(f);
            long a1 = Cost ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
            _lines.Clear(HudRenderer.ScaleFor(_cam.pixelWidth, _cam.pixelHeight) * Scale);
            _renderer.Draw(_lines, f);
            long a2 = Cost ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
            _lines.Commit();
            long a3 = Cost ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
            if (_audio != null) _audio.Drive(_world);
            if (Cost && _costFrames == 599) { _lines.Areas(out float sa, out float ta); MnLog.Info($"HUD AREA shapes {sa / 1e6f:0.000} Mpx, type {ta / 1e6f:0.000} Mpx (a 1080p screen is 2.07 Mpx)"); }
            if (Cost) { _tRead += a1 - a0; _tBuild += a2 - a1; _tCommit += a3 - a2; }
        }
    }
}
