namespace VikingColonies.Core
{
    /// <summary>
    /// What a finished take-down sweeps into the Town Hall's chest: its materials lying on the hut's own ground, and those
    /// that came down during the take-down and rolled off it, a few metres further (the second full run on the 1.4.0 build,
    /// 2026-10-19: on a hillock a stack rolled off and was left). A player's pile beside the hut, lying there before the
    /// take-down, stays (0.26, the review of 0.24).
    /// </summary>
    public static class TakeDownSweep
    {
        /// <summary>How far past the hut's ground (metres) a stack that came down may have rolled.</summary>
        public const float RolledFar = 4f;

        /// <param name="nearItsGround">Within <see cref="RolledFar"/> of the hut's ground.</param>
        public static bool Takes(bool onHutGround, bool newSinceTakeDown, bool nearItsGround) =>
            onHutGround || (newSinceTakeDown && nearItsGround);
    }
}
