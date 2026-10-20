using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>
    /// Full runs of 16 and 19 October: a settler inside a building whose door stood outside the colony's area spent the
    /// night aiming for far doors they couldn't reach (the Longhouse's, a Builder's Hut's) and never left by the door
    /// beside them.
    /// </summary>
    public class LeaveByOwnDoorTests
    {
        [Fact]
        public void The_door_beside_him_is_his_way_out_wherever_the_colony_ends()
        {
            Assert.True(DoorRules.MayLeaveBy(1.0f, goalInColony: true));
            Assert.True(DoorRules.MayLeaveBy(DoorRules.LeaveByReach, goalInColony: true));
            Assert.False(DoorRules.MayLeaveBy(DoorRules.LeaveByReach + 0.5f, goalInColony: true));
            Assert.False(DoorRules.MayLeaveBy(23f, goalInColony: true));   // the Longhouse's door across the meadow
        }

        [Fact]
        public void A_key_or_another_ward_still_keeps_it_shut()
        {
            Assert.True(DoorRules.MayOpen(inColonyArea: DoorRules.MayLeaveBy(1f, true), needsKey: false, otherWard: false));
            Assert.False(DoorRules.MayOpen(inColonyArea: DoorRules.MayLeaveBy(1f, true), needsKey: true, otherWard: false));
            Assert.False(DoorRules.MayOpen(inColonyArea: DoorRules.MayLeaveBy(1f, true), needsKey: false, otherWard: true));
        }

        [Fact]
        public void A_far_door_he_can_reach_neither_side_of_is_not_forced_on_him_by_its_huts_shape()
        {
            Assert.False(DoorRules.HutSideApplies(frontReachable: false, backReachable: false, fromDoor: 23f));   // Signe's night
            Assert.True(DoorRules.HutSideApplies(frontReachable: false, backReachable: false, fromDoor: 2f));     // right by it, in its hut
            Assert.True(DoorRules.HutSideApplies(frontReachable: true, backReachable: false, fromDoor: 23f));     // a turned hut's map, wrong side
            Assert.True(DoorRules.HutSideApplies(frontReachable: true, backReachable: true, fromDoor: 23f));      // both: the hut decides
        }

        [Fact]
        public void Out_in_the_open_a_far_huts_shape_still_tells_his_side()
        {
            // 1.4.1's full run: the builder, out in the open 18 m from the Farm's yard gate (the walking map reaching neither
            // side of it), was refused its side and left a pole unmended. Only from inside something (Signe's night) does a
            // far door's hut shape not decide.
            Assert.True(DoorRules.HutSideApplies(frontReachable: false, backReachable: false, fromDoor: 18f, outInTheOpen: true));
            Assert.False(DoorRules.HutSideApplies(frontReachable: false, backReachable: false, fromDoor: 23f, outInTheOpen: false));   // Signe's night
        }

        [Fact]
        public void Inside_the_doors_own_hut_its_shape_still_decides_however_far_the_door()
        {
            // A level-5 Longhouse is 21 m long: from a bed 18 m from its door, the walking map may reach neither side (the
            // turned-hut case of 0.20), and the hut's shape is what sends him out (the review of 1.4.1).
            Assert.True(DoorRules.HutSideApplies(frontReachable: false, backReachable: false, fromDoor: 18f, insideItsHut: true));
            Assert.False(DoorRules.HutSideApplies(frontReachable: false, backReachable: false, fromDoor: 18f, insideItsHut: false));
        }

        [Fact]
        public void The_door_beside_him_is_his_only_on_his_way_home_into_the_colony()
        {
            Assert.True(DoorRules.MayLeaveBy(1.0f, goalInColony: true));
            Assert.False(DoorRules.MayLeaveBy(1.0f, goalInColony: false));   // walking somewhere else past a stranger's door: not his
        }

        [Fact]
        public void Under_a_roof_the_door_beside_him_is_his_way_out_wherever_he_goes()
        {
            // The second review of 1.4.1: a worker in a stranger's house past the colony's edge, off to work (a tree 40 m
            // from his hut can lie outside the colony's 50 m), was refused the door beside him again.
            Assert.True(DoorRules.MayLeaveBy(1.0f, goalInColony: false, underRoof: true));
            Assert.False(DoorRules.MayLeaveBy(5f, goalInColony: false, underRoof: true));
        }

        [Fact]
        public void Under_a_roof_the_nearest_door_is_his_way_out_across_the_room()
        {
            // B1's game check in 1.4.1: inside a building the colony doesn't know, Leif stood 5.7 m from its door, across the
            // room; the door was refused as outside the colony's area and he stayed in all night.
            Assert.True(DoorRules.MayLeaveBy(5.7f, goalInColony: true, underRoof: true, nearestToHim: true, sheltered: true));
            Assert.True(DoorRules.MayLeaveBy(DoorRules.RoomReach, goalInColony: false, underRoof: true, nearestToHim: true, sheltered: true));
            Assert.False(DoorRules.MayLeaveBy(DoorRules.RoomReach + 1f, goalInColony: true, underRoof: true, nearestToHim: true, sheltered: true));
            Assert.False(DoorRules.MayLeaveBy(5.7f, goalInColony: true, underRoof: true, nearestToHim: false, sheltered: true));   // another room's
            Assert.False(DoorRules.MayLeaveBy(5.7f, goalInColony: true, underRoof: false, nearestToHim: true));   // out in the open
        }

        [Fact]
        public void Under_eaves_or_a_porch_is_not_inside_a_room()
        {
            // The review of the game-check fixes: our huts have 2 m eaves and porches; under them a settler is under a roof but
            // outside, and a door 12 m off isn't the door of his room.
            Assert.False(DoorRules.MayLeaveBy(5.7f, goalInColony: true, underRoof: true, nearestToHim: true, sheltered: false));
            Assert.True(DoorRules.MayLeaveBy(1.0f, goalInColony: true, underRoof: true, nearestToHim: true, sheltered: false));   // beside it, as before
            Assert.True(DoorRules.Sheltered(underRoof: true, cover: DoorRules.ShelteredCover));
            Assert.False(DoorRules.Sheltered(underRoof: true, cover: 0.5f));   // a porch: roof above, open round
            Assert.False(DoorRules.Sheltered(underRoof: false, cover: 0.9f));
        }
    }
}
