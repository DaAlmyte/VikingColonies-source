using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>
    /// Settlers are never "he" in what players read (the project's words rule): the settings, the bug report (pasted into
    /// public issues), the console's help. The review round of 2026-10-23 found the bug report's "another player's game runs
    /// him: his recent states".
    /// </summary>
    public class PlayerWordsTests
    {
        private static readonly Regex Literal = new Regex("\"((?:[^\"\\\\]|\\\\.)*)\"");
        private static readonly Regex HeWords = new Regex(@"\b(he|him|his|himself)\b", RegexOptions.IgnoreCase);

        private static string Src()
        {
            DirectoryInfo? dir = new DirectoryInfo(System.AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "VikingColonies.slnx"))) dir = dir.Parent;
            Assert.NotNull(dir);
            return Path.Combine(dir!.FullName, "src", "VikingColonies");
        }

        /// <summary>The text a line shows players, leaving out comments and log lines (the log is for us).</summary>
        private static IEnumerable<string> Shown(string line)
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith("//") || line.Contains("Log.")) yield break;
            foreach (Match m in Literal.Matches(line)) yield return m.Groups[1].Value;
        }

        private static List<string> Offending(IEnumerable<string> files, System.Func<string, bool>? only = null) =>
            files.SelectMany(f => File.ReadAllLines(f).Select((line, i) => (f, i, line)))
                .Where(x => only == null || only(x.line))
                .SelectMany(x => Shown(x.line).Where(t => HeWords.IsMatch(t)).Select(t => $"{Path.GetFileName(x.f)}:{x.i + 1}: {t}"))
                .ToList();

        [Fact]
        public void The_settings_and_the_bug_report_never_call_a_settler_he()
        {
            string src = Src();
            var files = Directory.GetFiles(Path.Combine(src, "Config"), "*.cs").Where(f => !f.EndsWith("Translations.cs"))
                .Concat(new[] { Path.Combine(src, "Debug", "DevReport.cs") });
            List<string> found = Offending(files);
            Assert.True(found.Count == 0, string.Join("\n", found));
        }

        /// <summary>
        /// Each statement that starts on a line holding one of <paramref name="starts"/>, whole (up to its ';'): a help text or
        /// a console answer runs over several lines (its review: a test of first lines only missed vc_pose and vc_watch).
        /// </summary>
        private static IEnumerable<(string file, int line, string text)> Statements(string file, string[] starts)
        {
            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                if (!starts.Any(lines[i].Contains) || lines[i].Trim().StartsWith("//")) continue;
                for (int j = i; j < lines.Length; j++)
                {
                    yield return (file, j + 1, lines[j]);
                    if (lines[j].TrimEnd().EndsWith(";")) break;
                }
            }
        }

        [Fact]
        public void The_console_help_and_answers_never_call_a_settler_he()
        {
            List<string> found = Directory.GetFiles(Src(), "*.cs", SearchOption.AllDirectories)
                .SelectMany(f => Statements(f, new[] { "Help =>", "Print(" }))
                .SelectMany(x => Literal.Matches(x.text).Cast<Match>().Select(m => m.Groups[1].Value).Where(t => HeWords.IsMatch(t))
                    .Select(t => $"{Path.GetFileName(x.file)}:{x.line}: {t}"))
                .Distinct().ToList();
            Assert.True(found.Count == 0, string.Join("\n", found));
        }

        [Fact]   // the review round: test worlds are names starting with VCTest; four texts still said the name "contains 'test'"
        public void Test_worlds_are_described_as_names_starting_with_VCTest()
        {
            var old = new Regex(@"contain(s|ing)?\s+['""]?test\b", RegexOptions.IgnoreCase);
            List<string> found = Directory.GetFiles(Src(), "*.cs", SearchOption.AllDirectories)
                .SelectMany(f => File.ReadAllLines(f).Select((line, i) => (f, i, line)))
                .Where(x => !x.line.Trim().StartsWith("//"))
                .SelectMany(x => Literal.Matches(x.line).Cast<Match>().Where(m => old.IsMatch(m.Groups[1].Value))
                    .Select(m => $"{Path.GetFileName(x.f)}:{x.i + 1}: {m.Groups[1].Value}"))
                .ToList();
            Assert.True(found.Count == 0, string.Join("\n", found));
        }
    }
}
