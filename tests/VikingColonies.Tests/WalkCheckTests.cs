using System;
using System.Collections.Generic;
using System.Linq;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>vc_walktest's measures (1.5.1, "I just want it to feel smooth"): the fair way round a hut, and a walk's own.</summary>
    public class WalkCheckTests
    {
        // A hut's walls x -3..3, z -4..4.
        private static readonly Footprint Walls = new Footprint(new Vec3(0, 0, 0), Quat.Identity, 3f, 4f);

        [Fact]
        public void With_nothing_in_the_way_the_shortest_way_is_straight()
        {
            Assert.Equal(20f, WalkCheck.ShortestAround(new Vec3(-10, 0, 10), new Vec3(10, 0, 10), Walls), 2);
        }

        [Fact]
        public void Through_the_hut_the_shortest_way_goes_round_its_corners()
        {
            // (0, -10) to (3, -4) to (3, 4) to (0, 10): 6.708 + 8 + 6.708.
            Assert.Equal(21.416f, WalkCheck.ShortestAround(new Vec3(0, 0, -10), new Vec3(0, 0, 10), Walls), 2);
        }

        [Fact]
        public void A_turned_hut_is_gone_round_as_it_stands()
        {
            // Turned a quarter: 4 m each side in x, 3 in z. (0, -10) to (4, -3) to (4, 3) to (0, 10): 8.062 + 6 + 8.062.
            var turned = new Footprint(new Vec3(0, 0, 0), Quat.Yaw(90f), 3f, 4f);
            Assert.Equal(22.125f, WalkCheck.ShortestAround(new Vec3(0, 0, -10), new Vec3(0, 0, 10), turned), 2);
        }

        [Fact]
        public void To_a_door_in_the_far_wall_the_way_goes_round_to_it()
        {
            // From behind to the door in the north wall: (0, -10) to (3, -4) to (3, 4) to (0, 4): 6.708 + 8 + 3.
            Assert.Equal(17.708f, WalkCheck.ShortestAround(new Vec3(0, 0, -10), new Vec3(0, 0, 4), Walls), 2);
        }

        [Fact]
        public void A_door_set_a_little_into_the_wall_counts_from_the_wall()
        {
            // A door frame 0.2 m inside the outline: measured to the wall's line, and the 0.2 m added.
            Assert.Equal(17.908f, WalkCheck.ShortestAround(new Vec3(0, 0, -10), new Vec3(0, 0, 3.8f), Walls), 2);
        }

        [Fact]
        public void A_walk_counts_the_metres_walked_and_the_longest_stand_away_from_the_door()
        {
            var trace = new WalkTrace();
            float t = 0f;
            for (int i = 0; i <= 8; i++, t += 0.25f) trace.Add(t, new Vec3(i * 1f, 0, 0), nearDoor: false);   // 8 m in 2 s
            for (int i = 0; i < 12; i++, t += 0.25f) trace.Add(t, new Vec3(8f, 0, 0), nearDoor: false);       // 3 s behind the hut
            for (int i = 1; i <= 4; i++, t += 0.25f) trace.Add(t, new Vec3(8f, 0, i * 1f), nearDoor: false);  // on 4 m
            Assert.Equal(12f, trace.Walked, 2);
            Assert.Equal(3f, trace.LongestStop, 1);
        }

        [Fact]
        public void Standing_at_the_door_to_open_it_isnt_a_stop()
        {
            var trace = new WalkTrace();
            float t = 0f;
            for (int i = 0; i < 12; i++, t += 0.25f) trace.Add(t, new Vec3(0, 0, 0), nearDoor: true);
            Assert.Equal(0f, trace.LongestStop, 2);
        }

        [Fact]
        public void A_walk_is_smooth_within_a_quarter_and_3_m_of_the_shortest_way_and_no_long_stop()
        {
            Assert.True(WalkCheck.Smooth(28f, 20f, 0.5f));
            Assert.False(WalkCheck.Smooth(40f, 20f, 0.5f));   // round the back and back again
            Assert.False(WalkCheck.Smooth(22f, 20f, 3f));     // stood behind the hut, then went round
        }

        [Fact]
        public void The_test_spot_is_levelled_back_everywhere_within_the_levellings_reach()
        {
            // 18 m each way round (20, -30), levellings of 2.6 m: every point of the square within one's reach, none far out.
            List<List<GroundPoint>> batches = WalkCheck.LevelBatches(new GroundPoint(20f, -30f), 18f, 2.6f);
            List<GroundPoint> spots = batches.SelectMany(b => b).ToList();
            for (float x = 2f; x <= 38f; x += 0.5f)
                for (float z = -48f; z <= -12f; z += 0.5f)
                    Assert.Contains(spots, s => (s.X - x) * (s.X - x) + (s.Z - z) * (s.Z - z) <= 2.6f * 2.6f);
            Assert.All(spots, s => Assert.True(Math.Abs(s.X - 20f) <= 18f && Math.Abs(s.Z + 30f) <= 18f));
            Assert.True(spots.Count <= 16 * 16, $"{spots.Count} levellings");
        }

        [Fact]
        public void No_two_levellings_of_one_batch_reach_the_same_ground()
        {
            // A levelling reads the ground's height as it was before the others of its frame took effect (the 2026-10-24
            // probe: 39.58 to 40.20 m after a pass of 361 at once): those that go together never overlap.
            foreach (List<GroundPoint> batch in WalkCheck.LevelBatches(new GroundPoint(20f, -30f), 18f, 2.6f))
                for (int i = 0; i < batch.Count; i++)
                    for (int k = i + 1; k < batch.Count; k++)
                    {
                        float dx = batch[i].X - batch[k].X, dz = batch[i].Z - batch[k].Z;
                        Assert.True(dx * dx + dz * dz > 2f * 2.6f * 2f * 2.6f, $"({batch[i].X}, {batch[i].Z}) and ({batch[k].X}, {batch[k].Z})");
                    }
        }
    }
}
