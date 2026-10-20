using System.Collections.Generic;
using System.Linq;

namespace VikingColonies.Core
{
    /// <summary>A bed in one of the colony's Longhouses: "longhouse id#plan index".</summary>
    public struct HomeBed
    {
        public HomeBed(string id, GroundPoint at)
        {
            Id = id ?? "";
            At = at;
        }

        public string Id { get; }
        public GroundPoint At { get; }
    }

    /// <summary>A settler of the colony, where he is and the bed he has ("" for none).</summary>
    public struct HomeSeeker
    {
        public HomeSeeker(string id, GroundPoint at, string home)
        {
            Id = id ?? "";
            At = at;
            Home = home ?? "";
        }

        public string Id { get; }
        public GroundPoint At { get; }
        public string Home { get; }
    }

    /// <summary>
    /// Which settler sleeps in which bed: a settler keeps a bed that's still there (two claiming one: the lower id keeps
    /// it); the homeless take the free beds, the nearest pairs first; nobody shares; the rest stay homeless.
    /// </summary>
    public static class Homes
    {
        /// <summary>His bed's number in its Longhouse, from 1 in the plan's order ("Longhouse bed 2"); 0 when it isn't there.</summary>
        public static int BedNumber(string bedId, IEnumerable<string> houseBedIds)
        {
            if (string.IsNullOrEmpty(bedId) || houseBedIds == null) return 0;
            int i = 0;
            foreach (string id in houseBedIds)
            {
                i++;
                if (id == bedId) return i;
            }
            return 0;
        }

        /// <summary>
        /// The colony's members this computer can't see (beyond the loaded area), as the roster last saw them: their beds
        /// stay theirs. Members gone missing (not seen where they should be) don't hold a bed.
        /// </summary>
        public static IEnumerable<HomeSeeker> OutOfSight(IEnumerable<RosterEntry> roster, ISet<string> loaded) =>
            roster.Where(e => e != null && !e.IsMissing && !loaded.Contains(e.CitizenId) && e.LastPos != null && e.LastPos.Length == 3)
                  .Select(e => new HomeSeeker(e.CitizenId, new GroundPoint(e.LastPos[0], e.LastPos[2]), e.Home ?? ""));

        /// <returns>Every settler's bed id, "" when he has none.</returns>
        public static Dictionary<string, string> Assign(IReadOnlyList<HomeBed> beds, IReadOnlyList<HomeSeeker> settlers)
        {
            var result = new Dictionary<string, string>();
            var bedIds = new HashSet<string>(beds.Select(b => b.Id));
            var taken = new HashSet<string>();
            foreach (HomeSeeker s in settlers.OrderBy(s => s.Id, System.StringComparer.Ordinal))
            {
                bool keeps = s.Home.Length > 0 && bedIds.Contains(s.Home) && taken.Add(s.Home);
                result[s.Id] = keeps ? s.Home : "";
            }
            var pairs = new List<BedChoice>();
            foreach (HomeSeeker s in settlers)
            {
                if (result[s.Id].Length > 0) continue;
                foreach (HomeBed b in beds)
                    if (!taken.Contains(b.Id)) pairs.Add(new BedChoice(ColonyRules.Distance(s.At, b.At), s, b));
            }
            foreach (BedChoice pair in pairs.OrderBy(p => p.Distance).ThenBy(p => p.Settler.Id, System.StringComparer.Ordinal)
                                      .ThenBy(p => p.Bed.Id, System.StringComparer.Ordinal))
            {
                if (result[pair.Settler.Id].Length > 0 || taken.Contains(pair.Bed.Id)) continue;
                result[pair.Settler.Id] = pair.Bed.Id;
                taken.Add(pair.Bed.Id);
            }
            return result;
        }

        /// <summary>A settler and a free bed, and how far apart (no tuples: the game's runtime lacks them).</summary>
        private struct BedChoice
        {
            public BedChoice(float distance, HomeSeeker settler, HomeBed bed)
            {
                Distance = distance;
                Settler = settler;
                Bed = bed;
            }

            public float Distance { get; }
            public HomeSeeker Settler { get; }
            public HomeBed Bed { get; }
        }
    }
}
