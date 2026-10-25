using System.Collections.Generic;
using System.Linq;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>The couriers' plan (1.5): what to carry, most urgent first.</summary>
    public class CourierPlanTests
    {
        private const int Load = 50;

        private static float FoodValue(string item) => item == "Raspberry" ? 1f : item == "CookedMeat" ? 3f : 0f;

        private static CourierSite Site(string id, float x, bool producer = false, float fill = 0f,
            Dictionary<string, int>? spare = null, params KeepEntry[] shortOf) => new CourierSite
        {
            Id = id, At = new GroundPoint(x, 0), IsProducer = producer, Fill = fill,
            Spare = spare ?? new Dictionary<string, int>(), Short = shortOf.ToList(),
        };

        private static CourierSite Store(Dictionary<string, int>? spare = null) => new CourierSite
            { Id = "store", At = new GroundPoint(0, 0), IsStorehouse = true, Spare = spare ?? new Dictionary<string, int>(), Short = new List<KeepEntry>() };

        private static StoreFacts Facts(int level = 1, Dictionary<string, int>? dropOff = null, params Dictionary<string, int>[] chests) => new StoreFacts
            { Id = "store", Level = level, DropOff = dropOff ?? new Dictionary<string, int>(), Chests = chests.Select(c => new StoreChest { Items = c }).ToList(), InsideHasRoom = true };

        private static KeepEntry Need(string item, int amount) => new KeepEntry { Item = item, Amount = amount };

        private static Dictionary<string, int> Of(params object[] pairs)
        {
            var d = new Dictionary<string, int>();
            for (int i = 0; i < pairs.Length; i += 2) d[(string)pairs[i]] = (int)pairs[i + 1];
            return d;
        }

        private static List<CourierTask> Plan(IEnumerable<CourierSite> sites, StoreFacts facts) =>
            CourierPlan.Tasks(sites.ToList(), facts, Load, FoodValue);

        [Fact]
        public void Deliveries_come_first_then_urgent_pickups_the_drop_off_pickups_and_sorting()
        {
            var tasks = Plan(new[] {
                Store(Of("Resin", 20, "Stone", 40)),
                Site("farm", 10, producer: true, fill: 0.9f, spare: Of("Carrot", 30)),
                Site("lumber", 20, producer: true, fill: 0.2f, spare: Of("Wood", 30)),
                Site("hut", 30, shortOf: Need("Resin", 5)) },
                Facts(3, Of("Resin", 20), Of("Stone", 30), Of("Stone", 10)));
            Assert.Equal(new[] { CourierTaskKind.Deliver, CourierTaskKind.UrgentPickup, CourierTaskKind.DropOff, CourierTaskKind.Pickup, CourierTaskKind.Sort },
                tasks.Select(t => t.Kind));
        }

        [Fact]
        public void A_delivery_takes_from_the_storehouse_before_another_huts_spare()
        {
            var tasks = Plan(new[] { Site("lumber", 1, producer: true, spare: Of("Wood", 40)), Store(Of("Wood", 40)), Site("hut", 5, shortOf: Need("Wood", 10)) }, Facts());
            CourierTask deliver = tasks.First(t => t.Kind == CourierTaskKind.Deliver);
            Assert.Equal("store", deliver.From);
            Assert.Equal("hut", deliver.To);
            Assert.Equal(10, deliver.Amount);
            tasks = Plan(new[] { Site("lumber", 1, producer: true, spare: Of("Wood", 40)), Store(), Site("hut", 5, shortOf: Need("Wood", 10)) }, Facts());
            Assert.Equal("lumber", tasks.First(t => t.Kind == CourierTaskKind.Deliver).From);
        }

        [Fact]
        public void Nothing_is_taken_from_the_Town_Hall_or_the_Builders_Hut()
        {
            CourierSite hall = Site("hall", 1, fill: 0.95f, spare: Of("Wood", 60));
            hall.IsTownHall = true;
            CourierSite builder = Site("builder", 2, fill: 0.95f, spare: Of("Wood", 60));
            builder.IsBuilderHut = true;
            var tasks = Plan(new[] { Store(), hall, builder, Site("hut", 5, shortOf: Need("Wood", 10)) }, Facts());
            Assert.DoesNotContain(tasks, t => t.From == "hall" || t.From == "builder");
        }

        [Fact]
        public void A_producer_three_quarters_full_is_an_urgent_pickup_the_fullest_first()
        {
            var tasks = Plan(new[] { Store(),
                Site("farm", 10, producer: true, fill: 0.8f, spare: Of("Carrot", 30)),
                Site("forager", 20, producer: true, fill: 0.95f, spare: Of("Raspberry", 70)),
                Site("lumber", 30, producer: true, fill: 0.74f, spare: Of("Wood", 30)) }, Facts());
            List<CourierTask> urgent = tasks.Where(t => t.Kind == CourierTaskKind.UrgentPickup).ToList();
            Assert.Equal(new[] { "forager", "farm" }, urgent.Select(t => t.From));
            Assert.All(urgent, t => Assert.Equal("store", t.To));
            Assert.Equal(50, urgent[0].Amount);   // one load of its 70
            Assert.Equal(CourierTaskKind.Pickup, tasks.Single(t => t.From == "lumber").Kind);   // under three-quarters: a plain pickup
        }

        [Fact]   // Review Focus 3: a Farm's working seeds are not in its Spare, so they are never picked up
        public void Surplus_leaves_kept_items_and_working_stock()
        {
            var tasks = Plan(new[] { Store(), Site("farm", 10, producer: true, spare: Of("Barley", 5)) }, Facts());
            Assert.Empty(tasks);   // 5 spare: less than a pickup worth the walk
            tasks = Plan(new[] { Store(), Site("farm", 10, producer: true, spare: Of("Barley", 12)) }, Facts());
            Assert.Equal(12, tasks.Single().Amount);
        }

        [Fact]
        public void The_drop_off_goes_to_a_building_short_of_it_before_the_store()
        {
            var tasks = Plan(new[] { Store(Of("Resin", 20)), Site("hut", 5, shortOf: Need("Resin", 5)) }, Facts(1, Of("Resin", 20)));
            CourierTask deliver = tasks.Single(t => t.Kind == CourierTaskKind.Deliver);
            Assert.Equal("store", deliver.From);
            Assert.Equal(5, deliver.Amount);
            CourierTask drop = tasks.Single(t => t.Kind == CourierTaskKind.DropOff);
            Assert.Equal("Resin", drop.Item);
            Assert.Equal(15, drop.Amount);   // the rest into the store
            Assert.Equal("store", drop.From);
            Assert.Equal("store", drop.To);
        }

        [Fact]   // Review Focus 5: only carried items reach DropOff; a sword there makes no task
        public void Gear_in_the_drop_off_is_left()
        {
            Assert.Empty(Plan(new[] { Store() }, Facts(1, Of())));
            StoreFacts full = Facts(1, Of("Resin", 20));
            full.InsideHasRoom = false;
            Assert.Empty(Plan(new[] { Store(Of("Resin", 20)) }, full));   // nowhere to put it: it stays
        }

        [Fact]
        public void Sorting_moves_the_smaller_stack_to_the_chest_with_the_most_from_level_3()
        {
            Dictionary<string, int>[] chests = { Of("Stone", 10, "Wood", 5), Of("Stone", 30) };
            Assert.Empty(Plan(new[] { Store() }, Facts(2, null, chests)));
            CourierTask sort = Plan(new[] { Store() }, Facts(3, null, chests)).Single();
            Assert.Equal(CourierTaskKind.Sort, sort.Kind);
            Assert.Equal("Stone", sort.Item);
            Assert.Equal(10, sort.Amount);
        }

        [Fact]
        public void A_hut_with_couriers_take_the_rest_off_is_never_picked_up_from()
        {
            CourierSite farm = Site("farm", 10, producer: true, fill: 0.9f, spare: Of("Carrot", 30));
            farm.TakeRest = false;
            Assert.Empty(Plan(new[] { Store(), farm }, Facts()));
        }

        [Fact]
        public void A_delivery_carries_at_most_one_load_and_food_of_any_kind_the_plainest_first()
        {
            var tasks = Plan(new[] { Store(Of("Wood", 400, "CookedMeat", 20, "Raspberry", 20)), Site("hut", 5, shortOf: Need("Wood", 300)),
                Site("hall", 6, shortOf: Need(StockList.AnyFood, 10)) }, Facts());
            Assert.Equal(50, tasks.Single(t => t.To == "hut").Amount);
            CourierTask food = tasks.Single(t => t.To == "hall");
            Assert.Equal("Raspberry", food.Item);
            Assert.Equal(StockList.AnyFood, food.Need);
        }

        // ---- Its review's fixes (1.5) ----

        [Fact]   // C1: a full store took every courier's load and kept it in their hands
        public void Pickups_wait_for_room_in_the_store()
        {
            StoreFacts full = Facts();
            full.InsideHasRoom = false;
            var tasks = Plan(new[] { Store(), Site("farm", 10, producer: true, fill: 0.9f, spare: Of("Carrot", 30)),
                Site("lumber", 20, producer: true, spare: Of("Wood", 30)) }, full);
            Assert.DoesNotContain(tasks, t => t.Kind == CourierTaskKind.UrgentPickup || t.Kind == CourierTaskKind.Pickup);
        }

        [Fact]   // I2: a delivery carried to a full chest and back, every minute
        public void A_building_with_no_room_gets_no_delivery_of_what_it_cant_take()
        {
            CourierSite hall = Site("hall", 5, shortOf: Need(StockList.AnyFood, 10));
            hall.CanTake = item => item == "CookedMeat";   // room only beside its stack of meat
            var tasks = Plan(new[] { Store(Of("Raspberry", 20, "CookedMeat", 20)), hall }, Facts());
            Assert.Equal("CookedMeat", tasks.Single(t => t.To == "hall").Item);   // not the plainest: the one it can take
            hall.CanTake = item => false;
            Assert.DoesNotContain(Plan(new[] { Store(Of("Raspberry", 20)), hall }, Facts()), t => t.To == "hall");
        }

        [Fact]   // I1: sorting into a full chest put the stack back where it was, for ever
        public void Sorting_needs_room_in_a_chest_holding_more()
        {
            var a = new StoreChest { Items = Of("Stone", 30), RoomFor = item => 0 };    // the most, but full
            var b = new StoreChest { Items = Of("Stone", 10) };
            StoreFacts facts = Facts(3);
            facts.Chests = new List<StoreChest> { a, b };
            Assert.Empty(Plan(new[] { Store() }, facts));
            var c = new StoreChest { Items = Of("Stone", 20) };
            var d = new StoreChest { Items = Of("Stone", 5) };
            facts.Chests = new List<StoreChest> { a, c, d };
            CourierTask sort = Plan(new[] { Store() }, facts).Single();
            Assert.Equal(5, sort.Amount);   // the 5 into the 20, which has room
        }

        [Fact]   // I5: a storehouse's own Keep in stock was never filled from its own chests inside
        public void A_storehouse_short_of_what_it_keeps_is_filled_from_its_chests_inside()
        {
            CourierSite house = Store();
            house.Short = new List<KeepEntry> { Need("Raspberry", 5) };
            CourierTask fill = Plan(new[] { house }, Facts(1, null, Of("Raspberry", 20))).Single();
            Assert.Equal(CourierTaskKind.Deliver, fill.Kind);
            Assert.Equal("store", fill.From);
            Assert.Equal("store", fill.To);
            Assert.Equal(5, fill.Amount);
        }

        [Fact]   // the review round of 2026-10-23: its own Keep in stock and another delivery counted the same goods (a wasted trip)
        public void The_storehouses_own_keep_and_a_delivery_never_count_the_same_goods()
        {
            CourierSite house = Store(Of("Wood", 30));   // what it may give: the 30 Wood inside
            house.Short = new List<KeepEntry> { Need("Wood", 20) };
            var tasks = Plan(new[] { house, Site("hut", 5, shortOf: Need("Wood", 30)) }, Facts(1, null, Of("Wood", 30)));
            int own = tasks.Where(t => t.Kind == CourierTaskKind.Deliver && t.To == "store").Sum(t => t.Amount);
            int toHut = tasks.Where(t => t.Kind == CourierTaskKind.Deliver && t.To == "hut" && t.From == "store").Sum(t => t.Amount);
            Assert.Equal(20, own);
            Assert.Equal(10, toHut);   // the rest, neither counted twice nor held back (its review)
        }

        [Fact]   // I4: couriers filled a store being emptied for its take-down
        public void A_closed_store_gets_no_pickups_drop_off_or_sorting()
        {
            StoreFacts closed = Facts(3, Of("Resin", 10), Of("Stone", 30), Of("Stone", 10));
            closed.Open = false;
            var tasks = Plan(new[] { Store(), Site("farm", 10, producer: true, fill: 0.9f, spare: Of("Carrot", 30)) }, closed);
            Assert.Empty(tasks);
        }

        [Fact]   // part 3: "Couriers take the rest" off keeps a hut's chest as the player arranged it
        public void A_hut_with_the_switch_off_is_no_delivery_source()
        {
            CourierSite farm = Site("farm", 5, producer: true, fill: 0.9f, spare: Of("Raspberry", 30));
            farm.TakeRest = false;
            var tasks = Plan(new[] { Store(), farm, Site("tower", 10, shortOf: Need("Raspberry", 10)) }, Facts());
            Assert.DoesNotContain(tasks, t => t.From == "farm");
            farm.TakeRest = true;
            tasks = Plan(new[] { Store(), farm, Site("tower", 10, shortOf: Need("Raspberry", 10)) }, Facts());
            Assert.Contains(tasks, t => t.Kind == CourierTaskKind.Deliver && t.From == "farm" && t.To == "tower");
        }

        [Fact]
        public void Only_huts_have_the_switch()
        {
            Assert.False(CourierRules.HasTakeRestSwitch(isTownHall: true, isBuilderHut: false, isStorehouse: false));
            Assert.False(CourierRules.HasTakeRestSwitch(isTownHall: false, isBuilderHut: true, isStorehouse: false));
            Assert.False(CourierRules.HasTakeRestSwitch(isTownHall: false, isBuilderHut: false, isStorehouse: true));
            Assert.True(CourierRules.HasTakeRestSwitch(isTownHall: false, isBuilderHut: false, isStorehouse: false));
        }

        private static StoreFacts Closing(params object[] clearing)
        {
            StoreFacts facts = Facts(3);
            facts.Open = false;
            facts.Closing = true;
            facts.Clearing = Of(clearing);
            return facts;
        }

        [Fact]   // part 3: a storehouse emptied by its couriers before it is taken down
        public void A_closing_store_is_cleared_to_who_is_short_then_the_hall_then_the_nearest()
        {
            CourierSite hall = Site("hall", 30);
            hall.IsTownHall = true;
            var sites = new[] { Store(), Site("near", 5), hall, Site("mason", 20, shortOf: Need("Stone", 10)) };
            var tasks = Plan(sites, Closing("Stone", 80, "Wood", 20));
            CourierTask stone = tasks.Single(t => t.Kind == CourierTaskKind.Clear && t.Item == "Stone");
            Assert.Equal("store", stone.From);
            Assert.Equal("mason", stone.To);
            Assert.Equal(50, stone.Amount);
            CourierTask wood = tasks.Single(t => t.Kind == CourierTaskKind.Clear && t.Item == "Wood");
            Assert.Equal("hall", wood.To);
            Assert.Equal(20, wood.Amount);
            hall.CanTake = item => false;
            wood = Plan(sites, Closing("Wood", 20)).Single(t => t.Kind == CourierTaskKind.Clear);
            Assert.Equal("near", wood.To);
        }

        [Fact]   // its review (part 3): emptying filled a Builder's Hut, or a hut the player keeps as arranged
        public void A_closing_store_never_fills_a_builders_hut_or_a_switched_off_hut()
        {
            CourierSite hall = Site("hall", 1);
            hall.IsTownHall = true;
            hall.CanTake = item => false;
            CourierSite builder = Site("builder", 2);
            builder.IsBuilderHut = true;
            CourierSite kept = Site("kept", 3);
            kept.TakeRest = false;
            var sites = new List<CourierSite> { Store(), hall, builder, kept };
            Assert.DoesNotContain(Plan(sites, Closing("Stone", 30)), t => t.Kind == CourierTaskKind.Clear);
            sites.Add(Site("far", 40));
            Assert.Equal("far", Plan(sites, Closing("Stone", 30)).Single(t => t.Kind == CourierTaskKind.Clear).To);
            builder.Short = new List<KeepEntry> { Need("Stone", 10) };   // short of it: then it is a delivery
            Assert.Equal("builder", Plan(sites, Closing("Stone", 30)).Single(t => t.Kind == CourierTaskKind.Clear).To);
        }

        [Fact]   // Review Focus 2: the whole colony full
        public void A_closing_store_has_no_clear_task_where_nothing_has_room()
        {
            CourierSite hut = Site("hut", 5);
            hut.CanTake = item => false;
            Assert.DoesNotContain(Plan(new[] { Store(), hut }, Closing("Stone", 30)), t => t.Kind == CourierTaskKind.Clear);
        }

        [Fact]
        public void A_closing_store_plans_no_pickup_dropoff_or_sort()
        {
            StoreFacts facts = Closing("Stone", 30);
            facts.DropOff = Of("Resin", 10);
            facts.Chests = new List<StoreChest> { new StoreChest { Items = Of("Stone", 20) }, new StoreChest { Items = Of("Stone", 10) } };
            var tasks = Plan(new[] { Store(), Site("farm", 10, producer: true, fill: 0.9f, spare: Of("Carrot", 30)), Site("hut", 5) }, facts);
            Assert.All(tasks, t => Assert.Equal(CourierTaskKind.Clear, t.Kind));
        }

        [Fact]
        public void Clear_tasks_round_trip_through_encode()
        {
            var task = new CourierTask { Kind = CourierTaskKind.Clear, From = "store", To = "hall", Item = "Stone", Amount = 50 };
            CourierTask copy = CourierTask.Decode(task.Encode());
            Assert.NotNull(copy);
            Assert.Equal(CourierTaskKind.Clear, copy!.Kind);
            Assert.Equal(task.Key, copy.Key);
        }

        [Fact]   // the player's pick (1.5): couriers put away like with like, at every level
        public void Put_away_goes_to_a_chest_holding_that_kind_first_the_most_first_then_the_nearest()
        {
            var chests = new List<StoreChest>
            {
                new StoreChest { Items = Of("Wood", 10) },                       // nearest
                new StoreChest { Items = Of("Stone", 5) },
                new StoreChest { Items = Of("Stone", 40, "Wood", 5) },
                new StoreChest { Items = Of() },
            };
            Assert.Equal(new[] { 2, 1, 0, 3 }, CourierPlan.PutAwayOrder(chests, "Stone"));
            Assert.Equal(new[] { 0, 2, 1, 3 }, CourierPlan.PutAwayOrder(chests, "Wood"));
            Assert.Equal(new[] { 0, 1, 2, 3 }, CourierPlan.PutAwayOrder(chests, "Resin"));   // a new kind: the nearest with room
        }

        [Fact]
        public void Put_away_skips_a_chest_with_no_room_for_it()
        {
            var chests = new List<StoreChest>
            {
                new StoreChest { Items = Of("Stone", 50), RoomFor = item => 0 },
                new StoreChest { Items = Of("Stone", 10) },
                new StoreChest { Items = Of(), RoomFor = item => 0 },
            };
            Assert.Equal(new[] { 1 }, CourierPlan.PutAwayOrder(chests, "Stone"));
        }

        [Fact]
        public void A_task_survives_saving()
        {
            var task = new CourierTask { Kind = CourierTaskKind.Deliver, From = "a", To = "b", Item = "Raspberry", Need = StockList.AnyFood, Amount = 7 };
            CourierTask copy = CourierTask.Decode(task.Encode());
            Assert.Equal(task.Key, copy.Key);
            Assert.Equal(7, copy.Amount);
            Assert.Equal("Raspberry", copy.Item);
            Assert.Null(CourierTask.Decode("nonsense"));
        }
    }
}
