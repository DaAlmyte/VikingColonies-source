using System;
using System.Collections.Generic;
using System.Linq;

namespace VikingColonies.Core
{
    /// <summary>
    /// A long list in pages (a hut's tick lists: mods add crops, plants and trees beyond what one page shows). A page
    /// number beyond the end shows the last page.
    /// </summary>
    public static class ListPage
    {
        /// <summary>How many pages (at least one, so an empty list still has its page).</summary>
        public static int Pages(int count, int perPage) => perPage <= 0 ? 1 : Math.Max(1, (count + perPage - 1) / perPage);

        /// <summary>A page that exists.</summary>
        public static int Clamp(int page, int count, int perPage) => Math.Max(0, Math.Min(page, Pages(count, perPage) - 1));

        public static IReadOnlyList<T> Slice<T>(IReadOnlyList<T> rows, int page, int perPage)
        {
            if (rows == null || perPage <= 0) return new List<T>();
            return rows.Skip(Clamp(page, rows.Count, perPage) * perPage).Take(perPage).ToList();
        }
    }
}
