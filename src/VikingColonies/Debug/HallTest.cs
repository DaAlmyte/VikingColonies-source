using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Jotunn.Entities;
using UnityEngine;
using VikingColonies.Buildings;
using VikingColonies.Citizens;
using VikingColonies.Config;
using VikingColonies.Construction;
using VikingColonies.Core;
using VikingColonies.Gui;

namespace VikingColonies.DevTools
{
    /// <summary>vc_halltest (test worlds): Town Hall levels (1.4), the Hall of Trophies, checked in the game in a minute.</summary>
    internal sealed class HallTestCommand : ConsoleCommand
    {
        public override string Name => "vc_halltest";
        public override string Help => "VikingColonies (test worlds): place a Town Hall here and raise it twice (a trophy in its chest, then one on an item " +
                                       "stand), checking the area, the settler limit, the hut cap, the chest and the Hall tab. Removes what it made.";
        public override bool IsCheat => true;

        public override void Run(string[] args)
        {
            Player p = Player.m_localPlayer;
            if (p == null || ZNet.instance == null || !TestWorlds.IsTestWorld(ZNet.instance.GetWorldName()))
            {
                BuildCommand.Print("vc_halltest: test worlds only");
                return;
            }
            Plugin.Instance.StartCoroutine(HallTest.Run(p));
        }
    }

    internal static class HallTest
    {
        private static int s_pass, s_fail;

        public static IEnumerator Run(Player player)
        {
            s_pass = s_fail = 0;
            Log.Event("vc_halltest: starting");
            if (!player.InGodMode()) player.SetGodMode(true);   // never off: the test character has 1 health
            EnvMan.instance.m_debugTimeOfDay = true;
            EnvMan.instance.m_debugTime = 0.4f;
            Vector3 origin = player.transform.position, forward = player.transform.forward, right = player.transform.right;
            forward.y = right.y = 0f;
            forward.Normalize();
            right.Normalize();
            var before = new HashSet<string>(Citizen.All.Where(c => c != null).Select(c => c.Id));
            var made = new List<GameObject>();
            bool levelsWas = ColonyConfig.HallLevels.Value, walkInWas = ColonyConfig.NewcomersWalkIn.Value;
            bool beatenWas = ColonyConfig.HallBeatenBossesCount.Value;
            const string testKey = "defeated_goblinking";
            bool keyWas = ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(testKey);
            try
            {
                ColonyConfig.HallLevels.Value = true;
                ColonyConfig.NewcomersWalkIn.Value = false;
                ColonyConfig.HallBeatenBossesCount.Value = false;   // the test world may have beaten them: trophies only, until step 6
                DevHelpers.ClearMonsters(origin, 60f, animalsToo: true);
                ColonyBuilding hall = DevHelpers.Place(BuildingTypes.TownHall.PrefabName, origin + forward * 6f, Quaternion.LookRotation(forward), player.GetPlayerID());
                if (hall != null) made.Add(hall.gameObject);
                DevHelpers.KeepNothing(hall);
                TownHallModule townHall = hall != null ? hall.GetComponent<TownHallModule>() : null;
                for (float t = 0f; townHall != null && !townHall.Founded && t < 20f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                if (townHall == null)
                {
                    Check("A Town Hall is placed", false);
                    yield break;
                }
                string colony = hall.ColonyId;
                CraftingStation station = hall.GetComponent<CraftingStation>();
                Inventory chest = hall.GetComponent<Container>().GetInventory();
                int rowsAtOne = hall.Storage.Rows;
                float baseRadius = ColonyConfig.ColonyRadius.Value;

                // 1. A new Town Hall is level 1: the colony's area as before 1.4, room for 10.
                yield return new WaitForSeconds(0.5f);
                Check($"A new Town Hall is level 1 with the colony's base area and room for {ColonyConfig.SettlersAtLevel1.Value} (level {hall.Level}, " +
                      $"area {HallRules.RadiusOf(colony):0} m, station {station.m_rangeBuild:0} m, room {townHall.NewcomerRules.HallSettlers})",
                    hall.Level == 1 && Mathf.Approximately(HallRules.RadiusOf(colony), baseRadius)
                    && Mathf.Approximately(station.m_rangeBuild, ColonyRules.HutBuildRange(baseRadius))
                    && townHall.NewcomerRules.HallSettlers == ColonyConfig.SettlersAtLevel1.Value);

                // 2. A level-1 hut can't go to level 2 while the hall is level 1 (review of 1.4: huts go as high as the hall).
                ColonyBuilding hut = DevHelpers.Place(BuildingTypes.LumberjackHut.PrefabName, hall.transform.position + right * 14f, Quaternion.LookRotation(forward), player.GetPlayerID());
                if (hut != null) made.Add(hut.gameObject);
                ConstructionModule site = hut != null ? hut.GetComponent<ConstructionModule>() : null;
                for (float t = 0f; site != null && site.Order == null && t < 15f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                if (site != null && site.Order != null) site.FinishNow();
                for (float t = 0f; site != null && !site.IsBuilt && t < 40f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                UpgradeLock capped = site != null ? site.UpgradeCheck() : UpgradeLock.None;
                Check($"A level-1 hut waits for the Town Hall to go to level 2 ({capped})", capped == UpgradeLock.TownHallTooLow);

                // 3. Raise refused without the trophy, and the Hall tab says what's missing.
                Windows.TownHall.Open(hall);
                Windows.TownHall.ShowTabForTest("hall");
                townHall.RequestRaise();
                yield return new WaitForSeconds(1f);
                string tab = Windows.TownHall.IsOpen ? DevHelpers.Plain(Windows.TownHall.TextForTest()) : "(closed)";
                string boss = Localization.instance.Localize("$enemy_eikthyr");
                Check($"Without Eikthyr's trophy the hall isn't raised, and the Hall tab says why (level {hall.Level}; '{tab}')",
                    hall.Level == 1 && tab.Contains(string.Format(Localization.instance.Localize("$vc_hall_raise_no_trophy"), boss))
                    && tab.Contains(string.Format(Localization.instance.Localize("$vc_hall_header"), 1, TownHallLevels.MaxLevel)));

                // 4. The trophy and the materials in the chest: level 2, the materials used up, the trophy still there.
                int celebrated = TownHallModule.CelebratedForTest;
                chest.AddItem(ObjectDB.instance.GetItemPrefab("TrophyEikthyr"), 1);
                foreach (KeyValuePair<string, int> item in HallRules.Cost(2)) chest.AddItem(ObjectDB.instance.GetItemPrefab(item.Key), item.Value);
                for (float t = 0f; townHall.RaiseCheck() != RaiseLock.None && t < 5f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                townHall.RequestRaise();
                for (float t = 0f; hall.Level < 2 && t < 5f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                yield return new WaitForSeconds(2.5f);   // the station follows every 2 s, the chest grows every few seconds
                Dictionary<string, int> left = hall.Storage.Contents();
                bool costGone = HallRules.Cost(2).All(c => !left.ContainsKey(c.Key));
                // Among the newest lines (the hut's 'built' may come just after).
                SagaEntry newest = townHall.Saga.Page(0, 3).FirstOrDefault(e => e.Kind == "raised");
                float radius2 = ColonyAreas.CirclesOf(colony).Select(c => c.Radius).DefaultIfEmpty(0f).First();
                Check($"With Eikthyr's trophy and the materials in the chest the hall rises to level 2 (level {hall.Level}, cost gone {costGone}, " +
                      $"trophy kept {left.ContainsKey("TrophyEikthyr")}, saga '{newest?.Kind} {string.Join(" ", newest?.Args ?? new string[0])}')",
                    hall.Level == 2 && costGone && left.ContainsKey("TrophyEikthyr") && newest != null && newest.Kind == "raised");
                Check($"Level 2 widens the colony and makes room for more (area {radius2:0} m, station {station.m_rangeBuild:0} m, " +
                      $"room {townHall.NewcomerRules.HallSettlers}, chest rows {rowsAtOne} -> {hall.Storage.Rows})",
                    Mathf.Approximately(radius2, baseRadius + ColonyConfig.HallRadiusPerLevel.Value)
                    && Mathf.Approximately(station.m_rangeBuild, ColonyRules.HutBuildRange(baseRadius + ColonyConfig.HallRadiusPerLevel.Value))
                    && townHall.NewcomerRules.HallSettlers == HallRules.PerksAt(2).Settlers
                    && hall.Storage.Rows == rowsAtOne + ColonyConfig.RowsPerLevel.Value);
                UpgradeLock freed = site != null ? site.UpgradeCheck() : UpgradeLock.TownHallTooLow;
                // The test colony has no Builder's Hut: that is all it waits for now.
                Check($"At hall level 2 the level-1 hut may go to level 2 as far as the hall goes ({freed})", freed == UpgradeLock.NoBuilderHut);
                Check($"The raise is celebrated here once ({TownHallModule.CelebratedForTest - celebrated})", TownHallModule.CelebratedForTest - celebrated == 1);
                // A second press for the level already raised (a double click) raises nothing.
                townHall.RaiseForTest(seenLevel: 1);
                yield return new WaitForSeconds(1f);
                Check($"A second Raise for the level already reached does nothing (level {hall.Level})", hall.Level == 2);
                Check($"The raise effect is one of the game's ('{TownHallModule.RaiseEffect}')", ZNetScene.instance.GetPrefab(TownHallModule.RaiseEffect) != null);

                // 5. Level 3 needs the Elder's and Bonemass's trophies: the Elder's on an item stand by the hall (not in the
                //    chest) and the materials aren't enough; with Bonemass's in the chest too, the hall rises to level 3.
                GameObject standGo = DevHelpers.Spawn("itemstandh", hall.transform.position + right * 4f);
                if (standGo != null) made.Add(standGo);
                ZNetView standView = standGo != null ? standGo.GetComponent<ZNetView>() : null;
                if (standView != null && standView.IsValid()) standView.GetZDO().Set(ZDOVars.s_item, "TrophyTheElder".GetStableHashCode());   // as the game attaches it
                foreach (KeyValuePair<string, int> item in HallRules.Cost(3)) chest.AddItem(ObjectDB.instance.GetItemPrefab(item.Key), item.Value);
                yield return new WaitForSeconds(1f);
                TrophyPlace place = townHall.WhereShown("TrophyTheElder");
                townHall.RequestRaise();
                yield return new WaitForSeconds(1f);
                string bonemass = Localization.instance.Localize("$enemy_bonemass");
                string refused = Windows.TownHall.IsOpen ? DevHelpers.Plain(Windows.TownHall.TextForTest()) : "(closed)";
                Check($"With only the Elder's trophy shown the hall stays at level 2 and asks for Bonemass's (seen {place}, level {hall.Level}; '{refused}')",
                    place == TrophyPlace.Stand && hall.Level == 2
                    && refused.Contains(string.Format(Localization.instance.Localize("$vc_hall_raise_no_trophy"), bonemass)));
                chest.AddItem(ObjectDB.instance.GetItemPrefab("TrophyBonemass"), 1);
                yield return new WaitForSeconds(1f);
                townHall.RequestRaise();
                for (float t = 0f; hall.Level < 3 && t < 5f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                Check($"With Bonemass's trophy in the chest as well the hall rises to level 3 (level {hall.Level})", hall.Level == 3);
                yield return new WaitForSeconds(1f);   // the window refreshes twice a second (the first run read it just before)
                tab = Windows.TownHall.IsOpen ? DevHelpers.Plain(Windows.TownHall.TextForTest()) : "(closed)";
                new ShotCommand().Run(new[] { "halltab" });   // the Hall tab at level 3, for a look
                yield return new WaitForSeconds(0.5f);
                string elder = Localization.instance.Localize("$enemy_gdking");
                Check($"The Hall tab lists the trophies shown and what level 4 needs ('{tab}')",
                    tab.Contains(string.Format(Localization.instance.Localize("$vc_hall_needs"), 4)) && tab.Contains(boss) && tab.Contains(elder));
                Windows.TownHall.Close();

                // 6. A Forsaken the world has beaten counts as shown (review of 1.4: trophies on the sacrificial stones stay there).
                ColonyConfig.HallBeatenBossesCount.Value = true;
                if (!keyWas) ZoneSystem.instance?.SetGlobalKey(testKey);
                for (float t = 0f; ZoneSystem.instance != null && !ZoneSystem.instance.GetGlobalKey(testKey) && t < 3f; t += 0.25f)
                    yield return new WaitForSeconds(0.25f);   // the key goes by way of the server
                TrophyPlace beaten = townHall.WhereShown("TrophyGoblinKing");
                ColonyConfig.HallBeatenBossesCount.Value = false;
                TrophyPlace notCounted = townHall.WhereShown("TrophyGoblinKing");
                if (!keyWas) ZoneSystem.instance?.RemoveGlobalKey(testKey);
                Check($"Yagluth beaten in this world counts for the hall, and not with BeatenBossesCount off ({beaten}, {notCounted})",
                    beaten == TrophyPlace.Beaten && notCounted == TrophyPlace.None);

                // 7. Levels off: the colony as before 1.4, whatever level the hall reached.
                ColonyConfig.HallLevels.Value = false;
                Check($"With levels off the colony has its base area again and huts aren't capped (area {HallRules.RadiusOf(colony):0} m, " +
                      $"cap {HallRules.PerksOf(colony).HutCap})",
                    Mathf.Approximately(HallRules.RadiusOf(colony), baseRadius) && HallRules.PerksOf(colony).HutCap == HutLevels.MaxLevel);
                ColonyConfig.HallLevels.Value = true;
            }
            finally
            {
                foreach (Citizen c in Citizen.All.Where(c => c != null && !before.Contains(c.Id)).ToList()) Remove(c.gameObject);
                foreach (GameObject go in made.Where(g => g != null)) Remove(go);
                EnvMan.instance.m_debugTimeOfDay = false;
                ColonyConfig.HallLevels.Value = levelsWas;
                ColonyConfig.NewcomersWalkIn.Value = walkInWas;
                ColonyConfig.HallBeatenBossesCount.Value = beatenWas;
                if (!keyWas && ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(testKey)) ZoneSystem.instance.RemoveGlobalKey(testKey);
                Player me = Player.m_localPlayer;
                if (me != null && !me.InGodMode()) me.SetGodMode(true);
                Log.Event($"vc_halltest: done — {s_pass} passed, {s_fail} failed");
                me?.Message(MessageHud.MessageType.Center, $"vc_halltest: {s_pass} passed, {s_fail} failed");
            }
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
            Log.Event($"vc_halltest: {(ok ? "PASS" : "FAIL")} {what}");
        }
    }
}
