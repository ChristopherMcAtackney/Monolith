using System;
using System.Collections.Generic;
using Monolith.Core;
using UnityEngine;

namespace Monolith.Cabin
{
    /// <summary>
    /// The pilot's own hands (and arms): human, but longer and more slender than a baseline hand, ashen with dark nails and ridged backs, in a worn sleeve. They are the only
    /// body the player sees. They live in the cabin, not on the camera: each hand is a wrist target and a finger pose, and a two-bone arm is solved from the shoulder under the
    /// eye to the wrist. At rest they hang low in front, relaxed, swaying with the walk and lagging the turn; at an interaction they reach to the work (the breaker's lever, a panel's
    /// module, a rack bay, the table's display) and do it: grip and pull, press and hold, pull and seat, touch. Procedural: no animation clips, only poses and timings from the
    /// <see cref="CabinSystem"/>'s action and its progress.
    /// </summary>
    public sealed class CabinHands
    {
        public const string PrefabPath = "Cabin/pilot_hands";

        sealed class Hand
        {
            public bool Right;
            public Transform Wrist, Upper, Fore;
            public Transform[][] Fingers = new Transform[5][];      // joints a, b, c of each finger (the thumb has two: a, b)
            public Quaternion[][] FingerRest = new Quaternion[5][];
            public Quaternion WristRest = Quaternion.identity;
            public Vector3 Pos, Smooth;                              // wrist target in cabin-root space and its smoothed value
            public Quaternion Rot = Quaternion.identity, SmoothRot = Quaternion.identity;
            public float Curl, CurlSmooth, Point, PointSmooth, Spread, SpreadSmooth;
            public Vector3 Shoulder;                                 // in camera space
        }

        readonly CabinSystem _cab;
        readonly Transform _cabinRoot;
        readonly Camera _cam;
        GameObject _go;
        readonly Hand _r = new Hand { Right = true }, _l = new Hand();
        Vector3 _lastCamPos; Quaternion _lastCamRot = Quaternion.identity;
        float _lastYaw, _yawRate, _swayT, _breathT;
        const float UpperLen = 0.30f, ForeLen = 0.27f;

        public bool Built { get; private set; }
        /// <summary>-mnHandsShowcase: hold the hands up in front of the eye (a relaxed one and a gripping one) so they can be looked at.</summary>
        static readonly bool Showcase = CommandLine.Has("-mnHandsShowcase");

        public static CabinHands Create(CabinSystem cab, Transform cabinRoot, Camera cam)
        {
            var h = new CabinHands(cab, cabinRoot, cam);
            var prefab = Resources.Load<GameObject>(PrefabPath);
            if (prefab == null) { MnLog.Warn($"CABIN hands: Resources/{PrefabPath}.prefab is missing; the pilot has no hands"); return h; }
            h._go = UnityEngine.Object.Instantiate(prefab, cabinRoot);
            h._go.name = "PilotHands";
            h.Bind(h._r, "R"); h.Bind(h._l, "L");
            h._r.Shoulder = new Vector3(0.20f, -0.30f, -0.10f);
            h._l.Shoulder = new Vector3(-0.20f, -0.30f, -0.10f);
            foreach (var r in h._go.GetComponentsInChildren<Renderer>(true)) { r.renderingLayerMask = 1u << 1; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; r.receiveShadows = true; }
            h.Built = true;
            h._go.SetActive(false);
            return h;
        }

        CabinHands(CabinSystem cab, Transform cabinRoot, Camera cam) { _cab = cab; _cabinRoot = cabinRoot; _cam = cam; }

        void Bind(Hand h, string p)
        {
            Transform Find(string n) { foreach (var t in _go.GetComponentsInChildren<Transform>(true)) if (t.name == n) return t; return null; }
            h.Wrist = Find($"{p}_wrist"); h.Upper = Find($"{p}_upper"); h.Fore = Find($"{p}_fore");
            if (h.Wrist != null) h.WristRest = h.Wrist.localRotation;
            for (int i = 0; i < 5; i++)
            {
                var a = Find($"{p}_f{i}_a"); var b = Find($"{p}_f{i}_b"); var c = i == 0 ? null : Find($"{p}_f{i}_c");
                h.Fingers[i] = i == 0 ? new[] { a, b } : new[] { a, b, c };
                h.FingerRest[i] = new Quaternion[h.Fingers[i].Length];
                for (int k = 0; k < h.Fingers[i].Length; k++) h.FingerRest[i][k] = h.Fingers[i][k] != null ? h.Fingers[i][k].localRotation : Quaternion.identity;
            }
        }

        // ------------------------------------------------------------------------------------------------ poses

        // Finger flexion in degrees for [knuckle, middle, tip]; a relaxed hand curls a little, a grip a lot, a pointing finger not at all.
        static readonly float[] Relaxed = { 22f, 30f, 18f };
        static readonly float[] Grip = { 62f, 85f, 55f };
        static readonly float[] Flat = { 4f, 6f, 4f };

        Transform Anchor(string name)
        {
            var t = _cabinRoot.Find(name);
            return t;
        }

        /// <summary>Where each hand wants to be this frame (cabin-root space) and how it holds itself; the arms then follow.</summary>
        public void Tick(float dt, bool show)
        {
            if (!Built) return;
            var b = _cab.Board;
            bool active = show && b.State != CabinBoard.Phase.Seated || b.State == CabinBoard.Phase.Rising || b.State == CabinBoard.Phase.Lowering;
            if (_go.activeSelf != active) _go.SetActive(active);
            if (!active || _cam == null) return;
            var camT = _cam.transform;
            // The camera's pose in cabin-root space (the hands live there).
            Vector3 eye = _cabinRoot.InverseTransformPoint(camT.position);
            Quaternion camRot = Quaternion.Inverse(_cabinRoot.rotation) * camT.rotation;
            Vector3 fwd = camRot * Vector3.forward, right = camRot * Vector3.right, up = camRot * Vector3.up;
            _yawRate = Mathf.Lerp(_yawRate, Mathf.DeltaAngle(_lastYaw, _cab.Yaw) / Mathf.Max(dt, 1e-3f), 1f - Mathf.Exp(-12f * dt));
            _lastYaw = _cab.Yaw;
            _swayT += dt * (1.2f + _cab.Board.Stand01 * 2.4f * Mathf.Clamp01(_cab.WalkSpeed01));
            _breathT += dt;
            float walk = _cab.WalkSpeed01;
            float sway = Mathf.Sin(_swayT * 3.1f) * 0.012f * walk, swayV = Mathf.Abs(Mathf.Sin(_swayT * 3.1f)) * 0.018f * walk;
            float breath = Mathf.Sin(_breathT * 1.5f) * 0.004f;
            float lag = Mathf.Clamp(_yawRate * 0.0006f, -0.05f, 0.05f);

            // Rest: low in front, a little apart, fingers relaxed.
            Vector3 restR = eye + right * (0.23f + lag) + up * (-0.34f + sway - swayV + breath) + fwd * 0.40f;
            Vector3 restL = eye - right * (0.23f - lag) + up * (-0.34f - sway - swayV - breath) + fwd * 0.40f;
            Quaternion restRot = Quaternion.LookRotation(fwd + up * -0.25f, up);
            SetTarget(_r, restR, restRot * Quaternion.Euler(0f, 18f, 12f), 0.28f, 0f, 0.15f);
            SetTarget(_l, restL, restRot * Quaternion.Euler(0f, -18f, -12f), 0.28f, 0f, 0.15f);
            float reach = 0f;

            var item = _cab.ActiveItem;
            string act = b.Action;
            float p = _cab.ActionProgress;
            if (b.State == CabinBoard.Phase.Rising || b.State == CabinBoard.Phase.Lowering)
            {
                // Pushing up out of the seat, or reaching for its arms: hands low and wide, flat.
                float s = b.State == CabinBoard.Phase.Rising ? 1f - b.Stand01 : b.Stand01;
                Vector3 arm = eye + up * -0.55f + fwd * 0.35f;
                SetTarget(_r, arm + right * 0.34f, Quaternion.LookRotation(fwd + up * -0.8f, up), 0.15f, 0f, 0.2f);
                SetTarget(_l, arm - right * 0.34f, Quaternion.LookRotation(fwd + up * -0.8f, up), 0.15f, 0f, 0.2f);
                reach = s;
            }
            else if (act == "breaker" && item != null)
            {
                var grip = Anchor(item.Grip);
                var off = Anchor("ANCHOR_breaker_grip_off");
                if (grip != null && off != null)
                {
                    float travel = Mathf.Clamp01(Mathf.InverseLerp(0.0f, 0.62f, p));
                    float lever = _cab.LeverPos;
                    Vector3 g = Vector3.Lerp(off.localPosition, grip.localPosition, lever);
                    var rot = Quaternion.LookRotation(Quaternion.Euler(0, 0, 0) * (g - eye).normalized + up * 0.15f, up) * Quaternion.Euler(0f, 0f, -75f);
                    float reachIn = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(p / 0.14f));
                    SetTarget(_r, Vector3.Lerp(restR, g, reachIn), Quaternion.Slerp(restRot, rot, reachIn), Mathf.Lerp(0.28f, 0.92f, reachIn), 0f, 0.1f);
                    reach = reachIn;
                    // the other hand braces on the housing
                    Vector3 brace = g + right * -0.16f + up * 0.13f;
                    SetTarget(_l, Vector3.Lerp(restL, brace, reachIn), Quaternion.Slerp(restRot, Quaternion.LookRotation((brace - eye).normalized, up), reachIn), 0.2f, 0f, 0.3f);
                }
            }
            else if (act == "repair" && item != null)
            {
                var grip = Anchor(item.Grip);
                if (grip != null)
                {
                    string need = _cab.PanelNeed(item.Id.Substring(6));
                    float prog = need != null ? _cab.RepairProgress(need) : 1f;
                    float reachIn = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_cab.ActionSeconds / 0.35f));
                    float press = Mathf.SmoothStep(0f, 1f, prog);
                    Vector3 module = grip.localPosition + grip.localRotation * Vector3.forward * (-0.13f * (1f - press) + 0.02f);
                    float tremble = Mathf.Sin(Time.unscaledTime * 38f) * 0.0025f * (0.4f + _cab.Board.Jolt * 3f);
                    Vector3 toward = (grip.localPosition - eye).normalized;
                    SetTarget(_r, Vector3.Lerp(restR, module - toward * 0.07f + up * tremble, reachIn), Quaternion.Slerp(restRot, Quaternion.LookRotation(toward, up) * Quaternion.Euler(0f, 0f, 90f), reachIn), 0.1f, 0f, 0.1f);
                    Vector3 rim = grip.localPosition + up * 0.14f - right * 0.18f;
                    SetTarget(_l, Vector3.Lerp(restL, rim, reachIn), Quaternion.Slerp(restRot, Quaternion.LookRotation((rim - eye).normalized, up), reachIn), 0.1f, 0f, 0.3f);
                    reach = reachIn;
                }
            }
            else if (act == "rack" && item != null)
            {
                var grip = Anchor(item.Grip);
                if (grip != null)
                {
                    float s = Mathf.Sin(Mathf.Clamp01(p) * Mathf.PI);
                    Vector3 g = grip.localPosition + grip.localRotation * Vector3.forward * (-0.22f * s);
                    float reachIn = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(p / 0.2f));
                    SetTarget(_r, Vector3.Lerp(restR, g, reachIn), Quaternion.Slerp(restRot, Quaternion.LookRotation((grip.localPosition - eye).normalized, up) * Quaternion.Euler(0f, 0f, 70f), reachIn), Mathf.Lerp(0.28f, 0.9f, reachIn), 0f, 0.1f);
                    Vector3 brace = grip.localPosition + up * 0.2f + right * 0.12f;
                    SetTarget(_l, Vector3.Lerp(restL, brace, reachIn), Quaternion.Slerp(restRot, Quaternion.LookRotation((brace - eye).normalized, up), reachIn), 0.1f, 0f, 0.3f);
                    reach = reachIn;
                }
            }
            else if (act == "table" && item != null)
            {
                var disp = Anchor("DISPLAY_table");
                if (disp != null)
                {
                    float tap = Mathf.Sin(Mathf.Clamp01(p) * Mathf.PI);
                    Vector3 pt = disp.localPosition + new Vector3(0.10f, 0.02f - 0.02f * tap, -0.10f);
                    SetTarget(_r, pt, Quaternion.LookRotation((pt - eye).normalized + Vector3.down * 0.5f, up), 0.3f, 1f, 0.25f);
                    reach = 1f;
                }
            }
            else
            {
                // At the table or the rack with nothing under the hands: the pilot's weight rests on the right hand if one is near.
            }
            if (Showcase)
            {
                Quaternion face = Quaternion.LookRotation(fwd + up * -0.1f, up);
                SetTarget(_r, eye + right * 0.13f + up * -0.10f + fwd * 0.50f, face * Quaternion.Euler(0f, -8f, 8f), 0.30f, 0f, 0.4f);
                SetTarget(_l, eye - right * 0.13f + up * -0.10f + fwd * 0.50f, face * Quaternion.Euler(0f, 8f, -8f), 0.95f, 0f, 0.1f);
            }
            _reach = reach;

            Blend(_r, dt); Blend(_l, dt);
            Pose(_r, eye, camRot); Pose(_l, eye, camRot);
        }

        float _reach;

        static void SetTarget(Hand h, Vector3 pos, Quaternion rot, float curl, float point, float spread)
        {
            h.Pos = pos; h.Rot = rot; h.Curl = curl; h.Point = point; h.Spread = spread;
        }

        void Blend(Hand h, float dt)
        {
            float k = 1f - Mathf.Exp(-14f * dt);
            if ((h.Smooth - h.Pos).sqrMagnitude > 1.0f) h.Smooth = h.Pos;
            h.Smooth = Vector3.Lerp(h.Smooth, h.Pos, k);
            h.SmoothRot = Quaternion.Slerp(h.SmoothRot, h.Rot, k);
            h.CurlSmooth = Mathf.Lerp(h.CurlSmooth, h.Curl, 1f - Mathf.Exp(-16f * dt));
            h.PointSmooth = Mathf.Lerp(h.PointSmooth, h.Point, 1f - Mathf.Exp(-16f * dt));
            h.SpreadSmooth = Mathf.Lerp(h.SpreadSmooth, h.Spread, 1f - Mathf.Exp(-10f * dt));
        }

        void Pose(Hand h, Vector3 eye, Quaternion camRot)
        {
            if (h.Wrist == null) return;
            h.Wrist.localPosition = h.Smooth; h.Wrist.localRotation = h.SmoothRot * h.WristRest;
            // The arm: shoulder under the eye, elbow out and down.
            Vector3 shoulder = eye + camRot * h.Shoulder;
            Vector3 wrist = h.Smooth;
            Vector3 pole = camRot * new Vector3(h.Right ? 0.6f : -0.6f, -1f, -0.3f);
            SolveArm(h, shoulder, wrist, pole);
            // The fingers.
            float curl = Mathf.Clamp01(h.CurlSmooth);
            for (int i = 0; i < 5; i++)
            {
                var j = h.Fingers[i];
                if (j == null) continue;
                float point = i == 1 ? h.PointSmooth : 0f;
                float c = Mathf.Lerp(curl, 0.05f, point);
                float splay = (i - 2) * 6f * h.SpreadSmooth * (h.Right ? 1f : -1f);
                for (int k = 0; k < j.Length; k++)
                {
                    if (j[k] == null) continue;
                    float ang = Mathf.Lerp(Mathf.Lerp(Flat[Mathf.Min(k, 2)], Relaxed[Mathf.Min(k, 2)], Mathf.Clamp01(c / 0.3f)), Grip[Mathf.Min(k, 2)], Mathf.Clamp01((c - 0.3f) / 0.7f));
                    if (i == 0) ang *= 0.55f;
                    j[k].localRotation = h.FingerRest[i][k] * Quaternion.Euler(ang, k == 0 ? splay : 0f, 0f);
                }
            }
        }

        void SolveArm(Hand h, Vector3 shoulder, Vector3 wrist, Vector3 pole)
        {
            if (h.Upper == null || h.Fore == null) return;
            Vector3 d = wrist - shoulder;
            float dist = Mathf.Clamp(d.magnitude, 0.12f, UpperLen + ForeLen - 0.01f);
            Vector3 dir = d.normalized;
            wrist = shoulder + dir * dist;
            // Law of cosines: how far along the shoulder-wrist line the elbow's foot is, and how far off it.
            float a = (UpperLen * UpperLen - ForeLen * ForeLen + dist * dist) / (2f * dist);
            float hgt = Mathf.Sqrt(Mathf.Max(0f, UpperLen * UpperLen - a * a));
            Vector3 side = Vector3.ProjectOnPlane(pole, dir).normalized;
            if (side.sqrMagnitude < 0.5f) side = Vector3.Cross(dir, Vector3.right).normalized;
            Vector3 elbow = shoulder + dir * a + side * hgt;
            h.Upper.localPosition = shoulder; h.Upper.localRotation = Quaternion.LookRotation(elbow - shoulder, side);
            h.Fore.localPosition = elbow; h.Fore.localRotation = Quaternion.LookRotation(wrist - elbow, side);
            // the hand rides the wrist the arm reached (its target was clamped)
            h.Wrist.localPosition = wrist;
        }
    }
}
