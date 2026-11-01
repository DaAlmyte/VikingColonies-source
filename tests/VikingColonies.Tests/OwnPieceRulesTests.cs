using System.Collections.Generic;
using System.Linq;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>
    /// The player's own pieces through a hut's upgrade (1.6):
    /// docs/superpowers/specs/2026-10-25-vikingcolonies-own-pieces-through-upgrades-design.md.
    /// </summary>
    public class OwnPieceRulesTests
    {
        private static PieceBox Box(float x0, float y0, float z0, float x1, float y1, float z1) =>
            new PieceBox(new Vec3(x0, y0, z0), new Vec3(x1, y1, z1));

        // A hut's floor from x 0 to 4, z 0 to 4, its top at y 0; a wall along z = 4 (z 4 to 4.2), 0 to 2 high.
        private static readonly PieceBox Floor = Box(0f, -0.5f, 0f, 4f, 0f, 4f);
        private static readonly PieceBox Wall = Box(0f, 0f, 4f, 4f, 2f, 4.2f);
        private static readonly PieceBox NewWall = Box(0f, 0f, 4f, 4f, 2f, 4.2f);   // the new level's wall in the same place
        private static readonly PieceBox Post = Box(1.9f, 0f, 1.9f, 2.1f, 2f, 2.1f);

        private static OwnPiece Piece(PieceBox box, bool onGround = false, bool holdsThings = false, bool playersBed = false) =>
            new OwnPiece(box, onGround, holdsThings, playersBed);

        private static OwnPieceVerdict One(OwnPiece piece, PieceBox[]? built = null, PieceBox[]? kept = null, PieceBox[]? keptForAWhile = null,
            PieceBox[]? takenAway = null) =>
            OwnPieceRules.Fates(new[] { piece }, built ?? new PieceBox[0], kept ?? new PieceBox[0], keptForAWhile ?? new PieceBox[0],
                takenAway ?? new PieceBox[0])[0];

        // A wall torch hung on the inside of the wall: 0.2 wide, from z 3.8 to 4 (touching the wall), 1.2 to 1.6 high.
        private static readonly PieceBox Torch = Box(1.9f, 1.2f, 3.8f, 2.1f, 1.6f, 4f);

        [Fact]
        public void A_piece_touches_what_it_rests_on_hangs_from_or_runs_into()
        {
            PieceBox bench = Box(1f, 0f, 1f, 2f, 0.5f, 1.5f);
            Assert.True(OwnPieceRules.Touches(bench, Floor));     // rests on it
            Assert.True(OwnPieceRules.Touches(Floor, bench));     // either way round
            Assert.True(OwnPieceRules.Touches(Torch, Wall));      // hangs on its side
            Assert.True(OwnPieceRules.Touches(Box(1f, 0.5f, 3.5f, 2f, 1f, 4.1f), Wall));   // runs into it
            Assert.True(OwnPieceRules.Touches(Box(1f, 2.1f, 1f, 1.2f, 2.5f, 1.2f), Box(0f, 2.5f, 0f, 4f, 2.8f, 4f)));   // hangs under a beam
            Assert.False(OwnPieceRules.Touches(bench, Wall));     // 2.5 m away
            Assert.False(OwnPieceRules.Touches(Box(1f, 0.5f, 3.5f, 2f, 1f, 3.7f), Wall));   // 0.3 m short of it
            Assert.False(OwnPieceRules.Touches(Box(4.05f, 0f, 4.25f, 5f, 1f, 5f), Wall));   // only a corner near it
        }

        [Fact]
        public void A_bench_on_a_kept_floor_beside_a_replaced_wall_stays()
        {
            PieceBox bench = Box(1f, 0f, 3.6f, 2f, 0.5f, 4f);   // pushed against the wall
            OwnPieceVerdict v = One(Piece(bench), built: new[] { NewWall }, kept: new[] { Floor }, takenAway: new[] { Wall });
            Assert.Equal(OwnPieceFate.Stays, v.Fate);   // held from below beats touching
        }

        [Fact]
        public void An_empty_wall_torch_on_a_replaced_wall_is_put_back()
        {
            OwnPieceVerdict v = One(Piece(Torch), built: new[] { NewWall }, kept: new[] { Floor }, takenAway: new[] { Wall });
            Assert.Equal(OwnPieceFate.PutBack, v.Fate);
            Assert.Equal(0, v.Which);   // the first piece taken away it touches
            Assert.Equal(OwnPieceWait.None, v.Wait);
        }

        [Fact]
        public void A_torch_on_a_kept_post_stays()
        {
            PieceBox torch = Box(2.1f, 1.2f, 1.9f, 2.3f, 1.6f, 2.1f);
            Assert.Equal(OwnPieceFate.Stays, One(Piece(torch), built: new[] { NewWall }, kept: new[] { Floor, Post }, takenAway: new[] { Wall }).Fate);
        }

        [Fact]
        public void An_empty_chest_on_a_replaced_floor_is_put_back_and_a_full_one_waits()
        {
            PieceBox chest = Box(1f, 0f, 1f, 2f, 0.8f, 1.6f);
            PieceBox newFloor = Box(0f, -0.5f, 0f, 4f, 0f, 4f);
            Assert.Equal(OwnPieceFate.PutBack, One(Piece(chest), built: new[] { newFloor }, takenAway: new[] { Floor }).Fate);
            OwnPieceVerdict full = One(Piece(chest, holdsThings: true), built: new[] { newFloor }, takenAway: new[] { Floor });
            Assert.Equal(OwnPieceFate.Waits, full.Fate);   // the old chest rule said it stays: it would have fallen and spilled
            Assert.Equal(OwnPieceWait.HoldsThings, full.Wait);
        }

        [Fact]
        public void An_item_stand_where_a_new_wall_goes_is_in_the_way()
        {
            PieceBox stand = Box(1f, 0.5f, 2f, 1.5f, 1f, 2.3f);
            PieceBox newInnerWall = Box(0f, 0f, 2f, 4f, 2f, 2.2f);
            OwnPieceVerdict v = One(Piece(stand), built: new[] { NewWall, newInnerWall }, kept: new[] { Floor }, takenAway: new[] { Wall });
            Assert.Equal(OwnPieceFate.InTheWay, v.Fate);
            Assert.Equal(1, v.Which);   // the built piece it is in the way of
        }

        [Fact]
        public void An_item_stand_with_a_trophy_on_a_replaced_wall_waits()
        {
            OwnPieceVerdict v = One(Piece(Torch, holdsThings: true), built: new[] { NewWall }, kept: new[] { Floor }, takenAway: new[] { Wall });
            Assert.Equal(OwnPieceFate.Waits, v.Fate);
            Assert.Equal(OwnPieceWait.HoldsThings, v.Wait);
        }

        [Fact]
        public void A_rug_sunk_into_a_kept_floor_stays()
        {
            PieceBox rug = Box(1f, -0.05f, 1f, 3f, 0.02f, 3f);
            Assert.Equal(OwnPieceFate.Stays, One(Piece(rug), built: new[] { NewWall }, kept: new[] { Floor }, takenAway: new[] { Wall }).Fate);
        }

        [Fact]
        public void A_lantern_on_the_players_own_table_stays_with_it()
        {
            PieceBox table = Box(1f, 0f, 1f, 2f, 0.8f, 2f);
            PieceBox lantern = Box(1.4f, 0.8f, 1.4f, 1.6f, 1.2f, 1.6f);
            List<OwnPieceVerdict> v = OwnPieceRules.Fates(new[] { Piece(table), Piece(lantern) }, new[] { NewWall }, new[] { Floor },
                new PieceBox[0], new[] { Wall });
            Assert.Equal(OwnPieceFate.Stays, v[0].Fate);
            Assert.Equal(OwnPieceFate.Stays, v[1].Fate);
        }

        [Fact]
        public void A_lantern_on_a_table_that_is_moved_follows_it()
        {
            PieceBox table = Box(1f, 0f, 2f, 2f, 0.8f, 2.6f);
            PieceBox lantern = Box(1.4f, 0.8f, 2.2f, 1.6f, 1.2f, 2.4f);
            PieceBox newInnerWall = Box(0f, 0f, 2f, 4f, 2f, 2.2f);   // goes through the table
            List<OwnPieceVerdict> v = OwnPieceRules.Fates(new[] { Piece(table), Piece(lantern) }, new[] { NewWall, newInnerWall },
                new[] { Floor }, new PieceBox[0], new[] { Wall });
            Assert.Equal(OwnPieceFate.InTheWay, v[0].Fate);
            Assert.NotEqual(OwnPieceFate.Stays, v[1].Fate);   // the player moves the table: the lantern can't stay on it
        }

        [Fact]
        public void A_torch_on_a_replaced_wall_with_nothing_of_the_new_level_beside_it_waits()
        {
            OwnPieceVerdict v = One(Piece(Torch), built: new[] { Box(0f, 0f, 0f, 0.2f, 2f, 4f) }, kept: new[] { Floor }, takenAway: new[] { Wall });
            Assert.Equal(OwnPieceFate.Waits, v.Fate);
            Assert.Equal(OwnPieceWait.NothingHoldsIt, v.Wait);
        }

        [Fact]
        public void A_kept_piece_taken_down_for_a_while_counts_as_taken_away_and_holds_it_after()
        {
            PieceBox torch = Box(2.1f, 1.2f, 1.9f, 2.3f, 1.6f, 2.1f);   // on the post
            OwnPieceVerdict v = One(Piece(torch), built: new[] { NewWall }, kept: new[] { Floor }, keptForAWhile: new[] { Post },
                takenAway: new[] { Wall });
            Assert.Equal(OwnPieceFate.PutBack, v.Fate);
            Assert.Equal(1, v.Which);   // after the taken-away pieces: the kept ones taken down for a while
        }

        [Fact]
        public void A_cleared_leftover_counts_as_taken_away()
        {
            PieceBox leftoverBeam = Box(0f, 2f, 1.9f, 4f, 2.2f, 2.1f);
            PieceBox banner = Box(1f, 1.2f, 1.92f, 1.5f, 2f, 2.08f);   // hangs under it
            Assert.Equal(OwnPieceFate.Waits, One(Piece(banner), kept: new[] { Floor }, takenAway: new[] { leftoverBeam }).Fate);
        }

        [Fact]
        public void A_players_bed_waits()
        {
            PieceBox bed = Box(1f, 0.05f, 3f, 3f, 0.6f, 4f);   // on a replaced floor, against a replaced wall
            OwnPieceVerdict v = One(Piece(bed, playersBed: true), built: new[] { NewWall }, takenAway: new[] { Wall, Floor },
                kept: new PieceBox[0]);
            Assert.Equal(OwnPieceFate.Waits, v.Fate);
            Assert.Equal(OwnPieceWait.PlayersBed, v.Wait);
        }

        [Fact]
        public void A_piece_on_the_ground_stays_and_one_touching_nothing_taken_away_stays()
        {
            PieceBox torch = Box(1.9f, 1.2f, 3.8f, 2.1f, 1.6f, 4f);
            Assert.Equal(OwnPieceFate.Stays, One(Piece(Box(5f, 0f, 5f, 6f, 1f, 6f), onGround: true), takenAway: new[] { Wall }).Fate);
            Assert.Equal(OwnPieceFate.Stays, One(Piece(torch), kept: new[] { Wall, Floor }, takenAway: new[] { Box(0f, 0f, 0f, 0.2f, 2f, 4f) }).Fate);
        }

        [Fact]
        public void A_take_down_names_what_rests_on_the_building()
        {
            PieceBox bench = Box(1f, 0f, 1f, 2f, 0.5f, 1.5f);
            PieceBox outside = Box(5f, 0f, 5f, 6f, 1f, 6f);
            List<int> falls = OwnPieceRules.ForTakeDown(new[] { Piece(bench), Piece(Torch), Piece(outside, onGround: true) },
                new[] { Floor, Wall, Post });
            Assert.Equal(new[] { 0, 1 }, falls);
        }

        [Fact]
        public void A_put_back_comes_down_before_what_it_touches_and_goes_back_once_the_new_pieces_stand()
        {
            Assert.True(OwnPieceRules.TakeDownFirst(Torch, Wall));
            Assert.False(OwnPieceRules.TakeDownFirst(Torch, Post));
            Assert.False(OwnPieceRules.ReadyToGoBack(Torch, oldStanding: new[] { Wall }, newLevel: new[] { NewWall }, newStanding: new[] { false }));
            Assert.False(OwnPieceRules.ReadyToGoBack(Torch, new PieceBox[0], new[] { NewWall }, new[] { false }));   // the new wall isn't up
            Assert.True(OwnPieceRules.ReadyToGoBack(Torch, new PieceBox[0], new[] { NewWall, Post }, new[] { true, false }));   // the post doesn't touch it
        }

        // A shelf of the player's hung on the replaced wall, a lantern of theirs on the shelf, touching the wall too.
        private static readonly PieceBox Shelf = Box(0.5f, 1f, 3.6f, 1.5f, 1.2f, 4f);
        private static readonly PieceBox ShelfLantern = Box(0.8f, 1.2f, 3.8f, 1.0f, 1.5f, 4f);

        [Fact]
        public void The_builder_takes_down_the_put_back_on_the_old_piece_and_what_rests_on_it_first()
        {
            PieceBox bench = Box(1f, 0f, 1f, 2f, 0.5f, 1.5f);
            Assert.Equal(1, OwnPieceRules.FirstToTakeDown(Wall, new[] { bench, Torch }));
            Assert.Equal(-1, OwnPieceRules.FirstToTakeDown(Wall, new[] { bench }));   // nothing of the player's on it
            Assert.Equal(-1, OwnPieceRules.FirstToTakeDown(Post, new[] { Torch }));
            // The shelf touches the wall, and the lantern on the shelf would fall with it: the lantern first, then the shelf.
            Assert.Equal(1, OwnPieceRules.FirstToTakeDown(Wall, new[] { Shelf, ShelfLantern }));
            Assert.Equal(0, OwnPieceRules.FirstToTakeDown(Wall, new[] { ShelfLantern, Shelf }));
            Assert.Equal(0, OwnPieceRules.FirstToTakeDown(Wall, new[] { Shelf }));
        }

        [Fact]
        public void The_builder_puts_back_a_piece_once_what_holds_it_stands_and_what_it_rests_on_first()
        {
            var newLevel = new[] { NewWall, Post };
            Assert.Equal(-1, OwnPieceRules.NextToPutBack(new[] { Torch }, new[] { Wall }, newLevel, new[] { true, true }));   // the old wall stands
            Assert.Equal(-1, OwnPieceRules.NextToPutBack(new[] { Torch }, new PieceBox[0], newLevel, new[] { false, true }));   // the new one isn't up
            Assert.Equal(0, OwnPieceRules.NextToPutBack(new[] { Torch }, new PieceBox[0], newLevel, new[] { true, false }));
            Assert.Equal(-1, OwnPieceRules.NextToPutBack(new PieceBox[0], new PieceBox[0], newLevel, new[] { true, true }));
            // The lantern rests on the shelf: the shelf goes back first, whichever is listed first.
            Assert.Equal(1, OwnPieceRules.NextToPutBack(new[] { ShelfLantern, Shelf }, new PieceBox[0], newLevel, new[] { true, true }));
            Assert.Equal(0, OwnPieceRules.NextToPutBack(new[] { Shelf, ShelfLantern }, new PieceBox[0], newLevel, new[] { true, true }));
            // Two torches side by side at the same height: one after the other, never each waiting for the other.
            PieceBox nextTorch = Box(2.1f, 1.2f, 3.8f, 2.3f, 1.6f, 4f);
            Assert.Equal(0, OwnPieceRules.NextToPutBack(new[] { Torch, nextTorch }, new PieceBox[0], newLevel, new[] { true, true }));
            Assert.Equal(0, OwnPieceRules.NextToPutBack(new[] { nextTorch }, new PieceBox[0], newLevel, new[] { true, true }));
        }

        // A beam of the player's laid on the replaced wall, a torch of theirs hung on the beam's side and nothing else.
        private static readonly PieceBox OwnBeam = Box(0f, 2f, 3.8f, 4f, 2.3f, 4.2f);
        private static readonly PieceBox BeamTorch = Box(1.9f, 1.95f, 3.6f, 2.1f, 2.35f, 3.8f);

        [Fact]
        public void A_piece_hanging_from_another_of_the_players_depends_on_it()
        {
            Assert.True(OwnPieceRules.DependsOn(ShelfLantern, Shelf));   // rests on it
            Assert.False(OwnPieceRules.DependsOn(Shelf, ShelfLantern));
            Assert.True(OwnPieceRules.DependsOn(BeamTorch, OwnBeam));     // hangs from its side: the smaller from the bigger
            Assert.False(OwnPieceRules.DependsOn(OwnBeam, BeamTorch));
            Assert.False(OwnPieceRules.DependsOn(BeamTorch, Wall));       // doesn't touch it
            PieceBox nextTorch = Box(2.1f, 1.2f, 3.8f, 2.3f, 1.6f, 4f);
            Assert.False(OwnPieceRules.DependsOn(Torch, nextTorch));      // two the same side by side hold neither
            Assert.False(OwnPieceRules.DependsOn(nextTorch, Torch));
            PieceBox post = Box(1.9f, 0f, 1.9f, 2.1f, 1f, 2.1f);          // a big table of theirs on a small post of theirs
            PieceBox table = Box(1f, 1f, 1f, 3f, 1.1f, 3f);
            Assert.True(OwnPieceRules.DependsOn(table, post));
            Assert.False(OwnPieceRules.DependsOn(post, table));
        }

        [Fact]
        public void What_hangs_from_the_players_own_piece_comes_down_before_it_and_goes_back_after()
        {
            // The review's finding 3: the beam came down first (the torch fell), or the torch went back first (it failed
            // three times and was lost).
            Assert.Equal(1, OwnPieceRules.FirstToTakeDown(Wall, new[] { OwnBeam, BeamTorch }));
            Assert.Equal(0, OwnPieceRules.FirstToTakeDown(Wall, new[] { BeamTorch, OwnBeam }));
            var newLevel = new[] { NewWall, Post };
            Assert.Equal(1, OwnPieceRules.NextToPutBack(new[] { BeamTorch, OwnBeam }, new PieceBox[0], newLevel, new[] { true, true }));
            Assert.Equal(0, OwnPieceRules.NextToPutBack(new[] { OwnBeam, BeamTorch }, new PieceBox[0], newLevel, new[] { true, true }));
            Assert.Equal(0, OwnPieceRules.NextToPutBack(new[] { BeamTorch }, new PieceBox[0], newLevel, new[] { true, true }));   // the beam is back
        }

        [Fact]
        public void Kept_pieces_resting_on_a_taken_away_piece_alone_come_down_for_a_while()
        {
            PieceBox beam = Box(0f, 2f, 1.9f, 4f, 2.2f, 2.1f);          // an old beam the upgrade replaces
            PieceBox pole = Box(1.9f, 2.2f, 1.9f, 2.1f, 3f, 2.1f);      // a kept pole on it alone
            PieceBox ornament = Box(2.9f, 2.2f, 1.9f, 3.1f, 2.6f, 2.1f);   // a kept ornament on the beam and the kept post
            PieceBox keptPost = Box(2.9f, 0f, 1.9f, 3.1f, 2.2f, 2.1f);
            List<int> forAWhile = OwnPieceRules.KeptForAWhile(new[] { Floor, pole, ornament, keptPost }, new[] { beam }, b => b.Min.Y <= -0.5f);
            Assert.Equal(new[] { 1 }, forAWhile);
            Assert.Empty(OwnPieceRules.KeptForAWhile(new[] { Floor, pole }, new PieceBox[0], b => false));
        }

        [Fact]
        public void The_first_piece_the_upgrade_waits_for_and_how_many_go_back()
        {
            var verdicts = new[]
            {
                new OwnPieceVerdict(OwnPieceFate.Stays, OwnPieceWait.None, -1),
                new OwnPieceVerdict(OwnPieceFate.PutBack, OwnPieceWait.None, 0),
                new OwnPieceVerdict(OwnPieceFate.Waits, OwnPieceWait.HoldsThings, -1),
                new OwnPieceVerdict(OwnPieceFate.PutBack, OwnPieceWait.None, -1),
                new OwnPieceVerdict(OwnPieceFate.InTheWay, OwnPieceWait.None, 3),
            };
            Assert.Equal(2, OwnPieceRules.FirstWait(verdicts));
            Assert.Equal(2, OwnPieceRules.PutBackCount(verdicts));
            Assert.Equal(-1, OwnPieceRules.FirstWait(verdicts.Take(2).ToList()));
            Assert.Equal(0, OwnPieceRules.PutBackCount(new OwnPieceVerdict[0]));
        }

        [Fact]
        public void The_window_says_why_the_upgrade_waits_for_a_piece()
        {
            Assert.Equal(OwnPieceWhy.None, OwnPieceRules.WhyOf(new OwnPieceVerdict(OwnPieceFate.Stays, OwnPieceWait.None, -1), false));
            Assert.Equal(OwnPieceWhy.None, OwnPieceRules.WhyOf(new OwnPieceVerdict(OwnPieceFate.PutBack, OwnPieceWait.None, 0), false));
            Assert.Equal(OwnPieceWhy.InTheWay, OwnPieceRules.WhyOf(new OwnPieceVerdict(OwnPieceFate.InTheWay, OwnPieceWait.None, 2), false));
            Assert.Equal(OwnPieceWhy.Full, OwnPieceRules.WhyOf(new OwnPieceVerdict(OwnPieceFate.Waits, OwnPieceWait.HoldsThings, -1), false));
            Assert.Equal(OwnPieceWhy.HasItem, OwnPieceRules.WhyOf(new OwnPieceVerdict(OwnPieceFate.Waits, OwnPieceWait.HoldsThings, -1), true));
            Assert.Equal(OwnPieceWhy.PlayersBed, OwnPieceRules.WhyOf(new OwnPieceVerdict(OwnPieceFate.Waits, OwnPieceWait.PlayersBed, -1), false));
            Assert.Equal(OwnPieceWhy.NothingHoldsIt, OwnPieceRules.WhyOf(new OwnPieceVerdict(OwnPieceFate.Waits, OwnPieceWait.NothingHoldsIt, -1), true));
            // What a piece holds says the line: a ward's list or a map table's map can't go back with it (the review's finding 1).
            var holds = new OwnPieceVerdict(OwnPieceFate.Waits, OwnPieceWait.HoldsThings, -1);
            Assert.Equal(OwnPieceWhy.KeepsWhat, OwnPieceRules.WhyOf(holds, OwnPieceWhy.KeepsWhat));
            Assert.Equal(OwnPieceWhy.HasItem, OwnPieceRules.WhyOf(holds, OwnPieceWhy.HasItem));
            Assert.Equal(OwnPieceWhy.Full, OwnPieceRules.WhyOf(holds, OwnPieceWhy.Full));
            Assert.Equal(OwnPieceWhy.Full, OwnPieceRules.WhyOf(holds, OwnPieceWhy.None));
            Assert.Equal(OwnPieceWhy.PlayersBed, OwnPieceRules.WhyOf(new OwnPieceVerdict(OwnPieceFate.Waits, OwnPieceWait.PlayersBed, -1), OwnPieceWhy.KeepsWhat));
        }

        [Fact]
        public void A_box_in_the_huts_frame_doesnt_swell_when_the_hut_is_turned()
        {
            // A 4 m wide, 0.2 m thick wall of a hut turned 45°: along the world's axes its box is 3 m each way.
            Quat turn = Quat.Yaw(45f);
            var origin = new Vec3(100f, 10f, 100f);
            Vec3 at = origin + turn.Rotate(new Vec3(0f, 0f, 4f));
            PieceBox box = OwnPieceRules.BoxIn(new Vec3(-2f, 0f, -0.1f), new Vec3(2f, 2f, 0.1f), at, turn, origin, turn);
            Assert.Equal(-2f, box.Min.X, 3);
            Assert.Equal(2f, box.Max.X, 3);
            Assert.Equal(3.9f, box.Min.Z, 3);
            Assert.Equal(4.1f, box.Max.Z, 3);
            Assert.Equal(0f, box.Min.Y, 3);
            Assert.Equal(2f, box.Max.Y, 3);
        }
    }
}
