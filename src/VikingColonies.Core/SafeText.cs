using System;
using System.Text.RegularExpressions;

namespace VikingColonies.Core
{
    /// <summary>
    /// string.Format for a pattern that comes from another computer or a save (a message token, a settler's activity):
    /// a broken pattern ("{5}") is shown as it is instead of throwing (the security sweep of 1.4.1).
    /// </summary>
    public static class SafeText
    {
        /// <summary>A pattern longer than this is shown cut, not filled in: no message of ours is near it.</summary>
        public const int MaxPattern = 600;

        private static readonly Regex s_wide = new Regex(@"\{\s*\d+\s*,\s*-?\d{3,}", RegexOptions.CultureInvariant);

        public static string Format(string pattern, params object[] args)
        {
            if (pattern == null) return "";
            // A long pattern, or a field 100 or more wide ("{0,999999}"), would fill memory without throwing (the review of 1.4.1).
            if (pattern.Length > MaxPattern) return pattern.Substring(0, MaxPattern);
            if (s_wide.IsMatch(pattern)) return pattern;
            try
            {
                return string.Format(pattern, args ?? new object[0]);
            }
            catch (FormatException)
            {
                return pattern;
            }
        }
    }
}
