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

        /// <summary>
        /// Whether a builder may set their site aside at all (1.6, the player's pick): not their own hut before it first
        /// stands (its first build), as a MineColonies builder builds their own hut before anything else; any other site,
        /// and their own hut's later upgrades, as before. Their own unbuilt hut too (the review of 1.6) when the wait is one
        /// the player can't answer there: a Builder's Hut level (<paramref name="needsLevel"/>; it can't come before the hut
        /// stands), no support or a site out of reach (<paramref name="hopeless"/>); or when another site has everything it
        /// needs (<paramref name="otherReady"/>: the colony gets on with something).
        /// </summary>
        public static bool MayLeave(bool ownSite, OrderKind kind, int needsLevel = 0, bool hopeless = false, bool otherReady = false) =>
            !(ownSite && kind == OrderKind.Build) || needsLevel > 0 || hopeless || otherReady;

        /// <summary>The highest level among the colony's Builder's Huts (level, has a builder) that have a builder; 0 for none.</summary>
        public static int BestStaffedLevel(IEnumerable<KeyValuePair<int, bool>> builderHuts)
        {
            int best = 0;
            foreach (KeyValuePair<int, bool> hut in builderHuts)
                if (hut.Value && hut.Key > best) best = hut.Key;
            return best;
        }
    }

    /// <summary>
    /// A builder's note of the site they left for now (1.6: the player's 1.4.2 watch saw a builder leave a hut for the camp
    /// with no word why): the building's id and until when (game seconds), and after it <c>|level</c> when left only for a
    /// builder from a higher Builder's Hut; saved on the builder so every computer's hover can say "Waiting for Flint at the
    /// Lumberjack's Hut, working here meanwhile" (or that it waits for a higher-level builder).
    /// </summary>
    public static class AwayNote
    {
        /// <summary>The note's mark for a site left only so a builder from a higher Builder's Hut can take it (no other work).</summary>
        private const string LevelMark = "|level";

        /// <param name="forHigherLevel">Left only so a builder from a higher Builder's Hut can take it: they have no other
        /// work, so the hover doesn't say "working here meanwhile" (v16's review).</param>
        public static string Encode(string buildingId, double until, bool forHigherLevel = false) =>
            (buildingId ?? "") + "|" + until.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + (forHigherLevel ? LevelMark : "");

        /// <summary>Never throws; false for no note or a broken one.</summary>
        public static bool Decode(string note, out string buildingId, out double until) => Decode(note, out buildingId, out until, out _);

        /// <summary>Never throws; false for no note or a broken one. <paramref name="forHigherLevel"/>: see <see cref="Encode"/>.</summary>
        public static bool Decode(string note, out string buildingId, out double until, out bool forHigherLevel)
        {
            buildingId = "";
            until = 0;
            forHigherLevel = false;
            if (string.IsNullOrEmpty(note)) return false;
            if (note.EndsWith(LevelMark, System.StringComparison.Ordinal))
            {
                forHigherLevel = true;
                note = note.Substring(0, note.Length - LevelMark.Length);
            }
            int bar = note.LastIndexOf('|');
            if (bar <= 0) return false;
            buildingId = note.Substring(0, bar);
            return double.TryParse(note.Substring(bar + 1), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out until)
                && !double.IsNaN(until) && !double.IsInfinity(until);
        }

        /// <summary>The note shows until the site's time aside is up, and never longer than that (a clock put back).</summary>
        public static bool Shows(double now, double until) => now < until && until - now <= OrderPatience.AsideSeconds + 1;

        /// <summary>Until when a note made now shows: the site's time aside (<see cref="OrderPatience.AsideSeconds"/>).</summary>
        public static double Until(double now) => now + OrderPatience.AsideSeconds;
    }
}
