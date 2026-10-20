using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using VikingColonies.Buildings;
using VikingColonies.Citizens;
using VikingColonies.Config;
using VikingColonies.Construction;
using VikingColonies.Colony;
using VikingColonies.Core;
using VikingColonies.Jobs;

namespace VikingColonies.AI
{
    /// <summary>
    /// The settler's brain stem. Runs only on the computer that owns the settler: checks colony and workplace
    /// still exist, then runs the job's brain or the jobless routine.
    /// </summary>
    public class CitizenAI : BaseAI
    {
        private const float MembershipCheckSeconds = 2f;
        private const float ThreatCheckSeconds = 0.5f;
        private const float ShelterStopDistance = 4f;
        private static readonly List<Character> s_nearby = new List<Character>();

        private readonly JoblessRoutine _jobless = new JoblessRoutine();
        private readonly MealTime _meals = new MealTime();
        private ErrorBackoff _errors = new ErrorBackoff();
        private bool _failForTest;
        private const float IndoorStopDistance = 0.8f;
        private const float RefugeRecheckSeconds = 5f;
        private IndoorReason _indoors = IndoorReason.None;
        private string _refugeId = "";
        private Vector3 _refugeSpot;
        private Vector3 _refugeChest;
        private Vector3 _refugeDoor;
        private float _refugeChosenAt = float.NegativeInfinity;
        private readonly RefugeProgress _refuge = new RefugeProgress();
        private bool _inside;
        private readonly Unstuck _unstuck = new Unstuck();
        private float _sidestepUntil;
        private Vector3 _sidestep;
        private float _insideSince;
        private float _settleUntil;
        private const float SettleDistance = 0.3f;
        private float _wayCheckAt;
        /// <summary>How often a walk checks whether its way reaches the goal.</summary>
        private const float WayCheckSeconds = 2f;
        /// <summary>Looking early found no door near: he looks again only after this long (each look weighs every near door).</summary>
        private const float EarlyLookAgainSeconds = 6f;
        private bool _withinWalls;
        private float _withinCheckAt;
        private const float WithinCheckSeconds = 1f;
        private readonly TrappedWatch _trapped = new TrappedWatch();
        private float _trappedCheckAt;
        private const float TrappedCheckSeconds = 2f;
        private static readonly AccessTools.FieldRef<BaseAI, List<Vector3>> s_path = AccessTools.FieldRefAccess<BaseAI, List<Vector3>>("m_path");
        private TargetBlacklist _badRefuges = new TargetBlacklist(IndoorRules.SkipSeconds);

        /// <summary>Self-test: the colony counts as raided.</summary>
        internal static bool RaidForTest;
        private readonly GoneTracker _colonyGone = new GoneTracker(ColonyRules.GoneChecksFor(MembershipCheckSeconds));
        private readonly GoneTracker _workplaceGone = new GoneTracker(ColonyRules.GoneChecksFor(MembershipCheckSeconds));
        private readonly GoneTracker _homeGone = new GoneTracker(ColonyRules.GoneChecksFor(MembershipCheckSeconds));
        private Citizen _citizen;
        private Job _job;
        private string _jobFor = "";
        private bool _hadOwnership;
        private float _dt;
        private float _checkTimer;
        private Vector3 _goal;
        private bool _hasGoal;
        private bool _arrived;
        private bool _running;
        private bool _movementLogged;
        private readonly FleeTracker _flee = new FleeTracker();
        private float _threatTimer;
        private Vector3 _threatPos;
        private bool _hasThreatPos;
        private string _threatName = "";
        private Vector3 _shelterTarget;
        private Vector3 _shelterChosenFrom;   // where the monster was when the shelter was chosen
        private float _shelterChosenAt = -1000f;   // when (a bite since makes him choose again)
        private bool _haveShelter;
        private static readonly AccessTools.FieldRef<BaseAI, float> s_lastFindPathTime = AccessTools.FieldRefAccess<BaseAI, float>("m_lastFindPathTime");
        private static readonly List<Vector3> s_way = new List<Vector3>();
        private const float KneeHeight = 0.4f;
        private static int s_wallMask;
        private DoorUser _doors;
        private Vector3 _testWalkTo;
        private float _testWalkUntil;
        private Vector3 _makeWayTo;
        private float _makeWayUntil;
        private bool _makingWay;
        private float _pausedForTestUntil;
        private FreeTimeRoutine _freeTime;
        private BreakfastRoutine _breakfast;

        /// <summary>His breakfast (the self-test reads the chest he went to).</summary>
        internal BreakfastRoutine BreakfastForTest => _breakfast;
        private bool _freeTimeAsked;
        /// <summary>He lies in his bed (0.15); like free time, only while the night's indoor walk keeps asking for it.</summary>
        private bool _lying, _lieAsked;
        private float _lieTryAt, _lieCheckAt;
        private Bed _bed;
        private Vector3 _bedSide;
        /// <summary>The day his bed was refused (a player's, or a player in it): he stands by it for the rest of that night.</summary>
        private int _noBedDay = -1;

        /// <summary>vc_pose: his own brain rests for a while (he stays where a test command put him).</summary>
        internal void PauseForTest(float seconds) => _pausedForTestUntil = Time.time + seconds;

        /// <summary>vc_lifetest: puts him on a spot as a recall does (off his seat and out of bed first, or the seat pulls him back).</summary>
        internal Vector3 PlaceForTest(Vector3 spot) => PlaceAt(spot);

        protected override void Awake()
        {
            base.Awake();
            _citizen = GetComponent<Citizen>();
            _doors = new DoorUser(this, _citizen);
            _freeTime = new FreeTimeRoutine(this, _citizen);
            _breakfast = new BreakfastRoutine(this, _citizen);
        }

        /// <summary>vc_perf: time spent in settlers' updates while measuring (Stopwatch ticks), and how many updates.</summary>
        internal static bool Profiling;
        internal static long ProfileTicks;
        internal static int ProfileCalls;
        /// <summary>vc_perf: the slowest single update (Stopwatch ticks) and the time per kind of work (his job's brain, or none).</summary>
        internal static long ProfileWorst;
        internal static readonly Dictionary<string, long> ProfileByWork = new Dictionary<string, long>();
        internal static readonly Dictionary<string, int> ProfileCallsByWork = new Dictionary<string, int>();
        internal static readonly Dictionary<string, long> ProfileWorstByWork = new Dictionary<string, long>();

        /// <summary>
        /// The game's creature loop calls this without a guard: an exception here would stop every creature after him on
        /// this computer (review of 0.20, bug 2). Anything that goes wrong in his update pauses him instead (0.25).
        /// </summary>
        public override bool UpdateAI(float dt)
        {
            try
            {
                return ProfiledTick(dt);
            }
            catch (Exception e)
            {
                OnTickError(e);
                return true;
            }
        }

        private bool ProfiledTick(float dt)
        {
            if (!Profiling) return Tick(dt);
            int collections = System.GC.CollectionCount(0);
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                return Tick(dt);
            }
            finally
            {
                long spent = System.Diagnostics.Stopwatch.GetTimestamp() - start;
                ProfileTicks += spent;
                ProfileCalls++;
                if (spent > ProfileWorst) ProfileWorst = spent;
                string work = _indoors != IndoorReason.None ? "indoors" : _job != null ? _job.GetType().Name : "no job";
                // The game's garbage collection can pause whichever update happens to be running: counted apart.
                if (System.GC.CollectionCount(0) != collections) work = "a garbage collection during " + work;
                ProfileByWork.TryGetValue(work, out long sum);
                ProfileByWork[work] = sum + spent;
                ProfileCallsByWork.TryGetValue(work, out int calls);
                ProfileCallsByWork[work] = calls + 1;
                ProfileWorstByWork.TryGetValue(work, out long worst);
                if (spent > worst) ProfileWorstByWork[work] = spent;
            }
        }

        private bool Tick(float dt)
        {
            long timer = Perf.Start();
            bool running = base.UpdateAI(dt);
            Perf.Stop("the game's own AI for settlers (walking, ways)", timer);
            if (!running)
            {
                _hadOwnership = false;   // first: an error below still has him start afresh when he comes back (review of 0.25)
                _freeTime.End();   // he stands up here; the new owner starts him afresh
                DropJob();   // another computer runs this settler now; start fresh if it comes back to us
                _citizen.MarkStale();
                return false;
            }
            _dt = dt;
            if (!_hadOwnership)
            {
                _citizen.LoadRecord();   // the previous owner may have changed it
                _citizen.ApplyAppearance();   // now that this computer owns it, save its looks for everyone
                // Another computer ran him meanwhile: where he was going indoors, and whether he had settled, is stale.
                _refugeId = "";
                _inside = false;
                _refuge.Restart(Time.time);
                _trapped.Arrived(transform.position.x, transform.position.z);
                // A pose and free time saved by another computer (or before a reload) aren't his here: he starts standing.
                _citizen.GetComponent<SettlerPose>()?.ResetForNewOwner();
                _freeTime.ResetForNewOwner();
                _breakfast.ResetForNewOwner();
                _mood.Restart(_citizen.Id);   // worked out on this computer soon (0.17)
                _lying = false;
                _citizen.ApplyClothes();   // the trade's clothes, worn on this computer too
                // No longer a guard (his tower gone while he was away): a guard's raised maximum goes (review of 1.2).
                if (_citizen.Record.Job != SaveKeys.GuardJob && _citizen.Body.GetMaxHealth() > _citizen.Body.GetMaxHealthBase() + 0.5f)
                    GuardJob.SetMaxHealth(_citizen.Body, _citizen.Body.GetMaxHealthBase());
                if (!string.IsNullOrEmpty(_citizen.Status)) _citizen.History.Add(_citizen.Status, Time.time);   // what he was doing when this game took him over (bug report, 0.27)
                _hadOwnership = true;   // last: an error above runs all of it again next time (review of 0.25)
                LogMovementSettings();   // a note for the log only (once)
            }
            if (Time.time >= _clothesCheckAt)
            {
                // The game puts no clothes on during a swing: fired mid-swing, he'd stay bare (review M1).
                _clothesCheckAt = Time.time + ClothesCheckSeconds;
                if (!_citizen.DressedRight()) _citizen.ApplyClothes();
            }
            if (_failTickForTest)
            {
                _failTickForTest = false;
                _wasTestError = true;
                throw new InvalidOperationException("self-test: a deliberate error outside their work");
            }
            _mood.Tick(_citizen);
            if (Time.time < _pausedForTestUntil)
            {
                StopMoving();   // a test tool posed him: his free time and bed stay as they are meanwhile
                return true;
            }
            // Free time lasts only while something asks for it each tick (his job's wait, or having no job): fleeing, night,
            // a recall or work coming up end it, and he stands up.
            bool freeTimeAsked = _freeTimeAsked;
            _freeTimeAsked = false;
            if (!freeTimeAsked && _freeTime.Active) _freeTime.End();
            bool lieAsked = _lieAsked;   // the same for lying in bed: morning, a monster or a recall gets him up
            _lieAsked = false;
            if (!lieAsked && _lying) GetUp();
            timer = Perf.Start();
            _doors.Tick(dt);   // closes the doors he opened once he is past them
            Perf.Stop("settler: closing doors behind him", timer);
            if (Time.time >= _trappedCheckAt)
            {
                _trappedCheckAt = Time.time + TrappedCheckSeconds;
                timer = Perf.Start();
                if (!RescueIfBurning() && !RescueIfOnRoof() && !RescueIfWedgedInHut() && _trapped.Due) RescueIfBoxedIn();
                Perf.Stop("settler: boxed-in check", timer);
            }

            _checkTimer -= dt;
            if (_checkTimer <= 0f)
            {
                _checkTimer = MembershipCheckSeconds;
                timer = Perf.Start();
                CheckMembership();
                Perf.Stop("settler: colony and job check", timer);
            }

            timer = Perf.Start();
            bool onDuty = OnGuardDuty();   // an armed guard (1.1): no breakfast table, evening, shelter or bed
            if (onDuty && _breakfast.Running) _breakfast.Interrupt();   // gone on duty mid-breakfast (1.2): he eats from the chests
            _meals.Tick(_citizen, dt, _breakfast.Serves && !onDuty);   // once a day, from the colony's chests (at the table, 0.15)
            Perf.Stop("settler: meals", timer);

            _threatTimer -= dt;
            if (_threatTimer <= 0f)
            {
                _threatTimer = ThreatCheckSeconds;
                timer = Perf.Start();
                UpdateThreat();
                Perf.Stop("settler: watching for monsters", timer);
            }
            // The red "!" shows exactly while fleeing; the game only ever clears it for monster brains.
            bool fleeing = _flee.Fleeing && !onDuty;
            if (IsAlerted() != fleeing) SetAlerted(fleeing);
            bool alarm = TownHallModule.ColonyAlarmFor(_citizen.ColonyId, out _);   // the Town Hall's alarm (1.3): in, as in a raid
            IndoorReason reason = GuardDuty.Indoors(IndoorRules.Reason(_flee.Fleeing, RaidAtColony() || alarm, ColonyConfig.SettlersShelterInRaids.Value || alarm,
                EnvMan.IsNight(), ColonyConfig.SettlersGoIndoorsAtNight.Value), onDuty);
            if (reason != _indoors) ChangeIndoors(reason);
            if (reason != IndoorReason.None && _breakfast.Running) _breakfast.Interrupt();   // he finishes it once it's over
            // The job is paused, not dropped, while he is indoors or fleeing: it keeps its target and carry.
            if (reason == IndoorReason.Attack)
            {
                if (!GoIndoors()) Flee();   // no hut to shelter in: he runs as before
                return true;
            }
            if (reason == IndoorReason.Raid && GoIndoors()) return true;   // no hut: he works on, and flees if monsters come
            if (Time.time < _testWalkUntil)
            {
                if (GoTo(_testWalkTo, 1f))   // the self-test sends him somewhere (through a door)
                {
                    Idle();
                    if (!TestWalkDone) Log.Info($"{_citizen.CitizenName} got to the test walk's end at ({_testWalkTo.x:0.0}, {_testWalkTo.z:0.0})");
                    TestWalkDone = true;
                }
                return true;
            }
            // A builder needs where he stood (SpaceWait): he keeps away a while, then his work carries on.
            if (Time.time < _makeWayUntil)
            {
                _citizen.Status = "$vc_status_making_way";
                if (GoTo(_makeWayTo, 1f)) Idle();
                return true;
            }
            if (_makingWay)
            {
                _makingWay = false;
                _job?.Resume();
            }

            if (_errors.Paused(Time.time))
            {
                _citizen.Status = "$vc_status_dazed";   // after an error in his work: he starts over when the pause ends
                Idle();
                return true;
            }
            if (_citizen.Status == "$vc_status_dazed") _citizen.Status = "$vc_status_working";   // his job says more as it runs
            // Breakfast at the table comes before work (0.15); his job waits, then carries on.
            if (reason == IndoorReason.None && !onDuty && (_breakfast.Running || _breakfast.Due()))
            {
                _breakfast.Tick();
                if (!_breakfast.Running) _job?.Resume();
                return true;
            }
            // The evening by the fire (0.15): work stops as at nightfall (the job finishes what it must first), then he
            // gathers with the others; night takes him indoors. Not for one who stays out at night (no hut to go into).
            if (reason == IndoorReason.None && !onDuty && Evening())
            {
                try
                {
                    if (!_eveningBegun)
                    {
                        if (_job != null && _job.FinishingBeforeNight(dt)) return true;
                        _eveningBegun = true;
                        _job?.OnNight();   // he puts things away for the day (the builder leaves his site)
                    }
                }
                catch (Exception e)
                {
                    OnWorkError(e);
                    return true;
                }
                _freeTimeAsked = true;
                _freeTime.TickEvening();
                return true;
            }
            if (_eveningBegun)
            {
                _eveningBegun = false;   // the evening is over (night, or the clock was put back): the job carries on as before
                if (reason == IndoorReason.None) _job?.Resume();
            }
            try
            {
                // Night: the job first finishes what it must (the lumberjack's load), then he goes in; no hut: as before.
                if (reason == IndoorReason.Night)
                {
                    if (_job != null && _job.FinishingBeforeNight(dt))
                    {
                        // Seen: a lumberjack's slow delivery used up his time to get into his Longhouse, and he gave it up.
                        _refuge.Wait(Time.time);   // that clock starts once he's free to go in
                        return true;   // only that this tick, not his work too
                    }
                    if (GoIndoors()) return true;
                }
                Work(dt);
            }
            catch (Exception e)
            {
                OnWorkError(e);
            }
            return true;
        }

        /// <summary>The self-test makes his next work tick fail once, to check he recovers.</summary>
        internal void FailWorkOnceForTest() => _failForTest = true;

        /// <summary>The self-test makes his next update fail once outside his work (0.25), to check the game's loop never sees it.</summary>
        internal void FailTickOnceForTest() => _failTickForTest = true;

        /// <summary>The self-test's own errors are forgotten: a real one later is reported as the first (review of 0.25).</summary>
        internal void ForgetErrorsForTest() => _errors = new ErrorBackoff();

        private bool _failTickForTest, _toldTickHandlerFailed;

        /// <summary>An error outside his work (meals, doors, rescues, the colony check…): as one in his work (0.25).</summary>
        private void OnTickError(Exception error)
        {
            try
            {
                OnWorkError(error, "outside their work");
            }
            catch (Exception again)
            {
                if (_toldTickHandlerFailed) return;
                _toldTickHandlerFailed = true;
                Log.Error($"A settler's error couldn't be handled ({again.Message}); the first error: {error}");
            }
        }

        private bool _wasTestError;

        private void Work(float dt)
        {
            if (_failForTest)
            {
                _failForTest = false;
                _wasTestError = true;
                throw new InvalidOperationException("self-test: a deliberate error in their work");
            }
            CitizenRecord record = _citizen.Record;
            if (record.Arriving && !record.IsEmployed && record.HasColony)
            {
                DropJob();
                WalkIn(record, dt);   // a newcomer on his way in from the colony's edge (0.16)
                return;
            }
            ClearOrigin(record);   // hired on his way in, or his Town Hall gone (review I3): not "arriving" any more
            ColonyBuilding workplace = record.IsEmployed ? ColonyBuilding.FindById(record.Workplace) : null;
            JobDefinition job = JobDefinitions.Get(record.Job);
            if (workplace != null && job != null)
            {
                string key = record.Workplace + "/" + record.Job;
                if (_job == null || _jobFor != key)
                {
                    DropJob();
                    _job = job.CreateBrain(_citizen, this, workplace);
                    _jobFor = key;
                }
                _job.Tick(dt);
            }
            else
            {
                DropJob();
                _jobless.Tick(this, _citizen, record.IsEmployed);
            }
        }

        /// <summary>
        /// An error in his work would otherwise repeat on every tick: he pauses (longer each time it happens again) and
        /// starts the job over; the error is logged at most once a minute.
        /// </summary>
        private void OnWorkError(Exception error, string where = "in their work")
        {
            if (!_wasTestError) _errorsSeen++;
            int count = _errors.Fail(Time.time);
            if (count > 0 && _wasTestError)   // the self-test's own: not an error to report (the soak run counts real ones)
                Log.Event($"{_citizen.CitizenName} had the self-test's deliberate error {where}; they pause {_errors.LastPause:0} s and start over");
            else if (count > 0)
                Log.Error($"{_citizen.CitizenName} ({_citizen.Record.Job}) had an error {where}" +
                          (count > 1 ? $" ({count} since the last report)" : "") +
                          $"; they pause {_errors.LastPause:0} s and start over: {error}");
            _wasTestError = false;
            DropJob();
            _citizen.Status = "$vc_status_dazed";
            Idle();
        }

        private void DropJob()
        {
            if (_job == null) return;   // nothing to drop (called every tick while jobless): a door he is going through stays
            Job job = _job;
            _job = null;   // gone even if stopping it fails, so it isn't stopped again on every tick
            _jobFor = "";
            _doors?.Reset();
            try
            {
                job.Stop();
            }
            catch (Exception e)
            {
                // An error escaping here would stop the game's AI loop for every creature this tick.
                Log.Error($"{_citizen.CitizenName}: stopping his job failed: {e}");
            }
        }

        /// <summary>
        /// Drops colony or job only when the building is confirmed gone: its spot is loaded and it has been missing for
        /// <see cref="ColonyRules.GoneConfirmSeconds"/>, never just because it's unloaded or still arriving.
        /// </summary>
        private void CheckMembership()
        {
            CitizenRecord r = _citizen.Record;
            if (r.HasColony)
            {
                ColonyBuilding hall = ColonyBuilding.FindTownHall(r.ColonyId);
                // Brought back while this body lay where nobody had loaded: this one is an old copy and goes (review of 0.10).
                string kept = hall != null ? hall.GetComponent<TownHallModule>()?.Keepsakes.Get(r.Id) : null;
                if (kept != null && Keepsake.Outdated(r.Life, CitizenRecord.Decode(kept).Life))
                {
                    Log.Event($"{_citizen.CitizenName}: an old copy of him (he was brought back since) goes");
                    ZNetScene.instance.Destroy(gameObject);
                    return;
                }
                if (hall != null) _citizen.RememberColonyPos(hall.transform.position);
                // v1 settlers never saved their hall's spot; they always stayed near it, so check around themselves.
                float[] spot = ColonyRules.CheckPoint(r.ColonyPos, Citizen.ToFloats(transform.position));
                if (hall != null && ColonyGrace.IsLost(r.HallLostAt)) _citizen.MarkHallLost(ColonyGrace.NotLost);   // it stands again
                if (_colonyGone.Observe(ColonyRules.PresenceOf(hall != null, AreaLoaded(spot))))
                {
                    // Gone: he keeps his colony and job for the grace days, so a new Town Hall can take the colony over.
                    double now = ZNet.instance.GetTimeSeconds();
                    if (!ColonyGrace.IsLost(r.HallLostAt))
                    {
                        _citizen.MarkHallLost(now);
                        Log.Info($"{r.Name}'s Town Hall is gone; he stays with the colony for {ColonyConfig.TownHallGraceDays.Value} days.");
                    }
                    if (ColonyGrace.Expired(_citizen.Record.HallLostAt, now, ColonyConfig.TownHallGraceDays.Value, ColonyBuilding.GraceDaySeconds))
                    {
                        Log.Info($"{r.Name}'s Town Hall is gone; leaving the colony.");
                        _citizen.MarkHallLost(ColonyGrace.NotLost);
                        _citizen.LeaveColony();
                        _colonyGone.Reset();
                        return;
                    }
                }
            }
            if (r.HasHome)
            {
                ColonyBuilding home = ColonyBuilding.FindById(ResidenceModule.LonghouseOf(r.Home));
                bool ours = home != null && (string.IsNullOrEmpty(home.ColonyId) || home.ColonyId == r.ColonyId);   // a Longhouse can change colony
                if (_homeGone.Observe(ColonyRules.PresenceOf(ours, AreaLoaded(r.HomePos))))
                {
                    _citizen.LeaveHome();
                    _homeGone.Reset();
                }
            }
            if (r.IsEmployed)
            {
                ColonyBuilding hut = ColonyBuilding.FindById(r.Workplace);
                if (_workplaceGone.Observe(ColonyRules.PresenceOf(hut != null, AreaLoaded(r.WorkplacePos))))
                {
                    Log.Info($"{r.Name}'s workplace is gone; now jobless.");
                    _citizen.Unemploy();
                    _workplaceGone.Reset();
                }
            }
        }

        private void UpdateThreat()
        {
            float nearest = float.PositiveInfinity;
            bool passive = ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(GlobalKeys.PassiveMobs);
            s_nearby.Clear();
            Character.GetCharactersInRange(transform.position, FleeTracker.WatchDistance, s_nearby);
            foreach (Character other in s_nearby)
            {
                if (other == null) continue;
                float distance = Vector3.Distance(transform.position, other.transform.position);
                if (distance >= nearest || !IsThreat(_citizen.Body, other, distance, passive)) continue;
                nearest = distance;
                _threatPos = other.transform.position;
                _hasThreatPos = true;
                _threatName = other.GetHoverName();
            }
            bool wasFleeing = _flee.Fleeing;
            _flee.Update(nearest, _citizen.LastMonsterHitTime, Time.time, Traits.FleeRange(_citizen.TraitKind, FleeTracker.StartDistance));   // brave or timid (0.16)
            if (_flee.Fleeing && !wasFleeing) Log.Info($"{_citizen.CitizenName} flees from {(_hasThreatPos ? _threatName : "an attack")}.");
            if (!_flee.Fleeing && wasFleeing)
            {
                Log.Info($"{_citizen.CitizenName} is safe again.");
                _haveShelter = false;
                _hasThreatPos = false;
                try
                {
                    _job?.Resume();   // carry on where it left off
                }
                catch (Exception e)
                {
                    OnWorkError(e);
                }
            }
        }

        /// <summary>
        /// Monsters with a monster's brain (not deer, birds or fish) that are close, alerted or hunting, and awake; with the
        /// world's passive enemies, only stirred-up ones; a weak one (a Neck) only right beside him (1.4.1). A bite counts
        /// whatever bit (FleeTracker's hit memory).
        /// </summary>
        private static bool IsThreat(Character me, Character other, float distance, bool passiveEnemies)
        {
            if (other == me || other.IsDead() || !BaseAI.IsEnemy(me, other)) return false;
            MonsterAI ai = other.GetComponent<MonsterAI>();
            Character target = ai != null ? ai.GetTargetCreature() : null;
            return ai != null && FleeTracker.Counts(ai.IsSleeping(), ai.IsAlerted(), target != null, distance, passiveEnemies,
                FleeTracker.IsWeak(Utils.GetPrefabName(other.gameObject), other.GetLevel()), targetsHim: target == me);
        }

        private void Flee()
        {
            _citizen.Status = "$vc_status_fleeing";
            // Choose once per flee, so a run-away spot stays put; again when the monster has moved a lot, bit him since, or
            // is at his shelter (FleeTracker.ChooseShelterAgain).
            if (FleeTracker.ChooseShelterAgain(_haveShelter, _hasThreatPos ? Vector3.Distance(_threatPos, _shelterChosenFrom) : 0f,
                    _citizen.LastMonsterHitTime > _shelterChosenAt, _hasThreatPos ? Utils.DistanceXZ(_threatPos, _shelterTarget) : float.MaxValue))
            {
                CitizenRecord r = _citizen.Record;
                GroundPoint? threat = _hasThreatPos ? Ground(_threatPos) : (GroundPoint?)null;
                Shelter shelter = FleeTracker.ChooseShelter(Ground(transform.position), GroundOf(r.ColonyPos), GroundOf(r.WorkplacePos), threat);
                switch (shelter.Kind)
                {
                    case ShelterKind.TownHall: _shelterTarget = Citizen.ToVector3(r.ColonyPos); break;
                    case ShelterKind.Hut: _shelterTarget = Citizen.ToVector3(r.WorkplacePos); break;
                    default: _shelterTarget = new Vector3(shelter.Point.X, transform.position.y, shelter.Point.Z); break;
                }
                _shelterChosenFrom = _threatPos;
                _shelterChosenAt = Time.time;
                _haveShelter = true;
            }
            if (GoTo(_shelterTarget, ShelterStopDistance, hurry: true)) Idle();
        }

        /// <summary>A new reason to be indoors (or none): he chooses his hut afresh; at night the job puts things away; when it's over the job carries on.</summary>
        private void ChangeIndoors(IndoorReason reason)
        {
            IndoorReason was = _indoors;
            _indoors = reason;
            _refugeId = "";
            _inside = false;
            _refuge.Restart(Time.time);
            _withinCheckAt = 0f;
            if (reason == IndoorReason.Night)
            {
                try
                {
                    _job?.OnNight();
                }
                catch (Exception e)
                {
                    OnWorkError(e);
                }
            }
            if (reason != IndoorReason.None || was == IndoorReason.None) return;
            Log.Info($"{_citizen.CitizenName} comes out ({was} is over).");
            if (was == IndoorReason.Attack) return;   // calming down already carried the job on (UpdateThreat)
            try
            {
                _job?.Resume();
            }
            catch (Exception e)
            {
                OnWorkError(e);
            }
        }

        /// <summary>
        /// Walks into his hut (through its door, which he shuts behind him) and stays on his spot by the chest. False when no
        /// hut can take him in. A hut he can't get into within a while, or where a monster reaches him, is skipped for a few
        /// minutes.
        /// </summary>
        private bool GoIndoors()
        {
            if (_refugeId.Length == 0 || Time.time - _refugeChosenAt > RefugeRecheckSeconds) ChooseRefuge();
            if (_refugeId.Length == 0) return false;
            _citizen.Status = _indoors == IndoorReason.Night ? "$vc_status_sleeping" : "$vc_status_sheltering";
            float now = Time.time;
            if (_lying && _indoors == IndoorReason.Night)
            {
                Idle();   // in bed: no walking to his spot beside it
                // A player who lies down there or makes it his own takes the bed: he gets up (review of 0.15).
                if (now >= _lieCheckAt)
                {
                    _lieCheckAt = now + 1f;
                    if (!BedFree(_bed))
                    {
                        _noBedDay = Today();
                        GetUp();
                        return true;
                    }
                }
                _lieAsked = true;
                return true;
            }
            // A way that ends at the hut's wall (its door blocked, or facing a cliff) gets him to his spot, but not inside.
            bool onSpot = !_refuge.Settled && GoTo(_refugeSpot, IndoorStopDistance, hurry: _indoors != IndoorReason.Night) &&
                          (_inside || !Walls.StructureBetween(transform.position, _refugeChest));
            // Within its walls but kept off his spot (someone stands on it, a bed is in the way), he doesn't give the hut up,
            // and after a moment he stays where he is (RefugeProgress). Seen: two settlers each on the other's sleeping spot
            // left their Longhouse for the Builder's Hut and got boxed into a corner of it on the way.
            bool within = !onSpot && WithinRefuge(now);
            float toSpot = Utils.DistanceXZ(transform.position, _refugeSpot);
            RefugeStep step = _refuge.Step(onSpot, within, Utils.DistanceXZ(transform.position, _refugeDoor), toSpot, _doors.Passes, _doors.Busy, now);
            if (step.JustSettled)
            {
                Idle();
                Log.Info($"{_citizen.CitizenName} can't get onto his spot in hut {_refugeId} ({toSpot:0.0} m away); he stays where he is, at {transform.position}.");
            }
            if (!step.In) _inside = false;
            else if (!_inside)
            {
                _inside = true;
                _insideSince = now;
                _settleUntil = now + 2f;
            }
            if (_inside)
            {
                // A monster that reaches him in there (it followed him in) makes the hut no refuge: another, or he runs.
                if (_indoors == IndoorReason.Attack && _citizen.LastMonsterHitTime > _insideSince)
                {
                    GiveUpRefuge("was attacked inside");
                    return true;
                }
                // His walk stops short of the spot, on the door side, which may be in the way in: the last steps onto it.
                if (!_refuge.Settled && now < _settleUntil && toSpot > SettleDistance) StepTowards(_refugeSpot);
                else
                {
                    Idle();
                    if (_indoors == IndoorReason.Night) LieInBed(toSpot, now);
                }
                return true;
            }
            if (step.GivesUp) GiveUpRefuge("couldn't get into");
            return true;
        }

        /// <summary>
        /// At night, on his spot by his own bed in his Longhouse: he lies down in it (0.15), unless a player lies there or has
        /// made it his own (a player's spawn bed).
        /// </summary>
        private void LieInBed(float toSpot, float now)
        {
            CitizenRecord r = _citizen.Record;
            if (!ColonyConfig.SleepInBeds.Value || !r.HasHome || _refugeId != ResidenceModule.LonghouseOf(r.Home)) return;
            if (_lying)
            {
                _lieAsked = true;
                return;
            }
            if (now < _lieTryAt || toSpot > 1.2f && !_refuge.Settled || _noBedDay == Today()) return;
            _lieTryAt = now + 5f;
            Bed bed = ResidenceModule.BedOf(r.Home);
            if (bed == null || bed.m_spawnPoint == null) return;
            if (!BedFree(bed))
            {
                _noBedDay = Today();   // no looking again all night
                return;
            }
            SettlerPose pose = _citizen.GetComponent<SettlerPose>();
            if (pose == null || !pose.LieIn(bed)) return;
            _bed = bed;
            _bedSide = _refugeSpot;   // where he gets up: by this bed, whatever hut he goes to next
            _lying = true;
            _lieAsked = true;
            Log.Info($"{_citizen.CitizenName} lies down in his bed.");
        }

        /// <summary>A bed nobody has made his own (a player's spawn bed) and no player lies in.</summary>
        private static bool BedFree(Bed bed)
        {
            if (bed == null || bed.m_spawnPoint == null) return false;
            ZNetView view = bed.GetComponent<ZNetView>();
            if (view != null && view.IsValid() && view.GetZDO().GetLong(ZDOVars.s_owner) != 0L) return false;   // a player's bed
            foreach (Player player in Player.GetAllPlayers())
                if (player != null && Vector3.Distance(player.transform.position, bed.m_spawnPoint.position) < 0.6f) return false;
            return true;
        }

        private static int Today() => EnvMan.instance != null && ZNet.instance != null ? EnvMan.instance.GetDay(ZNet.instance.GetTimeSeconds()) : 0;

        /// <summary>Up out of bed onto his spot beside it.</summary>
        private void GetUp()
        {
            _lying = false;
            _bed = null;
            SettlerPose pose = _citizen.GetComponent<SettlerPose>();
            if (pose == null) return;
            if (pose.Held) pose.StandAt(_bedSide);
            else pose.Stand();
            Log.Info($"{_citizen.CitizenName} gets up.");
        }

        /// <summary>Self-test: he lies in his bed now.</summary>
        internal bool LyingForTest => _lying;

        /// <summary>Within his refuge's walls (<see cref="IndoorRules.WithinWalls"/>), looked at once a second.</summary>
        private bool WithinRefuge(float now)
        {
            if (now < _withinCheckAt) return _withinWalls;
            _withinCheckAt = now + WithinCheckSeconds;
            ColonyBuilding hut = ColonyBuilding.FindById(_refugeId);
            ConstructionModule site = hut != null ? hut.GetComponent<ConstructionModule>() : null;
            Footprint ground = site != null ? site.SiteFootprint : null;
            bool onItsGround = ground != null && ground.Contains(CoreVectors.V(transform.position));
            _withinWalls = IndoorRules.WithinWalls(onItsGround, onItsGround && Walls.StructureBetween(transform.position, _refugeChest),
                Utils.DistanceXZ(transform.position, _refugeDoor));
            return _withinWalls;
        }

        /// <summary>The hut he chose is no refuge for a few minutes; he chooses again (or does without).</summary>
        private void GiveUpRefuge(string why)
        {
            Log.Info($"{_citizen.CitizenName} {why} hut {_refugeId} and tries another.");
            _badRefuges.Add(_refugeId, Time.time);
            _refugeId = "";
            _inside = false;
            _refuge.Restart(Time.time);
            _withinCheckAt = 0f;
            RefugesGivenUp++;
        }

        private void ChooseRefuge()
        {
            _refugeChosenAt = Time.time;
            CitizenRecord r = _citizen.Record;
            List<Refuge> huts = Refuges.For(r.ColonyId);
            GroundPoint? threat = _indoors == IndoorReason.Attack && _hasThreatPos ? Ground(_threatPos) : (GroundPoint?)null;
            string own = r.HasHome ? ResidenceModule.LonghouseOf(r.Home) : r.IsEmployed ? r.Workplace : "";   // his home first, then his hut
            int pick = IndoorRules.ChooseRefuge(huts, own, Ground(transform.position), threat,
                id => _badRefuges.IsBlocked(id, Time.time), current: _refugeId);
            string id = pick >= 0 ? huts[pick].Id : "";
            if (id == _refugeId) return;
            _refugeId = id;
            _inside = false;
            _refuge.Restart(Time.time);
            _withinCheckAt = 0f;
            if (id.Length == 0) return;
            ColonyBuilding hut = ColonyBuilding.FindById(id);
            float floor = hut != null ? hut.transform.position.y : transform.position.y;
            GroundPoint spot = IndoorRules.Spot(huts[pick].Chest, huts[pick].Door, huts[pick].Inward, _citizen.Id);
            _refugeSpot = new Vector3(spot.X, floor, spot.Z);
            if (r.HasHome && id == ResidenceModule.LonghouseOf(r.Home) && r.HomePos != null)
            {
                // By his bed: beside its long side, facing the room (where the chest stands).
                Vector3? beside = ResidenceModule.SleepSpotOf(r.Home, this);
                if (beside.HasValue) _refugeSpot = new Vector3(beside.Value.x, floor, beside.Value.z);
            }
            _refugeChest = new Vector3(huts[pick].Chest.X, floor, huts[pick].Chest.Z);
            _refugeDoor = new Vector3(huts[pick].Door.X, floor, huts[pick].Door.Z);
            Log.Info($"{_citizen.CitizenName} goes into the {(hut != null ? hut.TypeId : "hut")} {id} ({_indoors}).");
        }

        /// <summary>A raid at his colony (1.1: a guard's watch widens).</summary>
        internal bool RaidAtHome => RaidAtColony();

        /// <summary>An armed guard with <c>Guards.Enabled</c>, not hurt: he stays out to fight (1.1); otherwise he is as anyone.</summary>
        private bool OnGuardDuty() => _job is GuardJob guard
            ? GuardDuty.OnDuty(guard.Armed, ColonyConfig.GuardsEnabled.Value, guard.Retreating)
            : GuardDuty.NeedsBrain(_job != null, _citizen.Record.IsEmployed && _citizen.Record.Job == SaveKeys.GuardJob, ColonyConfig.GuardsEnabled.Value);

        /// <summary>A Valheim raid is running and its area holds his colony's Town Hall.</summary>
        private bool RaidAtColony()
        {
            if (RaidForTest) return _citizen.Record.HasColony;
            float[] hall = _citizen.Record.ColonyPos;
            RandomEvent raid = RandEventSystem.instance != null ? RandEventSystem.instance.GetCurrentRandomEvent() : null;
            return raid != null && hall != null && hall.Length == 3 &&
                   IndoorRules.RaidCovers(new GroundPoint(raid.m_pos.x, raid.m_pos.z), raid.m_eventRange, new GroundPoint(hall[0], hall[2]), hall[1]);
        }

        private static GroundPoint Ground(Vector3 v) => new GroundPoint(v.x, v.z);

        private static GroundPoint? GroundOf(float[] p) => p != null && p.Length == 3 ? new GroundPoint(p[0], p[2]) : (GroundPoint?)null;

        private static bool AreaLoaded(float[] pos) =>
            pos != null && pos.Length == 3 && ZNetScene.instance != null && ZNetScene.instance.IsAreaReady(Citizen.ToVector3(pos));

        /// <summary>
        /// Jogs towards a point (sprints when hurrying and far); true once there. Arrival and gait have margins, so
        /// the settler doesn't stop-start.
        /// </summary>
        public bool GoTo(Vector3 point, float stopDistance, bool hurry = false)
        {
            if (!_hasGoal || Utils.DistanceXZ(point, _goal) > MoveRules.NewGoalDistance)
            {
                _goal = point;
                _hasGoal = true;
                _arrived = false;
                _wayCheckAt = Time.time + UnityEngine.Random.Range(0f, 0.5f);   // soon, but not every settler on the same frame
            }
            float stop = MoveRules.StopDistance(stopDistance);
            float distance = Utils.DistanceXZ(transform.position, point);
            // The backstop (1.4.0): wanting to be more than a patch away counts whatever happens on the way, doors included.
            if (distance > TrappedWatch.Patch) _trapped.Wanting(transform.position.x, transform.position.z, Time.time);
            if (_doors.Passing(point))   // going through a door on the way
            {
                _arrived = false;
                return false;
            }
            // Close enough, but a wall between (a chest behind a wall or a window) isn't there yet.
            bool wallBetween = !_arrived && MoveRules.Arrived(false, distance, stop) && WallBetween(point);
            _arrived = MoveRules.InReach(_arrived, distance, stop, wallBetween);
            if (_arrived)
            {
                StopMoving();
                _trapped.Arrived(transform.position.x, transform.position.z);
                _doors.Arrived();
                return true;
            }
            _running = MoveRules.ShouldRun(_running, distance, hurry);
            // MoveTo also stops and returns true when no path exists; that only counts as arriving close by. Behind a
            // wall he walks on towards the goal itself: round to it, or up to the door in the way.
            bool pathEnded = PathTo(point, wallBetween ? MoveRules.MinStopDistance : stop, _running);
            if (TrappedWatch.CountsAsTrying(pathEnded, distance)) _trapped.Walking(transform.position.x, transform.position.z, Time.time);
            // A way that ends well short of the goal: he looks for a door before he walks it (its end can be a corner of the
            // hut he is in). Seen: a Longhouse household off to work behind its back wall crowded into the back corner first.
            // The game's own path, just worked out, says where the way ends.
            if (!pathEnded && Time.time >= _wayCheckAt)
            {
                _wayCheckAt = Time.time + WayCheckSeconds;
                List<Vector3> way = s_path(this);
                float gap = way.Count > 0 ? Utils.DistanceXZ(way[way.Count - 1], point) : 0f;
                if (DoorRules.LookForDoorNow(gap))
                {
                    if (_doors.Consider(point, unreachable: true, _dt, early: true, gapFromHere: gap)) return false;
                    _wayCheckAt = Time.time + EarlyLookAgainSeconds;
                }
            }
            // A path that ends short of the goal (even near enough to count as arriving), a wall between, or a closed
            // door right ahead: he may go through a door first.
            if ((!pathEnded || wallBetween || DoorRules.TryDoorFirst(pathEnded, distance, stop)) && _doors.Consider(point, unreachable: pathEnded, _dt))
                return false;
            if (!MoveRules.ReachedPathEnd(pathEnded, distance, stop)) return false;
            _arrived = true;
            StopMoving();
            _trapped.Arrived(transform.position.x, transform.position.z);
            return true;
        }

        private static float NearestPlayer(Vector3 to)
        {
            float nearest = float.MaxValue;
            foreach (Player player in Player.GetAllPlayers())
                if (player != null) nearest = Mathf.Min(nearest, Vector3.Distance(player.transform.position, to));
            return nearest;
        }

        /// <summary>
        /// Up on a roof for a while (<see cref="RoofRules"/>; the old-huts upgrade test, 2026-10-19: a builder landed on a
        /// roof stepping out under a low eave and walked about up there for 40 minutes): he is moved to a free spot by the
        /// Town Hall, as the boxed-in rescue does. True when moved.
        /// </summary>
        private bool RescueIfOnRoof()
        {
            Vector3 at = transform.position;
            float now = Time.time;
            float above = ZoneSystem.instance != null ? at.y - ZoneSystem.instance.GetGroundHeight(at) : 0f;
            if (above <= RoofRules.Above || !OnRoofPiece(at))
            {
                _onRoofSince = -1f;
                return false;
            }
            if (_onRoofSince < 0f) _onRoofSince = now;
            if (!RoofRules.StuckUp(true, above, now - _onRoofSince)) return false;
            Vector3? centre = TownHallSpot();
            if (!centre.HasValue || ZNetScene.instance == null || !ZNetScene.instance.IsAreaReady(centre.Value)) return false;
            Vector3? spot = FreeSpotNear(centre.Value, true);
            if (!spot.HasValue || !_trapped.ShouldMoveOffRoof(now)) return false;
            Vector3 to = PlaceAt(spot.Value);
            _trapped.Moved(to.x, to.z, now);
            _onRoofSince = -1f;
            Rescues++;
            _rescuesHere++;
            Log.Info($"{_citizen.CitizenName} was up on a roof at {At(at)} ({above:0.0} m above the ground): moved to {At(to)} by the Town Hall.");
            Saga.Record(_citizen.ColonyId, "rescued", _citizen.CitizenName);
            return true;
        }

        private float _onRoofSince = -1f;

        /// <summary>
        /// Burning where he stands for a few seconds (<see cref="BurnRules"/>; 1.4.0's self-test on master: squeezed between
        /// a hut's wall and a fire, a settler burned to death): he is moved to a free spot by the Town Hall, clear of fires,
        /// and the fire on him is put out. True when moved.
        /// </summary>
        private bool RescueIfBurning()
        {
            Vector3 at = transform.position;
            float now = Time.time;
            SEMan seman = _citizen.Body != null ? _citizen.Body.GetSEMan() : null;
            bool burning = seman != null && seman.HaveStatusEffect(SEMan.s_statusEffectBurning);
            if (!burning || _burningSince < 0f || !BurnRules.StillThere(at.x - _burningAt.x, at.z - _burningAt.z))
            {
                _burningSince = burning ? now : -1f;   // caught fire just now, or got away from where he did
                _burningAt = at;
                return false;
            }
            if (!BurnRules.StuckBurning(true, now - _burningSince)) return false;
            Vector3? centre = TownHallSpot();
            if (!centre.HasValue || ZNetScene.instance == null || !ZNetScene.instance.IsAreaReady(centre.Value)) return false;
            Vector3? spot = FreeSpotNear(centre.Value, true);
            if (!spot.HasValue || !_trapped.ShouldMoveOffRoof(now)) return false;
            Vector3 to = PlaceAt(spot.Value);
            seman.RemoveStatusEffect(SEMan.s_statusEffectBurning, true);
            _trapped.Moved(to.x, to.z, now);
            _burningSince = -1f;
            Rescues++;
            _rescuesHere++;
            Log.Info($"{_citizen.CitizenName} kept burning at {At(at)}: moved to {At(to)} by the Town Hall, the fire put out.");
            Saga.Record(_citizen.ColonyId, "rescued", _citizen.CitizenName);
            return true;
        }

        private float _burningSince = -1f;
        private Vector3 _burningAt;
        private static int s_roofMask;

        /// <summary>He stands on a roof piece: a roof tile, a ridge or a gable's top (their names say roof).</summary>
        private static bool OnRoofPiece(Vector3 at)
        {
            if (s_roofMask == 0) s_roofMask = LayerMask.GetMask("piece");
            if (!Physics.Raycast(at + Vector3.up * 0.3f, Vector3.down, out RaycastHit hit, 0.8f, s_roofMask)) return false;
            Piece piece = hit.collider != null ? hit.collider.GetComponentInParent<Piece>() : null;
            return piece != null && Utils.GetPrefabName(piece.gameObject).IndexOf("roof", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// The last resort for a settler boxed in for good (<see cref="TrappedWatch"/>): he is moved to a free spot by the
        /// Town Hall (where it stood, if it is gone), out of sight, and his job carries on from there.
        /// </summary>
        private void RescueIfBoxedIn()
        {
            Vector3 from = transform.position;
            float now = Time.time;
            Vector3? centre = TownHallSpot();
            if (!centre.HasValue || ZNetScene.instance == null || !ZNetScene.instance.IsAreaReady(centre.Value) || _indoors != IndoorReason.None)
            {
                _trapped.Arrived(from.x, from.z);   // nothing to do now: the clock starts again
                return;
            }
            bool anyway = _trapped.Overdue;
            Vector3? spot = FreeSpotNear(centre.Value, anyway);
            if (!spot.HasValue)
            {
                if (!_toldNoFreeSpot) Log.Info($"{_citizen.CitizenName} may be boxed in at {At(from)} ({_trapped.WalkedFor:0} s trying to walk, {_trapped.WantedFor:0} s wanting to be elsewhere), but there's no free spot by the Town Hall out of sight; he keeps trying.");
                _toldNoFreeSpot = true;
                _trappedCheckAt = now + TrappedBackOffSeconds;
                return;
            }
            if (!TrappedWatch.WorthMoving(from.x, from.z, spot.Value.x, spot.Value.z))
            {
                _trapped.Arrived(from.x, from.z);   // he stands by the Town Hall already: a move wouldn't help (his goal may be out of reach)
                return;
            }
            if (!_trapped.ShouldMove(NearestPlayer(from), indoors: false, now: now))
            {
                _trappedCheckAt = now + TrappedBackOffSeconds;   // a player near, or moved twice lately: look again later
                return;
            }
            double walked = _trapped.WalkedFor, wanted = _trapped.WantedFor;
            Vector3 to = PlaceAt(spot.Value);
            _trapped.Moved(to.x, to.z, now);
            _toldNoFreeSpot = false;
            Rescues++;
            _rescuesHere++;
            Log.Info($"{_citizen.CitizenName} was boxed in at {At(from)} (trying to walk for {walked:0} s, wanting to be elsewhere for {wanted:0} s): moved to {At(to)} by the Town Hall.");
            Saga.Record(_citizen.ColonyId, "rescued", _citizen.CitizenName);
        }

        /// <summary>
        /// Wedged inside a hut (worker polish, 0.20: a farmer didn't move for minutes where she spent the night in a turned
        /// Farm, though she kept trying to walk): after <see cref="TrappedWatch.InsideHutAfter"/> he is moved to the spot just
        /// inside the hut's door, behind its walls where nobody sees it, and goes on from there (at night too: he stays
        /// inside). True when moved.
        /// </summary>
        private bool RescueIfWedgedInHut()
        {
            float now = Time.time;
            if (!_trapped.ShouldMoveInsideHut(now)) return false;
            Vector3 from = transform.position;
            foreach (ColonyBuilding building in ColonyBuilding.All)
            {
                ConstructionModule site = building != null && building.IsValid ? building.GetComponent<ConstructionModule>() : null;
                Footprint ground = site != null ? site.RoomFootprint : null;   // within its walls, not in its yard or woodpile strip
                if (ground == null || !ground.Grow(-SiteSurvey.SiteMargin).Contains(CoreVectors.V(from))) continue;
                Door door = HutDoors.DoorOf(site);
                if (door == null && site.IsCamp) continue;   // the Town Hall's camp has no walls to be wedged in
                int inside = door != null ? DoorRules.SideOf(CoreVectors.V(door.transform.position), CoreVectors.V(door.transform.forward), ground.Centre) : 0;
                if (inside == 0) return false;
                // Under a porch in front of its door he is outside, not wedged in it (final review of 0.20).
                if (DoorRules.SideOf(CoreVectors.V(door.transform.position), CoreVectors.V(door.transform.forward), CoreVectors.V(from)) != inside) continue;
                double walked = _trapped.WalkedFor;
                _doors.Reset();
                Vector3 to = PlaceAt(DoorUser.Spot(door, inside, DoorRules.StandOff));
                _trapped.Moved(to.x, to.z, now);
                Rescues++;
                _rescuesHere++;
                Log.Info($"{_citizen.CitizenName} was wedged inside the {building.TypeId} at {At(from)} (trying to walk for {walked:0} s): " +
                         $"moved to just inside its door at {At(to)}.");
                return true;
            }
            return false;
        }

        /// <summary>
        /// A player called him back (the Town Hall window's Recall): he is moved to a free spot by the Town Hall at once,
        /// and his job carries on from there. False when there's no such spot (or no Town Hall), and he stays.
        /// </summary>
        internal bool Recall()
        {
            Vector3 from = transform.position;
            Vector3? centre = TownHallSpot();
            Vector3? spot = centre.HasValue ? FreeSpotNear(centre.Value, anyway: true) : null;
            if (!spot.HasValue)
            {
                Log.Info($"{_citizen.CitizenName} was called back to the Town Hall, but there's no free spot by it; he stays at {At(from)}.");
                return false;
            }
            _doors.Reset();   // a door passing half done would pull him back to that door
            Vector3 to = PlaceAt(spot.Value);
            _trapped.Arrived(to.x, to.z);
            Log.Info($"{_citizen.CitizenName} was called back from {At(from)} to {At(to)} by the Town Hall.");
            Saga.Record(_citizen.ColonyId, "recalled", _citizen.CitizenName);
            return true;
        }

        /// <summary>
        /// The hut window's Recall (worker polish, 0.20): he is moved to a free spot just outside his hut's door (by its chest
        /// when it has no door yet), and his job carries on from there. False when there's no such spot.
        /// </summary>
        internal bool RecallToHut()
        {
            Vector3 from = transform.position;
            ColonyBuilding hut = ColonyBuilding.FindById(_citizen.Record.Workplace);
            if (hut == null) return false;
            Vector3? spot = SpotOutside(hut.GetComponent<ConstructionModule>(), hut.transform.position);
            if (!spot.HasValue)
            {
                Log.Info($"{_citizen.CitizenName} was called back to his {hut.TypeId}, but there's no free spot by it; he stays at {At(from)}.");
                return false;
            }
            _doors.Reset();
            Vector3 to = PlaceAt(spot.Value);
            _trapped.Arrived(to.x, to.z);
            Log.Info($"{_citizen.CitizenName} was called back from {At(from)} to {At(to)} by his {hut.TypeId}.");
            return true;
        }

        /// <summary>
        /// A free spot just outside a hut's door, or one outside its ground (by its door, or <paramref name="near"/> when it
        /// has no door yet); null when there's none.
        /// </summary>
        private Vector3? SpotOutside(ConstructionModule site, Vector3 near)
        {
            Footprint ground = site != null ? site.SiteFootprint : null;
            Door door = site != null ? HutDoors.DoorOf(site) : null;
            int inside = door != null && ground != null
                ? DoorRules.SideOf(CoreVectors.V(door.transform.position), CoreVectors.V(door.transform.forward), ground.Centre) : 0;
            if (inside != 0)
                foreach (float distance in DoorRules.StandOffs)
                {
                    Vector3 outside = DoorUser.Spot(door, -inside, distance + 0.5f);
                    if (StandsFree(outside)) return outside;
                }
            return FreeSpotNear(inside != 0 ? DoorUser.Spot(door, -inside, DoorRules.StandOff) : near, anyway: true,
                skip: at => ground != null && ground.Contains(CoreVectors.V(at)));
        }

        /// <summary>
        /// A builder needs where he stands (<see cref="SpaceWait"/>; the old huts' third rerun, 2026-10-19: a forager stayed
        /// in his own hut while it was upgraded, where a roof piece went, for 30 minutes). He walks out to a free spot outside
        /// the hut (by its door; at the Town Hall's camp, a few metres off), or with <paramref name="now"/> is moved there at
        /// once, and keeps away a while. False when there's no such spot, or another computer runs him.
        /// </summary>
        internal bool MakeWay(ConstructionModule site, Vector3 piece, bool now, string builder)
        {
            if (OwnZdo() == null) return false;
            Vector3 from = transform.position;
            Vector3? spot = site != null && site.IsCamp ? FreeSpotNear(piece, anyway: true) : SpotOutside(site, piece);
            if (!spot.HasValue)
            {
                Log.Info($"{_citizen.CitizenName} would make way for {builder}, but there's no free spot to go to; he stays at {At(from)}.");
                return false;
            }
            _freeTime.End();   // off his seat
            if (_lying) GetUp();
            if (_breakfast.Running) _breakfast.Interrupt();
            Vector3 to = spot.Value;
            if (now)
            {
                _doors.Reset();
                to = PlaceAt(to);
                _trapped.Arrived(to.x, to.z);
            }
            _makeWayTo = to;
            _makeWayUntil = Time.time + SpaceWait.KeepAwaySeconds;
            _makingWay = true;
            Log.Info(now ? $"{_citizen.CitizenName} still stood where {builder} builds: moved from {At(from)} to {At(to)}."
                         : $"{_citizen.CitizenName} makes way for {builder}: from {At(from)} to {At(to)}.");
            return true;
        }

        /// <summary>Room for him to stand there (nothing solid in a capsule his size).</summary>
        private static bool StandsFree(Vector3 spot)
        {
            if (s_standMask == 0) s_standMask = LayerMask.GetMask("piece", "piece_nonsolid", "Default", "static_solid", "Default_small", "character", "vehicle");
            return !Physics.CheckCapsule(spot + Vector3.up * 0.5f, spot + Vector3.up * 1.6f, 0.4f, s_standMask, QueryTriggerInteraction.Ignore);
        }

        /// <summary>His colony's Town Hall, or where it stood when it's gone; null without a colony.</summary>
        private Vector3? TownHallSpot()
        {
            ColonyBuilding hall = ColonyBuilding.FindTownHall(_citizen.ColonyId);
            float[] last = _citizen.Record.ColonyPos;
            return hall != null ? hall.transform.position
                : last != null && last.Length == 3 ? new Vector3(last[0], last[1], last[2]) : (Vector3?)null;
        }

        /// <summary>
        /// Nothing to do just now (no job, or his job waits with nothing to do): free time near <paramref name="anchor"/>, his hut
        /// or the Town Hall (<see cref="FreeTimeRoutine"/>). With <c>Colony.FreeTime</c> off he waits by it, standing.
        /// </summary>
        public void FreeTime(Vector3 anchor)
        {
            _freeTimeAsked = true;
            if (!ColonyConfig.FreeTime.Value)
            {
                if (_freeTime.Active) _freeTime.End();
                if (GoTo(anchor, 4f)) Idle();
                return;
            }
            _freeTime.Tick(anchor);
        }

        private bool _eveningBegun;

        /// <summary>The evening (0.70 of the day until night), with the setting on, for a settler with a hut to go into tonight.</summary>
        private bool Evening()
        {
            if (EnvMan.instance == null || !ColonyConfig.EveningGathering.Value || !ColonyConfig.SettlersGoIndoorsAtNight.Value) return false;
            if (TheDay.Phase(EnvMan.instance.GetDayFraction(), EnvMan.IsNight()) != DayPhase.Evening) return false;
            CitizenRecord r = _citizen.Record;
            return r.HasColony && Refuges.For(r.ColonyId).Exists(h => h.Usable);
        }

        /// <summary>Only the time he spends walking in counts (review M2: breakfast or the evening on the way ran the clock).</summary>
        private readonly WalkInWatch _walkIn = new WalkInWatch();

        private readonly MoodTracker _mood = new MoodTracker();

        /// <summary>Self-test: his mood worked out now.</summary>
        internal void WorkOutMoodForTest() => MoodTracker.WorkOut(_citizen);

        private float _clothesCheckAt;
        private const float ClothesCheckSeconds = 5f;

        /// <summary>
        /// A newcomer walks from the colony's edge to the Town Hall (0.16), hovering "Arriving (from the coast)". There he
        /// waves and is one of the colony; after three minutes of walking he is placed by the Town Hall.
        /// </summary>
        private void WalkIn(CitizenRecord r, float dt)
        {
            _walkIn.Walk(dt);
            _citizen.Status = "$vc_status_arriving";
            ShowOrigin(r);
            Vector3? hall = TownHallSpot();
            bool there = hall.HasValue && GoTo(hall.Value, 4f);
            if (!there && !_walkIn.TimedOut && hall.HasValue) return;
            if (!there)
            {
                Log.Info($"{r.Name} didn't get to the Town Hall in {WalkInWatch.GiveUpSeconds:0} s of walking; he is placed there.");
                PlaceByHall();
            }
            Idle();
            _citizen.MarkArriving(false);
            ClearOrigin(r);
            _citizen.GetComponent<SettlerPose>()?.Gesture("emote_wave");
            Log.Info($"{r.Name} has arrived at the Town Hall.");
        }

        /// <summary>
        /// A newcomer who couldn't walk in is put by the Town Hall: out of every player's sight if there's such a spot. No Saga
        /// line (review M2: Recall wrote "called back", and nobody had).
        /// </summary>
        private void PlaceByHall()
        {
            Vector3? centre = TownHallSpot();
            Vector3? spot = centre.HasValue ? FreeSpotNear(centre.Value, anyway: false) ?? FreeSpotNear(centre.Value, anyway: true) : null;
            if (!spot.HasValue) return;
            _doors.Reset();
            Vector3 to = PlaceAt(spot.Value);
            _trapped.Arrived(to.x, to.z);
        }

        /// <summary>While he walks in, what he's doing is where he came from ("Arriving (from the coast)").</summary>
        private void ShowOrigin(CitizenRecord r)
        {
            ZDO zdo = OwnZdo();
            if (zdo != null && zdo.GetString(SaveKeys.Doing, "") != (r.Origin ?? "")) zdo.Set(SaveKeys.Doing, r.Origin ?? "");
        }

        /// <summary>
        /// Once he's no longer walking in, however that ended (hired on the way: review I3), his origin words come off; what
        /// another part of his day put there stays. Read from the saved value, so it holds after a change of computer too.
        /// </summary>
        private void ClearOrigin(CitizenRecord r)
        {
            _walkIn.Reset();
            ZDO zdo = OwnZdo();
            if (zdo != null && !string.IsNullOrEmpty(r.Origin) && zdo.GetString(SaveKeys.Doing, "") == r.Origin) zdo.Set(SaveKeys.Doing, "");
        }

        private ZDO OwnZdo()
        {
            ZNetView view = GetComponent<ZNetView>();
            return view != null && view.IsValid() && view.IsOwner() ? view.GetZDO() : null;
        }

        /// <summary>His free time (the self-test reads what he's doing).</summary>
        internal FreeTimeRoutine FreeTimeState => _freeTime;

        /// <summary>He is meant to be indoors now (night, a raid, an attack): after a recall he goes back in.</summary>
        internal bool IndoorsNow => _indoors != IndoorReason.None;

        /// <summary>Puts him on a spot at once (a rescue or a recall): body and all, no fall counted, his walk forgotten.</summary>
        private Vector3 PlaceAt(Vector3 spot)
        {
            _freeTime.End();   // off his seat first, or holding him there would pull him back
            if (_lying) GetUp();   // out of bed too, and off breakfast (the whole-mod review of 1.4.1: a recall left him lying or at the table)
            if (_breakfast.Running) _breakfast.Interrupt();
            _makeWayUntil = 0f;   // a recall or a rescue puts him elsewhere: no walking back to where he made way
            Vector3 to = spot + Vector3.up * 0.1f;
            transform.position = to;
            Rigidbody body = GetComponent<Rigidbody>();
            if (body != null)
            {
                body.position = to;
                body.linearVelocity = Vector3.zero;
            }
            s_maxAirAltitude(_citizen.Body) = to.y;   // Seen in review: from higher up the move would count as a fall
            ForgetPath();
            StopMoving();
            return to;
        }

        /// <summary>
        /// A spot 3 to 8 m from <paramref name="centre"/> on the ground (not a roof or a rock), or on a floor level with the
        /// Town Hall (it may stand on one), dry, no higher or lower than 2 m against the ground there, with room for him to
        /// stand, and out of every player's sight unless <paramref name="anyway"/>. Seen in review: a Town Hall on a
        /// wooden platform had no spot at all.
        /// </summary>
        private Vector3? FreeSpotNear(Vector3 centre, bool anyway, Func<Vector3, bool> skip = null)
        {
            if (s_standMask == 0) s_standMask = LayerMask.GetMask("piece", "piece_nonsolid", "Default", "static_solid", "Default_small", "character", "vehicle");
            float groundThere = ZoneSystem.instance.GetGroundHeight(centre);
            // Never in or beside a fire (1.4.0): the camp's fire pit burns in front of the Town Hall's chest, and its stones
            // are too low for the standing-room check below.
            var fires = new List<FireRing>();
            foreach (FirePlace fire in ColonyPlaces.Fires(_citizen.ColonyId))
                if (fire != null && fire.Valid) fires.Add(new FireRing(fire.Position.x, fire.Position.z, fire.Radius));
            foreach (float radius in new[] { 3f, 4.5f, 6f, 8f })
                for (int i = 0; i < 8; i++)
                {
                    float angle = i * Mathf.PI / 4f;
                    var spot = new Vector3(centre.x + Mathf.Cos(angle) * radius, 0f, centre.z + Mathf.Sin(angle) * radius);
                    float ground = ZoneSystem.instance.GetGroundHeight(spot), solid = ZoneSystem.instance.GetSolidHeight(spot);
                    bool onGround = solid - ground <= 0.3f, onHallsFloor = Mathf.Abs(solid - centre.y) <= 0.5f;
                    if (!onGround && !onHallsFloor) continue;   // a roof or a rock
                    spot.y = onGround ? ground : solid;
                    if (!onGround) ground = groundThere;   // level with the Town Hall: as high as it
                    if (ground < ZoneSystem.instance.m_waterLevel || Mathf.Abs(ground - groundThere) > 2f) continue;
                    if (!anyway && NearestPlayer(spot) <= TrappedWatch.SeenFrom) continue;
                    if (Physics.CheckCapsule(spot + Vector3.up * 0.5f, spot + Vector3.up * 1.6f, 0.4f, s_standMask, QueryTriggerInteraction.Ignore)) continue;
                    if (!RescueSpot.ClearOfFires(spot.x, spot.z, fires)) continue;
                    if (skip != null && skip(spot)) continue;
                    return spot;
                }
            return null;
        }

        private static int s_standMask;
        private bool _toldNoFreeSpot;
        private const float TrappedBackOffSeconds = 10f;
        private static readonly AccessTools.FieldRef<Character, float> s_maxAirAltitude = AccessTools.FieldRefAccess<Character, float>("m_maxAirAltitude");

        /// <summary>How many times a boxed-in settler was moved out (self-test).</summary>
        internal static int Rescues;

        private int _errorsSeen, _hops, _sidesteps, _rescuesHere;
        /// <summary>Walking this long without getting anywhere goes in his bug-report troubles.</summary>
        private const double StuckReportSeconds = 10;

        /// <summary>
        /// vc_bugreport (0.27): what went wrong for him on this computer — errors in his work or update, rescues, refuges
        /// given up, hops and steps aside, and how long he has been walking without getting anywhere; "" when nothing.
        /// </summary>
        internal string Troubles()
        {
            var parts = new List<string>();
            void Count(int n, string one, string many) { if (n > 0) parts.Add($"{n} {(n == 1 ? one : many)}"); }
            Count(_errorsSeen, "error", "errors");
            Count(_rescuesHere, "rescue", "rescues");
            Count(RefugesGivenUp, "refuge given up", "refuges given up");
            Count(_hops, "hop", "hops");
            Count(_sidesteps, "step aside", "steps aside");
            if (_trapped.WalkedFor >= StuckReportSeconds) parts.Add($"walking {_trapped.WalkedFor:0} s without getting anywhere");
            else if (_trapped.WantedFor >= StuckReportSeconds) parts.Add($"wanting to be elsewhere {_trapped.WantedFor:0} s without getting anywhere");
            return string.Join(", ", parts);
        }

        private static string At(Vector3 p) => $"({p.x:0.0}, {p.y:0.0}, {p.z:0.0})";

        /// <summary>A step aside lands on ground no lower than a stair's step, and not in water.</summary>
        private static bool SafeStep(Vector3 me, Vector3 direction)
        {
            if (s_wallMask == 0) s_wallMask = LayerMask.GetMask("piece");
            Vector3 landing = me + direction * 1.5f;
            int ground = s_wallMask | LayerMask.GetMask("terrain", "Default", "static_solid");
            if (!Physics.Raycast(landing + Vector3.up * 1.5f, Vector3.down, out RaycastHit hit, 3f, ground, QueryTriggerInteraction.Ignore)) return false;
            return hit.point.y > me.y - 0.6f && hit.point.y >= ZoneSystem.instance.m_waterLevel;
        }

        /// <summary>One line per settler with its movement settings, so choppy movement can be explained from the log.</summary>
        private void LogMovementSettings()
        {
            if (_movementLogged) return;
            _movementLogged = true;
            Humanoid body = _citizen.Body;
            Rigidbody rigidbody = GetComponent<Rigidbody>();
            ZNetView nview = GetComponent<ZNetView>();
            Log.Info($"{_citizen.CitizenName} movement: walk {body.m_walkSpeed}, jog {body.m_speed}, sprint {body.m_runSpeed}, " +
                     $"turn {body.m_turnSpeed}/{body.m_runTurnSpeed}, turn-while-walking {m_moveMinAngle} deg, " +
                     $"interpolation {(rigidbody != null ? rigidbody.interpolation.ToString() : "none")}, " +
                     $"network {(nview != null ? nview.m_type + (nview.m_distant ? " distant" : "") : "none")}, {_citizen.DescribeBody()}");
        }

        /// <summary>A walk with a path (for the door user); true when there, or when the path ends.</summary>
        internal bool WalkTowards(Vector3 point, float stopDistance) => PathTo(point, stopDistance, false);

        /// <summary>A walk with a path and nothing more (waiting his turn at a door: no hops among the others).</summary>
        internal bool WalkPlain(Vector3 point, float stopDistance) => MoveTo(_dt, point, stopDistance, false);

        /// <summary>
        /// The game's walk along a path (true when it ends), with a way out when he doesn't move although the path is
        /// open (a log, a lip, another settler the paths don't know): a hop, then a step to the side (<see cref="Unstuck"/>).
        /// </summary>
        private bool PathTo(Vector3 point, float stopDistance, bool run)
        {
            Vector3 me = transform.position;
            if (Time.time < _sidestepUntil)
            {
                MoveTowards(_sidestep, false);
                return false;
            }
            if (MoveTo(_dt, point, stopDistance, run)) return true;   // the path's end (or none): standing there is meant
            switch (_unstuck.Check(me.x, me.z, Time.time, Utils.DistanceXZ(me, point)))
            {
                case UnstuckMove.Hop:
                    _hops++;
                    Log.Info($"{_citizen.CitizenName} hasn't moved for {Unstuck.HopAfter:0} s on his way to ({point.x:0.0}, {point.z:0.0}): he hops " +
                             $"(at ({me.x:0.0}, {me.y:0.0}, {me.z:0.0}))");
                    _citizen.Body.Jump();
                    ForgetPath();   // the path he kept leads back into what stopped him
                    break;
                case UnstuckMove.Sidestep:
                    Vector3 way = point - me;
                    way.y = 0f;
                    Vector3 right = Vector3.Cross(Vector3.up, way.sqrMagnitude > 0.01f ? way.normalized : transform.forward);
                    int side = SafeStep(me, right * _unstuck.Side) ? _unstuck.Side : SafeStep(me, -right * _unstuck.Side) ? -_unstuck.Side : 0;
                    if (side == 0) break;   // a drop or water on both sides: he stays
                    _sidesteps++;
                    _sidestep = right * side;
                    _sidestepUntil = Time.time + Unstuck.SidestepSeconds;
                    ForgetPath();
                    Log.Info($"{_citizen.CitizenName} still hasn't moved on his way to ({point.x:0.0}, {point.z:0.0}): he steps to the " +
                             $"{(side > 0 ? "right" : "left")} (at ({me.x:0.0}, {me.y:0.0}, {me.z:0.0}))");
                    MoveTowards(_sidestep, false);
                    break;
            }
            return false;
        }

        /// <summary>A hop, as a player's jump (over a lip or a log in a doorway).</summary>
        internal void Hop() => _citizen.Body.Jump();

        /// <summary>The way he is going: his path's next point, else the way he faces (<see cref="DoorRules.WayHeading"/>).</summary>
        internal Vector3 WayHeading()
        {
            List<Vector3> path = s_path(this);
            Vec3 heading = DoorRules.WayHeading(CoreVectors.V(transform.position), path != null ? path.ConvertAll(CoreVectors.V) : null,
                CoreVectors.V(transform.forward));
            return CoreVectors.U(heading);
        }

        /// <summary>A straight step without a path (through a doorway the paths don't know is open yet).</summary>
        internal void StepTowards(Vector3 point)
        {
            Vector3 direction = point - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.0001f) MoveTowards(direction.normalized, false);
        }

        /// <summary>The game's paths reach this point from where he stands (all the way, not just near it).</summary>
        internal bool CanWalkTo(Vector3 point) => HavePath(point);

        /// <summary>The game's paths go all the way from <paramref name="from"/> to <paramref name="to"/>.</summary>
        internal bool WayReaches(Vector3 from, Vector3 to) => Pathfinding.instance != null && Pathfinding.instance.HavePath(from, to, m_pathAgentType);

        /// <summary>He is going through a door (his job's walk waits meanwhile; it isn't stuck).</summary>
        internal bool UsingDoor => _doors.Busy;

        /// <summary>vc_watch (0.24.1): what his walking does now, in one line: his path, goal, the door near him, why indoors.</summary>
        internal string WatchState()
        {
            var text = new System.Text.StringBuilder();
            List<Vector3> path = s_path(this);
            if (path != null && path.Count > 0)
            {
                Vector3 end = path[path.Count - 1];
                text.Append($"path {path.Count} points to ({end.x:0.0}, {end.z:0.0})");
            }
            else text.Append("no path");
            if (_hasGoal) text.Append($", goal ({_goal.x:0.0}, {_goal.z:0.0}) {Utils.DistanceXZ(transform.position, _goal):0.0} m off");
            Door near = null;
            float best = 4f;
            foreach (Door door in Jobs.WorldScan.Doors())
            {
                if (door == null) continue;
                float distance = Utils.DistanceXZ(door.transform.position, transform.position);
                if (distance < best)
                {
                    best = distance;
                    near = door;
                }
            }
            if (near != null) text.Append($", {Utils.GetPrefabName(near.gameObject)} {best:0.0} m, {(DoorUser.IsOpen(near) ? "open" : "closed")}");
            if (_doors.Busy) text.Append(", going through a door");
            if (_indoors != IndoorReason.None) text.Append($", indoors: {_indoors}");
            if (Time.time < _testWalkUntil) text.Append($", test walk to ({_testWalkTo.x:0.0}, {_testWalkTo.z:0.0})");
            return text.ToString();
        }

        /// <summary>Self-test: his "Town Hall gone" count so far and what he saw last.</summary>
        internal string ColonyGoneForTest => $"{_colonyGone.Streak} in a row, last {_colonyGone.Last}";

        /// <summary>How many times he went through a door (self-test).</summary>
        internal int DoorPasses => _doors.Passes;

        /// <summary>
        /// A building piece stands between him and the goal at knee height: a wall, or the wall under a window. The goal's
        /// own piece (a chest) doesn't count.
        /// </summary>
        private bool WallBetween(Vector3 goal)
        {
            if (s_wallMask == 0) s_wallMask = LayerMask.GetMask("piece");
            Vector3 from = transform.position + Vector3.up * KneeHeight, to = goal + Vector3.up * KneeHeight;
            if (!Physics.Linecast(from, to, out RaycastHit hit, s_wallMask, QueryTriggerInteraction.Ignore)) return false;
            Piece piece = hit.collider.GetComponentInParent<Piece>();
            return piece == null || Utils.DistanceXZ(piece.transform.position, goal) > 0.5f;
        }

        /// <summary>How far short of <paramref name="to"/> the game's way from <paramref name="from"/> ends; MaxValue when there is no way.</summary>
        internal float WayEndGap(Vector3 from, Vector3 to)
        {
            Pathfinding paths = Pathfinding.instance;
            if (paths == null || !paths.GetPath(from, to, s_way, m_pathAgentType, false, true, false) || s_way.Count == 0) return float.MaxValue;
            return Utils.DistanceXZ(s_way[s_way.Count - 1], to);
        }

        /// <summary>The next walk asks for a fresh path (the game keeps a path for up to 5 s for the same goal).</summary>
        internal void ForgetPath() => s_lastFindPathTime(this) = -1000f;

        /// <summary>Self-test: walks to this point for a while instead of working.</summary>
        internal void TestWalk(Vector3 point, float seconds)
        {
            _testWalkTo = point;
            _testWalkUntil = Time.time + seconds;
            TestWalkDone = false;
        }

        /// <summary>Worker test: his job now (null when none).</summary>
        internal Job JobForTest => _job;

        /// <summary>
        /// Worker test (0.20): as if he had tried to walk for 31 s without getting anywhere, here; then the wedged-in-a-hut
        /// rescue looks at once. True when it moved him.
        /// </summary>
        internal bool WedgeForTest()
        {
            Vector3 at = transform.position;
            double now = Time.time;
            for (double t = now - TrappedWatch.InsideHutAfter - 1.0; t <= now; t += 0.5) _trapped.Walking(at.x, at.z, t);
            return RescueIfWedgedInHut();
        }

        /// <summary>Self-test: he got to the point of the last <see cref="TestWalk"/>.</summary>
        internal bool TestWalkDone { get; private set; }

        /// <summary>Self-test: how many times he gave up on a hut as a refuge.</summary>
        internal int RefugesGivenUp { get; private set; }

        /// <summary>Self-test: the hut he is getting into ("" none).</summary>
        internal string RefugeForTest => _refugeId;

        /// <summary>Self-test: a building piece stands between him and the point.</summary>
        internal bool WallBetweenForTest(Vector3 goal) => WallBetween(goal);

        /// <summary>No wall or floor of a building between him and the point (its own piece aside): free time sits him down from here.</summary>
        internal bool NothingBetween(Vector3 goal) => !WallBetween(goal);

        /// <summary>Test tool (vc_ui sleepers): the hut he went into for the night or shelter, and whether he counts himself inside.</summary>
        internal string RefugeIdForTest => _refugeId;
        internal bool InsideForTest => _inside;
        internal Vector3 RefugeChestForTest => _refugeChest;

        /// <summary>Self-test: the huts he gave up on count as refuges again.</summary>
        internal void ForgetBadRefugesForTest() => _badRefuges = new TargetBlacklist(IndoorRules.SkipSeconds);

        public void Face(Vector3 point) => LookAt(point);

        public bool IsFacing(Vector3 point) => IsLookingAt(point, 20f);

        public void Idle() => StopMoving();
    }
}
