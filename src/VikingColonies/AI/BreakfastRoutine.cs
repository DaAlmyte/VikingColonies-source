using System.Collections.Generic;
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
    /// Breakfast at the table (settler life, 0.15): when his meal of the day is due, before work, he walks to the colony
    /// chest that holds the food he'd eat (the plainest, as <see cref="Meals"/> says; the Town Hall's when as near), takes
    /// one, and eats it on a seat near the chest, or standing by it. If he can't get there and take it within a minute he
    /// gives up for today and eats where he stands, as before (<see cref="MealTime"/>).
    /// </summary>
    internal sealed class BreakfastRoutine
    {
        private const float ReachChest = 2.5f;
        private const float ChestPatience = 60f;
        private const float SeatPatience = 20f;
        private const float EatSeconds = 10f;

        private enum Step { None, ToChest, ToSeat, Eating }

        private readonly CitizenAI _ai;
        private readonly Citizen _citizen;
        private readonly SettlerPose _pose;
        private readonly CarryLoad _plate = new CarryLoad();
        private Step _step = Step.None;
        private string _chestId = "";

        /// <summary>Self-test: the chest he last went to for breakfast.</summary>
        internal string LastChestForTest { get; private set; } = "";
        private string _food = "";
        private float _since;
        private float _eatUntil;
        private float _nextBite;
        private SeatPlace _seat;
        private Vector3 _seatSpot;
        private int _gaveUpDay = Meals.NeverAte;
        private string _savedDoing = "";

        public BreakfastRoutine(CitizenAI ai, Citizen citizen)
        {
            _ai = ai;
            _citizen = citizen;
            _pose = citizen.GetComponent<SettlerPose>();
        }

        public bool Running => _step != Step.None;

        /// <summary>His meal is due and he hasn't given up on a breakfast today: he goes to eat now.</summary>
        public bool Due()
        {
            CitizenRecord r = _citizen.Record;
            if (!ColonyConfig.SettlersEat.Value || !r.HasColony || EnvMan.instance == null || ZNet.instance == null) return false;
            int today = Today();
            if (Meals.FirstDay(r.LastMealDay) || !Meals.Due(r.LastMealDay, today, EnvMan.IsNight()) || _gaveUpDay == today)
            {
                _dueSince = -1f;
                return false;
            }
            if (_dueSince < 0f) _dueSince = Time.time;
            return true;
        }

        /// <summary>
        /// Each sets off at his own moment in the first minute (0.18: twenty settlers reached one chest on the same tick at
        /// dawn); meanwhile he stands where he is, his work not begun (review: a worker set off to work, then turned back).
        /// </summary>
        private bool Waiting(float now) => _dueSince >= 0f && now - _dueSince < Breakfast.Delay(_citizen.Id);

        private float _dueSince = -1f;

        /// <summary>Whether the meal is this routine's to serve today (else <see cref="MealTime"/> eats where he stands).</summary>
        public bool Serves => ColonyConfig.SettlersEat.Value && _gaveUpDay != Today();

        public void Tick()
        {
            float now = Time.time;
            if (_step != Step.None) SaveDoing("$vc_doing_eating");   // again after an interruption cleared it
            switch (_step)
            {
                case Step.None:
                    if (Waiting(now))
                    {
                        // Not "Sleeping" any more while he waits for his moment at dawn (a full run read it by day).
                        if (_citizen.Status == "$vc_status_sleeping") _citizen.Status = "$vc_status_waking";
                        _ai.Idle();
                        return;
                    }
                    Start(now);
                    break;
                case Step.ToChest: ToChest(now); break;
                case Step.ToSeat: ToSeat(now); break;
                case Step.Eating: Eat(now); break;
            }
        }

        /// <summary>
        /// He was called away (a monster, a raid, night): he gets up at once (review of 0.15: held on the seat he couldn't
        /// flee or go in), and the food in hand is eaten standing once he's back; without food yet he starts over.
        /// </summary>
        public void Interrupt()
        {
            if (_pose != null && _pose.Current != SettlerPose.Kind.None) _pose.Stand();
            if (_seat != null) FreeTimeClaims.Release(_seat.Id, _citizen.Id);
            _seat = null;
            if (_plate.Total == 0) _step = Step.None;
            else if (_step == Step.ToSeat) _step = Step.Eating;
            SaveDoing("");
        }

        private void Start(float now)
        {
            CitizenRecord r = _citizen.Record;
            var stock = new Dictionary<string, int>();
            var chests = new List<ColonyBuilding>();
            foreach (ColonyBuilding b in ColonyBuilding.All)
            {
                if (b == null || !b.IsValid || b.ColonyId != r.ColonyId || b.Storage == null) continue;
                chests.Add(b);
                foreach (KeyValuePair<string, int> item in b.Storage.SpareContents())   // kept food is left (0.21)
                    if (FoodItems.Value(item.Key) > 0f) stock[item.Key] = (stock.TryGetValue(item.Key, out int n) ? n : 0) + item.Value;
            }
            _food = Meals.Choose(FoodItems.In(stock), _citizen.Record.LastMeals) ?? "";   // not what he had lately, when there's a choice
            Vector3 me = _citizen.transform.position;
            _chestId = LastChestForTest = Breakfast.Chest(chests.Select(b => new BreakfastChest(b.Id, new GroundPoint(b.transform.position.x, b.transform.position.z),
                b.IsTownHall, _food.Length > 0 && b.Storage.Spare(_food) > 0)), new GroundPoint(me.x, me.z));
            if (_food.Length == 0 || _chestId.Length == 0)
            {
                GiveUp("nothing to eat in the colony's chests");
                return;
            }
            _step = Step.ToChest;
            _since = now;
            _citizen.Status = "$vc_status_eating";
            SaveDoing("$vc_doing_eating");
            Log.Info($"{_citizen.CitizenName} goes to breakfast: {FoodItems.Name(_food)} from {_chestId}");
        }

        private void ToChest(float now)
        {
            _citizen.Status = "$vc_status_eating";
            ColonyBuilding chest = ColonyBuilding.FindById(_chestId);
            if (chest == null || chest.Storage == null || now - _since > ChestPatience)
            {
                GiveUp(chest == null ? "their chest is gone" : "couldn't get to the chest in time");
                return;
            }
            if (!_ai.GoTo(chest.transform.position, ReachChest)) return;
            _ai.Idle();
            _ai.Face(chest.transform.position);
            if (!chest.Storage.TryTakeItem(_food, _plate))
            {
                // Someone ate the last of it on the way: he chooses again (review of 0.15); a busy chest: again next tick.
                if (chest.Storage.Spare(_food) <= 0) _step = Step.None;
                return;
            }
            // A seat near the chest is his table; none free: he eats standing.
            Vector3 at = chest.transform.position;
            _seat = ColonyPlaces.Seats(_citizen.ColonyId)
                .Where(s => s.Valid && Vector3.Distance(s.Point, at) <= Breakfast.SeatNear && !ColonyPlaces.Taken(s, _citizen)
                            && !FreeTimeClaims.TakenByOther(s.Id, s.Point, _citizen.Id))
                .OrderBy(s => Vector3.Distance(s.Point, at)).FirstOrDefault();
            _since = now;
            if (_seat != null)
            {
                FreeTimeClaims.TryClaim(_seat.Id, _seat.Point, _citizen.Id);
                _seatSpot = FreeTimeRoutine.SeatApproach(_seat);
                _step = Step.ToSeat;
            }
            else StartEating(now);
        }

        private void ToSeat(float now)
        {
            _citizen.Status = "$vc_status_eating";
            if (_seat == null || !_seat.Valid || ColonyPlaces.Taken(_seat, _citizen) || now - _since > SeatPatience)
            {
                if (_seat != null) FreeTimeClaims.Release(_seat.Id, _citizen.Id);
                _seat = null;
                StartEating(now);   // standing, where he is
                return;
            }
            Vector3 me = _citizen.transform.position, point = _seat.Point;
            bool close = Utils.DistanceXZ(me, point) <= 1.3f && Mathf.Abs(me.y - point.y) <= 1f && _ai.NothingBetween(point);
            if (!close && !_ai.GoTo(_seatSpot, 0.4f)) return;
            _ai.Idle();
            _pose?.SitOn(_seat.Chair);
            StartEating(now);
        }

        private void StartEating(float now)
        {
            _step = Step.Eating;
            _eatUntil = now + EatSeconds;
            _nextBite = now;
            _ai.Idle();
        }

        private void Eat(float now)
        {
            _citizen.Status = "$vc_status_eating";
            _ai.Idle();
            if (now >= _nextBite)
            {
                _pose?.Gesture("eat");
                _nextBite = now + 3f;
            }
            if (now < _eatUntil) return;
            _plate.Clear();
            _citizen.MarkMeal(Today(), hungry: false, item: _food);
            Log.Info($"{_citizen.CitizenName} ate {FoodItems.Name(_food)} at breakfast (day {Today()}).");
            Finish();
        }

        private void GiveUp(string why)
        {
            Log.Info($"{_citizen.CitizenName} has no breakfast at the table ({why}); they eat where they are.");
            _gaveUpDay = Today();
            Finish();
        }

        private void Finish()
        {
            _pose?.Stand();
            if (_seat != null) FreeTimeClaims.Release(_seat.Id, _citizen.Id);
            _seat = null;
            _step = Step.None;
            _chestId = _food = "";
            SaveDoing("");
        }

        /// <summary>This computer has just started running him: no breakfast half done.</summary>
        public void ResetForNewOwner()
        {
            _plate.Clear();
            _seat = null;
            _step = Step.None;
            _savedDoing = null;
        }

        private void SaveDoing(string token)
        {
            if (token == _savedDoing) return;
            ZNetView view = _citizen.GetComponent<ZNetView>();
            if (view == null || !view.IsValid() || !view.IsOwner()) return;
            view.GetZDO().Set(SaveKeys.Doing, token);
            _savedDoing = token;
        }

        private static int Today() => EnvMan.instance != null && ZNet.instance != null ? EnvMan.instance.GetDay(ZNet.instance.GetTimeSeconds()) : 0;
    }
}
