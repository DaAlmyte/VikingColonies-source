using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VikingColonies.Citizens;
using VikingColonies.Colony;
using VikingColonies.Core;

namespace VikingColonies.AI
{
    /// <summary>
    /// One settler's free time (settler life, 0.14), on the computer running him: near his anchor (his hut, or the Town
    /// Hall) he sits on a seat, sits by a fire, chats with another settler in free time, strolls or sits down, choosing
    /// again every 30-90 seconds (<see cref="FreeTimeRules"/>). His job asks for it each tick it has nothing to do; a tick
    /// that doesn't ask ends it (<see cref="End"/>: he stands up and leaves his chat).
    /// </summary>
    internal sealed partial class FreeTimeRoutine
    {
        /// <summary>The settlers in free time on this computer (chat partners come from here).</summary>
        private static readonly List<FreeTimeRoutine> s_active = new List<FreeTimeRoutine>();
        private static readonly System.Random s_rng = new System.Random();
        private static readonly string[] TalkGestures =
            { "emote_point", "emote_shrug", "emote_laugh", "emote_thumbsup", "emote_comehere", "emote_nonono", "emote_cheer" };
        /// <summary>Not there after this long: something is in the way; he picks something else.</summary>
        private const float ReachSeconds = 30f;
        /// <summary>He sits down from this near the seat's point (a player can from 2 m).</summary>
        private const float SitFrom = 1.3f;
        /// <summary>A seat he couldn't get to is passed over this long.</summary>
        private const double SkipSeatSeconds = 300;
        private readonly TargetBlacklist _skippedSeats = new TargetBlacklist(SkipSeatSeconds);
        private readonly TargetBlacklist _skippedFires = new TargetBlacklist(SkipSeatSeconds);
        /// <summary>The seat he last got up from: picked last next time (review of 0.14: he stood up and sat straight back down).</summary>
        private string _lastSeatId = "";

        private readonly CitizenAI _ai;
        private readonly Citizen _citizen;
        private readonly SettlerPose _pose;

        private Leisure _doing = Leisure.None;
        private float _chooseAt;
        private float _startedAt;
        private float _doUntil;
        private bool _arrived;
        private SeatPlace _seat;
        private FirePlace _fire;
        private Vector3 _spot;
        private FreeTimeRoutine _partner;
        private bool _myTurn;
        private float _nextGestureAt;
        private Vector3 _anchor;
        private string _savedDoing = "";

        public FreeTimeRoutine(CitizenAI ai, Citizen citizen)
        {
            _ai = ai;
            _citizen = citizen;
            _pose = citizen.GetComponent<SettlerPose>();
        }

        public bool Active { get; private set; }

        public Leisure Doing => _doing;

        /// <summary>Who he is chatting with, or null.</summary>
        public Citizen Partner => _partner != null ? _partner._citizen : null;

        /// <summary>He has got there (to his seat, the fire, the stroll's end, the chat).</summary>
        public bool Arrived => _arrived;

        /// <summary>Self-test: settlers in free time on this computer.</summary>
        internal static int ActiveCount => s_active.Count;

        private Leisure _forcedForTest = Leisure.None;

        /// <summary>Self-test: he starts a chat with this one now (no waiting for the dice or for the friend to be free).</summary>
        internal void ChatWithForTest(FreeTimeRoutine partner)
        {
            if (!Active || partner == null || !partner.Active || partner == this) return;
            StopActivity();
            StartChat(partner, Time.time);
        }

        /// <summary>Self-test: he picks this next (when it's possible), at once.</summary>
        internal void ForceForTest(Leisure leisure)
        {
            _forcedForTest = leisure;
            _chooseAt = 0f;
        }

        public void Tick(Vector3 anchor)
        {
            float now = Time.time;
            if (!Active)
            {
                Active = true;
                s_active.Add(this);
                _chooseAt = now;
            }
            // A new anchor far away (hired at another hut): he goes there now, not after this activity (review of 0.14).
            if (Utils.DistanceXZ(anchor, _anchor) > FreeTimeRules.Radius) _chooseAt = now;
            _anchor = anchor;
            if (_doing == Leisure.None || now >= _chooseAt || TooLongOnTheWay(now)) Choose(now);
            if (_arrived && _doing != Leisure.Chat && _pose.Current == SettlerPose.Kind.None) LookAtPlayerNear();
            switch (_doing)
            {
                case Leisure.Seat: TickSeat(now); break;
                case Leisure.Fire: TickFire(now); break;
                case Leisure.Chat: TickChat(now); break;
                case Leisure.Stroll: TickStroll(now); break;
                case Leisure.SitDown: TickSitDown(now); break;
                case Leisure.TendFire: TickTend(now); break;
                case Leisure.CarryStock: TickCarry(now); break;
                default: _ai.Idle(); break;
            }
        }

        /// <summary>This computer has just started running him: what another computer (or a reload) left saved is cleared.</summary>
        internal void ResetForNewOwner()
        {
            if (Active)
            {
                Active = false;
                s_active.Remove(this);
            }
            _partner = null;
            _seat = null;
            _fire = null;
            _doing = Leisure.None;
            _savedDoing = null;   // unknown here: the next save writes
            SaveDoing("", "");
        }

        /// <summary>Free time is over (his job has work, night, a monster…): he stands up and leaves his chat.</summary>
        public void End()
        {
            if (!Active) return;
            Active = false;
            s_active.Remove(this);
            StopActivity();
            SaveDoing("", "");
        }

        private bool TooLongOnTheWay(float now)
        {
            if (_arrived || _doing == Leisure.None || now - _startedAt <= ReachSeconds) return false;
            Log.Info($"{_citizen.CitizenName}'s free time: couldn't get to their {_doing} within {ReachSeconds:0} s; they pick something else");
            if (_seat != null) _skippedSeats.Add(_seat.Id, now);
            if (_fire != null) _skippedFires.Add(_fire.Id, now);
            if (_tendFire != null) _skippedFires.Add(_tendFire.Id, now);
            if (_run != null) _skippedRuns.Add(_runKey, now);
            return true;
        }

        private void Choose(float now)
        {
            StopActivity();
            string colony = _citizen.ColonyId;
            Vector3 me = _citizen.transform.position;
            bool wet = EnvMan.IsWet();
            List<SeatPlace> seats = ColonyPlaces.Seats(colony)
                .Where(s => s.Valid && Utils.DistanceXZ(s.Point, _anchor) <= FreeTimeRules.Radius && (!wet || s.Roofed)
                            && !_skippedSeats.IsBlocked(s.Id, now)
                            && !FreeTimeClaims.TakenByOther(s.Id, s.Point, _citizen.Id) && !ColonyPlaces.Taken(s, _citizen))
                .ToList();
            List<FirePlace> fires = ColonyPlaces.Fires(colony)
                .Where(f => f.Valid && f.Burning && Utils.DistanceXZ(f.Position, _anchor) <= FreeTimeRules.FireReach && (!wet || f.Roofed)
                            && !_skippedFires.IsBlocked(f.Id, now))
                .ToList();
            FreeTimeRoutine partner = FindPartner(me);
            // Nothing to sit on or by at all (not merely taken, or out in the rain): Hugin says so, once.
            if (!ColonyPlaces.Seats(colony).Any(s => s.Valid && Utils.DistanceXZ(s.Point, _anchor) <= FreeTimeRules.Radius)
                && !ColonyPlaces.Fires(colony).Any(f => f.Valid && f.Burning && Utils.DistanceXZ(f.Position, _anchor) <= FreeTimeRules.FireReach))
                Gui.HuginTips.ShowNear(_anchor, Gui.HuginTips.FreeTime);
            FirePlace tend = FireToTend(now, out string fuelItem, out int fuelWant, out string fuelChest);
            StockRun carry = StockToCarry(now);   // 0.21
            var choices = new LeisureChoices(seats.Count > 0, fires.Count > 0, partner != null, wet, tend != null, carry != null);
            Leisure pick = _forcedForTest != Leisure.None && FreeTimeRules.Weight(_forcedForTest, choices) > 0
                ? _forcedForTest : FreeTimeRules.Pick(choices, s_rng.NextDouble(), _citizen.TraitKind);   // by his nature (0.16)
            if (pick == Leisure.None) pick = Leisure.Stroll;   // a lazy one in the rain with nowhere to sit: a short walk all the same
            if (_forcedForTest != Leisure.None && pick != _forcedForTest)
                Log.Info($"{_citizen.CitizenName}'s free time: the test asked for {_forcedForTest}, not possible now ({seats.Count} seats, {fires.Count} fires, partner {partner != null})");
            _forcedForTest = Leisure.None;
            Begin(pick, now);
            _chooseAt = now + FreeTimeRules.ChoiceSeconds(s_rng.NextDouble());
            Log.Info($"{_citizen.CitizenName}'s free time: {pick} ({seats.Count} seats, {fires.Count} fires, partner {(partner != null ? partner._citizen.CitizenName : "none")}{(wet ? ", wet" : "")})");
            switch (pick)
            {
                case Leisure.Seat:
                    // One near a burning fire first, then the nearest.
                    _seat = seats.OrderBy(s => s.Id == _lastSeatId ? 1 : 0)   // not straight back onto the one he left
                                 .ThenBy(s => fires.Any(f => Vector3.Distance(f.Position, s.Point) <= FreeTimeRules.FireNearSeat) ? 0 : 1)
                                 .ThenBy(s => Vector3.Distance(s.Point, me)).First();
                    FreeTimeClaims.TryClaim(_seat.Id, _seat.Point, _citizen.Id);
                    _spot = SeatApproach(_seat);
                    SaveDoing(ActivityWords.Token(Leisure.Seat), "");
                    break;
                case Leisure.Fire:
                    _fire = fires.OrderBy(f => Vector3.Distance(f.Position, me)).First();
                    _spot = FireSpot(_fire.Position, me);
                    SaveDoing(ActivityWords.Token(Leisure.Fire), "");
                    break;
                case Leisure.Chat:
                    if (StartChat(partner, now)) break;
                    // A wall between them wherever they'd meet: a stroll instead (1.4.2).
                    Begin(Leisure.Stroll, now);
                    _spot = StrollSpot(me);
                    SaveDoing(ActivityWords.Token(Leisure.Stroll), "");
                    break;
                case Leisure.Stroll:
                    _spot = StrollSpot(me);
                    SaveDoing(ActivityWords.Token(Leisure.Stroll), "");
                    break;
                case Leisure.SitDown:
                    if (NearDoor(me) || NearFire(me))
                    {
                        // Not in a doorway (life run new3: one sat down outside the Longhouse door and another couldn't get out).
                        Begin(Leisure.Stroll, now);
                        _spot = StrollSpot(me);
                        SaveDoing(ActivityWords.Token(Leisure.Stroll), "");
                        break;
                    }
                    _spot = me;
                    SaveDoing(ActivityWords.Token(Leisure.SitDown), "");
                    break;
                case Leisure.TendFire:
                    StartTending(tend, fuelItem, fuelWant, fuelChest);
                    break;
                case Leisure.CarryStock:
                    StartCarrying(carry);
                    break;
            }
        }

        /// <summary>Starts an activity: on his way, no end time yet (it is set when he gets there).</summary>
        private void Begin(Leisure leisure, float now)
        {
            _doing = leisure;
            _startedAt = now;
            _arrived = false;
            _doUntil = float.MaxValue;
        }

        private void StopActivity()
        {
            _pose?.Stand();
            StopTending();
            StopCarrying();
            if (_seat != null)
            {
                FreeTimeClaims.Release(_seat.Id, _citizen.Id);
                _lastSeatId = _seat.Id;
            }
            _seat = null;
            _fire = null;
            if (_partner != null)
            {
                FreeTimeRoutine partner = _partner;
                _partner = null;
                if (partner._partner == this)
                {
                    partner._partner = null;
                    partner._chooseAt = Time.time;   // his chat is over too: he picks something else
                }
            }
            _doing = Leisure.None;
        }

        // ---- Seats ----

        private float _seatCheckAt;

        private void TickSeat(float now)
        {
            bool look = now >= _seatCheckAt;   // whether someone took it: once a second, not every step
            if (look) _seatCheckAt = now + 1f;
            // Gone, or taken: on his way, anyone at it; once seated, only a player sitting right on it (one beside him is fine).
            if (_seat == null || !_seat.Valid || look && (_arrived ? ColonyPlaces.PlayerSitsOn(_seat) : ColonyPlaces.Taken(_seat, _citizen)))
            {
                _chooseAt = now;
                return;
            }
            if (!_arrived)
            {
                // To the open side of the seat, then onto it, as a player sits down from beside it: from close by only
                // on the same floor with nothing between (review of 0.14: through a ceiling or a wall otherwise).
                Vector3 me = _citizen.transform.position, point = _seat.Point;
                bool close = Utils.DistanceXZ(me, point) <= SitFrom && Mathf.Abs(me.y - point.y) <= 1f && _ai.NothingBetween(point);
                if (!close && !_ai.GoTo(_spot, 0.4f)) return;
                _ai.Idle();
                if (ColonyPlaces.Taken(_seat, _citizen))   // a player sat down there just now
                {
                    _chooseAt = now;
                    return;
                }
                if (!_pose.SitOn(_seat.Chair))
                {
                    _chooseAt = now;
                    return;
                }
                Arrive(now, Leisure.Seat);
                return;
            }
            // No gestures while seated: the game never plays one on a seated character (review of 0.14).
            _ai.Idle();
            if (now >= _doUntil) _chooseAt = now;
        }

        // ---- Fires ----

        private void TickFire(float now)
        {
            if (_fire == null || !_fire.Valid || !_fire.Burning)
            {
                _chooseAt = now;
                return;
            }
            if (!_arrived)
            {
                if (!_ai.GoTo(_spot, 0.5f)) return;
                _ai.Idle();
                Arrive(now, Leisure.Fire);
                return;
            }
            Vector3 fire = _fire.Position;
            if (_pose.Current == SettlerPose.Kind.None)
            {
                _ai.Face(fire);
                // Facing the fire (or after a moment anyway), he sits down; in rain he stays standing.
                if (!EnvMan.IsWet() && (_ai.IsFacing(fire) || now - _startedAt > 3f && now - _arrivedAt > 2f)) _pose.SitGround();
            }
            if (now >= _doUntil) _chooseAt = now;
        }

        private float _arrivedAt;

        /// <summary>A spot 2-3 m from the fire on his side of it, at least a metre from anyone else there.</summary>
        private Vector3 FireSpot(Vector3 fire, Vector3 me)
        {
            Vector3 away = me - fire;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = Vector3.forward;
            away.Normalize();
            // Aimed at the nearer half: a walk stops up to half a metre short (the 0.14 test: 3.4 m from a 3 m spot).
            float radius = Random.Range(FreeTimeRules.FireSitMin, (FreeTimeRules.FireSitMin + FreeTimeRules.FireSitMax) / 2f);
            foreach (float turn in new[] { 0f, 35f, -35f, 70f, -70f, 105f, -105f, 140f, -140f, 180f })
            {
                Vector3 at = fire + Quaternion.Euler(0f, turn, 0f) * away * radius;
                if (!Crowded(at) && !NearDoor(at, 2f) && _ai.CanWalkTo(at)) return at;   // one he can get to (not behind a hearth's wall), not in a doorway
            }
            return fire + away * radius;
        }

        private bool Crowded(Vector3 at)
        {
            foreach (Citizen c in Citizen.All)
                if (c != null && c != _citizen && Utils.DistanceXZ(c.transform.position, at) < FreeTimeRules.FireSpacing) return true;
            foreach (FreeTimeRoutine r in s_active)
                if (r != this && r._doing == Leisure.Fire && Utils.DistanceXZ(r._spot, at) < FreeTimeRules.FireSpacing) return true;
            return false;
        }

        // ---- Chats ----

        private FreeTimeRoutine FindPartner(Vector3 me)
        {
            s_active.RemoveAll(r => r._citizen == null);   // a settler removed while in free time
            var candidates = s_active.Where(r => r != this && r._citizen != null && r._citizen.ColonyId == _citizen.ColonyId)
                .Select(r => new ChatCandidate(r._citizen.Id, Ground(r._citizen.transform.position),
                    r._partner == null && (r._doing == Leisure.None || r._doing == Leisure.Stroll)))
                .ToList();
            string id = ChatPairing.Partner(_citizen.Id, Ground(me), candidates);
            return id.Length == 0 ? null : s_active.FirstOrDefault(r => r._citizen != null && r._citizen.Id == id);
        }

        private static int s_chatWallMask;

        /// <summary>A wall, a post or a door between the two spots at chest height: they'd face each other through it.</summary>
        private static bool WallBetween(Vector3 first, Vector3 second)
        {
            if (s_chatWallMask == 0) s_chatWallMask = LayerMask.GetMask("piece");
            return Physics.Linecast(first + Vector3.up * 1.2f, second + Vector3.up * 1.2f, s_chatWallMask, QueryTriggerInteraction.Ignore);
        }

        /// <summary>
        /// Starts a chat at the first meeting place with no wall between the two (halfway, by him, by the other); false,
        /// and nothing changed, when every one has a wall between.
        /// </summary>
        /// <summary>
        /// The first meeting place with no wall between its two spots, both checked at one height
        /// (<see cref="ChatPairing.MeetingHeight"/>); false when every one has a wall (the life test checks it).
        /// </summary>
        internal static bool ChooseMeeting(Vector3 mine, Vector3 theirs, out ChatMeeting meeting)
        {
            List<ChatMeeting> places = ChatPairing.Meetings(Ground(mine), Ground(theirs));
            for (int i = 0; i < places.Count; i++)
            {
                float y = ChatPairing.MeetingHeight(i, mine.y, theirs.y);
                ChatMeeting place = places[i];
                if (WallBetween(new Vector3(place.First.X, y, place.First.Z), new Vector3(place.Second.X, y, place.Second.Z))) continue;
                meeting = place;
                return true;
            }
            meeting = default(ChatMeeting);
            return false;
        }

        private bool StartChat(FreeTimeRoutine partner, float now)
        {
            Vector3 mine = _citizen.transform.position, theirs = partner._citizen.transform.position;
            if (!ChooseMeeting(mine, theirs, out ChatMeeting chosen))
            {
                Log.Info($"{_citizen.CitizenName} won't chat with {partner._citizen.CitizenName}: a wall stands between them wherever they'd meet");
                return false;
            }
            partner.StopActivity();
            ChatMeeting meeting = chosen;
            string where = Mathf.Abs(chosen.First.X - mine.x) < 0.05f && Mathf.Abs(chosen.First.Z - mine.z) < 0.05f ? "beside " + _citizen.CitizenName
                : Mathf.Abs(chosen.Second.X - theirs.x) < 0.05f && Mathf.Abs(chosen.Second.Z - theirs.z) < 0.05f ? "beside " + partner._citizen.CitizenName : "halfway";
            Log.Info($"{_citizen.CitizenName} and {partner._citizen.CitizenName} meet to chat {where} (at ({chosen.First.X:0.0}, {chosen.First.Z:0.0}) " +
                     $"and ({chosen.Second.X:0.0}, {chosen.Second.Z:0.0}); {Utils.DistanceXZ(mine, theirs):0.0} m apart)");
            _partner = partner;
            partner._partner = this;
            // Both start afresh: a stroller's end time left over would end the chat as they meet (review of 0.14).
            Begin(Leisure.Chat, now);
            partner.Begin(Leisure.Chat, now);
            float length = FreeTimeRules.DoSeconds(Leisure.Chat, s_rng.NextDouble());
            _chooseAt = partner._chooseAt = now + ReachSeconds + length;
            _spot = Spot(meeting.First, _citizen.transform.position);
            partner._spot = Spot(meeting.Second, partner._citizen.transform.position);
            _myTurn = ChatPairing.Asks(_citizen.Id, partner._citizen.Id);
            partner._myTurn = !_myTurn;
            _chatLength = partner._chatLength = length;
            SaveDoing(ActivityWords.Token(Leisure.Chat), partner._citizen.CitizenName, partner._citizen.Id);
            partner.SaveDoing(ActivityWords.Token(Leisure.Chat), _citizen.CitizenName, _citizen.Id);
            return true;
        }

        private float _chatLength;

        private void TickChat(float now)
        {
            FreeTimeRoutine partner = _partner;
            if (partner == null || !partner.Active || partner._partner != this || partner._citizen == null)
            {
                _partner = null;
                _chooseAt = now;
                return;
            }
            if (!_arrived)
            {
                if (_ai.GoTo(_spot, 0.4f))
                {
                    _ai.Idle();
                    _arrived = true;
                    _arrivedAt = now;
                }
                return;
            }
            _ai.Idle();
            _ai.Face(partner._citizen.transform.position);
            if (!partner._arrived) return;
            if (_doUntil == float.MaxValue)
            {
                _doUntil = now + _chatLength;
                _nextGestureAt = now + (_myTurn ? 0.5f : 2.5f);
            }
            if (now >= _doUntil)
            {
                _chooseAt = now;
                return;
            }
            if (_myTurn && now >= _nextGestureAt)
            {
                _pose.Gesture(TalkGestures[s_rng.Next(TalkGestures.Length)]);
                _myTurn = false;
                partner._myTurn = true;
                partner._nextGestureAt = now + FreeTimeRules.GestureEverySeconds(s_rng.NextDouble());
            }
        }

        /// <summary>Where he walks to before he sits: in front of the seat (the way its sitter faces), on the ground.</summary>
        internal static Vector3 SeatApproach(SeatPlace seat)
        {
            Transform point = seat.Chair.m_attachPoint;
            Vector3 front = point.forward;
            front.y = 0f;
            front = front.sqrMagnitude > 0.01f ? front.normalized : Vector3.forward;
            Vector3 at = point.position + front * 0.7f;
            if (ZoneSystem.instance != null && ZoneSystem.instance.GetSolidHeight(at, out float solid, 1000)) at.y = Mathf.Min(point.position.y, solid);
            return at;
        }

        /// <summary>His side of the meeting, at the height he stands (the game's paths find the ground).</summary>
        private static Vector3 Spot(GroundPoint p, Vector3 from) => new Vector3(p.X, from.y, p.Z);

        // ---- Strolls and sitting down ----

        /// <summary>A stroll stays outdoors and clear of doors (self-test run 35: a settler strolled into a hut and stood in its doorway).</summary>
        private const float StrollDoorClearance = 3f;

        /// <summary>Within <paramref name="range"/> of a door: no sitting down or standing about there.</summary>
        internal static bool NearDoor(Vector3 at, float range = StrollDoorClearance)
        {
            foreach (Door door in Jobs.WorldScan.Doors())
                if (door != null && Utils.DistanceXZ(door.transform.position, at) < range && Mathf.Abs(door.transform.position.y - at.y) < 3f) return true;
            return false;
        }
        private static int s_roofMask;

        /// <summary>Within <see cref="FreeTimeRules.FireClearance"/> of one of his colony's fires: no place to stand about or sit down.</summary>
        private bool NearFire(Vector3 at) =>
            FreeTimeRules.TooNearAFire(new GroundPoint(at.x, at.z),
                ColonyPlaces.Fires(_citizen.ColonyId).Where(f => f.Valid).Select(f => new GroundPoint(f.Position.x, f.Position.z)));

        private Vector3 StrollSpot(Vector3 me)
        {
            if (s_roofMask == 0) s_roofMask = LayerMask.GetMask("piece", "piece_nonsolid");
            IReadOnlyList<Door> doors = Jobs.WorldScan.Doors();
            for (int i = 0; i < 8; i++)
            {
                Vector2 off = Random.insideUnitCircle * FreeTimeRules.Radius;
                var at = new Vector3(_anchor.x + off.x, me.y, _anchor.z + off.y);
                if (ZoneSystem.instance != null) at.y = ZoneSystem.instance.GetGroundHeight(at);
                if (Physics.Raycast(at + Vector3.up * 0.5f, Vector3.up, 20f, s_roofMask)) continue;   // under a roof: indoors
                float[] hall = _citizen.Record.ColonyPos;
                if (hall != null && hall.Length == 3
                    && Utils.DistanceXZ(at, Citizen.ToVector3(hall)) > Buildings.HallRules.RadiusOf(_citizen.ColonyId) - 2f) continue;   // inside the colony
                if (doors.Any(d => d != null && Utils.DistanceXZ(d.transform.position, at) < StrollDoorClearance)) continue;
                if (NearFire(at)) continue;   // not in a fire (full self-test run 6: one sat down in one and burned)
                if (Walls.StructureBetween(me, at)) continue;   // a wall between: the way there would go round or through a door
                if (_ai.CanWalkTo(at)) return at;
            }
            return me;
        }

        private void TickStroll(float now)
        {
            if (!_arrived)
            {
                if (!_ai.GoTo(_spot, 1f)) return;
                _ai.Idle();
                Arrive(now, Leisure.Stroll);
                return;
            }
            _ai.Idle();
            if (now >= _doUntil) _chooseAt = now;
        }

        private void TickSitDown(float now)
        {
            if (!_arrived)
            {
                _ai.Idle();
                if (EnvMan.IsWet())
                {
                    _chooseAt = now;
                    return;
                }
                _pose.SitGround();
                Arrive(now, Leisure.SitDown);
                return;
            }
            _ai.Idle();
            if (now >= _doUntil) _chooseAt = now;
        }

        private void Arrive(float now, Leisure leisure)
        {
            _arrived = true;
            _arrivedAt = now;
            _doUntil = now + FreeTimeRules.DoSeconds(leisure, s_rng.NextDouble());
            _chooseAt = Mathf.Max(_chooseAt, _doUntil);
        }

        // ---- Players ----

        private static readonly GreetMemory s_greeted = new GreetMemory();

        /// <summary>Standing, he turns to a player close by, and waves the first time he sees each player in a day.</summary>
        private void LookAtPlayerNear()
        {
            Player near = NearestPlayer(out float distance);
            if (near == null || distance > FreeTimeRules.LookAtPlayer) return;
            _ai.Face(near.transform.position);
            int today = EnvMan.instance != null && ZNet.instance != null ? EnvMan.instance.GetDay(ZNet.instance.GetTimeSeconds()) : 0;
            if (!s_greeted.ShouldGreet(_citizen.Id, near.GetPlayerID(), today, distance)) return;
            s_greeted.Mark(_citizen.Id, near.GetPlayerID(), today);
            if (_ai.IsFacing(near.transform.position) || distance < 4f) _pose.Gesture("emote_wave");
        }

        private Player NearestPlayer(out float distance)
        {
            distance = float.MaxValue;
            Player best = null;
            foreach (Player p in Player.GetAllPlayers())
            {
                if (p == null) continue;
                float d = Vector3.Distance(p.transform.position, _citizen.transform.position);
                if (d < distance)
                {
                    distance = d;
                    best = p;
                }
            }
            return best;
        }

        private void SaveDoing(string token, string partner, string partnerId = "")
        {
            string value = ActivityWords.Encode(token, partner, partnerId);
            if (value == _savedDoing) return;
            _savedDoing = value;
            ZNetView view = _citizen.GetComponent<ZNetView>();
            if (view != null && view.IsValid() && view.IsOwner()) view.GetZDO().Set(SaveKeys.Doing, value);
        }

        private static GroundPoint Ground(Vector3 v) => new GroundPoint(v.x, v.z);
    }
}
