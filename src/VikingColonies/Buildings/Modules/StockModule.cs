using UnityEngine;
using VikingColonies.Core;

namespace VikingColonies.Buildings
{
    /// <summary>
    /// A colony building's Keep in stock list (worker polish part 2, 0.21; MineColonies' minimum stock): what its chest
    /// keeps for its own, so other settlers leave it and settlers with free time bring what's missing. Saved on the
    /// building (<see cref="SaveKeys.Keep"/>), changed through its owner.
    /// </summary>
    public class StockModule : MonoBehaviour
    {
        private const string RpcSetKeep = "vc_SetKeep";
        private const string RpcMessage = "vc_StockMessage";   // its own: only job huts have WorkerModule's (0.24)
        private const string RpcSetTakeRest = "vc_SetTakeRest";

        private ColonyBuilding _building;
        private ZNetView _nview;

        private void Awake()
        {
            _building = GetComponent<ColonyBuilding>();
            _nview = GetComponent<ZNetView>();
            if (_nview == null || !_nview.IsValid()) return;
            _nview.Register<string>(RpcSetKeep, OnSet);
            _nview.Register<string>(RpcMessage, OnMessage);
            _nview.Register<bool>(RpcSetTakeRest, OnSetTakeRest);
        }

        private ZDO Zdo => _nview != null && _nview.IsValid() ? _nview.GetZDO() : null;

        private bool IsTownHall => _building != null && _building.IsTownHall;

        /// <summary>The list as saved (a new copy each time: change it, then <see cref="RequestSet"/>).</summary>
        public StockList List => StockList.Decode(Zdo?.GetString(SaveKeys.Keep, "") ?? "", IsTownHall);

        /// <summary>5 entries per level; the Longhouse 10 per level; the Town Hall (always level 1) 10.</summary>
        public int MaxEntries
        {
            get
            {
                bool big = IsTownHall || (_building != null && _building.TypeId == BuildingTypes.LonghouseId);
                return StockList.MaxEntries(big, IsTownHall || _building == null ? 1 : _building.Level);
            }
        }

        /// <summary>
        /// "Couriers take the rest" (1.5, part 3): on unless the player turned it off. Off, couriers take nothing from its
        /// chest, neither its surplus nor for another building's need: it stays as the player arranged it.
        /// </summary>
        public bool CouriersTakeRest => !(Zdo != null && Zdo.GetBool(SaveKeys.CouriersLeave, false));

        public void RequestSetTakeRest(bool on)
        {
            if (_nview != null && _nview.IsValid()) _nview.InvokeRPC(RpcSetTakeRest, on);
        }

        /// <summary>Test tool (vc_storehousetest): the switch as if <paramref name="sender"/> asked for it.</summary>
        internal void SetTakeRestAsForTest(long sender, bool on) => OnSetTakeRest(sender, on);

        private void OnSetTakeRest(long sender, bool on)
        {
            if (_nview == null || !_nview.IsOwner()) return;
            if (!ColonyPermissions.SenderMayManage(_building, sender))
            {
                _nview.InvokeRPC(sender, RpcMessage, "$vc_not_your_colony");
                return;
            }
            _nview.GetZDO().Set(SaveKeys.CouriersLeave, !on);
        }

        public void RequestSet(StockList list)
        {
            if (_nview != null && _nview.IsValid() && list != null) _nview.InvokeRPC(RpcSetKeep, list.Encode());
        }

        /// <summary>On the asker's computer: why nothing changed, in the open window or on screen.</summary>
        private void OnMessage(long sender, string token)
        {
            Gui.Windows.Reply(Localization.instance.Localize(token));
        }

        /// <summary>Test tool (vc_workertest): a change as if <paramref name="sender"/> asked for it.</summary>
        internal void SetAsForTest(long sender, string encoded) => OnSet(sender, encoded);

        /// <summary>On the owner: a player's changed list, cut to what the building's level allows.</summary>
        private void OnSet(long sender, string encoded)
        {
            if (_nview == null || !_nview.IsOwner()) return;
            if (!ColonyPermissions.SenderMayManage(_building, sender))
            {
                _nview.InvokeRPC(sender, RpcMessage, "$vc_not_your_colony");
                return;
            }
            StockList asked = StockList.Decode(string.IsNullOrEmpty(encoded) ? "1|" : encoded, false);
            var list = new StockList();
            int max = MaxEntries;
            foreach (KeepEntry entry in asked.Entries) list.Add(entry.Item, entry.Amount, max);
            _nview.GetZDO().Set(SaveKeys.Keep, list.Encode());
        }
    }
}
