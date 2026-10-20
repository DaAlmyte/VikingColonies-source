namespace VikingColonies.Core
{
    /// <summary>
    /// What a lumberjack works on: standing trees inside his hut's work area; the logs, stump and wood of a tree he felled
    /// wherever they landed (a tree near the edge falls, and its logs roll, past it).
    /// </summary>
    public static class ChopArea
    {
        /// <summary>The leftovers of the tree he felled lie within this of where it stood (a trunk reaches about 20 m; logs roll).</summary>
        public const float LeftoverReach = 25f;

        /// <summary>A log, stump or wood from the tree he felled last.</summary>
        public static bool IsLeftover(bool isTree, bool haveFelled, float fromFelled) => !isTree && haveFelled && fromFelled <= LeftoverReach;

        /// <summary>Inside the work area, or a leftover of the tree he felled.</summary>
        public static bool Counts(bool isTree, float fromHut, float radius, bool haveFelled, float fromFelled) =>
            fromHut <= radius || IsLeftover(isTree, haveFelled, fromFelled);
    }
}
