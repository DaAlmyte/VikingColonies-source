using System;

namespace VikingColonies.Core
{
    /// <summary>
    /// A settler keeps their own size (a player's report, 1.4.3: StarLevelSystem, which makes every creature 10% bigger a
    /// star, made starred settlers too big for a door, and they stayed outside their huts). Another mod's stars still count
    /// for everything else.
    /// </summary>
    public static class SettlerSize
    {
        /// <summary>Within this of their own size on every axis is their own size (a float's rounding, nothing a mod did).</summary>
        public const float Tolerance = 0.01f;

        /// <summary>Whether the body's scale is no longer the settler's own.</summary>
        public static bool Changed(float x, float y, float z, float ownX, float ownY, float ownZ) =>
            Math.Abs(x - ownX) > Tolerance || Math.Abs(y - ownY) > Tolerance || Math.Abs(z - ownZ) > Tolerance;
    }
}
