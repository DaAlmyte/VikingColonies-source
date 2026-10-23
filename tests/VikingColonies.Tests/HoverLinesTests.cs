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

        // 1.4.2's review (deferred minors, done in 1.4.3): our Shift+E skipped the game's own "not while attacking or
        // dodging", and took the key for another mod's object under a hut, which the game would have used first.
        [Fact]
        public void Shift_E_opens_a_colony_window_only_on_the_hut_itself_and_not_mid_attack_or_dodge()
        {
            Assert.True(ShiftE.OpensWindow(alt: true, hold: false, localPlayer: true, onBuilding: true, nearestIsBuilding: true, attacking: false, dodging: false));
            Assert.False(ShiftE.OpensWindow(alt: true, hold: false, localPlayer: true, onBuilding: true, nearestIsBuilding: false, attacking: false, dodging: false));
            Assert.False(ShiftE.OpensWindow(alt: true, hold: false, localPlayer: true, onBuilding: true, nearestIsBuilding: true, attacking: true, dodging: false));
            Assert.False(ShiftE.OpensWindow(alt: true, hold: false, localPlayer: true, onBuilding: true, nearestIsBuilding: true, attacking: false, dodging: true));
            Assert.False(ShiftE.OpensWindow(alt: false, hold: false, localPlayer: true, onBuilding: true, nearestIsBuilding: true, attacking: false, dodging: false));
            Assert.False(ShiftE.OpensWindow(alt: true, hold: true, localPlayer: true, onBuilding: true, nearestIsBuilding: true, attacking: false, dodging: false));
            Assert.False(ShiftE.OpensWindow(alt: true, hold: false, localPlayer: false, onBuilding: true, nearestIsBuilding: true, attacking: false, dodging: false));
            Assert.False(ShiftE.OpensWindow(alt: true, hold: false, localPlayer: true, onBuilding: false, nearestIsBuilding: false, attacking: false, dodging: false));
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

        // 1.4.3, the player's watch of 1.4.2: Hire with no jobless settler put "No jobless settlers. A newcomer comes when
        // ..." across the middle of the screen, too long to read. A reply goes to the open window's message line, else to
        // the corner where the game lists messages, never the middle.
        [Fact]
        public void A_reply_goes_to_the_open_window_else_the_corner()
        {
            Assert.Equal(ReplyPlace.HutWindow, Replies.Place(hutWindowOpen: true, hallWindowOpen: false));
            Assert.Equal(ReplyPlace.HallWindow, Replies.Place(hutWindowOpen: false, hallWindowOpen: true));
            Assert.Equal(ReplyPlace.Corner, Replies.Place(hutWindowOpen: false, hallWindowOpen: false));
        }
    }
}
