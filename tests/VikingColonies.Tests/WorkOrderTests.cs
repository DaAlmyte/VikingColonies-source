using System.Collections.Generic;
using System.Linq;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    // The Town Hall's Work orders tab (0.9.0): Do first, Set aside / Resume, and what the orders still need.
    public class WorkOrderTests
    {
        private static OrderOption Other(double created, bool aside = false, double first = 0, bool paused = false) =>
            new OrderOption(false, created, aside, first, paused);

        private static OrderOption Own(double created, bool aside = false, double first = 0, bool paused = false) =>
            new OrderOption(true, created, aside, first, paused);

        [Fact]
        public void An_order_keeps_its_first_mark_and_pause_through_saving()
        {
            var order = new BuildOrderRecord { Total = 3, First = 1234.5, Paused = true };
            BuildOrderRecord back = BuildOrderRecord.Decode(order.Encode());
            Assert.Equal(1234.5, back.First);
            Assert.True(back.Paused);
        }

        [Fact]
        public void An_order_saved_before_work_orders_is_neither_first_nor_set_aside()
        {
            string old = new BuildOrderRecord { Total = 3 }.Encode().Replace("first", "x1").Replace("paused", "x2");
            BuildOrderRecord back = BuildOrderRecord.Decode(old);
            Assert.Equal(0, back.First);
            Assert.False(back.Paused);
        }

        [Fact]
        public void An_order_the_player_set_aside_is_never_taken()
        {
            Assert.Equal(1, OrderChoice.Pick(new[] { Own(1, paused: true), Other(9) }));
            Assert.Equal(-1, OrderChoice.Pick(new[] { Other(1, paused: true, first: 50) }));
        }

        [Fact]
        public void The_order_marked_first_most_recently_comes_before_his_own_hut_and_the_oldest()
        {
            Assert.Equal(2, OrderChoice.Pick(new[] { Own(1), Other(2), Other(9, first: 100) }));
            Assert.Equal(1, OrderChoice.Pick(new[] { Other(1, first: 100), Other(9, first: 200), Own(3) }));
        }

        [Fact]
        public void Without_marks_he_takes_his_own_hut_then_the_oldest_as_before()
        {
            Assert.Equal(2, OrderChoice.Pick(new[] { Other(10), Other(5), Own(50) }));
            Assert.Equal(1, OrderChoice.Pick(new[] { Other(10), Other(5), Other(7) }));
        }

        [Fact]
        public void An_order_he_set_aside_is_passed_over_even_when_marked_first()
        {
            // The game side drops "set aside" when the player marked it first after he set it aside.
            Assert.Equal(1, OrderChoice.Pick(new[] { Other(1, aside: true, first: 100), Other(9) }));
        }

        [Fact]
        public void He_switches_only_to_an_order_marked_first_after_his_own()
        {
            Assert.True(OrderChoice.ShouldSwitch(currentFirst: 0, bestOtherFirst: 50));
            Assert.True(OrderChoice.ShouldSwitch(currentFirst: 40, bestOtherFirst: 50));
            Assert.False(OrderChoice.ShouldSwitch(currentFirst: 50, bestOtherFirst: 50));
            Assert.False(OrderChoice.ShouldSwitch(currentFirst: 60, bestOtherFirst: 50));
            Assert.False(OrderChoice.ShouldSwitch(currentFirst: 0, bestOtherFirst: 0));
        }

        [Fact]
        public void An_order_he_set_aside_stays_aside_until_the_player_marks_it_first_again()
        {
            // Review: once his five minutes were up, a builder kept leaving good work for a "first" order still waiting on
            // the player. His own set-aside counts until the player presses Do first again.
            Assert.True(OrderChoice.StillAside(blocked: true, first: 100, firstWhenSetAside: 100));
            Assert.False(OrderChoice.StillAside(blocked: true, first: 150, firstWhenSetAside: 100));
            Assert.False(OrderChoice.StillAside(blocked: false, first: 100, firstWhenSetAside: 100));
            Assert.True(OrderChoice.MaySwitchTo(first: 100, setAsideByHim: false, firstWhenSetAside: 0));
            Assert.False(OrderChoice.MaySwitchTo(first: 100, setAsideByHim: true, firstWhenSetAside: 100));   // even after his five minutes
            Assert.True(OrderChoice.MaySwitchTo(first: 150, setAsideByHim: true, firstWhenSetAside: 100));
            Assert.False(OrderChoice.MaySwitchTo(first: 0, setAsideByHim: false, firstWhenSetAside: 0));
        }

        private static WorkOrderRow Row(string id, double created, double first = 0, bool paused = false) =>
            new WorkOrderRow { BuildingId = id, Created = created, First = first, Paused = paused };

        [Fact]
        public void The_list_shows_orders_as_builders_take_them_set_aside_last()
        {
            var rows = new List<WorkOrderRow> { Row("old", 1), Row("aside", 0, paused: true), Row("first", 9, first: 50), Row("new", 5), Row("firster", 8, first: 70) };
            Assert.Equal(new[] { "firster", "first", "old", "new", "aside" }, WorkOrderList.Sorted(rows).Select(r => r.BuildingId).ToArray());
        }

        [Fact]
        public void Still_to_bring_is_what_the_orders_need_beyond_the_chests()
        {
            var total = new Dictionary<string, int>();
            NeedsTally.Add(total, new Dictionary<string, int> { { "Wood", 30 }, { "Stone", 4 } });
            NeedsTally.Add(total, new Dictionary<string, int> { { "Wood", 10 }, { "Resin", 2 } });
            Assert.Equal(40, total["Wood"]);
            Dictionary<string, int> missing = NeedsTally.Shortfall(total, new Dictionary<string, int> { { "Wood", 25 }, { "Resin", 9 } });
            Assert.Equal(15, missing["Wood"]);
            Assert.Equal(4, missing["Stone"]);
            Assert.False(missing.ContainsKey("Resin"));
        }

        [Fact]
        public void Nothing_to_bring_when_the_chests_hold_enough()
        {
            Assert.Empty(NeedsTally.Shortfall(new Dictionary<string, int> { { "Wood", 5 } }, new Dictionary<string, int> { { "Wood", 5 } }));
            Assert.Empty(NeedsTally.Shortfall(new Dictionary<string, int>(), new Dictionary<string, int>()));
        }
    }
}

namespace VikingColonies.Tests
{
    public class OrderNeedsTests
    {
        [Fact]   // 1.3.1 (the 1.0 play-through): an order set aside for materials says so once the chests hold them
        public void A_need_of_materials_is_told_from_one_of_tools_or_trees()
        {
            Assert.True(OrderWords.IsMaterials("1 $item_wood"));
            Assert.True(OrderWords.IsMaterials("12 $item_wood, 4 $item_stone"));
            Assert.True(OrderWords.IsMaterials("3 SomeModItem"));   // an item without a name token: its prefab
            Assert.False(OrderWords.IsMaterials(""));
            Assert.False(OrderWords.IsMaterials(null));
            Assert.False(OrderWords.IsMaterials("$vc_builder_hut_level 2 $vc_for $vc_thing_tree $vc_or_clear_yourself"));
            Assert.False(OrderWords.IsMaterials("$vc_a_hoe"));
        }
    }
}
