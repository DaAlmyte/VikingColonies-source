using System.Linq;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    public class HutLevelsTests
    {
        [Fact]
        public void A_builder_builds_huts_up_to_his_own_huts_level()
        {
            Assert.True(HutLevels.MayBuild(builderHutLevel: 1, targetLevel: 1, ownHut: false));
            Assert.False(HutLevels.MayBuild(builderHutLevel: 1, targetLevel: 2, ownHut: false));
            Assert.True(HutLevels.MayBuild(builderHutLevel: 3, targetLevel: 3, ownHut: false));
        }

        [Fact]
        public void A_builder_may_raise_his_own_hut_one_level_above_itself()
        {
            Assert.True(HutLevels.MayBuild(builderHutLevel: 1, targetLevel: 2, ownHut: true));
            Assert.False(HutLevels.MayBuild(builderHutLevel: 1, targetLevel: 3, ownHut: true));
        }

        [Fact]
        public void A_Lumberjacks_Hut_waits_for_a_high_enough_Builders_Hut()
        {
            Assert.Equal(UpgradeLock.BuilderTooLow, HutLevels.CanUpgrade(level: 1, finished: true, upgrading: false, highestBuilderHutLevel: 1, isBuilderHut: false));
            Assert.Equal(UpgradeLock.None, HutLevels.CanUpgrade(1, true, false, 2, false));
            Assert.Equal(UpgradeLock.BuilderTooLow, HutLevels.CanUpgrade(2, true, false, 2, false));
        }

        [Fact]
        public void The_Builders_Hut_itself_can_always_go_up_one_level()
        {
            Assert.Equal(UpgradeLock.None, HutLevels.CanUpgrade(1, true, false, 1, true));
            Assert.Equal(UpgradeLock.None, HutLevels.CanUpgrade(3, true, false, 3, true));
        }

        [Fact]
        public void A_hut_at_the_top_unfinished_or_being_upgraded_is_refused()
        {
            Assert.Equal(UpgradeLock.MaxLevel, HutLevels.CanUpgrade(5, true, false, 5, false));
            Assert.Equal(UpgradeLock.NotFinished, HutLevels.CanUpgrade(1, false, false, 5, false));
            Assert.Equal(UpgradeLock.Upgrading, HutLevels.CanUpgrade(1, false, true, 5, false));
        }

        [Fact]
        public void Without_a_Builders_Hut_no_hut_can_be_upgraded()
        {
            Assert.Equal(UpgradeLock.NoBuilderHut, HutLevels.CanUpgrade(1, true, false, 0, false));
        }

        [Fact]
        public void Each_level_allows_the_best_axe_and_pickaxe_of_its_band()
        {
            Assert.Equal(new[] { 1, 3, 4, 4, 6 }, Enumerable.Range(1, 5).Select(l => HutLevels.ToolTierCap(ToolKind.Axe, l)));
            Assert.Equal(new[] { 0, 2, 3, 3, 3 }, Enumerable.Range(1, 5).Select(l => HutLevels.ToolTierCap(ToolKind.Pickaxe, l)));
            Assert.Equal(int.MaxValue, HutLevels.ToolTierCap(ToolKind.Hoe, 1));
        }

        [Fact]
        public void The_level_a_tool_tier_needs()
        {
            Assert.Equal(1, HutLevels.LevelForTool(ToolKind.Axe, 0));
            Assert.Equal(2, HutLevels.LevelForTool(ToolKind.Axe, 2));   // an oak needs a bronze axe: a level-2 hut
            Assert.Equal(5, HutLevels.LevelForTool(ToolKind.Axe, 5));
            Assert.Equal(0, HutLevels.LevelForTool(ToolKind.Axe, 7));   // no level reaches it
            Assert.Equal(2, HutLevels.LevelForTool(ToolKind.Pickaxe, 1));
        }

        [Fact]
        public void The_work_area_and_the_chest_grow_with_the_level()
        {
            Assert.Equal(40f, HutLevels.WorkRadius(40f, 10f, 1));
            Assert.Equal(80f, HutLevels.WorkRadius(40f, 10f, 5));
            Assert.Equal(3, HutLevels.ChestRows(3, 1, 1));
            Assert.Equal(7, HutLevels.ChestRows(3, 1, 5));
            Assert.Equal(3, HutLevels.ChestRows(3, -2, 4));   // a chest never shrinks
        }
    }
}
