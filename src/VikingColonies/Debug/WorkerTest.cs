using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Jotunn.Entities;
using UnityEngine;
using VikingColonies.AI;
using VikingColonies.Buildings;
using VikingColonies.Citizens;
using VikingColonies.Colony;
using VikingColonies.Config;
using VikingColonies.Construction;
using VikingColonies.Core;
using VikingColonies.Jobs;

namespace VikingColonies.DevTools
{
    /// <summary>vc_workertest (test worlds): the worker polish (0.20 fields, 0.21 keep in stock) checked in the game in a few minutes.</summary>
    internal sealed class WorkerTestCommand : ConsoleCommand
    {
        public override string Name => "vc_workertest";
        public override string Help => "VikingColonies (test worlds): place a Town Hall and a turned Farm here and check the worker polish of 0.20 " +
                                       "(turned huts, seed batches, field markers, the field limit, Recall to the hut, the wedged rescue) and of 0.21 (keep in stock, " +
                                       "carrying in free time, the full chest, the Keep in stock tab) and 0.22 (the Trees list, trees by buildings, Stop at, skills) and 0.23 (hand over, take down). Removes what it made.";
        public override bool IsCheat => true;

        public override void Run(string[] args)
        {
            Player p = Player.m_localPlayer;
            if (p == null || ZNet.instance == null || !TestWorlds.IsTestWorld(ZNet.instance.GetWorldName()))
            {
                BuildCommand.Print("vc_workertest: test worlds only");
                return;
            }
            bool keep = args != null && args.Any(a => a == "keep");
            Plugin.Instance.StartCoroutine(WorkerTest.Run(p, keep));
        }
    }

    internal static class WorkerTest
    {
        private static int s_pass, s_fail;

        public static IEnumerator Run(Player player, bool keep)
        {
            s_pass = s_fail = 0;
            Log.Event("vc_workertest: starting");
            if (!player.InGodMode()) player.SetGodMode(true);
            EnvMan.instance.m_debugTimeOfDay = true;
            EnvMan.instance.m_debugTime = 0.4f;   // morning: a whole day of work ahead, no breakfast
            Vector3 origin = player.transform.position, forward = player.transform.forward, right = player.transform.right;
            forward.y = right.y = 0f;
            var before = new HashSet<string>(Citizen.All.Where(c => c != null).Select(c => c.Id));
            var markers = new List<GameObject>();
            ColonyBuilding hall = null, farm = null, lumberHut = null, builderHut = null, foragerHut = null;
            bool walkInWas = ColonyConfig.NewcomersWalkIn.Value;
            try
            {
                ColonyConfig.NewcomersWalkIn.Value = false;
                hall = DevHelpers.Place(BuildingTypes.TownHall.PrefabName, origin + forward * 8f, Quaternion.identity, player.GetPlayerID());
                TownHallModule townHall = hall != null ? hall.GetComponent<TownHallModule>() : null;
                for (float t = 0f; townHall != null && !townHall.Founded && t < 20f; t += 0.5f) yield return new WaitForSeconds(0.5f);

                // 1. A Farm turned 37°: its farmer walks out of it and plants (the walking map is wrong inside turned huts).
                Vector3 farmAt = origin + forward * 22f;
                farm = DevHelpers.Place(BuildingTypes.Farm.PrefabName, farmAt, Quaternion.Euler(0f, 37f, 0f), player.GetPlayerID());
                ConstructionModule site = farm != null ? farm.GetComponent<ConstructionModule>() : null;
                for (float t = 0f; site != null && site.Order == null && t < 15f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                if (site != null && site.Order != null) site.FinishNow();
                for (float t = 0f; site != null && !site.IsBuilt && t < 40f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                Citizen farmer = null;
                WorkerModule work = farm != null ? farm.GetComponent<WorkerModule>() : null;
                for (float t = 0f; work != null && farmer == null && t < 30f; t += 0.5f)
                {
                    yield return new WaitForSeconds(0.5f);
                    if (work.FindWorker(out Citizen found, out _) == WorkerModule.WorkerState.Loaded) farmer = found;
                }
                if (farm == null || farmer == null)
                {
                    Check($"A turned Farm is put up and hires a farmer (Farm {farm != null}, farmer {farmer != null})", false);
                    yield break;
                }
                Inventory chest = farm.GetComponent<Container>().GetInventory();
                chest.AddItem(ObjectDB.instance.GetItemPrefab("Cultivator"), 1);
                chest.AddItem(ObjectDB.instance.GetItemPrefab("CarrotSeeds"), 30);
                CitizenAI ai = farmer.GetComponent<CitizenAI>();
                FarmerJob Job() => ai != null ? ai.JobForTest as FarmerJob : null;
                FieldsModule fields = farm.GetComponent<FieldsModule>();

                // 5 (0.20.1). A Farm without a field works no ground of its own, and its farmer says so.
                for (float t = 0f; !(farmer.Status ?? "").Contains("vc_a_field") && t < 30f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                int noFieldPlots = FarmerJob.RulesOf(farm).Count;
                Check($"A Farm without a field works no ground of its own, and its farmer says so ({noFieldPlots} plots; " +
                      $"'{Localization.instance.Localize(farmer.Status ?? "")}')", noFieldPlots == 0 && (farmer.Status ?? "").Contains("vc_a_field"));
                int passesBefore = ai != null ? ai.DoorPasses : 0;

                // 3. A field marker 14 m off joins the Farm by itself; its 3x3 field of carrots gets planted.
                GameObject markerPrefab = ZNetScene.instance.GetPrefab(SaveKeys.FieldMarkerPiece);
                Vector3 fieldAt = farm.transform.position + right * 14f;
                fieldAt.y = ZoneSystem.instance.GetGroundHeight(fieldAt);
                GameObject markerGo = markerPrefab != null ? Object.Instantiate(markerPrefab, fieldAt, Quaternion.identity) : null;
                if (markerGo != null) markers.Add(markerGo);
                FieldMarker marker = markerGo != null ? markerGo.GetComponent<FieldMarker>() : null;
                for (float t = 0f; marker != null && marker.FarmId != farm.Id && t < 15f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                bool joined = marker != null && marker.FarmId == farm.Id;
                marker?.RequestSetSize(3);
                marker?.RequestSetCrop("sapling_carrot");
                yield return new WaitForSeconds(1f);
                bool CarrotOnField() => marker != null && WorldScan.Plants().Any(pl => pl != null && Utils.GetPrefabName(pl.gameObject) == "sapling_carrot"
                    && FieldPlots.OnPlot(CoreVectors.V(pl.transform.position), marker.PlotPositions().Select(CoreVectors.V)));
                for (float t = 0f; !CarrotOnField() && t < 180f; t += 1f) yield return new WaitForSeconds(1f);

                // 1. The farmer of a Farm turned 37° goes out by its door to his field and plants (the walking map is wrong
                // inside turned huts).
                for (float t = 0f; (Job() == null || Job().Planted < 3) && t < 180f; t += 1f) yield return new WaitForSeconds(1f);
                FarmerJob job = Job();
                Check($"A farmer in a Farm turned 37° goes in and out by its door and plants ({farmer.CitizenName}: door passes " +
                      $"{(ai != null ? ai.DoorPasses - passesBefore : -1)}, planted {(job != null ? job.Planted : -1)})",
                    ai != null && ai.DoorPasses > passesBefore && job != null && job.Planted >= 3);

                // 2. Seeds for several plots in one visit to the chest.
                Check($"He takes seeds for several plots at once ({(job != null ? job.SeedFetches : -1)} visits to the chest for " +
                      $"{(job != null ? job.Planted : -1)} plots)", job != null && job.Planted >= 3 && job.SeedFetches < job.Planted);

                Check($"A field marker 14 m away joins the Farm by itself, and its 3x3 field of carrots is planted (joined {joined}, " +
                      $"size {(marker != null ? marker.Size : 0)}, crop '{(marker != null ? marker.Crop : "")}', carrots planted on it: {CarrotOnField()})",
                    joined && marker.Size == 3 && marker.Crop == "sapling_carrot" && CarrotOnField());

                // 12 (1.4.3, the player's watch of 1.4.2: settlers ate a Farm's seed carrots). The Farm keeps the seeds its field
                // plants, one per plot; only what's beyond them is spare.
                yield return new WaitForSeconds(2.5f);   // worked out at most every 2 s
                IReadOnlyDictionary<string, int> working = farm.Storage.Working;
                int keeps = working != null && working.TryGetValue("CarrotSeeds", out int kept) ? kept : 0;
                int seedsBefore = farm.Storage.Contents().TryGetValue("CarrotSeeds", out int sb) ? sb : 0;
                int spareSeeds = farm.Storage.Spare("CarrotSeeds");
                int seedsAfter = farm.Storage.Contents().TryGetValue("CarrotSeeds", out int sa) ? sa : 0;
                int fieldPlots = marker != null ? marker.PlotPositions().Count : -1;
                Check($"The Farm keeps the seeds its field plants, one per plot ({keeps} CarrotSeeds kept for {fieldPlots} plots; {seedsAfter} in its chest, " +
                      $"{spareSeeds} spare)", keeps == fieldPlots && (spareSeeds == Mathf.Max(0, seedsBefore - keeps) || spareSeeds == Mathf.Max(0, seedsAfter - keeps)));

                // 4. A second field with a level-1 Farm stays without one; the Fields tab says so.
                Vector3 secondAt = farm.transform.position - right * 14f;
                secondAt.y = ZoneSystem.instance.GetGroundHeight(secondAt);
                GameObject secondGo = markerPrefab != null ? Object.Instantiate(markerPrefab, secondAt, Quaternion.identity) : null;
                if (secondGo != null) markers.Add(secondGo);
                FieldMarker second = secondGo != null ? secondGo.GetComponent<FieldMarker>() : null;
                yield return new WaitForSeconds(12f);
                // Next to the Farm, as the window closes beyond 8 m (run fields-self1: setting the position didn't stick).
                Vector3 nearFarm = farm.transform.position + forward * 4f;
                nearFarm.y = ZoneSystem.instance.GetGroundHeight(nearFarm) + 0.2f;
                player.TeleportTo(nearFarm, player.transform.rotation, distantTeleport: false);
                for (float t = 0f; Vector3.Distance(player.transform.position, farm.transform.position) > 6f && t < 10f; t += 0.5f)
                    yield return new WaitForSeconds(0.5f);
                Gui.Windows.Hut.Open(farm);
                Gui.Windows.Hut.ShowTabForTest("fields");
                yield return new WaitForSeconds(1f);
                string tab = Gui.Windows.Hut.TextForTest();
                float fromFarm = Vector3.Distance(player.transform.position, farm.transform.position);
                Gui.Windows.Hut.Close();
                Check($"A second field with a level-1 Farm stays without a Farm, and the Fields tab says 'Fields: 1 of 1' (its Farm: " +
                      $"'{(second != null ? second.FarmId : "none")}', you {fromFarm:0} m from the Farm, tab: {tab.Substring(0, Mathf.Min(120, tab.Length))})",
                    second != null && second.FarmId.Length == 0 && tab.Contains("Fields: 1 of 1"));

                // 6. The marker's size: a manager's request makes it 5x5 (24 plots); a stranger's is refused.
                if (marker != null)
                {
                    marker.SetSizeAsForTest(424242L, 7);   // nobody: refused
                    yield return null;
                    bool strangerRefused = marker.Size == 3;
                    marker.RequestSetSize(FieldSizes.Bigger(marker.Size));
                    yield return new WaitForSeconds(1f);
                    Check($"The marker grows to 5x5 (24 plots) for one who may manage the colony and not for a stranger (size {marker.Size}, " +
                          $"{marker.PlotPositions().Count} plots, stranger refused {strangerRefused})", marker.Size == 5 && marker.PlotPositions().Count == 24 && strangerRefused);
                }
                else Check("The marker's window (no marker)", false);

                // 9 (final review of 0.20). A field taken from its Farm stays without one, though the Farm takes new fields by
                // itself (auto-assign undid "Leave" within 5 s).
                if (marker != null)
                {
                    // The second field goes first, or it takes the free place and this one stays out anyway (run review-red).
                    if (secondGo != null)
                    {
                        markers.Remove(secondGo);
                        Remove(secondGo);
                    }
                    yield return new WaitForSeconds(1f);
                    marker.RequestAssign("");
                    yield return new WaitForSeconds(12f);   // two of its owner's checks
                    Check($"A field taken from its Farm by hand stays without one (its Farm: '{marker.FarmId}', the Farm takes new fields " +
                          $"by itself: {fields.AutoAssign})", marker.FarmId.Length == 0);
                }
                else Check("A field taken from its Farm (no marker)", false);

                // 11 (1.4.3, the player's watch of 1.4.2: a field in the Farm's fenced yard had all its plots left out, and its
                // farmer said "Needs a field"). A field on the finished Farm's own site, outside its walls, is worked.
                if (site != null && site.SiteFootprint != null && site.RoomFootprint != null)
                {
                    Vector3 yard = farm.transform.position;
                    int best = -1;
                    for (float dx = -10f; dx <= 10f; dx += 0.5f)
                        for (float dz = -10f; dz <= 10f; dz += 0.5f)
                        {
                            Vector3 c = farm.transform.position + new Vector3(dx, 0f, dz);
                            int inYard = 0;
                            for (int i = -1; i <= 1; i++)
                                for (int j = -1; j <= 1; j++)
                                {
                                    Vec3 plot = CoreVectors.V(c + new Vector3(i * FieldPlots.Spacing, 0f, j * FieldPlots.Spacing));
                                    if (site.SiteFootprint.Contains(plot) && !site.RoomFootprint.Contains(plot)) inYard++;
                                }
                            if (inYard > best)
                            {
                                best = inYard;
                                yard = c;
                            }
                        }
                    yard.y = ZoneSystem.instance.GetGroundHeight(yard);
                    GameObject yardGo = markerPrefab != null ? Object.Instantiate(markerPrefab, yard, Quaternion.identity) : null;
                    if (yardGo != null) markers.Add(yardGo);
                    FieldMarker yardField = yardGo != null ? yardGo.GetComponent<FieldMarker>() : null;
                    for (float t = 0f; yardField != null && yardField.FarmId != farm.Id && t < 15f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                    // The farmer looks at its plots again: every 8 s while it has nothing to do (watch143's first run checked after 4).
                    for (float t = 0f; (farmer.Status ?? "").Contains("vc_a_field") && t < 20f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                    int yardPlots = FarmerJob.RulesOf(farm, out PlotTally yardTally).Count;
                    Check($"A field in the finished Farm's own yard (on its site, outside its walls) is worked ({best} of 9 spots in the yard; joined " +
                          $"{yardField != null && yardField.FarmId == farm.Id}; {yardPlots} plots: {FieldWords.Tally(yardTally)}; " +
                          $"'{Localization.instance.Localize(farmer.Status ?? "")}')",
                        yardField != null && yardField.FarmId == farm.Id && best >= 3 && yardPlots > 0 && !(farmer.Status ?? "").Contains("vc_a_field"));
                    if (yardGo != null)
                    {
                        markers.Remove(yardGo);
                        Remove(yardGo);
                    }
                }
                else Check("A field in the Farm's own yard (no site footprint)", false);

                // 10 (final review of 0.20). A marker placed by one who may not manage the colony doesn't join it.
                Vector3 strangerAt = farm.transform.position - forward * 16f;
                strangerAt.y = ZoneSystem.instance.GetGroundHeight(strangerAt);
                GameObject strangerGo = markerPrefab != null ? Object.Instantiate(markerPrefab, strangerAt, Quaternion.identity) : null;
                if (strangerGo != null)
                {
                    markers.Add(strangerGo);
                    strangerGo.GetComponent<ZNetView>()?.GetZDO()?.Set(ZDOVars.s_creator, 424242L);   // built by nobody of this colony
                }
                FieldMarker strangers = strangerGo != null ? strangerGo.GetComponent<FieldMarker>() : null;
                yield return new WaitForSeconds(7f);
                Check($"A field marker placed by a stranger doesn't join the colony (its colony: '{(strangers != null ? strangers.ColonyId : "none")}', " +
                      $"its Farm: '{(strangers != null ? strangers.FarmId : "none")}')",
                    strangers != null && strangers.ColonyId.Length == 0 && strangers.FarmId.Length == 0);

                // 7. Recall to the hut: from the Town Hall back to just outside the Farm's door.
                if (ai != null) ai.Recall();
                yield return new WaitForSeconds(1f);
                farmer.RequestRecallToHut();
                yield return new WaitForSeconds(0.5f);   // before he walks off to work (full run: 6.2 m two seconds later)
                Door door = site != null ? HutDoors.DoorOf(site) : null;
                float fromDoor = door != null ? Utils.DistanceXZ(farmer.transform.position, door.transform.position) : 99f;
                Check($"Recall to the hut puts the farmer by the Farm's door ({fromDoor:0.0} m from it)", fromDoor < 6f);

                // 8. Wedged inside a hut: moved to just inside its door. Between the chest and the door (1.4.0: the new designs'
                // chests stand against the back wall, so behind the chest is outside the hut).
                Vector3 inside = farm.transform.position + ((door != null ? door.transform.position : farm.transform.position) - farm.transform.position).normalized * 1.5f;
                inside.y = farm.transform.position.y + 0.1f;
                farmer.transform.position = inside;
                Rigidbody body = farmer.GetComponent<Rigidbody>();
                if (body != null) body.position = inside;
                yield return new WaitForSeconds(0.5f);
                bool moved = ai != null && ai.WedgeForTest();
                yield return null;
                float toDoor = door != null ? Utils.DistanceXZ(farmer.transform.position, door.transform.position) : 99f;
                Check($"A settler wedged inside a hut is moved to just inside its door (moved {moved}, now {toDoor:0.0} m from the door)", moved && toDoor < 2.5f);

                // ---- Keep in stock (0.21) ----
                StorageModule hallStore = hall.Storage;
                Inventory hallChest = hall.GetComponent<Container>().GetInventory();
                StockModule hallStock = hall.Stock;
                Citizen carrier = Citizen.All.FirstOrDefault(c => c != null && c != farmer && c.IsOwner && c.ColonyId == hall.ColonyId && string.IsNullOrEmpty(c.Record.Job));
                FreeTimeRoutine carrierTime = carrier != null ? carrier.GetComponent<CitizenAI>()?.FreeTimeState : null;

                // 11. The Town Hall keeps 10 food to start; raspberries only in the Farm's chest: a settler in free time brings some.
                bool hallDefault = hallStock != null && hallStock.List.Kept(StockList.AnyFood) == StockList.TownHallFood;
                chest.AddItem(ObjectDB.instance.GetItemPrefab("Raspberry"), 20);
                int carriedBefore = FreeTimeRoutine.StockCarried;
                int HallFood() => FoodItems.Count(hallStore.Contents());
                for (float t = 0f; (FreeTimeRoutine.StockCarried == carriedBefore || HallFood() == 0) && t < 180f; t += 1f)
                {
                    // In free time and not carrying yet: he chooses carrying now (the dice may take minutes).
                    if (carrierTime != null && carrierTime.Active && carrierTime.Doing != Leisure.CarryStock && (int)t % 15 == 0) carrierTime.ForceForTest(Leisure.CarryStock);
                    yield return new WaitForSeconds(1f);
                }
                Check($"The Town Hall keeps 10 food to start, and a settler with free time brings raspberries from the Farm's chest " +
                      $"(kept at first: {hallDefault}; brought {FreeTimeRoutine.StockCarried - carriedBefore}; the Town Hall holds {HallFood()} food; " +
                      $"carrier {(carrier != null ? carrier.CitizenName : "none")})",
                    hallDefault && FreeTimeRoutine.StockCarried > carriedBefore && HallFood() > 0);

                // 12. Kept wood is left: the Town Hall keeps 30 Wood and holds 40; 10 are spare, a stranger's taking stops there,
                // its own use takes the rest.
                hallChest.AddItem(ObjectDB.instance.GetItemPrefab("Wood"), 40);
                Gui.StockTab.SetForTest(hall, "Wood", 30);
                yield return new WaitForSeconds(1f);
                int spareWood = hallStore.Spare("Wood");
                var takenWood = new CarryLoad();
                int took = 0, ownTook = 0;
                while (took < 60 && hallStore.TryTakeItem("Wood", takenWood)) took++;
                while (ownTook < 60 && hallStore.TryTakeItem("Wood", takenWood, ownUse: true)) ownTook++;
                Check($"With 30 Wood kept of 40, 10 are spare: taking stops after them, its own use takes the rest (spare {spareWood}, " +
                      $"taken {took}, then {ownTook} for its own use)", spareWood == 10 && took == 10 && ownTook == 30);
                Gui.StockTab.SetForTest(hall, "Wood", 0);
                yield return new WaitForSeconds(0.5f);

                // 13. A stranger's change to the list is refused.
                string listBefore = hallStock != null ? hallStock.List.Encode() : "";
                hallStock?.SetAsForTest(424242L, "1|Stone=5");
                yield return null;
                string listAfter = hallStock != null ? hallStock.List.Encode() : "?";
                Check($"A stranger's change to a Keep in stock list is refused ('{listBefore}' stays '{listAfter}')", hallStock != null && listAfter == listBefore);

                // 14. A full chest: the Farm's chest filled, the farmer's load goes to the Town Hall's.
                GameObject stone = ObjectDB.instance.GetItemPrefab("Stone");
                for (int i = 0; i < 200 && chest.CanAddItem(stone, 1); i++) chest.AddItem(stone, 50);
                int load = Mathf.Max(1, ColonyConfig.FarmerCarryCapacity.Value);
                farmer.Carry.Add("Resin", load);
                farmer.SaveCarry();
                int ResinInHall() => hallStore.Contents().TryGetValue("Resin", out int r) ? r : 0;
                bool sawFull = false;
                for (float t = 0f; ResinInHall() == 0 && t < 120f; t += 0.5f)
                {
                    if ((farmer.Status ?? "") == "$vc_status_delivering_full") sawFull = true;
                    yield return new WaitForSeconds(0.5f);
                }
                Check($"With the Farm's chest full the farmer takes his load to the Town Hall's chest ({ResinInHall()} of {load} resin there, " +
                      $"'Delivering (his chest is full)' seen: {sawFull}, still carrying {farmer.Carry.Count("Resin")})", ResinInHall() > 0 && sawFull);
                for (ItemDrop.ItemData it = chest.GetItem("Stone", -1, true); it != null; it = chest.GetItem("Stone", -1, true)) chest.RemoveItem(it);

                // 15. Only kept food in the colony: a settler whose meal is due eats it all the same (he'd rather than go hungry).
                foreach (ColonyBuilding b in ColonyBuilding.All.Where(b => b != null && b.IsValid && b.ColonyId == hall.ColonyId && b.Storage != null))
                {
                    Inventory items = b.GetComponent<Container>().GetInventory();
                    foreach (ItemDrop.ItemData it in items.GetAllItems().Where(it => it.m_dropPrefab != null && FoodItems.Value(it.m_dropPrefab.name) > 0f).ToList())
                        items.RemoveItem(it);
                }
                hallChest.AddItem(ObjectDB.instance.GetItemPrefab("Raspberry"), 5);   // the Town Hall keeps 10 food: none of it spare
                Citizen eater = carrier ?? Citizen.All.FirstOrDefault(c => c != null && c != farmer && c.IsOwner && c.ColonyId == hall.ColonyId);
                int mealDay = EnvMan.instance.GetDay(ZNet.instance.GetTimeSeconds());
                if (eater != null)
                {
                    eater.ForgetMealsForTest();
                    eater.MissMealForTest();
                    for (float t = 0f; eater.Record.LastMealDay != mealDay && t < 150f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                }
                int berriesLeft = hallStore.Contents().TryGetValue("Raspberry", out int bl) ? bl : 0;
                Check($"With only kept food in the colony a settler whose meal is due eats it ({(eater != null ? eater.CitizenName : "nobody")}: meal day " +
                      $"{(eater != null ? eater.Record.LastMealDay : -1)} of {mealDay}, hungry {(eater != null && eater.Record.Hungry)}, {berriesLeft} of 5 raspberries left)",
                    eater != null && eater.Record.LastMealDay == mealDay && !eater.Record.Hungry && berriesLeft < 5);

                // 16. The Keep in stock tab, on the Farm and the Town Hall; an entry added through the test hook shows.
                Vector3 byFarm = farm.transform.position + forward * 4f;
                byFarm.y = ZoneSystem.instance.GetGroundHeight(byFarm) + 0.2f;
                player.TeleportTo(byFarm, player.transform.rotation, distantTeleport: false);
                for (float t = 0f; Vector3.Distance(player.transform.position, farm.transform.position) > 6f && t < 10f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                Gui.Windows.Hut.Open(farm);
                Gui.Windows.Hut.ShowTabForTest("stock");
                yield return new WaitForSeconds(1f);
                string farmTabEmpty = Gui.Windows.Hut.TextForTest();
                Gui.StockTab.SetForTest(farm, "CarrotSeeds", 20);
                yield return new WaitForSeconds(1.5f);
                string farmTab = Gui.Windows.Hut.TextForTest();
                Gui.Windows.Hut.Close();
                Vector3 byHall = hall.transform.position - forward * 4f;
                byHall.y = ZoneSystem.instance.GetGroundHeight(byHall) + 0.2f;
                // Asked again until it takes: the game refuses a teleport within two seconds of the last (run stock-w1: the
                // Town Hall's window closed at once, 14 m away).
                for (float t = 0f; Vector3.Distance(player.transform.position, hall.transform.position) > 6f && t < 15f; t += 0.5f)
                {
                    player.TeleportTo(byHall, player.transform.rotation, distantTeleport: false);
                    yield return new WaitForSeconds(0.5f);
                }
                float fromHall = Vector3.Distance(player.transform.position, hall.transform.position);
                Gui.Windows.TownHall.Open(hall);
                Gui.Windows.TownHall.ShowTabForTest("stock");
                yield return new WaitForSeconds(1f);
                string hallTab = Gui.Windows.TownHall.TextForTest();
                Gui.Windows.TownHall.Close();
                string seeds = Gui.GrowWindow.ItemName("CarrotSeeds"), anyFood = Localization.instance.Localize("$vc_stock_any_food");
                bool farmEmptyShown = farmTabEmpty.Contains("Keep in stock: 0 of 5");
                bool farmEntryShown = farmTab.Contains("Keep in stock: 1 of 5") && farmTab.Contains(seeds) && farmTab.Contains("/ 20");
                bool hallShown = hallTab.Contains("Keep in stock: 1 of 10") && hallTab.Contains(anyFood);
                int tabAt = farmTab.IndexOf("Keep in stock:");
                string farmPart = tabAt >= 0 ? farmTab.Substring(tabAt) : farmTab;   // past the tab buttons
                Check($"The Keep in stock tab shows on the Farm ('Keep in stock: 0 of 5': {farmEmptyShown}; then {seeds} 20 kept: {farmEntryShown}) and the " +
                      $"Town Hall ({anyFood}: {hallShown}; you {fromHall:0} m from it) (Farm: {Short(farmPart)}; " +
                      $"Town Hall: {Short(hallTab)})",
                    farmEmptyShown && farmEntryShown && hallShown);

                // ---- Lumberjack and forager (0.22) ----
                // 17. The Trees list and trees by buildings: a Lumberjack's Hut behind the player; beech unticked from Fell; a
                // beech, a fir by a player's wall and a fir further off: he leaves the first two standing and fells the third.
                Vector3 lumberAt = origin - forward * 10f;
                lumberHut = DevHelpers.Place(BuildingTypes.LumberjackHut.PrefabName, lumberAt, Quaternion.LookRotation(forward), player.GetPlayerID());
                ConstructionModule lumberSite = lumberHut != null ? lumberHut.GetComponent<ConstructionModule>() : null;
                for (float t = 0f; lumberSite != null && lumberSite.Order == null && t < 15f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                if (lumberSite != null && lumberSite.Order != null) lumberSite.FinishNow();
                for (float t = 0f; lumberSite != null && !lumberSite.IsBuilt && t < 40f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                lumberHut?.GetComponent<Container>()?.GetInventory().AddItem(ObjectDB.instance.GetItemPrefab("AxeFlint"), 1);
                WorkerModule lumberWork = lumberHut != null ? lumberHut.GetComponent<WorkerModule>() : null;
                Citizen lumberjack = null;
                for (float t = 0f; lumberWork != null && lumberjack == null && t < 40f; t += 0.5f)
                {
                    yield return new WaitForSeconds(0.5f);
                    if (lumberWork.FindWorker(out Citizen found, out _) == WorkerModule.WorkerState.Loaded) lumberjack = found;
                }
                LumberjackJob Lumber() => lumberjack != null ? lumberjack.GetComponent<CitizenAI>()?.JobForTest as LumberjackJob : null;
                lumberWork?.RequestSetFell("beech", false);
                // 11 m from the hut, so its own pieces (a player's building too) don't keep them standing (review of 0.22).
                GameObject beech = DevHelpers.Spawn("Beech1", lumberAt + right * 11f);
                GameObject walledFir = DevHelpers.Spawn("FirTree", lumberAt - right * 11f);
                GameObject freeFir = DevHelpers.Spawn("FirTree", lumberAt - right * 14f + forward * 8f);
                // By a field marker: felled. 13 m to the side: 1.4.0's Lumberjack's Hut reaches 5.6 m that way, and at 8 m its
                // own pieces kept the fir standing (the release build's full run).
                GameObject markedFir = DevHelpers.Spawn("FirTree", lumberAt + right * 13f + forward * 6f);
                GameObject wall = null;
                GameObject wallPrefab = ZNetScene.instance.GetPrefab("woodwall");
                if (wallPrefab != null && walledFir != null)
                {
                    Vector3 wallAt = walledFir.transform.position + forward * 1.5f;
                    wallAt.y = ZoneSystem.instance.GetGroundHeight(wallAt);
                    wall = Object.Instantiate(wallPrefab, wallAt, Quaternion.LookRotation(right));
                    wall.GetComponent<Piece>()?.SetCreator(player.GetPlayerID(), default(Splatform.PlatformUserID));   // a player's
                }
                GameObject fieldSign = null;
                if (markerPrefab != null && markedFir != null)
                {
                    Vector3 signAt = markedFir.transform.position + forward * 2f;
                    signAt.y = ZoneSystem.instance.GetGroundHeight(signAt);
                    fieldSign = Object.Instantiate(markerPrefab, signAt, Quaternion.identity);
                    fieldSign.GetComponent<Piece>()?.SetCreator(player.GetPlayerID(), default(Splatform.PlatformUserID));
                }
                foreach (GameObject spawned in new[] { beech, walledFir, freeFir, markedFir, wall, fieldSign }) if (spawned != null) markers.Add(spawned);
                yield return new WaitForSeconds(1f);
                LumberjackJob lumberJob = Lumber();
                bool beechStays = lumberJob != null && beech != null && lumberJob.LeavesStandingForTest(beech);
                bool walledStays = lumberJob != null && walledFir != null && lumberJob.LeavesStandingForTest(walledFir);
                bool freeFelled = lumberJob != null && freeFir != null && !lumberJob.LeavesStandingForTest(freeFir);
                bool markedFelled = lumberJob != null && markedFir != null && fieldSign != null && !lumberJob.LeavesStandingForTest(markedFir);
                bool touchedKept = false, choseStandingBeech = false;
                string chose = "";
                // Watched for a while: a beech target chosen before the untick is dropped at his next look round (3 s).
                for (float t = 0f; t < 60f && (chose.Length == 0 || t < 8f); t += 0.5f)
                {
                    GameObject target = Lumber()?.TargetForTest;
                    if (target != null && (target == beech || target == walledFir)) touchedKept = true;
                    chose = target != null ? Utils.GetPrefabName(target) + (target == freeFir ? " (the far fir)" : "") : chose;
                    // A standing beech only: the stump or log of one he felled before the untick is his to finish (0.27 run).
                    if (t >= 3.5f && target != null && TreeKinds.Of(Utils.GetPrefabName(target)) == "beech"   // after his next look round (a wild beech by the full 1.3 run's spot)
                        && TreeCatalogBuilder.Catalog.KindOf(Utils.GetPrefabName(target)) == ChopKind.Tree) choseStandingBeech = true;
                    yield return new WaitForSeconds(0.5f);
                }
                // The wall gone, the fir by it may be felled (it was the wall that kept it).
                if (wall != null) Remove(wall);
                yield return new WaitForSeconds(0.5f);
                bool wallWasIt = lumberJob != null && walledFir != null && !lumberJob.LeavesStandingForTest(walledFir);
                // His tree once beech is unticked: never a beech (a wild one he chose just before is dropped; 0.22 worker run 1: he kept one).
                Check($"The Trees list and trees by buildings: beech unticked and a fir by a player's wall stay standing, a fir further off is felled " +
                      $"(lumberjack {(lumberjack != null ? lumberjack.CitizenName : "none")}; beech stays {beechStays}, walled fir stays {walledStays}, " +
                      $"far fir felled {freeFelled}, fir by a field marker felled {markedFelled}, the walled fir felled once the wall is gone {wallWasIt}; " +
                      $"he chose '{chose}', touched a kept one {touchedKept}, a standing beech {choseStandingBeech})",
                    lumberjack != null && beechStays && walledStays && freeFelled && markedFelled && wallWasIt && chose.Length > 0 && !touchedKept
                    && !choseStandingBeech);

                // 18. Stop at: 200 Wood in the Town Hall's chest with the hut's limit at 200, he says "Enough wood"; back to no
                // limit, he works again.
                lumberWork?.RequestSetWoodLimit(200);
                GameObject woodPrefab = ObjectDB.instance.GetItemPrefab("Wood");
                for (int i = 0; i < 4; i++) hallChest.AddItem(woodPrefab, 50);
                yield return new WaitForSeconds(0.5f);
                Lumber()?.CountWoodNowForTest();
                bool sawEnough = false;
                for (float t = 0f; !sawEnough && t < 60f; t += 0.5f)   // he finishes his last tree first (0.26)
                {
                    sawEnough = lumberjack != null && lumberjack.Status == "$vc_status_enough_wood";
                    yield return new WaitForSeconds(0.5f);
                }
                int limitSaved = lumberWork != null ? lumberWork.WoodLimit : -1;
                lumberWork?.RequestSetWoodLimit(0);
                for (ItemDrop.ItemData it = hallChest.GetItem("Wood", -1, true); it != null; it = hallChest.GetItem("Wood", -1, true)) hallChest.RemoveItem(it);
                yield return new WaitForSeconds(0.5f);
                Lumber()?.CountWoodNowForTest();
                bool backToWork = false;
                for (float t = 0f; !backToWork && t < 20f; t += 0.5f)
                {
                    yield return new WaitForSeconds(0.5f);
                    backToWork = lumberjack != null && lumberjack.Status != "$vc_status_enough_wood";
                }
                Check($"Stop at 200: with 200 Wood in the colony's chests the lumberjack says 'Enough wood', with no limit he works again " +
                      $"(limit saved {limitSaved}, enough seen {sawEnough}, back to work {backToWork}: '{Localization.instance.Localize(lumberjack != null ? lumberjack.Status ?? "" : "")}')",
                    limitSaved == 200 && sawEnough && backToWork);

                // 19. Skills: the farmer learned Farming from his planting; the Farm's window says so; the learning rate is a Colony setting.
                Vector3 byFarm2 = farm.transform.position + forward * 4f;
                byFarm2.y = ZoneSystem.instance.GetGroundHeight(byFarm2) + 0.2f;
                for (float t = 0f; Vector3.Distance(player.transform.position, farm.transform.position) > 6f && t < 15f; t += 0.5f)
                {
                    player.TeleportTo(byFarm2, player.transform.rotation, distantTeleport: false);
                    yield return new WaitForSeconds(0.5f);
                }
                Gui.Windows.Hut.Open(farm);
                Gui.Windows.Hut.ShowTabForTest("hut");
                yield return new WaitForSeconds(1f);
                string farmHut = Gui.Windows.Hut.TextForTest();
                Gui.Windows.Hut.Close();
                string farmingWord = Localization.instance.Localize("$vc_skill_farming");
                CitizenRecord fr = farmer.Record;
                string section = ColonyConfig.LearningRate.Definition.Section;
                Check($"The farmer learns Farming as he works (Farming {fr.Farming:0}, practice {fr.FarmingXp:0.00}); the Farm's window shows it " +
                      $"({farmHut.Contains(farmingWord)}); the learning rate is in the {section} settings",
                    (fr.Farming > 0f || fr.FarmingXp > 0f) && farmHut.Contains(farmingWord) && section == ConfigNames.ColonySection);

                // ---- The builder (0.23) ----
                // 20. Hand over: a Builder's Hut hires a builder; a Forager's Hut is ordered; with the colony's chests emptied of
                // what it needs and 40 Wood in your bag, Hand over on the Builder's Hut window moves just what it still needs.
                Vector3 builderAt = origin + right * 12f;
                builderHut = DevHelpers.Place(BuildingTypes.BuilderHut.PrefabName, builderAt, Quaternion.LookRotation(-right), player.GetPlayerID());
                ConstructionModule builderSite = builderHut != null ? builderHut.GetComponent<ConstructionModule>() : null;
                for (float t = 0f; builderSite != null && builderSite.Order == null && t < 15f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                if (builderSite != null && builderSite.Order != null) builderSite.FinishNow();
                for (float t = 0f; builderSite != null && !builderSite.IsBuilt && t < 40f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                WorkerModule builderWork = builderHut != null ? builderHut.GetComponent<WorkerModule>() : null;
                Citizen builder = null;
                for (float t = 0f; builderWork != null && builder == null && t < 40f; t += 0.5f)
                {
                    yield return new WaitForSeconds(0.5f);
                    if (builderWork.FindWorker(out Citizen found, out _) == WorkerModule.WorkerState.Loaded) builder = found;
                }
                foragerHut = DevHelpers.Place(BuildingTypes.ForagerHut.PrefabName, origin - right * 12f, Quaternion.LookRotation(right), player.GetPlayerID());
                ConstructionModule foragerSite = foragerHut != null ? foragerHut.GetComponent<ConstructionModule>() : null;
                for (float t = 0f; foragerSite != null && foragerSite.Order == null && t < 15f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                Dictionary<string, int> needs = foragerSite != null ? foragerSite.RemainingNeeds() : new Dictionary<string, int>();
                // The colony's chests emptied of what it needs, so there is something to hand over.
                foreach (ColonyBuilding b in ColonyBuilding.All.Where(b => b != null && b.IsValid && b.ColonyId == hall.ColonyId && b.Storage != null))
                {
                    Inventory items = b.GetComponent<Container>().GetInventory();
                    foreach (ItemDrop.ItemData it in items.GetAllItems().Where(it => it.m_dropPrefab != null && needs.ContainsKey(it.m_dropPrefab.name)).ToList())
                        items.RemoveItem(it);
                }
                Inventory bag = player.GetInventory();
                int WoodIn(Inventory inv) => inv.GetAllItems().Where(it => it.m_dropPrefab != null && it.m_dropPrefab.name == "Wood").Sum(it => it.m_stack);
                bag.AddItem(woodPrefab, 40);
                Vector3 byBuilder = builderAt - right * 4f;
                byBuilder.y = ZoneSystem.instance.GetGroundHeight(byBuilder) + 0.2f;
                for (float t = 0f; builderHut != null && Vector3.Distance(player.transform.position, builderHut.transform.position) > 6f && t < 15f; t += 0.5f)
                {
                    player.TeleportTo(byBuilder, player.transform.rotation, distantTeleport: false);
                    yield return new WaitForSeconds(0.5f);
                }
                Inventory builderChest = builderHut != null ? builderHut.GetComponent<Container>().GetInventory() : null;
                int bagBefore = WoodIn(bag), chestBefore = builderChest != null ? WoodIn(builderChest) : 0;
                // What the builder carries for the order counts as there (0.26), as Hand over counts it. Hand over serves the
                // builder's current order: once the camp's design ships (1.4.0) that can be the camp's, not the Forager's Hut.
                ConstructionModule handSite = (builderHut != null && builder != null ? ConstructionModule.CurrentFor(builderHut, builder.Id) : null) ?? foragerSite;
                int expected = Mathf.Min(bagBefore, NeedsTally.Shortfall(handSite != null ? handSite.RemainingNeeds() : needs,
                    HandOver.Available(BuilderTools.StoresForBuilders(hall.ColonyId, handSite != null ? handSite.transform.position : origin),
                        HandOverFromBag.BuilderCarries(handSite))).TryGetValue("Wood", out int short1) ? short1 : 0);
                string handed = "";
                if (builderHut != null)
                {
                    Gui.Windows.Hut.Open(builderHut);
                    handed = Gui.Windows.Hut.HandOver();
                    Gui.Windows.Hut.Close();
                }
                int bagGave = bagBefore - WoodIn(bag), chestGot = (builderChest != null ? WoodIn(builderChest) : 0) - chestBefore;
                Check($"Hand over moves what the order still needs from your bag into the Builder's Hut's chest (builder {(builder != null ? builder.CitizenName : "none")}; " +
                      $"wood expected {expected}, from the bag {bagGave}, into the chest {chestGot}; '{handed}')",
                    builder != null && expected > 0 && bagGave == expected && chestGot == expected && handed.Contains(Localization.instance.Localize("$vc_handover_done").Split('{')[0].Trim()));
                if (40 - bagGave > 0) bag.RemoveItem("$item_wood", 40 - bagGave);   // what the check gave and wasn't handed over (not the tester's own)

                // 21. Take down: the Forager's Hut finished (5 Flint in its chest); a stranger's take-down is refused, yours is
                // carried out by the builder: the hut is gone, the Flint is in the Town Hall's chest, the saga tells it.
                foragerSite?.FinishNow();
                for (float t = 0f; foragerSite != null && !foragerSite.IsBuilt && t < 30f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                // Its forager (0.24: checked jobless after), and its building's materials (none lie there after).
                WorkerModule foragerWork = foragerHut != null ? foragerHut.GetComponent<WorkerModule>() : null;
                Citizen forager = null;
                for (float t = 0f; foragerWork != null && forager == null && t < 30f; t += 0.5f)
                {
                    if (foragerWork.FindWorker(out Citizen f, out _) == WorkerModule.WorkerState.Loaded) forager = f;
                    else yield return new WaitForSeconds(0.5f);
                }
                Vector3 foragerSpot = foragerHut != null ? foragerHut.transform.position : origin;
                var materials = new HashSet<string>(foragerSite != null
                    ? foragerSite.Plan.Where(pl => pl.IsValid).SelectMany(pl => PiecePlacer.Needs(pl.Prefab).Keys) : Enumerable.Empty<string>());
                WorldScan.InvalidateDrops();
                var dropsBefore = new HashSet<ItemDrop>(WorldScan.Drops().Where(d => d != null && Vector3.Distance(d.transform.position, foragerSpot) < 8f));   // not ours to count
                foragerHut?.GetComponent<Container>()?.GetInventory().AddItem(ObjectDB.instance.GetItemPrefab("Flint"), 5);
                int FlintIn(Inventory inv) => inv.GetAllItems().Where(it => it.m_dropPrefab != null && it.m_dropPrefab.name == "Flint").Sum(it => it.m_stack);
                int hallFlintBefore = FlintIn(hallChest);
                foragerSite?.TakeDownForTest(424242L);   // nobody of this colony
                yield return null;
                bool takeDownRefused = foragerSite != null && !foragerSite.IsTakingDown;
                // Cancelled (review of 0.23): the finished order comes back; then asked again for good.
                foragerSite?.RequestTakeDown();
                yield return new WaitForSeconds(0.5f);
                foragerSite?.RequestCancelTakeDown();
                yield return new WaitForSeconds(0.5f);
                bool cancelled = foragerSite != null && !foragerSite.IsTakingDown && foragerSite.Order != null && foragerSite.Order.State == BuildState.Done;
                foragerSite?.RequestTakeDown();
                yield return new WaitForSeconds(0.5f);
                bool ordered = foragerSite != null && foragerSite.IsTakingDown;
                // Time for every piece (1.4.0's Forager's Hut has 50; the release build's full run took more than 240 s).
                float takeDownLimit = Mathf.Max(240f, (foragerSite != null ? foragerSite.Plan.Count : 0) * 8f);
                for (float t = 0f; foragerHut != null && t < takeDownLimit; t += 1f) yield return new WaitForSeconds(1f);
                bool gone = foragerHut == null;
                int flintGot = FlintIn(hallChest) - hallFlintBefore;
                TownHallModule hallModule = hall.GetComponent<TownHallModule>();
                bool told = hallModule != null && hallModule.Saga.Page(0, 12).Any(e => e.Kind == "takendown");
                yield return new WaitForSeconds(2f);   // his letting go reaches him
                bool foragerLetGo = forager != null && string.IsNullOrEmpty(forager.Record.Job);
                WorldScan.InvalidateDrops();
                int left = WorldScan.Drops().Count(d => d != null && !d.IsPiece() && !dropsBefore.Contains(d) && Vector3.Distance(d.transform.position, foragerSpot) < 8f
                                                         && materials.Contains(Utils.GetPrefabName(d.gameObject)));
                Check($"Take down: a stranger is refused ({takeDownRefused}), yours can be cancelled ({cancelled}) and is ordered ({ordered}); the builder " +
                      $"takes it down (gone {gone}), its chest's 5 Flint reach the Town Hall ({flintGot}), the saga tells it ({told}), its forager " +
                      $"{(forager != null ? forager.CitizenName : "none")} is jobless ({foragerLetGo}), materials left lying there: {left}",
                    takeDownRefused && cancelled && ordered && gone && flintGot == 5 && told && foragerLetGo && left == 0);
            }
            finally
            {
                EnvMan.instance.m_debugTimeOfDay = false;
                ColonyConfig.NewcomersWalkIn.Value = walkInWas;
            }
            Log.Event($"vc_workertest: done — {s_pass} passed, {s_fail} failed");
            player.Message(MessageHud.MessageType.Center, $"vc_workertest: {s_pass} passed, {s_fail} failed");
            if (keep) yield break;
            foreach (Citizen c in Citizen.All.Where(c => c != null && !before.Contains(c.Id)).ToList()) Remove(c.gameObject);
            foreach (GameObject m in markers.Where(m => m != null)) Remove(m);
            if (farm != null)
            {
                Vector3 at = farm.transform.position;
                foreach (Plant plant in WorldScan.Plants().Where(pl => pl != null && Utils.DistanceXZ(pl.transform.position, at) < 25f).ToList()) Remove(plant.gameObject);
                foreach (Pickable pick in WorldScan.Pickables().Where(pk => pk != null && Utils.GetPrefabName(pk.gameObject) == "Pickable_Carrot" && Utils.DistanceXZ(pk.transform.position, at) < 25f).ToList())
                    Remove(pick.gameObject);
                Remove(farm.gameObject);
            }
            if (lumberHut != null) Remove(lumberHut.gameObject);
            if (builderHut != null) Remove(builderHut.gameObject);
            if (foragerHut != null) Remove(foragerHut.gameObject);
            if (hall != null) Remove(hall.gameObject);
            Log.Event("vc_workertest: cleaned up");
        }

        private static void Remove(GameObject go)
        {
            ZNetView nview = go.GetComponent<ZNetView>();
            if (nview != null && nview.IsValid() && !nview.IsOwner()) nview.ClaimOwnership();
            ZNetScene.instance.Destroy(go);
        }

        private static string Short(string text) => text.Replace("\n", " | ").Substring(0, Mathf.Min(160, text.Length));

        private static void Check(string what, bool ok)
        {
            if (ok) s_pass++;
            else s_fail++;
            Log.Event($"vc_workertest: {(ok ? "PASS" : "FAIL")} {what}");
        }
    }
}
