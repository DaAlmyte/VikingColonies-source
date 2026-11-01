using System;
using System.Collections.Generic;
using System.Linq;

namespace VikingColonies.Core
{
    /// <summary>
    /// The memorial stone's look (1.6.0, the player at the 1.5.1 try: "A runestone is probably fine for now"): the stone of one
    /// of the game's lore runestones. Those stand only in the game's places (locations), not as prefabs of their own, so the
    /// stone is looked for in the places named as runestones, the meadows' one first (the plainest, and in every world).
    /// </summary>
    public static class MemorialLook
    {
        public const string First = "Runestone_Meadows";

        /// <summary>How far the stone's foot goes into the ground, so it stands on uneven ground without a gap under it.</summary>
        public const float Sink = 0.1f;

        /// <summary>
        /// The quarter turn that puts the meadows' stone's carved face to the piece's front (its pictures of 27 Oct: the
        /// carving faces the stone's own side): turning a stone with the hammer turns its carving with it.
        /// </summary>
        public const float FaceTurn = 90f;

        /// <summary>The places to look in, in order: the meadows' runestone, then every other runestone place by name.</summary>
        public static List<string> Candidates(IEnumerable<string> locationNames)
        {
            List<string> runestones = (locationNames ?? Enumerable.Empty<string>())
                .Where(n => !string.IsNullOrEmpty(n) && n.IndexOf("runestone", StringComparison.OrdinalIgnoreCase) >= 0)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            return runestones.Where(n => n == First)
                .Concat(runestones.Where(n => n != First).OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
                .ToList();
        }

        /// <summary>How far to raise the stone's model (whose lowest point is <paramref name="bottom"/> from its pivot) to stand.</summary>
        public static float Lift(float bottom) => -bottom - Sink;
    }
}
