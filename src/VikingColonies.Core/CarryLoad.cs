using System;
using System.Collections.Generic;
using System.Linq;

namespace VikingColonies.Core
{
    /// <summary>Items a settler is carrying, counted by item prefab name (e.g. "Wood").</summary>
    public sealed class CarryLoad
    {
        private readonly SortedDictionary<string, int> _items = new SortedDictionary<string, int>(StringComparer.Ordinal);

        /// <summary>The most of one item a load holds (no settler carries near it): a count never wraps round.</summary>
        public const int MaxCount = 1000000;

        public int Total => (int)Math.Min(int.MaxValue, _items.Values.Sum(n => (long)n));

        /// <summary>A copy of the item names, so the load can be changed while looping over it.</summary>
        public IEnumerable<string> ItemNames => _items.Keys.ToList();

        public int Count(string item) => item != null && _items.TryGetValue(item, out int n) ? n : 0;

        public void Add(string item, int amount)
        {
            if (string.IsNullOrEmpty(item)) throw new ArgumentException("Item name is required.", nameof(item));
            if (amount <= 0) return;
            _items[item] = (int)Math.Min(MaxCount, (long)Count(item) + amount);
        }

        /// <returns>How many were actually removed (never more than carried).</returns>
        public int Remove(string item, int amount)
        {
            int have = Count(item);
            int removed = Math.Min(have, Math.Max(0, amount));
            if (removed == 0) return 0;
            if (have == removed) _items.Remove(item);
            else _items[item] = have - removed;
            return removed;
        }

        public void Clear() => _items.Clear();

        /// <summary>What's left of the load beyond <paramref name="keep"/> (seeds he holds for replanting), as a new load.</summary>
        public CarryLoad Beyond(IReadOnlyDictionary<string, int> keep)
        {
            var rest = new CarryLoad();
            foreach (KeyValuePair<string, int> item in _items)
                rest.Add(item.Key, item.Value - (keep != null && keep.TryGetValue(item.Key, out int kept) ? kept : 0));
            return rest;
        }

        /// <summary>Two loads added up, as a new load (his carry and what he holds for a free-time errand, saved as one).</summary>
        public static CarryLoad Together(CarryLoad a, CarryLoad b)
        {
            var both = new CarryLoad();
            foreach (CarryLoad load in new[] { a, b })
                if (load != null)
                    foreach (KeyValuePair<string, int> item in load._items) both.Add(item.Key, item.Value);
            return both;
        }

        /// <summary>"BeechSeeds:1,Wood:12" — sorted, safe to store in a network object string.</summary>
        public string Serialize() => string.Join(",", _items.Select(kv => kv.Key + ":" + kv.Value));

        /// <summary>Reads Serialize() output. Malformed parts are skipped; never throws.</summary>
        public static CarryLoad Deserialize(string text)
        {
            var load = new CarryLoad();
            if (string.IsNullOrEmpty(text)) return load;
            foreach (string part in text.Split(','))
            {
                int colon = part.LastIndexOf(':');
                if (colon <= 0) continue;
                if (int.TryParse(part.Substring(colon + 1), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int amount))
                    load.Add(part.Substring(0, colon), amount);
            }
            return load;
        }
    }
}
