using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Jotunn.Entities;
using UnityEngine;
using VikingColonies.AI;
using VikingColonies.Buildings;
using VikingColonies.Citizens;
using VikingColonies.Colony;
using VikingColonies.Construction;
using VikingColonies.Core;

namespace VikingColonies.DevTools
{
    /// <summary>
    /// vc_seattest (test worlds): every seat inside a hut can be sat on by a settler walking in from its door (the Mead
    /// Hall's designs, 1.6: its benches stand against the wall with the long table right in front of them, so a settler
    /// reaches each from beside it, not from the front). One settler of the hut's colony a seat, from 2 m outside the
    /// door, the hut's seats held for the test alone meanwhile; each seat a PASS or FAIL line, then "vc_seattest: done".
    /// </summary>
    internal sealed class SeatTestCommand : ConsoleCommand
    {
        public override string Name => "vc_seattest";
        public override string Help => "VikingColonies (test worlds): 'vc_seattest <hut>' sends a settler of its colony to each seat inside the nearest " +
                                       "hut of that kind (within 40 m), from outside its door, and says which seats they sat on within 40 s each";
        public override bool IsCheat => true;

        public override void Run(string[] args)
        {
            Player p = Player.m_localPlayer;
            if (p == null || DevHelpers.RefuseOutsideTestWorld(Name)) return;
            BuildingType type = args != null && args.Length > 0 ? DevHelpers.TypeByWord(args[0]) : null;
            if (type == null)
            {
                BuildCommand.Print("vc_seattest: say which hut, e.g. vc_seattest meadhall");
                return;
            }
            ColonyBuilding hut = ColonyBuilding.All.Where(b => b != null && b.IsValid && b.TypeId == type.Id)
                .OrderBy(b => Vector3.Distance(b.transform.position, p.transform.position))
                .FirstOrDefault(b => Vector3.Distance(b.transform.position, p.transform.position) <= 40f);
            if (hut == null)
            {
                BuildCommand.Print($"vc_seattest: no {type.Id} within 40 m");
                return;
            }
            Plugin.Instance.StartCoroutine(LifeTest.Guarded(Run(hut)));
        }

        private static IEnumerator Run(ColonyBuilding hut)
        {
            Log.Event($"vc_seattest: starting at {hut.TypeId} {hut.Id} level {hut.Level}");
            ConstructionModule site = hut.GetComponent<ConstructionModule>();
            Footprint room = site != null ? site.RoomFootprint : null;
            List<Chair> chairs = Object.FindObjectsByType<Chair>(FindObjectsSortMode.None)
                .Where(c => c != null && c.m_attachPoint != null && c.GetComponentInParent<Piece>() != null
                            && PiecePlacer.SiteOf(c.GetComponentInParent<Piece>().gameObject) == hut.Id
                            && (room == null || room.Contains(CoreVectors.V(c.m_attachPoint.position))))
                .OrderBy(c => c.m_attachPoint.position.x).ThenBy(c => c.m_attachPoint.position.z).ToList();
            List<Citizen> settlers = Citizen.All.Where(c => c != null && c.ColonyId == hut.ColonyId && c.IsOwner && !c.Record.Arriving
                                                             && c.GetComponent<CitizenAI>()?.FreeTimeState != null).ToList();
            if (chairs.Count == 0 || settlers.Count == 0)
            {
                Log.Event($"vc_seattest: FAIL nothing to test ({chairs.Count} seats inside, {settlers.Count} settlers of the colony here)");
                Log.Event("vc_seattest: done");
                yield break;
            }
            Vector3 outside = DoorOutside(hut, site);
            ColonyPlaces.Forget();
            int pass = 0, n = 0;
            // The hut's seats are the test's alone while it runs: everyone else gets up and strolls off and doesn't come
            // back to them, so what is tested is the way to each seat, not whether it was free (runs 4-6: the colony's
            // other settlers sat on them, crowded the way along the table, or were sat on instead).
            FreeTimeRoutine.SeatsHeldForTest = new HashSet<Chair>(chairs);
            foreach (Citizen other in settlers)
                other.GetComponent<CitizenAI>()?.FreeTimeState?.ForceForTest(Leisure.Stroll);
            yield return new WaitForSeconds(5f);
            try
            {
                foreach (Chair chair in chairs)
                {
                    n++;
                    // One in their free time (not the cook, nor a builder at work): the next in turn, waiting a little for one.
                    Citizen sitter = null;
                    for (float w = 0f; sitter == null && w < 30f; w += 1f)
                    {
                        sitter = settlers.Skip(n % settlers.Count).Concat(settlers.Take(n % settlers.Count))
                            .FirstOrDefault(c => c != null && c.GetComponent<CitizenAI>().FreeTimeState.Active);
                        if (sitter == null) yield return new WaitForSeconds(1f);
                    }
                    if (sitter == null)
                    {
                        Log.Event($"vc_seattest: FAIL seat {n}: no settler in their free time");
                        continue;
                    }
                    SettlerPose pose = sitter.GetComponent<SettlerPose>();
                    FreeTimeRoutine routine = sitter.GetComponent<CitizenAI>().FreeTimeState;
                    if (pose != null && pose.Held) pose.StandAt(outside);
                    sitter.transform.position = outside;
                    yield return new WaitForSeconds(1f);
                    bool sat = false;
                    float t = 0f;
                    try
                    {
                        routine.ForceSeatForTest(chair);
                        for (; t < 40f && !(sat = pose != null && pose.Current == SettlerPose.Kind.Seat && pose.Held && pose.Point == chair.m_attachPoint); t += 0.5f)
                        {
                            // Anyone else standing at the seat under test goes out by the door: inside the hut a stroll has
                            // nowhere to go and sitting down is on the spot (v16's meadhall.txt, 27 Oct: Halvar sat down on the
                            // floor by it, Gunnar's stroll stayed put there, and it counted as taken for 40 s).
                            foreach (Citizen other in settlers)
                                if (other != null && other != sitter && Vector3.Distance(other.transform.position, chair.m_attachPoint.position) <= 1.5f)
                                {
                                    Log.Event($"vc_seattest: {other.CitizenName} stood at the seat under test; out by the door");
                                    other.GetComponent<CitizenAI>()?.PlaceForTest(outside);
                                    other.GetComponent<CitizenAI>()?.FreeTimeState?.ForceForTest(Leisure.Stroll);
                                }
                            yield return new WaitForSeconds(0.5f);
                        }
                    }
                    finally
                    {
                        routine.ClearSeatForTest();
                    }
                    Vector3 at = chair.m_attachPoint.position - hut.transform.position;
                    string where = $"({at.x:0.0}, {at.z:0.0}) from the chest, on {Utils.GetPrefabName(chair.transform.root.gameObject)}";
                    if (sat) pass++;
                    string instead = pose != null && pose.Held && pose.Current == SettlerPose.Kind.Seat ? ", sat on another seat instead" : "";
                    Log.Event($"vc_seattest: {(sat ? "PASS" : "FAIL")} seat {n} {where}: {sitter.CitizenName} " +
                              (sat ? $"sat down after {t:0.0} s" : $"didn't sit within 40 s (pose {pose?.Current}{instead}, " +
                               $"{Utils.DistanceXZ(sitter.transform.position, chair.m_attachPoint.position):0.0} m from it, at " +
                               $"({(sitter.transform.position - hut.transform.position).x:0.0}, {(sitter.transform.position - hut.transform.position).z:0.0}) from the chest)"));
                    routine.ForceForTest(Leisure.Stroll);   // up again and out for the next test's way in
                    yield return new WaitForSeconds(3f);
                }
            }
            finally
            {
                FreeTimeRoutine.SeatsHeldForTest = null;
            }
            Log.Event($"vc_seattest: {pass} of {n} seats sat on");
            Log.Event("vc_seattest: done");
        }

        /// <summary>2 m outside the hut's front door (the door farthest from the chest), on the ground.</summary>
        private static Vector3 DoorOutside(ColonyBuilding hut, ConstructionModule site)
        {
            Vector3 chest = hut.transform.position;
            Door door = Object.FindObjectsByType<Door>(FindObjectsSortMode.None)
                .Where(d => d != null && PiecePlacer.SiteOf(d.gameObject) == hut.Id)
                .OrderByDescending(d => Utils.DistanceXZ(d.transform.position, chest)).FirstOrDefault();
            Vector3 from = door != null ? door.transform.position : chest + hut.transform.forward * 8f;
            Vector3 out1 = from - chest;
            out1.y = 0f;
            Vector3 at = from + out1.normalized * 2f;
            at.y = ZoneSystem.instance.GetGroundHeight(at);
            return at;
        }
    }
}
