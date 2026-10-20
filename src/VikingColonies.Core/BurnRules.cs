using System;

namespace VikingColonies.Core
{
    /// <summary>
    /// A settler who keeps burning where he stands (1.4.0's self-test on master: squeezed between a hut's wall and the life
    /// test's fire, in her place for the evening, a settler burned to death 2 m from it). The camp brings a fire to every
    /// colony and the colony gathers round it each evening: one burning for a few seconds without getting away from where
    /// he caught fire is moved to a free spot by the Town Hall, clear of fires (<see cref="RescueSpot"/>).
    /// </summary>
    public static class BurnRules
    {
        /// <summary>Burning this long (seconds) in one place: he is moved (a step through the fire and out is fine).</summary>
        public const float Seconds = 4f;

        /// <summary>Getting this far from where he caught fire counts as getting away.</summary>
        public const float Patch = 2f;

        public static bool StuckBurning(bool burning, float seconds) => burning && seconds >= Seconds;

        public static bool StillThere(float dx, float dz) => Math.Sqrt(dx * dx + dz * dz) <= Patch;
    }
}
