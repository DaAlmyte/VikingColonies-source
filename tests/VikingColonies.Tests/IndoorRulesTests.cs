using System.Collections.Generic;
using System.Linq;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    public class IndoorRulesTests
    {
        [Fact]
        public void An_attack_comes_first_then_a_raid_then_night()
        {
            Assert.Equal(IndoorReason.Attack, IndoorRules.Reason(true, true, true, true, true));
            Assert.Equal(IndoorReason.Raid, IndoorRules.Reason(false, true, true, true, true));
            Assert.Equal(IndoorReason.Night, IndoorRules.Reason(false, false, true, true, true));
            Assert.Equal(IndoorReason.None, IndoorRules.Reason(false, false, true, false, true));
        }

        [Fact]
        public void Reason_ignores_night_and_raids_when_their_settings_are_off()
        {
            Assert.Equal(IndoorReason.None, IndoorRules.Reason(false, true, false, true, false));
            Assert.Equal(IndoorReason.Attack, IndoorRules.Reason(true, false, false, false, false));   // an attack always counts
        }

        [Fact]
        public void A_raid_covers_spots_within_its_range_on_the_ground()
        {
            var raid = new GroundPoint(100f, 100f);
            Assert.True(IndoorRules.RaidCovers(raid, 96f, new GroundPoint(150f, 100f), 30f));
            Assert.False(IndoorRules.RaidCovers(raid, 96f, new GroundPoint(300f, 100f), 30f));
            Assert.False(IndoorRules.RaidCovers(raid, 96f, new GroundPoint(110f, 100f), 5000f));   // a dungeon high above
        }

        private static readonly GroundPoint Me = new GroundPoint(0f, 0f);

        private static List<Refuge> Huts() => new List<Refuge>
        {
            new Refuge("near", new GroundPoint(5f, 0f), true),
            new Refuge("own", new GroundPoint(20f, 0f), true),
            new Refuge("far", new GroundPoint(40f, 0f), true),
        };

        [Fact]
        public void ChooseRefuge_takes_his_own_hut_when_usable()
        {
            Assert.Equal(1, IndoorRules.ChooseRefuge(Huts(), "own", Me, null));
        }

        [Fact]
        public void ChooseRefuge_takes_the_nearest_usable_hut_without_his_own()
        {
            Assert.Equal(0, IndoorRules.ChooseRefuge(Huts(), "", Me, null));
            Assert.Equal(0, IndoorRules.ChooseRefuge(Huts(), "gone", Me, null));
        }

        [Fact]
        public void ChooseRefuge_skips_a_hut_that_is_not_usable()
        {
            var huts = Huts();
            huts[1] = new Refuge("own", new GroundPoint(20f, 0f), false);
            Assert.Equal(0, IndoorRules.ChooseRefuge(huts, "own", Me, null));
        }

        [Fact]
        public void ChooseRefuge_gives_none_when_no_hut_is_usable()
        {
            var huts = Huts().Select(h => new Refuge(h.Id, h.Chest, false)).ToList();
            Assert.Equal(-1, IndoorRules.ChooseRefuge(huts, "own", Me, null));
            Assert.Equal(-1, IndoorRules.ChooseRefuge(new List<Refuge>(), "own", Me, null));
        }

        [Fact]
        public void In_an_attack_a_hut_the_monster_is_nearer_to_doesnt_count()
        {
            var monster = new GroundPoint(18f, 0f);   // by his own hut
            Assert.Equal(0, IndoorRules.ChooseRefuge(Huts(), "own", Me, monster));
        }

        [Fact]
        public void ChooseRefuge_passes_over_a_hut_he_was_told_to_skip()
        {
            Assert.Equal(0, IndoorRules.ChooseRefuge(Huts(), "own", Me, null, id => id == "own"));
            Assert.Equal(-1, IndoorRules.ChooseRefuge(Huts(), "own", Me, null, id => true));
        }

        // Seen in the first night run: with a Greyling moving about, Ragnar ran between two huts five times.
        [Fact]
        public void In_an_attack_he_keeps_to_the_hut_he_chose_while_it_stays_safe()
        {
            Assert.Equal(0, IndoorRules.ChooseRefuge(Huts(), "own", Me, new GroundPoint(-30f, 0f), null, current: "near"));
        }

        [Fact]
        public void He_leaves_the_hut_he_chose_once_the_monster_is_nearer_to_it()
        {
            var huts = new List<Refuge>
            {
                new Refuge("near", new GroundPoint(5f, 0f), true),
                new Refuge("own", new GroundPoint(-20f, 0f), true),   // behind him
            };
            Assert.Equal(1, IndoorRules.ChooseRefuge(huts, "own", Me, new GroundPoint(6f, 0f), null, current: "near"));
        }

        [Fact]
        public void A_settler_stands_on_the_same_spot_each_time_and_settlers_spread_out()
        {
            var chest = new GroundPoint(10f, 10f);
            var door = new GroundPoint(10f, 13f);
            GroundPoint a = IndoorRules.Spot(chest, door, "settler-a");
            Assert.Equal(a.X, IndoorRules.Spot(chest, door, "settler-a").X);
            Assert.Equal(a.Z, IndoorRules.Spot(chest, door, "settler-a").Z);
            Assert.Equal(IndoorRules.SpotDistance, ColonyRules.Distance(chest, a), 3);
            int spots = Enumerable.Range(0, 12).Select(i => IndoorRules.Spot(chest, door, "settler-" + i))
                .Select(p => (System.Math.Round(p.X, 2), System.Math.Round(p.Z, 2))).Distinct().Count();
            Assert.True(spots >= 3, $"only {spots} different spots for 12 settlers");
        }

        // In a small hut some settlers' spots fell outside its walls or inside its workbench (the level-1 Watchtower and
        // Lumberjack's Hut, 1.5.1's walking check, 2026-10-24): a spot the hut can't take (the game looks) is passed over.
        [Fact]
        public void A_spot_outside_the_walls_or_in_the_furniture_is_never_chosen()
        {
            var chest = new GroundPoint(0f, 0f);
            var door = new GroundPoint(1f, 2f);
            var inward = new GroundPoint(0f, -1f);
            System.Func<GroundPoint, bool> takes = p => p.X < 0.2f;   // the east half: beyond a wall, or the workbench
            for (int i = 0; i < 60; i++)
            {
                GroundPoint spot = IndoorRules.Spot(chest, door, inward, "settler-" + i, takes);
                Assert.True(takes(spot), $"settler-{i}'s spot ({spot.X:0.00}, {spot.Z:0.00}) is one the hut can't take");
                Assert.Equal(spot.X, IndoorRules.Spot(chest, door, inward, "settler-" + i, takes).X);   // the same each time
            }
            Assert.True(Enumerable.Range(0, 30).Select(i => IndoorRules.Spot(chest, door, inward, "settler-" + i, takes))
                .Select(p => System.Math.Round(p.X, 2)).Distinct().Count() >= 2, "settlers still spread over the spots left");
        }

        [Fact]
        public void With_no_spot_the_hut_can_take_it_is_the_chest_side_by_the_door_way()
        {
            var chest = new GroundPoint(0f, 0f);
            var door = new GroundPoint(0f, 2f);
            var inward = new GroundPoint(0f, -1f);
            GroundPoint spot = IndoorRules.Spot(chest, door, inward, "anyone", p => System.Math.Abs(p.X) < 0.01f);   // only the line to the door
            // Between the chest and the door, a little way off the chest: inside whatever the walls are.
            Assert.Equal(0f, spot.X, 3);
            Assert.True(spot.Z > 0.3f && spot.Z < 1.5f, $"the spot at {spot.Z:0.00} m");
            // Not even that: the spot as before 1.5.1 (its review: better a spot than none).
            GroundPoint before = IndoorRules.Spot(chest, door, inward, "anyone");
            GroundPoint none = IndoorRules.Spot(chest, door, inward, "anyone", p => false);
            Assert.Equal(before.X, none.X);
            Assert.Equal(before.Z, none.Z);
        }

        [Fact]
        public void When_the_hut_takes_only_spots_in_the_way_in_it_is_one_of_those_not_one_it_cannot_take()
        {
            var chest = new GroundPoint(0f, 0f);
            var door = new GroundPoint(0f, 1.6f);   // a door this near: every spot is near the way in
            var inward = new GroundPoint(0f, -1f);
            System.Func<GroundPoint, bool> takes = p => p.X > 0.1f;
            for (int i = 0; i < 20; i++)
                Assert.True(takes(IndoorRules.Spot(chest, door, inward, "settler-" + i, takes)));
        }

        [Fact]
        public void A_hut_that_takes_every_spot_gives_the_spots_as_before()
        {
            var chest = new GroundPoint(0f, 0f);
            var door = new GroundPoint(1f, 2f);
            var inward = new GroundPoint(0f, -1f);
            for (int i = 0; i < 20; i++)
            {
                GroundPoint before = IndoorRules.Spot(chest, door, inward, "settler-" + i);
                GroundPoint now = IndoorRules.Spot(chest, door, inward, "settler-" + i, p => true);
                Assert.Equal(before.X, now.X);
                Assert.Equal(before.Z, now.Z);
            }
        }

        // In the shipped Builder's Hut the door is 2.2 m from the chest: the spot straight towards it was 1 m inside the
        // door, where the next settler steps through, and two settlers wedged each other at the door.
        [Fact]
        public void No_spot_is_in_the_way_in_through_the_door()
        {
            var chest = new GroundPoint(0f, 0f);
            var door = new GroundPoint(1f, 2f);
            var inward = new GroundPoint(0f, -1f);   // the door faces north; the hut is south of it
            for (int i = 0; i < 60; i++)
            {
                GroundPoint spot = IndoorRules.Spot(chest, door, inward, "settler-" + i);
                float along = System.Math.Max(0f, System.Math.Min(IndoorRules.LaneLength, door.Z - spot.Z));
                float fromLane = ColonyRules.Distance(spot, new GroundPoint(door.X, door.Z - along));
                Assert.True(fromLane >= IndoorRules.LaneClearance, $"settler-{i}'s spot ({spot.X:0.00}, {spot.Z:0.00}) is {fromLane:0.00} m from the way in");
                Assert.Equal(IndoorRules.SpotDistance, ColonyRules.Distance(chest, spot), 3);
            }
        }

        // Review: in the 4x4 Builder's Hut only two spots are clear of the way in, and 5 of 6 settlers got the same one.
        [Fact]
        public void Settlers_share_out_the_spots_clear_of_the_way_in()
        {
            var chest = new GroundPoint(0f, 0f);
            var door = new GroundPoint(1f, 2f);
            var inward = new GroundPoint(0f, -1f);
            var used = Enumerable.Range(0, 12).Select(i => IndoorRules.Spot(chest, door, inward, "settler-" + i))
                .GroupBy(p => (System.Math.Round(p.X, 2), System.Math.Round(p.Z, 2))).Select(g => g.Count()).ToList();
            Assert.True(used.Count >= 2, $"only {used.Count} spots used");
            Assert.True(used.Max() <= 8, $"{used.Max()} of 12 settlers on one spot");
        }

        // Review: someone standing by the door or where the next one's step lands makes that one wait, and give up.
        [Fact]
        public void No_spot_is_by_the_door_or_where_a_step_through_lands()
        {
            var chest = new GroundPoint(0f, 0f);
            var door = new GroundPoint(0f, 3f);
            var inward = new GroundPoint(0f, -1f);
            var landing = new GroundPoint(0f, 3f - IndoorRules.StepLanding);
            for (int i = 0; i < 60; i++)
            {
                GroundPoint spot = IndoorRules.Spot(chest, door, inward, "settler-" + i);
                Assert.True(ColonyRules.Distance(spot, door) >= IndoorRules.DoorClearance, $"settler-{i}'s spot is {ColonyRules.Distance(spot, door):0.00} m from the door");
                Assert.True(ColonyRules.Distance(spot, landing) >= IndoorRules.LandingClearance, $"settler-{i}'s spot is {ColonyRules.Distance(spot, landing):0.00} m from the landing");
            }
        }

        [Fact]
        public void In_a_hut_too_small_for_a_clear_spot_he_takes_the_one_least_in_the_way()
        {
            var chest = new GroundPoint(0f, 0f);
            var door = new GroundPoint(0f, 1.6f);
            var inward = new GroundPoint(0f, -1f);
            var landing = new GroundPoint(0f, 0.1f);
            GroundPoint spot = IndoorRules.Spot(chest, door, inward, "anyone");
            for (int i = 0; i < 20; i++)
                Assert.True(ColonyRules.Distance(IndoorRules.Spot(chest, door, inward, "settler-" + i), landing) >= ColonyRules.Distance(spot, landing) - 0.001f);
        }

        [Fact]
        public void Settlers_still_spread_out_when_the_door_is_far_from_the_chest()
        {
            var chest = new GroundPoint(0f, 0f);
            var door = new GroundPoint(0f, 5.3f);
            var inward = new GroundPoint(0f, -1f);
            int spots = Enumerable.Range(0, 12).Select(i => IndoorRules.Spot(chest, door, inward, "settler-" + i))
                .Select(p => (System.Math.Round(p.X, 2), System.Math.Round(p.Z, 2))).Distinct().Count();
            Assert.True(spots >= 4, $"only {spots} different spots for 12 settlers");
        }

        // A chest may stand against the hut's back wall (the player's own lumberjack hut has it 0.7 m from it): a spot
        // behind the chest is outside, and he "slept" there.
        [Theory]
        [InlineData(0f, 5.3f)]
        [InlineData(-4f, -1f)]
        public void Every_spot_is_on_the_door_side_of_the_chest(float doorX, float doorZ)
        {
            var chest = new GroundPoint(10f, 10f);
            var door = new GroundPoint(10f + doorX, 10f + doorZ);
            float length = (float)System.Math.Sqrt(doorX * doorX + doorZ * doorZ);
            for (int i = 0; i < 60; i++)
            {
                GroundPoint spot = IndoorRules.Spot(chest, door, "settler-" + i);
                Assert.Equal(IndoorRules.SpotDistance, ColonyRules.Distance(chest, spot), 3);
                float towardsDoor = ((spot.X - chest.X) * doorX + (spot.Z - chest.Z) * doorZ) / length;
                Assert.True(towardsDoor > 0.3f, $"settler-{i}'s spot ({spot.X:0.00}, {spot.Z:0.00}) is only {towardsDoor:0.00} m towards the door");
            }
        }

        // ---- Inside his refuge, he stays: seen, two settlers each standing on the other's sleeping spot in their own
        // Longhouse gave it up as "couldn't get into" and set off for the Builder's Hut, then got boxed into a corner ----

        // (How he gets on inside, settling and giving up: RefugeProgressTests.)

        [Fact]
        public void He_settles_only_well_inside_the_door()
        {
            // Past where the next one's step through lands, and where the door he opened closes behind him (2 m).
            Assert.True(IndoorRules.SettleDoorClearance > DoorRules.StepBeyond + 0.5f);
            Assert.False(IndoorRules.RoomToSettle(fromDoor: 1.6f));
            Assert.True(IndoorRules.RoomToSettle(fromDoor: IndoorRules.SettleDoorClearance));
        }

        [Fact]
        public void Within_the_walls_means_on_its_ground_with_no_wall_to_the_chest_and_out_of_the_doorway()
        {
            Assert.True(IndoorRules.WithinWalls(onItsGround: true, wallToChest: false, fromDoor: IndoorRules.DoorClearance));
            Assert.False(IndoorRules.WithinWalls(onItsGround: false, wallToChest: false, fromDoor: 5f));
            Assert.False(IndoorRules.WithinWalls(onItsGround: true, wallToChest: true, fromDoor: 5f));   // just outside a wall
            Assert.False(IndoorRules.WithinWalls(onItsGround: true, wallToChest: false, fromDoor: 1f));  // in the open doorway: he'd block it
        }

        // A level-1 hut 4 m deep (z -2..2), its door in the front wall at (0, 2), outward +z.
        private static readonly GroundPoint Door = new GroundPoint(0f, 2f), Outward = new GroundPoint(0f, 1f);

        [Fact]
        public void A_monster_outside_in_front_of_the_door_is_at_the_door()
        {
            // The player's 1.5.0 try: a Troll at the hut hit the forager inside, and the forager walked out to another hut.
            Assert.True(IndoorRules.AtTheDoor(new GroundPoint(0f, 3.5f), Door, Outward, monsterInside: false));
            Assert.True(IndoorRules.AtTheDoor(new GroundPoint(2f, 2.3f), Door, Outward, monsterInside: false));   // beside it, against the front wall
        }

        [Fact]
        public void A_monster_behind_a_small_hut_isnt_at_the_door()
        {
            // The review: behind the back wall of a 4 m deep hut a Troll stands 5.5 m from the door, under the old 6 m line,
            // and the settler stayed in while it hit them through the wall. The door, on the other side, is clear.
            Assert.False(IndoorRules.AtTheDoor(new GroundPoint(0f, -3.5f), Door, Outward, monsterInside: false));
            Assert.False(IndoorRules.AtTheDoor(new GroundPoint(2.8f, -1f), Door, Outward, monsterInside: false));   // by the side wall, behind the door's line
        }

        [Fact]
        public void A_monster_well_out_in_front_or_inside_isnt_at_the_door()
        {
            Assert.False(IndoorRules.AtTheDoor(new GroundPoint(0f, 2f + IndoorRules.DoorClear + 1f), Door, Outward, monsterInside: false));
            Assert.False(IndoorRules.AtTheDoor(new GroundPoint(0f, 1f), Door, Outward, monsterInside: true));
        }

        [Fact]
        public void One_at_the_door_keeps_them_in_while_they_are_well()
        {
            Assert.False(IndoorRules.LeavesAfterHit(monsterInside: false, atTheDoor: true, health01: 1f));
            Assert.False(IndoorRules.LeavesAfterHit(monsterInside: false, atTheDoor: true, health01: 0.6f));
        }

        [Fact]
        public void Hurt_to_half_they_make_a_run_for_it_past_one_at_the_door()
        {
            // The review: nothing ended the stay, and a monster camping at the door killed them where they stood.
            Assert.True(IndoorRules.LeavesAfterHit(monsterInside: false, atTheDoor: true, health01: StandUp.BreakOffHealth));
            Assert.True(IndoorRules.LeavesAfterHit(monsterInside: false, atTheDoor: true, health01: 0.3f));
        }

        [Fact]
        public void A_monster_in_the_hut_with_them_sends_them_on_as_before()
        {
            // Since 0.5, wherever it stands in there (the review: one that followed them in stops by the door).
            Assert.True(IndoorRules.LeavesAfterHit(monsterInside: true, atTheDoor: false, health01: 1f));
        }

        [Fact]
        public void One_elsewhere_outside_leaves_the_door_clear_and_they_go()
        {
            Assert.True(IndoorRules.LeavesAfterHit(monsterInside: false, atTheDoor: false, health01: 1f));
        }
    }
}
