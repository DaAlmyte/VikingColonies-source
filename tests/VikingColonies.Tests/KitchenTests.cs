using System.Collections.Generic;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>The cook's rules (1.6, part 1): docs/superpowers/specs/2026-10-24-vikingcolonies-cook-and-mead-hall-design.md.</summary>
    public class KitchenTests
    {
        private static RawFood Raw(string item, int spare, string cooked, float value) =>
            new RawFood { Item = item, Spare = spare, Cooked = cooked, CookedValue = value };

        // A wooden cooking station takes raw meat and deer meat, not serpent meat (that's the iron one's).
        private static readonly HashSet<string> s_woodenStation = new HashSet<string> { "RawMeat", "DeerMeat", "NeckTail", "FishRaw" };

        private static readonly string[] s_none = new string[0];

        [Fact]
        public void The_colony_wants_a_hot_meal_each_and_two_more() => Assert.Equal(14, CookRules.HotTarget(12));

        [Fact]
        public void The_cook_roasts_until_the_hall_holds_that_many()
        {
            Assert.True(CookRules.ShouldRoast(13, 12));
            Assert.False(CookRules.ShouldRoast(14, 12));
            Assert.False(CookRules.ShouldRoast(20, 12));
        }

        [Fact]
        public void The_cook_roasts_the_plainest_raw_food_the_station_takes()
        {
            var spare = new[] { Raw("DeerMeat", 4, "CookedDeerMeat", 2f), Raw("RawMeat", 6, "CookedMeat", 1f), Raw("SerpentMeat", 3, "SerpentMeatCooked", 0.5f) };
            Assert.Equal("RawMeat", CookRules.NextRaw(spare, s_woodenStation, s_none).Item);
        }

        [Fact]   // Review Focus 5: raw serpent on a wooden cooking station is never fetched
        public void Raw_food_the_station_cannot_take_is_never_chosen()
        {
            var spare = new[] { Raw("SerpentMeat", 3, "SerpentMeatCooked", 5f) };
            Assert.Null(CookRules.NextRaw(spare, s_woodenStation, s_none));
        }

        [Fact]
        public void The_cook_varies_what_is_roasted()
        {
            var spare = new[] { Raw("RawMeat", 6, "CookedMeat", 1f), Raw("DeerMeat", 4, "CookedDeerMeat", 2f) };
            Assert.Equal("DeerMeat", CookRules.NextRaw(spare, s_woodenStation, new[] { "CookedMeat" }).Item);
        }

        [Fact]
        public void Only_the_last_three_roasted_count_as_lately()
        {
            var spare = new[] { Raw("RawMeat", 6, "CookedMeat", 1f), Raw("DeerMeat", 4, "CookedDeerMeat", 2f) };
            var lately = new[] { "CookedMeat", "CookedFish", "CookedFish", "CookedFish" };
            Assert.Equal("RawMeat", CookRules.NextRaw(spare, s_woodenStation, lately).Item);
        }

        [Fact]
        public void With_one_kind_spare_the_cook_roasts_it_again()
        {
            var spare = new[] { Raw("RawMeat", 6, "CookedMeat", 1f) };
            Assert.Equal("RawMeat", CookRules.NextRaw(spare, s_woodenStation, new[] { "CookedMeat", "CookedMeat", "CookedMeat" }).Item);
        }

        [Fact]
        public void Every_kind_roasted_lately_the_plainest_again()
        {
            var spare = new[] { Raw("DeerMeat", 4, "CookedDeerMeat", 2f), Raw("RawMeat", 6, "CookedMeat", 1f) };
            Assert.Equal("RawMeat", CookRules.NextRaw(spare, s_woodenStation, new[] { "CookedMeat", "CookedDeerMeat" }).Item);
        }

        [Fact]
        public void Equally_plain_raw_food_goes_by_name()
        {
            var spare = new[] { Raw("RawMeat", 6, "CookedMeat", 1f), Raw("FishRaw", 6, "FishCooked", 1f) };
            Assert.Equal("FishRaw", CookRules.NextRaw(spare, s_woodenStation, s_none).Item);
        }

        [Fact]
        public void Nothing_spare_nothing_to_roast()
        {
            var spare = new[] { Raw("RawMeat", 0, "CookedMeat", 1f), Raw("DeerMeat", 0, "CookedDeerMeat", 2f) };
            Assert.Null(CookRules.NextRaw(spare, s_woodenStation, s_none));
            Assert.Null(CookRules.NextRaw(new RawFood[0], s_woodenStation, s_none));
        }

        [Fact]
        public void A_hall_station_with_a_cook_never_burns()
        {
            Assert.True(CookRules.HoldsDone(true, true));
            Assert.False(CookRules.HoldsDone(true, false));
            Assert.False(CookRules.HoldsDone(false, true));
        }

        [Fact]   // Review Focus 3: the hall's pieces load a moment after its station; the station's own word holds meanwhile
        public void A_station_loaded_before_its_hall_holds_by_what_it_last_knew()
        {
            Assert.True(CookRules.HoldsWithoutHall(lastKnown: true, secondsSinceLoaded: 2f));
            Assert.False(CookRules.HoldsWithoutHall(lastKnown: false, secondsSinceLoaded: 2f));
            Assert.False(CookRules.HoldsWithoutHall(lastKnown: true, secondsSinceLoaded: CookRules.HallLoadSeconds + 1f));   // the hall's gone
            Assert.Equal("vc_cook_holds", SaveKeys.CookHolds);
        }

        [Fact]
        public void A_trip_is_the_smallest_of_the_load_the_free_slots_and_whats_spare()
        {
            Assert.Equal(2, CookRules.Load(20, 2, 7));
            Assert.Equal(7, CookRules.Load(20, 9, 7));
            Assert.Equal(3, CookRules.Load(3, 9, 7));
            Assert.Equal(0, CookRules.Load(20, 0, 7));
        }

        [Fact]
        public void A_second_Mead_Hall_in_a_colony_is_refused()
        {
            Assert.Equal(RedReason.OnlyOneMeadHall, MeadHallRules.PlaceRed(otherMeadHallsInColony: 1));
            Assert.Equal(RedReason.None, MeadHallRules.PlaceRed(0));
        }

        [Fact]
        public void The_Mead_Hall_and_the_cook_have_their_save_names()
        {
            Assert.Equal("meadhall", SaveKeys.MeadHallType);
            Assert.Equal("vc_hut_meadhall", SaveKeys.MeadHallPiece);
            Assert.Equal("cook", SaveKeys.CookJob);
        }

        [Fact]   // the game's Cooking skill as the cook's work skill (the spec)
        public void Cooks_learn_Cooking_and_keep_it_through_saving()
        {
            Assert.Equal(WorkSkill.Cooking, WorkSkills.ForJob(SaveKeys.CookJob));
            Assert.Equal("$vc_skill_cooking", WorkSkills.Token(WorkSkill.Cooking));
            var r = new CitizenRecord { Id = "a", Name = "Astrid", Cooking = 7f, CookingXp = 0.5f };
            Assert.True(r.Practise(WorkSkill.Cooking, 0.1f) || r.CookingXp > 0.5f);
            CitizenRecord copy = CitizenRecord.Decode(r.Encode());
            Assert.Equal(r.Cooking, copy.Level(WorkSkill.Cooking));
            Assert.Equal(r.CookingXp, copy.CookingXp);
            Assert.Equal(r.Cooking, FallenReturn.Restore(r, "c", 9).Cooking);   // a fallen cook comes back a cook
            Assert.DoesNotContain("cooking", new CitizenRecord { Id = "b", Name = "Ulf" }.Encode());   // none: not saved
            Assert.Contains("Cooking 7", WorkSkills.Line(new CitizenRecord { Cooking = 7f }, t => t == "$vc_skill_cooking" ? "Cooking" : t));
        }

        // ---- the review's fix pass ----

        [Fact]   // review 1: a full station whose fire died mid-batch was never fed: the cook rested by it
        public void A_cold_station_with_food_on_it_or_room_needs_its_fire_fed()
        {
            Assert.True(CookRules.NeedsFuel(lit: false, freeSlots: 0, uncooked: 3));
            Assert.True(CookRules.NeedsFuel(false, 2, 0));
            Assert.False(CookRules.NeedsFuel(false, 0, 0));   // full of done food: nothing to cook
            Assert.False(CookRules.NeedsFuel(true, 0, 3));
        }

        [Fact]   // review 2: a plan that found nothing ran again every tick
        public void After_a_plan_that_found_nothing_the_cook_waits_before_planning_again()
        {
            Assert.False(CookRules.MayPlan(now: 10f, failedAt: 9f));
            Assert.True(CookRules.MayPlan(10f, 10f - CookRules.PlanAgainSeconds));
            Assert.True(CookRules.MayPlan(10f, float.NegativeInfinity));
        }

        [Fact]   // review 7: a trip to a chest out of reach never ended
        public void A_trip_that_takes_too_long_is_given_up()
        {
            Assert.False(CookRules.TripOver(CookRules.TripSeconds - 1f));
            Assert.True(CookRules.TripOver(CookRules.TripSeconds));
        }

        [Fact]   // review 4: more pieces sent before the station's slots came back from its computer were lost
        public void No_more_pieces_go_on_until_the_last_ones_show_on_the_station()
        {
            Assert.False(CookRules.MayAddMore(filledNow: 1, filledExpected: 3, sinceSent: 2f));
            Assert.True(CookRules.MayAddMore(3, 3, 2f));
            Assert.True(CookRules.MayAddMore(1, 3, CookRules.SlotSyncTimeout));   // never shows: the station's word stands
        }

        [Fact]   // review 5: a piece dropped late on another computer was left on the floor after 10 s
        public void The_cook_keeps_looking_while_pieces_taken_off_are_still_owed()
        {
            Assert.True(CookRules.KeepGathering(owed: 1, sinceTakeOff: 20f));
            Assert.False(CookRules.KeepGathering(0, 1f));
            Assert.False(CookRules.KeepGathering(1, CookRules.GatherSeconds));
        }

        [Fact]   // review 10: cooked food kept by the hall's Keep in stock counted as the settlers' hot meals
        public void Only_spare_hot_food_counts_for_the_settlers()
        {
            var cooked = new HashSet<string> { "CookedMeat", "DeerStew" };
            var spare = new Dictionary<string, int> { ["CookedMeat"] = 4, ["DeerStew"] = 2, ["Raspberry"] = 9, ["Coal"] = 3 };
            Assert.Equal(6, HotFood.Count(spare, cooked, item => item != "Coal"));
        }

        [Fact]   // review 3: hot first only for the Mead Hall's hot food, so the player's stew in the camp's chest is left for them
        public void Hot_food_comes_first_only_from_the_Mead_Halls_chest()
        {
            var cooked = new HashSet<string> { "CookedMeat", "DeerStew" };
            System.Func<string, bool> first = HotFood.FirstAt(cooked, new Dictionary<string, int> { ["CookedMeat"] = 5, ["DeerStew"] = 0 });
            Assert.True(first("CookedMeat"));
            Assert.False(first("DeerStew"));    // hot, but none in the hall
            Assert.False(first("Raspberry"));
            Assert.False(HotFood.FirstAt(cooked, null)("CookedMeat"));   // no Mead Hall
            var stock = new[] { new FoodStock("Raspberry", 5, 1f), new FoodStock("DeerStew", 5, 9f) };
            Assert.Equal("Raspberry", Meals.Choose(stock, new string[0], HotFood.FirstAt(cooked, new Dictionary<string, int>())));
        }

        [Fact]   // the game check's run 1: the game's own check counted a hearth with no fuel as lit, and meat went on
        public void A_station_burns_only_when_its_hearth_has_fuel()
        {
            Assert.True(CookRules.FireBurns(gameSaysLit: true, hasHearth: true, hearthHasFuel: true));
            Assert.False(CookRules.FireBurns(true, true, false));
            Assert.False(CookRules.FireBurns(false, true, true));
            Assert.True(CookRules.FireBurns(true, false, false));   // a fire the cook can't find: the game's word
        }

        // ---- the review's four minors ----

        [Fact]   // minor 8: a hearth full of wood in the rain, or a station off its flames, kept the cook by it with wood in hand
        public void Wood_helps_only_a_fire_that_has_none()
        {
            Assert.Equal(FireNeed.Burns, CookRules.Need(lit: true, fuelKnown: true, fuelHave: 4f));
            Assert.Equal(FireNeed.Fuel, CookRules.Need(false, true, 0f));
            Assert.Equal(FireNeed.WontBurn, CookRules.Need(false, true, 6f));    // fed and still cold: rain on it, or off the flames
            Assert.Equal(FireNeed.WontBurn, CookRules.Need(false, false, 0f));   // no hearth under it to feed
            Assert.Equal(FireNeed.Fuel, CookRules.Need(CookRules.FireBurns(true, true, false), true, 0f));   // the game check's run 1: fed first
        }

        [Fact]   // the minors' review 2: fuel shows from another computer a moment before the flames, and "won't burn" flickered
        public void A_fire_just_fed_is_given_a_moment_to_catch()
        {
            Assert.Equal(FireNeed.Catching, CookRules.Need(lit: false, fuelKnown: true, fuelHave: 6f, sinceFed: 1f));
            Assert.Equal(FireNeed.WontBurn, CookRules.Need(false, true, 6f, CookRules.CatchSeconds));
            Assert.Equal(FireNeed.Fuel, CookRules.Need(false, true, 0f, 1f));   // none yet: still fuel
            Assert.Equal(FireNeed.Burns, CookRules.Need(true, true, 6f, 1f));
            Assert.Equal(CookRules.PutOnRank(FireNeed.Fuel), CookRules.PutOnRank(FireNeed.Catching));   // raw food waits for the flames there
        }

        [Fact]   // the minors' review 1: raw food in hand went to a station whose fire won't burn, every tick
        public void Raw_food_goes_on_a_burning_station_first_never_one_that_wont_burn()
        {
            Assert.True(CookRules.PutOnRank(FireNeed.Burns) < CookRules.PutOnRank(FireNeed.Fuel));
            Assert.Equal(-1, CookRules.PutOnRank(FireNeed.WontBurn));
        }

        [Fact]   // minor 9: the 30 s hold ran out for a station whose hall's chest was across an unloaded zone edge
        public void A_station_waits_for_its_hall_while_the_halls_spot_is_not_loaded()
        {
            float since = CookRules.HallWaitFrom(waitingSince: 5f, hallAreaReady: false, now: 400f);
            Assert.Equal(400f, since);   // still not loaded: the wait starts again
            Assert.True(CookRules.HoldsWithoutHall(true, 400f - since));
            Assert.Equal(5f, CookRules.HallWaitFrom(5f, true, 400f));   // loaded and no hall: it's gone, the 30 s run out
            Assert.False(CookRules.HoldsWithoutHall(true, 400f - CookRules.HallWaitFrom(5f, true, 400f)));
            Assert.Equal("vc_cook_hall", SaveKeys.CookHallPos);
        }

        [Fact]   // minor 12: one piece to the chest per walk
        public void The_cook_takes_every_done_piece_off_before_walking_to_the_chest()
        {
            Assert.True(CookRules.TakeMoreOff(carried: 1, carryCapacity: 20, anotherDone: true, distance: 3f));
            Assert.False(CookRules.TakeMoreOff(1, 20, false, 3f));
            Assert.False(CookRules.TakeMoreOff(20, 20, true, 3f));   // hands full: to the chest first
            // the minors' review 4: a done piece out of reach kept the hot food in hand from the chest
            Assert.False(CookRules.TakeMoreOff(1, 20, true, MoveRules.StuckDeliveryRange + 1f));
        }

        [Fact]   // minor 12: a piece lying by another hall station, not taken off there, counted as the cook's
        public void Only_what_dropped_by_a_station_taken_off_from_is_the_cooks()
        {
            var a = new Vec3(0f, 0f, 0f);
            var b = new Vec3(5f, 0f, 0f);
            var drops = new CookDrops();
            drops.TakingOffAt(a, new[] { new KeyValuePair<string, Vec3>("old", new Vec3(1f, 0f, 0f)) });
            Assert.False(drops.IsOwed("old", new Vec3(1f, 0f, 0f)));      // lying there before
            Assert.True(drops.IsOwed("new", new Vec3(0.5f, 0f, 0f)));
            Assert.False(drops.IsOwed("player's", new Vec3(9f, 0f, 0f)));  // by station b, nothing taken off there
            // b's first take-off while a piece from a is still owed: what lies by a too isn't taken for "there before"
            drops.TakingOffAt(b, new[] { new KeyValuePair<string, Vec3>("owed", new Vec3(2.5f, 0f, 0f)), new KeyValuePair<string, Vec3>("b's old", new Vec3(8f, 0f, 0f)) });
            Assert.True(drops.IsOwed("owed", new Vec3(2.5f, 0f, 0f)));
            Assert.False(drops.IsOwed("b's old", new Vec3(8f, 0f, 0f)));
            Assert.True(drops.IsOwed("from b", new Vec3(6f, 0f, 0f)));
            Assert.True(drops.HasLookedAt(b));   // the minors' review 7: no floor scan again for b this look
            Assert.False(drops.HasLookedAt(new Vec3(20f, 0f, 0f)));
            drops.NewLook();
            Assert.False(drops.IsOwed("new", new Vec3(0.5f, 0f, 0f)));    // a new look: nothing taken off yet
            Assert.False(drops.HasLookedAt(a));
        }

        [Fact]   // minor 12: the walk to a dropped piece checked for stuck twice a tick
        public void A_dropped_piece_is_picked_up_there_or_from_close_by_when_stuck()
        {
            Assert.Equal(GatherStep.PickUp, CookRules.Gather(there: true, stuck: false, distance: 1f));
            Assert.Equal(GatherStep.Walk, CookRules.Gather(false, false, 6f));
            Assert.Equal(GatherStep.PickUp, CookRules.Gather(false, true, MoveRules.StuckDeliveryRange));   // by the hearth: from here
            Assert.Equal(GatherStep.GiveUp, CookRules.Gather(false, true, MoveRules.StuckDeliveryRange + 1f));
        }

        [Fact]
        public void Hot_food_is_what_a_station_makes()
        {
            var cooked = new HashSet<string> { "CookedMeat", "DeerStew" };
            Assert.True(HotFood.Is("CookedMeat", cooked));
            Assert.False(HotFood.Is("Raspberry", cooked));
            Assert.False(HotFood.Is(null, cooked));
            Assert.False(HotFood.Is("CookedMeat", null));
        }
    }
}
