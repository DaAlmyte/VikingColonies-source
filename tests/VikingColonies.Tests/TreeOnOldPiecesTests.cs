using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>
    /// The old-huts upgrade test (2026-10-19): "Leif can't fell Birch1 without hitting a building; it stays in the way", and
    /// the Builder's Hut's site waited on the player for good: the only building in the tree's way was the hut's own old
    /// pieces, which the upgrade takes down anyway.
    /// </summary>
    public class TreeOnOldPiecesTests
    {
        [Fact]
        public void A_tree_may_fall_on_the_sites_own_old_pieces_due_to_come_down_never_on_anything_else_built()
        {
            Assert.False(FallPlanner.MustMiss(placedByPlayer: true, dueToComeDown: true));
            Assert.True(FallPlanner.MustMiss(placedByPlayer: true, dueToComeDown: false));   // kept, or anyone else's
            Assert.False(FallPlanner.MustMiss(placedByPlayer: false, dueToComeDown: false));
        }
    }
}
