using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace VikingColonies.Core
{
    /// <summary>
    /// A settler's last few states (0.27, for a bug report): what he did lately and how long ago. Kept in memory on the
    /// computer that runs him; a repeat of the newest isn't added.
    /// </summary>
    public sealed class StateHistory
    {
        private readonly int _capacity;
        private readonly List<string> _states = new List<string>();
        private readonly List<double> _times = new List<double>();

        public StateHistory(int capacity = 10) { _capacity = Math.Max(1, capacity); }

        public void Add(string state, double at)
        {
            state = state ?? "";
            if (_states.Count > 0 && _states[_states.Count - 1] == state) return;
            _states.Add(state);
            _times.Add(at);
            if (_states.Count > _capacity)
            {
                _states.RemoveAt(0);
                _times.RemoveAt(0);
            }
        }

        /// <summary>"Working (12 s ago)", newest first; <paramref name="words"/> turns a state into the player's words.</summary>
        public List<string> Lines(double now, Func<string, string> words)
        {
            var lines = new List<string>();
            for (int i = _states.Count - 1; i >= 0; i--)
            {
                string state = _states[i].Length == 0 ? "-" : words != null ? words(_states[i]) : _states[i];
                lines.Add($"{state} ({Ago(now - _times[i])})");
            }
            return lines;
        }

        private static string Ago(double seconds) =>
            seconds < 120 ? string.Format(CultureInfo.InvariantCulture, "{0:0} s ago", Math.Max(0, seconds))
                          : string.Format(CultureInfo.InvariantCulture, "{0:0} min ago", seconds / 60);
    }

    /// <summary>vc_bugreport (0.27): the report's opening lines.</summary>
    public static class BugReport
    {
        /// <summary>A report's date and time in the common calendar on every PC (1.5.1: a Thai PC wrote the year 2569).</summary>
        public static string Stamp(DateTime at) => at.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

        /// <summary>A time of day for the report's lines, with colons on every PC (some write 14.53.07).</summary>
        public static string Clock(DateTime at) => at.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

        public static string Header(string modVersion, string gameVersion, IEnumerable<string> otherMods, IReadOnlyCollection<string> gameCheckMissing,
            IReadOnlyCollection<string> startupFailures, string network)
        {
            var sb = new StringBuilder();
            sb.Append($"VikingColonies {modVersion} bug report\n");
            sb.Append($"Valheim {gameVersion}; {network}\n");
            List<string> mods = (otherMods ?? Enumerable.Empty<string>()).ToList();
            sb.Append("Other mods: ").Append(mods.Count == 0 ? "none" : string.Join(", ", mods)).Append('\n');
            sb.Append(gameCheckMissing == null || gameCheckMissing.Count == 0 ? "Game check: all found\n" : $"Game check: missing {string.Join("; ", gameCheckMissing)}\n");
            int failed = startupFailures?.Count ?? 0;
            sb.Append(failed == 0 ? "Start-up: every part ran\n" : $"Start-up: {failed} part{(failed == 1 ? "" : "s")} failed: {string.Join("; ", startupFailures)}\n");
            return sb.ToString();
        }
    }

    /// <summary>
    /// The Town Hall's Report a problem button (1.4): the issues page's bug form with the versions and the report's
    /// opening lines filled in through its address, and the whole report for the player to paste. Nothing is sent: the
    /// player sees it all on GitHub before pressing Submit. See docs/research/in-game-bug-report.md.
    /// </summary>
    public static class BugForm
    {
        public const string IssuesPage = "DaAlmyte/VikingColonies-issues";
        /// <summary>Windows hands a link of about 2,000 characters at most to the browser (GitHub itself refuses about 8,000).</summary>
        public const int MaxAddress = 2000;
        /// <summary>Under GitHub's 65,536 characters for an issue, with room for what the player writes.</summary>
        public const int MaxPaste = 60000;

        private const string PasteHere = "The whole report is copied: click below this line and press Ctrl+V to paste it. Drag in BepInEx/LogOutput.log too if you can.";
        private const string CutNote = "(The report is cut here: the whole of it is in BepInEx/VikingColonies-bugreport.txt.)\n";
        private const int Fixed = 3;   // PasteHere, a blank line, "Opened from": never shortened

        /// <summary>The bug form's address; a header too long for it has its longest lines shortened (the other mods, usually).</summary>
        public static string Address(string modVersion, string gameVersion, string header, string openedFrom)
        {
            string start = $"https://github.com/{IssuesPage}/issues/new?template=bug_report.yml&versions=" +
                           Uri.EscapeDataString($"VikingColonies {modVersion}, Valheim {gameVersion}") + "&files=";
            var lines = new List<string> { PasteHere, "", $"Opened from: {openedFrom}" };
            lines.AddRange((header ?? "").Replace("\r", "").Split('\n').Where(l => l.Length > 0));
            while (true)
            {
                string address = start + Uri.EscapeDataString(string.Join("\n", lines) + "\n");
                if (address.Length <= MaxAddress) return address;
                int longest = -1;
                for (int i = Fixed; i < lines.Count; i++)
                    if (longest < 0 || lines[i].Length > lines[longest].Length) longest = i;
                if (longest >= 0 && lines[longest].Length > 80)
                    lines[longest] = lines[longest].Substring(0, lines[longest].Length * 9 / 10).TrimEnd() + " …";
                else if (lines.Count > Fixed)
                    lines.RemoveAt(lines.Count - 1);
                else
                    return address.Substring(0, MaxAddress);
            }
        }

        /// <summary>What goes on the clipboard: the whole report, or as much as an issue holds, cut at a line, with a note.</summary>
        public static string Paste(string report)
        {
            report = report ?? "";
            if (report.Length <= MaxPaste) return report;
            string cut = report.Substring(0, MaxPaste - CutNote.Length - 1);
            int line = cut.LastIndexOf('\n');
            if (line > 0) cut = cut.Substring(0, line + 1);
            else cut += "\n";
            return cut + CutNote;
        }
    }

    /// <summary>
    /// One report at a time (Report a problem's spam guard): after one, the next is refused for <see cref="Seconds"/>,
    /// so quick clicks don't open a browser tab each and build the report each time. A refused click doesn't restart
    /// the wait. Times are seconds on a clock that doesn't stop or speed up with the game.
    /// </summary>
    public sealed class ReportCooldown
    {
        public const double Seconds = 10;
        private double _last = double.NegativeInfinity;

        public bool Busy(double now) => now - _last < Seconds;

        public bool TryStart(double now)
        {
            if (Busy(now)) return false;
            _last = now;
            return true;
        }

        /// <summary>Whole seconds until the next report may start (rounded up; 0 when it may now).</summary>
        public int SecondsLeft(double now) => Busy(now) ? (int)Math.Ceiling(Seconds - (now - _last)) : 0;
    }
}

namespace VikingColonies.Core
{
    /// <summary>
    /// Where the bug report file is (1.6, Report a problem's window): the path in words, shortened where it starts with
    /// the account's own folder (it can hold the Windows account's name, and players share screenshots), and how to open
    /// its folder for dragging the file into a Discord post or the bug form.
    /// </summary>
    public static class BugReportPlace
    {
        public const string FileName = "VikingColonies-bugreport.txt";

        /// <summary>
        /// The path to show: on Windows with back slashes and the account's folder as %USERPROFILE% (case-blind; also works
        /// typed into File Explorer's address bar), elsewhere as ~. Only a whole folder counts; without a home, as it was.
        /// </summary>
        public static string Shown(string path, string home, bool windows)
        {
            if (path == null) return "";
            char sep = windows ? '\\' : '/';
            string p = windows ? path.Replace('/', '\\') : path;
            string h = (windows ? (home ?? "").Replace('/', '\\') : home ?? "").TrimEnd(sep);
            StringComparison how = windows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (h.Length > 0 && p.StartsWith(h, how) && (p.Length == h.Length || p[h.Length] == sep))
                return (windows ? "%USERPROFILE%" : "~") + p.Substring(h.Length);
            if (windows)
            {
                // Proton: the game sees Wine's own user folder as home, while the mod manager's profile is in the Linux one.
                System.Text.RegularExpressions.Match linux = ProtonHome.Match(p);
                if (linux.Success && linux.Index == 0) return "~" + p.Substring(linux.Length);
            }
            return p;
        }

        /// <summary>A Linux home seen through Proton: Z:\home\name (either slash), up to the next folder or the end.</summary>
        private static readonly System.Text.RegularExpressions.Regex ProtonHome = new System.Text.RegularExpressions.Regex(
            @"Z:[\\/]home[\\/][^\\/'""\s]+(?![^\\/'""\s])", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        /// <summary>
        /// A message (why the file couldn't be written) with the account's folder shortened wherever it appears: either
        /// slash, case-blind on Windows, only where the folder's name ends (and a Proton Linux home as ~).
        /// </summary>
        public static string Scrubbed(string text, string home, bool windows)
        {
            if (text == null) return "";
            string h = (home ?? "").TrimEnd('\\', '/');
            string result = text;
            if (h.Length > 0)
            {
                string[] parts = h.Split('\\', '/');
                string pattern = string.Join(@"[\\/]", parts.Select(System.Text.RegularExpressions.Regex.Escape)) + "(?![A-Za-z0-9_-])";
                result = System.Text.RegularExpressions.Regex.Replace(result, pattern, (windows ? "%USERPROFILE%" : "~").Replace("$", "$$"),
                    windows ? System.Text.RegularExpressions.RegexOptions.IgnoreCase : System.Text.RegularExpressions.RegexOptions.None);
            }
            return windows ? ProtonHome.Replace(result, "~") : result;
        }

        /// <summary>The folder a file is in ("" when the path has none).</summary>
        public static string Folder(string file)
        {
            file = file ?? "";
            int at = Math.Max(file.LastIndexOf('\\'), file.LastIndexOf('/'));
            return at < 0 ? "" : file.Substring(0, at);
        }

        /// <summary>File Explorer's command line: the file selected, ready to drag; its folder when the file isn't there.</summary>
        public static string ExplorerArguments(string file, bool exists)
        {
            string f = (file ?? "").Replace('/', '\\');
            return exists ? "/select,\"" + f + "\"" : "\"" + Folder(f) + "\"";
        }

        /// <summary>The file's folder as a file:/// address (off Windows, for the system's file browser), each part escaped.</summary>
        public static string FolderAddress(string file)
        {
            string folder = Folder(file).Replace('\\', '/');
            string[] parts = folder.Split('/');
            for (int i = 0; i < parts.Length; i++)
            {
                bool drive = i == 0 && parts[i].Length == 2 && char.IsLetter(parts[i][0]) && parts[i][1] == ':';
                if (!drive) parts[i] = Uri.EscapeDataString(parts[i]);
            }
            return "file://" + (folder.StartsWith("/") ? "" : "/") + string.Join("/", parts) + "/";
        }
    }
}
