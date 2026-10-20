using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    // Seen (2026-10-15): a builder boxed in between his hut's old walls and the new ones round them tried for an hour to
    // get to his site. Other colony mods have a last resort for this (MineColonies moves stuck citizens; Hearthfolk's
    // "no longer permanently stuck"): a settler who keeps trying to walk without getting out of a small patch of ground
    // is moved to a free spot by the Town Hall.
    public class TrappedWatchTests
    {
        private static void WalkInPlace(TrappedWatch watch, double from, double to, float x = 10f, float z = 10f)
        {
            for (double t = from; t <= to; t += 0.5) watch.Walking(x + (float)(t % 2) * 0.3f, z, t);   // hops and small steps
        }

        [Fact]
        public void Walking_in_one_patch_for_two_minutes_with_nobody_near_moves_him()
        {
            var watch = new TrappedWatch();
            WalkInPlace(watch, 0, TrappedWatch.MoveAfter - 1);
            Assert.False(watch.ShouldMove(nearestPlayer: 100f, indoors: false, now: 1000));
            WalkInPlace(watch, TrappedWatch.MoveAfter - 0.5, TrappedWatch.MoveAfter + 1);
            Assert.True(watch.ShouldMove(nearestPlayer: 100f, indoors: false, now: 1000));
        }

        [Fact]
        public void With_a_player_near_enough_to_see_it_he_is_moved_only_much_later()
        {
            var watch = new TrappedWatch();
            WalkInPlace(watch, 0, TrappedWatch.MoveAfter + 1);
            Assert.False(watch.ShouldMove(nearestPlayer: 10f, indoors: false, now: 1000));
            WalkInPlace(watch, TrappedWatch.MoveAfter + 1.5, TrappedWatch.MoveAnywayAfter + 1);
            Assert.True(watch.ShouldMove(nearestPlayer: 10f, indoors: false, now: 1000));
        }

        [Fact]
        public void Getting_out_of_the_patch_starts_the_clock_again()
        {
            var watch = new TrappedWatch();
            WalkInPlace(watch, 0, 100);
            watch.Walking(10f + TrappedWatch.Patch + 0.5f, 10f, 100.5);   // he got somewhere
            WalkInPlace(watch, 101, 200, x: 10f + TrappedWatch.Patch + 0.5f);
            Assert.False(watch.ShouldMove(nearestPlayer: 100f, indoors: false, now: 1000));
        }

        [Fact]
        public void Arriving_starts_the_clock_again()
        {
            var watch = new TrappedWatch();
            WalkInPlace(watch, 0, 100);
            watch.Arrived(10f, 10f);
            WalkInPlace(watch, 100.5, 200);
            Assert.False(watch.ShouldMove(nearestPlayer: 100f, indoors: false, now: 1000));
        }

        [Fact]
        public void Only_time_spent_trying_to_walk_counts_not_waiting_or_working_in_between()
        {
            // A builder who waited ten minutes in his hut for materials isn't trapped the moment he sets off.
            var watch = new TrappedWatch();
            WalkInPlace(watch, 0, 60);
            WalkInPlace(watch, 660, 700);   // ten minutes idle in between
            Assert.True(watch.WalkedFor < 105);
            Assert.False(watch.ShouldMove(nearestPlayer: 100f, indoors: false, now: 1000));
        }

        [Fact]
        public void After_being_moved_the_clock_starts_again()
        {
            var watch = new TrappedWatch();
            WalkInPlace(watch, 0, TrappedWatch.MoveAfter + 1);
            watch.Moved(50f, 50f, TrappedWatch.MoveAfter + 1);
            Assert.Equal(0, watch.WalkedFor);
            Assert.False(watch.ShouldMove(nearestPlayer: 100f, indoors: false, now: 1000));
        }

        [Fact]
        public void Standing_at_the_end_of_his_way_on_purpose_is_not_trying_to_walk()
        {
            // Seen in review: a lumberjack with a full chest waits by his hut at the end of his way, his walk "not there"
            // for minutes on end. Only following a way he can't get along counts (he hops, steps aside).
            Assert.False(TrappedWatch.CountsAsTrying(wayEnded: true, toGoal: 12f));
            Assert.True(TrappedWatch.CountsAsTrying(wayEnded: false, toGoal: 12f));
        }

        [Fact]
        public void Jostling_among_others_by_his_goal_is_not_trying_to_walk()
        {
            Assert.False(TrappedWatch.CountsAsTrying(wayEnded: false, toGoal: Unstuck.NearGoal - 0.1f));
        }

        [Fact]
        public void Nobody_is_moved_out_at_night_or_in_a_raid()
        {
            var watch = new TrappedWatch();
            WalkInPlace(watch, 0, TrappedWatch.MoveAnywayAfter + 1);
            Assert.False(watch.ShouldMove(nearestPlayer: 100f, indoors: true, now: 1000));
        }

        [Fact]
        public void Not_due_before_two_minutes_of_trying()
        {
            var watch = new TrappedWatch();
            WalkInPlace(watch, 0, TrappedWatch.MoveAfter - 1);
            Assert.False(watch.Due);
        }

        [Fact]
        public void At_most_two_moves_in_ten_minutes()
        {
            // A goal he can never reach would otherwise have him moved every two minutes.
            var watch = new TrappedWatch();
            double t = 0;
            for (int i = 0; i < 2; i++)
            {
                WalkInPlace(watch, t, t + TrappedWatch.MoveAfter + 1);
                t += TrappedWatch.MoveAfter + 2;
                Assert.True(watch.ShouldMove(nearestPlayer: 100f, indoors: false, now: t));
                watch.Moved(10f, 10f, t);
            }
            WalkInPlace(watch, t, t + TrappedWatch.MoveAfter + 1);
            t += TrappedWatch.MoveAfter + 2;
            Assert.False(watch.ShouldMove(nearestPlayer: 100f, indoors: false, now: t));
            Assert.True(watch.ShouldMove(nearestPlayer: 100f, indoors: false, now: t + TrappedWatch.RescueWindow));
        }
        // Worker polish (0.20): a farmer wedged where she spent the night inside a turned Farm didn't move for minutes
        // (hops and side-steps didn't help); inside a hut nobody sees him moved, so he goes to just inside its door soon.
        [Fact]
        public void Wedged_inside_a_hut_he_is_moved_after_30_seconds()
        {
            var watch = new TrappedWatch();
            WalkInPlace(watch, 0, TrappedWatch.InsideHutAfter - 1);
            Assert.False(watch.ShouldMoveInsideHut(now: 1000));
            WalkInPlace(watch, TrappedWatch.InsideHutAfter - 0.5, TrappedWatch.InsideHutAfter + 1);
            Assert.True(watch.ShouldMoveInsideHut(now: 1000));
            watch.Moved(10f, 10f, 1000);
            WalkInPlace(watch, 1001, 1001 + TrappedWatch.InsideHutAfter + 1);
            Assert.True(watch.ShouldMoveInsideHut(now: 1040));
            watch.Moved(10f, 10f, 1040);
            WalkInPlace(watch, 1041, 1041 + TrappedWatch.InsideHutAfter + 1);
            Assert.False(watch.ShouldMoveInsideHut(now: 1080));   // twice in ten minutes already
        }
    
        // The backstop (1.4.0's old-huts upgrade test, the player: "simple fail safes like teleporting the settler if they
        // have been stuck for x amount of time"): a builder stood 25 minutes in one spot of a hut being rebuilt, trying a
        // door 53 m off every few seconds. Going through doors and ways that end short don't count as trying to walk, so the
        // last resort never came. Wanting to be somewhere else counts whatever he does on the way.
        private static void WantInPlace(TrappedWatch watch, double from, double to, float x = 10f, float z = 10f)
        {
            for (double t = from; t <= to; t += 0.5) watch.Wanting(x + (float)(t % 2) * 0.3f, z, t);
        }

        [Fact]
        public void Wanting_to_be_elsewhere_for_four_minutes_in_one_patch_moves_him_whatever_he_does_on_the_way()
        {
            var watch = new TrappedWatch();
            WantInPlace(watch, 0, TrappedWatch.BackstopAfter - 1);
            Assert.False(watch.Due);
            Assert.False(watch.ShouldMove(nearestPlayer: 100f, indoors: false, now: 1000));
            WantInPlace(watch, TrappedWatch.BackstopAfter - 0.5, TrappedWatch.BackstopAfter + 1);
            Assert.True(watch.Due);
            Assert.True(watch.ShouldMove(nearestPlayer: 100f, indoors: false, now: 1000));
        }

        [Fact]
        public void The_backstop_waits_longer_when_a_player_could_see_it()
        {
            var watch = new TrappedWatch();
            WantInPlace(watch, 0, TrappedWatch.BackstopAfter + 1);
            Assert.False(watch.ShouldMove(nearestPlayer: 10f, indoors: false, now: 1000));
            WantInPlace(watch, TrappedWatch.BackstopAfter + 1.5, TrappedWatch.MoveAnywayAfter + 1);
            Assert.True(watch.ShouldMove(nearestPlayer: 10f, indoors: false, now: 1000));
            Assert.True(watch.Overdue);
        }

        [Fact]   // the rerun: a builder walked in and out of her hut's door, 8 m apart, for 40 minutes, wanting a site 50 m off
        public void Roaming_round_a_small_place_without_getting_out_counts_for_the_backstop()
        {
            var watch = new TrappedWatch();
            for (double t = 0; t <= TrappedWatch.BackstopAfter + 1; t += 0.5)
                watch.Wanting(10f + (float)(t % 20 < 10 ? t % 10 : 10 - t % 10) * 0.8f, 10f, t);   // to and fro over 8 m
            Assert.True(watch.Due);
            Assert.True(watch.ShouldMove(nearestPlayer: 100f, indoors: false, now: 1000));
        }

        [Fact]
        public void Walking_out_of_the_small_patch_doesnt_restart_the_backstop()
        {
            // Both clocks run while he walks to and fro: the walking clock's 4 m patch must not wipe the backstop's count.
            var watch = new TrappedWatch();
            for (double t = 0; t <= TrappedWatch.BackstopAfter + 1; t += 0.5)
            {
                float x = 10f + (float)(t % 20 < 10 ? t % 10 : 10 - t % 10) * 0.8f;
                watch.Walking(x, 10f, t);
                watch.Wanting(x, 10f, t);
            }
            Assert.True(watch.Due);
        }

        [Fact]   // the third rerun: a guard whose post couldn't be reached stood by the Town Hall and was "moved" 3.6 m
        public void Nobody_is_moved_to_a_spot_he_already_stands_by()
        {
            Assert.False(TrappedWatch.WorthMoving(fromX: 1296.5f, fromZ: 793.8f, toX: 1300f, toZ: 793f));
            Assert.True(TrappedWatch.WorthMoving(fromX: 1272f, fromZ: 818f, toX: 1300f, toZ: 793f));
            Assert.True(TrappedWatch.WorthMoving(0f, 0f, TrappedWatch.BackstopPatch + 0.5f, 0f));
        }

        [Fact]
        public void Getting_out_of_the_backstops_wider_patch_restarts_it()
        {
            var watch = new TrappedWatch();
            WantInPlace(watch, 0, 200);
            watch.Wanting(10f + TrappedWatch.BackstopPatch + 0.5f, 10f, 200.5);
            WantInPlace(watch, 201, 400, x: 10f + TrappedWatch.BackstopPatch + 0.5f);
            Assert.False(watch.Due);
        }

        [Fact]
        public void The_backstop_restarts_when_he_gets_somewhere_or_arrives()
        {
            var watch = new TrappedWatch();
            WantInPlace(watch, 0, 200);
            watch.Wanting(10f + TrappedWatch.BackstopPatch + 0.5f, 10f, 200.5);   // he got out of the patch
            WantInPlace(watch, 201, 400, x: 10f + TrappedWatch.BackstopPatch + 0.5f);
            Assert.False(watch.Due);
            watch.Arrived(10f, 10f);
            WantInPlace(watch, 400.5, 600);
            Assert.False(watch.Due);
        }

        [Fact]
        public void Waiting_or_working_between_wants_isnt_counted()
        {
            // A builder waiting for materials in his hut doesn't want to be elsewhere: only the time spent wanting counts.
            var watch = new TrappedWatch();
            for (double t = 0; t < 10 * 30; t += 30) WantInPlace(watch, t, t + 5);   // 5 minutes go by, 10 wants of 5 s
            Assert.InRange(watch.WantedFor, 45, 55);
            Assert.False(watch.Due);
        }

        [Fact]
        public void Night_raids_and_the_move_limit_hold_for_the_backstop_too()
        {
            var watch = new TrappedWatch();
            WantInPlace(watch, 0, TrappedWatch.MoveAnywayAfter + 1);
            Assert.False(watch.ShouldMove(nearestPlayer: 100f, indoors: true, now: 1000));
            watch.Moved(10f, 10f, 1000);
            WantInPlace(watch, 1001, 1001 + TrappedWatch.MoveAnywayAfter + 1);
            watch.Moved(10f, 10f, 1302);
            WantInPlace(watch, 1303, 1303 + TrappedWatch.MoveAnywayAfter + 1);
            Assert.False(watch.ShouldMove(nearestPlayer: 100f, indoors: false, now: 1590));   // twice in ten minutes already
        }

        [Fact]
        public void The_backstop_doesnt_move_him_to_a_hut_door_only_to_the_town_hall()
        {
            // A door that doesn't get him out is what the backstop is for: moving him back behind it wouldn't help.
            var watch = new TrappedWatch();
            WantInPlace(watch, 0, TrappedWatch.BackstopAfter + 1);
            Assert.False(watch.ShouldMoveInsideHut(now: 1000));
        }
    }
}
