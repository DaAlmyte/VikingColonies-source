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
using VikingColonies.Gui;

namespace VikingColonies.DevTools
{
    /// <summary>vc_creativetest (test worlds): creative mode (1.5) checked in the game, in a world with No build cost.</summary>
    internal sealed class CreativeTestCommand : ConsoleCommand
    {
        public override string Name => "vc_creativetest";
        public override string Help => "VikingColonies (test worlds): creative mode's checks in a colony of its own here: placing built, Build, Upgrade " +
                                       "and Repair now, Finish all now, the Town Hall's level, Add settlers, free builders, meals off, every refusal. " +
                                       "Turns the world's No build cost on for the test and back as it was; removes what it made.";
        public override bool IsCheat => true;

        public override void Run(string[] args)
        {
            Player p = Player.m_localPlayer;
            if (p == null || ZNet.instance == null || !TestWorlds.IsTestWorld(ZNet.instance.GetWorldName()))
            {
                BuildCommand.Print("vc_creativetest: test worlds only");
                return;
            }
            Plugin.Instance.StartCoroutine(CreativeTest.Run(p));
        }
    }

    internal static class CreativeTest
    {
        private static int s_pass, s_fail;

        private static void Check(string what, bool ok)
        {
            if (ok) s_pass++;
            else s_fail++;
            string line = $"vc_creativetest: {(ok ? "PASS" : "FAIL")} {what}";
            if (ok) Log.Event(line);
            else Log.Warn(line);
            BuildCommand.Print(line);
        }

        private static void WorldFree(bool on)
        {
            if (on) ZoneSystem.instance.SetGlobalKey(GlobalKeys.NoBuildCost);
            else ZoneSystem.instance.RemoveGlobalKey(GlobalKeys.NoBuildCost);
        }

        private static int Settlers(string colony) => Citizen.All.Count(c => c != null && c.Record.ColonyId == colony);

        private static bool AllStand(ConstructionModule site)
        {
            if (site == null || !site.IsValid) return false;
            site.StandReport(out int total, out int standing);
            return total > 0 && standing == total;
        }

        private static int Pieces(ColonyBuilding building)
        {
            var near = new List<Piece>();
            Piece.GetAllPiecesInRadius(building.transform.position, 30f, near);
            return near.Count(p => p != null && PiecePlacer.SiteOf(p.gameObject) == building.Id);
        }

        /// <summary>The rock's parts still solid within <paramref name="r"/> m of a spot (a broken part's collider goes).</summary>
        private static int RockPartsNear(GameObject rock, Vector3 at, float r) => rock == null ? 0 :
            rock.GetComponentsInChildren<Collider>(false).Count(c => c != null && c.enabled && Utils.DistanceXZ(c.bounds.center, at) < r);

        /// <summary>How many of an item there are: lying within 25 m of a spot, in the colony's chests and in its settlers' hands.</summary>
        private static int Anywhere(string item, Vector3 at, string colony)
        {
            int n = Object.FindObjectsByType<ItemDrop>(FindObjectsSortMode.None)
                .Where(d => d != null && d.m_itemData != null && d.m_itemData.m_dropPrefab != null && d.m_itemData.m_dropPrefab.name == item
                            && Utils.DistanceXZ(d.transform.position, at) < 25f)
                .Sum(d => d.m_itemData.m_stack);
            n += ColonyBuilding.All.Where(b => b != null && b.IsValid && b.ColonyId == colony && b.Storage != null)
                .Sum(b => b.Storage.Contents().TryGetValue(item, out int held) ? held : 0);
            n += Citizen.All.Where(c => c != null && c.Record.ColonyId == colony).Sum(c => c.Carry.Count(item));
            return n;
        }

        private static Vector3 Centre(ConstructionModule site, ColonyBuilding building)
        {
            Footprint f = site != null ? site.SiteFootprint : null;
            return f == null ? building.transform.position : new Vector3(f.Centre.X, building.transform.position.y, f.Centre.Z);
        }

        private static IEnumerator WaitFor(System.Func<bool> done, float seconds)
        {
            for (float waited = 0f; !done() && waited < seconds; waited += 0.5f) yield return new WaitForSeconds(0.5f);
        }

        public static IEnumerator Run(Player player)
        {
            s_pass = s_fail = 0;
            Log.Event("vc_creativetest: starting");
            if (!player.InGodMode()) player.SetGodMode(true);   // never off: the test character has 1 health
            EnvMan.instance.m_debugTimeOfDay = true;
            EnvMan.instance.m_debugTime = 0.4f;
            Vector3 origin = player.transform.position, forward = player.transform.forward, right = player.transform.right;
            forward.y = right.y = 0f;
            forward.Normalize();
            right.Normalize();
            Quaternion facing = Quaternion.LookRotation(forward);
            long me = player.GetPlayerID();
            bool freeWas = Creative.WorldNoBuildCost();
            int maxSettlersWas = ColonyConfig.MaxSettlers.Value;
            bool walkInWas = ColonyConfig.NewcomersWalkIn.Value;
            try
            {
                ColonyConfig.NewcomersWalkIn.Value = false;
                DevHelpers.ClearMonsters(origin, 70f, animalsToo: true);
                WorldFree(true);
                ColonyBuilding hall = DevHelpers.Place(BuildingTypes.TownHall.PrefabName, origin + forward * 6f, facing, me);
                TownHallModule townHall = hall != null ? hall.GetComponent<TownHallModule>() : null;
                yield return WaitFor(() => townHall != null && townHall.Founded, 20f);
                if (townHall == null || !townHall.Founded)
                {
                    Check("A Town Hall is placed and founded", false);
                    yield break;
                }
                DevHelpers.KeepNothing(hall);
                string colony = hall.ColonyId;
                townHall.SetLevelForTest(5);   // every hut level allowed (check 6 sets it with creative)
                Vector3 Spot(float ahead, float side) => origin + forward * ahead + right * side;

                // 11a. In creative the windows show the creative buttons.
                Windows.TownHall.Open(hall);
                yield return new WaitForSeconds(1f);
                string hallText = Windows.TownHall.TextForTest();
                Windows.TownHall.Close();
                Check($"In a No build cost world the Town Hall's window shows the creative buttons (creative: {Creative.LocalIsCreative()}; " +
                      $"'Finish all now' shown: {hallText.Contains(Localization.instance.Localize("$vc_creative_finish_all"))})",
                    Creative.LocalIsCreative() && hallText.Contains(Localization.instance.Localize("$vc_creative_finish_all")));

                // 1. Placed through the placing path at level 3: up at once, every piece standing.
                Vector3 at1 = Spot(32f, -36f);
                CreativePlacing.NoteForTest(BuildingTypes.LumberjackHut, 3, at1);
                ColonyBuilding placed = DevHelpers.Place(BuildingTypes.LumberjackHut.PrefabName, at1, facing, me);
                ConstructionModule placedSite = placed != null ? placed.GetComponent<ConstructionModule>() : null;
                yield return WaitFor(() => placed != null && placed.Level >= 3 && placedSite.IsBuilt, 60f);
                yield return new WaitForSeconds(3f);
                Check($"A Lumberjack's Hut placed with level 3 picked goes up at once at level 3, every piece standing (level {(placed != null ? placed.Level : 0)}, " +
                      $"stands: {AllStand(placedSite)})", placed != null && placed.Level == 3 && AllStand(placedSite));

                // 2. The same on a site with a tree: the tree cleared, every piece standing.
                Vector3 at2 = Spot(32f, -12f);
                GameObject treePrefab = ZNetScene.instance.GetPrefab("Beech1");
                GameObject tree = treePrefab != null ? Object.Instantiate(treePrefab, at2 + right * 1.5f + Vector3.up * 0.1f, Quaternion.identity) : null;
                yield return new WaitForSeconds(1f);
                CreativePlacing.NoteForTest(BuildingTypes.ForagerHut, 2, at2);
                ColonyBuilding forager = DevHelpers.Place(BuildingTypes.ForagerHut.PrefabName, at2, facing, me);
                ConstructionModule foragerSite = forager != null ? forager.GetComponent<ConstructionModule>() : null;
                yield return WaitFor(() => forager != null && forager.Level >= 2 && foragerSite.IsBuilt, 60f);
                yield return new WaitForSeconds(3f);
                Check($"A Forager's Hut placed at level 2 over a tree: the tree cleared, every piece standing (tree there: {tree != null}, " +
                      $"level {(forager != null ? forager.Level : 0)}, stands: {AllStand(foragerSite)})",
                    treePrefab != null && tree == null && forager != null && forager.Level == 2 && AllStand(foragerSite));

                // 3. Upgrade now pressed twice at once on a level-1 hut: two levels.
                Vector3 at3 = Spot(32f, 12f);
                CreativePlacing.NoteForTest(BuildingTypes.Farm, 1, at3);
                ColonyBuilding farm = DevHelpers.Place(BuildingTypes.Farm.PrefabName, at3, facing, me);
                ConstructionModule farmSite = farm != null ? farm.GetComponent<ConstructionModule>() : null;
                // Its order done, not just "built" (a hut with no order yet counts as built: the first run pressed too soon).
                yield return WaitFor(() => farmSite != null && farmSite.Order != null && farmSite.Order.State == BuildState.Done, 60f);
                farmSite?.RequestCreative(CreativeAction.Upgrade);
                farmSite?.RequestCreative(CreativeAction.Upgrade);
                yield return WaitFor(() => farm != null && farm.Level >= 3 && farmSite.IsBuilt, 90f);
                yield return new WaitForSeconds(2f);
                Check($"Upgrade now pressed twice at once takes a level-1 Farm up two levels (level {(farm != null ? farm.Level : 0)})", farm != null && farm.Level == 3);

                // 4. Upgrade now on a hut being taken down: refused, the take-down goes on.
                farmSite?.RequestTakeDown();
                yield return WaitFor(() => farmSite != null && farmSite.IsTakingDown, 10f);
                farmSite?.RequestCreative(CreativeAction.Upgrade);
                yield return new WaitForSeconds(3f);
                Check($"Upgrade now on a hut being taken down changes nothing (level {(farm != null ? farm.Level : 0)}, taking down: {farmSite != null && farmSite.IsTakingDown})",
                    farm != null && farm.Level == 3 && farmSite.IsTakingDown);
                // 4b (its review). Repair now during a take-down puts nothing back (it would leave a shell the builder never takes).
                var farmNear = new List<Piece>();
                Piece.GetAllPiecesInRadius(farm.transform.position, 30f, farmNear);
                foreach (Piece piece in farmNear.Where(p => p != null && PiecePlacer.SiteOf(p.gameObject) == farm.Id).Take(3).ToList())
                    ZNetScene.instance.Destroy(piece.gameObject);
                yield return new WaitForSeconds(1f);
                int beforeRepair = Pieces(farm);
                farmSite?.RequestCreative(CreativeAction.Repair);
                yield return new WaitForSeconds(3f);
                Check($"Repair now on a hut being taken down puts nothing back ({beforeRepair} pieces, then {Pieces(farm)})", Pieces(farm) == beforeRepair);
                farmSite?.RequestCancelTakeDown();   // or check 8's builder spends its wait taking the Farm down (the first run)
                yield return WaitFor(() => farmSite == null || !farmSite.IsTakingDown, 10f);

                // 4c (its review). Build now while an Upgrade now is going up is refused as busy (two finishes at once put pieces
                // up on ground still being levelled).
                int busyBefore = ConstructionModule.BusyRefusalsForTest;
                placedSite?.RequestCreative(CreativeAction.Upgrade);
                placedSite?.RequestCreative(CreativeAction.Build);
                yield return WaitFor(() => placed != null && placed.Level >= 4 && placedSite.Order != null && placedSite.Order.State == BuildState.Done, 60f);
                yield return new WaitForSeconds(2f);
                Check($"Build now while an upgrade is going up is refused as busy, and the upgrade stands whole (busy answers {ConstructionModule.BusyRefusalsForTest - busyBefore}, " +
                      $"level {(placed != null ? placed.Level : 0)}, stands: {AllStand(placedSite)})",
                    ConstructionModule.BusyRefusalsForTest > busyBefore && placed != null && placed.Level == 4 && AllStand(placedSite));

                // 4d (its review). A rock on the site of a hut placed built is cleared too.
                Vector3 at4 = Spot(10f, -36f);
                // A rock of parts (MineRock5, as a meadow boulder): its parts are what the old clearing left (a small whole rock
                // went already, the red run showed).
                GameObject rockPrefab = ZNetScene.instance.m_prefabs.Where(g => g != null && g.GetComponent<MineRock5>() != null && !g.name.Contains("frac"))
                    .OrderBy(g => g.name == "rock4_forest" ? 0 : g.name == "rock4_coast" ? 1 : g.name.StartsWith("rock4") ? 2 : 3).ThenBy(g => g.name).FirstOrDefault();
                GameObject rock = rockPrefab != null ? Object.Instantiate(rockPrefab, at4 + right * 1.5f, Quaternion.identity) : null;
                yield return new WaitForSeconds(1f);
                // Placed for the builder first, so the site's own scan can show the boulder's parts on it; then Build now.
                ColonyBuilding rocky = DevHelpers.Place(BuildingTypes.ForagerHut.PrefabName, at4, facing, me);
                ConstructionModule rockySite = rocky != null ? rocky.GetComponent<ConstructionModule>() : null;
                yield return WaitFor(() => rockySite != null && rockySite.Order != null && !string.IsNullOrEmpty(rocky.ColonyId), 20f);
                int rockPartsBefore = RockPartsNear(rock, at4, 7f);   // the boulder's own solid parts by the hut (the site's list empties once it's built)
                rockySite?.RequestCreative(CreativeAction.Build);
                yield return WaitFor(() => rockySite != null && rockySite.Order != null && rockySite.Order.State == BuildState.Done, 60f);
                yield return new WaitForSeconds(3f);
                int rockParts = RockPartsNear(rock, at4, 7f);
                Check($"Build now on a site with a boulder breaks its parts on the site ({(rockPrefab != null ? rockPrefab.name : "no rock of parts")}: {rockPartsBefore} parts " +
                      $"by the hut before, {rockParts} after), every piece standing: {AllStand(rockySite)}", rockPrefab != null && rockPartsBefore > 0 && rockParts < rockPartsBefore && AllStand(rockySite));
                // What's left of the boulder goes: the flat test world has no rocks of its own, and later runs' sites ran into
                // them (the 1.5.0 build-through: a hut gave up on one that needs a black-metal pickaxe).
                if (rock != null)
                {
                    ZNetView rockView = rock.GetComponent<ZNetView>();
                    if (rockView != null && rockView.IsValid()) rockView.ClaimOwnership();
                    ZNetScene.instance.Destroy(rock);
                }

                // 5. Finish all now on 7 open orders: all done, no stall.
                var opened = new List<ConstructionModule>();
                BuildingType[] kinds = { BuildingTypes.LumberjackHut, BuildingTypes.ForagerHut, BuildingTypes.Longhouse, BuildingTypes.LumberjackHut,
                                         BuildingTypes.ForagerHut, BuildingTypes.Longhouse, BuildingTypes.LumberjackHut };
                for (int i = 0; i < kinds.Length; i++)
                {
                    ColonyBuilding hut = DevHelpers.Place(kinds[i].PrefabName, Spot(58f + (i / 4) * 24f, -36f + (i % 4) * 24f), facing, me);
                    if (hut != null) opened.Add(hut.GetComponent<ConstructionModule>());
                }
                yield return WaitFor(() => opened.All(s => s != null && s.Order != null), 15f);
                float worst = 0f;
                for (float t = 0f; t < 2f; t += Time.unscaledDeltaTime)
                {
                    yield return null;
                    worst = Mathf.Max(worst, Time.unscaledDeltaTime);
                }
                townHall.FinishAllNow();
                float worstDuring = 0f;
                for (float t = 0f; t < 45f && !opened.All(s => s != null && s.IsBuilt); t += Time.unscaledDeltaTime)
                {
                    yield return null;
                    worstDuring = Mathf.Max(worstDuring, Time.unscaledDeltaTime);
                }
                int done = opened.Count(s => s != null && s.IsBuilt);
                Check($"Finish all now finishes 7 open orders without a stall ({done} of {opened.Count} done; the worst frame {worstDuring * 1000f:0} ms, " +
                      $"{worst * 1000f:0} ms before)", opened.Count == 7 && done == 7 && worstDuring < 1.5f);

                // 6. Set level 3, then 1: the level follows, nothing built goes.
                int campPieces = Pieces(hall);
                townHall.RequestCreative(HallCreativeAction.SetLevel, 3);
                yield return WaitFor(() => hall.Level == 3, 5f);
                int atThree = hall.Level;
                townHall.RequestCreative(HallCreativeAction.SetLevel, 1);
                yield return WaitFor(() => hall.Level == 1, 5f);
                yield return new WaitForSeconds(2f);
                Check($"Set level takes the Town Hall to 3 and back to 1, and nothing built goes (levels {atThree} then {hall.Level}; the camp's pieces " +
                      $"{campPieces} then {Pieces(hall)})", atThree == 3 && hall.Level == 1 && Pieces(hall) >= campPieces);

                // 7. Add 5 settlers with room for 2: 2 come.
                int settlers = Settlers(colony);
                ColonyConfig.MaxSettlers.Value = settlers + 2;
                townHall.RequestCreative(HallCreativeAction.AddSettlers, 5);
                yield return new WaitForSeconds(3f);
                Check($"Add 5 settlers with room for 2 brings 2 ({settlers} then {Settlers(colony)})", Settlers(colony) == settlers + 2);
                ColonyConfig.MaxSettlers.Value = maxSettlersWas;
                townHall.RequestCreative(HallCreativeAction.AddSettlers, 5);   // the builder for check 8 needs someone jobless
                yield return new WaitForSeconds(2f);

                // 8. A builder builds with no materials anywhere.
                CreativePlacing.NoteForTest(BuildingTypes.BuilderHut, 1, Spot(-26f, -20f));
                ColonyBuilding builderHut = DevHelpers.Place(BuildingTypes.BuilderHut.PrefabName, Spot(-26f, -20f), facing, me);
                ConstructionModule builderSite = builderHut != null ? builderHut.GetComponent<ConstructionModule>() : null;
                yield return WaitFor(() => builderSite != null && builderSite.Order != null && builderSite.Order.State == BuildState.Done, 60f);
                DevHelpers.KeepNothing(builderHut);
                builderHut?.GetComponent<Container>()?.GetInventory()?.RemoveAll();
                hall.GetComponent<Container>()?.GetInventory()?.RemoveAll();
                ColonyBuilding freeHut = DevHelpers.Place(BuildingTypes.LumberjackHut.PrefabName, Spot(-26f, 12f), facing, me);
                ConstructionModule freeSite = freeHut != null ? freeHut.GetComponent<ConstructionModule>() : null;
                yield return WaitFor(() => freeSite != null && freeSite.Order != null && freeSite.Order.DoneCount >= 3, 150f);
                Check($"In a No build cost world a builder puts pieces up with no materials anywhere ({(freeSite?.Order != null ? freeSite.Order.DoneCount : 0)} " +
                      $"up, state {freeSite?.Order?.State})", freeSite != null && freeSite.Order != null && freeSite.Order.DoneCount >= 3);

                // 14. The review round (2026-10-23): where builders build free, no materials are listed as needed (the Work orders'
                //     "Still to bring", the storehouse's Needs and its couriers, Hand over, the upgrade line).
                int stillNeeded = freeSite != null && freeSite.Order != null && freeSite.Order.State != BuildState.Done ? freeSite.RemainingNeeds().Count : -1;
                UpgradePreview nextLevel = placedSite != null ? placedSite.PreviewUpgrade() : null;
                Check($"In a No build cost world an open order and an upgrade need no materials (the open order lists {stillNeeded} kinds; " +
                      $"the upgrade to level {nextLevel?.Level} lists {nextLevel?.Needs.Count})",
                    stillNeeded == 0 && nextLevel != null && nextLevel.Needs.Count == 0);

                // 12. The review round: Build now never deletes what lies on the site: a stack lying there and a leftover chest's
                //     contents are kept (into the Builder's Hut, or dropped where they were).
                Vector3 at5 = Spot(-8f, 30f);
                ColonyBuilding itemsHut = DevHelpers.Place(BuildingTypes.ForagerHut.PrefabName, at5, facing, me);
                ConstructionModule itemsSite = itemsHut != null ? itemsHut.GetComponent<ConstructionModule>() : null;
                yield return WaitFor(() => itemsSite != null && itemsSite.Order != null && !string.IsNullOrEmpty(itemsHut.ColonyId), 20f);
                int ironBefore = Anywhere("Iron", at5, colony), bronzeBefore = Anywhere("Bronze", at5, colony);
                if (itemsSite != null && itemsSite.Order != null)
                {
                    Vector3 middle = Centre(itemsSite, itemsHut);
                    GameObject ironPrefab = ObjectDB.instance.GetItemPrefab("Iron");
                    ItemDrop.ItemData iron = ironPrefab.GetComponent<ItemDrop>().m_itemData.Clone();
                    iron.m_dropPrefab = ironPrefab;   // a prefab's own item data has none: the drop made nothing (the first run)
                    ItemDrop.DropItem(iron, 20, middle + right * 1.5f + Vector3.up * 0.3f, Quaternion.identity);
                    GameObject chestPrefab = ZNetScene.instance.GetPrefab("piece_chest_wood");
                    GameObject leftover = chestPrefab != null ? PiecePlacer.Place(chestPrefab, middle - right * 1.5f, facing, "vc_creativetest_gone", me) : null;
                    leftover?.GetComponent<Container>()?.GetInventory()?.AddItem(ObjectDB.instance.GetItemPrefab("Bronze"), 10);
                    yield return new WaitForSeconds(1f);
                    itemsSite.RequestCreative(CreativeAction.Build);
                    yield return WaitFor(() => itemsSite.Order != null && itemsSite.Order.State == BuildState.Done, 60f);
                    yield return new WaitForSeconds(3f);
                }
                int ironAfter = Anywhere("Iron", at5, colony), bronzeAfter = Anywhere("Bronze", at5, colony);
                Check($"Build now keeps what lies on the site: 20 Iron lying there and a leftover chest's 10 Bronze (Iron {ironBefore} then {ironAfter}, " +
                      $"Bronze {bronzeBefore} then {bronzeAfter}; built: {itemsSite != null && itemsSite.IsBuilt})",
                    itemsSite != null && itemsSite.IsBuilt && ironAfter == ironBefore + 20 && bronzeAfter == bronzeBefore + 10);

                // 13. The review round: Upgrade now where the next level's site is red (a player's wall inside it) is refused, as
                //     the builder's upgrade is: pieces left out and a repair blocked for good otherwise.
                townHall.SetLevelForTest(5);   // only the red site may refuse (back at level 1 from check 6, the old code was refused too)
                yield return new WaitForSeconds(1f);
                int foragerLevel = forager != null ? forager.Level : 0;
                GameObject wallPrefab = ZNetScene.instance.GetPrefab("woodwall");
                GameObject playersWall = forager != null && wallPrefab != null
                    ? PiecePlacer.Place(wallPrefab, Centre(foragerSite, forager), facing, "", me) : null;
                yield return new WaitForSeconds(1f);
                UpgradePreview red = foragerSite != null ? foragerSite.PreviewUpgrade() : null;
                foragerSite?.RequestCreative(CreativeAction.Upgrade);
                bool started = false;
                for (float t = 0f; t < 5f; t += 0.25f)
                {
                    yield return new WaitForSeconds(0.25f);
                    started |= foragerSite != null && foragerSite.IsUpgrading;   // its review: an upgrade still going up after 5 s passed too
                }
                Check($"Upgrade now is refused while the next level's site is red (a player's wall inside: {(red != null ? red.Site.Colour.ToString() : "no preview")}; " +
                      $"an upgrade started: {started}; level {foragerLevel} then {(forager != null ? forager.Level : 0)})",
                    playersWall != null && red != null && red.Site.Colour == SiteColour.Red && !started && forager != null && forager.Level == foragerLevel);
                if (playersWall != null) ZNetScene.instance.Destroy(playersWall);

                // 15. Its review: Repair now leaves plants and crops on the site (a Farm's yard), as the builder's repair does. On the
                //     level-4 Lumberjack's Hut, which nothing else changes now (on the Forager's Hut, check 13's old-code upgrade stood in the way).
                if (placed != null) DevHelpers.ClearMonsters(placed.transform.position, RepairRules.MonsterRange + 10f);   // repairs wait for one to go
                var placedPieces = new List<Piece>();
                if (placed != null) Piece.GetAllPiecesInRadius(placed.transform.position, 30f, placedPieces);
                Piece top = placed != null
                    ? placedPieces.Where(p => p != null && PiecePlacer.SiteOf(p.gameObject) == placed.Id && p.GetComponent<WearNTear>() != null)
                        .OrderByDescending(p => p.transform.position.y).FirstOrDefault()
                    : null;
                string topName = top != null ? Utils.GetPrefabName(top.gameObject) : "";
                GameObject flowerPrefab = ZNetScene.instance.GetPrefab("Pickable_Dandelion");
                GameObject flower = flowerPrefab != null && placed != null
                    ? Object.Instantiate(flowerPrefab, Centre(placedSite, placed) + right * 1.5f, Quaternion.identity) : null;
                if (top != null)
                {
                    ZNetView topView = top.GetComponent<ZNetView>();
                    if (topView != null && topView.IsValid() && !topView.IsOwner()) topView.ClaimOwnership();
                    ZNetScene.instance.Destroy(top.gameObject);
                }
                yield return new WaitForSeconds(1f);
                placedSite?.CheckForRepair();   // as the minute's check would
                bool repairOpen = placedSite != null && placedSite.Order != null && placedSite.Order.Kind == OrderKind.Repair
                                  && placedSite.Order.State != BuildState.Done;
                placedSite?.RequestCreative(CreativeAction.Repair);
                yield return WaitFor(() => placedSite != null && placedSite.Order != null && placedSite.Order.Kind == OrderKind.Repair
                                           && placedSite.Order.State == BuildState.Done, 30f);
                yield return new WaitForSeconds(2f);
                Check($"Repair now puts a fallen piece back and leaves a plant on the site (knocked off: '{topName}'; a repair opened: {repairOpen}; " +
                      $"then {placedSite?.Order?.Kind} {placedSite?.Order?.State}; the plant still there: {flower != null})",
                    topName.Length > 0 && repairOpen && placedSite.Order != null && placedSite.Order.Kind == OrderKind.Repair
                    && placedSite.Order.State == BuildState.Done && flower != null);

                // 16. The review round's full run: a settler standing where a building goes up at once is moved out first, as the
                //     builder moves one (one stayed wedged under a hut's floor for 20 minutes). Checked after: no piece covers them.
                Vector3 at7 = Spot(-52f, -4f);
                ColonyBuilding wedgeHut = DevHelpers.Place(BuildingTypes.ForagerHut.PrefabName, at7, facing, me);
                ConstructionModule wedgeSite = wedgeHut != null ? wedgeHut.GetComponent<ConstructionModule>() : null;
                yield return WaitFor(() => wedgeSite != null && wedgeSite.Order != null && !string.IsNullOrEmpty(wedgeHut.ColonyId), 20f);
                Citizen standing = Citizen.All.FirstOrDefault(c => c != null && c.IsOwner && c.Record.ColonyId == colony && c.Record.Job != SaveKeys.BuilderJob);
                CitizenAI standingAi = standing != null ? standing.GetComponent<CitizenAI>() : null;
                bool CoveredByThePlan() => standing != null && wedgeSite != null && wedgeSite.Plan.Any(p =>
                {
                    GameObject prefab = p.IsValid ? PiecePlacer.Prefab(p.Prefab) : null;
                    return prefab != null && PiecePlacer.Covers(prefab, new Vector3(p.Position.X, p.Position.Y, p.Position.Z),
                        new Quaternion(p.Rotation.X, p.Rotation.Y, p.Rotation.Z, p.Rotation.W), standing.Body);
                });
                bool coveredFirst = false;
                if (wedgeSite != null && wedgeSite.Order != null && standingAi != null)
                {
                    standingAi.PauseForTest(150f);   // they'd walk off on their own: here they stand while it goes up
                    // Where a floor goes (what wedged Knut), else any piece of it.
                    PlannedPiece under = wedgeSite.Plan.FirstOrDefault(p => p.IsValid && p.Prefab.Contains("floor")) ?? wedgeSite.Plan.FirstOrDefault(p => p.IsValid);
                    Vector3 spot = under != null ? new Vector3(under.Position.X, 0f, under.Position.Z) : Centre(wedgeSite, wedgeHut);
                    spot.y = ZoneSystem.instance.GetGroundHeight(spot);
                    standingAi.PlaceForTest(spot);
                    yield return new WaitForSeconds(1f);
                    coveredFirst = CoveredByThePlan();
                    wedgeSite.RequestCreative(CreativeAction.Build);
                    yield return WaitFor(() => wedgeSite.Order != null && wedgeSite.Order.State == BuildState.Done, 90f);
                    yield return new WaitForSeconds(2f);
                }
                bool coveredAfter = CoveredByThePlan();
                standingAi?.PauseForTest(0f);
                Check($"A settler standing where a building goes up at once is moved out first ({standing?.CitizenName}: where a piece goes first " +
                      $"{coveredFirst}; built {wedgeSite?.Order?.State}; a piece over them after {coveredAfter})",
                    coveredFirst && wedgeSite.Order != null && wedgeSite.Order.State == BuildState.Done && !coveredAfter);

                // 17. 1.5.0 (the review round's pick, the player's yes): a Storehouse placed already built still gets Hugin's
                //     storehouse tip (the one that says to hire a courier); placed built, it never reached the tips' check. A tip
                //     comes only to a player within 40 m: the player stands by it meanwhile, then goes back.
                Vector3 at8 = Spot(-52f, 26f);
                GotoCommand.Go(player, Spot(-40f, 26f), at8);
                yield return WaitFor(() => Utils.DistanceXZ(player.transform.position, Spot(-40f, 26f)) < 4f, 15f);
                SelfTest.ForgetTipForTest(player, HuginTips.Storehouse);
                HuginTips.Unqueue(HuginTips.Storehouse);
                HuginTips.Unqueue(HuginTips.Builder);   // an earlier check's may wait there
                CreativePlacing.NoteForTest(BuildingTypes.Storehouse, 1, at8);
                ColonyBuilding builtStore = DevHelpers.Place(BuildingTypes.Storehouse.PrefabName, at8, facing, me,
                    PlacementOffset.StartFor(BuildingTypes.Storehouse.PrefabName));
                ConstructionModule builtStoreSite = builtStore != null ? builtStore.GetComponent<ConstructionModule>() : null;
                yield return WaitFor(() => builtStoreSite != null && builtStoreSite.IsBuilt && builtStore.Level >= 1 && HuginTips.Queued(HuginTips.Storehouse), 60f);
                bool storeTip = HuginTips.Queued(HuginTips.Storehouse);
                bool builderTip = HuginTips.Queued(HuginTips.Builder);
                HuginTips.Unqueue(HuginTips.Storehouse);
                GotoCommand.Go(player, origin, origin + forward * 5f);
                yield return WaitFor(() => Utils.DistanceXZ(player.transform.position, origin) < 4f, 15f);
                Check($"A Storehouse placed already built still gets Hugin's storehouse tip, never the builder's (built {builtStoreSite != null && builtStoreSite.IsBuilt}, " +
                      $"storehouse tip {storeTip}, builder tip {builderTip})", builtStoreSite != null && builtStoreSite.IsBuilt && storeTip && !builderTip);

                // 9. Meals off only while the world has No build cost; builders free only then.
                townHall.RequestCreative(HallCreativeAction.EatOff, 1);
                yield return new WaitForSeconds(1f);
                bool eatsFree = Creative.SettlersEat(colony);
                WorldFree(false);
                yield return new WaitForSeconds(0.5f);
                bool eatsAfter = Creative.SettlersEat(colony), freeAfter = Creative.BuildersFree();
                Check($"Meals off spare the colony only while the world has No build cost (eat then: {eatsFree}; with the setting gone: {eatsAfter}, " +
                      $"builders free: {freeAfter})", !eatsFree && eatsAfter == ColonyConfig.SettlersEat.Value && !freeAfter);

                // 10. The refusals: a survival world and a sender who isn't an admin.
                Creative.TestNonAdmin = true;
                ColonyBuilding refused = DevHelpers.Place(BuildingTypes.LumberjackHut.PrefabName, Spot(10f, 36f), facing, me);
                ConstructionModule refusedSite = refused != null ? refused.GetComponent<ConstructionModule>() : null;
                yield return WaitFor(() => refusedSite != null && refusedSite.Order != null, 10f);
                int doneBefore = refusedSite?.Order?.DoneCount ?? -1;
                int placedLevel = placed != null ? placed.Level : 0;
                int hallLevel = hall.Level;
                int settlersBefore = Settlers(colony);
                refusedSite?.RequestCreative(CreativeAction.Build);
                placedSite?.RequestCreative(CreativeAction.Upgrade);
                townHall.RequestCreative(HallCreativeAction.SetLevel, 4);
                townHall.RequestCreative(HallCreativeAction.AddSettlers, 1);
                townHall.FinishAllNow();
                yield return new WaitForSeconds(5f);
                Check($"Without No build cost and not an admin, every creative request is refused (the open order {doneBefore} then " +
                      $"{refusedSite?.Order?.DoneCount} up; a hut level {placedLevel} then {(placed != null ? placed.Level : 0)}; the Town Hall {hallLevel} " +
                      $"then {hall.Level}; settlers {settlersBefore} then {Settlers(colony)})",
                    refusedSite != null && refusedSite.Order != null && refusedSite.Order.State != BuildState.Done && refusedSite.Order.DoneCount == doneBefore
                    && placed != null && placed.Level == placedLevel && hall.Level == hallLevel && Settlers(colony) == settlersBefore);
                Creative.TestNonAdmin = false;

                // 11b. Not creative: the creative buttons are hidden.
                Windows.Hut.Open(placed);
                yield return new WaitForSeconds(1f);
                string hutText = Windows.Hut.TextForTest();
                Windows.Hut.Close();
                Check($"Without No build cost and free building, a hut's window shows no creative buttons (creative: {Creative.LocalIsCreative()})",
                    !Creative.LocalIsCreative() && !hutText.Contains(Localization.instance.Localize("$vc_creative_upgrade")));
            }
            finally
            {
                Creative.TestNonAdmin = false;
                WorldFree(freeWas);
                ColonyConfig.MaxSettlers.Value = maxSettlersWas;
                ColonyConfig.NewcomersWalkIn.Value = walkInWas;
                EnvMan.instance.m_debugTimeOfDay = false;
            }
            yield return new WaitForSeconds(1f);
            Log.Event($"vc_creativetest: removed what it made: {DevHelpers.WipeColonies(origin, 110f)}");
            Log.Event($"vc_creativetest: done — {s_pass} passed, {s_fail} failed");
        }
    }
}
