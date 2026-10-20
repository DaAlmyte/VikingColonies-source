using System.Collections.Generic;
using System.Linq;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    public class MoodTests
    {
        private static int Change(MoodInputs inputs) => Mood.Compute(inputs).Value - Mood.Start;

        private static MoodInputs Nothing() => new MoodInputs { BedComfort = MoodInputs.Unknown };

        [Fact]
        public void With_nothing_to_say_he_is_content_at_fifty()
        {
            MoodResult result = Mood.Compute(Nothing());
            Assert.Equal(50, result.Value);
            Assert.Equal(MoodLevel.Content, result.Level);
            Assert.Empty(result.Reasons);
        }

        [Fact]
        public void Each_reason_adds_or_takes_away_its_share()
        {
            var home = Nothing(); home.HasHome = true;
            Assert.Equal(5, Change(home));
            var noHome = Nothing(); noHome.HasHome = false; noHome.Homeless = true;
            Assert.Equal(-15, Change(noHome));
            var cosy = Nothing(); cosy.HasHome = true; cosy.BedComfort = 4;
            Assert.Equal(10, Change(cosy));
            var veryCosy = Nothing(); veryCosy.HasHome = true; veryCosy.BedComfort = 7;
            Assert.Equal(15, Change(veryCosy));
            var ate = Nothing(); ate.AteToday = true;
            Assert.Equal(5, Change(ate));
            var hungry = Nothing(); hungry.Hungry = true; hungry.AteToday = true;
            Assert.Equal(-20, Change(hungry));
            var varied = Nothing(); varied.DistinctFoods = 3;
            Assert.Equal(5, Change(varied));
            var veryVaried = Nothing(); veryVaried.DistinctFoods = 4;
            Assert.Equal(10, Change(veryVaried));
            var same = Nothing(); same.SameFoodRunning = true; same.DistinctFoods = 1;
            Assert.Equal(-5, Change(same));
            var evening = Nothing(); evening.EveningByFire = true;
            Assert.Equal(5, Change(evening));
            var company = Nothing(); company.EveningByFire = true; company.EveningCompany = true;
            Assert.Equal(10, Change(company));
            var lonelyCompany = Nothing(); lonelyCompany.EveningCompany = true;   // no fire: no company either
            Assert.Equal(0, Change(lonelyCompany));
            var calling = Nothing(); calling.InCalling = true;
            Assert.Equal(5, Change(calling));
            var jobless = Nothing(); jobless.JoblessDays = 2;
            Assert.Equal(-5, Change(jobless));
            var newlyJobless = Nothing(); newlyJobless.JoblessDays = 1;
            Assert.Equal(0, Change(newlyJobless));
            var death = Nothing(); death.Death = "Ingrid";
            Assert.Equal(-10, Change(death));
            var hit = Nothing(); hit.HitYesterday = true;
            Assert.Equal(-5, Change(hit));
            var repelled = Nothing(); repelled.RaidRepelled = true;
            Assert.Equal(5, Change(repelled));
            var cheerful = Nothing(); cheerful.Trait = Trait.Cheerful;
            Assert.Equal(10, Change(cheerful));
            var grumpy = Nothing(); grumpy.Trait = Trait.Grumpy;
            Assert.Equal(-10, Change(grumpy));
            var brave = Nothing(); brave.Trait = Trait.Brave;
            Assert.Equal(0, Change(brave));
        }

        [Fact]
        public void A_bed_of_unknown_comfort_or_no_home_gives_no_cosy_bed()
        {
            var unknown = Nothing(); unknown.HasHome = true; unknown.BedComfort = MoodInputs.Unknown;
            Assert.Equal(5, Change(unknown));
            var homeless = Nothing(); homeless.Homeless = true; homeless.BedComfort = 9;
            Assert.Equal(-15, Change(homeless));
        }

        [Fact]
        public void The_mood_stays_between_0_and_100()
        {
            var worst = new MoodInputs
            {
                Homeless = true, BedComfort = MoodInputs.Unknown, Hungry = true, AteToday = true, SameFoodRunning = true,
                JoblessDays = 9, Death = "Ingrid", HitYesterday = true, Trait = Trait.Grumpy,
            };
            Assert.Equal(0, Mood.Compute(worst).Value);
            var best = new MoodInputs
            {
                HasHome = true, BedComfort = 10, AteToday = true, DistinctFoods = 5, EveningByFire = true, EveningCompany = true,
                InCalling = true, RaidRepelled = true, Trait = Trait.Cheerful,
            };
            Assert.Equal(100, Mood.Compute(best).Value);
            Assert.Equal(MoodLevel.Joyful, Mood.Compute(best).Level);
        }

        [Fact]
        public void The_reasons_come_in_the_table_s_order_with_their_changes()
        {
            var inputs = new MoodInputs
            {
                HasHome = true, BedComfort = 5, AteToday = true, DistinctFoods = 3, EveningByFire = true, Trait = Trait.Cheerful,
                Death = "Ingrid",
            };
            MoodResult result = Mood.Compute(inputs);
            Assert.Equal(new[] { "$vc_mood_home", "$vc_mood_cosy", "$vc_mood_ate", "$vc_mood_varied", "$vc_mood_evening", "$vc_mood_death", "$vc_mood_cheerful" },
                result.Reasons.Select(r => r.Token).ToArray());
            Assert.Equal(new[] { 5, 5, 5, 5, 5, -10, 10 }, result.Reasons.Select(r => r.Change).ToArray());
            Assert.Equal("Ingrid", result.Reasons.Single(r => r.Token == "$vc_mood_death").Name);
            Assert.Equal(75, result.Value);
        }

        [Theory]
        [InlineData(100, MoodLevel.Joyful)]
        [InlineData(75, MoodLevel.Joyful)]
        [InlineData(74, MoodLevel.Content)]
        [InlineData(40, MoodLevel.Content)]
        [InlineData(39, MoodLevel.Unhappy)]
        [InlineData(20, MoodLevel.Unhappy)]
        [InlineData(19, MoodLevel.Miserable)]
        [InlineData(0, MoodLevel.Miserable)]
        public void The_levels(int value, MoodLevel level) => Assert.Equal(level, MoodLevels.Of(value));

        [Fact]
        public void Each_level_has_its_work_speed()
        {
            Assert.Equal(1.15f, MoodLevels.WorkFactor(MoodLevel.Joyful), 3);
            Assert.Equal(1f, MoodLevels.WorkFactor(MoodLevel.Content), 3);
            Assert.Equal(0.85f, MoodLevels.WorkFactor(MoodLevel.Unhappy), 3);
            Assert.Equal(0.7f, MoodLevels.WorkFactor(MoodLevel.Miserable), 3);
        }

        [Fact]
        public void An_unknown_mood_is_content()
        {
            Assert.Equal(MoodLevel.Content, MoodLevels.Of(Mood.Unknown));
            Assert.Equal(1f, MoodLevels.WorkFactor(MoodLevels.Of(Mood.Unknown)), 3);
        }

        [Fact]
        public void His_worst_reason_is_the_most_negative_one()
        {
            var reasons = new List<MoodReason>
            {
                new MoodReason("$vc_mood_home", 5), new MoodReason("$vc_mood_samefood", -5), new MoodReason("$vc_mood_hungry", -20),
                new MoodReason("$vc_mood_death", -10, "Ingrid"),
            };
            Assert.Equal("$vc_mood_hungry", MoodLevels.Worst(reasons)?.Token);
            Assert.Null(MoodLevels.Worst(new[] { new MoodReason("$vc_mood_home", 5) }));
            Assert.Null(MoodLevels.Worst(new MoodReason[0]));
        }

        [Fact]
        public void The_reasons_are_saved_and_read_back()
        {
            var reasons = new[] { new MoodReason("$vc_mood_home", 5), new MoodReason("$vc_mood_death", -10, "Ingrid") };
            string saved = MoodLevels.Encode(reasons);
            Assert.Equal("$vc_mood_home:5,$vc_mood_death:-10:Ingrid", saved);
            IReadOnlyList<MoodReason> back = MoodLevels.Decode(saved);
            Assert.Equal(2, back.Count);
            Assert.Equal("$vc_mood_death", back[1].Token);
            Assert.Equal(-10, back[1].Change);
            Assert.Equal("Ingrid", back[1].Name);
            Assert.Empty(MoodLevels.Decode(""));
            Assert.Empty(MoodLevels.Decode(null));
            Assert.Single(MoodLevels.Decode("$vc_mood_home:5,garbage,:3"));
        }

        [Fact]
        public void A_name_with_the_separators_in_it_does_not_break_the_reasons()
        {
            string saved = MoodLevels.Encode(new[] { new MoodReason("$vc_mood_death", -10, "Odd:name,here") });
            Assert.Equal("Odd name here", MoodLevels.Decode(saved)[0].Name);
        }

        [Fact]
        public void The_level_s_word()
        {
            Assert.Equal("$vc_mood_level_joyful", MoodLevels.Token(MoodLevel.Joyful));
            Assert.Equal("$vc_mood_level_miserable", MoodLevels.Token(MoodLevel.Miserable));
        }
    }

    public class ColonyMoodTests
    {
        [Fact]
        public void The_colony_s_mood_is_its_settlers_average()
        {
            Assert.Equal(50, ColonyMood.Of(new[] { 40, 60 }));
            Assert.Equal(67, ColonyMood.Of(new[] { 66, 67, 67 }));
        }

        [Fact]
        public void Settlers_not_worked_out_yet_and_an_empty_colony_count_as_content()
        {
            Assert.Equal(Mood.Start, ColonyMood.Of(new int[0]));
            Assert.Equal(Mood.Start, ColonyMood.Of(new[] { Mood.Unknown, Mood.Unknown }));
            Assert.Equal(30, ColonyMood.Of(new[] { 30, Mood.Unknown }));
        }
    }

    public class MoodAndNewcomersTests
    {
        private static readonly NewcomerRules Rules = new NewcomerRules(perDay: 1, foodEach: 5, maxSettlers: 30);

        private static ColonyNeeds Colony(MoodLevel mood) => new ColonyNeeds(settlers: 4, beds: 8, food: 20, lastArrivalDay: 1, today: 5, night: false, mood: mood);

        [Fact]
        public void Nobody_joins_an_unhappy_colony()
        {
            Assert.Equal(NewcomerWait.Mood, Newcomers.WaitsFor(Colony(MoodLevel.Unhappy), Rules));
            Assert.Equal(NewcomerWait.Mood, Newcomers.WaitsFor(Colony(MoodLevel.Miserable), Rules));
            Assert.Equal(0, Newcomers.HowMany(Colony(MoodLevel.Unhappy), Rules));
        }

        [Fact]
        public void A_content_colony_grows_as_before_and_a_joyful_one_gets_one_more()
        {
            Assert.Equal(NewcomerWait.None, Newcomers.WaitsFor(Colony(MoodLevel.Content), Rules));
            Assert.Equal(1, Newcomers.HowMany(Colony(MoodLevel.Content), Rules));
            Assert.Equal(2, Newcomers.HowMany(Colony(MoodLevel.Joyful), Rules));
        }

        [Fact]
        public void A_joyful_colony_still_needs_the_beds_and_the_food()
        {
            var oneBed = new ColonyNeeds(settlers: 4, beds: 5, food: 20, lastArrivalDay: 1, today: 5, night: false, mood: MoodLevel.Joyful);
            Assert.Equal(1, Newcomers.HowMany(oneBed, Rules));
            var foodForOne = new ColonyNeeds(settlers: 4, beds: 8, food: 7, lastArrivalDay: 1, today: 5, night: false, mood: MoodLevel.Joyful);
            Assert.Equal(1, Newcomers.HowMany(foodForOne, Rules));
            Assert.Equal(0, Newcomers.HowMany(Colony(MoodLevel.Joyful), new NewcomerRules(perDay: 0, foodEach: 5, maxSettlers: 30)));
        }

        [Fact]
        public void A_colony_without_a_mood_given_is_content()
        {
            var needs = new ColonyNeeds(settlers: 4, beds: 8, food: 20, lastArrivalDay: 1, today: 5, night: false);
            Assert.Equal(MoodLevel.Content, needs.Mood);
        }
    }

    public class VariedMealTests
    {
        private static readonly FoodStock[] Larder =
        {
            new FoodStock("Raspberry", 10, 1f), new FoodStock("Mushroom", 10, 2f), new FoodStock("CookedMeat", 10, 5f),
        };

        [Fact]
        public void He_eats_the_plainest_food_he_did_not_have_last_time()
        {
            Assert.Equal("Raspberry", Meals.Choose(Larder, null));
            Assert.Equal("Mushroom", Meals.Choose(Larder, new List<string> { "Raspberry" }));
            Assert.Equal("Raspberry", Meals.Choose(Larder, new List<string> { "Mushroom" }));
        }

        [Fact]
        public void With_only_one_kind_he_eats_it_again()
        {
            Assert.Equal("Raspberry", Meals.Choose(new[] { new FoodStock("Raspberry", 3, 1f) }, new List<string> { "Raspberry" }));
            Assert.Null(Meals.Choose(new FoodStock[0], new List<string> { "Raspberry" }));
        }

        [Fact]
        public void His_last_five_meals_are_remembered()
        {
            var meals = new List<string>();
            foreach (string m in new[] { "A", "B", "C", "D", "E", "F" }) MealMemory.Add(meals, m);
            Assert.Equal(new[] { "B", "C", "D", "E", "F" }, meals);
            Assert.Equal(5, MealMemory.Distinct(meals));
            Assert.False(MealMemory.SameRunning(meals));
            var same = new List<string> { "A", "A", "A", "A", "A" };
            Assert.True(MealMemory.SameRunning(same));
            Assert.Equal(1, MealMemory.Distinct(same));
            Assert.False(MealMemory.SameRunning(new List<string> { "A", "A", "A", "A" }));   // not five yet
            Assert.Null(MealMemory.Last(new List<string>()));
            Assert.Equal("F", MealMemory.Last(meals));
        }
    }

    public class MoodPaceTests
    {
        [Fact]
        public void Mood_multiplies_into_the_pace_and_the_floor_holds()
        {
            Assert.Equal(1.15f, WorkPace.Factor(false, 0.5f, false, 0.2f, Trait.Brave, 1.15f), 3);
            Assert.Equal(0.35f, WorkPace.Factor(true, 0.5f, false, 0.2f, Trait.Brave, 0.7f), 3);
            Assert.Equal(0.1f, WorkPace.Factor(true, 0.1f, false, 0f, Trait.Lazy, 0.7f), 3);
            Assert.Equal(1f, WorkPace.Factor(false, 0.5f, false, 0.2f, Trait.Brave), 3);
        }
    }
}
