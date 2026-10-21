using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>
    /// 1.4.2, a player's report: debug mode's K ran killenemies and killed every settler. The game's killall, killenemies
    /// and killtame hit every creature with 10,000,000,000 damage and no attacker; settlers take no such blow.
    /// </summary>
    public class CheatKillTests
    {
        [Fact]
        public void The_consoles_kill_blow_is_one_a_settler_doesnt_take()
        {
            Assert.True(SettlerSafety.CheatKill(genericDamage: 1E+10f, hasAttacker: false));
            Assert.True(SettlerSafety.CheatKill(genericDamage: 1E+9f, hasAttacker: false));
        }

        [Fact]
        public void A_real_blow_or_one_from_someone_still_lands()
        {
            Assert.False(SettlerSafety.CheatKill(genericDamage: 1E+10f, hasAttacker: true));    // a monster, a player, our vc_kill
            Assert.False(SettlerSafety.CheatKill(genericDamage: 99999f, hasAttacker: false));  // the edge of the world, a fall
            Assert.False(SettlerSafety.CheatKill(genericDamage: 0f, hasAttacker: false));
        }
    }
}
