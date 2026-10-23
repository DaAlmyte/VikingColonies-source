using System.Collections.Generic;
using System.Linq;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    public class DevToolsTests
    {
        // ---- the flat test world (2026-10-18) ----

        [Fact]
        public void Only_a_test_world_that_says_flat_is_made_flat()
        {
            Assert.True(TestWorlds.IsFlatTestWorld("VCTestFlat"));
            Assert.True(TestWorlds.IsFlatTestWorld("vctestflat"));
            Assert.True(TestWorlds.IsFlatTestWorld("VCTestFlat2"));
            // A player's world that merely says "flat" and "test" is never flattened (the mod hub's check of 1.4.0: "Flattest"
            // would have been made with no locations, so no boss altars).
            Assert.False(TestWorlds.IsFlatTestWorld("flattest"));
            Assert.False(TestWorlds.IsFlatTestWorld("Flattest Server"));
            Assert.False(TestWorlds.IsFlatTestWorld("TestFlat"));
            Assert.False(TestWorlds.IsFlatTestWorld("MyVCTestFlat"));
            Assert.False(TestWorlds.IsFlatTestWorld("VCTest"));          // the worker tests need its trees
            Assert.False(TestWorlds.IsFlatTestWorld("FlatLands"));       // not a test world: someone's own
            Assert.False(TestWorlds.IsFlatTestWorld("MyWorld"));
            Assert.False(TestWorlds.IsFlatTestWorld(""));
            Assert.False(TestWorlds.IsFlatTestWorld(null));
        }

        [Fact]   // the 1.4.0 full run: on uneven ground a 2 m block below a raised doorstep was a step up to it
        public void A_test_door_block_stands_two_metres_over_the_doorstep()
        {
            Assert.Equal(2, TestDoorBlock.Layers(ground: 10f, doorstep: 10f));     // level ground, as before
            Assert.Equal(2, TestDoorBlock.Layers(ground: 10f, doorstep: 9.5f));    // the doorstep below the ground
            Assert.Equal(5, TestDoorBlock.Layers(ground: 73.6f, doorstep: 75.7f)); // the run's raised hut: up to 78.6
            Assert.Equal(3, TestDoorBlock.Layers(ground: 10f, doorstep: 11f));
        }

        // ---- vc_watch (0.24.1) ----

        [Fact]
        public void Watch_request_takes_a_name_or_nearest_and_seconds()
        {
            WatchRequest r = WatchRequest.Parse(new[] { "Runa" });
            Assert.True(r.Valid);
            Assert.Equal("Runa", r.Target);
            Assert.Equal(60f, r.Seconds);
            r = WatchRequest.Parse(new[] { "nearest", "15" });
            Assert.Equal("nearest", r.Target);
            Assert.Equal(15f, r.Seconds);
            Assert.Equal(600f, WatchRequest.Parse(new[] { "Runa", "5000" }).Seconds);
            Assert.Equal(1f, WatchRequest.Parse(new[] { "Runa", "0.2" }).Seconds);
        }

        [Fact]
        public void Watch_request_stops_one_with_0_and_all_with_off()
        {
            WatchRequest one = WatchRequest.Parse(new[] { "Runa", "0" });
            Assert.True(one.Valid);
            Assert.True(one.Stop);
            Assert.Equal("Runa", one.Target);
            WatchRequest all = WatchRequest.Parse(new[] { "off" });
            Assert.True(all.Valid);
            Assert.True(all.StopAll);
        }

        [Fact]
        public void Watch_request_without_a_settler_says_how()
        {
            Assert.False(WatchRequest.Parse(new string[0]).Valid);
            Assert.False(WatchRequest.Parse(null).Valid);
            Assert.False(WatchRequest.Parse(new[] { "Runa", "soon" }).Valid);
            Assert.False(WatchRequest.Parse(new[] { "Runa", "NaN" }).Valid);   // review of 0.24.1: it never ended
            Assert.Contains("vc_watch", WatchRequest.Parse(null).Error);
        }

        [Fact]
        public void Watch_logs_changes_and_every_5_s()
        {
            Assert.True(WatchRules.ShouldLog("at (1, 2)", null, 0.0));
            Assert.True(WatchRules.ShouldLog("at (1, 3)", "at (1, 2)", 0.5));
            Assert.False(WatchRules.ShouldLog("at (1, 2)", "at (1, 2)", 4.9));
            Assert.True(WatchRules.ShouldLog("at (1, 2)", "at (1, 2)", 5.0));
        }

        // ---- vc_shot (0.24.1) ----

        [Fact]
        public void Shot_file_is_named_by_time_and_a_safe_label()
        {
            var at = new System.DateTime(2026, 10, 17, 5, 30, 12);
            Assert.Equal("20261017-053012.png", ShotFile.Name(at, null));
            Assert.Equal("20261017-053012-door-check.png", ShotFile.Name(at, "door-check"));
            Assert.Equal("20261017-053012-a_b_c.png", ShotFile.Name(at, "a/../b c"));
            Assert.Equal("20261017-053012.png", ShotFile.Name(at, "../"));
            Assert.Equal(16 + 40 + 4, ShotFile.Name(at, new string('x', 90)).Length);
        }

        [Fact]
        public void Shot_file_keeps_the_newest()
        {
            var names = new[] { "20261017-053012.png", "20261016-010101-a.png", "20261017-060000.png", "20261015-000000.png" };
            Assert.Equal(new[] { "20261015-000000.png", "20261016-010101-a.png" }, ShotFile.Oldest(names, 2));
            Assert.Empty(ShotFile.Oldest(names, 30));
        }

        [Fact]
        public void Shot_file_keeps_every_picture_of_the_last_day()
        {
            // 2026-10-21: a 260-picture run kept them all ('vc_shot keep 300'); the next game's first picture, back at 60,
            // took the rest away before they were looked at.
            var now = new System.DateTime(2026, 10, 21, 7, 10, 0);
            var names = new[] { "20261021-053000-a.png", "20261021-062000-b.png", "20261020-070000-c.png", "20261019-120000-d.png" };
            Assert.Equal(new[] { "20261019-120000-d.png", "20261020-070000-c.png" }, ShotFile.Oldest(names, 1, now));
            Assert.Empty(ShotFile.Oldest(new[] { "20261021-053000-a.png", "20261021-062000-b.png" }, 0, now));
            Assert.Equal(new[] { "odd.png" }, ShotFile.Oldest(new[] { "odd.png", "20261021-062000-b.png" }, 0, now));   // no time in its name
        }

        [Fact]
        public void Shot_file_never_keeps_more_than_the_most_however_new()
        {
            // The review of 2026-10-21: the day's rule had lifted the 500 (about 1.5 GB) cap.
            var now = new System.DateTime(2026, 10, 21, 12, 0, 0);
            var names = Enumerable.Range(0, ShotFile.MaxKeep + 100).Select(i => now.AddSeconds(-i).ToString("yyyyMMdd-HHmmss") + ".png").ToList();
            List<string> gone = ShotFile.Oldest(names, 299, now);
            Assert.Equal(100, gone.Count);
            Assert.All(gone, n => Assert.True(string.CompareOrdinal(n, names[ShotFile.MaxKeep - 1]) < 0));
        }

        [Fact]
        public void Shot_file_keeps_more_for_a_long_picture_run_but_never_fewer_than_60()
        {
            // The hut review (2026-10-21) takes 185 pictures: with 60 kept, its first huts' were gone before the end.
            Assert.Equal(60, ShotFile.DefaultKeep);
            Assert.Equal(300, ShotFile.ClampKeep(300));
            Assert.Equal(ShotFile.DefaultKeep, ShotFile.ClampKeep(10));
            Assert.Equal(ShotFile.MaxKeep, ShotFile.ClampKeep(100000));
        }

        // ---- vc_speed ----

        [Fact]
        public void The_game_speed_stays_between_1_and_5()
        {
            Assert.Equal(1, GameSpeed.Clamp(0));
            Assert.Equal(1, GameSpeed.Clamp(-3));
            Assert.Equal(3, GameSpeed.Clamp(3));
            Assert.Equal(5, GameSpeed.Clamp(12));
        }

        // ---- The command file ----

        [Fact]
        public void The_command_file_gives_its_commands_one_per_line_without_blanks_or_comments()
        {
            List<string> lines = CommandFile.Parse("vc_report\r\n\r\n  # a note\n goto 10 20 \n");
            Assert.Equal(new[] { "vc_report", "goto 10 20" }, lines);
            Assert.Empty(CommandFile.Parse(""));
            Assert.Empty(CommandFile.Parse(null));
        }

        [Fact]
        public void The_command_file_runs_our_commands_and_a_few_harmless_dev_ones_only()
        {
            Assert.True(CommandFile.IsAllowed("vc_build status"));
            Assert.True(CommandFile.IsAllowed("VC_report auto 10"));
            Assert.True(CommandFile.IsAllowed("goto 352 128"));
            Assert.True(CommandFile.IsAllowed("god"));
            Assert.True(CommandFile.IsAllowed("tod 0.5"));
            Assert.False(CommandFile.IsAllowed("devcommands"));
            Assert.False(CommandFile.IsAllowed("killenemies"));   // it kills settlers too: use vc_world clear
            Assert.False(CommandFile.IsAllowed("exit"));
            Assert.False(CommandFile.IsAllowed("ban somebody"));
            Assert.False(CommandFile.IsAllowed(""));
        }

        [Fact]
        public void The_command_file_runs_only_in_a_test_world_and_waits_for_one()
        {
            // The whole-mod review of 1.4.1: a profile with the file on, opened on the player's own world, ran what was in it.
            Assert.Equal(CommandFileState.Run, CommandFile.In("VCTest"));
            Assert.Equal(CommandFileState.Run, CommandFile.In("VCTestRun-20261019-120000"));
            Assert.Equal(CommandFileState.Refuse, CommandFile.In("MyWorld"));
            Assert.Equal(CommandFileState.Refuse, CommandFile.In("Midgard"));
            Assert.Equal(CommandFileState.Wait, CommandFile.In(""));   // no world yet: the commands wait in the file
            Assert.Equal(CommandFileState.Wait, CommandFile.In(null));
        }

        [Fact]
        public void The_command_file_can_start_and_stop_a_raid_in_a_test_world()
        {
            Assert.True(CommandFile.IsAllowed("event army_eikthyr"));
            Assert.True(CommandFile.IsAllowed("randomevent"));
            Assert.True(CommandFile.IsAllowed("stopevent"));
        }

        [Fact]
        public void The_command_file_can_set_the_weather_for_pictures()
        {
            Assert.True(CommandFile.IsAllowed("env Clear"));   // the huts' showcase photographs in clear weather (2026-10-18)
            Assert.True(CommandFile.IsAllowed("resetenv"));
        }

        // ---- Auto-start ----

        [Fact]
        public void Auto_start_only_goes_into_a_world_whose_name_says_test()
        {
            Assert.True(TestWorlds.IsTestWorld("VCTest"));
            Assert.True(TestWorlds.IsTestWorld("VCTestMP"));
            Assert.True(TestWorlds.IsTestWorld("tester"));
            Assert.False(TestWorlds.IsTestWorld("MyWorld"));
            Assert.False(TestWorlds.IsTestWorld(""));
            Assert.False(TestWorlds.IsTestWorld(null));
        }

        // ---- vc_findflat ----

        [Fact]
        public void A_flat_dry_spot_far_from_colonies_is_good()
        {
            FlatSpot.Verdict verdict = FlatSpot.Judge(new[] { 32f, 32.4f, 31.8f, 32.2f }, waterLevel: 30f, nearestColony: 120f);
            Assert.True(verdict.Good);
            Assert.Equal(0.6f, verdict.Spread, 3);
        }

        [Fact]
        public void Uneven_wet_or_crowded_spots_are_not_good_and_say_why()
        {
            Assert.True(FlatSpot.Judge(new[] { 32f, 34f }, 30f, 120f).Good);   // meadows roll: 2 m over the spot is flat enough
            Assert.Contains("uneven", FlatSpot.Judge(new[] { 32f, 35f }, 30f, 120f).Reason);
            Assert.Contains("water", FlatSpot.Judge(new[] { 30.5f, 30.6f }, 30f, 120f).Reason);
            Assert.Contains("colony", FlatSpot.Judge(new[] { 32f, 32f }, 30f, 20f).Reason);
            Assert.False(FlatSpot.Judge(new float[0], 30f, 120f).Good);
        }

        [Fact]
        public void The_search_goes_round_the_start_in_widening_rings()
        {
            List<Vec3> spots = FlatSpot.Candidates(new Vec3(100f, 0f, 200f), step: 10f, rings: 2).ToList();
            Assert.Equal(8 + 16, spots.Count);
            Assert.All(spots.Take(8), s => Assert.Equal(10f, System.Math.Max(System.Math.Abs(s.X - 100f), System.Math.Abs(s.Z - 200f)), 3));
            Assert.All(spots.Skip(8), s => Assert.Equal(20f, System.Math.Max(System.Math.Abs(s.X - 100f), System.Math.Abs(s.Z - 200f)), 3));
            Assert.Equal(spots.Count, spots.Select(s => (s.X, s.Z)).Distinct().Count());
        }

        // ---- vc_report ----

        [Fact]
        public void The_report_lists_each_colony_with_its_buildings_and_settlers()
        {
            var colony = new ReportColony
            {
                Id = "4281be2f",
                Hall = new Vec3(356f, 31.4f, 68f),
                Buildings =
                {
                    new ReportBuilding
                    {
                        Type = "builder_hut", Id = "f7df5b8b", Level = 3, Position = new Vec3(365f, 31.37f, 63.83f), Turn = 90f,
                        Door = new Vec3(365f, 32.4f, 66.8f), Order = "Upgrade to level 3, Building, 12 of 65 pieces, 40 old left",
                        Chest = new Dictionary<string, int> { { "Wood", 5 }, { "Stone", 48 } },
                        ChestSize = "6x4",
                    },
                },
                Settlers =
                {
                    new ReportSettler { Name = "Ulf", Job = "builder", Workplace = "f7df5b8b", Status = "Fetching", Position = new Vec3(364f, 31.3f, 66f), Carry = "Stone:6" },
                },
            };
            string text = ColonyReport.Format("header line", new[] { colony }, new List<ReportSettler>());
            string[] lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
            Assert.Equal("header line", lines[0]);
            Assert.Contains("Colony 4281be2f: Town Hall at (356.0, 31.4, 68.0)", text);
            Assert.Contains("builder_hut f7df5b8b level 3 at (365.0, 31.4, 63.8) turned 90, door at (365.0, 32.4, 66.8)", text);
            Assert.Contains("order: Upgrade to level 3, Building, 12 of 65 pieces, 40 old left", text);
            Assert.Contains("chest 6x4: Stone 48, Wood 5", text);
            Assert.Contains("Ulf, builder at f7df5b8b: Fetching, at (364.0, 31.3, 66.0), carrying Stone:6", text);
        }

        [Fact]
        public void The_report_says_so_when_there_are_no_colonies_and_lists_settlers_without_one()
        {
            string text = ColonyReport.Format("h", new ReportColony[0], new[] { new ReportSettler { Name = "Sigrid", Position = new Vec3(1f, 2f, 3f) } });
            Assert.Contains("No colonies.", text);
            Assert.Contains("Without a colony:", text);
            Assert.Contains("Sigrid, jobless: -, at (1.0, 2.0, 3.0), carrying nothing", text);
        }
    }
}
