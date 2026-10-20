using System.Text.RegularExpressions;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    // A beech that only needs an axe used to hide a birch that needs a level-2 Builder's Hut: he named the first only,
    // and a better builder never got the chance to take the site.
    public class SiteNeedsTests
    {
        private const string Level2Tree = "$vc_builder_hut_level 2 $vc_for $vc_thing_tree $vc_or_clear_yourself";
        private const string Level3Rock = "$vc_builder_hut_level 3 $vc_for $vc_thing_rock $vc_or_clear_yourself";

        [Fact]
        public void A_missing_tool_and_a_higher_hut_level_are_both_named_tool_first()
        {
            var needs = new SiteNeeds();
            needs.Add(Level2Tree, 2);
            needs.Add("an axe", 0);
            Assert.Equal("an axe, " + Level2Tree, needs.Text);
            Assert.Equal(2, needs.Level);
        }

        [Fact]
        public void The_first_tool_and_the_highest_level_are_named()
        {
            var needs = new SiteNeeds();
            needs.Add(Level2Tree, 2);
            needs.Add("a pickaxe", 0);
            needs.Add(Level3Rock, 3);
            needs.Add("a hoe", 0);
            Assert.Equal("a pickaxe, " + Level3Rock, needs.Text);
            Assert.Equal(3, needs.Level);
        }

        // Review: the game ends a $token only at a space or punctuation like ",": "; " left "[vc_or_clear_yourself;]" on screen.
        [Fact]
        public void Every_token_ends_where_the_game_can_see_its_end()
        {
            var needs = new SiteNeeds();
            needs.Add(Level2Tree, 2);
            needs.Add("$vc_tool_axe", 0);
            foreach (Match token in Regex.Matches(needs.Text, @"\$\w+(.|$)"))
            {
                string after = token.Groups[1].Value;
                Assert.True(after.Length == 0 || " (){}[]+-!?/\\&%,.:=<>\n".Contains(after), $"'{token.Value}' runs into '{after}'");
            }
        }

        [Fact]
        public void Nothing_missing_names_nothing()
        {
            var needs = new SiteNeeds();
            Assert.Null(needs.Text);
            Assert.Equal(0, needs.Level);
        }
    }
}
