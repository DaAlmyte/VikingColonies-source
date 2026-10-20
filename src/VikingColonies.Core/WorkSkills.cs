using System;
using System.Collections.Generic;
using System.Globalization;

namespace VikingColonies.Core
{
    /// <summary>The skill a worker learns at his job (the game's own skill names).</summary>
    public enum WorkSkill { None, Woodcutting, Farming, Crafting, Fighting }

    /// <summary>
    /// Skills for every worker (worker polish part 3, 0.22; MineColonies: skills make them faster): the lumberjack learns
    /// Woodcutting, the farmer and the forager Farming, the builder Crafting, the guard Fighting (1.1).
    /// </summary>
    public static class WorkSkills
    {
        private static readonly WorkSkill[] s_shown = { WorkSkill.Woodcutting, WorkSkill.Farming, WorkSkill.Crafting, WorkSkill.Fighting };

        public static WorkSkill ForJob(string jobId)
        {
            switch (jobId)
            {
                case SaveKeys.LumberjackJob: return WorkSkill.Woodcutting;
                case SaveKeys.FarmerJob:
                case SaveKeys.ForagerJob: return WorkSkill.Farming;
                case SaveKeys.BuilderJob: return WorkSkill.Crafting;
                case SaveKeys.GuardJob: return WorkSkill.Fighting;
                default: return WorkSkill.None;
            }
        }

        public static string Token(WorkSkill skill) => skill == WorkSkill.None ? "" : "$vc_skill_" + skill.ToString().ToLowerInvariant();

        /// <summary>"Woodcutting 12 · Farming 3": every skill he has above 0; "" for none.</summary>
        public static string Line(CitizenRecord r, Func<string, string> localize)
        {
            if (r == null) return "";
            var parts = new List<string>();
            foreach (WorkSkill skill in s_shown)
            {
                float level = r.Level(skill);
                if (level >= 0.5f) parts.Add(localize(Token(skill)) + " " + level.ToString("0", CultureInfo.InvariantCulture));
            }
            return string.Join(" · ", parts.ToArray());
        }
    }
}
