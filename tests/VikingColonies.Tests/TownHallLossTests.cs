using System.Collections.Generic;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    // Survival part 3: a troll that smashed the Town Hall chest ended the colony half a minute later.
    public class TownHallLossTests
    {
        private const double Day = ColonyGrace.DaySeconds;

        [Fact]
        public void Only_a_players_hit_counts_on_the_Town_Hall()
        {
            Assert.True(TownHallHits.Counts(attackerIsPlayer: true));
            Assert.False(TownHallHits.Counts(attackerIsPlayer: false));   // a monster, a raid, a thrown rock, no attacker
        }

        [Fact]
        public void The_colony_lives_on_for_the_grace_days()
        {
            Assert.False(ColonyGrace.Expired(lostAt: 1000, now: 1000 + Day, graceDays: 3));
            Assert.Equal(2, ColonyGrace.DaysLeft(1000, 1000 + Day, 3));
            Assert.Equal(1, ColonyGrace.DaysLeft(1000, 1000 + 2.5 * Day, 3));   // part of a day left shows as one
            Assert.True(ColonyGrace.Expired(1000, 1000 + 3 * Day, 3));
            Assert.Equal(0, ColonyGrace.DaysLeft(1000, 1000 + 3 * Day, 3));
        }

        // Review: some mods change the day's length; a grace day is the world's day.
        [Fact]
        public void A_grace_day_is_the_worlds_day()
        {
            Assert.True(ColonyGrace.Expired(0, 3 * 1200, graceDays: 3, daySeconds: 1200));
            Assert.Equal(1, ColonyGrace.DaysLeft(0, 2 * 1200, 3, daySeconds: 1200));
            var huts = new List<OrphanHut> { Hut("a", 10, 0) };
            Assert.Null(TownHallTakeover.Choose(huts, new GroundPoint(0f, 0f), 50f, now: 3 * 1200, graceDays: 3, daySeconds: 1200));
        }

        [Fact]
        public void No_grace_days_is_the_old_way()
        {
            Assert.True(ColonyGrace.Expired(1000, 1000, graceDays: 0));
        }

        private static OrphanHut Hut(string colony, float x, double lostAt) => new OrphanHut(colony, new GroundPoint(x, 0f), lostAt);

        [Fact]
        public void A_new_Town_Hall_takes_over_the_orphaned_colony_with_most_huts_in_range()
        {
            var huts = new List<OrphanHut> { Hut("a", 10, 0), Hut("b", 12, 0), Hut("b", 15, 0), Hut("c", 200, 0) };
            Assert.Equal("b", TownHallTakeover.Choose(huts, new GroundPoint(0f, 0f), radius: 50f, now: Day, graceDays: 3));
        }

        [Fact]
        public void A_colony_past_its_grace_or_out_of_range_is_not_taken_over()
        {
            var huts = new List<OrphanHut> { Hut("old", 10, 0), Hut("far", 80, 2 * Day) };
            Assert.Null(TownHallTakeover.Choose(huts, new GroundPoint(0f, 0f), 50f, now: 3 * Day, graceDays: 3));
        }

        [Fact]
        public void A_hut_that_never_lost_its_Town_Hall_is_not_an_orphan()
        {
            var huts = new List<OrphanHut> { Hut("living", 10, ColonyGrace.NotLost) };
            Assert.Null(TownHallTakeover.Choose(huts, new GroundPoint(0f, 0f), 50f, now: Day, graceDays: 3));
        }
    }
}
