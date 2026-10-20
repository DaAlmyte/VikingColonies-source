using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    public class ClaimBoardTests
    {
        private static readonly GroundPoint Here = new GroundPoint(0, 0);
        private static readonly GroundPoint Far = new GroundPoint(100, 0);

        [Fact]
        public void A_free_thing_can_be_claimed()
        {
            var board = new ClaimBoard();
            Assert.True(board.TryClaim("tree1", Here, "ulf", 0));
            Assert.False(board.IsTakenByOther("tree1", Here, "ulf", 1));
        }

        [Fact]
        public void Someone_elses_claim_keeps_others_away()
        {
            var board = new ClaimBoard();
            board.TryClaim("tree1", Here, "ulf", 0);
            Assert.True(board.IsTakenByOther("tree1", Here, "ivar", 1));
            Assert.False(board.TryClaim("tree1", Here, "ivar", 1));
        }

        [Fact]
        public void A_claim_nobody_works_on_lapses_after_a_minute()
        {
            var board = new ClaimBoard();
            board.TryClaim("tree1", Here, "ulf", 0);
            Assert.True(board.IsTakenByOther("tree1", Here, "ivar", 59));
            Assert.False(board.IsTakenByOther("tree1", Here, "ivar", 60));
        }

        [Fact]
        public void Working_on_it_renews_the_claim()
        {
            var board = new ClaimBoard();
            board.TryClaim("tree1", Here, "ulf", 0);
            board.TryClaim("tree1", Here, "ulf", 50);
            Assert.True(board.IsTakenByOther("tree1", Here, "ivar", 100));
        }

        [Fact]
        public void A_released_claim_is_free_again()
        {
            var board = new ClaimBoard();
            board.TryClaim("tree1", Here, "ulf", 0);
            board.Release("tree1", "ulf");
            Assert.True(board.TryClaim("tree1", Here, "ivar", 1));
        }

        [Fact]
        public void The_logs_and_stump_of_a_felled_tree_belong_to_its_feller()
        {
            var board = new ClaimBoard();
            board.ClaimArea("ulf", Here, 12f, 0);
            Assert.True(board.IsTakenByOther("log7", new GroundPoint(8, 3), "ivar", 1));
            Assert.False(board.IsTakenByOther("log7", new GroundPoint(8, 3), "ulf", 1));
            Assert.False(board.IsTakenByOther("tree9", Far, "ivar", 1));
        }

        [Fact]
        public void Working_inside_the_area_keeps_it_and_leaving_it_lets_it_lapse()
        {
            var board = new ClaimBoard();
            board.ClaimArea("ulf", Here, 12f, 0);
            board.TryClaim("log7", new GroundPoint(5, 0), "ulf", 50);   // still working there
            Assert.True(board.IsTakenByOther("log8", new GroundPoint(6, 0), "ivar", 100));
            Assert.False(board.IsTakenByOther("log8", new GroundPoint(6, 0), "ivar", 111));
        }
    }
}
