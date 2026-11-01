using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace VikingColonies.Core
{
    /// <summary>One line of the test-run banner; a warning line (a failed check) stands out.</summary>
    public sealed class BannerLine
    {
        public string Text;
        public bool Warn;
    }

    /// <summary>A run waiting in the run queue: its scenario (or "a build") and the session's short name.</summary>
    public sealed class QueuedRun
    {
        public string What = "";
        public string Who = "";
    }

    /// <summary>
    /// The test-run banner (the player, 2026-10-25: "show which test is running ingame and what stage it is at so when i
    /// open the game when tests are running i know"): the status file scripts/ingame.py keeps while it drives the game
    /// (BepInEx/VikingColonies-run-status.txt, 'key value' lines), whether it's fresh, and the banner's words.
    /// </summary>
    public sealed class RunStatus
    {
        /// <summary>The runner rewrites the file every 5 s; one older than this (or this far ahead: a clock that jumped) is
        /// a runner that died, and shows nothing.</summary>
        public const int StaleSeconds = 30;
        /// <summary>The most characters a banner line takes (a PASS line runs to 400).</summary>
        public const int MaxLine = 120;
        public const string Heading = "A test is running: please don't play. It ends by itself.";

        /// <summary>Unix seconds (0: missing), so the clocks going back in October can't fool it.</summary>
        public long UpdatedAt { get; private set; }
        public long StartedAt { get; private set; }
        public string Scenario { get; private set; } = "";
        public string Who { get; private set; } = "";
        /// <summary>The step's number, from 1; 0 before the steps (the build) and while the game closes.</summary>
        public int Step { get; private set; }
        public int Steps { get; private set; }
        public string Text { get; private set; } = "";
        /// <summary>The vc_*test command whose lines came last ("" before any).</summary>
        public string Test { get; private set; } = "";
        public int Passed { get; private set; }
        public int Failed { get; private set; }
        /// <summary>The checks the test made last time (0: unknown).</summary>
        public int Expected { get; private set; }
        /// <summary>The last check, "PASS …" or "FAIL …".</summary>
        public string Last { get; private set; } = "";
        public bool Done { get; private set; }
        /// <summary>The runs waiting in line after this one (the player, 2026-10-26: "Could we add the game queue to the
        /// ingame test label?"); -1: no queue read (a runner from before it, or the queue couldn't be read).</summary>
        public int Queue { get; private set; } = -1;
        /// <summary>The night's runs waiting for 01:00, besides those in line.</summary>
        public int QueueNight { get; private set; }
        /// <summary>The next few in line, in their order.</summary>
        public List<QueuedRun> Next { get; } = new List<QueuedRun>();

        public static RunStatus Parse(string text)
        {
            var s = new RunStatus();
            if (string.IsNullOrEmpty(text)) return s;
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                int gap = line.IndexOf(' ');
                if (gap <= 0) continue;
                string key = line.Substring(0, gap), value = line.Substring(gap + 1).Trim();
                switch (key)
                {
                    case "updated_at": s.UpdatedAt = Long(value); break;
                    case "started_at": s.StartedAt = Long(value); break;
                    case "scenario": s.Scenario = value; break;
                    case "who": s.Who = value; break;
                    case "step": s.Step = Int(value); break;
                    case "steps": s.Steps = Int(value); break;
                    case "text": s.Text = value; break;
                    case "test": s.Test = value; break;
                    case "passed": s.Passed = Int(value); break;
                    case "failed": s.Failed = Int(value); break;
                    case "expected": s.Expected = Int(value); break;
                    case "last": s.Last = value; break;
                    case "done": s.Done = value == "yes"; break;
                    case "queue": s.Queue = int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int q) ? q : -1; break;
                    case "queue_night": s.QueueNight = Int(value); break;
                    case "next":
                        int bar = value.IndexOf('|');
                        s.Next.Add(bar < 0 ? new QueuedRun { What = value }
                                           : new QueuedRun { What = value.Substring(0, bar).Trim(), Who = value.Substring(bar + 1).Trim() });
                        break;
                }
            }
            return s;
        }

        /// <summary>A whole number that isn't negative; anything else reads 0 (nothing).</summary>
        private static long Long(string value) =>
            long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out long n) ? n : 0;

        private static int Int(string value) =>
            int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? n : 0;

        /// <summary>Whether the runner wrote it within <see cref="StaleSeconds"/> of <paramref name="now"/> (Unix seconds).</summary>
        public bool IsFresh(long now) => UpdatedAt > 0 && Math.Abs(now - UpdatedAt) <= StaleSeconds;

        /// <summary>The banner: the heading, the scenario's step, the test's checks (once one has started), the time and who.</summary>
        public List<BannerLine> Lines(long now)
        {
            var lines = new List<BannerLine> { new BannerLine { Text = Heading } };
            string scenario = Scenario.Length > 0 ? Scenario : "a scenario";
            lines.Add(Line(Step > 0 ? $"{scenario} · step {Step} of {Steps}: {Text}" : $"{scenario} · {Text}", false));
            if (Test.Length > 0)
            {
                int checks = Passed + Failed;
                string words;
                if (Done) words = $"{Test} done: {Passed} passed, {Failed} failed";
                else if (checks == 0) words = $"{Test}: started, no checks yet";
                else words = $"{Test}: {checks}{(Expected > checks ? " of about " + Expected : "")} checks done " +
                             $"({Passed} passed, {Failed} failed){(Last.Length > 0 ? " · last: " + Last : "")}";
                lines.Add(Line(words, Failed > 0));
            }
            // No start time (a status from some other writer): no time, rather than 490,000 hours.
            string when = StartedAt > 0 ? Elapsed(now - StartedAt) + " so far" : "";
            string who = Who.Length > 0 ? "the game is held by " + Who : "";
            if (when.Length > 0 || who.Length > 0) lines.Add(Line(when + (when.Length > 0 && who.Length > 0 ? " · " : "") + who, false));
            if (Queue >= 0) lines.Add(Line(QueueWords(), false));
            return lines;
        }

        /// <summary>What waits behind the run: how many, the night's, and the next runs while the line has room.</summary>
        private string QueueWords()
        {
            string nights = QueueNight == 1 ? "1 night run" : QueueNight + " night runs";
            if (Queue == 0) return QueueNight > 0 ? $"The queue: nothing waits now · {nights} at 01:00" : "The queue: nothing waits after this run";
            string head = $"The queue: {Queue} waiting{(QueueNight > 0 ? $", {QueueNight} more at 01:00" : "")}";
            if (Next.Count == 0) return head;
            head += " · next: ";
            var names = new List<string>();
            foreach (QueuedRun run in Next.Take(Queue))
            {
                string name = run.Who.Length > 0 ? $"{run.What} ({run.Who})" : run.What;
                var tried = new List<string>(names) { name };
                bool all = tried.Count >= Queue;
                if (names.Count > 0 && (head + string.Join(", ", tried) + (all ? "" : ", …")).Length > MaxLine) break;
                names = tried;
            }
            return head + string.Join(", ", names) + (names.Count < Queue ? ", …" : "");
        }

        private static BannerLine Line(string text, bool warn) =>
            new BannerLine { Text = text.Length > MaxLine ? text.Substring(0, MaxLine - 1) + "…" : text, Warn = warn };

        /// <summary>How long the run has gone, in plain words: "under a minute", "12 min", "1 h 05 min".</summary>
        public static string Elapsed(long seconds)
        {
            if (seconds < 60) return "under a minute";
            long minutes = seconds / 60;
            return minutes < 60 ? $"{minutes} min" : $"{minutes / 60} h {minutes % 60:00} min";
        }
    }
}
