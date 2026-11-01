using System;
using System.Linq;
using UnityEngine;
using VikingColonies.Citizens;
using VikingColonies.Config;
using VikingColonies.Core;

namespace VikingColonies.Buildings
{
    /// <summary>
    /// The colony's fallen (1.5.1, the memorial stone): every settler it has lost, on the camp, with no limit. Written by the
    /// camp's owner, who also brings a settler back when the fallen return is on (a server's choice, off by default).
    /// </summary>
    public partial class TownHallModule
    {
        private const string RpcFallen = "vc_Fallen";   // one fallen line, checked by the owner (FallenChecks)

        private FallenList _fallen;
        private uint _fallenRevision;

        /// <summary>The colony's fallen as saved (read again only when the camp's saved data has changed, as the Saga).</summary>
        public FallenList Fallen
        {
            get
            {
                if (_nview == null || !_nview.IsValid()) return new FallenList();
                ZDO zdo = _nview.GetZDO();
                if (_fallen == null || zdo.DataRevision != _fallenRevision)
                {
                    _fallen = FallenList.Decode(zdo.GetString(SaveKeys.Fallen, ""));
                    _fallenRevision = zdo.DataRevision;
                }
                return _fallen;
            }
        }

        /// <summary>Puts a death on the list: on the camp's owner (from anywhere, the request goes there).</summary>
        public void RecordFallen(FallenLine line)
        {
            if (_nview == null || !_nview.IsValid() || line == null) return;
            var one = new FallenList();
            if (!one.Add(line)) return;
            _nview.InvokeRPC(RpcFallen, one.Encode());   // runs on the owner: right here when that's us
        }

        private void RPC_Fallen(long sender, string text)
        {
            if (_nview == null || !_nview.IsValid() || !_nview.IsOwner() || _building == null) return;
            FallenLine sent = FallenList.Decode(text).Lines.FirstOrDefault();
            // Only a death this computer was told of (the death message reaches every computer first), with words of a sane
            // length; the record only when it is that settler's (its review: a modified game's huge words, or a living
            // settler's record to make a copy walk in).
            FallenLine line = FallenChecks.Checked(sent, _building.ColonyId, sent != null && ColonyDeaths.IsDead(sent.Id));
            if (line == null)
            {
                Log.Info($"Colony {_building.ColonyId}: a fallen line for '{sent?.Name}' wasn't taken (no death known by that id, or not as the game sends it)");
                return;
            }
            FallenList list = FallenOrSaga();
            if (!list.Add(line)) return;   // the same death told twice
            SaveFallen(list);
            Log.Event($"Colony {_building.ColonyId}: {line.Name} is remembered among the fallen (day {line.Day}" +
                      (line.Record.Length > 0 ? ", to return" : "") + ")");
        }

        /// <summary>The saved list, or, for a camp from before 1.5.1, the deaths its Saga still holds.</summary>
        private FallenList FallenOrSaga() =>
            FallenList.IsSaved(_nview.GetZDO().GetString(SaveKeys.Fallen, "")) ? Fallen : FallenList.FromSaga(Saga);

        private void SaveFallen(FallenList list)
        {
            _nview.GetZDO().Set(SaveKeys.Fallen, list.Encode());
            _fallen = null;   // read again
        }

        /// <summary>
        /// On the owner, each tick of a founded camp: the list saved once from an old Saga; the words of the colony's memorial
        /// stone taken in (a camp that took over a lost one gets its fallen back from the stone, 1.5.1); a return when one is due.
        /// </summary>
        private void CheckFallen()
        {
            bool saved = FallenList.IsSaved(_nview.GetZDO().GetString(SaveKeys.Fallen, ""));
            FallenList list = FallenOrSaga();
            bool changed = !saved;
            foreach (Colony.MemorialStone stone in Colony.MemorialStone.All)
                if (stone != null && stone.IsValid && stone.ColonyId == _building.ColonyId) changed |= list.MergeFrom(stone.Words);
            if (changed) SaveFallen(list);
            TryReturn();
        }

        /// <summary>
        /// The fallen return: in daylight, a settler whose days have passed walks back in from the colony's edge as a newcomer
        /// does, one a day while the colony has room, the earliest death first; as themselves, with a new id.
        /// </summary>
        private void TryReturn()
        {
            if (!ColonyConfig.FallenReturn.Value || EnvMan.instance == null || ZNet.instance == null || !EnvMan.IsDay() || !CitizenPrefab.Available) return;
            int today = EnvMan.instance.GetDay(ZNet.instance.GetTimeSeconds());
            ZDO zdo = _nview.GetZDO();
            ColonyNeeds needs = Needs();
            NewcomerRules rules = NewcomerRules;
            FallenList list = Fallen;
            FallenLine due = list.NextBack(today, ColonyConfig.FallenReturnDays.Value, true, zdo.GetInt(SaveKeys.FallenBackDay, -1),
                needs.Settlers, Math.Min(rules.MaxSettlers, rules.HallSettlers));
            if (due == null) return;
            CitizenRecord dead = CitizenRecord.Decode(due.Record);
            if (string.IsNullOrEmpty(dead.Name))
            {
                Log.Warn($"Colony {_building.ColonyId}: {due.Name}'s way back can't be read; they stay among the fallen");
                due.Record = "";
                SaveFallen(list);
                return;
            }
            Vector3 hallPos = transform.position;
            Vector3? edge = ColonyConfig.NewcomersWalkIn.Value ? EdgeSpot(founding: false) : null;
            Vector3 at = edge ?? hallPos + Quaternion.Euler(0f, ModRandom.Range("fallen-return", "", 0f, 360f), 0f) * Vector3.forward * 3f;
            Citizen citizen = CitizenPrefab.Spawn(at + Vector3.up * 0.5f, Quaternion.identity);
            if (citizen == null) return;   // no settler body: tried again next tick
            CitizenRecord back = FallenReturn.Restore(dead, Guid.NewGuid().ToString("N"), today);
            citizen.InitRestored(back, _building.ColonyId, hallPos);
            if (edge.HasValue) citizen.MarkArriving(true);
            list.MarkReturned(due.Key, today);
            zdo.Set(SaveKeys.FallenBackDay, today);
            SaveFallen(list);
            Record("fallenback", back.Name);
            Log.Event($"Colony {_building.ColonyId}: {back.Name} returned from beyond the veil (died on day {due.Day}, back on day {today})");
            _rosterDirty = true;
        }

        /// <summary>The camp destroyed, on its owner: a return still waiting is lost with it (its words live on the memorial stone).</summary>
        private void OnCampDestroyed()
        {
            if (_nview == null || !_nview.IsValid() || !_nview.IsOwner() || _building == null) return;
            foreach (FallenLine waiting in Fallen.Lines.Where(l => !l.Returned && l.Record.Length > 0))
                Log.Event($"Colony {_building.ColonyId}: the camp is gone, and {waiting.Name}'s return with it");
        }
    }
}
