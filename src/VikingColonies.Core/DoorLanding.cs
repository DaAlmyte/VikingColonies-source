using System;

namespace VikingColonies.Core
{
    /// <summary>
    /// The ground in front of a hut's door that the builder levels with the site, so a door facing uphill doesn't open
    /// onto a bank (settlers got stuck in the dip between it and the door): from the site's edge out <see cref="Depth"/>
    /// metres, <see cref="HalfWidth"/> either side of the door, square to it.
    /// </summary>
    public static class DoorLanding
    {
        public const float HalfWidth = 1.5f;
        public const float Depth = 3f;
        /// <summary>How far back into the site the landing starts.</summary>
        public const float Overlap = 0.5f;

        /// <param name="outward">The way out of the hut through the door, flat.</param>
        public static Footprint For(Footprint site, Vec3 door, Vec3 outward)
        {
            Vec3 way = outward.Flat.Normalized;
            float toEdge = 0f;
            while (toEdge < 20f && site.Contains(door + way * toEdge)) toEdge += 0.25f;
            // From a little inside the site's edge (found in steps), so no sliver between them is left out (self-test run 33:
            // a bush there blocked a door), out to Depth beyond the edge.
            float from = Math.Max(0f, toEdge - Overlap), to = toEdge + Depth;
            Vec3 centre = door + way * ((from + to) / 2f);
            float yaw = (float)(Math.Atan2(way.X, way.Z) * 180.0 / Math.PI);
            return new Footprint(new Vec3(centre.X, door.Y, centre.Z), Quat.Yaw(yaw), HalfWidth, (to - from) / 2f);
        }
    }
}
