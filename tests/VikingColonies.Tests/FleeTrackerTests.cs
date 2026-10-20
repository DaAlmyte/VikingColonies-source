using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    public class FleeTrackerTests
    {
        private const float None = float.PositiveInfinity;
        private const double Never = double.NegativeInfinity;

        [Fact]
        public void A_monster_within_12_m_starts_fleeing()
        {
            Assert.True(new FleeTracker().Update(11f, Never, 0));
        }

        [Fact]
        public void A_monster_at_15_m_does_not_start_fleeing()
        {
            Assert.False(new FleeTracker().Update(15f, Never, 0));
        }

        [Fact]
        public void A_hit_starts_fleeing_even_with_no_monster_in_sight()
        {
            Assert.True(new FleeTracker().Update(None, 99, 100));
        }

        [Fact]
        public void A_monster_lingering_between_12_and_20_m_keeps_them_sheltering()
        {
            var flee = new FleeTracker();
            flee.Update(5f, Never, 0);
            for (int t = 1; t <= 60; t++) Assert.True(flee.Update(15f, Never, t));
        }

        [Fact]
        public void Fleeing_ends_15_s_after_the_last_monster_left()
        {
            var flee = new FleeTracker();
            flee.Update(5f, Never, 0);
            flee.Update(18f, Never, 10);   // last seen within 20 m at t = 10
            Assert.True(flee.Update(None, Never, 24.9));
            Assert.False(flee.Update(None, Never, 25));
        }

        [Fact]
        public void After_a_hit_they_shelter_15_s_from_the_hit()
        {
            var flee = new FleeTracker();
            flee.Update(None, 100, 100);
            Assert.True(flee.Update(None, 100, 114.9));
            Assert.False(flee.Update(None, 100, 115));
        }

        [Fact]
        public void A_new_hit_while_sheltering_restarts_the_calm_time()
        {
            var flee = new FleeTracker();
            flee.Update(None, 100, 100);
            flee.Update(None, 110, 110);   // hit again
            Assert.True(flee.Update(None, 110, 124.9));
            Assert.False(flee.Update(None, 110, 125));
        }

        [Fact]
        public void Shelter_is_the_closer_of_town_hall_and_hut()
        {
            Shelter s = FleeTracker.ChooseShelter(new GroundPoint(0, 0), new GroundPoint(10, 0), new GroundPoint(5, 0), new GroundPoint(-3, 0));
            Assert.Equal(ShelterKind.Hut, s.Kind);
            Assert.Equal(5f, s.Point.X);
        }

        [Fact]
        public void With_only_a_town_hall_they_shelter_there()
        {
            Shelter s = FleeTracker.ChooseShelter(new GroundPoint(0, 0), new GroundPoint(10, 0), null, new GroundPoint(-3, 0));
            Assert.Equal(ShelterKind.TownHall, s.Kind);
        }

        [Fact]
        public void A_shelter_the_monster_is_nearer_to_is_skipped()
        {
            // The Town Hall is right by the monster: run to the hut instead of into it.
            Shelter s = FleeTracker.ChooseShelter(new GroundPoint(0, 0), new GroundPoint(10, 0), new GroundPoint(-10, 0), new GroundPoint(9, 0));
            Assert.Equal(ShelterKind.Hut, s.Kind);
        }

        [Fact]
        public void When_every_shelter_is_by_the_monster_they_run_away_instead()
        {
            Shelter s = FleeTracker.ChooseShelter(new GroundPoint(0, 0), new GroundPoint(10, 0), null, new GroundPoint(9, 0));
            Assert.Equal(ShelterKind.AwayFromThreat, s.Kind);
            Assert.Equal(-15f, s.Point.X, 3);
        }

        [Fact]
        public void Hit_by_something_unseen_they_go_to_the_nearest_shelter()
        {
            Shelter s = FleeTracker.ChooseShelter(new GroundPoint(0, 0), new GroundPoint(10, 0), new GroundPoint(5, 0), null);
            Assert.Equal(ShelterKind.Hut, s.Kind);
        }

        [Theory]
        [InlineData(false, false, false, 10f, true)]   // close by: a threat even if it hasn't noticed them
        [InlineData(false, false, false, 15f, false)]  // further away and minding its own business (e.g. penned)
        [InlineData(false, true, false, 18f, true)]    // alerted
        [InlineData(false, false, true, 18f, true)]    // hunting someone
        [InlineData(true, true, true, 5f, false)]      // asleep
        public void Only_monsters_that_are_close_or_hunting_count(bool sleeping, bool alerted, bool hasTarget, float distance, bool expected)
        {
            Assert.Equal(expected, FleeTracker.Counts(sleeping, alerted, hasTarget, distance));
        }

        [Theory]
        [InlineData(false, false, false, 5f, false)]   // passive enemies: one close by that leaves them alone is no threat
        [InlineData(false, true, false, 18f, true)]    // one that was stirred up still is
        [InlineData(false, false, true, 18f, true)]    // and one hunting someone
        public void With_passive_enemies_only_stirred_up_monsters_count(bool sleeping, bool alerted, bool hasTarget, float distance, bool expected)
        {
            Assert.Equal(expected, FleeTracker.Counts(sleeping, alerted, hasTarget, distance, passiveEnemies: true));
        }

        // 1.4.0's hands-on run: Necks from a pond kept hunting the colony; everyone hid, came out 15 s later, saw them again and
        // hid again (54 times in 15 minutes, nothing built). A weak creature counts only when it is on top of them.
        [Theory]
        [InlineData(false, false, true, 10f, false)]   // a Neck hunting someone, 10 m off: no reason to hide
        [InlineData(false, true, false, 18f, false)]   // alerted further off
        [InlineData(false, false, false, 5f, false)]   // close, minding its own business
        [InlineData(false, false, true, 2.5f, true)]   // right beside him: he steps away
        [InlineData(true, false, true, 2f, false)]     // asleep
        public void A_weak_creature_counts_only_when_it_is_right_beside_him(bool sleeping, bool alerted, bool hasTarget, float distance, bool expected)
        {
            Assert.Equal(expected, FleeTracker.Counts(sleeping, alerted, hasTarget, distance, weak: true));
        }

        [Fact]
        public void A_weak_one_with_two_stars_isnt_weak_any_more()
        {
            Assert.True(FleeTracker.IsWeak("Neck", level: 1));    // no star
            Assert.True(FleeTracker.IsWeak("Neck", level: 2));    // one star
            Assert.False(FleeTracker.IsWeak("Neck", level: 3));   // two stars
        }

        [Fact]
        public void Weak_is_a_named_few_not_low_health()
        {
            Assert.True(FleeTracker.IsWeak("Neck"));
            Assert.True(FleeTracker.IsWeak("Greyling"));
            Assert.True(FleeTracker.IsWeak("Boar"));
            Assert.False(FleeTracker.IsWeak("Greydwarf"));
            Assert.False(FleeTracker.IsWeak("Deathsquito"));   // little health, a deadly sting (the review of 1.4.1)
            Assert.False(FleeTracker.IsWeak("Tick"));
            Assert.False(FleeTracker.IsWeak(""));
            Assert.False(FleeTracker.IsWeak(null));
        }

        // The first 1.4.1 necks run: counted only from 6 m, a Neck after a settler came on, bit, and he came back as soon as it
        // stood off; four settlers died in a minute. One after him counts as any monster does (he runs early and keeps off);
        // the others go on working unless it is right beside them.
        [Theory]
        [InlineData(false, false, true, true, 5f, true)]     // a Neck hunting him, 5 m off
        [InlineData(false, false, true, true, 9f, true)]     // further off too: he runs early, as from any monster after him
        [InlineData(false, false, true, true, 18f, true)]    // and keeps off while it is about
        [InlineData(true, false, true, true, 5f, false)]     // asleep
        [InlineData(false, false, true, false, 10f, false)]  // hunting someone else: he works on
        [InlineData(false, false, true, false, 6f, true)]    // but keeps clear of the fight (the second necks run: one strolled
                                                             // back into Necks biting at the player and died)
        [InlineData(false, false, false, false, 6f, false)]  // one minding its own business that close: he works on
        [InlineData(false, false, true, false, 2.5f, true)]  // right beside him
        public void A_weak_creature_hunting_him_counts_as_any_monster_does(bool sleeping, bool alerted, bool hasTarget, bool targetsHim, float distance, bool expected)
        {
            Assert.Equal(expected, FleeTracker.Counts(sleeping, alerted, hasTarget, distance, weak: true, targetsHim: targetsHim));
        }

        [Fact]
        public void In_a_passive_world_a_calm_weak_creature_doesnt_count_even_close()
        {
            Assert.False(FleeTracker.Counts(false, false, false, 2f, passiveEnemies: true, weak: true));
            Assert.True(FleeTracker.Counts(false, true, false, 2f, passiveEnemies: true, weak: true));
        }

        [Fact]
        public void A_bite_from_a_weak_one_still_sends_him_off_and_he_calms_down_after()
        {
            var flee = new FleeTracker();
            Assert.True(flee.Update(float.PositiveInfinity, lastHitTime: 100.0, now: 101.0));   // bitten: off he goes
            Assert.True(flee.Update(float.PositiveInfinity, 100.0, 110.0));
            Assert.False(flee.Update(float.PositiveInfinity, 100.0, 100.0 + FleeTracker.CalmSeconds + 0.5));
        }

        [Fact]
        public void A_shelter_with_the_monster_at_it_is_no_shelter_he_runs_on()
        {
            // The third 1.4.1 necks run: Runa fled to the Town Hall (a camp with no walls), stood there, and the Neck that
            // followed her bit her to death; Signe the same at her unbuilt hut.
            Shelter atHall = FleeTracker.ChooseShelter(new GroundPoint(1, 0), new GroundPoint(1, 0), null, new GroundPoint(3, 0));
            Assert.Equal(ShelterKind.AwayFromThreat, atHall.Kind);
            Assert.Equal(-14f, atHall.Point.X, 3);
            Shelter atHut = FleeTracker.ChooseShelter(new GroundPoint(0, 1), null, new GroundPoint(0, 0), new GroundPoint(0, 4));
            Assert.Equal(ShelterKind.AwayFromThreat, atHut.Kind);
            // A monster further from the hall than he is: the hall still does.
            Shelter clear = FleeTracker.ChooseShelter(new GroundPoint(10, 0), new GroundPoint(0, 0), null, new GroundPoint(17, 0));
            Assert.Equal(ShelterKind.TownHall, clear.Kind);
        }

        [Fact]
        public void He_chooses_where_to_flee_again_when_bitten_or_when_it_reaches_his_shelter()
        {
            // The review of the game-check fixes: chosen again only when the monster had moved 5 m, a settler could stand at
            // the camp while the Neck that followed him bit and circled within those 5 m.
            Assert.True(FleeTracker.ChooseShelterAgain(haveShelter: false, threatMoved: 0f, bittenSince: false, threatToShelter: 30f));
            Assert.True(FleeTracker.ChooseShelterAgain(true, 6f, false, 30f));      // it moved a lot
            Assert.True(FleeTracker.ChooseShelterAgain(true, 1f, true, 30f));       // bitten since
            Assert.True(FleeTracker.ChooseShelterAgain(true, 1f, false, 3f));       // it is at his shelter
            Assert.False(FleeTracker.ChooseShelterAgain(true, 1f, false, 30f));     // the run-away spot stays put
        }

        [Fact]
        public void Without_colony_or_hut_they_run_15_m_away_from_the_threat()
        {
            Shelter s = FleeTracker.ChooseShelter(new GroundPoint(0, 0), null, null, new GroundPoint(3, 4));
            Assert.Equal(ShelterKind.AwayFromThreat, s.Kind);
            Assert.Equal(-9f, s.Point.X, 3);
            Assert.Equal(-12f, s.Point.Z, 3);
        }
    }
}
