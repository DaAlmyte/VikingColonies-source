using System.Collections.Generic;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>Polish 0.18: a goal inside a hut is reached through that hut's own doors (self-test run 42).</summary>
    public class DoorOwnTests
    {
        // The Farm (x -2..2, z -2..2) with its door in the north wall; a neighbour hut (x 3..7, z -2..2) with its door
        // in its west wall, facing the Farm's plot between them.
        private static readonly Footprint Farm = new Footprint(new Vec3(0, 0, 0), Quat.Identity, 2f, 2f);
        private static readonly Footprint Neighbour = new Footprint(new Vec3(5, 0, 0), Quat.Identity, 2f, 2f);
        private static readonly Vec3 FarmDoor = new Vec3(0, 0, 2f), NeighbourDoor = new Vec3(3f, 0, 0);
        private static readonly List<Vec3> Doors = new List<Vec3> { FarmDoor, NeighbourDoor };
        private static readonly List<Footprint> Huts = new List<Footprint> { Farm, Neighbour };

        [Fact]
        public void A_goal_inside_a_hut_is_reached_through_its_own_door()
        {
            // Run 42: from a plot between the huts, the farmer took the neighbour's door to reach the Farm's chest.
            Vec3 plot = new Vec3(2.6f, 0, 1f), chest = new Vec3(0.5f, 0, 0);
            Assert.Equal(new[] { 0 }, DoorRules.OwnDoors(Doors, plot, chest, Huts));
        }

        [Fact]
        public void From_a_building_the_colony_doesnt_know_his_rooms_door_stays_among_them()
        {
            // B1's game check in 1.4.1: inside a stranger's building, on her way to her bed in the Longhouse, Signe's own way
            // out was dropped as "not the door of the hut it's in", and she stayed in.
            Vec3 strangerDoor = new Vec3(0, 0, 15f), inStranger = new Vec3(0, 0, 12f), chest = new Vec3(0.5f, 0, 0);
            var doors = new List<Vec3> { FarmDoor, NeighbourDoor, strangerDoor };
            Assert.Equal(new[] { 0, 2 }, DoorRules.OwnDoors(doors, inStranger, chest, Huts, roomDoor: 2));
            Assert.Equal(new[] { 0 }, DoorRules.OwnDoors(doors, inStranger, chest, Huts));   // no room door: as before
            // Inside one of the colony's huts its own outline decides, as before.
            Vec3 inNeighbour = new Vec3(5, 0, 0);
            Assert.Equal(new[] { 1 }, DoorRules.OwnDoors(doors, inNeighbour, chest, Huts, roomDoor: 2));
        }

        [Fact]
        public void A_neighbours_door_is_never_his_rooms_from_outside_it()
        {
            // The review of the game-check fixes: under the neighbour's porch (outside its walls), its door was the nearest
            // one and came back among the doors to the Farm's chest (self-test run 42's bug).
            Vec3 underPorch = new Vec3(2.6f, 0, -0.5f), chest = new Vec3(0.5f, 0, 0);
            Assert.Equal(new[] { 0 }, DoorRules.OwnDoors(Doors, underPorch, chest, Huts, roomDoor: 1));
        }

        [Fact]
        public void From_inside_a_hut_he_leaves_by_its_own_door_first()
        {
            Vec3 inNeighbour = new Vec3(5, 0, 0), chest = new Vec3(0.5f, 0, 0);
            Assert.Equal(new[] { 1 }, DoorRules.OwnDoors(Doors, inNeighbour, chest, Huts));
        }

        [Fact]
        public void Outside_every_hut_with_an_outside_goal_any_door_may_do()
        {
            Vec3 me = new Vec3(0, 0, 6), goal = new Vec3(8, 0, 6);
            Assert.Equal(new[] { 0, 1 }, DoorRules.OwnDoors(Doors, me, goal, Huts));
        }

        [Fact]
        public void Another_huts_door_never_leads_into_this_one()
        {
            // Review: with the Farm's door not among them (too far for the early look, or tried lately), the neighbour's
            // door came back.
            Vec3 plot = new Vec3(2.6f, 0, 1f), chest = new Vec3(0.5f, 0, 0);
            Assert.Empty(DoorRules.OwnDoors(new List<Vec3> { NeighbourDoor }, plot, chest, Huts));
        }

        [Fact]
        public void Leaving_a_hut_with_none_of_its_doors_among_them_any_door_may_do()
        {
            Vec3 inNeighbour = new Vec3(5, 0, 0), outside = new Vec3(0, 0, 8);
            Assert.Equal(new[] { 0 }, DoorRules.OwnDoors(new List<Vec3> { FarmDoor }, inNeighbour, outside, Huts));
        }

        [Fact]
        public void Pressed_against_a_huts_wall_outside_is_outside()
        {
            // Review: the site's half-metre margin counted a farmer against the Farm's outer wall as inside it.
            var site = new List<Footprint> { Farm.Grow(0.5f), Neighbour.Grow(0.5f) };
            Vec3 againstWall = new Vec3(2.3f, 0, 1f), chest = new Vec3(0.5f, 0, 0);
            Assert.Equal(new[] { 0 }, DoorRules.OwnDoors(Doors, againstWall, chest, site, siteMargin: 0.5f));
        }
    }
}
