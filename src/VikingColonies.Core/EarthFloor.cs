using System;
using System.Collections.Generic;
using System.Linq;

namespace VikingColonies.Core
{
    /// <summary>
    /// The earth under a finished building's roof, painted as trodden earth as the hoe's path does (the player, 2026-10-21,
    /// of the earth floors: "I think the dirt floors are fine ... maybe we can polish it a bit"; the review's pictures: the
    /// Longhouse's earth floor was meadow grass and shrubs, as the builder only paints the ground its levelling moves, and
    /// a site already level isn't moved). A player clears a floorless room so; a design's earth floor means trodden earth.
    /// Under a floor too: the game's grass stands up through a floor resting on the earth (the in-game check: through the
    /// Longhouse's sleeping platforms, 13 cm high).
    /// </summary>
    public static class EarthFloor
    {
        /// <summary>A roof piece (its tiles, ridge caps, a lean-to's): by its name; a gable's triangle (a "wall_roof") is a wall.</summary>
        public static bool IsRoof(string prefab)
        {
            string name = (prefab ?? "").ToLowerInvariant();
            return name.Contains("roof") && !name.Contains("wall");
        }

        /// <summary>
        /// Where to paint, in the plan's own space (the hut turned to face +z, its chest at the origin): the 1 m cells under a
        /// roof piece (each covers 2 x 2 m round its middle), one spot a block of
        /// <paramref name="spacing"/> metres (the paint's reach), at the middle of that block's cells.
        /// </summary>
        public static List<GroundPoint> Spots(IEnumerable<PlannedPiece> plan, float spacing)
        {
            List<Vec3> roofs = (plan ?? Enumerable.Empty<PlannedPiece>()).Where(p => p != null && p.IsValid && IsRoof(p.Prefab))
                .Select(p => p.Position).ToList();
            const float edge = 0.001f;
            var cells = new HashSet<long>();
            var blocks = new Dictionary<long, Block>();
            float step = Math.Max(0.5f, spacing);
            foreach (Vec3 roof in roofs)
            {
                for (int ix = (int)Math.Floor(roof.X - 1f); ix <= (int)Math.Floor(roof.X + 1f); ix++)
                    for (int iz = (int)Math.Floor(roof.Z - 1f); iz <= (int)Math.Floor(roof.Z + 1f); iz++)
                    {
                        float cx = ix + 0.5f, cz = iz + 0.5f;
                        if (Math.Abs(cx - roof.X) > 1f + edge || Math.Abs(cz - roof.Z) > 1f + edge) continue;
                        if (!cells.Add(Key(ix, iz))) continue;
                        long block = Key((int)Math.Floor(cx / step), (int)Math.Floor(cz / step));
                        if (!blocks.TryGetValue(block, out Block b)) blocks[block] = b = new Block();
                        b.X += cx;
                        b.Z += cz;
                        b.Count++;
                    }
            }
            return blocks.Values.Select(b => new GroundPoint(b.X / b.Count, b.Z / b.Count))
                .OrderBy(s => s.X).ThenBy(s => s.Z).ToList();
        }

        private static long Key(int a, int b) => ((long)a << 32) ^ (uint)b;

        private sealed class Block
        {
            public float X, Z;
            public int Count;
        }
    }
}
