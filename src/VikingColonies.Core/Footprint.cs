using System;
using System.Collections.Generic;

namespace VikingColonies.Core
{
    /// <summary>The ground a building covers: a box turned with the building.</summary>
    public sealed class Footprint
    {
        public Footprint(Vec3 centre, Quat rotation, float halfX, float halfZ)
        {
            Centre = centre;
            Rotation = rotation.Normalized;
            HalfX = halfX;
            HalfZ = halfZ;
        }

        public Vec3 Centre { get; }
        public Quat Rotation { get; }
        public float HalfX { get; }
        public float HalfZ { get; }

        /// <summary>The smallest box, turned like the building, around the points — plus a margin on every side.</summary>
        public static Footprint Around(IReadOnlyList<Vec3> points, Vec3 centre, Quat rotation, float margin)
        {
            Quat undo = rotation.Normalized.Inverse;
            float minX = 0f, maxX = 0f, minZ = 0f, maxZ = 0f;
            bool any = false;
            foreach (Vec3 p in points)
            {
                Vec3 local = undo.Rotate(p - centre);
                if (!any)
                {
                    minX = maxX = local.X;
                    minZ = maxZ = local.Z;
                    any = true;
                    continue;
                }
                minX = Math.Min(minX, local.X);
                maxX = Math.Max(maxX, local.X);
                minZ = Math.Min(minZ, local.Z);
                maxZ = Math.Max(maxZ, local.Z);
            }
            var middle = new Vec3((minX + maxX) / 2f, 0f, (minZ + maxZ) / 2f);
            return new Footprint(centre + rotation.Normalized.Rotate(middle), rotation, (maxX - minX) / 2f + margin, (maxZ - minZ) / 2f + margin);
        }

        public bool Contains(Vec3 point)
        {
            Vec3 local = Rotation.Inverse.Rotate(point - Centre);
            return Math.Abs(local.X) <= HalfX && Math.Abs(local.Z) <= HalfZ;
        }

        public Vec3 ToWorld(float localX, float localZ) => Centre + Rotation.Rotate(new Vec3(localX, 0f, localZ));

        /// <summary>The four corners, at the centre's height.</summary>
        public Vec3[] Corners() => new[] { ToWorld(-HalfX, -HalfZ), ToWorld(HalfX, -HalfZ), ToWorld(HalfX, HalfZ), ToWorld(-HalfX, HalfZ) };

        public Footprint Grow(float margin) => new Footprint(Centre, Rotation, HalfX + margin, HalfZ + margin);

        /// <summary>Points covering the footprint about every <paramref name="spacing"/> metres, its edges and corners included.</summary>
        public List<Vec3> GridPoints(float spacing)
        {
            spacing = Math.Max(0.5f, spacing);
            int nx = Math.Max(1, (int)Math.Ceiling(2f * HalfX / spacing)), nz = Math.Max(1, (int)Math.Ceiling(2f * HalfZ / spacing));
            var points = new List<Vec3>((nx + 1) * (nz + 1));
            for (int i = 0; i <= nx; i++)
                for (int k = 0; k <= nz; k++)
                    points.Add(ToWorld(-HalfX + 2f * HalfX * i / nx, -HalfZ + 2f * HalfZ * k / nz));
            return points;
        }

        /// <summary>Whether two footprints share any ground seen from above, however each is turned.</summary>
        public bool Overlaps(Footprint other)
        {
            Vec3 between = (other.Centre - Centre).Flat;
            foreach (Vec3 axis in new[] { Axis(1f, 0f), Axis(0f, 1f), other.Axis(1f, 0f), other.Axis(0f, 1f) })
                if (Math.Abs(Vec3.Dot(between, axis)) > Reach(axis) + other.Reach(axis)) return false;   // a gap along this axis
            return true;
        }

        private Vec3 Axis(float x, float z) => Rotation.Rotate(new Vec3(x, 0f, z)).Flat.Normalized;

        /// <summary>How far the footprint reaches from its centre along a direction.</summary>
        private float Reach(Vec3 axis) =>
            HalfX * Math.Abs(Vec3.Dot(Axis(1f, 0f), axis)) + HalfZ * Math.Abs(Vec3.Dot(Axis(0f, 1f), axis));
    }

    /// <summary>Where the builder stands: a ring of spots just outside the footprint. He never goes inside the building.</summary>
    public static class StandSpots
    {
        public static List<Vec3> Ring(Footprint f, float outside, float spacing)
        {
            float hx = f.HalfX + outside, hz = f.HalfZ + outside;
            float perimeter = 4f * (hx + hz);
            int count = Math.Max(4, (int)Math.Ceiling(perimeter / Math.Max(0.5f, spacing)));
            var spots = new List<Vec3>(count);
            for (int k = 0; k < count; k++)
            {
                float d = perimeter * k / count;
                float x, z;
                if (d < 2f * hx) { x = -hx + d; z = -hz; }
                else if (d < 2f * hx + 2f * hz) { x = hx; z = -hz + (d - 2f * hx); }
                else if (d < 4f * hx + 2f * hz) { x = hx - (d - 2f * hx - 2f * hz); z = hz; }
                else { x = -hx; z = hz - (d - 4f * hx - 2f * hz); }
                spots.Add(f.ToWorld(x, z));
            }
            return spots;
        }

        /// <summary>
        /// The spot to work from: his <paramref name="current"/> spot while it is usable and reaches the target (so he
        /// doesn't step to a neighbouring spot after every piece), otherwise the usable spot nearest the settler from
        /// which the target is within reach (measured from 1 m above the spot); -1 if none.
        /// </summary>
        public static int Choose(IReadOnlyList<Vec3> spots, Vec3 target, float reach, Vec3 settler, Func<int, bool> usable, int current = -1)
        {
            var eye = new Vec3(0f, 1f, 0f);
            if (current >= 0 && current < spots.Count && usable(current) && Vec3.Distance(spots[current] + eye, target) <= reach) return current;
            int best = -1;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < spots.Count; i++)
            {
                if (!usable(i) || Vec3.Distance(spots[i] + eye, target) > reach) continue;
                float distance = Vec3.Distance(spots[i].Flat, settler.Flat);
                if (distance < bestDistance)
                {
                    best = i;
                    bestDistance = distance;
                }
            }
            return best;
        }
    }
}
