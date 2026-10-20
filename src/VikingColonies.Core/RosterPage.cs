using System;
using System.Collections.Generic;
using System.Linq;

namespace VikingColonies.Core
{
    /// <summary>
    /// The Town Hall's Citizens tab: the roster sorted by name in pages, and the citizen the player chose kept chosen
    /// by his id while the list changes (dropped once he's gone: died or left).
    /// </summary>
    public static class RosterPage
    {
        public static IReadOnlyList<RosterEntry> Rows(IReadOnlyList<RosterEntry> entries, int page, int perPage)
        {
            if (entries == null || perPage <= 0 || page < 0) return new List<RosterEntry>();
            return entries.Where(e => e != null).OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                          .Skip(page * perPage).Take(perPage).ToList();
        }

        /// <summary>How many pages (at least one).</summary>
        public static int Pages(int count, int perPage) => perPage <= 0 ? 1 : Math.Max(1, (count + perPage - 1) / perPage);

        /// <summary>A page that exists: one beyond the end comes back to the last.</summary>
        public static int ClampPage(int page, int count, int perPage) => Math.Max(0, Math.Min(page, Pages(count, perPage) - 1));

        /// <summary>The chosen citizen's id while he's still on the roster; "" once he's gone.</summary>
        public static string KeepChoice(string chosenId, IReadOnlyList<RosterEntry> entries) =>
            !string.IsNullOrEmpty(chosenId) && entries != null && entries.Any(e => e != null && e.CitizenId == chosenId) ? chosenId : "";
    }
}
