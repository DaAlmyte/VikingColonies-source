using System.Collections.Generic;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>
    /// 1.5.1, the player's 1.5.0 watch: settlers ran round the back of a hut before going in at its door. The game's path to
    /// a goal inside a hut ends behind its back wall (a closed door is a wall to it), and the door was looked for only within
    /// 12 m of the settler. A hut's walls between the way's end and the goal now send them to its door at once.
    /// </summary>
    public class HutWallsBetweenTests
    {
        // A hut's walls x -3..3, z -4..4 (its room outline with the site's 0.5 m round it), its door in the north wall.
        private const float Margin = 0.5f;
        private static readonly Footprint Hut = new Footprint(new Vec3(0, 0, 0), Quat.Identity, 3.5f, 4.5f);
        private static readonly Footprint Other = new Footprint(new Vec3(20, 0, 0), Quat.Identity, 3.5f, 4.5f);
        private static readonly List<Footprint> Huts = new List<Footprint> { Hut, Other };
        private static readonly Vec3 ChestSide = new Vec3(0, 0, -2.4f);   // in front of the chest on the back wall

        [Fact]
        public void A_way_ending_behind_the_back_wall_of_the_goals_hut_has_its_walls_between()
        {
            Vec3 behind = new Vec3(0, 0, -4.8f);
            Assert.Same(Hut, DoorRules.HutWallsBetween(behind, ChestSide, Huts, Margin));
        }

        [Fact]
        public void A_way_ending_inside_the_goals_hut_has_no_walls_between()
        {
            Assert.Null(DoorRules.HutWallsBetween(new Vec3(1f, 0, 2f), ChestSide, Huts, Margin));
        }

        [Fact]
        public void Walking_out_to_just_behind_the_back_wall_the_walls_are_between()
        {
            // Seen before (0.20): a Longhouse household off to work behind its back wall crowded into the back corner first.
            Vec3 goal = new Vec3(0, 0, -5.5f), wayEnd = new Vec3(0, 0, -3.6f);
            Assert.Same(Hut, DoorRules.HutWallsBetween(wayEnd, goal, Huts, Margin));
        }

        [Fact]
        public void Out_in_the_open_with_no_hut_round_either_there_are_no_walls_between()
        {
            Assert.Null(DoorRules.HutWallsBetween(new Vec3(10, 0, 10), new Vec3(10, 0, 12), Huts, Margin));
        }

        [Fact]
        public void A_goal_against_the_outside_of_a_wall_isnt_inside_the_hut()
        {
            // A woodpile against the back wall, outside: the way ending beside it is right.
            Vec3 woodpile = new Vec3(0, 0, -4.3f);
            Assert.Null(DoorRules.HutWallsBetween(new Vec3(0, 0, -5f), woodpile, Huts, Margin));
        }

        [Fact]
        public void From_one_hut_to_inside_another_its_walls_are_the_ones_between()
        {
            // The way from inside the first hut ends behind the other's wall: the other's door is the way in.
            Vec3 inOther = new Vec3(20, 0, 1f), wayEnd = new Vec3(16f, 0, 1f);
            Assert.Same(Other, DoorRules.HutWallsBetween(wayEnd, inOther, Huts, Margin));
        }

        [Fact]
        public void No_huts_known_means_no_walls_between()
        {
            Assert.Null(DoorRules.HutWallsBetween(new Vec3(0, 0, -4.8f), ChestSide, null, Margin));
        }

        [Fact]
        public void Walls_between_have_him_look_for_the_door_now_however_short_the_gap()
        {
            // The way behind the back wall ends under 2 m from the chest's open side: it was never looked at early.
            Assert.True(DoorRules.LookForDoorNow(1.2f, wallsBetween: true));
            Assert.False(DoorRules.LookForDoorNow(1.2f, wallsBetween: false));
            Assert.True(DoorRules.LookForDoorNow(3f, wallsBetween: false));   // as before
        }

        [Fact]
        public void The_door_of_the_hut_whose_walls_are_between_counts_from_any_distance()
        {
            // Running to shelter from 25 m, the door was 22 m off and not looked at; 6 s later they stood behind the hut.
            Assert.True(DoorRules.NearEnoughToLookEarly(30f, ofWalledHut: true));
            Assert.False(DoorRules.NearEnoughToLookEarly(30f, ofWalledHut: false));
            Assert.True(DoorRules.NearEnoughToLookEarly(10f, ofWalledHut: false));   // as before
        }
    }
}
