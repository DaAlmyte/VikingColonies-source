using System;
using System.Collections.Generic;
using System.Linq;

namespace VikingColonies.Core
{
    /// <summary>A piece's box on the ground (0.26): its extent along x and z.</summary>
    public struct GroundBox
    {
        public GroundBox(float minX, float minZ, float maxX, float maxZ)
        {
            MinX = Math.Min(minX, maxX);
            MinZ = Math.Min(minZ, maxZ);
            MaxX = Math.Max(minX, maxX);
            MaxZ = Math.Max(minZ, maxZ);
        }

        public float MinX { get; }
        public float MinZ { get; }
        public float MaxX { get; }
        public float MaxZ { get; }

        /// <summary>A piece known only by its middle.</summary>
        public static GroundBox At(GroundPoint point) => new GroundBox(point.X, point.Z, point.X, point.Z);
    }

    /// <summary>A point or direction on the ground plane (world X and Z).</summary>
    public struct GroundPoint
    {
        public GroundPoint(float x, float z)
        {
            X = x;
            Z = z;
        }

        public float X { get; }
        public float Z { get; }
    }

    /// <summary>Picks which way to fell a tree so the trunk doesn't land on the player's buildings.</summary>
    public static class FallPlanner
    {
        /// <summary>
        /// A built piece a felled tree must miss: anything a player (or a builder for them) put up, except the site's own old
        /// pieces its upgrade takes down anyway (the old-huts upgrade test, 2026-10-19: a birch that could only fall on the
        /// hut's old pieces was never felled, and the site waited on the player for good).
        /// </summary>
        public static bool MustMiss(bool placedByPlayer, bool dueToComeDown) => placedByPlayer && !dueToComeDown;

        private const int Directions = 16;

        /// <param name="tree">Where the tree stands.</param>
        /// <param name="buildings">Positions of the player's building pieces nearby.</param>
        /// <param name="preferred">The direction we'd naturally fell it in (away from the settler).</param>
        /// <param name="length">How far the trunk reaches when it lands.</param>
        /// <param name="clearance">How far every building must stay from the fallen trunk.</param>
        /// <returns>A unit direction, or null when every direction would hit a building (leave the tree alone).</returns>
        public static GroundPoint? ChooseDirection(GroundPoint tree, IReadOnlyList<GroundPoint> buildings, GroundPoint preferred,
            float length, float clearance)
        {
            double preferredAngle = preferred.X == 0f && preferred.Z == 0f ? 0.0 : Math.Atan2(preferred.Z, preferred.X);
            IEnumerable<double> angles = Enumerable.Range(0, Directions)
                .Select(i => preferredAngle + i * 2 * Math.PI / Directions)
                .OrderBy(a => Math.Abs(Math.IEEERemainder(a - preferredAngle, 2 * Math.PI)));
            foreach (double angle in angles)
            {
                var dir = new GroundPoint((float)Math.Cos(angle), (float)Math.Sin(angle));
                if (buildings.All(b => DistanceToTrunk(tree, dir, length, b) >= clearance)) return dir;
            }
            return null;
        }

        private static float DistanceToTrunk(GroundPoint tree, GroundPoint dir, float length, GroundPoint building)
        {
            float bx = building.X - tree.X, bz = building.Z - tree.Z;
            float t = Math.Max(0f, Math.Min(length, bx * dir.X + bz * dir.Z));
            float dx = bx - dir.X * t, dz = bz - dir.Z * t;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }
    }
}
