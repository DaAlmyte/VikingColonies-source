using System.Collections.Generic;

namespace VikingColonies.Core
{
    /// <summary>A place items can be put, such as the hall's chest.</summary>
    public interface IItemStore
    {
        bool Accepts(string item);
        bool CanAdd(string item, int amount);
        /// <summary>May add fewer than asked (Valheim adds at most one stack per call); callers measure with Count.</summary>
        void Add(string item, int amount);
        int Count(string item);
    }

    public static class ItemTransfer
    {
        /// <summary>Moves a settler's load into a store, keeping <paramref name="keep"/> amounts back.</summary>
        /// <returns>How many items should have moved but didn't fit (0 means everything was stored).</returns>
        public static int Deposit(CarryLoad load, IItemStore store, IReadOnlyDictionary<string, int> keep, ICollection<string> discarded)
        {
            int leftover = 0;
            foreach (string item in load.ItemNames)
            {
                if (!store.Accepts(item))
                {
                    discarded?.Add(item);
                    load.Remove(item, load.Count(item));
                    continue;
                }
                int keepCount = keep != null && keep.TryGetValue(item, out int k) ? k : 0;
                int toMove = load.Count(item) - keepCount;
                int chunk = toMove;
                // Never trust "added" — measure what actually arrived. Valheim adds at most one stack per call,
                // and adds nothing when the whole amount doesn't fit; then fall back to one at a time.
                while (toMove > 0 && store.CanAdd(item, 1))
                {
                    int before = store.Count(item);
                    store.Add(item, chunk < toMove ? chunk : toMove);
                    int added = store.Count(item) - before;
                    if (added > toMove) added = toMove;
                    if (added <= 0)
                    {
                        if (chunk == 1) break;
                        chunk = 1;
                        continue;
                    }
                    load.Remove(item, added);
                    toMove -= added;
                }
                if (toMove > 0) leftover += toMove;
            }
            return leftover;
        }
    }
}
