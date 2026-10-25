using System.Collections.Generic;
using System.Linq;
using VikingColonies.Buildings;
using VikingColonies.Core;

namespace VikingColonies.Gui
{
    /// <summary>
    /// Keep in stock's Add… list (0.21): Food (any) first, then every item the colony's chests hold, by name; ticking one
    /// adds it to the building's list (a whole stack; Food (any) 10). What is on the list already is ticked and greyed,
    /// and a full list greys every line.
    /// </summary>
    internal sealed class StockAddWindow : TickListWindow
    {
        protected override string Title => "$vc_stock_add_title";
        protected override float Width => 480f;
        protected override string HintToken => "$vc_stock_add_hint";
        protected override float HintHeight => 46f;
        protected override float LineHeight => 30f;

        protected override List<TickRow> Rows()
        {
            ColonyBuilding building = Building;
            StockModule stock = building != null ? building.Stock : null;
            if (stock == null) return new List<TickRow>();
            bool mayManage = ColonyPermissions.LocalMayManage(building);
            StockList list = stock.List;
            bool full = list.Entries.Count >= stock.MaxEntries;
            var items = new HashSet<string>(list.Entries.Select(e => e.Item).Where(i => i != StockList.AnyFood));
            foreach (ColonyBuilding b in ColonyBuilding.All)
                if (b != null && b.IsValid && b.Storage != null && building.ColonyId.Length > 0 && b.ColonyId == building.ColonyId)
                    foreach (StorageModule store in b.Stores)   // a storehouse: its whole store (1.5)
                        items.UnionWith(store.Contents().Keys.Where(StorageModule.Carried));   // only what settlers carry: a sword would lose its quality
            var rows = new List<TickRow> { Row(StockList.AnyFood, list, mayManage, full) };
            rows.AddRange(items.Select(i => Row(i, list, mayManage, full)).OrderBy(r => r.Text, System.StringComparer.CurrentCultureIgnoreCase));
            return rows;
        }

        private static TickRow Row(string item, StockList list, bool mayManage, bool full)
        {
            bool kept = list.Kept(item) > 0;
            return new TickRow { Key = item, Text = StockTab.EntryName(item), Ticked = kept, Enabled = mayManage && !kept && !full };
        }

        protected override void SetTicked(string key, bool on)
        {
            StockModule stock = Building != null ? Building.Stock : null;
            if (!on || stock == null) return;
            StockList list = stock.List;
            if (list.Add(key, StockTab.StackOf(key), stock.MaxEntries)) stock.RequestSet(list);
        }
    }
}
