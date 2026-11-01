using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>Small talk at a pastime (1.6): each pastime has its own lines, in plain words, never calling a settler "he".</summary>
    public class PastimeTalkTests
    {
        private static TalkContext At(Pastime p, string with = "", bool listening = false) =>
            new TalkContext { DeadName = "", NewcomerName = "", BuiltToken = "", WaitStatus = "", Job = "", BossToken = "", Pastime = p, PastimeWith = with, Listening = listening };

        private static string Translations()
        {
            DirectoryInfo? dir = new DirectoryInfo(System.AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "VikingColonies.slnx"))) dir = dir.Parent;
            Assert.NotNull(dir);
            return File.ReadAllText(Path.Combine(dir!.FullName, "src", "VikingColonies", "Config", "Translations.cs"));
        }

        [Fact]
        public void Each_pastime_has_lines()
        {
            foreach (Pastime p in PastimeRules.All)
                Assert.True(SmallTalk.Count(TalkTopic.Pastime, At(p)) >= 4, p + " has fewer than 4 lines");
            Assert.True(SmallTalk.Count(TalkTopic.Pastime, At(Pastime.Story, "Ulf", listening: true)) >= 4, "a story's listeners have fewer than 4 lines");
            Assert.Equal(0, SmallTalk.Count(TalkTopic.Pastime, At(Pastime.None)));
        }

        [Fact]
        public void Pastime_lines_never_call_a_settler_he()
        {
            string text = Translations();
            var he = new Regex(@"\b(he|him|his|himself)\b", RegexOptions.IgnoreCase);
            var tokens = SmallTalk.AllTokens().Where(t => t.StartsWith("$vc_talk_pastime_")).ToList();
            Assert.NotEmpty(tokens);
            foreach (string token in tokens)
            {
                Match line = Regex.Match(text, "\\{ \"" + Regex.Escape(token.Substring(1)) + "\", \"((?:[^\"\\\\]|\\\\.)*)\" \\}");
                Assert.True(line.Success, token + " has no words");
                Assert.False(he.IsMatch(line.Groups[1].Value), token + ": \"" + line.Groups[1].Value + "\"");
            }
        }

        [Fact]
        public void A_pastime_line_is_chosen_from_its_own_set()
        {
            foreach (Pastime p in PastimeRules.All)
                for (int seed = 0; seed < 12; seed++)
                    Assert.StartsWith("$vc_talk_pastime_" + p.ToString().ToLowerInvariant() + "_", SmallTalk.Line(TalkTopic.Pastime, "", seed, At(p)).Token);
            for (int seed = 0; seed < 12; seed++)
                Assert.StartsWith("$vc_talk_pastime_listening_", SmallTalk.Line(TalkTopic.Pastime, "", seed, At(Pastime.Story, "Ulf", listening: true)).Token);
        }

        [Fact]
        public void The_partner_teller_or_animal_is_named()
        {
            Assert.Equal("Astrid", SmallTalk.Line(TalkTopic.Pastime, "", 1, At(Pastime.BoardGame, "Astrid")).Arg);
            Assert.Equal("Ulf", SmallTalk.Line(TalkTopic.Pastime, "", 1, At(Pastime.Story, "Ulf", listening: true)).Arg);
        }

        [Fact]
        public void At_a_pastime_they_talk_about_it_half_the_time()
        {
            Assert.Equal(TalkTopic.Pastime, SmallTalk.Topic(At(Pastime.Archery), () => 0.2));
            Assert.Equal(TalkTopic.General, SmallTalk.Topic(At(Pastime.Archery), () => 0.8));
            Assert.Equal(TalkTopic.General, SmallTalk.Topic(At(Pastime.None), () => 0.2));
        }

        [Fact]
        public void A_death_or_hunger_comes_before_the_pastime()
        {
            TalkContext mourning = At(Pastime.Toast, "Bjorn");
            mourning.DeadName = "Ingrid";
            Assert.Equal(TalkTopic.Mourn, SmallTalk.Topic(mourning, () => 0.2));
            TalkContext hungry = At(Pastime.Toast, "Bjorn");
            hungry.Hungry = true;
            Assert.Equal(TalkTopic.Hungry, SmallTalk.Topic(hungry, () => 0.2));
        }

        [Fact]
        public void The_hover_words_say_which_pastime()
        {
            Assert.Equal(Pastime.Archery, SmallTalk.PastimeOf(ActivityWords.Token(Pastime.Archery), out bool listening));
            Assert.False(listening);
            Assert.Equal(Pastime.Story, SmallTalk.PastimeOf(ActivityWords.Listening, out listening));
            Assert.True(listening);
            Assert.Equal(Pastime.None, SmallTalk.PastimeOf(ActivityWords.Token(Leisure.Seat), out listening));
            Assert.Equal(Pastime.None, SmallTalk.PastimeOf("", out listening));
            foreach (Pastime p in PastimeRules.All) Assert.Equal(p, SmallTalk.PastimeOf(ActivityWords.Token(p), out _));
        }
    }
}
