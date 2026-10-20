using System.Collections.Generic;
using System.Linq;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    public class SiteCheckTests
    {
        [Fact]
        public void Tools_already_in_the_colonys_chests_are_not_asked_for()
        {
            // Seen: the outline asked for "an axe, a pickaxe and a hoe in the colony's chests" with an axe and a hoe in them.
            var findings = new SiteFindings { Dig = true };
            findings.Add(ClearKind.Tree, 0);
            findings.Add(ClearKind.Rock, 0);
            var atHand = new[] { new ToolInfo("AxeFlint", "$item_axe_flint", ToolKind.Axe, 1), new ToolInfo("Hoe", "$item_hoe", ToolKind.Hoe, 0) };
            Assert.Equal(new[] { ToolKind.Pickaxe }, SiteCheck.Judge(findings, 1, atHand).Tools);
        }

        [Fact]
        public void A_tool_too_weak_for_the_work_is_still_asked_for()
        {
            var findings = new SiteFindings();
            findings.Add(ClearKind.Tree, 1);
            var atHand = new[] { new ToolInfo("AxeStone", "$item_axe_stone", ToolKind.Axe, 0) };
            Assert.Equal(new[] { ToolKind.Axe }, SiteCheck.Judge(findings, 1, atHand).Tools);
        }

        [Fact]
        public void Ground_to_level_with_a_hoe_asks_for_a_hoe_only_when_the_chests_have_none()
        {
            var findings = new SiteFindings { Dig = true };
            Assert.Equal(new[] { ToolKind.Hoe }, SiteCheck.Judge(findings, 1, new ToolInfo[0]).Tools);
            Assert.Empty(SiteCheck.Judge(findings, 1, new[] { new ToolInfo("Hoe", "$item_hoe", ToolKind.Hoe, 0) }).Tools);
        }

        [Fact]
        public void A_tool_above_what_the_builders_level_lets_him_use_is_still_asked_for()
        {
            // A level-1 Builder's Hut uses axes up to Flint: a bronze axe in a chest doesn't count.
            var findings = new SiteFindings();
            findings.Add(ClearKind.Tree, 1);
            var atHand = new[] { new ToolInfo("AxeBronze", "$item_axe_bronze", ToolKind.Axe, 2) };
            Assert.Equal(new[] { ToolKind.Axe }, SiteCheck.Judge(findings, 1, atHand).Tools);
        }

        [Fact]
        public void Without_knowing_the_chests_every_tool_needed_is_asked_for_as_before()
        {
            var findings = new SiteFindings { Dig = true };
            findings.Add(ClearKind.Tree, 0);
            findings.Add(ClearKind.Rock, 0);
            Assert.Equal(new[] { ToolKind.Axe, ToolKind.Pickaxe, ToolKind.Hoe }, SiteCheck.Judge(findings, 1).Tools.OrderBy(t => t).ToArray());
        }

        [Fact]
        public void Work_only_above_the_builders_level_asks_for_the_level_not_a_tool()
        {
            var findings = new SiteFindings();
            findings.Add(ClearKind.Tree, 4);
            var atHand = new[] { new ToolInfo("AxeFlint", "$item_axe_flint", ToolKind.Axe, 1) };
            SiteVerdict verdict = SiteCheck.Judge(findings, 1, atHand);
            Assert.Empty(verdict.Tools);
            Assert.True(verdict.NeedsLevel > 1);
        }

        [Fact]
        public void An_empty_flat_site_is_green()
        {
            SiteVerdict verdict = SiteCheck.Judge(new SiteFindings());
            Assert.Equal(SiteColour.Green, verdict.Colour);
            Assert.Equal(RedReason.None, verdict.Red);
            Assert.Empty(verdict.Tools);
            Assert.Empty(verdict.Clear);
        }

        [Fact]
        public void Trees_make_it_yellow_and_need_an_axe_good_enough_for_the_hardest()
        {
            var findings = new SiteFindings();
            findings.Add(ClearKind.Tree, 0);
            findings.Add(ClearKind.Stump, 1);
            findings.Add(ClearKind.Tree, 2);
            SiteVerdict verdict = SiteCheck.Judge(findings);
            Assert.Equal(SiteColour.Yellow, verdict.Colour);
            Assert.Equal(new[] { ToolKind.Axe }, verdict.Tools);
            Assert.Equal(2, verdict.AxeTier);
            Assert.Equal(new[] { new KeyValuePair<ClearKind, int>(ClearKind.Tree, 2), new KeyValuePair<ClearKind, int>(ClearKind.Stump, 1) }, verdict.Clear);
        }

        [Fact]
        public void Rocks_need_a_pickaxe_and_flattening_needs_a_hoe()
        {
            var findings = new SiteFindings { Dig = true };
            findings.Add(ClearKind.Rock, 1);
            SiteVerdict verdict = SiteCheck.Judge(findings);
            Assert.Equal(new[] { ToolKind.Pickaxe, ToolKind.Hoe }, verdict.Tools);
            Assert.Equal(1, verdict.PickaxeTier);
            Assert.True(verdict.Flatten);
        }

        [Fact]
        public void Shallow_digging_needs_only_a_hoe()
        {
            SiteVerdict verdict = SiteCheck.Judge(new SiteFindings { Dig = true, Stone = 5 });
            Assert.Equal(SiteColour.Yellow, verdict.Colour);
            Assert.Equal(new[] { ToolKind.Hoe }, verdict.Tools);
            Assert.Equal(-1, verdict.PickaxeTier);
            Assert.Equal(0, verdict.Stone);
        }

        [Fact]
        public void Deep_digging_needs_a_pickaxe_and_a_hoe()
        {
            SiteVerdict verdict = SiteCheck.Judge(new SiteFindings { Dig = true, DeepDig = true });
            Assert.Equal(new[] { ToolKind.Pickaxe, ToolKind.Hoe }, verdict.Tools);
            Assert.Equal(0, verdict.PickaxeTier);
        }

        [Fact]
        public void Raising_ground_needs_a_hoe_and_stone()
        {
            SiteVerdict verdict = SiteCheck.Judge(new SiteFindings { Raise = true, Stone = 12 });
            Assert.Equal(new[] { ToolKind.Hoe }, verdict.Tools);
            Assert.Equal(12, verdict.Stone);
            Assert.Equal(-1, verdict.PickaxeTier);
        }

        [Fact]
        public void Items_plants_and_old_pieces_need_no_tools()
        {
            var findings = new SiteFindings();
            findings.Add(ClearKind.Item);
            findings.Add(ClearKind.Plant);
            findings.Add(ClearKind.Leftover);
            SiteVerdict verdict = SiteCheck.Judge(findings);
            Assert.Equal(SiteColour.Yellow, verdict.Colour);
            Assert.Empty(verdict.Tools);
        }

        [Fact]
        public void Red_beats_yellow_and_the_first_reason_is_shown()
        {
            var findings = new SiteFindings();
            findings.Add(ClearKind.Tree);
            findings.AddRed(RedReason.OtherBuilding);
            findings.AddRed(RedReason.Water);
            findings.AddRed(RedReason.None);
            SiteVerdict verdict = SiteCheck.Judge(findings);
            Assert.Equal(SiteColour.Red, verdict.Colour);
            Assert.Equal(RedReason.Water, verdict.Red);
            Assert.Equal(2, findings.Red.Count);
        }

        // Review: a player who may not manage a colony could place a hut in it and make its builder spend its materials.
        [Fact]
        public void Someone_elses_colony_is_red_and_says_so_before_the_ground_does()
        {
            var findings = new SiteFindings();
            findings.AddRed(RedReason.TooSteep);
            findings.AddRed(RedReason.NotYourColony);
            SiteVerdict verdict = SiteCheck.Judge(findings);
            Assert.Equal(SiteColour.Red, verdict.Colour);
            Assert.Equal(RedReason.NotYourColony, verdict.Red);
        }

        [Fact]
        public void A_birch_above_a_level_1_builder_is_named_with_the_level_it_needs()
        {
            var findings = new SiteFindings();
            findings.Add(ClearKind.Tree, 0);   // a beech: a flint axe fells it
            findings.Add(ClearKind.Tree, 2);   // a birch: a bronze axe, so a level-2 Builder's Hut
            SiteVerdict verdict = SiteCheck.Judge(findings, 1);
            Assert.Equal(SiteColour.Yellow, verdict.Colour);   // it can still be placed
            Assert.Equal(new[] { new KeyValuePair<ClearKind, int>(ClearKind.Tree, 1) }, verdict.AboveLevel);
            Assert.Equal(2, verdict.NeedsLevel);
        }

        [Fact]
        public void A_level_2_builder_clears_the_birch_himself()
        {
            var findings = new SiteFindings();
            findings.Add(ClearKind.Tree, 2);
            SiteVerdict verdict = SiteCheck.Judge(findings, 2);
            Assert.Empty(verdict.AboveLevel);
            Assert.Equal(0, verdict.NeedsLevel);
        }

        [Fact]
        public void Without_a_builders_hut_the_first_ones_level_counts()
        {
            var findings = new SiteFindings();
            findings.Add(ClearKind.Tree, 2);
            SiteVerdict verdict = SiteCheck.Judge(findings, 0);
            Assert.Equal(new[] { new KeyValuePair<ClearKind, int>(ClearKind.Tree, 1) }, verdict.AboveLevel);
            Assert.Equal(2, verdict.NeedsLevel);
        }

        [Fact]
        public void Each_thing_counts_by_its_own_tool_and_the_hardest_sets_the_level()
        {
            var findings = new SiteFindings();
            findings.Add(ClearKind.Rock, 3);    // an iron pickaxe's rock: level 3
            findings.Add(ClearKind.Stump, 2);   // a birch stump: level 2
            findings.Add(ClearKind.Log, 1);     // within a flint axe
            findings.Add(ClearKind.Rock, 0);
            SiteVerdict verdict = SiteCheck.Judge(findings, 1);
            Assert.Equal(new[]
            {
                new KeyValuePair<ClearKind, int>(ClearKind.Stump, 1),
                new KeyValuePair<ClearKind, int>(ClearKind.Rock, 1),
            }, verdict.AboveLevel);
            Assert.Equal(3, verdict.NeedsLevel);
        }

        [Fact]
        public void Something_no_level_reaches_needs_level_0()
        {
            var findings = new SiteFindings();
            findings.Add(ClearKind.Tree, 2);
            findings.Add(ClearKind.Rock, 4);   // no vanilla pickaxe is that good
            SiteVerdict verdict = SiteCheck.Judge(findings, 1);
            Assert.Equal(2, verdict.AboveLevel.Count);
            Assert.Equal(0, verdict.NeedsLevel);
        }

        [Fact]
        public void Items_plants_and_old_pieces_are_never_above_a_level()
        {
            var findings = new SiteFindings();
            findings.Add(ClearKind.Item);
            findings.Add(ClearKind.Plant);
            findings.Add(ClearKind.Leftover);
            Assert.Empty(SiteCheck.Judge(findings, 1).AboveLevel);
            Assert.Empty(findings.Tiers);
        }

        [Fact]
        public void Judged_for_the_best_builder_there_is_a_birch_is_not_above_him()
        {
            var findings = new SiteFindings();
            findings.Add(ClearKind.Tree, 2);
            Assert.Empty(SiteCheck.Judge(findings).AboveLevel);
        }

        [Fact]
        public void The_tool_for_each_kind()
        {
            Assert.Equal(ToolKind.Axe, SiteCheck.ToolFor(ClearKind.Tree));
            Assert.Equal(ToolKind.Axe, SiteCheck.ToolFor(ClearKind.Log));
            Assert.Equal(ToolKind.Axe, SiteCheck.ToolFor(ClearKind.Stump));
            Assert.Equal(ToolKind.Pickaxe, SiteCheck.ToolFor(ClearKind.Rock));
            Assert.Null(SiteCheck.ToolFor(ClearKind.Plant));
            Assert.Null(SiteCheck.ToolFor(ClearKind.Leftover));
        }
    }
}
