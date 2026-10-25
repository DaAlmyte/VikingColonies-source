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

    /// <summary>What a storehouse's take-down does with what its chests inside still hold (1.5, part 3).</summary>
    public enum StoreTakeDown
    {
        /// <summary>Empty: taken down at once.</summary>
        Now,
        /// <summary>Its couriers carry it away first, then it is taken down.</summary>
        CouriersEmptyIt,
        /// <summary>No courier, or only gear left (couriers carry only what stacks): the player empties it.</summary>
        EmptyItYourself,
    }

    /// <summary>A storehouse emptied by its couriers for its take-down: what comes next (1.5, part 3's review).</summary>
    public enum ClosingStep
    {
        /// <summary>Goods still inside, or a repair under way: it stays closed.</summary>
        Wait,
        /// <summary>Empty, its building finished: the take-down order is made.</summary>
        TakeDown,
        /// <summary>Taken down already, or another order came: the store opens again.</summary>
        Reopen,
    }

    /// <summary>Take down (0.23): which buildings the player may have taken down.</summary>
    public static class TakeDownRules
    {
        /// <summary>
        /// A closing storehouse, every few seconds (part 3): taken down once it is empty and finished; a repair (a piece fell
        /// while it emptied) is waited for, not taken as the take-down called off (its review: the store reopened silently).
        /// </summary>
        public static ClosingStep WhileClosing(bool storeEmpty, bool orderDone, bool repairing, bool takingDown)
        {
            if (takingDown) return ClosingStep.Reopen;
            if (!orderDone) return repairing ? ClosingStep.Wait : ClosingStep.Reopen;
            return storeEmpty ? ClosingStep.TakeDown : ClosingStep.Wait;
        }

        /// <summary>A storehouse's take-down (part 3): at once when empty; else emptied by its couriers if they can.</summary>
        public static StoreTakeDown ForStore(bool storeEmpty, bool hasCourier, bool onlyGearLeft) =>
            storeEmpty ? StoreTakeDown.Now : hasCourier && !onlyGearLeft ? StoreTakeDown.CouriersEmptyIt : StoreTakeDown.EmptyItYourself;

        /// <summary>
        /// A hut, not the Town Hall, whose building is finished (no open order); a storehouse only once the chests standing
        /// inside it are empty (1.5: only its own chest's things move by themselves).
        /// </summary>
        public static bool MayTakeDown(bool isTownHall, bool hasOrder, bool orderDone, bool storeEmpty = true) =>
            !isTownHall && (!hasOrder || orderDone) && storeEmpty;

        /// <summary>
        /// An old piece an upgrade drops stays standing (counted as taken) when it is a bed a player sleeps in: their spawn
        /// point (the review of 2026-10-19: an old Longhouse's upgrade took a player's bed down without a word). In the way
        /// of a new piece, it is reported as in the way, for the player to move.
        /// </summary>
        public static bool LeaveStanding(bool isBed, long bedOwner) => isBed && bedOwner != 0L;

        /// <summary>
        /// An upgrade's old piece where a new one goes is taken down, unless it stays: a player's bed (their spawn point), or
        /// one already counted taken (left standing so). Then it is in the way, for the player to move, like another
        /// building's piece (the review round of 2026-10-23: "taken down" again and again, the upgrade never finished).
        /// </summary>
        public static bool OldPieceBlocks(bool isPlayersBed, bool countedTaken) => isPlayersBed || countedTaken;
    }
}
