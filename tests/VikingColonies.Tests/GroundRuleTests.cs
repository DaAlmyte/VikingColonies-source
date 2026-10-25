using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    public class GroundRuleTests
    {
        // ---- A design with posts under its floor (it reaches 1.25 m below the floor) ----

        [Fact]
        public void With_posts_the_ground_goes_30_cm_under_the_floor()
        {
            Assert.Equal(9.7f, GroundRule.Target(floor: 10f, reachBelow: 1.25f, natural: 10f), 3);
            Assert.Equal(RedReason.None, GroundRule.Problem(10f, 1.25f, 10f));
        }

        [Fact]
        public void A_raised_design_with_posts_stands_on_the_natural_ground()
        {
            Assert.Equal(10f, GroundRule.Target(floor: 11f, reachBelow: 1.25f, natural: 10f), 3);
            Assert.Equal(RedReason.None, GroundRule.Problem(11f, 1.25f, 10f));
        }

        [Fact]
        public void Raised_exactly_as_far_as_the_posts_reach_is_fine_but_higher_is_red()
        {
            Assert.Equal(RedReason.None, GroundRule.Problem(floor: 11.25f, reachBelow: 1.25f, natural: 10f));
            Assert.Equal(RedReason.TooHigh, GroundRule.Problem(floor: 11.5f, reachBelow: 1.25f, natural: 10f));
        }

        [Fact]
        public void Posts_that_only_just_reach_the_ground_get_their_feet_in_it()
        {
            // Raised so far that the posts' feet would only touch the natural ground: it is raised 15 cm under them.
            Assert.Equal(10.15f, GroundRule.Target(floor: 11.25f, reachBelow: 1.25f, natural: 10f), 3);
            Assert.Equal(10.1f, GroundRule.Target(floor: 11.2f, reachBelow: 1.25f, natural: 10f), 3);   // their feet: 9.95, now 15 cm in
        }

        [Fact]
        public void A_lowered_design_with_posts_digs_down()
        {
            Assert.Equal(8.7f, GroundRule.Target(floor: 9f, reachBelow: 1.25f, natural: 10f), 3);
            Assert.Equal(RedReason.None, GroundRule.Problem(9f, 1.25f, 10f));
        }

        // ---- An upgrade keeps the ground the hut already has, when the new design can stand on it ----

        [Fact]
        public void An_upgrade_to_a_design_with_posts_keeps_the_huts_ground()
        {
            // The level-2 hut's floors rest 13 cm under its chest; the level-3 stone floors reach 1 m down.
            Assert.Equal(31.24f, GroundRule.ForUpgrade(floor: 31.37f, reachBelow: 1f, current: 31.24f, natural: 31.24f), 3);
        }

        [Fact]
        public void An_upgrade_raises_the_ground_when_the_new_posts_would_not_reach_it()
        {
            Assert.Equal(10.15f, GroundRule.ForUpgrade(floor: 11.25f, reachBelow: 1.25f, current: 9.5f, natural: 9.5f), 3);
        }

        [Fact]
        public void An_upgrade_to_thicker_floors_keeps_the_ground_rather_than_digging_under_the_old_hut()
        {
            Assert.Equal(9.87f, GroundRule.ForUpgrade(floor: 10f, reachBelow: 0.5f, current: 9.87f, natural: 9.87f), 3);
        }

        [Fact]
        public void An_upgrade_raises_the_ground_when_the_new_floors_would_float()
        {
            Assert.Equal(9.87f, GroundRule.ForUpgrade(floor: 10f, reachBelow: 0.13f, current: 9.5f, natural: 9.5f), 3);
        }

        // ---- The site check of an upgrade judges the ground as the upgrade levels it ----

        [Fact]
        public void An_upgrade_from_a_design_with_posts_to_one_without_raises_the_ground_rather_than_being_refused()
        {
            // Seen: a level-1 hut of the player's own design with posts had its ground 30 cm under the floor; the built-in
            // level 2 (floors only, 13 cm deep) was refused as "can't stand in the air", though the upgrade raises the ground.
            RedReason problem = GroundRule.Judge(floor: 37.58f, reachBelow: 0.13f, natural: 37.28f, upgrade: true, out float target);
            Assert.Equal(RedReason.None, problem);
            Assert.Equal(37.45f, target, 3);
        }

        [Fact]
        public void An_upgrade_that_would_raise_the_ground_too_far_is_refused()
        {
            // An old design on tall stilts, a new one with floors only: the ground would go up nearly 5 m.
            Assert.Equal(RedReason.TooHigh, GroundRule.Judge(floor: 15f, reachBelow: 0.13f, natural: 10f, upgrade: true, out _));
            Assert.Equal(RedReason.None, GroundRule.Judge(floor: 10f + GroundRule.MaxUpgradeRaise, reachBelow: 0f, natural: 10f, upgrade: true, out _));
        }

        [Fact]
        public void An_upgrades_site_check_keeps_the_ground_the_upgrade_keeps()
        {
            GroundRule.Judge(floor: 31.37f, reachBelow: 1f, natural: 31.24f, upgrade: true, out float target);
            Assert.Equal(31.24f, target, 3);
        }

        [Fact]
        public void A_new_hut_is_judged_as_before()
        {
            Assert.Equal(RedReason.CantFloat, GroundRule.Judge(floor: 10.5f, reachBelow: 0.25f, natural: 10f, upgrade: false, out float floating));
            Assert.Equal(10.25f, floating, 3);
            Assert.Equal(RedReason.TooHigh, GroundRule.Judge(floor: 11.5f, reachBelow: 1.25f, natural: 10f, upgrade: false, out _));
        }

        [Fact]
        public void An_upgrade_never_keeps_ground_above_the_floor()
        {
            Assert.Equal(9.75f, GroundRule.ForUpgrade(floor: 10f, reachBelow: 0.25f, current: 10.2f, natural: 10.2f), 3);
        }

        // ---- A design whose lowest pieces are its floors (it reaches 0.25 m below the floor) ----

        [Fact]
        public void Without_posts_the_floors_rest_on_the_ground()
        {
            Assert.Equal(9.75f, GroundRule.Target(floor: 10f, reachBelow: 0.25f, natural: 10f), 3);
            Assert.Equal(RedReason.None, GroundRule.Problem(10f, 0.25f, 10f));
        }

        [Fact]
        public void A_design_without_posts_can_be_lowered()
        {
            Assert.Equal(8.75f, GroundRule.Target(floor: 9f, reachBelow: 0.25f, natural: 10f), 3);
            Assert.Equal(RedReason.None, GroundRule.Problem(9f, 0.25f, 10f));
        }

        [Fact]
        public void A_design_without_posts_cant_stand_in_the_air()
        {
            Assert.Equal(RedReason.None, GroundRule.Problem(floor: 10.25f, reachBelow: 0.25f, natural: 10f));   // within 10 cm
            Assert.Equal(RedReason.CantFloat, GroundRule.Problem(floor: 10.5f, reachBelow: 0.25f, natural: 10f));
        }

        [Fact]
        public void A_design_that_reaches_nowhere_below_its_floor_still_has_a_target()
        {
            Assert.Equal(10f, GroundRule.Target(floor: 10f, reachBelow: -1f, natural: 10f), 3);   // never above the floor
        }

        [Fact]
        public void Ground_varying_more_than_4_m_is_too_steep()
        {
            Assert.False(GroundRule.TooSteep(lowest: 10f, highest: 14f));
            Assert.True(GroundRule.TooSteep(lowest: 10f, highest: 14.1f));
        }

        // ---- Raising and lowering while placing ----

        [Fact]
        public void Raising_goes_in_25_cm_steps_up_to_2_m()
        {
            var offset = new PlacementOffset();
            offset.Raise();
            Assert.Equal(0.25f, offset.Value, 3);
            for (int i = 0; i < 20; i++) offset.Raise();
            Assert.Equal(2f, offset.Value, 3);
        }

        [Fact]
        public void Lowering_stops_at_2_m_down()
        {
            var offset = new PlacementOffset();
            for (int i = 0; i < 20; i++) offset.Lower();
            Assert.Equal(-2f, offset.Value, 3);
            offset.Raise();
            Assert.Equal(-1.75f, offset.Value, 3);
        }

        [Fact]
        public void Choosing_another_piece_resets_the_offset_but_the_same_piece_keeps_it()
        {
            var offset = new PlacementOffset();
            offset.Select("vc_hut_lumberjack");
            offset.Raise();
            offset.Select("vc_hut_lumberjack");
            Assert.Equal(0.25f, offset.Value, 3);
            offset.Select("vc_hut_builder");
            Assert.Equal(0f, offset.Value, 3);
        }

        [Fact]   // the Storehouse's hand-over (1.5): it stands on its posts as placed, not 30 cm up
        public void The_storehouse_starts_raised_three_quarters_of_a_metre()
        {
            var offset = new PlacementOffset();
            offset.Select(SaveKeys.StorehousePiece);
            Assert.Equal(0.75f, offset.Value, 3);
            offset.Lower();
            Assert.Equal(0.5f, offset.Value, 3);
            offset.Select("vc_hut_builder");
            Assert.Equal(0f, offset.Value, 3);
        }

        [Fact]
        public void The_ghost_is_lifted_once_however_often_the_game_updates_it()
        {
            var lift = new GhostLift();
            Vec3 lifted = lift.Lift(new Vec3(5, 10, 5), 1f);
            Assert.Equal(11f, lifted.Y, 3);
            Assert.Equal(11f, lift.Lift(lifted, 1f).Y, 3);                // the game left it where we put it
            Assert.Equal(10.5f, lift.Lift(lifted, 0.5f).Y, 3);            // lowered meanwhile: measured from the game's spot
            Assert.Equal(13f, lift.Lift(new Vec3(6, 12, 5), 1f).Y, 3);    // the game moved it: from the new spot
            lift.Forget();
            Assert.Equal(14f, lift.Lift(new Vec3(6, 13, 5), 1f).Y, 3);    // forgotten: whatever it gets is the game's spot
        }
    }
}
