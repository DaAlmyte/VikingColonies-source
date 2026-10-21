using System.Collections.Generic;
using UnityEngine;
using VikingColonies.Buildings;
using VikingColonies.Config;
using VikingColonies.Core;
using static VikingColonies.Construction.CoreVectors;

namespace VikingColonies.Construction
{
    /// <summary>
    /// Repair (0.13): a builder with no order to work on mends his colony's damaged pieces, the nearest first, with a
    /// swing of his hammer as a player repairs (free of materials). A new order takes him away at the next piece.
    /// </summary>
    public partial class BuilderJob
    {
        private readonly Cooldown _mendLook = new Cooldown(RepairRules.LookSeconds);
        private readonly TargetBlacklist _badMends = new TargetBlacklist(300);
        private WearNTear _mend;
        private string _mendHut = "";
        private Vector3 _mendSpot;   // where he stands to mend it: on the ground just outside its building

        /// <summary>True while he mends a piece (or walks to it); false when there is nothing to mend.</summary>
        private bool Mend(float dt)
        {
            if (_mend == null || !RepairRules.NeedsMending(_mend.GetHealthPercentage()))
            {
                _mend = null;
                if (!_mendLook.Ready(Time.time)) return false;
                _mendLook.Start(Time.time);
                _mend = FindDamaged(out _mendHut, out ColonyBuilding hut);
                if (_mend == null) return false;
                _mendSpot = MendSpot(_mend.transform.position, hut);
                _walkStuck.Reset();
                Log.Info($"{Citizen.CitizenName} mends the {Utils.GetPrefabName(_mend.gameObject)} at {_mend.transform.position} " +
                         $"({_mend.GetHealthPercentage() * 100f:0}% whole)");
            }
            PutToolAway();   // the hammer does this
            Citizen.Status = "$vc_status_mending " + _mendHut;
            Vector3 at = _mend.transform.position;
            Vector3 me = Citizen.transform.position;
            float work = BuildStance.WorkDistance(ColonyConfig.BuilderReach.Value);
            float away = Vector3.Distance(me + Vector3.up, at);
            if (away > work)
            {
                if (Citizen.Body.InAttack()) return true;   // let the swing finish before walking
                // To a spot outside the building, not the piece itself: a wall piece always has a wall "between" him and
                // it, so he never arrived (self-test run 29 of 1.0 prep).
                bool there = Ai.GoTo(_mendSpot, MoveRules.MinStopDistance);
                if (Ai.UsingDoor) _walkStuck.Reset();   // going round to a door is no lack of progress (review of 0.13)
                bool stuck = !there && _walkStuck.Update(Utils.DistanceXZ(me, _mendSpot), Time.time);
                // Stuck on the way but within his reach: he mends it from where he stands, as he builds (the test next to
                // other mods, 2026-10-15: on a hillside full of bushes he gave up a door 9 m away).
                if (stuck && away <= ColonyConfig.BuilderReach.Value)
                {
                    there = true;
                    stuck = false;
                    _walkStuck.Reset();
                }
                // Under it and still too far (high up), or no way there: he leaves it for a while, so it never holds him.
                bool beyond = there && away > ColonyConfig.BuilderReach.Value;
                if (beyond || stuck)
                {
                    Log.Info($"{Citizen.CitizenName} can't get near the {Utils.GetPrefabName(_mend.gameObject)} at {at}; they leave it for now");
                    _badMends.Add(IdOf(_mend), Time.time);
                    _mend = null;
                    _walkStuck.Reset();
                    return false;
                }
                if (!there) return true;
            }
            _walkStuck.Reset();
            WearNTear target = _mend;
            SwingWhenReady(at, ColonyConfig.SecondsPerPiece.Value / Citizen.SkillSpeed(WorkSkill.Crafting), dt, () =>
            {
                if (MendNow(target)) Citizen.Practise(WorkSkill.Crafting);   // only for a mend that happened (0.26)
            });
            return true;
        }

        private bool MendNow(WearNTear target)
        {
            if (target == null || !target.Repair()) return false;
            Log.Info($"{Citizen.CitizenName} mended the {Utils.GetPrefabName(target.gameObject)} at {target.transform.position}");
            if (_mend == target) _mend = null;
            return true;
        }

        /// <summary>
        /// Where he stands to mend a piece: of the spots round its building (as when he builds it), the one nearest him
        /// from which the piece is within his work distance, else his reach; beside the piece when the building has no plan.
        /// </summary>
        private Vector3 MendSpot(Vector3 piece, ColonyBuilding hut)
        {
            Footprint around = hut != null && hut.GetComponent<ConstructionModule>() is ConstructionModule site ? site.SiteFootprint : null;
            if (around != null)
            {
                var spots = new List<Vec3>();
                foreach (Vec3 spot in StandSpots.Ring(around, RingOutside, RingSpacing))
                    spots.Add(new Vec3(spot.X, ZoneSystem.instance.GetGroundHeight(U(spot)), spot.Z));
                Vec3 me = V(Citizen.transform.position);
                int pick = StandSpots.Choose(spots, V(piece), BuildStance.WorkDistance(ColonyConfig.BuilderReach.Value), me, i => true);
                if (pick < 0) pick = StandSpots.Choose(spots, V(piece), ColonyConfig.BuilderReach.Value, me, i => true);
                if (pick >= 0) return U(spots[pick]);
            }
            Vector3 beside = piece;
            if (hut != null)
            {
                Vector3 outward = piece - hut.transform.position;
                outward.y = 0f;
                beside += outward.sqrMagnitude > 0.01f ? outward.normalized * 2f : Vector3.forward * 2f;
            }
            beside.y = ZoneSystem.instance.GetGroundHeight(beside);
            return beside;
        }

        /// <summary>The nearest damaged piece the colony's builders put up for its buildings; null for none.</summary>
        private WearNTear FindDamaged(out string hutToken, out ColonyBuilding hutOf)
        {
            hutToken = "";
            hutOf = null;
            var huts = new Dictionary<string, ColonyBuilding>();
            foreach (ColonyBuilding b in ColonyBuilding.All)
                if (b != null && b.IsValid && b.ColonyId == Citizen.ColonyId && !string.IsNullOrEmpty(b.Id)) huts[b.Id] = b;
            if (huts.Count == 0) return null;
            var found = new List<WearNTear>();
            var spots = new List<Vec3>();
            var sites = new List<string>();
            // The huts' own chests too: losing one loses the hut (review of 0.13).
            foreach (KeyValuePair<string, ColonyBuilding> hut in huts)
            {
                WearNTear chest = hut.Value.GetComponent<WearNTear>();
                if (chest == null || !RepairRules.NeedsMending(chest.GetHealthPercentage()) || _badMends.IsBlocked(IdOf(chest), Time.time)) continue;
                found.Add(chest);
                spots.Add(V(chest.transform.position));
                sites.Add(hut.Key);
            }
            foreach (WearNTear wear in WearNTear.GetAllInstances())
            {
                if (wear == null || !RepairRules.NeedsMending(wear.GetHealthPercentage())) continue;
                string site = PiecePlacer.SiteOf(wear.gameObject);
                if (site.Length == 0 || !huts.ContainsKey(site) || _badMends.IsBlocked(IdOf(wear), Time.time)) continue;
                found.Add(wear);
                spots.Add(V(wear.transform.position));
                sites.Add(site);
            }
            int pick = RepairRules.Nearest(spots, V(Citizen.transform.position));
            if (pick < 0) return null;
            hutOf = huts[sites[pick]];
            hutToken = hutOf.Type?.NameToken ?? "";
            return found[pick];
        }

        private static string IdOf(WearNTear wear)
        {
            ZNetView nview = wear.GetComponent<ZNetView>();
            return nview != null && nview.IsValid() ? nview.GetZDO().m_uid.ToString() : wear.GetInstanceID().ToString();
        }
    }
}
