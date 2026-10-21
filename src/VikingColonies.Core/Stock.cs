using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace VikingColonies.Core
{
    /// <summary>One line of a Keep in stock list: an item (or <see cref="StockList.AnyFood"/>) and how many to keep.</summary>
    public sealed class KeepEntry
    {
        public string Item = "";
        public int Amount;
    }

    /// <summary>
    /// What a building keeps in its chest (worker polish part 2, 0.21; MineColonies' minimum stock): up to 5 items per level
    /// (the Longhouse and the Town Hall 10), each with an amount. Saved on the building as "1|item=amount;item=amount"; a
    /// building with nothing saved keeps nothing, except the Town Hall, which keeps 10 food of any kind for newcomers.
    /// </summary>
    public sealed class StockList
    {
        /// <summary>The entry for any food a settler may eat.</summary>
        public const string AnyFood = "*food";
        public const int MinAmount = 1, MaxAmount = 999, StepBy = 5;
        /// <summary>Shift-click steps Food (any) by this many.</summary>
        public const int AnyFoodStack = 10;
        /// <summary>The Town Hall keeps this much food of any kind until the player sets its list.</summary>
        public const int TownHallFood = 10;
        public const int PerLevel = 5, PerLevelHallOrLonghouse = 10;
        private const string Version = "1|";

        private readonly List<KeepEntry> _entries = new List<KeepEntry>();

        public IReadOnlyList<KeepEntry> Entries => _entries;

        public static int MaxEntries(bool hallOrLonghouse, int level) =>
            (hallOrLonghouse ? PerLevelHallOrLonghouse : PerLevel) * Math.Max(1, Math.Min(HutLevels.MaxLevel, level));

        /// <summary>The list saved on a building; nothing saved: the Town Hall's food for newcomers, or nothing.</summary>
        public static StockList Decode(string saved, bool isTownHall)
        {
            var list = new StockList();
            if (string.IsNullOrEmpty(saved))
            {
                if (isTownHall) list._entries.Add(new KeepEntry { Item = AnyFood, Amount = TownHallFood });
                return list;
            }
            string body = saved.StartsWith(Version, StringComparison.Ordinal) ? saved.Substring(Version.Length) : saved;
            foreach (string part in body.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                string item = part.Substring(0, eq).Trim();
                if (item.Length == 0 || list.Kept(item) > 0) continue;
                if (!int.TryParse(part.Substring(eq + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int amount)) continue;
                list._entries.Add(new KeepEntry { Item = item, Amount = Clamp(amount) });
            }
            return list;
        }

        /// <summary>The list as saved on the building (an emptied list is "1|", so it isn't taken for "never set").</summary>
        public string Encode() =>
            Version + string.Join(";", _entries.Select(e => e.Item + "=" + e.Amount.ToString(CultureInfo.InvariantCulture)).ToArray());

        /// <summary>Adds an entry; false when it is already on the list or the list is full.</summary>
        public bool Add(string item, int amount, int max)
        {
            if (string.IsNullOrEmpty(item) || _entries.Count >= max || _entries.Any(e => e.Item == item)) return false;
            _entries.Add(new KeepEntry { Item = item, Amount = Clamp(amount) });
            return true;
        }

        public void Remove(string item) => _entries.RemoveAll(e => e.Item == item);

        public void SetAmount(string item, int amount)
        {
            KeepEntry entry = _entries.FirstOrDefault(e => e.Item == item);
            if (entry != null) entry.Amount = Clamp(amount);
        }

        /// <summary>How many of this item the building keeps (0 when it isn't on the list).</summary>
        public int Kept(string item)
        {
            KeepEntry entry = _entries.FirstOrDefault(e => e.Item == item);
            return entry != null ? entry.Amount : 0;
        }

        /// <summary>The amount after a − or + click: by 5, or with Shift by a whole stack; never below 1 or above 999.</summary>
        public static int Step(int amount, bool up, bool wholeStack, int stackSize)
        {
            int by = wholeStack ? Math.Max(1, stackSize) : StepBy;
            return Clamp(up ? amount + by : amount - by);
        }

        private static int Clamp(int amount) => Math.Max(MinAmount, Math.Min(MaxAmount, amount));
    }

    /// <summary>A chest that holds an item, and how many of it it can spare (the rest its building keeps).</summary>
    public struct KeptChest
    {
        public KeptChest(string building, int held, int spare)
        {
            Building = building ?? "";
            Held = held;
            Spare = spare;
        }

        /// <summary>The building's name (its translation token).</summary>
        public string Building { get; }
        public int Held { get; }
        public int Spare { get; }
        public int Kept => Math.Max(0, Held - Spare);
    }

    /// <summary>What a chest can spare when its building keeps some of it, and what its building is short of.</summary>
    public static class StockRules
    {
        /// <summary>
        /// Where more of a missing item is kept: the building keeping the most of it, or null when none keeps any. The
        /// builder's "Needs 12 Wood" says so (1.4.2: kept items are left alone, a rule only the guide told).
        /// </summary>
        public static string KeptIn(IEnumerable<KeptChest> chests)
        {
            if (chests == null) return null;
            KeptChest? most = null;
            foreach (KeptChest chest in chests)
                if (chest.Kept > 0 && (most == null || chest.Kept > most.Value.Kept)) most = chest;
            return most?.Building;
        }

        /// <summary>
        /// Whether settlers carry an item from chest to chest: only what stacks. A settler's carry keeps an item's name and
        /// count, so a sword, armour or a tool would be put down as a new one, its quality, wear and maker lost (the whole-mod
        /// review of 1.4.1). Those stay where they are, and Keep in stock doesn't offer them.
        /// </summary>
        public static bool Carried(int maxStack) => maxStack > 1;

        /// <summary>
        /// How many of an item may be taken from a chest. The building's own use (its worker, the Town Hall feeding
        /// newcomers) takes everything; anyone else leaves the kept amount. Named food entries are kept first and Food (any)
        /// on top of them: with 10 Raspberries and 10 food of any kind kept, 15 Raspberries and 8 Mushrooms spare 3 of each.
        /// </summary>
        public static int Spare(IReadOnlyDictionary<string, int> contents, StockList keep, string item, Func<string, bool> isFood, bool ownUse)
        {
            int held = Held(contents, item);
            if (ownUse || keep == null) return held;
            int spare = held - keep.Kept(item);
            int anyFood = keep.Kept(StockList.AnyFood);
            if (anyFood > 0 && isFood != null && isFood(item))
                spare = Math.Min(spare, FoodBeyondNamed(contents, keep, isFood) - anyFood);
            return Math.Max(0, spare);
        }

        /// <summary>How many of an entry the chest holds as the rules count it: Food (any) is the food beyond the named food entries.</summary>
        public static int Held(IReadOnlyDictionary<string, int> contents, StockList keep, string item, Func<string, bool> isFood) =>
            item == StockList.AnyFood ? FoodBeyondNamed(contents, keep ?? new StockList(), isFood) : Held(contents, item);

        /// <summary>What the building is short of: each entry with how many are missing (none when it holds enough).</summary>
        public static List<KeepEntry> Short(IReadOnlyDictionary<string, int> contents, StockList keep, Func<string, bool> isFood)
        {
            var missing = new List<KeepEntry>();
            if (keep == null) return missing;
            foreach (KeepEntry entry in keep.Entries)
            {
                int have = entry.Item == StockList.AnyFood ? FoodBeyondNamed(contents, keep, isFood) : Held(contents, entry.Item);
                if (have < entry.Amount) missing.Add(new KeepEntry { Item = entry.Item, Amount = entry.Amount - have });
            }
            return missing;
        }

        /// <summary>The food in the chest beyond what its named food entries keep.</summary>
        private static int FoodBeyondNamed(IReadOnlyDictionary<string, int> contents, StockList keep, Func<string, bool> isFood)
        {
            if (contents == null || isFood == null) return 0;
            int food = 0, named = 0;
            foreach (KeyValuePair<string, int> item in contents)
            {
                if (!isFood(item.Key)) continue;
                food += item.Value;
                named += Math.Min(item.Value, keep.Kept(item.Key));
            }
            return food - named;
        }

        private static int Held(IReadOnlyDictionary<string, int> contents, string item) =>
            contents != null && item != null && contents.TryGetValue(item, out int n) ? n : 0;
    }

    /// <summary>A building short of a kept item: how many it misses (<see cref="StockList.AnyFood"/> for food of any kind).</summary>
    public struct StockNeed
    {
        public string BuildingId;
        public GroundPoint At;
        public string Item;
        public int Missing;
    }

    /// <summary>A chest with some of an item to spare (beyond its own building's kept amount).</summary>
    public struct StockSource
    {
        public string BuildingId;
        public GroundPoint At;
        public string Item;
        public int Spare;
    }

    /// <summary>A carry run: take <see cref="Amount"/> of <see cref="Item"/> from one building's chest to another's.</summary>
    public sealed class StockRun
    {
        public string To = "", From = "", Item = "";
        /// <summary>The entry it fills: the item, or <see cref="StockList.AnyFood"/>.</summary>
        public string Need = "";
        public int Amount;
    }

    /// <summary>
    /// Which carry run a settler in free time makes (worker polish part 2, 0.21; MineColonies' couriers, done in free time
    /// as the fires are fed): the nearest building short of a kept item that nobody else is carrying for, filled from the
    /// chest with spare that makes the shortest walk; never from the building itself; at most 20 at a time.
    /// </summary>
    public static class StockRuns
    {
        public const int MaxCarry = 20;

        /// <summary>One settler per building and item: the key he claims.</summary>
        public static string ClaimKey(string buildingId, string item) => "stock:" + buildingId + ":" + item;

        /// <param name="sources">For food of any kind, in the order to take it (the plainest first).</param>
        /// <param name="fits">Whether a building's chest can take one of an item (review of 0.21: a full one got nothing, again and again).</param>
        public static StockRun Choose(IReadOnlyList<StockNeed> needs, IReadOnlyList<StockSource> sources, GroundPoint me,
            Func<string, bool> claimed, Func<string, bool> isFood, Func<string, string, bool> fits = null)
        {
            if (needs == null || sources == null) return null;
            foreach (StockNeed need in needs.Where(n => n.Missing > 0).OrderBy(n => Distance(me, n.At)))
            {
                if (claimed != null && claimed(ClaimKey(need.BuildingId, need.Item))) continue;
                bool anyFood = need.Item == StockList.AnyFood;
                StockSource? best = null;
                float bestWalk = float.MaxValue;
                foreach (StockSource source in sources)
                {
                    if (source.Spare <= 0 || source.BuildingId == need.BuildingId) continue;
                    if (anyFood ? isFood == null || !isFood(source.Item) : source.Item != need.Item) continue;
                    if (fits != null && !fits(need.BuildingId, source.Item)) continue;
                    float walk = Distance(me, source.At) + Distance(source.At, need.At);
                    if (walk < bestWalk - 0.01f)
                    {
                        best = source;
                        bestWalk = walk;
                    }
                }
                if (!best.HasValue) continue;
                return new StockRun
                {
                    To = need.BuildingId,
                    From = best.Value.BuildingId,
                    Item = best.Value.Item,
                    Need = need.Item,
                    Amount = Math.Min(MaxCarry, Math.Min(need.Missing, best.Value.Spare)),
                };
            }
            return null;
        }

        private static float Distance(GroundPoint a, GroundPoint b) =>
            (float)Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));
    }

    /// <summary>A colony chest a full worker's load might go to.</summary>
    public struct ChestRoom
    {
        public string BuildingId;
        public GroundPoint At;
        public bool IsTownHall;
        public bool HasRoom;
    }

    /// <summary>
    /// A worker whose hut chest is full takes his load to the Town Hall when it has room, else the nearest colony chest
    /// with room (worker polish part 2, 0.21); null when every chest is full ("Storage full").
    /// </summary>
    public static class FullChest
    {
        public static string Choose(IReadOnlyList<ChestRoom> chests, GroundPoint me)
        {
            if (chests == null) return null;
            foreach (ChestRoom chest in chests)
                if (chest.IsTownHall && chest.HasRoom) return chest.BuildingId;
            string nearest = null;
            float best = float.MaxValue;
            foreach (ChestRoom chest in chests)
            {
                if (!chest.HasRoom) continue;
                float d = (float)Math.Sqrt((chest.At.X - me.X) * (chest.At.X - me.X) + (chest.At.Z - me.Z) * (chest.At.Z - me.Z));
                if (d < best)
                {
                    best = d;
                    nearest = chest.BuildingId;
                }
            }
            return nearest;
        }
    }
}
