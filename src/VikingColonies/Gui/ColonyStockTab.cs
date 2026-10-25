using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using VikingColonies.Buildings;
using VikingColonies.Core;

namespace VikingColonies.Gui
{
    /// <summary>
    /// The storehouse's Stock tab (1.5; MineColonies' warehouse list): what the colony's chests hold, most first, each line
    /// "40 Wood (most in the Lumberjack's Hut)", ten a page. A storehouse counts its whole store.
    /// </summary>
    internal sealed class ColonyStockTab
    {
        private const int RowCount = 10;
        private const float RowsTop = -140f, RowStep = 30f;

        private readonly Func<ColonyBuilding> _building;
        private readonly List<GameObject> _controls = new List<GameObject>();
        private readonly Text[] _labels = new Text[RowCount];
        private Text _header, _page;
        private Button _prev, _next;
        private int _pageIndex;

        public ColonyStockTab(Func<ColonyBuilding> building)
        {
            _building = building;
        }

        public IReadOnlyList<GameObject> Controls => _controls;

        public void Build(Transform root, float width, float height)
        {
            _controls.Clear();   // built again after a scene change
            _header = UiKit.Label(root, "", -105f, width - 60f, 28f, 20);
            _controls.Add(_header.gameObject);
            for (int i = 0; i < RowCount; i++)
            {
                _labels[i] = UiKit.Label(root, "", RowsTop - RowStep * i, width - 60f, 28f, 15);
                _controls.Add(_labels[i].gameObject);
            }
            _prev = UiKit.Button(root, L("$vc_prev"), -140f, 70f, 110f, 32f, () => Turn(-1));
            _next = UiKit.Button(root, L("$vc_next"), 140f, 70f, 110f, 32f, () => Turn(1));
            _page = UiKit.Label(root, "", -(height - 86f), 120f, 30f, 16, align: TextAnchor.MiddleCenter);
            _controls.AddRange(new[] { _prev.gameObject, _next.gameObject, _page.gameObject });
        }

        /// <summary>Each opening starts on the first page.</summary>
        public void Reset() => _pageIndex = 0;

        public void Refresh()
        {
            ColonyBuilding building = _building();
            if (building == null) return;
            List<StockLine> lines = Lines(building.ColonyId);
            _header.text = L("$vc_colony_stock_header");
            int pages = ListPage.Pages(lines.Count, RowCount);
            _pageIndex = ListPage.Clamp(_pageIndex, lines.Count, RowCount);
            IReadOnlyList<StockLine> shown = ListPage.Slice(lines, _pageIndex, RowCount);
            for (int i = 0; i < RowCount; i++)
            {
                bool on = i < shown.Count;
                _labels[i].gameObject.SetActive(on || (i == 0 && lines.Count == 0));
                if (on) _labels[i].text = Line(shown[i]);
                else if (i == 0 && lines.Count == 0) _labels[0].text = L("$vc_colony_stock_empty");
            }
            bool paged = pages > 1;
            _prev.gameObject.SetActive(paged);
            _next.gameObject.SetActive(paged);
            _prev.interactable = _pageIndex > 0;
            _next.interactable = _pageIndex < pages - 1;
            _page.text = paged ? $"{_pageIndex + 1} / {pages}" : "";
        }

        /// <summary>The colony's goods, one count per building (a storehouse's whole store as one), most first.</summary>
        internal static List<StockLine> Lines(string colonyId)
        {
            var chests = new List<ChestCount>();
            if (!string.IsNullOrEmpty(colonyId))
                foreach (ColonyBuilding b in ColonyBuilding.All)
                {
                    if (b == null || !b.IsValid || b.ColonyId != colonyId) continue;
                    var items = new Dictionary<string, int>();
                    foreach (StorageModule store in b.Stores)
                        foreach (KeyValuePair<string, int> item in store.Contents())
                            items[item.Key] = (items.TryGetValue(item.Key, out int n) ? n : 0) + item.Value;
                    if (items.Count > 0) chests.Add(new ChestCount(L(b.Type?.NameToken ?? ""), items));
                }
            return ColonyStock.Totals(chests);
        }

        /// <summary>"40 Wood (most in the Lumberjack's Hut)".</summary>
        internal static string Line(StockLine line) =>
            string.Format(L("$vc_colony_stock_line"), line.Total, GrowWindow.ItemName(line.Item), line.MostAt);

        private void Turn(int by)
        {
            _pageIndex += by;
            Refresh();
        }

        private static string L(string token) => Localization.instance.Localize(token);
    }
}
