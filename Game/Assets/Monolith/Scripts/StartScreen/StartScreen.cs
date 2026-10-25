using System;
using Monolith.Core;
using Monolith.Hud;
using Monolith.HudUi;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.HighDefinition;

namespace Monolith.Start
{
    /// <summary>
    /// The start screen: the scenario and the difficulty, drawn with the HUD's own renderer in the player's current HUD style (F9 changes it), on a camera of its own, over black. It reads the
    /// Xbox pad (D-pad or left stick to move, A to choose, B to go back) and the keyboard (arrows, Enter, Esc; F10 quits) and hands the choice to whoever made it (<see cref="Menu"/>,
    /// <see cref="Result"/>). It knows nothing of how a scenario is started: <c>Game/Start/StartFlow</c> does that.
    /// </summary>
    [DefaultExecutionOrder(150)]
    public sealed class StartScreen : MonoBehaviour
    {
        public static StartScreen Current { get; private set; }

        public StartMenu Menu { get; private set; }
        /// <summary>What the player chose, once they have: launch (read <see cref="StartMenu.Chosen"/> and <see cref="StartMenu.Level"/>) or quit. None while they are choosing.</summary>
        public MenuResult Result { get; private set; }
        public Camera Camera => _cam;
        /// <summary>False while something else (the loading screen) has the screen: no keys are read.</summary>
        public bool AcceptInput = true;
        /// <summary>Draw nothing and read nothing (the headless checks drive the menu without a screen).</summary>
        public bool Headless;

        HudLines _lines;
        HudRenderer _renderer;
        HudFrame _frame;
        HudMenu _data;
        HudPass _pass;
        Camera _cam;
        float _born, _nextRepeat;
        Vector2Int _held;
        string _hintFor;

        public static StartScreen Create(StartMenu menu, bool headless = false)
        {
            var go = new GameObject("StartScreen");
            DontDestroyOnLoad(go);
            var s = go.AddComponent<StartScreen>();
            s.Begin(menu, headless);
            return s;
        }

        void Begin(StartMenu menu, bool headless)
        {
            Current = this;
            Menu = menu;
            Headless = headless;
            _born = Time.unscaledTime;
            _data = new HudMenu();
            menu.Build(_data);
            _frame = new HudFrame { Menu = _data };
            _lines = new HudLines();
            _renderer = new HudRenderer();
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.None;

            // a camera of its own, black: there is no world behind this screen (the loading screen's camera does the same job while the world is built)
            var camGo = new GameObject("StartCamera");
            camGo.transform.SetParent(transform, false);
            _cam = camGo.AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = Color.black;
            _cam.cullingMask = 0;
            _cam.depth = 500f;
            _cam.allowHDR = false;
            _cam.allowMSAA = false;
            _cam.useOcclusionCulling = false;
            var data = camGo.AddComponent<HDAdditionalCameraData>();
            data.clearColorMode = HDAdditionalCameraData.ClearColorMode.Color;
            data.backgroundColorHDR = Color.black;
            data.antialiasing = HDAdditionalCameraData.AntialiasingMode.None;
            _pass = HudPass.Attach(gameObject, _cam, _lines, "MN Start", -60f, false);
            MnLog.Info($"START screen up: {menu.Entries.Count} entries, level {menu.LevelName}, style {HudStyles.Current.Name}");
        }

        void OnDestroy()
        {
            if (Current == this) Current = null;
            _lines?.Dispose();
        }

        /// <summary>Draw on another camera from now on (a capture: the picture is taken through the scene's camera); the screen's own is turned off.</summary>
        public void AttachTo(Camera cam)
        {
            if (cam == null || _pass == null) return;
            _pass.Camera = cam;
            if (_cam != null) _cam.enabled = false;
        }

        /// <summary>A fixed fade (0 to 1) instead of the clock's, for a capture.</summary>
        public float FixedFade = -1f;

        // ------------------------------------------------------------------------------------------------ input

        void Update()
        {
            if (Menu == null || Result != MenuResult.None) return;
            var kb = Keyboard.current; var pad = Gamepad.current;
            if (kb != null && kb.f9Key.wasPressedThisFrame) { var st = HudStyles.Cycle(1, save: true); MnLog.Info($"HUD style {st.Name}"); }
            if (!AcceptInput || Time.unscaledTime - _born < 0.3f) { _held = Vector2Int.zero; return; }
            if (kb != null && kb.f10Key.wasPressedThisFrame) { Result = MenuResult.Quit; return; }

            // moving: the D-pad, the left stick or the arrows; a held direction repeats after a pause
            var dir = Vector2Int.zero;
            if (pad != null)
            {
                if (pad.dpad.up.isPressed) dir.y = 1; else if (pad.dpad.down.isPressed) dir.y = -1;
                if (pad.dpad.left.isPressed) dir.x = -1; else if (pad.dpad.right.isPressed) dir.x = 1;
                var ls = pad.leftStick.ReadValue();
                if (dir == Vector2Int.zero && ls.magnitude > 0.55f)
                {
                    if (Mathf.Abs(ls.x) > Mathf.Abs(ls.y)) dir.x = ls.x > 0f ? 1 : -1; else dir.y = ls.y > 0f ? 1 : -1;
                }
            }
            if (kb != null && dir == Vector2Int.zero)
            {
                if (kb.upArrowKey.isPressed) dir.y = 1; else if (kb.downArrowKey.isPressed) dir.y = -1;
                if (kb.leftArrowKey.isPressed) dir.x = -1; else if (kb.rightArrowKey.isPressed) dir.x = 1;
            }
            float now = Time.unscaledTime;
            if (dir != _held)
            {
                _held = dir;
                if (dir != Vector2Int.zero) { Step(dir); _nextRepeat = now + 0.38f; }
            }
            else if (dir != Vector2Int.zero && now >= _nextRepeat) { Step(dir); _nextRepeat = now + 0.11f; }

            bool accept = pad != null && pad.buttonSouth.wasPressedThisFrame || kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame);
            bool back = pad != null && pad.buttonEast.wasPressedThisFrame || kb != null && (kb.escapeKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame);
            if (accept) Apply(MenuKey.Accept);
            else if (back) Apply(MenuKey.Back);
        }

        void Step(Vector2Int d)
        {
            // up and down move in the list; a sideways step moves between the lists (and a diagonal is the vertical one)
            if (d.y != 0) Apply(d.y > 0 ? MenuKey.Up : MenuKey.Down);
            else Apply(d.x > 0 ? MenuKey.Right : MenuKey.Left);
        }

        void Apply(MenuKey key)
        {
            var r = Menu.Press(key);
            if (r != MenuResult.None) { Result = r; MnLog.Info($"START {(r == MenuResult.Launch ? $"launch {Menu.Chosen.Id} at {Menu.LevelName}" : "quit")}"); }
        }

        /// <summary>Press a key as the pad or keyboard would (the checks and the scripted tours).</summary>
        public void Press(MenuKey key) { if (Result == MenuResult.None) Apply(key); }

        // ------------------------------------------------------------------------------------------------ the picture

        void LateUpdate()
        {
            if (Headless || _lines == null || _cam == null) return;
            var cam = _pass != null && _pass.Camera != null ? _pass.Camera : _cam;
            float w = cam.pixelWidth, h = cam.pixelHeight;
            if (w < 2f || h < 2f) { w = Screen.width; h = Screen.height; }
            Menu.Sync(_data);
            _data.Fade = FixedFade >= 0f ? FixedFade : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((Time.unscaledTime - _born) / 0.35f));
            _data.Corner = "HUD  " + HudStyles.Current.Title + "   F9";
            _data.Hint = Hint();
            _frame.Width = w; _frame.Height = h; _frame.Time = Time.unscaledTime;
            _lines.Clear(HudRenderer.ScaleFor(w, h));
            _renderer.Draw(_lines, _frame);
            _lines.Commit();
        }

        string Hint()
        {
            bool pad = Gamepad.current != null;
            string move = pad ? "D-PAD  MOVE" : "ARROWS  MOVE", a = pad ? "A" : "ENTER", b = pad ? "B" : "ESC";
            var sel = Menu.Selected;
            string act = Menu.LevelFocus && Menu.LevelApplies ? $"{a}  LAUNCH" : sel == null ? "" : sel.Kind == EntryKind.Quit ? $"{a}  QUIT THE GAME" : sel.Kind == EntryKind.Sandbox ? $"{a}  LAUNCH" : $"{a}  CHOOSE";
            string back = Menu.LevelFocus && Menu.LevelApplies ? $"{b}  BACK" : sel != null && sel.Kind == EntryKind.Quit ? $"{b}  QUIT" : $"{b}  QUIT ROW";
            return $"{move}          {act}          {back}";
        }
    }
}
