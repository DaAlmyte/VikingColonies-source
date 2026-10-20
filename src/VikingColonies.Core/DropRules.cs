namespace VikingColonies.Core
{
    /// <summary>Which of a site's dropped materials a builder goes and picks up.</summary>
    public static class DropRules
    {
        /// <summary>A drop lying higher than this above the site's floor is left where it lies.</summary>
        public const float MaxAboveFloor = 1.5f;

        /// <summary>
        /// Only drops he can get to from the ground: one lying on a roof or a wall top is left (the real builders' run,
        /// 2026-10-18: re-roofing a hut, a builder chased a ridge cap's drop onto the roof, 4 m up, and stayed stuck there).
        /// The floor is the site's (its chest's): a raised hut floor sits over the ground, and the ground outside is lower.
        /// </summary>
        public static bool WithinReach(float dropHeight, float floorHeight) => dropHeight - floorHeight <= MaxAboveFloor;
    }
}
