using System.Collections.Generic;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    public class CarryLoadTests
    {
        [Fact]
        public void Add_accumulates_per_item_and_total()
        {
            var load = new CarryLoad();
            load.Add("Wood", 3);
            load.Add("Wood", 2);
            load.Add("BeechSeeds", 1);
            Assert.Equal(5, load.Count("Wood"));
            Assert.Equal(1, load.Count("BeechSeeds"));
            Assert.Equal(6, load.Total);
        }

        [Fact]
        public void Add_ignores_zero_and_negative_amounts()
        {
            var load = new CarryLoad();
            load.Add("Wood", 0);
            load.Add("Wood", -4);
            Assert.Equal(0, load.Total);
            Assert.Empty(load.ItemNames);
        }

        [Fact]
        public void Remove_returns_amount_removed_and_drops_empty_entries()
        {
            var load = new CarryLoad();
            load.Add("Wood", 5);
            Assert.Equal(3, load.Remove("Wood", 3));
            Assert.Equal(2, load.Count("Wood"));
        }

        // Review focus 1: a partial deposit must leave exactly the rest behind.
        [Fact]
        public void Remove_more_than_carried_removes_only_what_is_there()
        {
            var load = new CarryLoad();
            load.Add("Wood", 2);
            load.Add("Resin", 4);
            Assert.Equal(2, load.Remove("Wood", 10));
            Assert.Equal(0, load.Count("Wood"));
            Assert.DoesNotContain("Wood", load.ItemNames);
            Assert.Equal(4, load.Total);
        }

        [Fact]
        public void Remove_unknown_item_removes_nothing()
        {
            Assert.Equal(0, new CarryLoad().Remove("Stone", 2));
        }

        [Fact]
        public void Serialize_round_trips()
        {
            var load = new CarryLoad();
            load.Add("Wood", 12);
            load.Add("BeechSeeds", 1);
            var copy = CarryLoad.Deserialize(load.Serialize());
            Assert.Equal(12, copy.Count("Wood"));
            Assert.Equal(1, copy.Count("BeechSeeds"));
            Assert.Equal(13, copy.Total);
        }

        [Fact]
        public void Serialize_is_sorted_and_stable()
        {
            var load = new CarryLoad();
            load.Add("Wood", 2);
            load.Add("BeechSeeds", 1);
            Assert.Equal("BeechSeeds:1,Wood:2", load.Serialize());
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void Deserialize_empty_gives_empty_load(string? text)
        {
            Assert.Equal(0, CarryLoad.Deserialize(text).Total);
        }

        // Review focus 4: old or damaged saved data must never crash a settler.
        [Fact]
        public void Deserialize_skips_malformed_parts_without_throwing()
        {
            var load = CarryLoad.Deserialize("Wood:3,,junk,:5,Stone:x,Resin:2,Flint:-4");
            Assert.Equal(3, load.Count("Wood"));
            Assert.Equal(2, load.Count("Resin"));
            Assert.Equal(5, load.Total);
        }

        [Fact]
        public void ItemNames_is_a_copy_so_removing_while_looping_is_safe()
        {
            var load = new CarryLoad();
            load.Add("Wood", 1);
            load.Add("Resin", 1);
            foreach (string item in load.ItemNames) load.Remove(item, 1);
            Assert.Equal(0, load.Total);
        }
    
        [Fact]   // review of 0.21 (C1): seeds he holds for replanting are no load to find room for
        public void Beyond_leaves_out_what_he_keeps()
        {
            var load = new CarryLoad();
            load.Add("Wood", 30);
            load.Add("PineCone", 3);
            CarryLoad rest = load.Beyond(new Dictionary<string, int> { ["PineCone"] = 3 });
            Assert.Equal(30, rest.Count("Wood"));
            Assert.Equal(0, rest.Count("PineCone"));
            Assert.Equal(33, load.Total);   // his own load is unchanged
            Assert.Equal(33, load.Beyond(null).Total);
        }

        [Fact]   // review of 0.21 (I1): what he holds for an errand is saved with his carry
        public void Together_adds_two_loads_up()
        {
            var carry = new CarryLoad();
            carry.Add("Wood", 5);
            var inHand = new CarryLoad();
            inHand.Add("Wood", 2);
            inHand.Add("Resin", 1);
            CarryLoad both = CarryLoad.Together(carry, inHand);
            Assert.Equal("Resin:1,Wood:7", both.Serialize());
            Assert.Equal(5, carry.Total);
            Assert.Equal(3, inHand.Total);
        }
}
}
