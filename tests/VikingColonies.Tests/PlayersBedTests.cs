using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>
    /// The review of 2026-10-19: an old Longhouse's beds aren't in the new design, and its upgrade took them down, a
    /// player's spawn bed among them, without a word. A bed a player sleeps in stays standing, counted as taken.
    /// </summary>
    public class PlayersBedTests
    {
        [Fact]
        public void An_upgrade_leaves_a_players_bed_standing()
        {
            Assert.True(TakeDownRules.LeaveStanding(isBed: true, bedOwner: 1234567L));
            Assert.False(TakeDownRules.LeaveStanding(isBed: true, bedOwner: 0L));     // a settler's bed: it goes
            Assert.False(TakeDownRules.LeaveStanding(isBed: false, bedOwner: 1234567L));
        }
    }
}
