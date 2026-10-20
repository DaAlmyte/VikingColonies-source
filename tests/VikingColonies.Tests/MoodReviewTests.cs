using System.Collections.Generic;
using System.Linq;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>The 0.17 review's fixes: fed through the night, varied food that can be earned, raids joined late, staggered moods.</summary>
    public class MoodReviewTests
    {
        [Fact]
        public void He_counts_as_fed_from_his_meal_until_the_next_one_is_due()
        {
            // Review I1: "ate today" dropped off at midnight, hours before breakfast.
            Assert.True(Meals.Fed(lastMealDay: 7, hungry: false, today: 7, eating: true));
            Assert.True(Meals.Fed(lastMealDay: 7, hungry: false, today: 8, eating: true));   // the night after, before breakfast
            Assert.False(Meals.Fed(lastMealDay: 6, hungry: false, today: 8, eating: true));
            Assert.False(Meals.Fed(lastMealDay: 8, hungry: true, today: 8, eating: true));
            Assert.True(Meals.Fed(Meals.NeverAte, hungry: false, today: 8, eating: true));   // a new settler comes fed
            Assert.True(Meals.Fed(lastMealDay: 2, hungry: true, today: 8, eating: false));   // meals off: nobody goes without
            Assert.False(Meals.Fed(lastMealDay: 9, hungry: false, today: 8, eating: true));   // a later day: the clock was put back
        }

        private static List<string> Eat(FoodStock[] larder, int meals)
        {
            var eaten = new List<string>();
            for (int i = 0; i < meals; i++) MealMemory.Add(eaten, Meals.Choose(larder, eaten));
            return eaten;
        }

        private static FoodStock Food(string item, float value) => new FoodStock(item, 50, value);

        [Fact]
        public void A_well_stocked_colony_eats_varied_food_enough_to_earn_it()
        {
            // Review I2: avoiding only the last meal, he alternated the two plainest foods for ever.
            FoodStock[] four = { Food("Raspberry", 1f), Food("Mushroom", 2f), Food("Blueberries", 3f), Food("CookedMeat", 5f), Food("Honey", 6f) };
            List<string> eaten = Eat(four, 5);
            Assert.Equal(4, MealMemory.Distinct(eaten));
            Assert.Equal(new[] { "Raspberry", "Mushroom", "Blueberries", "CookedMeat", "Raspberry" }, eaten);   // plainest first; the best last
            FoodStock[] three = { Food("Raspberry", 1f), Food("Mushroom", 2f), Food("CookedMeat", 5f) };
            Assert.Equal(3, MealMemory.Distinct(Eat(three, 5)));
            FoodStock[] two = { Food("Raspberry", 1f), Food("Mushroom", 2f) };
            Assert.Equal(new[] { "Raspberry", "Mushroom", "Raspberry", "Mushroom", "Raspberry" }, Eat(two, 5));
            FoodStock[] one = { Food("Raspberry", 1f) };
            Assert.True(MealMemory.SameRunning(Eat(one, 5)));
        }

        [Fact]
        public void Of_foods_he_has_all_had_lately_he_eats_the_one_he_had_longest_ago()
        {
            FoodStock[] two = { Food("Raspberry", 1f), Food("Mushroom", 2f) };
            Assert.Equal("Mushroom", Meals.Choose(two, new List<string> { "Mushroom", "Raspberry" }));
            Assert.Equal("Raspberry", Meals.Choose(two, new List<string> { "Raspberry", "Mushroom" }));
            Assert.Equal("Raspberry", Meals.Choose(two, new List<string>()));
            Assert.Equal("Raspberry", Meals.Choose(two, null));
            Assert.Null(Meals.Choose(new FoodStock[0], new List<string> { "Raspberry" }));
        }

        [Fact]
        public void A_raid_watched_from_its_start_and_ended_with_nobody_lost_was_held_off()
        {
            var watch = new RaidWatch();
            var saga = new SagaLog();
            saga.Add(new SagaEntry(3, "died", "Old"));
            Assert.False(watch.Update(false, 5, saga));   // the watch begins in peace
            Assert.False(watch.Update(true, 5, saga));
            Assert.False(watch.Update(true, 5, saga));
            Assert.True(watch.Update(false, 5, saga));
            Assert.False(watch.Update(false, 5, saga));   // once
        }

        [Fact]
        public void A_raid_with_a_death_was_not_held_off()
        {
            var watch = new RaidWatch();
            var saga = new SagaLog();
            watch.Update(false, 5, saga);
            watch.Update(true, 5, saga);
            saga.Add(new SagaEntry(5, "died", "Ingrid"));
            Assert.False(watch.Update(false, 6, saga));   // over the next day, still counted from its start
        }

        [Fact]
        public void A_raid_already_on_when_the_watch_began_counts_for_nothing()
        {
            // Review I3: a reload or a new owner mid-raid took deaths before it as the starting count.
            var watch = new RaidWatch();
            var saga = new SagaLog();
            saga.Add(new SagaEntry(5, "died", "Ingrid"));
            Assert.False(watch.Update(true, 5, saga));
            Assert.False(watch.Update(false, 5, saga));
            watch.Restart();   // a new owner while a second raid is on
            Assert.False(watch.Update(false, 5, saga));
            Assert.False(watch.Update(true, 6, saga));
            Assert.True(watch.Update(false, 6, saga));   // that one it saw begin
            watch.Restart();
            Assert.False(watch.Update(true, 7, saga));
            Assert.False(watch.Update(false, 7, saga));
        }

        [Fact]
        public void Each_settler_works_out_his_mood_at_his_own_moment()
        {
            // Review M4: every settler started his clock on the same frame at load. And not at once: his bed and his
            // "came fed" are in place a few seconds after he loads (review I1).
            var delays = new[] { "a1", "b2", "c3", "d4", "e5", "f6" }.Select(MoodClock.FirstDelay).ToList();
            Assert.All(delays, d => Assert.InRange(d, MoodClock.Settle, MoodClock.Settle + 60f));
            Assert.True(delays.Distinct().Count() > 3);
            Assert.Equal(MoodClock.FirstDelay("a1"), MoodClock.FirstDelay("a1"));
        }
    }
}
