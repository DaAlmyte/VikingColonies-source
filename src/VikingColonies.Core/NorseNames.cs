using System;
using System.Collections.Generic;
using System.Linq;

namespace VikingColonies.Core
{
    public static class NorseNames
    {
        private static readonly string[] MaleNames =
        {
            "Bjorn", "Ragnar", "Leif", "Ivar", "Ulf", "Halvar", "Torvald", "Erik", "Gunnar", "Sven", "Arne", "Knut",
        };

        private static readonly string[] FemaleNames =
        {
            "Sigrid", "Astrid", "Freydis", "Gudrun", "Ingrid", "Solveig", "Ylva", "Runa", "Hilda", "Thora", "Brynja", "Signe",
        };

        private static readonly string[] AllNames = MaleNames.Concat(FemaleNames).ToArray();

        public static IReadOnlyList<string> Male => MaleNames;
        public static IReadOnlyList<string> Female => FemaleNames;
        public static IReadOnlyList<string> All => AllNames;

        /// <summary>A name that fits the body: model 1 is the female body, model 0 the male one.</summary>
        public static string Pick(Random rng, bool female)
        {
            string[] names = female ? FemaleNames : MaleNames;
            return names[rng.Next(names.Length)];
        }

        /// <summary>
        /// A name nobody in <paramref name="taken"/> has (the colony's settlers), while one is left; then any. Seen in the
        /// 0.14 test: two Runas in a colony of four, chatting with each other.
        /// </summary>
        public static string PickUnused(Random rng, bool female, ICollection<string> taken)
        {
            string[] names = female ? FemaleNames : MaleNames;
            string[] free = taken == null ? names : names.Where(n => !taken.Contains(n)).ToArray();
            return free.Length > 0 ? free[rng.Next(free.Length)] : names[rng.Next(names.Length)];
        }

        /// <summary>The body a known name belongs to (1 = female, 0 = male); an unknown name keeps the current body.</summary>
        public static int BodyFor(string name, int currentModel) =>
            FemaleNames.Contains(name) ? 1 : MaleNames.Contains(name) ? 0 : currentModel;

        public const string NoBeard = "BeardNone";

        /// <summary>Women never have a beard (settlers made before bodies and names matched could have one).</summary>
        public static string BeardFor(int model, string beard) => model == 1 ? NoBeard : beard;

        public static string WithJob(string name, string jobTitle) => name + " the " + jobTitle;

        /// <summary>"Ingrid the Lumberjack"; just the name or just the job title when the other is missing (all in words).</summary>
        public static string Display(string name, string jobTitle, string joblessWord)
        {
            if (string.IsNullOrEmpty(name)) return string.IsNullOrEmpty(jobTitle) ? joblessWord : jobTitle;
            return string.IsNullOrEmpty(jobTitle) ? name : WithJob(name, jobTitle);
        }
    }
}
