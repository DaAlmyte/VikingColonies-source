using System.Collections.Generic;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>
    /// Worker polish (0.20): inside a hut turned off the compass the game's walking map is wrong (from the farmer's spot in a
    /// Farm turned 16° it said the door's outside spots were reachable and the inside ones not), so the side a settler
    /// starts from comes from the hut's shape there.
    /// </summary>
    public class DoorSideTests
    {
        // A hut 6 x 6 m turned 37°, its door in its +Z wall.
        private static readonly Quat Turn = Quat.Yaw(37f);
        private static readonly Vec3 Middle = new Vec3(100f, 0f, 50f);
        private static readonly Footprint Hut = new Footprint(Middle, Turn, 3f, 3f);
        private static readonly Vec3 Door = Middle + Turn.Rotate(new Vec3(0f, 0f, 3f));
        private static readonly Vec3 Forward = Turn.Rotate(new Vec3(0f, 0f, 1f));
        private static readonly List<Footprint> Huts = new List<Footprint> { Hut };
        private static readonly Vec3 FarOutside = Middle + Turn.Rotate(new Vec3(0f, 0f, 13f));

        private static int InsideSide => DoorRules.SideOf(Door, Forward, Middle);

        [Fact]
        public void A_settler_inside_a_turned_hut_starts_from_the_inside()
        {
            Vec3 inside = Middle + Turn.Rotate(new Vec3(1f, 0f, 0f));
            Assert.Equal(InsideSide, DoorRules.SideFromHut(Door, Forward, inside, FarOutside, Huts, 0.5f));
            Assert.NotEqual(0, InsideSide);
        }

        [Fact]
        public void A_settler_outside_going_in_starts_from_the_outside()
        {
            Vec3 inFront = Middle + Turn.Rotate(new Vec3(0f, 0f, 9f));
            Assert.Equal(-InsideSide, DoorRules.SideFromHut(Door, Forward, inFront, Middle, Huts, 0.5f));
        }

        [Fact]
        public void A_door_of_another_hut_is_left_to_the_walking_map()
        {
            Vec3 otherDoor = Middle + Turn.Rotate(new Vec3(0f, 0f, 6f));   // 3 m beyond this hut's wall
            Vec3 inside = Middle + Turn.Rotate(new Vec3(1f, 0f, 0f));
            Assert.Equal(0, DoorRules.SideFromHut(otherDoor, Forward, inside, FarOutside, Huts, 0.5f));
        }

        // Full self-test run 3: a settler inside one hut, going to a goal inside another, was sent to the other hut's door
        // from outside; he must leave his own hut first (by its door), as before.
        [Fact]
        public void A_settler_inside_another_hut_is_left_to_the_walking_map()
        {
            var other = new Footprint(Middle + Turn.Rotate(new Vec3(-12f, 0f, 0f)), Turn, 3f, 3f);
            var huts = new List<Footprint> { Hut, other };
            Vec3 inOther = other.Centre + Turn.Rotate(new Vec3(1f, 0f, 0f));
            Assert.Equal(0, DoorRules.SideFromHut(Door, Forward, inOther, Middle, huts, 0.5f));
        }

        // Final review of 0.20: a hut of one's own design with a porch roof reaching past its door. Under it, in front of the
        // door, he is outside: going in he starts from the outside, and he never walks straight at the closed door.
        [Fact]
        public void A_settler_under_a_porch_in_front_of_the_door_is_outside()
        {
            var withPorch = new Footprint(Middle + Turn.Rotate(new Vec3(0f, 0f, 0.75f)), Turn, 3f, 3.75f);   // 1.5 m past the door
            var huts = new List<Footprint> { withPorch };
            Vec3 underPorch = Middle + Turn.Rotate(new Vec3(0f, 0f, 3.8f));
            Assert.Equal(-InsideSide, DoorRules.SideFromHut(Door, Forward, underPorch, Middle, huts, 0.5f));
            Assert.False(DoorRules.InsideItsHut(Door, Forward, underPorch, huts, 0.5f));
        }

        [Fact]
        public void A_settler_between_huts_is_left_to_the_walking_map()
        {
            Vec3 outside = Middle + Turn.Rotate(new Vec3(8f, 0f, 0f));
            Assert.Equal(0, DoorRules.SideFromHut(Door, Forward, outside, FarOutside, Huts, 0.5f));
        }

        // Observation run 2 (0.20): inside the turned Farm the paths gave the farmer no way at all to her own door's spot
        // 3 m away, and she stood still till she gave up, 22 times; inside its hut he walks there straight.
        [Fact]
        public void A_settler_inside_the_doors_hut_is_inside_its_hut()
        {
            Vec3 inside = Middle + Turn.Rotate(new Vec3(1f, 0f, -1f));
            Assert.True(DoorRules.InsideItsHut(Door, Forward, inside, Huts, 0.5f));
        }

        // Observation run 3 (0.20): in the turned Farm the walking map said the outside was reachable, so her path led into
        // the wall instead of ending, and she never came closer to the spot.
        [Theory]
        [InlineData(true, true, 0f, true)]      // inside its hut, the paths give no way
        [InlineData(true, false, 2f, true)]     // inside its hut, a path that brings him no closer
        [InlineData(true, false, 1f, false)]    // inside, on his way a moment ago: the path still has its chance
        [InlineData(false, true, 5f, false)]    // outside: the paths are right there
        public void Inside_its_hut_he_walks_straight_when_the_paths_dont_bring_him_closer(bool inside, bool wayEnded, float notCloserFor, bool straight)
        {
            Assert.Equal(straight, DoorRules.WalkStraight(inside, wayEnded, notCloserFor));
        }

        [Fact]
        public void A_settler_outside_or_in_another_hut_is_not_inside_the_doors_hut()
        {
            var other = new Footprint(Middle + Turn.Rotate(new Vec3(-12f, 0f, 0f)), Turn, 3f, 3f);
            var huts = new List<Footprint> { Hut, other };
            Assert.False(DoorRules.InsideItsHut(Door, Forward, FarOutside, huts, 0.5f));
            Assert.False(DoorRules.InsideItsHut(Door, Forward, other.Centre, huts, 0.5f));
            Assert.False(DoorRules.InsideItsHut(Door, Forward, Middle, null, 0.5f));
        }
    }
}
