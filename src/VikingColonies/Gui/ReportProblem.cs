using UnityEngine;
using VikingColonies.Core;
using VikingColonies.DevTools;

namespace VikingColonies.Gui
{
    /// <summary>
    /// Report a problem (1.4; its window since 1.6): writes the bug report file, then, on the player's pick, copies the
    /// whole report and opens the issues page's bug form (the versions and the report's first lines filled in) or the
    /// Discord server (#bug-reports), or opens the file's folder for dragging it in. Nothing is sent: the player pastes,
    /// writes what happened and posts it themselves. See docs/research/in-game-bug-report.md and
    /// docs/superpowers/specs/2026-10-25-report-problem-discord-design.md.
    /// </summary>
    internal static class ReportProblem
    {
        internal enum Way { Form, Discord, Folder }

        private static readonly ReportCooldown Cooldown = new ReportCooldown();
        /// <summary>Show the file's own guard (the review): right after the form or Discord, the player opens the folder to drag the file in.</summary>
        private static readonly ReportCooldown FolderCooldown = new ReportCooldown();
        /// <summary>The report the window wrote on opening. 'vc_bugreport form' meanwhile rewrites the file, not this: harmless, a moment older.</summary>
        private static BugReportCommand.Report _prepared;

        /// <summary>A page was opened in the last 10 seconds: the bug form's and Discord's buttons are greyed and another press is refused.</summary>
        internal static bool Busy => Cooldown.Busy(Time.realtimeSinceStartup);

        /// <summary>The folder was opened in the last 10 seconds: Show the file is greyed.</summary>
        internal static bool FolderBusy => FolderCooldown.Busy(Time.realtimeSinceStartup);

        /// <summary>Where the file is, in words (the account's folder shortened, as in the report itself).</summary>
        internal static string ShownPath => BugReportPlace.Shown(BugReportCommand.FilePath, Home, OnWindows);

        /// <summary>A message (why the file couldn't be written) with the account's folder shortened.</summary>
        internal static string Shown(string message) => BugReportPlace.Scrubbed(message, Home, OnWindows);

        private static string Home => System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);

        private static bool OnWindows => Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor;

        /// <summary>The window's opening: writes the report file afresh and keeps the report for its buttons.</summary>
        internal static BugReportCommand.Report Prepare(string openedFrom)
        {
            _prepared = BugReportCommand.Make();
            Log.Event($"Report a problem from {openedFrom}: the window opened, the report " +
                      (_prepared.FileError == null ? $"written to {ShownPath}" : $"not written: {Shown(_prepared.FileError)}") + $" ({_prepared.Loaded} here)");
            return _prepared;
        }

        /// <summary>
        /// 'vc_bugreport form': straight to the bug form, as the button did before 1.6; returns what to tell the player.
        /// <paramref name="test"/> (the in-game runner, test worlds only): the address and the length of the paste go to
        /// the log, the browser and the clipboard are left alone. Within 10 seconds of the last page it does nothing and
        /// says when the next may start (the spam guard).
        /// </summary>
        internal static string Run(string openedFrom, bool test = false)
        {
            if (!TryStart(Cooldown, openedFrom, out string wait)) return wait;
            return Form(BugReportCommand.Make(), openedFrom, test);
        }

        /// <summary>One of the window's buttons, under the same guard; returns what to tell the player.</summary>
        internal static string Press(Way way, string openedFrom, bool test)
        {
            if (!TryStart(way == Way.Folder ? FolderCooldown : Cooldown, openedFrom, out string wait)) return wait;
            BugReportCommand.Report report = _prepared ?? Prepare(openedFrom);
            switch (way)
            {
                case Way.Discord: return Discord(report, openedFrom, test);
                case Way.Folder: return Folder(openedFrom, test);
                default: return Form(report, openedFrom, test);
            }
        }

        private static bool TryStart(ReportCooldown guard, string openedFrom, out string wait)
        {
            double now = Time.realtimeSinceStartup;
            wait = null;
            if (guard.TryStart(now)) return true;
            wait = string.Format(L("$vc_report_wait"), guard.SecondsLeft(now));
            Log.Event($"Report a problem from {openedFrom}: refused, one was just made ({guard.SecondsLeft(now)} s to wait)");
            return false;
        }

        private static bool IsTest(bool test) => test && ZNet.instance != null && TestWorlds.IsTestWorld(ZNet.instance.GetWorldName());

        private static string Form(BugReportCommand.Report report, string openedFrom, bool test)
        {
            string address = BugForm.Address(Plugin.PluginVersion, global::Version.GetVersionString(), report.Header, openedFrom);
            string paste = BugForm.Paste(report.Text);
            if (IsTest(test))
            {
                Log.Event($"Report a problem (test): the form {address} ({address.Length} characters); {paste.Length} characters to paste; " +
                          $"{report.Loaded} here; the file {(report.FileError == null ? "written" : "not written: " + Shown(report.FileError))}");
                return L("$vc_report_opened");
            }
            GUIUtility.systemCopyBuffer = paste;
            Application.OpenURL(address);
            Log.Event($"Report a problem from {openedFrom}: the bug form opened, {paste.Length} characters copied ({report.Loaded} here)" +
                      (report.FileError == null ? "" : $"; the file couldn't be written: {Shown(report.FileError)}"));
            return L("$vc_report_opened");
        }

        private static string Discord(BugReportCommand.Report report, string openedFrom, bool test)
        {
            string paste = BugForm.Paste(report.Text);
            if (IsTest(test))
            {
                Log.Event($"Report a problem (test): Discord would open {FeedbackLinks.Discord}; {paste.Length} characters to paste");
                return L("$vc_report_discord_opened");
            }
            GUIUtility.systemCopyBuffer = paste;
            Application.OpenURL(FeedbackLinks.Discord);
            Log.Event($"Report a problem from {openedFrom}: Discord opened, {paste.Length} characters copied");
            return L("$vc_report_discord_opened");
        }

        /// <summary>File Explorer with the file selected (its folder if the file isn't there); off Windows, the folder.</summary>
        private static string Folder(string openedFrom, bool test)
        {
            string file = BugReportCommand.FilePath;
            bool exists = System.IO.File.Exists(file);
            string home = Home;
            if (IsTest(test))
            {
                Log.Event("Report a problem (test): " + (OnWindows
                    ? $"File Explorer would open {BugReportPlace.ExplorerArguments(BugReportPlace.Shown(file, home, true), exists)}"
                    : $"the folder would open {BugReportPlace.FolderAddress(BugReportPlace.Shown(file, home, false))}"));
                return L(exists ? "$vc_report_folder_opened" : "$vc_report_folder_no_file");
            }
            bool opened = false;
            if (OnWindows)
            {
                try
                {
                    using (System.Diagnostics.Process.Start("explorer.exe", BugReportPlace.ExplorerArguments(file, exists))) { }
                    opened = true;
                }
                catch (System.Exception e)
                {
                    Log.Warn($"Report a problem: File Explorer didn't start ({e.Message}); opening the folder's address instead");
                }
            }
            if (!opened) Application.OpenURL(BugReportPlace.FolderAddress(file));
            Log.Event($"Report a problem from {openedFrom}: the folder opened{(exists ? ", the file selected" : ", the file isn't there")}");
            return L(exists ? "$vc_report_folder_opened" : "$vc_report_folder_no_file");
        }

        private static string L(string token) => Localization.instance.Localize(token);
    }
}
