using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    public class ColonyRulesTests
    {
        private static GroundPoint P(float x, float z) => new GroundPoint(x, z);

        [Fact]
        public void A_hut_joins_the_nearest_town_hall_in_range()
        {
            var halls = new[] { new HallInfo("far", P(40, 0)), new HallInfo("near", P(10, 0)), new HallInfo("out", P(80, 0)) };
            Assert.Equal("near", ColonyRules.ChooseColony(P(0, 0), halls, 50f));
        }

        [Fact]
        public void No_town_hall_in_range_means_no_colony()
        {
            Assert.Null(ColonyRules.ChooseColony(P(0, 0), new[] { new HallInfo("out", P(60, 0)) }, 50f));
            Assert.Null(ColonyRules.ChooseColony(P(0, 0), new HallInfo[0], 50f));
        }

        [Fact]
        public void Exactly_at_the_radius_counts_and_ties_pick_the_smaller_id()
        {
            var halls = new[] { new HallInfo("b", P(50, 0)), new HallInfo("a", P(-50, 0)) };
            Assert.Equal("a", ColonyRules.ChooseColony(P(0, 0), halls, 50f));
        }

        [Theory]
        [InlineData(4, 0, 4)]
        [InlineData(4, 1, 3)]
        [InlineData(4, 6, 0)]
        [InlineData(15, 0, 10)]
        [InlineData(-2, 0, 0)]
        [InlineData(4, -1, 4)]
        public void Top_up_spawns_the_missing_starting_citizens(int configured, int members, int expected)
        {
            Assert.Equal(expected, ColonyRules.CitizensToSpawn(configured, members));
        }

        [Fact]
        public void Only_colony_less_citizens_in_range_are_adopted()
        {
            Assert.True(ColonyRules.IsAdoptable("", P(10, 0), P(0, 0), 50f));
            Assert.False(ColonyRules.IsAdoptable("other", P(10, 0), P(0, 0), 50f));
            Assert.False(ColonyRules.IsAdoptable("", P(70, 0), P(0, 0), 50f));
        }

        [Fact]
        public void Pick_takes_the_nearest_jobless_member()
        {
            var citizens = new[]
            {
                new CitizenInfo("far", "col", "", P(30, 0)),
                new CitizenInfo("near", "col", "", P(5, 0)),
            };
            Assert.Equal("near", ColonyRules.PickWorker(P(0, 0), "col", citizens));
        }

        // Review focus 4
        [Fact]
        public void Pick_never_takes_someone_who_already_works()
        {
            var citizens = new[]
            {
                new CitizenInfo("busy", "col", "otherHut", P(1, 0)),
                new CitizenInfo("free", "col", "", P(20, 0)),
            };
            Assert.Equal("free", ColonyRules.PickWorker(P(0, 0), "col", citizens));
            Assert.Null(ColonyRules.PickWorker(P(0, 0), "col", new[] { citizens[0] }));
        }

        [Fact]
        public void Pick_ignores_other_colonies_and_needs_a_colony()
        {
            var citizens = new[] { new CitizenInfo("stranger", "other", "", P(1, 0)) };
            Assert.Null(ColonyRules.PickWorker(P(0, 0), "col", citizens));
            Assert.Null(ColonyRules.PickWorker(P(0, 0), "", new[] { new CitizenInfo("x", "", "", P(1, 0)) }));
        }

        [Fact]
        public void Pick_ties_go_to_the_smaller_id()
        {
            var citizens = new[] { new CitizenInfo("b", "col", "", P(5, 0)), new CitizenInfo("a", "col", "", P(-5, 0)) };
            Assert.Equal("a", ColonyRules.PickWorker(P(0, 0), "col", citizens));
        }

        [Theory]
        [InlineData(true, true, Presence.Present)]
        [InlineData(true, false, Presence.Present)]
        [InlineData(false, true, Presence.Gone)]
        [InlineData(false, false, Presence.Unknown)]
        public void Presence_is_gone_only_when_its_area_is_loaded(bool found, bool areaLoaded, Presence expected)
        {
            Assert.Equal(expected, ColonyRules.PresenceOf(found, areaLoaded));
        }

        // Review focus 1
        [Fact]
        public void Gone_needs_three_observations_in_a_row()
        {
            var tracker = new GoneTracker(3);
            Assert.False(tracker.Observe(Presence.Gone));
            Assert.False(tracker.Observe(Presence.Gone));
            Assert.True(tracker.Observe(Presence.Gone));
        }

        // Review focus 1
        [Fact]
        public void Unknown_or_present_resets_the_count()
        {
            var tracker = new GoneTracker(3);
            tracker.Observe(Presence.Gone);
            tracker.Observe(Presence.Gone);
            Assert.False(tracker.Observe(Presence.Unknown));
            Assert.False(tracker.Observe(Presence.Gone));
            Assert.False(tracker.Observe(Presence.Present));
            Assert.False(tracker.Observe(Presence.Gone));
            Assert.False(tracker.Observe(Presence.Gone));
            Assert.True(tracker.Observe(Presence.Gone));
        }

        [Theory]
        [InlineData(2f, 15)]
        [InlineData(5f, 6)]
        [InlineData(7f, 5)]
        [InlineData(0f, 30)]
        public void Gone_needs_thirty_seconds_of_absence_whatever_the_check_interval(float interval, int checks)
        {
            // A player joining a big base receives objects over many seconds; a few checks would be fooled.
            Assert.Equal(checks, ColonyRules.GoneChecksFor(interval));
        }

        [Fact]
        public void A_known_town_hall_spot_is_where_absence_is_checked()
        {
            float[] hall = { 10f, 0f, 20f };
            Assert.Same(hall, ColonyRules.CheckPoint(hall, new[] { 1f, 2f, 3f }));
        }

        [Fact]
        public void Without_a_known_town_hall_spot_the_citizens_own_spot_is_checked()
        {
            // v1 settlers never saved their hall's position; they always stayed close to it.
            float[] own = { 1f, 2f, 3f };
            Assert.Same(own, ColonyRules.CheckPoint(null, own));
            Assert.Same(own, ColonyRules.CheckPoint(new[] { 1f }, own));
        }

        [Theory]
        [InlineData(50f, 44f)]
        [InlineData(20f, 14f)]
        [InlineData(8f, 5f)]
        public void Huts_can_only_be_placed_where_the_whole_reach_stays_inside_the_colony(float colonyRadius, float expected)
        {
            // The game checks the player's spot, and a piece can be placed up to 5 m beyond it.
            Assert.Equal(expected, ColonyRules.HutBuildRange(colonyRadius));
        }
    }
}
