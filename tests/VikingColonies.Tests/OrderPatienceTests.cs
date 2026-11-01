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

        // 1.6 (the player's 1.4.2 watch and pick A, 2026-10-24): a builder waits at their own hut until it first stands,
        // as MineColonies starts; afterwards as before.
        [Fact]
        public void A_builder_never_leaves_their_own_hut_before_it_first_stands()
        {
            Assert.False(OrderPatience.MayLeave(ownSite: true, OrderKind.Build));
            Assert.True(OrderPatience.MayLeave(ownSite: true, OrderKind.Upgrade));
            Assert.True(OrderPatience.MayLeave(ownSite: true, OrderKind.Repair));
            Assert.True(OrderPatience.MayLeave(ownSite: true, OrderKind.Remove));
            Assert.True(OrderPatience.MayLeave(ownSite: false, OrderKind.Build));
        }

        // The review of 1.6: a wait only a higher Builder's Hut level answers (a birch on their own hut's spot) can never be
        // answered before that hut stands; nor can no support or a site out of reach; and a site that has everything is work.
        [Fact]
        public void A_builder_leaves_their_own_unbuilt_hut_only_for_a_wait_you_cant_answer_or_ready_work()
        {
            Assert.True(OrderPatience.MayLeave(true, OrderKind.Build, needsLevel: 2));
            Assert.True(OrderPatience.MayLeave(true, OrderKind.Build, hopeless: true));
            Assert.True(OrderPatience.MayLeave(true, OrderKind.Build, otherReady: true));
            Assert.False(OrderPatience.MayLeave(true, OrderKind.Build, needsLevel: 0, hopeless: false, otherReady: false));
        }

        [Fact]
        public void A_note_of_where_they_left_reads_back_and_shows_for_five_minutes()
        {
            string note = AwayNote.Encode("b1e10ec6", 1000.5);
            Assert.True(AwayNote.Decode(note, out string id, out double until));
            Assert.Equal("b1e10ec6", id);
            Assert.Equal(1000.5, until, 3);
            Assert.True(AwayNote.Shows(700.6, until));
            Assert.False(AwayNote.Shows(1000.6, until));
            Assert.False(AwayNote.Shows(100.0, until));   // the clock put back: never longer than a site's time aside
            Assert.Equal(1300.0, AwayNote.Until(1000.0), 3);   // OrderPatience.AsideSeconds on
        }

        [Fact]
        public void No_note_or_a_broken_one_reads_as_none()
        {
            Assert.False(AwayNote.Decode("", out _, out _));
            Assert.False(AwayNote.Decode("garbage", out _, out _));
            Assert.False(AwayNote.Decode("|12", out _, out _));
            Assert.False(AwayNote.Decode("id|NaN", out _, out _));
            Assert.False(AwayNote.Decode("id|Infinity", out _, out _));
        }

        [Fact]
        public void The_saved_note_never_changes_its_form()
        {
            Assert.Equal("b1e10ec6|1300.5", AwayNote.Encode("b1e10ec6", 1300.5));
            Assert.Equal("b1e10ec6|1300.5|level", AwayNote.Encode("b1e10ec6", 1300.5, forHigherLevel: true));
        }

        // v16's review: left only so a builder from a higher Builder's Hut can take the site, with no other work, the hover
        // still said "working here meanwhile". The note says which it was.
        [Fact]
        public void A_note_says_whether_they_left_for_a_higher_level_builder()
        {
            Assert.True(AwayNote.Decode(AwayNote.Encode("b1e10ec6", 1000.5, forHigherLevel: true), out string id, out double until, out bool forLevel));
            Assert.Equal("b1e10ec6", id);
            Assert.Equal(1000.5, until, 3);
            Assert.True(forLevel);
            Assert.True(AwayNote.Decode("b1e10ec6|1000.5", out _, out _, out bool other));
            Assert.False(other);
            Assert.True(AwayNote.Decode("b1e10ec6|1000.5|level", out string plainId, out _));   // the two-value read still reads it
            Assert.Equal("b1e10ec6", plainId);
            Assert.False(AwayNote.Decode("id|NaN|level", out _, out _, out _));
        }
    }
}
