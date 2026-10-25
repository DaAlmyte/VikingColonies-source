using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VikingColonies.Buildings;
using VikingColonies.Config;
using VikingColonies.Core;
using VikingColonies.Jobs;
using static VikingColonies.Construction.CoreVectors;

namespace VikingColonies.Construction
{
    /// <summary>
    /// Before the first piece the builder prepares the site: he clears it (items, plants, trees, logs, stumps, rocks,
    /// leftovers of old colony buildings) and flattens the ground to its level, with tools borrowed from the colony's
    /// chests. While building, he clears whatever grows or falls into the site.
    /// </summary>
    public partial class BuilderJob
    {
        private const float LookSeconds = 4f;              // how often he looks over the site while nothing's being cleared
        private const float HandReach = 1.6f;
        private const float ClearSwingSeconds = 0.6f;
        private const int MaxSwingsPerThing = 40;
        private const int MaxGroundPasses = 3;
        private const float GroundSettleSeconds = 2f;      // the terrain's owner applies the change; look again after this
        private const float ToolCheckSeconds = 5f;
        private const float TreeStandOff = 2f;   // how far behind a tree (opposite its fall) he stands to fell it

        private readonly List<SiteThing> _things = new List<SiteThing>();
        private readonly List<Piece> _siteBlockers = new List<Piece>();
        private readonly TargetBlacklist _badThings = new TargetBlacklist(60);   // something he gave up on is tried again after a minute
        private readonly SiteGiveUps _giveUps = new SiteGiveUps();   // ... and left after a few give-ups (a new site starts afresh)
        private static int s_standMask;
        private float _designTop;   // the top of the site's design: what is above it isn't where the building goes
        private float _designBottom;   // ... and its lowest point: what is below it isn't either
        private readonly List<Collider> _heldRocks = new List<Collider>();   // rocks another building rests on, where the building goes
        private bool _toldHeldRock;
        private string _standFor;   // the rock part his stand spot and aim are for (looked for once, not every frame)
        private string _fromHere;   // the rock part he breaks from where he stands: he couldn't walk up to it
        private string _fromHereOf;   // the part the walks back below are counted for
        private int _fromHereWalkBacks;   // times he walked back to it, moved off (RockWork.KeepsFromHere)
        private Vector3 _standAim, _standSpot;
        private readonly Cooldown _askAgain = new Cooldown(RetrySeconds);
        private readonly Cooldown _toolCheck = new Cooldown(ToolCheckSeconds);
        private LogWatch _logWatch;
        private Vector3 _thingPos;                // where the thing at hand was last seen (a felled tree is gone)
        private string _thingName = "";
        private bool _treeFromAnywhere;           // the spot behind the tree can't be reached: he fells it from where he gets to

        private LogWatch Watch => _logWatch ?? (_logWatch = new LogWatch(Citizen, Ai));
        private readonly Cooldown _hasToolCheck = new Cooldown(ToolCheckSeconds);
        private Vector3 _fallDir;   // the way the tree at hand is felled (away from the site and from buildings)
        private bool _fallPlanned;
        private Footprint _siteFootprint;
        private SiteThing _thing;
        private int _thingSwings;
        private float _lookTimer;
        private bool _prepWaiting;
        private int _prepLevel;   // the Builder's Hut level the site's clearing waits for (0: something else)
        private bool _prepBlocked;
        private bool _hasTool;
        private string _prepNeeds = "";
        private List<GroundOp> _groundOps;
        private List<Vec3> _smoothSpots;
        private int _groundPass;
        private float _groundWait;
        private bool _toldNoShaper;
        private bool _keepCarry;
        private BuildState _asked = BuildState.Waiting;
        private float _prepareAskedAt = -1000f;
        private const float PrepareAskSeconds = 5f;
        private ItemDrop.ItemData _toolCopy;
        private string _toolPrefab = "";
        private ToolKind _toolKind;
        private int _toolTier;

        /// <summary>True while he prepares the site (or waits for what he needs to); false once he may build.</summary>
        private bool Prepare(BuildOrderRecord order, float dt)
        {
            // Creative (1.5, a No build cost world): the site is cleared and flattened at once by its owner; he builds after.
            if (Colony.Creative.BuildersFree() && (order.State == BuildState.Clearing || order.State == BuildState.Shaping))
            {
                Ai.Idle();
                PutToolAway();
                if (Time.time >= _prepareAskedAt + PrepareAskSeconds)
                {
                    _prepareAskedAt = Time.time;
                    Log.Info($"{Citizen.CitizenName}: the {_site.Building.TypeId} site is prepared at once (creative)");
                    _site.RequestPrepareNow();
                }
                return true;
            }
            switch (order.State)
            {
                case BuildState.Waiting:
                    Ai.Idle();   // his claim moves it on
                    return true;
                case BuildState.Clearing:
                    if (ClearSite(order.State, dt)) return true;
                    if (Citizen.Carry.Total > 0 && _hut != null && !_keepCarry)
                    {
                        DeliverHome(dt);   // what he picked up goes to his hut first
                        return true;
                    }
                    Ask(BuildState.Shaping);
                    return true;
                case BuildState.Shaping:
                    if (ShapeGround(order, dt)) return true;
                    Ask(BuildState.Building);
                    return true;
                default:
                    // Something grew or fell into the site; not for a hut he takes down (0.24: he felled trees and broke rocks first).
                    if (order.Kind != OrderKind.Remove && ClearSite(order.State, dt)) return true;
                    PutToolAway();
                    return false;
            }
        }

        /// <summary>Asks the hut's owner to move the order on; again after a few seconds if the order hasn't moved.</summary>
        private void Ask(BuildState next)
        {
            Ai.Idle();
            PutToolAway();
            if (_asked == next && !_askAgain.Ready(Time.time)) return;
            if (_asked != next)
            {
                Log.Info($"{Citizen.CitizenName}: the {_site.Building.TypeId} site is {(next == BuildState.Shaping ? "clear" : "flat")}; next: {next}");
                if (next == BuildState.Building) PlaceSpots();   // the ground changed under his spots
            }
            _asked = next;
            _askAgain.Start(Time.time);
            _fetch = null;   // what he was fetching was for this step
            _site.RequestAdvance(next);
        }

        // ---- Clearing ----

        /// <summary>
        /// Works on the next thing on the site. False when the building may go on: nothing is left that he could clear, or
        /// (once building) nothing he's clearing right now — what he can't clear then is only named in the hut window.
        /// </summary>
        private bool ClearSite(BuildState state, float dt)
        {
            bool items = state == BuildState.Clearing;   // dropped items only on a new site
            if (_thing != null && _thing.IsGone) Progress();
            if (_thing != null && (_thing.IsGone || _thingSwings > MaxSwingsPerThing))
            {
                if (!_thing.IsGone)
                {
                    Log.Info($"{Citizen.CitizenName} gives up on {Utils.GetPrefabName(_thing.Root)} on the site for now");
                    GiveUpOn(_thing);
                }
                else if (_thing.Kind == ClearKind.Tree) Watch.AfterFelling(_thingPos, _fallDir, _thingName);
                else if (_thing.Kind == ClearKind.Log) Watch.AfterBreak(_thingPos, _thingName);
                EndThing();   // and look again once the logs lie still: a felled tree leaves a log and a stump
            }
            if (Watch.Active)
            {
                Citizen.Status = "$vc_status_clearing_site";
                Watch.Step();
                _walkStuck.Reset();   // waiting isn't being stuck: his progress check starts over afterwards
                return true;
            }
            if (_thing == null)
            {
                _lookTimer -= dt;
                if (_lookTimer <= 0f)
                {
                    _lookTimer = LookSeconds;
                    _prepWaiting = !ChooseThing(items, out bool anything) && anything;
                }
                if (_clear != null) return true;   // an old piece: taken down with the hammer (see Tick)
                if (_thing == null)
                {
                    bool holdsUp = SitePrep.HoldsUpBuilding(state, false, _prepWaiting);
                    if (holdsUp)
                    {
                        Ai.Idle();
                        WaitOnPlayer(_prepLevel);   // something left he can't take: a tool, a higher level, or he gave up on it
                    }
                    return holdsUp;
                }
            }
            Citizen.Status = "$vc_status_clearing_site";
            WorkOnThing(dt);
            return true;
        }

        /// <summary>
        /// Looks over the site and takes the next thing to clear: items, plants, trees, logs, stumps, rocks, then old
        /// pieces, nearest first. Things he lacks a tool for are passed over (and asked for); things he gave up on wait.
        /// </summary>
        /// <returns>True when he took something; <paramref name="anything"/> says whether anything is left at all.</returns>
        private bool ChooseThing(bool items, out bool anything)
        {
            BuildOrderRecord order = _site.Order;
            float floor = _site.transform.position.y;
            float bottom = (order != null && !float.IsNaN(order.Ground) ? Mathf.Min(order.Ground, floor) : floor) - 3f;
            SiteScanner.Scan(_siteFootprint, bottom, floor + 12f, _site.Building, null, _site.MatchesPlan, items, _things, _siteBlockers,
                null, null, _heldRocks);
            SiteScanner.AddDoorLandings(_site.DoorLandings, bottom, floor + 12f, _site.Building, _site.MatchesPlan, _things);
            if (order != null && !SiteRules.ClearsPlants(order.Kind)) _things.RemoveAll(t => t.Kind == ClearKind.Plant);   // a repair: the yard's crops stay
            _things.RemoveAll(t => _giveUps.Left(t.Key, InTheBuilding(t)));   // left after a few tries, outside where the building goes
            // A rock another building rests on, where the building itself goes: left whole, it holds the site and is named
            // (the whole-mod review of 1.4.1's builder, I1: the hut went up into it, and the window said nothing).
            anything = _things.Count > 0 || _heldRocks.Count > 0;
            if (_heldRocks.Count > 0 && !_toldHeldRock)
            {
                Log.Info($"{Citizen.CitizenName}: the {_site.Building.TypeId} site waits: a rock another building rests on stands where it goes");
                _toldHeldRock = true;
            }
            Vector3 me = Citizen.transform.position;
            List<ToolInfo> atHand = null;
            var missing = new SiteNeeds();
            string stuck = _heldRocks.Count > 0 ? ThingToken(ClearKind.Rock) : null;
            foreach (SiteThing thing in _things.OrderBy(t => t.Kind).ThenBy(t => Vector3.Distance(me, t.Position)))
            {
                if (_badThings.IsBlocked(thing.Key, Time.time))
                {
                    stuck = stuck ?? ThingToken(thing.Kind);
                    continue;
                }
                ToolKind? tool = SiteCheck.ToolFor(thing.Kind);
                if (tool != null)
                {
                    // The same rule HoldTool follows, so he never takes a thing his level's tools can't work.
                    atHand = atHand ?? BuilderTools.AtHand(ColonyStores());
                    ToolNeed need = ToolChoice.ForLevel(atHand, tool.Value, thing.Tier, Level, out _, out int needsLevel);
                    if (need != ToolNeed.Ready)
                    {
                        missing.Add(NeedText(need, tool.Value, thing.Tier, needsLevel, ThingName(thing)), need == ToolNeed.HutLevel ? needsLevel : 0);
                        continue;
                    }
                }
                ReportPrepNeeds("");
                _prepLevel = 0;
                Take(thing);
                return true;
            }
            if (missing.Text != null)
            {
                Citizen.Status = "$vc_status_needs " + missing.Text;
                ReportPrepNeeds(missing.Text);
                Tell(missing.Text);
            }
            else ReportPrepNeeds("");
            if (stuck != null && missing.Text == null)
            {
                Citizen.Status = "$vc_status_in_the_way " + stuck;
                ReportBlocked(stuck);
                _prepBlocked = true;
            }
            else if (stuck == null && _prepBlocked)
            {
                ReportBlocked("");   // what he named earlier is gone
                _prepBlocked = false;
            }
            _prepLevel = missing.Level;
            return false;
        }

        private void Take(SiteThing thing)
        {
            _thingName = Utils.GetPrefabName(thing.Root);
            _thingPos = thing.Position;
            _walkStuck.Reset();
            _thingSwings = 0;
            if (thing.Kind == ClearKind.Leftover)
            {
                _clear = thing.Piece;   // taken down with the hammer, as in Part 1 (left for a while if he can't get near it)
                _lookTimer = 0f;
                Log.Info($"{Citizen.CitizenName} takes down {Utils.GetPrefabName(thing.Root)} at {thing.Position}, a leftover of an old colony building on the site");
                return;
            }
            _thing = thing;
        }

        private void WorkOnThing(float dt)
        {
            SiteThing thing = _thing;
            _thingPos = thing.Position;
            switch (thing.Kind)
            {
                case ClearKind.Item:
                    PutToolAway();
                    if (PickUp(thing.Root.GetComponent<ItemDrop>(), out bool gaveUp))
                    {
                        if (gaveUp) GiveUpOn(thing);
                        EndThing();
                    }
                    return;
                case ClearKind.Tree:
                case ClearKind.Log:
                case ClearKind.Stump:
                    if (!ReadyToChop(thing)) return;
                    if (thing.Kind == ClearKind.Log && Watch.SeesMoving(thing.Root))
                    {
                        Watch.AfterBreak(thing.Position, _thingName + " (moving)");
                        return;
                    }
                    break;
                case ClearKind.Rock:
                    if (!HoldTool(ToolKind.Pickaxe, thing.Tier))
                    {
                        EndThing();   // the pickaxe went from the chest: he looks again, and asks for one
                        return;
                    }
                    break;
                default:
                    PutToolAway();   // plants come away by hand
                    break;
            }
            Bounds? rockBox = RockBox(thing);
            bool rockPart = rockBox.HasValue;   // a rock in one piece or a big rock's part: worked by its shape, not its pivot
            if (rockPart && _standFor != thing.Key)   // where to stand and aim, looked for once
            {
                _standAim = WorkPoint(thing);
                _standSpot = RockStand(_standAim);
                _standFor = thing.Key;
                Bounds box = rockBox.Value;
                Log.Info($"{Citizen.CitizenName} goes for {Utils.GetPrefabName(thing.Root)} ({box.center.x:0.0}, {box.center.y:0.0}, {box.center.z:0.0}; " +
                         $"{box.size.x:0.0} x {box.size.y:0.0} x {box.size.z:0.0} m): aims at ({_standAim.x:0.0}, {_standAim.y:0.0}, {_standAim.z:0.0}), " +
                         $"stands at ({_standSpot.x:0.0}, {_standSpot.y:0.0}, {_standSpot.z:0.0}); they are at {Citizen.transform.position}");
            }
            Vector3 at = rockPart ? _standAim : WorkPoint(thing);
            bool behind = thing.Kind == ClearKind.Tree && _fallPlanned && !_treeFromAnywhere;
            Vector3 stand = behind ? BehindTree(at) : rockPart ? _standSpot : at;
            if (!WalkUpTo(stand, thing, behind || rockPart ? MoveRules.MinStopDistance : HandReach, behind)) return;
            SwingWhenReady(at, ClearSwingSeconds, dt, () => LandOnThing(thing));
        }

        private bool WalkUpTo(Vector3 at, SiteThing thing, float stop, bool behindTree)
        {
            if (_fromHere == thing.Key)
            {
                // He breaks it from where he stands (RockWork.FromHere), while it is still in reach: moved off (a hop, a
                // nudge), he walks up to it again instead of swinging at the air (1.4.1's deferred minors, polish round 2).
                Bounds? reach = RockBox(thing);
                Vector3 here = Citizen.transform.position;
                float off = reach.HasValue ? Vector3.Distance(here, reach.Value.ClosestPoint(here)) : float.MaxValue;
                if (RockWork.KeepsFromHere(reach.HasValue, off, _fromHereWalkBacks))
                {
                    Ai.Idle();
                    return true;
                }
                _fromHere = null;
                _fromHereWalkBacks++;
                Log.Info($"{Citizen.CitizenName} is {off:0.0} m off {Utils.GetPrefabName(thing.Root)}: walks back to it ({_fromHereWalkBacks} of {RockWork.MaxWalkBacks})");
            }
            if (Ai.GoTo(at, stop))
            {
                _walkStuck.Reset();
                return true;
            }
            if (Citizen.Body.InAttack()) return false;
            if (_walkStuck.Update(Utils.DistanceXZ(Citizen.transform.position, at), Time.time))
            {
                _walkStuck.Reset();
                Vector3 me = Citizen.transform.position;
                Bounds? rockBox = RockBox(thing);
                float distance = rockBox.HasValue ? Vector3.Distance(me, rockBox.Value.ClosestPoint(me)) : float.MaxValue;
                if (RockWork.FromHere(true, rockBox.HasValue, distance))
                {
                    Log.Info($"{Citizen.CitizenName} can't get round to {Utils.GetPrefabName(thing.Root)}; breaks it from where they stand, {distance:0.0} m off");
                    if (_fromHereOf != thing.Key) _fromHereWalkBacks = 0;   // a new part: its walks back start afresh
                    _fromHereOf = thing.Key;
                    _fromHere = thing.Key;
                    return true;
                }
                if (behindTree)
                {
                    Log.Info($"{Citizen.CitizenName} can't get behind {Utils.GetPrefabName(thing.Root)}; they fell it from where they can get to");
                    _treeFromAnywhere = true;
                    return false;
                }
                Log.Info($"{Citizen.CitizenName} can't get to {Utils.GetPrefabName(thing.Root)} on the site; trying again later");
                GiveUpOn(thing);
                EndThing();
            }
            return false;
        }

        /// <summary>Where he stands to fell a tree: behind it, opposite its planned fall, on the ground.</summary>
        private Vector3 BehindTree(Vector3 trunk)
        {
            Vector3 spot = trunk - _fallDir * TreeStandOff;
            spot.y = ZoneSystem.instance.GetGroundHeight(spot);
            return spot;
        }

        /// <summary>The moment his hand or pickaxe lands.</summary>
        private void LandOnThing(SiteThing thing)
        {
            if (thing != _thing || thing.IsGone) return;
            _thingSwings++;
            switch (thing.Kind)
            {
                case ClearKind.Rock:
                    SiteClearer.HitRock(thing, _toolCopy, Citizen.Body);
                    break;
                case ClearKind.Tree:
                    if (_thingSwings == 1 && _fallDir != Vector3.zero)
                    {
                        // He stands behind the tree, opposite its fall: about 180 degrees from the way it falls.
                        Vector3 fromTrunk = Citizen.transform.position - thing.Root.transform.position;
                        fromTrunk.y = 0f;
                        Log.Info($"{Citizen.CitizenName} fells {Utils.GetPrefabName(thing.Root)}: it is to fall towards ({_fallDir.x:0.0}, {_fallDir.z:0.0}); " +
                                 $"they stand {Vector3.Angle(fromTrunk, new Vector3(_fallDir.x, 0f, _fallDir.z)):0} degrees from that, {fromTrunk.magnitude:0.0} m from the trunk" +
                                 (_treeFromAnywhere ? " (from where they could get to: behind it was out of reach)" : ""));
                    }
                    SiteClearer.Chop(thing, _toolCopy, Citizen.Body, _fallDir);
                    break;
                case ClearKind.Log:
                case ClearKind.Stump:
                    SiteClearer.Chop(thing, _toolCopy, Citizen.Body, Vector3.zero);
                    break;
                default:
                    SiteClearer.ClearByHand(thing, Citizen.Body);
                    break;
            }
        }

        /// <summary>
        /// He holds an axe good enough, and a tree has a way to fall that misses buildings (away from the site's middle;
        /// his own hut's chest doesn't count, trees can't hurt it). False when he can't chop it now (he looks again).
        /// </summary>
        /// <summary>The site's own old pieces its upgrade takes down anyway: a tree may fall on them (FallPlanner.MustMiss).</summary>
        private HashSet<GameObject> OldPiecesComingDown()
        {
            var down = new HashSet<GameObject>();
            BuildOrderRecord order = _site.Order;
            if (order == null || _takeAway.Count == 0) return down;
            List<Piece> pieces = SitePieces();
            for (int i = 0; i < _takeAway.Count; i++)
            {
                if (order.TakenAway.Contains(i)) continue;
                Piece piece = PiecePlacer.FindStanding(_takeAway[i], pieces);
                if (piece != null) down.Add(piece.gameObject);
            }
            return down;
        }

        private bool ReadyToChop(SiteThing thing)
        {
            if (!HoldTool(ToolKind.Axe, thing.Tier))
            {
                EndThing();   // the axe went from the chest: he looks again, and asks for one
                return false;
            }
            if (thing.Kind != ClearKind.Tree || _fallPlanned) return true;
            Vector3? planned = Jobs.Chopper.PlanFall(thing.Root, thing.Root.transform.position - U(_siteFootprint.Centre), _site.Building.gameObject,
                OldPiecesComingDown());
            if (planned == null)
            {
                Log.Info($"{Citizen.CitizenName} can't fell {Utils.GetPrefabName(thing.Root)} without hitting a building; it stays in the way");
                GiveUpOn(thing);
                EndThing();
                return false;
            }
            _fallDir = planned.Value;
            _fallPlanned = true;
            return true;
        }

        /// <summary>
        /// He gives up on a thing on the site: tried again in a minute, and after a few give-ups left where it is, so the
        /// building goes up without it (SiteGiveUps).
        /// </summary>
        private void GiveUpOn(SiteThing thing)
        {
            _badThings.Add(thing.Key, Time.time);
            _giveUps.Add(thing.Key);
            if (_giveUps.Left(thing.Key, InTheBuilding(thing)))
                Log.Info($"{Citizen.CitizenName} leaves {Utils.GetPrefabName(thing.Root)} on the {_site.Building.TypeId} site: given up on " +
                         $"{SiteGiveUps.LeaveAfter} times, and it isn't where the building goes; the building goes up without clearing it");
        }

        /// <summary>
        /// It stands where the building itself goes (the site without its margin, below the design's top): never left,
        /// it keeps holding the site and is named in the hut's window (the fix pass's review: pieces went up into a tree or
        /// a rock that was left).
        /// </summary>
        private bool InTheBuilding(SiteThing thing)
        {
            if (!SiteGiveUps.HoldsTheSite(thing.Kind)) return false;   // an item or a bush: left after a few tries anywhere (M1)
            if (_siteFootprint == null) return true;
            Footprint inside = _siteFootprint.Grow(-SiteSurvey.SiteMargin);
            Bounds? body = thing.Area != null ? thing.Area.bounds : SolidBounds(thing.Root);   // any of it, not only its root
            if (body.HasValue)   // between the design's lowest point and its top (M1: a rock part under the floor doesn't hold)
                return body.Value.max.y >= _designBottom && body.Value.min.y <= _designTop && SiteScanner.Reaches(inside, body.Value);
            Vector3 at = thing.Position;
            return at.y >= _designBottom && at.y <= _designTop && inside.Contains(V(at));
        }

        /// <summary>
        /// A rock's shape: a big rock's part, or a rock in one piece by all its solid colliders (the camp-at-the-boulder run on
        /// 1.4.1's builder: the boulder's rocks were rocks in one piece, and he was sent to their pivots, inside the boulder,
        /// where no way led). Null for anything else.
        /// </summary>
        private Bounds? RockBox(SiteThing thing)
        {
            if (thing.Area != null) return thing.Area.bounds;
            if (thing.Kind != ClearKind.Rock) return null;
            // The last rock's shape is kept: it doesn't change until it breaks, and looking up every collider each frame
            // made garbage (1.4.1's deferred minors, polish round 2).
            if (thing.Root != null && thing.Root == _rockBoxRoot) return _rockBoxOf;
            _rockBoxRoot = thing.Root;
            _rockBoxOf = SolidBounds(thing.Root);
            return _rockBoxOf;
        }

        private GameObject _rockBoxRoot;
        private Bounds? _rockBoxOf;

        /// <summary>What a thing's solid colliders take up together; null when it has none.</summary>
        private static Bounds? SolidBounds(GameObject root)
        {
            Bounds? all = null;
            if (root == null) return null;
            foreach (Collider collider in root.GetComponentsInChildren<Collider>())
            {
                if (collider == null || collider.isTrigger || !collider.enabled) continue;
                if (all.HasValue) { Bounds b = all.Value; b.Encapsulate(collider.bounds); all = b; }
                else all = collider.bounds;
            }
            return all;
        }

        /// <summary>
        /// Where he works on a thing: a big rock's part from the site's side (the side of its box towards the site's
        /// middle, at the ground's height), the nearest part of a log, otherwise where it stands.
        /// </summary>
        private Vector3 WorkPoint(SiteThing thing)
        {
            Vector3 me = Citizen.transform.position;
            Bounds? rockBox = RockBox(thing);
            if (rockBox.HasValue)
            {
                Bounds box = rockBox.Value;
                Vector3 middle = U(_siteFootprint.Centre);
                float ground = ZoneSystem.instance.GetGroundHeight(middle);
                return box.ClosestPoint(new Vector3(middle.x, Mathf.Clamp(ground + 1f, box.min.y, box.max.y), middle.z));
            }
            if (thing.Kind == ClearKind.Log && ChopShape.TryGet(thing.Root, out Vector3 a, out Vector3 b, out _))
            {
                Vector3 axis = b - a;
                float t = axis.sqrMagnitude < 0.0001f ? 0f : Mathf.Clamp01(Vector3.Dot(me - a, axis) / axis.sqrMagnitude);
                return a + axis * t;
            }
            return thing.Position;
        }

        /// <summary>
        /// Where he stands to break a big rock's part: on the ground on the site's side of it, a step at a time towards the
        /// site's middle until there's room for him (the camp-at-the-boulder run on the 1.4.0 build: the part's box nearest
        /// him was in the air or inside the boulder, he never got there and tried again every minute).
        /// </summary>
        private Vector3 RockStand(Vector3 side)
        {
            Vector3 first = Vector3.zero;
            bool any = false;
            // Towards the site's middle first, then towards him, then the four ways round (the whole-mod review, M2: a part
            // whose box covers the site's middle had every step that way inside the boulder).
            Vector3 me = Citizen.transform.position;
            foreach (Vector3 towards in new[] { U(_siteFootprint.Centre) - side, me - side, Vector3.forward, Vector3.back, Vector3.right, Vector3.left })
            {
                Vector3 flat = new Vector3(towards.x, 0f, towards.z);
                if (flat.sqrMagnitude < 0.0001f) continue;
                Vector3 way = flat.normalized;
                for (float step = RockStandStep; step <= RockStandFar; step += RockStandStep)
                {
                    Vector3 spot = side + way * step;
                    spot.y = ZoneSystem.instance.GetGroundHeight(spot);
                    if (!any) { first = spot; any = true; }
                    if (StandsFree(spot)) return spot;
                }
            }
            return any ? first : side;
        }

        /// <summary>
        /// A spot by the chest he can stand at with nothing between him and it (in front, behind, then beside it), on the
        /// floor it stands on; false when there's none.
        /// </summary>
        private static bool OpenSideOf(Transform chest, out Vector3 spot)
        {
            foreach (Vector3 way in new[] { chest.forward, -chest.forward, chest.right, -chest.right })
            {
                Vector3 flat = new Vector3(way.x, 0f, way.z).normalized;
                spot = chest.position + flat * ChestSide;
                if (!AI.Walls.StructureBetween(spot, chest.position) && StandsFree(spot)) return true;
            }
            spot = chest.position;
            return false;
        }

        private const float ChestSide = 1.1f;

        private const float RockStandStep = 0.6f;
        private const float RockStandFar = 4f;

        /// <summary>Room for him to stand there: nothing solid (a rock, a wall) in a capsule his size; other settlers don't count.</summary>
        private static bool StandsFree(Vector3 spot)
        {
            if (s_standMask == 0) s_standMask = LayerMask.GetMask("piece", "piece_nonsolid", "Default", "static_solid", "Default_small", "vehicle");
            return !Physics.CheckCapsule(spot + Vector3.up * 0.5f, spot + Vector3.up * 1.6f, 0.4f, s_standMask, QueryTriggerInteraction.Ignore);
        }

        private void EndThing()
        {
            _standFor = null;
            _fromHere = null;
            _fallPlanned = false;
            _fallDir = Vector3.zero;
            _thing = null;
            _thingSwings = 0;
            _treeFromAnywhere = false;
            _lookTimer = 0f;   // whatever ended it, he looks over the site again right away
        }

        /// <summary>
        /// The thing in words, a tree with its kind: "a tree (Oak)" (the 1.0 play-through: "for a tree" in a wood left the
        /// player guessing which one to clear).
        /// </summary>
        private static string ThingName(SiteThing thing)
        {
            string token = ThingToken(thing.Kind);
            if (thing.Kind != ClearKind.Tree || thing.Root == null) return token;
            string name = TreeKinds.Token(TreeKinds.Of(Utils.GetPrefabName(thing.Root)));
            return name.Length > 0 ? $"{token} ({name})" : token;
        }

        private static string ThingToken(ClearKind kind)
        {
            switch (kind)
            {
                case ClearKind.Item: return "$vc_thing_item";
                case ClearKind.Tree: return "$vc_thing_tree";
                case ClearKind.Log: return "$vc_thing_log";
                case ClearKind.Stump: return "$vc_thing_stump";
                case ClearKind.Rock: return "$vc_thing_rock";
                case ClearKind.Leftover: return "$vc_thing_leftover";
                default: return "$vc_thing_plant";
            }
        }

        /// <summary>What he picked up while clearing goes to his hut.</summary>
        private void DeliverHome(float dt)
        {
            PutToolAway();
            Citizen.Status = "$vc_status_delivering";
            if (!Reach(_hut.transform)) return;
            Ai.Idle();
            _recheckTimer -= dt;
            if (_recheckTimer > 0f) return;
            _recheckTimer = RecheckSeconds;
            StorageModule.DepositResult result = _hut.Storage.TryDeposit(Citizen.Carry);
            if (result == StorageModule.DepositResult.Busy) return;   // a player has the chest open
            Citizen.SaveCarry();
            if (result == StorageModule.DepositResult.StorageFull) _keepCarry = true;   // he carries the rest along
        }

        private void ReportPrepNeeds(string needs)
        {
            if (needs == _prepNeeds) return;
            _prepNeeds = needs;
            ReportNeeds(needs);
        }

        // ---- Shaping the ground ----

        /// <summary>Flattens the site. False once the ground is done, or when it can't be shaped in this game.</summary>
        private bool ShapeGround(BuildOrderRecord order, float dt)
        {
            if (!GroundShaper.Ready || float.IsNaN(order.Ground))
            {
                if (!_toldNoShaper) Log.Warn($"{Citizen.CitizenName}: the ground can't be shaped here; the {_site.Building.TypeId} is built on the ground as it is.");
                _toldNoShaper = true;
                return false;
            }
            Citizen.Status = "$vc_status_flattening";
            if (_groundWait > 0f)
            {
                _groundWait -= dt;
                Ai.Idle();
                return true;
            }
            if (_smoothSpots == null)
            {
                if (_groundOps == null)
                {
                    _groundOps = _groundPass < MaxGroundPasses
                        ? _site.GroundOps(order.Ground)   // the site and the ground in front of its doors
                        : new List<GroundOp>();
                    _groundPass++;
                    if (_groundOps.Count > 0)
                        Log.Info($"{Citizen.CitizenName} flattens the {_site.Building.TypeId} site to {order.Ground:0.00} (pass {_groundPass}): " +
                                 $"{_groundOps.Count} spots, {_groundOps.Count(o => o.Dig)} to dig, {_groundOps.Count(o => o.Raise)} to raise " +
                                 $"({GroundPlan.Stone(_groundOps, GroundShaper.StonePerCubicMetre)} {GroundShaper.StoneItem})");
                    else
                    {
                        if (_groundPass > MaxGroundPasses) Log.Info($"{Citizen.CitizenName}: the {_site.Building.TypeId} site is as flat as they get it");
                        _smoothSpots = _groundPass > 1   // smooth the edges he changed, but not next to another building
                            ? _site.SmoothSpots()
                            : new List<Vec3>();
                    }
                }
                if (_groundOps.Count > 0) return WorkGround(_groundOps[0], dt);
                if (_smoothSpots == null)
                {
                    _groundOps = null;   // that pass is done: look again once the ground has settled
                    _groundWait = GroundSettleSeconds;
                    return true;
                }
            }
            if (_smoothSpots.Count > 0) return SmoothEdge(order, _smoothSpots[0], dt);
            PutToolAway();
            return false;
        }

        private bool WorkGround(GroundOp op, float dt)
        {
            int stone = op.Raise ? GroundPlan.Stone(new[] { op }, GroundShaper.StonePerCubicMetre) : 0;
            if (stone > 0 && Citizen.Carry.Count(GroundShaper.StoneItem) < stone)
            {
                PutToolAway();
                Fetch(GroundNeeds, dt);
                return true;
            }
            _fetch = null;
            if (!HasTool(ToolKind.Hoe) || !HoldTool(op.DeepDig ? ToolKind.Pickaxe : ToolKind.Hoe, 0)) return true;   // he says what he needs
            Vector3 at = U(op.Position);
            if (!ReachFromSpot(at, out bool unreachable))
            {
                if (unreachable)
                {
                    Log.Info($"{Citizen.CitizenName} can't get near the ground at {at}; it stays as it is");
                    _groundOps.RemoveAt(0);
                }
                return true;
            }
            SwingWhenReady(at, ColonyConfig.SecondsPerPiece.Value, dt, () =>
            {
                if (_groundOps == null || _groundOps.Count == 0 || _groundOps[0] != op) return;
                GroundShaper.Level(at);
                Progress();
                if (stone > 0)
                {
                    Citizen.Carry.Remove(GroundShaper.StoneItem, stone);
                    Citizen.SaveCarry();
                }
                _groundOps.RemoveAt(0);
            });
            return true;
        }

        /// <summary>Stone for the spot at hand, then for the next raised spots.</summary>
        private List<IReadOnlyDictionary<string, int>> GroundNeeds() => _groundOps
            .Where(o => o.Raise)
            .Select(o => (IReadOnlyDictionary<string, int>)new Dictionary<string, int>
            {
                { GroundShaper.StoneItem, GroundPlan.Stone(new[] { o }, GroundShaper.StonePerCubicMetre) },
            })
            .ToList();

        /// <summary>Blends the ground just outside the site halfway towards the site's level, so its edge isn't a step.</summary>
        private bool SmoothEdge(BuildOrderRecord order, Vec3 spot, float dt)
        {
            if (!HoldTool(ToolKind.Hoe, 0)) return true;
            var at = new Vector3(spot.X, (SiteSurvey.GroundAt(spot.X, spot.Z) + order.Ground) / 2f, spot.Z);
            if (!ReachFromSpot(at, out bool unreachable))
            {
                if (unreachable) _smoothSpots.RemoveAt(0);
                return true;
            }
            SwingWhenReady(at, ClearSwingSeconds, dt, () =>
            {
                if (_smoothSpots == null || _smoothSpots.Count == 0 || !_smoothSpots[0].Equals(spot)) return;
                GroundShaper.Smooth(at);
                Progress();
                _smoothSpots.RemoveAt(0);
            });
            return true;
        }

        // ---- Borrowed tools ----

        private int Level => _hut != null ? _hut.Level : 1;

        /// <summary>The best tier of this kind his hut's level lets him use (hoes: any).</summary>
        private int ToolCap(ToolKind kind) => HutLevels.ToolTierCap(kind, Level);

        /// <summary>
        /// What to ask for when work waits on a tool: a higher Builder's Hut level (for <paramref name="thing"/>, a
        /// $vc_thing token) or clearing it yourself, or a tool his level allows.
        /// </summary>
        private string NeedText(ToolNeed need, ToolKind kind, int tier, int needsLevel, string thing = null)
        {
            if (need == ToolNeed.HutLevel && needsLevel > 0)   // his level's tools can't do it
                return $"$vc_builder_hut_level {needsLevel}" + (thing != null ? $" $vc_for {thing}" : "") + " $vc_or_clear_yourself";
            if (need == ToolNeed.HutLevel) return BuilderTools.Describe(kind, tier) + " $vc_or_clear_yourself";   // no hut level reaches it (modded tiers)
            return BuilderTools.Describe(kind, tier, ToolCap(kind));
        }

        /// <summary>Whether a chest holds a tool of this kind his level allows (he looks again every few seconds).</summary>
        private bool HasTool(ToolKind kind)
        {
            if (_toolCopy != null && _toolKind == kind) return true;
            if (_hasToolCheck.Ready(Time.time))
            {
                _hasToolCheck.Start(Time.time);
                _hasTool = BuilderTools.FindIn(ColonyStores(), kind, 0, ToolCap(kind)) != null;
            }
            if (!_hasTool) SayNeeds(BuilderTools.Describe(kind, 0, ToolCap(kind)));
            return _hasTool;
        }

        /// <summary>
        /// Holds a copy of the best tool of this kind the colony's chests hold (the tool stays in the chest). False — and
        /// he says what he needs — when there's none good enough.
        /// </summary>
        private bool HoldTool(ToolKind kind, int tier)
        {
            if (_toolCopy != null && _toolKind == kind && _toolTier >= tier && !_toolCheck.Ready(Time.time)) return true;
            _toolCheck.Start(Time.time);
            ToolNeed need = ToolChoice.ForLevel(BuilderTools.AtHand(ColonyStores()), kind, tier, Level, out ToolInfo tool, out int needsLevel);
            if (need != ToolNeed.Ready)
            {
                PutToolAway();
                SayNeeds(NeedText(need, kind, tier, needsLevel));
                return false;
            }
            ReportPrepNeeds("");
            if (_toolCopy == null || tool.Prefab != _toolPrefab)
            {
                PutToolAway();
                GameObject prefab = ObjectDB.instance.GetItemPrefab(tool.Prefab);
                Inventory inventory = Citizen.Body.GetInventory();
                if (prefab == null || !inventory.AddItem(prefab, 1)) return false;
                _toolCopy = inventory.GetAllItems().LastOrDefault(i => i.m_dropPrefab != null && i.m_dropPrefab.name == tool.Prefab);
                if (_toolCopy == null) return false;
                _toolPrefab = tool.Prefab;
                _toolKind = kind;
                _toolTier = tool.Tier;
                Log.Info($"{Citizen.CitizenName} works with a copy of the colony's {tool.Prefab} (it stays in the chest)");
            }
            EnsureTool();
            return true;
        }

        private void SayNeeds(string needs)
        {
            WaitOnPlayer();   // a tool missing
            Citizen.Status = "$vc_status_needs " + needs;
            ReportPrepNeeds(needs);
            Tell(needs);
            Ai.Idle();
        }

        /// <summary>Puts the borrowed tool's copy away; he holds his hammer again.</summary>
        private void PutToolAway()
        {
            if (_toolCopy == null) return;
            Humanoid body = Citizen.Body;
            if (body.IsItemEquiped(_toolCopy)) body.UnequipItem(_toolCopy, false);
            body.GetInventory().RemoveItem(_toolCopy);
            _toolCopy = null;
            _toolPrefab = "";
        }

        /// <summary>Forgets the site's preparation (he leaves the site).</summary>
        private void ResetPrep()
        {
            EndThing();
            _things.Clear();
            _siteBlockers.Clear();
            _lookTimer = 0f;
            _prepWaiting = false;
            _prepLevel = 0;
            _prepBlocked = false;
            _prepNeeds = "";
            _groundOps = null;
            _smoothSpots = null;
            _groundPass = 0;
            _groundWait = 0f;
            _keepCarry = false;
            _asked = BuildState.Waiting;
            _askAgain.Clear();
            _toolCheck.Clear();
            _hasToolCheck.Clear();
            PutToolAway();
        }
    }
}
