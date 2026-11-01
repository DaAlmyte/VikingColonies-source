using System;
using System.Collections.Generic;

namespace VikingColonies.Core
{
    /// <summary>
    /// The way round a hut to a spot on its far side, its door's (1.5.1, the walk check's game runs: the game's walking map
    /// took settlers 9-14 m out from a 4 by 6 m hut and back, its ways bending at the corners of its own large triangles
    /// rather than the hut's): close round the hut's own corners instead, each leg a straight one the game's paths walk well.
    /// </summary>
    public static class HutWay
    {
        /// <summary>A leg passing this close to the walls (about a body's width) goes through them.</summary>
        public const float Clear = 0.4f;

        /// <summary>The corners walked round stand this far out from the walls' corners (clear of the corner posts and most eaves).</summary>
        public const float Round = 1.5f;

        /// <summary>
        /// Where he heads next on his way from <paramref name="me"/> to <paramref name="spot"/>: the spot itself when the walls
        /// aren't in the way, else the first corner of the shortest way round them. Either point within the walls isn't this
        /// rule's way (the door's is): the spot.
        /// </summary>
        public static Vec3 Next(Vec3 me, Vec3 spot, Footprint walls)
        {
            if (walls == null || walls.Contains(me) || walls.Contains(spot)) return spot;
            // Pressed against a wall, he is already within a body's width of it: the walls themselves are what he can't cross.
            Footprint solid = walls.Grow(Clear).Contains(me) || walls.Grow(Clear).Contains(spot) ? walls : walls.Grow(Clear);
            if (!Crosses(me, spot, solid)) return spot;
            var points = new List<Vec3> { me, spot };
            points.AddRange(walls.Grow(Round).Corners());
            int n = points.Count;
            var best = new float[n];
            var from = new int[n];
            var done = new bool[n];
            for (int i = 0; i < n; i++)
            {
                best[i] = float.MaxValue;
                from[i] = -1;
            }
            best[0] = 0f;
            for (int round = 0; round < n; round++)
            {
                int at = -1;
                for (int i = 0; i < n; i++)
                    if (!done[i] && (at < 0 || best[i] < best[at])) at = i;
                if (at < 0 || best[at] == float.MaxValue || at == 1) break;
                done[at] = true;
                for (int i = 0; i < n; i++)
                {
                    if (done[i] || Crosses(points[at], points[i], solid)) continue;
                    float way = best[at] + Flat(points[at], points[i]);
                    if (way >= best[i]) continue;
                    best[i] = way;
                    from[i] = at;
                }
            }
            if (from[1] < 0) return spot;   // no way round (can't happen for one box): as the game's paths go
            int step = 1;
            while (from[step] != 0) step = from[step];
            return points[step];
        }

        /// <summary>
        /// The walls of the hut a door stands in: its outline (<paramref name="huts"/>, each with the site's margin round its
        /// walls) taken in by the margin, the door within <see cref="DoorRules.OwnDoorMargin"/> of them; null when the door is
        /// no hut's (a palisade gate).
        /// </summary>
        public static Footprint WallsOfDoor(Vec3 door, IReadOnlyList<Footprint> huts, float siteMargin)
        {
            if (huts == null) return null;
            foreach (Footprint site in huts)
                if (site != null && site.Grow(DoorRules.OwnDoorMargin - siteMargin).Contains(door)) return site.Grow(-siteMargin);
            return null;
        }

        /// <summary>A straight leg keeps this much clear of a fire's own reach (a body's width and a step).</summary>
        public const float FireClear = 1f;

        /// <summary>
        /// The straight line between the two points passes within <see cref="FireClear"/> of a fire's reach (the review of
        /// 2026-10-24: a fire pit's stones are 0.2 m high, under the look along a leg, and a Longhouse's pit is 0.8 m off its
        /// door's line; a settler once burned to death by a fire).
        /// </summary>
        public static bool PassesFire(Vec3 a, Vec3 b, IEnumerable<FireRing> fires)
        {
            if (fires == null) return false;
            float dx = b.X - a.X, dz = b.Z - a.Z, length2 = dx * dx + dz * dz;
            foreach (FireRing fire in fires)
            {
                float t = length2 > 1e-6f ? Math.Max(0f, Math.Min(1f, ((fire.X - a.X) * dx + (fire.Z - a.Z) * dz) / length2)) : 0f;
                float px = a.X + t * dx - fire.X, pz = a.Z + t * dz - fire.Z;
                float reach = Math.Max(0f, fire.Radius) + FireClear;
                if (px * px + pz * pz <= reach * reach) return true;
            }
            return false;
        }

        /// <summary>The straight line between the two points passes through the box's inside (along an edge doesn't).</summary>
        public static bool Crosses(Vec3 a, Vec3 b, Footprint box)
        {
            const float edge = 1e-3f;
            Vec3 la = box.Rotation.Inverse.Rotate(a - box.Centre), lb = box.Rotation.Inverse.Rotate(b - box.Centre);
            float t0 = 0f, t1 = 1f;
            return Clip(-(lb.X - la.X), la.X + box.HalfX - edge, ref t0, ref t1)
                   && Clip(lb.X - la.X, box.HalfX - edge - la.X, ref t0, ref t1)
                   && Clip(-(lb.Z - la.Z), la.Z + box.HalfZ - edge, ref t0, ref t1)
                   && Clip(lb.Z - la.Z, box.HalfZ - edge - la.Z, ref t0, ref t1)
                   && t1 - t0 > 1e-4f;
        }

        // Liang-Barsky: keeps the part of the line on the inside of one edge.
        private static bool Clip(float p, float q, ref float t0, ref float t1)
        {
            if (Math.Abs(p) < 1e-9f) return q > 0f;
            float r = q / p;
            if (p < 0f)
            {
                if (r > t1) return false;
                if (r > t0) t0 = r;
            }
            else
            {
                if (r < t0) return false;
                if (r < t1) t1 = r;
            }
            return true;
        }

        private static float Flat(Vec3 a, Vec3 b) => (float)Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));
    }
}
