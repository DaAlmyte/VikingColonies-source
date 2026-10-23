using System.Collections.Generic;
using System.Linq;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    // The farmer (0.12): his field, what he grows, and his day.
    public class FarmerTests
    {
        private const int Meadows = 1, BlackForest = 8, Plains = 16;

        private static readonly CropInfo Carrot = new CropInfo("sapling_carrot", "CarrotSeeds", "Pickable_Carrot", "Carrot", "$item_carrot", BlackForest | Meadows | Plains, 0.5f, isFood: true);
        private static readonly CropInfo SeedCarrot = new CropInfo("sapling_seedcarrot", "Carrot", "Pickable_SeedCarrot", "CarrotSeeds", "$item_carrotseeds", BlackForest | Meadows | Plains, 0.5f);
        private static readonly CropInfo Barley = new CropInfo("sapling_barley", "Barley", "Pickable_Barley", "Barley", "$item_barley", Plains, 0.5f);

        // --- his day ---

        private static FarmerSituation Day() => new FarmerSituation { HasHut = true, HasField = true, CarryCapacity = 20, HasCultivator = true };

        // 0.20.1: a Farm works only its fields, MineColonies' way; without one its farmer says so.
        [Fact]
        public void Without_a_field_he_says_so_after_taking_his_load_home()
        {
            FarmerSituation s = Day();
            s.HasField = false;
            Assert.Equal(FarmerAction.WaitNoField, FarmerBrain.Decide(s));
            s.CarryTotal = 4;
            Assert.Equal(FarmerAction.Deposit, FarmerBrain.Decide(s));
            s.CarryTotal = 0;
            s.IsNight = true;
            Assert.Equal(FarmerAction.Rest, FarmerBrain.Decide(s));
        }

        [Fact]
        public void Ripe_crops_come_before_new_work()
        {
            FarmerSituation s = Day();
            s.RipeCrop = true;
            s.UncultivatedPlot = true;
            s.PlantablePlot = true;
            Assert.Equal(FarmerAction.Harvest, FarmerBrain.Decide(s));
        }

        [Fact]
        public void He_gathers_what_dropped_before_anything_else_in_the_field()
        {
            FarmerSituation s = Day();
            s.DropsNearby = true;
            s.RipeCrop = true;
            Assert.Equal(FarmerAction.Gather, FarmerBrain.Decide(s));
        }

        [Fact]
        public void He_cultivates_then_plants()
        {
            FarmerSituation s = Day();
            s.UncultivatedPlot = true;
            s.PlantablePlot = true;
            s.SeedToPlant = true;
            Assert.Equal(FarmerAction.Plant, FarmerBrain.Decide(s));   // planting what's ready to plant comes first
            s.PlantablePlot = false;
            Assert.Equal(FarmerAction.Cultivate, FarmerBrain.Decide(s));
        }

        [Fact]
        public void Without_a_cultivator_he_can_still_plant_cultivated_plots_and_harvest()
        {
            FarmerSituation s = Day();
            s.HasCultivator = false;
            s.UncultivatedPlot = true;
            Assert.Equal(FarmerAction.WaitNeedsCultivator, FarmerBrain.Decide(s));
            s.PlantablePlot = true;
            s.SeedToPlant = true;
            Assert.Equal(FarmerAction.Plant, FarmerBrain.Decide(s));
        }

        [Fact]
        public void With_nothing_to_plant_he_says_what_is_missing_or_waits_for_the_crops()
        {
            FarmerSituation s = Day();
            s.PlantablePlot = true;   // an empty cultivated plot, but no seed of a ticked kind
            Assert.Equal(FarmerAction.WaitNeedsSeeds, FarmerBrain.Decide(s));
            Assert.Equal(FarmerAction.WaitGrowing, FarmerBrain.Decide(Day()));
        }

        [Fact]
        public void A_full_load_or_the_night_sends_him_home_first()
        {
            FarmerSituation s = Day();
            s.RipeCrop = true;
            s.CarryTotal = 20;
            Assert.Equal(FarmerAction.Deposit, FarmerBrain.Decide(s));
            s.CarryTotal = 3;
            s.IsNight = true;
            Assert.Equal(FarmerAction.Deposit, FarmerBrain.Decide(s));
            s.CarryTotal = 0;
            Assert.Equal(FarmerAction.Rest, FarmerBrain.Decide(s));
            Assert.Equal(FarmerAction.Unemployed, FarmerBrain.Decide(new FarmerSituation()));
        }

        [Fact]
        public void He_carries_his_last_harvest_home_when_there_is_nothing_more_to_do()
        {
            FarmerSituation s = Day();
            s.CarryTotal = 4;
            Assert.Equal(FarmerAction.Deposit, FarmerBrain.Decide(s));
        }

        // --- the field ---

        [Fact]
        public void A_crop_is_his_when_it_stands_on_one_of_his_plots()
        {
            var plots = new List<Vec3> { new Vec3(5, 0, 0), new Vec3(6.25f, 0, 0) };
            Assert.True(FieldPlots.OnPlot(new Vec3(5.2f, 3, 0.1f), plots));
            Assert.False(FieldPlots.OnPlot(new Vec3(5.7f, 0, 0), plots));   // between two plots
            Assert.False(FieldPlots.OnPlot(new Vec3(20, 0, 0), plots));
        }

        // --- what to grow ---

        [Fact]
        public void Each_crop_that_grows_here_is_offered_and_ticked_unless_unticked()
        {
            List<CropLine> lines = CropChoice.Lines(new[] { Carrot, SeedCarrot, Barley }, hutBiome: Meadows, unticked: TickList.Parse("sapling_seedcarrot"));
            Assert.True(lines.Single(l => l.Crop == Carrot).Ticked);
            Assert.False(lines.Single(l => l.Crop == SeedCarrot).Ticked);
            Assert.True(lines.Single(l => l.Crop == SeedCarrot).Offered);
            CropLine barley = lines.Single(l => l.Crop == Barley);
            Assert.False(barley.Offered);   // barley grows only in the plains
            Assert.False(barley.Ticked);
        }

        [Fact]
        public void He_plants_the_ticked_crops_in_turn_each_only_while_its_seed_is_in_the_chest()
        {
            var ticked = new List<CropInfo> { Carrot, SeedCarrot };
            int turn = 0;
            var stock = new Dictionary<string, int> { { "CarrotSeeds", 5 }, { "Carrot", 2 } };
            bool Has(string item) => stock.TryGetValue(item, out int n) && n > 0;
            Assert.Same(Carrot, CropChoice.Next(ticked, Has, ref turn));
            Assert.Same(SeedCarrot, CropChoice.Next(ticked, Has, ref turn));
            Assert.Same(Carrot, CropChoice.Next(ticked, Has, ref turn));
            stock["CarrotSeeds"] = 0;
            Assert.Same(SeedCarrot, CropChoice.Next(ticked, Has, ref turn));   // no carrot seeds: a seed crop (from a carrot)
            stock["Carrot"] = 0;
            Assert.Null(CropChoice.Next(ticked, Has, ref turn));
        }

        [Fact]
        public void A_seed_crop_is_planted_only_while_its_seed_runs_low()
        {
            // Review of 0.12: with every crop ticked, half of all plantings were seed crops eating up the colony's carrots.
            var crops = new List<CropInfo> { Carrot, SeedCarrot, Barley };
            Assert.True(CropChoice.WorthPlanting(Carrot, crops, item => 0));                        // a food crop: always
            Assert.True(CropChoice.WorthPlanting(SeedCarrot, crops, item => 2));                     // few carrot seeds: grow more
            Assert.False(CropChoice.WorthPlanting(SeedCarrot, crops, item => CropChoice.SeedReserve));   // enough: keep the carrots
            Assert.True(CropChoice.WorthPlanting(Barley, crops, item => 50));                         // its own seed, not another's
        }

        [Fact]
        public void A_food_crop_is_planted_however_much_of_it_is_stored()
        {
            // Self-test of 0.12: a carrot is also the carrot-seed crop's seed, and with 6 carrots stored the farmer would
            // have stopped planting carrots at all.
            var crops = new List<CropInfo> { Carrot, SeedCarrot };
            Assert.True(CropChoice.WorthPlanting(Carrot, crops, item => 50));
            Assert.False(CropChoice.WorthPlanting(SeedCarrot, crops, item => 50));
        }

        [Fact]
        public void A_crop_knows_whether_it_gives_food()
        {
            var plants = new[]
            {
                new PlantInfo("sapling_carrot", "CarrotSeeds", "$item_carrot", "Pickable_Carrot", "Carrot", isPickable: true, biomes: 1, growRadius: 0.5f, isFood: true),
                new PlantInfo("sapling_seedcarrot", "Carrot", "$item_carrotseeds", "Pickable_SeedCarrot", "CarrotSeeds", isPickable: true, biomes: 1, growRadius: 0.5f),
            };
            Assert.Equal(new[] { true, false }, CropCatalog.Crops(plants).Select(c => c.IsFood).ToArray());
        }

        [Fact]
        public void Vines_are_not_on_the_list()
        {
            var plants = new[]
            {
                new PlantInfo("VineAsh_sapling", "Vineberry", "$item_vineberry", "VineAsh", "Vineberry", isPickable: true, biomes: 32, growRadius: 0.5f, isVine: true),
                new PlantInfo("sapling_onion", "OnionSeeds", "$item_onion", "Pickable_Onion", "Onion", isPickable: true, biomes: 1, growRadius: 0.5f),
            };
            Assert.Equal(new[] { "sapling_onion" }, CropCatalog.Crops(plants).Select(c => c.Piece).ToArray());
        }

        [Fact]
        public void The_crop_list_comes_from_the_plants_that_grow_into_something_to_pick()
        {
            var plants = new[]
            {
                new PlantInfo("sapling_carrot", "CarrotSeeds", "$piece_sapling_carrot", "Pickable_Carrot", "Carrot", isPickable: true, biomes: Meadows | BlackForest, growRadius: 0.5f),
                new PlantInfo("Beech_Sapling", "BeechSeeds", "$piece_sapling_beech", "Beech1", "", isPickable: false, biomes: Meadows, growRadius: 2f),
                new PlantInfo("sapling_barley", "Barley", "$piece_sapling_barley", "Pickable_Barley", "Barley", isPickable: true, biomes: Plains, growRadius: 0.5f),
            };
            List<CropInfo> crops = CropCatalog.Crops(plants).ToList();
            Assert.Equal(new[] { "sapling_carrot", "sapling_barley" }, crops.Select(c => c.Piece).ToArray());
            Assert.Equal("CarrotSeeds", crops[0].Seed);
            Assert.Equal("Carrot", crops[0].Yield);
        }
    }
    /// <summary>Watching after 0.21 and 0.22: a plot where the crop can't be planted waits longer each time it fails.</summary>
    public class PlotRetryTests
    {
        [Fact]
        public void A_plot_that_fails_again_waits_longer()
        {
            Assert.Equal(600f, PlotRetry.Seconds(1));
            Assert.Equal(1800f, PlotRetry.Seconds(2));
            Assert.Equal(3600f, PlotRetry.Seconds(3));
            Assert.Equal(3600f, PlotRetry.Seconds(9));
            Assert.Equal(600f, PlotRetry.Seconds(0));
        }

        // 1.4.3, the player's watch of 1.4.2: the farmer said "Needs a field" with a field of 24 plots joined to its Farm. A
        // Farm with fields but no free ground says so, and the log says where its plots went.
        [Fact]
        public void A_farm_with_fields_but_no_free_ground_says_so_not_needs_a_field()
        {
            Assert.Equal("$vc_status_needs $vc_a_field", FieldWords.NoFieldStatus(fields: 0));
            Assert.Equal("$vc_status_field_no_ground", FieldWords.NoFieldStatus(fields: 1));
            var tally = new PlotTally { Fields = 1, Plots = 24, Building = 20, Water = 1, Slope = 1, Blocked = 2 };
            Assert.Equal(0, tally.Usable);
            Assert.Equal("1 field, 24 plots, none free: 20 by a building, 1 in water, 1 off the marker's height, 2 with something on them",
                FieldWords.Tally(tally));
        }

        // 1.4.3, the player's watch of 1.4.2: a field in the Farm's own fenced yard had all 24 plots left out (the yard is
        // part of the Farm's site), and the farmer said "Needs a field". A finished Farm's own yard is farmland; inside its
        // walls is not; while it's built or upgraded its whole site stays out; other buildings' sites stay out.
        [Fact]
        public void A_finished_farms_own_yard_is_farmland_inside_its_walls_and_other_sites_are_not()
        {
            Assert.False(FieldPlots.LeftOut(onSiteMargin: true, insideWalls: false, ownFarm: true, siteBusy: false));   // its yard
            Assert.True(FieldPlots.LeftOut(onSiteMargin: true, insideWalls: true, ownFarm: true, siteBusy: false));     // its floor
            Assert.True(FieldPlots.LeftOut(onSiteMargin: true, insideWalls: false, ownFarm: true, siteBusy: true));     // being upgraded
            Assert.True(FieldPlots.LeftOut(onSiteMargin: true, insideWalls: false, ownFarm: false, siteBusy: false));   // the Longhouse's site
            Assert.False(FieldPlots.LeftOut(onSiteMargin: false, insideWalls: false, ownFarm: false, siteBusy: false));
        }

        // 1.4.3's review: a repair (a raid broke a fence piece) took the yard field away until it was done. Only building or
        // upgrading the Farm keeps its yard out.
        [Fact]
        public void Only_building_or_upgrading_the_farm_keeps_its_yard_out_not_a_repair()
        {
            Assert.True(FieldPlots.SiteBusy(OrderKind.Build, done: false));
            Assert.True(FieldPlots.SiteBusy(OrderKind.Upgrade, done: false));
            Assert.False(FieldPlots.SiteBusy(OrderKind.Repair, done: false));
            Assert.False(FieldPlots.SiteBusy(OrderKind.Upgrade, done: true));
        }

        // 1.4.3's review: Hugin's "put a Field marker" tip showed to a Farm that had a field, only no free ground for it.
        [Fact]
        public void Hugins_field_tip_is_for_a_farm_with_no_field()
        {
            Assert.True(FieldWords.ShowsFieldTip(fields: 0));
            Assert.False(FieldWords.ShowsFieldTip(fields: 1));
        }
    }
}
