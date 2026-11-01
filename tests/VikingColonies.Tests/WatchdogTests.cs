using System.Collections.Generic;
using System.Linq;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>The watchdog's rules (26 Oct 2026): what always holds in a colony, checked every few seconds in a test run.</summary>
    public class WatchdogTests
    {
        private static SettlerFacts Settler(string name = "Ingrid", float x = 10f, float y = 30f, float z = 10f, float ground = 30f,
                                            string status = "$vc_status_heading_to_work", string id = null) =>
            new SettlerFacts { Id = id ?? "id-" + name, Name = name, X = x, Y = y, Z = z, Ground = ground, Status = status };

        private static List<string> Look(Watchdog dog, double now, params SettlerFacts[] settlers) =>
            dog.Look(now, settlers, new HutFacts[0]);

        // ---- the line ----

        [Fact]
        public void A_break_is_one_line_the_runner_counts()
        {
            Assert.Equal("vc_watchdog: FAIL stuck (Ingrid: x)", Watchdog.Line("stuck", "Ingrid: x"));
        }

        // ---- items ----

        [Fact]
        public void A_hand_over_that_keeps_every_item_is_fine()
        {
            var dog = new Watchdog();
            List<string> lines = dog.Handover(0, "deposit",
                new Dictionary<string, int> { { "Wood", 5 } }, new Dictionary<string, int> { { "Wood", 10 }, { "Stone", 2 } },
                new Dictionary<string, int> { { "Wood", 15 } }, new Dictionary<string, int> { { "Stone", 2 } }, _ => true);
            Assert.Empty(lines);
        }

        [Fact]
        public void An_item_lost_or_made_in_a_hand_over_is_a_break()
        {
            var dog = new Watchdog();
            List<string> lost = dog.Handover(0, "deposit by Ingrid into the Lumberjack's Hut",
                new Dictionary<string, int> { { "Wood", 5 } }, new Dictionary<string, int> { { "Wood", 10 } },
                new Dictionary<string, int> { { "Wood", 14 } }, new Dictionary<string, int>(), _ => true);
            Assert.Single(lost);
            Assert.StartsWith("vc_watchdog: FAIL items (deposit by Ingrid into the Lumberjack's Hut: Wood 15 before, 14 after", lost[0]);
            List<string> made = dog.Handover(0, "take by Leif",
                new Dictionary<string, int> { { "Resin", 3 } }, new Dictionary<string, int>(),
                new Dictionary<string, int> { { "Resin", 2 } }, new Dictionary<string, int> { { "Resin", 2 } }, _ => true);
            Assert.Single(made);
            Assert.Contains("Resin 3 before, 4 after", made[0]);
        }

        [Fact]
        public void An_item_the_chest_doesnt_know_may_go()
        {
            var dog = new Watchdog();
            List<string> lines = dog.Handover(0, "deposit", new Dictionary<string, int>(), new Dictionary<string, int> { { "OtherModsThing", 4 } },
                new Dictionary<string, int>(), new Dictionary<string, int>(), item => item != "OtherModsThing");
            Assert.Empty(lines);
        }

        [Fact]
        public void A_missing_count_is_none()
        {
            var dog = new Watchdog();
            Assert.Empty(dog.Handover(0, "take", null, null, null, null, null));
        }

        // ---- position ----

        [Fact]
        public void A_position_that_isnt_a_number_or_is_off_the_world_breaks_at_once()
        {
            var dog = new Watchdog();
            Assert.Contains(Look(dog, 0, Settler(x: float.NaN)), l => l.StartsWith("vc_watchdog: FAIL position (Ingrid"));
            dog = new Watchdog();
            Assert.Contains(Look(dog, 0, Settler(z: float.PositiveInfinity)), l => l.Contains("FAIL position"));
            dog = new Watchdog();
            Assert.Contains(Look(dog, 0, Settler(x: 9000f, z: 9000f)), l => l.Contains("FAIL position") && l.Contains("outside the world"));
            dog = new Watchdog();
            Assert.Empty(Look(dog, 0, Settler(x: 7000f, z: -7000f)));   // 9,900 m out: still in the world
        }

        [Fact]
        public void Under_the_ground_breaks_only_at_two_looks_in_a_row()
        {
            var dog = new Watchdog();
            Assert.Empty(Look(dog, 0, Settler(y: 27f, ground: 30f)));               // once: a fall through a step, caught next frame
            Assert.Empty(Look(dog, 5, Settler(y: 30f, ground: 30f)));               // back up
            Assert.Empty(Look(dog, 10, Settler(y: 27f, ground: 30f)));
            List<string> lines = Look(dog, 15, Settler(y: 27f, ground: 30f));
            Assert.Contains(lines, l => l.Contains("FAIL position") && l.Contains("3.0 m under the ground"));
        }

        [Fact]
        public void A_little_under_the_ground_or_an_unknown_ground_is_fine()
        {
            var dog = new Watchdog();
            for (int i = 0; i < 4; i++) Assert.Empty(Look(dog, i * 5, Settler(y: 29f, ground: 30f)));   // 1 m: a dug floor's edge
            dog = new Watchdog();
            for (int i = 0; i < 4; i++) Assert.Empty(Look(dog, i * 5, Settler(y: -50f, ground: float.NaN)));
        }

        // ---- stuck ----

        [Fact]
        public void On_the_way_without_moving_for_two_minutes_is_stuck()
        {
            var dog = new Watchdog();
            for (int t = 0; t < 120; t += 5) Assert.Empty(Look(dog, t, Settler(x: 10f + t * 0.005f)));
            List<string> lines = Look(dog, 120, Settler(x: 10.6f));
            Assert.Single(lines);
            Assert.StartsWith("vc_watchdog: FAIL stuck (Ingrid: vc_status_heading_to_work, not 1 m from (10, 10) in 120 s", lines[0]);
        }

        [Fact]
        public void Fleeing_or_arriving_may_wait_in_one_spot()
        {
            // A settler with no hut waits at a shelter spot while the threat lasts; a newcomer waits up to 3 minutes for a way in.
            foreach (string status in new[] { "$vc_status_fleeing", "$vc_status_arriving" })
            {
                var dog = new Watchdog();
                for (int t = 0; t <= 400; t += 5) Assert.Empty(Look(dog, t, Settler(status: status)));
            }
        }

        [Fact]
        public void Moving_resets_the_minute()
        {
            var dog = new Watchdog();
            for (int t = 0; t <= 50; t += 5) Look(dog, t, Settler());
            Assert.Empty(Look(dog, 55, Settler(x: 12f)));   // walked 2 m
            for (int t = 60; t < 175; t += 5) Assert.Empty(Look(dog, t, Settler(x: 12f)));
            Assert.Single(Look(dog, 175, Settler(x: 12f)));
        }

        [Fact]
        public void Standing_still_is_fine_while_working_resting_sitting_or_sleeping()
        {
            foreach (string status in new[] { "$vc_status_felling", "$vc_status_resting", "$vc_status_sleeping", "$vc_status_evening_fire", "", null,
                                              "$vc_status_building", "$vc_status_on_watch" })
            {
                var dog = new Watchdog();
                for (int t = 0; t <= 300; t += 5) Assert.Empty(Look(dog, t, Settler(status: status)));
            }
        }

        [Fact]
        public void A_status_with_words_after_it_counts_by_its_key()
        {
            var dog = new Watchdog();
            for (int t = 0; t < 120; t += 5) Look(dog, t, Settler(status: "$vc_status_fetching Wood"));
            Assert.Single(Look(dog, 120, Settler(status: "$vc_status_fetching Wood")));
        }

        [Fact]
        public void A_new_errand_starts_the_minute_again()
        {
            var dog = new Watchdog();
            for (int t = 0; t <= 110; t += 5) Look(dog, t, Settler(status: "$vc_status_fetching"));
            for (int t = 115; t < 230; t += 5) Assert.Empty(Look(dog, t, Settler(status: "$vc_status_delivering")));
        }

        [Fact]
        public void A_settler_a_test_has_paused_is_never_stuck_or_in_a_wall()
        {
            var dog = new Watchdog();
            for (int t = 0; t <= 600; t += 5)
            {
                SettlerFacts s = Settler();
                s.Paused = true;
                s.InsideSolid = true;
                Assert.Empty(Look(dog, t, s));
            }
        }

        // ---- wall ----

        [Fact]
        public void Inside_a_solid_piece_breaks_at_two_looks_in_a_row()
        {
            var dog = new Watchdog();
            SettlerFacts s = Settler(status: "$vc_status_felling");
            s.InsideSolid = true;
            Assert.Empty(Look(dog, 0, s));
            List<string> lines = Look(dog, 5, s);
            Assert.Single(lines);
            Assert.StartsWith("vc_watchdog: FAIL wall (Ingrid: inside a solid piece at (10, 30, 10)", lines[0]);
        }

        [Fact]
        public void Sitting_or_sleeping_inside_a_piece_is_fine()
        {
            var dog = new Watchdog();
            for (int t = 0; t <= 30; t += 5)
            {
                SettlerFacts s = Settler(status: "$vc_status_sleeping");
                s.InsideSolid = true;
                s.Resting = true;
                Assert.Empty(Look(dog, t, s));
            }
        }

        // ---- jobs ----

        [Fact]
        public void Two_settlers_with_one_id_break_at_two_looks_in_a_row()
        {
            var dog = new Watchdog();
            // A body brought back while an old copy lay unloaded: the old one goes at its next look.
            Assert.Empty(Look(dog, 0, Settler("Ingrid", id: "abc", status: ""), Settler("Leif", x: 50f, id: "abc", status: "")));
            Assert.Empty(Look(dog, 5, Settler("Ingrid", id: "abc", status: "")));
            Assert.Empty(Look(dog, 10, Settler("Ingrid", id: "abc", status: ""), Settler("Leif", x: 50f, id: "abc", status: "")));
            List<string> lines = Look(dog, 15, Settler("Ingrid", id: "abc", status: ""), Settler("Leif", x: 50f, id: "abc", status: ""));
            Assert.Single(lines);
            Assert.StartsWith("vc_watchdog: FAIL jobs (two settlers with the id abc: Ingrid and Leif", lines[0]);
        }

        [Fact]
        public void A_hut_with_more_workers_than_places_breaks_at_two_looks()
        {
            var dog = new Watchdog();
            var huts = new[] { new HutFacts { Id = "h1", Name = "Lumberjack's Hut", Workers = new List<string> { "Ingrid", "Leif" }, Places = 1 } };
            Assert.Empty(dog.Look(0, new SettlerFacts[0], huts));
            List<string> lines = dog.Look(5, new SettlerFacts[0], huts);
            Assert.Single(lines);
            Assert.StartsWith("vc_watchdog: FAIL jobs (Lumberjack's Hut has 2 workers for 1 place: Ingrid, Leif", lines[0]);
            var fine = new[] { new HutFacts { Id = "h2", Name = "Watchtower", Workers = new List<string> { "A", "B" }, Places = 2 } };
            Assert.Empty(new Watchdog().Look(0, new SettlerFacts[0], fine));
        }

        // ---- the throttle and the switches ----

        [Fact]
        public void One_line_per_rule_and_settler_a_minute()
        {
            var dog = new Watchdog();
            Assert.Single(Look(dog, 0, Settler(x: float.NaN)));
            Assert.Empty(Look(dog, 5, Settler(x: float.NaN)));
            Assert.Single(Look(dog, 5, Settler("Leif", x: float.NaN)));   // another settler: a line of their own
            Assert.Single(Look(dog, 61, Settler(x: float.NaN)));
        }

        [Fact]
        public void A_rule_switched_off_is_quiet_and_on_brings_it_back()
        {
            var dog = new Watchdog();
            Assert.True(dog.SetRule("position", false));
            Assert.Empty(Look(dog, 0, Settler(x: float.NaN)));
            Assert.False(dog.IsOn("position"));
            Assert.True(dog.IsOn("stuck"));
            dog.Reset();
            Assert.True(dog.IsOn("position"));
            Assert.Single(Look(dog, 0, Settler(x: float.NaN)));
            Assert.False(dog.SetRule("nonsense", false));
            Assert.True(dog.SetRule("items", false));
            Assert.Empty(dog.Handover(0, "take", new Dictionary<string, int> { { "Wood", 1 } }, null, null, null, _ => true));
        }

        [Fact]
        public void It_counts_what_it_has_seen()
        {
            var dog = new Watchdog();
            Look(dog, 0, Settler(x: float.NaN), Settler("Leif", x: float.NaN));
            Assert.Equal(2, dog.Seen["position"]);
            Assert.Equal(0, dog.Seen["stuck"]);
            Assert.Equal(new[] { "items", "position", "stuck", "wall", "jobs" }, Watchdog.Rules.ToArray());
        }

        [Fact]
        public void A_settler_gone_is_forgotten()
        {
            var dog = new Watchdog();
            for (int t = 0; t <= 110; t += 5) Look(dog, t, Settler());
            Look(dog, 115);   // unloaded (out of range)
            Assert.Empty(Look(dog, 120, Settler()));   // back: a new wait, not the old one
        }
    }
}
