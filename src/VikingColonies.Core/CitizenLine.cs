using System.Linq;

namespace VikingColonies.Core
{
    /// <summary>One line of the Town Hall's citizen list: name — job, status @ workplace (all already localized).</summary>
    public static class CitizenLine
    {
        public static string Format(string name, string jobTitle, string status, string place)
        {
            string what;
            if (string.IsNullOrEmpty(jobTitle) || jobTitle == status) what = status;
            else if (string.IsNullOrEmpty(status)) what = jobTitle;
            else what = jobTitle + ", " + status;
            string line = "<b>" + name + "</b>  —  " + what;
            return string.IsNullOrEmpty(place) ? line : line + "  @ " + place;
        }

        /// <summary>
        /// A member out of sight here (0.19): "far away" while the roster saw him today (or doesn't know), "seen day {0}" when
        /// it last saw him on an earlier day.
        /// </summary>
        public static string SeenToken(int lastSeenDay, int today) =>
            lastSeenDay > 0 && lastSeenDay < today ? "$vc_seen_day" : "$vc_far_away";

        /// <summary>"Came today" / "Here 1 day." / "Here {0} days." (0.18: "Here 1 days." read wrong).</summary>
        public static string DaysToken(int days) => days <= 0 ? "$vc_came_today" : days == 1 ? "$vc_here_day" : "$vc_here_days";

        /// <summary>
        /// The Town Hall's two lines about one settler (0.16), from words already in the player's language: "Astrid: good
        /// at foraging, cheerful. From the coast." and "Here 23 days. Home: Longhouse bed 2."
        /// </summary>
        public static string[] Details(string name, string calling, string trait, string origin, string daysHere, string home)
        {
            string who = string.Join(", ", new[] { calling, trait }.Where(w => !string.IsNullOrEmpty(w)));
            string first = name + (who.Length > 0 ? ": " + who + "." : ".");
            if (!string.IsNullOrEmpty(origin)) first += " " + char.ToUpperInvariant(origin[0]) + origin.Substring(1) + ".";
            string second = string.Join(" ", new[] { daysHere, home }.Where(w => !string.IsNullOrEmpty(w)));
            return new[] { first, second };
        }
    }
}
