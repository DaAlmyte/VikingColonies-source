using System.Collections.Generic;
using System.Linq;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    public class FreeTimeTests
    {
        private static readonly LeisureChoices Everything = new LeisureChoices(seat: true, fire: true, partner: true, wet: false);

        [Theory]
        [InlineData(0.0, Leisure.Seat)]
        [InlineData(0.29, Leisure.Seat)]
        [InlineData(0.31, Leisure.Fire)]
        [InlineData(0.59, Leisure.Fire)]
        [InlineData(0.61, Leisure.Chat)]
        [InlineData(0.79, Leisure.Chat)]
        [InlineData(0.81, Leisure.Stroll)]
        [InlineData(0.89, Leisure.Stroll)]
        [InlineData(0.91, Leisure.SitDown)]
        [InlineData(0.999, Leisure.SitDown)]
        public void With_everything_at_hand_each_activity_gets_its_share(double roll, Leisure expected)
        {
            Assert.Equal(expected, FreeTimeRules.Pick(Everything, roll));
        }

        [Theory]
        [InlineData(0.0, Leisure.Stroll)]
        [InlineData(0.49, Leisure.Stroll)]
        [InlineData(0.51, Leisure.SitDown)]
        public void With_no_seat_fire_or_friend_he_strolls_or_sits_down(double roll, Leisure expected)
        {
            Assert.Equal(expected, FreeTimeRules.Pick(new LeisureChoices(false, false, false, false), roll));
        }

        // Full self-test run 6 (0.20): a jobless settler strolled to a spot in the life test's fire, sat down there and
        // burned to death. Strolls end, and settlers sit down, clear of fires; by a fire he sits 2-3 m out.
        [Fact]
        public void A_spot_in_or_against_a_fire_is_too_near_it()
        {
            var fires = new List<GroundPoint> { new GroundPoint(10f, 10f) };
            Assert.True(FreeTimeRules.TooNearAFire(new GroundPoint(10.3f, 10f), fires));
            Assert.True(FreeTimeRules.TooNearAFire(new GroundPoint(11.4f, 10f), fires));
            Assert.False(FreeTimeRules.TooNearAFire(new GroundPoint(10f, 10f + FreeTimeRules.FireSitMin), fires));
            Assert.False(FreeTimeRules.TooNearAFire(new GroundPoint(10.3f, 10f), new List<GroundPoint>()));
        }

        // Worker polish part 2 (0.21): a building short of a kept item: carrying it there is part of free time, a chore
        // (a lazy one carries half as often, as he feeds fires).
        [Fact]
        public void With_something_to_carry_he_may_carry_it()
        {
            var carry = new LeisureChoices(false, false, false, false, tend: false, carry: true);
            Assert.Equal(4, FreeTimeRules.Weight(Leisure.CarryStock, carry));
            Assert.Equal(0, FreeTimeRules.Weight(Leisure.CarryStock, new LeisureChoices(false, false, false, false)));
            Assert.Equal(Leisure.CarryStock, FreeTimeRules.Pick(carry, 0.0));
            Assert.Equal(2, Traits.LeisureWeight(Trait.Lazy, Leisure.CarryStock, 4));
        }

        [Fact]
        public void In_rain_he_never_sits_on_the_open_ground()
        {
            var wet = new LeisureChoices(false, false, false, wet: true);
            Assert.Equal(0, FreeTimeRules.Weight(Leisure.SitDown, wet));
            Assert.All(new[] { 0.0, 0.5, 0.99 }, roll => Assert.Equal(Leisure.Stroll, FreeTimeRules.Pick(wet, roll)));
        }

        [Fact]
        public void Something_to_use_is_needed_for_a_seat_a_fire_or_a_chat()
        {
            var none = new LeisureChoices(false, false, false, false);
            Assert.Equal(0, FreeTimeRules.Weight(Leisure.Seat, none));
            Assert.Equal(0, FreeTimeRules.Weight(Leisure.Fire, none));
            Assert.Equal(0, FreeTimeRules.Weight(Leisure.Chat, none));
            Assert.Equal(1, FreeTimeRules.Weight(Leisure.Stroll, none));
            Assert.Equal(0, FreeTimeRules.Weight(Leisure.None, Everything));
        }

        [Fact]
        public void A_roll_out_of_range_still_picks_something()
        {
            Assert.Equal(Leisure.Seat, FreeTimeRules.Pick(Everything, -1));
            Assert.Equal(Leisure.SitDown, FreeTimeRules.Pick(Everything, 1));
        }

        [Theory]
        [InlineData(0.0, 30f)]
        [InlineData(0.5, 60f)]
        [InlineData(1.0, 90f)]
        public void He_picks_again_every_30_to_90_seconds(double roll, float seconds)
        {
            Assert.Equal(seconds, FreeTimeRules.ChoiceSeconds(roll), 3);
        }

        [Fact]
        public void Each_activity_lasts_its_own_time()
        {
            Assert.Equal(40f, FreeTimeRules.DoSeconds(Leisure.Seat, 0), 3);
            Assert.Equal(90f, FreeTimeRules.DoSeconds(Leisure.Fire, 1), 3);
            Assert.Equal(20f, FreeTimeRules.DoSeconds(Leisure.Chat, 0), 3);
            Assert.Equal(45f, FreeTimeRules.DoSeconds(Leisure.Chat, 1), 3);
            Assert.Equal(5f, FreeTimeRules.DoSeconds(Leisure.Stroll, 0), 3);
            Assert.Equal(60f, FreeTimeRules.DoSeconds(Leisure.SitDown, 1), 3);
            Assert.Equal(4f, FreeTimeRules.GestureEverySeconds(0), 3);
            Assert.Equal(7f, FreeTimeRules.GestureEverySeconds(1), 3);
        }

        [Theory]
        [InlineData("$vc_status_jobless", true)]
        [InlineData("$vc_status_no_trees", true)]
        [InlineData("$vc_status_crops_growing", true)]
        [InlineData("$vc_status_nothing_to_pick", true)]
        [InlineData("$vc_status_no_work", true)]
        [InlineData("$vc_status_needs 12 $item_wood", false)]
        [InlineData("$vc_status_storage_full", false)]
        [InlineData("$vc_status_working", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void Only_waits_with_nothing_to_do_are_free_time(string? status, bool free)
        {
            Assert.Equal(free, FreeTimeRules.IsFreeWait(status));
        }
    }

    public class ChatPairingTests
    {
        private static ChatCandidate At(string id, float x, float z, bool free = true) => new ChatCandidate(id, new GroundPoint(x, z), free);

        [Fact]
        public void The_nearest_free_settler_within_reach_is_the_partner()
        {
            var others = new[] { At("me", 0, 0), At("far", 15, 0), At("near", 5, 0), At("busy", 2, 0, free: false), At("out", 25, 0) };
            Assert.Equal("near", ChatPairing.Partner("me", new GroundPoint(0, 0), others));
        }

        [Fact]
        public void Nobody_free_within_reach_means_no_chat()
        {
            var others = new[] { At("me", 0, 0), At("out", 21, 0), At("busy", 1, 0, free: false) };
            Assert.Equal("", ChatPairing.Partner("me", new GroundPoint(0, 0), others));
            Assert.Equal("", ChatPairing.Partner("me", new GroundPoint(0, 0), new ChatCandidate[0]));
        }

        [Fact]
        public void The_one_with_the_lower_id_asks()
        {
            Assert.True(ChatPairing.Asks("a1", "b2"));
            Assert.False(ChatPairing.Asks("b2", "a1"));
            Assert.False(ChatPairing.Asks("a1", "a1"));
        }

        [Fact]
        public void They_meet_halfway_a_little_apart_facing_each_other()
        {
            ChatMeeting meeting = ChatPairing.Meeting(new GroundPoint(0, 0), new GroundPoint(10, 0));
            Assert.Equal(5f - FreeTimeRules.ChatGap / 2f, meeting.First.X, 3);
            Assert.Equal(5f + FreeTimeRules.ChatGap / 2f, meeting.Second.X, 3);
            Assert.Equal(0f, meeting.First.Z, 3);
            Assert.Equal(FreeTimeRules.ChatGap, ColonyRules.Distance(meeting.First, meeting.Second), 3);
        }

        [Fact]
        public void Two_on_the_same_spot_still_get_two_spots()
        {
            ChatMeeting meeting = ChatPairing.Meeting(new GroundPoint(3, 3), new GroundPoint(3, 3));
            Assert.Equal(FreeTimeRules.ChatGap, ColonyRules.Distance(meeting.First, meeting.Second), 3);
        }
    }

    public class SmallTalkTests
    {
        private static System.Func<double> Always(double value) => () => value;

        [Fact]
        public void A_death_comes_first_when_the_dice_say_so()
        {
            var context = new TalkContext { DeadName = "Ingrid", Hungry = true, Wet = true };
            Assert.Equal(TalkTopic.Mourn, SmallTalk.Topic(context, Always(0.1)));
        }

        [Fact]
        public void A_topic_that_doesn_t_apply_is_skipped()
        {
            var context = new TalkContext { Wet = true };
            Assert.Equal(TalkTopic.Weather, SmallTalk.Topic(context, Always(0.1)));
        }

        [Fact]
        public void When_every_roll_says_no_he_talks_about_anything()
        {
            var context = new TalkContext { DeadName = "Ingrid", Hungry = true, Wet = true, Jobless = true };
            Assert.Equal(TalkTopic.General, SmallTalk.Topic(context, Always(0.9)));
        }

        [Fact]
        public void The_topics_come_in_the_spec_s_order()
        {
            var rolls = new Queue<double>(new[] { 0.9, 0.9, 0.9, 0.1 });   // no to mourning, hunger, weather; yes to new here
            var context = new TalkContext { DeadName = "Ingrid", Hungry = true, Wet = true, IsNewToday = true, NewcomerName = "Ulf" };
            Assert.Equal(TalkTopic.NewHere, SmallTalk.Topic(context, () => rolls.Dequeue()));
        }

        [Fact]
        public void His_wait_gives_its_own_lines_and_the_jobless_have_theirs()
        {
            Assert.Equal(TalkTopic.Wait, SmallTalk.Topic(new TalkContext { WaitStatus = "$vc_status_no_trees" }, Always(0.1)));
            Assert.Equal(TalkTopic.Jobless, SmallTalk.Topic(new TalkContext { Jobless = true, WaitStatus = "$vc_status_jobless" }, Always(0.1)));
            Assert.Equal("$vc_talk_wait_no_trees_2", SmallTalk.Line(TalkTopic.Wait, "$vc_status_no_trees", 1).Token);
        }

        [Fact]
        public void Every_topic_has_at_least_four_lines_and_there_are_twelve_general_ones()
        {
            foreach (TalkTopic topic in System.Enum.GetValues(typeof(TalkTopic)).Cast<TalkTopic>().Where(t => t != TalkTopic.Wait))
                Assert.True(SmallTalk.Count(topic, "") >= 4, topic.ToString());
            foreach (string wait in new[] { "$vc_status_no_trees", "$vc_status_crops_growing", "$vc_status_nothing_to_pick", "$vc_status_no_work" })
                Assert.True(SmallTalk.Count(TalkTopic.Wait, wait) >= 4, wait);
            Assert.True(SmallTalk.Count(TalkTopic.General, "") >= 12);
        }

        [Fact]
        public void The_line_names_who_or_what_it_is_about()
        {
            var context = new TalkContext { DeadName = "Ingrid", NewcomerName = "Ulf", BuiltToken = "$vc_hut_farm" };
            TalkLine mourn = SmallTalk.Line(TalkTopic.Mourn, "", 0, context);
            Assert.Equal("$vc_talk_mourn_1", mourn.Token);
            Assert.Equal("Ingrid", mourn.Arg);
            Assert.Equal("Ulf", SmallTalk.Line(TalkTopic.Newcomer, "", 0, context).Arg);
            Assert.Equal("$vc_hut_farm", SmallTalk.Line(TalkTopic.Built, "", 0, context).Arg);
            Assert.Equal("", SmallTalk.Line(TalkTopic.General, "", 0, context).Arg);
        }

        [Fact]
        public void The_seed_picks_the_same_line_for_everyone_and_wraps_round()
        {
            Assert.Equal(SmallTalk.Seed("abc", 5), SmallTalk.Seed("abc", 5));
            Assert.NotEqual(SmallTalk.Seed("abc", 5), SmallTalk.Seed("abc", 6));
            int count = SmallTalk.Count(TalkTopic.General, "");
            Assert.Equal(SmallTalk.Line(TalkTopic.General, "", 0).Token, SmallTalk.Line(TalkTopic.General, "", count).Token);
            Assert.Equal(SmallTalk.Line(TalkTopic.General, "", 1).Token, SmallTalk.Line(TalkTopic.General, "", -count + 1).Token);
        }

        [Fact]
        public void The_saga_tells_who_died_lately_who_came_and_what_was_built_today()
        {
            var saga = new SagaLog();
            saga.Add(new SagaEntry(7, "died", "Old", "$vc_death_monster", "Troll"));
            saga.Add(new SagaEntry(9, "died", "Ingrid", "$vc_death_tree", ""));
            saga.Add(new SagaEntry(10, "arrived", "Ulf"));
            saga.Add(new SagaEntry(10, "built", "$vc_hut_farm"));
            TalkContext context = SmallTalk.FromSaga(saga, 10, "Astrid");
            Assert.Equal("Ingrid", context.DeadName);
            Assert.Equal("Ulf", context.NewcomerName);
            Assert.False(context.IsNewToday);
            Assert.Equal("$vc_hut_farm", context.BuiltToken);
        }

        [Fact]
        public void A_death_three_days_ago_is_no_longer_talked_about_and_the_newcomer_talks_about_himself()
        {
            var saga = new SagaLog();
            saga.Add(new SagaEntry(7, "died", "Old", "$vc_death_monster", "Troll"));
            saga.Add(new SagaEntry(10, "arrived", "Ulf"));
            TalkContext context = SmallTalk.FromSaga(saga, 10, "Ulf");
            Assert.Equal("", context.DeadName);
            Assert.True(context.IsNewToday);
            Assert.Equal("", context.NewcomerName);
        }
    }

    public class GreetingsTests
    {
        [Fact]
        public void He_greets_a_player_once_a_day_when_near()
        {
            var memory = new GreetMemory();
            Assert.False(memory.ShouldGreet("s1", 42, 10, 7f));
            Assert.True(memory.ShouldGreet("s1", 42, 10, 5f));
            memory.Mark("s1", 42, 10);
            Assert.False(memory.ShouldGreet("s1", 42, 10, 2f));
            Assert.True(memory.ShouldGreet("s1", 43, 10, 2f));
            Assert.True(memory.ShouldGreet("s2", 42, 10, 2f));
            Assert.True(memory.ShouldGreet("s1", 42, 11, 2f));
        }
    }

    public class ActivityWordsTests
    {
        [Fact]
        public void Each_activity_has_its_words()
        {
            Assert.Equal("$vc_doing_seat", ActivityWords.Token(Leisure.Seat));
            Assert.Equal("$vc_doing_fire", ActivityWords.Token(Leisure.Fire));
            Assert.Equal("$vc_doing_chat", ActivityWords.Token(Leisure.Chat));
            Assert.Equal("$vc_doing_stroll", ActivityWords.Token(Leisure.Stroll));
            Assert.Equal("$vc_doing_sitdown", ActivityWords.Token(Leisure.SitDown));
            Assert.Equal("$vc_doing_carrying", ActivityWords.Token(Leisure.CarryStock));
            Assert.Equal("", ActivityWords.Token(Leisure.None));
        }

        [Fact]
        public void What_he_does_and_with_whom_is_saved_as_one_value()
        {
            string saved = ActivityWords.Encode("$vc_doing_chat", "Bjorn");
            Assert.True(ActivityWords.Decode(saved, out string token, out string partner));
            Assert.Equal("$vc_doing_chat", token);
            Assert.Equal("Bjorn", partner);
            Assert.False(ActivityWords.Decode("", out _, out _));
            Assert.True(ActivityWords.Decode("$vc_doing_seat", out token, out partner));
            Assert.Equal("", partner);
        }

        [Fact]
        public void The_hover_puts_it_in_brackets_after_the_status()
        {
            Assert.Equal("Crops growing (on a bench)", ActivityWords.Hover("Crops growing", "on a bench"));
            Assert.Equal("on a bench", ActivityWords.Hover("", "on a bench"));
            Assert.Equal("Crops growing", ActivityWords.Hover("Crops growing", ""));
        }
    }
}
