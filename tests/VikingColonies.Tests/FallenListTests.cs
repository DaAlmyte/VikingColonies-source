using System.Linq;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>The memorial stone and the fallen return (1.5.1): the colony's fallen list and its rules.</summary>
    public class FallenListTests
    {
        private static FallenLine Line(string id, string name, int day, string record = "") =>
            new FallenLine { Id = id, Name = name, Job = "forager", Day = day, Cause = "$vc_cause_killed_by", CauseArg = "Greydwarf", Record = record };

        [Fact]
        public void Adding_the_same_death_twice_keeps_one_line()
        {
            var list = new FallenList();
            Assert.True(list.Add(Line("a1", "Ulf", 39)));
            Assert.False(list.Add(Line("a1", "Ulf", 39)));
            Assert.False(list.Add(Line("b2", "", 39)));
            Assert.Single(list.Lines);
        }

        [Fact]
        public void A_list_saves_and_reads_back()
        {
            var list = new FallenList();
            list.Add(Line("a1", "Ulf|the=odd\nname", 39, "rec|ord=x"));
            FallenLine gudrun = Line("b2", "Gudrun", 12);
            list.Add(gudrun);
            list.MarkReturned("b2", 15);
            FallenList back = FallenList.Decode(list.Encode());
            Assert.Equal(2, back.Lines.Count);
            FallenLine ulf = back.Lines[0];
            Assert.Equal("a1", ulf.Id);
            Assert.Equal("Ulf|the=odd\nname", ulf.Name);
            Assert.Equal("forager", ulf.Job);
            Assert.Equal(39, ulf.Day);
            Assert.Equal("$vc_cause_killed_by", ulf.Cause);
            Assert.Equal("Greydwarf", ulf.CauseArg);
            Assert.Equal("rec|ord=x", ulf.Record);
            Assert.False(ulf.Returned);
            Assert.True(back.Lines[1].Returned);
            Assert.Equal(15, back.Lines[1].BackDay);
        }

        [Fact]
        public void An_empty_saved_list_is_saved()
        {
            Assert.True(FallenList.IsSaved(new FallenList().Encode()));
            Assert.Equal("1", new FallenList().Encode());
            Assert.False(FallenList.IsSaved(""));
            Assert.False(FallenList.IsSaved(null));
        }

        [Fact]
        public void Decode_skips_malformed_lines()
        {
            Assert.Empty(FallenList.Decode("1\nname=\nday=x\nnonsense").Lines);
            Assert.Empty(FallenList.Decode("garbage").Lines);
            Assert.False(FallenList.IsSaved("garbage"));
            Assert.Empty(FallenList.Decode(null).Lines);
        }

        [Fact]
        public void Old_saves_take_the_sagas_deaths()
        {
            var saga = new SagaLog();
            saga.Add(new SagaEntry(30, "arrived", "Arne"));
            saga.Add(new SagaEntry(39, "died", "Ulf", "$vc_cause_killed_by", "Greydwarf"));
            FallenList list = FallenList.FromSaga(saga);
            FallenLine ulf = Assert.Single(list.Lines);
            Assert.Equal("Ulf", ulf.Name);
            Assert.Equal(39, ulf.Day);
            Assert.Equal("$vc_cause_killed_by", ulf.Cause);
            Assert.Equal("Greydwarf", ulf.CauseArg);
            Assert.Equal("", ulf.Id);
            Assert.Equal("", ulf.Record);
            Assert.Equal("saga:Ulf@39", ulf.Key);
        }

        [Fact]
        public void A_death_told_by_the_saga_and_by_its_own_line_is_one_line()
        {
            var saga = new SagaLog();
            saga.Add(new SagaEntry(39, "died", "Ulf", "$vc_cause_killed_by", "Greydwarf"));
            FallenList camp = FallenList.FromSaga(saga);
            Assert.True(camp.Add(Line("a1", "Ulf", 39, "r")));   // the old save's line becomes the full one
            FallenLine ulf = Assert.Single(camp.Lines);
            Assert.Equal("a1", ulf.Id);
            Assert.Equal("r", ulf.Record);
            Assert.False(camp.MergeFrom(FallenList.FromSaga(saga)));   // a stone that still has the Saga's line
            Assert.Single(camp.Lines);
            FallenList stone = FallenList.FromSaga(saga);
            Assert.True(stone.MergeFrom(camp));   // the stone takes the full line in place of its own
            Assert.Equal("a1", Assert.Single(stone.Lines).Id);
            Assert.Equal("", stone.Lines[0].Record);
        }

        [Fact]
        public void Merging_a_stones_words_adds_its_lines_without_records()
        {
            var stone = new FallenList();
            stone.Add(Line("a1", "Ulf", 39, "r"));
            stone.Add(Line("b2", "Gudrun", 12));
            stone.MarkReturned("b2", 15);
            var camp = new FallenList();
            Assert.True(camp.MergeFrom(stone));
            Assert.Equal(2, camp.Lines.Count);
            Assert.Equal("", camp.Lines.First(l => l.Name == "Ulf").Record);
            Assert.Equal(15, camp.Lines.First(l => l.Name == "Gudrun").BackDay);
            Assert.False(camp.MergeFrom(stone));
        }

        [Fact]
        public void Merging_takes_a_return_the_other_copy_has()
        {
            var camp = new FallenList();
            camp.Add(Line("a1", "Ulf", 39, "r"));
            var stone = camp.WordsOnly();
            Assert.Equal("", stone.Lines[0].Record);
            camp.MarkReturned("a1", 42);
            Assert.True(stone.MergeFrom(camp));
            Assert.Equal(42, stone.Lines[0].BackDay);
        }

        [Fact]
        public void NextBack_waits_for_its_days()
        {
            var list = new FallenList();
            list.Add(Line("a1", "Ulf", 39, "r"));
            Assert.Null(list.NextBack(41, 3, true, -1, 5, 10));
            Assert.Equal("Ulf", list.NextBack(42, 3, true, -1, 5, 10)?.Name);
        }

        [Fact]
        public void NextBack_needs_the_setting_and_a_record()
        {
            var list = new FallenList();
            list.Add(Line("a1", "Ulf", 39, "r"));
            Assert.Null(list.NextBack(50, 3, false, -1, 5, 10));
            var bare = new FallenList();
            bare.Add(Line("a1", "Ulf", 39));
            Assert.Null(bare.NextBack(50, 3, true, -1, 5, 10));
        }

        [Fact]
        public void NextBack_waits_for_room_and_takes_one_a_day()
        {
            var list = new FallenList();
            list.Add(Line("a1", "Ulf", 39, "r"));
            Assert.Null(list.NextBack(50, 3, true, -1, 10, 10));
            Assert.Null(list.NextBack(50, 3, true, 50, 5, 10));
            Assert.NotNull(list.NextBack(50, 3, true, 49, 9, 10));
        }

        [Fact]
        public void NextBack_takes_the_earliest_death_first()
        {
            var list = new FallenList();
            list.Add(Line("a1", "Ulf", 39, "r"));
            list.Add(Line("b2", "Gudrun", 12, "r"));
            Assert.Equal("Gudrun", list.NextBack(50, 3, true, -1, 5, 10)?.Name);
        }

        [Fact]
        public void A_returned_line_is_marked_and_loses_its_record()
        {
            var list = new FallenList();
            list.Add(Line("a1", "Ulf", 39, "r"));
            Assert.True(list.MarkReturned("a1", 42));
            Assert.True(list.Lines[0].Returned);
            Assert.Equal("", list.Lines[0].Record);
            Assert.Null(list.NextBack(50, 3, true, -1, 5, 10));
            Assert.False(list.MarkReturned("nobody", 42));
        }

        [Fact]
        public void A_returned_settler_keeps_who_they_are_and_starts_afresh()
        {
            var dead = new CitizenRecord
            {
                Id = "old", Name = "Ulf", Model = 1, Hair = "Hair3", Beard = "Beard2", Skin = new[] { 0.9f, 0.8f, 0.7f },
                HairColor = new[] { 0.2f, 0.1f, 0f }, Calling = "forager", Trait = "Brave", Origin = "$vc_origin_coast",
                Woodcutting = 37f, WoodcuttingXp = 4f, Farming = 12f, Kills = 3, ColonyId = "c1", Workplace = "hut1", Job = "forager",
                Home = "lh#2", Carry = "Wood:3", Arrived = 20, Life = 2,
            };
            CitizenRecord back = FallenReturn.Restore(dead, "new", 42);
            Assert.Equal("new", back.Id);
            Assert.Equal("Ulf", back.Name);
            Assert.Equal(1, back.Model);
            Assert.Equal("Hair3", back.Hair);
            Assert.Equal("Beard2", back.Beard);
            Assert.Equal(new[] { 0.9f, 0.8f, 0.7f }, back.Skin);
            Assert.Equal(new[] { 0.2f, 0.1f, 0f }, back.HairColor);
            Assert.Equal("forager", back.Calling);
            Assert.Equal("Brave", back.Trait);
            Assert.Equal("$vc_origin_coast", back.Origin);
            Assert.Equal(37f, back.Woodcutting);
            Assert.Equal(4f, back.WoodcuttingXp);
            Assert.Equal(12f, back.Farming);
            Assert.Equal(3, back.Kills);
            Assert.Equal("", back.ColonyId);
            Assert.Equal("", back.Workplace);
            Assert.Equal("", back.Job);
            Assert.Equal("", back.Home);
            Assert.Equal("", back.Carry);
            Assert.Equal(42, back.Arrived);
            Assert.Equal(42, back.JoblessSince);
            Assert.Equal(0, back.Life);
        }

        [Fact]
        public void The_stone_shows_the_newest_first_in_pages_of_20()
        {
            var list = new FallenList();
            for (int i = 0; i < 45; i++) list.Add(Line("id" + i, "S" + i, i));
            MemorialPage first = MemorialText.Page(list, 0);
            Assert.Equal(3, first.Pages);
            Assert.Equal(20, first.Lines.Count);
            Assert.Equal("S44", first.Lines[0].Name);
            MemorialPage last = MemorialText.Page(list, 2);
            Assert.Equal(5, last.Lines.Count);
            Assert.Equal("S0", last.Lines[4].Name);
            Assert.Equal(0, MemorialText.Page(list, 3).Page);
            MemorialPage empty = MemorialText.Page(new FallenList(), 0);
            Assert.Equal(1, empty.Pages);
            Assert.Empty(empty.Lines);
        }

        [Fact]
        public void The_same_day_shows_the_later_death_first()
        {
            var list = new FallenList();
            list.Add(Line("a", "First", 10));
            list.Add(Line("b", "Second", 10));
            Assert.Equal("Second", MemorialText.Page(list, 0).Lines[0].Name);
        }

        private static string RecordOf(string id, string name, string colony) =>
            new CitizenRecord { Id = id, Name = name, ColonyId = colony }.Encode();

        [Fact]
        public void A_sent_line_is_kept_only_for_a_known_death_of_a_settler_with_an_id()
        {
            FallenLine ok = FallenChecks.Checked(Line("a1", "Ulf", 39, RecordOf("a1", "Ulf", "c1")), "c1", knownDead: true);
            Assert.NotNull(ok);
            Assert.Equal("Ulf", ok.Name);
            Assert.NotEqual("", ok.Record);
            Assert.Null(FallenChecks.Checked(Line("a1", "Ulf", 39), "c1", knownDead: false));   // nobody this computer knows died
            Assert.Null(FallenChecks.Checked(Line("", "Ulf", 39), "c1", knownDead: true));      // no id: not a death the game told
            Assert.Null(FallenChecks.Checked(Line("a1", "Ulf", -1), "c1", knownDead: true));
            Assert.Null(FallenChecks.Checked(null, "c1", knownDead: true));
        }

        [Fact]
        public void A_sent_line_with_a_word_over_64_characters_is_dropped()
        {
            string longWord = new string('x', 65);
            Assert.Null(FallenChecks.Checked(Line("a1", longWord, 39), "c1", true));
            Assert.Null(FallenChecks.Checked(Line(longWord, "Ulf", 39), "c1", true));
            FallenLine job = Line("a1", "Ulf", 39);
            job.Job = longWord;
            Assert.Null(FallenChecks.Checked(job, "c1", true));
            FallenLine cause = Line("a1", "Ulf", 39);
            cause.CauseArg = longWord;
            Assert.Null(FallenChecks.Checked(cause, "c1", true));
            cause.CauseArg = "";
            cause.Cause = longWord;
            Assert.Null(FallenChecks.Checked(cause, "c1", true));
        }

        [Fact]
        public void A_sent_record_that_isnt_that_settlers_is_let_go_and_the_words_kept()
        {
            Assert.Equal("", FallenChecks.Checked(Line("a1", "Ulf", 39, RecordOf("b2", "Ulf", "c1")), "c1", true).Record);    // another settler's
            Assert.Equal("", FallenChecks.Checked(Line("a1", "Ulf", 39, RecordOf("a1", "Arne", "c1")), "c1", true).Record);   // another name
            Assert.Equal("", FallenChecks.Checked(Line("a1", "Ulf", 39, RecordOf("a1", "Ulf", "c9")), "c1", true).Record);    // another colony
            Assert.Equal("", FallenChecks.Checked(Line("a1", "Ulf", 39, new string('r', FallenChecks.MaxRecord + 1)), "c1", true).Record);
            FallenLine back = Line("a1", "Ulf", 39);
            back.BackDay = 40;   // a sender can't say anyone returned
            Assert.False(FallenChecks.Checked(back, "c1", true).Returned);
        }

        [Fact]
        public void A_death_far_from_its_camp_waits_until_the_camp_is_near()
        {
            var waiting = new FallenWaiting();
            waiting.Add("c1", Line("a1", "Ulf", 39));
            waiting.Add("c2", Line("b2", "Gudrun", 40));
            Assert.Empty(waiting.Take("c3"));
            Assert.Equal("Ulf", Assert.Single(waiting.Take("c1")).Name);
            Assert.Empty(waiting.Take("c1"));   // taken once
            Assert.Single(waiting.Take("c2"));
        }

        [Fact]
        public void At_most_50_deaths_wait()
        {
            var waiting = new FallenWaiting();
            for (int i = 0; i < 60; i++) waiting.Add("c1", Line("id" + i, "S" + i, i));
            System.Collections.Generic.IReadOnlyList<FallenLine> taken = waiting.Take("c1");
            Assert.Equal(FallenWaiting.Max, taken.Count);
            Assert.Equal("S10", taken[0].Name);   // the oldest let go first
        }

        [Fact]
        public void One_stone_per_colony()
        {
            Assert.True(MemorialRules.MayRaise("a", new[] { "b" }));
            Assert.False(MemorialRules.MayRaise("a", new[] { "b", "a" }));
            Assert.False(MemorialRules.MayRaise("", new string[0]));
            Assert.False(MemorialRules.MayRaise(null, null));
        }
    }
}
