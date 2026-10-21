using System.Collections.Generic;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    public class BuilderFixesTests
    {
        // ---- A hammer swing that never "lands" still puts the piece up ----

        [Fact]
        public void A_swing_that_ended_without_landing_places_the_piece()
        {
            Assert.True(BuildSwing.PlaceAfterSwing(wasInAttack: true, inAttack: false, placed: false, secondsSinceSwing: 1f));
        }

        [Fact]
        public void Nothing_extra_happens_while_swinging_or_once_placed()
        {
            Assert.False(BuildSwing.PlaceAfterSwing(wasInAttack: true, inAttack: true, placed: false, secondsSinceSwing: 1f));
            Assert.False(BuildSwing.PlaceAfterSwing(wasInAttack: true, inAttack: false, placed: true, secondsSinceSwing: 1f));
            Assert.False(BuildSwing.PlaceAfterSwing(wasInAttack: false, inAttack: false, placed: false, secondsSinceSwing: 1f));
        }

        [Fact]
        public void A_swing_that_never_seemed_to_start_places_the_piece_after_a_while()
        {
            Assert.False(BuildSwing.PlaceAfterSwing(false, false, false, BuildSwing.GiveUpSeconds - 0.1f));
            Assert.True(BuildSwing.PlaceAfterSwing(false, false, false, BuildSwing.GiveUpSeconds));
        }

        // ---- Waiting before looking again (stuck or idle builders) ----

        [Fact]
        public void A_cooldown_is_ready_until_started_then_after_its_time()
        {
            var cooldown = new Cooldown(5);
            Assert.True(cooldown.Ready(100));
            cooldown.Start(100);
            Assert.False(cooldown.Ready(104.9));
            Assert.True(cooldown.Ready(105));
        }

        [Fact]
        public void A_cooldown_is_ready_again_when_cleared_or_the_clock_goes_back()
        {
            var cooldown = new Cooldown(5);
            cooldown.Start(100);
            Assert.True(cooldown.Ready(50));
            cooldown.Start(100);
            cooldown.Clear();
            Assert.True(cooldown.Ready(101));
        }

        // ---- Saved orders are read once per change, not every tick ----

        [Fact]
        public void The_same_saved_text_is_read_only_once()
        {
            int reads = 0;
            var cache = new TextCache<string>(text => { reads++; return text.ToUpperInvariant(); });
            string saved = "v=1|state=1";
            Assert.Equal("V=1|STATE=1", cache.Get(saved));
            Assert.Equal("V=1|STATE=1", cache.Get(saved));
            Assert.Equal("V=1|STATE=1", cache.Get(new string(saved.ToCharArray())));
            Assert.Equal(1, reads);
            Assert.Equal("V=1|STATE=2", cache.Get("v=1|state=2"));
            Assert.Equal(2, reads);
        }

        [Fact]
        public void No_saved_text_reads_as_the_decoder_says()
        {
            var cache = new TextCache<string?>(text => text == "" ? null : text);
            Assert.Null(cache.Get(""));
            Assert.Null(cache.Get(null));
        }

        // ---- vc_blueprint save never overwrites a design with one that can't be used ----

        private static readonly string[] HutTypes = { "townhall", "lumberjack_hut", "builder_hut" };

        [Fact]
        public void A_design_named_after_its_own_hut_or_a_free_name_can_be_saved()
        {
            Assert.Null(BlueprintNames.SaveProblem("lumberjack_hut", "lumberjack_hut", HutTypes));
            Assert.Null(BlueprintNames.SaveProblem("my_longhouse", "lumberjack_hut", HutTypes));
        }

        [Fact]
        public void A_design_named_after_another_hut_is_refused_before_anything_is_written()
        {
            string problem = BlueprintNames.SaveProblem("lumberjack_hut", "builder_hut", HutTypes);
            Assert.NotNull(problem);
            Assert.Contains("builder_hut", problem);
            Assert.Contains("lumberjack_hut", problem);
        }

        // 1.4.2 (the hub's audit of the public code): a design's name is a file name, so it can't lead out of the folder.
        [Fact]
        public void A_design_name_with_anything_but_letters_digits_underscore_and_dash_is_refused()
        {
            foreach (string name in new[] { "../../x", "..\\x", "a/b", "C:x", "my design", "", ".hidden", "x.blueprint" })
                Assert.NotNull(BlueprintNames.SaveProblem(name, "lumberjack_hut", HutTypes));
            Assert.Null(BlueprintNames.SaveProblem("my-longhouse_2", "lumberjack_hut", HutTypes));
            Assert.True(BlueprintNames.IsPlainName("Hut_3-b"));
            Assert.False(BlueprintNames.IsPlainName(null));
        }

        [Fact]
        public void A_design_with_nothing_built_around_the_chest_is_refused()
        {
            string problem = BlueprintNames.EmptyProblem(0, 10f);
            Assert.NotNull(problem);
            Assert.Contains("10 m", problem);
            Assert.Null(BlueprintNames.EmptyProblem(1, 10f));
        }

        // ---- He stays on his spot between pieces instead of stepping to a neighbouring one ----

        [Fact]
        public void He_keeps_his_spot_while_it_still_reaches_the_next_piece()
        {
            var spots = new List<Vec3> { new Vec3(-6, 0, 0), new Vec3(-6, 0, 2) };
            var settler = new Vec3(-6, 0, 1.2f);   // stopped short of spot 0, now nearer spot 1
            Assert.Equal(0, StandSpots.Choose(spots, new Vec3(0, 1, 0), 15f, settler, i => true, current: 0));
            Assert.Equal(1, StandSpots.Choose(spots, new Vec3(0, 1, 0), 15f, settler, i => true));
            Assert.Equal(1, StandSpots.Choose(spots, new Vec3(0, 1, 0), 15f, settler, i => i != 0, current: 0));
        }

        // ---- He never builds into another building ----

        [Fact]
        public void Pieces_of_another_building_are_in_the_way()
        {
            Assert.True(SiteRules.IsInTheWay(isThisHutsChest: false, pieceSite: "", thisSite: "hut1", matchesPlan: false));      // a player's piece
            Assert.True(SiteRules.IsInTheWay(isThisHutsChest: false, pieceSite: "hut2", thisSite: "hut1", matchesPlan: false));  // another hut's building
        }

        [Fact]
        public void The_huts_own_chest_its_own_pieces_and_matching_pieces_are_not_in_the_way()
        {
            Assert.False(SiteRules.IsInTheWay(isThisHutsChest: true, pieceSite: "", thisSite: "hut1", matchesPlan: false));
            Assert.False(SiteRules.IsInTheWay(isThisHutsChest: false, pieceSite: "hut1", thisSite: "hut1", matchesPlan: false));
            Assert.False(SiteRules.IsInTheWay(isThisHutsChest: false, pieceSite: "", thisSite: "hut1", matchesPlan: true));
        }

        // ---- He stays where he is while he can reach the piece (the swing slides him a little) ----

        [Fact]
        public void He_works_from_where_he_stands_while_the_piece_is_within_reach()
        {
            var piece = new Vec3(0, 2, 0);
            Assert.True(BuildStance.InPlace(new Vec3(6.4f, 0, 0.3f), piece, 15f, insideFootprint: false));
        }

        [Fact]
        public void He_walks_when_out_of_reach_or_standing_inside_the_building()
        {
            var piece = new Vec3(0, 2, 0);
            Assert.False(BuildStance.InPlace(new Vec3(20, 0, 0), piece, 15f, insideFootprint: false));
            Assert.False(BuildStance.InPlace(new Vec3(1, 0, 0), piece, 15f, insideFootprint: true));
        }

        // ---- The hammer swing doesn't slide a builder forward ----

        [Fact]
        public void Only_a_builder_stays_put_while_swinging()
        {
            Assert.True(BuildSwing.StaysPutWhileSwinging(isSettler: true, isBuilder: true));
            Assert.False(BuildSwing.StaysPutWhileSwinging(isSettler: true, isBuilder: false));   // the lumberjack's axe swing is unchanged
            Assert.False(BuildSwing.StaysPutWhileSwinging(isSettler: false, isBuilder: true));   // players are unchanged
        }

        [Fact]
        public void Only_a_builders_swings_skip_the_games_own_hit()
        {
            Assert.True(BuildSwing.SkipsGameHit(isSettler: true, isBuilder: true));
            Assert.False(BuildSwing.SkipsGameHit(isSettler: true, isBuilder: false));   // the lumberjack's real swings still hit
            Assert.False(BuildSwing.SkipsGameHit(isSettler: false, isBuilder: true));   // players are unchanged
        }

        // ---- Pieces that would be completely underground are left out ----

        [Fact]
        public void A_piece_whose_top_is_well_below_the_ground_is_buried()
        {
            Assert.True(BuildStance.Buried(top: 46.1f, ground: 46.5f));
            Assert.False(BuildStance.Buried(top: 46.6f, ground: 46.5f));
        }

        [Fact]
        public void A_floor_just_sunk_into_the_ground_is_not_buried()
        {
            Assert.False(BuildStance.Buried(top: 46.42f, ground: 46.5f));
        }

        // ---- He stands close to what he is working on ----

        [Fact]
        public void He_works_within_eight_metres_however_long_his_reach()
        {
            Assert.Equal(8f, BuildStance.WorkDistance(15f));
            Assert.Equal(5f, BuildStance.WorkDistance(5f));
        }

        // ---- What's in the way: leftovers of a removed hut's building he takes down; anything else waits for the player ----

        [Fact]
        public void Pieces_of_a_hut_that_is_gone_are_leftovers_he_may_take_down()
        {
            Assert.True(SiteRules.IsLeftover(pieceSite: "oldhut", thisSite: "hut1", siteHutExists: false));
        }

        [Fact]
        public void A_players_own_pieces_and_a_standing_huts_building_are_never_leftovers()
        {
            Assert.False(SiteRules.IsLeftover(pieceSite: "", thisSite: "hut1", siteHutExists: false));
            Assert.False(SiteRules.IsLeftover(pieceSite: "hut2", thisSite: "hut1", siteHutExists: true));
            Assert.False(SiteRules.IsLeftover(pieceSite: "hut1", thisSite: "hut1", siteHutExists: true));
        }

        [Fact]
        public void The_hut_remembers_what_is_in_the_way_until_it_is_finished()
        {
            var order = new BuildOrderRecord { Total = 2, Blocked = "$piece_woodfloor" };
            Assert.Equal("$piece_woodfloor", BuildOrderRecord.Decode(order.Encode()).Blocked);
            order.MarkBuilt();
            Assert.Equal("", order.Blocked);
        }

        // ---- He only picks up wood where one of his own pieces just fell ----

        [Fact]
        public void Only_drops_near_a_recent_fall_are_his_to_pick_up()
        {
            var falls = new RecentSpots(60);
            falls.Add(new Vec3(10, 0, 10), 100);
            Assert.True(falls.Near(new Vec3(11, 0, 10), 3f, 120));
            Assert.False(falls.Near(new Vec3(20, 0, 10), 3f, 120));   // the lumberjack's wood elsewhere
            Assert.False(falls.Near(new Vec3(11, 0, 10), 3f, 161));   // long ago
        }

        // ---- A damaged plan line never stalls an order ----

        [Fact]
        public void A_piece_that_can_never_be_built_counts_as_handled()
        {
            var order = new BuildOrderRecord { Total = 3 };
            order.MarkDone(0);
            order.MarkDone(2);
            Assert.False(order.AllHandled);
            order.MarkSkipped(1);
            Assert.True(order.AllHandled);
            Assert.False(order.IsOpen(1));
        }

        [Fact]
        public void Each_level_has_its_own_design_file()
        {
            Assert.Equal("lumberjack_hut", BlueprintNames.FileName("lumberjack_hut", 1));
            Assert.Equal("lumberjack_hut_3", BlueprintNames.FileName("lumberjack_hut", 3));
            Assert.Equal("builder_hut_5", BlueprintNames.FileName("builder_hut", 9));
            Assert.Equal("builder_hut", BlueprintNames.FileName("builder_hut", 0));
        }
    }
}
