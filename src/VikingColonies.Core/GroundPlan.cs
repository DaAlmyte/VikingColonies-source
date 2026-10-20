using System;
using System.Collections.Generic;
using System.Linq;

namespace VikingColonies.Core
{
    /// <summary>One flattening step: the ground within the radius goes to the target height (the position's height).</summary>
    public sealed class GroundOp
    {
        public GroundOp(Vec3 position, float radius, bool dig, bool raise, float fill, bool deepDig = false)
        {
            Position = position;
            Radius = radius;
            Dig = dig;
            Raise = raise;
            Fill = fill;
            DeepDig = deepDig;
        }

        public Vec3 Position { get; }
        public float Radius { get; }
        /// <summary>Some ground here is higher than the target: levelled down with the hoe.</summary>
        public bool Dig { get; }
        /// <summary>Some ground here is more than half a metre too high: dug with a pickaxe.</summary>
        public bool DeepDig { get; }
        /// <summary>Some ground here is lower than the target: raised with a hoe and stone.</summary>
        public bool Raise { get; }
        /// <summary>Cubic metres of ground to add.</summary>
        public float Fill { get; }
    }

    /// <summary>
    /// How to flatten a site: it is covered by a few large level operations, and the ground under each is sampled about
    /// every metre; only operations where the ground is more than 10 cm off are kept. Huge sites are sampled more
    /// coarsely, so a look never costs more than about <see cref="MaxSamples"/> ground readings.
    /// </summary>
    public static class GroundPlan
    {
        public const float OpRadius = 2.5f;
        /// <summary>The radius of the level operation the mod registers with the game: no planned operation is wider.</summary>
        public const float LevelRadius = 2.6f;
        public const float SampleStep = 1f;
        public const int MaxSamples = 900;
        /// <summary>The edge of the flattened ground is smoothed this far outside the site, at spots this far apart.</summary>
        public const float SmoothOutside = 1.5f;
        public const float SmoothSpacing = 3f;

        /// <param name="groundAt">The ground's height at a point (x, z).</param>
        /// <param name="keepOff">
        /// Whether an operation at (x, z) with this radius would reach something it mustn't touch (another building);
        /// such spots are left as they are. Null: none.
        /// </param>
        public static List<GroundOp> Plan(Footprint site, float target, Func<float, float, float> groundAt,
            Func<float, float, float, bool> keepOff = null)
        {
            var ops = new List<GroundOp>();
            float cell = OpRadius * (float)Math.Sqrt(2.0);   // a square this size fits inside an operation's circle
            int nx = Math.Max(1, (int)Math.Ceiling(2f * site.HalfX / cell));
            int nz = Math.Max(1, (int)Math.Ceiling(2f * site.HalfZ / cell));
            float dx = 2f * site.HalfX / nx, dz = 2f * site.HalfZ / nz;
            float radius = (float)Math.Sqrt(dx * dx + dz * dz) / 2f + 0.05f;
            float step = Step(site);
            int sx = Math.Max(1, (int)Math.Round(dx / step)), sz = Math.Max(1, (int)Math.Round(dz / step));
            float sampleArea = dx * dz / (sx * sz);
            for (int i = 0; i < nx; i++)
            {
                for (int k = 0; k < nz; k++)
                {
                    float cx = -site.HalfX + dx * (i + 0.5f), cz = -site.HalfZ + dz * (k + 0.5f);
                    bool dig = false, raise = false, deep = false;
                    float fill = 0f;
                    for (int a = 0; a < sx; a++)
                    {
                        for (int b = 0; b < sz; b++)
                        {
                            Vec3 p = site.ToWorld(cx - dx / 2f + dx * (a + 0.5f) / sx, cz - dz / 2f + dz * (b + 0.5f) / sz);
                            float off = groundAt(p.X, p.Z) - target;
                            if (off > GroundRule.Tolerance) dig = true;
                            if (off > GroundRule.HoeDigDepth) deep = true;
                            if (off < -GroundRule.Tolerance)
                            {
                                raise = true;
                                fill += -off * sampleArea;
                            }
                        }
                    }
                    if (!dig && !raise) continue;
                    Vec3 centre = site.ToWorld(cx, cz);
                    if (keepOff != null && keepOff(centre.X, centre.Z, radius)) continue;
                    ops.Add(new GroundOp(new Vec3(centre.X, target, centre.Z), radius, dig, raise, fill, deep));
                }
            }
            return ops;
        }

        /// <summary>Stone for the ground to add, at the game's rate; at least one when anything is raised (unless raising is free).</summary>
        public static int Stone(IEnumerable<GroundOp> ops, float stonePerCubicMetre)
        {
            float fill = ops.Sum(o => o.Fill);
            if (fill <= 0f || stonePerCubicMetre <= 0f) return 0;
            return Math.Max(1, (int)Math.Ceiling(fill * stonePerCubicMetre));
        }

        /// <summary>The lowest and highest ground in the site.</summary>
        public static void Range(Footprint site, Func<float, float, float> groundAt, out float lowest, out float highest)
        {
            float step = Step(site);
            int nx = Math.Max(1, (int)Math.Floor(2f * site.HalfX / step)), nz = Math.Max(1, (int)Math.Floor(2f * site.HalfZ / step));
            lowest = float.MaxValue;
            highest = float.MinValue;
            for (int i = 0; i < nx; i++)
            {
                for (int k = 0; k < nz; k++)
                {
                    Vec3 p = site.ToWorld(-site.HalfX + 2f * site.HalfX * (i + 0.5f) / nx, -site.HalfZ + 2f * site.HalfZ * (k + 0.5f) / nz);
                    float h = groundAt(p.X, p.Z);
                    lowest = Math.Min(lowest, h);
                    highest = Math.Max(highest, h);
                }
            }
        }

        /// <summary>Where the edge of the flattened ground is smoothed: around the site, just outside it.</summary>
        public static List<Vec3> SmoothSpots(Footprint site, float spacing) => StandSpots.Ring(site.Grow(SmoothOutside), 0f, spacing);

        /// <summary>About a metre between ground readings; wider on huge sites.</summary>
        public static float Step(Footprint site) =>
            Math.Max(SampleStep, (float)Math.Sqrt(4f * site.HalfX * site.HalfZ / MaxSamples));
    }
}
