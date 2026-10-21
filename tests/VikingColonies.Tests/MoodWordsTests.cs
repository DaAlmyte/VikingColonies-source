using System.IO;
using System.Linq;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>Settler life part 4 (0.17): how mood is shown, and that every word of it is translated.</summary>
    public class MoodWordsTests
    {
        private static string Translations()
        {
            DirectoryInfo? dir = new DirectoryInfo(System.AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "VikingColonies.slnx"))) dir = dir.Parent;
            Assert.NotNull(dir);
            return File.ReadAllText(Path.Combine(dir.FullName, "src", "VikingColonies", "Config", "Translations.cs"));
        }

        [Fact]
        public void A_cheerful_settler_says_cheerful_things_and_a_grumpy_one_grumbles()
        {
            Assert.Equal(TalkTopic.Cheerful, SmallTalk.Topic(new TalkContext { Trait = Trait.Cheerful }, () => 0.1));
            Assert.Equal(TalkTopic.Grumpy, SmallTalk.Topic(new TalkContext { Trait = Trait.Grumpy }, () => 0.1));
            Assert.Equal(TalkTopic.General, SmallTalk.Topic(new TalkContext { Trait = Trait.Brave }, () => 0.1));
            Assert.Equal(TalkTopic.General, SmallTalk.Topic(new TalkContext { Trait = Trait.Cheerful }, () => 0.9));
            // News comes first: a death is talked about before his temper.
            Assert.Equal(TalkTopic.Mourn, SmallTalk.Topic(new TalkContext { Trait = Trait.Grumpy, DeadName = "Ulf" }, () => 0.1));
            Assert.Equal("$vc_talk_cheerful_1", SmallTalk.Line(TalkTopic.Cheerful, "", 0).Token);
            Assert.Equal(6, SmallTalk.Count(TalkTopic.Grumpy, ""));   // 4 until 1.4.2
        }

        [Fact]
        public void An_unhappy_settler_s_words_are_his_worst_reason()
        {
            Assert.Equal("$vc_mood_words_hungry", MoodLevels.WordsToken(new MoodReason("$vc_mood_hungry", -20)));
            Assert.Equal("$vc_mood_words_death", MoodLevels.WordsToken(new MoodReason("$vc_mood_death", -10, "Ingrid")));
            Assert.True(MoodLevels.ShowsWords(MoodLevel.Unhappy));
            Assert.True(MoodLevels.ShowsWords(MoodLevel.Miserable));
            Assert.False(MoodLevels.ShowsWords(MoodLevel.Content));
        }

        [Fact]
        public void His_reasons_read_as_a_line()
        {
            var reasons = new[] { new MoodReason("$vc_mood_home", 5), new MoodReason("$vc_mood_death", -10, "Ingrid") };
            string line = MoodLevels.ReasonsLine(reasons, token => token == "$vc_mood_home" ? "home" : "{0} died");
            Assert.Equal("+5 home, -10 Ingrid died", line);   // a plain minus: the game's font may lack "−"
            Assert.Equal("", MoodLevels.ReasonsLine(new MoodReason[0], t => t));
        }

        [Fact]
        public void Every_word_of_mood_is_translated()
        {
            string text = Translations();
            foreach (string token in Mood.ReasonTokens)
            {
                Assert.Contains("\"" + token.Substring(1) + "\"", text);
                if (Mood.TakesAway(token)) Assert.Contains("\"" + MoodLevels.WordsToken(new MoodReason(token, -1)).Substring(1) + "\"", text);
            }
            foreach (MoodLevel level in System.Enum.GetValues(typeof(MoodLevel)).Cast<MoodLevel>())
                Assert.Contains("\"" + MoodLevels.Token(level).Substring(1) + "\"", text);
            foreach (string token in new[] { "vc_mood_details", "vc_colony_mood", "vc_newcomer_unhappy", "vc_tip_mood", "vc_tip_mood_topic", "vc_saga_repelled" })
                Assert.Contains("\"" + token + "\"", text);
        }

        [Fact]
        public void Every_reason_the_mood_can_give_is_listed()
        {
            var all = new MoodInputs
            {
                HasHome = true, BedComfort = 9, AteToday = true, DistinctFoods = 4, EveningByFire = true, EveningCompany = true, InCalling = true,
                JoblessDays = 3, Death = "Ulf", HitYesterday = true, RaidRepelled = true, Trait = Trait.Cheerful,
            };
            var other = new MoodInputs { Homeless = true, Hungry = true, SameFoodRunning = true, Trait = Trait.Grumpy };
            foreach (MoodReason r in Mood.Compute(all).Reasons.Concat(Mood.Compute(other).Reasons))
            {
                Assert.Contains(r.Token, Mood.ReasonTokens);
                Assert.Equal(r.Change < 0, Mood.TakesAway(r.Token));
            }
        }
    }
}
