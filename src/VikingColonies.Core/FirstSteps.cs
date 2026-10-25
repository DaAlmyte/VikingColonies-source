namespace VikingColonies.Core
{
    /// <summary>
    /// The words a settler shows above his head (0.11): only a status that asks the player for help (materials or a tool,
    /// a full chest, a way he can't find, something in the way), or that he is hungry. Plain work shows nothing.
    /// </summary>
    public static class HelpWords
    {
        public const string HungryToken = "$vc_words_hungry";

        private static readonly string[] s_asking =
        {
            "$vc_status_needs", "$vc_status_storage_full", "$vc_status_cant_reach_hut", "$vc_status_cant_reach_site",
            "$vc_status_no_support", "$vc_status_in_the_way",
            "$vc_status_fighting_off", "$vc_status_driving_off",   // 1.5.0: what they do about a weak creature, said where it's seen
        };

        /// <summary>Statuses where "Hungry" isn't shown: asleep, sheltering or fleeing (review: a Longhouse at night was full of it).</summary>
        private static readonly string[] s_resting = { "$vc_status_sleeping", "$vc_status_sheltering", "$vc_status_fleeing" };

        /// <summary>Asleep, sheltering or fleeing: he greets nobody and makes no small talk (0.14).</summary>
        public static bool IsResting(string status) => status != null && System.Array.IndexOf(s_resting, status) >= 0;

        /// <summary>The status to show (it may carry its details, "$vc_status_needs 12 Wood"), the hungry words, or "".</summary>
        /// <summary>
        /// "Hungry" shows above his head for this many seconds in every <see cref="HungerEvery"/>, at his own moment by the
        /// world's clock (the same on every computer); his talk shows in between (0.18: shown all the time, it stuck over
        /// his head all day and through his chats). The hover always says it.
        /// </summary>
        public const double HungerFor = 8, HungerEvery = 30;

        public static bool HungerShows(double worldSeconds, string id)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (char c in id ?? "") hash = (hash ^ c) * 16777619;
                double phase = (worldSeconds + hash % 300 / 10.0) % HungerEvery;
                return phase < HungerFor;
            }
        }

        public static string Words(string status, bool hungry)
        {
            string s = status ?? "";
            foreach (string token in s_asking)
                if (s == token || s.StartsWith(token + " ", System.StringComparison.Ordinal)) return s;
            foreach (string token in s_resting)
                if (s == token) return "";
            return hungry ? HungryToken : "";
        }
    }

    /// <summary>When Hugin's tips come (0.11); each comes once per character, as the game's own tips.</summary>
    public static class TipRules
    {
        /// <summary>The first night the player spends near a colony that has settlers.</summary>
        public static bool NightNearColony(bool night, float distanceToTownHall, float colonyRadius, int settlers) =>
            night && settlers > 0 && distanceToTownHall <= colonyRadius;

        /// <summary>A job hut placed in a colony that has no Builder's Hut yet: nobody will put it up.</summary>
        public static bool NeedsBuilderTip(bool isBuilderHut, bool colonyHasBuilderHut) => !isBuilderHut && !colonyHasBuilderHut;

        /// <summary>
        /// The tips a hut placed just now gets, once it has joined its colony. One placed already built (creative mode,
        /// 1.5.0) gets the storehouse's, never the builder's: its words say the hut stands empty until a builder puts it up.
        /// </summary>
        public static PlacedTips TipsForPlaced(bool isBuilderHut, bool isStorehouse, bool colonyHasBuilderHut, bool placedBuilt)
        {
            PlacedTips tips = PlacedTips.None;
            if (!placedBuilt && NeedsBuilderTip(isBuilderHut, colonyHasBuilderHut)) tips |= PlacedTips.Builder;
            if (isStorehouse) tips |= PlacedTips.Storehouse;
            return tips;
        }
    }

    /// <summary>Hugin's tips for a hut placed just now (<see cref="TipRules.TipsForPlaced"/>).</summary>
    [System.Flags]
    public enum PlacedTips
    {
        None = 0,
        Builder = 1,
        Storehouse = 2,
    }
}
