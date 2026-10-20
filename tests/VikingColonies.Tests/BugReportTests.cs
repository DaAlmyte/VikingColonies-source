using System.Collections.Generic;
using System.Linq;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>0.27: a settler's recent states, for a bug report.</summary>
    public class StateHistoryTests
    {
        [Fact]
        public void Newest_first_with_how_long_ago()
        {
            var h = new StateHistory();
            h.Add("$vc_status_working", 100);
            h.Add("$vc_status_delivering", 130);
            List<string> lines = h.Lines(142, s => s.Replace("$vc_status_", ""));
            Assert.Equal(new[] { "delivering (12 s ago)", "working (42 s ago)" }, lines);
        }

        [Fact]
        public void A_repeat_of_the_newest_isnt_added()
        {
            var h = new StateHistory();
            h.Add("a", 1);
            h.Add("a", 2);
            h.Add("b", 3);
            h.Add("a", 4);
            Assert.Equal(new[] { "a", "b", "a" }, h.Lines(4, null).Select(l => l.Split(' ')[0]));
        }

        [Fact]
        public void At_most_the_capacity_the_oldest_go()
        {
            var h = new StateHistory(3);
            for (int i = 0; i < 5; i++) h.Add("s" + i, i);
            Assert.Equal(new[] { "s4", "s3", "s2" }, h.Lines(5, null).Select(l => l.Split(' ')[0]));
        }

        [Fact]
        public void Long_ago_in_minutes_and_empty_states_shown_as_dash()
        {
            var h = new StateHistory();
            h.Add("", 0);
            Assert.Equal(new[] { "- (5 min ago)" }, h.Lines(300, null));
            Assert.Empty(new StateHistory().Lines(10, null));
        }
    }

    /// <summary>0.27: the bug report's text.</summary>
    public class BugReportTests
    {
        [Fact]
        public void Settler_history_and_troubles()
        {
            var colony = new ReportColony { Id = "abc", Hall = new Vec3(0, 0, 0) };
            colony.Settlers.Add(new ReportSettler
            {
                Name = "Runa", Job = "Lumberjack", Status = "Working", Position = new Vec3(1, 2, 3),
                Recent = new List<string> { "Working (3 s ago)", "Delivering (40 s ago)" },
                Troubles = "2 errors, 1 rescue",
            });
            colony.Settlers.Add(new ReportSettler { Name = "Ulf", Status = "Jobless", Position = new Vec3(4, 5, 6) });
            string text = ColonyReport.Format("h", new[] { colony }, new List<ReportSettler>());
            Assert.Contains("    recently: Working (3 s ago); Delivering (40 s ago)\n", text);
            Assert.Contains("    troubles: 2 errors, 1 rescue\n", text);
            Assert.Equal(1, text.Split('\n').Count(l => l.StartsWith("    recently:")));   // only Runa has a history
        }

        [Fact]
        public void Header_names_versions_mods_and_the_start_up_check()
        {
            string text = BugReport.Header("0.27.0", "0.221.4", new[] { "Jotunn 2.30.2", "PlantEverything 1.19.1" },
                new string[0], new[] { "hook X (a part of the mod): MissingMethodException" }, "single player");
            Assert.Contains("VikingColonies 0.27.0", text);
            Assert.Contains("Valheim 0.221.4", text);
            Assert.Contains("Jotunn 2.30.2, PlantEverything 1.19.1", text);
            Assert.Contains("Start-up: 1 part failed: hook X (a part of the mod): MissingMethodException", text);
            Assert.Contains("Game check: all found", text);
            Assert.Contains("single player", text);
        }

        [Fact]
        public void Header_lists_missing_game_parts()
        {
            string text = BugReport.Header("1", "2", new string[0], new[] { "Player.m_placementGhost (the placement outline)" }, new string[0], "client");
            Assert.Contains("Game check: missing Player.m_placementGhost (the placement outline)", text);
            Assert.Contains("Start-up: every part ran", text);
            Assert.Contains("Other mods: none", text);
        }
    }

    /// <summary>The Town Hall's Report a problem button: the bug form's address, and the report the player pastes.</summary>
    public class BugFormTests
    {
        private static readonly string Header = BugReport.Header("1.4.0", "0.221.4", new[] { "Jotunn 2.30.2" }, new string[0], new string[0], "single player");

        [Fact]
        public void The_address_opens_the_bug_form_with_the_versions_and_the_header()
        {
            string address = BugForm.Address("1.4.0", "0.221.4", Header, "the Town Hall window");
            Assert.StartsWith("https://github.com/DaAlmyte/VikingColonies-issues/issues/new?template=bug_report.yml&", address);
            Assert.Contains("&versions=VikingColonies%201.4.0%2C%20Valheim%200.221.4", address);
            Assert.Contains("&files=", address);
            Assert.Contains("VikingColonies%201.4.0%20bug%20report%0A", address);
            Assert.Contains("Opened%20from%3A%20the%20Town%20Hall%20window", address);
            Assert.DoesNotContain("title=", address);   // the form's own "[Bug] " title stays
            Assert.DoesNotContain(" ", address);
            Assert.DoesNotContain("\n", address);
        }

        [Fact]
        public void Letters_outside_english_are_sent_as_utf8()
        {
            string header = BugReport.Header("1.4.0", "0.221.4", new[] { "Jötunn 2.30.2" }, new string[0], new string[0], "single player");
            Assert.Contains("J%C3%B6tunn", BugForm.Address("1.4.0", "0.221.4", header, "the Town Hall window"));
        }

        [Fact]
        public void A_long_header_has_its_longest_line_shortened_and_the_address_stays_short()
        {
            var mods = Enumerable.Range(0, 400).Select(i => $"SomeLongModName{i} 1.2.{i}").ToArray();
            string header = BugReport.Header("1.4.0", "0.221.4", mods, new string[0], new[] { "a part failed" }, "single player");
            string address = BugForm.Address("1.4.0", "0.221.4", header, "the Town Hall window");
            Assert.True(address.Length <= BugForm.MaxAddress, $"{address.Length} characters");
            Assert.Contains("versions=VikingColonies%201.4.0", address);
            Assert.Contains("VikingColonies%201.4.0%20bug%20report", address);
            Assert.Contains("Valheim%200.221.4%3B%20single%20player", address);
            Assert.Contains("Start-up%3A%201%20part%20failed%3A%20a%20part%20failed", address);   // the short lines matter most
            Assert.Contains("SomeLongModName0%201.2.0", address);
        }

        [Fact]
        public void The_paste_is_the_whole_report_when_it_fits()
        {
            Assert.Equal("a report\n", BugForm.Paste("a report\n"));
            Assert.Equal("", BugForm.Paste(null));
        }

        [Fact]
        public void One_report_at_a_time_the_next_waits_ten_seconds()
        {
            var cooldown = new ReportCooldown();
            Assert.False(cooldown.Busy(100));
            Assert.True(cooldown.TryStart(100));
            Assert.True(cooldown.Busy(101));
            Assert.False(cooldown.TryStart(101));     // a second click: refused, and it doesn't restart the wait
            Assert.False(cooldown.TryStart(109.5));
            Assert.Equal(1, cooldown.SecondsLeft(109.5));
            Assert.Equal(9, cooldown.SecondsLeft(101));
            Assert.True(cooldown.TryStart(110));
            Assert.False(cooldown.TryStart(115));
        }

        [Fact]
        public void A_huge_report_is_cut_with_a_note_where_the_rest_is()
        {
            string report = string.Concat(Enumerable.Repeat("a line of the report\n", 10000));
            string paste = BugForm.Paste(report);
            Assert.True(paste.Length <= BugForm.MaxPaste, $"{paste.Length} characters");
            Assert.StartsWith("a line of the report\n", paste);
            Assert.EndsWith("(The report is cut here: the whole of it is in BepInEx/VikingColonies-bugreport.txt.)\n", paste);
        }
    }
}
