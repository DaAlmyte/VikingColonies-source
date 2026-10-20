using System.Collections.Generic;
using System.Linq;

namespace VikingColonies.Core
{
    /// <summary>
    /// Where a settler spends the night by his bed. A Valheim bed is 1.2 m wide (its own x) and 2.8 m long (its own z):
    /// the four spots around it (beside each long side, past each end) stand clear of it, nearest the chest (the room)
    /// first. The game takes the first that is free and can be walked to from the door.
    /// Seen: "1 m from the bed's middle towards the chest" ended against the bed's own edge, where no way led.
    /// </summary>
    public static class BedSpot
    {
        public const float HalfWidth = 0.6f;
        public const float HalfLength = 1.4f;
        /// <summary>How far from the bed he stands: room for his body and the game's paths.</summary>
        public const float Clearance = 0.6f;

        /// <summary>The four spots, on the chest's floor, nearest the chest first.</summary>
        public static IReadOnlyList<Vec3> Candidates(Vec3 bed, Quat rotation, Vec3 chest)
        {
            Vec3 across = rotation.Rotate(new Vec3(1f, 0f, 0f)).Flat.Normalized;
            Vec3 along = rotation.Rotate(new Vec3(0f, 0f, 1f)).Flat.Normalized;
            float side = HalfWidth + Clearance, end = HalfLength + Clearance;
            var spots = new[]
            {
                bed + across * side, bed + across * -side, bed + along * end, bed + along * -end,
            };
            return spots.Select(s => new Vec3(s.X, chest.Y, s.Z))
                        .OrderBy(s => Vec3.Distance(s.Flat, chest.Flat))
                        .ToList();
        }
    }
}
