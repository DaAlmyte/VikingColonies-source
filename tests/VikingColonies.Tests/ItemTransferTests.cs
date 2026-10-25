using System;
using System.Collections.Generic;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    public class ItemTransferTests
    {
        /// <summary>Behaves like Valheim's chest: all-or-nothing per call, at most one stack per call.</summary>
        private sealed class FakeStore : IItemStore
        {
            private readonly Dictionary<string, int> _items = new Dictionary<string, int>();
            public int Room = 1000;
            public int StackSize = 50;

            public bool Accepts(string item) => item != "Mystery";
            public bool CanAdd(string item, int amount) => amount <= Room;
            public int Count(string item) => _items.TryGetValue(item, out int n) ? n : 0;

            public void Add(string item, int amount)
            {
                if (amount > Room) return;               // doesn't fit at all: nothing added
                int n = Math.Min(amount, StackSize);     // one stack per call
                _items[item] = Count(item) + n;
                Room -= n;
            }
        }

        private static CarryLoad Load(params (string item, int amount)[] items)
        {
            var load = new CarryLoad();
            foreach (var (item, amount) in items) load.Add(item, amount);
            return load;
        }

        // Review C1: more than one stack must all arrive, not silently shrink to one stack.
        [Fact]
        public void More_than_one_stack_is_stored_completely()
        {
            var store = new FakeStore();
            var load = Load(("Wood", 56));
            int leftover = ItemTransfer.Deposit(load, store, null, null);
            Assert.Equal(56, store.Count("Wood"));
            Assert.Equal(0, load.Total);
            Assert.Equal(0, leftover);
        }

        [Fact]   // a courier putting away like with like (1.5): one kind into a chest, the rest still carried
        public void Only_the_named_item_is_stored()
        {
            var store = new FakeStore();
            var load = Load(("Wood", 20), ("Stone", 30));
            int leftover = ItemTransfer.Deposit(load, store, null, null, only: "Stone");
            Assert.Equal(30, store.Count("Stone"));
            Assert.Equal(0, store.Count("Wood"));
            Assert.Equal(20, load.Count("Wood"));
            Assert.Equal(0, load.Count("Stone"));
            Assert.Equal(0, leftover);
        }

        [Fact]
        public void Nearly_full_storage_takes_what_fits_and_the_rest_stays_carried()
        {
            var store = new FakeStore { Room = 30 };
            var load = Load(("Wood", 56));
            int leftover = ItemTransfer.Deposit(load, store, null, null);
            Assert.Equal(30, store.Count("Wood"));
            Assert.Equal(26, load.Count("Wood"));
            Assert.Equal(26, leftover);
        }

        [Fact]
        public void Kept_items_stay_carried_and_do_not_count_as_leftover()
        {
            var store = new FakeStore();
            var load = Load(("Wood", 10), ("BeechSeeds", 2));
            int leftover = ItemTransfer.Deposit(load, store, new Dictionary<string, int> { { "BeechSeeds", 1 } }, null);
            Assert.Equal(10, store.Count("Wood"));
            Assert.Equal(1, store.Count("BeechSeeds"));
            Assert.Equal(1, load.Count("BeechSeeds"));
            Assert.Equal(0, leftover);
        }

        [Fact]
        public void Keeping_more_than_carried_moves_nothing_of_that_item()
        {
            var store = new FakeStore();
            var load = Load(("BeechSeeds", 1));
            int leftover = ItemTransfer.Deposit(load, store, new Dictionary<string, int> { { "BeechSeeds", 3 } }, null);
            Assert.Equal(0, store.Count("BeechSeeds"));
            Assert.Equal(1, load.Count("BeechSeeds"));
            Assert.Equal(0, leftover);
        }

        [Fact]
        public void Unknown_items_are_discarded_and_reported()
        {
            var store = new FakeStore();
            var load = Load(("Mystery", 3), ("Wood", 1));
            var discarded = new List<string>();
            int leftover = ItemTransfer.Deposit(load, store, null, discarded);
            Assert.Equal(new[] { "Mystery" }, discarded);
            Assert.Equal(0, load.Total);
            Assert.Equal(1, store.Count("Wood"));
            Assert.Equal(0, leftover);
        }
    }
}
