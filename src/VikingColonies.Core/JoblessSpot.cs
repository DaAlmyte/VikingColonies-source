using System;

namespace VikingColonies.Core
{
    /// <summary>
    /// Where a jobless settler waits: his own spot on a ring round the Town Hall, always the same for him, so a big
    /// colony's jobless don't crowd one point (they jostled there and hopped to get free).
    /// </summary>
    public static class JoblessSpot
    {
        public const float MinRadius = 5f;
        public const float MaxRadius = 10f;

        public static GroundPoint For(GroundPoint hall, string settlerId)
        {
            uint hash = 2166136261;
            foreach (char c in settlerId ?? "")
            {
                hash ^= c;
                hash *= 16777619;
            }
            double angle = (hash % 3600) / 3600.0 * 2 * Math.PI;
            float radius = MinRadius + (hash / 3600 % 1000) / 1000f * (MaxRadius - MinRadius);
            return new GroundPoint(hall.X + (float)(Math.Cos(angle) * radius), hall.Z + (float)(Math.Sin(angle) * radius));
        }
    }
}
