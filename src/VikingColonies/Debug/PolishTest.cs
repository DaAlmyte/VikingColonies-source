using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;
using VikingColonies.Buildings;
using VikingColonies.Citizens;
using VikingColonies.Config;
using VikingColonies.Construction;
using VikingColonies.Core;

namespace VikingColonies.DevTools
{
    /// <summary>vc_polishtest (test worlds): the colony polish (0.19) checked in the game in a minute or two.</summary>
    internal sealed class PolishTestCommand : ConsoleCommand
    {
        public override string Name => "vc_polishtest";
        public override string Help => "VikingColonies (test worlds): place a Town Hall here and check the colony polish of 0.19 (costs that follow " +
                                       "the settings, no settler body). Removes what it made.";
        public override bool IsCheat => true;

        public override void Run(string[] args)
        {
            Player p = Player.m_localPlayer;
            if (p == null || ZNet.instance == null || !TestWorlds.IsTestWorld(ZNet.instance.GetWorldName()))
            {
                BuildCommand.Print("vc_polishtest: test worlds only");
                return;
            }
            Plugin.Instance.StartCoroutine(PolishTest.Run(p));
        }
    }

    internal static class PolishTest
    {
        private static int s_pass, s_fail;

        public static IEnumerator Run(Player player)
        {
            s_pass = s_fail = 0;
            Log.Event("vc_polishtest: starting");
            if (!player.InGodMode()) player.SetGodMode(true);
            Vector3 origin = player.transform.position, forward = player.transform.forward;
            var before = new HashSet<string>(Citizen.All.Where(c => c != null).Select(c => c.Id));
            int woodWas = ColonyConfig.HallWoodCost.Value;
            bool walkInWas = ColonyConfig.NewcomersWalkIn.Value;
            ColonyBuilding hall = null, farm = null;
            try
            {
                // 1. A cost set in the settings (or by the server) is what the hammer asks for, at once.
                CustomPiece hallPiece = PieceManager.Instance.GetPiece(BuildingTypes.TownHall.PrefabName);
                int setTo = woodWas + 3 <= 500 ? woodWas + 3 : woodWas - 3;
                ColonyConfig.HallWoodCost.Value = setTo;
                yield return null;
                int asked = hallPiece != null && hallPiece.Piece != null
                    ? hallPiece.Piece.m_resources.Where(r => r != null && r.m_resItem != null && r.m_resItem.name == "Wood").Sum(r => r.m_amount) : -1;
                Check($"A Town Hall's wood cost changed in the settings is what the hammer asks for at once ({setTo} set, {asked} asked)", asked == setTo);
                ColonyConfig.HallWoodCost.Value = woodWas;
                yield return null;

                // 2. With no settler body, a newcomer gives a message and nothing half made, and the Town Hall carries on.
                ColonyConfig.NewcomersWalkIn.Value = false;
                hall = DevHelpers.Place(BuildingTypes.TownHall.PrefabName, origin + forward * 8f, Quaternion.identity, player.GetPlayerID());
                DevHelpers.KeepNothing(hall);
                TownHallModule townHall = hall != null ? hall.GetComponent<TownHallModule>() : null;
                for (float t = 0f; townHall != null && !townHall.Founded && t < 20f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                int settlersBefore = Citizen.All.Count(c => c != null && hall != null && c.ColonyId == hall.ColonyId);
                int toldBefore = CitizenPrefab.MissingTold;
                CitizenPrefab.HideForTest = true;
                Citizen made = null;
                string error = "";
                try
                {
                    made = townHall != null ? townHall.NewcomerForTest() : null;
                }
                catch (Exception e)
                {
                    error = e.GetType().Name + ": " + e.Message;
                }
                CitizenPrefab.HideForTest = false;
                yield return new WaitForSeconds(1f);
                int settlersAfter = Citizen.All.Count(c => c != null && hall != null && c.ColonyId == hall.ColonyId);
                Check($"With no settler body a newcomer is a message, not an error (made: {made != null}, error: '{error}', settlers {settlersBefore} -> {settlersAfter}, " +
                      $"told {CitizenPrefab.MissingTold - toldBefore})",
                    townHall != null && made == null && error.Length == 0 && settlersAfter == settlersBefore && CitizenPrefab.MissingTold > toldBefore);

                // 3. A newcomer walks in from a spot the player isn't looking at, on walkable ground (0.16 review M3, M4).
                if (townHall != null)
                {
                    ColonyConfig.NewcomersWalkIn.Value = true;
                    Vector3 toHall = hall.transform.position - player.GetEyePoint();
                    player.SetLookDir(toHall.normalized);   // facing the hall, as one who just placed it
                    yield return null;
                    // The spot is chosen by where the player looks right now; measured at once, by the same look (two full
                    // runs on 2026-10-18: a frame later the game's own mouse look had turned the player, and a spot chosen out
                    // of view measured 38 and 70 degrees off).
                    Vector3 lookWas = player.GetLookDir();
                    Citizen newcomer = townHall.NewcomerForTest();
                    // Where he stands: he appears half a metre over his spot (SpawnNewcomer) and drops onto it.
                    Vector3 start = newcomer != null ? newcomer.transform.position - Vector3.up * 0.5f : Vector3.zero;
                    bool seenByRule = newcomer != null && TownHallModule.SeenForTest(start);
                    float off = newcomer != null ? Vector3.Angle(lookWas, start + Vector3.up * 1.6f - player.GetEyePoint()) : -1f;
                    yield return null;
                    bool hidden = newcomer != null && Physics.Linecast(player.GetEyePoint(), start + Vector3.up * 1.6f,
                        LayerMask.GetMask("piece", "static_solid", "terrain", "Default"), QueryTriggerInteraction.Ignore);
                    // Not inside a rock, a tree or a floor (the game's walking map out there may not be built yet to ask).
                    bool walkable = newcomer != null && !Physics.CheckCapsule(start + Vector3.up * 0.6f, start + Vector3.up * 1.6f, 0.3f,
                        LayerMask.GetMask("piece", "static_solid", "Default"), QueryTriggerInteraction.Ignore);
                    // What stood in the way, for a miss to explain itself (1.4.3's second full run: "standing free False", once).
                    string inTheWay = newcomer == null || walkable ? "" : ", in the way: " + string.Join(", ",
                        Physics.OverlapCapsule(start + Vector3.up * 0.6f, start + Vector3.up * 1.6f, 0.3f,
                                LayerMask.GetMask("piece", "static_solid", "Default"), QueryTriggerInteraction.Ignore)
                            .Select(c => $"{Utils.GetPrefabName(c.transform.root.gameObject)} ({LayerMask.LayerToName(c.gameObject.layer)})")
                            .Distinct().ToArray());
                    float fromHall = newcomer != null ? Utils.DistanceXZ(start, hall.transform.position) : 0f;
                    Check($"A newcomer appears where you aren't looking, on walkable ground ({(newcomer != null ? newcomer.CitizenName : "none")}: " +
                          $"{fromHall:0} m from the hall, {off:0} degrees off your view, hidden {hidden}, seen by the game's rule {seenByRule}, standing free {walkable}{inTheWay}; " +
                          $"chosen in view: {TownHallModule.LastEdgeSeen}, usable out of view then: {TownHallModule.LastUnseenUsable})",
                        newcomer != null && walkable
                        && (fromHall < 15f || off > 70f || hidden || !seenByRule || TownHallModule.LastEdgeSeen && TownHallModule.LastUnseenUsable == 0));   // in view only when nothing out of view would do
                }

                // 4. With a planting mod (PlantEverything), the farmer plants its raspberry bush on his field and picks it,
                // and it stays to regrow. Without one, there is nothing to check.
                CropInfo bushCrop = Jobs.CropList.Crops.FirstOrDefault(c => c.Piece == "RaspberryBush");
                if (bushCrop == null) Check("Bushes on the Farm's list: no planting mod here, nothing to check", true);
                else if (townHall != null)
                {
                    EnvMan.instance.m_debugTimeOfDay = true;
                    EnvMan.instance.m_debugTime = 0.5f;
                    farm = DevHelpers.Place(BuildingTypes.Farm.PrefabName, hall.transform.position + player.transform.right * 16f, Quaternion.identity, player.GetPlayerID());
                    ConstructionModule farmSite = farm != null ? farm.GetComponent<ConstructionModule>() : null;
                    for (float t = 0f; farmSite != null && farmSite.Order == null && t < 15f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                    if (farmSite != null && farmSite.Order != null) farmSite.FinishNow();
                    for (float t = 0f; farmSite != null && !farmSite.IsBuilt && t < 40f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                    WorkerModule farmWork = farm != null ? farm.GetComponent<WorkerModule>() : null;
                    if (farmWork != null)
                    {
                        foreach (CropInfo crop in Jobs.CropList.Crops.Where(c => c != bushCrop)) farmWork.RequestSetGrowTicked(crop.Piece, false);
                        farmWork.RequestSetGrowTicked(bushCrop.Piece, true);   // a bush is off until ticked (review of 0.19)
                        Inventory farmChest = farm.GetComponent<Container>().GetInventory();
                        farmChest.AddItem(ObjectDB.instance.GetItemPrefab("Cultivator"), 1);
                        farmChest.AddItem(ObjectDB.instance.GetItemPrefab("Raspberry"), bushCrop.SeedCount * 2);
                    }
                    // A Farm works only its fields (0.20.1): a field for the Farm's list (the bush alone ticked).
                    Colony.FieldMarker bushField = farm != null ? DevHelpers.PlaceField(farm, farm.transform.position + farm.transform.forward * 9f) : null;
                    yield return new WaitForSeconds(1f);
                    List<Vector3> plots = farm != null ? Jobs.FarmerJob.RulesOf(farm).Select(r => CoreVectors.U(r.At)).ToList() : new List<Vector3>();
                    Pickable PlantedBush() => Jobs.WorldScan.Pickables().FirstOrDefault(p => p != null && Utils.GetPrefabName(p.gameObject) == "RaspberryBush"
                        && FieldPlots.OnPlot(CoreVectors.V(p.transform.position), plots.Select(CoreVectors.V)));
                    for (float t = 0f; PlantedBush() == null && t < 150f; t += 1f) yield return new WaitForSeconds(1f);
                    Pickable bushOnPlot = PlantedBush();
                    for (float t = 0f; bushOnPlot != null && !bushOnPlot.GetPicked() && t < 90f; t += 1f) yield return new WaitForSeconds(1f);
                    Check($"With PlantEverything the farmer plants a raspberry bush on his field for {bushCrop.SeedCount} raspberries, and picks it, and it stays to regrow " +
                          $"(planted: {bushOnPlot != null}, picked: {bushOnPlot != null && bushOnPlot.GetPicked()}, still there: {bushOnPlot != null})",
                        bushOnPlot != null && bushOnPlot.GetPicked());
                    EnvMan.instance.m_debugTimeOfDay = false;
                }
            }
            finally
            {
                EnvMan.instance.m_debugTimeOfDay = false;
                CitizenPrefab.HideForTest = false;
                ColonyConfig.HallWoodCost.Value = woodWas;
                ColonyConfig.NewcomersWalkIn.Value = walkInWas;
            }
            Log.Event($"vc_polishtest: done — {s_pass} passed, {s_fail} failed");
            player.Message(MessageHud.MessageType.Center, $"vc_polishtest: {s_pass} passed, {s_fail} failed");
            foreach (Citizen c in Citizen.All.Where(c => c != null && !before.Contains(c.Id)).ToList()) Remove(c.gameObject);
            if (hall != null) Remove(hall.gameObject);
            if (farm != null)
            {
                Vector3 farmAt = farm.transform.position;
                foreach (Pickable bush in Jobs.WorldScan.Pickables().Where(b => b != null && Utils.GetPrefabName(b.gameObject) == "RaspberryBush" && Utils.DistanceXZ(b.transform.position, farmAt) < 20f).ToList())
                    Remove(bush.gameObject);
                foreach (Colony.FieldMarker field in Colony.FieldMarker.All.Where(m => m != null && Utils.DistanceXZ(m.transform.position, farmAt) < 20f).ToList())
                    Remove(field.gameObject);
                Remove(farm.gameObject);
            }
            Log.Event("vc_polishtest: cleaned up");
        }

        private static void Remove(GameObject go)
        {
            ZNetView nview = go.GetComponent<ZNetView>();
            if (nview != null && nview.IsValid() && !nview.IsOwner()) nview.ClaimOwnership();
            ZNetScene.instance.Destroy(go);
        }

        private static void Check(string what, bool ok)
        {
            if (ok) s_pass++;
            else s_fail++;
            Log.Event($"vc_polishtest: {(ok ? "PASS" : "FAIL")} {what}");
        }
    }
}
