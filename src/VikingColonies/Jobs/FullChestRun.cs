using System.Collections.Generic;
using UnityEngine;
using VikingColonies.AI;
using VikingColonies.Buildings;
using VikingColonies.Citizens;
using VikingColonies.Core;

namespace VikingColonies.Jobs
{
    /// <summary>
    /// A worker whose own chest is full takes his load to the Town Hall, or the nearest colony chest with room (worker
    /// polish part 2, 0.21; before, he stood by his hut saying "Storage full" till the player emptied it). The lumberjack,
    /// the forager and the farmer use it for their hut, a settler without a job for the Town Hall (<see cref="FullChest"/>).
    /// </summary>
    internal sealed class FullChestRun
    {
        private const float Reach = 2.5f;
        /// <summary>Not there by then (in the way, or a player keeps it open): that chest is passed over for a while.</summary>
        private const float PatienceSeconds = 60f;
        private const double SkipSeconds = 300;

        private readonly StuckDetector _stuck = new StuckDetector();
        private readonly TargetBlacklist _skipped = new TargetBlacklist(SkipSeconds);
        private string _to = "";
        private float _since;

        /// <summary>He is taking his load to another chest.</summary>
        public bool Active => _to.Length > 0;

        /// <summary>
        /// Where his load goes, the chest of <paramref name="full"/> being full: false when every other one is full too.
        /// <paramref name="keep"/> stays in his hands, so it needs no room (review of 0.21: a lumberjack's seeds found
        /// "room" in a partial stack of them, and he went back and forth to a chest that took nothing).
        /// </summary>
        public bool Choose(Citizen citizen, ColonyBuilding full, IReadOnlyDictionary<string, int> keep = null)
        {
            float now = Time.time;
            CarryLoad load = citizen.Carry.Beyond(keep);
            var chests = new List<ChestRoom>();
            foreach (ColonyBuilding b in ColonyBuilding.All)
            {
                if (b == null || !b.IsValid || b == full || b.ColonyId != citizen.ColonyId || b.Storage == null || b.Id.Length == 0
                    || _skipped.IsBlocked(b.Id, now)) continue;
                Vector3 at = b.transform.position;
                chests.Add(new ChestRoom { BuildingId = b.Id, At = new GroundPoint(at.x, at.z), IsTownHall = b.IsTownHall, HasRoom = b.Storage.HasRoomFor(load) });
            }
            Vector3 me = citizen.transform.position;
            _to = FullChest.Choose(chests, new GroundPoint(me.x, me.z)) ?? "";
            _since = now;
            _stuck.Reset();
            if (Active) Log.Info($"{citizen.CitizenName}'s chest is full: they take their load to {_to}");
            return Active;
        }

        public void Forget() => _to = "";

        /// <summary>
        /// On his way there and putting in what fits (<paramref name="keep"/> stays in hand, as at his hut). True while at
        /// it; false once his load is in, or that chest is gone or out of reach: then his job goes on.
        /// </summary>
        /// <param name="status">What he says on the way (a settler without a job: the Town Hall's chest is full, 0.24).</param>
        public bool Tick(Citizen citizen, CitizenAI ai, ColonyBuilding full, IReadOnlyDictionary<string, int> keep = null,
            string status = "$vc_status_delivering_full")
        {
            if (!Active) return false;
            ColonyBuilding to = ColonyBuilding.FindById(_to);
            if (citizen.Carry.Beyond(keep).Total == 0 || to == null || !to.IsValid || to.Storage == null)
            {
                _to = "";
                return false;
            }
            citizen.Status = status;
            float now = Time.time;
            if (now - _since > PatienceSeconds)
            {
                Log.Info($"{citizen.CitizenName} couldn't put their load in {_to}'s chest within {PatienceSeconds:0} s; they try elsewhere later");
                _skipped.Add(_to, now);
                _to = "";
                return false;
            }
            if (!Reached(citizen, ai, to.transform.position)) return true;
            ai.Idle();
            ai.Face(to.transform.position);
            int before = citizen.Carry.Total;
            StorageModule.DepositResult result = to.Storage.TryDeposit(citizen.Carry, keep);
            if (result == StorageModule.DepositResult.Busy) return true;   // its owner is handing it over, or a player has it open
            citizen.SaveCarry();
            int moved = before - citizen.Carry.Total;
            Log.Info($"{citizen.CitizenName} put {moved} of their load in {_to}'s chest (their own is full)");
            if (moved == 0) _skipped.Add(_to, now);   // it took nothing after all: not that one again for a while
            _to = "";
            if (result == StorageModule.DepositResult.StorageFull) Choose(citizen, full, keep);   // that one filled up too: the next
            return Active;
        }

        /// <summary>There, or as close as he gets (behind a door, upstairs: from where the walking stopped, as at his hut).</summary>
        private bool Reached(Citizen citizen, CitizenAI ai, Vector3 at)
        {
            if (ai.GoTo(at, Reach))
            {
                _stuck.Reset();
                return true;
            }
            if (ai.UsingDoor)
            {
                _stuck.Reset();
                return false;
            }
            float distance = Utils.DistanceXZ(citizen.transform.position, at);
            return MoveRules.DeliverFromHere(_stuck.Update(distance, Time.time), distance);
        }
    }
}
