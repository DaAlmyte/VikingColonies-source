using System;
using System.Collections.Generic;

namespace VikingColonies.Core
{
    /// <summary>One part of the mod's start-up: a group of hooks, a command, a component; and what it is for.</summary>
    public sealed class StartupStep
    {
        public StartupStep(string name, string feature, Action run)
        {
            Name = name;
            Feature = feature;
            Run = run;
        }

        public string Name { get; }
        /// <summary>What stops working without it, in the player's words.</summary>
        public string Feature { get; }
        public Action Run { get; }
    }

    /// <summary>
    /// Start-up one part at a time (0.25, review of 0.20, bug 1): a part that fails (a game method a Valheim update
    /// renamed) is named with what stops working, and every other part still starts.
    /// </summary>
    public static class StartupSteps
    {
        /// <summary>Runs every step; returns the failed ones as "name (what stops working): the error".</summary>
        public static List<string> Run(IEnumerable<StartupStep> steps)
        {
            var failed = new List<string>();
            if (steps == null) return failed;
            foreach (StartupStep step in steps)
            {
                if (step == null) continue;
                try
                {
                    step.Run?.Invoke();
                }
                catch (Exception e)
                {
                    failed.Add($"{step.Name} ({step.Feature}): {Describe(e)}");
                }
            }
            return failed;
        }

        /// <summary>The error and what caused it (Harmony wraps the game's own).</summary>
        private static string Describe(Exception e)
        {
            Exception root = e.GetBaseException();
            return root == e ? $"{e.GetType().Name}: {e.Message}" : $"{e.GetType().Name}: {e.Message} ({root.GetType().Name}: {root.Message})";
        }
    }
}
