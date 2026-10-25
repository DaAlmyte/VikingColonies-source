using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace VikingColonies.Core
{
    /// <summary>
    /// Couriers (1.5, the storehouse's part 2): hired at the storehouse, they move goods for the whole colony
    /// (docs/superpowers/specs/2026-10-21-vikingcolonies-storehouse-design.md).
    /// </summary>
    public static class CourierRules
    {
        /// <summary>A producer's chest this full has its surplus taken away first.</summary>
        public const float FullShare = 0.75f;

        /// <summary>A pickup worth the walk: at least this many spare items.</summary>
        public const int PickupAtLeast = 10;

        /// <summary>How many couriers a storehouse hires: as many as a Watchtower's guards (1, 1, 2, 2, 3).</summary>
        public static int Couriers(int level) => GuardLevels.Guards(level);

        /// <summary>How much a courier carries a trip: <paramref name="carry"/> at level 1 and <paramref name="perLevel"/> more a level.</summary>
        public static int Load(int level, int carry, int perLevel) => carry + perLevel * (HutLevels.Clamp(level) - 1);

        /// <summary>Settlers in free time carry Keep in stock until the colony's storehouse has a courier; then couriers do.</summary>
        public static bool FreeTimeCarries(bool storehouseHasCourier) => !storehouseHasCourier;

        /// <summary>
        /// "Couriers take the rest" (part 3) is on a hut's Keep in stock tab, not on the Town Hall's, a Builder's Hut's or
        /// the storehouse's: couriers never take from the first two, and the storehouse's own chest is the drop-off.
        /// </summary>
        public static bool HasTakeRestSwitch(bool isTownHall, bool isBuilderHut, bool isStorehouse) => !isTownHall && !isBuilderHut && !isStorehouse;
    }

    /// <summary>How a put into one chest went.</summary>
    public enum PutOutcome { Stored, NoRoom, Busy }

    /// <summary>What kind of carrying a task is; the order is the urgency (the spec's "most urgent first").</summary>
    public enum CourierTaskKind { Deliver = 0, UrgentPickup = 1, DropOff = 2, Pickup = 3, Sort = 4, Clear = 5 }

    /// <summary>One thing for a courier to carry: from one building's chests to another's.</summary>
    public sealed class CourierTask
    {
        public CourierTaskKind Kind;
        public string From = "", To = "";
        /// <summary>The item; "" for a pickup (any of the hut's surplus).</summary>
        public string Item = "";
        /// <summary>A delivery's Keep in stock entry it fills: the item, or <see cref="StockList.AnyFood"/>.</summary>
        public string Need = "";
        public int Amount;

        /// <summary>One courier per key (<see cref="CourierClaims"/>).</summary>
        public string Key => ((int)Kind).ToString(CultureInfo.InvariantCulture) + ":" + From + ":" + To + ":" + (Need.Length > 0 ? Need : Item);

        public string Encode() =>
            string.Join("|", new[] { ((int)Kind).ToString(CultureInfo.InvariantCulture), Esc(From), Esc(To), Esc(Item), Esc(Need), Amount.ToString(CultureInfo.InvariantCulture) });

        /// <summary>
        /// A field with '|', a tab or a line break written so it can't split the task or the claims' line (the review round:
        /// a modded item's name with '|' made a claim unreadable, and its task went out twice). Plain names are unchanged.
        /// </summary>
        private static string Esc(string s) =>
            (s ?? "").Replace("\\", "\\\\").Replace("|", "\\p").Replace("\t", "\\t").Replace("\n", "\\n");

        private static string Unesc(string s)
        {
            if (s.IndexOf('\\') < 0) return s;
            var sb = new System.Text.StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c != '\\' || i + 1 >= s.Length)
                {
                    sb.Append(c);
                    continue;
                }
                char next = s[++i];
                sb.Append(next == 'p' ? '|' : next == 't' ? '\t' : next == 'n' ? '\n' : next);
            }
            return sb.ToString();
        }

        /// <summary>Null for anything that isn't a saved task.</summary>
        public static CourierTask Decode(string text)
        {
            string[] f = (text ?? "").Split('|');
            if (f.Length != 6 || !int.TryParse(f[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int kind)
                || kind < 0 || kind > (int)CourierTaskKind.Clear || !int.TryParse(f[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out int amount))
                return null;
            return new CourierTask { Kind = (CourierTaskKind)kind, From = Unesc(f[1]), To = Unesc(f[2]), Item = Unesc(f[3]), Need = Unesc(f[4]), Amount = amount };
        }
    }

    /// <summary>
    /// Who carries what (the spec: the storehouse's owner hands out the tasks, saved on the storehouse, so two computers
    /// never send two couriers to one task). A courier keeps its task until it is done, fails, is held too long, or the
    /// courier is no longer hired.
    /// </summary>
    public sealed class CourierClaims
    {
        /// <summary>A task held longer than this is given to someone else (a courier unloaded or stuck).</summary>
        public const double HoldSeconds = 300;

        private sealed class Held
        {
            public CourierTask Task;
            public double Since;
        }

        private readonly Dictionary<string, Held> _held = new Dictionary<string, Held>();

        public int Count => _held.Count;

        /// <summary>Every courier's task (the Needs tab: what's on its way, and who carries it).</summary>
        public IEnumerable<KeyValuePair<string, CourierTask>> All =>
            _held.OrderBy(h => h.Key, StringComparer.Ordinal).Select(h => new KeyValuePair<string, CourierTask>(h.Key, h.Value.Task));

        public CourierTask For(string courierId) =>
            !string.IsNullOrEmpty(courierId) && _held.TryGetValue(courierId, out Held held) ? held.Task : null;

        /// <summary>When the courier was given its task (network time); 0 when it holds none.</summary>
        public double Since(string courierId) =>
            !string.IsNullOrEmpty(courierId) && _held.TryGetValue(courierId, out Held held) ? held.Since : 0;

        /// <summary>
        /// The courier's own task while it holds one; else the first in the plan nobody holds, now its own; null when none.
        /// <paramref name="lastDone"/> is the task the courier last finished: still held, its Done was lost (the storehouse
        /// changed computers), so it is let go and, the plan being older than it, not given again this once (its review, 1.5).
        /// </summary>
        public CourierTask Assign(IReadOnlyList<CourierTask> plan, string courierId, double now, string lastDone = "")
        {
            if (string.IsNullOrEmpty(courierId)) return null;
            CourierTask own = For(courierId);
            bool stale = own != null && !string.IsNullOrEmpty(lastDone) && own.Key == lastDone;
            if (stale) _held.Remove(courierId);
            else if (own != null) return own;
            if (plan == null) return null;
            var taken = new HashSet<string>(_held.Values.Select(h => h.Task.Key));
            if (stale) taken.Add(lastDone);
            CourierTask next = plan.FirstOrDefault(t => t != null && !taken.Contains(t.Key));
            if (next != null) _held[courierId] = new Held { Task = next, Since = now };
            return next;
        }

        public void Release(string courierId)
        {
            if (!string.IsNullOrEmpty(courierId)) _held.Remove(courierId);
        }

        /// <returns>How many tasks were freed.</returns>
        public int Expire(double now, double seconds = HoldSeconds)
        {
            List<string> old = _held.Where(h => now - h.Value.Since > seconds).Select(h => h.Key).ToList();
            foreach (string courier in old) _held.Remove(courier);
            return old.Count;
        }

        /// <summary>Drops the tasks of couriers no longer hired here.</summary>
        public void Forget(Func<string, bool> stillHired)
        {
            if (stillHired == null) return;
            foreach (string courier in _held.Keys.Where(c => !stillHired(c)).ToList()) _held.Remove(courier);
        }

        /// <summary>One line per courier: its id, when it got the task, the task.</summary>
        public string Encode() => string.Join("\n", _held.OrderBy(h => h.Key, StringComparer.Ordinal)
            .Select(h => h.Key + "\t" + h.Value.Since.ToString("R", CultureInfo.InvariantCulture) + "\t" + h.Value.Task.Encode()));

        /// <summary>Never throws; a line that isn't a claim is skipped.</summary>
        public static CourierClaims Decode(string text)
        {
            var claims = new CourierClaims();
            foreach (string line in (text ?? "").Split('\n'))
            {
                string[] f = line.Split('\t');
                if (f.Length != 3 || f[0].Length == 0) continue;
                if (!RecordCodec.TryParseFinite(f[1], out double since)) continue;
                CourierTask task = CourierTask.Decode(f[2]);
                if (task != null) claims._held[f[0]] = new Held { Task = task, Since = since };
            }
            return claims;
        }
    }

    /// <summary>A colony building as the couriers' plan sees it.</summary>
    public sealed class CourierSite
    {
        public string Id = "";
        public GroundPoint At;
        public bool IsStorehouse, IsTownHall, IsBuilderHut;
        /// <summary>A Lumberjack's Hut, a Forager's Hut or a Farm: its surplus is picked up.</summary>
        public bool IsProducer;
        /// <summary>"Couriers take the rest" (part 3): off, couriers take nothing from it, neither surplus nor for a delivery.</summary>
        public bool TakeRest = true;
        /// <summary>How full its chest is, 0 to 1 (its slots).</summary>
        public float Fill;
        /// <summary>What it is short of: its Keep in stock, and a Builder's Hut's order beyond its chest.</summary>
        public List<KeepEntry> Short = new List<KeepEntry>();
        /// <summary>What may be taken from it: carried items beyond its Keep in stock and its worker's working stock.</summary>
        public Dictionary<string, int> Spare = new Dictionary<string, int>();
        /// <summary>Whether its chest has room for one of an item; null: room for anything (its review, 1.5: no delivery to a full chest).</summary>
        public Func<string, bool> CanTake;
    }

    /// <summary>One chest inside the storehouse, for sorting: what it holds and its room.</summary>
    public sealed class StoreChest
    {
        public Dictionary<string, int> Items = new Dictionary<string, int>();
        /// <summary>How many more of an item it takes; null: as many as asked.</summary>
        public Func<string, int> RoomFor;

        public int Room(string item) => RoomFor != null ? RoomFor(item) : int.MaxValue;
    }

    /// <summary>The storehouse's own facts for the plan.</summary>
    public sealed class StoreFacts
    {
        public string Id = "";
        public int Level = 1;
        /// <summary>Its own chest's carried items beyond its Keep in stock: the player's drop-off.</summary>
        public Dictionary<string, int> DropOff = new Dictionary<string, int>();
        /// <summary>The chests inside: what each holds and its room, for sorting and for the storehouse's own Keep in stock.</summary>
        public List<StoreChest> Chests = new List<StoreChest>();
        /// <summary>A chest inside has room: the drop-off can be put away and surplus picked up (its review: a full store jammed every courier).</summary>
        public bool InsideHasRoom;
        /// <summary>False while the storehouse is being taken down: nothing is put in it (its review, 1.5).</summary>
        public bool Open = true;
        /// <summary>Its take-down is ordered and its couriers empty it first (part 3): <see cref="Clearing"/> goes elsewhere.</summary>
        public bool Closing;
        /// <summary>While closing: the carried items in the chests inside, to be taken away.</summary>
        public Dictionary<string, int> Clearing = new Dictionary<string, int>();
    }

    /// <summary>
    /// The colony's carrying, worked out once for every courier (the spec): deliveries (Keep in stock, the newcomers' food,
    /// the builder's order) from the storehouse first, else the nearest spare; producers three-quarters full; the drop-off;
    /// producers' surplus, the most first; sorting from level 3. Never from the Town Hall or a Builder's Hut.
    /// </summary>
    public static class CourierPlan
    {
        private static readonly Dictionary<string, int> s_none = new Dictionary<string, int>();

        /// <param name="foodValue">How filling an item is (0: not food): food of any kind goes the plainest first.</param>
        public static List<CourierTask> Tasks(IReadOnlyList<CourierSite> sites, StoreFacts store, int load, Func<string, float> foodValue)
        {
            var tasks = new List<CourierTask>();
            if (sites == null || store == null || load <= 0) return tasks;
            List<CourierSite> all = sites.Where(s => s != null && !string.IsNullOrEmpty(s.Id)).ToList();
            CourierSite house = all.FirstOrDefault(s => s.IsStorehouse && s.Id == store.Id);
            GroundPoint home = house != null ? house.At : default(GroundPoint);
            var spare = new Dictionary<string, Dictionary<string, int>>();
            foreach (CourierSite site in all) spare[site.Id] = new Dictionary<string, int>(site.Spare ?? s_none);
            var dropOff = new Dictionary<string, int>(store.DropOff ?? s_none);
            List<StoreChest> chests = (store.Chests ?? new List<StoreChest>()).Where(c => c != null && c.Items != null).ToList();
            var inside = new Dictionary<string, int>();   // the chests inside, for the storehouse's own Keep in stock
            if (store.Open)
                foreach (StoreChest chest in chests)
                    foreach (KeyValuePair<string, int> item in chest.Items.Where(i => i.Value > 0))
                        inside[item.Key] = (inside.TryGetValue(item.Key, out int n) ? n : 0) + item.Value;

            // 1. Deliveries, the needs nearest the storehouse first.
            foreach (CourierSite site in all.OrderBy(s => Distance(home, s.At)))
                foreach (KeepEntry need in site.Short ?? new List<KeepEntry>())
                {
                    if (need == null || need.Amount <= 0) continue;
                    Func<string, bool> fits = site.CanTake;
                    CourierSite from = null;
                    string item = null;
                    if (site == house)
                    {
                        // The storehouse's own Keep in stock: from its chests inside into its own chest.
                        item = Pick(inside, need.Item, foodValue, fits);
                        if (item == null) continue;
                        int own = Math.Min(load, Math.Min(need.Amount, inside[item]));
                        inside[item] -= own;
                        Take(spare[house.Id], item, own);   // kept now, not offered to anyone else too (the review round)
                        tasks.Add(new CourierTask { Kind = CourierTaskKind.Deliver, From = house.Id, To = house.Id, Item = item, Need = need.Item, Amount = own });
                        continue;
                    }
                    if (house != null) item = Pick(spare[house.Id], need.Item, foodValue, fits);
                    if (item != null) from = house;
                    else
                    {
                        float bestWalk = float.MaxValue;
                        foreach (CourierSite other in all)
                        {
                            if (other == site || other.IsTownHall || other.IsBuilderHut || other.IsStorehouse || !other.TakeRest) continue;
                            string it = Pick(spare[other.Id], need.Item, foodValue, fits);
                            if (it == null) continue;
                            float walk = Distance(home, other.At) + Distance(other.At, site.At);
                            if (walk >= bestWalk - 0.01f) continue;
                            from = other;
                            item = it;
                            bestWalk = walk;
                        }
                    }
                    if (from == null) continue;
                    int amount = Math.Min(load, Math.Min(need.Amount, spare[from.Id][item]));
                    spare[from.Id][item] -= amount;
                    if (from == house) Take(dropOff, item, amount);   // the courier takes from the drop-off first
                    tasks.Add(new CourierTask { Kind = CourierTaskKind.Deliver, From = from.Id, To = site.Id, Item = item, Need = need.Item, Amount = amount });
                }

            // Nothing more is put in a store being taken down; surplus waits for room inside (its review, 1.5). One
            // closing for its take-down is emptied (part 3).
            if (!store.Open)
            {
                if (store.Closing) tasks.AddRange(Clear(all, house, home, store, load, foodValue));
                return tasks;
            }

            // 2. Producers three-quarters full, the fullest first.
            List<CourierSite> producers = store.InsideHasRoom
                ? all.Where(s => s.IsProducer && s.TakeRest && !s.IsTownHall && !s.IsBuilderHut && !s.IsStorehouse).ToList()
                : new List<CourierSite>();
            var urgent = new HashSet<string>();
            foreach (CourierSite site in producers.Where(s => s.Fill >= CourierRules.FullShare).OrderByDescending(s => s.Fill))
            {
                int total = Total(spare[site.Id]);
                if (total <= 0) continue;
                urgent.Add(site.Id);
                tasks.Add(new CourierTask { Kind = CourierTaskKind.UrgentPickup, From = site.Id, To = store.Id, Amount = Math.Min(load, total) });
            }

            // 3. The drop-off put away (what no delivery took).
            if (store.InsideHasRoom)
                foreach (KeyValuePair<string, int> item in dropOff.Where(i => i.Value > 0).OrderBy(i => i.Key, StringComparer.Ordinal))
                    tasks.Add(new CourierTask { Kind = CourierTaskKind.DropOff, From = store.Id, To = store.Id, Item = item.Key, Amount = Math.Min(load, item.Value) });

            // 4. Producers' surplus, the most first.
            foreach (CourierSite site in producers.Where(s => !urgent.Contains(s.Id) && Total(spare[s.Id]) >= CourierRules.PickupAtLeast)
                         .OrderByDescending(s => Total(spare[s.Id])))
                tasks.Add(new CourierTask { Kind = CourierTaskKind.Pickup, From = site.Id, To = store.Id, Amount = Math.Min(load, Total(spare[site.Id])) });

            // 5. Sorting, from level 3: a kind in two or more chests inside, its smallest stack to a chest holding more of
            //    it with room for it (its review: into a full chest it went back where it was, for ever).
            if (store.Level >= 3)
                foreach (string item in chests.SelectMany(c => c.Items.Where(i => i.Value > 0).Select(i => i.Key)).Distinct().OrderBy(i => i, StringComparer.Ordinal))
                {
                    StoreChest least = SortFrom(chests, item);
                    if (least == null || SortInto(chests, item, least) == null) continue;
                    tasks.Add(new CourierTask { Kind = CourierTaskKind.Sort, From = store.Id, To = store.Id, Item = item, Amount = Math.Min(load, least.Items[item]) });
                }
            return tasks;
        }

        /// <summary>
        /// A closing store's chests inside emptied (part 3): each kind to a building short of it, else the Town Hall, else
        /// the nearest; only where its chest has room, and never filling a Builder's Hut or a hut with "Couriers take the
        /// rest" off with what it isn't short of (its review: goods nobody would take out again).
        /// </summary>
        private static IEnumerable<CourierTask> Clear(List<CourierSite> all, CourierSite house, GroundPoint home, StoreFacts store, int load, Func<string, float> foodValue)
        {
            foreach (KeyValuePair<string, int> item in (store.Clearing ?? s_none).Where(i => i.Value > 0).OrderBy(i => i.Key, StringComparer.Ordinal))
            {
                bool food = foodValue != null && foodValue(item.Key) > 0f;
                bool ShortOf(CourierSite s) =>
                    (s.Short ?? new List<KeepEntry>()).Any(n => n != null && n.Amount > 0 && (n.Item == item.Key || (food && n.Item == StockList.AnyFood)));
                CourierSite to = all
                    .Where(s => s != house && !s.IsStorehouse && (s.CanTake == null || s.CanTake(item.Key)) && (ShortOf(s) || (!s.IsBuilderHut && s.TakeRest)))
                    .OrderByDescending(ShortOf)
                    .ThenByDescending(s => s.IsTownHall)
                    .ThenBy(s => Distance(home, s.At))
                    .FirstOrDefault();
                if (to != null)
                    yield return new CourierTask { Kind = CourierTaskKind.Clear, From = store.Id, To = to.Id, Item = item.Key, Amount = Math.Min(load, item.Value) };
            }
        }

        /// <summary>
        /// Where a courier puts an item away in the store (the player's pick, 1.5: like with like at every level, so sorting
        /// is left to tidy up): the chests already holding it, the most first, then the others in their order (nearest
        /// first); only those with room for it. Indices into <paramref name="chests"/>.
        /// </summary>
        public static List<int> PutAwayOrder(IReadOnlyList<StoreChest> chests, string item)
        {
            var order = new List<int>();
            if (chests == null || string.IsNullOrEmpty(item)) return order;
            int Held(int i) => chests[i].Items != null && chests[i].Items.TryGetValue(item, out int n) ? n : 0;
            List<int> withRoom = Enumerable.Range(0, chests.Count).Where(i => chests[i] != null && chests[i].Room(item) > 0).ToList();
            order.AddRange(withRoom.Where(i => Held(i) > 0).OrderByDescending(Held).ThenBy(i => i));
            order.AddRange(withRoom.Where(i => Held(i) <= 0));
            return order;
        }

        /// <summary>
        /// The next chest a courier walks to with its load, like with like (the player's pick, 1.5; its review): the kinds that
        /// already have a chest with room first, so a new kind doesn't take their last room; that kind's first choice
        /// (<see cref="PutAwayOrder"/>) among the chests not <paramref name="done"/>. It walks to each chest it puts into, one
        /// after another (the review round of 2026-10-23: it reached every chest from the store's own chest, through walls).
        /// </summary>
        /// <returns>The chest's index, or -1 when nothing it carries has room anywhere left.</returns>
        public static int NextPutAway(IReadOnlyList<StoreChest> chests, IEnumerable<string> kinds, Func<string, int> carried,
            ICollection<int> done, out string kind)
        {
            IReadOnlyList<StoreChest> open = Open(chests, done);
            foreach (string item in PutAwayKinds(open, kinds, carried))
            {
                List<int> order = PutAwayOrder(open, item);
                if (order.Count == 0) continue;
                kind = item;
                return order[0];
            }
            kind = null;
            return -1;
        }

        /// <summary>
        /// At chest <paramref name="at"/>: every kind whose first choice it is goes in (two kinds of one chest on one visit),
        /// none whose chest is another.
        /// </summary>
        /// <param name="look">The chests now.</param>
        /// <param name="put">Puts that kind into chest i.</param>
        /// <param name="carried">How many of a kind are still in hand.</param>
        /// <param name="waitForLike">
        /// A busy chest holding the kind is waited for, not passed by (a player has it open: at levels 1-2 nothing would sort
        /// the split back). Putting back, with no task to wait in, passes it by; a busy chest new to the kind is passed by.
        /// </param>
        /// <returns>True when it waits at this chest (busy): try again soon; false when done here.</returns>
        public static bool PutAwayAt(int at, IEnumerable<string> kinds, Func<IReadOnlyList<StoreChest>> look, Func<int, string, PutOutcome> put,
            Func<string, int> carried, ICollection<int> done, bool waitForLike)
        {
            List<string> order = PutAwayKinds(Open(look(), done), kinds, carried);
            foreach (string item in order)
            {
                if (carried(item) <= 0) continue;
                IReadOnlyList<StoreChest> chests = look();
                List<int> choice = PutAwayOrder(Open(chests, done), item);
                if (choice.Count == 0 || choice[0] != at) continue;
                if (put(at, item) != PutOutcome.Busy) continue;
                StoreChest busy = chests[at];
                return waitForLike && busy != null && busy.Items != null && busy.Items.TryGetValue(item, out int held) && held > 0;
            }
            return false;
        }

        /// <summary>The kinds it carries, those with a chest of their own (with room) first, then by name.</summary>
        private static List<string> PutAwayKinds(IReadOnlyList<StoreChest> chests, IEnumerable<string> kinds, Func<string, int> carried)
        {
            bool HasChest(string item) => chests.Any(c => c != null && c.Items != null && c.Items.TryGetValue(item, out int n) && n > 0 && c.Room(item) > 0);
            return kinds.Where(k => carried(k) > 0).Distinct().OrderByDescending(HasChest).ThenBy(k => k, StringComparer.Ordinal).ToList();
        }

        /// <summary>The chests, with those done left out (null), their indexes kept.</summary>
        private static IReadOnlyList<StoreChest> Open(IReadOnlyList<StoreChest> chests, ICollection<int> done) =>
            done == null || done.Count == 0 ? chests : chests.Select((c, i) => done.Contains(i) ? null : c).ToList();

        /// <summary>Sorting's chest to take from: the one with the least of the item, among two or more holding it; null otherwise.</summary>
        public static StoreChest SortFrom(IReadOnlyList<StoreChest> chests, string item)
        {
            List<StoreChest> holding = chests.Where(c => c != null && c.Items != null && c.Items.TryGetValue(item, out int n) && n > 0).ToList();
            return holding.Count < 2 ? null : holding.OrderBy(c => c.Items[item]).First();
        }

        /// <summary>Sorting's chest to put into: the one holding the most of the item, other than <paramref name="from"/>, with room for all of it; null when none.</summary>
        public static StoreChest SortInto(IReadOnlyList<StoreChest> chests, string item, StoreChest from) =>
            chests.Where(c => c != null && c != from && c.Items != null && c.Items.TryGetValue(item, out int n) && n > 0 && c.Room(item) >= from.Items[item])
                .OrderByDescending(c => c.Items[item]).FirstOrDefault();

        /// <summary>
        /// The item a source gives for a need: the item itself, or for food of any kind the plainest it has; only one the
        /// building has room for (<paramref name="fits"/>, null: any).
        /// </summary>
        private static string Pick(Dictionary<string, int> spare, string need, Func<string, float> foodValue, Func<string, bool> fits = null)
        {
            if (spare == null || string.IsNullOrEmpty(need)) return null;
            if (need != StockList.AnyFood) return spare.TryGetValue(need, out int n) && n > 0 && (fits == null || fits(need)) ? need : null;
            if (foodValue == null) return null;
            return spare.Where(i => i.Value > 0 && foodValue(i.Key) > 0f && (fits == null || fits(i.Key)))
                .OrderBy(i => foodValue(i.Key)).ThenBy(i => i.Key, StringComparer.Ordinal).Select(i => i.Key).FirstOrDefault();
        }

        private static int Total(Dictionary<string, int> spare) => spare.Values.Where(n => n > 0).Sum();

        private static void Take(Dictionary<string, int> from, string item, int amount)
        {
            if (from.TryGetValue(item, out int n)) from[item] = Math.Max(0, n - amount);
        }

        private static float Distance(GroundPoint a, GroundPoint b) =>
            (float)Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));
    }
}
