using System;
using System.Collections.Generic;
using System.Linq;

namespace VikingColonies.Core
{
    /// <summary>
    /// A settler's calling (settler life, 0.16): the trade he's good at (a job id), or none. In it he works faster.
    /// Newcomers often bring the trade an empty hut needs; the first settlers bring a builder.
    /// </summary>
    public static class Callings
    {
        /// <summary>No calling: a jack of all trades.</summary>
        public const string None = "none";
        /// <summary>How often a newcomer brings an empty hut's trade.</summary>
        public const double OpenHutChance = 0.6;

        public static string Pick(Random rng, IReadOnlyList<string> jobs, IReadOnlyList<string> openHutJobs)
        {
            if (openHutJobs != null && openHutJobs.Count > 0 && rng.NextDouble() < OpenHutChance)
                return openHutJobs[rng.Next(openHutJobs.Count)];
            return Any(rng, jobs);
        }

        /// <summary>The first settlers: a builder, then any trades (the colony's first hut is a Builder's Hut).</summary>
        public static IReadOnlyList<string> Founders(Random rng, int count, IReadOnlyList<string> jobs)
        {
            var callings = new List<string>();
            for (int i = 0; i < count; i++)
                callings.Add(i == 0 && jobs.Contains("builder") ? "builder" : Any(rng, jobs));
            return callings;
        }

        /// <summary>For a settler from before 0.16: from his id, always the same.</summary>
        public static string FromId(string id, IReadOnlyList<string> jobs)
        {
            if (jobs == null || jobs.Count == 0) return None;
            int pick = (int)(Hash(id ?? "") % (uint)(jobs.Count + 1));
            return pick == jobs.Count ? None : jobs[pick];
        }

        /// <summary>He works in his calling.</summary>
        public static bool Matches(string calling, string job) =>
            !string.IsNullOrEmpty(job) && !string.IsNullOrEmpty(calling) && calling != None && calling == job;

        /// <summary>One in five has none; the others any job alike.</summary>
        private static string Any(Random rng, IReadOnlyList<string> jobs)
        {
            if (jobs == null || jobs.Count == 0 || rng.Next(5) == 0) return None;
            return jobs[rng.Next(jobs.Count)];
        }

        internal static uint Hash(string text)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (char c in text)
                {
                    hash ^= c;
                    hash *= 16777619;
                }
                hash ^= hash >> 13;
                hash *= 0x5bd1e995;
                hash ^= hash >> 15;
                return hash;
            }
        }
    }

    /// <summary>A settler's trait (0.16): one each, with one plain effect.</summary>
    public enum Trait { Hardworking, Lazy, Cheerful, Grumpy, Chatty, Quiet, Brave, Timid }

    public static class Traits
    {
        private static readonly Trait[] s_all = (Trait[])Enum.GetValues(typeof(Trait));

        /// <summary>From his id: always the same.</summary>
        public static Trait FromId(string id) => s_all[(int)(Callings.Hash("trait:" + (id ?? "")) % (uint)s_all.Length)];

        public static float WorkFactor(Trait trait) => trait == Trait.Hardworking ? 1.1f : trait == Trait.Lazy ? 0.9f : 1f;

        /// <summary>How near a monster comes before he runs: a brave one waits, a timid one runs early.</summary>
        public static float FleeRange(Trait trait, float normal) => trait == Trait.Brave ? 6f : trait == Trait.Timid ? 20f : normal;

        /// <summary>His free time by his nature: the chatty talk twice as often, the quiet never and sit more, the lazy only sit.</summary>
        public static int LeisureWeight(Trait trait, Leisure leisure, int weight)
        {
            switch (trait)
            {
                case Trait.Chatty: return leisure == Leisure.Chat ? weight * 2 : weight;
                case Trait.Quiet:
                    if (leisure == Leisure.Chat) return 0;
                    return leisure == Leisure.Seat || leisure == Leisure.Fire ? (int)Math.Ceiling(weight * 1.5) : weight;
                case Trait.Lazy:
                    if (leisure == Leisure.TendFire || leisure == Leisure.CarryStock) return weight / 2;   // a chore: half as often (0.18, 0.21)
                    return leisure == Leisure.Stroll || leisure == Leisure.Chat ? 0 : weight;
                default: return weight;
            }
        }

        /// <summary>How long between his lines of small talk, compared with others: the chatty half, the quiet twice.</summary>
        public static float TalkEvery(Trait trait) => trait == Trait.Chatty ? 0.5f : trait == Trait.Quiet ? 2f : 1f;

        public static string Name(Trait trait) => trait.ToString().ToLowerInvariant();

        public static Trait Parse(string name, Trait otherwise)
        {
            foreach (Trait t in s_all)
                if (Name(t) == name) return t;
            return otherwise;
        }

        /// <summary>His trait in the players' words: "$vc_trait_cheerful".</summary>
        public static string Token(Trait trait) => "$vc_trait_" + Name(trait);
    }

    /// <summary>Where a settler came from (0.16): words for his story, the Saga and the details.</summary>
    public static class Origins
    {
        private static readonly string[] s_all =
        {
            "$vc_origin_coast", "$vc_origin_meadows_farm", "$vc_origin_forest_edge", "$vc_origin_burned_steading",
            "$vc_origin_swamps", "$vc_origin_mountains", "$vc_origin_nowhere", "$vc_origin_fishing_village",
            "$vc_origin_far_north", "$vc_origin_trader_cart",
        };

        public static IReadOnlyList<string> All => s_all;

        public static string Pick(Random rng) => s_all[rng.Next(s_all.Length)];

        public static string FromId(string id) => s_all[(int)(Callings.Hash("origin:" + (id ?? "")) % (uint)s_all.Length)];
    }

    /// <summary>How fast a settler works (0.16): hunger, his calling and his trait together, never below a tenth.</summary>
    public static class WorkPace
    {
        public static float Factor(bool hungry, float hungrySpeed, bool inCalling, float callingBonus, Trait trait, float mood = 1f)
        {
            float pace = Meals.WorkSpeed(hungry, hungrySpeed) * (inCalling ? 1f + Math.Max(0f, callingBonus) : 1f) * Traits.WorkFactor(trait)
                         * Math.Max(0f, mood);
            return Math.Max(Meals.SlowestWork, pace);
        }
    }

    /// <summary>A spot at the colony's edge a newcomer could walk in from.</summary>
    public struct ArrivalCandidate
    {
        public ArrivalCandidate(GroundPoint at, bool dry, bool reachable, float nearestPlayer, bool seen = false)
        {
            At = at;
            Dry = dry;
            Reachable = reachable;
            NearestPlayer = nearestPlayer;
            Seen = seen;
        }

        public GroundPoint At { get; }
        public bool Dry { get; }
        public bool Reachable { get; }
        public float NearestPlayer { get; }
        /// <summary>A player is looking that way (0.19): in front of him, near enough, nothing between.</summary>
        public bool Seen { get; }
    }

    /// <summary>Newcomers walk in from the colony's edge (0.16): out of sight, on dry ground with a way to the Town Hall.</summary>
    public static class ArrivalSpot
    {
        public const float Far = 35f;
        public const float Near = 25f;

        /// <summary>
        /// The valid spot out of every player's view farthest from them; with none out of view, the farthest valid one (0.19:
        /// the farthest was often right in front of the player who had placed the hall); -1 when none is valid.
        /// </summary>
        public static int Choose(IReadOnlyList<ArrivalCandidate> candidates)
        {
            int unseen = Farthest(candidates, true);
            return unseen >= 0 ? unseen : Farthest(candidates, false);
        }

        private static int Farthest(IReadOnlyList<ArrivalCandidate> candidates, bool unseenOnly)
        {
            int best = -1;
            for (int i = 0; i < candidates.Count; i++)
            {
                ArrivalCandidate c = candidates[i];
                if (!c.Dry || !c.Reachable || unseenOnly && c.Seen) continue;
                if (best < 0 || c.NearestPlayer > candidates[best].NearestPlayer) best = i;
            }
            return best;
        }

        /// <summary>
        /// Of the rings (far first), the valid spot farthest from every player in the first ring that has one; for a new Town
        /// Hall only (<paramref name="founding"/>: the game may not have worked out its paths out there yet) dry ground with
        /// no way in will do after that, again far ring first. The index runs over the rings one after the other; -1 when
        /// none fits and he appears by the Town Hall.
        /// </summary>
        public static int Choose(IReadOnlyList<IReadOnlyList<ArrivalCandidate>> rings, bool founding)
        {
            // Out of view first, in any ring (0.19), then in view; dry ground with no way in only for a new Town Hall.
            for (int pass = 0; pass < (founding ? 4 : 2); pass++)
            {
                bool dryWillDo = pass >= 2, unseenOnly = pass % 2 == 0;
                int offset = 0;
                foreach (IReadOnlyList<ArrivalCandidate> ring in rings)
                {
                    IReadOnlyList<ArrivalCandidate> tried = !dryWillDo ? ring
                        : ring.Select(c => new ArrivalCandidate(c.At, c.Dry, c.Dry, c.NearestPlayer, c.Seen)).ToList();
                    int pick = Farthest(tried, unseenOnly);
                    if (pick >= 0) return offset + pick;
                    offset += ring.Count;
                }
            }
            return -1;
        }

        public static IReadOnlyList<GroundPoint> Directions(GroundPoint hall, float radius, int count = 16)
        {
            var spots = new List<GroundPoint>();
            for (int i = 0; i < count; i++)
            {
                double angle = 2 * Math.PI * i / count;
                spots.Add(new GroundPoint(hall.X + (float)(Math.Cos(angle) * radius), hall.Z + (float)(Math.Sin(angle) * radius)));
            }
            return spots;
        }
    }

    /// <summary>
    /// How long a newcomer has spent walking in (0.16): only the time he was on his way counts, not breakfast, the evening
    /// or running from a monster on the way. After <see cref="GiveUpSeconds"/> of it he is placed by the Town Hall.
    /// </summary>
    public sealed class WalkInWatch
    {
        public const float GiveUpSeconds = 180f;

        public float Walked { get; private set; }

        public bool TimedOut => Walked >= GiveUpSeconds;

        public void Walk(float seconds) => Walked += Math.Max(0f, seconds);

        public void Reset() => Walked = 0f;
    }

    /// <summary>Clothes by trade (0.16), from the game's own items; the jobless wear rags.</summary>
    public static class Wardrobe
    {
        private static readonly string[] s_rags = { "ArmorRagsChest", "ArmorRagsLegs" };

        private static readonly Dictionary<string, string[]> s_byJob = new Dictionary<string, string[]>
        {
            ["lumberjack"] = new[] { "ArmorLeatherChest", "ArmorLeatherLegs" },
            ["builder"] = new[] { "ArmorLeatherChest", "ArmorLeatherLegs", "HelmetLeather" },
            ["farmer"] = new[] { "ArmorRagsChest", "ArmorLeatherLegs" },
            ["forager"] = new[] { "ArmorRagsChest", "ArmorTrollLeatherLegs" },
            ["guard"] = new[] { "ArmorLeatherChest", "ArmorLeatherLegs", "CapeDeerHide" },
        };

        public static string[] For(string jobId) => jobId != null && s_byJob.TryGetValue(jobId, out string[] items) ? items : s_rags;

        /// <summary>
        /// What he wears (1.2): his trade's clothes, but armour (slot → item) in place of the trade's piece in its slot.
        /// </summary>
        public static string[] Merge(IEnumerable<KeyValuePair<string, string>> tradeBySlot, IReadOnlyDictionary<string, string> armour)
        {
            var worn = new List<string>();
            foreach (KeyValuePair<string, string> piece in tradeBySlot ?? Enumerable.Empty<KeyValuePair<string, string>>())
                if (armour == null || !armour.ContainsKey(piece.Key)) worn.Add(piece.Value);
            if (armour != null) worn.AddRange(armour.Values);
            return worn.ToArray();
        }

        /// <summary>Every item any trade wears (to take off the old set).</summary>
        public static IEnumerable<string> AllItems => s_byJob.Values.SelectMany(v => v).Concat(s_rags).Distinct();
    }
}
