using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    // How a settler gets on with getting into his refuge for the night (or a raid): on his spot he is in; within its walls
    // but kept off his spot he stays where he is after a while; he never gives up a hut he is inside; outside, he gives it
    // up after trying for a while. Seen: two settlers each on the other's sleeping spot left their own Longhouse.
    public class RefugeProgressTests
    {
        private const float FarFromDoor = 4f;

        private static RefugeStep Inside(RefugeProgress p, double now, float toSpot = 3f, float fromDoor = FarFromDoor, int passes = 0) =>
            p.Step(onSpot: false, withinWalls: true, fromDoor: fromDoor, toSpot: toSpot, doorPasses: passes, doorBusy: false, now: now);

        private static RefugeStep Outside(RefugeProgress p, double now, int passes = 0, bool doorBusy = false) =>
            p.Step(onSpot: false, withinWalls: false, fromDoor: 10f, toSpot: 12f, doorPasses: passes, doorBusy: doorBusy, now: now);

        [Fact]
        public void On_his_spot_he_is_in()
        {
            var p = new RefugeProgress();
            p.Restart(0);
            Assert.True(p.Step(onSpot: true, withinWalls: false, fromDoor: 4f, toSpot: 0.2f, doorPasses: 0, doorBusy: false, now: 1).In);
        }

        [Fact]
        public void Kept_off_his_spot_inside_he_stays_where_he_is_after_a_while()
        {
            var p = new RefugeProgress();
            p.Restart(0);
            Assert.False(Inside(p, 1).In);
            Assert.False(Inside(p, 1 + IndoorRules.SettleAnywhereSeconds - 0.1).In);
            RefugeStep settled = Inside(p, 1 + IndoorRules.SettleAnywhereSeconds);
            Assert.True(settled.In);
            Assert.True(settled.JustSettled);
            Assert.False(Inside(p, 20).JustSettled);   // said once
            Assert.True(p.Settled);
        }

        [Fact]
        public void Getting_closer_to_his_spot_starts_the_wait_again()
        {
            var p = new RefugeProgress();
            p.Restart(0);
            Inside(p, 0, toSpot: 5f);
            Inside(p, 6, toSpot: 5f - IndoorRules.SpotProgress - 0.01f);   // he got on
            Assert.False(Inside(p, 6 + IndoorRules.SettleAnywhereSeconds - 0.1, toSpot: 4.6f).In);
            Assert.True(Inside(p, 6 + IndoorRules.SettleAnywhereSeconds, toSpot: 4.6f).In);
        }

        [Fact]
        public void He_never_settles_by_the_door()
        {
            // Seen in review: settled 1.6 m in, he kept the door open all night and stood where the next one steps in.
            var p = new RefugeProgress();
            p.Restart(0);
            for (double t = 0; t < 60; t += 0.5)
                Assert.False(Inside(p, t, fromDoor: IndoorRules.SettleDoorClearance - 0.1f).In);
        }

        [Fact]
        public void Pushed_out_of_the_walls_he_no_longer_counts_as_settled()
        {
            var p = new RefugeProgress();
            p.Restart(0);
            Inside(p, 0);
            Inside(p, IndoorRules.SettleAnywhereSeconds);
            Assert.True(p.Settled);
            Assert.False(Outside(p, 20).In);
            Assert.False(p.Settled);
        }

        [Fact]
        public void Inside_he_never_gives_the_hut_up()
        {
            var p = new RefugeProgress();
            p.Restart(0);
            for (double t = 0; t < 600; t += 1)
                Assert.False(Inside(p, t, fromDoor: 1f).GivesUp);   // even in the doorway, where he doesn't settle
        }

        [Fact]
        public void Outside_he_gives_it_up_after_trying_for_a_while_but_not_while_at_its_door()
        {
            var p = new RefugeProgress();
            p.Restart(0);
            Assert.False(Outside(p, IndoorRules.GiveUpSeconds).GivesUp);
            Assert.False(Outside(p, IndoorRules.GiveUpSeconds + 1, doorBusy: true).GivesUp);
            Assert.True(Outside(p, IndoorRules.GiveUpSeconds + 1).GivesUp);
        }

        [Fact]
        public void Coming_through_a_door_starts_the_give_up_clock_again()
        {
            // Seen: a forager who took long to reach the door gave the Longhouse up the moment he stepped in.
            var p = new RefugeProgress();
            p.Restart(0);
            Outside(p, 40);
            Assert.False(Outside(p, 50, passes: 1).GivesUp);
            Assert.False(Outside(p, 50 + IndoorRules.GiveUpSeconds, passes: 1).GivesUp);
            Assert.True(Outside(p, 51 + IndoorRules.GiveUpSeconds, passes: 1).GivesUp);
        }

        [Fact]
        public void While_he_finishes_his_work_first_the_give_up_clock_waits()
        {
            var p = new RefugeProgress();
            p.Restart(0);
            p.Wait(100);   // the lumberjack delivered his load till then
            Assert.False(Outside(p, 100 + IndoorRules.GiveUpSeconds).GivesUp);
        }

        [Fact]
        public void A_new_refuge_starts_everything_again()
        {
            var p = new RefugeProgress();
            p.Restart(0);
            Inside(p, 0);
            Inside(p, IndoorRules.SettleAnywhereSeconds);
            p.Restart(30);
            Assert.False(p.Settled);
            Assert.False(Outside(p, 30 + IndoorRules.GiveUpSeconds).GivesUp);
        }
    }
}
