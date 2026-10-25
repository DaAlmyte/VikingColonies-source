using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using VikingColonies.Buildings;
using VikingColonies.Citizens;
using VikingColonies.Core;

namespace VikingColonies.Gui
{
    /// <summary>
    /// The Keep in stock tab (worker polish part 2, 0.21; MineColonies' minimum stock), on every hut's window and the Town
    /// Hall's: "Keep in stock: 2 of 5", then one row per entry with what the chest holds of what it keeps, − / + (by 5,
    /// with Shift by a whole stack) and ×, and Add… for the list of what the colony's chests hold.
    /// </summary>
    internal sealed class StockTab
    {
        private const int RowCount = 6;
        private const float RowsTop = -190f, RowStep = 34f;

        private readonly Func<ColonyBuilding> _building;
        private readonly Action<ColonyBuilding> _openAdd;
        private readonly List<GameObject> _controls = new List<GameObject>();
        private readonly Text[] _labels = new Text[RowCount];
        private readonly Button[] _less = new Button[RowCount], _more = new Button[RowCount], _remove = new Button[RowCount];
        private readonly string[] _items = new string[RowCount];
        private Text _header, _message, _page;
        private Button _add, _prev, _next, _takeRest;
        private int _pageIndex;

        /// <param name="building">The window's building.</param>
        /// <param name="openAdd">Opens the Add… list (the window closes itself first, as for its other lists).</param>
        public StockTab(Func<ColonyBuilding> building, Action<ColonyBuilding> openAdd)
        {
            _building = building;
            _openAdd = openAdd;
        }

        public IReadOnlyList<GameObject> Controls => _controls;

        public void Build(Transform root, float width, float height)
        {
            _controls.Clear();   // built again after a scene change
            _header = UiKit.Label(root, "", -105f, width - 60f, 28f, 20);
            Text hint = UiKit.Label(root, L("$vc_stock_hint"), -137f, width - 60f, 46f, 15);
            _controls.Add(_header.gameObject);
            _controls.Add(hint.gameObject);
            float right = width / 2f - 30f;
            for (int i = 0; i < RowCount; i++)
            {
                int row = i;
                float y = RowsTop - RowStep * i;
                _labels[i] = UiKit.Label(root, "", y - 2f, width - 210f, 30f, 15);
                RectTransform rect = _labels[i].GetComponent<RectTransform>();
                rect.anchoredPosition = new Vector2(rect.anchoredPosition.x - 75f, rect.anchoredPosition.y);
                _less[i] = UiKit.TopButton(root, "−", right - 110f, y, 40f, 30f, () => Step(row, false));
                _more[i] = UiKit.TopButton(root, "+", right - 64f, y, 40f, 30f, () => Step(row, true));
                _remove[i] = UiKit.TopButton(root, "×", right - 18f, y, 40f, 30f, () => Remove(row));
                _controls.AddRange(new[] { _labels[i].gameObject, _less[i].gameObject, _more[i].gameObject, _remove[i].gameObject });
            }
            _add = UiKit.TopButton(root, L("$vc_stock_add"), 0f, RowsTop - RowStep * RowCount - 8f, 160f, 32f, OnAdd);
            // "Couriers take the rest" (1.5, part 3), beside Add… in a colony with a storehouse.
            _takeRest = UiKit.TopButton(root, "", 95f, RowsTop - RowStep * RowCount - 8f, 210f, 32f, OnTakeRest);
            UiKit.Fitted(_takeRest.GetComponentInChildren<Text>());
            _message = UiKit.Fitted(UiKit.Label(root, "", RowsTop - RowStep * RowCount - 46f, width - 60f, 24f, 15, false, TextAnchor.MiddleCenter));
            _prev = UiKit.Button(root, L("$vc_prev"), -140f, 70f, 110f, 32f, () => Turn(-1));
            _next = UiKit.Button(root, L("$vc_next"), 140f, 70f, 110f, 32f, () => Turn(1));
            _page = UiKit.Label(root, "", -(height - 86f), 120f, 30f, 16, align: TextAnchor.MiddleCenter);
            _controls.AddRange(new[] { _add.gameObject, _takeRest.gameObject, _message.gameObject, _prev.gameObject, _next.gameObject, _page.gameObject });
        }

        /// <summary>Each opening starts on the first page.</summary>
        public void Reset()
        {
            _pageIndex = 0;
            if (_message != null) _message.text = "";
        }

        public void Refresh()
        {
            ColonyBuilding building = _building();
            StockModule stock = building != null ? building.Stock : null;
            if (stock == null || building.Storage == null) return;
            bool mayManage = ColonyPermissions.LocalMayManage(building);
            StockList list = stock.List;
            int max = stock.MaxEntries;
            _header.text = string.Format(L("$vc_stock_count"), list.Entries.Count, max);
            Dictionary<string, int> contents = building.Storage.Contents();
            int pages = ListPage.Pages(list.Entries.Count, RowCount);
            _pageIndex = ListPage.Clamp(_pageIndex, list.Entries.Count, RowCount);
            IReadOnlyList<KeepEntry> shown = ListPage.Slice(list.Entries, _pageIndex, RowCount);
            for (int i = 0; i < RowCount; i++)
            {
                bool on = i < shown.Count;
                _items[i] = on ? shown[i].Item : "";
                foreach (Button b in new[] { _less[i], _more[i], _remove[i] }) b.gameObject.SetActive(on);
                _labels[i].gameObject.SetActive(on || (i == 0 && list.Entries.Count == 0));
                if (!on)
                {
                    if (i == 0 && list.Entries.Count == 0) _labels[0].text = L("$vc_stock_none");
                    continue;
                }
                KeepEntry entry = shown[i];
                int held = StockRules.Held(contents, list, entry.Item, StorageModule.IsFood);
                string count = held < entry.Amount ? $"<color=orange>{held}</color> / {entry.Amount}" : $"{held} / {entry.Amount}";
                _labels[i].text = $"{EntryName(entry.Item)}: {count}";
                _less[i].interactable = mayManage && entry.Amount > StockList.MinAmount;
                _more[i].interactable = mayManage && entry.Amount < StockList.MaxAmount;
                _remove[i].interactable = mayManage;
            }
            bool full = list.Entries.Count >= max;
            _add.interactable = mayManage && !full;
            bool takeRest = ShowsTakeRest(building);
            _takeRest.gameObject.SetActive(takeRest);
            RectTransform addRect = _add.GetComponent<RectTransform>();
            addRect.anchoredPosition = new Vector2(takeRest ? -110f : 0f, addRect.anchoredPosition.y);
            if (takeRest)
            {
                UiKit.SetLabel(_takeRest, L(stock.CouriersTakeRest ? "$vc_stock_take_rest_on" : "$vc_stock_take_rest_off"));
                _takeRest.interactable = mayManage;
            }
            string fullText = string.Format(L("$vc_stock_full"), max, building.IsTownHall ? 1 : building.Level);
            if (full) _message.text = fullText;
            else if (_message.text == fullText) _message.text = "";
            bool paged = pages > 1;
            _prev.gameObject.SetActive(paged);
            _next.gameObject.SetActive(paged);
            _prev.interactable = _pageIndex > 0;
            _next.interactable = _pageIndex < pages - 1;
            _page.text = paged ? $"{_pageIndex + 1} / {pages}" : "";
        }

        public void ShowMessage(string text)
        {
            if (_message != null) _message.text = text;
        }

        /// <summary>"Food (any)", or the item's name in the player's language.</summary>
        internal static string EntryName(string item) => item == StockList.AnyFood ? L("$vc_stock_any_food") : GrowWindow.ItemName(item);

        /// <summary>A whole stack of the item (Food (any): 10).</summary>
        internal static int StackOf(string item)
        {
            if (item == StockList.AnyFood) return StockList.AnyFoodStack;
            ItemDrop drop = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(item)?.GetComponent<ItemDrop>() : null;
            return drop != null ? Math.Max(1, drop.m_itemData.m_shared.m_maxStackSize) : StockList.StepBy;
        }

        /// <summary>Test tool (vc_ui keep): sets an entry's amount (0 takes it off; an item not on the list is added).</summary>
        internal static bool SetForTest(ColonyBuilding building, string item, int amount)
        {
            StockModule stock = building != null ? building.Stock : null;
            if (stock == null) return false;
            StockList list = stock.List;
            if (amount <= 0) list.Remove(item);
            else if (list.Kept(item) > 0) list.SetAmount(item, amount);
            else if (!list.Add(item, amount, stock.MaxEntries)) return false;
            stock.RequestSet(list);
            return true;
        }

        private void Step(int row, bool up)
        {
            ColonyBuilding building = _building();
            StockModule stock = building != null ? building.Stock : null;
            string item = _items[row];
            if (stock == null || string.IsNullOrEmpty(item)) return;
            StockList list = stock.List;
            bool shift = Keyboard.current != null && Keyboard.current.shiftKey.isPressed;
            list.SetAmount(item, StockList.Step(list.Kept(item), up, shift, StackOf(item)));
            stock.RequestSet(list);
        }

        private void Remove(int row)
        {
            ColonyBuilding building = _building();
            StockModule stock = building != null ? building.Stock : null;
            string item = _items[row];
            if (stock == null || string.IsNullOrEmpty(item)) return;
            StockList list = stock.List;
            list.Remove(item);
            stock.RequestSet(list);
        }

        /// <summary>A hut's tab, in a colony with a storehouse: not the Town Hall's, a Builder's Hut's or the storehouse's own.</summary>
        internal static bool ShowsTakeRest(ColonyBuilding building) =>
            building != null && CourierRules.HasTakeRestSwitch(building.IsTownHall, building.TypeId == BuildingTypes.BuilderHutId, building.IsStorehouse)
            && ColonyBuilding.FindStorehouse(building.ColonyId) != null;

        private void OnTakeRest()
        {
            ColonyBuilding building = _building();
            StockModule stock = building != null ? building.Stock : null;
            if (stock == null) return;
            bool on = !stock.CouriersTakeRest;
            stock.RequestSetTakeRest(on);
            ShowMessage(L(on ? "$vc_stock_take_rest_now_on" : "$vc_stock_take_rest_now_off"));
        }

        private void OnAdd()
        {
            ColonyBuilding building = _building();
            if (building != null) _openAdd(building);
        }

        private void Turn(int by)
        {
            _pageIndex += by;
            Refresh();
        }

        private static string L(string token) => Localization.instance.Localize(token);
    }
}
