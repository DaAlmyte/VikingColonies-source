using System.Collections.Generic;
using VikingColonies.Core;
using Xunit;

namespace VikingColonies.Tests
{
    /// <summary>
    /// vc_walkwatch (1.5.1, "I just want it to feel smooth"): every walk of a living colony judged as the player's eye would:
    /// a long way round, or standing still on the way.
    /// </summary>
    public class WalkWatchTests
    {
        /// <summary>
        /// Walks along the points at 4 m/s, sampled every 0.25 s, and arrives (the settler's own arrival) at the last one.
        /// The walk's end, if any.
        /// </summary>
        private static List<WalkEnd> Walk(WalkWatch watch, Vec3 goal, params Vec3[] points)
        {
            var ends = new List<WalkEnd>();
            float t = 0f;
            Vec3 at = points[0];
            void Feed(Vec3 p, bool walking, Vec3? arrived = null)
            {
                WalkEnd? end = watch.Sample(t, p, walking, goal, nearDoor: false, arrivedAt: arrived);
                if (end.HasValue) ends.Add(end.Value);
                t += 0.25f;
            }
            Feed(at, true);
            for (int i = 1; i < points.Length; i++)
            {
                Vec3 to = points[i];
                float length = Vec3.Distance(at, to);
                int steps = (int)System.Math.Ceiling(length / 1f);
                for (int s = 1; s <= steps; s++) Feed(at + (to - at) * (s / (float)steps), true);
                at = to;
            }
            Feed(at, false, goal);
            return ends;
        }

        [Fact]
        public void A_walk_the_straight_way_isnt_flagged()
        {
            List<WalkEnd> ends = Walk(new WalkWatch(), new Vec3(20, 0, 0), new Vec3(0, 0, 0), new Vec3(19, 0, 0));
            Assert.Single(ends);
            Assert.True(ends[0].Reached);
            Assert.Equal(19f, ends[0].Walked, 1);
            Assert.Equal(20f, ends[0].Straight, 1);
            Assert.False(ends[0].LongWay(ends[0].Straight, throughDoor: false));
            Assert.False(ends[0].StoodStill);
        }

        [Fact]
        public void Round_the_back_of_a_hut_and_back_is_a_long_way_round()
        {
            // To a chest 10 m off, by way of 12 m out behind the hut and back: 33 m walked.
            List<WalkEnd> ends = Walk(new WalkWatch(), new Vec3(10, 0, 0),
                new Vec3(0, 0, 0), new Vec3(0, 0, 12), new Vec3(10, 0, 12), new Vec3(10, 0, 1));
            Assert.True(ends[0].Reached);
            Assert.True(ends[0].LongWay(ends[0].Straight, throughDoor: false));
        }

        [Fact]
        public void Going_round_a_hut_to_its_door_isnt_a_long_way_round()
        {
            // 12 m straight, round the corner of a hut: about 15 m walked, well within half again and 8 m.
            List<WalkEnd> ends = Walk(new WalkWatch(), new Vec3(12, 0, 0),
                new Vec3(0, 0, 0), new Vec3(4, 0, 4), new Vec3(10, 0, 4), new Vec3(12, 0, 1));
            Assert.False(ends[0].LongWay(ends[0].Straight, throughDoor: false));
        }

        [Fact]
        public void Into_a_hut_from_behind_is_judged_against_the_way_through_its_door()
        {
            // The review: 7 m straight to a chest just inside a Longhouse's back wall, 35 m round by its front door: right,
            // not a long way round. Measured against the fair way (WalkCheck.FairWay), it isn't flagged; round the back
            // and round again would be.
            var walls = new Footprint(new Vec3(0, 0, 0), Quat.Identity, 4f, 10f);
            Vec3 from = new Vec3(0, 0, -16f), door = new Vec3(0, 0, 10f), chest = new Vec3(0, 0, -9f);
            float fair = WalkCheck.FairWay(from, chest, null, from, walls, door);
            Assert.True(fair > 30f);
            var end = new WalkEnd { Reached = true, Walked = 55f, Straight = 7f };
            Assert.False(end.LongWay(fair, throughDoor: true));
            Assert.True(end.LongWay(end.Straight, throughDoor: false));   // against the straight line it was flagged
            Assert.True(new WalkEnd { Reached = true, Walked = 80f }.LongWay(fair, throughDoor: true));
        }

        [Fact]
        public void An_arrival_straight_into_the_next_errand_is_still_judged()
        {
            // The review: the lumberjack drops their load the moment they arrive and sets off for a tree; the walk is never
            // seen standing. The settler's own arrival ends it, reached.
            var watch = new WalkWatch();
            Vec3 chest = new Vec3(10, 0, 0), tree = new Vec3(-20, 0, 0);
            float t = 0f;
            for (int i = 0; i <= 9; i++, t += 0.25f) watch.Sample(t, new Vec3(i, 0, 0), true, chest, false);
            WalkEnd? end = watch.Sample(t, new Vec3(9.5f, 0, 0), true, tree, false, arrivedAt: chest);
            Assert.True(end.HasValue && end.Value.Reached);
            Assert.Equal(chest.X, end.Value.Goal.X, 1);
        }

        [Fact]
        public void A_walk_ended_by_its_own_arrival_4_m_short_is_reached()
        {
            // The review: shelter and going home stop 4 m off.
            List<WalkEnd> ends = Walk(new WalkWatch(), new Vec3(20, 0, 0), new Vec3(0, 0, 0), new Vec3(16, 0, 0));
            Assert.True(ends[0].Reached);
        }

        [Fact]
        public void A_walk_that_stops_without_arriving_isnt_reached()
        {
            var watch = new WalkWatch();
            Vec3 goal = new Vec3(20, 0, 0);
            float t = 0f;
            for (int i = 0; i <= 19; i++, t += 0.25f) watch.Sample(t, new Vec3(i, 0, 0), true, goal, false);
            WalkEnd? end = watch.Sample(t, new Vec3(19, 0, 0), false, goal, false);
            Assert.True(end.HasValue && !end.Value.Reached);
        }

        [Fact]
        public void Standing_still_on_the_way_is_flagged_but_not_by_a_door()
        {
            var watch = new WalkWatch();
            Vec3 goal = new Vec3(20, 0, 0);
            float t = 0f;
            for (int i = 0; i <= 8; i++, t += 0.25f) watch.Sample(t, new Vec3(i, 0, 0), true, goal, false);
            for (int i = 0; i < 12; i++, t += 0.25f) watch.Sample(t, new Vec3(8, 0, 0), true, goal, false);   // 3 s still
            for (int i = 9; i <= 19; i++, t += 0.25f) watch.Sample(t, new Vec3(i, 0, 0), true, goal, false);
            WalkEnd? end = watch.Sample(t, new Vec3(19, 0, 0), false, goal, false, arrivedAt: goal);
            Assert.True(end.HasValue && end.Value.StoodStill);
            Assert.Equal(3f, end.Value.LongestStop, 1);

            var atDoor = new WalkWatch();
            t = 0f;
            for (int i = 0; i < 12; i++, t += 0.25f) atDoor.Sample(t, new Vec3(0, 0, 0), true, goal, nearDoor: true);
            for (int i = 1; i <= 19; i++, t += 0.25f) atDoor.Sample(t, new Vec3(i, 0, 0), true, goal, false);
            WalkEnd? doorEnd = atDoor.Sample(t, new Vec3(19, 0, 0), false, goal, false, arrivedAt: goal);
            Assert.True(doorEnd.HasValue && !doorEnd.Value.StoodStill);
        }

        [Fact]
        public void A_new_goal_ends_the_walk_before_it_unreached()
        {
            var watch = new WalkWatch();
            float t = 0f;
            for (int i = 0; i <= 6; i++, t += 0.25f) watch.Sample(t, new Vec3(0, 0, i * 4f), true, new Vec3(10, 0, 0), false);
            WalkEnd? end = watch.Sample(t, new Vec3(0, 0, 24), true, new Vec3(-30, 0, 0), false);
            Assert.True(end.HasValue);
            Assert.False(end.Value.Reached);
            Assert.False(end.Value.LongWay(end.Value.Straight, throughDoor: false));
        }

        [Fact]
        public void A_short_walk_isnt_judged_for_its_way()
        {
            // 3 m to a seat, round a table: never "a long way round".
            List<WalkEnd> ends = Walk(new WalkWatch(), new Vec3(3, 0, 0), new Vec3(0, 0, 0), new Vec3(0, 0, 4), new Vec3(3, 0, 4), new Vec3(3, 0, 0.5f));
            Assert.False(ends[0].LongWay(ends[0].Straight, throughDoor: false));
        }

        [Fact]
        public void A_gap_in_the_watching_drops_the_walk_unjudged()
        {
            // The review: the settler run by another computer a while, or the area unloaded: "stood still 180 s".
            var watch = new WalkWatch();
            Vec3 goal = new Vec3(30, 0, 0);
            for (int i = 0; i <= 4; i++) watch.Sample(i * 0.25f, new Vec3(i, 0, 0), true, goal, false);
            Assert.Null(watch.Sample(181f, new Vec3(4, 0, 0), true, goal, false));
            WalkEnd? end = null;
            for (int i = 1; i <= 8; i++) end = watch.Sample(181f + i * 0.25f, new Vec3(4 + i, 0, 0), true, goal, false) ?? end;
            end = watch.Sample(183.5f, new Vec3(12, 0, 0), false, goal, false, arrivedAt: goal);
            Assert.True(end.HasValue);
            Assert.False(end.Value.StoodStill);
            Assert.Equal(4f, end.Value.From.X, 1);   // a fresh walk from where the watching went on
        }

        [Fact]
        public void A_jump_drops_the_walk_unjudged()
        {
            // The review: a rescue moves the settler; the jump isn't walking.
            var watch = new WalkWatch();
            Vec3 goal = new Vec3(30, 0, 0);
            for (int i = 0; i <= 4; i++) watch.Sample(i * 0.25f, new Vec3(i, 0, 0), true, goal, false);
            Assert.Null(watch.Sample(1.25f, new Vec3(4, 0, 20), true, goal, false));
            WalkEnd? end = watch.Sample(1.5f, new Vec3(4.5f, 0, 20), false, goal, false, arrivedAt: goal);
            Assert.True(end.HasValue);
            Assert.True(end.Value.Walked < 1f);
        }
    }
}
