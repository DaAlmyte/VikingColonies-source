using System.Collections.Generic;
using System.Linq;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    public class ReplantChoiceTests
    {
        private const int Meadows = 1, Swamp = 2, Mountain = 4, BlackForest = 8, Plains = 16;
        private static readonly SaplingInfo Beech = new SaplingInfo("Beech_Sapling", "BeechSeeds", "Beech", 0, Meadows | BlackForest | Plains);
        private static readonly SaplingInfo Oak = new SaplingInfo("Oak_Sapling", "Acorn", "Oak", 2, Meadows | BlackForest | Plains);
        private static readonly SaplingInfo Fir = new SaplingInfo("FirTree_Sapling", "FirCone", "Fir", 0, Meadows | Mountain | BlackForest | Plains);
        private static readonly SaplingInfo[] All = { Beech, Oak, Fir };

        private static ReplantLine LineOf(List<ReplantLine> lines, SaplingInfo s) => lines.Single(l => l.Sapling == s);

        [Fact]
        public void Every_kind_the_hut_allows_is_offered_and_ticked_by_default()
        {
            List<ReplantLine> lines = ReplantChoice.Lines(All, hutLevel: 2, hutBiome: Meadows, unticked: new HashSet<string>());
            Assert.All(lines, l => Assert.True(l.Offered && l.Ticked));
        }

        [Fact]
        public void A_tree_the_levels_axe_cannot_fell_is_greyed_with_the_level_it_needs()
        {
            ReplantLine oak = LineOf(ReplantChoice.Lines(All, 1, Meadows, new HashSet<string>()), Oak);
            Assert.False(oak.Offered);
            Assert.False(oak.Ticked);
            Assert.Equal(2, oak.NeedsLevel);
        }

        [Fact]
        public void A_kind_that_does_not_grow_where_the_hut_stands_is_greyed()
        {
            List<ReplantLine> lines = ReplantChoice.Lines(All, 5, Swamp, new HashSet<string>());
            Assert.All(lines, l => Assert.True(!l.Offered && l.WrongBiome));
            Assert.True(LineOf(ReplantChoice.Lines(All, 5, Mountain, new HashSet<string>()), Fir).Offered);
        }

        [Fact]
        public void An_unticked_kind_stays_offered_but_unticked()
        {
            ReplantLine beech = LineOf(ReplantChoice.Lines(All, 1, Meadows, new HashSet<string> { "Beech_Sapling" }), Beech);
            Assert.True(beech.Offered);
            Assert.False(beech.Ticked);
        }

        [Fact]
        public void Next_takes_the_ticked_kinds_in_turn()
        {
            int turn = 0;
            var ticked = new[] { Beech, Oak, Fir };
            var planted = Enumerable.Range(0, 4).Select(_ => ReplantChoice.Next(ticked, Meadows, seed => true, ref turn).Sapling).ToList();
            Assert.Equal(new[] { "Beech_Sapling", "Oak_Sapling", "FirTree_Sapling", "Beech_Sapling" }, planted);
        }

        [Fact]
        public void Next_skips_kinds_without_a_seed_and_gives_nothing_when_none_has_one()
        {
            int turn = 0;
            var ticked = new[] { Beech, Oak, Fir };
            Assert.Equal("FirTree_Sapling", ReplantChoice.Next(ticked, Meadows, seed => seed == "FirCone", ref turn).Sapling);
            Assert.Equal("FirTree_Sapling", ReplantChoice.Next(ticked, Meadows, seed => seed == "FirCone", ref turn).Sapling);
            int before = turn;
            Assert.Null(ReplantChoice.Next(ticked, Meadows, seed => false, ref turn));
            Assert.Equal(before, turn);
        }

        [Fact]
        public void Next_plants_nothing_where_no_ticked_kind_grows()
        {
            int turn = 0;
            Assert.Null(ReplantChoice.Next(new[] { Beech, Oak }, Mountain, seed => true, ref turn));
            Assert.Equal("FirTree_Sapling", ReplantChoice.Next(new[] { Beech, Fir }, Mountain, seed => true, ref turn).Sapling);
        }

        [Fact]
        public void The_unticked_kinds_are_saved_as_a_short_text()
        {
            string saved = ReplantChoice.SaveUnticked(new[] { "Oak_Sapling", "Beech_Sapling" });
            Assert.Equal("Beech_Sapling,Oak_Sapling", saved);
            Assert.Equal(new HashSet<string> { "Beech_Sapling", "Oak_Sapling" }, ReplantChoice.ParseUnticked(saved));
            Assert.Empty(ReplantChoice.ParseUnticked(""));
            Assert.Empty(ReplantChoice.ParseUnticked(null));
        }
    }
}
