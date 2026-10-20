using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>
    /// The speed runs on 1.4.1 (2026-10-20): on uneven ground the Lumberjack's Hut's door torch fell within seconds of the
    /// hut going up. The levelling left 3 of the site's 12 spots as they were, "near another building": mushrooms lying
    /// on the ground beside the hut's west wall (the game gives a mushroom item a Piece). The ground under the torch stayed
    /// a metre below its foot. Ground work keeps off another building, never off a loose item, a wild plant or a
    /// settler's sapling: they aren't built.
    /// </summary>
    public class GroundKeepOffTests
    {
        [Fact]
        public void Another_buildings_pieces_keep_the_ground_work_off()
        {
            Assert.True(SiteRules.KeepsGroundOff(isThisHutsChest: false, pieceSite: "", thisSite: "hut1", matchesPlan: false, vegetation: false, looseItem: false));      // a player's piece
            Assert.True(SiteRules.KeepsGroundOff(isThisHutsChest: false, pieceSite: "hut2", thisSite: "hut1", matchesPlan: false, vegetation: false, looseItem: false));  // another hut's building
        }

        [Fact]
        public void Wild_plants_and_a_settlers_sapling_dont_keep_the_ground_work_off()
        {
            Assert.False(SiteRules.KeepsGroundOff(isThisHutsChest: false, pieceSite: "", thisSite: "hut1", matchesPlan: false, vegetation: true, looseItem: false));
        }

        [Fact]
        public void A_loose_item_lying_there_doesnt_keep_the_ground_work_off()
        {
            Assert.False(SiteRules.KeepsGroundOff(isThisHutsChest: false, pieceSite: "", thisSite: "hut1", matchesPlan: false, vegetation: false, looseItem: true));
        }

        // The same mushrooms on a site: the site check took them for another building's pieces (red), and the builder
        // never carried them off. One a player placed is theirs, as any piece.
        [Fact]
        public void A_dropped_item_is_a_loose_item_one_a_player_placed_is_a_piece()
        {
            Assert.True(SiteRules.IsLooseItem(isItem: true, placedByPlayer: false));
            Assert.False(SiteRules.IsLooseItem(isItem: true, placedByPlayer: true));
            Assert.False(SiteRules.IsLooseItem(isItem: false, placedByPlayer: false));
        }

        [Fact]
        public void The_huts_own_chest_its_own_pieces_and_matching_pieces_dont_keep_the_ground_work_off()
        {
            Assert.False(SiteRules.KeepsGroundOff(isThisHutsChest: true, pieceSite: "", thisSite: "hut1", matchesPlan: false, vegetation: false, looseItem: false));
            Assert.False(SiteRules.KeepsGroundOff(isThisHutsChest: false, pieceSite: "hut1", thisSite: "hut1", matchesPlan: false, vegetation: false, looseItem: false));
            Assert.False(SiteRules.KeepsGroundOff(isThisHutsChest: false, pieceSite: "", thisSite: "hut1", matchesPlan: true, vegetation: false, looseItem: false));
        }
    }
}
