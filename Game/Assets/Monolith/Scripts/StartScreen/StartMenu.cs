using System;
using System.Collections.Generic;
using Monolith.HudUi;

namespace Monolith.Start
{
    public enum EntryKind { Mission, Sandbox, Quit }

    /// <summary>One thing the start screen can launch: a mission (by id, the name of its file) or a sandbox (a scene name), or the quit row.</summary>
    public sealed class StartEntry
    {
        public string Id, Title, Detail;
        public EntryKind Kind;
    }

    public enum MenuKey { Up, Down, Left, Right, Accept, Back }

    public enum MenuResult { None, Launch, Quit }

    /// <summary>
    /// The start screen's choice, with no drawing and no devices in it, so it can be checked without a screen. Two lists with one cursor: the scenarios (missions, then sandboxes, then quit)
    /// and the difficulty levels. Up and down move in the list the cursor is in; accepting a mission moves the cursor to the levels, accepting a level launches, accepting a sandbox
    /// launches it (it has no difficulty); left and back return to the scenarios; back from the scenarios jumps to the quit row and, pressed there, quits.
    /// </summary>
    public sealed class StartMenu
    {
        public readonly List<StartEntry> Entries = new List<StartEntry>();
        public readonly List<string> Levels = new List<string>();
        /// <summary>For each level, how hard Earth's guns hit you (0 to 1), for the second gauge.</summary>
        public readonly List<float> Stat = new List<float>();
        public int Index, Level;
        public bool LevelFocus;

        /// <summary>What was chosen when <see cref="Press"/> last returned <see cref="MenuResult.Launch"/>.</summary>
        public StartEntry Chosen { get; private set; }

        public StartEntry Selected => Entries.Count > 0 ? Entries[Math.Max(0, Math.Min(Index, Entries.Count - 1))] : null;
        public bool LevelApplies => Selected != null && Selected.Kind == EntryKind.Mission;
        public int QuitIndex { get { for (int i = 0; i < Entries.Count; i++) if (Entries[i].Kind == EntryKind.Quit) return i; return Entries.Count - 1; } }
        public string LevelName => Level >= 0 && Level < Levels.Count ? Levels[Level] : "Medium";

        static int Wrap(int v, int n) => n <= 0 ? 0 : ((v % n) + n) % n;

        public MenuResult Press(MenuKey key)
        {
            if (Entries.Count == 0) return MenuResult.None;
            if (LevelFocus && !LevelApplies) LevelFocus = false;
            switch (key)
            {
                case MenuKey.Up:
                case MenuKey.Down:
                {
                    int d = key == MenuKey.Up ? -1 : 1;
                    if (LevelFocus) Level = Wrap(Level + d, Levels.Count);
                    else Index = Wrap(Index + d, Entries.Count);
                    return MenuResult.None;
                }
                case MenuKey.Left:
                    LevelFocus = false;
                    return MenuResult.None;
                case MenuKey.Right:
                    if (!LevelFocus && LevelApplies) LevelFocus = true;
                    return MenuResult.None;
                case MenuKey.Accept:
                    if (LevelFocus) { Chosen = Selected; return MenuResult.Launch; }
                    switch (Selected.Kind)
                    {
                        case EntryKind.Quit: return MenuResult.Quit;
                        case EntryKind.Sandbox: Chosen = Selected; return MenuResult.Launch;
                        default: LevelFocus = true; return MenuResult.None;
                    }
                case MenuKey.Back:
                    if (LevelFocus) { LevelFocus = false; return MenuResult.None; }
                    if (Selected.Kind != EntryKind.Quit) { Index = QuitIndex; return MenuResult.None; }
                    return MenuResult.Quit;
            }
            return MenuResult.None;
        }

        /// <summary>Put the cursor on an entry by id (a remembered choice); false when there is none.</summary>
        public bool Select(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            for (int i = 0; i < Entries.Count; i++)
                if (Entries[i].Kind != EntryKind.Quit && string.Equals(Entries[i].Id, id, StringComparison.OrdinalIgnoreCase)) { Index = i; return true; }
            return false;
        }

        /// <summary>Choose a level by name (a remembered choice, a flag); false when there is none by that name.</summary>
        public bool SelectLevel(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (int.TryParse(name, out int n) && n >= 0 && n < Levels.Count) { Level = n; return true; }
            for (int i = 0; i < Levels.Count; i++)
                if (string.Equals(Levels[i], name, StringComparison.OrdinalIgnoreCase) || string.Equals(Levels[i].Replace(" ", ""), name.Replace(" ", ""), StringComparison.OrdinalIgnoreCase)) { Level = i; return true; }
            return false;
        }

        // ------------------------------------------------------------------------------------------------ what the HUD draws

        /// <summary>The row of <see cref="HudMenu.Rows"/> an entry is on.</summary>
        public int RowOf(int entry)
        {
            int row = 0;
            EntryKind? group = null;
            for (int i = 0; i < Entries.Count; i++)
            {
                if (Entries[i].Kind != EntryKind.Quit && group != Entries[i].Kind) { group = Entries[i].Kind; row++; }
                if (i == entry) return row;
                row++;
            }
            return row;
        }

        /// <summary>Make the HUD's menu data for these entries (the rows and the levels; the cursor is <see cref="Sync"/>'s).</summary>
        public void Build(HudMenu m)
        {
            m.Rows.Clear(); m.Levels.Clear(); m.Stat.Clear();
            EntryKind? group = null;
            foreach (var e in Entries)
            {
                if (e.Kind != EntryKind.Quit && group != e.Kind)
                {
                    group = e.Kind;
                    m.Rows.Add(new HudMenu.Row { Heading = true, Label = e.Kind == EntryKind.Mission ? "Missions" : "Sandboxes" });
                }
                m.Rows.Add(new HudMenu.Row { Label = e.Title, Quit = e.Kind == EntryKind.Quit });
            }
            m.Levels.AddRange(Levels);
            m.Stat.AddRange(Stat);
        }

        /// <summary>Copy the cursor and the highlighted scenario's line into the HUD's menu data.</summary>
        public void Sync(HudMenu m)
        {
            m.Selected = RowOf(Index);
            m.Level = Level;
            m.LevelApplies = LevelApplies;
            m.LevelFocus = LevelFocus && LevelApplies;
            m.Description = Selected?.Detail;
        }
    }
}
