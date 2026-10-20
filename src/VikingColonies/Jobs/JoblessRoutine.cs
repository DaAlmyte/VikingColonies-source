using UnityEngine;
using VikingColonies.AI;
using VikingColonies.Buildings;
using VikingColonies.Citizens;
using VikingColonies.Config;
using VikingColonies.Core;

namespace VikingColonies.Jobs
{
    /// <summary>What a citizen does without a (loaded) job: wait at the Town Hall, or stand still with no colony.</summary>
    internal sealed class JoblessRoutine
    {
        /// <summary>How close to the Town Hall's chest he puts his load in.</summary>
        private const float DropOffReach = 2.5f;
        /// <summary>A Town Hall he can't reach (or that is full) keeps his load with him for a while before he tries again.</summary>
        private const float DropOffPatience = 60f;

        private float _dropOffSince = -1f;
        private float _dropOffPausedUntil;
        private readonly FullChestRun _fullChest = new FullChestRun();   // the Town Hall's chest full (0.21)

        public void Tick(CitizenAI ai, Citizen citizen, bool headingToWork)
        {
            CitizenRecord r = citizen.Record;
            if (!r.HasColony)
            {
                citizen.Status = "$vc_status_no_colony";
                ai.Idle();
                return;
            }
            if (headingToWork && r.WorkplacePos != null)
            {
                // Employed, but the hut isn't loaded here: walk towards it.
                citizen.Status = "$vc_status_working";
                if (ai.GoTo(Citizen.ToVector3(r.WorkplacePos), 3f)) ai.Idle();
                return;
            }
            if (citizen.Carry.Total > 0 && DropOff(ai, citizen)) return;
            citizen.Status = "$vc_status_jobless";
            if (r.ColonyPos == null)
            {
                ai.Idle();
                return;
            }
            if (ColonyConfig.FreeTime.Value)
            {
                ai.FreeTime(Citizen.ToVector3(r.ColonyPos));   // free time round the Town Hall (0.14)
                return;
            }
            // His own spot round the Town Hall: a big colony's jobless don't crowd one point.
            GroundPoint spot = JoblessSpot.For(new GroundPoint(r.ColonyPos[0], r.ColonyPos[2]), citizen.Id);
            if (ai.GoTo(new Vector3(spot.X, r.ColonyPos[1], spot.Z), 1.5f)) ai.Idle();
        }

        /// <summary>
        /// A settler let go with a load (a lumberjack fired with wood in his arms) takes it to the Town Hall's chest. False
        /// when there's no Town Hall here, or he gave up on it for now (he keeps the load till he can).
        /// </summary>
        private bool DropOff(CitizenAI ai, Citizen citizen)
        {
            if (Time.time < _dropOffPausedUntil) return false;
            ColonyBuilding hall = ColonyBuilding.FindTownHall(citizen.Record.ColonyId);
            if (_fullChest.Tick(citizen, ai, hall, null, "$vc_status_delivering_hall_full")) return true;
            if (hall == null || !hall.IsValid || hall.Storage == null) return false;
            if (_dropOffSince < 0f) _dropOffSince = Time.time;
            citizen.Status = "$vc_status_delivering";
            if (Time.time - _dropOffSince > DropOffPatience)
            {
                Log.Info($"{citizen.CitizenName} couldn't put his load in the Town Hall's chest; he keeps it for now");
                Pause();
                return false;
            }
            if (!ai.GoTo(hall.transform.position, DropOffReach)) return true;
            ai.Idle();
            StorageModule.DepositResult result = hall.Storage.TryDeposit(citizen.Carry);
            if (result == StorageModule.DepositResult.Busy) return true;   // a player has it open
            citizen.SaveCarry();
            if (result == StorageModule.DepositResult.StorageFull && !_fullChest.Choose(citizen, hall)) Pause();   // every chest full
            else _dropOffSince = -1f;
            return true;
        }

        private void Pause()
        {
            _dropOffSince = -1f;
            _dropOffPausedUntil = Time.time + DropOffPatience * 5f;
        }
    }
}
