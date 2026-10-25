using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Jotunn.Entities;
using UnityEngine;
using VikingColonies.Buildings;
using VikingColonies.Citizens;
using VikingColonies.Config;
using VikingColonies.AI;
using VikingColonies.Construction;
using VikingColonies.Core;
using VikingColonies.Gui;
using VikingColonies.Jobs;
using static VikingColonies.Construction.CoreVectors;

namespace VikingColonies.DevTools
{
    /// <summary>vc_storehousetest (test worlds): the Storehouse and its store (1.5) checked in the game in a few minutes.</summary>
    internal sealed class StorehouseTestCommand : ConsoleCommand
    {
        public override string Name => "vc_storehousetest";
        public override string Help => "VikingColonies (test worlds): place a Town Hall, a Builder's Hut and a Storehouse here and check the storehouse of " +
                                       "1.5 (one per colony, which chests join its store, a chest broken inside). Removes what it made.";
        public override bool IsCheat => true;

        public override void Run(string[] args)
        {
            Player p = Player.m_localPlayer;
            if (p == null || ZNet.instance == null || !TestWorlds.IsTestWorld(ZNet.instance.GetWorldName()))
            {
                BuildCommand.Print("vc_storehousetest: test worlds only");
                return;
            }
            Plugin.Instance.StartCoroutine(StorehouseTest.Run(p));
        }
    }

    internal static class StorehouseTest
    {
        private static int s_pass, s_fail;

        public static IEnumerator Run(Player player)
        {
            s_pass = s_fail = 0;
            Log.Event("vc_storehousetest: starting");
            if (!player.InGodMode()) player.SetGodMode(true);
            EnvMan.instance.m_debugTimeOfDay = true;
            EnvMan.instance.m_debugTime = 0.4f;   // morning: no meals due, no evening
            Vector3 origin = player.transform.position, forward = player.transform.forward, right = player.transform.right;
            forward.y = right.y = 0f;
            forward.Normalize();
            right.Normalize();
            Quaternion facing = Quaternion.LookRotation(forward);
            var made = new List<GameObject>();
            bool walkInWas = ColonyConfig.NewcomersWalkIn.Value;
            try
            {
                ColonyConfig.NewcomersWalkIn.Value = false;
                DevHelpers.ClearMonsters(origin, 80f, animalsToo: true);
                ColonyBuilding hall = DevHelpers.Place(BuildingTypes.TownHall.PrefabName, origin + forward * 8f, facing, player.GetPlayerID());
                DevHelpers.KeepNothing(hall);
                TownHallModule townHall = hall != null ? hall.GetComponent<TownHallModule>() : null;
                for (float t = 0f; townHall != null && !townHall.Founded && t < 20f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                if (hall == null || townHall == null || !townHall.Founded)
                {
                    Check($"A Town Hall is founded (hall {hall != null})", false);
                    yield break;
                }
                Vector3 hallAt = hall.transform.position;
                // The spots judged on the ground (the first run judged at the hall's height: "can't stand in the air"); the
                // storehouse itself is placed 0.75 m up below, as the hammer starts it.
                Vector3 storeAt = OnGround(hallAt + forward * 16f), builderAt = OnGround(hallAt + right * 16f), secondAt = OnGround(hallAt - right * 16f);

                // 1. A storehouse needs a Builder's Hut, and a colony has one.
                SiteVerdict noBuilder = DevHelpers.Judge(BuildingTypes.Storehouse, storeAt, facing);
                ColonyBuilding builderHut = DevHelpers.Place(BuildingTypes.BuilderHut.PrefabName, builderAt, facing, player.GetPlayerID());
                for (float t = 0f; builderHut != null && builderHut.ColonyId != hall.ColonyId && t < 15f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                SiteVerdict withBuilder = DevHelpers.Judge(BuildingTypes.Storehouse, storeAt, facing);
                ColonyBuilding store = DevHelpers.Place(BuildingTypes.Storehouse.PrefabName, storeAt, facing, player.GetPlayerID(),
                    PlacementOffset.StartFor(BuildingTypes.Storehouse.PrefabName));   // as the hammer starts it: on its posts
                for (float t = 0f; store != null && store.ColonyId != hall.ColonyId && t < 15f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                // Its couriers are hired by hand in their own checks (1.5, part 2): none meanwhile, to keep the store as the checks left it.
                store?.GetComponent<WorkerModule>()?.RequestSetAutoHire(false);
                SiteVerdict second = DevHelpers.Judge(BuildingTypes.Storehouse, secondAt, facing);
                Check($"A storehouse needs a Builder's Hut, and a colony has one (no Builder's Hut: {noBuilder?.Red}; with one: {withBuilder?.Red}; " +
                      $"a second: {second?.Red})",
                    noBuilder != null && noBuilder.Red == RedReason.NeedsBuilderHut && withBuilder != null && withBuilder.Red == RedReason.None
                    && second != null && second.Red == RedReason.OnlyOneStorehouse);
                if (store == null || store.Storehouse == null)
                {
                    Check("A storehouse is placed with its store", false);
                    yield break;
                }
                yield return Finish(store);
                StorehouseModule storehouse = store.Storehouse;
                ConstructionModule site = store.GetComponent<ConstructionModule>();
                Footprint room = site.RoomFootprint;
                float floor = store.transform.position.y;

                // 1b. Its hand-over: a settler climbs the stair from the door's landing to the storehouse's own chest.
                yield return Climb(store, site, store.transform.position, "its own chest");

                // 2. A plain chest placed inside joins the store within a scan and can be handed over; a personal one, or one
                //    outside its walls, doesn't.
                Vector3 middle = new Vector3(room.Centre.X, floor, room.Centre.Z);
                Vector3 outside = U(room.ToWorld(room.HalfX + 1.5f, 0f));
                outside.y = ZoneSystem.instance.GetGroundHeight(outside);
                yield return new WaitForSeconds(StorehouseRules.ScanSeconds + 1f);
                int designChests = store.Stores.Count;   // its own chest and its design's (the real designs bring some: 4 at level 1)
                GameObject plain = PlaceChest("piece_chest_wood", middle + forward * 0.6f, facing, made);
                GameObject personal = PlaceChest("piece_chest_private", middle - forward * 0.6f, facing, made);
                GameObject beyond = PlaceChest("piece_chest_wood", outside, facing, made);
                yield return new WaitForSeconds(StorehouseRules.ScanSeconds + 1f);
                StorageModule plainStore = plain != null ? plain.GetComponent<StorageModule>() : null;
                bool plainIn = plainStore != null && storehouse.Chests.Contains(plainStore);
                bool personalIn = personal != null && storehouse.Chests.Any(c => c != null && c.gameObject == personal);
                bool beyondIn = beyond != null && storehouse.Chests.Any(c => c != null && c.gameObject == beyond);
                Check($"A plain chest inside joins the store with a hand-over, a personal one or one outside doesn't (inside {plainIn}, " +
                      $"hand-over {plainStore != null && plainStore.Claim() != StorageModule.Access.Busy}, personal {personalIn}, outside {beyondIn}, " +
                      $"{store.Stores.Count} chests in its stores, {designChests} before)",
                    plainIn && plainStore.Claim() != StorageModule.Access.Busy && !personalIn && !beyondIn && store.Stores.Count == designChests + 1);

                // 3. A chest broken inside leaves the store, with nothing destroyed held.
                bool threw = false;
                Remove(plain);
                yield return null;
                int right1 = 0;
                try
                {
                    right1 = store.Stores.Count(s => s != null);
                }
                catch (System.Exception e)
                {
                    threw = true;
                    Log.Warn($"vc_storehousetest: Stores after a chest broke: {e}");
                }
                yield return new WaitForSeconds(StorehouseRules.ScanSeconds + 1f);
                int after = store.Stores.Count;
                Check($"A chest broken inside leaves the store at once, with no error (stores right after {right1}, after a scan {after}, error {threw})",
                    !threw && right1 == designChests && after == designChests && store.Stores.All(s => s != null));

                // 4. The builder counts what the store holds: 30 Wood in a chest inside.
                // Where neither this level nor the next has a piece (the stand-in design's level 2 has a wall in the middle).
                GameObject inside = PlaceChest("piece_chest_wood", ClearSpot(site, room, floor, facing, middle + forward * 0.6f), facing, made);
                yield return new WaitForSeconds(StorehouseRules.ScanSeconds + 1f);
                StorageModule insideStore = inside != null ? inside.GetComponent<StorageModule>() : null;
                Inventory insideItems = inside != null ? inside.GetComponent<Container>().GetInventory() : null;
                int woodBefore = Count(BuilderTools.StoresForBuilders(hall.ColonyId, hallAt), "Wood");
                insideItems?.AddItem(ObjectDB.instance.GetItemPrefab("Wood"), 30);
                int woodAfter = Count(BuilderTools.StoresForBuilders(hall.ColonyId, hallAt), "Wood");
                Check($"The builder counts 30 Wood put in a chest inside the storehouse (in the store {insideStore != null && storehouse.Chests.Contains(insideStore)}, " +
                      $"wood {woodBefore} -> {woodAfter})", insideStore != null && storehouse.Chests.Contains(insideStore) && woodAfter - woodBefore == 30);

                // 5. A full worker's load: the storehouse before the Town Hall, its own chest full, so into a chest inside.
                Inventory own = store.GetComponent<Container>().GetInventory();
                GameObject stone = ObjectDB.instance.GetItemPrefab("Stone");
                for (int guard = 0; own.HaveEmptySlot() && guard < 200; guard++) own.AddItem(stone, 50);
                List<Citizen> settlers = Citizen.All.Where(c => c != null && c.ColonyId == hall.ColonyId).ToList();
                Citizen worker = settlers.FirstOrDefault();
                var run = new FullChestRun();
                int resinIn = -1, resinLeft = -1;
                if (worker != null)
                {
                    CitizenAI ai = worker.GetComponent<CitizenAI>();
                    ai?.PauseForTest(20f);
                    Vector3 stand = store.transform.position + (middle - store.transform.position).normalized * 1.2f;
                    stand.y = floor + 0.1f;
                    worker.transform.position = stand;
                    Rigidbody body = worker.GetComponent<Rigidbody>();
                    if (body != null) body.position = stand;   // the first run: the transform alone stayed 14 m off
                    yield return new WaitForSeconds(0.5f);
                    worker.Carry.Add("Resin", 5);
                    run.Choose(worker, null);
                    string chosen = run.To;
                    for (float t = 0f; run.Active && t < 10f; t += Time.deltaTime)
                    {
                        run.Tick(worker, ai, null);
                        yield return null;
                    }
                    resinIn = storehouse.Chests.Sum(c => CountIn(c.GetComponent<Container>().GetInventory(), "Resin"));   // any chest inside
                    resinLeft = worker.Carry.Count("Resin");
                    float away = Utils.DistanceXZ(worker.transform.position, store.transform.position);
                    Check($"A full worker takes the load to the storehouse before the Town Hall, into a chest inside when its own is full (chose '{chosen}', " +
                          $"the storehouse '{store.Id}', {away:0.0} m from its chest, resin inside {resinIn}, left in hand {resinLeft})",
                        chosen == store.Id && resinIn == 5 && resinLeft == 0);
                }
                else Check("A full worker's load (no settler to test with)", false);
                own.RemoveAll();

                // 6. A meal from the store: the only food in the colony lies in a chest inside the storehouse.
                foreach (ColonyBuilding b in ColonyBuilding.All.Where(b => b != null && b.IsValid && b.ColonyId == hall.ColonyId && b.Storage != null))
                {
                    Inventory items = b.GetComponent<Container>().GetInventory();
                    foreach (ItemDrop.ItemData item in items.GetAllItems().Where(i => FoodItems.Value(i.m_dropPrefab != null ? i.m_dropPrefab.name : "") > 0f).ToList())
                        items.RemoveItem(item);
                }
                insideItems?.AddItem(ObjectDB.instance.GetItemPrefab("Raspberry"), 3);
                Citizen eater = settlers.Count > 1 ? settlers[1] : worker;
                int day = EnvMan.instance.GetDay(ZNet.instance.GetTimeSeconds());
                if (eater != null)
                {
                    eater.ForgetMealsForTest();
                    eater.MissMealForTest();
                    for (float t = 0f; t < 150f && eater.Record.LastMealDay != day; t += 0.5f) yield return new WaitForSeconds(0.5f);
                }
                int berries = CountIn(insideItems, "Raspberry");
                Check($"A hungry settler eats from the store when the only food lies in a chest inside it ({eater?.CitizenName}: meal day {eater?.Record.LastMealDay} " +
                      $"of {day}, ate {eater?.LastMeal}, raspberries inside 3 -> {berries})",
                    eater != null && eater.Record.LastMealDay == day && eater.LastMeal == "Raspberry" && berries == 2);

                // 7. Taking it down waits for the chests inside to be empty, and says what's left.
                bool refused = !site.MayTakeDown && site.MayTakeDownOnceEmpty;
                string words = string.Format(Localization.instance.Localize("$vc_takedown_store_full"), Localization.instance.Localize(site.StoreHolds()));
                foreach (StorageModule chest in storehouse.Chests) chest.GetComponent<Container>().GetInventory().RemoveAll();   // the design's too
                bool allowed = site.MayTakeDown;
                string wood = Localization.instance.Localize("$item_wood");
                Check($"Taking a storehouse down waits for its chests inside to be empty (refused {refused}, '{words}', then allowed {allowed})",
                    refused && words.Contains(wood) && allowed);

                // 8. The Stock tab: the item with the most first, and where most of it lies.
                insideItems?.AddItem(ObjectDB.instance.GetItemPrefab("Stone"), 50);
                insideItems?.AddItem(ObjectDB.instance.GetItemPrefab("Stone"), 50);
                insideItems?.AddItem(ObjectDB.instance.GetItemPrefab("Stone"), 50);
                List<StockLine> lines = ColonyStockTab.Lines(hall.ColonyId);
                string first = lines.Count > 0 ? ColonyStockTab.Line(lines[0]) : "";
                string storeName = Localization.instance.Localize(BuildingTypes.Storehouse.NameToken);
                Check($"The Stock tab's first line names the item with the most and where most lies ('{first}', {lines.Count} lines)",
                    lines.Count > 0 && lines[0].Item == "Stone" && lines[0].MostAt == storeName && first.Contains(storeName));
                insideItems?.RemoveAll();

                // 9. Its review: a full search buffer still finds every chest inside (a big storehouse has many pieces).
                StorehouseModule.ShrinkBufferForTest(8);
                storehouse.Rescan();
                bool foundWithTiny = insideStore != null && storehouse.Chests.Contains(insideStore);
                StorehouseModule.ShrinkBufferForTest(256);
                storehouse.Rescan();
                Check($"A storehouse finds its chests however many pieces stand in it (search buffer of 8: chest found {foundWithTiny})", foundWithTiny);
                var watch = System.Diagnostics.Stopwatch.StartNew();   // the plan's measure of the scan's cost (it runs every few seconds)
                for (int i = 0; i < 20; i++) storehouse.Rescan();
                watch.Stop();
                Log.Event($"vc_storehousetest: one look for its chests takes {watch.Elapsed.TotalMilliseconds / 20:0.000} ms, every {StorehouseRules.ScanSeconds:0} s");

                // 10. Its review: a second storehouse in the colony (two players placing at once) doesn't stop the first's upgrades.
                ColonyBuilding extra = DevHelpers.Place(BuildingTypes.Storehouse.PrefabName, secondAt, facing, player.GetPlayerID());
                for (float t = 0f; extra != null && extra.ColonyId != hall.ColonyId && t < 15f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                UpgradePreview withTwo = site.PreviewUpgrade();
                bool oneOnly = withTwo != null && withTwo.Findings != null && withTwo.Findings.Red.Contains(RedReason.OnlyOneStorehouse);
                if (extra != null) Remove(extra.gameObject);
                Check($"A colony's second storehouse doesn't stop the first one's upgrade (second placed {extra != null}, its upgrade red " +
                      $"[{(withTwo?.Findings != null ? string.Join(", ", withTwo.Findings.Red) : "no preview")}])", extra != null && withTwo != null && !oneOnly);

                // 11. Its review: the owner says so when it refuses a take-down for full chests (the window may have seen them empty).
                insideItems?.AddItem(stone, 10);
                int refusalsWas = ConstructionModule.StoreFullRefusalsForTest;
                site.RequestTakeDown();
                yield return new WaitForSeconds(1f);
                Check($"A take-down asked for while a chest inside holds something is refused with words (refusals {refusalsWas} -> " +
                      $"{ConstructionModule.StoreFullRefusalsForTest}, taking down {site.IsTakingDown})",
                    ConstructionModule.StoreFullRefusalsForTest == refusalsWas + 1 && !site.IsTakingDown);

                // 12. Its review: a player's chest inside doesn't block the storehouse's upgrade, and keeps its things through it.
                //     (The test's chest outside the walls goes first: on the site, it is in the way as any building is.)
                Remove(beyond);
                yield return null;
                UpgradePreview withChest = site.PreviewUpgrade();
                string inTheWay = SiteSurvey.LastBlockersForTest;
                bool blocked = withChest != null && withChest.Findings != null && withChest.Findings.Red.Contains(RedReason.OtherBuilding);
                int levelWas = store.Level;
                site.RequestUpgradeNow();
                for (float t = 0f; store.Level == levelWas && t < 60f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                yield return new WaitForSeconds(3f);   // the new pieces' support settles
                int stoneKept = CountIn(insideItems, "Stone");
                Check($"A player's chest inside doesn't block the storehouse's upgrade and keeps its things (blocked {blocked}: [{inTheWay}], " +
                      $"level {levelWas} -> {store.Level}, chest standing {inside != null}, personal one standing {personal != null}, stone in it {stoneKept} of 10)",
                    !blocked && store.Level == levelWas + 1 && inside != null && personal != null && stoneKept == 10);
                insideItems?.RemoveAll();

                // 13-21. Couriers (1.5, the storehouse's part 2).
                yield return Couriers(player, hall, store, room, floor, facing, forward, right, made);
            }
            finally
            {
                ColonyConfig.NewcomersWalkIn.Value = walkInWas;
                EnvMan.instance.m_debugTimeOfDay = false;
                foreach (GameObject go in made.Where(g => g != null)) Remove(go);
                Log.Event($"vc_storehousetest: removed what it made: {DevHelpers.WipeColonies(origin, 60f)}");
                Log.Event($"vc_storehousetest: done — {s_pass} passed, {s_fail} failed");
                Player.m_localPlayer?.Message(MessageHud.MessageType.Center, $"vc_storehousetest: {s_pass} passed, {s_fail} failed");
            }
        }

        /// <summary>
        /// The couriers' checks: hired by hand, one at levels 1-2 and two at 3; free-time carrying hands over; a kept item
        /// delivered; a lumberjack's surplus picked up; a Farm's seeds left; the drop-off; the builder's order; never from the
        /// Town Hall; a target gone mid-delivery; sorting. Then part 3's: the switch, the Needs tab, the emptying take-down.
        /// </summary>
        private static IEnumerator Couriers(Player player, ColonyBuilding hall, ColonyBuilding store, Footprint room, float floor, Quaternion facing,
            Vector3 forward, Vector3 right, List<GameObject> made)
        {
            WorkerModule worker = store.GetComponent<WorkerModule>();
            CourierDispatch dispatch = store.GetComponent<CourierDispatch>();
            StorehouseModule storehouse = store.Storehouse;
            ConstructionModule site = store.GetComponent<ConstructionModule>();
            string colony = hall.ColonyId;
            Vector3 hallAt = hall.transform.position;
            GameObject Prefab(string name) => ObjectDB.instance.GetItemPrefab(name);
            // The game's AddItem puts one stack at most (the second run: "60 Feathers" were 50): stack by stack.
            void Add(Inventory into, string name, int count)
            {
                int stack = Mathf.Max(1, Prefab(name).GetComponent<ItemDrop>().m_itemData.m_shared.m_maxStackSize);
                for (int left = count; left > 0; left -= stack) into.AddItem(Prefab(name), Mathf.Min(stack, left));
            }
            int InStore(string item) => store.Stores.Sum(s => s.Contents().TryGetValue(item, out int n) ? n : 0);
            int In(ColonyBuilding b, string item) => b != null && b.Storage != null && b.Storage.Contents().TryGetValue(item, out int n) ? n : 0;
            bool everFromHall = false;
            IEnumerator Watch(float seconds, System.Func<bool> done)
            {
                for (float t = 0f; t < seconds && !done(); t += 0.5f)
                {
                    if (dispatch.PlanForTest.Any(task => task.From == hall.Id)) everFromHall = true;
                    yield return new WaitForSeconds(0.5f);
                }
            }

            // Every colony chest empty of what the checks count, so only the couriers move it.
            foreach (ColonyBuilding b in ColonyBuilding.All.Where(b => b != null && b.IsValid && b.ColonyId == colony))
                foreach (StorageModule chest in b.Stores)
                    chest.GetComponent<Container>().GetInventory().RemoveAll();

            // 13. One courier at level 2, hired; free-time carrying hands over to it (Review Focus 4).
            bool carriedBefore = CourierRules.FreeTimeCarries(AI.FreeTimeRoutine.HasCourier(colony));
            int places = worker.Capacity;
            worker.RequestHire();
            Citizen courier = null;
            for (float t = 0f; courier == null && t < 15f; t += 0.5f)
            {
                yield return new WaitForSeconds(0.5f);
                if (worker.FindWorker(out Citizen found, out _) == WorkerModule.WorkerState.Loaded) courier = found;
            }
            bool carriedAfter = CourierRules.FreeTimeCarries(AI.FreeTimeRoutine.HasCourier(colony));
            Check($"A level-{store.Level} storehouse hires one courier, and free time stops carrying once it has one (places {places}, hired " +
                  $"{courier?.CitizenName ?? "nobody"}, free time carries before {carriedBefore}, after {carriedAfter})",
                places == 1 && courier != null && carriedBefore && !carriedAfter);
            if (courier == null) yield break;
            CourierJob Job() => courier.GetComponent<CitizenAI>()?.JobForTest as CourierJob;

            // 14. A kept item delivered: the Town Hall's food for newcomers, food only in the storehouse (Review Focus 1: its task saved).
            StockTab.SetForTest(hall, StockList.AnyFood, 10);
            storehouse.Chests.First().GetComponent<Container>().GetInventory().AddItem(Prefab("Raspberry"), 20);
            bool taskSaved = false;
            for (float t = 0f; t < 150f && FoodIn(hall) < 10; t += 0.5f)
            {
                if (dispatch.TaskFor(courier.Id) != null) taskSaved = true;
                yield return new WaitForSeconds(0.5f);
            }
            Check($"A courier fills the Town Hall's food for newcomers from the storehouse (food in the hall {FoodIn(hall)} of 10, its task saved " +
                  $"on the storehouse {taskSaved})", FoodIn(hall) >= 10 && taskSaved);

            // 15. A lumberjack's surplus picked up: 60 Feathers in a Lumberjack's Hut that keeps none (nothing anyone needs: the
            //     first run's Wood went, rightly, to the builder's order).
            ColonyBuilding lumber = DevHelpers.Place(BuildingTypes.LumberjackHut.PrefabName, OnGround(hallAt - forward * 16f), facing, player.GetPlayerID());
            for (float t = 0f; lumber != null && lumber.ColonyId != colony && t < 15f; t += 0.5f) yield return new WaitForSeconds(0.5f);
            lumber?.GetComponent<WorkerModule>()?.RequestSetAutoHire(false);
            DevHelpers.KeepNothing(lumber);
            if (lumber != null) Add(lumber.Storage.GetComponent<Container>().GetInventory(), "Feathers", 60);
            yield return Watch(150f, () => InStore("Feathers") == 60);
            Check($"A courier picks up a lumberjack's surplus (feathers in the hut 60 -> {In(lumber, "Feathers")}, in the storehouse {InStore("Feathers")})",
                lumber != null && In(lumber, "Feathers") == 0 && InStore("Feathers") == 60);

            // 16. A Farm's seeds stay: its field's seed is its working stock; only what's beyond it is picked up (Review Focus 3).
            ColonyBuilding farm = DevHelpers.Place(BuildingTypes.Farm.PrefabName, OnGround(hallAt + right * 16f - forward * 16f), facing, player.GetPlayerID());
            for (float t = 0f; farm != null && farm.ColonyId != colony && t < 15f; t += 0.5f) yield return new WaitForSeconds(0.5f);
            farm?.GetComponent<WorkerModule>()?.RequestSetAutoHire(false);
            DevHelpers.KeepNothing(farm);
            CropInfo crop = CropList.Crops.FirstOrDefault(c => !c.IsBush && c.Seed.Length > 0);
            Colony.FieldMarker field = farm != null && crop != null ? DevHelpers.PlaceField(farm, farm.transform.position + farm.transform.forward * 9f, FieldSizes.New, crop.Piece) : null;
            yield return new WaitForSeconds(2f);
            int working = farm != null && crop != null && farm.Storage.Working != null && farm.Storage.Working.TryGetValue(crop.Seed, out int w) ? w : 0;
            if (farm != null) Add(farm.Storage.GetComponent<Container>().GetInventory(), crop != null ? crop.Seed : "CarrotSeeds", working + 40);
            yield return Watch(150f, () => crop != null && In(farm, crop.Seed) <= working);
            Check($"A courier leaves a Farm the seed its fields need ({crop?.Seed}: working stock {working}, in the Farm {working + 40} -> " +
                  $"{(crop != null ? In(farm, crop.Seed) : -1)}, field {field != null})",
                crop != null && field != null && working > 0 && In(farm, crop.Seed) == working);

            // 17. The drop-off: a kept item put in the storehouse's own chest goes straight to the building short of it, the
            //     rest into the store; a sword stays (Review Focus 5).
            StockTab.SetForTest(lumber, "Resin", 5);
            Inventory dropOff = store.GetComponent<Container>().GetInventory();
            dropOff.AddItem(Prefab("Resin"), 12);
            dropOff.AddItem(Prefab("SwordBronze"), 1);
            yield return Watch(150f, () => In(lumber, "Resin") >= 5 && In(store, "Resin") == 0);
            Check($"The drop-off: kept Resin goes to the hut short of it and the rest into the store, a sword stays (resin at the Lumberjack's " +
                  $"{In(lumber, "Resin")} of 5, left in the drop-off {In(store, "Resin")}, in the chests inside {InStore("Resin") - In(store, "Resin")}, " +
                  $"sword {In(store, "SwordBronze")})", In(lumber, "Resin") >= 5 && In(store, "Resin") == 0 && In(store, "SwordBronze") == 1);

            // 18. The Builder's Hut filled for its order: Wood only in the storehouse.
            Add(storehouse.Chests.First().GetComponent<Container>().GetInventory(), "Wood", 100);
            ColonyBuilding builderHut = ColonyBuilding.All.FirstOrDefault(b => b != null && b.IsValid && b.ColonyId == colony && b.TypeId == BuildingTypes.BuilderHutId);
            int sawWood = 0;
            for (float t = 0f; t < 150f && sawWood == 0; t += 0.5f)
            {
                if (dispatch.PlanForTest.Any(task => task.From == hall.Id)) everFromHall = true;
                sawWood = Mathf.Max(sawWood, In(builderHut, "Wood"));
                yield return new WaitForSeconds(0.5f);
            }
            Check($"A courier brings the builder's order its Wood from the storehouse (most Wood seen in the Builder's Hut {sawWood})", builderHut != null && sawWood > 0);

            // 19. Never from the Town Hall: 60 Wood there makes no task from it, all the while.
            Add(hall.Storage.GetComponent<Container>().GetInventory(), "Wood", 60);
            yield return Watch(15f, () => false);
            Check($"Couriers never take from the Town Hall (a task from it in any plan: {everFromHall}, wood in the hall {In(hall, "Wood")})", !everFromHall);

            // 20. A target gone mid-delivery: the load goes back to the storehouse, the task failed (Review Focus 2).
            StockTab.SetForTest(lumber, "Stone", 20);
            storehouse.Chests.First().GetComponent<Container>().GetInventory().AddItem(Prefab("Stone"), 40);
            for (float t = 0f; t < 120f && !(Job()?.TaskForTest?.To == lumber.Id && courier.Carry.Count("Stone") > 0); t += 0.25f) yield return new WaitForSeconds(0.25f);
            int carried = courier.Carry.Count("Stone");
            bool carrying = carried > 0;
            int storeAtTake = InStore("Stone");   // the builder's order took some Stone too (the second run): counted from here
            string failedWas = CourierJob.LastFailedForTest;
            if (lumber != null) Remove(lumber.gameObject);
            for (float t = 0f; t < 90f && courier.Carry.Count("Stone") > 0; t += 0.5f) yield return new WaitForSeconds(0.5f);
            Check($"A courier whose target goes mid-delivery takes the load back to the storehouse (carrying {carrying}, in hand now " +
                  $"{courier.Carry.Count("Stone")}, stone in the storehouse {storeAtTake} -> {InStore("Stone")} with {carried} back, task failed {CourierJob.LastFailedForTest != failedWas})",
                carrying && courier.Carry.Count("Stone") == 0 && InStore("Stone") == storeAtTake + carried && CourierJob.LastFailedForTest != failedWas);

            // 21. Level 3: two couriers' places, and the store sorted (a smaller stack moves to the chest with the most of it).
            int levelWas = store.Level;
            site.RequestUpgradeNow();
            for (float t = 0f; store.Level == levelWas && t < 60f; t += 0.5f) yield return new WaitForSeconds(0.5f);
            yield return new WaitForSeconds(StorehouseRules.ScanSeconds + 1f);
            // Its hand-over: from level 3 a store room behind; a settler walks to its farthest chest.
            StorageModule farthest = storehouse.Chests.OrderByDescending(c => Utils.DistanceXZ(c.transform.position, store.transform.position)).FirstOrDefault();
            if (farthest != null) yield return Climb(store, site, farthest.transform.position, "the store room's farthest chest");
            else Check("A settler walks to the store room's farthest chest (no chest inside at level 3)", false);
            GameObject second = PlaceChest("piece_chest_wood", ClearSpot(site, room, floor, facing, store.transform.position, storehouse.Chests.First().transform.position), facing, made);
            yield return new WaitForSeconds(StorehouseRules.ScanSeconds + 1f);
            StorageModule a = storehouse.Chests.FirstOrDefault(), b2 = second != null ? second.GetComponent<StorageModule>() : null;
            foreach (StorageModule chest in storehouse.Chests) chest.GetComponent<Container>().GetInventory().RemoveAll();
            a?.GetComponent<Container>().GetInventory().AddItem(Prefab("Stone"), 30);
            b2?.GetComponent<Container>().GetInventory().AddItem(Prefab("Stone"), 10);
            int Stone(StorageModule s) => s != null && s.Contents().TryGetValue("Stone", out int n) ? n : 0;
            yield return Watch(150f, () => Stone(b2) == 0 && Stone(a) == 40);   // taken and put (the first run looked between)
            var watch = System.Diagnostics.Stopwatch.StartNew();   // the plan's cost (it runs every few seconds on the storehouse's owner)
            for (int i = 0; i < 20; i++) dispatch.ReplanForTest();
            watch.Stop();
            Log.Event($"vc_storehousetest: the couriers' plan takes {watch.Elapsed.TotalMilliseconds / 20:0.000} ms, every {CourierDispatch.PlanSeconds:0} s");
            Check($"A level-3 storehouse has two couriers' places and sorts its store (places {worker.Capacity}, stone {Stone(a)} and {Stone(b2)}, " +
                  $"second chest in the store {b2 != null && storehouse.Chests.Contains(b2)})",
                worker.Capacity == 2 && b2 != null && Stone(b2) == 0 && Stone(a) == 40);

            // ---- Its review's fixes (1.5) ----
            void Empty(ColonyBuilding b)
            {
                foreach (StorageModule chest in b.Storehouse != null ? b.Storehouse.Chests.Concat(new[] { b.Storage }) : new[] { b.Storage })
                    chest.GetComponent<Container>().GetInventory().RemoveAll();
            }
            void Fill(Inventory into, string name)
            {
                for (int i = 0; into.HaveEmptySlot() && i < 300; i++) Add(into, name, 1000);
            }
            int Planned(System.Func<CourierTask, bool> what) { dispatch.ReplanForTest(); return dispatch.PlanForTest.Count(what); }
            bool IsPickup(CourierTask t) => t.Kind == CourierTaskKind.Pickup || t.Kind == CourierTaskKind.UrgentPickup;

            // 22. I5: the storehouse's own Keep in stock is filled from its chests inside.
            Empty(store);
            StockTab.SetForTest(store, "Raspberry", 5);
            Add(a.GetComponent<Container>().GetInventory(), "Raspberry", 20);
            yield return Watch(150f, () => In(store, "Raspberry") >= 5);
            Check($"The storehouse's own Keep in stock is filled from its chests inside (raspberries in its own chest {In(store, "Raspberry")} of 5)",
                In(store, "Raspberry") >= 5);
            StockTab.SetForTest(store, "Raspberry", 0);
            Empty(store);

            // 23. I3: a pickup at a chest kept busy (a player has it open, another computer hands it over) waits instead of failing.
            Container farmChest = farm.GetComponent<Container>();
            string failedBefore = CourierJob.LastFailedForTest;
            Add(farmChest.GetInventory(), "Feathers", 40);
            farmChest.SetInUse(true);
            for (float t = 0f; t < 90f && Job()?.TaskForTest?.From != farm.Id; t += 0.5f) yield return new WaitForSeconds(0.5f);
            bool pickupGiven = Job()?.TaskForTest?.From == farm.Id;
            yield return new WaitForSeconds(15f);
            bool failedWhileBusy = CourierJob.LastFailedForTest != failedBefore;
            farmChest.SetInUse(false);
            yield return Watch(120f, () => InStore("Feathers") == 40);
            Check($"A courier waits at a busy chest instead of giving up (pickup given {pickupGiven}, failed while busy {failedWhileBusy}, " +
                  $"feathers in the store after {InStore("Feathers")} of 40)", pickupGiven && !failedWhileBusy && InStore("Feathers") == 40);

            // 24. M3 (re-graded): a full trip's take is quick (it was a take per item).
            Check($"A courier's take is quick (the last, {CourierJob.LastTakeMsForTest:0.00} ms for up to {CourierJob.LoadAt(store)})", CourierJob.LastTakeMsForTest < 5.0);
            Empty(store);

            // 25. I4: a storehouse being taken down gets no pickups, drop-off or sorting.
            Add(farmChest.GetInventory(), "Feathers", 40);
            site.RequestTakeDown();
            yield return new WaitForSeconds(2f);
            bool takingDown = site.IsTakingDown;
            int intoClosed = Planned(t => IsPickup(t) || t.Kind == CourierTaskKind.DropOff || t.Kind == CourierTaskKind.Sort);
            site.RequestCancelTakeDown();
            yield return new WaitForSeconds(2f);
            Check($"A storehouse being taken down gets nothing put in it (taking down {takingDown}, pickups, drop-off or sorting planned {intoClosed}, " +
                  $"taking down after the cancel {site.IsTakingDown})", takingDown && intoClosed == 0 && !site.IsTakingDown);

            // 26. C1: a courier holding a load when the store fills doesn't keep it for good (nor the drop-off fill up).
            for (float t = 0f; t < 120f && courier.Carry.Count("Feathers") == 0; t += 0.25f) yield return new WaitForSeconds(0.25f);
            bool holding = courier.Carry.Count("Feathers") > 0;
            foreach (StorageModule chest in storehouse.Chests) Fill(chest.GetComponent<Container>().GetInventory(), "Stone");
            Fill(store.GetComponent<Container>().GetInventory(), "Stone");
            for (float t = 0f; t < 90f && courier.Carry.Total > 0; t += 0.5f) yield return new WaitForSeconds(0.5f);
            Check($"A courier whose store fills puts the load elsewhere, not kept for good (held {holding}, in hand now {courier.Carry.Total}, " +
                  $"feathers in the Town Hall {In(hall, "Feathers")}, in the Farm {In(farm, "Feathers")})", holding && courier.Carry.Total == 0);

            // 27. I1: sorting into a full chest isn't planned, nor looped.
            Empty(store);
            Fill(a.GetComponent<Container>().GetInventory(), "Stone");
            Add(b2.GetComponent<Container>().GetInventory(), "Stone", 10);
            int tripsWas = CourierJob.TripsForTest;
            yield return new WaitForSeconds(20f);
            int sorts = Planned(t => t.Kind == CourierTaskKind.Sort);
            Check($"Sorting into a full chest isn't planned or looped (sorts planned {sorts}, trips meanwhile {CourierJob.TripsForTest - tripsWas})",
                sorts == 0 && CourierJob.TripsForTest == tripsWas);

            // 28. I2: no delivery to a chest with no room (the Town Hall full, short of food).
            Empty(store);
            Add(a.GetComponent<Container>().GetInventory(), "Raspberry", 20);
            Inventory hallItems = hall.GetComponent<Container>().GetInventory();
            hallItems.RemoveAll();
            Fill(hallItems, "Wood");
            int toHall = Planned(t => t.Kind == CourierTaskKind.Deliver && t.To == hall.Id);
            Check($"A full Town Hall gets no delivery it can't take (deliveries to it planned {toHall}, food in it {FoodIn(hall)} of 10 kept)", toHall == 0);
            hallItems.RemoveAll();

            // ---- Part 3: the window and words (1.5) ----
            // 29. "Couriers take the rest" off: a Farm's chest stays as the player arranged it; on again, its surplus goes.
            Empty(store);
            farmChest.GetInventory().RemoveAll();
            bool switchShown = StockTab.ShowsTakeRest(farm) && !StockTab.ShowsTakeRest(hall) && !StockTab.ShowsTakeRest(store);
            farm.Stock.RequestSetTakeRest(false);
            yield return new WaitForSeconds(1f);
            bool off = !farm.Stock.CouriersTakeRest;
            Add(farmChest.GetInventory(), "Feathers", 40);
            int fromFarm = 0;
            for (float t = 0f; t < 25f; t += 0.5f)
            {
                fromFarm += Planned(task => task.From == farm.Id);
                yield return new WaitForSeconds(0.5f);
            }
            int keptOff = In(farm, "Feathers");
            farm.Stock.RequestSetTakeRest(true);
            yield return Watch(150f, () => In(farm, "Feathers") == 0);
            Check($"With Couriers take the rest off a hut's chest stays as it is, on again its surplus goes (switch on a hut only {switchShown}, off {off}, " +
                  $"tasks from it while off {fromFarm}, feathers kept {keptOff} of 40, after on {In(farm, "Feathers")})",
                switchShown && off && fromFarm == 0 && keptOff == 40 && In(farm, "Feathers") == 0 && farm.Stock.CouriersTakeRest);

            // 30. The Needs tab: what nobody has is on the shopping list; what the store has is fetched.
            Empty(store);
            StockTab.SetForTest(farm, "Obsidian", 5);
            StockTab.SetForTest(farm, "Resin", 5);
            Add(a.GetComponent<Container>().GetInventory(), "Resin", 20);
            NeedsReport needs = dispatch.Needs();
            NeedLine obsidian = needs.Lines.FirstOrDefault(l => l.Site == farm.Id && l.Item == "Obsidian");
            NeedLine resin = needs.Lines.FirstOrDefault(l => l.Site == farm.Id && l.Item == "Resin");
            string obsidianLine = obsidian != null ? NeedsTab.Line(obsidian, dispatch) : "";
            string bringLine = NeedsTab.Bring(needs.Bring);
            bool onTheList = needs.Bring.Any(b => b.Item == "Obsidian" && b.Amount == 5) && bringLine.Contains(Localization.instance.Localize("$item_obsidian"));
            yield return Watch(150f, () => In(farm, "Resin") >= 5);
            bool resinGone = !dispatch.Needs().Lines.Any(l => l.Site == farm.Id && l.Item == "Resin");
            Check($"The Needs tab: nobody has Obsidian, so it is brought ('{obsidianLine}'; '{bringLine}'), the store's Resin is fetched " +
                  $"(fetched or coming {resin?.Waiting + resin?.Coming} of 5, at the Farm now {In(farm, "Resin")}, its line gone {resinGone})",
                obsidian != null && obsidian.Missing == 5 && onTheList && resin != null && resin.Waiting + resin.Coming == 5 && In(farm, "Resin") >= 5 && resinGone);
            StockTab.SetForTest(farm, "Obsidian", 0);
            StockTab.SetForTest(farm, "Resin", 0);

            // 31. A take-down with goods inside: its couriers carry them away first, then it is taken down (and the cancel).
            Empty(store);
            hallItems.RemoveAll();
            Add(a.GetComponent<Container>().GetInventory(), "Stone", 30);
            Add(b2.GetComponent<Container>().GetInventory(), "Wood", 20);
            site.RequestTakeDown();
            yield return new WaitForSeconds(2f);
            bool closing = site.IsClosingStore, notAtOnce = !site.IsTakingDown, shut = !storehouse.Open;
            bool clearing = false;
            for (float t = 0f; t < 300f && !site.IsTakingDown; t += 0.5f)
            {
                if (Job()?.TaskForTest?.Kind == CourierTaskKind.Clear) clearing = true;
                yield return new WaitForSeconds(0.5f);
            }
            bool takenDown = site.IsTakingDown;
            int leftInside = Stone(a) + (b2 != null && b2.Contents().TryGetValue("Wood", out int wd) ? wd : 0);
            site.RequestCancelTakeDown();
            yield return new WaitForSeconds(2f);
            Check($"A storehouse with goods inside is emptied by its couriers before it's taken down (closing {closing}, not at once {notAtOnce}, " +
                  $"its store shut {shut}, a courier clearing {clearing}, then taken down {takenDown}, left inside {leftInside}, stone at the Town Hall " +
                  $"{In(hall, "Stone")}, after the cancel taking down {site.IsTakingDown} closing {site.IsClosingStore})",
                closing && notAtOnce && shut && clearing && takenDown && leftInside == 0 && !site.IsTakingDown && !site.IsClosingStore);

            // 32. Gear couriers can't carry: the take-down is the player's to empty, and says so.
            Empty(store);
            a.GetComponent<Container>().GetInventory().AddItem(Prefab("SwordBronze"), 1);
            StoreTakeDown choice = site.StoreChoice();
            int gearRefusalsWas = ConstructionModule.StoreFullRefusalsForTest;
            site.RequestTakeDown();
            yield return new WaitForSeconds(1.5f);
            bool leftAlone = !site.IsClosingStore && !site.IsTakingDown;
            bool refusedWithWords = ConstructionModule.StoreFullRefusalsForTest == gearRefusalsWas + 1;   // its review: refused, not passed by
            Check($"Gear couriers can't carry is the player's to take out before a take-down (choice {choice}, refused with words {refusedWithWords}, " +
                  $"closing {site.IsClosingStore}, taking down {site.IsTakingDown})", choice == StoreTakeDown.EmptyItYourself && refusedWithWords && leftAlone);
            Empty(store);

            // 33. Like with like (the player's pick, 1.5): a Farm's Feathers go into the chest inside already holding
            //     Feathers, the farthest one, not the nearest.
            List<StorageModule> inside = storehouse.Chests.ToList();
            StorageModule farthestChest = inside.LastOrDefault();
            farthestChest?.GetComponent<Container>().GetInventory().AddItem(Prefab("Feathers"), 10);
            farmChest.GetInventory().RemoveAll();
            Add(farmChest.GetInventory(), "Feathers", 40);
            int Feathers(StorageModule s) => s != null && s.Contents().TryGetValue("Feathers", out int n) ? n : 0;
            CourierJob.MaxHandDistanceForTest = 0f;
            yield return Watch(150f, () => In(farm, "Feathers") == 0 && courier.Carry.Count("Feathers") == 0);
            float handed = CourierJob.MaxHandDistanceForTest;
            int elsewhere = inside.Where(c => c != farthestChest).Sum(Feathers) + In(store, "Feathers");
            Check($"Couriers put away like with like: a Farm's Feathers go into the chest already holding them, the farthest of {inside.Count} " +
                  $"(in it {Feathers(farthestChest)} of 50, in the other chests {elsewhere}, left at the Farm {In(farm, "Feathers")})",
                inside.Count >= 2 && Feathers(farthestChest) == 50 && elsewhere == 0);
            // The review round (2026-10-23): it walked to that chest, not handing them in from the store's own chest by the
            // door, through the room (and through a wall where one stood between).
            Check($"A courier walks to the chest it puts into (it stood at most {handed:0.0} m from it; reach {HandReach} m)",
                handed > 0f && handed <= HandReach);
            Empty(store);

            // 34. The review round (2026-10-23): its hut's area unloaded and loaded again (the storehouse made anew, its id the
            //     same), its courier works on for the new one (it stood "Unemployed" for good, as any worker would).
            //     Their work doesn't run while the hut is gone (indoors at night, at the fire: as when it was found; working, the
            //     brain saw no workplace, let the job go and made a new one, and the first run of this check passed on the old
            //     code). Then real work: the drop-off put away.
            string storeId = store.Id;
            CitizenAI courierAi = courier != null ? courier.GetComponent<CitizenAI>() : null;
            courierAi?.PauseForTest(30f);
            yield return new WaitForSeconds(0.5f);
            DevHelpers.UnloadAndReload(store.GetComponent<ZNetView>());
            ColonyBuilding again = null;
            for (float t = 0f; t < 15f && (again == null || again == store); t += 0.5f)
            {
                yield return new WaitForSeconds(0.5f);
                again = ColonyBuilding.FindById(storeId);
            }
            yield return new WaitForSeconds(2f);
            courierAi?.PauseForTest(0f);   // back to work, the new storehouse there
            Container newDropOff = again != null && again != store ? again.GetComponent<Container>() : null;
            newDropOff?.GetInventory().AddItem(Prefab("Wood"), 10);
            for (float t = 0f; t < 90f && newDropOff != null && In(again, "Wood") > 0; t += 0.5f) yield return new WaitForSeconds(0.5f);
            string status = courier != null ? courier.Status : "";
            Check($"A courier works on when its storehouse's area unloads and loads again (made anew {again != null && again != store}, " +
                  $"10 Wood dropped off, {(newDropOff != null ? In(again, "Wood") : -1)} left there; status '{Localization.instance.Localize(status)}')",
                again != null && again != store && newDropOff != null && In(again, "Wood") == 0 && status != "$vc_status_unemployed");

            // 35. 1.5.0 (the review round's pick, the player's yes): the Town Hall's overview says when the storehouse is full,
            //     with what to do, and stops once it has room. Every chest inside filled to its last slot with stone.
            ColonyBuilding nowStore = again != null ? again : store;
            List<Inventory> insideNow = nowStore != null && nowStore.Storehouse != null
                ? nowStore.Storehouse.Chests.Where(c => c != null).Select(c => c.GetComponent<Container>().GetInventory()).ToList()
                : new List<Inventory>();
            int stoneStack = Prefab("Stone").GetComponent<ItemDrop>().m_itemData.m_shared.m_maxStackSize;
            foreach (Inventory chest in insideNow)
                for (int i = 0; i < 200 && chest.HaveEmptySlot(); i++) chest.AddItem(Prefab("Stone"), stoneStack);
            TownHallModule hallModule = hall.GetComponent<TownHallModule>();
            string FullLine() => hallModule != null ? Localization.instance.Localize(TownHallWindow.Summary(hallModule)) : "";
            bool Says() => FullLine().Contains("storehouse is full");
            for (float t = 0f; t < 20f && !Says(); t += 0.5f) yield return new WaitForSeconds(0.5f);
            bool sawFull = Says();
            string fullText = FullLine();
            foreach (Inventory chest in insideNow) chest.RemoveAll();
            for (float t = 0f; t < 20f && Says(); t += 0.5f) yield return new WaitForSeconds(0.5f);
            bool gone = !Says();
            Check($"The Town Hall's overview says when the storehouse is full, with what to do, and stops once it has room ({insideNow.Count} chests " +
                  $"inside filled: said {sawFull}; emptied: gone {gone}; '{fullText.Replace("\n", " | ")}')",
                insideNow.Count > 0 && sawFull && fullText.Contains("more chests") && gone);
        }

        /// <summary>Within reach of a chest: a walk ends within its reach plus the slack the game's path ending allows.</summary>
        private const float HandReach = 2.5f + MoveRules.PathEndSlack;

        /// <summary>
        /// A settler put on the ground in front of the storehouse's door walks to a chest: there, at its floor, with no wall
        /// between (not handing over from under the floor). The Storehouse's hand-over: vcbp's walk check is flat.
        /// </summary>
        private static IEnumerator Climb(ColonyBuilding store, ConstructionModule site, Vector3 chest, string what)
        {
            Citizen walker = Citizen.All.FirstOrDefault(c => c != null && c.ColonyId == store.ColonyId);
            CitizenAI ai = walker != null ? walker.GetComponent<CitizenAI>() : null;
            Door door = DevHelpers.DoorOf(site);
            if (walker == null || ai == null || door == null)
            {
                Check($"A settler climbs to {what} (settler {walker != null}, door {door != null})", false);
                yield break;
            }
            Vector3 outward = door.transform.position - store.transform.position;
            outward.y = 0f;
            Vector3 start = door.transform.position + outward.normalized * 3f;
            start.y = ZoneSystem.instance.GetGroundHeight(start);
            Rigidbody body = walker.GetComponent<Rigidbody>();
            Vector3 at = start;
            float away = 0f, below = 0f;
            bool clear = false;
            // Twice at most: right after an upgrade the game may still be making the new room walkable (a busy computer,
            // the like-with-like run of 2026-10-22: the walk ended outside, under the floor); couriers keep looking then.
            for (int attempt = 1; attempt <= 2; attempt++)
            {
                if (attempt == 2)
                {
                    Log.Event($"vc_storehousetest: the walk to {what} ended outside ({away:0.0} m off, {below:0.00} m below it); again in 10 s");
                    yield return new WaitForSeconds(10f);
                }
                walker.transform.position = start;
                if (body != null) body.position = start;
                yield return new WaitForSeconds(0.5f);
                ai.TestWalk(chest, 60f);
                for (float t = 0f; t < 60f && !ai.TestWalkDone; t += 0.5f) yield return new WaitForSeconds(0.5f);
                at = walker.transform.position;
                away = Utils.DistanceXZ(at, chest);
                below = chest.y - at.y;
                clear = ai.NothingBetween(chest);
                if (ai.TestWalkDone && away <= 2.5f && below < 1.0f && clear) break;
            }
            Check($"A settler climbs from the door's landing to {what} (got there {ai.TestWalkDone}, {away:0.0} m off, {below:0.00} m below it, " +
                  $"nothing between {clear})", ai.TestWalkDone && away <= 2.5f && below < 1.0f && clear);
        }

        private static int FoodIn(ColonyBuilding b) =>
            b != null && b.Storage != null ? b.Storage.Contents().Where(i => FoodItems.Value(i.Key) > 0f).Sum(i => i.Value) : 0;

        /// <summary>A spot inside the walls, clear of them, where no piece of this level or the next stands and the next one's floor holds a chest.</summary>
        private static Vector3 ClearSpot(ConstructionModule site, Footprint room, float floor, Quaternion facing, Vector3 fallback, Vector3? awayFrom = null)
        {
            List<PieceBox> now = site.Plan.Where(p => p.IsValid).Select(PiecePlacer.PlannedBox).ToList();
            List<PlannedPiece> next = site.NextLevelPlanForTest();
            List<PieceBox> later = next != null ? next.Where(p => p.IsValid).Select(PiecePlacer.PlannedBox).ToList() : new List<PieceBox>();
            Footprint walls = room.Grow(-SiteSurvey.SiteMargin);
            Footprint clear = walls.Grow(-0.8f);
            for (float dz = -2f; dz <= 2f; dz += 0.5f)
                for (float dx = -2f; dx <= 2f; dx += 0.5f)
                {
                    Vector3 at = U(walls.ToWorld(dx, dz));
                    at.y = floor;
                    if (!clear.Contains(V(at))) continue;
                    if (awayFrom.HasValue && Utils.DistanceXZ(at, awayFrom.Value) < 1.6f) continue;   // another chest stands there
                    PieceBox box = PiecePlacer.PlannedBox(new PlannedPiece(0, "piece_chest_wood", V(at), Q(facing)));
                    if (StorehouseRules.InTheWayOf(box, now) >= 0 || !StorehouseRules.ChestStaysThroughUpgrade(box, later, false)) continue;
                    return at;
                }
            Log.Warn("vc_storehousetest: no clear spot inside the storehouse for a chest; the middle it is");
            return fallback;
        }

        private static Vector3 OnGround(Vector3 p) => new Vector3(p.x, ZoneSystem.instance.GetGroundHeight(p), p.z);

        private static int Count(Dictionary<string, int> items, string item) => items.TryGetValue(item, out int n) ? n : 0;

        /// <summary>How many of an item (by its prefab) a chest holds; -1 without the chest.</summary>
        private static int CountIn(Inventory items, string prefab) =>
            items == null ? -1 : items.GetAllItems().Where(i => i.m_dropPrefab != null && i.m_dropPrefab.name == prefab).Sum(i => i.m_stack);

        /// <summary>A chest of the game's (as a player would place it), standing here.</summary>
        private static GameObject PlaceChest(string prefabName, Vector3 at, Quaternion rotation, List<GameObject> made)
        {
            GameObject prefab = ZNetScene.instance.GetPrefab(prefabName);
            if (prefab == null) return null;
            GameObject chest = Object.Instantiate(prefab, at, rotation);
            made.Add(chest);
            return chest;
        }

        private static IEnumerator Finish(ColonyBuilding building)
        {
            ConstructionModule site = building != null ? building.GetComponent<ConstructionModule>() : null;
            for (float t = 0f; site != null && site.Order == null && t < 15f; t += 0.5f) yield return new WaitForSeconds(0.5f);
            if (site != null && site.Order != null) site.FinishNow();
            for (float t = 0f; site != null && !site.IsBuilt && t < 40f; t += 0.5f) yield return new WaitForSeconds(0.5f);
        }

        private static void Remove(GameObject go)
        {
            if (go == null) return;
            ZNetView nview = go.GetComponent<ZNetView>();
            if (nview != null && nview.IsValid() && !nview.IsOwner()) nview.ClaimOwnership();
            ZNetScene.instance.Destroy(go);
        }

        private static void Check(string what, bool ok)
        {
            if (ok) s_pass++;
            else s_fail++;
            Log.Event($"vc_storehousetest: {(ok ? "PASS" : "FAIL")} {what}");
        }
    }
}
