using System.Collections.Generic;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    public class OrderPatienceTests
    {
        private static OrderOption Other(double created, bool aside = false) => new OrderOption(false, created, aside);
        private static OrderOption Own(double created, bool aside = false) => new OrderOption(true, created, aside);

        [Fact]
        public void His_own_hut_comes_first_even_when_ordered_last()
        {
            Assert.Equal(2, OrderChoice.Pick(new[] { Other(10), Other(5), Own(50) }));
        }

        [Fact]
        public void Otherwise_the_oldest_order()
        {
            Assert.Equal(1, OrderChoice.Pick(new[] { Other(10), Other(5), Other(7) }));
        }

        [Fact]
        public void Set_aside_orders_are_passed_over()
        {
            Assert.Equal(2, OrderChoice.Pick(new[] { Other(5, aside: true), Own(1, aside: true), Other(9) }));
        }

        [Fact]
        public void When_every_open_order_is_set_aside_there_is_none()
        {
            Assert.Equal(-1, OrderChoice.Pick(new[] { Other(5, aside: true), Own(1, aside: true) }));
            Assert.Equal(-1, OrderChoice.Pick(new OrderOption[0]));
        }

        [Fact]
        public void Too_soon_he_waits()
        {
            Assert.False(OrderPatience.MoveOn(59, 60, anotherOrder: true, needsLevel: 0, highestBuilderHutLevel: 1));
            Assert.False(OrderPatience.MoveOn(-5, 60, anotherOrder: true, needsLevel: 0, highestBuilderHutLevel: 1));   // the clock went back
        }

        [Fact]
        public void After_the_wait_he_moves_on_to_another_order()
        {
            Assert.True(OrderPatience.MoveOn(60, 60, anotherOrder: true, needsLevel: 0, highestBuilderHutLevel: 1));
            Assert.True(OrderPatience.MoveOn(300, 60, anotherOrder: true, needsLevel: 2, highestBuilderHutLevel: 1));
        }

        [Fact]
        public void With_nothing_else_to_do_he_stays()
        {
            Assert.False(OrderPatience.MoveOn(600, 60, anotherOrder: false, needsLevel: 0, highestBuilderHutLevel: 1));
            Assert.False(OrderPatience.MoveOn(600, 60, anotherOrder: false, needsLevel: 2, highestBuilderHutLevel: 1));   // no better builder in the colony
        }

        [Fact]
        public void A_site_waiting_for_a_level_the_colony_has_is_left_to_a_better_builder()
        {
            Assert.True(OrderPatience.MoveOn(60, 60, anotherOrder: false, needsLevel: 2, highestBuilderHutLevel: 2));
            Assert.True(OrderPatience.MoveOn(60, 60, anotherOrder: false, needsLevel: 2, highestBuilderHutLevel: 3));
        }

        [Fact]
        public void The_wait_and_the_time_away()
        {
            Assert.Equal(60, OrderPatience.WaitSeconds);
            Assert.Equal(300, OrderPatience.AsideSeconds);
        }

        // Review: a Builder's Hut with nobody working there isn't a better builder; a lone builder kept leaving his site.
        [Fact]
        public void Only_a_Builders_Hut_with_a_builder_counts_as_a_better_builder()
        {
            var huts = new[] { new KeyValuePair<int, bool>(3, false), new KeyValuePair<int, bool>(1, true) };
            Assert.Equal(1, OrderPatience.BestStaffedLevel(huts));
            Assert.Equal(0, OrderPatience.BestStaffedLevel(new KeyValuePair<int, bool>[0]));
        }
    }
}
