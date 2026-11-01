using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
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
    /// <summary>vc_cooktest (test worlds): the Mead Hall's cook (1.6, part 1), checked in the game.</summary>
    internal sealed class CookTestCommand : ConsoleCommand
    {
        public override string Name => "vc_cooktest";
        public override string Help => "VikingColonies (test worlds): a Chieftain's Camp and a Mead Hall here with a cooking station over a fire and a " +
                                       "cook; raw meat roasted into the hall's chest, nothing left on the floor or burnt, the hearth out, breakfast at " +
                                       "the hall. Removes what it made.";
        public override bool IsCheat => true;

        public override void Run(string[] args)
        {
            Player p = Player.m_localPlayer;
            if (p == null || ZNet.instance == null || !TestWorlds.IsTestWorld(ZNet.instance.GetWorldName()))
            {
                BuildCommand.Print("vc_cooktest: test worlds only");
                return;
            }
            Plugin.Instance.StartCoroutine(CookTest.Run(p));
        }
    }

    /// <summary>
    /// Checks the cook in the game (the plan's Task 6): each logs "vc_cooktest: check N PASS|FAIL", then "vc_cooktest: done".
    /// Written against the game's own station and the mod's parts before the cook's loop, so its checks fail there first.
    /// </summary>
    internal static class CookTest
    {
        private static readonly System.Reflection.MethodInfo s_getSlot = AccessTools.Method(typeof(CookingStation), "GetSlot");
        private static int s_pass, s_fail;

        public static IEnumerator Run(Player player)
        {
            s_pass = s_fail = 0;
            Log.Event("vc_cooktest: starting");
            if (!player.InGodMode()) player.SetGodMode(true);   // never off: the test character has 1 health
            Vector3 origin = player.transform.position, forward = player.transform.forward;
            forward.y = 0f;
            forward.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Quaternion facing = Quaternion.LookRotation(forward);
            var made = new List<GameObject>();
            bool tendWas = Config.ColonyConfig.TendFires.Value;
            try
            {
                if (EnvMan.IsNight() || EnvMan.instance.GetDayFraction() > 0.45f) yield return SkipToMorning();   // a whole day's light for the checks
                DevHelpers.ClearMonsters(origin, 60f, animalsToo: true);
                ColonyBuilding camp = DevHelpers.Place(BuildingTypes.TownHall.PrefabName, origin + forward * 8f, facing, player.GetPlayerID());
                if (camp != null) made.Add(camp.gameObject);
                DevHelpers.KeepNothing(camp);
                TownHallModule townHall = camp != null ? camp.GetComponent<TownHallModule>() : null;
                for (float t = 0f; townHall != null && !townHall.Founded && t < 20f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                if (camp == null || townHall == null || !townHall.Founded)
                {
                    Check(0, $"A Chieftain's Camp is founded (camp {camp != null})", false);
                    yield break;
                }
                string colony = camp.ColonyId;
                Vector3 meadAt = OnGround(camp.transform.position + right * 14f);
                ColonyBuilding mead = DevHelpers.Place(SaveKeys.MeadHallPiece, meadAt, facing, player.GetPlayerID());
                if (mead != null) made.Add(mead.gameObject);
                for (float t = 0f; mead != null && mead.ColonyId != colony && t < 15f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                // Since its five designs (1.6), a Mead Hall goes down as a build order, and an unbuilt hut hires nobody: it is
                // finished at once, and its own stations and fires taken away, so the checks' one station over its one fire
                // is the hall's only one (v16's first cook.txt: "The Mead Hall hires a cook" failed).
                ConstructionModule meadSite = mead != null ? mead.GetComponent<ConstructionModule>() : null;
                for (float t = 0f; meadSite != null && meadSite.Order == null && !meadSite.IsBuilt && t < 15f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                if (meadSite != null && meadSite.Order != null) meadSite.FinishNow();
                for (float t = 0f; meadSite != null && !meadSite.IsBuilt && t < 40f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                MeadHallModule built = mead != null ? mead.GetComponent<MeadHallModule>() : null;
                if (built != null)
                {
                    int removed = 0;
                    foreach (Component own in Object.FindObjectsOfType<CookingStation>().Cast<Component>().Concat(Object.FindObjectsOfType<Fireplace>())
                                 .Where(c => c != null && built.Holds(c.transform.position)).ToList())
                    {
                        ZNetView view = own.GetComponent<ZNetView>();
                        if (view != null && view.IsValid()) { view.ClaimOwnership(); ZNetScene.instance.Destroy(own.gameObject); removed++; }
                    }
                    Log.Event($"vc_cooktest: the Mead Hall built (its own {removed} stations and fires taken away)");
                }
                DevHelpers.KeepNothing(mead);
                Vector3 fireAt = OnGround(meadAt + forward * 3f);
                GameObject fireGo = Spawn("fire_pit", fireAt, facing, made);
                GameObject stationGo = Spawn("piece_cookingstation", fireAt, facing, made);
                Fireplace fire = fireGo != null ? fireGo.GetComponent<Fireplace>() : null;
                CookingStation station = stationGo != null ? stationGo.GetComponent<CookingStation>() : null;
                SetFuel(fire, fire != null ? fire.m_maxFuel : 0f);
                MeadHallModule hall = mead != null ? mead.GetComponent<MeadHallModule>() : null;
                if (hall != null) hall.Rescan();
                if (mead == null || hall == null || station == null || fire == null || mead.ColonyId != colony)
                {
                    Check(0, $"A Mead Hall stands in the colony with a cooking station over a fire (hall {mead != null}, module {hall != null}, " +
                             $"station {station != null}, fire {fire != null})", false);
                    yield break;
                }

                // Every colony chest emptied, then wood and raw meat in the camp's chest only.
                foreach (ColonyBuilding b in ColonyBuilding.All.Where(b => b != null && b.IsValid && b.ColonyId == colony))
                    foreach (StorageModule chest in b.Stores)
                        chest.GetComponent<Container>().GetInventory().RemoveAll();
                Inventory campChest = camp.Storage.GetComponent<Container>().GetInventory();
                campChest.AddItem(ObjectDB.instance.GetItemPrefab("Wood"), 30);
                campChest.AddItem(ObjectDB.instance.GetItemPrefab("RawMeat"), 6);

                WorkerModule worker = mead.GetComponent<WorkerModule>();
                worker.RequestHire();
                Citizen cook = null;
                for (float t = 0f; cook == null && t < 20f; t += 0.5f)
                {
                    yield return new WaitForSeconds(0.5f);
                    if (worker.FindWorker(out Citizen found, out _) == WorkerModule.WorkerState.Loaded) cook = found;
                }
                if (cook == null)
                {
                    Check(0, "The Mead Hall hires a cook", false);
                    yield break;
                }
                Log.Event($"vc_cooktest: {cook.CitizenName} is the cook; {townHall.Roster.Entries.Count} settlers");

                // 1. Raw meat in the camp's chest is roasted into the hall's chest.
                int cooked = 0, rawLeft = 6;
                for (float t = 0f; t < 240f && !(cooked >= 6 && rawLeft == 0); t += 1f)
                {
                    yield return new WaitForSeconds(1f);
                    cooked = In(mead, "CookedMeat");
                    rawLeft = In(camp, "RawMeat");
                }
                Check(1, $"6 raw meat in the camp's chest become cooked meat in the Mead Hall's chest (cooked {cooked}, raw left in the camp {rawLeft}, " +
                         $"on the station {string.Join(",", Slots(station))}, the cook '{Localization.instance.Localize(cook.Status)}')",
                    cooked == 6 && rawLeft == 0);

                // 2. A minute after, nothing cooked lies on the floor round the hall (Review Focus 2).
                yield return new WaitForSeconds(60f);
                ICollection<string> hot = MeadHallModule.Cooked;
                List<string> lying = Object.FindObjectsByType<ItemDrop>(FindObjectsSortMode.None)
                    .Where(d => d != null && !d.IsPiece() && Vector3.Distance(d.transform.position, meadAt) <= 10f
                                && (hot.Contains(Utils.GetPrefabName(d.gameObject)) || Utils.GetPrefabName(d.gameObject) == "Coal"))
                    .Select(d => Utils.GetPrefabName(d.gameObject)).ToList();
                Check(2, $"Nothing cooked lies within 10 m of the hall a minute after (lying: {string.Join(",", lying)})", lying.Count == 0);

                // 3. Nothing doubled or lost (Review Focus 1): what went in is in the chest, on the station or in hand.
                int onStation = Slots(station).Count(s => s.Length > 0);
                int total = In(mead, "CookedMeat") + In(camp, "RawMeat") + onStation + cook.Carry.Count("RawMeat") + cook.Carry.Count("CookedMeat");
                Check(3, $"The station and the hall's chest agree: 6 in, {total} counted (chest {In(mead, "CookedMeat")}, station {onStation}, " +
                         $"in hand {cook.Carry.Total})", total == 6);

                // 4. A piece done while the cook is held away waits, never coal; with the cook fired, the game burns it.
                CitizenAI ai = cook.GetComponent<CitizenAI>();
                ai?.PauseForTest(400f);
                ZNetView stationView = stationGo.GetComponent<ZNetView>();
                stationView.InvokeRPC("RPC_AddItem", "RawMeat", false);
                float cookTime = station.m_conversion.First(c => c.m_from.gameObject.name == "RawMeat").m_cookTime;
                yield return new WaitForSeconds(cookTime * 2f + 120f);
                List<string> held = Slots(station);
                Check(4, $"A piece done while the cook is away waits done for 2 minutes, no coal (cook time {cookTime:0} s, on the station " +
                         $"{string.Join(",", held)}, coal in the chest {In(mead, "Coal")})",
                    held.Contains("CookedMeat") && !held.Contains("Coal") && In(mead, "Coal") == 0);
                worker.RequestSetAutoHire(false);
                worker.RequestFire();
                List<string> burnt = held;
                for (float t = 0f; t < 20f && !burnt.Contains("Coal"); t += 1f)
                {
                    yield return new WaitForSeconds(1f);
                    burnt = Slots(station);
                }
                Check(4, $"With the cook fired, the game burns it as usual (on the station {string.Join(",", burnt)})", burnt.Contains("Coal"));
                ClearStation(station);
                ai?.PauseForTest(0f);

                // 5. The hearth out and no wood anywhere: nothing goes on the station, and the cook says so (Review Focus 4).
                // Free time's fire tending off meanwhile, so only the cook feeds the hearth (run 1: a settler fed it in 5b).
                tendWas = Config.ColonyConfig.TendFires.Value;
                Config.ColonyConfig.TendFires.Value = false;
                worker.RequestHire();
                for (float t = 0f; t < 20f && worker.FindWorker(out cook, out _) != WorkerModule.WorkerState.Loaded; t += 0.5f) yield return new WaitForSeconds(0.5f);
                foreach (ColonyBuilding b in ColonyBuilding.All.Where(b => b != null && b.IsValid && b.ColonyId == colony))
                    foreach (StorageModule chest in b.Stores)
                        chest.GetComponent<Container>().GetInventory().RemoveAll();
                SetFuel(fire, 0f);
                campChest.AddItem(ObjectDB.instance.GetItemPrefab("RawMeat"), 4);
                string status = "";
                for (float t = 0f; t < 45f; t += 1f)
                {
                    yield return new WaitForSeconds(1f);
                    status = cook != null ? Localization.instance.Localize(cook.Status) : "";
                }
                List<string> cold = Slots(station);
                Check(5, $"The hearth out and no wood: nothing goes on the station, and the cook says so (station {string.Join(",", cold)}, " +
                         $"the cook '{status}')",
                    cold.All(s => s.Length == 0) && status == Localization.instance.Localize("$vc_status_cook_hearth_out"));

                // 5b. Wood in the camp's chest again: the cook lights the hearth and the meat goes on (the review's check).
                campChest.AddItem(ObjectDB.instance.GetItemPrefab("Wood"), 30);
                float fuelNow = 0f;
                List<string> lit = cold;
                for (float t = 0f; t < 90f && !(fuelNow > 0f && lit.Any(s => s.Length > 0)); t += 1f)
                {
                    yield return new WaitForSeconds(1f);
                    fuelNow = fire.GetComponent<ZNetView>().GetZDO().GetFloat(ZDOVars.s_fuel, 0f);
                    lit = Slots(station);
                }
                Check(5, $"With wood in the camp's chest the cook feeds the hearth and the meat goes on (fuel {fuelNow:0.0}, station {string.Join(",", lit)})",
                    fuelNow > 0f && lit.Any(s => s.Length > 0));
                Config.ColonyConfig.TendFires.Value = tendWas;
                cook.GetComponent<CitizenAI>()?.PauseForTest(600f);   // the roasting stops here: the breakfast checks count chests
                ClearStation(station);

                // 6. A settler whose meal is due eats breakfast from the hall's chest at its table, and is glad of a hot meal;
                // the hall's chest first even with the same food in the camp's chest by them.
                Inventory meadChest = mead.Storage.GetComponent<Container>().GetInventory();
                meadChest.RemoveAll();
                meadChest.AddItem(ObjectDB.instance.GetItemPrefab("CookedMeat"), 10);
                campChest.AddItem(ObjectDB.instance.GetItemPrefab("CookedMeat"), 10);
                Citizen eater = Citizen.All.FirstOrDefault(c => c != null && c != cook && c.Record.ColonyId == colony && c.Body != null && !c.Body.IsDead());
                if (eater == null)
                {
                    Check(6, "A settler other than the cook for breakfast", false);
                    yield break;
                }
                eater.transform.position = OnGround(camp.transform.position - forward * 2f);   // by the camp's chest: the hall farther
                yield return Breakfast(eater);
                string usedChest = eater.GetComponent<CitizenAI>().BreakfastForTest.LastChestForTest;
                string why = eater.Record.WhyMood ?? "";
                for (float t = 0f; t < 75f && !why.Contains("$vc_mood_hotmeal"); t += 1f)   // the mood is worked out once a minute
                {
                    yield return new WaitForSeconds(1f);
                    why = eater.Record.WhyMood ?? "";
                }
                Check(6, $"{eater.CitizenName} eats breakfast from the Mead Hall's chest, and their mood has a hot meal (chest {usedChest} — the hall's " +
                         $"{mead.Id}, ate {eater.LastMeal}, mood '{why}')",
                    usedChest == mead.Id && eater.LastMeal == "CookedMeat" && why.Contains("$vc_mood_hotmeal"));

                // 7. The hall's chest empty and raspberries in the camp's: breakfast as before, from the camp's chest.
                meadChest.RemoveAll();
                campChest.RemoveAll();
                campChest.AddItem(ObjectDB.instance.GetItemPrefab("Raspberry"), 10);
                yield return Breakfast(eater);
                usedChest = eater.GetComponent<CitizenAI>().BreakfastForTest.LastChestForTest;
                Check(7, $"With nothing in the hall's chest, breakfast comes from the camp's chest as before (chest {usedChest} — the camp's " +
                         $"{camp.Id}, ate {eater.LastMeal})", usedChest == camp.Id && eater.LastMeal == "Raspberry");

                // 8. The fire under the hall's station is the cook's, and the evening's gathering passes it by (1.6, v16's review):
                // the colony's places read afresh, the hearth burning.
                SetFuel(fire, fire.m_maxFuel);
                yield return new WaitForSeconds(2f);
                ColonyPlaces.Forget();
                List<FirePlace> fires = ColonyPlaces.Fires(colony).Where(f => f.Valid).ToList();
                FirePlace cooks = fires.FirstOrDefault(f => f.Fire == fire);
                int evening = FreeTimeRoutine.EveningFire(colony, fires, camp.transform.position);
                Check(8, $"The hall's hearth is the cook's fire and not the evening's (found {cooks != null}, cook's {cooks != null && cooks.Cooks}, " +
                         $"burning {cooks != null && cooks.Burning}, the evening at {(evening >= 0 ? fires[evening].Id : "the camp")}, of {fires.Count} fires)",
                    cooks != null && cooks.Cooks && (evening < 0 || fires[evening].Fire != fire));
            }
            finally
            {
                Config.ColonyConfig.TendFires.Value = tendWas;
                DevHelpers.ClearMonsters(origin, 60f, animalsToo: true);
                foreach (GameObject go in made.Where(g => g != null)) Remove(go);
                Log.Event($"vc_cooktest: done — {s_pass} passed, {s_fail} failed");
                Player.m_localPlayer?.Message(MessageHud.MessageType.Center, $"vc_cooktest: {s_pass} passed, {s_fail} failed");
            }
        }

        /// <summary>Their meal made due (the life test's way) and waited out: they set off within a minute.</summary>
        private static IEnumerator Breakfast(Citizen eater)
        {
            eater.ForgetMealsForTest();
            yield return new WaitForSeconds(1f);
            eater.MissMealForTest();
            int today = EnvMan.instance.GetDay(ZNet.instance.GetTimeSeconds());
            for (float t = 0f; t < 150f && eater.Record.LastMealDay != today; t += 0.5f) yield return new WaitForSeconds(0.5f);
        }

        /// <summary>The game's own skip to the next morning (as sleeping does), waited out.</summary>
        private static IEnumerator SkipToMorning()
        {
            int day = EnvMan.instance.GetDay();
            EnvMan.instance.SkipToMorning();
            for (float t = 0f; t < 30f && (EnvMan.instance.IsTimeSkipping() || EnvMan.instance.GetDay() == day); t += 0.5f) yield return new WaitForSeconds(0.5f);
            Log.Event($"vc_cooktest: skipped to the morning of day {EnvMan.instance.GetDay()}");
        }

        private static int In(ColonyBuilding b, string item) =>
            b != null && b.Storage != null && b.Storage.Contents().TryGetValue(item, out int n) ? n : 0;

        /// <summary>Each slot's item ("" for empty), read the game's way.</summary>
        private static List<string> Slots(CookingStation station)
        {
            var slots = new List<string>();
            if (station == null || station.m_slots == null || s_getSlot == null) return slots;
            for (int i = 0; i < station.m_slots.Length; i++)
            {
                object[] args = { i, null, null, null, null };
                s_getSlot.Invoke(station, args);
                slots.Add(args[1] as string ?? "");
            }
            return slots;
        }

        /// <summary>Empties the station's slots (as its owner), so the next check starts clean.</summary>
        private static void ClearStation(CookingStation station)
        {
            System.Reflection.MethodInfo setSlot = AccessTools.Method(typeof(CookingStation), "SetSlot");
            if (station == null || setSlot == null) return;
            ZNetView view = station.GetComponent<ZNetView>();
            if (view != null && view.IsValid() && !view.IsOwner()) view.ClaimOwnership();
            object done = System.Enum.ToObject(AccessTools.Inner(typeof(CookingStation), "Status"), 0);
            // Valheim 1.0.21 added a sixth parameter (long sender = 0): the arguments as many as the game's method takes.
            int count = setSlot.GetParameters().Length;
            for (int i = 0; i < station.m_slots.Length; i++)
            {
                var args = new object[] { i, "", 0f, done, false, 0L };
                if (count < args.Length) System.Array.Resize(ref args, count);
                setSlot.Invoke(station, args);
            }
        }

        private static void SetFuel(Fireplace fire, float fuel)
        {
            ZNetView view = fire != null ? fire.GetComponent<ZNetView>() : null;
            if (view == null || !view.IsValid()) return;
            if (!view.IsOwner()) view.ClaimOwnership();
            view.GetZDO().Set(ZDOVars.s_fuel, fuel);
        }

        private static GameObject Spawn(string prefab, Vector3 at, Quaternion facing, List<GameObject> made)
        {
            GameObject source = ZNetScene.instance.GetPrefab(prefab);
            GameObject go = source != null ? Object.Instantiate(source, at, facing) : null;
            if (go != null) made.Add(go);
            return go;
        }

        private static Vector3 OnGround(Vector3 at)
        {
            at.y = ZoneSystem.instance.GetGroundHeight(at);
            return at;
        }

        private static void Remove(GameObject go)
        {
            ZNetView nview = go.GetComponent<ZNetView>();
            if (nview != null && nview.IsValid() && !nview.IsOwner()) nview.ClaimOwnership();
            ZNetScene.instance.Destroy(go);
        }

        private static void Check(int n, string what, bool ok)
        {
            if (ok) s_pass++;
            else s_fail++;
            Log.Event($"vc_cooktest: check {n} {(ok ? "PASS" : "FAIL")} {what}");
        }
    }
}
