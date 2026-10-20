using System.Linq;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    public class TreeCatalogTests
    {
        private static TreeCatalog Sample()
        {
            var c = new TreeCatalog();
            c.AddTree("Beech1", 0, "Beech_Stub");
            c.AddStump("Beech_Stub", 0);
            c.AddLog("BeechLog", 0);
            c.AddLog("BeechLogHalf", 0);
            c.AddTree("Oak1", 2, "Oak1_Stub");
            c.AddStump("Oak1_Stub", 2);
            c.AddTree("SwampTree1", 2, "SwampTree1_Stub");   // no sapling exists for it
            c.AddStump("SwampTree1_Stub", 2);
            c.AddTree("Beech_small1", 0, null);              // small tree, leaves no stump
            c.AddSapling("Beech_Sapling", "BeechSeeds", new[] { "Beech1", "Beech_small1" });
            c.AddSapling("Oak_Sapling", "Acorn", new[] { "Oak1" });
            c.AddCollectible("Wood");
            return c;
        }

        [Theory]
        [InlineData("Beech1", ChopKind.Tree)]
        [InlineData("BeechLog", ChopKind.Log)]
        [InlineData("Beech_Stub", ChopKind.Stump)]
        [InlineData("Rock", ChopKind.None)]
        [InlineData(null, ChopKind.None)]
        public void KindOf_identifies_choppable_things(string? prefab, ChopKind expected)
        {
            Assert.Equal(expected, Sample().KindOf(prefab));
        }

        [Fact]
        public void CanChop_respects_tool_tier()
        {
            var c = Sample();
            Assert.True(c.CanChop("Beech1", 1));
            Assert.False(c.CanChop("Oak1", 1));
            Assert.True(c.CanChop("Oak1", 2));
            Assert.False(c.CanChop("Rock", 99));
        }

        [Fact]
        public void Replant_for_a_stump_finds_the_sapling_and_seed()
        {
            Assert.True(Sample().TryGetReplantForStump("Beech_Stub", out ReplantInfo info));
            Assert.Equal("Beech_Sapling", info.Sapling);
            Assert.Equal("BeechSeeds", info.Seed);
        }

        // Review focus 5
        [Fact]
        public void Trees_without_a_sapling_are_not_replanted()
        {
            var c = Sample();
            Assert.False(c.TryGetReplantForStump("SwampTree1_Stub", out _));
            Assert.False(c.TryGetReplantForTree("SwampTree1", out _));
            Assert.False(c.TryGetReplantForStump("NotAStump", out _));
            Assert.False(c.TryGetReplantForStump(null, out _));
        }

        // Review focus 5
        [Fact]
        public void Shared_stump_uses_any_tree_that_has_a_sapling()
        {
            var c = new TreeCatalog();
            c.AddTree("WeirdTree", 0, "Shared_Stub");     // added first, has no sapling
            c.AddTree("Birch1", 0, "Shared_Stub");
            c.AddSapling("Birch_Sapling", "BirchSeeds", new[] { "Birch1" });
            Assert.True(c.TryGetReplantForStump("Shared_Stub", out ReplantInfo info));
            Assert.Equal("Birch_Sapling", info.Sapling);
        }

        [Fact]
        public void HasStump_tells_whether_a_tree_leaves_a_stump()
        {
            var c = Sample();
            Assert.True(c.HasStump("Beech1"));
            Assert.False(c.HasStump("Beech_small1"));
            Assert.False(c.HasStump(null));
        }

        [Fact]
        public void Seeds_from_saplings_are_collectible()
        {
            var c = Sample();
            Assert.True(c.IsCollectible("BeechSeeds"));
            Assert.True(c.IsCollectible("Acorn"));
            Assert.True(c.IsCollectible("Wood"));
            Assert.False(c.IsCollectible("Stone"));
            Assert.False(c.IsCollectible(null));
        }

        [Fact]
        public void A_log_never_overrides_a_tree_with_the_same_name()
        {
            var c = new TreeCatalog();
            c.AddTree("Odd", 0, null);
            c.AddLog("Odd", 3);
            Assert.Equal(ChopKind.Tree, c.KindOf("Odd"));
            Assert.True(c.CanChop("Odd", 0));
        }

        [Fact]
        public void Counts_are_reported_for_logging()
        {
            var c = Sample();
            Assert.Equal(4, c.TreeCount);
            Assert.Equal(2, c.SaplingCount);
            Assert.Equal(3, c.CollectibleCount);
        }

        [Fact]
        public void A_sapling_knows_its_seed_its_biomes_and_the_axe_its_tree_needs()
        {
            var c = new TreeCatalog();
            c.AddTree("Birch1", 2, "BirchStump");
            c.AddTree("Birch2", 2, "BirchStump");
            c.AddTree("Beech1", 0, "Beech_Stub");
            c.AddSapling("Birch_Sapling", "BirchSeeds", new[] { "Birch1", "Birch2" }, biomes: 1 | 8 | 16, name: "$piece_sapling_birch");
            c.AddSapling("Beech_Sapling", "BeechSeeds", new[] { "Beech1" });
            SaplingInfo birch = c.Saplings.Single(s => s.Sapling == "Birch_Sapling");
            Assert.Equal("BirchSeeds", birch.Seed);
            Assert.Equal("$piece_sapling_birch", birch.Name);
            Assert.Equal(2, birch.TreeTier);
            Assert.Equal(1 | 8 | 16, birch.Biomes);
            Assert.Equal(-1, c.Saplings.Single(s => s.Sapling == "Beech_Sapling").Biomes);   // everywhere when not given
            Assert.Equal("Beech_Sapling", c.Saplings.Single(s => s.Sapling == "Beech_Sapling").Name);
        }
    }
}
