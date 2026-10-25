using System.Collections.Generic;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>The review round of 23 October 2026, the building review.</summary>
    public class ReviewRoundBuildingTests
    {
        [Fact]   // Important: an old Longhouse's upgrade "took down" a player's spawn bed again and again, left standing each time, and never finished
        public void An_old_piece_that_stays_is_in_the_way_not_taken_down()
        {
            Assert.True(TakeDownRules.OldPieceBlocks(isPlayersBed: true, countedTaken: false));
            Assert.True(TakeDownRules.OldPieceBlocks(isPlayersBed: false, countedTaken: true));
            Assert.False(TakeDownRules.OldPieceBlocks(isPlayersBed: false, countedTaken: false));
        }

        [Fact]   // Important: a fence piece a troll knocked off a Farm opened a repair, and the builder pulled up every crop in its yard
        public void A_repair_leaves_the_plants_where_the_farmer_farms()
        {
            Assert.False(SiteRules.ClearsPlants(OrderKind.Repair));
            Assert.True(SiteRules.ClearsPlants(OrderKind.Build));
            Assert.True(SiteRules.ClearsPlants(OrderKind.Upgrade));
            // the same orders the farmer leaves the Farm's yard to the builder for (1.4.3's FieldPlots.SiteBusy)
            foreach (OrderKind kind in new[] { OrderKind.Build, OrderKind.Upgrade, OrderKind.Repair })
                Assert.Equal(FieldPlots.SiteBusy(kind, done: false), SiteRules.ClearsPlants(kind));
        }
    
        [Fact]   // the 1.5.0 build-through (a Longhouse's level 4 finished with two kept poles on the ground) and its fix's review
                 // (reopened at once, a kept piece lower than the new one could go straight back up on the old piece, again and again)
        public void A_kept_piece_taken_down_goes_back_on_the_list_once_its_old_piece_is_down()
        {
            var kept = new KeptDown();
            kept.Add(keptIndex: 12, oldIndex: 3);
            kept.Add(keptIndex: 20, oldIndex: -1);   // no old piece to wait for
            var taken = new HashSet<int>();
            Assert.Equal(new[] { 20 }, kept.Due(taken.Contains));
            Assert.Empty(kept.Due(taken.Contains));   // once only
            taken.Add(3);
            Assert.Equal(new[] { 12 }, kept.Due(taken.Contains));
            Assert.Empty(kept.Due(taken.Contains));
            kept.Add(5, 7);
            kept.Clear();   // he left the site: the site's own look finds it
            taken.Add(7);
            Assert.Empty(kept.Due(taken.Contains));
        }
    }
}
