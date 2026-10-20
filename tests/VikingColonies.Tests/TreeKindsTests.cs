using System.Collections.Generic;
using System.Linq;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>Worker polish part 3 (0.22): which kind a tree prefab is, for the Trees list.</summary>
    public class TreeKindsTests
    {
        [Theory]
        [InlineData("Beech1", "beech")]
        [InlineData("Beech_small1", "beech")]
        [InlineData("Birch2_aut", "birch")]
        [InlineData("Pinetree_01", "pine")]
        [InlineData("FirTree", "fir")]
        [InlineData("FirTree_small", "fir")]
        [InlineData("Oak1", "oak")]
        [InlineData("SwampTree1", "ancient")]
        [InlineData("YggaShoot_small1", "yggdrasil")]
        [InlineData("FirTreeSmall_snowfall", "fir")]   // review of 0.24 (M4): CamelCase vanilla names
        [InlineData("PineTreeOLD", "pine")]
        [InlineData("AshlandsTree6_big", "ashwood")]
        [InlineData("SnowFirTree", "snowfir")]   // the 1.0 play-through: the list showed "SnowFirTree"
        [InlineData("SnowFirTree 2", "snowfir")]   // the game has this name, with a space
        public void Known_kinds_by_name(string prefab, string kind)
        {
            Assert.Equal(kind, TreeKinds.Of(prefab));
            Assert.Equal("$vc_tree_" + kind, TreeKinds.Token(kind));
        }

        [Fact]   // Review Focus: a tree a mod adds gets its own line under its base name
        public void Unknown_tree_keeps_its_base_name()
        {
            Assert.Equal("Palm", TreeKinds.Of("Palm_small3"));
            Assert.Equal("Palm", TreeKinds.Of("Palm2"));
            Assert.Equal("", TreeKinds.Token("Palm"));
            Assert.Equal("", TreeKinds.Of(null));
        }

        [Fact]   // 0.24 (review of 0.22 M9): a mod's name that only starts like a vanilla stem keeps its own line
        public void Near_names_keep_their_own_line()
        {
            Assert.Equal("FireTree", TreeKinds.Of("FireTree3"));
            Assert.Equal("Pineapple", TreeKinds.Of("Pineapple1"));
            Assert.Equal("Oakmoss", TreeKinds.Of("Oakmoss"));
            Assert.Equal("oak", TreeKinds.Of("Oak_small2"));
            Assert.Equal("oak", TreeKinds.Of("Oak"));
        }

        [Fact]   // 0.24 (review of 0.22 M12): a mod name with spaces can be unticked
        public void Names_are_trimmed() => Assert.Equal("Palm Tree", TreeKinds.Of("Palm Tree 1"));
    }

    /// <summary>The Trees list's lines: Fell and Replant per kind (0.22, MineColonies' Saplings tab).</summary>
    public class TreeChoiceTests
    {
        private const int Meadows = 1, BlackForest = 8;
        private static readonly SaplingInfo BeechSapling = new SaplingInfo("Beech_Sapling", "BeechSeeds", "Beech", 0, Meadows | BlackForest);
        private static readonly SaplingInfo OakSapling = new SaplingInfo("Oak_Sapling", "Acorn", "Oak", 2, Meadows | BlackForest);

        private static readonly Dictionary<string, int> Trees = new Dictionary<string, int>
        {
            ["Beech1"] = 0, ["Beech_small1"] = 0, ["FirTree"] = 0, ["Oak1"] = 2, ["SwampTree1"] = 2,
        };

        private static IEnumerable<string> SaplingsOf(string tree) =>
            tree == "Beech1" ? new[] { "Beech_Sapling" } : tree == "Oak1" ? new[] { "Oak_Sapling" } : new string[0];

        private static List<TreeLine> Lines(int level, ICollection<string> unfelled, ICollection<string>? unreplanted = null)
        {
            List<ReplantLine> replant = ReplantChoice.Lines(new[] { BeechSapling, OakSapling }, level, Meadows, unreplanted ?? new HashSet<string>());
            return TreeChoice.Lines(Trees, replant, SaplingsOf, level, unfelled);
        }

        private static TreeLine Kind(List<TreeLine> lines, string kind) => lines.Single(l => l.Kind == kind);

        [Fact]
        public void One_line_per_kind_with_its_lowest_tier()
        {
            List<TreeLine> lines = Lines(1, new HashSet<string>());
            Assert.Equal(new[] { "beech", "fir", "ancient", "oak" }, lines.Select(l => l.Kind).ToArray());   // by tier, then kind
            Assert.Equal(0, Kind(lines, "beech").Tier);
            Assert.Equal("$vc_tree_beech", Kind(lines, "beech").Token);
        }

        [Fact]
        public void Fell_is_ticked_for_what_his_axe_allows()
        {
            List<TreeLine> lines = Lines(1, new HashSet<string>());
            Assert.True(Kind(lines, "beech").CanFell && Kind(lines, "beech").Fell);
            TreeLine oak = Kind(lines, "oak");
            Assert.False(oak.CanFell);
            Assert.False(oak.Fell);
            Assert.Equal(HutLevels.LevelForTool(ToolKind.Axe, 2), oak.NeedsLevel);
            Assert.True(Kind(Lines(5, new HashSet<string>()), "oak").Fell);
        }

        [Fact]
        public void Unticked_kind_is_not_felled()
        {
            var unfelled = new HashSet<string> { "beech" };
            Assert.False(Kind(Lines(1, unfelled), "beech").Fell);
            Assert.False(TreeChoice.MayFell("Beech_small1", unfelled));
            Assert.True(TreeChoice.MayFell("FirTree", unfelled));
        }

        [Fact]
        public void A_kind_without_a_sapling_offers_no_replanting()
        {
            TreeLine fir = Kind(Lines(1, new HashSet<string>()), "fir");
            Assert.Empty(fir.Saplings);
            Assert.False(fir.ReplantOffered);
            Assert.False(fir.Replant);
        }

        [Fact]   // Review Focus: an old world's replanting choices carry over; every kind is felled as before
        public void Old_replant_choices_carry_over()
        {
            TreeLine beech = Kind(Lines(1, new HashSet<string>(), new HashSet<string> { "Beech_Sapling" }), "beech");
            Assert.Same(BeechSapling, beech.Saplings.Single());
            Assert.True(beech.ReplantOffered);
            Assert.False(beech.Replant);
            Assert.True(beech.Fell);
            Assert.True(Kind(Lines(1, new HashSet<string>()), "beech").Replant);
        }

        [Fact]   // 0.24 (review of 0.22 M10): a greyed Replant tick says why when the sapling's tree is above his axe
        public void Replant_reason_for_a_sapling_above_the_axe()
        {
            var tall = new SaplingInfo("Birch_Sapling", "BirchSeeds", "Birch", 2, Meadows);
            var trees = new Dictionary<string, int> { ["Birch1"] = 0, ["Birch2"] = 2 };
            List<ReplantLine> replant = ReplantChoice.Lines(new[] { tall }, 1, Meadows, new HashSet<string>());
            TreeLine line = TreeChoice.Lines(trees, replant, t => new[] { "Birch_Sapling" }, 1, new HashSet<string>()).Single();
            Assert.True(line.CanFell);
            Assert.False(line.ReplantOffered);
            Assert.Equal(HutLevels.LevelForTool(ToolKind.Axe, 2), line.ReplantNeedsLevel);
        }

        [Fact]   // review of 0.22 (I2): a mod's second sapling for a kind is on that kind's line, so it can be unticked
        public void Two_saplings_of_one_kind_are_both_on_its_line()
        {
            var autumn = new SaplingInfo("BirchAutumn_Sapling", "BirchSeeds", "Autumn birch", 0, Meadows);
            var birch = new SaplingInfo("Birch_Sapling", "BirchSeeds", "Birch", 0, Meadows);
            var trees = new Dictionary<string, int> { ["Birch1"] = 0, ["Birch1_aut"] = 0 };
            IEnumerable<string> Of(string tree) => tree == "Birch1" ? new[] { "Birch_Sapling" } : new[] { "BirchAutumn_Sapling" };
            List<ReplantLine> replant = ReplantChoice.Lines(new[] { birch, autumn }, 1, Meadows, new HashSet<string> { "BirchAutumn_Sapling" });
            TreeLine line = TreeChoice.Lines(trees, replant, Of, 1, new HashSet<string>()).Single();
            Assert.Equal(new[] { "BirchAutumn_Sapling", "Birch_Sapling" }, line.Saplings.Select(x => x.Sapling).OrderBy(x => x, System.StringComparer.Ordinal).ToArray());
            Assert.True(line.Replant);   // ticked while any of its saplings is
            Assert.True(line.ReplantOffered);
        }
    }

    /// <summary>A tree by one of the player's buildings stays standing (0.22, Lumberjack.LeaveTreesNearBuildings).</summary>
    public class TreeRulesTests
    {
        private static readonly List<GroundBox> Wall = new List<GroundBox> { GroundBox.At(new GroundPoint(10f, 10f)) };

        [Fact]
        public void Within_metres_of_a_piece()
        {
            Assert.True(TreeRules.NearBuilding(new GroundPoint(12f, 10f), Wall, 3f));
            Assert.False(TreeRules.NearBuilding(new GroundPoint(14f, 10f), Wall, 3f));
            Assert.False(TreeRules.NearBuilding(new GroundPoint(12f, 10f), new List<GroundBox>(), 3f));
        }

        [Fact]   // Review Focus: 0 m turns it off
        public void Zero_metres_is_off() => Assert.False(TreeRules.NearBuilding(new GroundPoint(10f, 10f), Wall, 0f));

        [Fact]   // 0.26 (review of 0.22): a long wall protects along its whole length, not only round its middle
        public void Near_a_long_wall_by_its_box()
        {
            var wall = new List<GroundBox> { new GroundBox(10f, 10f, 14f, 10.2f) };   // a 4 m wall
            Assert.True(TreeRules.NearBuilding(new GroundPoint(16f, 10f), wall, 3f));    // 2 m past its end (5 m from its middle)
            Assert.True(TreeRules.NearBuilding(new GroundPoint(12f, 12.5f), wall, 3f));  // beside it
            Assert.False(TreeRules.NearBuilding(new GroundPoint(17.5f, 10f), wall, 3f));
            Assert.False(TreeRules.NearBuilding(new GroundPoint(12f, 13.5f), wall, 3f));
            Assert.True(TreeRules.NearBuilding(new GroundPoint(12f, 10.1f), wall, 3f));  // inside its box
            Assert.False(TreeRules.NearBuilding(new GroundPoint(16f, 10f), wall, 0f));
            Assert.True(TreeRules.NearBuilding(new GroundPoint(12f, 10f), new List<GroundBox> { GroundBox.At(new GroundPoint(10f, 10f)) }, 3f));
        }

        [Fact]   // review of 0.22 (I1): a player's structure counts; a field marker (a sign piece), plants and others' pieces don't
        public void Only_a_players_structure_counts_as_a_building()
        {
            Assert.True(TreeRules.CountsAsBuilding(placedByPlayer: true, structure: true, fieldMarker: false));
            Assert.False(TreeRules.CountsAsBuilding(placedByPlayer: true, structure: true, fieldMarker: true));
            Assert.False(TreeRules.CountsAsBuilding(placedByPlayer: true, structure: false, fieldMarker: false));
            Assert.False(TreeRules.CountsAsBuilding(placedByPlayer: false, structure: true, fieldMarker: false));
        }
    }

    /// <summary>Stop at an amount of wood (0.22).</summary>
    public class WoodLimitTests
    {
        [Fact]
        public void Next_cycles()
        {
            Assert.Equal(200, WoodLimit.Next(0));
            Assert.Equal(500, WoodLimit.Next(200));
            Assert.Equal(1000, WoodLimit.Next(500));
            Assert.Equal(0, WoodLimit.Next(1000));
            Assert.Equal(0, WoodLimit.Next(123));
        }

        [Fact]   // Review Focus: at the limit he rests, below it he works; no limit never rests
        public void Rests_at_the_limit_and_works_below()
        {
            Assert.False(WoodLimit.Enough(200, 199));
            Assert.True(WoodLimit.Enough(200, 200));
            Assert.False(WoodLimit.Enough(0, 5000));
        }
    }

    /// <summary>Skills for every worker (0.22): which skill a job uses, and the details line.</summary>
    public class WorkSkillsTests
    {
        [Fact]
        public void Each_job_has_its_skill()
        {
            Assert.Equal(WorkSkill.Woodcutting, WorkSkills.ForJob(SaveKeys.LumberjackJob));
            Assert.Equal(WorkSkill.Farming, WorkSkills.ForJob(SaveKeys.FarmerJob));
            Assert.Equal(WorkSkill.Farming, WorkSkills.ForJob(SaveKeys.ForagerJob));
            Assert.Equal(WorkSkill.Crafting, WorkSkills.ForJob(SaveKeys.BuilderJob));
            Assert.Equal(WorkSkill.None, WorkSkills.ForJob(""));
            Assert.Equal("$vc_skill_farming", WorkSkills.Token(WorkSkill.Farming));
        }

        [Fact]
        public void Line_lists_skills_above_zero()
        {
            var r = new CitizenRecord { Woodcutting = 12.4f, Farming = 3f };
            Assert.Equal("Woodcutting 12 · Farming 3", WorkSkills.Line(r, t => t == "$vc_skill_woodcutting" ? "Woodcutting" : t == "$vc_skill_farming" ? "Farming" : "Crafting"));
            Assert.Equal("", WorkSkills.Line(new CitizenRecord(), t => t));
        }
    }
}
