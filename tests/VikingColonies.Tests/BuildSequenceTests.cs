using System.Collections.Generic;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    public class BuildSequenceTests
    {
        private static PlannedPiece P(int index, float x, float y, float z, string prefab = "wood_floor") =>
            new PlannedPiece(index, prefab, new Vec3(x, y, z), Quat.Identity);

        [Fact]
        public void Floors_come_before_the_roof_and_the_middle_before_the_edge()
        {
            var plan = new List<PlannedPiece> { P(0, 0, 2, 0), P(1, 4, 0, 0), P(2, 1, 0, 0), P(3, 0, 0, 0, "") };
            Assert.Equal(new[] { 2, 1, 0 }, BuildSequence.Order(plan, Vec3.Zero));
        }

        [Fact]
        public void The_next_piece_is_the_first_open_one_that_will_hold()
        {
            var cursor = new BuildCursor(new[] { 5, 6, 7 });
            int next = cursor.Next(i => i != 5, i => i == 7, 10);
            Assert.Equal(7, next);
            Assert.False(cursor.Stuck);
        }

        [Fact]
        public void It_tries_only_a_few_pieces_per_call_and_carries_on_where_it_stopped()
        {
            var cursor = new BuildCursor(new[] { 1, 2, 3, 4 });
            var tried = new List<int>();
            Assert.Equal(-1, cursor.Next(i => true, i => { tried.Add(i); return false; }, 2));
            Assert.Equal(-1, cursor.Next(i => true, i => { tried.Add(i); return false; }, 2));
            Assert.Equal(new[] { 1, 2, 3, 4 }, tried);
        }

        [Fact]
        public void When_no_open_piece_will_hold_it_is_stuck_and_says_which_it_tried_last()
        {
            var cursor = new BuildCursor(new[] { 1, 2 });
            cursor.Next(i => true, i => false, 1);
            Assert.False(cursor.Stuck);
            cursor.Next(i => true, i => false, 1);
            Assert.True(cursor.Stuck);
            Assert.Equal(2, cursor.LastTried);
            cursor.Restart();
            Assert.False(cursor.Stuck);
        }

        [Fact]
        public void After_finding_a_piece_it_starts_again_from_the_lowest()
        {
            var cursor = new BuildCursor(new[] { 1, 2, 3 });
            Assert.Equal(2, cursor.Next(i => true, i => i == 2, 5));
            var tried = new List<int>();
            cursor.Next(i => true, i => { tried.Add(i); return false; }, 1);
            Assert.Equal(new[] { 1 }, tried);
        }

        [Fact]
        public void Nothing_open_means_nothing_to_do_and_not_stuck()
        {
            var cursor = new BuildCursor(new[] { 1, 2 });
            Assert.Equal(-1, cursor.Next(i => false, i => true, 5));
            Assert.False(cursor.Stuck);
        }
    }
}
