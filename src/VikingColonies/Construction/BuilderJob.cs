using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VikingColonies.AI;
using VikingColonies.Buildings;
using VikingColonies.Citizens;
using VikingColonies.Config;
using VikingColonies.Core;
using VikingColonies.Jobs;
using static VikingColonies.Construction.CoreVectors;

namespace VikingColonies.Construction
{
    /// <summary>
    /// The builder: takes a build order (his own hut first, then the oldest in the colony; a site that waits on the player
    /// is set aside for a while, see BuilderJob.MoveOn), fetches materials from the
    /// colony's chests, and puts the pieces up one by one from spots around the site — lowest first, and only where the
    /// game's support rule says they'll hold. Each new piece is watched for a few seconds before it counts as done.
    /// </summary>
    public partial class BuilderJob : Job
    {
        private const float ClaimRenewSeconds = 20f;
        private const float WatchSeconds = 5f;
        private const float StandingCheckSeconds = 15f;
        private const float RecheckSeconds = 5f;
        private const float DropScanSeconds = 3f;
        private const float ChestReach = 2.5f;
        private const int ProbesPerTick = 6;
        private const int UpcomingPieces = 30;
        private const float FootprintMargin = 0.3f;   // around the pieces' real outline
        private const float RingOutside = 1f;         // he stands just outside the building
        private const float RingSpacing = 1.5f;
        private const float DropRadius = 4f;
        private const float TellPlayerRange = 30f;
        private const float RetrySeconds = 5f;   // a stuck or idle builder looks again this often

        private struct Watched
        {
            public int Index;
            public GameObject Piece;
            public float Until;
            public float Support;
            public float PlacedAt;
            public float GameSupport;   // what the game said after a second; NaN until then
        }

        private readonly ColonyBuilding _hut;
        private readonly List<Vector3> _spots = new List<Vector3>();
        private readonly List<Vec3> _coreSpots = new List<Vec3>();
        private readonly HashSet<int> _badSpots = new HashSet<int>();
        private readonly List<Watched> _watching = new List<Watched>();
        private readonly StuckDetector _walkStuck = new StuckDetector();
        private bool _toldFromHere;   // the log already says he reaches a chest from where he stopped
        private readonly ChestRound _chestRound = new ChestRound();   // a chest through a wall: its open side, kept per chest
        private readonly Dictionary<string, Vector3> _roundSpots = new Dictionary<string, Vector3>();
        private readonly TargetBlacklist _badDrops = new TargetBlacklist(300);
        private readonly BuildTally _tally = new BuildTally();
        private readonly HashSet<string> _told = new HashSet<string>();
        private readonly Cooldown _stuckRetry = new Cooldown(RetrySeconds);
        private readonly Cooldown _lookAgain = new Cooldown(RetrySeconds);
        private readonly List<Piece> _sitePieces = new List<Piece>();
        private int _sitePiecesFrame = -1;
        private bool _wasInAttack;
        private System.Action _onSwingLands;   // what happens when the hammer lands: a piece goes up or comes down
        private bool _swingPlaced = true;
        private float _swingStarted;
        private bool _toldNoLanding;
        private readonly Dictionary<int, string> _inTheWay = new Dictionary<int, string>();
        private readonly RecentSpots _falls = new RecentSpots(60);
        private Piece _clear;   // a leftover of an old colony building he takes down to make room
        private int _clearTakeAway = -1;   // the index of the upgrade's old piece he is taking down, or -1
        private IReadOnlyList<PlannedPiece> _takeAway = new List<PlannedPiece>();
        private readonly Cooldown _takeAwayRetry = new Cooldown(2f);
        private readonly Cooldown _doorwayRetry = new Cooldown(2f);
        /// <summary>An upgrade's doorway pieces still to take down first (ChooseDoorway); null until looked at.</summary>
        private HashSet<int> _doorway;
        private string _reportedBlocked = "";
        private int _walksLogged;
        private string _lastWalkReason = "";
        // Diagnostics for the first few hammer swings: how the builder moves and turns around a swing.
        private const int SwingsToTrace = 3;
        private int _swingsTraced;
        private float _traceUntil;
        private float _traceNext;
        private float _traceStart;
        private Vector3 _tracePos;
        private float _traceYaw;
        private readonly List<string> _trace = new List<string>();
        private ConstructionModule _site;
        private IReadOnlyList<PlannedPiece> _sitePlan;   // the plan he took the site with
        private List<int> _order;
        private BuildCursor _cursor;
        private Footprint _footprint;
        private HashSet<string> _materials = new HashSet<string>();
        private List<StorageModule> _stores = new List<StorageModule>();
        private MaterialPlan _fetch;
        private ItemDrop _drop;
        private int _piece = -1;
        private int _spot = -1;
        private float _claimTimer;
        private float _swingTimer;
        private float _standingTimer;
        private float _recheckTimer;
        /// <summary>Taking a new site: what of his load he keeps for it; the rest goes back to a chest first (null: nothing to).</summary>
        private Dictionary<string, int> _putBack;
        private int _spaceLogged = -1;   // the piece he last said he waits for space at
        private float _spaceSince;   // since he began waiting there, or since his last step (SpaceWait)
        private int _spaceSteps;   // the settlers there asked to make way, then moved out
        /// <summary>Pieces someone stood on, put aside until then (SpaceWait).</summary>
        private readonly Dictionary<int, float> _asideUntil = new Dictionary<int, float>();
        private StorageModule _putBackTo;
        private float _putBackSince;
        /// <summary>How long he tries to get his leftovers to a chest before he keeps them.</summary>
        private const float PutBackSeconds = 60f;
        private float _dropScanTimer;
        private string _reportedNeeds = "";

        public BuilderJob(Citizen citizen, CitizenAI ai, ColonyBuilding hut) : base(citizen, ai) { _hut = hut; }

        public override void Tick(float dt)
        {
            EnsureTool();
            WatchPlaced();
            FinishSwing();
            TraceSwing();
            if (_site != null && !StillOpen(_site)) LeaveSite();   // finished, taken by another, or set aside by the player
            if (_site != null && !ReferenceEquals(_site.Plan, _sitePlan))
            {
                Log.Info($"{Citizen.CitizenName}: the {_site.Building.TypeId} {_site.Building.Id} has a new plan; they take it afresh");
                LeaveSite();   // an upgrade ordered or cancelled meanwhile (review of 0.13): his work was for the old plan
            }
            MoveOnIfStuck();
            SwitchToFirst();
            if (_site == null && _mend == null && _lookAgain.Ready(Time.time))   // a new order: once the piece he mends is mended
            {
                PickSite();
                if (_site == null) _lookAgain.Start(Time.time);   // nothing to build: look again in a few seconds
            }
            if (_site == null)
            {
                if (Citizen.Carry.Total == 0 && Mend(dt)) return;   // nothing to build: he mends the colony's damaged pieces
                Idle(dt);
                return;
            }
            RenewClaim(dt);
            BuildOrderRecord order = _site.Order;
            if (order == null || order.Builder != Citizen.Id)
            {
                Citizen.Status = "$vc_status_no_work";   // waiting for the hut's owner to confirm the claim
                Ai.Idle();
                return;
            }
            NoteState(order);
            if (_putBack != null && PutBack(dt)) return;   // what this site doesn't need goes back to a chest first
            CheckStanding(order, dt);
            if (CollectFallenMaterials(dt)) return;
            if (_clear != null)
            {
                Clear(dt);
                return;
            }
            if (Prepare(order, dt)) return;   // the site is cleared and flattened first
            if (_piece < 0 && ChooseDoorway(order)) return;   // an upgrade: the old doorways come down before anything new goes up
            if (_piece < 0 && !ChoosePiece(order))
            {
                if (_takeAway.Count > 0) ChooseTakeAway(order);   // an upgrade: an old piece comes down meanwhile
                return;
            }
            if (!MaterialPlanner.CanBuild(NeedsOf(_site.Plan[_piece]), Citizen.Carry))
            {
                Fetch(PieceNeeds, dt);
                return;
            }
            _fetch = null;
            if (_takeAway.Count > 0 && OldPieceInTheWayOf(_site.Plan[_piece]))
            {
                _piece = -1;   // he has its materials now: choosing it again takes the old piece down first
                return;
            }
            Build(order, dt);
        }

        public override void Resume()
        {
            _walkStuck.Reset();
            _swingTimer = 0f;
            _fetch = null;
            _spot = -1;
            ResetSwing();
            SwingLog.EndSwing(Citizen);
        }

        public override void Stop() => LeaveSite();

        /// <summary>Night: the borrowed tool goes back and he leaves his site; in the morning he picks a site again, his own hut first.</summary>
        public override void OnNight()
        {
            PutToolAway();
            LeaveSite();
        }

        // ---- Orders ----

        private void PickSite()
        {
            LeaveSite();
            ConstructionModule chosen = ChooseOrder(null);
            if (chosen == null) return;
            _site = chosen;
            _mend = null;
            IReadOnlyList<PlannedPiece> plan = chosen.Plan;
            _sitePlan = plan;
            Vec3 centre = V(chosen.transform.position);
            _order = BuildSequence.Order(plan, centre);
            _cursor = new BuildCursor(_order);
            _footprint = Footprint.Around(SiteSurvey.OutlinePoints(plan), centre, Q(chosen.transform.rotation), FootprintMargin);
            _siteFootprint = chosen.SiteFootprint;
            SiteSurvey.Span(chosen.Plan, chosen.transform.position.y, out float reachBelow, out float topAbove);
            _designTop = chosen.transform.position.y + Mathf.Max(topAbove, 2f);
            _designBottom = chosen.transform.position.y - Mathf.Max(reachBelow, 0f);
            _takeAway = chosen.TakeAwayPlan;
            _clearTakeAway = -1;
            PlaceSpots();
            // What the old pieces he takes away drop is picked up too, even what the new design doesn't use: it goes back to his hut.
            _materials = new HashSet<string>(plan.Concat(_takeAway).Where(p => p.IsValid).SelectMany(p => NeedsOf(p).Keys));
            _claimTimer = 0f;
            _seenState = chosen.Order != null ? chosen.Order.State : BuildState.Waiting;
            Progress();   // a new site: the wait starts now
            Log.Info($"{Citizen.CitizenName} takes the build order for {chosen.Building.TypeId} {chosen.Building.Id} ({plan.Count} pieces)");
            Dictionary<string, int> stillNeeded = chosen.RemainingNeeds();
            _putBack = Leftovers.AnyToPutBack(Citizen.Carry, stillNeeded) ? Leftovers.KeepFor(Citizen.Carry, stillNeeded) : null;
        }

        /// <summary>
        /// What he carries that his new site doesn't need goes into the nearest colony chest before he starts there, where
        /// every builder looks (the real builders' run, 2026-10-18: a spare 3 Bronze rode in a pocket while the Farm waited
        /// for 1). True while he's at it; he gives up on a chest he can't get to or that is full.
        /// </summary>
        private bool PutBack(float dt)
        {
            if (Citizen.Carry.Beyond(_putBack).Total == 0)
            {
                EndPutBack();
                return false;
            }
            if (_putBackTo == null)
            {
                Vector3 me = Citizen.transform.position;
                _putBackTo = ColonyStores().Where(s => s != null).OrderBy(s => Vector3.Distance(s.transform.position, me)).FirstOrDefault();
                _putBackSince = Time.time;
                if (_putBackTo == null)
                {
                    EndPutBack();
                    return false;
                }
            }
            if (Time.time - _putBackSince > PutBackSeconds)   // can't get there: he keeps it, as before
            {
                EndPutBack();
                return false;
            }
            Citizen.Status = "$vc_status_delivering";
            if (!Reach(_putBackTo.transform)) return true;
            Ai.Idle();
            int before = Citizen.Carry.Total;
            if (_putBackTo.TryDeposit(Citizen.Carry, _putBack) == StorageModule.DepositResult.Busy) return true;
            Citizen.SaveCarry();
            ColonyBuilding where = _putBackTo.GetComponent<ColonyBuilding>();
            Log.Info($"{Citizen.CitizenName} put back {before - Citizen.Carry.Total} items their new site doesn't need in the " +
                     $"{(where != null ? where.TypeId : "colony")} storage (still carrying {Citizen.Carry.Total})");
            EndPutBack();
            return false;
        }

        private bool PutAside(int piece) => _asideUntil.TryGetValue(piece, out float until) && Time.time < until;

        private void EndPutBack()
        {
            _putBack = null;
            _putBackTo = null;
        }

        /// <summary>The spots around the building he works from, on the ground as it is now (it changes when he flattens the site).</summary>
        private void PlaceSpots()
        {
            _spots.Clear();
            _coreSpots.Clear();
            foreach (Vec3 spot in StandSpots.Ring(_footprint, RingOutside, RingSpacing))
            {
                var ground = new Vector3(spot.X, ZoneSystem.instance.GetGroundHeight(U(spot)), spot.Z);
                _spots.Add(ground);
                _coreSpots.Add(V(ground));
            }
            _badSpots.Clear();
        }

        private static bool Open(ConstructionModule site, string me, double now)
        {
            if (site == null || !site.IsValid) return false;
            BuildOrderRecord order = site.Order;
            return order != null && order.State != BuildState.Done && !order.Paused && site.Plan.Count > 0 && !order.IsClaimedByOther(me, now);
        }

        /// <summary>He may build this order: its level is within what his own Builder's Hut allows (his own hut one more).</summary>
        private bool MayBuild(ConstructionModule site)
        {
            BuildOrderRecord order = site.Order;
            return order != null && HutLevels.MayBuild(_hut != null ? _hut.Level : 1, order.TargetLevel, site.Building == _hut);
        }

        private bool StillOpen(ConstructionModule site) => site != null && site.IsValid && Open(site, Citizen.Id, ConstructionModule.Now());

        private void LeaveSite()
        {
            if (_site != null && _site.IsValid) _site.RequestRelease(Citizen.Id);
            _site = null;
            _piece = -1;
            _spot = -1;
            _fetch = null;
            _drop = null;
            _putBack = null;
            _putBackTo = null;
            _doorway = null;
            _asideUntil.Clear();
            _giveUps.Clear();   // a new site: nothing left over yet
            _chestRound.Leave();
            _roundSpots.Clear();
            _heldRocks.Clear();
            _toldHeldRock = false;
            _spaceLogged = -1;
            _watching.Clear();
            _reportedNeeds = "";
            _stuckRetry.Clear();
            _inTheWay.Clear();
            _clear = null;
            _clearTakeAway = -1;
            _takeAway = new List<PlannedPiece>();
            _reportedBlocked = "";
            _walksLogged = 0;
            _lastWalkReason = "";
            ResetPrep();
            ResetSwing();
        }

        private void RenewClaim(float dt)
        {
            _claimTimer -= dt;
            if (_claimTimer > 0f) return;
            _claimTimer = ClaimRenewSeconds;
            BuildOrderRecord order = _site.Order;
            if (order != null && !order.HasLiveClaim(ConstructionModule.Now())) ForgetReports();   // a fresh claim clears them on the order
            _site.RequestClaim(Citizen.Id);
        }

        /// <summary>He says again what the site needs and what is in the way (the order forgot it when he took it afresh).</summary>
        private void ForgetReports()
        {
            _reportedNeeds = "";
            _reportedBlocked = "";
            _prepNeeds = "";
            _prepBlocked = false;
        }

        // ---- Choosing and checking pieces ----

        /// <returns>True when a piece was chosen; otherwise the status says why he waits.</returns>
        private bool ChoosePiece(BuildOrderRecord order)
        {
            IReadOnlyList<PlannedPiece> plan = _site.Plan;
            if (_cursor.Stuck && !_stuckRetry.Ready(Time.time))
            {
                WaitOnPlayer();
                Ai.Idle();   // nothing will hold yet: look again in a few seconds instead of every tick
                return false;
            }
            int next = _cursor.Next(i => order.IsOpen(i) && !IsWatched(i) && !PutAside(i), i => CanPlace(plan[i]), ProbesPerTick);
            if (next >= 0)
            {
                if (_inTheWay.Count == 0) ReportBlocked("");
                if (next != _piece) _spaceLogged = -1;   // another piece: a later wait at one starts afresh (the whole-mod review, M7)
                _piece = next;
                _fetch = null;
                _walkStuck.Reset();
                return true;
            }
            Ai.Idle();
            if (_watching.Count > 0) Citizen.Status = BuildingStatus(order);   // waiting to see whether the last pieces hold
            else if (_cursor.Stuck && _cursor.LastTried >= 0)
            {
                WaitOnPlayer();   // something in the way, or no support
                if (_inTheWay.TryGetValue(_cursor.LastTried, out string blocker))
                {
                    Citizen.Status = "$vc_status_in_the_way " + blocker;
                    ReportBlocked(blocker);   // shown in the hut window, so the player knows what to remove
                }
                else Citizen.Status = "$vc_status_no_support " + PieceName(plan[_cursor.LastTried]);
                _stuckRetry.Start(Time.time);
            }
            return false;
        }

        private bool IsWatched(int index) => _watching.Any(w => w.Index == index);

        /// <summary>
        /// An upgrade's next old piece to take away: from the top, never one something staying rests on only, until every
        /// new piece stands (then the rest from the top). Old pieces that are already gone are ticked off.
        /// </summary>
        private void ChooseTakeAway(BuildOrderRecord order)
        {
            if (_clear != null || !_takeAwayRetry.Ready(Time.time)) return;
            _takeAwayRetry.Start(Time.time);
            List<Piece> pieces = SitePieces();
            var standing = new Dictionary<int, Piece>();
            List<KeyValuePair<int, PieceBox>> old = StandingOld(order, pieces, standing);
            if (old.Count == 0) return;
            // Taking a hut down (0.23): nothing stays, so every piece comes down from the top.
            List<PieceBox> staying = order.Kind == OrderKind.Remove ? new List<PieceBox>()
                : pieces.Where(p => p != null && _site.MatchesPlan(p)).Select(PiecePlacer.BoxOf).ToList();
            int next = RemovalOrder.Next(old, staying, OnGround, order.BuildHandled);
            if (next < 0) return;   // something new must stand first
            _clear = standing[next];
            _clearTakeAway = next;
            Log.Info($"{Citizen.CitizenName} takes down the old {Utils.GetPrefabName(_clear.gameObject)} at {_clear.transform.position}");
        }

        /// <summary>A piece of the plan standing here that rests on this old one and on nothing else that stays (RemovalOrder.HeldUpBy).</summary>
        private bool KeptOnlyOn(Piece old, out Piece kept)
        {
            List<Piece> staying = SitePieces().Where(p => p != null && p != old && _site.MatchesPlan(p)
                && !_badThings.IsBlocked(SiteThing.KeyOf(p.gameObject, null), Time.time)).ToList();   // one he couldn't get to waits
            List<int> held = RemovalOrder.HeldUpBy(PiecePlacer.BoxOf(old), staying.Select(PiecePlacer.BoxOf).ToList(), OnGround);
            kept = held.Count > 0 ? staying[held[0]] : null;
            return kept != null;
        }

        /// <summary>The upgrade's old pieces still standing, with their boxes; old pieces already gone are ticked off.</summary>
        private List<KeyValuePair<int, PieceBox>> StandingOld(BuildOrderRecord order, List<Piece> pieces, Dictionary<int, Piece> standing)
        {
            var old = new List<KeyValuePair<int, PieceBox>>();
            for (int i = 0; i < _takeAway.Count; i++)
            {
                if (order.TakenAway.Contains(i)) continue;
                Piece piece = PiecePlacer.FindStanding(_takeAway[i], pieces);
                if (piece == null)
                {
                    _site.ReportTaken(i);   // already gone (fell, or a player took it)
                    continue;
                }
                if (_badThings.IsBlocked(SiteThing.KeyOf(piece.gameObject, null), Time.time)) continue;
                standing[i] = piece;
                old.Add(new KeyValuePair<int, PieceBox>(i, PiecePlacer.BoxOf(piece)));
            }
            return old;
        }

        /// <summary>
        /// An upgrade's doorways come first (the old-huts upgrade test, 2026-10-19: an old door led into the new design's
        /// wall 129 times, and the builder was walled in before the new door's spot was cleared): its old doors and gates,
        /// and the old pieces standing where its new doors go, come down before anything new goes up (RemovalOrder.NextDoorway).
        /// True when he has one to take down now.
        /// </summary>
        private bool ChooseDoorway(BuildOrderRecord order)
        {
            if (_clear != null || order.Kind != OrderKind.Upgrade || _takeAway.Count == 0 || !_doorwayRetry.Ready(Time.time)) return false;
            _doorwayRetry.Start(Time.time);
            List<Piece> pieces = SitePieces();
            var standing = new Dictionary<int, Piece>();
            List<KeyValuePair<int, PieceBox>> old = StandingOld(order, pieces, standing);
            if (_doorway == null) _doorway = DoorwayPieces(old, standing);
            _doorway.RemoveWhere(key => !standing.ContainsKey(key));   // down already
            if (_doorway.Count == 0) return false;
            List<PieceBox> staying = pieces.Where(p => p != null && _site.MatchesPlan(p)).Select(PiecePlacer.BoxOf).ToList();
            int next = RemovalOrder.NextDoorway(old, _doorway, staying, OnGround);
            if (next < 0) return false;
            _clear = standing[next];
            _clearTakeAway = next;
            Log.Info($"{Citizen.CitizenName} takes down the old {Utils.GetPrefabName(_clear.gameObject)} at {_clear.transform.position} first: the doorways come first");
            return true;
        }

        /// <summary>The take-away keys of the old doors and gates, and of the old pieces standing in the new doors' doorways.</summary>
        private HashSet<int> DoorwayPieces(List<KeyValuePair<int, PieceBox>> old, Dictionary<int, Piece> standing)
        {
            List<PieceBox> newDoors = _site.Plan.Where(p => p.IsValid && PiecePlacer.Prefab(p.Prefab)?.GetComponent<Door>() != null)
                .Select(PiecePlacer.PlannedBox).ToList();
            var keys = new HashSet<int>();
            foreach (KeyValuePair<int, PieceBox> piece in old)
                if (standing[piece.Key].GetComponent<Door>() != null || RemovalOrder.InDoorway(piece.Value, newDoors)) keys.Add(piece.Key);
            return keys;
        }

        private bool OldPieceInTheWayOf(PlannedPiece piece)
        {
            GameObject prefab = PiecePlacer.Prefab(piece.Prefab);
            return prefab != null && PiecePlacer.OldPieceInTheWay(prefab, U(piece.Position), U(piece.Rotation), _site.Building, _takeAway, out _) != null;
        }

        private static bool OnGround(PieceBox box) =>
            box.Min.Y <= SiteSurvey.GroundAt((box.Min.X + box.Max.X) / 2f, (box.Min.Z + box.Max.Z) / 2f) + 0.3f;

        private bool CanPlace(PlannedPiece piece)
        {
            GameObject prefab = PiecePlacer.Prefab(piece.Prefab);
            if (prefab == null)
            {
                _site.ReportPiece(piece.Index, ConstructionModule.PieceFailed);   // a piece the game doesn't know: skipped after three tries
                return false;
            }
            if (PiecePlacer.FindStanding(piece, SitePieces()) != null)
            {
                _site.ReportPiece(piece.Index, ConstructionModule.PieceDone);   // already standing (a player built it, or before a reload)
                return false;
            }
            PiecePlacer.TopAndGround(prefab, U(piece.Position), U(piece.Rotation), out float top, out float ground);
            if (BuildStance.Buried(top, ground))
            {
                _site.ReportPiece(piece.Index, ConstructionModule.PieceUnderground);   // the game would break it at once
                return false;
            }
            if (_takeAway.Count > 0)
            {
                Piece old = PiecePlacer.OldPieceInTheWay(prefab, U(piece.Position), U(piece.Rotation), _site.Building, _takeAway, out int oldIndex);
                if (old != null)
                {
                    // Nothing old comes down before he has what the new piece takes: he fetches it first (or waits for
                    // it with the hut untouched, so the upgrade can still be cancelled).
                    if (!MaterialPlanner.CanBuild(NeedsOf(piece), Citizen.Carry)) return true;
                    if (_clear == null)
                    {
                        // Old pieces resting on it come down first, from the top, so nothing old falls.
                        var standing = new Dictionary<int, Piece>();
                        int first = RemovalOrder.TopOf(StandingOld(_site.Order, SitePieces(), standing), oldIndex);
                        if (first != oldIndex && standing.TryGetValue(first, out Piece above))
                        {
                            _clear = above;
                            _clearTakeAway = first;
                            Log.Info($"{Citizen.CitizenName} takes down the old {Utils.GetPrefabName(above.gameObject)} at {above.transform.position} first: it rests on the old {Utils.GetPrefabName(old.gameObject)} in the way of {piece.Prefab} #{piece.Index}");
                        }
                        else if (KeptOnlyOn(old, out Piece kept))
                        {
                            // A kept piece resting on it alone comes down first, with the hammer, not falling; it goes back on
                            // the list and up again on the new one (A3: a gable's ornament fell with its ridge).
                            _clear = kept;
                            _clearTakeAway = -1;
                            Log.Info($"{Citizen.CitizenName} takes down {Utils.GetPrefabName(kept.gameObject)} at {kept.transform.position} first: it rests " +
                                     $"on the old {Utils.GetPrefabName(old.gameObject)} alone, in the way of {piece.Prefab} #{piece.Index}; it goes up again after");
                        }
                        else
                        {
                            _clear = old;
                            _clearTakeAway = oldIndex;
                            Log.Info($"{Citizen.CitizenName} takes down the old {Utils.GetPrefabName(old.gameObject)} at {old.transform.position} to put up {piece.Prefab} #{piece.Index}");
                        }
                    }
                    return false;
                }
            }
            Piece blocker = PiecePlacer.InTheWay(prefab, U(piece.Position), U(piece.Rotation), _site.Building, _site.MatchesPlan);
            if (blocker != null)
            {
                if (!_inTheWay.ContainsKey(piece.Index))
                    Log.Info($"{Citizen.CitizenName}: {piece.Prefab} #{piece.Index} of {_site.Building.TypeId} would go into {Utils.GetPrefabName(blocker.gameObject)} at {blocker.transform.position}; waiting");
                _inTheWay[piece.Index] = blocker.m_name;
                string blockerSite = PiecePlacer.SiteOf(blocker.gameObject);
                if (_clear == null && SiteRules.IsLeftover(blockerSite, _site.Building.Id, ColonyBuilding.FindById(blockerSite) != null)
                    && !_badThings.IsBlocked(SiteThing.KeyOf(blocker.gameObject, null), Time.time))
                {
                    _clear = blocker;
                    Log.Info($"{Citizen.CitizenName} takes down {Utils.GetPrefabName(blocker.gameObject)} at {blocker.transform.position}, a leftover of an old colony building, to make room");
                }
                return false;
            }
            if (PiecePlacer.IntoAny(prefab, U(piece.Position), U(piece.Rotation), _heldRocks))
            {
                // A rock another building rests on: left whole, so nothing goes up into it (the whole-mod review, I1).
                if (!_inTheWay.ContainsKey(piece.Index))
                    Log.Info($"{Citizen.CitizenName}: {piece.Prefab} #{piece.Index} of {_site.Building.TypeId} would go into a rock another building rests on; waiting");
                _inTheWay[piece.Index] = "$vc_thing_rock";
                return false;
            }
            _inTheWay.Remove(piece.Index);
            return SupportProbe.WouldHold(prefab, U(piece.Position), U(piece.Rotation), out _);
        }

        /// <summary>Done pieces that disappeared (knocked down) go back on the list.</summary>
        private void CheckStanding(BuildOrderRecord order, float dt)
        {
            _standingTimer -= dt;
            if (_standingTimer > 0f) return;
            _standingTimer = StandingCheckSeconds;
            if (!ZNetScene.instance.IsAreaReady(_site.transform.position)) return;
            IReadOnlyList<PlannedPiece> plan = _site.Plan;
            foreach (int index in order.Done)
            {
                if (index >= plan.Count || IsWatched(index) || PiecePlacer.FindStanding(plan[index], SitePieces()) != null) continue;
                Log.Info($"{Citizen.CitizenName}: {plan[index].Prefab} #{index} of {_site.Building.TypeId} is gone; it goes back on the list");
                _site.ReportPiece(index, ConstructionModule.PieceGone);
            }
        }

        // ---- Materials ----

        private Dictionary<string, int> NeedsOf(PlannedPiece piece) => PiecePlacer.Needs(piece.Prefab);

        private float _fetchPauseUntil;

        /// <param name="upcoming">What the next jobs need, the one at hand first (pieces, or stone for the ground).</param>
        private void Fetch(System.Func<List<IReadOnlyDictionary<string, int>>> upcoming, float dt)
        {
            if (Time.time < _fetchPauseUntil)   // took none of it last time: a moment before trying again (1.4.3)
            {
                Ai.Idle();
                return;
            }
            if (_fetch == null) PlanFetch(upcoming());
            if (_fetch.Missing.Count > 0)
            {
                WaitOnPlayer();   // materials or stone missing
                string needs = DescribeItems(_fetch.Missing);
                // Where more is kept, on their words only: the order's needs stay as they are (Hand over reads them).
                Citizen.Status = "$vc_status_needs " + needs + KeptNote(needs, _fetch.Missing, dt);
                ReportNeeds(needs);
                Tell(needs);
                Ai.Idle();
                _recheckTimer -= dt;
                if (_recheckTimer > 0f) return;
                _recheckTimer = RecheckSeconds;
                _fetch = null;   // look again: something may have been put in a chest
                return;
            }
            ReportNeeds("");
            if (_fetch.Steps.Count == 0)
            {
                _fetch = null;
                return;
            }
            Citizen.Status = "$vc_status_fetching";
            FetchStep step = _fetch.Steps[0];
            StorageModule store = step.Store < _stores.Count ? _stores[step.Store] : null;
            if (store == null)
            {
                _fetch = null;
                return;
            }
            if (!Reach(store.transform)) return;
            Ai.Idle();
            if (store.Claim() != StorageModule.Access.Ready) return;   // its owner is handing it over, or a player has it open: he waits
            int taken = 0;
            while (taken < step.Amount && store.TryTakeItem(step.Item, Citizen.Carry, ownUse: store == OwnStore, toBuildWith: true)) taken++;
            if (taken > 0)
            {
                Citizen.SaveCarry();
                Progress();
            }
            ColonyBuilding from = store.GetComponent<ColonyBuilding>();
            Log.Info($"{Citizen.CitizenName} fetched {taken} of {step.Amount} {step.Item} from the {(from != null ? from.TypeId : "?")} storage (now carrying {Citizen.Carry.Total})");
            _fetch.Steps.RemoveAt(0);
            if (taken < step.Amount) _fetch = null;   // taken by someone else meanwhile: plan again
            _fetchPauseUntil = Time.time + StockRules.FetchPause(taken);
        }

        /// <summary>The piece at hand's needs, then the next pieces'.</summary>
        private List<IReadOnlyDictionary<string, int>> PieceNeeds()
        {
            BuildOrderRecord order = _site.Order;
            IReadOnlyList<PlannedPiece> plan = _site.Plan;
            var upcoming = new List<IReadOnlyDictionary<string, int>> { NeedsOf(plan[_piece]) };
            foreach (int index in _order.Where(i => i != _piece && order.IsOpen(i) && !IsWatched(i)).Take(UpcomingPieces))
                upcoming.Add(NeedsOf(plan[index]));
            return upcoming;
        }

        private void PlanFetch(List<IReadOnlyDictionary<string, int>> upcoming)
        {
            _stores = ColonyStores();
            // His own hut's chest is his to use; another building's kept items are left (0.21).
            var stocks = _stores.Select(s => (IReadOnlyDictionary<string, int>)(s == OwnStore ? s.Contents() : s.SpareContents())).ToList();
            _fetch = MaterialPlanner.Plan(upcoming, Citizen.Carry, ColonyConfig.BuilderCarryCapacity.Value, stocks);
        }

        private StorageModule OwnStore => _hut != null ? _hut.Storage : null;

        /// <summary>His hut's storage, then the Town Hall's, then the colony's other huts, nearest first.</summary>
        private List<StorageModule> ColonyStores() => BuilderTools.ColonyStores(Citizen.ColonyId, _hut, Citizen.transform.position);

        /// <summary>Picks up building materials lying at the site — a fallen piece drops what it was made of. True while busy.</summary>
        private bool CollectFallenMaterials(float dt)
        {
            if (_drop == null)
            {
                _dropScanTimer -= dt;
                if (_dropScanTimer > 0f) return false;
                _dropScanTimer = DropScanSeconds;
                _drop = FindDrop();
                if (_drop == null) return false;
            }
            Citizen.Status = "$vc_status_fetching";
            if (PickUp(_drop, out _)) _drop = null;
            return true;
        }

        /// <summary>
        /// Walks to a dropped item and picks it up. True once it's in his load or gone, or when he can't get to it
        /// (<paramref name="gaveUp"/>; it's left alone for 5 minutes); false while he's still busy with it.
        /// </summary>
        private bool PickUp(ItemDrop drop, out bool gaveUp)
        {
            gaveUp = false;
            if (drop == null) return true;
            Vector3 position = drop.transform.position;
            if (!Ai.GoTo(position, 1.2f))
            {
                if (!_walkStuck.Update(Vector3.Distance(Citizen.transform.position, position), Time.time)) return false;
                _badDrops.Add(IdOf(drop.gameObject), Time.time);
                _walkStuck.Reset();
                gaveUp = true;
                return true;
            }
            _walkStuck.Reset();
            ZNetView nview = drop.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid()) return true;
            if (!nview.IsOwner())
            {
                drop.RequestOwn();   // try again next tick once we own it
                return false;
            }
            drop.Load();   // the previous owner may have merged nearby stacks into this one; read the current size
            Citizen.Carry.Add(Utils.GetPrefabName(drop.gameObject), drop.m_itemData.m_stack);
            Citizen.SaveCarry();
            Log.Info($"{Citizen.CitizenName} picked up {drop.m_itemData.m_stack} {Utils.GetPrefabName(drop.gameObject)} (now carrying {Citizen.Carry.Total})");
            ZNetScene.instance.Destroy(drop.gameObject);
            Progress();
            return true;
        }

        private ItemDrop FindDrop()
        {
            Vector3 centre = U(_footprint.Centre);
            float range = Mathf.Max(_footprint.HalfX, _footprint.HalfZ) * 1.5f + DropRadius;
            float floor = _site.transform.position.y;
            foreach (ItemDrop drop in WorldScan.Drops())
            {
                if (drop == null || drop.IsPiece() || Vector3.Distance(drop.transform.position, centre) > range) continue;
                if (!DropRules.WithinReach(drop.transform.position.y, floor)) continue;   // on a roof: left where it lies
                if (!_materials.Contains(Utils.GetPrefabName(drop.gameObject)) || _badDrops.IsBlocked(IdOf(drop.gameObject), Time.time)) continue;
                if (!_falls.Near(V(drop.transform.position), 3f, Time.time)) continue;   // not from his pieces (e.g. the lumberjack's wood)
                return drop;
            }
            return null;
        }

        /// <summary>
        /// Walks to a chest; takes from close by when he can't get right up to it (as the lumberjack delivers), but not through
        /// a wall: within reach with a wall between, he goes round to a side of it with nothing between first (ChestApproach;
        /// the old huts' upgrade test: a builder took from the Builder's Hut's chest through its back wall).
        /// </summary>
        private bool Reach(Transform chest)
        {
            string key = chest.GetInstanceID().ToString();
            bool goingRound = _chestRound.GoingRound(key) && _roundSpots.ContainsKey(key);
            Vector3 position = goingRound ? _roundSpots[key] : chest.position;
            if (Ai.GoTo(position, goingRound ? MoveRules.MinStopDistance : ChestReach))
            {
                bool wall = !goingRound && Walls.StructureBetween(Citizen.transform.position, chest.position);
                if (_chestRound.AtReach(key, wall) == ChestStep.GoRound)
                {
                    if (OpenSideOf(chest, out Vector3 spot))
                    {
                        Log.Info($"{Citizen.CitizenName} is by the chest at {chest.position} with a wall between; goes round to {spot}");
                        _chestRound.WentRound(key);
                        _roundSpots[key] = spot;
                        _walkStuck.Reset();
                        return false;
                    }
                    _chestRound.Failed(key);   // no open side: from where he is, as before, and not looked for again
                }
                _walkStuck.Reset();
                return true;
            }
            if (Ai.UsingDoor)
            {
                _toldFromHere = false;
                _walkStuck.Reset();   // going round to a door is no lack of progress
                return false;
            }
            float distance = Utils.DistanceXZ(Citizen.transform.position, position);
            bool stuck = _walkStuck.Update(distance, Time.time);
            if (stuck && goingRound)
            {
                Log.Info($"{Citizen.CitizenName} can't get round to the chest at {chest.position}; takes from where they can");
                _chestRound.Failed(key);   // its open side is out of his way: back to the chest itself, and the usual ways from there
                _walkStuck.Reset();
                return false;
            }
            if (MoveRules.DeliverFromHere(stuck, distance))
            {
                if (!_toldFromHere) Log.Info($"{Citizen.CitizenName} can't get closer to the chest and reaches it from {distance:0.0} m away");
                _toldFromHere = true;
                return true;
            }
            _toldFromHere = false;
            if (stuck) Citizen.Status = "$vc_status_cant_reach_hut";
            return false;
        }

        // ---- Building ----

        private void Build(BuildOrderRecord order, float dt)
        {
            PlannedPiece piece = _site.Plan[_piece];
            Vector3 target = U(piece.Position);
            Citizen.Status = BuildingStatus(order);
            // He works close to the piece (within his work distance, never beyond his reach), from just outside the
            // building; he keeps his spot while it is close enough, so he doesn't step around after every piece.
            float workDistance = BuildStance.WorkDistance(ColonyConfig.BuilderReach.Value);
            _spot = StandSpots.Choose(_coreSpots, piece.Position, workDistance, V(Citizen.transform.position),
                i => !_badSpots.Contains(i), _spot);
            if (_spot < 0)   // nothing that close: any spot within his reach
                _spot = StandSpots.Choose(_coreSpots, piece.Position, ColonyConfig.BuilderReach.Value, V(Citizen.transform.position),
                    i => !_badSpots.Contains(i));
            if (_spot < 0)
            {
                WaitOnPlayer();
                Citizen.Status = "$vc_status_cant_reach_site";
                Ai.Idle();
                _badSpots.Clear();   // try every spot again next time
                _piece = -1;
                return;
            }
            GameObject prefab = PiecePlacer.Prefab(piece.Prefab);
            if (prefab == null)
            {
                _piece = -1;
                return;
            }
            Vector3 me = Citizen.transform.position;
            bool swinging = Citizen.Body.InAttack();
            // Work from where he stands while he can reach the piece: each swing slides him a little, and walking back
            // to his spot after every swing made him step and twitch. He only walks when out of reach or in the way.
            bool inside = _footprint.Contains(V(me));
            bool covers = PiecePlacer.Covers(prefab, target, U(piece.Rotation), Citizen.Body);
            bool close = BuildStance.InPlace(V(me), piece.Position, BuildStance.WorkDistance(ColonyConfig.BuilderReach.Value), inside);
            bool inPlace = close && !covers;
            if (!inPlace)
            {
                if (swinging) return;   // let the swing finish before walking
                LogWalk(inside ? "standing inside the building" : covers ? "standing where the piece goes" : "too far from the piece", piece);
                Vector3 stand = _spots[_spot];
                if (!Ai.GoTo(stand, MoveRules.MinStopDistance))
                {
                    if (_walkStuck.Update(Utils.DistanceXZ(me, stand), Time.time))
                    {
                        _badSpots.Add(_spot);
                        _spot = -1;
                        _walkStuck.Reset();
                    }
                    return;
                }
            }
            _walkStuck.Reset();
            Ai.Idle();
            Vector3 aim = new Vector3(target.x, me.y, target.z);   // face it from the ground, however high it is
            Ai.Face(aim);
            if (PiecePlacer.Occupied(prefab, target, U(piece.Rotation), Citizen.Body))
            {
                Citizen.Status = "$vc_status_waiting_space";
                if (_spaceLogged != _piece)   // once a piece: a stall with no word in the log (the camp's level 5, 2026-10-18)
                {
                    _spaceLogged = _piece;
                    _spaceSince = Time.time;
                    _spaceSteps = 0;
                    Log.Info($"{Citizen.CitizenName} waits for {PiecePlacer.Occupants(prefab, target, U(piece.Rotation), Citizen.Body)} " +
                             $"to move out of where {piece.Prefab} #{_piece} goes");
                    return;
                }
                List<CitizenAI> settlers = PiecePlacer.SettlersAt(prefab, target, U(piece.Rotation), Citizen.Body);
                SpaceStep step = SpaceWait.Next(Time.time - _spaceSince, _spaceSteps, settlers.Count > 0);
                if (step == SpaceStep.AskToMove || step == SpaceStep.MoveOut)   // a settler there: asked to make way, then moved out
                {
                    foreach (CitizenAI settler in settlers) settler.MakeWay(_site, target, step == SpaceStep.MoveOut, Citizen.CitizenName);
                    _spaceSteps++;
                    _spaceSince = Time.time;
                }
                else if (step == SpaceStep.PutAside)   // they stay: other pieces first, this one later
                {
                    _asideUntil[_piece] = Time.time + SpaceWait.AsideSeconds;
                    Log.Info($"{Citizen.CitizenName} puts {piece.Prefab} #{_piece} aside while someone stands there and works on other pieces");
                    _spaceLogged = -1;
                    _piece = -1;
                }
                return;
            }
            _swingTimer -= dt;
            if (_swingTimer > 0f || Citizen.Body.InAttack() || !Ai.IsFacing(aim)) return;
            _swingTimer = ColonyConfig.SecondsPerPiece.Value / Citizen.SkillSpeed(WorkSkill.Crafting);   // slower when hungry, faster with Crafting (0.22)
            int index = _piece;
            StartSwing(() =>
            {
                if (PlaceNow(index)) Citizen.Practise(WorkSkill.Crafting);   // only for a piece that went up (0.26, review of 0.22)
            });
        }

        /// <summary>Swings the hammer; <paramref name="onLands"/> happens the moment it lands.</summary>
        private void StartSwing(System.Action onLands)
        {
            _onSwingLands = onLands;
            _swingPlaced = false;
            _swingStarted = Time.time;
            StartTrace();
            if (Citizen.Body.StartAttack(null, false)) SwingLog.BeginSwing(Citizen, LandSwing);
            else LandSwing();   // no hammer swing for settlers: it happens without the animation
        }

        private void LandSwing()
        {
            SwingLog.EndSwing(Citizen);
            System.Action onLands = _onSwingLands;
            ResetSwing();
            onLands?.Invoke();
        }

        /// <summary>Takes down a leftover of an old colony building that stands where his building goes.</summary>
        private void Clear(float dt)
        {
            PutToolAway();   // the hammer does this
            Piece target = _clear;
            BuildOrderRecord siteOrder = _site != null && _site.IsValid ? _site.Order : null;
            Citizen.Status = _clearTakeAway >= 0 && siteOrder != null && siteOrder.Kind == OrderKind.Remove ? BuildingStatus(siteOrder)   // review of 0.23
                : _clearTakeAway >= 0 ? "$vc_status_taking_down" : "$vc_status_clearing";
            Vector3 at = target.transform.position;
            if (!ReachFromSpot(at, out bool unreachable))
            {
                if (!unreachable) return;
                _badThings.Add(SiteThing.KeyOf(target.gameObject, null), Time.time);   // can't get near it: it stays in the way for now
                Log.Info($"{Citizen.CitizenName} can't get near {Utils.GetPrefabName(target.gameObject)} at {at}; it stays in the way for now");
                _clear = null;
                _clearTakeAway = -1;
                return;
            }
            int takeAway = _clearTakeAway;
            SwingWhenReady(at, ColonyConfig.SecondsPerPiece.Value / Citizen.SkillSpeed(WorkSkill.Crafting), dt, () =>
            {
                if (TakeDown(target, takeAway)) Citizen.Practise(WorkSkill.Crafting);
            });
        }

        /// <summary>
        /// Gets within work distance of a point from the spots around the site (as when building). True when he can work
        /// from where he stands; <paramref name="unreachable"/> when no spot reaches it.
        /// </summary>
        private bool ReachFromSpot(Vector3 at, out bool unreachable)
        {
            unreachable = false;
            Vector3 me = Citizen.transform.position;
            float work = BuildStance.WorkDistance(ColonyConfig.BuilderReach.Value);
            if (Vector3.Distance(me + Vector3.up, at) <= work) return true;
            if (Citizen.Body.InAttack()) return false;   // let the swing finish before walking
            int spot = StandSpots.Choose(_coreSpots, V(at), work, V(me), i => !_badSpots.Contains(i));
            if (spot < 0) spot = StandSpots.Choose(_coreSpots, V(at), ColonyConfig.BuilderReach.Value, V(me), i => !_badSpots.Contains(i));
            if (spot < 0)
            {
                unreachable = true;
                return false;
            }
            if (Ai.GoTo(_spots[spot], MoveRules.MinStopDistance))
            {
                _walkStuck.Reset();
                return true;
            }
            if (_walkStuck.Update(Utils.DistanceXZ(me, _spots[spot]), Time.time))
            {
                _badSpots.Add(spot);   // he can't get there: another spot next time
                _walkStuck.Reset();
            }
            return false;
        }

        /// <summary>Faces the point and swings when the last swing is done and the pause after it is over.</summary>
        private void SwingWhenReady(Vector3 at, float pause, float dt, System.Action onLands)
        {
            Ai.Idle();
            Vector3 me = Citizen.transform.position;
            var aim = new Vector3(at.x, me.y, at.z);
            Ai.Face(aim);
            _swingTimer -= dt;
            if (_swingTimer > 0f || _onSwingLands != null || Citizen.Body.InAttack() || !Ai.IsFacing(aim)) return;
            _swingTimer = pause;
            StartSwing(onLands);
        }

        /// <param name="takeAway">The upgrade's index of this old piece (reported as taken), or -1 for a leftover.</param>
        /// <returns>Whether a piece came down (the builder practises for that, 0.26).</returns>
        private bool TakeDown(Piece target, int takeAway = -1)
        {
            if (_clear == target)
            {
                _clear = null;
                _clearTakeAway = -1;
            }
            if (target != null && takeAway >= 0 && PiecePlacer.IsPlayersBed(target))
            {
                Log.Info($"{Citizen.CitizenName} leaves the player's bed at {target.transform.position} standing (a spawn point)");
                target = null;   // counted as taken below; in the way of a new piece, it is reported as in the way
            }
            if (target != null)
            {
                Vector3 at = target.transform.position;
                _falls.Add(V(at), Time.time);   // its materials drop here: his to pick up
                Log.Info($"{Citizen.CitizenName} took down {Utils.GetPrefabName(target.gameObject)} at {at}");
                Progress();
                ZNetView nview = target.GetComponent<ZNetView>();
                if (nview != null && nview.IsValid() && !nview.IsOwner()) nview.ClaimOwnership();
                WearNTear wear = target.GetComponent<WearNTear>();
                if (wear != null) wear.Remove();
                else ZNetScene.instance.Destroy(target.gameObject);
                _cursor?.Restart();
            }
            // Reported after it is down: the last piece of a take-down finishes the hut, and its materials must lie there first (review of 0.24).
            if (takeAway >= 0 && _site != null && _site.IsValid) _site.ReportTaken(takeAway);
            return target != null;
        }

        private void ReportBlocked(string what)
        {
            if (what == _reportedBlocked || _site == null) return;
            _reportedBlocked = what;
            _site.ReportBlocked(what);
        }

        /// <summary>The moment the hammer lands: the piece appears and the materials are used. True when it went up.</summary>
        private bool PlaceNow(int index)
        {
            if (_site == null || !_site.IsValid || index != _piece) return false;
            PlannedPiece piece = _site.Plan[index];
            GameObject prefab = PiecePlacer.Prefab(piece.Prefab);
            Dictionary<string, int> needs = NeedsOf(piece);
            _piece = -1;
            _cursor.Restart();
            if (prefab == null || !MaterialPlanner.CanBuild(needs, Citizen.Carry) || PiecePlacer.FindStanding(piece, SitePieces()) != null) return false;
            Vector3 position = U(piece.Position);
            Quaternion rotation = U(piece.Rotation);
            if (PiecePlacer.InTheWay(prefab, position, rotation, _site.Building, _site.MatchesPlan) != null) return false;   // built into meanwhile
            SupportProbe.WouldHold(prefab, position, rotation, out float support);
            GameObject placed = PiecePlacer.Place(prefab, position, rotation, _site.Building.Id, _site.ColonyOwner());
            foreach (KeyValuePair<string, int> need in needs) Citizen.Carry.Remove(need.Key, need.Value);
            Citizen.SaveCarry();
            _watching.Add(new Watched { Index = index, Piece = placed, Until = Time.time + WatchSeconds, Support = support, PlacedAt = Time.time, GameSupport = float.NaN });
            if (_spaceLogged == index) _spaceLogged = -1;   // it went up: a later wait at it (re-placed after falling) starts afresh (A8)
            Progress();
            Record(piece, BuildOutcome.Placed, support);
            return true;
        }

        /// <summary>If the hammer swing never reported landing, the piece goes up as the swing ends.</summary>
        private void FinishSwing()
        {
            bool inAttack = Citizen.Body.InAttack();
            if (_onSwingLands != null && BuildSwing.PlaceAfterSwing(_wasInAttack, inAttack, _swingPlaced, Time.time - _swingStarted))
            {
                if (!_toldNoLanding) Log.Info($"{Citizen.CitizenName}'s hammer swing didn't report landing; it takes effect as the swing ends.");
                _toldNoLanding = true;
                LandSwing();
            }
            _wasInAttack = inAttack;
        }

        /// <summary>Starts recording how the builder moves and turns around one of his first hammer swings.</summary>
        private void StartTrace()
        {
            if (_swingsTraced >= SwingsToTrace) return;
            if (_swingsTraced == 0)
            {
                ItemDrop.ItemData hammer = Citizen.Body.GetInventory().GetAllItems().FirstOrDefault(IsHammer);
                Attack attack = hammer?.m_shared.m_attack;
                if (attack != null)
                    Log.Info($"{Citizen.CitizenName}'s hammer swing: type {attack.m_attackType}, animation '{attack.m_attackAnimation}', range {attack.m_attackRange}, " +
                             $"recoil {attack.m_recoilPushback}, chain {attack.m_attackChainLevels}, speed factor {attack.m_speedFactor}/{attack.m_speedFactorRotation}");
            }
            _swingsTraced++;
            _trace.Clear();
            _traceStart = Time.time;
            _traceUntil = Time.time + 3f;
            _traceNext = 0f;
            _tracePos = Citizen.transform.position;
            _traceYaw = Citizen.transform.eulerAngles.y;
        }

        private void TraceSwing()
        {
            if (_traceUntil <= 0f) return;
            if (Time.time >= _traceUntil)
            {
                Log.Info($"{Citizen.CitizenName} swing {_swingsTraced} motion: " + string.Join("; ", _trace));
                _traceUntil = 0f;
                return;
            }
            if (Time.time < _traceNext) return;
            _traceNext = Time.time + 0.1f;
            Vector3 moved = Citizen.transform.position - _tracePos;
            float turned = Mathf.DeltaAngle(_traceYaw, Citizen.transform.eulerAngles.y);
            Rigidbody body = Citizen.GetComponent<Rigidbody>();
            float speed = body != null ? body.linearVelocity.magnitude : 0f;
            _trace.Add($"{Time.time - _traceStart:0.0}s {(Citizen.Body.InAttack() ? "swing" : "-")} moved {moved.x:0.00},{moved.z:0.00} turned {turned:0} speed {speed:0.0}");
        }

        /// <summary>Why he walks, for the first walks of each order (so stepping back and forth can be read from the log).</summary>
        private void LogWalk(string reason, PlannedPiece piece)
        {
            if (reason == _lastWalkReason && _walksLogged > 0) return;
            _lastWalkReason = reason;
            if (_walksLogged >= 20) return;
            _walksLogged++;
            Log.Info($"{Citizen.CitizenName} walks to a spot: {reason} ({piece.Prefab} #{piece.Index}, {Vector3.Distance(Citizen.transform.position, U(piece.Position)):0.0} m away)");
        }

        private void ResetSwing()
        {
            _onSwingLands = null;
            _swingPlaced = true;
        }

        /// <summary>
        /// The pieces standing at the site, gathered once per frame: finding pieces by radius walks every loaded piece
        /// in the world, so the builder does it once and checks every planned piece against that.
        /// </summary>
        private List<Piece> SitePieces()
        {
            if (_sitePiecesFrame == Time.frameCount) return _sitePieces;
            _sitePiecesFrame = Time.frameCount;
            _sitePieces.Clear();
            float radius = Mathf.Sqrt(_footprint.HalfX * _footprint.HalfX + _footprint.HalfZ * _footprint.HalfZ) + 12f;   // + room for tall buildings
            Piece.GetAllPiecesInRadius(U(_footprint.Centre), radius, _sitePieces);
            return _sitePieces;
        }

        /// <summary>A new piece counts as done once it has stood for a few seconds; one that fell is tried again later.</summary>
        private void WatchPlaced()
        {
            for (int i = _watching.Count - 1; i >= 0; i--)
            {
                Watched watched = _watching[i];
                bool fell = watched.Piece == null;
                if (!fell && float.IsNaN(watched.GameSupport) && Time.time >= watched.PlacedAt + 1f)
                {
                    WearNTear wear = watched.Piece.GetComponent<WearNTear>();
                    watched.GameSupport = wear != null ? SupportProbe.SupportOf(wear) : -1f;
                    _watching[i] = watched;
                }
                if (!fell && Time.time < watched.Until) continue;
                _watching.RemoveAt(i);
                if (_site == null || !_site.IsValid) continue;
                _site.ReportPiece(watched.Index, fell ? ConstructionModule.PieceFailed : ConstructionModule.PieceDone);
                if (!fell) continue;
                PlannedPiece fallen = _site.Plan[watched.Index];
                _falls.Add(fallen.Position, Time.time);   // its materials drop here: his to pick up
                GameObject fallenPrefab = PiecePlacer.Prefab(fallen.Prefab);
                float top = 0f, ground = 0f;
                if (fallenPrefab != null) PiecePlacer.TopAndGround(fallenPrefab, U(fallen.Position), U(fallen.Rotation), out top, out ground);
                Log.Info($"{Citizen.CitizenName}: {fallen.Prefab} #{fallen.Index} fell {Time.time - watched.PlacedAt:0.0} s after placing " +
                         $"(expected support {watched.Support:0}, game said {(float.IsNaN(watched.GameSupport) ? "-" : watched.GameSupport.ToString("0"))} after 1 s; " +
                         $"its top {top - ground:+0.00;-0.00} m from the ground)");
                Record(fallen, BuildOutcome.Fell, watched.Support);
                _cursor?.Restart();
            }
        }

        // ---- Idle, words and the log ----

        private void Idle(float dt)
        {
            Citizen.Status = "$vc_status_no_work";
            if (_hut == null)
            {
                Ai.Idle();
                return;
            }
            if (Citizen.Carry.Total == 0)
            {
                Ai.FreeTime(_hut.transform.position);   // nothing to build or mend: free time by his hut (0.14)
                return;
            }
            Citizen.Status = "$vc_status_delivering";   // leftovers go back to his hut
            if (!Reach(_hut.transform)) return;
            Ai.Idle();
            _recheckTimer -= dt;
            if (_recheckTimer > 0f) return;
            _recheckTimer = RecheckSeconds;
            int before = Citizen.Carry.Total;
            if (_hut.Storage.TryDeposit(Citizen.Carry) == StorageModule.DepositResult.Busy) return;
            Citizen.SaveCarry();
            if (Citizen.Carry.Total != before)
                Log.Info($"{Citizen.CitizenName} put {before - Citizen.Carry.Total} leftover items in their hut (still carrying {Citizen.Carry.Total})");
        }

        private string BuildingStatus(BuildOrderRecord order) => order.Kind == OrderKind.Remove
            ? $"$vc_status_taking_down_hut {_site.Building.Type?.NameToken} ({order.TakeAwayLeft} $vc_pieces_left)"   // 0.23
            : $"{(order.Kind == OrderKind.Repair ? "$vc_status_repairing" : "$vc_status_building")} {_site.Building.Type?.NameToken} ({order.DoneCount}/{order.Total})";

        private static string PieceName(PlannedPiece piece)
        {
            GameObject prefab = PiecePlacer.Prefab(piece.Prefab);
            Piece p = prefab != null ? prefab.GetComponent<Piece>() : null;
            return p != null ? p.m_name : piece.Prefab;
        }

        private static string DescribeItems(Dictionary<string, int> items) => PiecePlacer.DescribeItems(items);

        private string _keptNote = "", _keptNoteFor = "";
        private float _keptNoteTimer;

        /// <summary>
        /// " (more Wood is kept in the Lumberjack's Hut)" when another building's chest keeps some of a missing item (its Keep
        /// in stock list), else "". Looked at again when the needs change, or every few seconds.
        /// </summary>
        private string KeptNote(string needs, Dictionary<string, int> missing, float dt)
        {
            _keptNoteTimer -= dt;
            if (needs == _keptNoteFor && _keptNoteTimer > 0f) return _keptNote;
            _keptNoteFor = needs;
            _keptNoteTimer = 5f;
            _keptNote = "";
            foreach (string item in missing.Keys.OrderBy(k => k))
            {
                var chests = new List<KeptChest>();
                foreach (StorageModule store in ColonyStores())
                {
                    if (store == null || store == OwnStore) continue;
                    int held = store.Contents().TryGetValue(item, out int n) ? n : 0;
                    if (held <= 0) continue;
                    chests.Add(new KeptChest(store.GetComponent<ColonyBuilding>()?.Type?.NameToken, held, store.Spare(item)));
                }
                string where = StockRules.KeptIn(chests);
                if (string.IsNullOrEmpty(where)) continue;
                string itemName = ObjectDB.instance?.GetItemPrefab(item)?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_name ?? item;
                _keptNote = $" ($vc_needs_more {itemName} $vc_needs_kept_in {where})";
                break;
            }
            return _keptNote;
        }

        private void ReportNeeds(string needs)
        {
            if (needs == _reportedNeeds || _site == null) return;
            _reportedNeeds = needs;
            _site.ReportNeeds(needs);
        }

        /// <summary>Tells a player nearby, once per shortage.</summary>
        private void Tell(string needs)
        {
            Player player = Player.m_localPlayer;
            if (player == null || Vector3.Distance(player.transform.position, Citizen.transform.position) > TellPlayerRange || !_told.Add(needs)) return;
            player.Message(MessageHud.MessageType.TopLeft,
                string.Format(Localization.instance.Localize("$vc_builder_needs_msg"), Citizen.CitizenName, Localization.instance.Localize(needs)));
            Gui.HuginTips.Show(Gui.HuginTips.Needs);
        }

        private void Record(PlannedPiece piece, BuildOutcome outcome, float support)
        {
            switch (_tally.Record(outcome))
            {
                case SwingLogKind.Detail:
                    Log.Info($"{Citizen.CitizenName} {(outcome == BuildOutcome.Placed ? "placed" : "saw fall")} {piece.Prefab} #{piece.Index} " +
                             $"at {piece.Position} (expected support {support:0})");
                    break;
                case SwingLogKind.Summary:
                    Log.Info($"{Citizen.CitizenName}: {_tally.Placed} pieces placed, {_tally.Fell} fell");
                    break;
            }
        }

        /// <summary>Holds the tool he borrowed for the work at hand, otherwise his hammer. Neither ever wears out.</summary>
        private void EnsureTool()
        {
            Humanoid body = Citizen.Body;
            Inventory inventory = body.GetInventory();
            if (_toolCopy != null && inventory.GetAllItems().Contains(_toolCopy))
            {
                Hold(body, _toolCopy);
                return;
            }
            _toolCopy = null;
            ItemDrop.ItemData hammer = inventory.GetAllItems().FirstOrDefault(IsHammer);
            if (hammer == null)
            {
                GameObject prefab = ObjectDB.instance.GetItemPrefab(CitizenPrefab.StartingHammer);
                if (prefab != null) inventory.AddItem(prefab, 1);
                return;
            }
            Hold(body, hammer);
        }

        private static void Hold(Humanoid body, ItemDrop.ItemData item)
        {
            if (item.m_shared.m_useDurability && item.m_durability < item.GetMaxDurability()) item.m_durability = item.GetMaxDurability();
            if (!body.IsItemEquiped(item) && !SettlerPose.IsHeld(body)) body.EquipItem(item, false);   // put away while he sits
        }

        private static bool IsHammer(ItemDrop.ItemData item) =>
            item.m_dropPrefab != null && item.m_dropPrefab.name == CitizenPrefab.StartingHammer;

        private static string IdOf(GameObject go)
        {
            ZNetView nview = go != null ? go.GetComponent<ZNetView>() : null;
            if (nview != null && nview.IsValid()) return nview.GetZDO().m_uid.ToString();
            return go != null ? go.GetInstanceID().ToString() : "";
        }
    }
}
