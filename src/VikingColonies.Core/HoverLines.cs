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
}
