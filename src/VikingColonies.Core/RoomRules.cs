using System;

namespace VikingColonies.Core
{
    /// <summary>
    /// Inside a hut is what its walls on the ground enclose, not its whole design's outline (the old huts' upgrade test,
    /// 2026-10-19: the Builder's Hut's outline takes in the woodpile strip behind its back wall; a builder standing there
    /// was taken to be inside, walked straight through the back wall, onto the woodpile and up onto the roof).
    /// </summary>
    public static class RoomRules
    {
        /// <summary>A wall stands on the ground when its bottom is this close above the hut's floor (or anywhere below it).</summary>
        public const float OnTheGround = 0.6f;

        /// <summary>A wall whose top is no higher than this above the floor stands under it (a sill, a stone course).</summary>
        public const float UnderTheFloor = 0.1f;

        /// <summary>
        /// A wall of the hut's room: a wall piece (not a gable) standing on the ground, not one of a storey above, nor one
        /// wholly under the floor (the Storehouse's hand-over, 1.5: its log sills and stone course under a raised floor grew
        /// "inside" by half a metre).
        /// </summary>
        public static bool IsRoomWall(string prefab, float bottomAboveFloor, float topAboveFloor = float.MaxValue) =>
            !string.IsNullOrEmpty(prefab)
            && prefab.IndexOf("wall", StringComparison.OrdinalIgnoreCase) >= 0
            && prefab.IndexOf("roof", StringComparison.OrdinalIgnoreCase) < 0
            && bottomAboveFloor <= OnTheGround
            && topAboveFloor > UnderTheFloor;
    }
}
