using System.Collections.Generic;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    public class DesignDiffTests
    {
        private static PlannedPiece P(int index, string prefab, float x, float z, float yaw = 0f) =>
            new PlannedPiece(index, prefab, new Vec3(x, 0f, z), Quat.Yaw(yaw));

        [Fact]
        public void Pieces_in_the_same_place_stay_the_rest_are_taken_away_or_built()
        {
            var old = new List<PlannedPiece> { P(0, "wood_floor", 0, 0), P(1, "woodwall", 0, 2), P(2, "woodwall", 2, 0) };
            var next = new List<PlannedPiece> { P(0, "wood_floor", 0, 0), P(1, "wood_wall_log", 0, 2), P(2, "woodwall", 2, 0), P(3, "wood_floor", 0, -2) };
            DesignDiff diff = DesignDiff.Compare(old, next);
            Assert.Equal(new[] { 0, 2 }, diff.Kept);
            Assert.Equal(new[] { 1, 3 }, diff.Build);
            Assert.Equal(new[] { 1 }, diff.TakeAway);
        }

        [Fact]
        public void A_piece_replaced_by_another_kind_in_the_same_place_is_taken_away_and_built()
        {
            DesignDiff diff = DesignDiff.Compare(new List<PlannedPiece> { P(0, "woodwall", 0, 2) }, new List<PlannedPiece> { P(0, "stone_wall_2x1", 0, 2) });
            Assert.Empty(diff.Kept);
            Assert.Equal(new[] { 0 }, diff.TakeAway);
            Assert.Equal(new[] { 0 }, diff.Build);
        }

        [Fact]
        public void A_piece_moved_10_cm_or_turned_is_taken_away_and_built_again()
        {
            DesignDiff moved = DesignDiff.Compare(new List<PlannedPiece> { P(0, "woodwall", 0, 2) }, new List<PlannedPiece> { P(0, "woodwall", 0.1f, 2) });
            Assert.Empty(moved.Kept);
            DesignDiff turned = DesignDiff.Compare(new List<PlannedPiece> { P(0, "woodwall", 0, 2) }, new List<PlannedPiece> { P(0, "woodwall", 0, 2, 90f) });
            Assert.Empty(turned.Kept);
            DesignDiff close = DesignDiff.Compare(new List<PlannedPiece> { P(0, "woodwall", 0, 2) }, new List<PlannedPiece> { P(0, "woodwall", 0.04f, 2, 1f) });
            Assert.Equal(new[] { 0 }, close.Kept);
        }

        // The real builders' run on ship-huts (2026-10-19): the Lumberjack's item stand moves 10 cm out at level 5 (the
        // grausten wall's face); the diff took the old one away and built the new one, but a standing piece was known
        // within 10 cm, so the builder took his new item stand for the old one, took it down three times and skipped it.
        [Fact]
        public void A_standing_piece_is_known_as_the_diff_knows_it()
        {
            DesignDiff diff = DesignDiff.Compare(new List<PlannedPiece> { P(0, "itemstand", 3.2f, 3.4f) }, new List<PlannedPiece> { P(0, "itemstand", 3.2f, 3.5f) });
            Assert.Equal(new[] { 0 }, diff.TakeAway);
            Assert.Equal(new[] { 0 }, diff.Build);
            Assert.False(DesignDiff.SamePiece(0.1f, 0f));    // the new one standing isn't the old one, nor the old one the new
            Assert.False(DesignDiff.SamePiece(0f, 3f));
            Assert.True(DesignDiff.SamePiece(0.001f, 0.5f));   // a piece where its plan put it
        }

        [Fact]
        public void Two_identical_pieces_in_one_place_are_matched_one_to_one()
        {
            DesignDiff diff = DesignDiff.Compare(new List<PlannedPiece> { P(0, "wood_floor", 0, 0), P(1, "wood_floor", 0, 0) }, new List<PlannedPiece> { P(0, "wood_floor", 0, 0) });
            Assert.Equal(new[] { 0 }, diff.Kept);
            Assert.Equal(new[] { 1 }, diff.TakeAway);
        }
    }
}
