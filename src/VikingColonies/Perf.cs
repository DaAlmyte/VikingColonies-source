using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace VikingColonies
{
    /// <summary>
    /// vc_perf's stopwatch for named parts of the work (world scans, fall planning, target searches): costs nothing
    /// unless vc_perf is measuring. Usage: <c>long t = Perf.Start(); …; Perf.Stop("world scan: trees", t);</c>
    /// </summary>
    internal static class Perf
    {
        public static bool On;
        private static readonly Dictionary<string, long> s_total = new Dictionary<string, long>();
        private static readonly Dictionary<string, long> s_worst = new Dictionary<string, long>();
        private static readonly Dictionary<string, int> s_count = new Dictionary<string, int>();

        public static long Start() => On ? Stopwatch.GetTimestamp() : 0;

        public static void Stop(string part, long start)
        {
            if (start == 0 || !On) return;
            long spent = Stopwatch.GetTimestamp() - start;
            s_total.TryGetValue(part, out long total);
            s_total[part] = total + spent;
            s_count.TryGetValue(part, out int count);
            s_count[part] = count + 1;
            s_worst.TryGetValue(part, out long worst);
            if (spent > worst) s_worst[part] = spent;
        }

        public static void Clear()
        {
            s_total.Clear();
            s_worst.Clear();
            s_count.Clear();
        }

        /// <summary>"world scan: trees 12 times, 3.1 ms in all, slowest 0.4 ms; …", costliest first.</summary>
        public static string Report()
        {
            double toMs = 1000.0 / Stopwatch.Frequency;
            return s_total.Count == 0 ? "nothing measured" : string.Join("; ", s_total.OrderByDescending(p => p.Value).Select(p =>
                $"{p.Key} {s_count[p.Key]} times, {p.Value * toMs:0.0} ms in all, slowest {s_worst[p.Key] * toMs:0.0} ms"));
        }
    }
}
