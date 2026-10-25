using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VikingColonies.Citizens;
using VikingColonies.Construction;
using VikingColonies.Core;
using VikingColonies.Jobs;

namespace VikingColonies.Buildings
{
    /// <summary>
    /// The couriers' hub on the storehouse (1.5, the storehouse's part 2). On the storehouse's owner it works out the
    /// colony's carrying every few seconds (<see cref="CourierPlan"/>) and hands a task to each courier that asks, whichever
    /// computer runs it; who carries what is saved on the storehouse (<see cref="CourierClaims"/>), so every computer reads
    /// the same and two couriers never get one task.
    /// </summary>
    public class CourierDispatch : MonoBehaviour
    {
        public const float PlanSeconds = 5f;
        private const string RpcAsk = "vc_CourierAsk";
        private const string RpcDone = "vc_CourierDone";
        /// <summary>A task that failed (a full chest, a building gone) isn't handed out again for this long.</summary>
        private const double FailedSeconds = 60;

        private ZNetView _nview;
        private ColonyBuilding _building;
        private WorkerModule _worker;
        private List<CourierTask> _plan = new List<CourierTask>();
        private float _plannedAt = float.NegativeInfinity;
        private int _lastCount = -1;
        private readonly TargetBlacklist _failed = new TargetBlacklist(FailedSeconds);

        /// <summary>vc_storehousetest: how many plans were worked out on this computer.</summary>
        internal static int PlansForTest;

        public IReadOnlyList<CourierTask> PlanForTest => _plan;

        /// <summary>vc_storehousetest: the plan worked out now (its cost is measured).</summary>
        internal void ReplanForTest() => Replan();

        private void Awake()
        {
            _nview = GetComponent<ZNetView>();
            _building = GetComponent<ColonyBuilding>();
            _worker = GetComponent<WorkerModule>();
            if (_nview == null || !_nview.IsValid()) return;
            _nview.Register<string, string>(RpcAsk, OnAsk);
            _nview.Register<string, bool>(RpcDone, OnDone);
        }

        private void Update()
        {
            if (_nview == null || !_nview.IsValid() || !_nview.IsOwner() || _building == null) return;
            if (Time.time - _plannedAt >= PlanSeconds) Replan();
        }

        /// <summary>
        /// A courier asks the storehouse's owner for a task (its answer is saved: <see cref="TaskFor"/>), saying which task
        /// it last finished, so a Done lost while the storehouse changed computers doesn't give it that task again.
        /// </summary>
        public void RequestTask(string courierId, string lastDone = "")
        {
            if (_nview != null && _nview.IsValid()) _nview.InvokeRPC(RpcAsk, courierId ?? "", lastDone ?? "");
        }

        /// <summary>A courier is done with its task: delivered, or it couldn't be.</summary>
        public void ReportDone(string courierId, bool delivered)
        {
            if (_nview != null && _nview.IsValid()) _nview.InvokeRPC(RpcDone, courierId ?? "", delivered);
        }

        /// <summary>The courier's task as saved on the storehouse, on any computer; null when it has none.</summary>
        public CourierTask TaskFor(string courierId) => Claims().For(courierId);

        /// <summary>When the courier was given its task (network time), on any computer; 0 when it holds none.</summary>
        public double SinceFor(string courierId) => Claims().Since(courierId);

        private void OnAsk(long sender, string courierId, string lastDone)
        {
            if (!_nview.IsOwner() || string.IsNullOrEmpty(courierId)) return;
            if (Time.time - _plannedAt >= PlanSeconds) Replan();
            double now = ConstructionModule.Now();
            CourierClaims claims = Claims();
            string before = claims.Encode();
            claims.Expire(now);
            HashSet<string> hired = Hired();
            if (hired != null) claims.Forget(id => hired.Contains(id));
            claims.Assign(_plan.Where(t => !_failed.IsBlocked(t.Key, now)).ToList(), courierId, now, lastDone);
            Save(claims, before);
        }

        private void OnDone(long sender, string courierId, bool delivered)
        {
            if (!_nview.IsOwner()) return;
            CourierClaims claims = Claims();
            string before = claims.Encode();
            CourierTask task = claims.For(courierId);
            if (task != null && !delivered) _failed.Add(task.Key, ConstructionModule.Now());
            claims.Release(courierId);
            Save(claims, before);
            _plannedAt = float.NegativeInfinity;   // the chests changed: a fresh plan for the next ask
        }

        private CourierClaims Claims() =>
            CourierClaims.Decode(_nview != null && _nview.IsValid() ? _nview.GetZDO().GetString(SaveKeys.CourierTasks, "") : "");

        private void Save(CourierClaims claims, string before)
        {
            string now = claims.Encode();
            if (now != before) _nview.GetZDO().Set(SaveKeys.CourierTasks, now);
        }

        /// <summary>Its couriers' ids; null when that isn't known (the Town Hall unloaded): then nobody is forgotten.</summary>
        private HashSet<string> Hired()
        {
            if (_worker == null || !_worker.Workers(out List<Citizen> loaded, out List<RosterEntry> elsewhere)) return null;
            return new HashSet<string>(loaded.Select(c => c.Id).Concat(elsewhere.Select(e => e.CitizenId)));
        }

        /// <summary>The colony's carrying now, from its chests' contents.</summary>
        private void Replan()
        {
            _plannedAt = Time.time;
            PlansForTest++;
            StorehouseModule store = _building.Storehouse;
            string colony = _building.ColonyId;
            if (store == null || string.IsNullOrEmpty(colony))
            {
                _plan = new List<CourierTask>();
                SaveFull(false);
                return;
            }
            List<CourierSite> sites = Sites(colony);
            ConstructionModule site = _building.GetComponent<ConstructionModule>();
            bool closing = site != null && site.IsClosingStore && !site.IsTakingDown;
            var facts = new StoreFacts
            {
                Id = _building.Id,
                Level = _building.Level,
                DropOff = Carried(_building.Storage.SpareContents()),
                Chests = store.Open ? store.Chests.Select(c => new StoreChest { Items = Carried(c.Contents()), RoomFor = c.RoomFor }).ToList() : new List<StoreChest>(),
                InsideHasRoom = store.Open && store.Chests.Any(c => c.HasEmptySlot),
                Open = store.Open,
                Closing = closing,
                // Emptied for its take-down (part 3): what its chests inside hold goes elsewhere.
                Clearing = closing ? Carried(Sum(store.Chests.Select(c => c.Contents()))) : new Dictionary<string, int>(),
            };
            SaveFull(facts.Open && !facts.InsideHasRoom);   // the Town Hall's overview reads it on every computer (1.5.0)
            _plan = CourierPlan.Tasks(sites, facts, CourierJob.LoadAt(_building), FoodItems.Value);
            if (_plan.Count != _lastCount)
                Log.Info($"{_building.TypeId} {_building.Id} couriers' plan: {_plan.Count} tasks " +
                         $"({_plan.Count(t => t.Kind == CourierTaskKind.Deliver)} to deliver)");
            _lastCount = _plan.Count;
        }

        /// <summary>No chest inside has an empty slot: kept on the storehouse for every computer, written only when it changes.</summary>
        private void SaveFull(bool full)
        {
            if (_nview == null || !_nview.IsValid() || !_nview.IsOwner()) return;
            if (_nview.GetZDO().GetBool(SaveKeys.StoreFull) != full) _nview.GetZDO().Set(SaveKeys.StoreFull, full);
        }

        /// <summary>The colony's buildings as the plan sees them.</summary>
        private List<CourierSite> Sites(string colony)
        {
            var sites = new List<CourierSite>();
            foreach (ColonyBuilding b in ColonyBuilding.All)
            {
                if (b == null || !b.IsValid || b.ColonyId != colony || b.Storage == null || b.Id.Length == 0) continue;
                if (b.IsStorehouse && b != _building) continue;   // one per colony; another's store isn't this one's
                sites.Add(Site(b));
            }
            return sites;
        }

        /// <summary>
        /// The Needs tab (part 3): every building's shortfalls, nearest the storehouse first, what's on its way and what
        /// nobody has; on any computer, from the chests and the couriers' tasks saved on the storehouse.
        /// </summary>
        internal NeedsReport Needs()
        {
            string colony = _building != null ? _building.ColonyId : "";
            if (string.IsNullOrEmpty(colony) || _building.Storehouse == null) return new NeedsReport();
            Vector3 me = transform.position;
            List<CourierSite> sites = Sites(colony).OrderBy(s => (s.At.X - me.x) * (s.At.X - me.x) + (s.At.Z - me.z) * (s.At.Z - me.z)).ToList();
            return NeedsList.Of(sites, Claims().All, FoodItems.Value);
        }

        /// <summary>A courier of this storehouse by its id ("Ulf"); "" when it isn't known here.</summary>
        internal string CourierName(string courierId)
        {
            if (string.IsNullOrEmpty(courierId) || _worker == null || !_worker.Workers(out List<Citizen> loaded, out List<RosterEntry> elsewhere)) return "";
            Citizen citizen = loaded.FirstOrDefault(c => c != null && c.Id == courierId);
            if (citizen != null) return citizen.CitizenName;
            RosterEntry entry = elsewhere.FirstOrDefault(e => e != null && e.CitizenId == courierId);
            return entry != null ? entry.Name : "";
        }

        private CourierSite Site(ColonyBuilding b)
        {
            StorageModule chest = b.Storage;
            Dictionary<string, int> contents = chest.Contents();
            var site = new CourierSite
            {
                Id = b.Id,
                At = new GroundPoint(b.transform.position.x, b.transform.position.z),
                IsStorehouse = b == _building,
                IsTownHall = b.IsTownHall,
                IsBuilderHut = b.TypeId == BuildingTypes.BuilderHutId,
                IsProducer = b.TypeId == BuildingTypes.LumberjackHutId || b.TypeId == BuildingTypes.ForagerHutId || b.TypeId == BuildingTypes.FarmId,
                Fill = chest.Fill,
                CanTake = chest.CanTake,   // its review: no delivery to a chest with no room
                TakeRest = b.Stock == null || b.Stock.CouriersTakeRest,   // part 3's switch
            };
            StockList keep = b.Stock != null ? b.Stock.List : null;
            foreach (KeepEntry missing in StockRules.Short(contents, keep, StorageModule.IsFood))
                if (missing.Item == StockList.AnyFood || StorageModule.Carried(missing.Item)) site.Short.Add(missing);
            if (site.IsBuilderHut) AddOrderNeeds(b, contents, site.Short);
            if (site.IsStorehouse)
            {
                // Its whole store: its own chest beyond what it keeps (the drop-off), the chests inside whole.
                site.Spare = Carried(chest.SpareContents());
                foreach (StorageModule inside in _building.Stores.Where(s => s != chest))   // none while it's taken down (its review)
                    foreach (KeyValuePair<string, int> item in Carried(inside.Contents()))
                        site.Spare[item.Key] = (site.Spare.TryGetValue(item.Key, out int n) ? n : 0) + item.Value;
            }
            else site.Spare = Carried(chest.SpareContents());   // beyond its Keep in stock and its worker's working stock
            return site;
        }

        /// <summary>A Builder's Hut is short of what its builder's order still needs beyond its chest (carried items only).</summary>
        private static void AddOrderNeeds(ColonyBuilding builderHut, Dictionary<string, int> contents, List<KeepEntry> into)
        {
            WorkerModule worker = builderHut.GetComponent<WorkerModule>();
            string builderId = "";
            if (worker != null)
            {
                WorkerModule.WorkerState state = worker.FindWorker(out Citizen loaded, out RosterEntry elsewhere);
                if (state == WorkerModule.WorkerState.Loaded && loaded != null) builderId = loaded.Id;
                else if (state == WorkerModule.WorkerState.Elsewhere && elsewhere != null) builderId = elsewhere.CitizenId;
            }
            ConstructionModule site = ConstructionModule.CurrentFor(builderHut, builderId);
            if (site == null) return;
            foreach (KeyValuePair<string, int> need in site.RemainingNeeds())
            {
                int missing = need.Value - (contents.TryGetValue(need.Key, out int have) ? have : 0);
                if (missing > 0 && StorageModule.Carried(need.Key) && into.All(e => e.Item != need.Key))
                    into.Add(new KeepEntry { Item = need.Key, Amount = missing });
            }
        }

        private static Dictionary<string, int> Sum(IEnumerable<Dictionary<string, int>> chests)
        {
            var sum = new Dictionary<string, int>();
            foreach (Dictionary<string, int> chest in chests)
                foreach (KeyValuePair<string, int> item in chest)
                    sum[item.Key] = (sum.TryGetValue(item.Key, out int n) ? n : 0) + item.Value;
            return sum;
        }

        private static Dictionary<string, int> Carried(Dictionary<string, int> items) =>
            items.Where(i => i.Value > 0 && StorageModule.Carried(i.Key)).ToDictionary(i => i.Key, i => i.Value);
    }
}
