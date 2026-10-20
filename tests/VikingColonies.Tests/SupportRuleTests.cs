using System.Collections.Generic;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    public class SupportRuleTests
    {
        // The game's numbers for wood: max 100, min 10, horizontal loss 0.2, vertical loss 0.125.
        private static readonly SupportMaterial Wood = new SupportMaterial(100f, 10f, 0.2f, 0.125f);

        private static float Support(bool grounded, params SupportContact[] contacts) =>
            SupportRule.Evaluate(Wood, grounded, new List<SupportContact>(contacts));

        [Fact]
        public void Touching_the_ground_gives_full_support()
        {
            Assert.Equal(100f, Support(true));
        }

        [Fact]
        public void A_piece_touching_nothing_has_no_support_and_falls()
        {
            Assert.Equal(0f, Support(false));
            Assert.False(SupportRule.Holds(Wood, 0f));
        }

        [Fact]
        public void Resting_on_a_piece_below_loses_the_vertical_amount()
        {
            // 100 - 0.125 * 1.1 * 100
            Assert.Equal(86.25f, Support(false, new SupportContact(100f, 1.1f, new Vec3(0, -0.5f, 0))), 2);
        }

        [Fact]
        public void Hanging_on_a_piece_beside_it_loses_the_horizontal_amount()
        {
            // 100 - 0.2 * 2.1 * 100
            Assert.Equal(58f, Support(false, new SupportContact(100f, 2.1f, new Vec3(1, 0, 0))), 2);
        }

        [Fact]
        public void A_point_slightly_below_leans_between_horizontal_and_vertical()
        {
            float support = Support(false, new SupportContact(50f, 2.1f, new Vec3(1, -0.1f, 0)));
            Assert.InRange(support, 31.2f, 31.3f);
        }

        [Fact]
        public void Two_supports_on_opposite_sides_carry_it_like_a_bridge()
        {
            float support = Support(false,
                new SupportContact(50f, 2.1f, new Vec3(-1, -0.1f, 0)),
                new SupportContact(50f, 2.1f, new Vec3(1, -0.1f, 0)));
            Assert.Equal(36.875f, support, 2);   // 50 - 0.125 * 2.1 * 50
        }

        [Fact]
        public void Two_supports_at_a_right_angle_are_not_a_bridge()
        {
            float support = Support(false,
                new SupportContact(50f, 2.1f, new Vec3(1, -0.1f, 0)),
                new SupportContact(50f, 2.1f, new Vec3(0, -0.1f, 1)));
            Assert.InRange(support, 31.2f, 31.3f);
        }

        [Fact]
        public void Support_never_exceeds_the_materials_maximum()
        {
            Assert.Equal(100f, Support(false, new SupportContact(1000f, 1.1f, new Vec3(0, -0.5f, 0))));
        }

        [Fact]
        public void A_piece_holds_from_its_minimum_up()
        {
            Assert.True(SupportRule.Holds(Wood, 10f));
            Assert.False(SupportRule.Holds(Wood, 9.99f));
        }

        [Fact]
        public void The_distance_uses_the_nearer_of_the_neighbours_centre_and_position()
        {
            var me = new Vec3(0, 0, 0);
            Assert.Equal(1.1f, SupportRule.ContactDistance(me, new Vec3(3, 0, 0), new Vec3(1, 0, 0), false), 3);
            Assert.Equal(3.1f, SupportRule.ContactDistance(me, new Vec3(3, 0, 0), new Vec3(1, 0, 0), true), 3);
        }
    }
}
