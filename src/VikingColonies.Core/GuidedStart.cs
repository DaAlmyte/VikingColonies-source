using System;
using System.Collections.Generic;

namespace VikingColonies.Core
{
    /// <summary>Hugin's first steps (1.5.1, review 4), in order; <see cref="None"/> once all are done.</summary>
    public enum StartStep { Camp, BuilderHut, Worker, Longhouse, Food, Watchtower, CampLevel2, None }

    /// <summary>What a colony has, as the steps read it (nothing of the steps is saved).</summary>
    public sealed class StartFacts
    {
        public bool HasCamp, BuiltBuilderHut, HasWorker, BuiltLonghouse, BuiltWatchtower;
        public int CampFood, FoodEach, CampLevel;
        /// <summary>The camp's levels are on (the setting); with them off the camp can't reach level 2, so that step is done.</summary>
        public bool LevelsOn = true;
    }

    /// <summary>
    /// A short guided start told by Hugin (1.5.1, from review 4: the tips say what is happening, nothing said what to do
    /// next). Each step is done when the colony has it now, so an old colony arrives with its steps ticked.
    /// </summary>
    public static class GuidedStart
    {
        public static bool Done(StartStep step, StartFacts facts)
        {
            if (facts == null) return false;
            switch (step)
            {
                case StartStep.Camp: return facts.HasCamp;
                case StartStep.BuilderHut: return facts.BuiltBuilderHut;
                case StartStep.Worker: return facts.HasWorker;
                case StartStep.Longhouse: return facts.BuiltLonghouse;
                case StartStep.Food: return facts.CampFood >= Math.Max(1, facts.FoodEach);   // one newcomer's worth
                case StartStep.Watchtower: return facts.BuiltWatchtower;
                case StartStep.CampLevel2: return !facts.LevelsOn || facts.CampLevel >= 2;
                default: return false;
            }
        }

        /// <summary>The first step not done; <see cref="StartStep.None"/> when all are.</summary>
        public static StartStep Next(StartFacts facts)
        {
            for (StartStep step = StartStep.Camp; step < StartStep.None; step++)
                if (!Done(step, facts)) return step;
            return StartStep.None;
        }

        /// <summary>The highest step done before the next one (the Overview ticks it), or None when none is.</summary>
        public static StartStep LastDone(StartFacts facts)
        {
            StartStep next = Next(facts);
            for (StartStep step = next - 1; step >= StartStep.Camp; step--)
                if (Done(step, facts)) return step;
            return StartStep.None;
        }

        /// <summary>
        /// A settler's job ticks "give a settler a job": any but the builder's, whom the Builder's Hut hires before it
        /// stands (review of 1.5.1).
        /// </summary>
        public static bool CountsAsWorker(string job) => !string.IsNullOrEmpty(job) && job != SaveKeys.BuilderJob;

        /// <summary>
        /// Hugin's lines for steps already done: taken off his waiting list, so he never tells the player to do what is
        /// done (review of 1.5.1: a line could wait until the player next talked to him).
        /// </summary>
        public static IEnumerable<string> StaleLines(StartFacts facts)
        {
            for (StartStep step = StartStep.BuilderHut; step < StartStep.None; step++)
                if (Done(step, facts)) yield return LineToken(step);
        }

        /// <summary>The step's words on the Overview: "$vc_step_builder_hut".</summary>
        public static string Token(StartStep step) => "$vc_step_" + Snake(step);

        /// <summary>What Hugin says as the step opens; none for the camp (the colony tip of 0.11 says it).</summary>
        public static string LineToken(StartStep step) =>
            step == StartStep.Camp ? "" : step == StartStep.None ? "$vc_step_done_line" : Token(step) + "_line";

        /// <summary>
        /// What Hugin says as the step opens, none for the Builder's Hut once the colony tip has been said: it says the same
        /// (review of 1.5.1: at founding the two came together).
        /// </summary>
        public static string LineToken(StartStep step, bool colonyTipSaid) =>
            step == StartStep.BuilderHut && colonyTipSaid ? "" : LineToken(step);

        private static string Snake(StartStep step)
        {
            switch (step)
            {
                case StartStep.BuilderHut: return "builder_hut";
                case StartStep.CampLevel2: return "camp_level_2";
                default: return step.ToString().ToLowerInvariant();
            }
        }
    }
}
