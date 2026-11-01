using System;
using System.Collections.Generic;

namespace VikingColonies.Core
{
    /// <summary>
    /// Placing a hut: the site check looks at the level it's placed at only, but most huts grow with their levels, and
    /// mostly to one side (a Longhouse only backwards), so a neighbour placed there blocks the upgrade later ("Another
    /// building is in the way"). Until the check looks at the later levels too, the line under the crosshair says how far
    /// it grows and which way, seen from where the player stands, so they leave room on that side.
    /// </summary>
    public static class GrowRoom
    {
        /// <summary>Growing at least this much (metres) on a side is worth naming it.</summary>
        public const float Worth = 1f;

        /// <summary>A side's growth past the placed level by the last level, in metres: in the hut's own turn, or as seen by the player.</summary>
        public sealed class Growth
        {
            /// <summary>Clockwise seen from above: the hut's front, right, back and left; or far from the player, right, near, left.</summary>
            public float[] Sides { get; } = new float[4];
            /// <summary>The last level that reaches further on some side; 0 when none does.</summary>
            public int Level { get; set; }
            public bool Worth => Sides[0] >= GrowRoom.Worth || Sides[1] >= GrowRoom.Worth || Sides[2] >= GrowRoom.Worth || Sides[3] >= GrowRoom.Worth;
        }

        /// <summary>
        /// How far every later level reaches past <paramref name="fromLevel"/>'s site on each side, all levels together
        /// (one may be wider and the next deeper). byLevel[i] is level i + 1's site, put at the chest unturned; null where
        /// there's no design. Null when the placed level has no design.
        /// </summary>
        public static Growth From(IReadOnlyList<Footprint> byLevel, int fromLevel)
        {
            int from = Math.Max(1, fromLevel);
            if (from > byLevel.Count || byLevel[from - 1] == null) return null;
            float[] reach = Reach(byLevel[from - 1]);
            float[] start = (float[])reach.Clone();
            var growth = new Growth();
            for (int level = from + 1; level <= byLevel.Count; level++)
            {
                if (byLevel[level - 1] == null) continue;
                float[] next = Reach(byLevel[level - 1]);
                for (int side = 0; side < 4; side++)
                {
                    if (next[side] <= reach[side] + 0.05f) continue;
                    reach[side] = next[side];
                    growth.Level = level;
                }
            }
            for (int side = 0; side < 4; side++) growth.Sides[side] = reach[side] - start[side];
            return growth;
        }

        /// <summary>
        /// The growth as the player sees it: the hut turned <paramref name="quarterTurns"/> quarter turns clockwise from
        /// facing straight away from them (0: its front is the far side).
        /// </summary>
        public static Growth Seen(Growth growth, int quarterTurns)
        {
            var seen = new Growth { Level = growth.Level };
            int k = ((quarterTurns % 4) + 4) % 4;
            for (int side = 0; side < 4; side++) seen.Sides[(side + k) % 4] = growth.Sides[side];
            return seen;
        }

        /// <summary>How far an unturned site reaches from the chest: front (+z), right (+x), back (-z), left (-x).</summary>
        private static float[] Reach(Footprint f) =>
            new[] { f.Centre.Z + f.HalfZ, f.Centre.X + f.HalfX, f.HalfZ - f.Centre.Z, f.HalfX - f.Centre.X };
    }
}
