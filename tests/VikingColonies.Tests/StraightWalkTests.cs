using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>
    /// Observation run 4 (0.20): walking straight to her turned Farm's door from behind its chest (in the hut's middle),
    /// the farmer stood still: a hop doesn't clear a chest. She steps aside, away from the hut's middle, and goes on.
    /// </summary>
    public class StraightWalkTests
    {
        [Fact]
        public void Moving_on_he_keeps_walking_straight()
        {
            var walk = new StraightWalk();
            walk.Start(0f, 0f, 0.0, 1);
            Assert.Equal(UnstuckMove.None, walk.Next(0.5f, 0f, 1.0));
            Assert.Equal(UnstuckMove.None, walk.Next(1.0f, 0f, 2.0));
        }

        [Fact]
        public void Stopped_he_hops_first_then_steps_aside_for_a_moment()
        {
            var walk = new StraightWalk();
            walk.Start(0f, 0f, 0.0, -1);
            Assert.Equal(UnstuckMove.None, walk.Next(0f, 0f, 0.5));   // not a second yet
            Assert.Equal(UnstuckMove.Hop, walk.Next(0f, 0f, 1.0));
            Assert.Equal(UnstuckMove.Sidestep, walk.Next(0.1f, 0f, 2.0));
            Assert.Equal(-1, walk.Side);
            Assert.Equal(UnstuckMove.Sidestep, walk.Next(0.4f, 0f, 2.5));   // still stepping aside
            Assert.Equal(UnstuckMove.None, walk.Next(1.2f, 0f, 3.0));       // aside done, and he moved: straight on
        }

        [Fact]
        public void He_steps_aside_twice_to_one_side_then_tries_the_other()
        {
            var walk = new StraightWalk();
            walk.Start(0f, 0f, 0.0, 1);
            double t = 0.0;
            var sides = new System.Collections.Generic.List<int>();
            for (int i = 0; i < 12 && sides.Count < 3; i++)
            {
                t += StraightWalk.CheckSeconds;
                if (walk.Next(0f, 0f, t) == UnstuckMove.Sidestep)
                {
                    sides.Add(walk.Side);
                    t += StraightWalk.AsideSeconds;   // the step aside took him nowhere either
                }
            }
            Assert.Equal(new[] { 1, 1, -1 }, sides);
        }

        [Fact]
        public void He_steps_aside_away_from_the_huts_middle()
        {
            // Walking north (+z) from the origin: x+ is his right.
            Vec3 me = new Vec3(0f, 0f, 0f), spot = new Vec3(0f, 0f, 3f);
            Assert.Equal(1, DoorRules.AsideFromMiddle(me, spot, new Vec3(-0.5f, 0f, 1f)));   // the middle on his left: right
            Assert.Equal(-1, DoorRules.AsideFromMiddle(me, spot, new Vec3(0.5f, 0f, 1f)));   // on his right: left
            Assert.Equal(1, DoorRules.AsideFromMiddle(me, spot, new Vec3(0f, 0f, 1f)));      // on his way: right
        }
    }
}
