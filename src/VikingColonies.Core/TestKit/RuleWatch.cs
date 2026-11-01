using System;
using System.Collections.Generic;
using System.Linq;

namespace TestKit
{
    /// <summary>
    /// The machinery of a watchdog that checks a game's rules while a test runs (mod-neutral; tools/testkit/README.md):
    /// named rules switched on and off, a break held for some looks in a row before it counts, one line per rule and
    /// subject a while, and how many lines each rule wrote. The rules themselves are the mod's.
    /// A break's line: "&lt;name&gt;: FAIL &lt;rule&gt; (&lt;details&gt;)", which the test runner counts as a failure.
    /// </summary>
    public sealed class RuleWatch
    {
        private readonly string _name;
        private readonly double _repeatSeconds;
        private readonly HashSet<string> _off = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, double> _lastLine = new Dictionary<string, double>(StringComparer.Ordinal);
        private readonly Dictionary<string, Dictionary<string, int>> _looks = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);

        public RuleWatch(string name, IEnumerable<string> rules, double repeatSeconds)
        {
            _name = name ?? "watchdog";
            Rules = (rules ?? new string[0]).ToList();
            _repeatSeconds = repeatSeconds;
            Seen = Rules.ToDictionary(r => r, r => 0, StringComparer.Ordinal);
        }

        public IReadOnlyList<string> Rules { get; }

        /// <summary>How many lines each rule has written since the last reset.</summary>
        public Dictionary<string, int> Seen { get; }

        public string Line(string rule, string details) => $"{_name}: FAIL {rule} ({details})";

        public bool IsOn(string rule) => !_off.Contains(rule);

        /// <summary>Switches a rule on or off; false for a rule that doesn't exist.</summary>
        public bool SetRule(string rule, bool on)
        {
            if (!Rules.Contains(rule)) return false;
            if (on) _off.Remove(rule);
            else _off.Add(rule);
            return true;
        }

        /// <summary>Every rule on, nothing remembered, the counts at 0 (a run's start).</summary>
        public void Reset()
        {
            _off.Clear();
            _lastLine.Clear();
            _looks.Clear();
            foreach (string rule in Rules) Seen[rule] = 0;
        }

        /// <summary>
        /// Whether a condition of this kind has held for <paramref name="subject"/> at <paramref name="looks"/> looks in a row
        /// (a moment's glitch the game puts right by itself is no break).
        /// </summary>
        public bool Held(string kind, string subject, bool holds, int looks = 2)
        {
            if (!_looks.TryGetValue(kind, out Dictionary<string, int> counts)) _looks[kind] = counts = new Dictionary<string, int>(StringComparer.Ordinal);
            string key = subject ?? "";
            if (!holds)
            {
                counts.Remove(key);
                return false;
            }
            counts.TryGetValue(key, out int n);
            counts[key] = n + 1;
            return n + 1 >= looks;
        }

        /// <summary>Forgets the subjects of this kind that are no longer there (unloaded, gone).</summary>
        public void Forget(string kind, ICollection<string> present)
        {
            if (!_looks.TryGetValue(kind, out Dictionary<string, int> counts)) return;
            foreach (string gone in counts.Keys.Where(k => !present.Contains(k)).ToList()) counts.Remove(gone);
        }

        /// <summary>A break's line into <paramref name="lines"/>, unless its rule is off or it wrote one for this subject lately.</summary>
        public void Emit(List<string> lines, double now, string rule, string subject, string details)
        {
            if (!IsOn(rule)) return;
            string key = rule + "|" + (subject ?? "");
            if (_lastLine.TryGetValue(key, out double last) && now - last < _repeatSeconds) return;
            _lastLine[key] = now;
            if (Seen.ContainsKey(rule)) Seen[rule]++;
            lines.Add(Line(rule, details));
        }
    }
}
