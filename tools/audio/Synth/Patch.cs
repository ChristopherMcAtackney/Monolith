using System;
using System.Collections.Generic;

namespace Monolith.AudioTools
{
    public enum VoiceKind { Subtractive, Fm, Formant }
    public enum FilterKind { None, Ladder, Svf12, Svf24 }

    /// <summary>One oscillator of a subtractive patch. Wave: 0 saw, 1 pulse, 2 triangle, 3 sine.</summary>
    public sealed class OscDef
    {
        public int Wave;
        public float Level = 1f;
        public int Semis;                 // transpose in semitones (12 = an octave up)
        public float Cents;               // detune
        public float Width = 0.5f;        // pulse width
        public float PwmDepth, PwmRate = 0.4f;
        public OscDef(int wave, float level = 1f, int semis = 0, float cents = 0f, float width = 0.5f, float pwmDepth = 0f, float pwmRate = 0.4f)
        { Wave = wave; Level = level; Semis = semis; Cents = cents; Width = width; PwmDepth = pwmDepth; PwmRate = pwmRate; }
    }

    /// <summary>One FM operator pair (a carrier and the modulator that bends it), for bells and electric pianos.</summary>
    public sealed class FmPair
    {
        public float CarrierRatio = 1f, ModRatio = 1f, Index = 2f, IndexDecay = 0.6f, IndexSustain = 0f, Level = 1f, AmpDecay = 1.5f, Feedback = 0f;
        public FmPair(float carrier, float mod, float index, float indexDecay, float level, float ampDecay, float indexSustain = 0f)
        { CarrierRatio = carrier; ModRatio = mod; Index = index; IndexDecay = indexDecay; Level = level; AmpDecay = ampDecay; IndexSustain = indexSustain; }
    }

    /// <summary>
    /// An analog-style patch: everything the offline synth needs to turn a MIDI note into sound. Patches are defined in code (Patches.cs); the
    /// per-track mix (level, pan, effect sends) comes from the cue's JSON. <see cref="Calibrate"/> evens out levels so that 0 dB means the same
    /// loudness on every patch.
    /// </summary>
    public sealed class Patch
    {
        public string Name;
        public VoiceKind Kind = VoiceKind.Subtractive;
        public List<OscDef> Osc = new List<OscDef>();
        public float Sub, Noise, NoiseColor = 0.5f; public int SubWave = 2;
        public int Unison = 1; public float UniCents = 8f, Spread = 0.5f;

        public FilterKind Filter = FilterKind.Ladder;
        public float Cut = 3000f, Res = 0.2f, KeyTrack = 0.5f, FEnvAmt, VelCut, LfoCut, Drive = 1f;
        public float[] Amp = { 0.01f, 0.3f, 0.8f, 0.3f };       // attack, decay, sustain, release (seconds, level)
        public float[] FEnv = { 0.01f, 0.4f, 0.0f, 0.3f };
        public bool AmpPercussive;                               // the amp envelope decays on its own even while the key is down (plucks)

        public float Glide;                                      // seconds; applies to notes that follow another closely
        public bool Mono;                                        // a new note cuts the last one (and glides from it)
        public float VibDelay = 0.35f, VibRate = 5.3f, VibCents;
        public float LfoRate = 0.3f;                             // for LfoCut; locked to the loop length by the renderer
        public float Drift = 3f;                                 // cents of analog wander
        public float VelSens = 0.5f;
        public float ScoopSemis, ScoopTime = 0.09f;              // a pitch scoop into the note (brass)
        public float ExprToCut;                                  // octaves the expression pedal opens the filter (the CS-80's brightness)
        public float BendRange = 2f;
        public float Gain = 1f;                                  // set by Calibrate

        // formant (choir)
        public float[][] Vowels;                                 // [vowel][f1,f2,f3,g1,g2,g3,q]
        public float Breath = 0.05f;

        // FM
        public List<FmPair> Fm = new List<FmPair>();

        // default effect sends and insert for a track using this patch (the cue JSON overrides them)
        public string Chorus = "";                               // "", "I", "II", "I+II", "ens"
        public float HallSend, PlateSend, DelaySend;
        public float Sat;                                        // insert saturation drive (0 = off)

        public double CalibratedNorm = 1.0;
    }
}
