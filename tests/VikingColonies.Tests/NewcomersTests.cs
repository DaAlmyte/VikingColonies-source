using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    // People and growth, part 3: while the colony has a free bed and food in the Town Hall, a newcomer arrives each day.
    public class NewcomersTests
    {
        private static ColonyNeeds Colony(int settlers = 4, int beds = 6, int food = 20, int lastArrival = Newcomers.NeverCame,
                                          int today = 10, bool night = false) =>
            new ColonyNeeds(settlers, beds, food, lastArrival, today, night);

        private static readonly NewcomerRules Rules = new NewcomerRules(perDay: 1, foodEach: 5, maxSettlers: 30);

        [Fact]
        public void With_a_free_bed_and_food_one_comes()
        {
            Assert.Equal(1, Newcomers.HowMany(Colony(), Rules));
        }

        [Fact]
        public void Without_a_free_bed_nobody_comes()
        {
            Assert.Equal(0, Newcomers.HowMany(Colony(settlers: 6, beds: 6), Rules));
            Assert.Equal(0, Newcomers.HowMany(Colony(settlers: 4, beds: 0), Rules));
        }

        [Fact]
        public void Without_enough_food_in_the_Town_Hall_nobody_comes()
        {
            Assert.Equal(0, Newcomers.HowMany(Colony(food: 4), Rules));
            Assert.Equal(1, Newcomers.HowMany(Colony(food: 5), Rules));
        }

        [Fact]
        public void Once_a_day_and_not_at_night()
        {
            Assert.Equal(0, Newcomers.HowMany(Colony(lastArrival: 10, today: 10), Rules));
            Assert.Equal(1, Newcomers.HowMany(Colony(lastArrival: 9, today: 10), Rules));
            Assert.Equal(0, Newcomers.HowMany(Colony(night: true), Rules));
        }

        [Fact]
        public void A_clock_put_back_doesnt_stop_newcomers()
        {
            Assert.Equal(1, Newcomers.HowMany(Colony(lastArrival: 12, today: 10), Rules));
        }

        [Fact]
        public void More_a_day_when_set_but_never_more_than_beds_food_or_the_cap_allow()
        {
            var three = new NewcomerRules(perDay: 3, foodEach: 5, maxSettlers: 30);
            Assert.Equal(3, Newcomers.HowMany(Colony(settlers: 2, beds: 10, food: 50), three));
            Assert.Equal(2, Newcomers.HowMany(Colony(settlers: 8, beds: 10, food: 50), three));    // two free beds
            Assert.Equal(2, Newcomers.HowMany(Colony(settlers: 2, beds: 10, food: 12), three));    // food for two
            Assert.Equal(1, Newcomers.HowMany(Colony(settlers: 2, beds: 10, food: 50), new NewcomerRules(3, 5, maxSettlers: 3)));
        }

        [Fact]
        public void No_newcomers_when_switched_off()
        {
            Assert.Equal(0, Newcomers.HowMany(Colony(), new NewcomerRules(perDay: 0, foodEach: 5, maxSettlers: 30)));
        }

        [Fact]
        public void Free_food_when_a_newcomer_needs_none()
        {
            Assert.Equal(1, Newcomers.HowMany(Colony(food: 0), new NewcomerRules(perDay: 1, foodEach: 0, maxSettlers: 30)));
        }

        [Fact]
        public void The_window_says_what_the_next_newcomer_waits_for()
        {
            Assert.Equal(NewcomerWait.None, Newcomers.WaitsFor(Colony(), Rules));
            Assert.Equal(NewcomerWait.Bed, Newcomers.WaitsFor(Colony(settlers: 6, beds: 6), Rules));
            Assert.Equal(NewcomerWait.Food, Newcomers.WaitsFor(Colony(food: 1), Rules));
            Assert.Equal(NewcomerWait.Full, Newcomers.WaitsFor(Colony(settlers: 30, beds: 40), Rules));
            Assert.Equal(NewcomerWait.Tomorrow, Newcomers.WaitsFor(Colony(lastArrival: 10, today: 10), Rules));
            Assert.Equal(NewcomerWait.Morning, Newcomers.WaitsFor(Colony(night: true), Rules));
            Assert.Equal(NewcomerWait.Off, Newcomers.WaitsFor(Colony(), new NewcomerRules(0, 5, 30)));
        }
    }
}
