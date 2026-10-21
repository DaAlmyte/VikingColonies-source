using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VikingColonies.AI;
using VikingColonies.Buildings;
using VikingColonies.Citizens;
using VikingColonies.Construction;
using VikingColonies.Core;

namespace VikingColonies.Jobs
{
    public enum ChopStep { Idle, Walking, Swinging, Unreachable, CantDamage }

    /// <summary>
    /// Chopping one tree, log or stump — shared by the lumberjack and the builder: where to stand (beside a log on its
    /// uphill side, opposite a tree's planned fall, by a stump), facing and swinging the axe, and making sure the swings
    /// land. <see cref="PlanFall"/> finds the felling direction that misses buildings.
    /// </summary>
    internal sealed class Chopper
    {
        public const float FallLength = 20f;      // how far a felled trunk can reach
        public const float FallClearance = 3f;   // how far buildings must stay from it
        private const float SiteGridSpacing = 2f;   // unfinished sites, as points this far apart (the clearance covers the gaps)

        // Set once his real axe swings turn out not to hurt his targets (spec fallback); his own, not every lumberjack's.
        private bool _directDamage;
        private static readonly List<Piece> s_pieces = new List<Piece>();

        private readonly Citizen _citizen;
        private readonly CitizenAI _ai;
        private readonly System.Action _onHit;
        private readonly bool _alwaysDirect;
        private readonly StuckDetector _stuck = new StuckDetector();
        private readonly SwingTracker _swings = new SwingTracker();
        private GameObject _target;
        private Vector3 _fallDir;
        private string _targetPrefab;
        private ChopKind _targetKind;
        private Vector3 _targetPos;
        private Vector3 _standSpot;
        private GameObject _toldStanceFor;   // the tree whose felling stance the log already has
        private Vector3 _aimPoint;
        private Vector3 _stanceFor;   // where the target was when the stance was worked out
        private bool _haveStance;
        private Vector3 _axisA;
        private Vector3 _axisB;
        private float _targetRadius;
        private float _reach;
        private bool _wasInAttack;
        private float _healthBeforeSwing;
        private bool _directHitThisSwing;   // the stand-in already hit at the moment the axe landed

        /// <param name="onHit">Called when a swing hits the target (the lumberjack practises woodcutting).</param>
        /// <param name="alwaysDirect">The settler's swings hit nothing themselves (the builder's): every swing hits the target as the axe lands.</param>
        public Chopper(Citizen citizen, CitizenAI ai, System.Action onHit, bool alwaysDirect)
        {
            _citizen = citizen;
            _ai = ai;
            _onHit = onHit;
            _alwaysDirect = alwaysDirect;
        }

        /// <summary>The tree, log or stump being chopped. Once it broke, it is == null but not ReferenceEquals null.</summary>
        public GameObject Target => _target;
        public string TargetPrefab => _targetPrefab;
        public ChopKind Kind => _targetKind;
        public Vector3 TargetPos => _targetPos;
        public Vector3 FallDir => _fallDir;

        public void SetTarget(GameObject target, Vector3 fallDir)
        {
            _target = target;
            _fallDir = fallDir;
            _stuck.Reset();
            _swings.Reset();
            _wasInAttack = false;
            _haveStance = false;
            if (target == null) return;
            _targetPrefab = Utils.GetPrefabName(target);
            _targetKind = TreeCatalogBuilder.Catalog.KindOf(_targetPrefab);
            if (_targetKind == ChopKind.None)   // a kind the catalog hasn't met (the builder takes whatever stands on his site)
                _targetKind = target.GetComponent<TreeBase>() != null ? ChopKind.Tree : target.GetComponent<TreeLog>() != null ? ChopKind.Log : ChopKind.Stump;
            _targetPos = target.transform.position;
        }

        /// <summary>After the job was interrupted: an interrupted swing isn't a swing that did no damage.</summary>
        public void Resume()
        {
            _stuck.Reset();
            _wasInAttack = false;
            _directHitThisSwing = false;
        }

        /// <summary>One tick of work: walk to where he chops from, then face the target and swing.</summary>
        public ChopStep Step()
        {
            if (_target == null) return ChopStep.Idle;
            Vector3 targetPos = _target.transform.position;
            if (!_haveStance || Vector3.Distance(targetPos, _stanceFor) > 1f) WorkOutStance(targetPos);

            Vector3 me = _citizen.transform.position;
            float toSurface = LogStance.DistanceToSurface(Ground(_axisA), Ground(_axisB), _targetRadius, Ground(me));
            if (!LogStance.InPlace(toSurface, _reach))
            {
                float toSpot = Utils.DistanceXZ(me, _standSpot);
                _ai.GoTo(_standSpot, MoveRules.MinStopDistance);
                return _stuck.Update(toSpot, Time.time) ? ChopStep.Unreachable : ChopStep.Walking;
            }

            _stuck.Reset();
            _ai.Idle();
            _ai.Face(_aimPoint);

            Humanoid body = _citizen.Body;
            bool inAttack = body.InAttack();
            bool swingEnded = _wasInAttack && !inAttack;
            _wasInAttack = inAttack;
            if (swingEnded && OnSwingFinished()) return ChopStep.CantDamage;
            if (!inAttack && _target != null && _ai.IsFacing(_aimPoint))
            {
                _healthBeforeSwing = ReadHealth(_target);
                if (body.StartAttack(null, false))
                {
                    _directHitThisSwing = false;
                    SwingLog.BeginSwing(_citizen, OnAxeLands, _target);
                }
            }
            return ChopStep.Swinging;
        }

        /// <summary>The target broke: ends the log of the swing that broke it.</summary>
        public void OnTargetBroken() => FinishSwingLog(true);

        /// <summary>
        /// Direction to fell this tree so the trunk misses players' buildings, every colony building and every unfinished
        /// building site, as close to <paramref name="preferred"/> as possible; null when every way would hit one.
        /// </summary>
        /// <param name="ignore">A hut whose building and site don't count (the builder clearing his own site: trees can't hurt its chest).</param>
        /// <param name="comingDown">Pieces the tree may fall on: a site's own old pieces its upgrade takes down anyway.</param>
        public static Vector3? PlanFall(GameObject tree, Vector3 preferred, GameObject ignore = null, ICollection<GameObject> comingDown = null)
        {
            long timer = Perf.Start();
            try { return PlanFallTimed(tree, preferred, ignore, comingDown); }
            finally { Perf.Stop("planning a tree's fall", timer); }
        }

        private static Vector3? PlanFallTimed(GameObject tree, Vector3 preferred, GameObject ignore, ICollection<GameObject> comingDown)
        {
            Vector3 pos = tree.transform.position;
            s_pieces.Clear();
            Piece.GetAllPiecesInRadius(pos, FallLength + FallClearance, s_pieces);
            List<GroundPoint> buildings = s_pieces
                .Where(p => p != null && p.gameObject != ignore
                            && FallPlanner.MustMiss(p.IsPlacedByPlayer(), comingDown != null && comingDown.Contains(p.gameObject)))
                .Select(p => new GroundPoint(p.transform.position.x, p.transform.position.z))
                .Concat(ColonyBuilding.All
                    .Where(c => c != null && c.gameObject != ignore && Vector3.Distance(c.transform.position, pos) <= FallLength + FallClearance)
                    .Select(c => new GroundPoint(c.transform.position.x, c.transform.position.z)))
                .Concat(UnfinishedSites(pos, ignore))
                .ToList();
            GroundPoint? dir = FallPlanner.ChooseDirection(new GroundPoint(pos.x, pos.z), buildings,
                new GroundPoint(preferred.x, preferred.z), FallLength, FallClearance);
            return dir.HasValue ? new Vector3(dir.Value.X, 0f, dir.Value.Z) : (Vector3?)null;
        }

        /// <summary>The ground of unfinished building sites near the tree (except the ignored hut's), as points a trunk must keep off.</summary>
        private static IEnumerable<GroundPoint> UnfinishedSites(Vector3 tree, GameObject ignore)
        {
            foreach (ConstructionModule site in ConstructionModule.All)
            {
                if (site == null || !site.IsValid || site.gameObject == ignore) continue;
                BuildOrderRecord order = site.Order;
                if (order == null || order.State == BuildState.Done) continue;
                Footprint footprint = site.SiteFootprint;
                if (footprint == null) continue;
                float reach = FallLength + FallClearance + Mathf.Max(footprint.HalfX, footprint.HalfZ) * 1.5f;
                if (Vector3.Distance(site.transform.position, tree) > reach) continue;
                foreach (Vec3 point in footprint.GridPoints(SiteGridSpacing)) yield return new GroundPoint(point.X, point.Z);
            }
        }

        /// <summary>
        /// Where to stand and where to aim. Logs: beside the log on the uphill side, aiming at the nearest part of it.
        /// Trees: opposite the planned fall. Stumps: on the near side. Always within the axe's reach.
        /// </summary>
        private void WorkOutStance(Vector3 targetPos)
        {
            _haveStance = true;
            _stanceFor = targetPos;
            _reach = AxeReach();
            float standOff = LogStance.StandOff(_reach);
            Vector3 me = _citizen.transform.position;

            if (_targetKind == ChopKind.Log)
            {
                if (!ChopShape.TryGet(_target, out _axisA, out _axisB, out _targetRadius))
                {
                    _axisA = _axisB = targetPos;
                    _targetRadius = 0f;
                }
                GroundPoint a = Ground(_axisA), b = Ground(_axisB), settler = Ground(me);
                LogStance.Candidates(a, b, _targetRadius, settler, standOff, out GroundPoint first, out GroundPoint second);
                GroundPoint spot = LogStance.Choose(first, GroundHeight(first), second, GroundHeight(second), settler);
                _standSpot = OnGround(spot);
                _aimPoint = Vector3.Lerp(_axisA, _axisB, LogStance.AxisParameter(a, b, spot)) + Vector3.up * _targetRadius;   // the log's top
                return;
            }

            // Trees and stumps: the trunk stands at the object's position; measure its thickness where the axe lands.
            Vector3 trunk = targetPos;
            float aimHeight = _targetKind == ChopKind.Tree ? 1f : 0.3f;
            Vector3 away = _targetKind == ChopKind.Tree && _fallDir != Vector3.zero ? -_fallDir : me - trunk;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = Vector3.forward;
            _targetRadius = ChopShape.TrunkRadius(_target, trunk, trunk + away, aimHeight);
            _axisA = _axisB = trunk;
            _standSpot = OnGround(Ground(trunk + away.normalized * (_targetRadius + standOff)));
            _aimPoint = trunk + Vector3.up * aimHeight;
            if (_targetKind == ChopKind.Tree && _fallDir != Vector3.zero && !ReferenceEquals(_toldStanceFor, _target))
            {
                // A tree falls away from the axe: he stands on its far side from where it falls (180 degrees from the fall).
                Vector3 fromTrunk = _standSpot - trunk;
                fromTrunk.y = 0f;
                Log.Info($"{_citizen.CitizenName} fells {_targetPrefab}: it is to fall towards ({_fallDir.x:0.0}, {_fallDir.z:0.0}); " +
                         $"they stand {Vector3.Angle(fromTrunk, new Vector3(_fallDir.x, 0f, _fallDir.z)):0} degrees from that, {fromTrunk.magnitude:0.0} m from the trunk");
                _toldStanceFor = _target;
            }
        }

        private float AxeReach()
        {
            ItemDrop.ItemData axe = _citizen.Body.GetCurrentWeapon();
            return LogStance.ReachFor(axe?.m_shared.m_attack?.m_attackRange ?? 0f);
        }

        /// <returns>True when the target can't be damaged and should be given up.</returns>
        private bool OnSwingFinished()
        {
            bool hitOther = HitSomethingElse(SwingLog.Peek(_citizen));
            FinishSwingLog(false);
            if (_target == null) return false;   // it broke; the job notices
            bool damaged = !Mathf.Approximately(ReadHealth(_target), _healthBeforeSwing);
            SwingOutcome outcome = _swings.OnSwingFinished(damaged, IsMine(_target), _directDamage || _alwaysDirect, hitOther);
            if (outcome.EnableDirectDamage && !_alwaysDirect)
            {
                _directDamage = true;
                Log.Event($"{_citizen.CitizenName}'s axe swings are not hurting the targets; from now on the hits land directly.");   // handled: no warning
            }
            if (outcome.ApplyDirectHit && !_directHitThisSwing) ApplyDirectHit(_target);
            return outcome.GiveUp;
        }

        /// <summary>The moment the axe lands. With the stand-in on, a swing that missed hits now, in time with the animation.</summary>
        private void OnAxeLands()
        {
            if (_target == null || _directHitThisSwing) return;
            List<SwingLog.Hit> hits = SwingLog.Peek(_citizen);
            bool hitTarget = hits != null && hits.Exists(h => ReferenceEquals(h.Target, _target));
            if (_alwaysDirect || SwingTracker.ShouldHitAtTrigger(_directDamage, hitTarget, HitSomethingElse(hits)))
            {
                ApplyDirectHit(_target);
                _directHitThisSwing = true;
                hitTarget = true;
            }
            if (hitTarget) _onHit?.Invoke();
        }

        /// <summary>The swing landed on something other than the target (stopped there): his aim was off, not his swings.</summary>
        private bool HitSomethingElse(List<SwingLog.Hit> hits) => hits != null && hits.Exists(h => !ReferenceEquals(h.Target, _target));

        private void ApplyDirectHit(GameObject target)
        {
            IDestructible destructible = target.GetComponent<IDestructible>();
            ItemDrop.ItemData axe = _citizen.Body.GetCurrentWeapon();
            if (destructible == null || axe == null) return;
            var hit = new HitData();
            hit.m_damage.m_chop = axe.m_shared.m_damages.m_chop * SettlerSkill.DamageFactor(_citizen.Record.Woodcutting, Random.value);
            hit.m_toolTier = (short)axe.m_shared.m_toolTier;
            hit.m_point = target.transform.position + Vector3.up;
            // The hit direction decides which way a tree falls; use the planned, building-free direction.
            hit.m_dir = _targetKind == ChopKind.Tree && _fallDir != Vector3.zero
                ? _fallDir
                : (target.transform.position - _citizen.transform.position).normalized;
            hit.m_attacker = _citizen.Body.GetZDOID();
            SwingLog.DirectHit = true;
            try
            {
                destructible.Damage(hit);
            }
            finally
            {
                SwingLog.DirectHit = false;
            }
        }

        private void FinishSwingLog(bool targetBroke)
        {
            List<SwingLog.Hit> hits = SwingLog.EndSwing(_citizen);
            if (hits == null) return;   // no swing was under way
            SwingResult result = hits.Count == 0 ? SwingResult.HitNothing
                : targetBroke || hits.Exists(h => ReferenceEquals(h.Target, _target)) ? SwingResult.HitTarget
                : SwingResult.HitOther;
            SwingTally tally = SwingLog.TallyFor(_citizen);
            switch (tally.Record(result))
            {
                case SwingLogKind.Detail:
                    string what = hits.Count == 0
                        ? "hit nothing"
                        : "hit " + string.Join(", ", hits.Select(h => $"{h.Name} for {h.Chop:0} chop{(h.Stopped ? " (stopped: not their target)" : "")}"));
                    Log.Info($"{_citizen.CitizenName} swing {tally.Swings} at {_targetPrefab}, " +
                             $"{Utils.DistanceXZ(_citizen.transform.position, _targetPos):0.0} m from its centre: {what}");
                    break;
                case SwingLogKind.Summary:
                    Log.Info($"{_citizen.CitizenName}: {tally.Swings} swings, {tally.HitTarget} hit the target, " +
                             $"{tally.HitOther} hit something else, {tally.HitNothing} hit nothing");
                    break;
            }
        }

        private static GroundPoint Ground(Vector3 v) => new GroundPoint(v.x, v.z);

        private static float GroundHeight(GroundPoint p) => ZoneSystem.instance.GetGroundHeight(new Vector3(p.X, 0f, p.Z));

        private static Vector3 OnGround(GroundPoint p) => new Vector3(p.X, GroundHeight(p), p.Z);

        private static float ReadHealth(GameObject go)
        {
            ZNetView nview = go.GetComponent<ZNetView>();
            return nview != null && nview.IsValid() ? nview.GetZDO().GetFloat(ZDOVars.s_health, -1f) : -1f;
        }

        private static bool IsMine(GameObject go)
        {
            ZNetView nview = go.GetComponent<ZNetView>();
            return nview != null && nview.IsValid() && nview.IsOwner();
        }
    }
}
