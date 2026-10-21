using System.Collections.Generic;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    public class DoorRulesTests
    {
        private static readonly Vec3 Door = new Vec3(0, 0, 3);
        private static readonly Vec3 Forward = new Vec3(0, 0, 1);   // the way through, from inside (z < 3) to outside

        // 0.20.1, from Valheim Villages' door handler: a door opened from the far side swings towards this one and its leaf
        // stands in the doorway; opening it again does nothing, so he closes it and opens it from his side.
        [Fact]
        public void A_door_opened_from_the_far_side_swings_towards_him()
        {
            Assert.Equal(-1, DoorRules.OpenStateFrom(1));   // the game's UseDoor: opened from behind (side -1) is +1
            Assert.Equal(1, DoorRules.OpenStateFrom(-1));
            Assert.True(DoorRules.SwingsTowards(state: 1, side: 1));
            Assert.False(DoorRules.SwingsTowards(state: -1, side: 1));
            Assert.True(DoorRules.SwingsTowards(state: -1, side: -1));
            Assert.False(DoorRules.SwingsTowards(state: 0, side: 1));
        }

        // 0.20.1: after a hop or a step aside he may face sideways at a closed gate; the way he is going is his path's.
        [Fact]
        public void His_heading_is_his_paths_next_point()
        {
            var me = new Vec3(0, 0, 0);
            Vec3 facing = new Vec3(1, 0, 0);
            Vec3 heading = DoorRules.WayHeading(me, new List<Vec3> { new Vec3(0.1f, 0, 0.1f), new Vec3(0, 0, 4) }, facing);
            Assert.True(heading.Z > 0.99f && System.Math.Abs(heading.X) < 0.01f);
            Assert.Equal(facing, DoorRules.WayHeading(me, new List<Vec3>(), facing));
            Assert.Equal(facing, DoorRules.WayHeading(me, null, facing));
        }

        // 0.20.1, from Hearthfolk: a player standing in a doorway is waited for a moment, then he goes on through.
        [Fact]
        public void A_player_in_the_doorway_is_waited_for_two_seconds()
        {
            Assert.False(DoorRules.PressOnPastPlayer(1.9f));
            Assert.True(DoorRules.PressOnPastPlayer(DoorRules.PlayerWaitSeconds));
        }

        // Review of 0.20.1: a player in the doorway hid a monster there (the first one found was the player), and he pressed on
        // past the monster after 2 s.
        [Fact]
        public void A_player_in_the_doorway_with_a_monster_there_means_giving_the_door_up()
        {
            Assert.Equal(DoorwayChoice.GiveUp, DoorRules.PlayerInDoorway(hostileThere: true, waited: 5f));
            Assert.Equal(DoorwayChoice.Wait, DoorRules.PlayerInDoorway(hostileThere: false, waited: 1f));
            Assert.Equal(DoorwayChoice.PressOn, DoorRules.PlayerInDoorway(hostileThere: false, waited: DoorRules.PlayerWaitSeconds));
        }

        // Review of 0.20.1: the re-swing closed the door on a player standing in the doorway; only with the doorway clear,
        // only a door he didn't open himself, once a passing.
        [Fact]
        public void A_door_is_reswung_only_with_the_doorway_clear_once_and_not_his_own()
        {
            Assert.True(DoorRules.MayReswing(openedIt: false, reswung: false, state: 1, side: 1, doorwayClear: true));
            Assert.False(DoorRules.MayReswing(openedIt: false, reswung: false, state: 1, side: 1, doorwayClear: false));
            Assert.False(DoorRules.MayReswing(openedIt: false, reswung: true, state: 1, side: 1, doorwayClear: true));
            Assert.False(DoorRules.MayReswing(openedIt: true, reswung: false, state: 1, side: 1, doorwayClear: true));
            Assert.False(DoorRules.MayReswing(openedIt: false, reswung: false, state: -1, side: 1, doorwayClear: true));
        }

        [Fact]
        public void A_settler_opens_only_colony_doors_the_owner_may_open_without_a_key()
        {
            Assert.True(DoorRules.MayOpen(inColonyArea: true, needsKey: false, otherWard: false));
            Assert.False(DoorRules.MayOpen(inColonyArea: false, needsKey: false, otherWard: false));
            Assert.False(DoorRules.MayOpen(inColonyArea: true, needsKey: true, otherWard: false));
            Assert.False(DoorRules.MayOpen(inColonyArea: true, needsKey: false, otherWard: true));
        }

        [Fact]
        public void A_huts_own_door_counts_as_in_the_colony_though_just_past_its_edge()
        {
            Assert.True(DoorRules.InColonyArea(fromHall: 30f, radius: 50f, fromNearestHut: 99f));
            Assert.True(DoorRules.InColonyArea(fromHall: 54f, radius: 50f, fromNearestHut: 5.3f));   // the hut's chest is inside, its door 5 m on
            Assert.False(DoorRules.InColonyArea(fromHall: 54f, radius: 50f, fromNearestHut: 30f));
        }

        [Fact]
        public void Standing_off_to_the_side_he_first_lines_up_with_the_doorway_then_walks_straight_through()
        {
            // opened from 1.2 m beside the doorway's middle line: a straight step would hit the wall
            Vec3 first = DoorRules.ThroughStep(Door, Forward, startSide: -1, settler: new Vec3(1.2f, 0, 1.9f));
            Assert.Equal(0f, first.X, 3);
            Assert.Equal(3f - DoorRules.StandOff, first.Z, 3);
            // lined up: straight through to beyond the doorway
            Vec3 then = DoorRules.ThroughStep(Door, Forward, startSide: -1, settler: new Vec3(0.2f, 0, 1.9f));
            Assert.Equal(0f, then.X, 3);
            Assert.True(then.Z > 4f);
            // already in the doorway, though a little to the side: on through
            Assert.True(DoorRules.ThroughStep(Door, Forward, startSide: -1, settler: new Vec3(0.5f, 0, 3.1f)).Z > 4f);
        }

        // Seen: a settler lined up, his step drifted just past the line-up limit (a stair's edge, a gate post), he went
        // back to line up, drifted again, and after 6 s gave up.
        [Fact]
        public void Once_lined_up_a_little_drift_doesnt_send_him_back()
        {
            var drifted = new Vec3(0.5f, 0, 2.4f);
            Assert.True(DoorRules.ThroughStep(Door, Forward, startSide: -1, settler: drifted, linedUp: true).Z > 4f);
            Assert.Equal(3f - DoorRules.StandOff, DoorRules.ThroughStep(Door, Forward, startSide: -1, settler: drifted, linedUp: false).Z, 3);
            var wayOff = new Vec3(1.0f, 0, 2.4f);   // pushed well aside: lines up again
            Assert.Equal(3f - DoorRules.StandOff, DoorRules.ThroughStep(Door, Forward, startSide: -1, settler: wayOff, linedUp: true).Z, 3);
        }

        [Fact]
        public void A_door_has_two_sides_and_the_doorway_between_them()
        {
            Assert.Equal(-1, DoorRules.SideOf(Door, Forward, new Vec3(0.5f, 0, 1)));
            Assert.Equal(1, DoorRules.SideOf(Door, Forward, new Vec3(-0.5f, 0, 5)));
            Assert.Equal(0, DoorRules.SideOf(Door, Forward, new Vec3(0.3f, 0, 3.02f)));
            Assert.Equal(1, DoorRules.SideOf(Door, new Vec3(0, 7, 2), new Vec3(0, -3, 5)));   // heights don't matter
        }

        [Fact]
        public void He_stands_in_front_of_the_door_and_walks_on_beyond_it()
        {
            Vec3 before = DoorRules.OnSide(Door, Forward, -1, 1.2f);
            Vec3 beyond = DoorRules.OnSide(Door, new Vec3(0, 0, 2), 1, 1.5f);
            Assert.Equal(1.8f, before.Z, 3);
            Assert.Equal(4.5f, beyond.Z, 3);
            Assert.Equal(0f, beyond.X, 3);
        }

        [Fact]
        public void A_door_he_went_through_is_closed_once_he_is_2_m_past_it()
        {
            Assert.False(DoorRules.ShouldClose(wentThrough: true, distance: 1.5f, doorwayClear: true, othersPassing: false));
            Assert.True(DoorRules.ShouldClose(wentThrough: true, distance: 2f, doorwayClear: true, othersPassing: false));
        }

        [Fact]
        public void A_door_he_opened_but_never_went_through_is_closed_once_he_is_4_m_away()
        {
            Assert.False(DoorRules.ShouldClose(wentThrough: false, distance: 3f, doorwayClear: true, othersPassing: false));
            Assert.True(DoorRules.ShouldClose(wentThrough: false, distance: 4.5f, doorwayClear: true, othersPassing: false));
        }

        [Fact]
        public void A_door_is_never_closed_on_someone_in_the_doorway()
        {
            Assert.False(DoorRules.ShouldClose(wentThrough: true, distance: 10f, doorwayClear: false, othersPassing: false));
            Assert.False(DoorRules.ShouldClose(wentThrough: false, distance: 10f, doorwayClear: false, othersPassing: false));
        }

        [Fact]
        public void From_inside_a_hut_he_takes_its_own_door_not_a_far_one()
        {
            var doors = new List<Vec3> { new Vec3(0, 0, 40), Door, new Vec3(20, 0, 3) };   // too far; its own; a longer way
            Assert.Equal(1, DoorRules.ChooseDoor(doors, settler: new Vec3(0, 0, 0), goal: new Vec3(2, 0, 12)));
        }

        [Fact]
        public void From_outside_he_takes_the_door_with_the_shortest_way_to_his_goal()
        {
            var doors = new List<Vec3> { new Vec3(-6, 0, 0), new Vec3(6, 0, 0) };   // a hut with doors on both sides
            Assert.Equal(1, DoorRules.ChooseDoor(doors, settler: new Vec3(15, 0, 0), goal: new Vec3(1, 0, 0)));
        }

        [Fact]
        public void Doors_far_from_him_and_from_his_goal_are_left_alone()
        {
            var doors = new List<Vec3> { new Vec3(100, 0, 100) };
            Assert.Equal(-1, DoorRules.ChooseDoor(doors, settler: Vec3.Zero, goal: new Vec3(10, 0, 0)));
            Assert.Equal(-1, DoorRules.ChooseDoor(new List<Vec3>(), Vec3.Zero, new Vec3(10, 0, 0)));
        }

        [Fact]
        public void A_door_he_walks_straight_at_with_his_goal_beyond_is_on_his_way()
        {
            var heading = new Vec3(0, 0, 1);
            var beyond = new Vec3(0, 0, 10);
            Assert.True(DoorRules.IsOnWayThrough(new Vec3(0, 0, 2), heading, Door, Forward, beyond));
            Assert.True(DoorRules.IsOnWayThrough(new Vec3(0.4f, 0, 2), new Vec3(-0.3f, 0, 1), Door, Forward, beyond));
            Assert.False(DoorRules.IsOnWayThrough(new Vec3(0, 0, 1), heading, Door, Forward, beyond));   // 2 m away: not yet
            Assert.False(DoorRules.IsOnWayThrough(new Vec3(0, 0, 2), Vec3.Zero, Door, Forward, beyond)); // standing still
        }

        [Fact]
        public void A_door_he_only_walks_past_is_not_on_his_way()
        {
            // along the outside wall of a hut, past its door, to a goal on the same side
            Assert.False(DoorRules.IsOnWayThrough(new Vec3(-1, 0, 3.75f), new Vec3(1, 0, 0), Door, Forward, new Vec3(10, 0, 4)));
            // heading through, but the goal is on his own side
            Assert.False(DoorRules.IsOnWayThrough(new Vec3(0, 0, 2), new Vec3(0, 0, 1), Door, Forward, new Vec3(5, 0, 0)));
            // off to the side of the doorway
            Assert.False(DoorRules.IsOnWayThrough(new Vec3(-1.2f, 0, 2.8f), new Vec3(0, 0, 1), Door, Forward, new Vec3(0, 0, 10)));
            // walking away from it
            Assert.False(DoorRules.IsOnWayThrough(new Vec3(0, 0, 2), new Vec3(0, 0, -1), Door, Forward, new Vec3(0, 0, 10)));
        }

        [Fact]
        public void A_door_leads_towards_his_goal_only_when_its_far_side_gets_him_closer()
        {
            Assert.True(DoorRules.LeadsTowards(gapFromHere: 3.65f, gapFromBeyond: 1f, reachesFromBeyond: false));     // the chest inside the hut
            Assert.False(DoorRules.LeadsTowards(gapFromHere: 6f, gapFromBeyond: 5.5f, reachesFromBeyond: false));     // a tree in the water: the hut doesn't help
            Assert.False(DoorRules.LeadsTowards(gapFromHere: 6f, gapFromBeyond: float.MaxValue, reachesFromBeyond: false));
        }

        [Fact]
        public void A_door_leads_to_a_chest_against_the_back_wall_though_he_is_close_to_it_outside()
        {
            // outside the back wall he is 1.35 m from the chest; inside, the way goes all the way to it
            Assert.True(DoorRules.LeadsTowards(gapFromHere: 1.35f, gapFromBeyond: 0.9f, reachesFromBeyond: true));
        }

        [Fact]
        public void He_goes_through_from_the_side_he_can_reach()
        {
            Assert.Equal(1, DoorRules.SideToUse(frontReachable: true, backReachable: false));
            Assert.Equal(-1, DoorRules.SideToUse(frontReachable: false, backReachable: true));
            Assert.Equal(0, DoorRules.SideToUse(frontReachable: true, backReachable: true));     // the door divides nothing
            Assert.Equal(0, DoorRules.SideToUse(frontReachable: false, backReachable: false));   // he is somewhere else
        }

        [Fact]
        public void A_door_that_only_did_not_fit_from_where_he_stood_is_tried_again_soon()
        {
            Assert.Equal(30f, DoorRules.RetryAfter(failedPassing: true));
            Assert.True(DoorRules.RetryAfter(failedPassing: false) <= 5f);
        }

        [Fact]
        public void A_path_ending_short_of_a_chest_behind_a_door_tries_the_door_before_counting_as_arrived()
        {
            Assert.True(DoorRules.TryDoorFirst(pathEnded: true, distance: 3.65f, stopDistance: 2.5f));   // the Lumberjack's Hut chest
            Assert.True(DoorRules.TryDoorFirst(pathEnded: true, distance: 2.65f, stopDistance: 2.5f));   // the Builder's Hut chest, just outside the wall
            Assert.False(DoorRules.TryDoorFirst(pathEnded: true, distance: 2.4f, stopDistance: 2.5f));
            Assert.False(DoorRules.TryDoorFirst(pathEnded: false, distance: 8f, stopDistance: 2.5f));
        }

        [Fact]
        public void A_way_ending_well_short_of_the_goal_looks_for_a_door_at_once()
        {
            // Seen: a Longhouse household off to work behind its back wall all walked into the back corner (where their
            // ways ended) before thinking of the door, and crowded each other in there.
            Assert.True(DoorRules.LookForDoorNow(wayEndGap: 57f));
            Assert.True(DoorRules.LookForDoorNow(wayEndGap: DoorRules.EarlyLookGap));
            Assert.True(DoorRules.LookForDoorNow(wayEndGap: float.MaxValue));   // no way at all
            Assert.False(DoorRules.LookForDoorNow(wayEndGap: 0.6f));            // the near side of a tree trunk he walks to
        }

        [Fact]
        public void Only_a_settler_or_player_walking_through_the_doorway_is_a_turn_to_wait_out()
        {
            // Seen in review: a monster moving in the doorway kept a settler waiting there for good.
            Assert.True(DoorRules.TakesATurn(person: true, settler: false, hostile: false, speed: 1.2f));
            Assert.False(DoorRules.TakesATurn(person: true, settler: false, hostile: false, speed: DoorRules.MovingSpeed));   // a player standing there
            Assert.False(DoorRules.TakesATurn(person: false, settler: false, hostile: false, speed: 2f));                     // a wolf, a boar
            Assert.False(DoorRules.TakesATurn(person: true, settler: false, hostile: true, speed: 2f));
        }

        [Fact]
        public void A_settler_going_through_the_doorway_gets_his_turn_even_standing_still()
        {
            // Seen (self-test, 2026-10-15): a settler stepping out stood in the doorway waiting for the one outside to clear
            // his landing, and the one outside, waiting for him to move, gave up after 12 s. A settler going through is
            // waiting his own turn: the one outside steps aside (the whole passing still has its limit). One just standing
            // there isn't (review: he held a household outside for a minute).
            Assert.True(DoorRules.TakesATurn(person: true, settler: true, hostile: false, speed: 0f));
            Assert.False(DoorRules.TakesATurn(person: true, settler: false, hostile: false, speed: 0f));
        }

        [Fact]
        public void Waiting_turns_at_a_door_has_an_end()
        {
            // Turns (others going through first) start the passing's clock again, but a whole passing has a limit too.
            Assert.False(DoorRules.PassingOver(sinceStart: 50, sinceProgress: 10));
            Assert.True(DoorRules.PassingOver(sinceStart: 50, sinceProgress: DoorRules.PassSeconds + 1));
            Assert.True(DoorRules.PassingOver(sinceStart: DoorRules.MaxPassSeconds + 1, sinceProgress: 0));
        }

        [Fact]
        public void A_monster_in_the_doorway_is_never_waited_for()
        {
            Assert.True(DoorRules.GiveUpAtOnce(hostile: true));
            Assert.False(DoorRules.GiveUpAtOnce(hostile: false));
        }

        [Fact]
        public void Looking_early_only_a_door_near_him_counts()
        {
            // Seen: a forager 62 m from his Longhouse set off for its door at once, and the door's time ran out on the way.
            Assert.True(DoorRules.NearEnoughToLookEarly(doorFromHim: 9f));    // across a Longhouse, from the far bed
            Assert.True(DoorRules.NearEnoughToLookEarly(doorFromHim: DoorRules.EarlyLookReach));
            Assert.False(DoorRules.NearEnoughToLookEarly(doorFromHim: 62f));
        }

        [Fact]
        public void He_opens_the_door_only_once_he_stands_at_it()
        {
            Assert.True(DoorRules.ReadyToOpen(1.6f));
            Assert.False(DoorRules.ReadyToOpen(3f));   // a path that ended round the corner
        }

        // Seen: the player's own hut has a stair in front of its gate; the spot 1.2 m out was on the stair, out of reach,
        // so no settler used the gate. Further out, at the stair's foot, he can stand and open it from there.
        [Fact]
        public void From_a_spot_further_out_he_opens_it_from_there()
        {
            Assert.Equal(new[] { DoorRules.StandOff, 2f, 2.8f }, DoorRules.StandOffs);
            Assert.True(DoorRules.ReadyToOpen(3.4f, 2.8f));
            Assert.False(DoorRules.ReadyToOpen(3.4f, DoorRules.StandOff));
        }

        [Fact]
        public void Someone_on_the_floor_of_the_doorway_or_waiting_by_it_is_in_the_doorway()
        {
            Assert.True(DoorRules.InDoorway(Door, new Vec3(0.2f, -1f, 3f)));    // feet on the floor, 1 m below the door's middle
            Assert.True(DoorRules.InDoorway(Door, new Vec3(0f, -1f, 1.9f)));    // waiting in front of it
            Assert.False(DoorRules.InDoorway(Door, new Vec3(0f, -1f, 5f)));
            Assert.False(DoorRules.InDoorway(Door, new Vec3(0f, 5f, 3f)));      // on the floor above
        }

        // The 1.4.1 release run (2026-10-20): at night a settler came round a hut whose door the self-test had blocked,
        // opened the door from 3.5 m along its wall, stepped up onto the low eave there and "went through" past the door's
        // line, then opened it again from the roof, 4 m above the doorway, and "went through" onto the roof. Each pass
        // started the 45 s give-up clock again, and the hut wasn't passed over before morning. A door is opened in front of
        // its doorway, at its height, and only a step through the doorway counts as going through.
        // The wood door of the tests: its doorway's middle at (0, 3) on a floor at y -1, 2 m tall.
        private static readonly Doorway WoodDoor = new Doorway(new Vec3(0, -1, 3), Forward, floorY: -1f, height: 2f);

        [Fact]
        public void He_opens_a_door_only_in_front_of_its_doorway_at_its_height()
        {
            Assert.True(DoorRules.ReadyToOpen(WoodDoor, new Vec3(0f, -1f, 3f - 2.8f), 2.8f));      // on the floor, at his spot
            Assert.True(DoorRules.ReadyToOpen(WoodDoor, new Vec3(1.5f, -1f, 3f - 1.5f), 2.8f));    // coming from an angle
            Assert.False(DoorRules.ReadyToOpen(WoodDoor, new Vec3(3.4f, -1f, 2.9f), 2.8f));        // along its wall
            Assert.False(DoorRules.ReadyToOpen(WoodDoor, new Vec3(0.6f, 3.3f, 3f - 1f), 2.8f));    // on the roof above it
            Assert.False(DoorRules.ReadyToOpen(WoodDoor, new Vec3(0f, -1f, 3f - 4f), 2.8f));       // too far out
        }

        [Fact]
        public void Only_a_step_through_the_doorway_counts_as_going_through()
        {
            Assert.Equal(DoorCrossing.Through, DoorRules.Crossing(WoodDoor, startSide: -1, settler: new Vec3(0.3f, -1f, 4.5f), onGround: true));
            Assert.Equal(DoorCrossing.Through, DoorRules.Crossing(WoodDoor, startSide: -1, settler: new Vec3(1.2f, -1f, 4.4f), onGround: true));   // drifted a little
            Assert.Equal(DoorCrossing.NotYet, DoorRules.Crossing(WoodDoor, startSide: -1, settler: new Vec3(0f, -1f, 2f), onGround: true));        // still in front
            Assert.Equal(DoorCrossing.NotYet, DoorRules.Crossing(WoodDoor, startSide: -1, settler: new Vec3(0f, -1f, 3.5f), onGround: true));      // in the doorway
            Assert.Equal(DoorCrossing.Round, DoorRules.Crossing(WoodDoor, startSide: -1, settler: new Vec3(3.2f, 0.2f, 3.3f), onGround: true));    // past its line, beside it
            Assert.Equal(DoorCrossing.Round, DoorRules.Crossing(WoodDoor, startSide: -1, settler: new Vec3(0f, 3.2f, 4.5f), onGround: true));      // on the roof beyond it
        }

        // The review of the door rules (1.4.2): a door's pivot isn't always 1 m above its floor (a palisade gate's is at its
        // floor, the hexagonal door's 2 m up), so its height is judged from its doorway: its floor and how tall it is.
        [Fact]
        public void A_tall_gate_is_opened_from_ground_that_rises_to_it_a_low_door_only_a_step_above_its_floor()
        {
            var gate = new Doorway(new Vec3(0, 0, 3), Forward, floorY: 0f, height: 4f);   // a palisade gate sunk into a slope
            Assert.True(DoorRules.ReadyToOpen(gate, new Vec3(0f, 2.2f, 3f - 1.2f), 1.2f));   // its height less a settler's
            Assert.False(DoorRules.ReadyToOpen(gate, new Vec3(0f, 2.4f, 3f - 1.2f), 1.2f));
            Assert.True(DoorRules.ReadyToOpen(WoodDoor, new Vec3(0f, -1f + DoorRules.AboveFloor, 3f - 1.2f), 1.2f));   // a step up
            Assert.False(DoorRules.ReadyToOpen(WoodDoor, new Vec3(0f, -1f + DoorRules.AboveFloor + 0.1f, 3f - 1.2f), 1.2f));
        }

        [Fact]
        public void A_door_is_opened_from_below_its_floor_at_a_stairs_foot_or_a_step_down()
        {
            Assert.True(DoorRules.ReadyToOpen(WoodDoor, new Vec3(0f, -1f - 2.5f, 3f - 2.8f), 2.8f));   // the stair's foot, 2.8 m out
            Assert.True(DoorRules.ReadyToOpen(WoodDoor, new Vec3(0f, -1f - DoorRules.BelowFloor, 3f - 2.8f), 2.8f));
            Assert.False(DoorRules.ReadyToOpen(WoodDoor, new Vec3(0f, -1f - DoorRules.BelowFloor - 0.1f, 3f - 2.8f), 2.8f));
            var hexagonal = new Doorway(new Vec3(0, 0, 3), Forward, floorY: 0f, height: 4.3f);
            Assert.True(DoorRules.ReadyToOpen(hexagonal, new Vec3(0f, -0.5f, 3f - 1.2f), 1.2f));   // a step down in front
        }

        [Fact]
        public void The_sideways_limits_hold_to_the_edge()
        {
            Assert.True(DoorRules.ReadyToOpen(WoodDoor, new Vec3(DoorRules.OpenReach, -1f, 3f - 0.5f), 2.8f));
            Assert.False(DoorRules.ReadyToOpen(WoodDoor, new Vec3(DoorRules.OpenReach + 0.05f, -1f, 3f - 0.5f), 2.8f));
            Assert.Equal(DoorCrossing.Through, DoorRules.Crossing(WoodDoor, -1, new Vec3(DoorRules.ThroughSideways, -1f, 4.5f), onGround: true));
            Assert.Equal(DoorCrossing.Round, DoorRules.Crossing(WoodDoor, -1, new Vec3(DoorRules.ThroughSideways + 0.05f, -1f, 4.5f), onGround: true));
        }

        [Fact]
        public void In_the_air_on_a_hop_he_isnt_judged_round_until_he_lands()
        {
            var hopping = new Vec3(0f, -1f + DoorRules.AboveFloor + 0.3f, 4.3f);   // up on a hop, 1.3 m past the door
            Assert.Equal(DoorCrossing.NotYet, DoorRules.Crossing(WoodDoor, -1, hopping, onGround: false));
            Assert.Equal(DoorCrossing.Round, DoorRules.Crossing(WoodDoor, -1, hopping, onGround: true));
        }

        [Fact]
        public void A_door_another_settler_is_going_through_stays_open()
        {
            Assert.False(DoorRules.ShouldClose(wentThrough: true, distance: 10f, doorwayClear: true, othersPassing: true));
        }

        [Fact]
        public void No_door_leading_there_is_logged_only_when_a_door_by_the_goal_was_weighed_or_the_settler_is_under_a_roof()
        {
            // 1.4.1's colony watch: 119 of 123 such lines were a lumberjack's logs just off the walking map, out in the
            // open, with only the door of their own hut nearby.
            Assert.False(DoorRules.ExplainNoDoor(doorNearGoal: false, underRoof: false));
            Assert.True(DoorRules.ExplainNoDoor(doorNearGoal: true, underRoof: false));
            Assert.True(DoorRules.ExplainNoDoor(doorNearGoal: false, underRoof: true));
        }

        [Fact]
        public void A_door_that_did_not_move_is_asked_again_after_a_second_not_every_moment()
        {
            Assert.False(DoorRules.AskAgain(sinceAsked: 0.3f));
            Assert.True(DoorRules.AskAgain(sinceAsked: 1.2f));
        }
    }
}
