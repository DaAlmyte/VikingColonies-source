using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VikingColonies.Citizens;
using VikingColonies.Config;
using VikingColonies.Construction;
using VikingColonies.Core;

namespace VikingColonies.Buildings
{
    /// <summary>
    /// A building's worker slot: hires/fires, auto-hires (its colony: <see cref="HutMembership"/>). Menu actions arrive as RPCs and are
    /// applied on the building's owner, which checks they're still valid.
    /// </summary>
    public class WorkerModule : MonoBehaviour
    {
        public enum WorkerState { None, Loaded, Elsewhere, Unknown }

        private const string AutoHireKey = SaveKeys.AutoHire;
        private const string UntickedKey = SaveKeys.Unticked;
        private const string RpcSetTicked = "vc_SetTicked";
        private const string RpcSetFell = "vc_SetFell";
        private const string RpcSetWoodLimit = "vc_SetWoodLimit";
        private const string PickUntickedKey = SaveKeys.Unpicked;
        private const string RpcSetPickTicked = "vc_SetPickTicked";
        private const string GrowUntickedKey = SaveKeys.Ungrown;
        private const string RpcSetGrowTicked = "vc_SetGrowTicked";
        private const string RpcHire = "vc_Hire";
        private const string RpcFire = "vc_Fire";
        private const string RpcSetAutoHire = "vc_SetAutoHire";
        internal const string RpcMessage = "vc_Message";   // shows a message to the player who asked (also used by the hut's building)
        private const float TickSeconds = 5f;
        private const float HireAnswerSeconds = 3f;

        private ColonyBuilding _building;
        private ZNetView _nview;
        private ConstructionModule _construction;
        private float _timer = 2f;
        private float _hirePendingUntil;   // a citizen was asked to work here; wait for its answer before asking another

        public string JobId => _building.Type?.JobId;
        public bool AutoHire => _nview != null && _nview.IsValid() && _nview.GetZDO().GetBool(AutoHireKey, true);

        /// <summary>A hut hires once its building stands; the Builder's Hut hires straight away (its builder builds it).</summary>
        internal bool MayHire => _building.TypeId == BuildingTypes.BuilderHutId || _construction == null || _construction.IsBuilt || _construction.IsUpgrading;

        private void Awake()
        {
            _building = GetComponent<ColonyBuilding>();
            _nview = GetComponent<ZNetView>();
            _construction = GetComponent<ConstructionModule>();
            if (_nview == null || !_nview.IsValid()) return;
            _nview.Register(RpcHire, OnHire);
            _nview.Register(RpcFire, OnFire);
            _nview.Register<bool>(RpcSetAutoHire, OnSetAutoHire);
            _nview.Register<string>(RpcMessage, OnMessage);
            _nview.Register<string, bool>(RpcSetTicked, OnSetTicked);
            _nview.Register<string, bool>(RpcSetFell, (sender, kind, fell) => SetTicked(sender, SaveKeys.Unfelled, kind, fell));
            _nview.Register<int>(RpcSetWoodLimit, OnSetWoodLimit);
            _nview.Register<string, bool>(RpcSetPickTicked, OnSetPickTicked);
            _nview.Register<string, bool>(RpcSetGrowTicked, OnSetGrowTicked);
        }

        /// <summary>The sapling kinds the player unticked on the replanting list (saved with the hut).</summary>
        public HashSet<string> Unticked => TickList.Parse(Saved(UntickedKey));

        /// <summary>
        /// What the player changed on a Forager's Hut's "What to pick" list (saved with the hut): kinds unticked, and
        /// off-to-start kinds ticked as "+name" (<see cref="ForageChoice"/>).
        /// </summary>
        public HashSet<string> PickUnticked => TickList.Parse(Saved(PickUntickedKey));

        public void RequestSetTicked(string sapling, bool ticked) => _nview.InvokeRPC(RpcSetTicked, sapling, ticked);

        /// <summary>The tree kinds the player unticked from Fell on the Trees list (0.22, saved with the hut).</summary>
        public HashSet<string> Unfelled => TickList.Parse(Saved(SaveKeys.Unfelled));

        /// <summary>"Stop at" on a Lumberjack's Hut (0.22): with this much Wood in the colony's chests he rests; 0 for no limit.</summary>
        public int WoodLimit => _nview != null && _nview.IsValid() ? _nview.GetZDO().GetInt(SaveKeys.WoodLimit, 0) : 0;

        public void RequestSetWoodLimit(int limit) => _nview.InvokeRPC(RpcSetWoodLimit, limit);

        private void OnSetWoodLimit(long sender, int limit)
        {
            if (!_nview.IsOwner() || !Allowed(sender)) return;
            _nview.GetZDO().Set(SaveKeys.WoodLimit, System.Array.IndexOf(Core.WoodLimit.Options, limit) >= 0 ? limit : 0);
        }

        /// <summary>Ticks or unticks Fell for a tree kind on the Trees list (0.22).</summary>
        public void RequestSetFell(string kind, bool fell) => _nview.InvokeRPC(RpcSetFell, kind, fell);

        /// <summary>Ticks or unticks a kind on the "What to pick" list (flint and branches are saved as "+name" when ticked).</summary>
        public void RequestSetPickTicked(string item, bool ticked)
        {
            ForageKind kind = Jobs.ForageList.Kind(item);
            if (kind == null) return;
            // The saved list holds the changes; the RPC takes "ticked" as "leave the name out of it".
            _nview.InvokeRPC(RpcSetPickTicked, ForageChoice.Entry(kind), !ForageChoice.EntrySaved(kind, ticked));
        }

        /// <summary>The crops the player unticked on a Farm's "What to grow" list (saved with the hut).</summary>
        public HashSet<string> GrowUnticked => TickList.Parse(Saved(GrowUntickedKey));

        public void RequestSetGrowTicked(string crop, bool ticked)
        {
            // A bush is saved as "+name" when ticked (0.19); the RPC takes "ticked" as "leave the name out of it".
            CropInfo info = Jobs.CropList.Crops.FirstOrDefault(c => c.Piece == crop);
            if (info != null && info.IsBush)
            {
                _nview.InvokeRPC(RpcSetGrowTicked, CropChoice.Entry(info), !CropChoice.EntrySaved(info, ticked));
                return;
            }
            RequestSetGrowTickedRaw(crop, ticked);
        }

        private void RequestSetGrowTickedRaw(string crop, bool ticked) => _nview.InvokeRPC(RpcSetGrowTicked, crop, ticked);

        private void OnSetGrowTicked(long sender, string crop, bool ticked) => SetTicked(sender, GrowUntickedKey, crop, ticked);

        private void OnSetTicked(long sender, string sapling, bool ticked) => SetTicked(sender, UntickedKey, sapling, ticked);

        private void OnSetPickTicked(long sender, string item, bool ticked) => SetTicked(sender, PickUntickedKey, item, ticked);

        private void SetTicked(long sender, string key, string name, bool ticked)
        {
            if (!_nview.IsOwner() || string.IsNullOrEmpty(name) || !Allowed(sender)) return;
            HashSet<string> unticked = TickList.Parse(Saved(key));
            if (ticked) unticked.Remove(name);
            else unticked.Add(name);
            _nview.GetZDO().Set(key, TickList.Save(unticked));
        }

        private string Saved(string key) => _nview != null && _nview.IsValid() ? _nview.GetZDO().GetString(key, "") : "";

        /// <summary>How many it employs: one, but a Watchtower's guards (1.2) and a storehouse's couriers (1.5) by its level.</summary>
        public int Capacity => _building == null ? 1
            : _building.TypeId == BuildingTypes.WatchtowerId ? GuardLevels.Guards(_building.Level)
            : _building.IsStorehouse ? CourierRules.Couriers(_building.Level)
            : 1;

        /// <summary>
        /// Everyone working here: those loaded here (by name) and the roster's others; false when that isn't known (the
        /// Town Hall isn't loaded and nobody of them is).
        /// </summary>
        public bool Workers(out List<Citizen> loaded, out List<RosterEntry> elsewhere)
        {
            string id = _building.Id;
            loaded = Citizen.All.Where(c => c != null && c.Record.Workplace == id).OrderBy(c => c.CitizenName, StringComparer.Ordinal).ToList();
            elsewhere = new List<RosterEntry>();
            ColonyBuilding hall = ColonyBuilding.FindTownHall(_building.ColonyId);
            TownHallModule townHall = hall != null ? hall.GetComponent<TownHallModule>() : null;
            // Without the Town Hall a colleague who isn't loaded here can't be seen: known only when those here fill every place
            // (review of 1.2: a tower of two or three places could hire past them). One place: as before.
            if (townHall == null) return loaded.Count >= Capacity || string.IsNullOrEmpty(_building.ColonyId);
            var here = new HashSet<string>(loaded.Select(c => c.Id));
            elsewhere = townHall.Roster.WorkersOf(id).Where(e => !here.Contains(e.CitizenId)).ToList();
            return true;
        }

        /// <summary>It has a place for one more (and that is known).</summary>
        public bool HasRoom => Workers(out List<Citizen> loaded, out List<RosterEntry> elsewhere) && loaded.Count + elsewhere.Count < Capacity;

        public WorkerState FindWorker(out Citizen loaded, out RosterEntry elsewhere)
        {
            string id = _building.Id;
            loaded = Citizen.All.Where(c => c != null && c.Record.Workplace == id).OrderBy(c => c.CitizenName, StringComparer.Ordinal).FirstOrDefault();   // the window's first (review of 1.2: Recall)
            elsewhere = null;
            if (loaded != null) return WorkerState.Loaded;
            ColonyBuilding hall = ColonyBuilding.FindTownHall(_building.ColonyId);
            TownHallModule townHall = hall != null ? hall.GetComponent<TownHallModule>() : null;
            if (townHall == null) return string.IsNullOrEmpty(_building.ColonyId) ? WorkerState.None : WorkerState.Unknown;
            elsewhere = townHall.Roster.WorkerOf(id);
            return elsewhere != null ? WorkerState.Elsewhere : WorkerState.None;
        }

        public void RequestHire() => _nview.InvokeRPC(RpcHire);
        public void RequestFire() => _nview.InvokeRPC(RpcFire);
        public void RequestSetAutoHire(bool on) => _nview.InvokeRPC(RpcSetAutoHire, on);

        private void OnHire(long sender)
        {
            if (!_nview.IsOwner() || !Allowed(sender)) return;
            string problem = TryHire();
            if (problem != null) Reply(sender, problem);
        }

        private void OnFire(long sender)
        {
            if (!_nview.IsOwner() || !Allowed(sender)) return;
            string message = TryFire();
            if (message != null) Reply(sender, message);
        }

        private void OnSetAutoHire(long sender, bool on)
        {
            if (_nview.IsOwner() && Allowed(sender)) _nview.GetZDO().Set(AutoHireKey, on);
        }

        /// <summary>Owner only: the asker may manage this colony; otherwise he is told why nothing happened.</summary>
        private bool Allowed(long sender)
        {
            if (ColonyPermissions.SenderMayManage(_building, sender)) return true;
            Reply(sender, "$vc_not_your_colony");
            return false;
        }

        private void OnMessage(long sender, string token) => Gui.Windows.Reply(Localization.instance.Localize(token));

        private void Reply(long sender, string token) => _nview.InvokeRPC(sender, RpcMessage, token);

        /// <summary>Owner only. Returns a $token explaining why not, or null when a citizen was asked to work here.</summary>
        private string TryHire()
        {
            if (string.IsNullOrEmpty(_building.ColonyId)) return "$vc_build_near_town_hall";
            if (!MayHire) return "$vc_not_built";
            if (!Workers(out List<Citizen> loadedNow, out List<RosterEntry> elsewhereNow)) return "$vc_town_hall_not_loaded";
            if (loadedNow.Count + elsewhereNow.Count >= Capacity) return Capacity > 1 ? "$vc_places_taken" : "$vc_has_worker";
            if (Time.time < _hirePendingUntil) return "$vc_hire_pending";
            var candidates = Citizen.All.Where(c => c != null).Select(c => new CitizenInfo(
                c.Record.Id, c.Record.ColonyId, c.Record.Workplace, Ground(c.transform.position), c.Record.Calling));
            string pick = ColonyRules.PickWorker(Ground(transform.position), _building.ColonyId, candidates, JobId);   // good at it first (0.16)
            if (pick == null) return "$vc_no_jobless";
            Citizen citizen = Citizen.All.First(c => c != null && c.Record.Id == pick);
            citizen.RequestEmploy(_building, JobId);
            _hirePendingUntil = Time.time + HireAnswerSeconds;
            Log.Info($"Asked {citizen.CitizenName} to work at {_building.TypeId} {_building.Id}");
            return null;
        }

        /// <summary>Owner, the hut taken down (0.23): its worker is let go and it hires nobody more.</summary>
        internal void LetGo()
        {
            if (_nview == null || !_nview.IsValid() || !_nview.IsOwner()) return;
            _nview.GetZDO().Set(AutoHireKey, false);
            Workers(out List<Citizen> loaded, out _);
            foreach (Citizen worker in loaded) worker.RequestUnemploy(_building.Id);
        }

        /// <summary>Owner only. Firing turns auto-hire off, or the hut would hire the same citizen straight back.</summary>
        private string TryFire()
        {
            Workers(out List<Citizen> loaded, out List<RosterEntry> elsewhere);
            if (loaded.Count == 0) return elsewhere.Count > 0 ? "$vc_worker_far" : "$vc_no_worker";
            Citizen worker = loaded[loaded.Count - 1];   // one at a time: the last by name (a Watchtower's guards, 1.2)
            bool wasAuto = AutoHire;
            if (wasAuto) _nview.GetZDO().Set(AutoHireKey, false);
            worker.RequestUnemploy(_building.Id);
            Log.Info($"{worker.CitizenName} was fired from {_building.TypeId} {_building.Id}");
            return wasAuto ? "$vc_fired_autohire_off" : null;
        }

        private void Update()
        {
            if (_nview == null || !_nview.IsValid() || !_nview.IsOwner()) return;
            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _timer = TickSeconds;
            if (AutoHire && MayHire && HasRoom) TryHire();
        }

        private static GroundPoint Ground(Vector3 v) => new GroundPoint(v.x, v.z);
    }
}
