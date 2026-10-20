using System.IO;
using System.Linq;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>Every line small talk can pick is in the English translations (else the game shows "[vc_talk_…]").</summary>
    public class TalkTranslationTests
    {
        private static string Translations()
        {
            DirectoryInfo? dir = new DirectoryInfo(System.AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "VikingColonies.slnx"))) dir = dir.Parent;
            Assert.NotNull(dir);
            return File.ReadAllText(Path.Combine(dir.FullName, "src", "VikingColonies", "Config", "Translations.cs"));
        }

        [Fact]
        public void Every_small_talk_line_has_its_words()
        {
            string text = Translations();
            string[] waits = { "$vc_status_no_trees", "$vc_status_crops_growing", "$vc_status_nothing_to_pick", "$vc_status_no_work" };
            foreach (TalkTopic topic in System.Enum.GetValues(typeof(TalkTopic)).Cast<TalkTopic>())
                foreach (string wait in topic == TalkTopic.Wait ? waits : new[] { "" })
                    for (int seed = 0; seed < SmallTalk.Count(topic, wait); seed++)
                    {
                        string token = SmallTalk.Line(topic, wait, seed).Token.Substring(1);
                        Assert.Contains("\"" + token + "\"", text);
                    }
        }

        [Fact]
        public void Every_origin_trait_and_trade_has_its_words()
        {
            string text = Translations();
            foreach (string origin in Origins.All) Assert.Contains("\"" + origin.Substring(1) + "\"", text);
            foreach (Trait trait in System.Enum.GetValues(typeof(Trait)).Cast<Trait>()) Assert.Contains("\"" + Traits.Token(trait).Substring(1) + "\"", text);
            foreach (string job in new[] { SaveKeys.LumberjackJob, SaveKeys.BuilderJob, SaveKeys.ForagerJob, SaveKeys.FarmerJob, SaveKeys.GuardJob })
            {
                Assert.Contains("\"vc_calling_" + job + "\"", text);
                Assert.Contains("\"vc_good_at_" + job + "\"", text);
            }
            Assert.Contains("\"vc_calling_" + Callings.None + "\"", text);
            foreach (string token in new[] { "vc_founders_coming", "vc_newcomer_coming", "vc_saga_came", "vc_status_arriving", "vc_here_days",
                         "vc_came_today", "vc_home_bed", "vc_no_home" })
                Assert.Contains("\"" + token + "\"", text);
        }

        [Fact]
        public void Every_activity_and_the_greeting_have_their_words()
        {
            string text = Translations();
            foreach (Leisure leisure in new[] { Leisure.Seat, Leisure.Fire, Leisure.Chat, Leisure.Stroll, Leisure.SitDown, Leisure.Evening, Leisure.TendFire })
                Assert.Contains("\"" + ActivityWords.Token(leisure).Substring(1) + "\"", text);
            Assert.Contains("\"vc_greet\"", text);
            Assert.Contains("\"vc_tip_freetime\"", text);
            Assert.Contains("\"vc_tip_evening\"", text);
            Assert.Contains("\"vc_doing_eating\"", text);
            Assert.Contains("\"vc_status_eating\"", text);
        }
    }
}
