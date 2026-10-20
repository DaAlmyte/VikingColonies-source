using System.Collections.Generic;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>Settler life part 4 (0.17): what mood saves, reads from the Saga, and its setting.</summary>
    public class MoodRecordTests
    {
        private const string Citizen013 = "v=2|id=abc|name=Ivar|model=0|hair=Hair3|beard=Beard2|skin=1,0.5,0.25|haircolor=0.1,0.2,0.3|colony=col1|" +
            "colonypos=1,2,3|work=hut1|workpos=4,5,6|job=lumberjack|carry=Wood:12|status=$vc_status_chopping|woodcutting=12,0.5|meal=7|hungry=1|" +
            "home=long1#3|homepos=7,8,9|halllost=|life=2";
        private const string Citizen016 = Citizen013 + "|calling=farmer|trait=cheerful|origin=$vc_origin_coast|arrived=12|arriving=1";
        private const string Citizen017 = Citizen016 + "|mood=62|whymood=$vc_mood_home:5,$vc_mood_death:-10:Ingrid|meals=Raspberry,Mushroom" +
            "|evening=11|company=1|hitday=10|joblesssince=9";

        [Fact]
        public void A_settler_written_by_0_17_keeps_his_mood_and_what_it_comes_from()
        {
            CitizenRecord r = CitizenRecord.Decode(Citizen017);
            Assert.Equal(62, r.Mood);
            Assert.Equal("$vc_mood_home:5,$vc_mood_death:-10:Ingrid", r.WhyMood);
            Assert.Equal(new[] { "Raspberry", "Mushroom" }, r.LastMeals);
            Assert.Equal(11, r.EveningDay);
            Assert.True(r.EveningCompany);
            Assert.Equal(10, r.HitDay);
            Assert.Equal(9, r.JoblessSince);
            Assert.Equal(Citizen017, r.Encode());
        }

        [Fact]
        public void A_settler_from_before_0_17_has_no_mood_yet_and_reads_back_unchanged()
        {
            CitizenRecord r = CitizenRecord.Decode(Citizen016);
            Assert.Equal(Mood.Unknown, r.Mood);
            Assert.Equal("", r.WhyMood);
            Assert.Empty(r.LastMeals);
            Assert.Equal(CitizenRecord.NoDay, r.EveningDay);
            Assert.False(r.EveningCompany);
            Assert.Equal(CitizenRecord.NoDay, r.HitDay);
            Assert.Equal(CitizenRecord.NoDay, r.JoblessSince);
            Assert.Equal(Citizen016, r.Encode());
            Assert.Equal(Citizen013, CitizenRecord.Decode(Citizen013).Encode());
        }

        [Fact]
        public void A_meal_name_with_a_comma_is_not_split()
        {
            var r = CitizenRecord.Decode(Citizen013);
            MealMemory.Add(r.LastMeals, "Odd,Food");
            Assert.Equal(new[] { "Odd Food" }, CitizenRecord.Decode(r.Encode()).LastMeals);
        }

        [Fact]
        public void The_saga_knows_a_raid_was_repelled_and_mood_has_a_setting()
        {
            Assert.True(SagaLog.IsKnownKind("repelled"));
            Assert.Contains("Mood", ConfigNames.All);
        }

        [Fact]
        public void A_death_in_the_last_three_days_saddens_the_colony()
        {
            var saga = new SagaLog();
            saga.Add(new SagaEntry(7, "died", "Ingrid", "$vc_death_monster"));
            Assert.Equal("Ingrid", MoodSaga.RecentDeath(saga, today: 7));
            Assert.Equal("Ingrid", MoodSaga.RecentDeath(saga, today: 9));
            Assert.Null(MoodSaga.RecentDeath(saga, today: 10));
            Assert.Null(MoodSaga.RecentDeath(new SagaLog(), today: 7));
        }

        [Fact]
        public void A_raid_repelled_in_the_last_two_days_cheers_it()
        {
            var saga = new SagaLog();
            saga.Add(new SagaEntry(7, "repelled"));
            Assert.True(MoodSaga.Repelled(saga, today: 8));
            Assert.False(MoodSaga.Repelled(saga, today: 9));
        }

        [Fact]
        public void Deaths_since_a_raid_began_are_counted_from_the_saga()
        {
            var saga = new SagaLog();
            saga.Add(new SagaEntry(5, "died", "Ulf"));
            Assert.Equal(1, MoodSaga.DeathsSince(saga, 5));
            Assert.Equal(0, MoodSaga.DeathsSince(saga, 6));
            saga.Add(new SagaEntry(6, "built", "x"));
            saga.Add(new SagaEntry(6, "died", "Ingrid"));
            Assert.Equal(1, MoodSaga.DeathsSince(saga, 6));
            Assert.Equal(2, MoodSaga.DeathsSince(saga, 5));
            Assert.Equal(0, MoodSaga.DeathsSince(null, 5));
        }

        [Fact]
        public void Recent_days_count_today_and_the_days_before()
        {
            Assert.True(MoodDays.Within(5, today: 6, days: 2));
            Assert.False(MoodDays.Within(4, today: 6, days: 2));
            Assert.False(MoodDays.Within(CitizenRecord.NoDay, today: 0, days: 2));
            Assert.True(MoodDays.Within(6, today: 6, days: 1));
            Assert.Equal(3, MoodDays.Since(3, today: 6));
            Assert.Equal(0, MoodDays.Since(CitizenRecord.NoDay, today: 6));
        }
    }
}
