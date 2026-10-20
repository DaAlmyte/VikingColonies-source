using System.Linq;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    // Review of 0.10 (2026-10-15): Bring back must never revive the dead nor make two of a settler.
    public class BringBackRulesTests
    {
        [Fact]
        public void A_settler_s_life_number_is_saved_and_old_records_start_at_zero()
        {
            var record = new CitizenRecord { Id = "abc", Name = "Ivar", Life = 2 };
            Assert.Equal(2, CitizenRecord.Decode(record.Encode()).Life);
            Assert.Equal(0, CitizenRecord.Decode(new CitizenRecord { Id = "abc" }.Encode().Replace("life", "x")).Life);
            Assert.Equal(2, CitizenRecord.Decode(Keepsake.Of(record)).Life);   // the keepsake keeps it
        }

        [Fact]
        public void A_body_older_than_its_keepsake_is_an_outdated_copy()
        {
            Assert.True(Keepsake.Outdated(bodyLife: 0, keptLife: 1));
            Assert.False(Keepsake.Outdated(bodyLife: 1, keptLife: 1));
            Assert.False(Keepsake.Outdated(bodyLife: 2, keptLife: 1));   // the keepsake hasn't caught up yet
        }

        [Fact]
        public void Bring_back_is_offered_only_after_a_whole_day_missing()
        {
            // A settler who stepped just past the loaded area shows as missing at once; a day tells a lost body.
            var entry = new RosterEntry { CitizenId = "abc", MissingSinceDay = 10 };
            Assert.False(ColonyRoster.MayBringBack(entry, today: 10));
            Assert.True(ColonyRoster.MayBringBack(entry, today: 11));
            Assert.False(ColonyRoster.MayBringBack(new RosterEntry { CitizenId = "abc" }, today: 20));   // not missing
            Assert.False(ColonyRoster.MayBringBack(null, today: 20));
        }

        [Fact]
        public void The_dead_are_remembered_newest_first_and_the_list_stays_short()
        {
            string saved = DeathList.Add("", "a");
            saved = DeathList.Add(saved, "b");
            saved = DeathList.Add(saved, "a");   // already there: not twice
            Assert.Equal(new[] { "b", "a" }, DeathList.Parse(saved).ToArray());
            for (int i = 0; i < DeathList.MaxRemembered + 10; i++) saved = DeathList.Add(saved, "id" + i);
            Assert.Equal(DeathList.MaxRemembered, DeathList.Parse(saved).Count);
            Assert.Equal("id" + (DeathList.MaxRemembered + 9), DeathList.Parse(saved)[0]);
            Assert.Empty(DeathList.Parse(null));
        }
    }
}
