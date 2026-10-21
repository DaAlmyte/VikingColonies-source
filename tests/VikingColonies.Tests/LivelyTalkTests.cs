using System.Collections.Generic;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>
    /// 1.4.2's polish round 2 (the player: "more fun and good"): settlers talk about their trade, a raid the colony held
    /// off, and the latest boss to fall, and the thin topics have more lines.
    /// </summary>
    public class LivelyTalkTests
    {
        private static System.Func<double> Always(double value) => () => value;

        [Fact]
        public void A_worker_talks_about_their_trade()
        {
            var context = new TalkContext { Job = "lumberjack" };
            Assert.Equal(TalkTopic.Trade, SmallTalk.Topic(context, Always(0.1)));
            TalkLine line = SmallTalk.Line(TalkTopic.Trade, "", 2, context);
            Assert.StartsWith("$vc_talk_trade_lumberjack_", line.Token);
            foreach (string job in new[] { "builder", "farmer", "forager", "guard", "lumberjack" })
                Assert.True(SmallTalk.Count(TalkTopic.Trade, "", job) >= 4, job);
        }

        [Fact]
        public void A_settler_with_no_trade_or_an_unknown_one_doesnt()
        {
            Assert.NotEqual(TalkTopic.Trade, SmallTalk.Topic(new TalkContext { Job = "" }, Always(0.1)));
            Assert.NotEqual(TalkTopic.Trade, SmallTalk.Topic(new TalkContext { Job = "someone_elses_job" }, Always(0.1)));
        }

        [Fact]
        public void A_raid_held_off_lately_is_talked_about()
        {
            var saga = new SagaLog();
            saga.Add(new SagaEntry(8, "repelled", "$vc_raid_army_eikthyr"));
            Assert.True(SmallTalk.FromSaga(saga, 9, "Ulf").RepelledLately);
            Assert.False(SmallTalk.FromSaga(saga, 8 + SmallTalk.MournDays + 1, "Ulf").RepelledLately);
            Assert.Equal(TalkTopic.Repelled, SmallTalk.Topic(new TalkContext { RepelledLately = true }, Always(0.1)));
        }

        [Fact]
        public void The_latest_boss_to_fall_is_named()
        {
            var context = new TalkContext { BossToken = "$enemy_bonemass" };
            Assert.Equal(TalkTopic.Boss, SmallTalk.Topic(context, Always(0.1)));
            TalkLine line = SmallTalk.Line(TalkTopic.Boss, "", 1, context);
            Assert.StartsWith("$vc_talk_boss_", line.Token);
            Assert.Equal("$enemy_bonemass", line.Arg);
        }

        [Fact]
        public void The_latest_boss_is_the_furthest_along_the_way_beaten()
        {
            Assert.Equal("", TownHallLevels.LatestBeaten(key => false));
            Assert.Equal("$enemy_gdking", TownHallLevels.LatestBeaten(key => key == "defeated_eikthyr" || key == "defeated_gdking"));
            Assert.Equal("$enemy_bonemass", TownHallLevels.LatestBeaten(key => key == "defeated_bonemass"));   // one skipped still counts
        }

        [Fact]
        public void Trade_and_bosses_come_after_the_colonys_own_news_and_the_trait_lines_still_get_their_turn()
        {
            // Each topic that applies is taken half the time, in order: news first, then the trade, the trait, the boss.
            var news = new TalkContext { BuiltToken = "$vc_hut_farm", Job = "farmer", BossToken = "$enemy_eikthyr" };
            Assert.Equal(TalkTopic.Built, SmallTalk.Topic(news, Always(0.1)));
            var rolls = new Queue<double>(new[] { 0.9, 0.1 });
            var cheerfulFarmer = new TalkContext { Job = "farmer", Trait = Trait.Cheerful, BossToken = "$enemy_eikthyr" };
            Assert.Equal(TalkTopic.Cheerful, SmallTalk.Topic(cheerfulFarmer, () => rolls.Dequeue()));
        }

        [Fact]
        public void A_boss_is_news_only_when_the_colony_hears_of_one_further_along_the_way()
        {
            // The review of polish round 2: the boss topic never ended ("Eikthyr, fallen!" on day 300).
            Assert.True(TownHallLevels.BossNews("", "$enemy_eikthyr"));
            Assert.True(TownHallLevels.BossNews("$enemy_gdking", "$enemy_bonemass"));
            Assert.False(TownHallLevels.BossNews("$enemy_eikthyr", "$enemy_eikthyr"));
            Assert.False(TownHallLevels.BossNews("$enemy_bonemass", "$enemy_gdking"));   // a key gone back (a mod, a reset): no news
            Assert.False(TownHallLevels.BossNews("$enemy_gdking", ""));
            Assert.False(TownHallLevels.BossNews("", "$enemy_someone_elses"));
        }

        [Fact]
        public void A_bosss_fall_is_talked_about_for_two_days_from_the_saga()
        {
            Assert.True(SagaLog.IsKnownKind("bossfell"));
            var saga = new SagaLog();
            saga.Add(new SagaEntry(10, "bossfell", "$enemy_eikthyr"));
            saga.Add(new SagaEntry(11, "bossfell", "$enemy_gdking"));
            Assert.Equal("$enemy_gdking", SmallTalk.FromSaga(saga, 12, "Ulf").BossToken);   // the newest
            Assert.Equal("", SmallTalk.FromSaga(saga, 11 + SmallTalk.MournDays + 1, "Ulf").BossToken);
            Assert.Equal("", SmallTalk.FromSaga(new SagaLog(), 12, "Ulf").BossToken);
        }

        [Fact]
        public void The_trade_is_taken_a_third_of_the_time_so_the_trait_and_general_lines_keep_theirs()
        {
            // The review of polish round 2: trade at one half took half of every worker's lines.
            var farmer = new TalkContext { Job = "farmer" };
            Assert.Equal(TalkTopic.Trade, SmallTalk.Topic(farmer, Always(0.3)));
            Assert.Equal(TalkTopic.General, SmallTalk.Topic(farmer, Always(0.4)));
            Assert.Equal(TalkTopic.Built, SmallTalk.Topic(new TalkContext { BuiltToken = "$vc_hut_farm", Job = "farmer" }, Always(0.4)));
        }

        [Fact]
        public void The_thin_topics_have_six_lines_or_more_and_general_sixteen()
        {
            foreach (TalkTopic topic in new[] { TalkTopic.Mourn, TalkTopic.Hungry, TalkTopic.Weather, TalkTopic.NewHere, TalkTopic.Newcomer,
                         TalkTopic.Built, TalkTopic.Jobless, TalkTopic.Evening, TalkTopic.Cheerful, TalkTopic.Grumpy })
                Assert.True(SmallTalk.Count(topic, "") >= 6, topic.ToString());
            Assert.True(SmallTalk.Count(TalkTopic.General, "") >= 16);
        }

        [Fact]
        public void Every_line_has_a_token_the_game_can_be_given()
        {
            foreach (string token in SmallTalk.AllTokens())
                Assert.Matches(@"^\$vc_talk_[a-z_]+_[0-9]+$", token);
        }
    }
}
