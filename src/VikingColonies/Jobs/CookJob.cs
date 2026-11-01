using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using VikingColonies.AI;
using VikingColonies.Buildings;
using VikingColonies.Citizens;
using VikingColonies.Colony;
using VikingColonies.Config;
using VikingColonies.Construction;
using VikingColonies.Core;
using static VikingColonies.Construction.CoreVectors;

namespace VikingColonies.Jobs
{
    /// <summary>
    /// The Mead Hall's cook (1.6, part 1): roasts the colony's spare raw food on the hall's cooking stations, takes each
    /// piece off when it's done into the hall's chest, and keeps the hearth going, so the settlers have a hot breakfast
    /// (docs/superpowers/specs/2026-10-24-vikingcolonies-cook-and-mead-hall-design.md). Every item goes on and comes off
    /// through the station's own calls, which run on the station's computer, as a player's do. Each tick decides afresh:
    /// what the station dropped first, then done food off, raw food in hand on, the rest to the chest, then a new trip.
    /// </summary>
    public class CookJob : Job
    {
        private const float StationReach = 2f;
        private const float ChestReach = 2.5f;
        /// <summary>Taking a piece off, or putting one on, takes this long (shorter with the Cooking skill).</summary>
        private const float HandSeconds = 1f;
        /// <summary>After items go on, the station's slots are read again only after this (from another computer they come later).</summary>
        private const float SlotSyncSeconds = 1.5f;
        private const float RetrySeconds = 2f;

        private static readonly System.Reflection.MethodInfo s_getSlot = AccessTools.Method(typeof(CookingStation), "GetSlot");
        private static readonly Func<CookingStation, bool> s_haveDone = Delegate<Func<CookingStation, bool>>("HaveDoneItem");
        private static readonly Func<CookingStation, bool> s_fireLit = Delegate<Func<CookingStation, bool>>("IsFireLit");
        private static readonly Func<CookingStation, float> s_fuel = Delegate<Func<CookingStation, float>>("GetFuel");
        private static readonly AccessTools.FieldRef<CookingStation, ZNetView> s_view = AccessTools.FieldRefAccess<CookingStation, ZNetView>("m_nview");

        private readonly ColonyBuilding _hut;
        private readonly FullChestRun _fullChest = new FullChestRun();
        private readonly StuckDetector _stuck = new StuckDetector();
        private readonly TargetBlacklist _blacklist = new TargetBlacklist(120);
        private readonly CookDrops _drops = new CookDrops();   // what's the cook's on the floor (minor 12)
        private readonly List<string> _lately = new List<string>();
        private int _owed;   // pieces taken off and not yet picked up (review 5)
        private float _lastTakeOff = float.NegativeInfinity;
        private float _fedAt = float.NegativeInfinity;   // the cook last put fuel on a fire (the minors' review 2)
        private float _planFailedAt = float.NegativeInfinity;
        private CookingStation _sentTo;   // the last pieces put on: where, how many slots should then be filled, when (review 4)
        private int _filledExpected;
        private float _sentAt;
        private float _handTimer, _retryAt, _slotsReadAt;
        private ItemDrop _drop;
        // A trip for raw food or wood: what, how many, from which chest, for which station.
        private string _fetchItem = "";
        private int _fetchWant;
        private StorageModule _fetchChest;
        private CookingStation _fetchFor;
        private float _fetchSince;
        private string _lastLog = "";

        public CookJob(Citizen citizen, CitizenAI ai, ColonyBuilding hut) : base(citizen, ai)
        {
            _hut = hut;
        }

        private MeadHallModule Hall => _hut != null ? _hut.GetComponent<MeadHallModule>() : null;

        public override void Tick(float dt)
        {
            MeadHallModule hall = Hall;
            if (hall == null || _hut.Storage == null)
            {
                Citizen.Status = "$vc_status_unemployed";
                Ai.Idle();
                return;
            }
            if (_fullChest.Tick(Citizen, Ai, _hut)) return;
            float now = Time.time;
            IReadOnlyList<CookingStation> stations = hall.Stations();

            // What the station dropped comes first, so it reaches the chest and not the floor (Review Focus 2).
            if (_owed > 0 && !CookRules.KeepGathering(_owed, now - _lastTakeOff)) _owed = 0;   // never came: given up
            if (_owed > 0 && Gather(stations, now)) return;
            CookingStation done = stations.FirstOrDefault(HasDone);
            bool hot = CarriesHot();
            float toDone = done != null ? Utils.DistanceXZ(Citizen.transform.position, done.transform.position) : float.MaxValue;
            if (hot ? CookRules.TakeMoreOff(Citizen.Carry.Total, ColonyConfig.CookCarryCapacity.Value, done != null, toDone) : done != null)
            {
                Citizen.Status = "$vc_status_cook_taking_off";   // every done piece off, then one walk to the chest (minor 12)
                TakeOff(done, now);
                return;
            }
            if (hot)
            {
                Citizen.Status = "$vc_status_cook_taking_off";
                Deliver(dt);
                return;
            }
            if (CarriedFuel(stations, out CookingStation cold, out string fuel))
            {
                Citizen.Status = "$vc_status_cook_roasting";
                FeedNow(cold, fuel);
                return;
            }
            if (_fetchItem.Length > 0 && Fetch(now)) return;
            if (CarriedRaw(stations, out CookingStation forStation, out string raw))
            {
                Citizen.Status = "$vc_status_cook_roasting " + ItemToken(raw);
                PutOn(forStation, raw, now);
                return;
            }
            if (Citizen.Carry.Total > 0)   // wood left over, or raw food no station takes now: to the hall's chest
            {
                Deliver(dt);
                return;
            }
            if (stations.Count == 0)
            {
                Say("$vc_status_cook_no_station", "has no cooking station in the Mead Hall");
                Rest();
                return;
            }
            if (!CookRules.MayPlan(now, _planFailedAt))   // nothing to do a moment ago: not every tick again (review 2)
            {
                Rest();
                return;
            }
            CookingStation stalled = stations.FirstOrDefault(s => CookRules.NeedsFuel(Lit(s), 0, Uncooked(s)) && NeedOf(s) == FireNeed.Fuel);
            if (stalled != null)   // food on a station whose fire died: fed first (review 1)
            {
                if (!PlanFuel(stalled)) Failed("$vc_status_cook_hearth_out", "finds the hearth out and no wood in the chests", now);
                Rest();
                return;
            }
            if (!CookRules.ShouldRoast(HotInHall(), Settlers()))
            {
                Citizen.Status = "$vc_status_cook_ready";
                Rest();
                return;
            }
            List<CookingStation> free = stations.Where(s => FreeSlots(s) > 0).ToList();
            if (free.Count == 0)
            {
                List<CookingStation> cooking = stations.Where(s => Uncooked(s) > 0).ToList();
                if (cooking.Count > 0 && cooking.All(s => NeedOf(s) == FireNeed.WontBurn)) Failed("$vc_status_cook_fire_wont_burn", WontBurnLog, now);   // minor 8
                else Citizen.Status = "$vc_status_cook_roasting";
                Rest();
                return;
            }
            CookingStation lit = free.FirstOrDefault(Lit);
            if (lit == null)
            {
                CookingStation feedable = free.FirstOrDefault(s => NeedOf(s) == FireNeed.Fuel);
                if (feedable == null && free.Any(s => NeedOf(s) == FireNeed.Catching)) Citizen.Status = "$vc_status_cook_roasting";   // just fed: a moment
                else if (feedable == null) Failed("$vc_status_cook_fire_wont_burn", WontBurnLog, now);   // wood can't help (minor 8)
                else if (!PlanFuel(feedable)) Failed("$vc_status_cook_hearth_out", "finds the hearth out and no wood in the chests", now);
                Rest();
                return;
            }
            if (!PlanRaw(lit)) Failed("$vc_status_cook_no_raw", "finds no raw food the colony can spare", now);
            Rest();
        }

        private const string WontBurnLog = "finds no fire burning under the cooking station though wood can't help (no hearth under it, rain on it, or the station off the flames)";

        /// <summary>A plan that found nothing: its status, and no planning again for a moment.</summary>
        private void Failed(string status, string log, float now)
        {
            Say(status, log);
            _planFailedAt = now;
        }

        public override void Resume()
        {
            _stuck.Reset();
            _retryAt = 0f;
        }

        /// <summary>At dusk: what's in hand goes to the hall's chest first (raw food too: it's spare there for tomorrow).</summary>
        public override bool FinishingBeforeNight(float dt)
        {
            if (_hut == null || _hut.Storage == null) return false;
            if (_fullChest.Tick(Citizen, Ai, _hut)) return true;
            if (CookRules.KeepGathering(_owed, Time.time - _lastTakeOff) && Hall != null && Gather(Hall.Stations(), Time.time)) return true;
            if (Citizen.Carry.Total <= 0) return false;
            Deliver(dt);
            return true;
        }

        public override void Stop() => ForgetTrip();

        // ---- the steps ----

        /// <summary>Walks to the station, then takes one done piece off through the station's own call; it drops by the station.</summary>
        private void TakeOff(CookingStation station, float now)
        {
            ForgetTrip();
            if (!Reach(station.transform.position, StationReach)) return;
            Ai.Idle();
            Ai.Face(station.transform.position);
            if (now < _slotsReadAt) return;   // the last piece's slot isn't back from the station's computer yet
            _handTimer += Time.deltaTime;
            if (_handTimer < HandSeconds / Mathf.Max(0.1f, Citizen.SkillSpeed(WorkSkill.Cooking))) return;
            _handTimer = 0f;
            ZNetView view = s_view(station);
            if (view == null || !view.IsValid()) return;
            string item = DoneItem(station);
            if (_owed == 0) _drops.NewLook();   // nothing owed: a new look (a piece still owed is the cook's, review 5)
            if (!_drops.HasLookedAt(V(station.transform.position)))   // what lies there now isn't the cook's (the first time a look)
            {
                var lying = new List<KeyValuePair<string, Vec3>>();
                foreach (ItemDrop drop in WorldScan.Drops())
                    if (drop != null && Vector3.Distance(drop.transform.position, station.transform.position) <= CookDrops.Radius)
                        lying.Add(new KeyValuePair<string, Vec3>(IdOf(drop.gameObject), V(drop.transform.position)));
                _drops.TakingOffAt(V(station.transform.position), lying);
            }
            view.InvokeRPC("RPC_RemoveDoneItem", Citizen.transform.position, 1);   // on the station's computer: one piece, dropped by it
            Citizen.Practise(WorkSkill.Cooking);
            _owed++;
            _lastTakeOff = now;
            _slotsReadAt = now + SlotSyncSeconds;
            WorldScan.InvalidateDrops();
            Log.Info($"{Citizen.CitizenName} takes {item} off the cooking station into the Mead Hall's chest");
        }

        /// <summary>
        /// Picks up a cooked piece the hall's stations dropped, or the coal of one burnt while nobody cooked (not one lying
        /// there before); false when there's none.
        /// </summary>
        private bool Gather(IReadOnlyList<CookingStation> stations, float now)
        {
            if (_drop == null) _drop = FindDrop(stations, now);
            if (_drop == null) return false;
            Citizen.Status = "$vc_status_cook_taking_off";
            Vector3 pos = _drop.transform.position;
            bool there = Ai.GoTo(pos, 1.2f);
            if (!there && Ai.UsingDoor)
            {
                _stuck.Reset();
                return true;
            }
            float distance = Utils.DistanceXZ(Citizen.transform.position, pos);
            GatherStep step = CookRules.Gather(there, !there && _stuck.Update(distance, now), distance);   // one stuck check a tick (minor 12)
            if (step == GatherStep.Walk) return true;
            _stuck.Reset();
            if (step == GatherStep.GiveUp)
            {
                _blacklist.Add(IdOf(_drop.gameObject), now);
                _drop = null;
                return true;
            }
            ZNetView nview = _drop.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid())
            {
                _drop = null;
                return true;
            }
            if (!nview.IsOwner())
            {
                _drop.RequestOwn();   // again next tick, once it's ours
                return true;
            }
            _drop.Load();
            Citizen.Carry.Add(Utils.GetPrefabName(_drop.gameObject), _drop.m_itemData.m_stack);
            Citizen.SaveCarry();
            ZNetScene.instance.Destroy(_drop.gameObject);
            _drop = null;
            _owed = Mathf.Max(0, _owed - 1);
            WorldScan.InvalidateDrops();
            return true;
        }

        /// <summary>The nearest piece the cook is owed (<see cref="CookDrops.IsOwed"/>): cooked food, or a burnt piece's coal.</summary>
        private ItemDrop FindDrop(IReadOnlyList<CookingStation> stations, float now)
        {
            var wanted = new HashSet<string>(MeadHallModule.Cooked);
            foreach (CookingStation s in stations)
                if (s != null && s.m_overCookedItem != null) wanted.Add(s.m_overCookedItem.gameObject.name);
            ItemDrop best = null;
            float bestDistance = float.MaxValue;
            foreach (ItemDrop drop in WorldScan.Drops())
            {
                if (drop == null || drop.IsPiece() || !wanted.Contains(Utils.GetPrefabName(drop.gameObject))) continue;
                Vector3 pos = drop.transform.position;
                string id = IdOf(drop.gameObject);
                if (!_drops.IsOwed(id, V(pos)) || _blacklist.IsBlocked(id, now)) continue;
                float distance = Vector3.Distance(Citizen.transform.position, pos);
                if (distance >= bestDistance) continue;
                best = drop;
                bestDistance = distance;
            }
            return best;
        }

        /// <summary>
        /// Walks to the station and puts the raw food in hand on it, one call per piece, only as many as its free slots and
        /// only while its fire burns (Review Focus 4); what doesn't fit waits in hand for a slot.
        /// </summary>
        private void PutOn(CookingStation station, string raw, float now)
        {
            if (!Reach(station.transform.position, StationReach)) return;
            Ai.Idle();
            Ai.Face(station.transform.position);
            if (!Lit(station))
            {
                FireNeed need = NeedOf(station);
                if (need == FireNeed.Catching) return;   // just fed: the flames a moment later
                if (need != FireNeed.Fuel) Say("$vc_status_cook_fire_wont_burn", WontBurnLog);   // wood can't help (minor 8)
                else if (!PlanFuel(station)) Say("$vc_status_cook_hearth_out", "finds the hearth out and no wood in the chests");
                return;
            }
            if (_sentTo == station && !CookRules.MayAddMore(Filled(station), _filledExpected, now - _sentAt)) return;   // the last pieces not back yet
            _handTimer += Time.deltaTime;
            if (_handTimer < HandSeconds / Mathf.Max(0.1f, Citizen.SkillSpeed(WorkSkill.Cooking))) return;
            _handTimer = 0f;
            ZNetView view = s_view(station);
            if (view == null || !view.IsValid()) return;
            int n = Mathf.Min(FreeSlots(station), Citizen.Carry.Count(raw));
            if (n <= 0) return;
            _filledExpected = Filled(station) + n;
            _sentTo = station;
            _sentAt = now;
            for (int i = 0; i < n; i++) view.InvokeRPC("RPC_AddItem", raw, false);   // on the station's computer, as a player's
            Citizen.Carry.Remove(raw, n);
            Citizen.SaveCarry();
            if (MeadHallModule.Takes(station).TryGetValue(raw, out CookingStation.ItemConversion made))
            {
                _lately.Add(made.m_to.gameObject.name);
                if (_lately.Count > CookRules.VaryLast) _lately.RemoveAt(0);
            }
            Log.Info($"{Citizen.CitizenName} puts {n} {raw} on the cooking station");
        }

        /// <summary>Puts everything in hand in the hall's chest; full: the rest goes to another colony chest (as everyone's).</summary>
        private void Deliver(float dt)
        {
            ForgetTrip();
            if (!Reach(_hut.transform.position, ChestReach)) return;
            Ai.Idle();
            if (Time.time < _retryAt) return;
            _retryAt = Time.time + RetrySeconds;
            StorageModule.DepositResult result = _hut.Storage.TryDeposit(Citizen.Carry);
            if (result == StorageModule.DepositResult.Busy) return;   // a player has the chest open
            if (result == StorageModule.DepositResult.StorageFull) _fullChest.Choose(Citizen, _hut);
            Citizen.SaveCarry();
            _retryAt = 0f;
        }

        /// <summary>A trip planned: walks to the chest and takes what it can spare; false when the trip is over.</summary>
        private bool Fetch(float now)
        {
            if (_fetchChest == null || _fetchFor == null || _fetchWant <= 0)
            {
                ForgetTrip();
                return false;
            }
            if (CookRules.TripOver(now - _fetchSince))   // a chest out of reach: given up, and passed over for a while
            {
                _blacklist.Add("chest" + _fetchChest.GetInstanceID(), now);
                Log.Info($"{Citizen.CitizenName} can't get to the chest for {_fetchItem}; they leave it for now");
                ForgetTrip();
                _planFailedAt = now;
                return false;
            }
            Citizen.Status = "$vc_status_cook_fetching " + ItemToken(_fetchItem);   // not the last step's (minor 12)
            if (!Reach(_fetchChest.transform.position, ChestReach)) return true;
            Ai.Idle();
            int got = _fetchChest.TakeUpTo(_fetchItem, _fetchWant, Citizen.Carry, out bool busy);
            if (busy && now < _fetchSince + 30f) return true;   // a player has it open, or it's being handed over: a moment
            if (got > 0) Citizen.SaveCarry();
            ForgetTrip();
            return got > 0;
        }

        /// <summary>
        /// The next roast for a station whose fire burns: the plainest spare raw food it takes (<see cref="CookRules.NextRaw"/>),
        /// as much as its free slots and a load allow, from the first colony chest that can spare it. False when none.
        /// </summary>
        private bool PlanRaw(CookingStation station)
        {
            Dictionary<string, CookingStation.ItemConversion> takes = MeadHallModule.Takes(station);
            List<StorageModule> stores = Stores();
            var spare = new List<RawFood>();
            foreach (KeyValuePair<string, CookingStation.ItemConversion> t in takes)
            {
                int n = stores.Sum(s => s.Spare(t.Key));
                if (n <= 0) continue;
                ItemDrop cooked = t.Value.m_to;
                float value = cooked.m_itemData.m_shared.m_food + cooked.m_itemData.m_shared.m_foodStamina;
                spare.Add(new RawFood { Item = t.Key, Spare = n, Cooked = cooked.gameObject.name, CookedValue = value });
            }
            RawFood next = CookRules.NextRaw(spare, takes.Keys, _lately);
            if (next == null) return false;
            int load = CookRules.Load(ColonyConfig.CookCarryCapacity.Value, FreeSlots(station), next.Spare);
            StorageModule chest = stores.FirstOrDefault(s => s.Spare(next.Item) > 0);
            if (load <= 0 || chest == null) return false;
            StartTrip(next.Item, load, chest, station);
            Say("", $"fetches {load} {next.Item} for the cooking station");
            return true;
        }

        /// <summary>
        /// The station's fire is out: wood (its fuel) from the colony's spare for the hearth under it, or the station's own
        /// fuel (an oven). Fed once there (<see cref="FeedNow"/>). False when the colony can't spare any.
        /// </summary>
        private bool PlanFuel(CookingStation station)
        {
            string fuel = FuelOf(station, out float have, out float max);
            if (fuel.Length == 0) return false;
            List<StorageModule> stores = Stores();
            int inChests = stores.Sum(s => s.Spare(fuel));
            int want = FireFuel.ToFetch(have, max, inChests);
            if (Citizen.Carry.Count(fuel) > 0) return FeedNow(station, fuel);
            StorageModule chest = stores.FirstOrDefault(s => s.Spare(fuel) > 0);
            if (want <= 0 || chest == null) return false;
            StartTrip(fuel, want, chest, station);
            Say("", $"fetches {want} {fuel} for the hearth");
            return true;
        }

        /// <summary>At the station with its fuel in hand: puts it on the hearth's fire, or in the oven, through their own calls.</summary>
        private bool FeedNow(CookingStation station, string fuel)
        {
            Fireplace fire = HearthOf(station);
            Vector3 at = fire != null ? fire.transform.position : station.transform.position;
            if (!Reach(at, StationReach)) return true;
            Ai.Idle();
            Ai.Face(at);
            int n;
            if (fire != null)
            {
                ZNetView view = fire.GetComponent<ZNetView>();
                if (view == null || !view.IsValid()) return true;
                float have = view.GetZDO().GetFloat(ZDOVars.s_fuel, 0f);
                n = Mathf.Min(Citizen.Carry.Count(fuel), Mathf.FloorToInt(fire.m_maxFuel - have));
                if (n > 0) view.InvokeRPC("RPC_AddFuelAmount", (float)n);   // on the fire's computer, as a player's
            }
            else
            {
                ZNetView view = s_view(station);
                if (view == null || !view.IsValid()) return true;
                n = Mathf.Min(Citizen.Carry.Count(fuel), Mathf.Max(0, station.m_maxFuel - Mathf.CeilToInt(s_fuel != null ? s_fuel(station) : 0f)));
                for (int i = 0; i < n; i++) view.InvokeRPC("RPC_AddFuel");
            }
            if (n > 0)
            {
                Citizen.Carry.Remove(fuel, n);
                Citizen.SaveCarry();
                _fedAt = Time.time;
                Log.Info($"{Citizen.CitizenName} puts {n} {fuel} on the Mead Hall's hearth");
            }
            return true;
        }

        // ---- what the cook looks at ----

        private bool CarriesHot()
        {
            ICollection<string> cooked = MeadHallModule.Cooked;
            return Citizen.Carry.ItemNames.Any(i => cooked.Contains(i) && Citizen.Carry.Count(i) > 0);
        }

        /// <summary>Raw food in hand that one of the hall's stations takes, with a free slot: that station, the trip's first.</summary>
        private bool CarriedRaw(IReadOnlyList<CookingStation> stations, out CookingStation station, out string raw)
        {
            station = null;
            raw = "";
            foreach (string item in Citizen.Carry.ItemNames.Where(i => Citizen.Carry.Count(i) > 0))
            {
                IEnumerable<CookingStation> order = stations.Where(s => CookRules.PutOnRank(NeedOf(s)) >= 0).OrderBy(s => CookRules.PutOnRank(NeedOf(s)));   // never one wood can't help (the minors' review 1)
                foreach (CookingStation s in order)
                {
                    if (s == null || !MeadHallModule.Takes(s).ContainsKey(item)) continue;
                    bool waiting = _sentTo == s && !CookRules.MayAddMore(Filled(s), _filledExpected, Time.time - _sentAt);
                    if (FreeSlots(s) <= 0 && !waiting) continue;
                    station = s;
                    raw = item;
                    return true;
                }
            }
            return false;
        }

        /// <summary>The fuel in hand for a station whose fire is out: that station and the fuel.</summary>
        private bool CarriedFuel(IReadOnlyList<CookingStation> stations, out CookingStation station, out string fuel)
        {
            station = null;
            fuel = "";
            if (_fetchItem.Length > 0) return false;   // still fetching it
            foreach (CookingStation s in stations)
            {
                string item = FuelOf(s, out _, out _);
                if (item.Length == 0 || Citizen.Carry.Count(item) <= 0 || NeedOf(s) != FireNeed.Fuel) continue;   // wood can't help: to the chest (minor 8)
                station = s;
                fuel = item;
                return true;
            }
            return false;
        }

        /// <summary>Hot food the hall's own chest can spare (what the settlers' breakfast comes from).</summary>
        private int HotInHall()
        {
            ICollection<string> cooked = MeadHallModule.Cooked;
            return HotFood.Count(_hut.Storage.SpareContents(), cooked, StorageModule.IsFood);   // what it keeps isn't the settlers' (review 10)
        }

        /// <summary>The colony's settlers: its camp's roster, or those loaded here when the camp isn't.</summary>
        private int Settlers()
        {
            string colony = _hut.ColonyId;
            TownHallModule hall = ColonyBuilding.FindTownHall(colony)?.GetComponent<TownHallModule>();
            if (hall != null) return hall.Roster.Entries.Count;
            return Citizen.All.Count(c => c != null && c.Record.ColonyId == colony);
        }

        private List<StorageModule> Stores() =>
            BuilderTools.ColonyStores(_hut.ColonyId, _hut, _hut.transform.position).Where(s => !_blacklist.IsBlocked("chest" + s.GetInstanceID(), Time.time)).ToList();

        private static bool HasDone(CookingStation station) => station != null && s_haveDone != null && s_haveDone(station);

        /// <summary>Its fire burns (a hearth under it with fuel, <see cref="CookRules.FireBurns"/>), or it has fuel of its own (an oven), or it needs neither.</summary>
        internal static bool Lit(CookingStation station)
        {
            if (station == null) return false;
            if (station.m_requireFire)
            {
                Fireplace fire = HearthOf(station);
                ZNetView view = fire != null ? fire.GetComponent<ZNetView>() : null;
                bool fuel = fire == null || fire.m_infiniteFuel || view != null && view.IsValid() && view.GetZDO().GetFloat(ZDOVars.s_fuel, 0f) > 0f;
                return CookRules.FireBurns(s_fireLit != null && s_fireLit(station), fire != null, fuel);
            }
            if (station.m_useFuel) return s_fuel != null && s_fuel(station) > 0f;
            return true;
        }

        /// <summary>What the station's fire needs (<see cref="CookRules.Need"/>): fuel only when it has none; a moment to catch just after feeding.</summary>
        private FireNeed NeedOf(CookingStation station)
        {
            if (Lit(station)) return FireNeed.Burns;
            string fuel = FuelOf(station, out float have, out _);
            return CookRules.Need(false, fuel.Length > 0, have, Time.time - _fedAt);
        }

        internal static int Filled(CookingStation station) => station == null || station.m_slots == null ? 0 : station.m_slots.Length - FreeSlots(station);

        /// <summary>Pieces on the station not done yet.</summary>
        internal static int Uncooked(CookingStation station)
        {
            if (station == null || s_getSlot == null || station.m_slots == null) return 0;
            int n = 0;
            for (int i = 0; i < station.m_slots.Length; i++)
            {
                object[] args = { i, null, null, null, null };
                s_getSlot.Invoke(station, args);
                if (!string.IsNullOrEmpty(args[1] as string) && args[3] != null && Convert.ToInt32(args[3]) == 0) n++;   // Status.NotDone
            }
            return n;
        }

        internal static int FreeSlots(CookingStation station)
        {
            if (station == null || s_getSlot == null || station.m_slots == null) return 0;
            int free = 0;
            for (int i = 0; i < station.m_slots.Length; i++)
            {
                object[] args = { i, null, null, null, null };
                s_getSlot.Invoke(station, args);
                if (string.IsNullOrEmpty(args[1] as string)) free++;
            }
            return free;
        }

        private static string DoneItem(CookingStation station)
        {
            if (station == null || s_getSlot == null || station.m_slots == null) return "";
            for (int i = 0; i < station.m_slots.Length; i++)
            {
                object[] args = { i, null, null, null, null };
                s_getSlot.Invoke(station, args);
                if (args[3] != null && Convert.ToInt32(args[3]) == 1) return args[1] as string ?? "";   // Status.Done
            }
            return "";
        }

        /// <summary>What the station's fire burns: the hearth's fuel under it, or its own (an oven); "" when neither needs any.</summary>
        private static string FuelOf(CookingStation station, out float have, out float max)
        {
            have = max = 0f;
            if (station == null) return "";
            if (station.m_requireFire)
            {
                Fireplace fire = HearthOf(station);
                ZNetView view = fire != null ? fire.GetComponent<ZNetView>() : null;
                if (fire == null || fire.m_infiniteFuel || fire.m_fuelItem == null || view == null || !view.IsValid()) return "";
                have = view.GetZDO().GetFloat(ZDOVars.s_fuel, 0f);
                max = fire.m_maxFuel;
                return Utils.GetPrefabName(fire.m_fuelItem.gameObject);
            }
            if (station.m_useFuel && station.m_fuelItem != null)
            {
                have = s_fuel != null ? s_fuel(station) : 0f;
                max = station.m_maxFuel;
                return Utils.GetPrefabName(station.m_fuelItem.gameObject);
            }
            return "";
        }

        private static readonly Dictionary<CookingStation, KeyValuePair<float, Fireplace>> s_hearths = new Dictionary<CookingStation, KeyValuePair<float, Fireplace>>();
        private static readonly Collider[] s_hits = new Collider[64];
        private static int s_mask;

        /// <summary>The fire under a cooking station: the nearest within 3 m; looked for again every few seconds (review 2).</summary>
        private static Fireplace HearthOf(CookingStation station)
        {
            if (s_hearths.TryGetValue(station, out KeyValuePair<float, Fireplace> known) && Time.time < known.Key && known.Value != null) return known.Value;
            if (s_hearths.Count > 32) foreach (CookingStation gone in s_hearths.Keys.Where(s => s == null).ToList()) s_hearths.Remove(gone);
            Fireplace found = FindHearth(station);
            s_hearths[station] = new KeyValuePair<float, Fireplace>(Time.time + 5f, found);
            return found;
        }

        private static Fireplace FindHearth(CookingStation station)
        {
            Vector3 at = station.transform.position;
            Fireplace best = null;
            float bestDistance = 3f;
            if (s_mask == 0) s_mask = LayerMask.GetMask("piece", "piece_nonsolid", "Default_small");
            int count = Physics.OverlapSphereNonAlloc(at, 3f, s_hits, s_mask);
            for (int i = 0; i < count; i++)
            {
                Collider hit = s_hits[i];
                Fireplace fire = hit != null ? hit.GetComponentInParent<Fireplace>() : null;
                if (fire == null) continue;
                float distance = Vector3.Distance(fire.transform.position, at);
                if (distance >= bestDistance) continue;
                best = fire;
                bestDistance = distance;
            }
            return best;
        }

        private void StartTrip(string item, int want, StorageModule chest, CookingStation station)
        {
            _fetchSince = Time.time;
            _fetchItem = item;
            _fetchWant = want;
            _fetchChest = chest;
            _fetchFor = station;
        }

        private void ForgetTrip()
        {
            _fetchItem = "";
            _fetchWant = 0;
            _fetchChest = null;
            _fetchFor = null;
        }

        private void Rest()
        {
            if (Ai.GoTo(_hut.transform.position, 4f)) Ai.Idle();
        }

        /// <summary>Walks there; one that can't be reached fully is worked from close by (as the forager's chest).</summary>
        private bool Reach(Vector3 at, float reach)
        {
            if (Ai.GoTo(at, reach))
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
            return MoveRules.DeliverFromHere(_stuck.Update(distance, Time.time), distance);
        }

        /// <summary>A status (when given) and its log line once per change.</summary>
        private void Say(string status, string log)
        {
            if (status.Length > 0) Citizen.Status = status;
            if (log == _lastLog) return;
            _lastLog = log;
            Log.Info($"{Citizen.CitizenName} {log}");
        }

        private static string ItemToken(string prefab)
        {
            ItemDrop drop = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(prefab)?.GetComponent<ItemDrop>() : null;
            return drop != null ? drop.m_itemData.m_shared.m_name : prefab;
        }

        private static T Delegate<T>(string method) where T : Delegate
        {
            System.Reflection.MethodInfo info = AccessTools.Method(typeof(CookingStation), method);
            return info != null ? AccessTools.MethodDelegate<T>(info) : null;
        }

        private static string IdOf(GameObject go)
        {
            ZNetView nview = go != null ? go.GetComponent<ZNetView>() : null;
            if (nview != null && nview.IsValid()) return nview.GetZDO().m_uid.ToString();
            return go != null ? go.GetInstanceID().ToString() : "";
        }
    }
}
