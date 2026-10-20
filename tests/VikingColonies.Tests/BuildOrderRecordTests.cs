using System.Collections.Generic;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    public class BuildOrderRecordTests
    {
        private static BuildOrderRecord Order(int total = 5) => new BuildOrderRecord { Total = total, Created = 12.5 };

        [Fact]
        public void An_order_reads_back_the_same()
        {
            BuildOrderRecord order = Order();
            order.State = BuildState.Building;
            order.MarkDone(0);
            order.MarkDone(3);
            order.MarkFailed(2);
            order.TryClaim("bjorn", 100.25);
            order.Needs = "6 $item_finewood";
            BuildOrderRecord copy = BuildOrderRecord.Decode(order.Encode());
            Assert.Equal(BuildState.Building, copy.State);
            Assert.Equal(5, copy.Total);
            Assert.Equal(new[] { 0, 3 }, copy.Done);
            Assert.Equal(1, copy.Failures[2]);
            Assert.Equal("bjorn", copy.Builder);
            Assert.Equal(100.25, copy.ClaimTime);
            Assert.Equal(12.5, copy.Created);
            Assert.Equal("6 $item_finewood", copy.Needs);
        }

        [Fact]
        public void No_text_or_a_damaged_text_means_no_order()
        {
            Assert.Null(BuildOrderRecord.Decode(""));
            Assert.Null(BuildOrderRecord.Decode("garbage"));
        }

        [Fact]
        public void A_claimed_order_keeps_other_builders_away_for_a_minute()
        {
            BuildOrderRecord order = Order();
            Assert.True(order.TryClaim("bjorn", 0));
            Assert.False(order.TryClaim("ivar", 30));
            Assert.True(order.IsClaimedByOther("ivar", 30));
            Assert.True(order.TryClaim("bjorn", 30));   // renewing
            Assert.True(order.TryClaim("ivar", 30 + BuildOrderRecord.ClaimSeconds));
        }

        [Fact]
        public void A_claim_from_the_future_does_not_lock_the_order()
        {
            BuildOrderRecord order = Order();
            order.TryClaim("bjorn", 10000);
            Assert.True(order.TryClaim("ivar", 0));
        }

        [Fact]
        public void Only_the_holder_can_release_a_claim()
        {
            BuildOrderRecord order = Order();
            order.TryClaim("bjorn", 0);
            order.Release("ivar");
            Assert.Equal("bjorn", order.Builder);
            order.Release("bjorn");
            Assert.False(order.HasLiveClaim(1));
        }

        [Fact]
        public void A_piece_that_falls_three_times_is_skipped()
        {
            BuildOrderRecord order = Order();
            Assert.False(order.MarkFailed(1));
            Assert.False(order.MarkFailed(1));
            Assert.True(order.MarkFailed(1));
            Assert.True(order.IsSkipped(1));
            Assert.False(order.IsOpen(1));
            Assert.Equal(1, order.SkippedCount);
        }

        [Fact]
        public void A_piece_that_was_up_and_falls_goes_back_on_the_list()
        {
            BuildOrderRecord order = Order();
            order.MarkDone(2);
            order.MarkFailed(2);
            Assert.True(order.IsOpen(2));
            order.MarkDone(4);
            order.MarkGone(4);
            Assert.True(order.IsOpen(4));
        }

        [Fact]
        public void Everything_is_handled_when_each_piece_is_up_or_skipped()
        {
            BuildOrderRecord order = Order(3);
            order.MarkDone(0);
            order.MarkDone(1);
            Assert.False(order.AllHandled);
            for (int i = 0; i < BuildOrderRecord.MaxFailures; i++) order.MarkFailed(2);
            Assert.True(order.AllHandled);
        }

        [Fact]
        public void Finishing_drops_the_claim_and_the_needs()
        {
            BuildOrderRecord order = Order();
            order.TryClaim("bjorn", 0);
            order.Needs = "2 $item_stone";
            order.MarkBuilt();
            Assert.Equal(BuildState.Done, order.State);
            Assert.Equal("", order.Builder);
            Assert.Equal("", order.Needs);
        }

        [Fact]
        public void An_order_moves_from_waiting_through_clearing_and_shaping_to_building()
        {
            BuildOrderRecord order = Order();
            Assert.True(order.Advance(BuildState.Clearing));
            Assert.True(order.IsPreparing);
            Assert.True(order.Advance(BuildState.Shaping));
            Assert.True(order.Advance(BuildState.Building));
            Assert.False(order.IsPreparing);
            Assert.Equal(BuildState.Building, order.State);
        }

        [Fact]
        public void An_order_never_moves_back_or_straight_to_finished()
        {
            BuildOrderRecord order = Order();
            order.Advance(BuildState.Shaping);
            Assert.False(order.Advance(BuildState.Clearing));
            Assert.False(order.Advance(BuildState.Shaping));
            Assert.False(order.Advance(BuildState.Done));
            Assert.False(order.Advance(BuildState.Waiting));
            Assert.Equal(BuildState.Shaping, order.State);
        }

        [Fact]
        public void A_site_with_nothing_to_shape_goes_from_clearing_to_building()
        {
            BuildOrderRecord order = Order();
            order.Advance(BuildState.Clearing);
            Assert.True(order.Advance(BuildState.Building));
        }

        [Fact]
        public void The_new_states_and_the_ground_level_read_back()
        {
            BuildOrderRecord order = Order();
            order.Advance(BuildState.Shaping);
            order.Ground = 41.25f;
            BuildOrderRecord copy = BuildOrderRecord.Decode(order.Encode());
            Assert.Equal(BuildState.Shaping, copy.State);
            Assert.Equal(41.25f, copy.Ground);
            Assert.True(float.IsNaN(BuildOrderRecord.Decode(Order().Encode()).Ground));   // not known yet
        }

        [Fact]
        public void An_order_saved_before_site_preparation_keeps_building_and_has_no_ground_level()
        {
            string old = RecordCodec.Encode(new[]
            {
                new KeyValuePair<string, string>("v", "1"),
                new KeyValuePair<string, string>("state", "1"),
                new KeyValuePair<string, string>("total", "5"),
                new KeyValuePair<string, string>("done", "0,1"),
            });
            BuildOrderRecord order = BuildOrderRecord.Decode(old);
            Assert.Equal(BuildState.Building, order.State);
            Assert.True(float.IsNaN(order.Ground));
            Assert.False(order.Advance(BuildState.Clearing));   // it never goes back to clearing or shaping
            Assert.False(order.Advance(BuildState.Shaping));
        }

        [Fact]
        public void An_upgrade_order_reads_back_the_same()
        {
            BuildOrderRecord order = Order(3);
            order.Kind = OrderKind.Upgrade;
            order.TargetLevel = 3;
            order.TakeAwayTotal = 4;
            order.MarkTaken(1);
            order.MarkTaken(3);
            BuildOrderRecord copy = BuildOrderRecord.Decode(order.Encode());
            Assert.Equal(OrderKind.Upgrade, copy.Kind);
            Assert.Equal(3, copy.TargetLevel);
            Assert.Equal(4, copy.TakeAwayTotal);
            Assert.Equal(new[] { 1, 3 }, copy.TakenAway);
            Assert.Equal(2, copy.TakeAwayLeft);
        }

        [Fact]
        public void An_order_saved_before_upgrades_reads_as_a_level_1_build_with_nothing_to_take_away()
        {
            string old = RecordCodec.Encode(new[]
            {
                new KeyValuePair<string, string>("v", "1"),
                new KeyValuePair<string, string>("state", "2"),
                new KeyValuePair<string, string>("total", "2"),
                new KeyValuePair<string, string>("done", "0,1"),
            });
            BuildOrderRecord order = BuildOrderRecord.Decode(old);
            Assert.Equal(OrderKind.Build, order.Kind);
            Assert.Equal(1, order.TargetLevel);
            Assert.Equal(0, order.TakeAwayTotal);
            Assert.True(order.AllHandled);
        }

        [Fact]
        public void An_upgrade_is_not_handled_while_old_pieces_still_stand()
        {
            BuildOrderRecord order = Order(2);
            order.Kind = OrderKind.Upgrade;
            order.TakeAwayTotal = 2;
            order.MarkDone(0);
            order.MarkDone(1);
            order.MarkTaken(0);
            Assert.True(order.BuildHandled);
            Assert.False(order.AllHandled);
            order.MarkTaken(1);
            Assert.True(order.AllHandled);
        }

        [Fact]
        public void Taking_away_ignores_pieces_the_order_does_not_have()
        {
            BuildOrderRecord order = Order(2);
            order.TakeAwayTotal = 2;
            order.MarkTaken(-1);
            order.MarkTaken(5);
            Assert.Empty(order.TakenAway);
        }

        [Fact]
        public void An_upgrade_that_only_counted_its_kept_pieces_is_unchanged()
        {
            BuildOrderRecord order = Order(3);
            order.TakeAwayTotal = 2;
            order.MarkKept(0);
            Assert.Contains(0, order.Done);
            Assert.False(order.Changed);
        }

        [Fact]
        public void Putting_up_a_piece_or_taking_an_old_one_away_changes_an_upgrade()
        {
            BuildOrderRecord built = Order(3);
            built.MarkKept(0);
            built.MarkDone(1);
            Assert.True(built.Changed);

            BuildOrderRecord taken = Order(3);
            taken.TakeAwayTotal = 2;
            taken.MarkTaken(1);
            Assert.True(taken.Changed);
        }

        [Fact]
        public void Changed_is_saved_and_older_orders_read_as_unchanged()
        {
            BuildOrderRecord order = Order(3);
            order.MarkDone(2);
            Assert.True(BuildOrderRecord.Decode(order.Encode()).Changed);
            Assert.False(BuildOrderRecord.Decode(Order(3).Encode()).Changed);
            Assert.False(BuildOrderRecord.Decode("v=1|state=1|total=3|done=0").Changed);
        }

        [Fact]
        public void A_builder_taking_a_site_afresh_clears_what_was_reported_before()
        {
            // He left it waiting for a higher Builder's Hut and comes back (or another builder takes it): he looks again.
            var order = new BuildOrderRecord();
            order.TryClaim("bjorn", 0);
            order.Needs = "$vc_builder_hut_level 2 $vc_for $vc_thing_tree $vc_or_clear_yourself";
            order.Blocked = "$vc_thing_tree";
            order.Release("bjorn");
            Assert.True(order.TryClaim("bjorn", 300));
            Assert.Equal("", order.Needs);
            Assert.Equal("", order.Blocked);
        }

        [Fact]
        public void Another_builder_taking_over_a_lapsed_claim_clears_what_was_reported_before()
        {
            var order = new BuildOrderRecord();
            order.TryClaim("bjorn", 0);
            order.Needs = "2 $item_stone";
            Assert.True(order.TryClaim("ivar", BuildOrderRecord.ClaimSeconds + 1));
            Assert.Equal("", order.Needs);
        }

        [Fact]
        public void Renewing_a_claim_keeps_what_he_reported()
        {
            var order = new BuildOrderRecord();
            order.TryClaim("bjorn", 0);
            order.Needs = "2 $item_stone";
            order.Blocked = "$piece_woodwall";
            Assert.True(order.TryClaim("bjorn", 20));
            Assert.Equal("2 $item_stone", order.Needs);
            Assert.Equal("$piece_woodwall", order.Blocked);
        }
    }
}
