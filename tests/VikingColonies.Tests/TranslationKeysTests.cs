using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace VikingColonies.Tests
{
    // The mod's English texts are one dictionary: a key written twice compiles, but throws when the game starts and then
    // none of the mod's texts load (the cloud session of 2026-10-18 added "vc_and" a second time; found in the game).
    public class TranslationKeysTests
    {
        private static string RepoFile(params string[] parts)
        {
            DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "VikingColonies.slnx"))) dir = dir.Parent;
            Assert.NotNull(dir);
            return Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
        }

        [Fact]
        public void Every_text_key_is_written_once()
        {
            string source = File.ReadAllText(RepoFile("src", "VikingColonies", "Config", "Translations.cs"));
            string[] keys = Regex.Matches(source, "\\{\\s*\"(vc_[A-Za-z0-9_]+)\"\\s*,").Cast<Match>().Select(m => m.Groups[1].Value).ToArray();
            Assert.True(keys.Length > 100, $"only {keys.Length} keys found: has the file moved?");
            string[] twice = keys.GroupBy(k => k).Where(g => g.Count() > 1).Select(g => g.Key).ToArray();
            Assert.True(twice.Length == 0, "written more than once: " + string.Join(", ", twice));
        }
    }
}
