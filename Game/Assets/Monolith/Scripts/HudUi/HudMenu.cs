using System.Collections.Generic;

namespace Monolith.HudUi
{
    /// <summary>
    /// What the start screen shows, as plain data, the way <see cref="HudFrame"/> is the HUD's: a list of scenarios on the left (group headings, the scenarios, a quit row), a list of
    /// difficulty levels on the right, the two main gauges between them showing the chosen level, a line about the highlighted scenario and a line of keys. The HUD's own renderer
    /// draws it (<see cref="HudRenderer"/>, when <see cref="HudFrame.Menu"/> is set), in whatever HUD style the player has.
    /// </summary>
    public sealed class HudMenu
    {
        public sealed class Row
        {
            public string Label;
            /// <summary>A group heading (never selected).</summary>
            public bool Heading;
            /// <summary>The quit row.</summary>
            public bool Quit;
        }

        public string Title = "MONOLITH", Subtitle = "SELECT A SCENARIO";

        /// <summary>The left list: headings and scenarios, the quit row last.</summary>
        public readonly List<Row> Rows = new List<Row>();
        /// <summary>The highlighted row (an index into <see cref="Rows"/>, never a heading).</summary>
        public int Selected;

        /// <summary>The difficulty levels, and for each one a number from 0 to 1 for the second gauge (<see cref="StatName"/>: how hard Earth's guns hit you).</summary>
        public readonly List<string> Levels = new List<string>();
        public readonly List<float> Stat = new List<float>();
        public string StatName = "DAMAGE TO YOU";
        public int Level;
        /// <summary>False for a scenario with no difficulty (a sandbox): the level list and the gauges stand down.</summary>
        public bool LevelApplies = true;
        /// <summary>True when the cursor is in the difficulty list, false when it is in the scenario list.</summary>
        public bool LevelFocus;

        /// <summary>A line or two about the highlighted scenario.</summary>
        public string Description;
        /// <summary>The line of keys at the bottom, and the corner note at the top right (the HUD style and how to change it).</summary>
        public string Hint, Corner;
        /// <summary>0 to 1: how much of the screen's contents show (the screen fades in).</summary>
        public float Fade = 1f;
    }
}
