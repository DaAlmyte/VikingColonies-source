using System;
using System.Collections.Generic;
using System.Linq;

namespace VikingColonies.Core
{
    /// <summary>One building's shortfall of one thing on the Needs tab, and what's being done about it.</summary>
    public sealed class NeedLine
    {
        /// <summary>The building's id.</summary>
        public string Site = "";
        /// <summary>What it is short of: an item, or <see cref="StockList.AnyFood"/>.</summary>
        public string Item = "";
        /// <summary>How many it is short.</summary>
        public int Amount;
        /// <summary>On their way: a courier has the delivery (<see cref="Courier"/>, the first one).</summary>
        public int Coming;
        public string Courier = "";
        /// <summary>A courier can fetch them: the storehouse or a hut has them to spare.</summary>
        public int Waiting;
        /// <summary>Only where couriers don't take from (the Town Hall, a Builder's Hut, a hut with "Couriers take the rest" off).</summary>
        public int Elsewhere;
        /// <summary>The building holding most of <see cref="Elsewhere"/>.</summary>
        public string ElsewhereAt = "";
        /// <summary>Its chest has no room for it: nothing more can come.</summary>
        public bool NoRoom;

        /// <summary>What nobody in the colony has: the player's to bring.</summary>
        public int Missing => NoRoom ? 0 : Math.Max(0, Amount - Coming - Waiting - Elsewhere);
    }

    /// <summary>The Needs tab: every building's shortfalls, and the colony's shopping list.</summary>
    public sealed class NeedsReport
    {
        public List<NeedLine> Lines = new List<NeedLine>();
        /// <summary>"Bring: 40 Iron, 10 Bronze": what nobody has, the most first.</summary>
        public List<KeepEntry> Bring = new List<KeepEntry>();
    }

    /// <summary>
    /// The storehouse's Needs tab (1.5, part 3; the spec: "what every building is short of, what's on its way, and what
    /// nobody has"), from the couriers' view of the colony (<see cref="CourierSite"/>) and who carries what.
    /// </summary>
    public static class NeedsList
    {
        /// <param name="sites">The colony's buildings, in the order their lines are shown.</param>
        /// <param name="claimed">Each courier's task (<see cref="CourierClaims.All"/>).</param>
        /// <param name="foodValue">How filling an item is (0: not food): food of any kind is counted the plainest first.</param>
        public static NeedsReport Of(IReadOnlyList<CourierSite> sites, IEnumerable<KeyValuePair<string, CourierTask>> claimed, Func<string, float> foodValue)
        {
            var report = new NeedsReport();
            if (sites == null) return report;
            List<CourierSite> all = sites.Where(s => s != null && !string.IsNullOrEmpty(s.Id)).ToList();
            // Each building's spare, used up as needs count it: what couriers may fetch, and what lies where they don't take.
            var pools = new Dictionary<string, Dictionary<string, int>>();   // not ToDictionary: two with one id (its review) mustn't throw
            foreach (CourierSite s in all) pools[s.Id] = new Dictionary<string, int>(s.Spare ?? new Dictionary<string, int>());
            List<KeyValuePair<string, CourierTask>> carrying = (claimed ?? Enumerable.Empty<KeyValuePair<string, CourierTask>>())
                .Where(c => c.Value != null && c.Value.Kind == CourierTaskKind.Deliver).ToList();
            var bring = new Dictionary<string, int>();
            foreach (CourierSite site in all)
                foreach (KeepEntry need in site.Short ?? new List<KeepEntry>())
                {
                    if (need == null || need.Amount <= 0 || string.IsNullOrEmpty(need.Item)) continue;
                    var line = new NeedLine { Site = site.Id, Item = need.Item, Amount = need.Amount };
                    report.Lines.Add(line);
                    foreach (KeyValuePair<string, CourierTask> c in carrying.Where(c => c.Value.To == site.Id && (c.Value.Need.Length > 0 ? c.Value.Need : c.Value.Item) == need.Item))
                    {
                        if (line.Courier.Length == 0) line.Courier = c.Key;
                        line.Coming += c.Value.Amount;
                    }
                    line.Coming = Math.Min(line.Coming, line.Amount);
                    int rest = line.Amount - line.Coming;
                    if (rest <= 0) continue;
                    if (site.CanTake != null && !Fits(site, need.Item, all, pools, foodValue))
                    {
                        line.NoRoom = true;
                        continue;
                    }
                    // The storehouse's own need comes from its own store; anyone else's from what couriers may take, the
                    // storehouse first (as the plan).
                    IEnumerable<CourierSite> sources = site.IsStorehouse ? (IEnumerable<CourierSite>)new[] { site }
                        : all.Where(s => s != site && (s.IsStorehouse || Fetchable(s))).OrderByDescending(s => s.IsStorehouse);
                    foreach (CourierSite from in sources)
                    {
                        if (rest <= 0) break;
                        int got = TakeFrom(pools[from.Id], need.Item, rest, foodValue);
                        line.Waiting += got;
                        rest -= got;
                    }
                    int most = 0;
                    foreach (CourierSite from in all.Where(s => s != site && !s.IsStorehouse && !Fetchable(s)))
                    {
                        if (rest <= 0) break;
                        int got = TakeFrom(pools[from.Id], need.Item, rest, foodValue);
                        line.Elsewhere += got;
                        rest -= got;
                        if (got > most)
                        {
                            most = got;
                            line.ElsewhereAt = from.Id;
                        }
                    }
                    if (rest > 0) bring[need.Item] = (bring.TryGetValue(need.Item, out int n) ? n : 0) + rest;
                }
            report.Bring = bring.OrderByDescending(b => b.Value).ThenBy(b => b.Key, StringComparer.Ordinal)
                .Select(b => new KeepEntry { Item = b.Key, Amount = b.Value }).ToList();
            return report;
        }

        /// <summary>Couriers take from it: not the Town Hall, a Builder's Hut, or a hut with "Couriers take the rest" off.</summary>
        private static bool Fetchable(CourierSite site) => !site.IsTownHall && !site.IsBuilderHut && site.TakeRest;

        /// <summary>Its chest has room for the item (for food of any kind: for some food the colony has).</summary>
        private static bool Fits(CourierSite site, string item, List<CourierSite> all, Dictionary<string, Dictionary<string, int>> pools, Func<string, float> foodValue)
        {
            if (item != StockList.AnyFood) return site.CanTake(item);
            if (foodValue == null) return true;
            List<string> foods = pools.Values.SelectMany(p => p.Where(i => i.Value > 0 && foodValue(i.Key) > 0f).Select(i => i.Key)).Distinct().ToList();
            return foods.Count == 0 || foods.Any(site.CanTake);
        }

        /// <summary>Takes up to <paramref name="want"/> of the item (food of any kind: the plainest first) off a pool; how many.</summary>
        private static int TakeFrom(Dictionary<string, int> pool, string item, int want, Func<string, float> foodValue)
        {
            if (item != StockList.AnyFood)
            {
                if (!pool.TryGetValue(item, out int have) || have <= 0) return 0;
                int got = Math.Min(have, want);
                pool[item] = have - got;
                return got;
            }
            if (foodValue == null) return 0;
            int taken = 0;
            foreach (string food in pool.Where(i => i.Value > 0 && foodValue(i.Key) > 0f).OrderBy(i => foodValue(i.Key)).ThenBy(i => i.Key, StringComparer.Ordinal).Select(i => i.Key).ToList())
            {
                if (taken >= want) break;
                int got = Math.Min(pool[food], want - taken);
                pool[food] -= got;
                taken += got;
            }
            return taken;
        }
    }
}
