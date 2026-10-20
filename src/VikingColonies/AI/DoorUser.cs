using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using VikingColonies.Buildings;
using VikingColonies.Citizens;
using VikingColonies.Config;
using VikingColonies.Core;
using Object = UnityEngine.Object;

namespace VikingColonies.AI
{
    /// <summary>
    /// A settler using doors. The game's walking paths treat a closed door as a wall (they are rebuilt from colliders every
    /// 10 s), so a settler whose goal is behind a closed door gets a path that stops short. Then he picks the colony door
    /// with the shortest way to his goal whose far side gets him closer to it, walks up to it from the side he can reach,
    /// opens it with the door's own RPC (everyone sees it), steps through without a path and goes on; a closed door he is
    /// walking straight through is opened too. He closes the doors he opened once he is past them, nobody stands in the
    /// doorway and no other settler is going through. Only doors in his colony's area that the colony's owner may open: no
    /// key, and no one else's ward.
    /// </summary>
    internal sealed class DoorUser
    {
        private enum Stage { None, Approach, Opening, Through }

        private sealed class Opened
        {
            public Door Door;
            public bool WentThrough;
            public float AskedAt = -1000f;   // when he last asked it to close
            public int Asks;
        }

        private const float AheadCheckSeconds = 0.25f;
        private const float RecheckSeconds = 2f;      // a goal out of reach is looked at again for a door after this long
        private const float CloseCheckSeconds = 0.25f;
        private const float ApproachSeconds = 12f;
        private float _makeWayUntil;
        private const float OpeningSeconds = 4f;
        private const float SwingSeconds = 0.4f;      // a door he opened is open enough to walk through
        private const float ThroughSeconds = 6f;      // lining up with the doorway, then the step through
        private const float NotAskedSeconds = 1f;     // his walk stopped asking: he isn't going through any more
        private const float GoalMoved = 2f;
        private const float PassMemorySeconds = 20f;  // the door he just went through isn't taken straight back for the same goal
        private const int DoorsPerCheck = 3;
        private const int MaxCloseAsks = 10;

        private static readonly FieldInfo s_allAreas = typeof(PrivateArea).GetField("m_allAreas", BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly MethodInfo s_isEnabled = typeof(PrivateArea).GetMethod("IsEnabled", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo s_isInside = typeof(PrivateArea).GetMethod("IsInside", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo s_isPermitted = typeof(PrivateArea).GetMethod("IsPermitted", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo s_areaPiece = typeof(PrivateArea).GetField("m_piece", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly List<Character> s_near = new List<Character>();
        private static readonly List<Door> s_candidates = new List<Door>();
        private static int s_groundMask;
        private static int s_wallMask;
        private static readonly Dictionary<Door, List<DoorUser>> s_passing = new Dictionary<Door, List<DoorUser>>();   // who is going through which door, on this computer

        private readonly CitizenAI _ai;
        private readonly Citizen _citizen;
        private readonly List<Opened> _opened = new List<Opened>();
        private readonly TargetBlacklist _failed = new TargetBlacklist(DoorRules.RetryAfter(failedPassing: true));
        private readonly TargetBlacklist _skipped = new TargetBlacklist(DoorRules.RetryAfter(failedPassing: false));
        private readonly DoorLoop _loop = new DoorLoop();
        private readonly TargetBlacklist _noWay = new TargetBlacklist(DoorLoop.NoWayFor);   // doors that brought him no closer
        private readonly HashSet<string> _explained = new HashSet<string>();   // goals whose door search went into the log
        private Stage _stage;
        private Door _door;
        private Vector3 _goal;
        private int _startSide;
        private float _standOff = DoorRules.StandOff;
        private int _waitIndex = -1;
        private bool _linedUp;
        private Vector3 _throughFrom;
        private float _throughCheckAt;
        /// <summary>The site of the hut this door belongs to, when he set off from inside it (<see cref="DoorRules.HutAround"/>).</summary>
        private Footprint _hutAround;
        private readonly StraightWalk _straightWalk = new StraightWalk();
        /// <summary>He walks straight to the door's spot for the rest of this approach (<see cref="DoorRules.WalkStraight"/>).</summary>
        private bool _straight;
        private float _closest;
        private float _closerAt;
        /// <summary>How far the door's spot was when he set off for it (DoorRules.ApproachGivesUp); NaN until his first step.</summary>
        private float _approachFrom = float.NaN;
        /// <summary>Coming this much nearer the door's spot counts as getting closer.</summary>
        private const float CloserBy = 0.1f;
        /// <summary>Stepping through, he hops when he hasn't moved this far in a second (a lip, a step, a log).</summary>
        private const float ThroughProgress = 0.25f;
        /// <summary>Someone this near where his step through lands is in its way.</summary>
        private const float LandingReach = 0.6f;
        /// <summary>Waiting spots straight back from the door are numbered from here (the side ones from 0).</summary>
        private const int BackOffIndex = 100;
        private bool _waitChosen;
        private Vector3? _waitAt;
        private bool _openedIt;
        /// <summary>He closed a door open towards him to open it from his side, this passing (0.20.1).</summary>
        private bool _reswung;
        /// <summary>When a player in the doorway started holding him up; -1 when none is (0.20.1).</summary>
        private float _playerWaitFrom = -1f;
        private float _askedAt;
        private float _stageSince;
        private float _passSince;
        private float _passStarted;   // when he set off for this door (turns waited out don't move it)
        private float _lastAsked;
        private float _aheadTimer;
        private Vector3 _checkedGoal;
        private float _checkedAt = -1000f;
        private float _closeTimer;
        private string _lastPass = "";
        private float _lastPassAt;

        public DoorUser(CitizenAI ai, Citizen citizen)
        {
            _ai = ai;
            _citizen = citizen;
        }

        /// <summary>He is going through a door right now.</summary>
        public bool Busy => _stage != Stage.None;

        /// <summary>His walk got where it was going: the doors he passed on the way were a trip, not a loop (DoorLoop).</summary>
        public void Arrived() => _loop.Arrived();

        /// <summary>How many times he went through a door.</summary>
        public int Passes { get; private set; }

        /// <summary>The door's open state: 0 closed, +1 or -1 by the side it was opened from (the game's UseDoor).</summary>
        private static int StateOf(Door door)
        {
            ZNetView nview = door != null ? door.GetComponent<ZNetView>() : null;
            return nview != null && nview.IsValid() ? nview.GetZDO().GetInt(ZDOVars.s_state) : 0;
        }

        public static bool IsOpen(Door door)
        {
            ZNetView nview = door != null ? door.GetComponent<ZNetView>() : null;
            return nview != null && nview.IsValid() && nview.GetZDO().GetInt(ZDOVars.s_state) != 0;
        }

        /// <summary>
        /// While he goes through a door on his way to <paramref name="goal"/>: walks him up to it, opens it and steps him
        /// through. True while busy (his walk to the goal waits); false when he isn't passing a door.
        /// </summary>
        public bool Passing(Vector3 goal)
        {
            if (_stage == Stage.None) return false;
            if (_door == null || Utils.DistanceXZ(goal, _goal) > GoalMoved)
            {
                Stop(failed: false);   // the door is gone, or he is going somewhere else now
                return false;
            }
            float now = Time.time;
            _lastAsked = now;
            if (DoorRules.PassingOver(now - _passStarted, now - _passSince))
            {
                Stop(failed: true, why: "it took too long");
                return false;
            }
            Vector3 me = _citizen.transform.position;
            Vector3 centre = _door.transform.position;
            float elapsed = now - _stageSince;
            switch (_stage)
            {
                case Stage.Approach:
                    if (SomeoneElseGoingThrough(_door) || OtherSideGoesFirst(_door))
                    {
                        WaitAside(me, centre);   // one at a time: two settlers in the doorway wedge each other
                        _stageSince = now;   // waiting his turn isn't failing to get there
                        _passSince = now;
                        NotCloserYet(now);
                        return true;
                    }
                    if (now < _makeWayUntil)   // out of the way of a settler in the doorway (the passing's own limit still runs)
                    {
                        WaitAside(me, centre);
                        _stageSince = now;
                        _passSince = now;
                        NotCloserYet(now);
                        return true;
                    }
                    _waitChosen = false;
                    _waitIndex = -1;
                    // He opens it only standing at it on his own side, so going through is a real crossing.
                    if (SideOf(_door, me) == _startSide && DoorRules.ReadyToOpen(Utils.DistanceXZ(me, centre), _standOff))
                    {
                        // Someone in the doorway who isn't going through with the door (a player, a settler walking through an open
                        // door or run by another computer): he waits for them, but not for ever (the approach clock runs on).
                        Vec3 landing = DoorRules.OnSide(V(centre), V(_door.transform.forward), -_startSide, DoorRules.StepBeyond);
                        Character occupant = InDoorway(centre, _citizen.Body) ?? OnSpot(new Vector3(landing.X, centre.y, landing.Z), _citizen.Body);
                        Vector3 landingAt = new Vector3(landing.X, centre.y, landing.Z);
                        bool hostileThere = HostileAt(centre, landingAt, _citizen.Body);   // the first one found may hide it
                        if (occupant is Player)
                        {
                            // A player standing in the doorway: a moment's wait, then he goes on through, pushing past as
                            // the game's characters do (0.20.1, as Hearthfolk; observation run 4: settlers gave the door up);
                            // a monster there too, he gives the door up (review of 0.20.1).
                            if (_playerWaitFrom < 0f) _playerWaitFrom = now;
                            switch (DoorRules.PlayerInDoorway(hostileThere, now - _playerWaitFrom))
                            {
                                case DoorwayChoice.PressOn:
                                    Log.Info($"{_citizen.CitizenName} presses on past {NameOf(occupant)} in the {Describe(_door)}");
                                    Next(Stage.Opening);
                                    return true;
                                case DoorwayChoice.Wait:
                                    _ai.Idle();
                                    _ai.Face(centre);
                                    _stageSince = now;   // waiting for the player doesn't count against getting to the door
                                    _passSince = now;
                                    return true;
                            }
                        }
                        else _playerWaitFrom = -1f;
                        if (occupant != null)
                        {
                            _ai.Idle();
                            _ai.Face(centre);
                            bool hostile = hostileThere || BaseAI.IsEnemy(_citizen.Body, occupant);
                            bool settler = occupant.GetComponent<Citizen>() != null;
                            bool person = occupant is Player || settler;
                            // Only one going through a door himself is waiting his turn; one standing there isn't (review).
                            bool passing = settler && (occupant.GetComponent<CitizenAI>()?.UsingDoor ?? false);
                            // Seen: a household going into its Longhouse at dusk, the last two gave up behind the others walking
                            // through. Someone walking through is a turn, not a block: the clock waits while he moves (a whole
                            // passing still has its limit). A monster at the door is never waited for.
                            if (DoorRules.GiveUpAtOnce(hostile)) Stop(failed: true, why: $"{NameOf(occupant)} is in the doorway");
                            else if (DoorRules.TakesATurn(person, passing, hostile, occupant.GetVelocity().magnitude))
                            {
                                // A settler going through may be waiting for the very spot he stands on: he steps aside a moment.
                                if (passing) _makeWayUntil = now + DoorRules.MakeWaySeconds;
                                _stageSince = now;
                                _passSince = now;
                            }
                            else if (elapsed > ApproachSeconds) Stop(failed: true, why: $"{NameOf(occupant)} stood in the doorway");
                            return true;
                        }
                        Next(Stage.Opening);
                        return true;
                    }
                    Vector3 stand = Spot(_door, _startSide, _standOff);
                    float toSpot = Utils.DistanceXZ(me, stand);
                    if (toSpot < _closest - CloserBy)
                    {
                        _closest = toSpot;
                        _closerAt = now;
                        _passSince = now;   // getting closer is progress: the passing's own limit waits (the whole-mod review of 1.4.1)
                    }
                    // Inside a hut turned off the compass the paths may give him no way to his own door's spot, or one into the
                    // wall: he walks there straight, hopping over what stops him, as through the doorway (observation runs 2
                    // and 3, 0.20: the farmer stood still 3 m from it in the turned Farm, the builder 1.5 m from his in the
                    // Longhouse).
                    if (!_straight)
                    {
                        bool wayEnded = _ai.WalkTowards(stand, 0.5f);
                        _straight = toSpot > 0.5f && DoorRules.WalkStraight(_hutAround != null, wayEnded, now - _closerAt);
                        if (_straight)
                        {
                            Log.Info($"{_citizen.CitizenName} walks straight to the spot in front of the {Describe(_door)} at {At(stand)}: " +
                                     $"inside its hut the paths {(wayEnded ? "give him no way there" : "bring him no closer")} (he stands at {At(me)})");
                            _straightWalk.Start(me.x, me.z, now, DoorRules.AsideFromMiddle(V(me), V(stand), _hutAround.Centre));
                        }
                    }
                    if (_straight) WalkStraight(me, stand, now, toSpot);
                    if (float.IsNaN(_approachFrom)) _approachFrom = toSpot;
                    // He gets no closer, or his time for the way there is up (1.4.1: a flat 12 s cut off settlers walking well
                    // to a door 40-60 m off).
                    if (DoorRules.ApproachGivesUp(elapsed, now - _closerAt, _approachFrom))
                        Stop(failed: true, why: $"he couldn't get to the spot in front of it at {At(stand)} (he stands at {At(me)}, " +
                                                $"{(now - _closerAt > DoorRules.ApproachStallSeconds ? "getting no closer" : "out of time")})");
                    return true;
                case Stage.Opening:
                    _ai.Idle();
                    _ai.Face(centre);
                    if (IsOpen(_door))
                    {
                        // Opened from the far side, its leaf swings towards him and stands in the doorway; opening it again
                        // does nothing, so he closes it and opens it from his side, once (0.20.1, as Valheim Villages;
                        // observation run 5: "couldn't step through … in his way: wood_door").
                        if (_reswung && !_openedIt && !DoorRules.AskAgain(now - _askedAt)) return true;   // let it close first
                        if (DoorRules.MayReswing(_openedIt, _reswung, StateOf(_door), _startSide, DoorwayClear(centre, _citizen.Body)))
                        {
                            Log.Info($"{_citizen.CitizenName} re-swings the {Describe(_door)}: it was open towards him");
                            Use(_door, me);   // closes it; it opens from his side on the next ask
                            _reswung = true;
                            _askedAt = now;
                            _stageSince = now;
                            return true;
                        }
                        if (!_openedIt || now - _askedAt >= SwingSeconds) Next(Stage.Through);
                        return true;
                    }
                    if (DoorRules.AskAgain(now - _askedAt))   // asked again while it ignores him (still swinging)
                    {
                        if (!_openedIt) Log.Info($"{_citizen.CitizenName} opens the {Describe(_door)} standing at {At(me)}");
                        Use(_door, me);
                        _askedAt = now;
                        _openedIt = true;
                    }
                    if (elapsed > OpeningSeconds) Stop(failed: true, why: "it didn't open");
                    return true;
                default:   // Through: a straight step, as the paths don't know the doorway is open yet
                    if (SideOf(_door, me) == -_startSide && Utils.DistanceXZ(me, centre) >= DoorRules.StandOff)
                    {
                        Log.Info($"{_citizen.CitizenName} went through the {Describe(_door)}; now at {At(me)}");
                        Passes++;
                        Stop(failed: false);
                        return false;
                    }
                    if (!IsOpen(_door))
                    {
                        Next(Stage.Opening);   // someone closed it on him: he opens it again
                        return true;
                    }
                    Vector3 fromMiddle = me - centre, across = _door.transform.forward;
                    fromMiddle.y = across.y = 0f;
                    across.Normalize();
                    if ((fromMiddle - across * Vector3.Dot(fromMiddle, across)).magnitude <= DoorRules.LineUpSideways) _linedUp = true;
                    Vec3 step = DoorRules.ThroughStep(V(centre), V(_door.transform.forward), _startSide, V(me), _linedUp);
                    var stepTo = new Vector3(step.X, me.y, step.Z);
                    if (elapsed > ThroughSeconds)
                    {
                        Stop(failed: true, why: $"he couldn't step through (he stands at {At(me)}, {Utils.DistanceXZ(me, centre):0.0} m from its " +
                                                $"middle, {me.y - centre.y:+0.0;-0.0} m in height; {InTheWay(me, stepTo)})");
                        return false;
                    }
                    StepStraight(me, stepTo, now);
                    return true;
            }
        }

        /// <summary>
        /// He is walking to <paramref name="goal"/>. When his way ends short of it (<paramref name="unreachable"/>), or a
        /// closed door is right ahead on his way, he goes through a colony door. True when he now is. <paramref name="early"/>:
        /// he hasn't walked that way yet, and only a door near him counts (<see cref="DoorRules.EarlyLookReach"/>).
        /// </summary>
        public bool Consider(Vector3 goal, bool unreachable, float dt, bool early = false, float gapFromHere = float.NaN)
        {
            if (_stage != Stage.None) return false;
            float now = Time.time;
            Vector3 me = _citizen.transform.position;
            Door chosen;
            int side;
            if (unreachable)
            {
                if (Utils.DistanceXZ(goal, _checkedGoal) < 1f && now - _checkedAt < RecheckSeconds) return false;
                chosen = DoorTowards(goal, me, early, gapFromHere, out side, out _standOff);
                if (chosen == null && !early)   // looking early, a door far off may still lead there from the way's end
                {
                    _checkedGoal = goal;   // no door leads there: don't look again for a moment
                    _checkedAt = now;
                }
            }
            else
            {
                _aheadTimer -= dt;
                if (_aheadTimer > 0f) return false;
                _aheadTimer = AheadCheckSeconds;
                chosen = DoorAhead(goal, me, out side);
                _standOff = DoorRules.StandOff;
            }
            if (chosen == null) return false;
            _door = chosen;
            _goal = goal;
            _startSide = side;
            _reswung = false;
            _playerWaitFrom = -1f;
            _hutAround = unreachable ? DoorRules.HutAround(V(chosen.transform.position), V(chosen.transform.forward), V(me), s_huts, Construction.SiteSurvey.SiteMargin) : null;
            _straight = false;
            NotCloserYet(now);
            _openedIt = false;
            _askedAt = -1000f;
            _passSince = now;
            _passStarted = now;
            _lastAsked = now;
            ForgetGoneDoors();
            if (!s_passing.TryGetValue(chosen, out List<DoorUser> users)) s_passing[chosen] = users = new List<DoorUser>();
            if (!users.Contains(this)) users.Add(this);
            Next(Stage.Approach);
            _ai.ForgetPath();   // his kept path still leads to his goal, not to the door
            Log.Info($"{_citizen.CitizenName} goes through the {Describe(chosen)} on his way to {At(goal)} " +
                     $"({(unreachable ? "his way ends short of it" : "it is closed in front of him")}); he stands at {At(me)}, " +
                     $"goes in from {At(Spot(chosen, side, _standOff))}");
            return true;
        }

        /// <summary>Closes the doors he opened once he is past them (or, if he never went through, well away from them).</summary>
        public void Tick(float dt)
        {
            float now = Time.time;
            if (_stage != Stage.None && now - _lastAsked > NotAskedSeconds) Stop(failed: false);   // his job stopped walking him
            if (_opened.Count == 0) return;
            _closeTimer -= dt;
            if (_closeTimer > 0f) return;
            _closeTimer = CloseCheckSeconds;
            Vector3 me = _citizen.transform.position;
            for (int i = _opened.Count - 1; i >= 0; i--)
            {
                Opened opened = _opened[i];
                if (opened.Door == null || !IsOpen(opened.Door))
                {
                    _opened.RemoveAt(i);   // gone, or closed (by him or anyone)
                    continue;
                }
                if (opened.Door == _door || !DoorRules.AskAgain(now - opened.AskedAt)) continue;
                Vector3 centre = opened.Door.transform.position;
                if (!DoorRules.ShouldClose(opened.WentThrough, Utils.DistanceXZ(me, centre), DoorwayClear(centre), OthersPassing(opened.Door))) continue;
                if (opened.Asks >= MaxCloseAsks)
                {
                    Log.Warn($"{_citizen.CitizenName} couldn't close the {Describe(opened.Door)}; he leaves it");
                    _opened.RemoveAt(i);
                    continue;
                }
                if (opened.Asks == 0) Log.Info($"{_citizen.CitizenName} closes the {Describe(opened.Door)} behind him");
                Use(opened.Door, me);   // the door's own RPC closes an open door; it stays on the list until it reads closed
                opened.AskedAt = now;
                opened.Asks++;
            }
        }

        /// <summary>He stops whatever door he was going through (his job changed).</summary>
        public void Reset()
        {
            if (_stage != Stage.None) Stop(failed: false);
        }

        private void Next(Stage stage)
        {
            if (stage == Stage.Through) _linedUp = false;
            _throughFrom = _citizen.transform.position;
            _throughCheckAt = Time.time;
            _stage = stage;
            _stageSince = Time.time;
        }

        /// <summary>
        /// Straight to the door's spot inside its hut: a hop over a lip, a step aside round what a hop doesn't clear (the
        /// hut's chest in its middle), then straight on (<see cref="StraightWalk"/>).
        /// </summary>
        private void WalkStraight(Vector3 me, Vector3 spot, float now, float toSpot)
        {
            if (toSpot <= 0.3f)
            {
                _ai.Idle();
                return;
            }
            switch (_straightWalk.Next(me.x, me.z, now))
            {
                case UnstuckMove.Hop:
                    _ai.Hop();
                    break;
                case UnstuckMove.Sidestep:
                    Vector3 way = spot - me;
                    way.y = 0f;
                    Vector3 right = Vector3.Cross(Vector3.up, way.normalized);
                    _ai.StepTowards(me + right * _straightWalk.Side);
                    return;
            }
            _ai.StepTowards(spot);
        }

        /// <summary>The clock for getting closer to the door's spot starts again (setting off, or after waiting his turn).</summary>
        private void NotCloserYet(float now)
        {
            _closest = float.MaxValue;
            _closerAt = now;
            _approachFrom = float.NaN;
        }

        /// <summary>A straight step without a path; a lip, a step or a log stops it: he hops over it, as a player would.</summary>
        private void StepStraight(Vector3 me, Vector3 to, float now)
        {
            if (now - _throughCheckAt >= 1f)
            {
                if (Utils.DistanceXZ(me, _throughFrom) < ThroughProgress) _ai.Hop();
                _throughFrom = me;
                _throughCheckAt = now;
            }
            _ai.StepTowards(to);
        }

        private void Stop(bool failed, string why = null)
        {
            _makeWayUntil = 0f;
            if (_door != null)
            {
                string key = Key(_door, _goal);
                bool through = SideOf(_door, _citizen.transform.position) == -_startSide;
                if (failed)
                {
                    _failed.Add(key, Time.time);
                    Log.Info($"{_citizen.CitizenName} gave up going through the {Describe(_door)}: {why}");
                }
                else if (through)
                {
                    _lastPass = key;
                    _lastPassAt = Time.time;
                    // Through it again and no closer: it's no way there, and his job's own stuck clock takes over (each
                    // pass had reset it; the real builders' run, 2026-10-18: 92 passes from a roof).
                    if (_loop.Passed(key, Vector3.Distance(_citizen.transform.position, _goal), Time.time))
                    {
                        _noWay.Add(key, Time.time);
                        Log.Info($"{_citizen.CitizenName} keeps going through the {Describe(_door)} without getting closer to " +
                                 $"{At(_goal)}: no way there through it for now");
                    }
                }
                Opened known = _opened.FirstOrDefault(o => o.Door == _door);
                if (known != null) known.WentThrough |= through;
                else if (_openedIt && !_door.m_canNotBeClosed) _opened.Add(new Opened { Door = _door, WentThrough = through });
            }
            LeaveList(_door);
            _stage = Stage.None;
            _door = null;
            _waitChosen = false;
            _waitIndex = -1;
            _ai.ForgetPath();   // from this side of the door the way looks different
        }

        /// <summary>Off the list of who is going through the door (the whole-mod review of 1.4.1: the list kept him).</summary>
        private void LeaveList(Door door)
        {
            if (door == null || !s_passing.TryGetValue(door, out List<DoorUser> users)) return;
            users.Remove(this);
            if (users.Count == 0) s_passing.Remove(door);
        }

        private static readonly List<Door> s_goneDoors = new List<Door>();

        /// <summary>Doors destroyed or unloaded (a world left, a door broken) leave the list with whoever was at them.</summary>
        private static void ForgetGoneDoors()
        {
            s_goneDoors.Clear();
            foreach (KeyValuePair<Door, List<DoorUser>> pair in s_passing)
                if (pair.Key == null || pair.Value.TrueForAll(u => u._citizen == null)) s_goneDoors.Add(pair.Key);
            foreach (Door gone in s_goneDoors) s_passing.Remove(gone);
        }

        /// <summary>
        /// The door to go through for a goal his way ends short of: near him or near the goal, the shortest way among those
        /// whose far side gets him closer to the goal, closed or open; and the side he goes through it from (the side he
        /// can walk to). A door that doesn't fit from where he stands is tried again a few seconds later.
        /// </summary>
        private Door DoorTowards(Vector3 goal, Vector3 me, bool early, float gapFromHere, out int side, out float standOff)
        {
            side = 0;
            standOff = DoorRules.StandOff;
            s_candidates.Clear();
            if (!ColonyAccess(out Vector3 hall, out long owner)) return null;
            // The first search for a goal says in the log why each door near it didn't fit.
            string goalKey = $"{Mathf.RoundToInt(goal.x)},{Mathf.RoundToInt(goal.z)}";
            if (_explained.Count > 64) _explained.Clear();
            StringBuilder why = !early && _explained.Add(goalKey) ? new StringBuilder() : null;
            // Heading home into the colony: the door beside him may be his way out wherever the colony ends (DoorRules.MayLeaveBy).
            bool goalInColony = DoorRules.InColonyArea(Utils.DistanceXZ(goal, hall), HallRules.RadiusOf(_citizen.ColonyId), NearestHut(goal));
            Cover.GetCoverForPoint(me + Vector3.up, out float cover, out bool underRoof);   // inside something: the door beside him is his way out
            bool sheltered = DoorRules.Sheltered(underRoof, cover);   // inside a room, not under eaves or a porch
            float nearestDoor = float.MaxValue;   // sheltered, the nearest door on his floor is the door of his room (DoorRules.RoomReach)
            Door roomDoor = null;
            foreach (Door door in Jobs.WorldScan.Doors())
            {
                if (door == null || Mathf.Abs(door.transform.position.y - me.y) > RoomFloorHeight) continue;
                float away = Utils.DistanceXZ(door.transform.position, me);
                if (away < nearestDoor)
                {
                    nearestDoor = away;
                    roomDoor = door;
                }
            }
            if (!sheltered || nearestDoor > DoorRules.RoomReach) roomDoor = null;
            foreach (Door door in Jobs.WorldScan.Doors())
            {
                if (door == null) continue;
                Vector3 at = door.transform.position;
                if (Utils.DistanceXZ(at, me) > DoorRules.SearchRange && Utils.DistanceXZ(at, goal) > DoorRules.SearchRange) continue;
                if (early && !DoorRules.NearEnoughToLookEarly(Utils.DistanceXZ(at, me))) continue;
                if (Blocked(Key(door, goal)))
                {
                    why?.Append($"; {Describe(door)}: tried lately");
                    continue;
                }
                string refusal = Refusal(door, hall, owner, goalInColony, underRoof, door == roomDoor, sheltered);
                if (refusal != null)
                {
                    why?.Append($"; {Describe(door)}: {refusal}");
                    continue;
                }
                s_candidates.Add(door);
            }
            KeepOwnDoors(me, goal, why, roomDoor);
            for (int tries = 0; tries < DoorsPerCheck && s_candidates.Count > 0; tries++)
            {
                int index = DoorRules.ChooseDoor(s_candidates.Select(d => V(d.transform.position)).ToList(), V(me), V(goal));
                if (index < 0) break;
                Door door = s_candidates[index];
                s_candidates.RemoveAt(index);
                float frontOff = ReachableStandOff(door, 1), backOff = ReachableStandOff(door, -1);
                Vector3 front = Spot(door, 1, frontOff > 0f ? frontOff : DoorRules.StandOff), back = Spot(door, -1, backOff > 0f ? backOff : DoorRules.StandOff);
                bool frontReachable = frontOff > 0f, backReachable = backOff > 0f;
                side = DoorRules.SideToUse(frontReachable, backReachable);
                // Inside a hut, or going into one, its shape says which side he starts from: in a hut turned off the compass
                // the walking map can be wrong there (worker polish, 0.20).
                int byHut = DoorRules.SideFromHut(V(door.transform.position), V(door.transform.forward), V(me), V(goal), s_huts,
                    Construction.SiteSurvey.SiteMargin);
                // Not for a far door he can walk to neither side of from outside its hut (1.4.1, DoorRules.HutSideApplies).
                bool insideItsHut = DoorRules.HutAround(V(door.transform.position), V(door.transform.forward), V(me), s_huts, Construction.SiteSurvey.SiteMargin) != null;
                if (byHut != 0 && !DoorRules.HutSideApplies(frontReachable, backReachable, Utils.DistanceXZ(door.transform.position, me), insideItsHut,
                        outInTheOpen: !underRoof)) byHut = 0;
                if (byHut != 0 && byHut != side)
                {
                    why?.Append($"; {Describe(door)}: the hut's shape says he starts from side {byHut} (the walking map said {side})");
                    Log.Info($"{_citizen.CitizenName} at {At(me)}: the hut's shape puts him on side {byHut} of the {Describe(door)} (the walking map said {side})");
                }
                if (byHut != 0) side = byHut;
                if (side != 0)
                {
                    standOff = side == 1 ? frontOff : backOff;
                    if (standOff <= 0f) standOff = DoorRules.StandOff;
                    Vector3 beyond = side == 1 ? Spot(door, -1, DoorRules.StandOff) : Spot(door, 1, DoorRules.StandOff);
                    bool reaches = _ai.WayReaches(beyond, goal);
                    if (!reaches && float.IsNaN(gapFromHere)) gapFromHere = _ai.WayEndGap(me, goal);
                    float gapBeyond = reaches ? 0f : _ai.WayEndGap(beyond, goal);
                    if (DoorRules.LeadsTowards(gapFromHere, gapBeyond, reaches)) return door;
                    why?.Append($"; {Describe(door)}: beyond it the way ends {gapBeyond:0.0} m from it, from here {gapFromHere:0.0} m");
                }
                else
                {
                    float doorY = door.transform.position.y;
                    why?.Append($"; {Describe(door)}: he can reach {(frontReachable ? "both sides" : "neither side")} of it " +
                                $"(spots {front.y - doorY:+0.0;-0.0} and {back.y - doorY:+0.0;-0.0} m from its middle)");
                }
                _skipped.Add(Key(door, goal), Time.time);   // it doesn't stand between him and his goal, from here
                side = 0;
            }
            if (why != null && why.Length > 0) Log.Info($"{_citizen.CitizenName} can't get right to ({goal.x:0}, {goal.z:0}) and no door leads there{why}");
            return null;
        }

        private static readonly List<Footprint> s_huts = new List<Footprint>();

        /// <summary>A door more than this above or below him is on another floor, not his room's (the review of the game-check fixes).</summary>
        private const float RoomFloorHeight = 2.5f;

        /// <summary>
        /// A goal inside a hut is reached through that hut's own doors, and he leaves the hut he is in by its own
        /// (<see cref="DoorRules.OwnDoors"/>; self-test run 42: the farmer went through a neighbour's door to the Farm's chest).
        /// </summary>
        private static void KeepOwnDoors(Vector3 me, Vector3 goal, StringBuilder why, Door roomDoor = null)
        {
            if (s_candidates.Count == 0) return;
            s_huts.Clear();
            foreach (ColonyBuilding building in ColonyBuilding.All)
            {
                if (building == null || !building.IsValid) continue;
                Vector3 at = building.transform.position;
                if (Utils.DistanceXZ(at, me) > HutNear && Utils.DistanceXZ(at, goal) > HutNear) continue;
                Construction.ConstructionModule construction = building.GetComponent<Construction.ConstructionModule>();
                // The Town Hall's camp has no walls or doors: a goal in it isn't behind a hut's door (the review of
                // 2026-10-19: a palisade gate on the way was dropped, and a walled colony lost its way to the hall).
                if (construction == null || construction.IsCamp) continue;
                Footprint site = construction.RoomFootprint;   // inside the hut is within its walls, not its whole outline
                if (site != null) s_huts.Add(site);
            }
            List<int> own = DoorRules.OwnDoors(s_candidates.Select(d => V(d.transform.position)).ToList(), V(me), V(goal), s_huts, Construction.SiteSurvey.SiteMargin,
                roomDoor != null ? s_candidates.IndexOf(roomDoor) : -1);
            if (own.Count == s_candidates.Count) return;
            List<Door> kept = own.Select(i => s_candidates[i]).ToList();
            foreach (Door other in s_candidates.Where(d => !kept.Contains(d))) why?.Append($"; {Describe(other)}: not the door of the hut it's in");
            s_candidates.Clear();
            s_candidates.AddRange(kept);
        }

        /// <summary>Huts this near him or his goal are looked at for <see cref="KeepOwnDoors"/>.</summary>
        private const float HutNear = 40f;

        /// <summary>A closed door he is walking straight through, with his goal beyond it, that he may open; and the side he is on.</summary>
        private Door DoorAhead(Vector3 goal, Vector3 me, out int side)
        {
            side = 0;
            Vector3 heading = _ai.WayHeading();   // his path's way, not the way he faces after a hop (0.20.1)
            foreach (Door door in Jobs.WorldScan.Doors())
            {
                if (door == null) continue;
                Vector3 at = door.transform.position;
                if (Utils.DistanceXZ(at, me) > DoorRules.AheadReach) continue;
                if (!DoorRules.IsOnWayThrough(V(me), V(heading), V(at), V(door.transform.forward), V(goal)) || IsOpen(door)) continue;
                if (Blocked(Key(door, goal)) || !ColonyAccess(out Vector3 hall, out long owner) || Refusal(door, hall, owner) != null) continue;
                side = SideOf(door, me);
                return door;
            }
            return null;
        }

        /// <summary>The nearest stand-off on that side whose spot he can walk to; 0 when none is.</summary>
        private float ReachableStandOff(Door door, int side)
        {
            foreach (float distance in DoorRules.StandOffs)
                if (_ai.CanWalkTo(Spot(door, side, distance))) return distance;
            return 0f;
        }

        /// <summary>
        /// His turn hasn't come: he stands aside, out of the way in and out, each waiter on his own spot beside the door
        /// (one he can walk to with no wall between it and the door), or where he is when there is none.
        /// </summary>
        private void WaitAside(Vector3 me, Vector3 centre)
        {
            if (!_waitChosen)
            {
                _waitChosen = true;
                _waitAt = ChooseWaitSpot(FreeWaitIndex(_door));
            }
            if (_waitAt.HasValue && Utils.DistanceXZ(me, _waitAt.Value) > 1f) _ai.WalkPlain(_waitAt.Value, 0.5f);
            else
            {
                _ai.Idle();
                _ai.Face(centre);
            }
        }

        private Vector3? ChooseWaitSpot(int first)
        {
            if (s_wallMask == 0) s_wallMask = LayerMask.GetMask("piece");
            Vector3 stand = Spot(_door, _startSide, _standOff), right = _door.transform.right;
            for (int i = first; i < first + 6; i++)
            {
                if (WaitIndexTaken(_door, i)) continue;
                float offset = (i % 2 == 0 ? 1f : -1f) * (DoorRules.WaitAside + (i / 2) * 0.9f);
                Vector3 spot = stand + right * offset;
                if (Physics.Linecast(stand + Vector3.up * 0.5f, spot + Vector3.up * 0.5f, s_wallMask, QueryTriggerInteraction.Ignore)) continue;
                if (!_ai.CanWalkTo(spot)) continue;
                _waitIndex = i;
                return spot;
            }
            // Walls on both sides: straight back from the door, off the landing of whoever comes the other way.
            for (int i = 0; i < 3; i++)
            {
                int index = BackOffIndex + i;
                if (WaitIndexTaken(_door, index)) continue;
                Vector3 spot = Spot(_door, _startSide, DoorRules.BackOff(i));
                if (Physics.Linecast(stand + Vector3.up * 0.5f, spot + Vector3.up * 0.5f, s_wallMask, QueryTriggerInteraction.Ignore)) continue;
                if (!_ai.CanWalkTo(spot)) continue;
                _waitIndex = index;
                return spot;
            }
            _waitIndex = -1;
            return null;
        }

        /// <summary>The lowest waiting spot at this door no other waiter holds.</summary>
        private int FreeWaitIndex(Door door)
        {
            int i = 0;
            while (WaitIndexTaken(door, i)) i++;
            return i;
        }

        private bool WaitIndexTaken(Door door, int index)
        {
            if (!s_passing.TryGetValue(door, out List<DoorUser> users)) return false;
            foreach (DoorUser u in users)
                if (u != this && u._waitChosen && u._waitIndex == index && u._startSide == _startSide) return true;
            return false;
        }

        /// <summary>
        /// Someone on the other side is coming through this door too, and goes first (the lower id): he waits aside till
        /// that one is through, or they'd each wait for the other to leave the doorway.
        /// </summary>
        private bool OtherSideGoesFirst(Door door)
        {
            if (!OthersPassing(door)) return false;
            foreach (DoorUser u in s_passing[door])
                if (u != this && u._startSide == -_startSide && string.CompareOrdinal(u._citizen.Id, _citizen.Id) < 0) return true;
            return false;
        }

        /// <summary>Another settler (on this computer) is opening this door or stepping through it.</summary>
        private bool SomeoneElseGoingThrough(Door door)
        {
            if (!OthersPassing(door)) return false;
            foreach (DoorUser u in s_passing[door])
                if (u != this && (u._stage == Stage.Opening || u._stage == Stage.Through)) return true;
            return false;
        }

        /// <summary>
        /// Another settler on this computer is going through this door right now. One that was unloaded, taken over by
        /// another computer or stopped half way doesn't count once his walk stops asking.
        /// </summary>
        private bool OthersPassing(Door door)
        {
            if (!s_passing.TryGetValue(door, out List<DoorUser> users)) return false;
            float now = Time.time;
            bool others = false;
            for (int i = users.Count - 1; i >= 0; i--)
            {
                DoorUser u = users[i];
                if (u._door != door || u._stage == Stage.None || DoorRules.PassingOver(now - u._passStarted, now - u._passSince) || now - u._lastAsked > NotAskedSeconds)
                {
                    users.RemoveAt(i);
                    continue;
                }
                if (u != this) others = true;
            }
            if (users.Count == 0) s_passing.Remove(door);
            return others;
        }

        private bool Blocked(string key) =>
            _failed.IsBlocked(key, Time.time) || _skipped.IsBlocked(key, Time.time) || _noWay.IsBlocked(key, Time.time) ||
            (key == _lastPass && Time.time - _lastPassAt < PassMemorySeconds);

        /// <summary>Where his colony's Town Hall stands and who owns the colony (the Town Hall's builder). False without a Town Hall.</summary>
        private bool ColonyAccess(out Vector3 hallAt, out long owner)
        {
            ColonyBuilding hall = ColonyBuilding.FindTownHall(_citizen.ColonyId);
            owner = ColonyBuilding.OwnerOfColony(_citizen.ColonyId);
            if (hall != null)
            {
                hallAt = hall.transform.position;
                return true;
            }
            // The Town Hall is gone (the colony lives on for a while): where it stood, and its owner as the huts remember.
            float[] last = _citizen.Record.ColonyPos;
            hallAt = last != null && last.Length == 3 ? new Vector3(last[0], last[1], last[2]) : Vector3.zero;
            return _citizen.Record.HasColony && last != null && last.Length == 3;
        }

        /// <summary>
        /// Why he may not open this door, or null when he may: in his colony's area (or by one of its huts), no key, and no
        /// ward that the colony's owner isn't on.
        /// </summary>
        private string Refusal(Door door, Vector3 hallAt, long owner, bool goalInColony = false, bool underRoof = false, bool nearestToHim = false,
                               bool sheltered = false)
        {
            Vector3 at = door.transform.position;
            // The door beside him (or, under a roof, the nearest one, across his room) is his way out of where he stands, on his
            // way home into the colony or from under a roof, wherever the colony ends (1.4.1, DoorRules.MayLeaveBy); not a
            // stranger's door he only passes (DoorAhead).
            bool inColony = DoorRules.MayLeaveBy(Utils.DistanceXZ(at, _citizen.transform.position), goalInColony, underRoof, nearestToHim, sheltered)
                            || DoorRules.InColonyArea(Utils.DistanceXZ(at, hallAt), HallRules.RadiusOf(_citizen.ColonyId), NearestHut(at));
            if (!inColony) return "outside the colony's area";
            bool needsKey = door.m_keyItem != null;
            if (needsKey) return "it needs a key";
            if (!DoorRules.MayOpen(inColony, needsKey, OtherWard(at, owner))) return "under a ward the colony's owner isn't on";   // the costly check last
            return null;
        }

        /// <summary>How far the nearest of his colony's buildings is.</summary>
        private float NearestHut(Vector3 at)
        {
            float nearest = float.MaxValue;
            string colony = _citizen.ColonyId;
            foreach (ColonyBuilding building in ColonyBuilding.All)
                if (building != null && building.ColonyId == colony) nearest = Mathf.Min(nearest, Utils.DistanceXZ(at, building.transform.position));
            return nearest;
        }

        /// <summary>An enabled ward covers this spot and the colony's owner isn't its owner or permitted on it.</summary>
        private static bool OtherWard(Vector3 at, long owner)
        {
            if (!(s_allAreas?.GetValue(null) is List<PrivateArea> areas)) return false;
            foreach (PrivateArea area in areas)
            {
                if (area == null || !(bool)s_isEnabled.Invoke(area, null) || !(bool)s_isInside.Invoke(area, new object[] { at, 0f })) continue;
                if (s_areaPiece.GetValue(area) is Piece piece && piece.GetCreator() == owner) continue;
                if ((bool)s_isPermitted.Invoke(area, new object[] { owner })) continue;
                return true;
            }
            return false;
        }

        /// <summary>Nobody but <paramref name="except"/> stands where his step through will land.</summary>
        private static bool SpotClear(Vector3 at, Character except) => OnSpot(at, except) == null;

        /// <summary>Someone standing on a spot (measured across the floor), or null.</summary>
        private static Character OnSpot(Vector3 at, Character except)
        {
            // Measured across the floor: the spot is at the door's middle height, a metre above anyone's feet. Seen in review:
            // measured straight, no one ever stood on it.
            s_near.Clear();
            Character.GetCharactersInRange(at, LandingReach + 2.5f, s_near);
            foreach (Character character in s_near)
                if (character != null && character != except && Utils.DistanceXZ(character.transform.position, at) < LandingReach &&
                    Mathf.Abs(character.transform.position.y - at.y) < 2.5f) return character;
            return null;
        }

        /// <summary>A monster in the doorway or where his step through lands (review of 0.20.1: a player there hid it).</summary>
        private static bool HostileAt(Vector3 centre, Vector3 landing, Character me)
        {
            s_near.Clear();
            Character.GetCharactersInRange(centre, DoorRules.DoorwayReach + 2.5f, s_near);
            foreach (Character character in s_near)
                if (character != null && character != me && BaseAI.IsEnemy(me, character)
                    && (DoorRules.InDoorway(V(centre), V(character.transform.position)) || Utils.DistanceXZ(character.transform.position, landing) <= LandingReach))
                    return true;
            return false;
        }

        /// <summary>Someone in the doorway or waiting by it (measured across the floor), or null.</summary>
        private static Character InDoorway(Vector3 centre, Character except)
        {
            s_near.Clear();
            Character.GetCharactersInRange(centre, DoorRules.DoorwayReach + 2.5f, s_near);
            foreach (Character character in s_near)
                if (character != null && character != except && DoorRules.InDoorway(V(centre), V(character.transform.position))) return character;
            return null;
        }

        private static string NameOf(Character character) =>
            character.GetComponent<Citizen>()?.CitizenName ?? (character is Player player ? player.GetPlayerName() : Utils.GetPrefabName(character.gameObject));

        /// <summary>Nobody stands in the doorway or waits by it (measured across the floor, so a floor above doesn't count).</summary>
        /// <param name="except">Someone who doesn't count (the settler asking).</param>
        private static bool DoorwayClear(Vector3 centre, Character except = null)
        {
            s_near.Clear();
            Character.GetCharactersInRange(centre, DoorRules.DoorwayReach + 2.5f, s_near);
            foreach (Character character in s_near)
                if (character != null && character != except && DoorRules.InDoorway(V(centre), V(character.transform.position))) return false;
            return true;
        }

        /// <summary>Self-test: closes a door left open (by a settler the test removed before he could close it).</summary>
        internal static void CloseForTest(Door door)
        {
            if (door != null && IsOpen(door)) Use(door, door.transform.position + door.transform.forward);
        }

        /// <summary>The door's own RPC: opens a closed door away from him, closes an open one. Runs on the door's owner.</summary>
        private static void Use(Door door, Vector3 from)
        {
            ZNetView nview = door.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid()) return;
            bool forward = Vector3.Dot(door.transform.forward, from - door.transform.position) < 0f;   // as the game's Door.Open
            nview.InvokeRPC("UseDoor", forward);
        }

        private static int SideOf(Door door, Vector3 point) => DoorRules.SideOf(V(door.transform.position), V(door.transform.forward), V(point));

        /// <summary>
        /// A spot on one side of the door, on whatever is underfoot there: the floor, a stair, the ground (a door on a raised
        /// floor can be well above the ground in front of it).
        /// </summary>
        internal static Vector3 Spot(Door door, int side, float distance)
        {
            Vector3 at = door.transform.position;
            Vec3 spot = DoorRules.OnSide(V(at), V(door.transform.forward), side, distance);
            var point = new Vector3(spot.X, at.y, spot.Z);
            if (s_groundMask == 0) s_groundMask = LayerMask.GetMask("piece", "terrain", "Default", "static_solid", "Default_small");
            if (Physics.Raycast(point + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 6f, s_groundMask, QueryTriggerInteraction.Ignore))
                point.y = hit.point.y;
            return point;
        }

        private static string At(Vector3 point) => $"({point.x:0.0}, {point.y:0.0}, {point.z:0.0})";

        /// <summary>
        /// vc_doors: how a settler sees a door: the spots he would stand on at either side, at a few distances, their height
        /// against the door's middle, and whether his way reaches each (or how far short it ends).
        /// </summary>
        internal static string Probe(Door door, CitizenAI ai)
        {
            var text = new StringBuilder($"{Describe(door)}, open: {IsOpen(door)}");
            float doorY = door.transform.position.y;
            foreach (int side in new[] { 1, -1 })
            {
                text.Append($"\n  side {side:+0;-0}:");
                foreach (float distance in new[] { DoorRules.StandOff, 2f, 2.8f, 3.6f })
                {
                    Vector3 spot = Spot(door, side, distance);
                    float gap = ai.CanWalkTo(spot) ? 0f : ai.WayEndGap(ai.transform.position, spot);
                    text.Append($" {distance:0.0} m (height {spot.y - doorY:+0.0;-0.0}): " +
                                (gap <= 0f ? "reachable" : gap == float.MaxValue ? "no way" : $"way ends {gap:0.0} m short") + ";");
                }
            }
            // The ground along the way through: what a straight step meets (a lip, a stair, a pole).
            text.Append("\n  ground across it (side +1 to -1, height against its middle):");
            if (s_groundMask == 0) s_groundMask = LayerMask.GetMask("piece", "terrain", "Default", "static_solid", "Default_small");
            for (float along = 3.6f; along >= -2.01f; along -= 0.4f)
            {
                Vector3 at = door.transform.position + door.transform.forward * along;
                // From the door's middle, about a metre above its foot: higher, inside a hut, the ray met the ceiling.
                if (Physics.Raycast(at, Vector3.down, out RaycastHit hit, 5f, s_groundMask, QueryTriggerInteraction.Ignore))
                {
                    Piece piece = hit.collider.GetComponentInParent<Piece>();
                    string what = piece != null ? Utils.GetPrefabName(piece.gameObject) : LayerMask.LayerToName(hit.collider.gameObject.layer);
                    text.Append($" {along:+0.0;-0.0}: {hit.point.y - doorY:+0.00;-0.00} {what};");
                }
                else text.Append($" {along:+0.0;-0.0}: nothing;");
            }
            return text.ToString();
        }

        /// <summary>The layers a settler's body bumps into (not the camera's view blockers and the like).</summary>
        private int BumpMask()
        {
            int layer = _citizen.gameObject.layer, mask = 0;
            for (int i = 0; i < 32; i++)
                if (!Physics.GetIgnoreLayerCollision(layer, i)) mask |= 1 << i;
            return mask;
        }

        /// <summary>
        /// For the log when he can't step through: what stands between him and his step (as wide as his body, at foot, waist
        /// and head height) and the ground just ahead. Seen: ten times "nothing" with a thinner, knee-and-chest look.
        /// </summary>
        private string InTheWay(Vector3 me, Vector3 target)
        {
            Vector3 way = target - me;
            way.y = 0f;
            if (way.sqrMagnitude < 0.01f) return "he stands on his step's end";
            var found = new List<string>();
            CapsuleCollider body = _citizen.GetComponent<CapsuleCollider>();
            float radius = body != null ? body.radius : 0.4f;
            foreach (float height in new[] { radius + 0.1f, 0.9f, 1.5f })
            {
                RaycastHit[] hits = Physics.SphereCastAll(me + Vector3.up * height, radius, way.normalized, way.magnitude + 0.3f, BumpMask(), QueryTriggerInteraction.Ignore);
                foreach (RaycastHit hit in hits.OrderBy(h => h.distance))
                {
                    if (hit.collider.transform.IsChildOf(_citizen.transform)) continue;
                    Character someone = hit.collider.GetComponentInParent<Character>();
                    Piece piece = hit.collider.GetComponentInParent<Piece>();
                    string what = someone != null ? (someone.GetComponent<Citizen>()?.CitizenName ?? Utils.GetPrefabName(someone.gameObject))
                        : piece != null ? Utils.GetPrefabName(piece.gameObject) : $"{hit.collider.name} ({LayerMask.LayerToName(hit.collider.gameObject.layer)})";
                    found.Add($"{what} {hit.distance:0.0} m ahead at {height:0.0} m");
                    break;
                }
            }
            // The ground just ahead: a lip or a step too high to walk up.
            if (s_groundMask == 0) s_groundMask = LayerMask.GetMask("piece", "terrain", "Default", "static_solid", "Default_small");
            var ground = new List<string>();
            foreach (float ahead in new[] { 0.3f, 0.6f, 0.9f })
            {
                Vector3 probe = me + way.normalized * ahead + Vector3.up * 1.5f;
                if (Physics.Raycast(probe, Vector3.down, out RaycastHit hit, 3f, s_groundMask, QueryTriggerInteraction.Ignore))
                {
                    Piece piece = hit.collider.GetComponentInParent<Piece>();
                    string what = piece != null ? Utils.GetPrefabName(piece.gameObject) : LayerMask.LayerToName(hit.collider.gameObject.layer);
                    ground.Add($"{ahead:0.0} m: {hit.point.y - me.y:+0.00;-0.00} {what}");
                }
            }
            return "in his way: " + (found.Count > 0 ? string.Join(", ", found) : "nothing") + "; ground ahead: " + string.Join(", ", ground);
        }

        private static string Describe(Door door)
        {
            Vector3 at = door.transform.position;
            return $"{Utils.GetPrefabName(door.gameObject)} at ({at.x:0}, {at.z:0})";
        }

        private static string Key(Door door, Vector3 goal)
        {
            ZNetView nview = door.GetComponent<ZNetView>();
            string id = nview != null && nview.IsValid() ? nview.GetZDO().m_uid.ToString() : door.GetInstanceID().ToString();
            return $"{id}@{Mathf.RoundToInt(goal.x)},{Mathf.RoundToInt(goal.z)}";
        }

        private static Vec3 V(Vector3 v) => new Vec3(v.x, v.y, v.z);
    }
}
