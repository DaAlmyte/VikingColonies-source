using System.Collections.Generic;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    public class BuildablePiecesTests
    {
        private static BuildablePieces Tools() => new BuildablePieces(new List<IEnumerable<string>>
        {
            new[] { "woodwall", "wood_floor", "piece_chest_wood", "vc_hut_builder" },   // the Hammer, the mod's huts on it
            new[] { "sapling_carrot", "sapling_turnip" },                              // the Cultivator
            new[] { "raise", "path" },                                                 // the Hoe
        });

        [Theory]
        [InlineData("woodwall")]
        [InlineData("vc_hut_builder")]
        [InlineData("sapling_carrot")]
        [InlineData("path")]
        public void A_piece_on_a_build_tools_list_may_be_put_up(string prefab)
        {
            Assert.True(Tools().Allows(prefab));
        }

        [Theory]
        [InlineData("Eikthyr")]                // a boss
        [InlineData("SwordIronFire")]          // an item: it would lie there for anyone to take
        [InlineData("TreasureChest_meadows")]  // a loot chest
        [InlineData("Pickable_Thistle")]       // a pickable: free to pick
        public void Anything_no_build_tool_makes_is_refused(string prefab)
        {
            Assert.False(Tools().Allows(prefab));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("Woodwall")]    // the game's names are exact
        [InlineData(" woodwall")]
        public void Names_are_matched_exactly_and_an_empty_one_is_refused(string prefab)
        {
            Assert.False(Tools().Allows(prefab));
        }

        [Fact]
        public void With_no_tools_known_nothing_may_be_put_up()
        {
            Assert.False(new BuildablePieces(null).Allows("woodwall"));
            Assert.False(new BuildablePieces(new List<IEnumerable<string>> { null, new string[] { null, "" } }).Allows("woodwall"));
            Assert.Equal(0, new BuildablePieces(null).Count);
        }

        [Fact]
        public void A_piece_on_two_tools_counts_once()
        {
            var both = new BuildablePieces(new List<IEnumerable<string>> { new[] { "woodwall" }, new[] { "woodwall", "bed" } });
            Assert.Equal(2, both.Count);
        }
    }
}
