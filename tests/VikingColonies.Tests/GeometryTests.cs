using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    public class GeometryTests
    {
        private static void Near(Vec3 expected, Vec3 actual)
        {
            Assert.Equal(expected.X, actual.X, 3);
            Assert.Equal(expected.Y, actual.Y, 3);
            Assert.Equal(expected.Z, actual.Z, 3);
        }

        [Fact]
        public void A_quarter_turn_to_the_right_points_east_to_south_like_unity()
        {
            // Unity: Quaternion.Euler(0, 90, 0) * Vector3.right == Vector3.back
            Near(new Vec3(0, 0, -1), Quat.Yaw(90).Rotate(new Vec3(1, 0, 0)));
        }

        [Fact]
        public void Turning_and_turning_back_gives_the_original_point()
        {
            var p = new Vec3(3, 1, -2);
            Quat q = Quat.Yaw(37);
            Near(p, q.Inverse.Rotate(q.Rotate(p)));
        }

        [Fact]
        public void Turns_add_up()
        {
            Near(Quat.Yaw(30).Rotate(Quat.Yaw(60).Rotate(new Vec3(0, 0, 1))), (Quat.Yaw(30) * Quat.Yaw(60)).Rotate(new Vec3(0, 0, 1)));
            Assert.Equal(90f, Quat.Angle(Quat.Identity, Quat.Yaw(90)), 2);
        }

        [Fact]
        public void Angles_between_directions_ignore_length_and_are_zero_for_nothing()
        {
            Assert.Equal(180f, Vec3.Angle(new Vec3(1, 0, 0), new Vec3(-5, 0, 0)), 2);
            Assert.Equal(0f, Vec3.Angle(Vec3.Zero, new Vec3(1, 0, 0)));
        }
    }
}
