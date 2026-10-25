using System;
using System.Collections.Generic;
using System.Linq;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>
    /// A courier's load put away in the storehouse, like with like (the player's pick, 1.5), and its review's cases; one chest
    /// at a time, walking to each (the review round of 2026-10-23: it reached every chest from the store's own chest).
    /// </summary>
    public class PutAwayTests
    {
        private const int Stack = 50;

        /// <summary>A chest of slots, a stack of 50 each, as Valheim's.</summary>
        private sealed class Chest
        {
            public readonly Dictionary<string, int> Items = new Dictionary<string, int>();
            public int Slots;
            public bool Busy;

            private int SlotsUsed => Items.Values.Sum(n => (n + Stack - 1) / Stack);

            public int RoomFor(string item)
            {
                int held = Items.TryGetValue(item, out int n) ? n : 0;
                int partial = held % Stack == 0 ? 0 : Stack - held % Stack;
                return Math.Max(0, Slots - SlotsUsed) * Stack + partial;
            }

            public StoreChest Facts() => new StoreChest { Items = new Dictionary<string, int>(Items), RoomFor = RoomFor };
        }

        private sealed class Courier
        {
            public readonly Dictionary<string, int> Load = new Dictionary<string, int>();
            public int Carried(string item) => Load.TryGetValue(item, out int n) ? n : 0;
        }

        /// <summary>
        /// The courier's walk, as CourierJob takes it: to the next chest, puts there what goes there, then on to the next; true
        /// when it waits at a busy chest. <paramref name="visits"/>: the chests it walked to, in order.
        /// </summary>
        private static bool Put(List<Chest> chests, Courier courier, bool waitForLike, List<int>? visits = null)
        {
            var done = new HashSet<int>();
            for (int walk = 0; walk < 100; walk++)
            {
                int at = CourierPlan.NextPutAway(chests.Select(c => c.Facts()).ToList(), courier.Load.Keys.ToList(), courier.Carried, done, out string? kind);
                if (at < 0) return false;
                Assert.NotNull(kind);
                visits?.Add(at);
                bool wait = CourierPlan.PutAwayAt(at, courier.Load.Keys.ToList(), () => chests.Select(c => c.Facts()).ToList(),
                    (i, item) =>
                    {
                        Assert.Equal(at, i);   // only into the chest it stands at
                        Chest chest = chests[i];
                        if (chest.Busy) return PutOutcome.Busy;
                        int moved = Math.Min(courier.Carried(item), chest.RoomFor(item));
                        if (moved <= 0) return PutOutcome.NoRoom;
                        chest.Items[item] = (chest.Items.TryGetValue(item, out int n) ? n : 0) + moved;
                        courier.Load[item] -= moved;
                        return courier.Load[item] == 0 ? PutOutcome.Stored : PutOutcome.NoRoom;
                    },
                    courier.Carried, done, waitForLike);
                if (wait) return true;
                done.Add(at);
            }
            throw new InvalidOperationException("the walk never ended");
        }

        [Fact]   // its review: a player has the Wood chest open; the Wood went into another chest, split for good at levels 1-2
        public void A_busy_chest_holding_the_kind_is_waited_for()
        {
            var chests = new List<Chest> { new Chest { Slots = 10 }, new Chest { Slots = 10, Busy = true } };
            chests[1].Items["Wood"] = 20;
            var courier = new Courier();
            courier.Load["Wood"] = 30;
            Assert.True(Put(chests, courier, waitForLike: true));
            Assert.False(chests[0].Items.ContainsKey("Wood"));
            Assert.Equal(30, courier.Carried("Wood"));
            Assert.False(Put(chests, courier, waitForLike: false));   // putting back has no wait: anywhere with room
            Assert.Equal(30, chests[0].Items["Wood"]);
        }

        [Fact]   // its review: a new kind took the last room of a kind's own chest, which then spilled into a second
        public void Kinds_that_have_a_chest_go_in_first()
        {
            var chests = new List<Chest> { new Chest { Slots = 2 }, new Chest { Slots = 10 } };
            chests[0].Items["Wood"] = 40;   // one slot with room for 10 more, one free slot
            var courier = new Courier();
            courier.Load["Feathers"] = 10;
            courier.Load["Wood"] = 60;
            Assert.False(Put(chests, courier, waitForLike: true));
            Assert.Equal(100, chests[0].Items["Wood"]);
            Assert.False(chests[1].Items.ContainsKey("Wood"));
            Assert.Equal(10, chests[1].Items["Feathers"]);
        }

        [Fact]
        public void A_kind_spills_on_into_the_next_chest_when_its_own_fills()
        {
            var chests = new List<Chest> { new Chest { Slots = 1 }, new Chest { Slots = 10 } };
            chests[0].Items["Stone"] = 30;
            var courier = new Courier();
            courier.Load["Stone"] = 40;
            Assert.False(Put(chests, courier, waitForLike: true));
            Assert.Equal(50, chests[0].Items["Stone"]);
            Assert.Equal(20, chests[1].Items["Stone"]);
            Assert.Equal(0, courier.Carried("Stone"));
        }
    
        [Fact]   // the review round: each chest the load goes into is walked to, once, the kinds with a chest of their own first
        public void It_walks_to_each_chest_once_like_with_like()
        {
            var chests = new List<Chest> { new Chest { Slots = 10 }, new Chest { Slots = 10 }, new Chest { Slots = 10 } };
            chests[1].Items["Stone"] = 10;
            chests[2].Items["Wood"] = 10;
            var courier = new Courier();
            courier.Load["Resin"] = 5;
            courier.Load["Stone"] = 5;
            courier.Load["Wood"] = 5;
            var visits = new List<int>();
            Assert.False(Put(chests, courier, waitForLike: true, visits));
            Assert.Equal(new[] { 1, 2, 0 }, visits);
            Assert.Equal(15, chests[1].Items["Stone"]);
            Assert.Equal(15, chests[2].Items["Wood"]);
            Assert.Equal(5, chests[0].Items["Resin"]);
        }

        [Fact]
        public void Two_kinds_of_one_chest_go_in_on_one_visit()
        {
            var chests = new List<Chest> { new Chest { Slots = 10 }, new Chest { Slots = 10 } };
            chests[1].Items["Stone"] = 10;
            chests[1].Items["Wood"] = 10;
            var courier = new Courier();
            courier.Load["Stone"] = 5;
            courier.Load["Wood"] = 5;
            var visits = new List<int>();
            Put(chests, courier, waitForLike: true, visits);
            Assert.Equal(new[] { 1 }, visits);
            Assert.Equal(0, courier.Carried("Stone") + courier.Carried("Wood"));
        }

        [Fact]   // a busy chest that doesn't hold the kind (the nearest with room) is passed by, not waited for
        public void A_busy_empty_chest_is_passed_by()
        {
            var chests = new List<Chest> { new Chest { Slots = 10, Busy = true }, new Chest { Slots = 10 } };
            var courier = new Courier();
            courier.Load["Resin"] = 5;
            var visits = new List<int>();
            Assert.False(Put(chests, courier, waitForLike: true, visits));
            Assert.Equal(new[] { 0, 1 }, visits);
            Assert.Equal(5, chests[1].Items["Resin"]);
        }

        [Fact]
        public void Nothing_to_put_or_no_room_is_no_walk()
        {
            var full = new List<Chest> { new Chest { Slots = 1 } };
            full[0].Items["Stone"] = 50;
            var courier = new Courier();
            courier.Load["Wood"] = 5;
            Assert.Equal(-1, CourierPlan.NextPutAway(full.Select(c => c.Facts()).ToList(), courier.Load.Keys.ToList(), courier.Carried, new HashSet<int>(), out string? kind));
            Assert.Null(kind);
            courier.Load["Wood"] = 0;
            Assert.Equal(-1, CourierPlan.NextPutAway(new List<StoreChest> { new Chest { Slots = 10 }.Facts() }, courier.Load.Keys.ToList(), courier.Carried, new HashSet<int>(), out _));
        }
    }
}
