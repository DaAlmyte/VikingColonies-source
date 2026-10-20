using System;
using System.Linq;
using VikingColonies.Core;

namespace VikingColonies.Gui
{
    /// <summary>A saga line in the reader's language: "Day 12: Astrid came to live in the colony."</summary>
    internal static class SagaText
    {
        public static string Line(SagaEntry entry)
        {
            Localization loc = Localization.instance;
            string[] words = entry.Args.Select(a => loc.Localize(a)).ToArray();
            // A death keeps its cause as a token and its argument ("killed by {0}", "Greydwarf"): put them together.
            if (entry.Kind == "died" && entry.Args.Count >= 3)
                words = new[] { words[0], Format(loc.Localize(entry.Args[1]), loc.Localize(entry.Args[2])) };
            string token = "$vc_saga_" + entry.Kind;
            if (entry.Kind == "repelled" && entry.Args.Count > 0) token = "$vc_saga_repelled_by";   // the guards who held it (1.1)
            string format = loc.Localize(token);
            if (string.IsNullOrEmpty(format) || format.StartsWith("[")) return Unknown(entry.Day, words);
            string line = Format(format, new object[] { entry.Day }.Concat(words).ToArray());
            return line ?? Unknown(entry.Day, words);
        }

        private static string Unknown(int day, string[] words) =>
            string.Format(Localization.instance.Localize("$vc_saga_unknown"), day, string.Join(" ", words));

        /// <summary>string.Format that gives null instead of throwing when a line has fewer words than its text expects.</summary>
        private static string Format(string format, params object[] args)
        {
            try
            {
                return string.Format(format, args);
            }
            catch (FormatException)
            {
                return null;
            }
        }
    }
}
