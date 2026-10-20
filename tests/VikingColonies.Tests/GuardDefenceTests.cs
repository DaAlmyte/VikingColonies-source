using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>1.3: a tower's orders and where its guards may hold a post.</summary>
    public class GuardOrdersTests
    {
        [Fact]   // Review Focus
        public void A_post_only_within_the_towers_patrol_circle()
        {
            Assert.True(GuardPosts.MayPost(20f, 25f));
            Assert.True(GuardPosts.MayPost(25f, 25f));
            Assert.False(GuardPosts.MayPost(26f, 25f));
            Assert.Equal(0, (int)GuardOrders.Patrol);
            Assert.Equal(1, (int)GuardOrders.HoldPost);
        }

        [Fact]   // review of 1.3: at the alarm the guards of every tower stood on one spot
        public void At_the_alarm_each_guard_of_the_colony_has_his_own_spot()
        {
            GuardPosts.AlarmSpot(0, out float x0, out float z0);
            Assert.Equal(0f, x0, 3);
            Assert.Equal(0f, z0, 3);
            GuardPosts.AlarmSpot(1, out float x1, out float z1);
            Assert.Equal(GuardPosts.Spacing, (float)System.Math.Sqrt(x1 * x1 + z1 * z1), 3);   // the first ring
            var spots = new System.Collections.Generic.List<float[]>();
            for (int i = 0; i < 40; i++)
            {
                GuardPosts.AlarmSpot(i, out float x, out float z);
                foreach (float[] s in spots)
                    Assert.True(System.Math.Sqrt((s[0] - x) * (s[0] - x) + (s[1] - z) * (s[1] - z)) >= GuardPosts.Spacing * 0.95, $"spot {i} too near another");
                Assert.True(System.Math.Sqrt(x * x + z * z) <= GuardPosts.Spacing * 4f, $"spot {i} too far out");
                spots.Add(new[] { x, z });
            }
        }
    }

    /// <summary>1.3 runs: a shield held up between blows never keeps him from striking for long.</summary>
    public class ShieldTimingTests
    {
        [Fact]   // the fifth and seventh 1.3 runs: a Greyling striking at the player beside it kept his shield up for a minute
        public void A_shielded_guard_strikes_at_least_every_few_seconds()
        {
            Assert.True(ShieldGuard.Guarding(raised: true, sinceOwnBlow: 0.2f, guardLeft: 0f));    // just after his blow
            Assert.False(ShieldGuard.Guarding(raised: true, sinceOwnBlow: 1.0f, guardLeft: 0f));   // his turn
            Assert.True(ShieldGuard.Guarding(raised: true, sinceOwnBlow: 1.0f, guardLeft: 0.3f));  // the monster's blow coming
            Assert.False(ShieldGuard.Guarding(raised: true, sinceOwnBlow: ShieldGuard.SwingOverdue + 0.1f, guardLeft: 0.3f));   // overdue: he strikes
            Assert.False(ShieldGuard.Guarding(raised: false, sinceOwnBlow: 0.2f, guardLeft: 0.3f));   // lowered after three blocks
        }
    }

    /// <summary>1.3 review: the ballista errand and the give-up clock.</summary>
    public class GuardErrandTests
    {
        [Fact]   // review of 1.3: 2 m from his post the errand turned into "back to the post"
        public void A_ballista_errand_holds_until_done_below_a_fight()
        {
            var s = new GuardSituation { HasWeapon = true, Loading = true, AtPost = false };
            Assert.Equal(GuardAction.Loading, GuardBrain.Decide(s));
            s.PatrolDue = true;
            Assert.Equal(GuardAction.Loading, GuardBrain.Decide(s));   // the round waits
            s.HasTarget = true;
            Assert.Equal(GuardAction.Fight, GuardBrain.Decide(s));
            s.Retreating = true;
            Assert.Equal(GuardAction.Retreat, GuardBrain.Decide(s));
            Assert.Equal(GuardAction.ReturnToPost, GuardBrain.Decide(new GuardSituation { HasWeapon = true }));
        }

        [Fact]   // review of 1.3: the clock never ran (reset each look round); a far call needs the walk added
        public void The_give_up_clock_allows_for_the_walk_there()
        {
            Assert.Equal(GuardRules.GiveUpSeconds, GuardRules.GiveUpAfter(0f), 3);
            Assert.Equal(GuardRules.GiveUpSeconds, GuardRules.GiveUpAfter(-5f), 3);
            Assert.Equal(GuardRules.GiveUpSeconds + 80f / GuardRules.ApproachSpeed, GuardRules.GiveUpAfter(80f), 3);
            Assert.True(GuardRules.GiveUpAfter(80f) > 30f);
        }
    }

    /// <summary>1.3: guards answer a call for help inside the colony's area, within the help radius.</summary>
    public class GuardHelpTests
    {
        [Fact]
        public void The_help_radius_is_the_setting_or_twice_the_patrol()
        {
            Assert.Equal(80f, GuardRules.HelpRadius(25f, 80f), 3);
            Assert.Equal(90f, GuardRules.HelpRadius(45f, 80f), 3);
        }

        [Fact]   // Review Focus
        public void A_call_is_answered_inside_the_area_within_the_help_radius()
        {
            // Within his watch as before.
            Assert.True(GuardRules.Answers(20f, 25f, 10f, false, attackingColony: false, insideArea: true, helpRadius: 80f));
            // Past his watch: only a monster attacking someone of the colony, inside its area, within the help radius.
            Assert.True(GuardRules.Answers(60f, 25f, 2f, false, attackingColony: true, insideArea: true, helpRadius: 80f));
            Assert.False(GuardRules.Answers(60f, 25f, 2f, false, attackingColony: false, insideArea: true, helpRadius: 80f));
            Assert.False(GuardRules.Answers(60f, 25f, 2f, false, attackingColony: true, insideArea: false, helpRadius: 80f));
            Assert.False(GuardRules.Answers(81f, 25f, 2f, false, attackingColony: true, insideArea: true, helpRadius: 80f));
        }

        [Fact]   // Review Focus
        public void The_leash_of_a_call_is_its_radius_and_the_chase()
        {
            Assert.False(GuardRules.LetGoFrom(85f, 80f, 10f));
            Assert.True(GuardRules.LetGoFrom(91f, 80f, 10f));
            Assert.True(GuardRules.LetGoFrom(88f, 80f, 10f, speed: 7f));   // where he would stop
            Assert.Equal(GuardRules.LetGo(36f, 25f, 10f, raid: false), GuardRules.LetGoFrom(36f, 25f, 10f));
        }

        [Fact]   // deferred from 1.2's review: only running away from the middle carries him further out
        public void Only_the_speed_away_from_the_middle_counts_for_the_leash()
        {
            Assert.Equal(7f, GuardRules.OutwardSpeed(10f, 0f, 7f, 0f), 3);    // straight out
            Assert.Equal(0f, GuardRules.OutwardSpeed(10f, 0f, -7f, 0f), 3);   // coming back
            Assert.Equal(0f, GuardRules.OutwardSpeed(10f, 0f, 0f, 7f), 3);    // round the edge
            Assert.Equal(5f, GuardRules.OutwardSpeed(0f, 20f, 0f, 5f), 3);
            Assert.Equal(0f, GuardRules.OutwardSpeed(0f, 0f, 7f, 0f), 3);     // on the middle: no way is out
        }
    }

    /// <summary>1.3: the Town Hall's alarm.</summary>
    public class ColonyAlarmTests
    {
        [Fact]   // Review Focus
        public void It_sounds_five_minutes_unless_called_off()
        {
            double until = ColonyAlarm.Until(100);
            Assert.Equal(400, until, 3);
            Assert.True(ColonyAlarm.Active(until, 399));
            Assert.False(ColonyAlarm.Active(until, 400));
            Assert.False(ColonyAlarm.Active(0, 50));   // never sounded, or All clear
            Assert.Equal(30f, ColonyAlarm.Radius);
        }

        [Fact]   // 1.4's review: the alarm pulled guards away from every other fight
        public void While_it_sounds_a_guard_still_answers_calls_for_help()
        {
            // Near where it was sounded: any monster.
            Assert.Equal(AlarmAnswer.Alarm, ColonyAlarm.Answers(fromAlarm: 20f, fromTower: 90f, attackingColony: false, insideArea: true, helpRadius: 80f));
            // Elsewhere: one attacking someone of the colony inside its area, within his help radius of the tower.
            Assert.Equal(AlarmAnswer.Help, ColonyAlarm.Answers(fromAlarm: 70f, fromTower: 40f, attackingColony: true, insideArea: true, helpRadius: 80f));
            Assert.Equal(AlarmAnswer.None, ColonyAlarm.Answers(fromAlarm: 70f, fromTower: 40f, attackingColony: false, insideArea: true, helpRadius: 80f));
            Assert.Equal(AlarmAnswer.None, ColonyAlarm.Answers(fromAlarm: 70f, fromTower: 40f, attackingColony: true, insideArea: false, helpRadius: 80f));
            Assert.Equal(AlarmAnswer.None, ColonyAlarm.Answers(fromAlarm: 70f, fromTower: 81f, attackingColony: true, insideArea: true, helpRadius: 80f));
            // Both: the alarm's area holds him (it is where the guards gather).
            Assert.Equal(AlarmAnswer.Alarm, ColonyAlarm.Answers(fromAlarm: 10f, fromTower: 40f, attackingColony: true, insideArea: true, helpRadius: 80f));
        }
    }

    /// <summary>1.3: where raiders come from, in words (z is north).</summary>
    public class CompassTests
    {
        [Theory]
        [InlineData(0f, 10f, "$vc_dir_n")]
        [InlineData(10f, 10f, "$vc_dir_ne")]
        [InlineData(10f, 0f, "$vc_dir_e")]
        [InlineData(10f, -10f, "$vc_dir_se")]
        [InlineData(0f, -10f, "$vc_dir_s")]
        [InlineData(-10f, -10f, "$vc_dir_sw")]
        [InlineData(-10f, 0f, "$vc_dir_w")]
        [InlineData(-10f, 10f, "$vc_dir_nw")]
        [InlineData(1f, 10f, "$vc_dir_n")]
        public void Token(float dx, float dz, string token) => Assert.Equal(token, Compass.Token(dx, dz));
    }

    /// <summary>1.3: a ballista below half gets what the chests can spare, never more than it holds.</summary>
    public class BallistaTests
    {
        [Fact]   // Review Focus
        public void Loaded_below_half_with_what_can_be_spared()
        {
            Assert.Equal(0, BallistaRules.ToLoad(30, 50, 100));   // half or more: left alone
            Assert.Equal(50, BallistaRules.ToLoad(0, 50, 100));
            Assert.Equal(20, BallistaRules.ToLoad(10, 50, 20));   // all the chests can spare
            Assert.Equal(0, BallistaRules.ToLoad(10, 50, 0));
            Assert.Equal(0, BallistaRules.ToLoad(0, 0, 10));
        }
    }

    /// <summary>1.3: a guard's tally.</summary>
    public class KillsRecordTests
    {
        [Fact]
        public void Kills_round_trip_and_arent_written_unset()
        {
            var r = new CitizenRecord { Id = "a", Name = "Ulf", Kills = 7 };
            Assert.Equal(7, CitizenRecord.Decode(r.Encode()).Kills);
            Assert.DoesNotContain("kills", new CitizenRecord { Id = "a", Name = "Ivar" }.Encode());
            Assert.Equal("vc_guard_orders", SaveKeys.GuardOrders);
            Assert.Equal("vc_guard_post", SaveKeys.GuardPost);
            Assert.Equal("vc_alarm_until", SaveKeys.AlarmUntil);
            Assert.Equal("vc_alarm_at", SaveKeys.AlarmAt);
        }
    }
}
