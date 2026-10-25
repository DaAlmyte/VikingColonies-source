using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VikingColonies.AI;
using VikingColonies.Buildings;
using VikingColonies.Citizens;
using VikingColonies.Config;
using VikingColonies.Construction;
using VikingColonies.Core;

namespace VikingColonies.Jobs
{
    /// <summary>
    /// The storehouse's courier (1.5, the storehouse's part 2): asks the storehouse for a task (<see cref="CourierDispatch"/>),
    /// takes at one building and puts at another: deliveries to whoever is short, producers' surplus to the storehouse,
    /// the drop-off put away (like with like), the store sorted, and the store emptied before its take-down (part 3). A busy chest (a player has it open, another computer hands it over) is
    /// waited for; what it couldn't put goes back to the store, else to the Town Hall, else it holds it a while; with
    /// nothing to carry it takes free time by the storehouse.
    /// </summary>
    internal sealed class CourierJob : Job
    {
        private const float Reach = 2.5f;
        private const float AskSeconds = 3f;
        /// <summary>A take or a put, before skill and hunger.</summary>
        private const float HandSeconds = 1.5f;
        /// <summary>A task not done by then is given up (the way blocked, a chest kept open).</summary>
        private const float TaskSeconds = 240f;
        /// <summary>Nowhere to put what it holds: it tries again after this long (its review: a full store kept it at it for good).</summary>
        private const float HoldSeconds = 60f;

        private readonly ColonyBuilding _hut;
        private readonly StuckDetector _stuck = new StuckDetector();
        private CourierTask _task;
        private bool _taken;
        private float _since, _askAt, _handUntil = -1f, _holdUntil;
        private string _doneKey = "";
        private double _doneNet = double.NegativeInfinity;
        private string _savedDoing;
        /// <summary>Sorting: the chest it takes from, so the stack never goes back into it.</summary>
        private StorageModule _sortFrom;
        /// <summary>
        /// The chests already done in this part of the task (taking, then putting): it walks to each chest it uses, one after
        /// another (the review round of 2026-10-23: it reached every chest of the store room from the hut's own chest, through
        /// the back wall).
        /// </summary>
        private readonly HashSet<StorageModule> _done = new HashSet<StorageModule>();
        /// <summary>
        /// The chest it walks to now: chosen once and kept until it hands there (its review: chosen afresh every tick, and its
        /// stuck clock carried over, so after one hand-over from afar the next chest's counted at once).
        /// </summary>
        private StorageModule _chest;
        private int _took;
        /// <summary>Putting back with no task: given up after a while (the review round: a blocked stair kept it out all night).</summary>
        private float _putBackSince = -1f;
        private readonly HashSet<StorageModule> _putBackDone = new HashSet<StorageModule>();
        /// <summary>vc_storehousetest: the farthest it stood from a chest it took from or put into.</summary>
        internal static float MaxHandDistanceForTest;

        /// <summary>vc_storehousetest: trips that moved something, and the last task given up.</summary>
        internal static int TripsForTest;
        internal static string LastFailedForTest = "";
        /// <summary>vc_storehousetest: how long the last take took (its review: 150 single takes stuttered).</summary>
        internal static double LastTakeMsForTest;

        /// <summary>How much a courier of this storehouse carries a trip (Courier.Carry and CarryPerLevel by its level).</summary>
        public static int LoadAt(ColonyBuilding storehouse) =>
            CourierRules.Load(storehouse != null ? storehouse.Level : 1, ColonyConfig.CourierCarry.Value, ColonyConfig.CourierCarryPerLevel.Value);

        public CourierJob(Citizen citizen, CitizenAI ai, ColonyBuilding hut) : base(citizen, ai)
        {
            _hut = hut;
        }

        /// <summary>The task it is on (vc_storehousetest), or null.</summary>
        internal CourierTask TaskForTest => _task;

        public override void Tick(float dt)
        {
            ColonyBuilding hut = _hut;
            CourierDispatch dispatch = hut != null && hut.IsValid ? hut.GetComponent<CourierDispatch>() : null;
            if (dispatch == null)
            {
                Citizen.Status = "$vc_status_unemployed";
                Ai.Idle();
                return;
            }
            if (_task == null)
            {
                if (Citizen.Carry.Total > 0)
                {
                    // Its hands full: no new task is asked for meanwhile (the review round: a courier holding a load kept the
                    // most urgent task claimed from the others for minutes).
                    if (Time.time >= _holdUntil) PutBack(hut);
                    else
                    {
                        Citizen.Status = "$vc_status_courier_holding";
                        SaveDoing("", "", "");
                        Ai.FreeTime(hut.transform.position);
                    }
                    return;
                }
                _putBackSince = -1f;
                if (Time.time >= _askAt)
                {
                    _askAt = Time.time + AskSeconds;
                    dispatch.RequestTask(Citizen.Id, _doneKey);
                }
                CourierTask given = dispatch.TaskFor(Citizen.Id);
                // The one just finished, until the owner has seen it done (a task given after it is a new one).
                bool old = given != null && given.Key == _doneKey && dispatch.SinceFor(Citizen.Id) <= _doneNet;
                if (given == null || old)
                {
                    Citizen.Status = "$vc_status_nothing_to_carry";
                    SaveDoing("", "", "");
                    Ai.FreeTime(hut.transform.position);
                    return;
                }
                Start(given);
            }
            if (Time.time - _since > TaskSeconds)
            {
                Log.Info($"{Citizen.CitizenName} gave up carrying ({_task.Kind} from {_task.From} to {_task.To}): too long on the way");
                Finish(dispatch, false);
                return;
            }
            ColonyBuilding target = ColonyBuilding.FindById(_taken ? _task.To : _task.From);
            if (target == null || !target.IsValid || target.Storage == null)
            {
                Finish(dispatch, false);   // gone: what it holds goes back next
                return;
            }
            ShowWhat();
            if (_taken) DiscardUnknown();
            StorageModule chest = _chest != null && !_done.Contains(_chest) ? _chest : _taken ? PutChest(target) : TakeChest(target);
            if (chest != _chest)
            {
                _chest = chest;
                _stuck.Reset();   // a new walk: getting nowhere is counted afresh
                _handUntil = -1f;
            }
            if (chest == null)
            {
                if (!_taken)
                {
                    if (_took > 0) ToPutting();
                    else Finish(dispatch, false);   // nothing there to take
                    return;
                }
                // Nowhere left to put it: what it still holds goes back next.
                Log.Info($"{Citizen.CitizenName} carried what fitted ({_task.Kind}) from {_task.From} to {_task.To}; {Citizen.Carry.Total} left in hand");
                Finish(dispatch, Citizen.Carry.Total == 0);
                return;
            }
            if (!Reached(chest.transform.position)) return;
            Ai.Idle();
            Ai.Face(chest.transform.position);
            if (_handUntil < 0f)
            {
                _handUntil = Time.time + HandSeconds / Mathf.Max(0.1f, Citizen.SkillSpeed(WorkSkill.Hauling));
                return;
            }
            if (Time.time < _handUntil) return;
            _handUntil = -1f;
            MaxHandDistanceForTest = Mathf.Max(MaxHandDistanceForTest, Utils.DistanceXZ(Citizen.transform.position, chest.transform.position));
            if (!_taken)
            {
                int took = Take(chest, target, out bool busy);
                if (took > 0)
                {
                    Citizen.SaveCarry();
                    _took += took;
                }
                if (busy) return;   // a busy chest is waited for (its review: another computer's chest)
                _done.Add(chest);
                _chest = null;
                if (_took >= Want()) ToPutting();
                return;
            }
            int had = Citizen.Carry.Total;
            bool waiting = PutAt(chest, target);
            if (Citizen.Carry.Total < had)
            {
                Citizen.SaveCarry();
                Citizen.Practise(WorkSkill.Hauling);
                TripsForTest++;
            }
            if (waiting) return;   // a busy chest there: waited for
            _done.Add(chest);
            _chest = null;
            if (Citizen.Carry.Total > 0) return;   // the next chest
            Log.Info($"{Citizen.CitizenName} carried {_took} ({_task.Kind}) from {_task.From} to {_task.To}");
            Finish(dispatch, true);
        }

        public override void Resume()
        {
            _stuck.Reset();
            _handUntil = -1f;
            _putBackSince = -1f;   // a put-back cut off by the evening starts its clock afresh (its review: held at once in the morning)
        }

        public override void OnNight() => _putBackSince = -1f;

        /// <summary>At dusk: its hands emptied first if anywhere has room; its task let go for the night.</summary>
        public override bool FinishingBeforeNight(float dt)
        {
            CourierDispatch dispatch = _hut != null && _hut.IsValid ? _hut.GetComponent<CourierDispatch>() : null;
            if (_task != null && dispatch != null) Finish(dispatch, true);   // let go, not failed
            if (Citizen.Carry.Total == 0 || _hut == null || !_hut.IsValid || Time.time < _holdUntil) return false;
            PutBack(_hut);
            return Citizen.Carry.Total > 0 && Time.time >= _holdUntil;   // nowhere with room, or given up: it goes in with it
        }

        public override void Stop()
        {
            CourierDispatch dispatch = _hut != null && _hut.IsValid ? _hut.GetComponent<CourierDispatch>() : null;
            if (_task != null && dispatch != null) dispatch.ReportDone(Citizen.Id, true);   // let go: someone else may carry it
            _task = null;
            SaveDoing("", "", "");
        }

        private void Start(CourierTask task)
        {
            _task = task;
            _taken = false;
            _sortFrom = null;
            _done.Clear();
            _chest = null;
            _took = 0;
            _since = Time.time;
            _handUntil = -1f;
            _stuck.Reset();
            Log.Info($"{Citizen.CitizenName} takes a task: {task.Kind} {task.Amount} {(task.Item.Length > 0 ? task.Item : "surplus")} from {task.From} to {task.To}");
        }

        private void ToPutting()
        {
            _taken = true;
            _done.Clear();
            _chest = null;
            _handUntil = -1f;
            _stuck.Reset();
        }

        private void Finish(CourierDispatch dispatch, bool delivered)
        {
            if (_task != null)
            {
                if (!delivered) LastFailedForTest = _task.Key;
                _doneKey = _task.Key;
                _doneNet = ConstructionModule.Now();
                dispatch.ReportDone(Citizen.Id, delivered);
            }
            _task = null;
            _taken = false;
            _sortFrom = null;
            _done.Clear();
            _chest = null;
            _took = 0;
            _handUntil = -1f;
            _askAt = Time.time + (delivered ? 0.5f : AskSeconds);
        }

        private int Want() => Mathf.Min(_task.Amount, LoadAt(_hut));

        /// <summary>The next chest to take from for the task (one not done yet), or null when none has anything for it.</summary>
        private StorageModule TakeChest(ColonyBuilding from)
        {
            string item = _task.Item;
            switch (_task.Kind)
            {
                case CourierTaskKind.Deliver:
                    // A storehouse's own need comes from its chests inside; anyone else's from the source's chests, its own
                    // chest (the drop-off) first.
                    IEnumerable<StorageModule> sources = _task.From == _task.To && from.Storehouse != null ? Inside(from) : from.Stores;
                    return sources.FirstOrDefault(c => c != null && !_done.Contains(c) && c.Spare(item) > 0);
                case CourierTaskKind.UrgentPickup:
                case CourierTaskKind.Pickup:
                    return !_done.Contains(from.Storage) && from.Storage.SpareContents().Any(i => i.Value > 0 && StorageModule.Carried(i.Key)) ? from.Storage : null;
                case CourierTaskKind.DropOff:
                    return !_done.Contains(from.Storage) && from.Storage.Spare(item) > 0 ? from.Storage : null;
                case CourierTaskKind.Clear:
                    // A storehouse emptied for its take-down (part 3): its chests inside, closed as they are.
                    IEnumerable<StorageModule> inside = from.Storehouse != null ? from.Storehouse.Chests : Enumerable.Empty<StorageModule>();
                    return inside.FirstOrDefault(c => c != null && !_done.Contains(c) && c.Contents().TryGetValue(item, out int n) && n > 0);
                case CourierTaskKind.Sort:
                    if (_sortFrom == null)
                    {
                        List<KeyValuePair<StorageModule, StoreChest>> chests = SortChests();
                        StoreChest least = CourierPlan.SortFrom(chests.Select(c => c.Value).ToList(), item);
                        _sortFrom = least != null ? chests.First(c => c.Value == least).Key : null;
                    }
                    return _sortFrom != null && !_done.Contains(_sortFrom) ? _sortFrom : null;
                default:
                    return null;
            }
        }

        /// <summary>Takes what the task wants at this one chest; how many. <paramref name="busy"/>: the chest wasn't free.</summary>
        private int Take(StorageModule chest, ColonyBuilding from, out bool busy)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                busy = false;
                int left = Want() - _took;
                if (left <= 0) return 0;
                if (_task.Kind != CourierTaskKind.UrgentPickup && _task.Kind != CourierTaskKind.Pickup)
                    return chest.TakeUpTo(_task.Item, left, Citizen.Carry, out busy);
                // Its surplus, any kinds: beyond its Keep in stock and its worker's working stock, carried items only.
                int taken = 0;
                foreach (KeyValuePair<string, int> item in chest.SpareContents().Where(i => StorageModule.Carried(i.Key)).OrderByDescending(i => i.Value).ToList())
                {
                    if (taken >= left) break;
                    taken += chest.TakeUpTo(item.Key, left - taken, Citizen.Carry, out bool waiting);
                    busy |= waiting;
                }
                return taken;
            }
            finally
            {
                LastTakeMsForTest = watch.Elapsed.TotalMilliseconds;
            }
        }

        /// <summary>Surplus and the drop-off go into the store's chests inside, like with like (the player's pick, 1.5).</summary>
        private bool PutsAway(ColonyBuilding to) =>
            to.Storehouse != null && (_task.Kind == CourierTaskKind.UrgentPickup || _task.Kind == CourierTaskKind.Pickup || _task.Kind == CourierTaskKind.DropOff);

        /// <summary>
        /// The next chest to put into (one not done yet), or null when nothing has room: a storehouse's own need into its own
        /// chest; surplus and the drop-off into the chests inside like with like, never the drop-off; a sorted stack into the
        /// chest holding more of it, never back into the one it came from; anything else into the building's own chest.
        /// </summary>
        private StorageModule PutChest(ColonyBuilding to)
        {
            if (to.Storehouse == null || _task.Kind == CourierTaskKind.Deliver)
                return !_done.Contains(to.Storage) ? to.Storage : null;
            if (_task.Kind == CourierTaskKind.Sort)
            {
                List<KeyValuePair<StorageModule, StoreChest>> others = SortChests().Where(c => c.Key != _sortFrom && !_done.Contains(c.Key)).ToList();
                var carried = new StoreChest { Items = new Dictionary<string, int> { { _task.Item, Citizen.Carry.Count(_task.Item) } } };
                StoreChest into = CourierPlan.SortInto(others.Select(c => c.Value).ToList(), _task.Item, carried);
                return others.Where(c => c.Value == into).Select(c => c.Key).FirstOrDefault();
            }
            return NextPutAway(Inside(to).ToList(), _done);
        }

        /// <summary>Like with like: the chest the load goes into next, among those not <paramref name="done"/> (null: no room).</summary>
        private StorageModule NextPutAway(List<StorageModule> chests, ICollection<StorageModule> done)
        {
            int at = CourierPlan.NextPutAway(Facts(chests), Citizen.Carry.ItemNames, Citizen.Carry.Count, DoneAt(chests, done), out _);
            return at >= 0 ? chests[at] : null;
        }

        /// <summary>
        /// Puts at this one chest what goes there, like with like; true when it waits for it (busy). With
        /// <paramref name="waitForLike"/> a busy chest holding the kind is waited for, not split into another (its review).
        /// </summary>
        private bool PutAwayAt(StorageModule chest, List<StorageModule> chests, ICollection<StorageModule> done, bool waitForLike)
        {
            int at = chests.IndexOf(chest);
            if (at < 0) return false;
            return CourierPlan.PutAwayAt(at, Citizen.Carry.ItemNames.ToList(), () => Facts(chests),
                (i, item) =>
                {
                    StorageModule.DepositResult result = chests[i].TryDeposit(Citizen.Carry, null, item);
                    return result == StorageModule.DepositResult.Busy ? PutOutcome.Busy
                        : result == StorageModule.DepositResult.Stored ? PutOutcome.Stored : PutOutcome.NoRoom;
                },
                Citizen.Carry.Count, DoneAt(chests, done), waitForLike);
        }

        private static List<StoreChest> Facts(List<StorageModule> chests) =>
            chests.Select(c => new StoreChest { Items = c.Contents(), RoomFor = c.RoomFor }).ToList();

        private static HashSet<int> DoneAt(List<StorageModule> chests, ICollection<StorageModule> done) =>
            new HashSet<int>(Enumerable.Range(0, chests.Count).Where(i => done.Contains(chests[i])));

        /// <summary>Puts at this one chest what goes there; true when it was busy (and waited for).</summary>
        private bool PutAt(StorageModule chest, ColonyBuilding to)
        {
            if (!PutsAway(to)) return chest.TryDeposit(Citizen.Carry) == StorageModule.DepositResult.Busy;
            return PutAwayAt(chest, Inside(to).ToList(), _done, waitForLike: true);
        }

        /// <summary>Items the game no longer knows (a removed mod's) are let go of, never held for good (its review).</summary>
        private void DiscardUnknown()
        {
            if (ObjectDB.instance == null) return;
            foreach (string item in Citizen.Carry.ItemNames.Where(i => ObjectDB.instance.GetItemPrefab(i) == null).ToList())
            {
                Log.Warn($"Unknown item '{item}' in {Citizen.CitizenName}'s load; discarding it.");
                Citizen.Carry.Remove(item, Citizen.Carry.Count(item));
                Citizen.SaveCarry();
            }
        }

        /// <summary>The storehouse's chests inside while it is open (none while it is taken down, its review).</summary>
        private static IEnumerable<StorageModule> Inside(ColonyBuilding storehouse) =>
            storehouse.Storehouse != null && storehouse.Storehouse.Open ? storehouse.Storehouse.Chests : Enumerable.Empty<StorageModule>();

        /// <summary>The chests inside, each with what it holds and its room, for sorting.</summary>
        private List<KeyValuePair<StorageModule, StoreChest>> SortChests() =>
            Inside(_hut).Select(c => new KeyValuePair<StorageModule, StoreChest>(c, new StoreChest { Items = c.Contents(), RoomFor = c.RoomFor })).ToList();

        /// <summary>
        /// No task, something in its hands: into the store's chests inside, like with like, walking to each; else the Town
        /// Hall's chest; with room nowhere, or not reached in a while, it holds the load a while (its review: a full store
        /// kept every courier at it, all night; the review round: a blocked stair kept it out all night).
        /// </summary>
        private void PutBack(ColonyBuilding hut)
        {
            if (_putBackSince < 0f)
            {
                _putBackSince = Time.time;
                _putBackDone.Clear();
            }
            if (Time.time - _putBackSince > TaskSeconds)
            {
                Hold($"{Citizen.CitizenName} couldn't get to a chest with room for what they carry ({Citizen.Carry.Total}); they keep it for now");
                return;
            }
            DiscardUnknown();
            if (Citizen.Carry.Total == 0) return;
            List<StorageModule> inside = Inside(hut).ToList();
            StorageModule chest = NextPutAway(inside, _putBackDone);
            ColonyBuilding hall = ColonyBuilding.FindTownHall(hut.ColonyId);
            bool toHall = false;
            if (chest == null && hall != null && hall.IsValid && hall.Storage != null && !_putBackDone.Contains(hall.Storage) && hall.Storage.HasRoomFor(Citizen.Carry))
            {
                chest = hall.Storage;
                toHall = true;
            }
            if (chest == null)
            {
                Hold($"{Citizen.CitizenName} has nowhere with room for what they carry ({Citizen.Carry.Total}); they keep it for now");
                return;
            }
            Citizen.Status = "$vc_status_courier_putting_back";
            SaveDoing("", "", "");
            if (!Reached(chest.transform.position)) return;
            Ai.Idle();
            int had = Citizen.Carry.Total;
            if (toHall) chest.TryDeposit(Citizen.Carry);
            else PutAwayAt(chest, inside, _putBackDone, waitForLike: false);   // no task to wait in: a busy chest is passed by
            _putBackDone.Add(chest);
            if (Citizen.Carry.Total < had) Citizen.SaveCarry();
            if (Citizen.Carry.Total == 0) _putBackSince = -1f;
        }

        private void Hold(string why)
        {
            _holdUntil = Time.time + HoldSeconds;
            _putBackSince = -1f;
            Log.Info(why);
        }

        /// <summary>Its status and hover: "carrying 50 Wood to the Builder's Hut", "picking up at the Farm".</summary>
        private void ShowWhat()
        {
            switch (_task.Kind)
            {
                case CourierTaskKind.Deliver:
                case CourierTaskKind.Clear:
                    Citizen.Status = _task.Kind == CourierTaskKind.Clear ? "$vc_status_courier_clearing" : "$vc_status_courier_delivering";
                    ColonyBuilding to = ColonyBuilding.FindById(_task.To);
                    SaveDoing(ActivityWords.Token(Leisure.CarryStock), Mathf.Min(_task.Amount, LoadAt(_hut)) + " " + ItemToken(_task.Item),
                        to != null && to.Type != null ? to.Type.NameToken : "");
                    break;
                case CourierTaskKind.UrgentPickup:
                case CourierTaskKind.Pickup:
                    Citizen.Status = "$vc_status_courier_picking_up";
                    ColonyBuilding from = ColonyBuilding.FindById(_task.From);
                    SaveDoing("$vc_doing_courier_picking_up", from != null && from.Type != null ? from.Type.NameToken : "", "");
                    break;
                default:
                    Citizen.Status = "$vc_status_courier_sorting";
                    SaveDoing("", "", "");
                    break;
            }
        }

        private void SaveDoing(string token, string partner, string partnerId)
        {
            string value = ActivityWords.Encode(token, partner, partnerId);
            if (value == _savedDoing) return;
            ZNetView view = Citizen.GetComponent<ZNetView>();
            if (view == null || !view.IsValid() || !view.IsOwner()) return;
            view.GetZDO().Set(SaveKeys.Doing, value);
            _savedDoing = value;
        }

        private static string ItemToken(string item)
        {
            ItemDrop drop = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(item)?.GetComponent<ItemDrop>() : null;
            return drop != null ? drop.m_itemData.m_shared.m_name : item;
        }

        /// <summary>
        /// There, or as close as it gets (upstairs, on a platform: from where the walking stopped), but never through a wall
        /// (the player's watch of 1.5: Ulf handed things over through the storehouse's wall). A wall between: it keeps
        /// trying the way in, and gives the task up when it's too long on the way.
        /// </summary>
        private bool Reached(Vector3 at)
        {
            if (Ai.GoTo(at, Reach))
            {
                _stuck.Reset();
                return true;
            }
            if (Ai.UsingDoor)
            {
                _stuck.Reset();
                return false;
            }
            float distance = Utils.DistanceXZ(Citizen.transform.position, at);
            if (!MoveRules.DeliverFromHere(_stuck.Update(distance, Time.time), distance)) return false;
            if (Ai.NothingBetween(at)) return true;
            if (Time.time - _lastWallNote > 30f)
            {
                _lastWallNote = Time.time;
                Log.Info($"{Citizen.CitizenName} can't get to the chest at {at} and won't hand over through the wall; still looking for the way in");
            }
            return false;
        }

        private float _lastWallNote = float.NegativeInfinity;
    }
}
