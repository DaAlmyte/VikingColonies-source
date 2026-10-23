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

        // 1.4.2: the builder's "Needs 12 Wood" says where more is kept (a rule only the guide told).
        [Fact]
        public void Where_more_is_kept_names_the_building_keeping_the_most_of_it()
        {
            var chests = new List<KeptChest>
            {
                new KeptChest("$vc_hut_farm", held: 10, spare: 10),
                new KeptChest("$vc_hut_lumberjack", held: 50, spare: 0),
                new KeptChest("$vc_town_hall", held: 30, spare: 25),
            };
            Assert.Equal("$vc_hut_lumberjack", StockRules.KeptIn(chests));
        }

        [Fact]
        public void Nothing_kept_anywhere_names_nobody()
        {
            Assert.Null(StockRules.KeptIn(new List<KeptChest> { new KeptChest("$vc_hut_farm", held: 10, spare: 10) }));
            Assert.Null(StockRules.KeptIn(new List<KeptChest>()));
            Assert.Null(StockRules.KeptIn(null));
        }

        // 1.4.3, the player's watch of 1.4.2: settlers ate the carrots in a Farm's chest, its seed for seed carrots (in the
        // game a carrot is planted to grow carrot seeds). A Farm keeps the seeds its fields plant: one per plot, a mixed
        // field's plots shared among its crops.
        private static readonly CropInfo CarrotCrop = new CropInfo("sapling_carrot", "CarrotSeeds", "Pickable_Carrot", "Carrot", "$item_carrot", 0, 0.5f, isFood: true);
        private static readonly CropInfo SeedCarrotCrop = new CropInfo("sapling_seedcarrot", "Carrot", "Pickable_SeedCarrot", "CarrotSeeds", "$item_carrotseeds", 0, 0.5f);

        [Fact]
        public void A_farms_seed_for_its_fields_is_its_working_stock()
        {
            // A 5x5 field of seed carrots: 24 plots (the marker stands in the middle), each planted with a carrot.
            Dictionary<string, int> working = WorkingStock.OfFarm(new[] { new FieldSeeds(24, new[] { SeedCarrotCrop }) });
            Assert.Equal(24, working["Carrot"]);
            // Mixed crops: the plots shared among the ticked crops, rounded up.
            working = WorkingStock.OfFarm(new[] { new FieldSeeds(24, new[] { CarrotCrop, SeedCarrotCrop }), new FieldSeeds(8, new[] { CarrotCrop }) });
            Assert.Equal(12, working["Carrot"]);
            Assert.Equal(12 + 8, working["CarrotSeeds"]);
            Assert.Empty(WorkingStock.OfFarm(new FieldSeeds[0]));   // no fields, nothing kept
        }

        [Fact]
        public void What_a_hut_works_with_isnt_spare_for_meals_or_anyone()
        {
            var contents = new Dictionary<string, int> { { "Carrot", 30 }, { "Raspberry", 5 } };
            var working = new Dictionary<string, int> { { "Carrot", 24 } };
            Assert.Equal(6, StockRules.Spare(contents, null, "Carrot", IsFood, false, working));
            Assert.Equal(5, StockRules.Spare(contents, null, "Raspberry", IsFood, false, working));
            Assert.Equal(0, StockRules.Spare(new Dictionary<string, int> { { "Carrot", 20 } }, null, "Carrot", IsFood, false, working));
            Assert.Equal(30, StockRules.Spare(contents, null, "Carrot", IsFood, true, working));   // the farmer's own use: all of it
            // With Keep in stock too, the larger of the two holds.
            Assert.Equal(0, StockRules.Spare(contents, Keep(("Carrot", 30)), "Carrot", IsFood, false, working));
            Assert.Equal(6, StockRules.Spare(contents, Keep(("Carrot", 10)), "Carrot", IsFood, false, working));
        }

        // 1.4.3's review: a bush (PlantEverything's raspberry bush) is planted once and regrows: a field of them keeps no
        // seed back (it kept 5 Raspberries a plot for good).
        [Fact]
        public void A_bush_field_keeps_no_seed_back()
        {
            var bush = new CropInfo("RaspberryBush", "Raspberry", "RaspberryBush", "Raspberry", "$item_raspberries", 0, 0.5f, isFood: true, seedCount: 5);
            Assert.True(bush.IsBush);
            Assert.Empty(WorkingStock.OfFarm(new[] { new FieldSeeds(24, new[] { bush }) }));
            Assert.Equal(12, WorkingStock.OfFarm(new[] { new FieldSeeds(24, new[] { bush, SeedCarrotCrop }) })["Carrot"]);
        }

        // 1.4.3's upgrade test: the builder fetched "0 of 1 Lantern" for ever. A Dvergr lantern is built from a Lantern,
        // which doesn't stack, and since 1.4.1 settlers took only what stacks: no hut whose level 4 needs one got there. The
        // builder takes anything a piece is built from; carrying runs still take only what stacks.
        [Fact]
        public void The_builder_takes_what_a_piece_is_built_from_even_if_it_doesnt_stack()
        {
            Assert.True(StockRules.MayTake(maxStack: 1, toBuildWith: true));
            Assert.False(StockRules.MayTake(maxStack: 1, toBuildWith: false));
            Assert.True(StockRules.MayTake(maxStack: 50, toBuildWith: false));
        }

        [Fact]
        public void A_builder_who_takes_nothing_waits_before_planning_again()
        {
            Assert.True(StockRules.FetchPause(taken: 0) > 0f);
            Assert.Equal(0f, StockRules.FetchPause(taken: 1));
        }
    }
}
