using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TestKit;

namespace VikingColonies.Core
{
    /// <summary>What the watchdog knows of one loaded settler at a look (the plugin gathers it).</summary>
    public sealed class SettlerFacts
    {
        public string Id = "";
        public string Name = "";
        public float X, Y, Z;
        /// <summary>The ground's height under them (NaN: not known, e.g. the zone isn't loaded).</summary>
        public float Ground = float.NaN;
        public string Status = "";
        /// <summary>A test has them paused, walking a test walk, or the mod has them making way (their status stays as it was).</summary>
        public bool Paused;
        /// <summary>Sitting or lying down (a chair's back or a bed may hold their middle).</summary>
        public bool Resting;
        /// <summary>Their middle is inside a solid piece.</summary>
        public bool InsideSolid;
    }

    /// <summary>A hut with places for workers.</summary>
    public sealed class HutFacts
    {
        public string Id = "";
        public string Name = "";
        public List<string> Workers = new List<string>();
        public int Places = 1;
    }

    /// <summary>
    /// The watchdog (the flake hunt and the watchdog, 26 Oct 2026): the colony's rules that always hold, checked every few
    /// seconds while a test runs (vc_watchdog, test worlds only), on TestKit's <see cref="RuleWatch"/>. A break is one line,
    /// <c>vc_watchdog: FAIL rule (details)</c>, that the runner counts as a failure; once per rule and settler (or hut, or
    /// hand-over) a minute. Cheap and certain rules only: a rule that could fire on a healthy colony is worse than none.
    /// </summary>
    public sealed class Watchdog
    {
        public const float LookSeconds = 5f;
        /// <summary>Longer than any wait the mod makes on purpose in an on-the-way status (a chest's 60 s patience, the review of 26 Oct).</summary>
        public const float StuckSeconds = 120f;
        public const float StuckMetres = 1f;
        /// <summary>Deeper than this under the ground is under it (a dug floor's edge or a step is less).</summary>
        public const float UnderGround = 1.5f;
        /// <summary>Valheim's world is 10,000 m round its middle, with its edge beyond.</summary>
        public const float WorldRadius = 10500f;
        public const float RepeatSeconds = 60f;

        public static readonly IReadOnlyList<string> Rules = new[] { "items", "position", "stuck", "wall", "jobs" };

        /// <summary>
        /// Statuses that say a settler is on their way somewhere: standing still past <see cref="StuckSeconds"/> in one is stuck.
        /// Not fleeing (they wait at a shelter spot while the threat lasts) nor arriving (a newcomer waits up to 3 minutes for a
        /// way in before the hall takes them in): the review of 26 Oct.
        /// </summary>
        public static readonly HashSet<string> OnTheWay = new HashSet<string>(StringComparer.Ordinal)
        {
            "$vc_status_heading_to_work", "$vc_status_fetching", "$vc_status_delivering", "$vc_status_delivering_full",
            "$vc_status_delivering_hall_full", "$vc_status_courier_picking_up", "$vc_status_courier_delivering",
            "$vc_status_courier_putting_back", "$vc_status_cook_fetching",
        };

        private readonly RuleWatch _watch = new RuleWatch("vc_watchdog", Rules, RepeatSeconds);
        private readonly Dictionary<string, Anchor> _anchors = new Dictionary<string, Anchor>(StringComparer.Ordinal);

        private sealed class Anchor
        {
            public float X, Z;
            public double Since;
            public string Status;
        }

        /// <summary>How many lines each rule has written since the last reset.</summary>
        public Dictionary<string, int> Seen => _watch.Seen;

        public static string Line(string rule, string details) => $"vc_watchdog: FAIL {rule} ({details})";

        public bool IsOn(string rule) => _watch.IsOn(rule);

        /// <summary>Switches a rule on or off; false for a rule that doesn't exist.</summary>
        public bool SetRule(string rule, bool on) => _watch.SetRule(rule, on);

        /// <summary>Every rule on, nothing remembered, the counts at 0 (a run's start).</summary>
        public void Reset()
        {
            _watch.Reset();
            _anchors.Clear();
        }

        /// <summary>One look at the loaded settlers and huts: the lines of what broke.</summary>
        public List<string> Look(double now, IList<SettlerFacts> settlers, IList<HutFacts> huts)
        {
            var lines = new List<string>();
            List<SettlerFacts> here = (settlers ?? new SettlerFacts[0]).Where(s => s != null).ToList();
            List<HutFacts> hutsHere = (huts ?? new HutFacts[0]).Where(h => h != null).ToList();
            var present = new HashSet<string>(here.Select(s => s.Id ?? ""), StringComparer.Ordinal);
            foreach (string gone in _anchors.Keys.Where(k => !present.Contains(k)).ToList()) _anchors.Remove(gone);
            _watch.Forget("under", present);
            _watch.Forget("wall", present);
            _watch.Forget("twins", present);
            _watch.Forget("over", new HashSet<string>(hutsHere.Select(h => h.Id ?? ""), StringComparer.Ordinal));

            foreach (SettlerFacts s in here)
            {
                string id = s.Id ?? "";
                string name = string.IsNullOrEmpty(s.Name) ? id : s.Name;
                if (!Finite(s.X) || !Finite(s.Y) || !Finite(s.Z))
                {
                    _watch.Emit(lines, now, "position", id, $"{name}: not a position ({F(s.X)}, {F(s.Y)}, {F(s.Z)})");
                    _anchors.Remove(id);
                    continue;   // nothing else about them can be measured
                }
                if (Math.Sqrt((double)s.X * s.X + (double)s.Z * s.Z) > WorldRadius)
                    _watch.Emit(lines, now, "position", id, $"{name}: outside the world at ({F(s.X)}, {F(s.Z)})");
                float under = Finite(s.Ground) ? s.Ground - s.Y : 0f;
                if (_watch.Held("under", id, under > UnderGround))
                    _watch.Emit(lines, now, "position", id, $"{name}: {under.ToString("0.0", CultureInfo.InvariantCulture)} m under the ground at ({F(s.X)}, {F(s.Y)}, {F(s.Z)})");
                if (_watch.Held("wall", id, s.InsideSolid && !s.Resting && !s.Paused))
                    _watch.Emit(lines, now, "wall", id, $"{name}: inside a solid piece at ({F(s.X)}, {F(s.Y)}, {F(s.Z)}), status {Key(s.Status)}");
                Stuck(lines, now, s, id, name);
            }

            // Two bodies with one id: a body brought back while an old copy lay unloaded is put right at the old one's next
            // look (two looks in a row, the review of 26 Oct).
            var twins = here.Where(s => !string.IsNullOrEmpty(s.Id)).GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.ToList());
            foreach (KeyValuePair<string, List<SettlerFacts>> t in twins)
                if (_watch.Held("twins", t.Key, t.Value.Count > 1))
                    _watch.Emit(lines, now, "jobs", "id " + t.Key, $"two settlers with the id {t.Key}: {string.Join(" and ", t.Value.Select(v => v.Name))}");

            foreach (HutFacts h in hutsHere)
            {
                int workers = h.Workers != null ? h.Workers.Count : 0;
                if (_watch.Held("over", h.Id ?? "", workers > h.Places))
                    _watch.Emit(lines, now, "jobs", "hut " + h.Id, $"{h.Name} has {workers} workers for {h.Places} place{(h.Places == 1 ? "" : "s")}: {string.Join(", ", h.Workers)}");
            }
            return lines;
        }

        /// <summary>
        /// A hand-over between a chest and a settler's load (a deposit, a take): every item's total in the two is the same
        /// before and after, but for items the chest doesn't know (<paramref name="known"/> false), which a deposit discards.
        /// </summary>
        public List<string> Handover(double now, string what, IDictionary<string, int> chestBefore, IDictionary<string, int> loadBefore,
                                     IDictionary<string, int> chestAfter, IDictionary<string, int> loadAfter, Func<string, bool> known)
        {
            var lines = new List<string>();
            Dictionary<string, int> before = Sum(chestBefore, loadBefore), after = Sum(chestAfter, loadAfter);
            var changed = new List<string>();
            foreach (string item in before.Keys.Union(after.Keys).OrderBy(i => i, StringComparer.Ordinal))
            {
                before.TryGetValue(item, out int b);
                after.TryGetValue(item, out int a);
                if (a == b || (a < b && known != null && !known(item))) continue;
                changed.Add($"{item} {b} before, {a} after");
            }
            if (changed.Count > 0) _watch.Emit(lines, now, "items", what ?? "", $"{what}: {string.Join("; ", changed)}");
            return lines;
        }

        private void Stuck(List<string> lines, double now, SettlerFacts s, string id, string name)
        {
            string key = Key(s.Status);
            if (s.Paused || !OnTheWay.Contains("$" + key))
            {
                _anchors.Remove(id);
                return;
            }
            if (!_anchors.TryGetValue(id, out Anchor at) || at.Status != key || Distance(at.X, at.Z, s.X, s.Z) >= StuckMetres)
            {
                _anchors[id] = new Anchor { X = s.X, Z = s.Z, Since = now, Status = key };
                return;
            }
            if (now - at.Since >= StuckSeconds)
                _watch.Emit(lines, now, "stuck", id, $"{name}: {key}, not {StuckMetres:0} m from ({F(at.X)}, {F(at.Z)}) in {now - at.Since:0} s");
        }

        /// <summary>The status's key without its '$' and the words after it ("$vc_status_fetching Wood": vc_status_fetching).</summary>
        private static string Key(string status)
        {
            string s = (status ?? "").Trim();
            int space = s.IndexOf(' ');
            if (space >= 0) s = s.Substring(0, space);
            return s.TrimStart('$');
        }

        private static Dictionary<string, int> Sum(IDictionary<string, int> a, IDictionary<string, int> b)
        {
            var total = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (IDictionary<string, int> part in new[] { a, b })
            {
                if (part == null) continue;
                foreach (KeyValuePair<string, int> kv in part)
                    total[kv.Key] = (total.TryGetValue(kv.Key, out int n) ? n : 0) + kv.Value;
            }
            return total;
        }

        private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        private static float Distance(float x1, float z1, float x2, float z2) => (float)Math.Sqrt((x1 - x2) * (x1 - x2) + (z1 - z2) * (z1 - z2));

        private static string F(float v) => Finite(v) ? v.ToString("0", CultureInfo.InvariantCulture) : v.ToString(CultureInfo.InvariantCulture);
    }
}
