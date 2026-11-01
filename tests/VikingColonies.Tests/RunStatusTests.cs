using System.Collections.Generic;
using System.Linq;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>The test-run banner (the player, 2026-10-25): what the runner's status file says, and how the game words it.</summary>
    public class RunStatusTests
    {
        private const string File =
            "updated_at 1000\nstarted_at 280\nscenario weak.txt\nwho VC · night runs · Mon 26 Oct\nstep 9\nsteps 14\n" +
            "text waitlog vc_weaktest: done 900\ntest vc_weaktest\npassed 11\nfailed 1\nexpected 30\n" +
            "last FAIL Settlers stand up to a boar\ndone no\n";

        private static List<string> Texts(RunStatus status, long now) => status.Lines(now).Select(l => l.Text).ToList();

        [Fact]
        public void Reads_the_runners_status_file()
        {
            foreach (string text in new[] { File, File.Replace("\n", "\r\n") })
            {
                RunStatus s = RunStatus.Parse(text);
                Assert.Equal(1000, s.UpdatedAt);
                Assert.Equal(280, s.StartedAt);
                Assert.Equal("weak.txt", s.Scenario);
                Assert.Equal("VC · night runs · Mon 26 Oct", s.Who);
                Assert.Equal(9, s.Step);
                Assert.Equal(14, s.Steps);
                Assert.Equal("waitlog vc_weaktest: done 900", s.Text);
                Assert.Equal("vc_weaktest", s.Test);
                Assert.Equal(11, s.Passed);
                Assert.Equal(1, s.Failed);
                Assert.Equal(30, s.Expected);
                Assert.Equal("FAIL Settlers stand up to a boar", s.Last);
                Assert.False(s.Done);
            }
            Assert.True(RunStatus.Parse("done yes\n").Done);
        }

        [Fact]
        public void Missing_or_bad_values_read_as_nothing()
        {
            foreach (string? text in new[] { null, "", "step x\nsteps\nupdated_at soon\nstep -3\nwhat is this\n" })
            {
                RunStatus s = RunStatus.Parse(text!);
                Assert.Equal(0, s.Step);
                Assert.Equal(0, s.Steps);
                Assert.Equal(0, s.UpdatedAt);
                Assert.Equal("", s.Scenario);
                Assert.Equal("", s.Who);
                Assert.Equal("", s.Text);
                Assert.Equal("", s.Test);
                Assert.Equal("", s.Last);
                Assert.False(s.IsFresh(0));
            }
        }

        [Fact]   // a killed runner's file goes within half a minute; a clock that jumped can't keep one up
        public void Fresh_for_30_seconds_either_way()
        {
            RunStatus s = RunStatus.Parse("updated_at 1000\n");
            Assert.True(s.IsFresh(1000));
            Assert.True(s.IsFresh(1030));
            Assert.True(s.IsFresh(970));
            Assert.False(s.IsFresh(1031));
            Assert.False(s.IsFresh(969));
            Assert.False(RunStatus.Parse("scenario weak.txt\n").IsFresh(0));   // no time: never shown
        }

        [Fact]
        public void The_heading_comes_first()
        {
            BannerLine first = RunStatus.Parse(File).Lines(1000)[0];
            Assert.Equal("A test is running: please don't play. It ends by itself.", first.Text);
            Assert.Equal(RunStatus.Heading, first.Text);
            Assert.False(first.Warn);
        }

        [Fact]
        public void Before_the_steps_says_what_it_does()
        {
            RunStatus s = RunStatus.Parse("updated_at 1000\nstarted_at 1000\nscenario weak.txt\nstep 0\nsteps 14\n" +
                                          "text building the mod (before step 1)\n");
            List<string> lines = Texts(s, 1000);
            Assert.Equal(3, lines.Count);
            Assert.Equal("weak.txt · building the mod (before step 1)", lines[1]);
        }

        [Fact]
        public void A_step_says_its_number_and_text()
        {
            Assert.Equal("weak.txt · step 9 of 14: waitlog vc_weaktest: done 900", Texts(RunStatus.Parse(File), 1000)[1]);
        }

        [Fact]
        public void A_test_going_says_its_checks()
        {
            BannerLine line = RunStatus.Parse(File).Lines(1000)[2];
            Assert.Equal("vc_weaktest: 12 of about 30 checks done (11 passed, 1 failed) · last: FAIL Settlers stand up to a boar", line.Text);
            Assert.True(line.Warn);   // a failed check shows at a glance
            // No count to go by (a new test), or the count reached: no "of about".
            Assert.Equal("vc_weaktest: 12 checks done (11 passed, 1 failed) · last: FAIL Settlers stand up to a boar",
                Texts(RunStatus.Parse(File.Replace("expected 30", "expected 0")), 1000)[2]);
            Assert.Equal("vc_weaktest: 12 checks done (11 passed, 1 failed) · last: FAIL Settlers stand up to a boar",
                Texts(RunStatus.Parse(File.Replace("expected 30", "expected 12")), 1000)[2]);
            RunStatus none = RunStatus.Parse(File.Replace("passed 11", "passed 0").Replace("failed 1", "failed 0").Replace("last FAIL Settlers stand up to a boar\n", ""));
            Assert.Equal("vc_weaktest: started, no checks yet", none.Lines(1000)[2].Text);
            Assert.False(none.Lines(1000)[2].Warn);
            Assert.False(RunStatus.Parse(File.Replace("failed 1", "failed 0")).Lines(1000)[2].Warn);
        }

        [Fact]
        public void A_test_done_says_its_result()
        {
            BannerLine line = RunStatus.Parse(File.Replace("passed 11", "passed 30").Replace("failed 1", "failed 0").Replace("done no", "done yes")).Lines(1000)[2];
            Assert.Equal("vc_weaktest done: 30 passed, 0 failed", line.Text);
            Assert.False(line.Warn);
            Assert.True(RunStatus.Parse(File.Replace("done no", "done yes")).Lines(1000)[2].Warn);   // done with a failure
        }

        [Fact]   // a PASS line runs to 400 characters, and so can a step's regex
        public void Long_lines_are_cut()
        {
            string longText = new string('x', 400);
            List<string> lines = Texts(RunStatus.Parse(File.Replace("waitlog vc_weaktest: done 900", longText)
                                                           .Replace("FAIL Settlers stand up to a boar", "FAIL " + longText)), 1000);
            Assert.Equal(RunStatus.MaxLine, lines[1].Length);
            Assert.EndsWith("…", lines[1]);
            Assert.Equal(RunStatus.MaxLine, lines[2].Length);
            Assert.EndsWith("…", lines[2]);
        }

        [Fact]
        public void The_last_line_says_the_time_and_who()
        {
            RunStatus s = RunStatus.Parse(File);
            Assert.Equal("12 min so far · the game is held by VC · night runs · Mon 26 Oct", Texts(s, 280 + 720).Last());
            Assert.Equal("12 min so far", Texts(RunStatus.Parse(File.Replace("who VC · night runs · Mon 26 Oct\n", "")), 280 + 720).Last());
            // No start time: no time (the review: it read about 490,000 hours); nor who: no last line.
            Assert.Equal("the game is held by VC · night runs · Mon 26 Oct", Texts(RunStatus.Parse(File.Replace("started_at 280\n", "")), 1000).Last());
            Assert.Equal(2, Texts(RunStatus.Parse("updated_at 1000\nscenario weak.txt\nstep 1\nsteps 2\ntext wait 1\n"), 1000).Count);
        }

        private const string Queue =
            "queue 9\nqueue_night 11\nnext meshfind.txt|1.6 · runestone memorial\nnext buildtest.txt|huts\nnext outfits.txt|design · job outfits\n";

        [Fact]
        public void Reads_the_queue_behind_the_run()
        {
            RunStatus s = RunStatus.Parse(File + Queue);
            Assert.Equal(9, s.Queue);
            Assert.Equal(11, s.QueueNight);
            Assert.Equal(new[] { "meshfind.txt", "buildtest.txt", "outfits.txt" }, s.Next.Select(n => n.What));
            Assert.Equal(new[] { "1.6 · runestone memorial", "huts", "design · job outfits" }, s.Next.Select(n => n.Who));
            RunStatus older = RunStatus.Parse(File);   // a runner from before the queue line: no queue read
            Assert.Equal(-1, older.Queue);
            Assert.Empty(older.Next);
            Assert.Equal(-1, RunStatus.Parse("queue soon\n").Queue);
            Assert.Equal("", RunStatus.Parse("next weak.txt\n").Next.Single().Who);   // no who: just the scenario
        }

        [Fact]   // the player, 26 Oct: "Could we add the game queue to the ingame test label?"
        public void The_queue_comes_last_with_the_next_runs()
        {
            List<string> lines = Texts(RunStatus.Parse(File + Queue), 1000);
            Assert.Equal(5, lines.Count);
            Assert.Equal("The queue: 9 waiting, 11 more at 01:00 · next: meshfind.txt (1.6 · runestone memorial), buildtest.txt (huts), …",
                lines[4]);
            Assert.True(lines[4].Length <= RunStatus.MaxLine);
            Assert.Equal("The queue: 2 waiting · next: life.txt (release), a build",
                Texts(RunStatus.Parse(File + "queue 2\nqueue_night 0\nnext life.txt|release\nnext a build|\n"), 1000)[4]);
            Assert.Equal("The queue: 1 waiting, 1 more at 01:00 · next: life.txt (release)",
                Texts(RunStatus.Parse(File + "queue 1\nqueue_night 1\nnext life.txt|release\n"), 1000)[4]);
            Assert.False(RunStatus.Parse(File + Queue).Lines(1000)[4].Warn);
            Assert.Equal(4, Texts(RunStatus.Parse(File), 1000).Count);   // an older runner's status: no queue line
        }

        [Fact]
        public void An_empty_queue_says_the_game_is_free_after_it()
        {
            Assert.Equal("The queue: nothing waits after this run",
                Texts(RunStatus.Parse(File + "queue 0\nqueue_night 0\n"), 1000).Last());
            Assert.Equal("The queue: nothing waits now · 3 night runs at 01:00",
                Texts(RunStatus.Parse(File + "queue 0\nqueue_night 3\n"), 1000).Last());
            Assert.Equal("The queue: nothing waits now · 1 night run at 01:00",
                Texts(RunStatus.Parse(File + "queue 0\nqueue_night 1\n"), 1000).Last());
        }

        [Fact]   // the review: a status written by hand, or by some other writer
        public void Odd_queue_lines_still_read_right()
        {
            Assert.Equal("The queue: 3 waiting", Texts(RunStatus.Parse(File + "queue 3\nqueue_night 0\n"), 1000).Last());
            Assert.Equal("The queue: 1 waiting · next: a.txt (huts)",
                Texts(RunStatus.Parse(File + "queue 1\nnext a.txt|huts\nnext b.txt|huts\n"), 1000).Last());
        }

        [Fact]   // a long first name is cut like any line; the next ones go only while they fit
        public void A_long_queue_line_stays_short()
        {
            string longWho = new string('w', 200);
            string line = Texts(RunStatus.Parse(File + $"queue 2\nqueue_night 0\nnext a.txt|{longWho}\nnext b.txt|huts\n"), 1000)[4];
            Assert.Equal(RunStatus.MaxLine, line.Length);
            Assert.EndsWith("…", line);
            Assert.DoesNotContain("b.txt", line);
        }

        [Fact]
        public void Elapsed_in_plain_words()
        {
            Assert.Equal("under a minute", RunStatus.Elapsed(0));
            Assert.Equal("under a minute", RunStatus.Elapsed(59));
            Assert.Equal("under a minute", RunStatus.Elapsed(-5));
            Assert.Equal("1 min", RunStatus.Elapsed(60));
            Assert.Equal("59 min", RunStatus.Elapsed(3599));
            Assert.Equal("1 h 05 min", RunStatus.Elapsed(3900));
            Assert.Equal("2 h 00 min", RunStatus.Elapsed(7200));
        }
    }
}
