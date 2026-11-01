using System.Text.RegularExpressions;

namespace VikingColonies.Core
{
    /// <summary>
    /// A bug report's line with its folders shortened: BepInEx's, the game's and the home folder (a path holds the Windows
    /// account's name), in any case and either slash, and any other X:\Users\&lt;name&gt; too (the security sweep of 27 Oct
    /// 2026: only the exact spelling was shortened before).
    /// </summary>
    public static class ReportPaths
    {
        private const RegexOptions Blind = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;   // the same in Turkish (I/ı)
        private static readonly Regex AnyUser = new Regex(@"[A-Za-z]:[\\/]Users[\\/][^\\/\r\n""'<>|]+", Blind);

        public static string Scrub(string line, string bepInEx, string game, string home)
        {
            if (string.IsNullOrEmpty(line)) return "";
            line = Shorten(line, bepInEx, "<BepInEx>");   // first: it is usually inside the home folder
            line = Shorten(line, game, "<game>");
            line = Shorten(line, home, "~");
            return AnyUser.Replace(line, "~");
        }

        /// <summary>
        /// The folder, whichever case and slashes the line spells it with, where its name ends (E:\Home\me, not
        /// E:\Home\meadow). A drive or a root alone ("/", "D:\") shortens nothing (the sweep's review).
        /// </summary>
        private static string Shorten(string line, string folder, string with)
        {
            string trimmed = (folder ?? "").Trim().TrimEnd('\\', '/');
            if (trimmed.Length == 0 || (trimmed.Length == 2 && trimmed[1] == ':')) return line;
            var pattern = new System.Text.StringBuilder();
            foreach (char c in trimmed)
                pattern.Append(c == '\\' || c == '/' ? @"[\\/]" : Regex.Escape(c.ToString()));
            pattern.Append(@"(?![^\\/\r\n""'<>|\s])");
            return Regex.Replace(line, pattern.ToString(), with.Replace("$", "$$"), Blind);
        }
    }
}
