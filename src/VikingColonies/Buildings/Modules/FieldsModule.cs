using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VikingColonies.Colony;
using VikingColonies.Construction;
using VikingColonies.Core;

namespace VikingColonies.Buildings
{
    /// <summary>
    /// A Farm's fields (worker polish, 0.20): whether new fields join it by themselves, and which field markers are its. One
    /// field per level (<see cref="FieldAssignment.Capacity"/>); a Farm works only its fields (0.20.1).
    /// </summary>
    public class FieldsModule : MonoBehaviour
    {
        private const string RpcSetAuto = "vc_SetFieldsAuto";

        private ColonyBuilding _building;
        private ZNetView _nview;

        private void Awake()
        {
            _building = GetComponent<ColonyBuilding>();
            _nview = GetComponent<ZNetView>();
            if (_nview == null || !_nview.IsValid()) return;
            _nview.Register<bool>(RpcSetAuto, (sender, on) => Set(sender, SaveKeys.FieldsAuto, on));
        }

        private ZDO Zdo => _nview != null && _nview.IsValid() ? _nview.GetZDO() : null;

        /// <summary>New field markers in the colony join this Farm by themselves while it has room (on to start).</summary>
        public bool AutoAssign => Zdo?.GetBool(SaveKeys.FieldsAuto, true) ?? true;

        public int Capacity => FieldAssignment.Capacity(_building != null ? _building.Level : 1);

        /// <summary>Its field markers (loaded ones), by id.</summary>
        public List<FieldMarker> Fields()
        {
            string id = _building != null ? _building.Id : "";
            return FieldMarker.All.Where(m => m != null && m.IsValid && id.Length > 0 && m.FarmId == id).OrderBy(m => m.Id).ToList();
        }

        public void RequestSetAutoAssign(bool on) => _nview.InvokeRPC(RpcSetAuto, on);

        private void Set(long sender, string key, bool on)
        {
            if (_nview == null || !_nview.IsOwner()) return;
            if (!ColonyPermissions.SenderMayManage(_building, sender))
            {
                _nview.InvokeRPC(sender, WorkerModule.RpcMessage, "$vc_not_your_colony");
                return;
            }
            _nview.GetZDO().Set(key, on);
        }
    }
}
