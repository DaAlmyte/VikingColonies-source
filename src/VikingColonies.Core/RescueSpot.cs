using System;
using System.Collections.Generic;

namespace VikingColonies.Core
{
    /// <summary>A fire on the ground: its middle and how far it reaches (a bonfire about 2.2 m; 0 when not known).</summary>
    public struct FireRing
    {
        public readonly float X, Z, Radius;

        public FireRing(float x, float z, float radius)
        {
            X = x;
            Z = z;
            Radius = radius;
        }
    }

    /// <summary>
    /// Where a rescue may set a settler down (1.4.0): never in or beside a fire. Every rescue puts its settler on a free
    /// spot by the Town Hall, where the camp's fire pit (a bonfire from level 3) burns; a fire pit's stones are too low for
    /// the spot's standing-room check, and a settler stuck by a fire burned to death in the self-test.
    /// </summary>
    public static class RescueSpot
    {
        /// <summary>Kept clear beyond a fire's own reach.</summary>
        public const float FireMargin = 1.5f;

        public static bool ClearOfFires(float x, float z, IEnumerable<FireRing> fires)
        {
            if (fires == null) return true;
            foreach (FireRing f in fires)
            {
                float dx = x - f.X, dz = z - f.Z;
                if (Math.Sqrt(dx * dx + dz * dz) <= Math.Max(0f, f.Radius) + FireMargin) return false;
            }
            return true;
        }
    }
}
