using System.IO;
using System.Collections.Generic;
using System.Linq;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    // Repair (0.13): idle builders mend their colony's damaged pieces; a finished hut whose pieces are gone has its order
    // reopened as a repair (docs/superpowers/specs/2026-10-15-vikingcolonies-repair-design.md).
    public class RepairTests
    {
        private static BuildOrderRecord Finished(int total, params int[] skipped)
        {
            var order = new BuildOrderRecord { Total = total, TargetLevel = 2, Created = 100 };
            for (int i = 0; i < total; i++)
                if (skipped.Contains(i)) order.MarkSkipped(i);
                else order.MarkDone(i);
            order.MarkBuilt();
            return order;
        }

        [Fact]
        public void A_piece_below_three_quarters_of_its_health_needs_mending()
        {
            Assert.True(RepairRules.NeedsMending(0.5f));
            Assert.True(RepairRules.NeedsMending(0.74f));
            Assert.False(RepairRules.NeedsMending(0.75f));
            Assert.False(RepairRules.NeedsMending(1f));
            Assert.False(RepairRules.NeedsMending(0f));   // gone already: nothing to mend
        }

        [Fact]
        public void The_missing_pieces_are_those_that_were_up_and_stand_no_more()
        {
            BuildOrderRecord order = Finished(5, 3);   // piece 3 was skipped: it never stood
            var standing = new HashSet<int> { 0, 2 };
            Assert.Equal(new[] { 1, 4 }, RepairRules.Missing(order, standing.Contains).ToArray());
            Assert.Empty(RepairRules.Missing(new BuildOrderRecord { Total = 5 }, standing.Contains));   // not finished: the builder sees to it
            Assert.Empty(RepairRules.Missing(null, standing.Contains));
        }

        [Fact]
        public void A_finished_order_reopens_as_a_repair_with_only_the_missing_pieces_open()
        {
            BuildOrderRecord order = Finished(5);
            order.First = 50;
            Assert.True(order.ReopenForRepair(new[] { 1, 4 }, now: 200));
            Assert.Equal(OrderKind.Repair, order.Kind);
            Assert.Equal(BuildState.Building, order.State);   // the site was cleared and shaped long ago
            Assert.Equal(2, order.TargetLevel);               // its level unchanged
            Assert.Equal(new[] { 1, 4 }, Enumerable.Range(0, 5).Where(order.IsOpen).ToArray());
            Assert.Equal(200, order.Created);
            Assert.Equal(0, order.First);
            Assert.False(order.Changed);

            BuildOrderRecord back = BuildOrderRecord.Decode(order.Encode());
            Assert.Equal(OrderKind.Repair, back.Kind);
            Assert.Equal(new[] { 1, 4 }, Enumerable.Range(0, 5).Where(back.IsOpen).ToArray());
        }

        [Fact]
        public void Only_a_finished_order_reopens_and_only_for_its_own_pieces()
        {
            var building = new BuildOrderRecord { Total = 5 };
            Assert.False(building.ReopenForRepair(new[] { 1 }, now: 200));
            Assert.Equal(OrderKind.Build, building.Kind);
            BuildOrderRecord done = Finished(5);
            Assert.False(done.ReopenForRepair(new int[0], now: 200));
            Assert.False(done.ReopenForRepair(new[] { 7, -1 }, now: 200));
            Assert.Equal(BuildState.Done, done.State);
        }

        [Fact]
        public void A_piece_that_keeps_falling_while_repaired_is_skipped_and_the_repair_ends()
        {
            BuildOrderRecord order = Finished(3);
            order.ReopenForRepair(new[] { 2 }, now: 200);
            for (int i = 0; i < BuildOrderRecord.MaxFailures; i++) order.MarkFailed(2);
            Assert.True(order.AllHandled);
            Assert.Empty(RepairRules.Missing(order, i => i != 2));   // skipped: not asked for again
        }

        [Fact]
        public void Leaving_a_repair_as_it_is_skips_its_open_pieces()
        {
            BuildOrderRecord order = Finished(4);
            order.ReopenForRepair(new[] { 0, 3 }, now: 200);
            order.MarkDone(0);
            Assert.Equal(1, order.LeaveAsItIs());
            Assert.True(order.AllHandled);
            Assert.True(order.IsSkipped(3));
            Assert.Equal(0, Finished(4).LeaveAsItIs());   // only a repair
        }

        [Fact]
        public void Pieces_are_put_back_only_when_on_no_raid_and_no_monsters()
        {
            Assert.True(RepairRules.MayRebuild(settingOn: true, raid: false, monstersNear: false));
            Assert.False(RepairRules.MayRebuild(settingOn: false, raid: false, monstersNear: false));
            Assert.False(RepairRules.MayRebuild(settingOn: true, raid: true, monstersNear: false));
            Assert.False(RepairRules.MayRebuild(settingOn: true, raid: false, monstersNear: true));
        }

        [Fact]   // the review round of 2026-10-23: an upgrade's row read "Lumberjack's Hut, (level 2)"
        public void A_work_orders_row_names_a_repair()
        {
            Assert.Equal("$vc_order_new", OrderWords.KindToken(OrderKind.Build));
            Assert.Equal("$vc_order_level", OrderWords.KindToken(OrderKind.Upgrade));
            Assert.Equal("$vc_order_repair", OrderWords.KindToken(OrderKind.Repair));
            DirectoryInfo? dir = new DirectoryInfo(System.AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "VikingColonies.slnx"))) dir = dir.Parent;
            string words = File.ReadAllText(Path.Combine(dir!.FullName, "src", "VikingColonies", "Config", "Translations.cs"));
            Assert.Contains("{ \"vc_order_level\", \"level {0}\" }", words);   // after a comma: no brackets
        }

        [Fact]
        public void A_piece_the_player_takes_away_is_left_out_not_put_back()
        {
            // Review of 0.13: a wall knocked out on purpose (a door to the player's house) was walled up again.
            BuildOrderRecord done = Finished(4);
            Assert.True(done.SkipRemoved(2));
            Assert.True(done.IsSkipped(2));
            Assert.Empty(RepairRules.Missing(done, i => i != 2));
            Assert.False(done.SkipRemoved(2));   // once

            BuildOrderRecord repair = Finished(4);
            repair.ReopenForRepair(new[] { 0 }, now: 200);
            Assert.True(repair.SkipRemoved(3));   // during a repair too

            var building = new BuildOrderRecord { Total = 4 };
            building.MarkDone(1);
            Assert.False(building.SkipRemoved(1));   // under construction: the builder puts it back, as before
            Assert.False(building.IsSkipped(1));
        }

        [Fact]
        public void Pieces_gone_before_repairs_began_are_left_out_once()
        {
            // Review of 0.13: a world from 0.12 with a piece taken away long ago had it rebuilt a minute after loading.
            BuildOrderRecord old = Finished(4);
            Assert.False(old.Watched);
            Assert.Equal(1, old.TakeBaseline(new[] { 1 }));
            Assert.True(old.Watched);
            Assert.True(old.IsSkipped(1));
            Assert.Empty(RepairRules.Missing(old, i => i != 1));
            Assert.Equal(BuildState.Done, old.State);
            Assert.True(BuildOrderRecord.Decode(old.Encode()).Watched);
            Assert.False(BuildOrderRecord.Decode("v=1|state=2|total=3|done=0,1,2").Watched);   // from before 0.13
        }

        [Fact]
        public void A_piece_that_keeps_falling_after_it_is_put_back_is_left_out()
        {
            // Review of 0.13: a piece that stood its first seconds and then fell was rebuilt every minute.
            BuildOrderRecord order = Finished(3);
            for (int i = 0; i < BuildOrderRecord.MaxRebuilds; i++)
            {
                Assert.True(order.ReopenForRepair(new[] { 1 }, now: 200 + i));
                order.MarkDone(1);
                order.MarkBuilt();
            }
            Assert.False(order.ReopenForRepair(new[] { 1 }, now: 300));
            Assert.True(order.IsSkipped(1));
            Assert.Equal(BuildState.Done, order.State);
        }

        [Fact]
        public void A_piece_broken_now_and_then_is_always_put_back()
        {
            // A wall a troll breaks every few days isn't one that can't stand: the count is of repairs close together.
            BuildOrderRecord order = Finished(3);
            for (int i = 0; i < BuildOrderRecord.MaxRebuilds + 2; i++)
            {
                Assert.True(order.ReopenForRepair(new[] { 1 }, now: 1000 + i * (BuildOrderRecord.RebuildMemorySeconds + 10)));
                order.MarkDone(1);
                order.MarkBuilt();
            }
            Assert.False(order.IsSkipped(1));
        }

        [Fact]
        public void The_rebuild_count_is_saved()
        {
            BuildOrderRecord order = Finished(3);
            order.ReopenForRepair(new[] { 2 }, now: 200);
            Assert.Equal(1, BuildOrderRecord.Decode(order.Encode()).Rebuilt[2]);
        }

        [Fact]
        public void Pieces_never_put_up_stay_out_of_a_repair()
        {
            // Self-test run 25 of 0.13: a hut marked built with pieces never put up (on a slope; or a half-built hut saved
            // as its design) reopened them all with the one broken piece.
            var order = new BuildOrderRecord { Total = 6 };
            order.MarkDone(0);
            order.MarkDone(1);
            order.MarkBuilt();   // pieces 2-5 never went up
            Assert.True(order.ReopenForRepair(new[] { 1 }, now: 200));
            Assert.Equal(new[] { 1 }, Enumerable.Range(0, 6).Where(order.IsOpen).ToArray());
            Assert.True(order.IsSkipped(4));
        }

        [Fact]
        public void The_damaged_piece_mended_first_is_the_nearest()
        {
            var pieces = new[] { new Vec3(10, 0, 0), new Vec3(3, 0, 4), new Vec3(-2, 0, 0) };
            Assert.Equal(2, RepairRules.Nearest(pieces, new Vec3(0, 0, 0)));
            Assert.Equal(-1, RepairRules.Nearest(new Vec3[0], new Vec3(0, 0, 0)));
        }

        // 1.5.0, repair's small polish (review 9): the worst-damaged building first, finished before the next.
        private static readonly List<Vec3> s_mendSpots = new List<Vec3> { new Vec3(2, 0, 0), new Vec3(3, 0, 0), new Vec3(40, 0, 0) };
        private static readonly List<string> s_mendSites = new List<string> { "A", "A", "B" };

        [Fact]
        public void The_worst_damaged_building_is_mended_first()
        {
            var health = new List<float> { 0.7f, 0.6f, 0.2f };
            Assert.Equal(2, RepairRules.MendNext(s_mendSpots, health, s_mendSites, new Vec3(0, 0, 0), current: ""));
        }

        [Fact]
        public void A_building_being_mended_is_finished_before_the_next()
        {
            var health = new List<float> { 0.7f, 0.6f, 0.1f };
            Assert.Equal(0, RepairRules.MendNext(s_mendSpots, health, s_mendSites, new Vec3(0, 0, 0), current: "A"));
            Assert.Equal(-1, RepairRules.MendNext(new List<Vec3>(), new List<float>(), new List<string>(), new Vec3(0, 0, 0), current: "A"));
        }

        [Fact]   // its review: rain wears uncovered wood down to half, below the mending line: only real damage counts
        public void Only_a_piece_mended_from_below_half_counts_for_the_saga()
        {
            Assert.False(RepairRules.CountsForSaga(0.6f));
            Assert.False(RepairRules.CountsForSaga(0.5f));   // the rain's floor
            Assert.True(RepairRules.CountsForSaga(0.2f));
        }

        [Fact]
        public void Big_repairs_go_in_the_saga_the_daily_wear_doesnt()
        {
            Assert.False(RepairRules.SagaWorthy(9));
            Assert.True(RepairRules.SagaWorthy(10));
            Assert.True(SagaLog.IsKnownKind("mended"));
        }
    }
}
