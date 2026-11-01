using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>
    /// Out of a hut by its door to a goal behind it (1.5.1's walk check: the game's own way round the Builder's Hut's corner
    /// went 3 m wide): round its walls close by, then the game's own way once the goal is in sight.
    /// </summary>
    public class LeftHutTests
    {
        // A hut's walls x -2..2, z -3..3, its door in the north wall; out of it at (0, 4.2), off to (0, -8) behind it.
        private static readonly Footprint Walls = new Footprint(new Vec3(0, 0, 0), Quat.Identity, 2f, 3f);
        private static readonly Vec3 Out = new Vec3(0, 0, 4.2f), Behind = new Vec3(0, 0, -8f);
        private const float R = HutWay.Round;

        private static void Near(Vec3 expected, Vec3 actual)
        {
            Assert.Equal(expected.X, actual.X, 2);
            Assert.Equal(expected.Z, actual.Z, 2);
        }

        [Fact]
        public void Just_out_of_its_door_he_heads_round_its_corner()
        {
            var left = new LeftHut();
            left.Left(Walls, Out, Behind, 0.0);
            Vec3 next = left.Next(Out, Behind, 0.5);
            Assert.Equal(2f + R, System.Math.Abs(next.X), 2);
            Assert.Equal(3f + R, next.Z, 2);
        }

        [Fact]
        public void With_the_goal_in_sight_the_games_own_way_from_there_on()
        {
            var left = new LeftHut();
            left.Left(Walls, Out, Behind, 0.0);
            var pastCorner = new Vec3(-2 - R, 0, -3 - R);
            Near(Behind, left.Next(pastCorner, Behind, 3.0));
            Near(Behind, left.Next(Out, Behind, 3.5));   // forgotten: it doesn't send him round again
        }

        [Fact]
        public void A_new_goal_or_a_while_later_it_is_forgotten()
        {
            var left = new LeftHut();
            left.Left(Walls, Out, Behind, 0.0);
            var elsewhere = new Vec3(10, 0, -8);
            Near(elsewhere, left.Next(Out, elsewhere, 0.5));
            left.Left(Walls, Out, Behind, 10.0);
            Near(Behind, left.Next(Out, Behind, 10.0 + LeftHut.ForSeconds + 1));
        }

        [Fact]
        public void Going_into_a_hut_or_to_a_goal_inside_one_it_isnt_this_rules_way()
        {
            var left = new LeftHut();
            left.Left(Walls, new Vec3(0, 0, 2), Behind, 0.0);   // through the door inwards
            Near(Behind, left.Next(new Vec3(0, 0, 2), Behind, 0.5));
            left.Left(Walls, Out, new Vec3(0, 0, -1), 0.0);   // the goal within its walls
            Near(new Vec3(0, 0, -1), left.Next(Out, new Vec3(0, 0, -1), 0.5));
        }

        [Fact]
        public void Far_from_the_hut_he_left_it_is_forgotten()
        {
            // Moved away within the time with the same goal (a rescue): not walked back to the hut's corner.
            var left = new LeftHut();
            left.Left(Walls, Out, Behind, 0.0);
            var far = new Vec3(40, 0, 30);
            Near(Behind, left.Next(far, Behind, 1.0));
            Near(Behind, left.Next(Out, Behind, 1.5));
        }
    }
}
