using System;
using System.Collections.Generic;

namespace VikingColonies.Core
{
    /// <summary>
    /// vc_walktest's measures (1.5.1, the player after their 1.5.0 watch: "I just want it to feel smooth"): the shortest way
    /// round a hut's walls, and whether a settler's walk kept close to it without standing about on the way.
    /// </summary>
    public static class WalkCheck
    {
        /// <summary>A walk this many times the shortest way...</summary>
        public const float DetourFactor = 1.25f;

        /// <summary>...plus this much (lining up with a doorway, a body's width round each corner) is still smooth.</summary>
        public const float DetourSlack = 3f;

        /// <summary>Standing still longer than this away from the door (not opening it) isn't smooth.</summary>
        public const float StopAllowed = 1.5f;

        public static bool Smooth(float walked, float shortest, float longestStop) =>
            walked <= shortest * DetourFactor + DetourSlack && longestStop <= StopAllowed;

        /// <summary>
        /// The shortest way over the ground from one point to another that doesn't cross the box (a hut's walls): straight
        /// when nothing is in the way, else round its corners. A point inside the box (a door set into the wall) is measured
        /// from the nearest point of its edge, the step to it added.
        /// </summary>
        public static float ShortestAround(Vec3 from, Vec3 to, Footprint box)
        {
            float extra = 0f;
            from = OntoEdge(from, box, ref extra);
            to = OntoEdge(to, box, ref extra);
            var points = new List<Vec3> { from, to };
            points.AddRange(box.Corners());
            int n = points.Count;
            var best = new float[n];
            var done = new bool[n];
            for (int i = 0; i < n; i++) best[i] = float.MaxValue;
            best[0] = 0f;
            for (int round = 0; round < n; round++)
            {
                int at = -1;
                for (int i = 0; i < n; i++)
                    if (!done[i] && (at < 0 || best[i] < best[at])) at = i;
                if (at < 0 || best[at] == float.MaxValue) break;
                done[at] = true;
                if (at == 1) break;
                for (int i = 0; i < n; i++)
                {
                    if (done[i] || Crosses(points[at], points[i], box)) continue;
                    float way = best[at] + Flat(points[at], points[i]);
                    if (way < best[i]) best[i] = way;
                }
            }
            return best[1] + extra;
        }

        /// <summary>
        /// The fair way between two points when either is inside a hut (its walls, and its door): out through the door of
        /// the hut <paramref name="from"/> is in, round to the door of the hut <paramref name="to"/> is in, and in (vc_walkwatch's
        /// review: 7 m straight to a chest behind a Longhouse's back wall is 35 m by its door, and right). The straight line
        /// when neither is in a hut, or both in the same one.
        /// </summary>
        public static float FairWay(Vec3 from, Vec3 to, Footprint fromHut, Vec3 fromDoor, Footprint toHut, Vec3 toDoor)
        {
            if (ReferenceEquals(fromHut, toHut)) return Flat(from, to);
            if (fromHut == null) return ShortestAround(from, toDoor, toHut) + Flat(toDoor, to);
            if (toHut == null) return Flat(from, fromDoor) + ShortestAround(fromDoor, to, fromHut);
            return Flat(from, fromDoor) + ShortestAround(fromDoor, toDoor, toHut) + Flat(toDoor, to);
        }

        private static float Flat(Vec3 a, Vec3 b) => (float)Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));

        /// <summary>
        /// Where to level the ground so a square <paramref name="half"/> each way round <paramref name="centre"/> is all
        /// within reach of one levelling of <paramref name="radius"/> (a grid no wider apart than the radius), in batches
        /// whose levellings never reach the same ground: a levelling reads the ground's height as it was before the others of
        /// its frame took effect (the 2026-10-24 probe: a pass of 361 at once left it 39.58 to 40.20 m), so a batch goes
        /// at once and the next waits for the ground to settle, as a builder's do. The test spot put back to the meadow's
        /// height before each hut (2026-10-24: each hut levelled it 0.13 m below the last).
        /// </summary>
        public static List<List<GroundPoint>> LevelBatches(GroundPoint centre, float half, float radius)
        {
            var batches = new List<List<GroundPoint>>();
            if (half <= 0f || radius <= 0f)
            {
                batches.Add(new List<GroundPoint> { centre });
                return batches;
            }
            int n = (int)Math.Ceiling(2f * half / radius) + 1;
            float step = 2f * half / (n - 1);
            int stride = (int)Math.Floor(2f * radius / step) + 1;   // spots a batch apart are over twice the radius apart
            for (int bi = 0; bi < stride; bi++)
                for (int bj = 0; bj < stride; bj++)
                {
                    var batch = new List<GroundPoint>();
                    for (int i = bi; i < n; i += stride)
                        for (int j = bj; j < n; j += stride)
                            batch.Add(new GroundPoint(centre.X - half + i * step, centre.Z - half + j * step));
                    if (batch.Count > 0) batches.Add(batch);
                }
            return batches;
        }

        /// <summary>A point inside the box moved to the nearest point of its edge, the distance added to <paramref name="extra"/>.</summary>
        private static Vec3 OntoEdge(Vec3 point, Footprint box, ref float extra)
        {
            Vec3 local = box.Rotation.Inverse.Rotate(point - box.Centre);
            float toX = box.HalfX - Math.Abs(local.X), toZ = box.HalfZ - Math.Abs(local.Z);
            if (toX <= 0f || toZ <= 0f) return point;
            if (toX < toZ)
            {
                extra += toX;
                return box.ToWorld(Math.Sign(local.X == 0f ? 1f : local.X) * box.HalfX, local.Z);
            }
            extra += toZ;
            return box.ToWorld(local.X, Math.Sign(local.Z == 0f ? 1f : local.Z) * box.HalfZ);
        }

        /// <summary>The straight line between the two points passes through the box's inside (along an edge doesn't).</summary>
        private static bool Crosses(Vec3 a, Vec3 b, Footprint box)
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
    }

    /// <summary>A walk as it went (vc_walktest): the metres walked and the longest time standing still away from the door.</summary>
    public sealed class WalkTrace
    {
        /// <summary>Slower than this, they are standing still.</summary>
        public const float StillSpeed = 0.3f;

        private bool _any;
        private Vec3 _last;
        private float _lastTime;
        private float _stillSince = float.NaN;

        public float Walked { get; private set; }
        public float LongestStop { get; private set; }

        /// <param name="nearDoor">By the door: standing there to open it, or waiting a turn, isn't a stop.</param>
        public void Add(float time, Vec3 at, bool nearDoor)
        {
            if (_any)
            {
                float step = (float)Math.Sqrt((at.X - _last.X) * (at.X - _last.X) + (at.Z - _last.Z) * (at.Z - _last.Z));
                Walked += step;
                float speed = step / Math.Max(1e-3f, time - _lastTime);
                if (speed < StillSpeed && !nearDoor)
                {
                    if (float.IsNaN(_stillSince)) _stillSince = _lastTime;
                    LongestStop = Math.Max(LongestStop, time - _stillSince);
                }
                else _stillSince = float.NaN;
            }
            _any = true;
            _last = at;
            _lastTime = time;
        }
    }
}
