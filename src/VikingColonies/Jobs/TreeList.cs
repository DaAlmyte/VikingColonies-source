using System.Collections.Generic;
using VikingColonies.Buildings;
using VikingColonies.Core;

namespace VikingColonies.Jobs
{
    /// <summary>A Lumberjack's Hut's Trees list (0.22): the game's trees by kind, its level, its replanting list and saved choices.</summary>
    internal static class TreeList
    {
        public static List<TreeLine> For(ColonyBuilding hut)
        {
            TreeCatalog catalog = TreeCatalogBuilder.Catalog;
            WorkerModule worker = hut != null ? hut.GetComponent<WorkerModule>() : null;
            return TreeChoice.Lines(catalog.Trees, ReplantList.For(hut), catalog.SaplingsOfTree,
                hut != null ? hut.Level : 1, worker != null ? (ICollection<string>)worker.Unfelled : new HashSet<string>());
        }
    }
}
