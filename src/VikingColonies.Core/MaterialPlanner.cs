using System;
using System.Collections.Generic;
using System.Linq;

namespace VikingColonies.Core
{
    public sealed class FetchStep
    {
        public FetchStep(int store, string item, int amount)
        {
            Store = store;
            Item = item;
            Amount = amount;
        }

        /// <summary>Position in the list of stores given to the planner.</summary>
        public int Store { get; }
        public string Item { get; }
        public int Amount { get; }
    }

    public sealed class MaterialPlan
    {
        public List<FetchStep> Steps = new List<FetchStep>();
        /// <summary>What the next piece still lacks after what he carries and every store: nothing to fetch it from.</summary>
        public Dictionary<string, int> Missing = new Dictionary<string, int>();
    }

    /// <summary>What the builder should fetch, and from where, before putting up the next pieces.</summary>
    public static class MaterialPlanner
    {
        public static bool CanBuild(IReadOnlyDictionary<string, int> needs, CarryLoad carried) =>
            needs.All(n => carried.Count(n.Key) >= n.Value);

        /// <param name="upcoming">Each next piece's needs, the piece about to be built first.</param>
        /// <param name="stores">What each store holds, nearest (or preferred) first.</param>
        public static MaterialPlan Plan(IReadOnlyList<IReadOnlyDictionary<string, int>> upcoming, CarryLoad carried, int capacity,
            IReadOnlyList<IReadOnlyDictionary<string, int>> stores)
        {
            var plan = new MaterialPlan();
            if (upcoming.Count == 0) return plan;

            // The next piece always; then more pieces while the load stays within his capacity.
            var wanted = new Dictionary<string, int>();
            int total = 0;
            for (int i = 0; i < upcoming.Count; i++)
            {
                int size = upcoming[i].Values.Sum();
                if (i > 0 && total + size > capacity) break;
                foreach (KeyValuePair<string, int> need in upcoming[i]) wanted[need.Key] = Get(wanted, need.Key) + need.Value;
                total += size;
            }

            var left = stores.Select(s => s.ToDictionary(kv => kv.Key, kv => kv.Value)).ToList();
            foreach (KeyValuePair<string, int> item in wanted.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                int need = item.Value - carried.Count(item.Key);
                for (int s = 0; s < left.Count && need > 0; s++)
                {
                    int take = Math.Min(need, Get(left[s], item.Key));
                    if (take <= 0) continue;
                    plan.Steps.Add(new FetchStep(s, item.Key, take));
                    left[s][item.Key] -= take;
                    need -= take;
                }
            }
            plan.Steps = plan.Steps.OrderBy(step => step.Store).ToList();

            foreach (KeyValuePair<string, int> need in upcoming[0])
            {
                int available = carried.Count(need.Key) + stores.Sum(s => Get(s, need.Key));
                if (available < need.Value) plan.Missing[need.Key] = need.Value - available;
            }
            return plan;
        }

        private static int Get(IReadOnlyDictionary<string, int> items, string key) =>
            items.TryGetValue(key, out int n) ? n : 0;

        private static int Get(Dictionary<string, int> items, string key) =>
            items.TryGetValue(key, out int n) ? n : 0;
    }
}
