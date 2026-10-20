using System.Globalization;
using System.Threading;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    public class CitizenRecordTests
    {
        private static CitizenRecord Full() => new CitizenRecord
        {
            Id = "c1", Name = "Bjorn", Model = 1, Hair = "Hair7", Beard = "BeardNone",
            Skin = new[] { 0.9f, 0.78f, 0.68f }, HairColor = new[] { 0.5f, 0.25f, 0.125f },
            ColonyId = "col", ColonyPos = new[] { 10.5f, 31f, -4.25f },
            Workplace = "hut1", WorkplacePos = new[] { 1f, 2f, 3f }, Job = "lumberjack",
            Carry = "BeechSeeds:1,Wood:12", Status = "$vc_status_working",
            Woodcutting = 12f,
            WoodcuttingXp = 3.5f,
        };

        [Fact]
        public void Round_trip_keeps_every_field()
        {
            CitizenRecord copy = CitizenRecord.Decode(Full().Encode());
            Assert.Equal("c1", copy.Id);
            Assert.Equal("Bjorn", copy.Name);
            Assert.Equal(1, copy.Model);
            Assert.Equal("Hair7", copy.Hair);
            Assert.Equal("BeardNone", copy.Beard);
            Assert.Equal(new[] { 0.9f, 0.78f, 0.68f }, copy.Skin);
            Assert.Equal(new[] { 0.5f, 0.25f, 0.125f }, copy.HairColor);
            Assert.Equal("col", copy.ColonyId);
            Assert.Equal(new[] { 10.5f, 31f, -4.25f }, copy.ColonyPos);
            Assert.Equal("hut1", copy.Workplace);
            Assert.Equal(new[] { 1f, 2f, 3f }, copy.WorkplacePos);
            Assert.Equal("lumberjack", copy.Job);
            Assert.Equal("BeechSeeds:1,Wood:12", copy.Carry);
            Assert.Equal("$vc_status_working", copy.Status);
            Assert.Equal(12f, copy.Woodcutting);
            Assert.Equal(3.5f, copy.WoodcuttingXp);
        }

        [Fact]
        public void Citizens_saved_before_skills_existed_start_as_beginners()
        {
            CitizenRecord record = CitizenRecord.Decode("v=2|id=c3|name=Leif");
            Assert.Equal(0f, record.Woodcutting);
            Assert.Equal(0f, record.WoodcuttingXp);
        }

        [Fact]
        public void Values_with_separator_characters_survive()
        {
            var record = new CitizenRecord { Id = "x", Name = "A|B=C\\D\nE" };
            Assert.Equal("A|B=C\\D\nE", CitizenRecord.Decode(record.Encode()).Name);
        }

        // Review focus 2
        [Fact]
        public void Floats_are_saved_with_a_dot_on_a_swedish_pc()
        {
            CultureInfo before = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("sv-SE");
                var record = new CitizenRecord { Id = "x", ColonyPos = new[] { 0.5f, 1.25f, -2.75f } };
                string saved = record.Encode();
                Assert.Contains("0.5,1.25,-2.75", saved);
                Assert.Equal(new[] { 0.5f, 1.25f, -2.75f }, CitizenRecord.Decode(saved).ColonyPos);
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = before;
            }
        }

        // Review focus 3
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("|||=x|junk|v=abc|model=zz|skin=1,2|colonypos=a,b,c|id")]
        public void Decoding_garbage_never_throws(string? text)
        {
            CitizenRecord record = CitizenRecord.Decode(text);
            Assert.Equal("", record.Id);
            Assert.Equal(0, record.Model);
            Assert.Equal(new[] { 1f, 1f, 1f }, record.Skin);
            Assert.Null(record.ColonyPos);
            Assert.False(record.HasColony);
            Assert.False(record.IsEmployed);
        }

        // Review focus 3
        [Fact]
        public void FromV1_makes_a_jobless_member_keeping_name_look_and_carry()
        {
            CitizenRecord record = CitizenRecord.FromV1("col", "Signe", 1, "Hair3", "BeardNone",
                new[] { 0.5f, 0.37f, 0.28f }, new[] { 0.6f, 0.6f, 0.6f }, "Wood:7", "$vc_status_working");
            Assert.Equal("col", record.ColonyId);
            Assert.Equal("Signe", record.Name);
            Assert.Equal(1, record.Model);
            Assert.Equal("Hair3", record.Hair);
            Assert.Equal(new[] { 0.5f, 0.37f, 0.28f }, record.Skin);
            Assert.Equal("Wood:7", record.Carry);
            Assert.False(record.IsEmployed);
            Assert.Equal("", record.Job);
            Assert.Null(record.ColonyPos);
            Assert.Equal("", record.Id);   // the game assigns a fresh id when it saves the record
        }

        [Fact]
        public void FromV1_with_missing_values_uses_defaults()
        {
            CitizenRecord record = CitizenRecord.FromV1("", "", 0, "", "", null, null, "", "");
            Assert.False(record.HasColony);
            Assert.Equal(new[] { 1f, 1f, 1f }, record.Skin);
            Assert.Equal(new[] { 1f, 1f, 1f }, record.HairColor);
        }

        [Fact]
        public void ClearColony_also_clears_the_workplace()
        {
            CitizenRecord record = Full();
            record.ClearColony();
            Assert.False(record.HasColony);
            Assert.Null(record.ColonyPos);
            Assert.False(record.IsEmployed);
            Assert.Null(record.WorkplacePos);
            Assert.Equal("", record.Job);
        }

        [Fact]
        public void His_last_meal_and_hunger_are_kept()
        {
            CitizenRecord record = Full();
            record.LastMealDay = 12;
            record.Hungry = true;
            CitizenRecord again = CitizenRecord.Decode(record.Encode());
            Assert.Equal(12, again.LastMealDay);
            Assert.True(again.Hungry);
            Assert.Equal(Meals.NeverAte, CitizenRecord.Decode("v=2|id=c2|name=Ulf").LastMealDay);
            Assert.False(CitizenRecord.Decode("v=2|id=c2|name=Ulf").Hungry);
        }

        [Fact]
        public void His_home_is_kept()
        {
            CitizenRecord record = Full();
            record.Home = "longhouse1#7";
            record.HomePos = new[] { 1f, 2f, 3f };
            CitizenRecord again = CitizenRecord.Decode(record.Encode());
            Assert.Equal("longhouse1#7", again.Home);
            Assert.Equal(new[] { 1f, 2f, 3f }, again.HomePos);
            Assert.True(again.HasHome);
        }

        [Fact]
        public void Leaving_the_colony_leaves_the_home_too()
        {
            CitizenRecord record = Full();
            record.Home = "longhouse1#7";
            record.HomePos = new[] { 1f, 2f, 3f };
            record.ClearColony();
            Assert.False(record.HasHome);
            Assert.Null(record.HomePos);
        }

        [Fact]
        public void When_his_Town_Hall_was_lost_is_kept()
        {
            CitizenRecord record = Full();
            record.HallLostAt = 123456.5;
            Assert.Equal(123456.5, CitizenRecord.Decode(record.Encode()).HallLostAt, 1);
        }

        [Fact]
        public void A_record_from_before_the_grace_time_never_lost_its_Town_Hall()
        {
            Assert.Equal(ColonyGrace.NotLost, CitizenRecord.Decode("v=2|id=c2|name=Ulf|colony=abc").HallLostAt);
            Assert.Equal(ColonyGrace.NotLost, new CitizenRecord().HallLostAt);
        }

        [Fact]
        public void Unknown_fields_from_a_newer_version_are_ignored()
        {
            CitizenRecord record = CitizenRecord.Decode("v=9|id=c2|name=Ulf|mood=happy|skills=a:1");
            Assert.Equal("c2", record.Id);
            Assert.Equal("Ulf", record.Name);
        }
    
        [Fact]   // 0.22: Farming and Crafting are kept as Woodcutting is
        public void Farming_and_crafting_round_trip()
        {
            var r = new CitizenRecord { Id = "a", Name = "Astrid", Farming = 7f, FarmingXp = 1.5f, Crafting = 3f, CraftingXp = 0.25f };
            CitizenRecord copy = CitizenRecord.Decode(r.Encode());
            Assert.Equal(7f, copy.Farming);
            Assert.Equal(1.5f, copy.FarmingXp);
            Assert.Equal(3f, copy.Crafting);
            Assert.Equal(0.25f, copy.CraftingXp);
        }

        [Fact]   // Review Focus: a record from before 0.22 reads back and is saved again unchanged
        public void Records_without_new_skills_read_back_unchanged()
        {
            string before = new CitizenRecord { Id = "a", Name = "Ivar", Woodcutting = 4f }.Encode();
            Assert.DoesNotContain("farming", before);
            Assert.DoesNotContain("crafting", before);
            CitizenRecord read = CitizenRecord.Decode(before);
            Assert.Equal(0f, read.Farming);
            Assert.Equal(0f, read.Crafting);
            Assert.Equal(before, read.Encode());
        }

        [Fact]
        public void Practise_raises_the_right_skill()
        {
            var r = new CitizenRecord();
            bool up = false;
            for (int i = 0; i < 10 && !up; i++) up = r.Practise(WorkSkill.Farming, 1f);
            Assert.True(up);
            Assert.Equal(1f, r.Level(WorkSkill.Farming));
            Assert.Equal(0f, r.Level(WorkSkill.Crafting));
            Assert.Equal(0f, r.Level(WorkSkill.Woodcutting));
            Assert.False(r.Practise(WorkSkill.None, 5f));
            r.Practise(WorkSkill.Crafting, 0.5f);
            Assert.Equal(0.5f, r.CraftingXp);
        }
}
}
