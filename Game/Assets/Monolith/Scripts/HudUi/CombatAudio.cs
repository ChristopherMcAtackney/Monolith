using Monolith.Core;
using UnityEngine;

namespace Monolith.HudUi
{
    /// <summary>
    /// The two tones the design asks for: the lock tone (a steady beep that quickens as a lock builds and holds as one continuous
    /// tone when it is made, a softer growl for a heat seeker that sees its target) and the missile warning (urgent double beeps that
    /// quicken as the missile closes; a slower beep when only a lock is being built on you). Generated in code and played through
    /// OnAudioFilterRead; the real audio system will replace this (it only reads <see cref="ShipCombat"/>). Silent in captures and in batch mode.
    /// </summary>
    public sealed class CombatAudio : MonoBehaviour
    {
        // Shared with the audio thread: plain floats, written by the main thread, read by the filter (a torn read costs one glitchy sample).
        volatile float _freq = 880f, _gain, _gateHz, _duty = 0.5f;
        float _phase, _gatePhase, _smoothGain;
        int _sampleRate = 48000;
        bool _mute;

        public static CombatAudio Create(Transform parent)
        {
            var go = new GameObject("CombatAudio");
            go.transform.SetParent(parent, false);
            var a = go.AddComponent<CombatAudio>();
            a._mute = Application.isBatchMode || CommandLine.Has("-mnCapture") && !CommandLine.Has("-mnAudio") || CommandLine.Has("-mnNoAudio") || CommandLine.Has("-mnAudioMute");
            if (!a._mute)
            {
                var src = go.AddComponent<AudioSource>();
                src.clip = AudioClip.Create("mn_tone_carrier", 2048, 1, AudioSettings.outputSampleRate, false);
                src.loop = true;
                src.spatialBlend = 0f;
                src.volume = 1f;
                src.Play();
            }
            a._sampleRate = AudioSettings.outputSampleRate;
            return a;
        }

        /// <summary>Choose the tone for this frame from the player's combat state.</summary>
        public void Drive(GameWorld world)
        {
            var me = world.Combat.Get(world.LocalPlayerShip);
            float gain = 0f, freq = 880f, gate = 0f, duty = 0.5f;
            if (me != null && me.Alive)
            {
                var slot = me.SelectedSlot;
                var def = slot != null ? WeaponCatalog.Get(slot.Weapon) : null;
                if (me.IncomingMissiles > 0)
                {
                    // Urgent: faster as it closes.
                    float k = Mathf.Clamp01(1f - me.IncomingEta / 6f);
                    gain = 0.20f; freq = 1560f; gate = Mathf.Lerp(3f, 11f, k); duty = 0.45f;
                }
                else if (me.LockedOnMe) { gain = 0.16f; freq = 1320f; gate = 5f; duty = 0.6f; }
                else if (me.BeingLocked > 0) { gain = 0.12f; freq = 1180f; gate = 2.5f; duty = 0.4f; }
                else if (def != null && def.NeedsLock && me.Lock == LockPhase.Acquiring) { gain = 0.14f; freq = 740f + 360f * me.LockProgress01; gate = Mathf.Lerp(2f, 9f, me.LockProgress01); duty = 0.5f; }
                else if (def != null && def.NeedsLock && me.Lock == LockPhase.Locked) { gain = 0.16f; freq = 1180f; gate = 0f; }
                else if (def != null && !def.NeedsLock && me.Lock == LockPhase.Locked) { gain = 0.07f; freq = 520f; gate = 0f; }
            }
            _gain = _mute || AudioLink.Owned ? 0f : gain; _freq = freq; _gateHz = gate; _duty = duty;
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            float freq = _freq, gate = _gateHz, duty = _duty, target = _gain;
            float inc = 2f * Mathf.PI * freq / _sampleRate;
            float gateInc = gate / _sampleRate;
            for (int i = 0; i < data.Length; i += channels)
            {
                _smoothGain += (target - _smoothGain) * 0.002f;
                float g = _smoothGain;
                if (gate > 0f)
                {
                    _gatePhase += gateInc; if (_gatePhase >= 1f) _gatePhase -= 1f;
                    if (_gatePhase > duty) g = 0f;
                }
                _phase += inc; if (_phase > 2f * Mathf.PI) _phase -= 2f * Mathf.PI;
                float sample = Mathf.Sin(_phase) * g;
                for (int c = 0; c < channels; c++) data[i + c] += sample;
            }
        }
    }
}
