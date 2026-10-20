using System;

namespace VikingColonies.Core
{
    /// <summary>
    /// Where a settler steps to keep clear of a falling tree or a moving log. Points and directions are on the ground
    /// plane; a zero direction is allowed.
    /// </summary>
    public static class RetreatSpot
    {
        /// <summary>How far from a felled tree's stump he steps, and how close a moving log may come before he steps aside.</summary>
        public const float Distance = 4f;

        /// <summary>How far past a moving log's edge he steps to be out of its path.</summary>
        public const float PathMargin = 1.5f;

        /// <summary>
        /// Behind the stump (opposite the fall) and 45° off the fall line on the side the settler stands, at
        /// <paramref name="distance"/> from the stump, so a trunk whose base kicks back along its line misses him. Without a
        /// fall direction: straight away from the stump through the settler.
        /// </summary>
        public static GroundPoint FromFall(GroundPoint stump, GroundPoint fallDir, GroundPoint settler, float distance)
        {
            float fx, fz;
            if (!Unit(fallDir.X, fallDir.Z, out fx, out fz))
            {
                float ax, az;
                if (!Unit(settler.X - stump.X, settler.Z - stump.Z, out ax, out az))
                {
                    ax = 1f;
                    az = 0f;
                }
                return new GroundPoint(stump.X + ax * distance, stump.Z + az * distance);
            }
            float sx = -fz, sz = fx;   // one side of the fall line
            if (sx * (settler.X - stump.X) + sz * (settler.Z - stump.Z) < 0f)
            {
                sx = -sx;   // the settler's side
                sz = -sz;
            }
            float dx, dz;
            Unit(sx - fx, sz - fz, out dx, out dz);
            return new GroundPoint(stump.X + dx * distance, stump.Z + dz * distance);
        }

        /// <summary>
        /// A spot out of the path of a log (axis a→b, <paramref name="radius"/> thick) moving along <paramref name="motion"/>:
        /// sideways to the motion, past the log's nearer edge by <paramref name="margin"/>. The settler's own spot when the
        /// log isn't moving, is moving away from him, or passes beside him.
        /// </summary>
        public static GroundPoint OutOfPath(GroundPoint a, GroundPoint b, float radius, GroundPoint motion, GroundPoint settler, float margin)
        {
            float mx, mz;
            if (!Unit(motion.X, motion.Z, out mx, out mz)) return settler;
            float qa = mx * a.X + mz * a.Z, qb = mx * b.X + mz * b.Z, qs = mx * settler.X + mz * settler.Z;
            if (qs < Math.Min(qa, qb) - radius) return settler;   // behind the log: it moves away from him
            float px = -mz, pz = mx;   // across the path
            float pa = px * a.X + pz * a.Z, pb = px * b.X + pz * b.Z, ps = px * settler.X + pz * settler.Z;
            float low = Math.Min(pa, pb) - radius, high = Math.Max(pa, pb) + radius;
            if (ps <= low || ps >= high) return settler;   // the log passes beside him
            float target = ps - low <= high - ps ? low - margin : high + margin;
            float shift = target - ps;
            return new GroundPoint(settler.X + px * shift, settler.Z + pz * shift);
        }

        private static bool Unit(float x, float z, out float ux, out float uz)
        {
            float length = (float)Math.Sqrt(x * x + z * z);
            if (length < 1e-4f)
            {
                ux = uz = 0f;
                return false;
            }
            ux = x / length;
            uz = z / length;
            return true;
        }
    }
}
