using System.Collections.Generic;
using HarmonyLib;
using Jotunn.Managers;
using UnityEngine;
using VikingColonies.Buildings;
using VikingColonies.Core;
using Object = UnityEngine.Object;

namespace VikingColonies.Citizens
{
    /// <summary>
    /// The cairn a settler leaves where he fell: a chest with what he carried (opened with E), "Here lies …" when hovered.
    /// Built from the player's tombstone, so nothing can damage it, and the game destroys it once it's emptied and closed.
    /// Players told of the death get a map pin to it, which goes when they empty it.
    /// </summary>
    internal sealed class SettlerCairn : MonoBehaviour
    {
        public const string PrefabName = "vc_settler_cairn";
        private const string NameKey = SaveKeys.CairnName;
        private const string JobKey = SaveKeys.CairnJob;
        private const string CauseKey = SaveKeys.CairnCause;
        private const string CauseArgKey = SaveKeys.CairnCauseArg;
        private const string DayKey = SaveKeys.CairnDay;
        private const string SpawnKey = SaveKeys.CairnSpawn;
        /// <summary>A cairn that drifts further than this from where it was raised is put back (as the player's tombstone is).</summary>
        private const float MaxDrift = 4f;
        /// <summary>The cairn may settle a little from where he fell (it falls, or floats in water).</summary>
        private const float PinReach = 5f;

        private static readonly List<SettlerCairn> s_all = new List<SettlerCairn>();
        private static readonly AccessTools.FieldRef<InventoryGui, Container> s_openContainer =
            AccessTools.FieldRefAccess<InventoryGui, Container>("m_currentContainer");
        private static readonly AccessTools.FieldRef<Minimap, List<Minimap.PinData>> s_pins =
            AccessTools.FieldRefAccess<Minimap, List<Minimap.PinData>>("m_pins");

        private ZNetView _nview;
        private Rigidbody _body;

        public static GameObject Prefab { get; private set; }

        /// <summary>The cairns loaded on this computer.</summary>
        public static IReadOnlyList<SettlerCairn> All => s_all;

        public Container Container { get; private set; }

        public string SettlerName => Valid ? _nview.GetZDO().GetString(NameKey) : "";

        private bool Valid => _nview != null && _nview.IsValid();

        public static void Register()
        {
            GameObject player = PrefabManager.Instance.GetPrefab("Player");
            GameObject tombstone = player != null ? player.GetComponent<Player>()?.m_tombstone : null;
            if (tombstone == null)
            {
                Log.Error("No tombstone to build the settlers' cairn from; a settler who dies leaves none.");
                return;
            }
            GameObject go = PrefabManager.Instance.CreateClonedPrefab(PrefabName, tombstone);
            foreach (TombStone part in go.GetComponentsInChildren<TombStone>(true)) Object.DestroyImmediate(part);
            Container container = go.GetComponent<Container>();
            container.m_name = "$vc_cairn";
            container.m_width = Graves.Columns;
            container.m_height = Graves.Rows;
            container.m_autoDestroyEmpty = true;
            container.m_privacy = Container.PrivacySetting.Public;
            go.GetComponent<ZNetView>().m_persistent = true;
            go.AddComponent<SettlerCairn>();
            PrefabManager.Instance.AddPrefab(go);
            Prefab = go;
        }

        /// <summary>
        /// Puts his cairn down where he fell, holding what he carried; on the computer that ran him. Null when he carried
        /// nothing or there is no cairn to build. Anything that doesn't fit drops on the ground beside it.
        /// </summary>
        public static SettlerCairn Raise(Vector3 at, string name, string jobId, DeathCause cause, CarryLoad carry)
        {
            if (Prefab == null || !Graves.HasGrave(carry)) return null;
            GameObject go = Object.Instantiate(Prefab, at + Vector3.up * 0.2f, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            SettlerCairn cairn = go.GetComponent<SettlerCairn>();
            if (cairn == null || !cairn.Valid) return null;
            ZDO zdo = cairn._nview.GetZDO();
            zdo.Set(NameKey, name ?? "");
            zdo.Set(JobKey, jobId ?? "");
            zdo.Set(CauseKey, cause.Token ?? "");
            zdo.Set(CauseArgKey, cause.Arg ?? "");
            zdo.Set(SpawnKey, go.transform.position);
            zdo.Set(DayKey, EnvMan.instance != null && ZNet.instance != null ? EnvMan.instance.GetDay(ZNet.instance.GetTimeSeconds()) : 0);
            CarryLoad load = CarryLoad.Deserialize(carry.Serialize());   // his own load goes with his body
            var unknown = new List<string>();
            ItemTransfer.Deposit(load, new InventoryStore(cairn.Container.GetInventory()), null, unknown);
            foreach (string item in unknown) Log.Warn($"Unknown item '{item}' in {name}'s load; it isn't in the cairn.");
            foreach (string item in load.ItemNames) DropBeside(go.transform.position, item, load.Count(item));
            if (cairn.Container.GetInventory().NrOfItems() == 0)   // nothing the game knows went in: no cairn, no pin
            {
                ZNetScene.instance.Destroy(go);
                return null;
            }
            return cairn;
        }

        /// <summary>"Here lies Ingrid the Lumberjack: killed by Greydwarf, day 37" in this player's words.</summary>
        public string Line()
        {
            if (!Valid) return "";
            ZDO zdo = _nview.GetZDO();
            string who = Citizen.DisplayNameFor(zdo.GetString(NameKey), zdo.GetString(JobKey));
            string cause = DeathMessages.CauseText(new DeathCause(zdo.GetString(CauseKey), zdo.GetString(CauseArgKey)));
            return string.Format(Localization.instance.Localize("$vc_cairn_hover"), who, cause, zdo.GetInt(DayKey));
        }

        /// <summary>A pin on this player's map to the cairn of a settler they were told had died.</summary>
        public static void PinOnMap(Vector3 at, string settlerName)
        {
            if (Minimap.instance == null) return;
            Minimap.instance.AddPin(at, Minimap.PinType.Death, Graves.PinName(settlerName), true, false);
        }

        /// <summary>This player's pin to that settler's cairn near here, if they have one.</summary>
        public static Minimap.PinData PinNear(Vector3 at, string settlerName)
        {
            if (Minimap.instance == null) return null;
            string pinName = Graves.PinName(settlerName);
            foreach (Minimap.PinData pin in s_pins(Minimap.instance))
                if (pin.m_name == pinName && Utils.DistanceXZ(pin.m_pos, at) <= PinReach) return pin;
            return null;
        }

        private void Awake()
        {
            _nview = GetComponent<ZNetView>();
            Container = GetComponent<Container>();
            s_all.Add(this);
            // The player's tombstone does this too: the physics mustn't fling it out of a wall or a slope.
            _body = GetComponent<Rigidbody>();
            if (_body != null)
            {
                _body.maxDepenetrationVelocity = 1f;
                _body.solverIterations = 10;
            }
            InvokeRepeating(nameof(KeepInPlace), 1f, 1f);
        }

        /// <summary>On its owner: back where it was raised if it drifted off (water, a slope), up if it ended underground.</summary>
        private void KeepInPlace()
        {
            if (!Valid || !_nview.IsOwner()) return;
            Vector3 spawn = _nview.GetZDO().GetVec3(SpawnKey, Vector3.zero), at = transform.position;
            if (spawn != Vector3.zero && Utils.DistanceXZ(at, spawn) > MaxDrift) at = spawn + Vector3.up * 0.3f;
            float ground = ZoneSystem.instance.GetGroundHeight(at);
            if (at.y < ground - 1f) at.y = ground + 0.3f;
            if (at == transform.position) return;
            transform.position = at;
            if (_body != null) _body.linearVelocity = Vector3.zero;
        }

        private void OnDestroy() => s_all.Remove(this);

        private static void DropBeside(Vector3 at, string item, int amount)
        {
            GameObject prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(item) : null;
            ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
            if (drop == null || amount <= 0) return;
            int perStack = Mathf.Max(1, drop.m_itemData.m_shared.m_maxStackSize);
            for (int left = amount; left > 0; left -= perStack)
            {
                GameObject dropped = Object.Instantiate(prefab, at + Vector3.up + Random.insideUnitSphere * 0.5f, Quaternion.identity);
                dropped.GetComponent<ItemDrop>()?.SetStack(Mathf.Min(left, perStack));
            }
        }

        [HarmonyPatch]
        private static class HoverLineHook
        {
            [HarmonyPatch(typeof(Container), nameof(Container.GetHoverText)), HarmonyPostfix]
            private static void HoverLine(Container __instance, ref string __result)
            {
                SettlerCairn cairn = __instance.GetComponent<SettlerCairn>();
                if (cairn != null) __result = cairn.Line() + "\n" + __result;
            }
        }

        /// <summary>This player closes a cairn they emptied: their pin to it goes.</summary>
        [HarmonyPatch]
        private static class ClosingCairnHook
        {
            [HarmonyPatch(typeof(InventoryGui), "CloseContainer"), HarmonyPrefix]
            private static void ClosingCairn(InventoryGui __instance)
            {
                Container open = s_openContainer(__instance);
                SettlerCairn cairn = open != null ? open.GetComponent<SettlerCairn>() : null;
                if (cairn == null || open.GetInventory().NrOfItems() > 0) return;
                Minimap.PinData pin = PinNear(cairn.transform.position, cairn.SettlerName);
                if (pin != null) Minimap.instance.RemovePin(pin);
            }
        }
    }
}
