using System.Linq;
using UnityEngine;
using VikingColonies.Buildings;
using VikingColonies.Colony;
using VikingColonies.Config;
using VikingColonies.Core;

namespace VikingColonies.AI
{
    /// <summary>
    /// Keeping the colony's fires fed (settler life, 0.15): a settler in free time takes the fuel a fire below half needs
    /// from a colony chest (never the last <see cref="FireFuel.Keep"/>), carries it there and puts it in, as a player does
    /// (the game's own add-fuel call, run by the fire's owner). One settler per fire.
    /// </summary>
    internal sealed partial class FreeTimeRoutine
    {
        private FirePlace _tendFire;
        private string _fuelItem = "";
        private string _fuelChest = "";
        private int _fuelWant;
        private bool _fuelTaken;
        /// <summary>The fuel in his hands: saved with his carry (review of 0.21).</summary>
        private CarryLoad _fuel => _citizen.InHand;

        /// <summary>Self-test: fires he fed.</summary>
        internal int FiresFed { get; private set; }

        /// <summary>A fire in his area below half, whose fuel is in the colony's chests: what to fetch and from where.</summary>
        private FirePlace FireToTend(float now, out string item, out int want, out string chestId)
        {
            item = chestId = "";
            want = 0;
            if (!ColonyConfig.TendFires.Value) return null;
            string colony = _citizen.ColonyId;
            Vector3 me = _citizen.transform.position;
            foreach (FirePlace fire in ColonyPlaces.Fires(colony)
                         .Where(f => f.Valid && Utils.DistanceXZ(f.Position, _anchor) <= FreeTimeRules.FireReach && !_skippedFires.IsBlocked(f.Id, now)
                                     && !_busyFires.IsBlocked(f.Id, now) && !FreeTimeClaims.TakenByOther(f.Id, f.Position, _citizen.Id))
                         .OrderBy(f => Utils.DistanceXZ(f.Position, me)))
            {
                Fireplace fp = fire.Fire;
                ZNetView view = fp.GetComponent<ZNetView>();
                if (fp.m_infiniteFuel || fp.m_fuelItem == null || view == null || !view.IsValid()) continue;
                float fuel = view.GetZDO().GetFloat(ZDOVars.s_fuel, 0f);
                if (fuel >= fp.m_maxFuel / 2f) continue;
                string name = Utils.GetPrefabName(fp.m_fuelItem.gameObject);
                int inChests = 0;
                ColonyBuilding nearest = null;
                foreach (ColonyBuilding b in ColonyBuilding.All)
                {
                    if (b == null || !b.IsValid || b.ColonyId != colony || b.Storage == null) continue;
                    int n = b.Storage.Spare(name);   // what a building keeps is left (0.21)
                    if (n <= 0) continue;
                    inChests += n;
                    if (nearest == null || Utils.DistanceXZ(b.transform.position, me) < Utils.DistanceXZ(nearest.transform.position, me)) nearest = b;
                }
                int fetch = FireFuel.ToFetch(fuel, fp.m_maxFuel, inChests);
                if (fetch <= 0 || nearest == null) continue;
                item = name;
                want = fetch;
                chestId = nearest.Id;
                return fire;
            }
            return null;
        }

        private readonly TargetBlacklist _busyFires = new TargetBlacklist(20);

        /// <summary>The colony's chests can spare this many of an item (beyond what their buildings keep).</summary>
        private int InChests(string item)
        {
            int total = 0;
            foreach (ColonyBuilding b in ColonyBuilding.All)
                if (b != null && b.IsValid && b.ColonyId == _citizen.ColonyId && b.Storage != null)
                    total += b.Storage.Spare(item);
            return total;
        }

        private void StartTending(FirePlace fire, string item, int want, string chestId)
        {
            _tendFire = fire;
            _fuelItem = item;
            _fuelWant = want;
            _fuelChest = chestId;
            _fuelTaken = false;
            FreeTimeClaims.TryClaim(fire.Id, fire.Position, _citizen.Id);
            _chooseAt = Time.time + 3f * ReachSeconds;   // the walk to the chest and the fire (TooLongOnTheWay guards each)
            SaveDoing(ActivityWords.Token(Leisure.TendFire), "");
            Log.Info($"{_citizen.CitizenName} goes to feed a fire: {want} {item} from {chestId}");
        }

        private void TickTend(float now)
        {
            if (_tendFire == null || !_tendFire.Valid)
            {
                _chooseAt = now;
                return;
            }
            if (_arrived)
            {
                _ai.Idle();   // fed: a moment by the fire, then something else (review of 0.15: he stayed there all day)
                return;
            }
            FreeTimeClaims.TryClaim(_tendFire.Id, _tendFire.Position, _citizen.Id);   // kept while he's at it
            if (!_fuelTaken)
            {
                ColonyBuilding chest = ColonyBuilding.FindById(_fuelChest);
                if (chest == null || chest.Storage == null)
                {
                    _chooseAt = now;
                    return;
                }
                if (!_ai.GoTo(chest.transform.position, 2.5f)) return;
                _ai.Idle();
                // Counted again here, item by item: another settler may have taken some since (the 10 stay, review of 0.15).
                int left = InChests(_fuelItem);
                while (_fuel.Count(_fuelItem) < _fuelWant && left > FireFuel.Keep && chest.Storage.TryTakeItem(_fuelItem, _fuel)) left--;
                _citizen.SaveCarry();
                if (_fuel.Count(_fuelItem) == 0)
                {
                    _busyFires.Add(_tendFire.Id, now);   // the chest is busy or the fuel is gone: this fire waits a little
                    _chooseAt = now;
                    return;
                }
                _fuelTaken = true;
                _startedAt = now;   // a fresh half minute to get to the fire
                return;
            }
            Vector3 at = _tendFire.Position;
            if (!_ai.GoTo(at, 2f)) return;
            _ai.Idle();
            _ai.Face(at);
            ZNetView view = _tendFire.Fire.GetComponent<ZNetView>();
            if (view != null && view.IsValid())
            {
                // Only what it still holds (someone else may have fed it meanwhile); the rest goes back with his carry.
                float fuel = view.GetZDO().GetFloat(ZDOVars.s_fuel, 0f);
                int n = Mathf.Min(_fuel.Count(_fuelItem), Mathf.FloorToInt(_tendFire.Fire.m_maxFuel - fuel));
                if (n > 0)
                {
                    view.InvokeRPC("RPC_AddFuelAmount", (float)n);   // runs on the fire's owner, as a player's does
                    _fuel.Remove(_fuelItem, n);
                    _citizen.SaveCarry();
                    FiresFed++;
                    Log.Info($"{_citizen.CitizenName} put {n} {_fuelItem} in the fire at ({at.x:0}, {at.z:0}).");
                }
            }
            _arrived = true;
            _arrivedAt = now;
            _doUntil = now + 3f;
            _chooseAt = now + 3f;
        }

        /// <summary>Leaving the fire: its claim goes, and fuel still in hand goes to a chest with what he carries.</summary>
        private void StopTending()
        {
            if (_tendFire != null) FreeTimeClaims.Release(_tendFire.Id, _citizen.Id);
            _tendFire = null;
            ReturnInHand();
        }
    }
}
