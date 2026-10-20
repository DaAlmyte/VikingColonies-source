using System.Collections.Generic;

namespace VikingColonies.Core
{
    /// <summary>
    /// An open build order a builder could take: his own hut's or not, when it was ordered, whether he set it aside, when
    /// the player marked it to be done first (0 for never), whether the player set it aside, and whether it waits for the
    /// others (the Town Hall's camp: a colony's first builder puts its huts up first).
    /// </summary>
    public struct OrderOption
    {
        public OrderOption(bool own, double created, bool setAside, double first = 0, bool paused = false, bool later = false)
        {
            Own = own;
            Created = created;
            SetAside = setAside;
            First = first;
            Paused = paused;
            Later = later;
        }

        public bool Own { get; }
        public double Created { get; }
        /// <summary>He moved on from it a short while ago (it waits on the player).</summary>
        public bool SetAside { get; }
        /// <summary>When the player pressed Do first (Town Hall › Work orders); 0 for never.</summary>
        public double First { get; }
        /// <summary>The player set it aside: nobody takes it until it's resumed.</summary>
        public bool Paused { get; }
        /// <summary>Taken after every other open order unless marked first (the Town Hall's camp, huts round 2).</summary>
        public bool Later { get; }
    }

    public static class OrderChoice
    {
        /// <summary>
        /// The order a builder takes: never one the player set aside, nor one he set aside himself (until its time is
        /// up); then the one the player marked first most recently, then his own hut's, then the oldest, an order that
        /// waits for the others (the camp) last. -1 when none.
        /// </summary>
        public static int Pick(IReadOnlyList<OrderOption> options)
        {
            int first = -1, own = -1, oldest = -1, later = -1;
            for (int i = 0; i < options.Count; i++)
            {
                OrderOption o = options[i];
                if (o.Paused || o.SetAside) continue;
                if (o.First > 0 && (first < 0 || o.First > options[first].First)) first = i;
                if (o.Own && own < 0) own = i;
                if (o.Later)
                {
                    if (later < 0 || o.Created < options[later].Created) later = i;
                }
                else if (oldest < 0 || o.Created < options[oldest].Created) oldest = i;
            }
            return first >= 0 ? first : own >= 0 ? own : oldest >= 0 ? oldest : later;
        }

        /// <summary>A builder leaves his order for another only when the player marked that one first after his own.</summary>
        public static bool ShouldSwitch(double currentFirst, double bestOtherFirst) => bestOtherFirst > currentFirst;

        /// <summary>
        /// An order he set aside (it waited on the player; <paramref name="blocked"/> while his five minutes run) stays aside
        /// unless the player pressed Do first after he set it aside.
        /// </summary>
        public static bool StillAside(bool blocked, double first, double firstWhenSetAside) => blocked && !(first > firstWhenSetAside);

        /// <summary>
        /// Whether he leaves his work for this order marked first: never for one he set aside himself until the player marks
        /// it again, even after his five minutes (review: he kept leaving good work for an order still waiting on the
        /// player). An idle builder may still go back to it once his five minutes are up.
        /// </summary>
        public static bool MaySwitchTo(double first, bool setAsideByHim, double firstWhenSetAside) =>
            first > 0 && (!setAsideByHim || first > firstWhenSetAside);
    }

    /// <summary>
    /// When a builder whose site waits on the player (a tree or rock above his level, a tool, materials, something in the
    /// way) lets it go for a while, so one such site never holds him for good.
    /// </summary>
    public static class OrderPatience
    {
        /// <summary>How long he waits on the player, with nothing getting done, before he moves on.</summary>
        public const double WaitSeconds = 60;

        /// <summary>How long a site he moved on from is passed over.</summary>
        public const double AsideSeconds = 300;

        /// <summary>
        /// Whether he lets his site go for now: it has waited on the player for <paramref name="waitSeconds"/> with nothing
        /// getting done, and someone can get on with something — he has another order to take, or the site waits for a
        /// Builder's Hut level (<paramref name="needsLevel"/>; 0 for any other wait) the colony already has, so a better
        /// builder can take it over. With nothing else to do he stays, and carries on as soon as the player has helped.
        /// </summary>
        public static bool MoveOn(double sinceProgress, double waitSeconds, bool anotherOrder, int needsLevel, int highestBuilderHutLevel) =>
            sinceProgress >= waitSeconds && (anotherOrder || (needsLevel > 0 && highestBuilderHutLevel >= needsLevel));

        /// <summary>The highest level among the colony's Builder's Huts (level, has a builder) that have a builder; 0 for none.</summary>
        public static int BestStaffedLevel(IEnumerable<KeyValuePair<int, bool>> builderHuts)
        {
            int best = 0;
            foreach (KeyValuePair<int, bool> hut in builderHuts)
                if (hut.Value && hut.Key > best) best = hut.Key;
            return best;
        }
    }
}
