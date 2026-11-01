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
        /// <summary>From where the test starts: the hall 8 m ahead, the huts 16 m from it, and their pieces round them.</summary>
        private const float LeftoversRadius = 32f;

        public static IEnumerator Run(Player player)
        {
            s_pass = s_fail = 0;
            Log.Event("vc_polishtest: starting");
            if (!player.InGodMode()) player.SetGodMode(true);
            Vector3 origin = player.transform.position, forward = player.transform.forward;
            // The pieces of a hut an earlier run built and took away (check 9 finishes the Builder's Hut): the next builder
            // takes them down for their wood, and a site with all it needs lets them leave their own hut (own-first's first
            // game run, 2026-10-24: 135 Wood from the last run's hut sent check 8's builder to the other site).
            int leftovers = DevHelpers.RemoveOrphanPieces(origin, LeftoversRadius);
            if (leftovers > 0) Log.Info($"vc_polishtest: cleared {leftovers} pieces of huts an earlier run left");
            var before = new HashSet<string>(Citizen.All.Where(c => c != null).Select(c => c.Id));
            int woodWas = ColonyConfig.HallWoodCost.Value;
            bool walkInWas = ColonyConfig.NewcomersWalkIn.Value;
            ColonyBuilding hall = null, farm = null, held = null, builderHut = null, other = null, third = null;
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

                // 5. A settler made bigger by another mod keeps their own size (a player's report, 1.4.3: StarLevelSystem
                // makes a creature 10% bigger a star, and starred settlers no longer fit through a door).
                Citizen sized = Citizen.All.FirstOrDefault(c => c != null && c.Body != null && hall != null && c.Record.ColonyId == hall.ColonyId);
                if (sized == null) Check("A settler of the test's colony to make bigger", false);
                else
                {
                    Vector3 own = sized.transform.localScale;
                    sized.transform.localScale = own * 1.3f;   // as StarLevelSystem sizes a three-star creature
                    yield return new WaitForSeconds(3f);
                    Vector3 now = sized != null ? sized.transform.localScale : Vector3.zero;
                    Check($"A settler made bigger by another mod is kept at their own size ({sized.CitizenName}: {own.x:0.00} -> {own.x * 1.3f:0.00} " +
                          $"-> {now.x:0.00} 3 s later)", (now - own).magnitude < 0.02f);
                }

                // 6. A hut's chest keeps what its own open build order still needs (1.5.1: in the build-through a builder on the
                // Forager's Hut took the Resin put in the Lumberjack's Hut's chest for its own level 2, and that level waited).
                held = hall != null ? DevHelpers.Place(BuildingTypes.ForagerHut.PrefabName, hall.transform.position - player.transform.right * 16f, Quaternion.identity, player.GetPlayerID()) : null;
                ConstructionModule heldSite = held != null ? held.GetComponent<ConstructionModule>() : null;
                for (float t = 0f; heldSite != null && heldSite.Order == null && t < 15f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                Dictionary<string, int> orderNeeds = heldSite != null && heldSite.Order != null
                    ? heldSite.RemainingNeeds().Where(n => StorageModule.Carried(n.Key)).ToDictionary(n => n.Key, n => n.Value) : new Dictionary<string, int>();
                Inventory heldChest = held != null ? held.GetComponent<Container>()?.GetInventory() : null;
                if (orderNeeds.Count == 0 || heldChest == null) Check($"A hut with an open order and a chest for check 6 (needs: {orderNeeds.Count})", false);
                else
                {
                    const int beyond = 5;
                    string extra = orderNeeds.Keys.OrderBy(k => k, StringComparer.Ordinal).First();
                    foreach (KeyValuePair<string, int> need in orderNeeds) Fill(heldChest, need.Key, need.Value + (need.Key == extra ? beyond : 0));
                    yield return new WaitForSeconds(0.5f);
                    Dictionary<string, int> spare = held.Storage.SpareContents();
                    string seen = string.Join(", ", orderNeeds.Select(n => $"{n.Key} {n.Value} needed, {(spare.TryGetValue(n.Key, out int s) ? s : 0)} spare"));
                    Check($"A hut's chest keeps what its own order still needs from everyone else; only what's beyond it is spare ({seen}; {beyond} {extra} beyond)",
                        orderNeeds.All(n => (spare.TryGetValue(n.Key, out int s) ? s : 0) == (n.Key == extra ? beyond : 0)));

                    // 7. ...but it is that order's own: its builder may take it, and tries it right after their own hut; the window
                    // counts it for that order (and for all the orders together), not for another; an order set aside holds nothing.
                    for (float t = 0f; held.ColonyId != hall.ColonyId && t < 10f; t += 0.5f) yield return new WaitForSeconds(0.5f);   // joined its colony
                    List<StorageModule> order = BuilderTools.ColonyStores(hall.ColonyId, null, hall.transform.position, held);
                    bool siteFirst = order.Count > 0 && order[0] == held.Storage;
                    Dictionary<string, int> forOwn = held.Storage.SpareContents(forOwnOrder: true);
                    Dictionary<string, int> forThis = BuilderTools.StoresForBuilders(hall.ColonyId, held.transform.position, held);
                    Dictionary<string, int> forAll = BuilderTools.StoresForBuilders(hall.ColonyId, held.transform.position);
                    Dictionary<string, int> forOther = BuilderTools.StoresForBuilders(hall.ColonyId, held.transform.position, hall);
                    int Got(Dictionary<string, int> d, string k) => d.TryGetValue(k, out int n) ? n : 0;
                    bool ownTakes = orderNeeds.All(n => Got(forOwn, n.Key) >= n.Value);
                    bool counted = orderNeeds.All(n => Got(forThis, n.Key) >= n.Value && Got(forAll, n.Key) >= n.Value
                        && Got(forOther, n.Key) == Got(forThis, n.Key) - n.Value);
                    heldSite.RequestPause(true);
                    yield return new WaitForSeconds(5.5f);   // the chest looks at its order every 5 s
                    Dictionary<string, int> setAside = held.Storage.SpareContents();
                    bool released = orderNeeds.All(n => Got(setAside, n.Key) >= n.Value);
                    Check($"...the order's own builder may take it ({ownTakes}) and tries it first after their own hut ({siteFirst}), the Work orders count it " +
                          $"for that order and for all of them, not for another ({counted}), and set aside it holds nothing ({released})",
                        ownTakes && siteFirst && counted && released);
                }

                // 8. The colony's pieces sit in the build menu's Misc tab (the player's 1.5.0 try: in Furniture). Valheim 1.0's
                // menu files a piece by its usage tags, which ours kept from the chest, sign or pillar they're made from.
                var names = BuildingTypes.All.Select(t => t.PrefabName).Concat(new[] { SaveKeys.FieldMarkerPiece, SaveKeys.MemorialPiece }).ToList();
                var missing = names.Where(n => PieceManager.Instance.GetPiece(n)?.Piece == null).ToList();
                var wrong = names.Select(n => PieceManager.Instance.GetPiece(n)?.Piece)
                    .Where(p => p != null && p.m_usage != Piece.UsageTagFlags.Misc).Select(p => $"{p.name}: {p.m_usage}").ToList();
                Check($"The colony's pieces are in the build menu's Misc tab ({names.Count} pieces; " +
                      $"{(wrong.Count == 0 && missing.Count == 0 ? "all Misc" : string.Join(", ", wrong.Concat(missing.Select(m => m + ": missing"))))})",
                    wrong.Count == 0 && missing.Count == 0);
                // Check 6's hut goes before checks 9-10 (the 1.6 trial merge): set aside, its chest's materials are spare to every
                // order, which would give the next checks' other site all it needs (and its spot is the Builder's Hut's).
                if (held != null) { Remove(held.gameObject); held = null; }
                yield return new WaitForSeconds(1f);

                // 9. A builder never leaves their own hut before it first stands, though another site waits (1.6, the player's
                // pick A: the 1.4.2 watch saw a builder leave a hut). Nothing to build with anywhere, so their own hut waits on
                // the player; their patience is cut short here.
                BuilderJob.WaitOnPlayerSeconds = 8f;
                EnvMan.instance.m_debugTimeOfDay = true;   // day all through: evening would send the builder off their site (its review)
                EnvMan.instance.m_debugTime = 0.4f;
                Vector3 side = player.transform.right;
                builderHut = hall != null ? DevHelpers.Place(BuildingTypes.BuilderHut.PrefabName, hall.transform.position - side * 16f, Quaternion.identity, player.GetPlayerID()) : null;
                ConstructionModule ownSite = builderHut != null ? builderHut.GetComponent<ConstructionModule>() : null;
                Citizen builder = null;
                for (float t = 0f; builderHut != null && builder == null && t < 30f; t += 0.5f)
                {
                    yield return new WaitForSeconds(0.5f);
                    if (builderHut.GetComponent<WorkerModule>()?.FindWorker(out Citizen found, out _) == WorkerModule.WorkerState.Loaded) builder = found;
                }
                other = hall != null ? DevHelpers.Place(BuildingTypes.ForagerHut.PrefabName, hall.transform.position + player.transform.forward * 16f, Quaternion.identity, player.GetPlayerID()) : null;
                ConstructionModule otherSite = other != null ? other.GetComponent<ConstructionModule>() : null;
                bool Waits(Citizen who) => who != null && (who.Status ?? "").StartsWith("$vc_status_needs");
                for (float t = 0f; builder != null && !(ClaimedBy(ownSite, builder) && Waits(builder)) && t < 120f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                bool waited = Waits(builder);
                yield return new WaitForSeconds(BuilderJob.WaitOnPlayerSeconds + 15f);
                Check($"A builder stays at their own hut until it first stands, though another site waits (waited on you: {waited}, on their own: " +
                      $"{ClaimedBy(ownSite, builder)}, on the other: {ClaimedBy(otherSite, builder)})",
                    builder != null && otherSite?.Order != null && waited && ClaimedBy(ownSite, builder) && !ClaimedBy(otherSite, builder));

                // 10. Once it stands, a site that waits on the player is left for another, as before, and they say why: their
                // hover, on every computer, from a note saved on them.
                if (ownSite != null && ownSite.Order != null) ownSite.FinishNow();
                for (float t = 0f; ownSite != null && !ownSite.IsBuilt && t < 30f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                third = hall != null ? DevHelpers.Place(BuildingTypes.LumberjackHut.PrefabName, hall.transform.position - player.transform.forward * 16f, Quaternion.identity, player.GetPlayerID()) : null;
                for (float t = 0f; builder != null && !(ClaimedBy(otherSite, builder) && Waits(builder)) && t < 120f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                for (float t = 0f; builder != null && ClaimedBy(otherSite, builder) && t < BuilderJob.WaitOnPlayerSeconds + 30f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                // The third site can wait on the player too (the Lumberjack's needs an axe as well): left in the same moment,
                // the note names whichever went last (29 Oct, the self-test), so either site counts.
                bool noted = builder != null && AwayNote.Decode(builder.AwayNote, out string awayFrom, out _)
                    && ((other != null && awayFrom == other.Id) || (third != null && awayFrom == third.Id));
                string hover = builder != null && builder.Body != null ? DevHelpers.Plain(builder.Body.GetHoverText()) : "";
                string meanwhile = Localization.instance.Localize("$vc_builder_away").Split(',').Last().Trim().Replace("{0}", "").Replace("{1}", "");
                Check($"A builder who leaves a site for now says why (left it: {!ClaimedBy(otherSite, builder)}, note: {noted}, hover: '{hover.Replace("\n", " | ")}')",
                    builder != null && !ClaimedBy(otherSite, builder) && noted && hover.Contains(meanwhile)
                    && (hover.Contains(Localization.instance.Localize(BuildingTypes.ForagerHut.NameToken))
                        || hover.Contains(Localization.instance.Localize(BuildingTypes.LumberjackHut.NameToken))));
            }
            finally
            {
                BuilderJob.WaitOnPlayerSeconds = (float)OrderPatience.WaitSeconds;
                EnvMan.instance.m_debugTimeOfDay = false;
                CitizenPrefab.HideForTest = false;
                ColonyConfig.HallWoodCost.Value = woodWas;
                ColonyConfig.NewcomersWalkIn.Value = walkInWas;
            }
            Log.Event($"vc_polishtest: done — {s_pass} passed, {s_fail} failed");
            player.Message(MessageHud.MessageType.Center, $"vc_polishtest: {s_pass} passed, {s_fail} failed");
            foreach (Citizen c in Citizen.All.Where(c => c != null && !before.Contains(c.Id)).ToList()) Remove(c.gameObject);
            if (hall != null) Remove(hall.gameObject);
            foreach (ColonyBuilding b in new[] { builderHut, other, third }) if (b != null) Remove(b.gameObject);
            if (held != null) Remove(held.gameObject);
            if (farm != null)
            {
                Vector3 farmAt = farm.transform.position;
                foreach (Pickable bush in Jobs.WorldScan.Pickables().Where(b => b != null && Utils.GetPrefabName(b.gameObject) == "RaspberryBush" && Utils.DistanceXZ(b.transform.position, farmAt) < 20f).ToList())
                    Remove(bush.gameObject);
                foreach (Colony.FieldMarker field in Colony.FieldMarker.All.Where(m => m != null && Utils.DistanceXZ(m.transform.position, farmAt) < 20f).ToList())
                    Remove(field.gameObject);
                Remove(farm.gameObject);
            }
            yield return null;   // the huts gone, their pieces are left over: those go too
            DevHelpers.RemoveOrphanPieces(origin, LeftoversRadius);
            Log.Event("vc_polishtest: cleaned up");
        }

        private static void Remove(GameObject go)
        {
            ZNetView nview = go.GetComponent<ZNetView>();
            if (nview != null && nview.IsValid() && !nview.IsOwner()) nview.ClaimOwnership();
            ZNetScene.instance.Destroy(go);
        }

        private static bool ClaimedBy(ConstructionModule site, Citizen builder) =>
            site != null && site.IsValid && builder != null && site.Order is BuildOrderRecord order
            && order.Builder == builder.Id && order.HasLiveClaim(ConstructionModule.Now());
        /// <summary>Puts so many of an item in the chest, a full stack at a time.</summary>
        private static void Fill(Inventory chest, string item, int amount)
        {
            GameObject prefab = ObjectDB.instance.GetItemPrefab(item);
            int stack = prefab != null ? Math.Max(1, prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_maxStackSize) : 1;
            for (int left = amount; prefab != null && left > 0; left -= stack) chest.AddItem(prefab, Math.Min(stack, left));
        }

        private static void Check(string what, bool ok)
        {
            if (ok) s_pass++;
            else s_fail++;
            Log.Event($"vc_polishtest: {(ok ? "PASS" : "FAIL")} {what}");
        }
    }
}
