using System.Collections.Generic;
using UnityEngine;
using VikingColonies.Config;
using VikingColonies.Construction;
using VikingColonies.Core;

namespace VikingColonies.Buildings
{
    /// <summary>
    /// The Watchtower (1.1): once it is finished, in a colony whose Town Hall stands, its circle (cut down to
    /// <c>Colony.MaxReach</c>) is part of the colony's area, and the colony's station on it reaches as far, so the game lets
    /// huts be placed by it. Worked out from saved building data, the same on every computer.
    /// </summary>
    public class WatchtowerModule : MonoBehaviour
    {
        private const float TickSeconds = 2f;

        private static readonly List<WatchtowerModule> s_all = new List<WatchtowerModule>();

        private const string RpcSetRole = "vc_SetGuardRole";
        private const string RpcSetOrders = "vc_SetGuardOrders", RpcSetPost = "vc_SetGuardPost", RpcClearPost = "vc_ClearGuardPost";
        private const string RpcMessage = "vc_TowerMessage";

        private ColonyBuilding _building;
        private ZNetView _nview;
        private ConstructionModule _construction;
        private CraftingStation _station;
        private SphereCollider _area;
        private float _timer;

        public static IReadOnlyList<WatchtowerModule> All => s_all;

        public ColonyBuilding Building => _building;

        /// <summary>Its guard's patrol and the circle it adds to the colony, by its level.</summary>
        public float Radius => GuardLevels.Radius(_building != null ? _building.Level : 1);

        /// <summary>
        /// Its circle of its colony's area now, cut down to <c>Colony.MaxReach</c>; null while it adds none (not finished, in no
        /// colony, its Town Hall gone, or too near the edge). Being upgraded or repaired, it still stands (review of 1.1).
        /// </summary>
        public AreaCircle? Circle
        {
            get
            {
                if (_building == null || !_building.IsValid || _building.ColonyId.Length == 0 || !_building.HasColonyPos) return null;
                if (ColonyGrace.IsLost(_building.HallLostAt)) return null;
                if (_construction != null && !_construction.IsBuilt && !ColonyArea.TowerStands(_construction.Order)) return null;
                Vector3 hall = _building.ColonyPos;
                return ColonyArea.TowerCircle(new GroundPoint(hall.x, hall.z), new GroundPoint(transform.position.x, transform.position.z), Radius,
                    ColonyConfig.MaxReach.Value);
            }
        }

        /// <summary>Monsters don't spring up within its circle now (vc_guardtest).</summary>
        public bool StopsSpawns => _area != null && _area.gameObject.activeSelf;

        /// <summary>Its circle is part of its colony's area now.</summary>
        public bool Counts => Circle != null;

        /// <summary>Its guards fight up close or shoot (1.2), as the colony's managers set it; warriors to start.</summary>
        public GuardRole Role
        {
            get
            {
                int role = _nview != null && _nview.IsValid() ? _nview.GetZDO().GetInt(SaveKeys.GuardRole, 0) : 0;
                return role == (int)GuardRole.Archer ? GuardRole.Archer : GuardRole.Warrior;
            }
        }

        public void RequestSetRole(GuardRole role)
        {
            if (_nview != null && _nview.IsValid()) _nview.InvokeRPC(RpcSetRole, (int)role);
        }

        /// <summary>Its guards walk rounds or hold their post (1.3), as the colony's managers set it; rounds to start.</summary>
        public GuardOrders Orders =>
            _nview != null && _nview.IsValid() && _nview.GetZDO().GetInt(SaveKeys.GuardOrders, 0) == (int)GuardOrders.HoldPost ? GuardOrders.HoldPost : GuardOrders.Patrol;

        /// <summary>Where its guards hold their post (1.3), as a manager chose it; null: in front of the tower.</summary>
        public Vector3? PostAt
        {
            get
            {
                Vector3 at = _nview != null && _nview.IsValid() ? _nview.GetZDO().GetVec3(SaveKeys.GuardPost, Vector3.zero) : Vector3.zero;
                return at == Vector3.zero ? (Vector3?)null : at;
            }
        }

        public void RequestSetOrders(GuardOrders orders)
        {
            if (_nview != null && _nview.IsValid()) _nview.InvokeRPC(RpcSetOrders, (int)orders);
        }

        /// <summary>The post where the asker stands (refused past the tower's patrol circle).</summary>
        public void RequestSetPost(Vector3 at)
        {
            if (_nview != null && _nview.IsValid()) _nview.InvokeRPC(RpcSetPost, at);
        }

        public void RequestClearPost()
        {
            if (_nview != null && _nview.IsValid()) _nview.InvokeRPC(RpcClearPost);
        }

        private void RPC_SetOrders(long sender, int orders)
        {
            if (!_nview.IsOwner() || !ColonyPermissions.SenderMayManage(_building, sender)) return;
            _nview.GetZDO().Set(SaveKeys.GuardOrders, orders == (int)GuardOrders.HoldPost ? (int)GuardOrders.HoldPost : (int)GuardOrders.Patrol);
            Log.Info($"watchtower {_building.Id}: its guards {(orders == (int)GuardOrders.HoldPost ? "hold their post" : "walk their rounds")} now");
        }

        private void RPC_SetPost(long sender, Vector3 at)
        {
            if (!_nview.IsOwner() || !ColonyPermissions.SenderMayManage(_building, sender)) return;
            float from = Utils.DistanceXZ(at, transform.position);
            if (!GuardPosts.MayPost(from, Radius))
            {
                _nview.InvokeRPC(sender, RpcMessage, string.Format("$vc_post_too_far {0:0} m", Radius));
                return;
            }
            _nview.GetZDO().Set(SaveKeys.GuardPost, at);
            _nview.InvokeRPC(sender, RpcMessage, "$vc_post_set");
            Log.Info($"watchtower {_building.Id}: its guards' post is at ({at.x:0.0}, {at.z:0.0}), {from:0.0} m from it");
        }

        private void RPC_ClearPost(long sender)
        {
            if (!_nview.IsOwner() || !ColonyPermissions.SenderMayManage(_building, sender)) return;
            _nview.GetZDO().Set(SaveKeys.GuardPost, Vector3.zero);
            _nview.InvokeRPC(sender, RpcMessage, "$vc_post_cleared");
        }

        private void RPC_Message(long sender, string text)
        {
            if (Player.m_localPlayer != null) Player.m_localPlayer.Message(MessageHud.MessageType.Center, Localization.instance.Localize(text));
        }

        /// <summary>On the tower's owner: a colony manager's choice of role.</summary>
        private void RPC_SetRole(long sender, int role)
        {
            if (!_nview.IsOwner() || !ColonyPermissions.SenderMayManage(_building, sender)) return;
            _nview.GetZDO().Set(SaveKeys.GuardRole, role == (int)GuardRole.Archer ? (int)GuardRole.Archer : (int)GuardRole.Warrior);
            Log.Info($"watchtower {_building.Id}: its guards are {(role == (int)GuardRole.Archer ? "archers" : "warriors")} now");
        }

        private void Awake()
        {
            _building = GetComponent<ColonyBuilding>();
            _construction = GetComponent<ConstructionModule>();
            _station = GetComponent<CraftingStation>();
            _nview = GetComponent<ZNetView>();
            if (_nview == null || !_nview.IsValid()) return;   // the build-preview ghost
            s_all.Add(this);
            // No monsters spring up within its circle (1.3): the game's own base area, as a workbench's or a fire's.
            var go = new GameObject("vc_tower_area") { layer = LayerMask.NameToLayer("character_trigger") };
            go.SetActive(false);
            go.transform.SetParent(transform, false);
            _area = go.AddComponent<SphereCollider>();
            _area.isTrigger = true;
            go.AddComponent<EffectArea>().m_type = EffectArea.Type.PlayerBase;
            _nview.Register<int>(RpcSetRole, RPC_SetRole);
            _nview.Register<int>(RpcSetOrders, RPC_SetOrders);
            _nview.Register<Vector3>(RpcSetPost, RPC_SetPost);
            _nview.Register(RpcClearPost, RPC_ClearPost);
            _nview.Register<string>(RpcMessage, RPC_Message);
        }

        private void OnDestroy() => s_all.Remove(this);

        private void Update()
        {
            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _timer = TickSeconds;
            if (_station == null) return;
            AreaCircle? circle = Circle;
            _station.m_rangeBuild = circle != null ? ColonyRules.HutBuildRange(circle.Value.Radius) : 0f;
            _station.m_discoverRange = circle != null ? circle.Value.Radius : 0f;
            if (_area == null) return;
            bool stops = circle != null && ColonyConfig.TowersStopSpawns.Value;
            if (stops) _area.radius = circle.Value.Radius;
            if (_area.gameObject.activeSelf != stops) _area.gameObject.SetActive(stops);
        }
    }
}
