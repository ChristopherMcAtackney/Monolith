using System.Collections.Generic;
using Monolith.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XInput;

namespace Monolith.Juice
{
    /// <summary>
    /// The pad's rumble, driven by the same events as the shake: pulses that fade out (a hit, a blast), plus a steady level
    /// that holds while something lasts (guns, afterburner, a beam). Two motors: low (the heavy one, weight and blasts) and
    /// high (the light one, buzz, guns and sparks).
    ///
    /// Trigger rumble: Unity's Input System can drive the Xbox impulse triggers only through <see cref="IXboxOneRumble"/>,
    /// which no Windows device implements (XInput has just the two motors; the triggers need GameInput or Windows.Gaming.Input).
    /// <see cref="TriggerRumbleSupported"/> is true only if the connected pad exposes it, which on this build it does not,
    /// so the game does not pretend to: gun fire is carried by the high motor instead. Never active in capture mode.
    /// </summary>
    public sealed class RumbleOut
    {
        struct PulseState { public float Low, High, Left, Total; }

        readonly List<PulseState> _pulses = new List<PulseState>(16);
        readonly JuiceSettings _settings;
        float _susLow, _susHigh;
        float _lastLow = -1f, _lastHigh = -1f;
        bool _active;
        readonly bool _disabled;

        public float CurrentLow { get; private set; }
        public float CurrentHigh { get; private set; }

        public RumbleOut(JuiceSettings settings)
        {
            _settings = settings;
            _disabled = CommandLine.Has("-mnCapture") || CommandLine.Has("-mnNoRumble");
        }

        /// <summary>The connected pad, if it can rumble at all.</summary>
        public static Gamepad Pad => Gamepad.current;

        /// <summary>True only when the pad reports separate trigger motors (not on Windows XInput).</summary>
        public static bool TriggerRumbleSupported => Gamepad.current is IXboxOneRumble;

        public static string Describe()
        {
            var pad = Gamepad.current;
            if (pad == null) return "no pad";
            return $"{pad.displayName}: motors ok, trigger rumble {(pad is IXboxOneRumble ? "available" : "not available (XInput has two motors)")}";
        }

        /// <summary>A pulse that fades linearly to nothing over <paramref name="seconds"/>.</summary>
        public void Pulse(float low, float high, float seconds)
        {
            if (_disabled || seconds <= 0f) return;
            if (_pulses.Count >= 16)
            {
                // Drop the weakest.
                int weakest = 0; float w = float.MaxValue;
                for (int i = 0; i < _pulses.Count; i++) { float s = Mathf.Max(_pulses[i].Low, _pulses[i].High) * _pulses[i].Left; if (s < w) { w = s; weakest = i; } }
                _pulses.RemoveAt(weakest);
            }
            _pulses.Add(new PulseState { Low = Mathf.Clamp01(low), High = Mathf.Clamp01(high), Left = seconds, Total = seconds });
        }

        /// <summary>Hold a steady level for this frame (call each frame it lasts).</summary>
        public void Sustain(float low, float high)
        {
            _susLow = Mathf.Max(_susLow, low);
            _susHigh = Mathf.Max(_susHigh, high);
        }

        /// <summary>Once per rendered frame with the real frame time.</summary>
        public void Update(float realDt)
        {
            float low = _susLow, high = _susHigh;
            _susLow = _susHigh = 0f;
            for (int i = _pulses.Count - 1; i >= 0; i--)
            {
                var p = _pulses[i];
                float k = p.Left / p.Total;
                low = Mathf.Max(low, p.Low * k);
                high = Mathf.Max(high, p.High * k);
                p.Left -= realDt;
                if (p.Left <= 0f) _pulses.RemoveAt(i); else _pulses[i] = p;
            }
            float scale = _settings.RumbleScale;
            low = Mathf.Clamp01(low * scale);
            high = Mathf.Clamp01(high * scale);
            CurrentLow = low; CurrentHigh = high;
            if (_disabled) return;

            var pad = Gamepad.current;
            if (pad == null || !Application.isFocused)
            {
                if (_active && pad != null) pad.ResetHaptics();
                _active = false; _lastLow = _lastHigh = -1f;
                return;
            }
            if (Mathf.Abs(low - _lastLow) < 0.012f && Mathf.Abs(high - _lastHigh) < 0.012f) return;
            if (low <= 0.001f && high <= 0.001f)
            {
                if (_active) pad.SetMotorSpeeds(0f, 0f);
                _active = false;
            }
            else
            {
                pad.SetMotorSpeeds(low, high);
                _active = true;
            }
            _lastLow = low; _lastHigh = high;
        }

        public void Stop()
        {
            _pulses.Clear();
            _susLow = _susHigh = 0f;
            var pad = Gamepad.current;
            if (pad != null && _active) pad.ResetHaptics();
            _active = false; _lastLow = _lastHigh = -1f;
        }
    }

    /// <summary>
    /// A tiny freeze on a big kill (optional, off by default): the game runs at a fraction of its speed for a few real
    /// milliseconds. The director multiplies the host's own time scale by <see cref="Factor"/>.
    /// </summary>
    public sealed class HitStop
    {
        float _left;
        float _scale = 1f;
        public float Factor => _left > 0f ? _scale : 1f;
        public bool Active => _left > 0f;

        public void Trigger(float milliseconds, float scale)
        {
            if (milliseconds <= 0f) return;
            _left = Mathf.Max(_left, milliseconds * 0.001f);
            _scale = Mathf.Clamp(scale, 0.01f, 1f);
        }

        public void Update(float realDt)
        {
            if (_left > 0f) _left -= realDt;
        }

        public void Clear() { _left = 0f; }
    }
}
