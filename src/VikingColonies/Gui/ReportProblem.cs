using UnityEngine;
using VikingColonies.Core;
using VikingColonies.DevTools;

namespace VikingColonies.Gui
{
    /// <summary>
    /// Report a problem (1.4), one click from the Town Hall's window or 'vc_bugreport form': writes the bug report file,
    /// copies the whole report and opens the issues page's bug form with the versions and the report's first lines
    /// filled in. Nothing is sent: the player pastes, writes what happened and presses Submit on GitHub.
    /// See docs/research/in-game-bug-report.md.
    /// </summary>
    internal static class ReportProblem
    {
        private static readonly ReportCooldown Cooldown = new ReportCooldown();

        /// <summary>A report was made in the last 10 seconds: the button is greyed and another click is refused.</summary>
        internal static bool Busy => Cooldown.Busy(Time.realtimeSinceStartup);

        /// <summary>
        /// Does it; returns what to tell the player. <paramref name="test"/> (the in-game runner, test worlds only): the
        /// address and the length of the paste go to the log, the browser and the clipboard are left alone. Within 10
        /// seconds of the last report it does nothing and says when the next may start (the spam guard).
        /// </summary>
        internal static string Run(string openedFrom, bool test = false)
        {
            double now = Time.realtimeSinceStartup;
            if (!Cooldown.TryStart(now))
            {
                string wait = string.Format(Localization.instance.Localize("$vc_report_wait"), Cooldown.SecondsLeft(now));
                Log.Event($"Report a problem from {openedFrom}: refused, one was just made ({Cooldown.SecondsLeft(now)} s to wait)");
                return wait;
            }
            BugReportCommand.Report report = BugReportCommand.Make();
            string address = BugForm.Address(Plugin.PluginVersion, global::Version.GetVersionString(), report.Header, openedFrom);
            string paste = BugForm.Paste(report.Text);
            if (test && ZNet.instance != null && TestWorlds.IsTestWorld(ZNet.instance.GetWorldName()))
            {
                Log.Event($"Report a problem (test): the form {address} ({address.Length} characters); {paste.Length} characters to paste; " +
                          $"{report.Loaded} here; the file {(report.FileError == null ? "written" : "not written: " + report.FileError)}");
                return Localization.instance.Localize("$vc_report_opened");
            }
            GUIUtility.systemCopyBuffer = paste;
            Application.OpenURL(address);
            Log.Event($"Report a problem from {openedFrom}: the bug form opened, {paste.Length} characters copied ({report.Loaded} here)" +
                      (report.FileError == null ? "" : $"; the file couldn't be written: {report.FileError}"));
            return Localization.instance.Localize("$vc_report_opened");
        }
    }
}
