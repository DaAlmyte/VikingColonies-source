using System;

namespace VikingColonies.Core
{
    /// <summary>
    /// Steady movement: when a settler counts as arrived and whether it runs. Each rule has a margin,
    /// so a settler near a threshold doesn't flip between stop and go, or walk and run.
    /// </summary>
    public static class MoveRules
    {
        public const float MinStopDistance = 0.8f;
        public const float ArriveSlack = 0.5f;
        public const float RunAbove = 12f;
        public const float WalkBelow = 8f;
        /// <summary>A goal that moves further than this counts as a new goal.</summary>
        public const float NewGoalDistance = 1f;
        /// <summary>How far short of the stop distance a path may end and still count as arriving.</summary>
        public const float PathEndSlack = 2f;

        public static float StopDistance(float requested) => Math.Max(requested, MinStopDistance);

        public static bool Arrived(bool wasArrived, float distance, float stopDistance) =>
            distance <= stopDistance || (wasArrived && distance <= stopDistance + ArriveSlack);

        /// <summary>
        /// Close enough to the goal to work on it, and no wall between (a chest behind a wall or a window isn't within
        /// reach from outside: he walks round, through a door if there is one). Once there, he stays there.
        /// </summary>
        public static bool InReach(bool wasArrived, float distance, float stopDistance, bool wallBetween) =>
            Arrived(wasArrived, distance, stopDistance) && (wasArrived || !wallBetween);

        /// <summary>
        /// Sprint (the player's 7 m/s burst) only when hurrying, e.g. fleeing, and far from the goal; otherwise jog
        /// like a player normally does.
        /// </summary>
        public static bool ShouldRun(bool runningNow, float distance, bool hurry) =>
            hurry && (distance > RunAbove || (runningNow && distance >= WalkBelow));

        /// <summary>How close a settler that can't get any closer to its hut may be and still deliver.</summary>
        public const float StuckDeliveryRange = 8f;

        /// <summary>A hut behind a door, upstairs or on a platform: deliver from where the walking stopped, if close.</summary>
        public static bool DeliverFromHere(bool stuck, float distance) => stuck && distance <= StuckDeliveryRange;

        /// <summary>The game stops a settler when its path ends (or no path exists); that only counts as arriving close by.</summary>
        public static bool ReachedPathEnd(bool pathEnded, float distance, float stopDistance) =>
            pathEnded && distance <= stopDistance + PathEndSlack;
    }
}
