using System;
using System.Collections.Generic;
using System.Linq;

namespace VikingColonies.Core
{
    /// <summary>One line of the Town Hall's Work orders tab, as far as its place in the list goes.</summary>
    public sealed class WorkOrderRow
    {
        public string BuildingId = "";
        public double First;
        public bool Paused;
        public double Created;
        /// <summary>Waits for the other orders unless marked first (the Town Hall's camp).</summary>
        public bool Later;
    }

    /// <summary>The Work orders tab's list, in the order builders take orders.</summary>
    public static class WorkOrderList
    {
        /// <summary>
        /// Not set aside before set aside; marked first (newest mark first) before unmarked; an order that waits for the
        /// others (the camp) after them; then the oldest.
        /// </summary>
        public static List<WorkOrderRow> Sorted(IEnumerable<WorkOrderRow> rows) =>
            rows.Where(r => r != null)
                .OrderBy(r => r.Paused ? 1 : 0)
                .ThenBy(r => r.First > 0 ? 0 : 1)
                .ThenByDescending(r => r.First)
                .ThenBy(r => r.First > 0 || !r.Later ? 0 : 1)
                .ThenBy(r => r.Created)
                .ThenBy(r => r.BuildingId, StringComparer.Ordinal)
                .ToList();
    }

    /// <summary>What the colony's orders still need altogether, and what of it the chests don't hold.</summary>
    public static class NeedsTally
    {
        public static void Add(Dictionary<string, int> total, IReadOnlyDictionary<string, int> needs)
        {
            foreach (KeyValuePair<string, int> need in needs)
                if (need.Value > 0) total[need.Key] = (total.TryGetValue(need.Key, out int n) ? n : 0) + need.Value;
        }

        /// <summary>Each item the orders need more of than <paramref name="stock"/> holds, with how many more.</summary>
        public static Dictionary<string, int> Shortfall(IReadOnlyDictionary<string, int> total, IReadOnlyDictionary<string, int> stock)
        {
            var missing = new Dictionary<string, int>();
            foreach (KeyValuePair<string, int> need in total)
            {
                int have = stock.TryGetValue(need.Key, out int n) ? n : 0;
                if (need.Value > have) missing[need.Key] = need.Value - have;
            }
            return missing;
        }
    }
}
