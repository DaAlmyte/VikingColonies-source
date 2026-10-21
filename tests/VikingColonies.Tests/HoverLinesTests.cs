using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>Issue #1 (1.4.1, MyLittleUI): a colony building's hover drops another mod's "[Shift + E] Rename": Shift+E manages it.</summary>
    public class HoverLinesTests
    {
        [Fact]
        public void A_key_line_offering_the_action_goes_and_the_rest_stays()
        {
            string hover = "Town Hall\n[<color=yellow><b>E</b></color>] Open\n[<color=yellow><b>Shift + E</b></color>] Rename\nLevel 1 of 5";
            Assert.Equal("Town Hall\n[<color=yellow><b>E</b></color>] Open\nLevel 1 of 5", HoverLines.WithoutKeyLine(hover, "Rename"));
        }

        [Fact]
        public void Only_a_Shift_E_line_goes_another_keys_rename_stays()
        {
            // The review of 1.4.2: "[Ctrl+E] Rename" from another mod is a different key, and does what it says.
            string hover = "Chest\n[<b>Shift + E</b>] Rename\n[<b>Ctrl + E</b>] Rename";
            Assert.Equal("Chest\n[<b>Ctrl + E</b>] Rename", HoverLines.WithoutKeyLine(hover, "Rename", "Shift"));
        }

        [Fact]
        public void A_name_with_the_word_in_it_is_no_key_line_and_stays()
        {
            Assert.Equal("Renamed things\n[E] Open", HoverLines.WithoutKeyLine("Renamed things\n[E] Open", "Rename"));
        }

        [Fact]
        public void Nothing_to_drop_leaves_it_as_it_is()
        {
            Assert.Equal("Chest\n[E] Open", HoverLines.WithoutKeyLine("Chest\n[E] Open", "Rename"));
            Assert.Equal("", HoverLines.WithoutKeyLine(null, "Rename"));
            Assert.Equal("Chest", HoverLines.WithoutKeyLine("Chest", ""));
        }
    }
}
