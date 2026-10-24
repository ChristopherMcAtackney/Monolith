using System;
using System.Collections.Generic;
using Monolith.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Monolith.Juice
{
    /// <summary>One control on the sandbox panel.</summary>
    public sealed class PanelItem
    {
        public enum Kind { Button, Toggle, Slider, Choice, Info }
        public Kind Type;
        public string Label;
        public string Hint;                              // the pad shortcut, shown small on the right
        public Action OnPress;
        public Func<bool> GetBool; public Action<bool> SetBool;
        public Func<float> GetFloat; public Action<float> SetFloat; public float Min, Max, Step = 0.05f; public string Format = "0.00";
        public Func<string> GetText;                     // Info text, or a button's changing label
        public string[] Choices; public Func<int> GetChoice; public Action<int> SetChoice;
        public Func<bool> Highlight;                     // a button that shows as "on" (the current preset)

        public static PanelItem Button(string label, Action press, string hint = null, Func<bool> on = null) => new PanelItem { Type = Kind.Button, Label = label, OnPress = press, Hint = hint, Highlight = on };
        public static PanelItem Toggle(string label, Func<bool> get, Action<bool> set) => new PanelItem { Type = Kind.Toggle, Label = label, GetBool = get, SetBool = set };
        public static PanelItem Slider(string label, Func<float> get, Action<float> set, float min, float max, float step = 0.05f, string format = "0.00") =>
            new PanelItem { Type = Kind.Slider, Label = label, GetFloat = get, SetFloat = set, Min = min, Max = max, Step = step, Format = format };
        public static PanelItem Info(Func<string> text) => new PanelItem { Type = Kind.Info, GetText = text };
    }

    /// <summary>A row of one or two items (two sit side by side).</summary>
    public sealed class PanelRow
    {
        public PanelItem A, B;
        public PanelRow(PanelItem a, PanelItem b = null) { A = a; B = b; }
    }

    public sealed class PanelPage
    {
        public string Title;
        public readonly List<PanelRow> Rows = new List<PanelRow>();
        public PanelPage(string title) { Title = title; }
        public PanelPage Add(PanelItem a, PanelItem b = null) { Rows.Add(new PanelRow(a, b)); return this; }
    }

    /// <summary>
    /// The juice sandbox's on-screen panel, driven by pad and mouse: pages of buttons, switches and sliders, drawn on the left
    /// with IMGUI (so it is never in a capture). Pad: Start opens and closes it; D-pad or left stick moves, A presses, left/right
    /// adjusts a slider or steps a choice, LB/RB change page, B closes. Mouse: click, drag sliders. While it is open the host stops
    /// reading flight controls (<see cref="Visible"/>).
    /// </summary>
    public sealed class JuicePanel : MonoBehaviour
    {
        public readonly List<PanelPage> Pages = new List<PanelPage>();
        public bool Visible { get; private set; }
        public Action<bool> OnVisibleChanged;
        public Func<string> Footer;
        public Action OnChanged;                          // any switch or slider moved

        int _page, _row, _col;
        Texture2D _white;
        GUIStyle _label, _small, _title;
        float _repeatAt; Vector2 _lastNav;
        bool _mouseWasDown; int _dragRow = -1, _dragCol = -1;
        readonly List<(Rect rect, int row, int col, PanelItem item)> _hit = new List<(Rect, int, int, PanelItem)>();
        float _scrollY;

        static readonly Color Ink = new Color(1.0f, 0.66f, 0.26f, 1f);
        static readonly Color Dim = new Color(1.0f, 0.66f, 0.26f, 0.55f);
        static readonly Color Back = new Color(0.02f, 0.03f, 0.04f, 0.84f);
        static readonly Color Sel = new Color(1.0f, 0.66f, 0.26f, 0.22f);
        static readonly Color OnCol = new Color(0.42f, 0.95f, 0.62f, 1f);

        public static JuicePanel Create(Transform parent)
        {
            var go = new GameObject("JuicePanel");
            go.transform.SetParent(parent, false);
            var p = go.AddComponent<JuicePanel>();
            p._white = Texture2D.whiteTexture;
            return p;
        }

        public void SetVisible(bool v)
        {
            if (Visible == v) return;
            Visible = v;
            if (v) EnsureSelectable();
            Cursor.visible = v;
            Cursor.lockState = v ? CursorLockMode.None : CursorLockMode.Locked;
            OnVisibleChanged?.Invoke(v);
        }

        public void Toggle() => SetVisible(!Visible);

        public void GoToPage(int page)
        {
            if (Pages.Count == 0) return;
            _page = Mathf.Clamp(page, 0, Pages.Count - 1); _row = 0; _col = 0;
            EnsureSelectable();
        }

        /// <summary>Put the cursor on a row that can be pressed (the first rows of a page may be notes).</summary>
        void EnsureSelectable()
        {
            var p = Page; if (p == null) return;
            for (int r = 0; r < p.Rows.Count; r++)
                if (p.Rows[r].A != null && p.Rows[r].A.Type != PanelItem.Kind.Info) { _row = r; _col = 0; return; }
        }

        PanelPage Page => Pages.Count > 0 ? Pages[Mathf.Clamp(_page, 0, Pages.Count - 1)] : null;

        // ------------------------------------------------------------------------------------------------ input

        void Update()
        {
            var pad = Gamepad.current;
            var kb = Keyboard.current;
            if (pad != null && pad.startButton.wasPressedThisFrame) Toggle();
            else if (kb != null && (kb.f9Key.wasPressedThisFrame || kb.tabKey.wasPressedThisFrame && Visible || kb.escapeKey.wasPressedThisFrame)) Toggle();
            if (!Visible || Page == null) return;

            if (pad != null)
            {
                if (pad.buttonEast.wasPressedThisFrame) SetVisible(false);
                if (pad.leftShoulder.wasPressedThisFrame) ChangePage(-1);
                if (pad.rightShoulder.wasPressedThisFrame) ChangePage(1);
                Vector2 nav = Vector2.zero;
                if (pad.dpad.up.isPressed) nav.y += 1f;
                if (pad.dpad.down.isPressed) nav.y -= 1f;
                if (pad.dpad.left.isPressed) nav.x -= 1f;
                if (pad.dpad.right.isPressed) nav.x += 1f;
                var ls = pad.leftStick.ReadValue();
                if (nav == Vector2.zero && ls.magnitude > 0.55f) nav = Mathf.Abs(ls.x) > Mathf.Abs(ls.y) ? new Vector2(Mathf.Sign(ls.x), 0f) : new Vector2(0f, Mathf.Sign(ls.y));
                if (nav != Vector2.zero)
                {
                    float now = Time.unscaledTime;
                    if (nav != _lastNav) { _lastNav = nav; _repeatAt = now + 0.32f; Navigate(nav); }
                    else if (now >= _repeatAt) { _repeatAt = now + 0.075f; Navigate(nav); }
                }
                else _lastNav = Vector2.zero;
                if (pad.buttonSouth.wasPressedThisFrame) Press();
            }
            if (kb != null)
            {
                if (kb.upArrowKey.wasPressedThisFrame) Navigate(Vector2.up);
                if (kb.downArrowKey.wasPressedThisFrame) Navigate(Vector2.down);
                if (kb.leftArrowKey.wasPressedThisFrame) Navigate(Vector2.left);
                if (kb.rightArrowKey.wasPressedThisFrame) Navigate(Vector2.right);
                if (kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame) Press();
                if (kb.pageUpKey.wasPressedThisFrame) ChangePage(-1);
                if (kb.pageDownKey.wasPressedThisFrame) ChangePage(1);
            }
            Mouse();
        }

        /// <summary>Where a control is on screen now (for tests that click it); false when it is not on the page being shown.</summary>
        public bool TryGetRect(string label, out Rect rect)
        {
            foreach (var h in _hit)
                if (h.item.Label == label) { rect = h.rect; return true; }
            rect = default;
            return false;
        }

        void ChangePage(int d)
        {
            if (Pages.Count == 0) return;
            _page = (_page + d + Pages.Count) % Pages.Count;
            _row = 0; _col = 0; _scrollY = 0f;
            EnsureSelectable();
        }

        PanelItem ItemAt(int row, int col)
        {
            var p = Page; if (p == null || row < 0 || row >= p.Rows.Count) return null;
            var r = p.Rows[row];
            return col == 1 && r.B != null ? r.B : r.A;
        }

        void Navigate(Vector2 nav)
        {
            var p = Page; if (p == null) return;
            if (nav.y != 0f)
            {
                int dir = nav.y > 0f ? -1 : 1;
                int r = _row;
                for (int guard = 0; guard < p.Rows.Count; guard++)
                {
                    r = (r + dir + p.Rows.Count) % p.Rows.Count;
                    var it = p.Rows[r].A;
                    if (it != null && it.Type != PanelItem.Kind.Info) break;
                }
                _row = r;
                if (_col == 1 && p.Rows[_row].B == null) _col = 0;
                return;
            }
            var item = ItemAt(_row, _col);
            if (item == null) return;
            bool two = p.Rows[_row].B != null;
            if (item.Type == PanelItem.Kind.Slider)
            {
                float v = Mathf.Clamp(item.GetFloat() + item.Step * Mathf.Sign(nav.x), item.Min, item.Max);
                item.SetFloat(Mathf.Round(v / item.Step) * item.Step);
                OnChanged?.Invoke();
            }
            else if (two) _col = nav.x > 0f ? 1 : 0;
        }

        void Press()
        {
            var item = ItemAt(_row, _col);
            if (item == null) return;
            switch (item.Type)
            {
                case PanelItem.Kind.Button: item.OnPress?.Invoke(); break;
                case PanelItem.Kind.Toggle: item.SetBool(!item.GetBool()); OnChanged?.Invoke(); break;
            }
        }

        void Mouse()
        {
            var m = UnityEngine.InputSystem.Mouse.current;
            if (m == null) return;
            Vector2 pos = m.position.ReadValue();
            pos.y = Screen.height - pos.y;
            bool down = m.leftButton.isPressed;
            bool pressed = down && !_mouseWasDown;
            foreach (var h in _hit)
            {
                if (!h.rect.Contains(pos)) continue;
                if (pressed || (down && _dragRow == h.row && _dragCol == h.col && h.item.Type == PanelItem.Kind.Slider))
                {
                    _row = h.row; _col = h.col;
                    if (h.item.Type == PanelItem.Kind.Slider)
                    {
                        _dragRow = h.row; _dragCol = h.col;
                        SetSliderFromMouse(h.item, h.rect, pos.x);
                    }
                    else if (pressed) Press();
                }
            }
            if (down && _dragRow >= 0)
            {
                var item = ItemAt(_dragRow, _dragCol);
                foreach (var h in _hit) if (h.row == _dragRow && h.col == _dragCol) { SetSliderFromMouse(h.item, h.rect, pos.x); break; }
            }
            if (!down) { _dragRow = -1; _dragCol = -1; }
            _mouseWasDown = down;
            var wheel = m.scroll.ReadValue().y;
            if (wheel != 0f) ChangePage(wheel > 0f ? -1 : 1);
        }

        void SetSliderFromMouse(PanelItem item, Rect rect, float x)
        {
            float trackL = rect.x + rect.width * 0.46f, trackR = rect.xMax - 62f * _scale;
            float t = Mathf.Clamp01((x - trackL) / Mathf.Max(1f, trackR - trackL));
            float v = Mathf.Lerp(item.Min, item.Max, t);
            item.SetFloat(Mathf.Round(v / item.Step) * item.Step);
            OnChanged?.Invoke();
        }

        // ------------------------------------------------------------------------------------------------ drawing

        float _scale = 1f;

        void OnGUI()
        {
            if (!Visible || Event.current.type != EventType.Repaint || Page == null) return;
            _scale = Mathf.Max(0.6f, Screen.height / 1080f);
            float s = _scale;
            if (_label == null || _label.fontSize != Mathf.RoundToInt(19f * s))
            {
                _label = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(19f * s), alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip, padding = new RectOffset() };
                _small = new GUIStyle(_label) { fontSize = Mathf.RoundToInt(14f * s) };
                _title = new GUIStyle(_label) { fontSize = Mathf.RoundToInt(26f * s), fontStyle = FontStyle.Bold };
            }
            _hit.Clear();
            float w = 600f * s, margin = 24f * s, x0 = margin, y = margin;
            float rowH = 40f * s, gap = 6f * s;
            var page = Page;
            // Panel background sized to the content.
            float contentH = 96f * s + page.Rows.Count * (rowH + gap) + 70f * s;
            Fill(new Rect(x0 - 12f * s, y - 12f * s, w + 24f * s, Mathf.Min(contentH, Screen.height - 2f * margin + 24f * s)), Back);
            Text(new Rect(x0, y, w, 34f * s), "JUICE SANDBOX", _title, Ink);
            y += 36f * s;
            // Tabs.
            float tabW = w / Pages.Count;
            for (int i = 0; i < Pages.Count; i++)
            {
                var r = new Rect(x0 + i * tabW, y, tabW - 4f * s, 30f * s);
                Fill(r, i == _page ? Sel : new Color(1, 1, 1, 0.05f));
                Text(r, Pages[i].Title, _label, i == _page ? Ink : Dim, TextAnchor.MiddleCenter);
                int pi = i;
                _tabs.Add((r, pi));
            }
            y += 38f * s;
            var m = UnityEngine.InputSystem.Mouse.current;
            Vector2 mp = m != null ? m.position.ReadValue() : Vector2.zero; mp.y = Screen.height - mp.y;
            for (int i = 0; i < _tabs.Count; i++)
                if (m != null && m.leftButton.wasPressedThisFrame && _tabs[i].rect.Contains(mp)) { _page = _tabs[i].page; _row = 0; _col = 0; }
            _tabs.Clear();

            for (int ri = 0; ri < page.Rows.Count; ri++)
            {
                var row = page.Rows[ri];
                if (row.A.Type == PanelItem.Kind.Info)
                {
                    Text(new Rect(x0, y, w, rowH), row.A.GetText(), _small, Dim);
                    y += rowH * 0.7f;
                    continue;
                }
                if (row.B == null) DrawItem(row.A, new Rect(x0, y, w, rowH), ri, 0);
                else
                {
                    float half = (w - gap) * 0.5f;
                    DrawItem(row.A, new Rect(x0, y, half, rowH), ri, 0);
                    DrawItem(row.B, new Rect(x0 + half + gap, y, half, rowH), ri, 1);
                }
                y += rowH + gap;
            }
            y += 6f * s;
            Text(new Rect(x0, y, w, 24f * s), Footer != null ? Footer() : "", _small, Dim);
            y += 24f * s;
            Text(new Rect(x0, y, w, 24f * s), "START close   LB/RB page   DPAD move   A press   left/right adjust   B close", _small, Dim);
        }

        readonly List<(Rect rect, int page)> _tabs = new List<(Rect, int)>();

        void DrawItem(PanelItem it, Rect r, int ri, int ci)
        {
            bool selected = ri == _row && ci == _col;
            var m = UnityEngine.InputSystem.Mouse.current;
            Vector2 mp = m != null ? m.position.ReadValue() : Vector2.zero; mp.y = Screen.height - mp.y;
            bool hover = r.Contains(mp);
            _hit.Add((r, ri, ci, it));
            Fill(r, selected ? Sel : hover ? new Color(1, 1, 1, 0.09f) : new Color(1, 1, 1, 0.045f));
            if (selected) Outline(r, Ink);
            float s = _scale;
            switch (it.Type)
            {
                case PanelItem.Kind.Button:
                {
                    bool on = it.Highlight != null && it.Highlight();
                    string label = it.GetText != null ? it.GetText() : it.Label;
                    Text(new Rect(r.x + 12f * s, r.y, r.width - 24f * s, r.height), label, _label, on ? OnCol : Ink);
                    if (!string.IsNullOrEmpty(it.Hint)) Text(new Rect(r.x, r.y, r.width - 10f * s, r.height), it.Hint, _small, Dim, TextAnchor.MiddleRight);
                    break;
                }
                case PanelItem.Kind.Toggle:
                {
                    bool on = it.GetBool();
                    Text(new Rect(r.x + 12f * s, r.y, r.width - 70f * s, r.height), it.Label, _label, Ink);
                    var box = new Rect(r.xMax - 52f * s, r.y + r.height * 0.5f - 11f * s, 40f * s, 22f * s);
                    Fill(box, on ? new Color(OnCol.r, OnCol.g, OnCol.b, 0.30f) : new Color(1, 1, 1, 0.08f));
                    Outline(box, on ? OnCol : Dim);
                    Text(box, on ? "ON" : "OFF", _small, on ? OnCol : Dim, TextAnchor.MiddleCenter);
                    break;
                }
                case PanelItem.Kind.Slider:
                {
                    float v = it.GetFloat();
                    Text(new Rect(r.x + 12f * s, r.y, r.width * 0.44f, r.height), it.Label, _label, Ink);
                    float tl = r.x + r.width * 0.46f, tr = r.xMax - 62f * s, cy = r.y + r.height * 0.5f;
                    Fill(new Rect(tl, cy - 2f * s, tr - tl, 4f * s), new Color(1, 1, 1, 0.16f));
                    float t = Mathf.InverseLerp(it.Min, it.Max, v);
                    Fill(new Rect(tl, cy - 2f * s, (tr - tl) * t, 4f * s), Ink);
                    Fill(new Rect(tl + (tr - tl) * t - 5f * s, cy - 10f * s, 10f * s, 20f * s), selected || hover ? Color.white : Ink);
                    Text(new Rect(tr + 8f * s, r.y, 54f * s, r.height), v.ToString(it.Format, System.Globalization.CultureInfo.InvariantCulture), _small, Ink, TextAnchor.MiddleRight);
                    break;
                }
            }
        }

        void Fill(Rect r, Color c)
        {
            var old = GUI.color; GUI.color = c; GUI.DrawTexture(r, _white); GUI.color = old;
        }

        void Outline(Rect r, Color c)
        {
            float t = Mathf.Max(1f, 2f * _scale);
            Fill(new Rect(r.x, r.y, r.width, t), c); Fill(new Rect(r.x, r.yMax - t, r.width, t), c);
            Fill(new Rect(r.x, r.y, t, r.height), c); Fill(new Rect(r.xMax - t, r.y, t, r.height), c);
        }

        void Text(Rect r, string s, GUIStyle style, Color c, TextAnchor anchor = TextAnchor.MiddleLeft)
        {
            var old = GUI.contentColor;
            style.alignment = anchor;
            GUI.contentColor = c;
            GUI.Label(r, s, style);
            GUI.contentColor = old;
            style.alignment = TextAnchor.MiddleLeft;
        }
    }
}
