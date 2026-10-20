using System;
using System.Collections.Generic;

namespace VikingColonies.Core
{
    /// <summary>
    /// Hand over (worker polish part 4, 0.23; MineColonies' arrow in its Required Resources tab): what goes from the
    /// player's bag into a chest for the builder — what his order still needs beyond what the colony's chests hold for
    /// builders, as much as the bag has.
    /// </summary>
    public static class HandOver
    {
        public static Dictionary<string, int> Amounts(IReadOnlyDictionary<string, int> needs, IReadOnlyDictionary<string, int> inStores,
            IReadOnlyDictionary<string, int> inBag) => Amounts(needs, inStores, inBag, null, null);

        /// <param name="builderCarries">What the order's builder already carries (0.26: it counts as there).</param>
        /// <param name="keepDeficit">
        /// What the chest it goes into keeps beyond what it holds (0.26): handed over first, so the builder can still take
        /// what's short; only when something is short.
        /// </param>
        public static Dictionary<string, int> Amounts(IReadOnlyDictionary<string, int> needs, IReadOnlyDictionary<string, int> inStores,
            IReadOnlyDictionary<string, int> inBag, IReadOnlyDictionary<string, int> builderCarries, IReadOnlyDictionary<string, int> keepDeficit)
        {
            var moved = new Dictionary<string, int>();
            if (needs == null || inBag == null) return moved;
            foreach (KeyValuePair<string, int> need in needs)
            {
                int stored = Of(inStores, need.Key) + Of(builderCarries, need.Key);
                int shortfall = need.Value - stored;
                if (shortfall <= 0) continue;
                int amount = Math.Min(Of(inBag, need.Key), shortfall + Math.Max(0, Of(keepDeficit, need.Key)));
                if (amount > 0) moved[need.Key] = amount;
            }
            return moved;
        }

        /// <summary>
        /// What's there for the order: the colony's chests (as builders may take from them) and what its builder already
        /// carries (review of 0.26: Hand over, its "nothing needed" and the Town Hall's "Still to bring" count alike).
        /// </summary>
        public static Dictionary<string, int> Available(IReadOnlyDictionary<string, int> inStores, IReadOnlyDictionary<string, int> builderCarries)
        {
            var all = new Dictionary<string, int>();
            foreach (IReadOnlyDictionary<string, int> source in new[] { inStores, builderCarries })
                if (source != null)
                    foreach (KeyValuePair<string, int> item in source)
                        all[item.Key] = (all.TryGetValue(item.Key, out int n) ? n : 0) + item.Value;
            return all;
        }

        private static int Of(IReadOnlyDictionary<string, int> amounts, string item) =>
            amounts != null && amounts.TryGetValue(item, out int n) ? n : 0;
    }

    /// <summary>Take down (0.23): which buildings the player may have taken down.</summary>
    public static class TakeDownRules
    {
        /// <summary>A hut, not the Town Hall, whose building is finished (no open order).</summary>
        public static bool MayTakeDown(bool isTownHall, bool hasOrder, bool orderDone) => !isTownHall && (!hasOrder || orderDone);

        /// <summary>
        /// An old piece an upgrade drops stays standing (counted as taken) when it is a bed a player sleeps in: their spawn
        /// point (the review of 2026-10-19: an old Longhouse's upgrade took a player's bed down without a word). In the way
        /// of a new piece, it is reported as in the way, for the player to move.
        /// </summary>
        public static bool LeaveStanding(bool isBed, long bedOwner) => isBed && bedOwner != 0L;
    }
}
