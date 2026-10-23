using System;
using System.Linq;

namespace VikingColonies.Core
{
    /// <summary>Lines of a hover text (issue #1, 1.4.1: another mod's line on a colony building that Shift+E doesn't do).</summary>
    public static class HoverLines
    {
        /// <summary>
        /// The hover without its key lines ("[Shift + E] Rename") that offer <paramref name="action"/>, and when
        /// <paramref name="key"/> is given only those on that key (another key's line does what it says); other lines stay.
        /// </summary>
        public static string WithoutKeyLine(string hover, string action, string key = null)
        {
            if (string.IsNullOrEmpty(hover)) return "";
            if (string.IsNullOrEmpty(action)) return hover;
            return string.Join("\n", hover.Split('\n').Where(line => !(line.Contains("[") && line.Contains("]") &&
                line.IndexOf(action, StringComparison.Ordinal) > line.LastIndexOf(']') &&
                (string.IsNullOrEmpty(key) || line.Substring(0, line.LastIndexOf(']')).IndexOf(key, StringComparison.Ordinal) >= 0))).ToArray());
        }
    }

    public enum ReplyPlace { HutWindow, HallWindow, Corner }

    /// <summary>
    /// Where a reply to the player goes ("No jobless settlers…", "Not your colony"…): the open window's message line, else the
    /// corner where the game lists messages, never the middle of the screen (1.4.3, the player's watch of 1.4.2: Hire's
    /// long reply ran across the screen, too long to read).
    /// </summary>
    public static class Replies
    {
        public static ReplyPlace Place(bool hutWindowOpen, bool hallWindowOpen) =>
            hutWindowOpen ? ReplyPlace.HutWindow : hallWindowOpen ? ReplyPlace.HallWindow : ReplyPlace.Corner;
    }

    /// <summary>
    /// When Shift+E opens a colony building's window where the player presses it (issue #1's Player.Interact hook): only
    /// the local player's Shift+E (not held), on the building's own chest, the thing the game would use first (another
    /// mod's object under a hut keeps its own Shift+E), and not while attacking or dodging, as the game itself won't
    /// (1.4.2's review, done in 1.4.3).
    /// </summary>
    public static class ShiftE
    {
        public static bool OpensWindow(bool alt, bool hold, bool localPlayer, bool onBuilding, bool nearestIsBuilding, bool attacking, bool dodging) =>
            alt && !hold && localPlayer && onBuilding && nearestIsBuilding && !attacking && !dodging;
    }
}
