using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    // People and growth, part 2: settlers eat once a day from the colony's chests.
    public class MealsTests
    {
        [Fact]
        public void He_eats_the_plainest_food_and_leaves_the_best_for_the_player()
        {
            var stock = new[] { new FoodStock("CookedMeat", 10, 30f), new FoodStock("Raspberry", 3, 7f), new FoodStock("Honey", 5, 8f) };
            Assert.Equal("Raspberry", Meals.Choose(stock));
        }

        [Fact]
        public void Of_two_equally_plain_foods_he_eats_the_one_there_is_most_of()
        {
            var stock = new[] { new FoodStock("Blueberries", 2, 8f), new FoodStock("Honey", 9, 8f) };
            Assert.Equal("Honey", Meals.Choose(stock));
        }

        [Fact]
        public void Nothing_to_eat_is_no_meal()
        {
            Assert.Null(Meals.Choose(new FoodStock[0]));
            Assert.Null(Meals.Choose(new[] { new FoodStock("Raspberry", 0, 7f), new FoodStock("Resin", 4, 0f) }));
        }

        [Fact]
        public void A_meal_a_day_at_dawn()
        {
            Assert.True(Meals.Due(lastMealDay: 3, today: 4, night: false));
            Assert.False(Meals.Due(lastMealDay: 4, today: 4, night: false));
            Assert.False(Meals.Due(lastMealDay: 3, today: 4, night: true));   // the day has turned but it's still night
        }

        [Fact]
        public void A_clock_put_back_doesnt_keep_him_from_eating()
        {
            // A day-length mod or a time skip backwards: his last meal looks like a day still to come.
            Assert.True(Meals.Due(lastMealDay: 9, today: 4, night: false));
        }

        [Fact]
        public void A_settler_who_never_ate_counts_as_fed_on_his_first_day()
        {
            // A new colony's settlers, and settlers from before meals, aren't hungry before the player could stock food.
            Assert.False(Meals.Due(lastMealDay: Meals.NeverAte, today: 7, night: false));
            Assert.True(Meals.FirstDay(Meals.NeverAte));
            Assert.False(Meals.FirstDay(6));
        }

        [Fact]
        public void A_hungry_settler_works_slower()
        {
            Assert.Equal(1f, Meals.WorkSpeed(hungry: false, hungrySpeed: 0.5f));
            Assert.Equal(0.5f, Meals.WorkSpeed(hungry: true, hungrySpeed: 0.5f));
            Assert.Equal(0.1f, Meals.WorkSpeed(hungry: true, hungrySpeed: 0f));   // never quite still
        }
    
        // 1.4.2, a player's report: food put in after the meal was missed wasn't eaten until the next day ("Doesn't this
        // Town Hall have any food to eat?"). A hungry settler looks again during the day, now and then.
        [Fact]
        public void A_hungry_settler_looks_again_for_food_the_same_day()
        {
            Assert.True(Meals.LooksAgain(hungry: true, lastMealDay: 5, today: 5, night: false, sinceLooked: Meals.HungryLookSeconds));
            Assert.False(Meals.LooksAgain(hungry: true, lastMealDay: 5, today: 5, night: false, sinceLooked: Meals.HungryLookSeconds - 1f));
        }

        // The review of 1.4.2: a settler loaded again (the player away and back) "served at the table" never looked again.
        [Fact]
        public void A_look_again_goes_ahead_even_while_breakfast_says_it_serves_the_meal()
        {
            Assert.True(Meals.ShouldLook(due: false, again: true, atTable: true));
            Assert.True(Meals.ShouldLook(due: true, again: false, atTable: false));
            Assert.False(Meals.ShouldLook(due: true, again: false, atTable: true));   // breakfast serves today's meal
            Assert.False(Meals.ShouldLook(due: false, again: false, atTable: false));
        }

        [Fact]
        public void A_fed_settler_or_a_night_or_another_day_is_no_look_again()
        {
            Assert.False(Meals.LooksAgain(hungry: false, lastMealDay: 5, today: 5, night: false, sinceLooked: 999f));
            Assert.False(Meals.LooksAgain(hungry: true, lastMealDay: 5, today: 5, night: true, sinceLooked: 999f));
            Assert.False(Meals.LooksAgain(hungry: true, lastMealDay: 4, today: 5, night: false, sinceLooked: 999f));   // that's a meal due
        }
    }
}
