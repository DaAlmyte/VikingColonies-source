namespace VikingColonies.Core
{
    /// <summary>What the builder does about someone standing where his next piece goes.</summary>
    public enum SpaceStep
    {
        /// <summary>He waits a little: they may be passing.</summary>
        Wait,
        /// <summary>The settlers there are asked to make way: they walk out and keep away a while.</summary>
        AskToMove,
        /// <summary>Asked, they are still there (sitting, wedged, called back by their work): moved out by the door.</summary>
        MoveOut,
        /// <summary>Other pieces first, this one later.</summary>
        PutAside,
    }

    /// <summary>
    /// Someone stands where the builder's next piece goes (the real builders' runs, 2026-10-18/19: the Town Hall camp is
    /// where the colony gathers, and five settlers stood where its bonfire goes; the builder waited for them, silently and
    /// for good). He waits a little, then works on other pieces and tries that one again later. A settler there is asked to
    /// make way first, and moved out if that doesn't do it (the old huts' third rerun, 2026-10-19: the forager stayed in his
    /// own hut while it was upgraded, and the builder put the same roof piece aside for 30 minutes). Players and animals
    /// are only waited for.
    /// </summary>
    public static class SpaceWait
    {
        /// <summary>How long (seconds) he waits for someone to move out of where a piece goes, before each step.</summary>
        public const float WaitSeconds = 8f;

        /// <summary>How long (seconds) a piece he put aside waits before he tries it again.</summary>
        public const float AsideSeconds = 30f;

        /// <summary>How long (seconds) a settler who made way keeps away from where they stood.</summary>
        public const float KeepAwaySeconds = 30f;

        /// <param name="waited">Seconds since he began waiting at the piece, or since his last step there.</param>
        /// <param name="steps">The steps he has taken at it already (asked, moved).</param>
        /// <param name="settlers">Settlers are among those in the way.</param>
        public static SpaceStep Next(float waited, int steps, bool settlers)
        {
            if (waited < WaitSeconds) return SpaceStep.Wait;
            if (!settlers || steps >= 2) return SpaceStep.PutAside;
            return steps == 0 ? SpaceStep.AskToMove : SpaceStep.MoveOut;
        }
    }
}
