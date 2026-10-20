using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    public class SwingTrackerTests
    {
        // Review I2: once direct damage is on, trees owned by another computer must still get hit.
        [Fact]
        public void Direct_damage_also_hits_trees_owned_elsewhere()
        {
            var tracker = new SwingTracker();
            SwingOutcome outcome = tracker.OnSwingFinished(targetDamaged: false, targetIsMine: false, directDamageOn: true);
            Assert.True(outcome.ApplyDirectHit);
        }

        // Review I2: a tree that never takes damage must not be swung at forever.
        [Fact]
        public void Gives_up_after_many_swings_without_damage()
        {
            var tracker = new SwingTracker();
            SwingOutcome outcome = default;
            for (int i = 1; i < SwingTracker.SwingsBeforeGivingUp; i++)
            {
                outcome = tracker.OnSwingFinished(false, false, false);
                Assert.False(outcome.GiveUp);
            }
            outcome = tracker.OnSwingFinished(false, false, false);
            Assert.True(outcome.GiveUp);
        }

        [Fact]
        public void Own_tree_not_damaged_several_times_turns_on_direct_damage()
        {
            var tracker = new SwingTracker();
            for (int i = 1; i < SwingTracker.SwingsBeforeDirectDamage; i++)
                Assert.False(tracker.OnSwingFinished(false, true, false).EnableDirectDamage);
            SwingOutcome outcome = tracker.OnSwingFinished(false, true, false);
            Assert.True(outcome.EnableDirectDamage);
            Assert.True(outcome.ApplyDirectHit);
        }

        [Fact]
        public void Trees_owned_elsewhere_never_turn_direct_damage_on()
        {
            var tracker = new SwingTracker();
            for (int i = 0; i < SwingTracker.SwingsBeforeGivingUp; i++)
                Assert.False(tracker.OnSwingFinished(false, false, false).EnableDirectDamage);
        }

        [Fact]
        public void Damage_resets_the_count()
        {
            var tracker = new SwingTracker();
            for (int i = 1; i < SwingTracker.SwingsBeforeGivingUp; i++) tracker.OnSwingFinished(false, false, false);
            Assert.False(tracker.OnSwingFinished(true, false, false).GiveUp);
            Assert.False(tracker.OnSwingFinished(false, false, false).GiveUp);
        }

        [Theory]
        [InlineData(true, false, true)]    // the stand-in is on and the swing missed: hit now, as the axe lands
        [InlineData(true, true, false)]    // the real swing hit: nothing to add
        [InlineData(false, false, false)]  // the stand-in is off: misses stay misses
        public void The_stand_in_hits_at_the_moment_the_axe_lands_only_when_the_swing_missed(bool directDamageOn, bool hitTarget, bool expected)
        {
            Assert.Equal(expected, SwingTracker.ShouldHitAtTrigger(directDamageOn, hitTarget));
        }

        // Review bug 4: chopping a stump, the real swing hit the tree beside it.
        [Theory]
        [InlineData(false, false, true, true)]   // it hit another tree: the stand-in hits the stump as the axe lands
        [InlineData(false, true, true, false)]   // it hit the stump too: nothing to add
        [InlineData(false, false, false, false)] // it hit nothing and the stand-in is off: a miss
        public void A_swing_that_hit_something_else_hits_the_target_as_the_axe_lands(bool directDamageOn, bool hitTarget, bool hitOther, bool expected)
        {
            Assert.Equal(expected, SwingTracker.ShouldHitAtTrigger(directDamageOn, hitTarget, hitOther));
        }

        [Fact]
        public void Swings_that_hit_something_else_never_switch_the_stand_in_on()
        {
            var tracker = new SwingTracker();
            for (int i = 0; i < SwingTracker.SwingsBeforeDirectDamage * 2; i++)
            {
                SwingOutcome outcome = tracker.OnSwingFinished(false, true, false, hitSomethingElse: true);
                Assert.False(outcome.EnableDirectDamage);
                Assert.True(outcome.ApplyDirectHit);   // he still gets the target down
            }
        }

        [Fact]
        public void Only_swings_that_hit_nothing_count_toward_switching_the_stand_in_on()
        {
            var tracker = new SwingTracker();
            for (int i = 1; i < SwingTracker.SwingsBeforeDirectDamage; i++)
                Assert.False(tracker.OnSwingFinished(false, true, false).EnableDirectDamage);
            Assert.False(tracker.OnSwingFinished(false, true, false, hitSomethingElse: true).EnableDirectDamage);
            Assert.True(tracker.OnSwingFinished(false, true, false).EnableDirectDamage);
        }
    }
}
