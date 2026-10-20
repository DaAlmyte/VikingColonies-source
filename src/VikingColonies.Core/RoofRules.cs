namespace VikingColonies.Core
{
    /// <summary>
    /// A settler up on a roof (the old-huts upgrade test, 2026-10-19: stepping out of the Builder's Hut under a low eave, a
    /// builder landed on its roof and walked about up there for 40 minutes; roaming 8 m, the boxed-in backstop's 4 m patch
    /// never caught it). Standing on a roof piece, well above the ground, for a few seconds: he is put down.
    /// </summary>
    public static class RoofRules
    {
        /// <summary>This far above the ground under him, on a roof, he can't just step off.</summary>
        public const float Above = 1.5f;

        /// <summary>How long (seconds) he may stand up there before he is put down (a hop onto an eave and off again is fine).</summary>
        public const float Seconds = 8f;

        public static bool StuckUp(bool onRoof, float aboveGround, float seconds) => onRoof && aboveGround > Above && seconds >= Seconds;
    }
}
