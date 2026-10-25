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
    /// Keep in stock carried in free time (worker polish part 2, 0.21; MineColonies' couriers, done the way fires are fed):
    /// a settler with nothing to do takes what a building keeps and lacks from a chest that can spare it, anywhere in the
    /// colony, and puts it in that building's chest (<see cref="StockRuns"/>). One settler per building and item.
    /// </summary>
    internal sealed partial class FreeTimeRoutine
    {
        private StockRun _run;
        private string _runKey = "";
        private bool _stockTaken;
        /// <summary>What he carries for the run: saved with his carry (review of 0.21: lost on unload, takeover or death).</summary>
        private CarryLoad _stock => _citizen.InHand;
        private readonly TargetBlacklist _skippedRuns = new TargetBlacklist(SkipSeatSeconds);

        /// <summary>Self-test: items brought to buildings that keep them, by every settler on this computer.</summary>
        internal static int StockCarried { get; private set; }

        /// <summary>The run to make now: the nearest building short of a kept item, from the chest that makes the shortest walk.</summary>
        private StockRun StockToCarry(float now)
        {
            if (!ColonyConfig.CarryToStock.Value) return null;
            string colony = _citizen.ColonyId;
            if (!CourierRules.FreeTimeCarries(HasCourier(colony))) return null;   // the storehouse's couriers carry it (1.5)
            var needs = new List<StockNeed>();
            var sources = new List<StockSource>();
            var chests = new Dictionary<string, StorageModule>();
            foreach (ColonyBuilding b in ColonyBuilding.All)
            {
                if (b == null || !b.IsValid || b.ColonyId != colony || b.Storage == null || b.Stock == null || b.Id.Length == 0) continue;
                chests[b.Id] = b.Storage;
                GroundPoint at = Ground(b.transform.position);
                Dictionary<string, int> contents = b.Storage.Contents();
                StockList keep = b.Stock.List;
                IReadOnlyDictionary<string, int> working = b.Storage.Working;   // once a building, not once an item (its review)
                foreach (KeepEntry missing in StockRules.Short(contents, keep, StorageModule.IsFood))
                    if ((missing.Item == StockList.AnyFood || StorageModule.Carried(missing.Item)) && !_skippedRuns.IsBlocked(StockRuns.ClaimKey(b.Id, missing.Item), now))
                        needs.Add(new StockNeed { BuildingId = b.Id, At = at, Item = missing.Item, Missing = missing.Amount });
                // What it spares: its own chest beyond what it keeps, and a storehouse's other chests whole (1.5: its Keep
                // in stock counts in its own chest, as every hut's).
                var spares = new Dictionary<string, int>();
                foreach (KeyValuePair<string, int> item in contents)
                {
                    int spare = StorageModule.Carried(item.Key) ? StockRules.Spare(contents, keep, item.Key, StorageModule.IsFood, false, working) : 0;
                    if (spare > 0) spares[item.Key] = spare;
                }
                foreach (StorageModule store in b.Stores)
                {
                    if (store == b.Storage) continue;
                    foreach (KeyValuePair<string, int> item in store.Contents())
                        if (StorageModule.Carried(item.Key)) spares[item.Key] = (spares.TryGetValue(item.Key, out int n) ? n : 0) + item.Value;
                }
                foreach (KeyValuePair<string, int> spare in spares)
                    sources.Add(new StockSource { BuildingId = b.Id, At = at, Item = spare.Key, Spare = spare.Value });
            }
            if (needs.Count == 0) return null;
            // For food of any kind, the plainest first (as newcomers are fed).
            sources = sources.OrderBy(s => StorageModule.IsFood(s.Item) ? FoodItems.Value(s.Item) : 0f).ToList();
            Vector3 me = _citizen.transform.position;
            // Only to a chest with room for it (review of 0.21: a full Town Hall got nothing, run after run).
            return StockRuns.Choose(needs, sources, Ground(me), key => FreeTimeClaims.TakenByOther(key, me, _citizen.Id), StorageModule.IsFood,
                (building, item) => chests.TryGetValue(building, out StorageModule chest) && chest.CanTake(item));
        }

        /// <summary>The colony's storehouse has a courier hired (1.5): then free time is for resting, chatting and the fire.</summary>
        internal static bool HasCourier(string colony)
        {
            if (string.IsNullOrEmpty(colony)) return false;
            foreach (ColonyBuilding b in ColonyBuilding.All)
            {
                if (b == null || !b.IsValid || !b.IsStorehouse || b.ColonyId != colony) continue;
                WorkerModule worker = b.GetComponent<WorkerModule>();
                if (worker != null && worker.Workers(out List<Citizens.Citizen> loaded, out List<RosterEntry> elsewhere) && loaded.Count + elsewhere.Count > 0)
                    return true;
            }
            return false;
        }

        private void StartCarrying(StockRun run)
        {
            _run = run;
            _runKey = StockRuns.ClaimKey(run.To, run.Need);
            _stockTaken = false;
            FreeTimeClaims.TryClaim(_runKey, _citizen.transform.position, _citizen.Id);
            _chooseAt = Time.time + 3f * ReachSeconds;   // the walk to the chest and the building (TooLongOnTheWay guards each)
            SaveCarrying(run.Amount);
            Log.Info($"{_citizen.CitizenName} goes to carry {run.Amount} {run.Item} from {run.From} to {run.To} (it keeps {run.Need})");
        }

        private void TickCarry(float now)
        {
            if (_run == null)
            {
                _chooseAt = now;
                return;
            }
            if (_arrived)
            {
                _ai.Idle();
                return;
            }
            FreeTimeClaims.TryClaim(_runKey, _citizen.transform.position, _citizen.Id);   // kept while he's at it
            if (!_stockTaken)
            {
                ColonyBuilding from = ColonyBuilding.FindById(_run.From);
                if (from == null || from.Storage == null)
                {
                    _chooseAt = now;
                    return;
                }
                if (!_ai.GoTo(from.transform.position, 2.5f)) return;
                _ai.Idle();
                _ai.Face(from.transform.position);
                // A storehouse: chest by chest (1.5). Counted again item by item: what's spare may have changed on the way, and
                // the kept amount stays.
                bool waiting = false;
                foreach (StorageModule store in from.Stores)
                {
                    if (_stock.Count(_run.Item) >= _run.Amount) break;
                    if (store.Spare(_run.Item) <= 0) continue;
                    if (store.Claim() != StorageModule.Access.Ready)
                    {
                        waiting = true;   // its owner is handing it over
                        continue;
                    }
                    while (_stock.Count(_run.Item) < _run.Amount && store.TryTakeItem(_run.Item, _stock)) { }
                }
                if (waiting && _stock.Count(_run.Item) == 0) return;   // he waits for it (TooLongOnTheWay)
                _citizen.SaveCarry();
                if (_stock.Count(_run.Item) == 0)
                {
                    _skippedRuns.Add(_runKey, now);   // nothing spare after all: that need waits a little
                    _chooseAt = now;
                    return;
                }
                _stockTaken = true;
                _startedAt = now;   // a fresh half minute to get to the building
                SaveCarrying(_stock.Count(_run.Item));
                return;
            }
            ColonyBuilding to = ColonyBuilding.FindById(_run.To);
            if (to == null || to.Storage == null)
            {
                _chooseAt = now;   // gone: what he holds goes with his carry
                return;
            }
            if (!_ai.GoTo(to.transform.position, 2.5f)) return;
            _ai.Idle();
            _ai.Face(to.transform.position);
            int had = _stock.Count(_run.Item);
            if (to.Storage.TryDeposit(_stock) == StorageModule.DepositResult.Busy) return;   // waits (TooLongOnTheWay)
            int brought = had - _stock.Count(_run.Item);
            _citizen.SaveCarry();
            StockCarried += brought;
            if (brought == 0) _skippedRuns.Add(_runKey, now);   // no room after all: that need waits a little (review of 0.21)
            Log.Info($"{_citizen.CitizenName} brought {brought} {_run.Item} to {_run.To} (it keeps {_run.Need}).");
            _arrived = true;
            _arrivedAt = now;
            _doUntil = now + 3f;
            _chooseAt = now + 3f;
        }

        /// <summary>Stopping: the claim goes, and what's still in hand (it didn't fit, or he was called away) goes with his carry.</summary>
        private void StopCarrying()
        {
            if (_runKey.Length > 0) FreeTimeClaims.Release(_runKey, _citizen.Id);
            _runKey = "";
            _run = null;
            ReturnInHand();
        }

        /// <summary>What's still in his hands (it didn't fit, or he was called away) goes with his carry, for his job or the Town Hall.</summary>
        private void ReturnInHand()
        {
            if (_citizen.InHand.Total == 0) return;
            foreach (string item in _citizen.InHand.ItemNames) _citizen.Carry.Add(item, _citizen.InHand.Count(item));
            _citizen.InHand.Clear();
            _citizen.SaveCarry();
        }

        /// <summary>"carrying 10 Raspberries to the Town Hall" on his hover.</summary>
        private void SaveCarrying(int amount)
        {
            ColonyBuilding to = ColonyBuilding.FindById(_run.To);
            string what = amount + " " + ItemToken(_run.Item);
            SaveDoing(ActivityWords.Token(Leisure.CarryStock), what, to != null && to.Type != null ? to.Type.NameToken : "");
        }

        private static string ItemToken(string item)
        {
            ItemDrop drop = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(item)?.GetComponent<ItemDrop>() : null;
            return drop != null ? drop.m_itemData.m_shared.m_name : item;
        }
    }
}
