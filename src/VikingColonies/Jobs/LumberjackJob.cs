using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VikingColonies.AI;
using VikingColonies.Buildings;
using VikingColonies.Citizens;
using VikingColonies.Config;
using VikingColonies.Construction;
using VikingColonies.Core;
using Object = UnityEngine.Object;

namespace VikingColonies.Jobs
{
    /// <summary>
    /// Lumberjack routine. Every tick: look around (every few seconds), ask LumberjackBrain what to do,
    /// then do one small piece of it (walk a bit, swing once, pick one thing up, deliver).
    /// </summary>
    public class LumberjackJob : Job
    {
        private const float RescanSeconds = 3f;
        private const float AxeCheckSeconds = 10f;
        private const float IdleRescanSeconds = 30f;
        private const float CollectRadius = 8f;
        private const float HallReach = 2.5f;
        private const float DepositRetrySeconds = 2f;
        private const float StorageFullRetrySeconds = 10f;

        // Shared by every lumberjack this computer runs: who works on which tree, log, stump and piece of wood.
        private static readonly ClaimBoard s_claims = new ClaimBoard();
        private const float FelledAreaRadius = 12f;   // the logs and stump of a felled tree land within this
        private string _targetId = "";
        private string _dropId;

        private readonly TargetBlacklist _blacklist = new TargetBlacklist(300);
        private readonly StuckDetector _collectStuck = new StuckDetector();
        private readonly StuckDetector _depositStuck = new StuckDetector();
        private readonly FullChestRun _fullChest = new FullChestRun();   // his chest full: the load goes elsewhere (0.21)
        private bool _toldFromHere;   // the log already says he delivers from where he stopped

        private readonly ColonyBuilding _hut;
        private readonly Chopper _chopper;
        private readonly LogWatch _logWatch;
        private ItemDrop _drop;
        private Vector3 _lastChopSpot;
        private bool _haveChopSpot;
        private Vector3 _felledSpot;   // where the tree he felled last stood: its logs and stump are his to finish
        private bool _haveFelled;
        private float _rescanTimer;
        private float _axeCheck;
        private ItemDrop.ItemData _borrowedAxe;
        private string _borrowedAxePrefab = "";
        private float _lastBreakTime = float.NegativeInfinity;
        private float _depositTimer;
        private bool _storageFull;

        private sealed class PlantSpot
        {
            public PlantSpot(Vector3 position, ReplantInfo replant)
            {
                Position = position;
                Replant = replant;
            }

            public Vector3 Position { get; }
            public ReplantInfo Replant { get; }
        }

        private readonly List<PlantSpot> _plantSpots = new List<PlantSpot>();
        private readonly StuckDetector _plantStuck = new StuckDetector();
        private LumberjackAction? _lastAction;
        private ReplantState _replant = ReplantState.Off;
        private int _plantTurn;

        public LumberjackJob(Citizen citizen, CitizenAI ai, ColonyBuilding hut) : base(citizen, ai)
        {
            _hut = hut;
            // Each hit practises woodcutting: practice makes a better lumberjack.
            _chopper = new Chopper(citizen, ai, () => citizen.Practise(WorkSkill.Woodcutting), alwaysDirect: false);
            _logWatch = new LogWatch(citizen, ai);
        }

        private static readonly string[] s_actionParts = System.Enum.GetNames(typeof(LumberjackAction)).Select(a => "lumberjack: " + a).ToArray();

        public override void Tick(float dt)
        {
            ColonyBuilding hut = _hut;
            long timer = Perf.Start();
            EnsureAxeEquipped(hut, dt);
            Perf.Stop("lumberjack: the axe", timer);
            if (Citizen.Carry.Total == 0) _storageFull = false;
            if (_fullChest.Tick(Citizen, Ai, hut, SeedPlan.Reserved(Citizen.Carry, SpotSeeds()))) return;

            // Unity reports a destroyed object as == null while our reference is still set: the target broke.
            GameObject target = _chopper.Target;
            if (!ReferenceEquals(target, null) && target == null)
            {
                long broke = Perf.Start();
                OnTargetBroken();
                Perf.Stop("lumberjack: after a tree or log broke", broke);
            }

            _rescanTimer -= dt;
            timer = Perf.Start();
            if (_rescanTimer <= 0f) Rescan(hut);
            Perf.Stop("lumberjack: looking around", timer);

            timer = Perf.Start();
            bool replant = _replant == ReplantState.On;
            if (!replant) _plantSpots.Clear();   // the seeds are delivered to storage instead
            LumberjackAction action = LumberjackBrain.Decide(new LumberjackSituation
            {
                HasHall = hut != null,
                IsNight = EnvMan.IsNight(),
                CarryTotal = Citizen.Carry.Total,
                ReservedSeeds = SeedPlan.Total(SeedPlan.Reserved(Citizen.Carry, SpotSeeds())),
                CarryCapacity = ColonyConfig.CarryCapacity.Value,
                StorageFull = _storageFull,
                DropsNearby = _drop != null,
                DropIsLeftover = _drop != null && _haveFelled && Vector3.Distance(_drop.transform.position, _felledSpot) <= ChopArea.LeftoverReach,
                PlantSpotPending = _plantSpots.Count > 0,
                HasSeedForPlantSpot = _plantSpots.Any(p => Citizen.Carry.Count(p.Replant.Seed) > 0),
                SeedInStorage = hut != null && _plantSpots.Any(p => hut.Storage.HasItem(p.Replant.Seed)),
                HasChopTarget = _chopper.Target != null,
                ChopTargetIsLeftover = IsLeftover(_chopper.Target),
                ReplantBlocked = !replant,
                EnoughWood = EnoughWood(hut, dt),
            });
            Perf.Stop("lumberjack: deciding", timer);

            if (action != _lastAction)   // one log line per step change, so problems can be read from the log
            {
                Log.Info($"{Citizen.CitizenName}: {_lastAction?.ToString() ?? "start"} -> {action} (carrying {Citizen.Carry.Total})");
                _lastAction = action;
            }

            // After a felling blow or a log breaking he keeps clear until the logs nearby lie still.
            if (_logWatch.Active && (action == LumberjackAction.Chop || action == LumberjackAction.Collect || action == LumberjackAction.Plant))
            {
                Citizen.Status = "$vc_status_keeping_clear";   // 1.4.2: what he is doing, not "Working"
                timer = Perf.Start();
                _logWatch.Step();
                Perf.Stop("lumberjack: watching logs settle", timer);
                _chopper.Resume();   // waiting isn't being stuck: his progress checks start over afterwards
                return;
            }

            timer = Perf.Start();
            DoAction(action, hut, dt);
            Perf.Stop(s_actionParts[(int)action], timer);
        }

        private void DoAction(LumberjackAction action, ColonyBuilding hut, float dt)
        {
            switch (action)
            {
                case LumberjackAction.Unemployed:
                    Citizen.Status = "$vc_status_unemployed";
                    Ai.Idle();
                    break;
                case LumberjackAction.Rest:
                    Citizen.Status = "$vc_status_resting";
                    if (Ai.GoTo(hut.transform.position, 4f)) Ai.Idle();
                    break;
                case LumberjackAction.Deposit:
                    Citizen.Status = "$vc_status_delivering";
                    if (ReachHut(hut)) Deliver(hut, dt, DepositRetrySeconds);
                    break;
                case LumberjackAction.WaitStorageFull:
                    Citizen.Status = "$vc_status_storage_full";
                    if (ReachHut(hut)) Deliver(hut, dt, StorageFullRetrySeconds);
                    break;
                case LumberjackAction.Collect:
                    Citizen.Status = "$vc_status_gathering_logs";
                    Collect();
                    break;
                case LumberjackAction.Plant:
                    Citizen.Status = "$vc_status_planting";
                    Plant();
                    break;
                case LumberjackAction.Chop:
                    Citizen.Status = "$vc_status_felling";
                    Chop();
                    break;
                case LumberjackAction.WaitNoTrees:
                    // In a wood of birch and oak at level 1 "No trees nearby" puzzled the player (the 1.0 play-through).
                    Citizen.Status = _barredTreesNear ? "$vc_status_no_trees_to_fell" : "$vc_status_no_trees";
                    Ai.FreeTime(hut.transform.position);
                    break;
                case LumberjackAction.WaitEnoughWood:
                    Citizen.Status = "$vc_status_enough_wood";
                    Ai.FreeTime(hut.transform.position);
                    break;
            }
        }

        private const float WoodCountSeconds = 5f;
        private float _woodCountTimer;
        private bool _enoughWood;

        /// <summary>
        /// "Stop at" (0.22): the colony's chests hold the hut's amount of Wood; counted every few seconds. He starts again
        /// below it.
        /// </summary>
        private bool EnoughWood(ColonyBuilding hut, float dt)
        {
            _woodCountTimer -= dt;
            if (_woodCountTimer > 0f) return _enoughWood;
            _woodCountTimer = WoodCountSeconds;
            WorkerModule worker = hut != null ? hut.GetComponent<WorkerModule>() : null;
            int limit = worker != null ? worker.WoodLimit : 0;
            int wood = 0;
            if (limit > 0)
                foreach (ColonyBuilding b in ColonyBuilding.All)
                    if (b != null && b.IsValid && b.ColonyId == hut.ColonyId && b.Storage != null && b.Storage.Contents().TryGetValue("Wood", out int n))
                        wood += n;
            bool enough = WoodLimit.Enough(limit, wood);
            if (enough != _enoughWood)
                Log.Info(enough ? $"{Citizen.CitizenName} has enough wood: {wood} in the colony's chests (stop at {limit})"
                    : $"{Citizen.CitizenName} goes back to work: {wood} wood in the colony's chests (stop at {(limit > 0 ? limit.ToString() : "no limit")})");
            _enoughWood = enough;
            return enough;
        }

        /// <summary>Self-test: the wood count is done again at the next tick.</summary>
        internal void CountWoodNowForTest() => _woodCountTimer = 0f;

        public override void Resume()
        {
            _chopper.Resume();
            _collectStuck.Reset();
            _plantStuck.Reset();
            _depositStuck.Reset();
            SwingLog.EndSwing(Citizen);
            _rescanTimer = 0f;
        }

        private void Rescan(ColonyBuilding hut)
        {
            ReplantState replant = ReplantRules.State(ColonyConfig.ReplantEnabled.Value, ColonyConfig.ReplantNeedsCultivator.Value,
                Construction.BuilderTools.InColony(hut, ReplantRules.CultivatorPrefab));   // the hut's chest or another of the colony's (1.3.1)
            if (replant != _replant) Log.Info($"{Citizen.CitizenName}: replanting {replant}");
            _replant = replant;
            _rescanTimer = RescanSeconds;
            if (hut == null)
            {
                ClearTarget();
                ForgetDrop();
                return;
            }
            double now = Time.time;
            Vector3 hutPos = hut.transform.position;
            float radius = WorkRadiusOf(hut);
            // A standing tree whose kind the player unticked on the way there is left standing (0.22).
            GameObject current = _chopper.Target;
            string currentPrefab = current != null ? Utils.GetPrefabName(current) : "";
            if (current != null && TreeCatalogBuilder.Catalog.KindOf(currentPrefab) == ChopKind.Tree)
            {
                WorkerModule worker = hut.GetComponent<WorkerModule>();
                if (worker != null && !TreeChoice.MayFell(currentPrefab, worker.Unfelled))
                {
                    Log.Info($"{Citizen.CitizenName} leaves the {currentPrefab} standing: its kind is unticked on the Trees list");
                    ClearTarget();
                }
            }
            if (_chopper.Target == null)
            {
                long timer = Perf.Start();
                GameObject target = FindChopTarget(hutPos, radius, now, out Vector3 fallDir);
                Perf.Stop("lumberjack: choosing a tree", timer);
                SetTarget(target, fallDir);
            }
            if (_drop == null)
            {
                _drop = FindDrop(hutPos, radius, now);
                if (_drop != null)
                {
                    _dropId = IdOf(_drop.gameObject);
                    if (!s_claims.TryClaim(_dropId, Ground(_drop.transform.position), Citizen.Id, now)) ForgetDrop();
                }
            }
            // Just after something broke, its log, stump and wood can appear a moment later: keep looking for a while.
            if (_chopper.Target == null && _drop == null) _rescanTimer = Time.time - _lastBreakTime < 10f ? RescanSeconds : IdleRescanSeconds;
        }

        /// <summary>Nearest choppable thing; standing trees only if they can be felled without hitting a building.</summary>
        /// <summary>His area has trees his hut's level or the Trees list keeps him off (as of his last look round).</summary>
        private bool _barredTreesNear;

        private GameObject FindChopTarget(Vector3 hutPos, float radius, double now, out Vector3 fallDir)
        {
            fallDir = Vector3.zero;
            TreeCatalog catalog = TreeCatalogBuilder.Catalog;
            int axeTier = AxeTier();
            Vector3 me = Citizen.transform.position;
            var candidates = new List<KeyValuePair<float, GameObject>>();
            // The Trees list and the player's buildings (0.22): what he leaves standing.
            WorkerModule worker = _hut != null ? _hut.GetComponent<WorkerModule>() : null;
            HashSet<string> unfelled = worker != null ? worker.Unfelled : new HashSet<string>();
            float keepOff = ColonyConfig.LeaveTreesNearBuildings.Value;
            List<GroundBox> buildings = null;   // gathered when a near tree is first weighed (0.24: not for every tree in his area)
            bool barred = false;
            foreach (GameObject candidate in WorldScan.Choppables())
            {
                if (candidate == null) continue;   // destroyed since the last scan
                Vector3 pos = candidate.transform.position;
                float fromFelled = Vector3.Distance(pos, _felledSpot);
                // Far outside his area and not a leftover of his tree: skipped before the costlier look (vc_perf, 0.10).
                if (Vector3.Distance(pos, hutPos) > radius && !(_haveFelled && fromFelled <= ChopArea.LeftoverReach)) continue;
                string prefab = Utils.GetPrefabName(candidate);
                bool isTree = catalog.KindOf(prefab) == ChopKind.Tree;
                // A tree in his area that his hut's level or the Trees list keeps him off: said so when nothing else is left.
                if (!catalog.CanChop(prefab, axeTier) || (isTree && !TreeChoice.MayFell(prefab, unfelled)))
                {
                    barred |= isTree && Vector3.Distance(pos, hutPos) <= radius;
                    continue;
                }
                // Trees inside the work area; the logs and stump of the tree he felled wherever they landed.
                if (!ChopArea.Counts(isTree, Vector3.Distance(pos, hutPos), radius, _haveFelled, fromFelled)) continue;
                if (_blacklist.IsBlocked(IdOf(candidate), now)) continue;
                if (s_claims.IsTakenByOther(IdOf(candidate), Ground(pos), Citizen.Id, now)) continue;   // another lumberjack's
                float score = Vector3.Distance(me, pos);
                // Finish the tree we just felled (its logs and stump) before starting a new one.
                if (ChopArea.IsLeftover(isTree, _haveFelled, fromFelled)) score -= 1000f;
                candidates.Add(new KeyValuePair<float, GameObject>(score, candidate));
            }
            _barredTreesNear = barred;
            foreach (KeyValuePair<float, GameObject> candidate in candidates.OrderBy(c => c.Key))
            {
                if (catalog.KindOf(Utils.GetPrefabName(candidate.Value)) != ChopKind.Tree)
                {
                    float fromHut = Vector3.Distance(candidate.Value.transform.position, hutPos);
                    if (fromHut > radius)
                        Log.Info($"{Citizen.CitizenName} finishes the {Utils.GetPrefabName(candidate.Value)} of the tree they felled, past their work area ({fromHut:0} m from the hut)");
                    return candidate.Value;
                }
                // By one of the player's buildings: left standing (checked only for the nearest few, the pieces gathered once).
                if (keepOff > 0f)
                {
                    Vector3 at = candidate.Value.transform.position;
                    if (buildings == null) buildings = BuildingPieces(hutPos, radius + keepOff + LongPieceReach);
                    if (TreeRules.NearBuilding(Ground(at), buildings, keepOff))
                    {
                        if (_toldByBuilding.Add(IdOf(candidate.Value)))
                            Log.Info($"{Citizen.CitizenName} leaves the {Utils.GetPrefabName(candidate.Value)} at ({at.x:0}, {at.z:0}) standing: it is by a building");
                        continue;
                    }
                }
                Vector3? fall = Chopper.PlanFall(candidate.Value, candidate.Value.transform.position - Citizen.transform.position);
                if (fall.HasValue)
                {
                    fallDir = fall.Value;
                    return candidate.Value;
                }
                Log.Info($"{Citizen.CitizenName} leaves {Utils.GetPrefabName(candidate.Value)} standing: it would fall on a building.");
                _blacklist.Add(IdOf(candidate.Value), now);
            }
            return null;
        }

        private readonly HashSet<string> _toldByBuilding = new HashSet<string>();
        private static readonly List<Piece> s_nearPieces = new List<Piece>();

        /// <summary>
        /// Where the player's building pieces stand round his area: a piece a player built (structures, not plants), as its
        /// box on the ground (0.26: by its middle, a tree by the end of a long wall was felled).
        /// </summary>
        private static List<GroundBox> BuildingPieces(Vector3 centre, float radius)
        {
            s_nearPieces.Clear();
            Piece.GetAllPiecesInRadius(centre, radius, s_nearPieces);
            var boxes = new List<GroundBox>();
            foreach (Piece piece in s_nearPieces)
                if (piece != null && TreeRules.CountsAsBuilding(piece.IsPlacedByPlayer(), piece.GetComponent<WearNTear>() != null,
                        piece.GetComponent<Colony.FieldMarker>() != null))
                    boxes.Add(BoxOf(piece));
            return boxes;
        }

        /// <summary>A log or stump of the tree he felled last (finished even at Stop at, review of 0.26).</summary>
        private bool IsLeftover(GameObject target) =>
            target != null && ChopArea.IsLeftover(TreeCatalogBuilder.Catalog.KindOf(Utils.GetPrefabName(target)) == ChopKind.Tree, _haveFelled,
                Vector3.Distance(target.transform.position, _felledSpot));

        private static readonly List<Collider> s_colliders = new List<Collider>();

        /// <summary>How far a piece reaches past its middle at most (a 4 m beam): pieces are found by their middle.</summary>
        private const float LongPieceReach = 4f;

        /// <summary>A piece's box on the ground: its solid colliders' bounds; its middle when it has none.</summary>
        private static GroundBox BoxOf(Piece piece)
        {
            piece.GetComponentsInChildren(false, s_colliders);
            Bounds? all = null;
            foreach (Collider c in s_colliders)
            {
                if (c == null || !c.enabled || c.isTrigger) continue;
                if (all.HasValue)
                {
                    Bounds grown = all.Value;
                    grown.Encapsulate(c.bounds);
                    all = grown;
                }
                else all = c.bounds;
            }
            if (!all.HasValue) return GroundBox.At(Ground(piece.transform.position));
            Bounds b = all.Value;
            b.Encapsulate(piece.transform.position);   // never less than before 0.26 (by its middle)
            return new GroundBox(b.min.x, b.min.z, b.max.x, b.max.z);
        }

        /// <summary>Self-test: what he is chopping (a tree, log or stump), or null.</summary>
        internal GameObject TargetForTest => _chopper.Target;

        /// <summary>Self-test: whether the Trees list or a building by it keeps this tree standing (as his search does).</summary>
        internal bool LeavesStandingForTest(GameObject tree)
        {
            WorkerModule worker = _hut != null ? _hut.GetComponent<WorkerModule>() : null;
            float keepOff = ColonyConfig.LeaveTreesNearBuildings.Value;
            Vector3 pos = tree.transform.position;
            return !TreeChoice.MayFell(Utils.GetPrefabName(tree), worker != null ? worker.Unfelled : new HashSet<string>())
                   || TreeRules.NearBuilding(Ground(pos), keepOff > 0f ? BuildingPieces(pos, keepOff + LongPieceReach) : null, keepOff);
        }

        private ItemDrop FindDrop(Vector3 hutPos, float radius, double now)
        {
            TreeCatalog catalog = TreeCatalogBuilder.Catalog;
            Vector3 me = Citizen.transform.position;
            Vector3 center = _haveChopSpot ? _lastChopSpot : me;
            ItemDrop best = null;
            float bestDist = float.MaxValue;
            foreach (ItemDrop drop in WorldScan.Drops())
            {
                if (drop == null || drop.IsPiece() || !catalog.IsCollectible(Utils.GetPrefabName(drop.gameObject))) continue;
                Vector3 pos = drop.transform.position;
                if (Vector3.Distance(pos, center) > CollectRadius) continue;
                if (!ChopArea.Counts(isTree: false, Vector3.Distance(pos, hutPos), radius, _haveFelled, Vector3.Distance(pos, _felledSpot))) continue;
                if (_blacklist.IsBlocked(IdOf(drop.gameObject), now)) continue;
                if (s_claims.IsTakenByOther(IdOf(drop.gameObject), Ground(pos), Citizen.Id, now)) continue;
                float dist = Vector3.Distance(me, pos);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = drop;
                }
            }
            return best;
        }

        private void Chop()
        {
            GameObject target = _chopper.Target;
            if (target == null) return;
            if (!s_claims.TryClaim(_targetId, Ground(target.transform.position), Citizen.Id, Time.time))
            {
                ClearTarget();   // another lumberjack has it
                return;
            }
            if (_chopper.Kind == ChopKind.Log && _logWatch.SeesMoving(target))
            {
                _logWatch.AfterBreak(target.transform.position, _chopper.TargetPrefab + " (moving)");
                return;
            }
            switch (_chopper.Step())
            {
                case ChopStep.Unreachable:
                    Log.Info($"{Citizen.CitizenName} can't reach {_chopper.TargetPrefab}; skipping it for 5 minutes.");
                    _blacklist.Add(IdOf(target), Time.time);
                    ClearTarget();
                    break;
                case ChopStep.CantDamage:
                    Log.Info($"{Citizen.CitizenName} can't damage {_chopper.TargetPrefab}; skipping it for 5 minutes.");
                    _blacklist.Add(IdOf(target), Time.time);
                    ClearTarget();
                    break;
            }
        }

        private static GroundPoint Ground(Vector3 v) => new GroundPoint(v.x, v.z);

        private void OnTargetBroken()
        {
            _chopper.OnTargetBroken();   // the swing that broke it, if one was under way
            ChopKind kind = _chopper.Kind;
            Vector3 position = _chopper.TargetPos;
            string prefab = _chopper.TargetPrefab;
            if (kind == ChopKind.Tree) _logWatch.AfterFelling(position, _chopper.FallDir, prefab);
            else if (kind == ChopKind.Log) _logWatch.AfterBreak(position, prefab);
            if (kind == ChopKind.Tree)
            {
                s_claims.ClaimArea(Citizen.Id, Ground(position + _chopper.FallDir * (Chopper.FallLength * 0.4f)), FelledAreaRadius, Time.time);
                _felledSpot = position;
                _haveFelled = true;
            }
            _lastChopSpot = position;
            _haveChopSpot = true;
            if (_replant == ReplantState.On && (kind == ChopKind.Stump || (kind == ChopKind.Tree && !TreeCatalogBuilder.Catalog.HasStump(prefab))))
            {
                // The ticked kinds in turn, each where it grows and while a seed is at hand (one he carries, one in the
                // hut's chest, or the felled tree's own seed, which is about to drop).
                TreeCatalog catalog = TreeCatalogBuilder.Catalog;
                ReplantInfo own;
                bool hasOwn = kind == ChopKind.Stump ? catalog.TryGetReplantForStump(prefab, out own) : catalog.TryGetReplantForTree(prefab, out own);
                string ownSeed = hasOwn ? own.Seed : "";
                ColonyBuilding hut = _hut;
                List<SaplingInfo> ticked = ReplantList.For(hut).Where(l => l.Ticked).Select(l => l.Sapling).ToList();
                SaplingInfo next = ReplantChoice.Next(ticked, ReplantList.BiomeAt(position),
                    seed => seed == ownSeed || Citizen.Carry.Count(seed) > 0 || (hut != null && hut.Storage != null && hut.Storage.HasItem(seed)),
                    ref _plantTurn);
                if (next != null) _plantSpots.Add(new PlantSpot(position, new ReplantInfo(next.Sapling, next.Seed)));
            }
            ClearTarget();
            WorldScan.Invalidate();   // the shared list doesn't have the new log, stump and wood yet
            _lastBreakTime = Time.time;
            _rescanTimer = 0f;   // look for the fallen log, the stump and the drops right away
        }

        private void Collect()
        {
            if (_drop == null) return;
            Vector3 pos = _drop.transform.position;
            s_claims.TryClaim(_dropId, Ground(pos), Citizen.Id, Time.time);
            if (!Ai.GoTo(pos, 1.2f))
            {
                if (_collectStuck.Update(Vector3.Distance(Citizen.transform.position, pos), Time.time))
                {
                    _blacklist.Add(IdOf(_drop.gameObject), Time.time);
                    ForgetDrop();
                    _collectStuck.Reset();
                }
                return;
            }
            _collectStuck.Reset();
            ZNetView nview = _drop.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid())
            {
                ForgetDrop();
                return;
            }
            if (!nview.IsOwner())
            {
                _drop.RequestOwn();   // try again next tick once we own it
                return;
            }
            _drop.Load();   // the previous owner may have merged nearby stacks into this one; read the current size
            Citizen.Carry.Add(Utils.GetPrefabName(_drop.gameObject), _drop.m_itemData.m_stack);
            Citizen.SaveCarry();
            ZNetScene.instance.Destroy(_drop.gameObject);
            ForgetDrop();
            _rescanTimer = 0f;
        }

        private void Plant()
        {
            PlantSpot spot = _plantSpots.FirstOrDefault(p => Citizen.Carry.Count(p.Replant.Seed) > 0);
            if (spot == null) return;
            if (!Ai.GoTo(spot.Position, 1.5f))
            {
                if (_plantStuck.Update(Vector3.Distance(Citizen.transform.position, spot.Position), Time.time))
                {
                    _plantSpots.Remove(spot);   // unreachable; the seed goes back to storage on the next delivery
                    _plantStuck.Reset();
                }
                return;
            }
            _plantStuck.Reset();
            _plantSpots.Remove(spot);

            GameObject sapling = ZNetScene.instance.GetPrefab(spot.Replant.Sapling);
            if (sapling == null || !HasRoomToGrow(sapling, spot.Position))
            {
                Log.Info($"{Citizen.CitizenName} skipped replanting {spot.Replant.Sapling}: no room to grow.");
                return;
            }
            Object.Instantiate(sapling, spot.Position, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            Log.Info($"{Citizen.CitizenName} planted {spot.Replant.Sapling} at ({spot.Position.x:0}, {spot.Position.z:0})");
            Citizen.Carry.Remove(spot.Replant.Seed, 1);
            Citizen.SaveCarry();
        }

        private static bool HasRoomToGrow(GameObject saplingPrefab, Vector3 position)
        {
            Plant plant = saplingPrefab.GetComponent<Plant>();
            float radius = plant != null ? plant.m_growRadius : 1f;
            foreach (Collider collider in Physics.OverlapSphere(position, radius))
            {
                if (collider.GetComponentInParent<TreeBase>() != null || collider.GetComponentInParent<Plant>() != null) return false;
                Destructible destructible = collider.GetComponentInParent<Destructible>();
                if (destructible != null && destructible.GetDestructibleType() == DestructibleType.Tree) return false;
            }
            return true;
        }

        /// <summary>
        /// Walks to the hut. If it can't get closer (behind a door, upstairs, on a platform) it delivers from where it
        /// stopped when that is close, and otherwise says it can't reach the hut instead of standing there silently.
        /// </summary>
        private bool ReachHut(ColonyBuilding hut)
        {
            Vector3 hutPos = hut.transform.position;
            if (Ai.GoTo(hutPos, HallReach))
            {
                _depositStuck.Reset();
                return true;
            }
            if (Ai.UsingDoor)
            {
                _toldFromHere = false;
                _depositStuck.Reset();   // going round to the hut's door is no lack of progress
                return false;
            }
            float distance = Utils.DistanceXZ(Citizen.transform.position, hutPos);
            bool stuck = _depositStuck.Update(distance, Time.time);
            if (MoveRules.DeliverFromHere(stuck, distance))
            {
                if (!_toldFromHere) Log.Info($"{Citizen.CitizenName} can't get closer to the hut and reaches it from {distance:0.0} m away");
                _toldFromHere = true;
                return true;
            }
            _toldFromHere = false;
            if (stuck) Citizen.Status = "$vc_status_cant_reach_hut";
            return false;
        }

        private void Deliver(ColonyBuilding hut, float dt, float retrySeconds)
        {
            Ai.Idle();
            _depositTimer -= dt;
            if (_depositTimer > 0f) return;
            _depositTimer = retrySeconds;
            // Seeds held for pending plant spots stay carried; everything else goes into storage.
            Dictionary<string, int> keep = SeedPlan.Reserved(Citizen.Carry, SpotSeeds());
            StorageModule.DepositResult result = hut.Storage.TryDeposit(Citizen.Carry, keep);
            if (result == StorageModule.DepositResult.Busy) return;   // a player has the storage open
            // Full: the rest goes to the Town Hall or another chest with room; he waits only when every chest is full.
            _storageFull = result == StorageModule.DepositResult.StorageFull && !_fullChest.Choose(Citizen, hut, keep);
            RestockSeeds(hut);
            Citizen.SaveCarry();
            if (!_storageFull) _depositTimer = 0f;
        }

        /// <summary>Fetches a seed from storage for every pending spot that lacks one; forgets spots with no seed anywhere.</summary>
        private void RestockSeeds(ColonyBuilding hut)
        {
            foreach (KeyValuePair<string, int> missing in SeedPlan.Missing(Citizen.Carry, SpotSeeds()))
            {
                int fetched = 0;
                while (fetched < missing.Value && hut.Storage.TryTakeItem(missing.Key, Citizen.Carry, ownUse: true)) fetched++;
                int unserved = missing.Value - fetched;
                for (int i = _plantSpots.Count - 1; i >= 0 && unserved > 0; i--)
                {
                    if (_plantSpots[i].Replant.Seed != missing.Key) continue;
                    _plantSpots.RemoveAt(i);
                    unserved--;
                }
            }
        }

        private IEnumerable<string> SpotSeeds() => _plantSpots.Select(p => p.Replant.Seed);

        /// <summary>His work area around the hut: Lumberjack › WorkRadius plus RadiusPerLevel per level above 1.</summary>
        public static float WorkRadiusOf(ColonyBuilding hut) =>
            HutLevels.WorkRadius(ColonyConfig.WorkRadius.Value, ColonyConfig.RadiusPerLevel.Value, hut != null ? hut.Level : 1);

        public override void Stop()
        {
            SwingLog.EndSwing(Citizen);   // a swing under way mustn't land a stand-in hit for a job he no longer has
            BorrowAxe("");   // the copy goes back; the axe never left the chest
        }

        public override bool FinishingBeforeNight(float dt)
        {
            ColonyBuilding hut = _hut;
            if (_fullChest.Tick(Citizen, Ai, hut, SeedPlan.Reserved(Citizen.Carry, SpotSeeds()))) return true;
            if (hut == null || hut.Storage == null || _storageFull) return false;
            int load = Citizen.Carry.Total - SeedPlan.Total(SeedPlan.Reserved(Citizen.Carry, SpotSeeds()));
            if (load <= 0) return false;
            Citizen.Status = "$vc_status_delivering";
            if (ReachHut(hut)) Deliver(hut, dt, DepositRetrySeconds);
            return true;
        }

        /// <summary>
        /// He holds the best axe his hut's level allows that the colony's chests hold — a copy, the axe stays in the
        /// chest (as the builder's tools) — and his own flint axe otherwise.
        /// </summary>
        private void EnsureAxeEquipped(ColonyBuilding hut, float dt)
        {
            _axeCheck -= dt;
            if (_axeCheck <= 0f)
            {
                _axeCheck = AxeCheckSeconds;
                int cap = HutLevels.ToolTierCap(ToolKind.Axe, hut != null ? hut.Level : 1);
                ToolInfo best = BuilderTools.FindIn(BuilderTools.ColonyStores(Citizen.ColonyId, hut, Citizen.transform.position), ToolKind.Axe, 0, cap);
                string wanted = best != null && best.Tier > StartingAxeTier() ? best.Prefab : "";
                if (wanted != _borrowedAxePrefab) BorrowAxe(wanted);
            }
            Humanoid body = Citizen.Body;
            Inventory inventory = body.GetInventory();
            ItemDrop.ItemData axe = _borrowedAxe ?? inventory.GetAllItems().FirstOrDefault(i => i.m_shared.m_skillType == Skills.SkillType.Axes);
            if (axe == null)
            {
                GameObject prefab = ObjectDB.instance.GetItemPrefab(CitizenPrefab.StartingAxe);
                if (prefab != null) inventory.AddItem(prefab, 1);
                return;
            }
            if (body.GetCurrentWeapon() != axe && !SettlerPose.IsHeld(body)) body.EquipItem(axe, false);   // put away while he sits
        }

        private void BorrowAxe(string prefabName)
        {
            Humanoid body = Citizen != null ? Citizen.Body : null;
            if (body == null)   // the settler is being removed: nothing to hand back
            {
                _borrowedAxe = null;
                _borrowedAxePrefab = "";
                return;
            }
            if (_borrowedAxe != null)
            {
                if (body.IsItemEquiped(_borrowedAxe)) body.UnequipItem(_borrowedAxe, false);
                body.GetInventory().RemoveItem(_borrowedAxe);
                _borrowedAxe = null;
            }
            _borrowedAxePrefab = "";
            GameObject prefab = prefabName.Length > 0 ? ObjectDB.instance.GetItemPrefab(prefabName) : null;
            if (prefab == null || !body.GetInventory().AddItem(prefab, 1)) return;
            _borrowedAxe = body.GetInventory().GetAllItems().LastOrDefault(i => i.m_dropPrefab != null && i.m_dropPrefab.name == prefabName);
            if (_borrowedAxe == null) return;
            _borrowedAxePrefab = prefabName;
            Log.Info($"{Citizen.CitizenName} works with a copy of the colony's {prefabName} (it stays in the chest)");
        }

        private static int StartingAxeTier()
        {
            GameObject prefab = ObjectDB.instance.GetItemPrefab(CitizenPrefab.StartingAxe);
            return prefab != null ? prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_toolTier : 0;
        }

        private int AxeTier() => LumberjackBrain.AxeTier(_borrowedAxe?.m_shared.m_toolTier,
            Citizen.Body.GetInventory().GetAllItems().FirstOrDefault(i => i.m_shared.m_skillType == Skills.SkillType.Axes)?.m_shared.m_toolTier);

        private void SetTarget(GameObject target, Vector3 fallDir)
        {
            if (!string.IsNullOrEmpty(_targetId)) s_claims.Release(_targetId, Citizen.Id);
            _targetId = target != null ? IdOf(target) : "";
            if (target != null && !s_claims.TryClaim(_targetId, Ground(target.transform.position), Citizen.Id, Time.time))
            {
                target = null;   // another lumberjack got there first
                _targetId = "";
            }
            _chopper.SetTarget(target, fallDir);
        }

        private void ClearTarget() => SetTarget(null, Vector3.zero);

        private void ForgetDrop()
        {
            if (!string.IsNullOrEmpty(_dropId)) s_claims.Release(_dropId, Citizen.Id);
            _drop = null;
            _dropId = null;
        }

        private static string IdOf(GameObject go)
        {
            ZNetView nview = go != null ? go.GetComponent<ZNetView>() : null;
            if (nview != null && nview.IsValid()) return nview.GetZDO().m_uid.ToString();
            return go != null ? go.GetInstanceID().ToString() : "";
        }
    }
}
