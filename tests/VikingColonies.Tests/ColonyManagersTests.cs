using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    // Review bug 3: on a shared server any player could fire your workers or order upgrades that use up your iron.
    public class ColonyManagersTests
    {
        private const long Owner = 111L, Friend = 222L, Stranger = 333L;

        [Fact]
        public void The_player_who_built_the_Town_Hall_may_manage_the_colony()
        {
            Assert.True(ColonyManagers.MayManage(Owner, Owner, onOwnersWard: false));
        }

        [Fact]
        public void A_player_the_owner_put_on_a_ward_over_the_hut_may_manage_it()
        {
            Assert.True(ColonyManagers.MayManage(Owner, Friend, onOwnersWard: true));
        }

        [Fact]
        public void Anyone_else_may_not()
        {
            Assert.False(ColonyManagers.MayManage(Owner, Stranger, onOwnersWard: false));
            Assert.False(ColonyManagers.MayManage(Owner, 0L, onOwnersWard: false));   // a requester we can't identify
        }

        [Fact]
        public void A_colony_whose_owner_is_unknown_is_open_to_everyone()
        {
            Assert.True(ColonyManagers.MayManage(0L, Stranger, onOwnersWard: false));
            Assert.True(ColonyManagers.MayManage(0L, 0L, onOwnersWard: false));
        }
    }
}
