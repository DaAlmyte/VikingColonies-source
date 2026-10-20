using System.Collections.Generic;
using System.Linq;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>Worker polish part 2 (0.21): what a chest can spare when its building keeps some of it.</summary>
    public class StockRulesTests
    {
        private static readonly HashSet<string> Food = new HashSet<string> { "Raspberry", "Mushroom", "Blueberries" };
        private static bool IsFood(string item) => Food.Contains(item);

        private static StockList Keep(params (string, int)[] entries)
        {
            var list = new StockList();
            foreach (var e in entries) list.Add(e.Item1, e.Item2, 50);
            return list;
        }

        [Fact]
        public void Nothing_is_spare_below_the_kept_amount()
        {
            StockList keep = Keep(("Wood", 30));
            Assert.Equal(0, StockRules.Spare(new Dictionary<string, int> { ["Wood"] = 20 }, keep, "Wood", IsFood, ownUse: false));
            Assert.Equal(20, StockRules.Spare(new Dictionary<string, int> { ["Wood"] = 20 }, keep, "Wood", IsFood, ownUse: true));
            Assert.Equal(10, StockRules.Spare(new Dictionary<string, int> { ["Wood"] = 40 }, keep, "Wood", IsFood, ownUse: false));
        }

        [Fact]
        public void Any_food_and_a_named_food_both_hold()
        {
            StockList keep = Keep(("Raspberry", 10), (StockList.AnyFood, 10));
            var held = new Dictionary<string, int> { ["Raspberry"] = 15, ["Mushroom"] = 8 };
            Assert.Equal(3, StockRules.Spare(held, keep, "Raspberry", IsFood, ownUse: false));
            Assert.Equal(3, StockRules.Spare(held, keep, "Mushroom", IsFood, ownUse: false));
        }

        [Fact]
        public void An_item_not_kept_is_all_spare_and_a_building_with_no_list_keeps_nothing()
        {
            var held = new Dictionary<string, int> { ["Stone"] = 12, ["Raspberry"] = 4 };
            Assert.Equal(12, StockRules.Spare(held, Keep(("Wood", 30)), "Stone", IsFood, ownUse: false));
            Assert.Equal(4, StockRules.Spare(held, null, "Raspberry", IsFood, ownUse: false));
            Assert.Equal(0, StockRules.Spare(held, Keep(("Wood", 30)), "Resin", IsFood, ownUse: false));
        }

        [Fact]
        public void Short_lists_what_is_missing()
        {
            StockList keep = Keep(("Wood", 30), (StockList.AnyFood, 10), ("Resin", 5));
            var held = new Dictionary<string, int> { ["Wood"] = 20, ["Raspberry"] = 4, ["Resin"] = 9 };
            List<KeepEntry> shortOf = StockRules.Short(held, keep, IsFood);
            Assert.Equal(new[] { "Wood", StockList.AnyFood }, shortOf.Select(e => e.Item));
            Assert.Equal(new[] { 10, 6 }, shortOf.Select(e => e.Amount));
        }
    
        [Fact]   // review of 0.21 (M2): the tab shows what the rules count, so it never says "15 / 10" while settlers still bring food
        public void Held_counts_what_the_rules_count()
        {
            var keep = StockList.Decode("1|Raspberry=10;*food=10", false);
            var contents = new Dictionary<string, int> { ["Raspberry"] = 15 };
            Assert.Equal(5, StockRules.Held(contents, keep, StockList.AnyFood, IsFood));
            Assert.Equal(15, StockRules.Held(contents, keep, "Raspberry", IsFood));
        }

        [Fact]
        public void Only_items_that_stack_are_carried_from_chest_to_chest()
        {
            // The whole-mod review of 1.4.1: a carried sword was put down as a new one, its quality, wear and maker lost.
            Assert.True(StockRules.Carried(maxStack: 50));    // wood, bolts, food, seeds
            Assert.True(StockRules.Carried(maxStack: 2));
            Assert.False(StockRules.Carried(maxStack: 1));    // a sword, armour, a tool
            Assert.False(StockRules.Carried(maxStack: 0));    // an item the game doesn't know
        }
    }
}
