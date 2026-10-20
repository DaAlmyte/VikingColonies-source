using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using VikingColonies.AI;
using VikingColonies.Buildings;
using VikingColonies.Citizens;
using VikingColonies.Config;
using VikingColonies.Core;

namespace VikingColonies.Jobs
{
    /// <summary>
    /// The forager: picks what grows wild around his hut, the kinds ticked on its "What to pick" list (as a player does:
    /// it pops out of the bush), gathers what dropped and carries it home. Every tick: look around (every few seconds), ask <see cref="ForagerBrain"/>
    /// what to do, then do one small piece of it.
    /// </summary>
    public class ForagerJob : Job
    {
        private const float RescanSeconds = 3f;
        private const float IdleRescanSeconds = 10f;
        private const float HutReach = 2.5f;
        private const float DepositRetrySeconds = 2f;
        private const float StorageFullRetrySeconds = 10f;
        /// <summary>How long picking a bush takes him (twice as long hungry, at half speed).</summary>
        private const float PickSeconds = 1f;
        /// <summary>A bush he picked isn't his again for this long (a pick lost on the way to its computer isn't repeated at once).</summary>
        private const float PickedBushSeconds = 30f;
        private const float DropRescanSeconds = 0.5f;

        // Shared by every forager this computer runs: who goes for which bush and which item on the ground.
        private static readonly ClaimBoard s_claims = new ClaimBoard();
        private static readonly AccessTools.FieldRef<Pickable, ZNetView> s_pickableView = AccessTools.FieldRefAccess<Pickable, ZNetView>("m_nview");

        private readonly ColonyBuilding _hut;
        private readonly TargetBlacklist _blacklist = new TargetBlacklist(300);
        private readonly TargetBlacklist _picked = new TargetBlacklist(PickedBushSeconds);
        private readonly HashSet<string> _dropsBefore = new HashSet<string>();
        private readonly StuckDetector _pickStuck = new StuckDetector();
        private readonly StuckDetector _gatherStuck = new StuckDetector();
        private readonly StuckDetector _depositStuck = new StuckDetector();
        private readonly FullChestRun _fullChest = new FullChestRun();   // his chest full: the load goes elsewhere (0.21)
        private Pickable _target;
        private string _targetId = "";
        private ItemDrop _drop;
        private string _dropId = "";
        private Vector3 _pickedAt;
        private float _pickedTime = float.NegativeInfinity;
        private string _pickedItem = "";
        private bool _awaitingDrop;
        private float _pickTimer;
        private float _rescanTimer;
        private float _depositTimer;
        private bool _storageFull;
        private ForagerAction? _lastAction;

        public ForagerJob(Citizen citizen, CitizenAI ai, ColonyBuilding hut) : base(citizen, ai)
        {
            _hut = hut;
        }

        /// <summary>His work area around the hut: Forager › WorkRadius plus RadiusPerLevel per level above 1.</summary>
        public static float WorkRadiusOf(ColonyBuilding hut) =>
            HutLevels.WorkRadius(ColonyConfig.ForagerWorkRadius.Value, ColonyConfig.ForagerRadiusPerLevel.Value, hut != null ? hut.Level : 1);

        public override void Tick(float dt)
        {
            ColonyBuilding hut = _hut;
            if (Citizen.Carry.Total == 0) _storageFull = false;
            if (_fullChest.Tick(Citizen, Ai, hut)) return;
            if (!ReferenceEquals(_target, null) && _target == null) ForgetTarget();   // the bush went (unloaded, destroyed)
            if (!ReferenceEquals(_drop, null) && _drop == null) ForgetDrop();

            _rescanTimer -= dt;
            if (_rescanTimer <= 0f) Rescan(hut);

            ForagerAction action = ForagerBrain.Decide(new ForagerSituation
            {
                HasHut = hut != null && hut.Storage != null,
                IsNight = EnvMan.IsNight(),
                CarryTotal = Citizen.Carry.Total,
                CarryCapacity = ColonyConfig.ForagerCarryCapacity.Value,
                StorageFull = _storageFull,
                DropsNearby = _drop != null,
                JustPicked = _awaitingDrop && Time.time - _pickedTime < Foraging.DropWaitSeconds,
                HasPickTarget = _target != null,
            });
            if (action != _lastAction)   // one log line per step change
            {
                Log.Info($"{Citizen.CitizenName}: {_lastAction?.ToString() ?? "start"} -> {action} (carrying {Citizen.Carry.Total})");
                _lastAction = action;
            }

            switch (action)
            {
                case ForagerAction.Unemployed:
                    Citizen.Status = "$vc_status_unemployed";
                    Ai.Idle();
                    break;
                case ForagerAction.Rest:
                    Citizen.Status = "$vc_status_resting";
                    if (Ai.GoTo(hut.transform.position, 4f)) Ai.Idle();
                    break;
                case ForagerAction.Deposit:
                    Citizen.Status = "$vc_status_delivering";
                    if (ReachHut(hut)) Deliver(hut, dt, DepositRetrySeconds);
                    break;
                case ForagerAction.WaitStorageFull:
                    Citizen.Status = "$vc_status_storage_full";
                    if (ReachHut(hut)) Deliver(hut, dt, StorageFullRetrySeconds);
                    break;
                case ForagerAction.Gather:
                    Citizen.Status = "$vc_status_picking";
                    Gather();
                    break;
                case ForagerAction.Pick:
                    Citizen.Status = "$vc_status_picking";
                    Pick();
                    break;
                case ForagerAction.WaitNothingToPick:
                    Citizen.Status = "$vc_status_nothing_to_pick";
                    Ai.FreeTime(hut.transform.position);
                    break;
            }
        }

        public override void Resume()
        {
            _pickStuck.Reset();
            _gatherStuck.Reset();
            _depositStuck.Reset();
            _rescanTimer = 0f;
        }

        public override bool FinishingBeforeNight(float dt)
        {
            ColonyBuilding hut = _hut;
            if (_fullChest.Tick(Citizen, Ai, hut)) return true;
            if (hut == null || hut.Storage == null || _storageFull || Citizen.Carry.Total <= 0) return false;
            Citizen.Status = "$vc_status_delivering";
            if (ReachHut(hut)) Deliver(hut, dt, DepositRetrySeconds);
            return true;
        }

        public override void Stop()
        {
            ForgetTarget();
            ForgetDrop();
        }

        private void Rescan(ColonyBuilding hut)
        {
            _rescanTimer = RescanSeconds;
            if (hut == null)
            {
                ForgetTarget();
                ForgetDrop();
                return;
            }
            double now = Time.time;
            if (_drop == null && Time.time - _pickedTime < Foraging.GatherSeconds) FindDrop(now);
            if (_target == null)
            {
                long timer = Perf.Start();
                FindTarget(hut.transform.position, WorkRadiusOf(hut), now);
                Perf.Stop("forager: choosing a plant", timer);
            }
            // Waiting for what he picked to drop (from another computer it comes later): he looks again soon.
            if (_drop == null && _awaitingDrop && Time.time - _pickedTime < Foraging.DropWaitSeconds) _rescanTimer = DropRescanSeconds;
            else if (_target == null && _drop == null) _rescanTimer = IdleRescanSeconds;
        }

        /// <summary>The nearest wild kind on his hut's list in his area that's ready and no other forager is going for.</summary>
        private void FindTarget(Vector3 hutPos, float radius, double now)
        {
            Vector3 me = Citizen.transform.position;
            Pickable best = null;
            float bestDistance = float.MaxValue;
            HashSet<string> unticked = ForageList.Unticked(_hut);
            foreach (Pickable bush in WorldScan.Pickables())
            {
                if (bush == null) continue;
                Vector3 pos = bush.transform.position;
                // Cheap checks first, then only for one nearer than the best so far (vc_perf, 0.10: a meadow full of
                // dandelions made each look around cost milliseconds).
                if (Utils.DistanceXZ(pos, hutPos) > radius) continue;
                float distance = Vector3.Distance(me, pos);
                if (distance >= bestDistance || !IsHis(bush, unticked)) continue;
                string id = IdOf(bush.gameObject);
                if (_blacklist.IsBlocked(id, now) || _picked.IsBlocked(id, now) || s_claims.IsTakenByOther(id, Ground(pos), Citizen.Id, now)) continue;
                best = bush;
                bestDistance = distance;
            }
            if (best == null) return;
            _target = best;
            _targetId = IdOf(best.gameObject);
            if (!s_claims.TryClaim(_targetId, Ground(best.transform.position), Citizen.Id, now)) ForgetTarget();
        }

        /// <summary>
        /// Wild (it grows back, no player planted it), a kind ticked on his hut's list (<paramref name="unticked"/> are the
        /// hut's unticked kinds) and ready: see <see cref="Foraging.IsHis"/>. Cheap checks first.
        /// </summary>
        internal static bool IsHis(Pickable bush, ICollection<string> unticked)
        {
            if (bush == null || bush.m_itemPrefab == null || bush.m_respawnTimeMinutes <= 0f || !bush.CanBePicked()) return false;
            if (!ForageList.Wants(bush.m_itemPrefab.name, unticked)) return false;   // an item prefab is named as the game knows it
            ZNetView nview = s_pickableView(bush);
            if (nview == null || !nview.IsValid()) return false;   // a placement ghost, not a bush
            return Foraging.IsHis(growsBack: true, wanted: true, ready: true, planted: Planted(bush));
        }

        /// <summary>A player's: placed by a player (a planting mod's bush), or growing on cultivated ground.</summary>
        private static bool Planted(Pickable bush)
        {
            Piece piece = bush.GetComponent<Piece>();
            if (piece != null && piece.GetCreator() != 0L) return true;
            Vector3 pos = bush.transform.position;
            Heightmap ground = Heightmap.FindHeightmap(pos);
            return ground != null && ground.IsCultivated(pos);
        }

        /// <summary>What popped out of the bush he just picked: its food, lying by it, that wasn't there before he picked.</summary>
        private void FindDrop(double now)
        {
            ItemDrop best = null;
            float bestDistance = float.MaxValue;
            foreach (ItemDrop drop in WorldScan.Drops())
            {
                if (drop == null || drop.IsPiece()) continue;
                Vector3 pos = drop.transform.position;
                if (Vector3.Distance(pos, _pickedAt) > Foraging.GatherRadius) continue;
                if (Utils.GetPrefabName(drop.gameObject) != _pickedItem) continue;   // a player's food lying there stays his
                string id = IdOf(drop.gameObject);
                if (_dropsBefore.Contains(id) || _blacklist.IsBlocked(id, now) || s_claims.IsTakenByOther(id, Ground(pos), Citizen.Id, now)) continue;
                float distance = Vector3.Distance(Citizen.transform.position, pos);
                if (distance >= bestDistance) continue;
                best = drop;
                bestDistance = distance;
            }
            if (best == null) return;
            _awaitingDrop = false;
            _drop = best;
            _dropId = IdOf(best.gameObject);
            if (!s_claims.TryClaim(_dropId, Ground(best.transform.position), Citizen.Id, now)) ForgetDrop();
        }

        private void Pick()
        {
            if (_target == null) return;
            if (!_target.CanBePicked())   // someone picked it first
            {
                ForgetTarget();
                _rescanTimer = 0f;
                return;
            }
            Vector3 pos = _target.transform.position;
            s_claims.TryClaim(_targetId, Ground(pos), Citizen.Id, Time.time);
            if (!Ai.GoTo(pos, Foraging.Reach))
            {
                _pickTimer = 0f;
                if (_pickStuck.Update(Vector3.Distance(Citizen.transform.position, pos), Time.time))
                {
                    Log.Info($"{Citizen.CitizenName} can't reach the {Utils.GetPrefabName(_target.gameObject)} at ({pos.x:0}, {pos.z:0}); he leaves it for now");
                    _blacklist.Add(_targetId, Time.time);
                    ForgetTarget();
                    _pickStuck.Reset();
                }
                return;
            }
            _pickStuck.Reset();
            Ai.Face(pos);
            _pickTimer += Time.deltaTime;
            if (_pickTimer < PickSeconds / Citizen.SkillSpeed(WorkSkill.Farming)) return;   // slower when hungry, faster with Farming (0.22)
            _pickTimer = 0f;
            if (!IsHis(_target, ForageList.Unticked(_hut)))   // unticked meanwhile, or picked by someone
            {
                ForgetTarget();
                return;
            }
            ZNetView nview = s_pickableView(_target);
            if (nview == null || !nview.IsValid())
            {
                ForgetTarget();
                return;
            }
            // As a player picks: the bush's computer drops the food next to it and marks the bush picked.
            _pickedItem = _target.m_itemPrefab.name;
            _dropsBefore.Clear();
            foreach (ItemDrop lying in WorldScan.Drops())
                if (lying != null && Vector3.Distance(lying.transform.position, pos) <= Foraging.GatherRadius) _dropsBefore.Add(IdOf(lying.gameObject));
            nview.InvokeRPC("RPC_Pick", 0);
            Citizen.Practise(WorkSkill.Farming);   // for a pick that happened (0.26, review of 0.22)
            _picked.Add(_targetId, Time.time);
            _awaitingDrop = true;
            Log.Info($"{Citizen.CitizenName} picks the {Utils.GetPrefabName(_target.gameObject)} at ({pos.x:0}, {pos.z:0})");
            _pickedAt = pos;
            _pickedTime = Time.time;
            ForgetTarget();
            WorldScan.InvalidateDrops();   // the shared list doesn't have the dropped food yet
            _rescanTimer = DropRescanSeconds;   // the food appears a moment later
        }

        private void Gather()
        {
            if (_drop == null)
            {
                Ai.Idle();   // the food he just picked is about to drop
                return;
            }
            Vector3 pos = _drop.transform.position;
            s_claims.TryClaim(_dropId, Ground(pos), Citizen.Id, Time.time);
            if (!Ai.GoTo(pos, 1.2f))
            {
                if (_gatherStuck.Update(Vector3.Distance(Citizen.transform.position, pos), Time.time))
                {
                    _blacklist.Add(_dropId, Time.time);
                    ForgetDrop();
                    _gatherStuck.Reset();
                }
                return;
            }
            _gatherStuck.Reset();
            ZNetView nview = _drop.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid())
            {
                ForgetDrop();
                return;
            }
            if (!nview.IsOwner())
            {
                _drop.RequestOwn();   // again next tick, once it's ours
                return;
            }
            _drop.Load();   // nearby stacks may have merged into it: the current size
            Citizen.Carry.Add(Utils.GetPrefabName(_drop.gameObject), _drop.m_itemData.m_stack);
            Citizen.SaveCarry();
            ZNetScene.instance.Destroy(_drop.gameObject);
            ForgetDrop();
            _rescanTimer = 0f;   // more may lie there
        }

        /// <summary>Walks to the hut; one that can't be reached fully is delivered to from close by (as the lumberjack does).</summary>
        private bool ReachHut(ColonyBuilding hut)
        {
            Vector3 hutPos = hut.transform.position;
            if (Ai.GoTo(hutPos, HutReach))
            {
                _depositStuck.Reset();
                return true;
            }
            if (Ai.UsingDoor)
            {
                _depositStuck.Reset();
                return false;
            }
            float distance = Utils.DistanceXZ(Citizen.transform.position, hutPos);
            bool stuck = _depositStuck.Update(distance, Time.time);
            if (MoveRules.DeliverFromHere(stuck, distance)) return true;
            if (stuck) Citizen.Status = "$vc_status_cant_reach_hut";
            return false;
        }

        private void Deliver(ColonyBuilding hut, float dt, float retrySeconds)
        {
            Ai.Idle();
            _depositTimer -= dt;
            if (_depositTimer > 0f) return;
            _depositTimer = retrySeconds;
            StorageModule.DepositResult result = hut.Storage.TryDeposit(Citizen.Carry);
            if (result == StorageModule.DepositResult.Busy) return;   // a player has the chest open
            // Full: the rest goes to the Town Hall or another chest with room; he waits only when every chest is full.
            _storageFull = result == StorageModule.DepositResult.StorageFull && !_fullChest.Choose(Citizen, hut);
            Citizen.SaveCarry();
            if (!_storageFull) _depositTimer = 0f;
        }

        private void ForgetTarget()
        {
            if (_targetId.Length > 0) s_claims.Release(_targetId, Citizen.Id);
            _target = null;
            _targetId = "";
        }

        private void ForgetDrop()
        {
            if (_dropId.Length > 0) s_claims.Release(_dropId, Citizen.Id);
            _drop = null;
            _dropId = "";
        }

        private static GroundPoint Ground(Vector3 v) => new GroundPoint(v.x, v.z);

        private static string IdOf(GameObject go)
        {
            ZNetView nview = go != null ? go.GetComponent<ZNetView>() : null;
            if (nview != null && nview.IsValid()) return nview.GetZDO().m_uid.ToString();
            return go != null ? go.GetInstanceID().ToString() : "";
        }
    }
}
