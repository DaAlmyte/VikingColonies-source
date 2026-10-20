using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VikingColonies.Buildings;
using VikingColonies.Citizens;
using VikingColonies.Colony;
using VikingColonies.Config;
using VikingColonies.Core;
using static VikingColonies.Construction.CoreVectors;

namespace VikingColonies.Construction
{
    /// <summary>
    /// A hut's building: its build order (plan, progress, the builder's claim), saved with the hut. Changes arrive as
    /// RPCs and are applied by the hut's owner, like hiring. Huts placed before builders existed have no order and
    /// count as built.
    /// </summary>
    public class ConstructionModule : MonoBehaviour
    {
        public const string OrderKey = SaveKeys.Order;
        public const string PlanKey = SaveKeys.Plan;
        public const string TakeAwayKey = SaveKeys.TakeAway;
        private const string PrevPlanKey = SaveKeys.PlanPrev;
        private const string PrevOrderKey = SaveKeys.OrderPrev;
        public const int PieceDone = 0;
        public const int PieceFailed = 1;
        public const int PieceGone = 2;
        public const int PieceUnderground = 3;

        private const string RpcClaim = "vc_OrderClaim";
        private const string RpcRelease = "vc_OrderRelease";
        private const string RpcPiece = "vc_OrderPiece";
        private const string RpcNeeds = "vc_OrderNeeds";
        private const string RpcBlocked = "vc_OrderBlocked";
        private const string RpcCreate = "vc_OrderCreate";
        private const string RpcMarkBuilt = "vc_OrderMarkBuilt";
        private const string RpcState = "vc_OrderState";
        private const string RpcUpgrade = "vc_OrderUpgrade";
        private const string RpcUpgradeNow = "vc_OrderUpgradeNow";
        private const string RpcCancelUpgrade = "vc_OrderCancelUpgrade";
        private const string RpcTakeDown = "vc_OrderTakeDown";
        private const string RpcCancelTakeDown = "vc_OrderCancelTakeDown";
        private const string RpcTaken = "vc_OrderTaken";
        private const string RpcFirst = "vc_OrderFirst";
        private const string RpcPause = "vc_OrderPause";
        private const string RpcLeaveAsIs = "vc_OrderLeaveAsIs";
        private const string RpcRemoved = "vc_OrderPieceRemoved";
        private const string RpcStartCamp = "vc_CampStart";
        private const float CampCheckSeconds = 15f;
        private const string RpcMessage = "vc_ConstructionMessage";   // its own: the Town Hall and the Longhouse have no WorkerModule (0.25)
        private const float FinishStepSeconds = 0.25f;   // between ground changes in vc_build finish

        private static readonly List<ConstructionModule> s_all = new List<ConstructionModule>();
        private static readonly List<PlannedPiece> s_noPlan = new List<PlannedPiece>();

        private ColonyBuilding _building;
        private ZNetView _nview;
        private byte[] _planBlob;
        private List<PlannedPiece> _plan = s_noPlan;
        private byte[] _takeAwayBlob;
        private List<PlannedPiece> _takeAway = s_noPlan;
        private readonly TextCache<BuildOrderRecord> _orderCache = new TextCache<BuildOrderRecord>(BuildOrderRecord.Decode);
        private readonly Dictionary<string, List<Vector3>> _planByPrefab = new Dictionary<string, List<Vector3>>();
        private List<PlannedPiece> _planIndexed;
        private IReadOnlyList<PlannedPiece> _footprintPlan;
        private IReadOnlyList<PlannedPiece> _landingsPlan;
        private List<Footprint> _landings = new List<Footprint>();
        private Footprint _siteFootprint;

        public static IReadOnlyList<ConstructionModule> All => s_all;

        /// <summary>A colony's open work orders in the order builders take them (Do first, then oldest; set aside last).</summary>
        public static List<ConstructionModule> OpenOrders(string colonyId)
        {
            var byId = new Dictionary<string, ConstructionModule>();
            var rows = new List<WorkOrderRow>();
            foreach (ConstructionModule site in s_all)
            {
                BuildOrderRecord order = site != null && site.IsValid && site.Building != null && site.Building.ColonyId == colonyId ? site.Order : null;
                if (order == null || order.State == BuildState.Done || byId.ContainsKey(site.Building.Id)) continue;
                byId[site.Building.Id] = site;
                rows.Add(new WorkOrderRow { BuildingId = site.Building.Id, First = order.First, Paused = order.Paused, Created = order.Created,
                    Later = site.IsCamp });
            }
            return WorkOrderList.Sorted(rows).Select(r => byId[r.BuildingId]).ToList();
        }

        /// <summary>The order a Builder's Hut's builder holds now, else the colony's first open one (Hand over, 0.23); null for none.</summary>
        public static ConstructionModule CurrentFor(ColonyBuilding builderHut, string builderId)
        {
            if (builderHut == null) return null;
            List<ConstructionModule> open = OpenOrders(builderHut.ColonyId);
            open = open.Where(s => s.Order.Kind != OrderKind.Remove).ToList();   // a take-down needs nothing handed over (0.24)
            return open.FirstOrDefault(s => builderId.Length > 0 && s.Order.Builder == builderId) ?? open.FirstOrDefault(s => !s.Order.Paused);
        }
        public ColonyBuilding Building => _building;
        public bool IsValid => _nview != null && _nview.IsValid();

        private void Awake()
        {
            _building = GetComponent<ColonyBuilding>();
            _nview = GetComponent<ZNetView>();
            if (!IsValid) return;   // the hammer's placement ghost has no network object
            _nview.Register<string>(RpcClaim, OnClaim);
            _nview.Register<string>(RpcMessage, OnMessage);
            _nview.Register<string>(RpcRelease, OnRelease);
            _nview.Register<int, int>(RpcPiece, OnPiece);
            _nview.Register<string>(RpcNeeds, OnNeeds);
            _nview.Register<string>(RpcBlocked, OnBlocked);
            _nview.Register(RpcCreate, OnCreate);
            _nview.Register<bool>(RpcMarkBuilt, OnMarkBuilt);
            _nview.Register<int>(RpcState, OnState);
            _nview.Register<int>(RpcUpgrade, OnUpgrade);
            _nview.Register(RpcUpgradeNow, OnUpgradeNow);
            _nview.Register(RpcCancelUpgrade, OnCancelUpgrade);
            _nview.Register(RpcTakeDown, OnTakeDown);
            _nview.Register(RpcCancelTakeDown, OnCancelTakeDown);
            _nview.Register<int>(RpcTaken, OnTaken);
            _nview.Register(RpcFirst, OnFirst);
            _nview.Register<bool>(RpcPause, OnPause);
            _nview.Register(RpcLeaveAsIs, OnLeaveAsIs);
            _nview.Register<int>(RpcRemoved, OnRemoved);
            _nview.Register(RpcStartCamp, OnStartCamp);
            s_all.Add(this);
            // The Town Hall's camp grows with the hall's level (huts round 2): looked at now and then by its owner.
            if (_building != null && _building.IsTownHall)
                InvokeRepeating(nameof(CampTick), UnityEngine.Random.Range(5f, 5f + CampCheckSeconds), CampCheckSeconds);
            // Every minute (not every hut at once): is anything of the finished building gone? (Repair, 0.13)
            InvokeRepeating(nameof(RepairCheck), UnityEngine.Random.Range(20f, 20f + RepairRules.CheckSeconds), RepairRules.CheckSeconds);
        }

        private void Start()
        {
            // A hut placed just now gets its building ordered, by the computer that placed (and owns) it; a Town Hall its
            // camp, when there is a design for it and its site is clear (huts round 2).
            if (IsValid && _nview.IsOwner() && _building.PlacedThisSession && Order == null)
            {
                if (IsCamp)
                {
                    StartCoroutine(StartCampOnceFounded());
                    return;
                }
                CreateOrder();
                StartCoroutine(BuilderTipOnceJoined());
            }
        }

        private void OnDestroy() => s_all.Remove(this);

        /// <summary>
        /// Hugin's builder tip for a hut placed just now, once it has joined its colony (a second or so after placing;
        /// review of 0.11: checked at once, every new hut looked like it had no Builder's Hut).
        /// </summary>
        private System.Collections.IEnumerator BuilderTipOnceJoined()
        {
            for (float waited = 0f; IsValid && string.IsNullOrEmpty(_building.ColonyId) && waited < 15f; waited += 1f)
                yield return new WaitForSeconds(1f);
            if (!IsValid || string.IsNullOrEmpty(_building.ColonyId)) yield break;
            if (TipRules.NeedsBuilderTip(_building.TypeId == BuildingTypes.BuilderHutId, HasBuilderHut()))
                Gui.HuginTips.ShowNear(transform.position, Gui.HuginTips.Builder);
        }

        /// <summary>
        /// The saved order; null for huts that never had one. Read once per change and shared with every reader, so
        /// callers must not change it (changes go through the hut's owner).
        /// </summary>
        public BuildOrderRecord Order => IsValid ? _orderCache.Get(_nview.GetZDO().GetString(OrderKey, "")) : null;

        /// <summary>
        /// The hut may hire: its building is finished (or being repaired: it works on meanwhile), it never had an order,
        /// or its plan can't be read.
        /// </summary>
        public bool IsBuilt
        {
            get
            {
                BuildOrderRecord order = Order;
                // Being repaired or taken down (0.23), it works on meanwhile.
                return order == null || order.State == BuildState.Done || order.Kind == OrderKind.Repair || order.Kind == OrderKind.Remove || Plan.Count == 0;
            }
        }

        /// <summary>Pieces of the finished building are being put back (0.13).</summary>
        public bool IsRepairing
        {
            get
            {
                BuildOrderRecord order = Order;
                return order != null && order.Kind == OrderKind.Repair && order.State != BuildState.Done;
            }
        }

        /// <summary>The planned pieces in world positions; empty without a readable plan. Decoded once per saved plan.</summary>
        public IReadOnlyList<PlannedPiece> Plan
        {
            get
            {
                byte[] blob = IsValid ? _nview.GetZDO().GetByteArray(PlanKey) : null;
                if (blob == null || blob.Length == 0) return s_noPlan;
                if (!ReferenceEquals(blob, _planBlob))
                {
                    _planBlob = blob;
                    _plan = PlanCodec.Decode(PlanStore.Unpack(blob));
                }
                return _plan;
            }
        }

        /// <summary>An upgrade's old pieces to take away, in world positions; empty when there are none.</summary>
        public IReadOnlyList<PlannedPiece> TakeAwayPlan
        {
            get
            {
                byte[] blob = IsValid ? _nview.GetZDO().GetByteArray(TakeAwayKey) : null;
                if (blob == null || blob.Length == 0) return s_noPlan;
                if (!ReferenceEquals(blob, _takeAwayBlob))
                {
                    _takeAwayBlob = blob;
                    _takeAway = PlanCodec.Decode(PlanStore.Unpack(blob));
                }
                return _takeAway;
            }
        }

        /// <summary>The hut is being taken down (0.23).</summary>
        public bool IsTakingDown
        {
            get
            {
                BuildOrderRecord order = Order;
                return order != null && order.Kind == OrderKind.Remove && order.State != BuildState.Done;
            }
        }

        /// <summary>The player may ask for it to be taken down: a hut whose building is finished (0.23).</summary>
        public bool MayTakeDown
        {
            get
            {
                BuildOrderRecord order = Order;
                return _building != null && TakeDownRules.MayTakeDown(_building.IsTownHall, order != null, order != null && order.State == BuildState.Done);
            }
        }

        /// <summary>An upgrade is under way (the hut keeps working at its old level meanwhile).</summary>
        public bool IsUpgrading
        {
            get
            {
                BuildOrderRecord order = Order;
                return order != null && order.Kind == OrderKind.Upgrade && order.State != BuildState.Done;
            }
        }

        /// <summary>A running upgrade can still be cancelled: the builder hasn't started building or taking down.</summary>
        public bool CanCancelUpgrade
        {
            get
            {
                BuildOrderRecord order = Order;
                return IsUpgrading && !order.Changed;   // until the builder has put up or taken away the first piece
            }
        }

        // ---- The Town Hall's camp (huts round 2, 2026-10-18) ----

        /// <summary>
        /// This is the Town Hall's camp: the colony's meeting place round the hall's chest, built by the builder from the
        /// camp's design like a hut, its level following the hall's (CampRules). Without a design for it nothing happens,
        /// as before.
        /// </summary>
        public bool IsCamp => _building != null && _building.IsTownHall;

        /// <summary>The camp's level: 0 until its first level stands.</summary>
        public int CampLevel => IsValid ? _nview.GetZDO().GetInt(SaveKeys.CampLevel, 0) : 0;

        /// <summary>Why the camp's next level waits (the site check's reason, or no design for it); "" when it doesn't.</summary>
        public string CampWaits => IsValid ? _nview.GetZDO().GetString(SaveKeys.CampWaits, "") : "";

        /// <summary>The level of the design the building has: a hut's level; the camp's own for the Town Hall.</summary>
        private int DesignLevel => IsCamp ? CampLevel : _building.Level;

        /// <summary>Whether the mod has a design for the camp's level.</summary>
        public bool HasCampDesign(int level) => IsCamp && BlueprintLibrary.For(_building.Type, level, out _) != null;

        private void SetCampLevel(int level)
        {
            _nview.GetZDO().Set(SaveKeys.CampLevel, level);
            _nview.GetZDO().Set(SaveKeys.CampWaits, "");
            Log.Info($"{_building.TypeId} {_building.Id}: its camp is now level {level}");
        }

        /// <summary>A Town Hall placed just now orders its camp once it has founded its colony (the site check needs it).</summary>
        private IEnumerator StartCampOnceFounded()
        {
            for (float waited = 0f; IsValid && string.IsNullOrEmpty(_building.ColonyId) && waited < 15f; waited += 1f)
                yield return new WaitForSeconds(1f);
            if (!IsValid || !_nview.IsOwner() || string.IsNullOrEmpty(_building.ColonyId)) yield break;
            if (!TryStartCamp(out string why) && why.Length > 0) Log.Info($"{_building.TypeId} {_building.Id}: no camp yet ({ShowWait(why)})");
        }

        /// <summary>
        /// Owner only: the camp's first level is ordered when the mod has a design for it and its site is clear. The site
        /// check's red (a player's building in the way, a ward, water…) leaves it unordered, with why for the Hall tab: the
        /// builder never takes a player's building down (the anti-goal of the Hall of Trophies).
        /// </summary>
        private bool TryStartCamp(out string why)
        {
            why = "";
            if (!IsCamp || Order != null) return false;
            BlueprintFile blueprint = BlueprintLibrary.For(_building.Type, CampRules.FirstLevel, out _);
            if (blueprint == null || !BlueprintPlan.TryPlace(blueprint, _building.Type.PrefabName, V(transform.position), Q(transform.rotation),
                    out List<PlannedPiece> plan, out _))
                return false;
            SiteFindings findings = SiteSurvey.Look(plan, transform.position, transform.rotation, this, null, MatchesPlan,
                GroundShaper.StonePerCubicMetre, new List<SiteThing>(), new List<Piece>());
            SiteVerdict verdict = SiteCheck.Judge(findings, ColonyBuilderLevel(_building.ColonyId));
            if (verdict.Colour == SiteColour.Red)
            {
                why = CampWait.Store(SiteText.RedToken(verdict.Red));   // its key: each player reads it in their own language
                _nview.GetZDO().Set(SaveKeys.CampWaits, why);
                return false;
            }
            CreateOrder();
            _nview.GetZDO().Set(SaveKeys.CampWaits, "");
            return Order != null;
        }

        private void OnStartCamp(long sender)
        {
            if (!IsValid || !_nview.IsOwner() || !IsCamp || !Allowed(sender)) return;
            if (Order != null) return;
            if (TryStartCamp(out string why)) Tell(sender, "$vc_camp_started");
            else Tell(sender, why.Length > 0 ? why : "$vc_camp_no_design");
        }

        /// <summary>Soon after the hall is raised: the camp's next level, without waiting for the next look.</summary>
        public void CampSoon()
        {
            if (!IsCamp || !IsValid || !_nview.IsOwner()) return;
            _campRedAt = double.NegativeInfinity;   // the hall was raised: its next level is looked at now
            Invoke(nameof(CampTick), 2f);
        }

        /// <summary>
        /// Owner only, now and then: when the camp stands and the hall's level is above it, its next level is ordered (one
        /// at a time). A red site leaves it waiting, with why for the Hall tab.
        /// </summary>
        private void CampTick()
        {
            if (!IsCamp || !IsValid || !_nview.IsOwner()) return;
            BuildOrderRecord order = Order;
            if (order == null) return;
            bool busy = order.State != BuildState.Done;
            int builderHut = HighestBuilderHutLevel(_building.ColonyId);
            int next = CampRules.NextLevel(_building.Level, CampLevel, busy, builderHut);
            if (next == 0)
            {
                // The hall is ahead, but no builder may build the camp's next level yet: say so on the Hall tab.
                string wait = CampRules.WaitsForBuilder(_building.Level, CampLevel, busy, builderHut)
                    ? CampWait.Store("$vc_camp_needs_builder", CampLevel + 1) : "";
                if (wait != CampWaits) _nview.GetZDO().Set(SaveKeys.CampWaits, wait);
                return;
            }
            // A red site isn't surveyed again on every tick: after a minute, or at once when the hall is raised (A8).
            if (next == _campRedFor && !CampRules.LookAgain(_campRedAt, Time.time)) return;
            UpgradePreview preview = PreviewUpgrade();
            string why = preview == null ? "$vc_camp_no_design" : preview.Site.Colour == SiteColour.Red ? CampWait.Store(SiteText.RedToken(preview.Site.Red)) : "";
            if (why.Length > 0)
            {
                if (why != CampWaits) Log.Info($"{_building.TypeId} {_building.Id}: its camp's level {next} waits ({ShowWait(why)})");
                _nview.GetZDO().Set(SaveKeys.CampWaits, why);
                _campRedAt = Time.time;
                _campRedFor = next;
                return;
            }
            _campRedFor = 0;
            if (CreateUpgrade(next)) _nview.GetZDO().Set(SaveKeys.CampWaits, "");
        }

        /// <summary>For the Hall tab: what the camp is and does, one line; null when the mod has no design for it.</summary>
        public string CampLine()
        {
            BuildOrderRecord order = Order;
            if (order == null)
            {
                if (!HasCampDesign(CampRules.FirstLevel)) return null;
                string waits = CampWaits;
                return L("$vc_camp_none") + (waits.Length > 0 ? " " + string.Format(L("$vc_camp_waits"), ShowWait(waits)) : "");
            }
            if (order.State != BuildState.Done)
            {
                string doing = order.Kind == OrderKind.Upgrade ? string.Format(L("$vc_camp_growing"), order.TargetLevel, order.DoneCount, order.Total)
                    : order.Kind == OrderKind.Repair ? L("$vc_camp_mending")
                    : string.Format(L("$vc_camp_building"), order.DoneCount, order.Total);
                string who = order.HasLiveClaim(Now()) ? string.Format(L("$vc_builder_is"), NameOf(order.Builder))
                    : L(HasBuilderHut() ? "$vc_waiting_for_builder" : "$vc_needs_builder");
                return doing + " " + who;
            }
            string line = string.Format(L("$vc_camp_level"), CampLevel);
            string wait = CampWaits;
            return wait.Length > 0 && CampLevel < TownHallLevels.Clamp(_building.Level) ? line + " " + string.Format(L("$vc_camp_waits"), ShowWait(wait)) : line;
        }

        /// <summary>The camp's stored waiting reason in this player's language (CampWait: a key, and its number).</summary>
        private static string ShowWait(string stored) =>
            CampWait.Read(stored, out string token, out int number) ? string.Format(L(token), number) : L(token);

        private double _campRedAt = double.NegativeInfinity;   // when the camp's next level's site was last found red (A8)
        private int _campRedFor;

        /// <summary>The Hall tab offers Build the camp: none yet, and a design for it.</summary>
        public bool MayStartCamp => IsCamp && Order == null && HasCampDesign(CampRules.FirstLevel);

        /// <summary>Whether this hut may go up a level now, and why not.</summary>
        public UpgradeLock UpgradeCheck()
        {
            BuildOrderRecord order = Order;
            if (order == null) return UpgradeLock.NotFinished;   // a hut from before builders: build it first
            bool isBuilderHut = _building.TypeId == BuildingTypes.BuilderHutId;
            // Not while a repair runs (review of 0.13: the builder on it kept the old plan's work).
            return HutLevels.CanUpgrade(_building.Level, IsBuilt && !IsRepairing, IsUpgrading, HighestBuilderHutLevel(_building.ColonyId), isBuilderHut,
                IsTakingDown,   // review of 0.23: an upgrade then a cancel left the builder stuck
                Buildings.HallRules.PerksOf(_building.ColonyId).HutCap);   // 1.4: no higher than the Town Hall allows
        }

        /// <summary>The level of the colony's highest Builder's Hut; 0 when it has none.</summary>
        public static int HighestBuilderHutLevel(string colonyId) => ColonyBuilding.All
            .Where(b => b != null && b.IsValid && b.TypeId == BuildingTypes.BuilderHutId && !string.IsNullOrEmpty(colonyId) && b.ColonyId == colonyId)
            .Select(b => b.Level)
            .DefaultIfEmpty(0)
            .Max();

        /// <summary>The highest level among the colony's Builder's Huts that have a builder (one who could take a site over).</summary>
        public static int HighestStaffedBuilderHutLevel(string colonyId) => OrderPatience.BestStaffedLevel(ColonyBuilding.All
            .Where(b => b != null && b.IsValid && b.TypeId == BuildingTypes.BuilderHutId && !string.IsNullOrEmpty(colonyId) && b.ColonyId == colonyId)
            .Select(b => new KeyValuePair<int, bool>(b.Level, HasWorker(b))));

        private static bool HasWorker(ColonyBuilding hut)
        {
            WorkerModule worker = hut.GetComponent<WorkerModule>();
            if (worker == null) return false;
            WorkerModule.WorkerState state = worker.FindWorker(out _, out _);
            return state == WorkerModule.WorkerState.Loaded || state == WorkerModule.WorkerState.Elsewhere;
        }

        /// <summary>The Builder's Hut level that decides what the colony's builders may clear: its highest, or 1 without one (its first starts there).</summary>
        public static int ColonyBuilderLevel(string colonyId) => System.Math.Max(1, HighestBuilderHutLevel(colonyId));

        /// <summary>The next level's design laid around this chest, or null (with why) when it can't be used.</summary>
        private List<PlannedPiece> NextLevelPlan(out string problem)
        {
            problem = "no design for the next level";
            BuildingType type = _building.Type;
            BlueprintFile blueprint = type != null ? BlueprintLibrary.For(type, DesignLevel + 1, out _) : null;
            if (blueprint == null) return null;
            return BlueprintPlan.TryPlace(blueprint, type.PrefabName, V(transform.position), Q(transform.rotation), out List<PlannedPiece> pieces, out problem)
                ? pieces : null;
        }

        /// <summary>
        /// What the upgrade to the next level needs: the materials of the pieces it adds, and the check of its site (red:
        /// it can't go there). Null when the next level has no usable design.
        /// </summary>
        public UpgradePreview PreviewUpgrade()
        {
            List<PlannedPiece> plan = NextLevelPlan(out _);
            if (plan == null) return null;
            DesignDiff diff = DesignDiff.Compare(Plan, plan);
            var needs = new Dictionary<string, int>();
            foreach (int index in diff.Build)
                foreach (KeyValuePair<string, int> need in PiecePlacer.Needs(plan[index].Prefab))
                    needs[need.Key] = (needs.TryGetValue(need.Key, out int n) ? n : 0) + need.Value;
            SiteFindings findings = SiteSurvey.Look(plan, transform.position, transform.rotation, this, null, MatchesPlan,
                GroundShaper.StonePerCubicMetre, new List<SiteThing>(), new List<Piece>(), upgrade: true);
            List<ToolInfo> atHand = BuilderTools.AtHand(BuilderTools.ColonyStores(_building.ColonyId, null, transform.position));
            return new UpgradePreview(DesignLevel + 1, needs, SiteCheck.Judge(findings, ColonyBuilderLevel(_building.ColonyId), atHand), findings);
        }

        /// <summary>The ground in front of the plan's doors, levelled with the site so a door doesn't open onto a bank.</summary>
        public IReadOnlyList<Footprint> DoorLandings
        {
            get
            {
                Footprint site = SiteFootprint;
                if (site == null) return new List<Footprint>();
                if (ReferenceEquals(_landingsPlan, _footprintPlan)) return _landings;
                _landingsPlan = _footprintPlan;
                _landings = SiteSurvey.DoorLandings(_footprintPlan, site, transform.position);
                return _landings;
            }
        }

        /// <summary>The level operations for the site and the ground in front of its doors, keeping clear of other buildings.</summary>
        public List<GroundOp> GroundOps(float target)
        {
            var ops = new List<GroundOp>();
            Footprint site = SiteFootprint;
            if (site == null) return ops;
            ops.AddRange(GroundPlan.Plan(site, target, SiteSurvey.GroundAt, (x, z, r) => SiteSurvey.NearOtherBuilding(this, x, z, r)));
            foreach (Footprint landing in DoorLandings)
            {
                List<GroundOp> front = GroundPlan.Plan(landing, target, SiteSurvey.GroundAt, (x, z, r) => SiteSurvey.NearOtherBuilding(this, x, z, r));
                Log.Info($"{_building.TypeId} {_building.Id}: the ground in front of a door, round ({landing.Centre.X:0.0}, {landing.Centre.Z:0.0}), " +
                         $"is levelled to {target:0.00} with {front.Count} spots (the ground there is {SiteSurvey.GroundAt(landing.Centre.X, landing.Centre.Z):0.00})");
                ops.AddRange(front);
            }
            return ops;
        }

        /// <summary>Where the edges of the levelled ground are smoothed: around the site and around each door's landing.</summary>
        public List<Vec3> SmoothSpots()
        {
            var spots = new List<Vec3>();
            Footprint site = SiteFootprint;
            if (site == null) return spots;
            spots.AddRange(GroundPlan.SmoothSpots(site, GroundPlan.SmoothSpacing));
            foreach (Footprint landing in DoorLandings)
                spots.AddRange(GroundPlan.SmoothSpots(landing, GroundPlan.SmoothSpacing).Where(s => !site.Contains(s)));
            return spots.Where(s => !SiteSurvey.NearOtherBuilding(this, s.X, s.Z, GroundShaper.SmoothRadius)).ToList();
        }

        /// <summary>
        /// What the hut's walls on the ground enclose, with the site's margin as <see cref="SiteFootprint"/> has: inside the
        /// hut, for its doors and the wedged-in rescue (the old huts' upgrade test, 2026-10-19: the whole outline took in
        /// the woodpile strip behind a back wall). The whole site for a design without such walls.
        /// </summary>
        public Footprint RoomFootprint
        {
            get
            {
                IReadOnlyList<PlannedPiece> plan = Plan;
                if (plan.Count == 0) return null;
                if (!ReferenceEquals(plan, _roomPlan))
                {
                    _roomPlan = plan;
                    _roomFootprint = SiteSurvey.RoomFootprint(plan, transform.position, transform.rotation) ?? SiteFootprint;
                }
                return _roomFootprint;
            }
        }

        private IReadOnlyList<PlannedPiece> _roomPlan;
        private Footprint _roomFootprint;

        /// <summary>The ground the building and its flattened site cover (the pieces' outline + 0.5 m); null without a plan.</summary>
        public Footprint SiteFootprint
        {
            get
            {
                IReadOnlyList<PlannedPiece> plan = Plan;
                if (plan.Count == 0) return null;
                if (!ReferenceEquals(plan, _footprintPlan))
                {
                    _footprintPlan = plan;
                    _siteFootprint = SiteSurvey.SiteFootprint(plan, transform.position, transform.rotation);
                }
                return _siteFootprint;
            }
        }

        /// <summary>The player who built the colony's Town Hall; the builder's pieces count as theirs. 0 when unknown.</summary>
        public long ColonyOwner() => ColonyPermissions.OwnerOf(_building);

        /// <summary>
        /// Test command: puts up every planned piece not yet standing, without materials, except where another building is
        /// in the way. A site not yet prepared is flattened first, one spot at a time as the builder does (the game updates
        /// the ground's heights a frame after each change, so changes made together stack up into spikes and pits), and its
        /// pieces go up a few seconds later. Returns how many pieces it puts up.
        /// </summary>
        public int FinishNow()
        {
            BuildOrderRecord order = Order;
            if (order == null) return 0;
            if (order.Kind == OrderKind.Remove)
            {
                Log.Info($"{_building.TypeId} {_building.Id}: is being taken down: not finished at once");   // 0.24: it put the pieces back
                return 0;
            }
            int cleared = ClearSiteNow();
            if (cleared > 0) Log.Info($"{_building.TypeId} {_building.Id}: {cleared} things cleared off the site at once");
            var levels = new List<Vector3>();
            var smooths = new List<Vector3>();
            bool flatten = PlanFlatten(order, levels, smooths);
            List<PlannedPiece> pieces = Plan.Where(p => CanFinish(p, buriedCheck: !flatten)).ToList();
            foreach (PlannedPiece left in Plan.Where(p => !pieces.Contains(p)).Take(12))
                Log.Info($"{_building.TypeId} {_building.Id}: at once, {left.Prefab} at {U(left.Position)} is left out: {WhyNotFinish(left, !flatten)}");
            if (!flatten)
            {
                PutUp(pieces);
                return pieces.Count;
            }
            RequestAdvance(BuildState.Building);   // the builder stops shaping: this flattens it
            StartCoroutine(FlattenThenPutUp(levels, smooths, pieces, order.Ground));
            return pieces.Count;
        }

        /// <summary>
        /// The test shortcut first clears the site as the builder would (trees, plants, stumps, rocks, logs, loose items, old
        /// pieces). Seen: a small beech left standing inside a hut's doorway by "upgrade at once" blocked its door.
        /// A big rock's parts are left: the builder breaks those part by part.
        /// </summary>
        private int ClearSiteNow()
        {
            int cleared = 0;
            foreach (SiteThing thing in ThingsToClear())
            {
                if (thing.IsGone || thing.Area != null) continue;
                ZNetView nview = thing.Root.GetComponent<ZNetView>();
                if (nview == null || !nview.IsValid()) continue;
                nview.ClaimOwnership();
                ZNetScene.instance.Destroy(thing.Root);
                cleared++;
            }
            return cleared;
        }

        /// <summary>What a builder would clear here: on the site, and what grows or lies in front of its doors.</summary>
        internal List<SiteThing> ThingsToClear()
        {
            BuildOrderRecord order = Order;
            float floor = transform.position.y;
            float bottom = (order != null && !float.IsNaN(order.Ground) ? Mathf.Min(order.Ground, floor) : floor) - 3f;
            var things = new List<SiteThing>();
            SiteScanner.Scan(SiteFootprint, bottom, floor + 12f, _building, null, MatchesPlan, true, things, new List<Piece>());
            SiteScanner.AddDoorLandings(DoorLandings, bottom, floor + 12f, _building, MatchesPlan, things);
            return things;
        }

        /// <param name="buriedCheck">Leave out pieces that would be underground (not when the site is being flattened first).</param>
        private bool CanFinish(PlannedPiece piece, bool buriedCheck)
        {
            if (!piece.IsValid || PiecePlacer.FindStanding(piece) != null) return false;
            GameObject prefab = PiecePlacer.Prefab(piece.Prefab);
            if (prefab == null || PiecePlacer.InTheWay(prefab, U(piece.Position), U(piece.Rotation), _building, MatchesPlan) != null) return false;
            if (!buriedCheck) return true;
            PiecePlacer.TopAndGround(prefab, U(piece.Position), U(piece.Rotation), out float top, out float ground);
            return !BuildStance.Buried(top, ground);
        }

        /// <summary>Test shortcut: why <see cref="CanFinish"/> leaves a piece out (for the log).</summary>
        private string WhyNotFinish(PlannedPiece piece, bool buriedCheck)
        {
            if (!piece.IsValid) return "not a valid piece";
            if (PiecePlacer.FindStanding(piece) != null) return "one stands there already";
            GameObject prefab = PiecePlacer.Prefab(piece.Prefab);
            if (prefab == null) return "the game has no such piece";
            Piece inTheWay = PiecePlacer.InTheWay(prefab, U(piece.Position), U(piece.Rotation), _building, MatchesPlan);
            if (inTheWay != null) return $"in the way: {Utils.GetPrefabName(inTheWay.gameObject)} at {inTheWay.transform.position}";
            return buriedCheck ? "underground" : "?";
        }

        /// <summary>
        /// Test command (vc_build mend): every piece of the design that doesn't stand goes back up at once, as the builder's
        /// repairs put them back in time. An upgrade can drop a piece it keeps: re-covering a roof takes away the ridge the
        /// gable's dragon sat on (2026-10-18). After it, a piece still missing can't stand where the design has it.
        /// </summary>
        public int MendNow()
        {
            List<PlannedPiece> pieces = Plan.Where(p => CanFinish(p, buriedCheck: false)).ToList();
            long creator = ColonyOwner();
            foreach (PlannedPiece piece in pieces)
            {
                GameObject prefab = PiecePlacer.Prefab(piece.Prefab);
                if (prefab != null) PiecePlacer.Place(prefab, U(piece.Position), U(piece.Rotation), _building.Id, creator);
            }
            return pieces.Count;
        }

        private void PutUp(IEnumerable<PlannedPiece> pieces)
        {
            long creator = ColonyOwner();
            foreach (PlannedPiece piece in pieces)
            {
                if (PiecePlacer.FindStanding(piece) != null) continue;   // the builder put it up meanwhile
                GameObject prefab = PiecePlacer.Prefab(piece.Prefab);
                if (prefab != null) PiecePlacer.Place(prefab, U(piece.Position), U(piece.Rotation), _building.Id, creator);
            }
            RequestMarkBuilt(true);
        }

        private IEnumerator FlattenThenPutUp(List<Vector3> levels, List<Vector3> smooths, List<PlannedPiece> pieces, float ground)
        {
            // As the builder does: level, let the ground settle, look again where it's still off (up to three passes).
            for (int pass = 0; pass < 3 && levels.Count > 0; pass++)
            {
                foreach (Vector3 at in levels)
                {
                    GroundShaper.Level(at);
                    yield return new WaitForSeconds(FinishStepSeconds);
                }
                yield return new WaitForSeconds(1f);
                levels = GroundOps(ground).Select(o => U(o.Position)).ToList();
            }
            foreach (Vector3 at in smooths)
            {
                GroundShaper.Smooth(at);
                yield return new WaitForSeconds(FinishStepSeconds);
            }
            yield return new WaitForSeconds(FinishStepSeconds);
            PutUp(pieces);
        }

        /// <summary>
        /// The spots to level and smooth for a site not yet prepared, keeping clear of other buildings (smoothing blends the
        /// edge halfway towards the site's level, as the builder does). False when the site needs no flattening.
        /// </summary>
        private bool PlanFlatten(BuildOrderRecord order, List<Vector3> levels, List<Vector3> smooths)
        {
            if (order.State == BuildState.Building || order.State == BuildState.Done || float.IsNaN(order.Ground) || !GroundShaper.Ready) return false;
            Footprint site = SiteFootprint;
            if (site == null) return false;
            levels.AddRange(GroundOps(order.Ground).Select(o => U(o.Position)));
            smooths.AddRange(SmoothSpots().Select(s => new Vector3(s.X, (SiteSurvey.GroundAt(s.X, s.Z) + order.Ground) / 2f, s.Z)));
            return true;
        }

        /// <summary>Test command: removes the pieces the builder put up for this hut. Returns how many.</summary>
        public int RemoveBuilderPieces()
        {
            var pieces = new List<Piece>();
            Piece.GetAllPiecesInRadius(transform.position, 40f, pieces);
            int removed = 0;
            foreach (Piece piece in pieces)
            {
                if (piece == null || PiecePlacer.SiteOf(piece.gameObject) != _building.Id) continue;
                ZNetView nview = piece.GetComponent<ZNetView>();
                if (nview != null && nview.IsValid() && !nview.IsOwner()) nview.ClaimOwnership();
                ZNetScene.instance.Destroy(piece.gameObject);
                removed++;
            }
            return removed;
        }

        /// <summary>
        /// Test command: removes the pieces the builder put up for this hut that its design no longer has (an old level's
        /// pieces an earlier test shortcut left standing). Returns how many.
        /// </summary>
        public int RemoveLeftovers(bool remove = true)
        {
            var pieces = new List<Piece>();
            Piece.GetAllPiecesInRadius(transform.position, 40f, pieces);
            int removed = 0;
            foreach (Piece piece in pieces)
            {
                if (piece == null || piece.gameObject == gameObject || PiecePlacer.SiteOf(piece.gameObject) != _building.Id || MatchesPlan(piece)) continue;
                if (!remove)   // only counted (the old huts' upgrade test)
                {
                    removed++;
                    continue;
                }
                ZNetView nview = piece.GetComponent<ZNetView>();
                if (nview != null && nview.IsValid() && !nview.IsOwner()) nview.ClaimOwnership();
                Log.Info($"{_building.TypeId} {_building.Id}: removed the leftover {Utils.GetPrefabName(piece.gameObject)} at {piece.transform.position}");
                ZNetScene.instance.Destroy(piece.gameObject);
                removed++;
            }
            return removed;
        }

        /// <summary>Whether this standing piece is exactly one of the plan's pieces (same kind, within DesignDiff.SamePlace).</summary>
        public bool MatchesPlan(Piece piece)
        {
            IReadOnlyList<PlannedPiece> plan = Plan;
            if (!ReferenceEquals(plan, _planIndexed))
            {
                _planIndexed = plan as List<PlannedPiece>;
                _planByPrefab.Clear();
                foreach (PlannedPiece p in plan)
                {
                    if (!p.IsValid) continue;
                    if (!_planByPrefab.TryGetValue(p.Prefab, out List<Vector3> spots)) _planByPrefab[p.Prefab] = spots = new List<Vector3>();
                    spots.Add(U(p.Position));
                }
            }
            if (piece == null || !_planByPrefab.TryGetValue(Utils.GetPrefabName(piece.gameObject), out List<Vector3> positions)) return false;
            Vector3 at = piece.transform.position;
            return positions.Any(p => Vector3.Distance(p, at) <= DesignDiff.SamePlace);
        }

        /// <summary>What a hut's window says about its building.</summary>
        public string Describe()
        {
            BuildOrderRecord order = Order;
            if (order == null) return L("$vc_built_before_builders");
            var lines = new List<string>();
            if (order.State == BuildState.Done) lines.Add(L("$vc_built"));
            else
            {
                if (order.Kind == OrderKind.Upgrade)
                    lines.Add(string.Format(L("$vc_upgrading"), order.TargetLevel, order.DoneCount, order.Total, order.TakeAwayLeft));
                else if (order.Kind == OrderKind.Remove)
                    lines.Add(string.Format(L("$vc_taking_down"), order.TakeAwayLeft, order.TakeAwayTotal));
                else if (order.Kind == OrderKind.Repair)
                    lines.Add(string.Format(L("$vc_repairing"), order.Total - order.DoneCount - order.SkippedCount));
                else lines.Add(string.Format(L("$vc_under_construction"), order.DoneCount, order.Total));
                if (order.State == BuildState.Clearing) lines.Add(L("$vc_site_clearing"));
                else if (order.State == BuildState.Shaping) lines.Add(L("$vc_site_shaping"));
                if (order.HasLiveClaim(Now())) lines.Add(string.Format(L("$vc_builder_is"), NameOf(order.Builder)));
                else lines.Add(L(HasBuilderHut() ? "$vc_waiting_for_builder" : "$vc_needs_builder"));
                if (order.Needs.Length > 0) lines.Add(string.Format(L("$vc_needs_list"), L(order.Needs)));
                if (order.Blocked.Length > 0) lines.Add(string.Format(L("$vc_blocked_line"), L(order.Blocked)));
                if (order.Paused) lines.Add($"<color=orange>{L("$vc_order_paused_line")}</color>");
                else if (order.First > 0) lines.Add(L("$vc_order_first_line"));
            }
            if (order.SkippedCount > 0) lines.Add(string.Format(L("$vc_skipped"), order.SkippedCount));
            return string.Join("\n", lines);
        }

        /// <summary>For a Builder's Hut window: what its builder works on, and how many buildings wait.</summary>
        public static string DescribeBuilderWork(ColonyBuilding builderHut, Citizen builder)
        {
            double now = Now();
            var lines = new List<string>();
            ConstructionModule current = builder == null ? null : s_all.FirstOrDefault(s =>
                s != null && s.IsValid && s.Order is BuildOrderRecord o && o.Builder == builder.Id && o.HasLiveClaim(now));
            if (current != null)
            {
                BuildOrderRecord order = current.Order;
                lines.Add(order.Kind == OrderKind.Remove
                    ? string.Format(L("$vc_working_on_takedown"), L(current.Building.Type?.NameToken ?? ""), order.TakeAwayLeft)
                    : string.Format(L("$vc_working_on"), L(current.Building.Type?.NameToken ?? ""), order.DoneCount, order.Total));
            }
            int waiting = s_all.Count(s => s != null && s.IsValid && s != current && s.Building.ColonyId == builderHut.ColonyId
                && s.Order is BuildOrderRecord o && o.State != BuildState.Done);
            if (waiting > 0) lines.Add(string.Format(L("$vc_orders_waiting"), waiting));
            return string.Join("\n", lines);
        }

        private bool HasBuilderHut() => ColonyBuilding.All.Any(b =>
            b != null && b.IsValid && b.TypeId == BuildingTypes.BuilderHutId && b.ColonyId == _building.ColonyId);

        private static string NameOf(string citizenId)
        {
            Citizen loaded = Citizen.All.FirstOrDefault(c => c != null && c.Id == citizenId);
            return loaded != null ? loaded.CitizenName : "?";
        }

        private static string L(string token) => Localization.instance.Localize(token);

        public void RequestClaim(string builderId) => _nview.InvokeRPC(RpcClaim, builderId);
        public void RequestRelease(string builderId) => _nview.InvokeRPC(RpcRelease, builderId);
        public void ReportPiece(int index, int outcome) => _nview.InvokeRPC(RpcPiece, index, outcome);
        public void ReportNeeds(string needs) => _nview.InvokeRPC(RpcNeeds, needs ?? "");
        public void ReportBlocked(string what) => _nview.InvokeRPC(RpcBlocked, what ?? "");
        public void RequestCreateOrder() => _nview.InvokeRPC(RpcCreate);
        public void RequestMarkBuilt(bool allPiecesPlaced) => _nview.InvokeRPC(RpcMarkBuilt, allPiecesPlaced);
        public void RequestAdvance(BuildState to) => _nview.InvokeRPC(RpcState, (int)to);
        public void RequestUpgrade() => _nview.InvokeRPC(RpcUpgrade, DesignLevel + 1);
        /// <summary>The Hall tab's Build the camp (huts round 2).</summary>
        public void RequestStartCamp() => _nview.InvokeRPC(RpcStartCamp);
        public void RequestUpgradeNow() => _nview.InvokeRPC(RpcUpgradeNow);
        public void RequestCancelUpgrade() => _nview.InvokeRPC(RpcCancelUpgrade);
        /// <summary>Take down (0.23): the builder takes the finished hut's building down, then the hut goes.</summary>
        public void RequestTakeDown() => _nview.InvokeRPC(RpcTakeDown);
        /// <summary>Cancel a take-down (review of 0.23): the finished order comes back, and what came down is put back as a repair.</summary>
        public void RequestCancelTakeDown() => _nview.InvokeRPC(RpcCancelTakeDown);
        /// <summary>Test tool: a take-down as if <paramref name="sender"/> asked for it.</summary>
        internal void TakeDownForTest(long sender) => OnTakeDown(sender);
        public void ReportTaken(int index) => _nview.InvokeRPC(RpcTaken, index);
        /// <summary>Town Hall › Work orders: builders take this order before the others (and it's resumed if set aside).</summary>
        public void RequestFirst() => _nview.InvokeRPC(RpcFirst);
        /// <summary>Town Hall › Work orders: no builder takes this order until it's resumed (false resumes it).</summary>
        public void RequestPause(bool paused) => _nview.InvokeRPC(RpcPause, paused);
        /// <summary>Town Hall › Work orders: a repair is left as it is (its missing pieces aren't put back).</summary>
        public void RequestLeaveAsIs() => _nview.InvokeRPC(RpcLeaveAsIs);

        /// <summary>The materials of the pieces still to put up (not up, not skipped); empty for a finished order.</summary>
        public Dictionary<string, int> RemainingNeeds()
        {
            var total = new Dictionary<string, int>();
            BuildOrderRecord order = Order;
            if (order == null || order.State == BuildState.Done) return total;
            IReadOnlyList<PlannedPiece> plan = Plan;
            for (int i = 0; i < plan.Count && i < order.Total; i++)
                if (order.IsOpen(i) && plan[i].IsValid) NeedsTally.Add(total, PiecePlacer.Needs(plan[i].Prefab));
            return total;
        }

        public static double Now() => ZNet.instance != null ? ZNet.instance.GetTimeSeconds() : Time.time;

        private void OnClaim(long sender, string builder) => Change(order =>
        {
            // Set aside by the player: a claim already on its way, or from a computer that hasn't seen the pause yet, is refused.
            if (order.State == BuildState.Done || order.Paused || !order.TryClaim(builder, Now())) return false;
            if (order.State == BuildState.Waiting && order.Kind == OrderKind.Remove) order.Advance(BuildState.Building);   // nothing to clear or flatten (0.23)
            else if (order.State == BuildState.Waiting)
            {
                order.Advance(BuildState.Clearing);
                if (float.IsNaN(order.Ground)) order.Ground = SiteSurvey.Target(Plan, transform.position);   // an order from before site preparation
            }
            return true;
        });

        private void OnState(long sender, int state) => Change(order =>
        {
            if (!System.Enum.IsDefined(typeof(BuildState), state) || order.State == BuildState.Done || !order.Advance((BuildState)state)) return false;
            Log.Info($"{_building.TypeId} {_building.Id}: {(BuildState)state}");
            return true;
        });

        private void OnRelease(long sender, string builder) => Change(order =>
        {
            order.Release(builder);
            return true;
        });

        private void OnPiece(long sender, int index, int outcome) => Change(order =>
        {
            if (outcome == PieceDone) order.MarkDone(index);
            else if (outcome == PieceFailed && order.MarkFailed(index))
                Log.Info($"{_building.TypeId} {_building.Id}: piece {index} is skipped after falling {BuildOrderRecord.MaxFailures} times");
            else if (outcome == PieceGone && order.State != BuildState.Done) order.MarkGone(index);
            else if (outcome == PieceUnderground && order.IsOpen(index))
            {
                order.MarkSkipped(index);
                Log.Info($"{_building.TypeId} {_building.Id}: piece {index} would be underground here; left out");
            }
            return true;
        });

        private void OnBlocked(long sender, string what) => Change(order =>
        {
            if (order.Blocked == what || order.State == BuildState.Done) return false;
            order.Blocked = what;
            return true;
        });

        private void OnNeeds(long sender, string needs) => Change(order =>
        {
            if (order.Needs == needs || order.State == BuildState.Done) return false;
            order.Needs = needs;
            return true;
        });

        private void OnCreate(long sender)
        {
            if (IsValid && _nview.IsOwner() && Order == null && Allowed(sender)) CreateOrder();
        }

        /// <summary>Owner only: the asker may manage this colony; otherwise he is told why nothing happened.</summary>
        private bool Allowed(long sender)
        {
            if (ColonyPermissions.SenderMayManage(_building, sender)) return true;
            Tell(sender, "$vc_not_your_colony");
            return false;
        }

        /// <summary>Shows a message to the player who asked, on any colony building (0.25: before, only job huts showed it).</summary>
        private void Tell(long sender, string token) => _nview.InvokeRPC(sender, RpcMessage, token);

        /// <summary>On the asker's computer: why nothing happened, in the open window or on screen.</summary>
        private void OnMessage(long sender, string token)
        {
            string text = Localization.instance.Localize(token);
            if (Gui.Windows.Hut.IsOpen) Gui.Windows.Hut.ShowMessage(text);
            else if (Gui.Windows.TownHall.IsOpen) Gui.Windows.TownHall.ShowMessage(text);
            else if (Player.m_localPlayer != null) Player.m_localPlayer.Message(MessageHud.MessageType.Center, text);
        }

        private void OnUpgrade(long sender, int target)
        {
            if (!IsValid || !_nview.IsOwner() || target != DesignLevel + 1 || !Allowed(sender)) return;
            UpgradeLock locked = UpgradeCheck();
            if (locked != UpgradeLock.None)
            {
                Log.Info($"{_building.TypeId} {_building.Id}: upgrade to level {target} refused ({locked})");
                Tell(sender, "$vc_upgrade_refused");
                return;
            }
            UpgradePreview preview = PreviewUpgrade();
            if (preview == null || preview.Site.Colour == SiteColour.Red)
            {
                Log.Info($"{_building.TypeId} {_building.Id}: upgrade to level {target} refused ({(preview == null ? "no design" : preview.Site.Red.ToString())})");
                Tell(sender, "$vc_upgrade_refused");
                return;
            }
            CreateUpgrade(target);
        }

        /// <summary>Test command: makes the upgrade if needed, takes the old pieces away at once and puts the new ones up.</summary>
        private void OnUpgradeNow(long sender)
        {
            // A test shortcut (vc_build), but a network message all the same: only who may manage the colony (review of 0.20, bug 4).
            if (!IsValid || !_nview.IsOwner() || !Allowed(sender)) return;
            if (IsTakingDown)
            {
                Log.Info($"{_building.TypeId} {_building.Id}: is being taken down: not upgraded at once");
                Tell(sender, "$vc_upgrade_lock_takedown");
                return;
            }
            if (IsCamp && Order == null && !TryStartCamp(out _)) return;   // a test shortcut: the camp's first level
            if (!IsUpgrading && (DesignLevel >= HutLevels.MaxLevel || !CreateUpgrade(DesignLevel + 1))) return;
            int removed = TakeAwayAllNow();
            StartCoroutine(FinishAfterRemoval(removed));
        }

        /// <summary>
        /// Removed pieces only disappear at the end of the frame, and until then they no longer count as this hut's own
        /// (they would be "in the way"): the new pieces go up a moment later.
        /// </summary>
        private IEnumerator FinishAfterRemoval(int removed)
        {
            yield return null;
            yield return null;
            int placed = FinishNow();
            Log.Info($"{_building.TypeId} {_building.Id}: upgrading at once to level {Order?.TargetLevel}: {removed} old pieces removed, {placed} going up");
        }

        private void OnFirst(long sender)
        {
            if (!IsValid || !_nview.IsOwner() || !Allowed(sender)) return;
            Change(order =>
            {
                if (order.State == BuildState.Done) return false;
                order.First = Now();
                order.Paused = false;
                Log.Info($"{_building.TypeId} {_building.Id}: its order is to be done first");
                return true;
            });
        }

        private void OnPause(long sender, bool paused)
        {
            if (!IsValid || !_nview.IsOwner() || !Allowed(sender)) return;
            Change(order =>
            {
                if (order.State == BuildState.Done || order.Paused == paused) return false;
                order.Paused = paused;
                if (paused) order.Release(order.Builder);   // the builder on it leaves it
                Log.Info($"{_building.TypeId} {_building.Id}: its order is {(paused ? "set aside" : "resumed")}");
                return true;
            });
        }

        /// <summary>
        /// Owner: a take-down is ordered (0.23). Every piece of the building standing now is to be taken away, from the top,
        /// as an upgrade's old pieces are; when none stand, the hut goes (<see cref="FinishTakeDown"/>).
        /// </summary>
        private void OnTakeDown(long sender)
        {
            if (!IsValid || !_nview.IsOwner() || !MayTakeDown || !Allowed(sender)) return;
            // Every piece must be loaded, or those out of reach would stay standing (0.26, review of 0.23).
            Footprint site = SiteFootprint;
            if (site != null && ZoneSystem.instance != null && site.Corners().Any(c => !ZoneSystem.instance.IsZoneLoaded(CoreVectors.U(c))))
            {
                Log.Info($"{_building.TypeId} {_building.Id}: not taken down: part of it isn't loaded");
                Tell(sender, "$vc_takedown_not_loaded");
                return;
            }
            IReadOnlyList<PlannedPiece> plan = Plan;
            var near = new List<Piece>();
            Piece.GetAllPiecesInRadius(transform.position, PlanRadius(plan), near);
            var takeAway = new List<PlannedPiece>();
            foreach (PlannedPiece p in plan)
                if (p.IsValid && PiecePlacer.FindStanding(p, near) != null)
                    takeAway.Add(new PlannedPiece(takeAway.Count, p.Prefab, p.Position, p.Rotation));
            // An old level's leftovers built for this hut come down too (0.24; they stood on after it went).
            foreach (Piece piece in near)
                if (piece != null && piece.gameObject != gameObject && piece.GetComponent<ColonyBuilding>() == null   // never the hut itself (its chest goes last)
                    && PiecePlacer.SiteOf(piece.gameObject) == _building.Id && !MatchesPlan(piece))
                    takeAway.Add(new PlannedPiece(takeAway.Count, Utils.GetPrefabName(piece.gameObject),
                        CoreVectors.V(piece.transform.position), CoreVectors.Q(piece.transform.rotation)));
            ZDO zdo = _nview.GetZDO();
            zdo.Set(PrevOrderKey, zdo.GetString(OrderKey, ""));   // kept for a cancel (review of 0.23)
            zdo.Set(TakeAwayKey, takeAway.Count > 0 ? PlanStore.Pack(PlanCodec.Encode(takeAway)) : new byte[0]);
            BuildOrderRecord order = BuildOrderRecord.ForRemove(takeAway.Count, 1, Now());   // level 1: any builder may take it down
            zdo.Set(OrderKey, order.Encode());
            Log.Info($"{_building.TypeId} {_building.Id}: to be taken down ({takeAway.Count} pieces)");
            NoteDropsBefore(plan);
            if (takeAway.Count == 0) Change(o => true);   // nothing stands: it goes at once
        }

        private void OnCancelTakeDown(long sender)
        {
            if (!IsValid || !_nview.IsOwner() || !IsTakingDown || _finishing || !Allowed(sender)) return;
            ZDO zdo = _nview.GetZDO();
            zdo.Set(OrderKey, zdo.GetString(PrevOrderKey, ""));
            zdo.Set(PrevOrderKey, "");
            zdo.Set(TakeAwayKey, new byte[0]);
            Log.Info($"{_building.TypeId} {_building.Id}: take-down cancelled (what came down goes back as a repair)");
        }

        private bool _finishing;
        private const float ChestWaitSeconds = 5f;

        /// <summary>
        /// Owner: the last piece is down (0.23). The Town Hall's chest may be another computer's: it is asked for over a
        /// few seconds before anything drops (review of 0.23: in multiplayer everything dropped at once).
        /// </summary>
        private System.Collections.IEnumerator FinishTakeDownSoon()
        {
            if (_finishing) yield break;
            _finishing = true;
            // The last piece's materials land and settle, and those another computer drops reach this one (review of 0.24);
            // the ones lying there are asked for, so they are this computer's to move.
            yield return new WaitForSeconds(1f);
            foreach (ItemDrop drop in MaterialDrops())
            {
                ZNetView view = drop.GetComponent<ZNetView>();
                if (view != null && view.IsValid() && !view.IsOwner()) view.ClaimOwnership();
            }
            yield return new WaitForSeconds(0.5f);
            ColonyBuilding hall = ColonyBuilding.FindTownHall(_building.ColonyId);
            for (float waited = 0f; waited < ChestWaitSeconds; waited += 0.5f)
            {
                bool ownReady = _building.Storage == null || _building.Storage.Claim() == StorageModule.Access.Ready;   // reloads: no stale copy (review of 0.23)
                bool hallReady = hall == null || hall.Storage == null || hall.Storage.Claim() == StorageModule.Access.Ready;
                if (ownReady && hallReady) break;
                yield return new WaitForSeconds(0.5f);
            }
            if (this == null || !IsValid) yield break;
            // Only the computer that owns the hut and has its chest may empty it and let it go: another one's copy of the
            // chest could be handed over twice (the whole-mod review of 1.4.1's builder, M4). The minute's check tries again.
            bool chestReady = _building.Storage == null || _building.Storage.Claim() == StorageModule.Access.Ready;
            if (!_nview.IsOwner() || !chestReady)
            {
                Log.Info($"{_building.TypeId} {_building.Id}: its last piece is down, but its chest isn't this computer's to empty yet; tried again in a minute");
                _finishing = false;
                yield break;
            }
            FinishTakeDown();
        }

        /// <summary>
        /// Owner: the last piece is down (0.23). The hut's chest goes too: its contents into the Town Hall's chest (what
        /// doesn't fit, or with the Town Hall's chest busy, drops by the hut), its worker is let go, the saga tells it.
        /// </summary>
        private void FinishTakeDown()
        {
            Container own = GetComponent<Container>();
            Inventory items = own != null ? own.GetInventory() : null;
            ColonyBuilding hall = ColonyBuilding.FindTownHall(_building.ColonyId);
            Inventory hallItems = hall != null && hall.Storage != null && hall.Storage.Claim() == StorageModule.Access.Ready
                ? hall.GetComponent<Container>()?.GetInventory() : null;
            if (items != null && items.NrOfItems() > 0)
            {
                int before = items.NrOfItems();
                if (hallItems != null)
                    foreach (ItemDrop.ItemData item in items.GetAllItems().ToList())
                        if (hallItems.CanAddItem(item) && hallItems.AddItem(item)) items.RemoveItem(item);
                // What the Town Hall's chest didn't take drops by the hut, as when a player breaks a chest.
                if (items.NrOfItems() > 0)
                    Log.Info($"{_building.TypeId} {_building.Id}: {items.NrOfItems()} of {before} stacks from its chest drop by it (the Town Hall's chest {(hallItems == null ? "wasn't free" : "is full")})");
                foreach (ItemDrop.ItemData item in items.GetAllItems().ToList())
                {
                    ItemDrop.DropItem(item, item.m_stack, transform.position + Vector3.up + Random.insideUnitSphere * 0.5f, Quaternion.identity);
                    items.RemoveItem(item);
                }
            }
            SweepMaterials(hallItems);   // after the chest's own contents: they come first for the Town Hall's room (review of 0.24)
            _building.GetComponent<WorkerModule>()?.LetGo();
            Saga.Record(_building.ColonyId, "takendown", Saga.HutWord(_building));
            Log.Info($"{_building.TypeId} {_building.Id}: taken down");
            ZNetScene.instance.Destroy(gameObject);
        }

        /// <summary>
        /// The materials of the last pieces lie where the hut stood (the builder left with the order done): what of them
        /// lies within its plan goes into the Town Hall's chest (0.24, review of 0.23). Nothing else is touched.
        /// </summary>
        private void SweepMaterials(Inventory hallItems)
        {
            if (hallItems == null) return;
            int moved = 0;
            foreach (ItemDrop drop in MaterialDrops())
            {
                ZNetView view = drop.GetComponent<ZNetView>();
                if (view == null || !view.IsValid() || !view.IsOwner()) continue;   // only this computer's: never one another computer still holds (no copies)
                drop.Load();   // its saved size
                if (!hallItems.CanAddItem(drop.m_itemData) || !hallItems.AddItem(drop.m_itemData.Clone())) continue;
                ZNetScene.instance.Destroy(drop.gameObject);
                moved++;
            }
            if (moved > 0) Log.Info($"{_building.TypeId} {_building.Id}: {moved} stacks of its materials picked up where it stood");
        }

        /// <summary>The drops lying round the hut when its take-down was ordered (this computer, this session): not its own.</summary>
        private HashSet<ZDOID> _dropsBefore;

        private void NoteDropsBefore(IReadOnlyList<PlannedPiece> plan)
        {
            Jobs.WorldScan.InvalidateDrops();
            _dropsBefore = new HashSet<ZDOID>(Jobs.WorldScan.Drops()
                .Where(d => d != null && NearItsGround(d.transform.position, plan))
                .Select(IdOf).Where(id => id != ZDOID.None));
        }

        /// <summary>Within TakeDownSweep.RolledFar of the hut's ground (its site, grown by the site margin).</summary>
        private bool NearItsGround(Vector3 at, IReadOnlyList<PlannedPiece> plan)
        {
            Footprint ground = SiteFootprint?.Grow(SiteSurvey.SiteMargin);
            return ground != null ? ground.Grow(TakeDownSweep.RolledFar).Contains(CoreVectors.V(at))
                                  : Vector3.Distance(at, transform.position) <= PlanRadius(plan) + TakeDownSweep.RolledFar;
        }

        private static ZDOID IdOf(ItemDrop drop)
        {
            ZNetView view = drop != null ? drop.GetComponent<ZNetView>() : null;
            return view != null && view.IsValid() ? view.GetZDO().m_uid : ZDOID.None;
        }

        /// <summary>
        /// Loose drops of the building's materials, from a fresh look (the cached list may be 2 s old): on the hut's own
        /// ground (its site, grown by the site margin), and those that came down during its take-down and rolled off it, a
        /// few metres further (TakeDownSweep; the second full run on 1.4.0: on a hillock a stack rolled off and was left).
        /// A player's pile beside the hut, lying there before, stays where it is (0.26, review of 0.24).
        /// </summary>
        private List<ItemDrop> MaterialDrops()
        {
            IReadOnlyList<PlannedPiece> plan = Plan;
            var materials = new HashSet<string>(plan.Concat(TakeAwayPlan).Where(p => p.IsValid).SelectMany(p => PiecePlacer.Needs(p.Prefab).Keys));
            Footprint ground = SiteFootprint?.Grow(SiteSurvey.SiteMargin);
            float radius = PlanRadius(plan);
            Jobs.WorldScan.InvalidateDrops();
            return Jobs.WorldScan.Drops().Where(d => d != null && !d.IsPiece() && materials.Contains(Utils.GetPrefabName(d.gameObject))
                                                      && TakeDownSweep.Takes(
                                                          ground != null ? ground.Contains(CoreVectors.V(d.transform.position))
                                                                         : Vector3.Distance(d.transform.position, transform.position) <= radius,
                                                          _dropsBefore != null && !_dropsBefore.Contains(IdOf(d)),
                                                          NearItsGround(d.transform.position, plan))).ToList();
        }

        private void OnCancelUpgrade(long sender)
        {
            if (!IsValid || !_nview.IsOwner() || !CanCancelUpgrade || !Allowed(sender)) return;
            ZDO zdo = _nview.GetZDO();
            zdo.Set(PlanKey, zdo.GetByteArray(PrevPlanKey) ?? new byte[0]);
            zdo.Set(OrderKey, zdo.GetString(PrevOrderKey, ""));
            ClearUpgradeKeys(zdo);
            Log.Info($"{_building.TypeId} {_building.Id}: upgrade cancelled");
        }

        private void OnTaken(long sender, int index) => Change(order =>
        {
            if ((order.Kind != OrderKind.Upgrade && order.Kind != OrderKind.Remove) || order.TakenAway.Contains(index)) return false;
            order.MarkTaken(index);
            return true;
        });

        /// <summary>
        /// Owner only: the next level's design becomes the hut's plan. Pieces the same in both designs stay (done when they
        /// stand); the old design's other pieces are to be taken away. The old plan and order are kept for a cancel. The
        /// ground stays where the hut has it when the new design stands on it, so nothing is dug away under the old hut.
        /// </summary>
        private bool CreateUpgrade(int target)
        {
            List<PlannedPiece> plan = NextLevelPlan(out string problem);
            if (plan == null || target != DesignLevel + 1)
            {
                Log.Warn($"{_building.TypeId} {_building.Id}: can't upgrade to level {target} ({problem})");
                return false;
            }
            IReadOnlyList<PlannedPiece> oldPlan = Plan;
            DesignDiff diff = DesignDiff.Compare(oldPlan, plan);
            var takeAway = new List<PlannedPiece>();
            foreach (int index in diff.TakeAway)
                takeAway.Add(new PlannedPiece(takeAway.Count, oldPlan[index].Prefab, oldPlan[index].Position, oldPlan[index].Rotation));
            ZDO zdo = _nview.GetZDO();
            zdo.Set(PrevPlanKey, zdo.GetByteArray(PlanKey) ?? new byte[0]);
            zdo.Set(PrevOrderKey, zdo.GetString(OrderKey, ""));
            zdo.Set(PlanKey, PlanStore.Pack(PlanCodec.Encode(plan)));
            zdo.Set(TakeAwayKey, takeAway.Count > 0 ? PlanStore.Pack(PlanCodec.Encode(takeAway)) : new byte[0]);
            var order = new BuildOrderRecord
            {
                Kind = OrderKind.Upgrade,
                TargetLevel = target,
                Watched = true,
                Created = Now(),
                Total = plan.Count,
                TakeAwayTotal = takeAway.Count,
                Ground = SiteSurvey.UpgradeTarget(plan, transform.position),
            };
            foreach (int kept in diff.Kept)
                if (PiecePlacer.FindStanding(plan[kept]) != null) order.MarkKept(kept);
            zdo.Set(OrderKey, order.Encode());
            Log.Info($"{_building.TypeId} {_building.Id}: upgrade to level {target}: {diff.Kept.Count} pieces kept, {takeAway.Count} to take away, " +
                     $"{diff.Build.Count} to build; ground level {order.Ground:0.00}");
            return true;
        }

        /// <summary>Test command: takes every old piece of a running upgrade away at once (no materials drop).</summary>
        private int TakeAwayAllNow()
        {
            IReadOnlyList<PlannedPiece> old = TakeAwayPlan;
            int removed = 0;
            for (int i = 0; i < old.Count; i++)
            {
                Piece standing = PiecePlacer.FindStanding(old[i]);
                if (standing != null && PiecePlacer.SiteOf(standing.gameObject) == _building.Id && !PiecePlacer.IsPlayersBed(standing))
                {
                    ZNetView nview = standing.GetComponent<ZNetView>();
                    if (nview != null && nview.IsValid() && !nview.IsOwner()) nview.ClaimOwnership();
                    ZNetScene.instance.Destroy(standing.gameObject);
                    removed++;
                }
                ReportTaken(i);
            }
            return removed;
        }

        /// <summary>Owner only: the upgrade is finished — the hut has its new level, and the upgrade's leftovers go.</summary>
        private void FinishUpgrade(BuildOrderRecord order)
        {
            ClearUpgradeKeys(_nview.GetZDO());
            if (IsCamp)
            {
                SetCampLevel(order.TargetLevel);   // the hall keeps its own level
                return;
            }
            _building.SetLevel(order.TargetLevel);
            Saga.Record(_building.ColonyId, "level", Saga.HutWord(_building), order.TargetLevel.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Log.Info($"{_building.TypeId} {_building.Id}: now level {order.TargetLevel}");
        }

        /// <summary>The building of a Build order is finished: a hut's saga line, or the camp's first level.</summary>
        private void FinishBuild(BuildOrderRecord order)
        {
            if (IsCamp) SetCampLevel(order.TargetLevel);
            else Saga.Record(_building.ColonyId, "built", Saga.HutWord(_building));
        }

        private static void ClearUpgradeKeys(ZDO zdo)
        {
            zdo.Set(TakeAwayKey, new byte[0]);
            zdo.Set(PrevPlanKey, new byte[0]);
            zdo.Set(PrevOrderKey, "");
        }

        private void OnMarkBuilt(long sender, bool allPiecesPlaced) => Change(order =>
        {
            if (order.Kind == OrderKind.Remove)
            {
                Log.Info($"{_building.TypeId} {_building.Id}: is being taken down: not marked built");   // 0.24: it never finished
                return false;
            }
            if (allPiecesPlaced)
            {
                // Only what stands (review of 0.13: a piece the shortcut couldn't put up counted as built, and repairs
                // chased it for ever).
                IReadOnlyList<PlannedPiece> plan = Plan;
                var near = new List<Piece>();
                Piece.GetAllPiecesInRadius(transform.position, PlanRadius(plan), near);
                for (int i = 0; i < order.Total; i++)
                {
                    if (i < plan.Count && plan[i].IsValid && PiecePlacer.FindStanding(plan[i], near) != null) order.MarkDone(i);
                    else if (!order.Done.Contains(i)) order.MarkSkipped(i);   // couldn't go up here
                }
                for (int i = 0; i < order.TakeAwayTotal; i++) order.MarkTaken(i);
            }
            bool upgrade = order.Kind == OrderKind.Upgrade && order.State != BuildState.Done;
            bool built = order.Kind == OrderKind.Build && order.State != BuildState.Done;
            order.MarkBuilt();
            Log.Info($"{_building.TypeId} {_building.Id}: counted as built");
            if (upgrade) FinishUpgrade(order);
            if (built) FinishBuild(order);
            return true;
        });

        /// <summary>
        /// Owner only: lets <paramref name="change"/> edit a fresh copy of the order (the cached one is shared with
        /// readers), skips pieces that can never be built, finishes the order once every piece is up or skipped, and
        /// saves it when anything changed.
        /// </summary>
        private void Change(System.Func<BuildOrderRecord, bool> change)
        {
            if (!IsValid || !_nview.IsOwner()) return;
            BuildOrderRecord order = BuildOrderRecord.Decode(_nview.GetZDO().GetString(OrderKey, ""));
            if (order == null) return;
            bool changed = change(order);
            changed |= SkipUnbuildable(order);
            if (order.Kind == OrderKind.Remove && order.State != BuildState.Done && order.TakeAwayLeft == 0)
            {
                order.MarkBuilt();
                _nview.GetZDO().Set(OrderKey, order.Encode());
                StartCoroutine(FinishTakeDownSoon());   // the hut goes once the chests are ready
                return;
            }
            if (order.State != BuildState.Done && order.Total > 0 && order.AllHandled)
            {
                order.MarkBuilt();
                Log.Info($"{_building.TypeId} {_building.Id}: {(order.Kind == OrderKind.Repair ? "repaired" : "building finished")} " +
                         $"({order.DoneCount} of {order.Total} pieces, {order.SkippedCount} skipped)");
                if (order.Kind == OrderKind.Upgrade) FinishUpgrade(order);
                else if (order.Kind == OrderKind.Build) FinishBuild(order);
                changed = true;
            }
            if (changed) _nview.GetZDO().Set(OrderKey, order.Encode());
        }

        /// <summary>Pieces whose saved plan line is damaged can never be built: they're skipped so the order can finish.</summary>
        private bool SkipUnbuildable(BuildOrderRecord order)
        {
            IReadOnlyList<PlannedPiece> plan = Plan;
            bool changed = false;
            for (int i = 0; i < order.Total; i++)
            {
                if ((i < plan.Count && plan[i].IsValid) || !order.IsOpen(i)) continue;
                order.MarkSkipped(i);
                changed = true;
            }
            return changed;
        }

        private void OnLeaveAsIs(long sender)
        {
            if (!IsValid || !_nview.IsOwner() || !IsRepairing || !Allowed(sender)) return;
            Change(order =>
            {
                int left = order.LeaveAsItIs();
                if (left > 0) Log.Info($"{_building.TypeId} {_building.Id}: repair left as it is ({left} pieces stay away)");
                return left > 0;
            });
        }

        private void RepairCheck()
        {
            BuildOrderRecord order = IsValid && _nview.IsOwner() ? Order : null;
            if (order != null && order.Kind == OrderKind.Remove && order.State == BuildState.Done) StartCoroutine(FinishTakeDownSoon());
            else CheckForRepair();
        }

        /// <summary>
        /// Owner only (every minute, and the self-test): a finished building whose pieces are gone reopens its order as a
        /// repair, with the setting on, no raid there and no monsters by the hut. Returns how many pieces are to go back.
        /// </summary>
        internal int CheckForRepair()
        {
            if (!IsValid || !_nview.IsOwner() || string.IsNullOrEmpty(_building.ColonyId)) return 0;
            BuildOrderRecord order = Order;
            IReadOnlyList<PlannedPiece> plan = Plan;
            if (order == null || order.State != BuildState.Done || plan.Count == 0 || order.Total != plan.Count) return 0;
            if (!RepairRules.MayRebuild(ColonyConfig.RebuildMissingPieces.Value, RaidHere(), MonstersNear())) return 0;
            // The chest's area ready means the zones round it are loaded: every piece of the building with them (review of
            // 0.13: a check per piece cost much and missed pieces across a zone's edge).
            if (ZNetScene.instance == null || !ZNetScene.instance.IsAreaReady(transform.position)) return 0;
            var near = new List<Piece>();
            Piece.GetAllPiecesInRadius(transform.position, PlanRadius(plan), near);
            List<int> missing = RepairRules.Missing(order, i => i >= plan.Count || !plan[i].IsValid || IsPlant(plan[i].Prefab)
                || PiecePlacer.FindStanding(plan[i], near) != null);
            if (missing.Count == 0) return 0;
            BuildOrderRecord fresh = BuildOrderRecord.Decode(_nview.GetZDO().GetString(OrderKey, ""));
            if (fresh == null) return 0;
            string before = fresh.Encode();
            if (!fresh.Watched)
            {
                // A building from before 0.13: what's gone now was taken away by the player, or long ago. Left out, once.
                int left = fresh.TakeBaseline(missing);
                _nview.GetZDO().Set(OrderKey, fresh.Encode());
                Log.Info($"{_building.TypeId} {_building.Id}: repairs start; {left} pieces gone from before are left out");
                return 0;
            }
            bool reopened = fresh.ReopenForRepair(missing, Now());
            if (fresh.Encode() != before) _nview.GetZDO().Set(OrderKey, fresh.Encode());
            if (!reopened)
            {
                Log.Info($"{_building.TypeId} {_building.Id}: {missing.Count} pieces keep falling after they're put back; left out");
                return 0;
            }
            int open = fresh.Total - fresh.DoneCount - fresh.SkippedCount;
            Log.Event($"{_building.TypeId} {_building.Id}: {open} pieces of its building are gone; the order reopens as a repair " +
                      $"({string.Join(", ", missing.Take(5).Select(i => plan[i].Prefab))}{(missing.Count > 5 ? ", …" : "")})");
            return open;
        }

        private static readonly Dictionary<string, bool> s_isPlant = new Dictionary<string, bool>();

        /// <summary>A crop or sapling saved in a design (it grows into something else, so it never "stands" as planned).</summary>
        private static bool IsPlant(string prefabName)
        {
            if (s_isPlant.TryGetValue(prefabName, out bool plant)) return plant;
            GameObject prefab = PiecePlacer.Prefab(prefabName);
            plant = prefab != null && (prefab.GetComponent<Plant>() != null || prefab.GetComponent<Pickable>() != null);
            if (prefab != null) s_isPlant[prefabName] = plant;
            return plant;
        }

        /// <summary>
        /// Test command (vc_build stands): how many of the design's pieces stand now, each one that doesn't (it fell, or
        /// never went up), and the pieces nearest to falling, at their places in the design (from the chest). 2026-10-18:
        /// the generated huts' grausten roofs fell off their wooden walls and nothing said so.
        /// </summary>
        internal List<string> StandReport(out int total, out int standing)
        {
            IReadOnlyList<PlannedPiece> plan = Plan;
            var near = new List<Piece>();
            Piece.GetAllPiecesInRadius(transform.position, PlanRadius(plan), near);
            var missing = new List<string>();
            var weak = new List<KeyValuePair<float, string>>();
            total = standing = 0;
            foreach (PlannedPiece p in plan)
            {
                if (!p.IsValid) continue;
                total++;
                Vector3 at = transform.InverseTransformPoint(U(p.Position));
                string where = System.FormattableString.Invariant($"{p.Prefab} at ({at.x:0.##}, {at.y:0.##}, {at.z:0.##})");
                Piece found = PiecePlacer.FindStanding(p, near);
                if (found == null)
                {
                    missing.Add(where);
                    continue;
                }
                standing++;
                WearNTear wear = found.GetComponent<WearNTear>();
                if (wear == null) continue;
                SupportMaterial m = SupportProbe.MaterialOf(wear);
                float support = SupportProbe.SupportOf(wear);
                if (support < m.MinSupport * 1.5f)
                    weak.Add(new KeyValuePair<float, string>(support / m.MinSupport, System.FormattableString.Invariant($"{where}: support {support:0}, falls below {m.MinSupport:0}")));
            }
            var lines = missing.Select(w => "missing " + w).ToList();
            lines.AddRange(weak.OrderBy(w => w.Key).Take(8).Select(w => "weak " + w.Value));
            return lines;
        }

        /// <summary>
        /// Test command (vc_build costchest): the materials of the pieces this level added to the one before (all its
        /// pieces at level 1), as the builder needed them; the hut's own chest isn't counted.
        /// </summary>
        internal Dictionary<string, int> LevelCost()
        {
            IReadOnlyList<PlannedPiece> plan = Plan;
            IEnumerable<int> added = Enumerable.Range(0, plan.Count);
            BuildingType type = _building.Type;
            if (DesignLevel > 1 && type != null)
            {
                BlueprintFile before = BlueprintLibrary.For(type, DesignLevel - 1, out _);
                if (before != null && BlueprintPlan.TryPlace(before, type.PrefabName, V(transform.position), Q(transform.rotation), out List<PlannedPiece> previous, out _))
                    added = DesignDiff.Compare(previous, plan).Build;
            }
            var needs = new Dictionary<string, int>();
            foreach (int index in added)
            {
                if (!plan[index].IsValid || plan[index].Prefab.StartsWith("vc_")) continue;
                foreach (KeyValuePair<string, int> need in PiecePlacer.Needs(plan[index].Prefab))
                    needs[need.Key] = (needs.TryGetValue(need.Key, out int n) ? n : 0) + need.Value;
            }
            return needs;
        }

        /// <summary>Test command: how far the plan's pieces reach in the hut's own space (x from the chest's right, z to its front).</summary>
        internal void PlanReach(out float maxX, out float maxZ)
        {
            maxX = maxZ = 0f;
            foreach (PlannedPiece p in Plan)
            {
                if (!p.IsValid) continue;
                Vector3 local = transform.InverseTransformPoint(U(p.Position));
                maxX = Mathf.Max(maxX, local.x);
                maxZ = Mathf.Max(maxZ, local.z);
            }
        }

        /// <summary>How far from the chest the plan's pieces reach (to gather them once).</summary>
        private float PlanRadius(IReadOnlyList<PlannedPiece> plan)
        {
            float radius = 1f;
            foreach (PlannedPiece p in plan)
                if (p.IsValid) radius = Mathf.Max(radius, Vector3.Distance(U(p.Position), transform.position) + 1f);
            return radius;
        }

        /// <summary>
        /// A piece of this building was taken away with a hammer (a player, or a builder clearing another site): a finished
        /// building leaves it out instead of putting it back (review of 0.13). Any computer; the owner changes the order.
        /// </summary>
        internal void ReportRemoved(Piece piece)
        {
            if (!IsValid || piece == null) return;
            IReadOnlyList<PlannedPiece> plan = Plan;
            var one = new List<Piece> { piece };
            for (int i = 0; i < plan.Count; i++)
            {
                if (!plan[i].IsValid || PiecePlacer.FindStanding(plan[i], one) == null) continue;
                _nview.InvokeRPC(RpcRemoved, i);
                return;
            }
        }

        private void OnRemoved(long sender, int index) => Change(order =>
        {
            bool left = order.SkipRemoved(index);
            if (left) Log.Info($"{_building.TypeId} {_building.Id}: piece {index} was taken away with a hammer; it's left out");
            return left;
        });

        /// <summary>A Valheim raid is on here.</summary>
        private bool RaidHere()
        {
            if (AI.CitizenAI.RaidForTest) return true;
            RandomEvent raid = RandEventSystem.instance != null ? RandEventSystem.instance.GetCurrentRandomEvent() : null;
            Vector3 at = transform.position;
            return raid != null && IndoorRules.RaidCovers(new GroundPoint(raid.m_pos.x, raid.m_pos.z), raid.m_eventRange, new GroundPoint(at.x, at.z), at.y);
        }

        /// <summary>A monster (not an animal, a tame one, a dwarf or a settler) is by the hut.</summary>
        private bool MonstersNear()
        {
            foreach (Character c in Character.GetAllCharacters())
            {
                if (c == null || c.IsDead() || c.IsPlayer() || c.IsTamed() || c.GetComponent<MonsterAI>() == null) continue;
                Character.Faction faction = c.GetFaction();
                if (faction == Character.Faction.AnimalsVeg || faction == Character.Faction.Players || faction == Character.Faction.Dverger
                    || faction == Character.Faction.PlayerSpawned || faction == Character.Faction.TrainingDummy) continue;
                if (Vector3.Distance(c.transform.position, transform.position) <= RepairRules.MonsterRange) return true;
            }
            return false;
        }

        /// <summary>
        /// Owner only: plans the building from the hut's blueprint. Without a usable blueprint the hut counts as built; the
        /// Town Hall's camp is then left unordered (none until there is a design for it).
        /// </summary>
        private void CreateOrder()
        {
            BuildingType type = _building.Type;
            int level = IsCamp ? CampRules.FirstLevel : _building.Level;
            var order = new BuildOrderRecord { Created = Now(), TargetLevel = level, Watched = true };
            string source = null, problem = "no blueprint found";
            List<PlannedPiece> pieces = null;
            BlueprintFile blueprint = type != null ? BlueprintLibrary.For(type, level, out source) : null;
            if (IsCamp && blueprint == null) return;
            if (blueprint != null && BlueprintPlan.TryPlace(blueprint, type.PrefabName, V(transform.position), Q(transform.rotation), out pieces, out problem))
            {
                order.Total = pieces.Count;
                order.Ground = SiteSurvey.Target(pieces, transform.position);   // measured now, before anyone changes the ground
                _nview.GetZDO().Set(PlanKey, PlanStore.Pack(PlanCodec.Encode(pieces)));
                Log.Info($"{_building.TypeId} {_building.Id}: build order for {pieces.Count} pieces ({source}); ground level {order.Ground:0.00} (chest at {transform.position.y:0.00})");
            }
            else
            {
                order.MarkBuilt();
                Log.Warn($"{_building.TypeId} {_building.Id}: no usable blueprint ({problem}); the hut counts as built.");
            }
            _nview.GetZDO().Set(OrderKey, order.Encode());
        }
    }

    /// <summary>What an upgrade to the next level needs, for the hut's window.</summary>
    public sealed class UpgradePreview
    {
        public UpgradePreview(int level, Dictionary<string, int> needs, SiteVerdict site, SiteFindings findings = null)
        {
            Level = level;
            Needs = needs;
            Site = site;
            Findings = findings;
        }

        public int Level { get; }
        public Dictionary<string, int> Needs { get; }
        public SiteVerdict Site { get; }
        /// <summary>What the site check found (for test commands).</summary>
        public SiteFindings Findings { get; }
    }
}
