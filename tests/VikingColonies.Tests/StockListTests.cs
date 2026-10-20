using System.Linq;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>Worker polish part 2 (0.21): a building's Keep in stock list, as MineColonies' minimum stock.</summary>
    public class StockListTests
    {
        [Fact]
        public void No_saved_list()
        {
            StockList hall = StockList.Decode(null, isTownHall: true);
            Assert.Single(hall.Entries);
            Assert.Equal(StockList.AnyFood, hall.Entries[0].Item);
            Assert.Equal(10, hall.Entries[0].Amount);
            Assert.Empty(StockList.Decode("", isTownHall: false).Entries);
        }

        [Fact]
        public void Encode_and_decode_round_trip()
        {
            var list = new StockList();
            Assert.True(list.Add("Raspberry", 15, 5));
            Assert.True(list.Add(StockList.AnyFood, 10, 5));
            StockList back = StockList.Decode(list.Encode(), isTownHall: false);
            Assert.Equal(new[] { "Raspberry", StockList.AnyFood }, back.Entries.Select(e => e.Item));
            Assert.Equal(new[] { 15, 10 }, back.Entries.Select(e => e.Amount));
        }

        [Fact]
        public void An_emptied_list_stays_empty()
        {
            Assert.Equal("1|", new StockList().Encode());
            Assert.Empty(StockList.Decode("1|", isTownHall: true).Entries);
        }

        [Fact]
        public void Entries_per_level()
        {
            Assert.Equal(15, StockList.MaxEntries(hallOrLonghouse: false, level: 3));
            Assert.Equal(20, StockList.MaxEntries(hallOrLonghouse: true, level: 2));
            Assert.Equal(10, StockList.MaxEntries(hallOrLonghouse: true, level: 1));
            Assert.Equal(5, StockList.MaxEntries(false, 0));
            Assert.Equal(25, StockList.MaxEntries(false, 9));
        }

        [Fact]
        public void Steps_by_five_or_a_stack()
        {
            Assert.Equal(15, StockList.Step(10, up: true, wholeStack: false, stackSize: 50));
            Assert.Equal(1, StockList.Step(3, up: false, wholeStack: false, stackSize: 50));
            Assert.Equal(60, StockList.Step(10, up: true, wholeStack: true, stackSize: 50));
            Assert.Equal(999, StockList.Step(997, up: true, wholeStack: false, stackSize: 50));
        }

        [Fact]
        public void Add_refuses_a_full_list_or_a_second_entry_for_one_item()
        {
            var list = new StockList();
            Assert.True(list.Add("Wood", 20, max: 2));
            Assert.False(list.Add("Wood", 5, max: 2));
            Assert.True(list.Add("Stone", 5, max: 2));
            Assert.False(list.Add("Resin", 5, max: 2));
            list.Remove("Wood");
            Assert.True(list.Add("Resin", 5, max: 2));
            Assert.Equal(0, list.Kept("Wood"));
            list.SetAmount("Resin", 0);
            Assert.Equal(1, list.Kept("Resin"));
        }

        [Fact]
        public void Saved_amounts_are_kept_within_bounds_and_nonsense_is_skipped()
        {
            StockList list = StockList.Decode("1|Wood=0;Stone=5000;=4;Resin=x;Flint", isTownHall: false);
            Assert.Equal(new[] { "Wood", "Stone" }, list.Entries.Select(e => e.Item));
            Assert.Equal(1, list.Kept("Wood"));
            Assert.Equal(999, list.Kept("Stone"));
        }
    }
}
