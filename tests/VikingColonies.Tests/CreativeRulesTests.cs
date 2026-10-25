using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>Creative mode's rules (1.5): docs/superpowers/specs/2026-10-22-vikingcolonies-creative-mode-design.md.</summary>
    public class CreativeRulesTests
    {
        [Fact]
        public void Creative_follows_the_world_or_the_players_free_building_unless_off()
        {
            Assert.True(CreativeRules.IsCreative(CreativeSetting.Auto, worldNoBuildCost: true, playerNoCost: false));
            Assert.True(CreativeRules.IsCreative(CreativeSetting.Auto, false, true));
            Assert.False(CreativeRules.IsCreative(CreativeSetting.Auto, false, false));
            Assert.False(CreativeRules.IsCreative(CreativeSetting.Off, true, true));
        }

        [Fact]
        public void The_owner_accepts_only_a_manager_in_a_free_world_or_an_admin()
        {
            Assert.True(CreativeRules.OwnerAccepts(CreativeSetting.Auto, true, false, true));
            Assert.True(CreativeRules.OwnerAccepts(CreativeSetting.Auto, false, true, true));
            Assert.False(CreativeRules.OwnerAccepts(CreativeSetting.Auto, false, false, true));   // survival, no admin
            Assert.False(CreativeRules.OwnerAccepts(CreativeSetting.Auto, true, true, false));    // not their colony
            Assert.False(CreativeRules.OwnerAccepts(CreativeSetting.Off, true, true, true));
        }

        [Fact]   // the review round of 2026-10-23: an admin's request to a building another player's computer runs read "isn't on for you"
        public void Why_a_request_is_refused()
        {
            Assert.Equal(CreativeRefusal.AdminUnknown, CreativeRules.Refusal(CreativeSetting.Auto, worldNoBuildCost: false, senderIsAdmin: false,
                adminKnowable: false, senderMayManage: true));
            Assert.Equal(CreativeRefusal.NotCreative, CreativeRules.Refusal(CreativeSetting.Auto, false, false, true, true));
            Assert.Equal(CreativeRefusal.None, CreativeRules.Refusal(CreativeSetting.Auto, true, false, false, true));   // No build cost: any manager
            Assert.Equal(CreativeRefusal.None, CreativeRules.Refusal(CreativeSetting.Auto, false, true, true, true));
            Assert.Equal(CreativeRefusal.NotManager, CreativeRules.Refusal(CreativeSetting.Auto, true, true, true, false));
            Assert.Equal(CreativeRefusal.Off, CreativeRules.Refusal(CreativeSetting.Off, true, true, true, true));
        }

        [Fact]   // the review round: in a No build cost world Supply made free iron and bronze for any manager, again and again
        public void Supply_is_only_where_materials_count()
        {
            Assert.False(CreativeRules.SupplyNeeded(CreativeSetting.Auto, worldNoBuildCost: true));
            Assert.True(CreativeRules.SupplyNeeded(CreativeSetting.Auto, worldNoBuildCost: false));
        }

        [Fact]
        public void Builders_build_free_only_in_a_no_build_cost_world()
        {
            Assert.True(CreativeRules.BuildersFree(CreativeSetting.Auto, true));
            Assert.False(CreativeRules.BuildersFree(CreativeSetting.Auto, false));
            Assert.False(CreativeRules.BuildersFree(CreativeSetting.Off, true));
        }

        [Fact]
        public void A_colony_spared_its_meals_only_while_the_world_has_no_build_cost()
        {
            Assert.False(CreativeRules.SettlersEat(true, CreativeSetting.Auto, true, colonyEatOff: true));
            Assert.True(CreativeRules.SettlersEat(true, CreativeSetting.Auto, false, true));   // the world's key went: they eat again
            Assert.True(CreativeRules.SettlersEat(true, CreativeSetting.Auto, true, false));
            Assert.False(CreativeRules.SettlersEat(false, CreativeSetting.Auto, false, false)); // the server's setting off: nobody eats
            Assert.True(CreativeRules.SettlersEat(true, CreativeSetting.Off, true, true));
        }

        [Fact]
        public void A_placed_hut_goes_up_to_the_halls_level_and_a_town_hall_to_five()
        {
            Assert.Equal(5, CreativeRules.MaxPlacedLevel(isTownHall: true, hallLevel: 1));
            Assert.Equal(3, CreativeRules.MaxPlacedLevel(false, 3));
            Assert.Equal(1, CreativeRules.MaxPlacedLevel(false, 0));
            Assert.Equal(0, CreativeRules.StepPlacedLevel(0, -1, 3));
            Assert.Equal(1, CreativeRules.StepPlacedLevel(0, +1, 3));
            Assert.Equal(3, CreativeRules.StepPlacedLevel(3, +1, 3));
            Assert.Equal(2, CreativeRules.StepPlacedLevel(5, -1, 3));   // a lower limit after the hall went down
        }

        [Fact]
        public void An_admin_is_this_computers_admin_or_host_the_server_itself_or_a_peer_the_server_knows()
        {
            // From this computer: its own player as admin or host (single player too).
            Assert.True(CreativeRules.SenderIsAdmin(fromThisComputer: true, thisIsServer: false, localAdminOrHost: true, fromServer: false, serverSaysAdmin: false));
            Assert.True(CreativeRules.SenderIsAdmin(true, true, false, false, false));
            Assert.False(CreativeRules.SenderIsAdmin(true, false, false, false, false));
            // On a client owner, a request from the server is the host's (the review of 1.5: the host was refused when a friend owned the hut).
            Assert.True(CreativeRules.SenderIsAdmin(false, false, false, fromServer: true, serverSaysAdmin: false));
            // On the server, another computer as the server's admin list says; on a client, another client can't be vouched for.
            Assert.True(CreativeRules.SenderIsAdmin(false, true, false, false, true));
            Assert.False(CreativeRules.SenderIsAdmin(false, true, false, false, false));
            Assert.False(CreativeRules.SenderIsAdmin(false, false, true, false, true));
        }

        [Fact]
        public void Settlers_added_never_pass_the_cap()
        {
            Assert.Equal(5, CreativeRules.SettlersToAdd(5, 3, 20));
            Assert.Equal(2, CreativeRules.SettlersToAdd(5, 18, 20));
            Assert.Equal(0, CreativeRules.SettlersToAdd(1, 25, 20));
        }

        [Fact]
        public void Finish_all_goes_oldest_first_the_camp_last_and_skips_take_downs_and_finished_orders()
        {
            var orders = new[]
            {
                new FinishCandidate { Created = 30, Kind = OrderKind.Build },
                new FinishCandidate { Created = 10, IsCamp = true, Kind = OrderKind.Upgrade },
                new FinishCandidate { Created = 20, Kind = OrderKind.Remove },
                new FinishCandidate { Created = 5, Kind = OrderKind.Upgrade, Done = true },
                new FinishCandidate { Created = 15, Kind = OrderKind.Repair },
                new FinishCandidate { Created = 1, Kind = OrderKind.Build, Paused = true },
            };
            Assert.Equal(new[] { 4, 0, 1 }, CreativeRules.FinishAllSequence(orders));
        }
    }
}
