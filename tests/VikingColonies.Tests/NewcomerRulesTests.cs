using System.Linq;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>Settler life part 3 (0.16): the rules around newcomers and who they are, outside the identity rules.</summary>
    public class NewcomerRulesTests
    {
        [Fact]
        public void The_saga_knows_a_newcomer_came_with_his_story_and_small_talk_counts_him_as_new()
        {
            Assert.True(SagaLog.IsKnownKind("came"));
            var saga = new SagaLog();
            saga.Add(new SagaEntry(10, "came", "Ulf", "$vc_origin_coast", "farmer", "cheerful"));
            Assert.Equal("Ulf", SmallTalk.FromSaga(saga, 10, "Astrid").NewcomerName);
            Assert.True(SmallTalk.FromSaga(saga, 10, "Ulf").IsNewToday);
        }

        [Fact]
        public void The_walk_in_and_the_calling_have_settings()
        {
            Assert.Contains("NewcomersWalkIn", ConfigNames.All);
            Assert.Contains("CallingBonus", ConfigNames.All);
        }

        [Fact]
        public void A_hut_hires_someone_good_at_its_trade_first_then_the_nearest()
        {
            var hut = new GroundPoint(0, 0);
            var settlers = new[]
            {
                new CitizenInfo("near", "col", "", new GroundPoint(2, 0), "builder"),
                new CitizenInfo("good", "col", "", new GroundPoint(20, 0), "farmer"),
                new CitizenInfo("busy", "col", "hut9", new GroundPoint(1, 0), "farmer"),
            };
            Assert.Equal("good", ColonyRules.PickWorker(hut, "col", settlers, "farmer"));
            Assert.Equal("near", ColonyRules.PickWorker(hut, "col", settlers, "forager"));
            Assert.Equal("near", ColonyRules.PickWorker(hut, "col", settlers));
        }

        [Fact]
        public void A_brave_settler_runs_later_and_a_timid_one_sooner()
        {
            var brave = new FleeTracker();
            Assert.False(brave.Update(8f, double.NegativeInfinity, 0, startDistance: 6f));
            var timid = new FleeTracker();
            Assert.True(timid.Update(18f, double.NegativeInfinity, 0, startDistance: 20f));
            Assert.True(new FleeTracker().Update(11f, double.NegativeInfinity, 0));   // the usual 12 m
        }

        [Fact]
        public void The_Town_Hall_says_who_he_is_in_two_lines()
        {
            // The words come in the player's language; Core only puts them together.
            string[] lines = CitizenLine.Details("Astrid", "good at foraging", "cheerful", "from the coast", "Here 23 days.", "Home: Longhouse bed 2.");
            Assert.Equal(2, lines.Length);
            Assert.Equal("Astrid: good at foraging, cheerful. From the coast.", lines[0]);
            Assert.Equal("Here 23 days. Home: Longhouse bed 2.", lines[1]);
            string[] none = CitizenLine.Details("Ulf", "", "grumpy", "", "", "No home.");
            Assert.Equal("Ulf: grumpy.", none[0]);
            Assert.Equal("No home.", none[1]);
        }
    }
}
