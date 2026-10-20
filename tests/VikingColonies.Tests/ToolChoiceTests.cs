using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    public class ToolChoiceTests
    {
        private static readonly ToolInfo[] Tools =
        {
            new ToolInfo("AxeStone", "$item_axe_stone", ToolKind.Axe, 0),
            new ToolInfo("AxeFlint", "$item_axe_flint", ToolKind.Axe, 1),
            new ToolInfo("AxeBronze", "$item_axe_bronze", ToolKind.Axe, 2),
            new ToolInfo("PickaxeAntler", "$item_pickaxe_antler", ToolKind.Pickaxe, 1),
            new ToolInfo("Hoe", "$item_hoe", ToolKind.Hoe, 0),
        };

        [Fact]
        public void The_builder_takes_the_best_tool_that_is_good_enough()
        {
            Assert.Equal("AxeBronze", ToolChoice.Best(Tools, ToolKind.Axe, 1).Prefab);
            Assert.Equal("AxeBronze", ToolChoice.Best(Tools, ToolKind.Axe, 0).Prefab);
        }

        [Fact]
        public void No_tool_is_chosen_when_none_is_good_enough_or_none_of_the_kind_is_there()
        {
            Assert.Null(ToolChoice.Best(Tools, ToolKind.Pickaxe, 2));
            Assert.Null(ToolChoice.Best(new ToolInfo[0], ToolKind.Hoe, 0));
        }

        [Fact]
        public void Kinds_dont_mix()
        {
            Assert.Equal("PickaxeAntler", ToolChoice.Best(Tools, ToolKind.Pickaxe, 0).Prefab);
            Assert.Equal("Hoe", ToolChoice.Best(Tools, ToolKind.Hoe, 0).Prefab);
        }

        [Fact]
        public void Messages_name_the_weakest_tool_that_would_do()
        {
            Assert.Equal("$item_axe_flint", ToolChoice.Weakest(Tools, ToolKind.Axe, 1).Name);
            Assert.Null(ToolChoice.Weakest(Tools, ToolKind.Axe, 3));
        }

        [Fact]
        public void Equal_tools_are_chosen_the_same_way_every_time()
        {
            var twins = new[] { new ToolInfo("HoeB", "$b", ToolKind.Hoe, 0), new ToolInfo("HoeA", "$a", ToolKind.Hoe, 0) };
            Assert.Equal("HoeA", ToolChoice.Best(twins, ToolKind.Hoe, 0).Prefab);
            Assert.Equal("HoeA", ToolChoice.Weakest(twins, ToolKind.Hoe, 0).Prefab);
        }

        [Fact]
        public void The_best_tool_within_a_cap()
        {
            var tools = new[]
            {
                new ToolInfo("AxeFlint", "Flint axe", ToolKind.Axe, 1),
                new ToolInfo("AxeIron", "Iron axe", ToolKind.Axe, 3),
                new ToolInfo("AxeBlackMetal", "Black metal axe", ToolKind.Axe, 4),
            };
            Assert.Equal("AxeIron", ToolChoice.Best(tools, ToolKind.Axe, 0, 3).Prefab);
            Assert.Equal("AxeFlint", ToolChoice.Best(tools, ToolKind.Axe, 0, 1).Prefab);
            Assert.Null(ToolChoice.Best(tools, ToolKind.Axe, 2, 1));
        }

        private static readonly ToolInfo[] Chests =
        {
            new ToolInfo("AxeFlint", "Flint axe", ToolKind.Axe, 1),
            new ToolInfo("AxeBronze", "Bronze axe", ToolKind.Axe, 2),
            new ToolInfo("AxeIron", "Iron axe", ToolKind.Axe, 3),
            new ToolInfo("AxeBlackMetal", "Black metal axe", ToolKind.Axe, 4),
            new ToolInfo("Hoe", "Hoe", ToolKind.Hoe, 0),
        };

        [Fact]
        public void A_level_1_builder_needs_a_level_2_hut_for_a_birch_even_with_a_bronze_axe_in_the_chests()
        {
            Assert.Equal(ToolNeed.HutLevel, ToolChoice.ForLevel(Chests, ToolKind.Axe, 2, 1, out ToolInfo tool, out int level));
            Assert.Null(tool);
            Assert.Equal(2, level);
        }

        [Fact]
        public void Work_within_his_level_takes_the_best_tool_his_level_allows()
        {
            Assert.Equal(ToolNeed.Ready, ToolChoice.ForLevel(Chests, ToolKind.Axe, 2, 2, out ToolInfo tool, out int level));
            Assert.Equal("AxeIron", tool.Prefab);
            Assert.Equal(0, level);
        }

        [Fact]
        public void Tools_only_above_his_level_in_the_chests_means_he_asks_for_a_tool()
        {
            var bronzeOnly = new[] { new ToolInfo("AxeBronze", "Bronze axe", ToolKind.Axe, 2) };
            Assert.Equal(ToolNeed.Tool, ToolChoice.ForLevel(bronzeOnly, ToolKind.Axe, 0, 1, out ToolInfo tool, out int level));
            Assert.Null(tool);
            Assert.Equal(0, level);
        }

        [Fact]
        public void A_tier_no_hut_level_reaches_needs_level_0()
        {
            Assert.Equal(ToolNeed.HutLevel, ToolChoice.ForLevel(Chests, ToolKind.Pickaxe, 4, 5, out ToolInfo tool, out int level));
            Assert.Null(tool);
            Assert.Equal(0, level);
        }

        [Fact]
        public void Hoes_are_not_capped_by_level()
        {
            Assert.Equal(ToolNeed.Ready, ToolChoice.ForLevel(Chests, ToolKind.Hoe, 0, 1, out ToolInfo tool, out _));
            Assert.Equal("Hoe", tool.Prefab);
        }
    }
}
