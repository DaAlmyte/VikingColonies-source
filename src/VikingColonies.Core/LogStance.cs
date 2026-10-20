using System;

namespace VikingColonies.Core
{
    /// <summary>
    /// Where the lumberjack stands to chop: beside a log, never under it, on the uphill side (logs roll downhill),
    /// and within the axe's reach. Points are on the ground plane; an axis runs from a to b.
    /// </summary>
    public static class LogStance
    {
        public const float Clearance = 1.2f;
        public const float LevelTolerance = 0.3f;
        public const float DefaultReach = 1.8f;
        public const float MinReach = 0.8f;
        public const float ReachMargin = 0.3f;
        /// <summary>Closer to the surface than this counts as under the log (or inside the trunk).</summary>
        public const float MinSurfaceGap = 0.1f;
        public const float MinStandOff = 0.3f;

        /// <summary>0..1 position along a→b of the axis point nearest to p (0 for a zero-length axis).</summary>
        public static float AxisParameter(GroundPoint a, GroundPoint b, GroundPoint p)
        {
            float dx = b.X - a.X, dz = b.Z - a.Z;
            float lengthSq = dx * dx + dz * dz;
            if (lengthSq < 1e-6f) return 0f;
            float t = ((p.X - a.X) * dx + (p.Z - a.Z) * dz) / lengthSq;
            return Math.Max(0f, Math.Min(1f, t));
        }

        public static GroundPoint PointAt(GroundPoint a, GroundPoint b, float t) =>
            new GroundPoint(a.X + (b.X - a.X) * t, a.Z + (b.Z - a.Z) * t);

        public static float DistanceToSurface(GroundPoint a, GroundPoint b, float radius, GroundPoint p)
        {
            GroundPoint nearest = PointAt(a, b, AxisParameter(a, b, p));
            return Math.Max(0f, ColonyRules.Distance(p, nearest) - radius);
        }

        /// <summary>
        /// The two spots beside the axis point nearest the settler, at radius + standOff on either side. For a
        /// zero-length axis the first spot is on the settler's side and the second opposite.
        /// </summary>
        public static void Candidates(GroundPoint a, GroundPoint b, float radius, GroundPoint settler, float standOff,
            out GroundPoint first, out GroundPoint second)
        {
            GroundPoint c = PointAt(a, b, AxisParameter(a, b, settler));
            float dx = b.X - a.X, dz = b.Z - a.Z;
            float length = (float)Math.Sqrt(dx * dx + dz * dz);
            float nx, nz;
            if (length < 1e-3f)
            {
                nx = settler.X - c.X;
                nz = settler.Z - c.Z;
                float l = (float)Math.Sqrt(nx * nx + nz * nz);
                if (l < 1e-3f)
                {
                    nx = 1f;
                    nz = 0f;
                }
                else
                {
                    nx /= l;
                    nz /= l;
                }
            }
            else
            {
                nx = -dz / length;
                nz = dx / length;
            }
            float d = radius + standOff;
            first = new GroundPoint(c.X + nx * d, c.Z + nz * d);
            second = new GroundPoint(c.X - nx * d, c.Z - nz * d);
        }

        /// <summary>The spot on higher ground; within LevelTolerance of each other, the one nearer the settler.</summary>
        public static GroundPoint Choose(GroundPoint first, float firstHeight, GroundPoint second, float secondHeight, GroundPoint settler)
        {
            if (firstHeight - secondHeight > LevelTolerance) return first;
            if (secondHeight - firstHeight > LevelTolerance) return second;
            return ColonyRules.Distance(settler, first) <= ColonyRules.Distance(settler, second) ? first : second;
        }

        /// <summary>How close the lumberjack must be to a surface to hit it: the axe's range minus a margin.</summary>
        public static float ReachFor(float attackRange) =>
            attackRange > 0f ? Math.Max(MinReach, attackRange - ReachMargin) : DefaultReach;

        /// <summary>
        /// How far from the surface to stand: the clearance, but close enough that stopping anywhere within the
        /// walking stop distance of the spot still leaves the surface within reach.
        /// </summary>
        public static float StandOff(float reach) =>
            Math.Max(MinStandOff, Math.Min(Clearance, reach - MoveRules.MinStopDistance - 0.1f));

        /// <summary>Ready to swing: the surface is within reach and he isn't under the log or inside the trunk.</summary>
        public static bool InPlace(float surfaceDistance, float reach) =>
            surfaceDistance >= MinSurfaceGap && surfaceDistance <= reach;
    }
}
