using System.Collections.Generic;
using UnityEngine;
using VikingColonies.AI;
using VikingColonies.Citizens;
using VikingColonies.Core;

namespace VikingColonies.Jobs
{
    /// <summary>
    /// Keeps a settler clear of falling trees and moving logs (the lumberjack and the builder): after a felling blow he
    /// steps back to the side of the fall line and waits until the logs nearby lie still; a moving log that comes at him
    /// while he waits, he steps out of its path. The rules are Core's <see cref="LogSettle"/> and <see cref="RetreatSpot"/>.
    /// </summary>
    internal sealed class LogWatch
    {
        private const float SampleSeconds = 0.25f;
        private const float WalkSeconds = 6f;     // how long he tries to reach his spot before waiting where he is
        private const float StaleSeconds = 2f;    // not stepped for this long: he was elsewhere, so the watch starts over
        private const float BrokenRadius = 10f;   // the pieces of a broken log land within this
        private const float NeighbourRadius = Chopper.FallLength + 5f;   // settlers this near another's felled tree wait for it too

        // Every settler's watch on this computer (one per settler: a new job's watch replaces the old one).
        private static readonly List<LogWatch> s_all = new List<LogWatch>();

        private struct Ends
        {
            public Vector3 A, B;
            public float Radius;
        }

        private readonly Citizen _citizen;
        private readonly CitizenAI _ai;
        private Dictionary<TreeLog, Ends> _seen = new Dictionary<TreeLog, Ends>();
        private Dictionary<TreeLog, Ends> _now = new Dictionary<TreeLog, Ends>();
        private LogSettle _settle;
        private string _what = "";
        private Vector3 _centre;
        private float _radius;
        private Vector3? _spot;
        private float _walkUntil;
        private float _lastSample;
        private float _lastStep;
        private float _started;
        private int _dodges;
        private GameObject _probe;
        private Vector3 _probeA, _probeB;
        private float _probeTime;
        private bool _probeMoving;
        private TreeLog _fastestLog;   // the log that moved fastest in the last look (for the log)
        private float _fastestSpeed;

        public LogWatch(Citizen citizen, CitizenAI ai)
        {
            _citizen = citizen;
            _ai = ai;
            s_all.RemoveAll(w => w._citizen == null || ReferenceEquals(w._citizen, citizen));
            s_all.Add(this);
        }

        /// <summary>True while he waits for logs to settle; the job calls <see cref="Step"/> instead of working.</summary>
        public bool Active => _settle != null;

        /// <summary>The blow that felled a tree: step back to the side of its fall line, and wait for its trunk.</summary>
        public void AfterFelling(Vector3 stump, Vector3 fallDir, string what)
        {
            Begin(stump, Chopper.FallLength, what);
            GoToSpot(RetreatSpot.FromFall(G(stump), G(fallDir), G(_citizen.transform.position), RetreatSpot.Distance));
            // Settlers working nearby wait for this trunk too, and step out of its way if it comes at them.
            foreach (LogWatch other in s_all)
            {
                if (ReferenceEquals(other, this) || other._citizen == null || other.Active) continue;
                if (Vector3.Distance(other._citizen.transform.position, stump) > NeighbourRadius) continue;
                other.Begin(stump, Chopper.FallLength, $"{_citizen.CitizenName}'s {what}");
            }
        }

        /// <summary>A log broke into pieces, or the log he was about to chop moves: wait for the logs near it.</summary>
        public void AfterBreak(Vector3 at, string what) => Begin(at, BrokenRadius, what);

        /// <summary>True when this log has moved faster than a settled log since he last looked at it (looks are 0.25 s apart).</summary>
        public bool SeesMoving(GameObject log)
        {
            if (log == null) return false;
            float now = Time.time;
            ChopShape.TryGet(log, out Vector3 a, out Vector3 b, out _);
            if (!ReferenceEquals(log, _probe))
            {
                _probe = log;
                _probeA = a;
                _probeB = b;
                _probeTime = now;
                _probeMoving = false;
                return false;
            }
            float dt = now - _probeTime;
            if (dt < SampleSeconds) return _probeMoving;
            float speed = Mathf.Max(Vector3.Distance(a, _probeA), Vector3.Distance(b, _probeB)) / dt;
            _probeA = a;
            _probeB = b;
            _probeTime = now;
            _probeMoving = LogSettle.IsMoving(speed, Afloat(log.GetComponent<TreeLog>()));
            return _probeMoving;
        }

        /// <summary>
        /// A log afloat bobs on the waves for ever; its movement doesn't count. The game's "in a water area" flag covers
        /// whole stretches of coast, so it takes the log's middle being at the water surface or below.
        /// </summary>
        private static bool Afloat(TreeLog log)
        {
            Floating floating = log != null ? log.GetComponent<Floating>() : null;
            if (floating == null || !floating.HaveLiquidLevel()) return false;
            Vector3 middle = log.transform.position;
            if (ChopShape.TryGet(log.gameObject, out Vector3 a, out Vector3 b, out _)) middle = (a + b) * 0.5f;
            return LogSettle.IsAfloat(middle.y, Floating.GetLiquidLevel(middle));
        }

        /// <summary>One tick while <see cref="Active"/>: look at the logs, keep out of their way, end once they've settled.</summary>
        public void Step()
        {
            if (_settle == null) return;
            float now = Time.time;
            if (now - _lastStep > StaleSeconds) Begin(_centre, _radius, _what);   // he was busy elsewhere: look afresh
            _lastStep = now;
            if (now - _lastSample >= SampleSeconds)
            {
                SettleState state = _settle.Update(Sample(now), now);
                if (state != SettleState.Moving)
                {
                    Finish(state, now);
                    return;
                }
            }
            if (_spot.HasValue && now < _walkUntil && !_ai.GoTo(_spot.Value, MoveRules.MinStopDistance, hurry: true)) return;
            _spot = null;
            _ai.Idle();
            _ai.Face(_centre);
        }

        private void Begin(Vector3 centre, float radius, string what)
        {
            float now = Time.time;
            _settle = new LogSettle(now);
            _centre = centre;
            _radius = radius;
            _what = string.IsNullOrEmpty(what) ? "the logs" : what;
            _spot = null;
            _seen.Clear();
            _lastSample = now;
            Sample(now);   // remember where the logs are; the first speeds come a sample later
            _started = _lastStep = now;
            _dodges = 0;
        }

        /// <summary>
        /// Measures every log near the centre and returns the fastest speed (a log not seen before counts as moving); steps
        /// him out of the path of the nearest moving log within <see cref="RetreatSpot.Distance"/>.
        /// </summary>
        private float Sample(float now)
        {
            float dt = Mathf.Max(0.01f, now - _lastSample);
            _lastSample = now;
            _now.Clear();
            float fastest = 0f, nearestGap = float.MaxValue;
            _fastestLog = null;
            Ends nearest = default(Ends);
            Vector3 nearestMotion = Vector3.zero;
            Vector3 me = _citizen.transform.position;
            foreach (TreeLog log in WorldScan.Logs())   // vc_perf: the search for every log cost milliseconds four times a second
            {
                if (log == null) continue;
                ChopShape.TryGet(log.gameObject, out Vector3 a, out Vector3 b, out float radius);
                if (Mathf.Min(Vector3.Distance(a, _centre), Vector3.Distance(b, _centre)) > _radius) continue;
                var ends = new Ends { A = a, B = b, Radius = radius };
                _now[log] = ends;
                if (!_seen.TryGetValue(log, out Ends before))
                {
                    if (!Afloat(log)) fastest = float.MaxValue;   // new here: not known to be still yet
                    continue;
                }
                float speed = Mathf.Max(Vector3.Distance(a, before.A), Vector3.Distance(b, before.B)) / dt;
                if (!LogSettle.IsMoving(speed, Afloat(log))) continue;
                if (speed > fastest) _fastestLog = log;
                fastest = Mathf.Max(fastest, speed);
                float gap = LogStance.DistanceToSurface(G(a), G(b), radius, G(me));
                if (gap >= RetreatSpot.Distance || gap >= nearestGap) continue;
                nearestGap = gap;
                nearest = ends;
                nearestMotion = (a + b) * 0.5f - (before.A + before.B) * 0.5f;
            }
            Dictionary<TreeLog, Ends> swap = _seen;
            _seen = _now;
            _now = swap;
            if (nearestGap < float.MaxValue) StepAside(nearest, nearestMotion, me, now);
            _fastestSpeed = fastest;
            return fastest;
        }

        private void StepAside(Ends log, Vector3 motion, Vector3 me, float now)
        {
            Vector3 spot = OnGround(RetreatSpot.OutOfPath(G(log.A), G(log.B), log.Radius, G(motion), G(me), RetreatSpot.PathMargin));
            if (Utils.DistanceXZ(spot, me) < 0.5f) return;   // already out of its path
            if (_spot.HasValue && now < _walkUntil && Utils.DistanceXZ(spot, _spot.Value) < 1f) return;   // already going there
            _spot = spot;
            _walkUntil = now + WalkSeconds;
            _dodges++;
            Log.Info($"{_citizen.CitizenName} steps out of the way of a moving log ({_what})");
        }

        private void GoToSpot(GroundPoint spot)
        {
            _spot = OnGround(spot);
            _walkUntil = Time.time + WalkSeconds;
        }

        private void Finish(SettleState state, float now)
        {
            string how = state == SettleState.GaveUp
                ? $"stopped waiting for {_what} after {now - _started:0} s (still moving: {Describe(_fastestLog, _fastestSpeed)})"
                : $"waited {now - _started:0.0} s for {_what} to settle";
            Log.Info($"{_citizen.CitizenName} {how}{(_dodges > 0 ? $", stepping out of a log's way {_dodges} times" : "")}");
            _settle = null;
            _spot = null;
        }

        /// <summary>For the log: which log kept moving, where, how fast, and whether it floats.</summary>
        private static string Describe(TreeLog log, float speed)
        {
            if (log == null) return speed >= float.MaxValue ? "a log just appeared" : "no log";
            Floating floating = log.GetComponent<Floating>();
            string water = Afloat(log) ? "afloat" : floating != null && floating.HaveLiquidLevel() ? "near water" : "dry";
            return $"{Utils.GetPrefabName(log.gameObject)} at {log.transform.position}, {speed:0.00} m/s, {water}";
        }

        private static GroundPoint G(Vector3 v) => new GroundPoint(v.x, v.z);

        private static Vector3 OnGround(GroundPoint p) =>
            new Vector3(p.X, ZoneSystem.instance.GetGroundHeight(new Vector3(p.X, 0f, p.Z)), p.Z);
    }
}
