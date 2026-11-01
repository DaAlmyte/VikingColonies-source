using System;
using System.Collections.Generic;
using System.Linq;

namespace VikingColonies.Core
{
    /// <summary>
    /// What the Chieftain's Camp's Overview and a settler's page choose to show (1.5.1, review 8): a few names and how many
    /// more, the mood reasons that weigh most across the colony, and a settler's own lines of the Saga.
    /// </summary>
    public static class CampOverview
    {
        /// <summary>The first <paramref name="shown"/> names; <paramref name="more"/> is how many are left out.</summary>
        public static IReadOnlyList<string> FirstNames(IReadOnlyList<string> names, int shown, out int more)
        {
            names = names ?? new string[0];
            int take = Math.Max(0, Math.Min(shown, names.Count));
            more = names.Count - take;
            return names.Take(take).ToList();
        }

        /// <summary>
        /// Each reason summed over the settlers (the same reason about another person stays apart: two deaths are two
        /// reasons), the largest by size first, <paramref name="max"/> at most; one summing to nothing is left out.
        /// </summary>
        public static IReadOnlyList<MoodReason> WeightiestReasons(IEnumerable<IReadOnlyList<MoodReason>> perSettler, int max)
        {
            var sums = new List<MoodReason>();
            foreach (IReadOnlyList<MoodReason> reasons in perSettler ?? Enumerable.Empty<IReadOnlyList<MoodReason>>())
            {
                if (reasons == null) continue;
                foreach (MoodReason r in reasons)
                {
                    int i = sums.FindIndex(s => s.Token == r.Token && s.Name == r.Name);
                    if (i < 0) sums.Add(new MoodReason(r.Token, r.Change, r.Name));
                    else sums[i] = new MoodReason(r.Token, sums[i].Change + r.Change, r.Name);
                }
            }
            return sums.Where(s => s.Change != 0)
                .Select((s, order) => new { s, order })
                .OrderByDescending(x => Math.Abs(x.s.Change)).ThenBy(x => x.order)   // ties: the first seen
                .Take(Math.Max(0, max)).Select(x => x.s).ToList();
        }

        /// <summary>The Saga's lines naming <paramref name="name"/> as one of their words, newest first, <paramref name="max"/> at most.</summary>
        public static IReadOnlyList<SagaEntry> LinesNaming(SagaLog saga, string name, int max)
        {
            if (saga == null || string.IsNullOrEmpty(name) || max <= 0) return new SagaEntry[0];
            return saga.Page(0, SagaLog.MaxEntries).Where(e => e.Args.Contains(name)).Take(max).ToList();
        }
    }
}
