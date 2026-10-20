using System.Linq;
using UnityEngine;
using VikingColonies.Buildings;
using VikingColonies.Citizens;
using VikingColonies.Colony;
using VikingColonies.Config;
using VikingColonies.Core;

namespace VikingColonies.AI
{
    /// <summary>
    /// The evening by the fire (settler life, 0.15): from 0.70 of the day until night, settlers gather at the colony's
    /// fire (the burning one nearest the Town Hall, a roofed one first; the Town Hall with none). Each takes a free seat
    /// near it, or his place on a ring round it, facing it: every other one sits on the ground (all stand in rain), and
    /// those standing raise a toast, laugh or cheer now and then. Night ends it like any free time.
    /// </summary>
    internal sealed partial class FreeTimeRoutine
    {
        private static readonly string[] EveningGestures = { "emote_toast", "emote_laugh", "emote_cheer" };
        /// <summary>The place is looked at again this often (a fire lit or gone out meanwhile).</summary>
        private const float EveningPlaceSeconds = 10f;
        private const float EveningReachSeconds = 40f;

        /// <summary>The ring round each gathering place, worked out once and shared (review of 0.15): spots behind a wall dropped.</summary>
        private static readonly System.Collections.Generic.Dictionary<string, EveningRing> s_rings =
            new System.Collections.Generic.Dictionary<string, EveningRing>();

        private sealed class EveningRing
        {
            public Vector3 Centre;
            public int Count;
            public float At;
            public float Inner;
            public readonly System.Collections.Generic.List<Vector3> Spots = new System.Collections.Generic.List<Vector3>();
        }

        private Vector3 _eveningCentre;
        private bool _eveningAtFire;
        private float _eveningPlaceAt = float.NegativeInfinity;
        private bool _eveningSits;

        /// <summary>Self-test: where the evening is (the fire's or the Town Hall's position) and whether at a fire.</summary>
        internal Vector3 EveningCentre => _eveningCentre;
        internal bool EveningAtFire => _eveningAtFire;

        private float _eveningMarkAt;

        /// <summary>
        /// Mood (0.17): he spends this evening by the colony's burning fire, with company once two others are within 6 m
        /// (looked at every 10 s).
        /// </summary>
        private void MarkEvening(float now)
        {
            _eveningMarkAt = now + 10f;
            if (EnvMan.instance == null || ZNet.instance == null) return;
            Vector3 me = _citizen.transform.position;
            string colony = _citizen.ColonyId;
            int near = 0;
            foreach (Citizen other in Citizen.All)
                if (other != null && other != _citizen && Utils.DistanceXZ(other.transform.position, me) <= 6f && other.ColonyId == colony) near++;
            _citizen.MarkEvening(EnvMan.instance.GetDay(ZNet.instance.GetTimeSeconds()), near >= 2);
        }

        public void TickEvening()
        {
            float now = Time.time;
            if (!Active)
            {
                Active = true;
                s_active.Add(this);
            }
            if (_doing != Leisure.Evening)
            {
                StopActivity();
                Begin(Leisure.Evening, now);
                _eveningPlaceAt = float.NegativeInfinity;
                SaveDoing(ActivityWords.Token(Leisure.Evening), "");
            }
            if (now - _eveningPlaceAt >= EveningPlaceSeconds) ChooseEveningPlace(now);
            if (!_arrived)
            {
                if (now - _startedAt > EveningReachSeconds)
                {
                    // Can't get to his seat or spot: he stays where he is, facing the fire.
                    ReleaseSeat();
                    _eveningSits = false;   // where he got stuck may be a doorway: he stands
                    _arrived = true;
                    _arrivedAt = now;
                    return;
                }
                if (_seat != null)
                {
                    Vector3 me = _citizen.transform.position, point = _seat.Point;
                    bool close = Utils.DistanceXZ(me, point) <= SitFrom && Mathf.Abs(me.y - point.y) <= 1f && _ai.NothingBetween(point);
                    if (!close && !_ai.GoTo(_spot, 0.4f)) return;
                    _ai.Idle();
                    if (ColonyPlaces.Taken(_seat, _citizen) || !_pose.SitOn(_seat.Chair))
                    {
                        ReleaseSeat();
                        _eveningPlaceAt = float.NegativeInfinity;   // a ring spot next time
                        return;
                    }
                }
                else if (_ai.GoTo(_spot, 0.5f)) _ai.Idle();
                else return;
                _arrived = true;
                _arrivedAt = now;
                _nextGestureAt = now + Random.Range(5f, 15f);
                return;
            }
            _ai.Idle();
            _citizen.Status = _eveningAtFire ? "$vc_status_evening_fire" : "$vc_status_evening_hall";
            if (_eveningAtFire && now >= _eveningMarkAt) MarkEvening(now);
            if (_seat != null) return;   // seated: no gestures (the game plays none on a seated character)
            _ai.Face(_eveningCentre);
            bool wet = EnvMan.IsWet();
            if (_eveningSits && !wet && _pose.Current == SettlerPose.Kind.None && now - _arrivedAt > 1.5f) _pose.SitGround();
            if (wet && _pose.Current == SettlerPose.Kind.Ground) _pose.Stand();
            if (_pose.Current == SettlerPose.Kind.None && now >= _nextGestureAt)
            {
                _pose.Gesture(EveningGestures[s_rng.Next(EveningGestures.Length)]);
                _nextGestureAt = now + Random.Range(10f, 20f);
            }
        }

        private void ChooseEveningPlace(float now)
        {
            bool first = float.IsNegativeInfinity(_eveningPlaceAt);
            _eveningPlaceAt = now;
            string colony = _citizen.ColonyId;
            ColonyBuilding hall = ColonyBuilding.FindTownHall(colony);
            float[] known = _citizen.Record.ColonyPos;
            Vector3 hallAt = hall != null ? hall.transform.position : known != null && known.Length == 3 ? Citizen.ToVector3(known) : _citizen.transform.position;
            var fires = ColonyPlaces.Fires(colony).Where(f => f.Valid).ToList();
            int pick = Gathering.Place(fires.Select(f => new GatheringFire(f.Id, Ground(f.Position), f.Burning, f.Roofed, f.AtHall, f.InHut)).ToList(),
                Ground(hallAt), HallRules.RadiusOf(colony));
            Vector3 centre = pick >= 0 ? fires[pick].Position : hallAt;
            bool moved = Utils.DistanceXZ(centre, _eveningCentre) > 1f;
            _eveningAtFire = pick >= 0;
            if (!_eveningAtFire) Gui.HuginTips.ShowNear(hallAt, Gui.HuginTips.Evening);
            if (!first && !moved) return;
            _eveningCentre = centre;
            ReleaseSeat();
            if (_pose.Current != SettlerPose.Kind.None) _pose.Stand();
            _arrived = false;
            _startedAt = now;
            // A free seat by the fire first; else his place on the ring (from his id among the colony's settlers).
            if (_eveningAtFire)
                _seat = ColonyPlaces.Seats(colony)
                    .Where(s => s.Valid && Vector3.Distance(s.Point, centre) <= FreeTimeRules.FireNearSeat && !ColonyPlaces.Taken(s, _citizen)
                                && !FreeTimeClaims.TakenByOther(s.Id, s.Point, _citizen.Id))
                    .OrderBy(s => Vector3.Distance(s.Point, _citizen.transform.position)).FirstOrDefault();
            if (_seat != null)
            {
                FreeTimeClaims.TryClaim(_seat.Id, _seat.Point, _citizen.Id);
                _spot = SeatApproach(_seat);
                return;
            }
            // The colony's roster with those loaded here: the same places on every computer (0.18: each counted only its own).
            Buildings.ColonyBuilding hallHere = Buildings.ColonyBuilding.FindTownHall(colony);
            Buildings.TownHallModule townHall = hallHere != null ? hallHere.GetComponent<Buildings.TownHallModule>() : null;
            System.Collections.Generic.List<string> ids = Gathering.Members(townHall != null ? townHall.Roster.Entries.Select(e => e.CitizenId) : null,
                Citizen.All.Where(c => c != null && c.ColonyId == colony).Select(c => c.Id));
            int index = Mathf.Max(0, Gathering.Index(_citizen.Id, ids));
            EveningRing ring = RingFor(colony, centre, ids.Count, now, Gathering.InnerFor(pick >= 0 ? fires[pick].Radius : 0f));
            _spot = ring.Spots.Count > 0 ? ring.Spots[index % ring.Spots.Count] : centre;
            _eveningSits = index % 2 == 0;   // every other one sits; the others stand and raise a toast
        }

        /// <summary>The ring round a place: twice as many spots as settlers worked out, those behind a wall or a step too high dropped.</summary>
        private static EveningRing RingFor(string colony, Vector3 centre, int count, float now, float inner)
        {
            if (s_rings.TryGetValue(colony, out EveningRing ring) && Utils.DistanceXZ(ring.Centre, centre) < 1f && ring.Count == count
                && Mathf.Approximately(ring.Inner, inner) && now - ring.At < 120f) return ring;
            ring = new EveningRing { Centre = centre, Count = count, At = now, Inner = inner };
            int candidates = Mathf.Max(count * 2, 8);
            for (int i = 0; i < candidates && ring.Spots.Count < count; i++)
            {
                GroundPoint p = Gathering.Ring(Ground(centre), i, candidates, inner);   // clear of a bonfire (huts round 2)
                var at = new Vector3(p.X, centre.y, p.Z);
                if (ZoneSystem.instance != null && ZoneSystem.instance.GetSolidHeight(at, out float solid, 1000)) at.y = solid;
                if (Mathf.Abs(at.y - centre.y) > 1.5f || Walls.StructureBetween(centre, at) || NearDoor(at, 2f)) continue;   // not in a doorway
                ring.Spots.Add(at);
            }
            s_rings[colony] = ring;
            return ring;
        }

        private void ReleaseSeat()
        {
            if (_seat == null) return;
            FreeTimeClaims.Release(_seat.Id, _citizen.Id);
            if (_pose.Current == SettlerPose.Kind.Seat) _pose.Stand();
            _seat = null;
        }
    }
}
