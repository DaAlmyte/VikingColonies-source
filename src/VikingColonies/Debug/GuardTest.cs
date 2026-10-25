using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Jotunn.Entities;
using UnityEngine;
using VikingColonies.AI;
using VikingColonies.Buildings;
using VikingColonies.Citizens;
using VikingColonies.Config;
using VikingColonies.Construction;
using VikingColonies.Core;
using VikingColonies.Jobs;

namespace VikingColonies.DevTools
{
    /// <summary>vc_guardtest (test worlds): the Watchtower and its guard (1.1) checked in the game in a few minutes.</summary>
    internal sealed class GuardTestCommand : ConsoleCommand
    {
        public override string Name => "vc_guardtest";
        public override string Help => "VikingColonies (test worlds): place a Town Hall and two Watchtowers here and check the guards of 1.1 (the border " +
                                       "and its reach, a guard's weapon, killing a Greyling, leaving a deer, the leash, night and raids, the mood, no weapon). " +
                                       "Removes what it made.";
        public override bool IsCheat => true;

        public override void Run(string[] args)
        {
            Player p = Player.m_localPlayer;
            if (p == null || ZNet.instance == null || !TestWorlds.IsTestWorld(ZNet.instance.GetWorldName()))
            {
                BuildCommand.Print("vc_guardtest: test worlds only");
                return;
            }
            Plugin.Instance.StartCoroutine(GuardTest.Run(p));
        }
    }

    internal static class GuardTest
    {
        private static int s_pass, s_fail;
        private static readonly HarmonyLib.AccessTools.FieldRef<Character, HitData> s_lastHit = HarmonyLib.AccessTools.FieldRefAccess<Character, HitData>("m_lastHit");
        private static readonly HarmonyLib.AccessTools.FieldRef<MonsterAI, Character> s_monsterTarget = HarmonyLib.AccessTools.FieldRefAccess<MonsterAI, Character>("m_targetCreature");
        private static readonly HarmonyLib.AccessTools.FieldRef<Turret, Character> s_turretTarget = HarmonyLib.AccessTools.FieldRefAccess<Turret, Character>("m_target");

        public static IEnumerator Run(Player player)
        {
            s_pass = s_fail = 0;
            Log.Event("vc_guardtest: starting");
            if (!player.InGodMode()) player.SetGodMode(true);
            EnvMan.instance.m_debugTimeOfDay = true;
            EnvMan.instance.m_debugTime = 0.4f;   // morning: no breakfast, no evening
            Vector3 origin = player.transform.position, forward = player.transform.forward, right = player.transform.right;
            forward.y = right.y = 0f;
            forward.Normalize();
            right.Normalize();
            var before = new HashSet<string>(Citizen.All.Where(c => c != null).Select(c => c.Id));
            var spawned = new List<GameObject>();
            var buildings = new List<ColonyBuilding>();
            bool walkInWas = ColonyConfig.NewcomersWalkIn.Value;
            float radiusWas = ColonyConfig.ColonyRadius.Value, reachWas = ColonyConfig.MaxReach.Value;
            int friendlyBefore = CitizenPatches.FriendlyHitsForTest;
            void Cleanup()
            {
                foreach (Citizen c in Citizen.All.Where(c => c != null && !before.Contains(c.Id)).ToList()) Remove(c.gameObject);
                foreach (GameObject go in spawned.Where(g => g != null)) Remove(go);
                foreach (ColonyBuilding b in buildings.Where(b => b != null)) Remove(b.gameObject);
                Log.Event("vc_guardtest: cleaned up");
            }
            try
            {
                ColonyConfig.NewcomersWalkIn.Value = false;
                ColonyConfig.ColonyRadius.Value = 20f;   // a small Town Hall circle, so the towers' circles reach past it on the test ground
                DevHelpers.ClearMonsters(origin, 90f, animalsToo: true);
                ColonyBuilding hall = DevHelpers.Place(BuildingTypes.TownHall.PrefabName, origin + forward * 8f, Quaternion.LookRotation(forward), player.GetPlayerID());
                if (hall != null) buildings.Add(hall);
                DevHelpers.KeepNothing(hall);
                TownHallModule townHall = hall != null ? hall.GetComponent<TownHallModule>() : null;
                for (float t = 0f; townHall != null && !townHall.Founded && t < 20f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                string colony = hall != null ? hall.ColonyId : "";

                // 1. A Watchtower is put up, hires a guard, and he keeps watch with a copy of the club in its chest.
                Vector3 towerAt = hall != null ? hall.transform.position + forward * 16f : origin;
                ColonyBuilding tower = DevHelpers.Place(BuildingTypes.Watchtower.PrefabName, towerAt, Quaternion.LookRotation(forward), player.GetPlayerID());
                if (tower != null) buildings.Add(tower);
                yield return Finish(tower);
                Citizen guard = null;
                WorkerModule work = tower != null ? tower.GetComponent<WorkerModule>() : null;
                for (float t = 0f; work != null && guard == null && t < 30f; t += 0.5f)
                {
                    yield return new WaitForSeconds(0.5f);
                    if (work.FindWorker(out Citizen found, out _) == WorkerModule.WorkerState.Loaded) guard = found;
                }
                if (tower == null || guard == null)
                {
                    Check($"A Watchtower is put up and hires a guard (tower {tower != null}, guard {guard != null})", false);
                    Cleanup();
                    yield break;
                }
                Inventory chest = tower.GetComponent<Container>().GetInventory();
                chest.AddItem(ObjectDB.instance.GetItemPrefab("Club"), 1);
                chest.AddItem(ObjectDB.instance.GetItemPrefab("ShieldWood"), 1);   // 1.2: held up between blows
                CitizenAI ai = guard.GetComponent<CitizenAI>();
                GuardJob Job() => ai != null ? ai.JobForTest as GuardJob : null;
                for (float t = 0f; !(Job()?.Armed ?? false) && t < 15f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                int clubs = chest.GetAllItems().Where(i => i.m_dropPrefab != null && i.m_dropPrefab.name == "Club").Sum(i => i.m_stack);
                Check($"A Watchtower hires a guard who keeps watch with a copy of the club in its chest ({guard.CitizenName}, holding '{Job()?.WeaponPrefab}', " +
                      $"clubs in the chest {clubs}, '{Localization.instance.Localize(guard.Status ?? "")}')", Job() != null && Job().WeaponPrefab == "Club" && clubs == 1);

                // 2. Its circle is part of the colony's area: a hut past the Town Hall's circle but by the tower joins the colony.
                WatchtowerModule module = tower.GetComponent<WatchtowerModule>();
                CraftingStation station = tower.GetComponent<CraftingStation>();
                for (float t = 0f; !module.Counts && t < 10f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                yield return new WaitForSeconds(2.5f);   // the station's range is set every 2 s
                Vector3 byTower = towerAt + right * 18f, beyond = towerAt + forward * 30f;
                Check($"A finished Watchtower adds its circle to the colony's area (counts {module.Counts}, station range {station.m_rangeBuild:0} m, " +
                      $"by it: '{ColonyAreas.ColonyAt(byTower)}', beyond it: '{ColonyAreas.ColonyAt(beyond)}')",
                    module.Counts && station.m_rangeBuild > 15f && ColonyAreas.ColonyAt(byTower) == colony && ColonyAreas.ColonyAt(beyond) == null);
                ColonyBuilding farHut = DevHelpers.Place(BuildingTypes.Longhouse.PrefabName, byTower, Quaternion.LookRotation(forward), player.GetPlayerID());
                if (farHut != null) buildings.Add(farHut);
                for (float t = 0f; farHut != null && farHut.ColonyId != colony && t < 15f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                Check($"A hut past the Town Hall's circle but by the tower joins its colony ({(hall != null && farHut != null ? Utils.DistanceXZ(farHut.transform.position, hall.transform.position) : 0f):0} m " +
                      $"from the hall, colony '{farHut?.ColonyId}')", farHut != null && farHut.ColonyId == colony);

                // 3. Tower by tower: a second tower in the first one's circle pushes the border further, but never past Colony.MaxReach.
                Vector3 secondAt = towerAt + forward * 22f;
                ColonyBuilding second = DevHelpers.Place(BuildingTypes.Watchtower.PrefabName, secondAt, Quaternion.LookRotation(forward), player.GetPlayerID());
                if (second != null)
                {
                    buildings.Add(second);
                    second.GetComponent<WorkerModule>()?.RequestSetAutoHire(false);   // one guard is enough here
                }
                for (float t = 0f; second != null && second.ColonyId != colony && t < 15f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                yield return Finish(second);
                WatchtowerModule secondModule = second != null ? second.GetComponent<WatchtowerModule>() : null;
                for (float t = 0f; secondModule != null && !secondModule.Counts && t < 10f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                Vector3 bySecond = secondAt + forward * 20f;   // 58 m from the hall
                bool chained = secondModule != null && secondModule.Counts && ColonyAreas.ColonyAt(bySecond) == colony;
                ColonyConfig.MaxReach.Value = 50f;   // the second tower stands 38 m out: 12 m of its circle is left, the first one's whole
                yield return new WaitForSeconds(0.5f);
                float cut = secondModule != null && secondModule.Circle != null ? secondModule.Circle.Value.Radius : -1f;
                bool capped = cut > 0f && cut <= 12.01f && module.Counts && ColonyAreas.ColonyAt(bySecond) == null && ColonyAreas.ColonyAt(byTower) == colony;
                ColonyConfig.MaxReach.Value = reachWas;
                Check($"A tower in another tower's circle joins and pushes the border further (joined '{second?.ColonyId}', counts then: {chained}), " +
                      $"but not past Colony.MaxReach (with 50 m its circle is cut to {cut:0} m: {capped})", second != null && second.ColonyId == colony && chained && capped);

                // 4. A Greyling by the far hut is killed within a minute, and his hits grow his Fighting. A settler and the player
                // right beside it, and a wall behind it in his swing, take nothing from him (review of 1.1).
                float leash = GuardLevels.Radius(tower.Level) + ColonyConfig.ChaseBeyondPatrol.Value;
                float farthest = 0f;
                Vector3 greylingAt = byTower + forward * 3f;
                Vector3 away = greylingAt - towerAt;
                away.y = 0f;
                away.Normalize();
                Vector3 side = Vector3.Cross(Vector3.up, away);
                Character greyling = SpawnCreature("Greyling", greylingAt, spawned);
                bool greylingCame = greyling != null;   // once dead the game takes it away: it is == null then
                GameObject wall = DevHelpers.Spawn("woodwall", greylingAt + away * 1.3f);
                if (wall != null)
                {
                    wall.transform.rotation = Quaternion.LookRotation(away);
                    spawned.Add(wall);
                }
                WearNTear wallWear = wall != null ? wall.GetComponent<WearNTear>() : null;
                float wallWas = wallWear != null ? wallWear.GetHealthPercentage() : -1f;
                Buildings.BuildingSafetyPatches.TrackHitsForTest = true;   // the Greyling may hit the wall too: what counts is who did
                Citizen bystander = Citizen.All.FirstOrDefault(c => c != null && c != guard && c.ColonyId == colony);
                if (bystander != null)
                {
                    Vector3 at = greylingAt + side * 1.3f;
                    at.y = ZoneSystem.instance.GetGroundHeight(at);
                    bystander.transform.position = at;
                    bystander.GetComponent<CitizenAI>()?.PauseForTest(70f);   // he stays put beside the fight
                }
                float bystanderWas = bystander != null ? bystander.Body.GetHealth() : 0f;
                Vector3 playerWasAt = player.transform.position;
                Vector3 beside = greylingAt - side * 1.3f;
                beside.y = ZoneSystem.instance.GetGroundHeight(beside) + 0.3f;
                player.TeleportTo(beside, player.transform.rotation, distantTeleport: false);
                yield return new WaitForSeconds(0.5f);
                // The player stays in god mode: his blows on a friend are counted before it (FriendlyHitsForTest). Out of it, at the
                // test character's 1 health, a stray hit killed him (the sixth 1.3 run).
                float playerWas = player.GetHealth();
                int friendlyBeforeFight = CitizenPatches.FriendlyHitsForTest;
                float fightingWas = guard.Record.Fighting + guard.Record.FightingXp;
                int blocksWas = GuardJob.BlocksForTest;
                float guardWas = guard.Body.GetHealth(), guardLeast = guardWas;
                float tookFor = 0f;
                for (; greyling != null && !greyling.IsDead() && tookFor < 60f; tookFor += 0.5f)
                {
                    yield return new WaitForSeconds(0.5f);
                    farthest = Mathf.Max(farthest, Utils.DistanceXZ(guard.transform.position, towerAt));
                    guardLeast = Mathf.Min(guardLeast, guard.Body.GetHealth());
                }
                int blocks = GuardJob.BlocksForTest - blocksWas;
                float playerNow = player.GetHealth();
                float bystanderNow = bystander != null ? bystander.Body.GetHealth() : 0f;
                Character playerHitBy = s_lastHit(player)?.GetAttacker(), bystanderHitBy = bystander != null ? s_lastHit(bystander.Body)?.GetAttacker() : null;
                float wallNow = wallWear != null ? wallWear.GetHealthPercentage() : -1f;
                Character wallHitBy = Buildings.BuildingSafetyPatches.LastHitForTest(wallWear);
                Buildings.BuildingSafetyPatches.StopTrackingForTest();
                bystander?.GetComponent<CitizenAI>()?.PauseForTest(0f);
                player.TeleportTo(playerWasAt, player.transform.rotation, distantTeleport: false);
                float fightingNow = guard.Record.Fighting + guard.Record.FightingXp;
                Check($"The guard kills a Greyling by the far hut within a minute (took {tookFor:0} s, dead {greyling == null || greyling.IsDead()}), " +
                      $"and his hits grow his Fighting ({fightingWas:0.00} -> {fightingNow:0.00})",
                    greylingCame && (greyling == null || greyling.IsDead()) && tookFor < 60f && fightingNow > fightingWas);
                bool notByHim(float was, float now, Character hitBy) => now >= was - 0.01f || hitBy != guard.Body;
                Check($"Nothing beside the fight takes a blow from him: the player (health {playerWas:0} -> {playerNow:0}, last hit by '{playerHitBy?.m_name}'), " +
                      $"{bystander?.CitizenName} ({bystanderWas:0} -> {bystanderNow:0}, last hit by '{bystanderHitBy?.m_name}'), the wall behind it " +
                      $"({wallWas:P0} -> {wallNow:P0}, last hit by '{wallHitBy?.m_name}'); blows that reached a friend: {CitizenPatches.FriendlyHitsForTest - friendlyBeforeFight}",
                    bystander != null && wallWear != null && notByHim(playerWas, playerNow, playerHitBy) && notByHim(bystanderWas, bystanderNow, bystanderHitBy)
                    && notByHim(wallWas, wallNow, wallHitBy) && CitizenPatches.FriendlyHitsForTest == friendlyBeforeFight);
                // 1.4.1: a settler's blow never hurts a building, whatever it is aimed at.
                if (wallWear != null && guard != null)
                {
                    float wallBefore = wallWear.GetHealthPercentage();
                    int blockedBefore = Buildings.BuildingSafetyPatches.SettlerHitsBlockedForTest;
                    var blow = new HitData();
                    blow.m_damage.m_slash = 80f;
                    blow.m_point = wallWear.transform.position;
                    blow.SetAttacker(guard.Body);
                    wallWear.Damage(blow);
                    yield return new WaitForSeconds(0.5f);
                    float wallAfter = wallWear.GetHealthPercentage();
                    Check($"A guard's blow on a wall does nothing to it ({wallBefore:P0} -> {wallAfter:P0}, blocked {Buildings.BuildingSafetyPatches.SettlerHitsBlockedForTest - blockedBefore})",
                        wallAfter >= wallBefore - 0.0001f && Buildings.BuildingSafetyPatches.SettlerHitsBlockedForTest > blockedBefore);
                }
                bool holdsShield = guard.Body.GetInventory().GetAllItems().Any(i => i.m_dropPrefab != null && i.m_dropPrefab.name == "ShieldWood" && guard.Body.IsItemEquiped(i));
                // His shield, in a fight with a Greyling that has only him to fight (the eighth and ninth 1.3 runs: by the player
                // and a settler it struck at them on and on and reached him only while he swung — no block can be had then).
                yield return new WaitForSeconds(2f);
                Vector3 duelAt = guard.transform.position + forward * 6f;
                duelAt.y = ZoneSystem.instance.GetGroundHeight(duelAt);
                Character duelist = SpawnCreature("Greyling", duelAt, spawned);
                int duelBlocksWas = GuardJob.BlocksForTest;
                float duelWas = guard.Body.GetHealth(), duelLeast = duelWas, duelFor = 0f;
                for (; duelist != null && !duelist.IsDead() && duelFor < 40f; duelFor += 0.25f)
                {
                    yield return new WaitForSeconds(0.25f);
                    duelLeast = Mathf.Min(duelLeast, guard.Body.GetHealth());
                }
                int duelBlocks = GuardJob.BlocksForTest - duelBlocksWas;
                bool struck = duelLeast < duelWas - 0.01f || duelBlocks > 0;
                Check($"He holds a copy of the shield, and it blocks when he is struck (shield held {holdsShield}; a Greyling with only him to fight: " +
                      $"dead {duelist == null || duelist.IsDead()} in {duelFor:0} s, struck {struck}, blocks {duelBlocks}; by the crowd of check 4: {blocks} blocks)",
                    holdsShield && (!struck || duelBlocks > 0));

                // 5. A deer by his post is left alone.
                yield return new WaitForSeconds(3f);
                Vector3 post = towerAt + tower.transform.forward * 3.5f;
                Character deer = SpawnCreature("Deer", post + right * 4f, spawned);
                bool deerCame = deer != null;
                float deerHealth = deer != null ? deer.GetHealth() : 0f;
                bool targeted = false;
                for (float t = 0f; t < 20f; t += 0.5f)
                {
                    yield return new WaitForSeconds(0.5f);
                    if (deer != null && Job()?.Target == deer) targeted = true;
                    farthest = Mathf.Max(farthest, Utils.DistanceXZ(guard.transform.position, towerAt));
                }
                Check($"A deer by his post is left alone (targeted {targeted}, health {deerHealth:0} -> {(deer != null ? deer.GetHealth() : 0f):0})",
                    deerCame && !targeted && (deer == null || deer.GetHealth() >= deerHealth - 0.01f));

                // 6. A Greyling far outside his watch isn't chased; he never goes past his patrol + the chase limit.
                Character far = SpawnCreature("Greyling", towerAt - right * (leash + 20f), spawned);
                bool chasedOutside = false;
                float watch = GuardLevels.Radius(tower.Level);
                for (float t = 0f; t < 15f; t += 0.5f)
                {
                    yield return new WaitForSeconds(0.5f);
                    if (far != null && Job()?.Target == far && Utils.DistanceXZ(far.transform.position, towerAt) > watch) chasedOutside = true;
                    farthest = Mathf.Max(farthest, Utils.DistanceXZ(guard.transform.position, towerAt));
                }
                Check($"A Greyling outside his watch isn't chased, and he never went past his patrol + {ColonyConfig.ChaseBeyondPatrol.Value:0} m " +
                      $"(chased outside {chasedOutside}, farthest from his tower {farthest:0.0} m of {leash:0} m)", !chasedOutside && farthest <= leash + 2f);
                if (far != null) Remove(far.gameObject);

                // 6b. A monster he chases past his patrol + the chase limit is let go, and he walks back (review of 1.1).
                Character runner = SpawnCreature("Greyling", towerAt + right * 12f, spawned);
                if (runner != null)
                {
                    runner.SetMaxHealth(100000f);   // it lives through his blows while it leads him off
                    runner.SetHealth(100000f);
                }
                for (float t = 0f; runner != null && Job()?.Target != runner && t < 15f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                bool chased = runner != null && Job()?.Target == runner;
                float pulled = 0f;
                for (float t = 0f; runner != null && t < 20f; t += 0.5f)
                {
                    Vector3 off = towerAt + right * Mathf.Min(12f + 3f * t, 60f);   // it runs off from the tower
                    off.y = ZoneSystem.instance.GetGroundHeight(off);
                    runner.transform.position = off;
                    yield return new WaitForSeconds(0.5f);
                    pulled = Mathf.Max(pulled, Utils.DistanceXZ(guard.transform.position, towerAt));
                }
                bool letGo = runner == null || Job()?.Target != runner;
                if (runner != null) Remove(runner.gameObject);
                float back = Utils.DistanceXZ(guard.transform.position, towerAt);
                for (float t = 0f; back > watch && t < 20f; t += 0.5f)
                {
                    yield return new WaitForSeconds(0.5f);
                    back = Utils.DistanceXZ(guard.transform.position, towerAt);
                }
                Check($"A Greyling he chases past his patrol + {ColonyConfig.ChaseBeyondPatrol.Value:0} m is let go, and he walks back (chased {chased}, " +
                      $"farthest {pulled:0.0} m of {leash:0} m, let go {letGo}, then {back:0.0} m from his tower)",
                    chased && letGo && pulled > watch && pulled <= leash + 3f && back <= watch);

                // 7. At night and in a raid he stays on watch.
                EnvMan.instance.m_debugTime = 0.95f;
                yield return new WaitForSeconds(8f);
                string atNight = guard.Status ?? "";
                AI.CitizenAI.RaidForTest = true;
                yield return new WaitForSeconds(6f);
                string inRaid = guard.Status ?? "";
                AI.CitizenAI.RaidForTest = false;
                EnvMan.instance.m_debugTime = 0.4f;
                bool onDuty(string s) => s.StartsWith("$vc_status_on_watch") || s.StartsWith("$vc_status_patrolling") || s.StartsWith("$vc_status_fighting");
                Check($"At night and in a raid the guard stays on watch ('{Localization.instance.Localize(atNight)}', '{Localization.instance.Localize(inRaid)}')",
                    onDuty(atNight) && onDuty(inRaid));

                // 8. Everyone in the colony is glad of a guard on watch.
                Citizen other = Citizen.All.FirstOrDefault(c => c != null && c != guard && c.ColonyId == colony);
                bool glad = false;
                for (float t = 0f; other != null && !glad && t < 25f; t += 1f)
                {
                    other.GetComponent<CitizenAI>()?.WorkOutMoodForTest();
                    glad = (other.Record.WhyMood ?? "").Contains("$vc_mood_guarded");
                    if (!glad) yield return new WaitForSeconds(1f);
                }
                Check($"A settler of the colony is glad of a guard on watch ({other?.CitizenName}: '{other?.Record.WhyMood}')", glad);

                // 9. With no weapon in any chest he says he needs one, and puts the copy away. Unarmed he runs from a monster, as
                // anyone: none is left about (the seventh 1.3 run: a wild boar, and he said "Fleeing").
                DevHelpers.ClearMonsters(origin, 90f, animalsToo: true);
                foreach (ItemDrop.ItemData club in chest.GetAllItems().Where(i => i.m_dropPrefab != null && i.m_dropPrefab.name == "Club").ToList()) chest.RemoveItem(club);
                for (float t = 0f; (Job()?.Armed ?? true) && t < 12f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                yield return new WaitForSeconds(1f);
                // Monsters that come meanwhile are cleared again until he says it (1.4.1's run at the full run's spot: Greylings
                // spawn there often; one came in those seconds, and he rightly went in from it, "Sheltering").
                for (float t = 0f; !(guard.Status ?? "").Contains("vc_a_weapon") && t < 30f; t += 2f)
                {
                    DevHelpers.ClearMonsters(origin, 90f, animalsToo: true);
                    yield return new WaitForSeconds(2f);
                }
                Check($"With no weapon in the colony's chests the guard says he needs one ('{Localization.instance.Localize(guard.Status ?? "")}', armed {Job()?.Armed})",
                    Job() != null && !Job().Armed && (guard.Status ?? "").Contains("vc_a_weapon"));

                // 11 (1.2). Armour from the chest is worn in place of his trade's clothes, protects him (review of 1.2), and is taken
                // off when it is gone.
                float lossBare = 0f, lossArmoured = 0f;
                yield return Blow(guard.Body, loss => lossBare = loss);
                chest.AddItem(ObjectDB.instance.GetItemPrefab("HelmetBronze"), 1);
                chest.AddItem(ObjectDB.instance.GetItemPrefab("ArmorBronzeChest"), 1);
                bool Wearing(string prefab) => guard.Body.GetInventory().GetAllItems().Any(i => i.m_dropPrefab != null && i.m_dropPrefab.name == prefab && guard.Body.IsItemEquiped(i));
                for (float t = 0f; !(Wearing("HelmetBronze") && Wearing("ArmorBronzeChest")) && t < 10f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                bool armoured = Wearing("HelmetBronze") && Wearing("ArmorBronzeChest") && !Wearing("ArmorLeatherChest") && Wearing("ArmorLeatherLegs");
                yield return Blow(guard.Body, loss => lossArmoured = loss);
                foreach (ItemDrop.ItemData piece in chest.GetAllItems().Where(i => i.m_dropPrefab != null && (i.m_dropPrefab.name == "HelmetBronze" || i.m_dropPrefab.name == "ArmorBronzeChest")).ToList())
                    chest.RemoveItem(piece);
                for (float t = 0f; !Wearing("ArmorLeatherChest") && t < 10f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                bool unarmoured = Wearing("ArmorLeatherChest") && !Wearing("HelmetBronze") && !Wearing("ArmorBronzeChest");
                Check($"He wears copies of the armour in the chest in place of his trade's clothes, it protects him, and his clothes again once it is gone " +
                      $"(armoured {armoured}, a 30 blow took {lossBare:0.0} in his clothes and {lossArmoured:0.0} in bronze, then {unarmoured})",
                    armoured && unarmoured && lossBare > 0f && lossArmoured < lossBare - 1f);

                // 12 (1.2). Fighting makes him tougher: half again his health at 100.
                float fightingSkill = guard.Record.Fighting, baseHealth = guard.Body.GetMaxHealthBase();
                guard.Body.SetHealth(guard.Body.GetMaxHealth());
                guard.Record.Fighting = 100f;
                for (float t = 0f; guard.Body.GetMaxHealth() < baseHealth * 1.49f && t < 8f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                float toughest = guard.Body.GetMaxHealth(), healthNow = guard.Body.GetHealth();
                guard.Record.Fighting = fightingSkill;
                Check($"At Fighting 100 his health is half again, and full ({baseHealth:0} -> {healthNow:0} of {toughest:0})",
                    Mathf.Abs(toughest - baseHealth * 1.5f) < 1f && healthNow >= toughest - 1f);

                // 13 (1.2). An archer shoots a Greyling dead with a bow and arrows from the chest.
                WatchtowerModule towerModule = tower.GetComponent<WatchtowerModule>();
                towerModule.RequestSetRole(GuardRole.Archer);
                chest.AddItem(ObjectDB.instance.GetItemPrefab("Bow"), 1);
                chest.AddItem(ObjectDB.instance.GetItemPrefab("ArrowWood"), 20);
                for (float t = 0f; !(Job()?.Ranged ?? false) && t < 12f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                Character target = SpawnCreature("Greyling", byTower + forward * 3f, spawned);
                bool targetCame = target != null;
                float closest = float.MaxValue, shotFor = 0f;
                int arrowsWas = chest.GetAllItems().Where(i => i.m_dropPrefab != null && i.m_dropPrefab.name == "ArrowWood").Sum(i => i.m_stack);
                for (; target != null && !target.IsDead() && shotFor < 60f; shotFor += 0.5f)
                {
                    yield return new WaitForSeconds(0.5f);
                    if (target != null) closest = Mathf.Min(closest, Utils.DistanceXZ(guard.transform.position, target.transform.position));
                }
                int arrowsNow = chest.GetAllItems().Where(i => i.m_dropPrefab != null && i.m_dropPrefab.name == "ArrowWood").Sum(i => i.m_stack);
                Check($"An archer shoots a Greyling dead with copies of the bow and arrows in the chest (role {towerModule.Role}, holding '{Job()?.WeaponPrefab}' " +
                      $"and '{Job()?.AmmoPrefab}', took {shotFor:0} s, closest {closest:0.0} m, arrows in the chest {arrowsWas} -> {arrowsNow})",
                    targetCame && (target == null || target.IsDead()) && shotFor < 60f && (Job()?.Ranged ?? false) && arrowsNow == arrowsWas);

                // 13b (review of 1.2). A crossbow fires too, loaded between shots.
                chest.AddItem(ObjectDB.instance.GetItemPrefab("CrossbowArbalest"), 1);
                chest.AddItem(ObjectDB.instance.GetItemPrefab("BoltBone"), 20);
                for (float t = 0f; Job()?.WeaponPrefab != "CrossbowArbalest" && t < 12f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                Character bolted = SpawnCreature("Greyling", byTower + forward * 3f, spawned);
                bool boltedCame = bolted != null;
                float boltFor = 0f;
                for (; bolted != null && !bolted.IsDead() && boltFor < 60f; boltFor += 0.5f) yield return new WaitForSeconds(0.5f);
                Check($"A crossbow archer shoots a Greyling dead too (holding '{Job()?.WeaponPrefab}' and '{Job()?.AmmoPrefab}', took {boltFor:0} s)",
                    boltedCame && (bolted == null || bolted.IsDead()) && boltFor < 60f && Job()?.WeaponPrefab == "CrossbowArbalest");

                // 14 (1.2). At level 3 the tower holds a second guard, beside the first.
                ConstructionModule towerSite = tower.GetComponent<ConstructionModule>();
                // The upgrade goes up at once, round whoever stands there (the third 1.3 run: Hilda shut in, Signe in the doorway):
                // the colony's other settlers wait by the Town Hall first. A builder puts pieces up one by one; they walk out.
                int waiting = 0;
                foreach (Citizen c in Citizen.All.Where(c => c != null && c.ColonyId == colony && c != guard).ToList())
                {
                    Vector3 spot = origin - forward * 4f + right * (1.5f * waiting++ - 4f);
                    spot.y = ZoneSystem.instance.GetGroundHeight(spot) + 0.1f;
                    c.transform.position = spot;
                }
                yield return new WaitForSeconds(1f);
                for (int level = 2; level <= 3 && towerSite != null; level++)
                {
                    towerSite.RequestUpgradeNow();
                    for (float t = 0f; (tower.Level < level || !towerSite.IsBuilt) && t < 120f; t += 1f) yield return new WaitForSeconds(1f);
                }
                List<Citizen> guards = new List<Citizen>();
                for (float t = 0f; guards.Count < 2 && t < 40f; t += 1f)
                {
                    yield return new WaitForSeconds(1f);
                    work.Workers(out guards, out _);
                }
                // Each has his own place, 3 m apart, and gets to it (the fifth 1.2 run: the newcomer was still on his way).
                GuardJob JobOf(Citizen c) => c.GetComponent<CitizenAI>()?.JobForTest as GuardJob;
                bool AtPosts() => guards.Count >= 2 && guards.All(g => JobOf(g) != null && Utils.DistanceXZ(g.transform.position, JobOf(g).PostSpot) <= 2.5f);
                for (float t = 0f; !AtPosts() && t < 70f; t += 1f) yield return new WaitForSeconds(1f);   // a wedged guard is freed after 30 s (a 1.3 run)
                // Measured once both have begun the job (the full 1.3 run: the new guard's job wasn't there yet, read as 0 m).
                float postsApart = guards.Count >= 2 && JobOf(guards[0]) != null && JobOf(guards[1]) != null
                    ? Utils.DistanceXZ(JobOf(guards[0]).PostSpot, JobOf(guards[1]).PostSpot) : 0f;
                Check($"At level 3 (level {tower.Level}) the tower holds a second guard with his own place ({guards.Count} of {work.Capacity}: " +
                      $"{string.Join(", ", guards.Select(g => g.CitizenName).ToArray())}, places {postsApart:0.0} m apart, both there {AtPosts()}; " +
                      $"the far hut still in the colony: {farHut != null && farHut.ColonyId == colony})",
                    tower.Level == 3 && work.Capacity == 2 && guards.Count == 2 && postsApart >= 2.9f && AtPosts() && farHut != null && farHut.ColonyId == colony);

                // 15 (1.3). Holding his post where a manager set it, he walks no rounds.
                Vector3 postAt = towerAt + forward * 9f;   // open ground before the tower (the first run: by the far Longhouse, on a slope)
                postAt.y = ZoneSystem.instance.GetGroundHeight(postAt);
                towerModule.RequestSetOrders(GuardOrders.HoldPost);
                towerModule.RequestSetPost(postAt);
                for (float t = 0f; (towerModule.PostAt == null || towerModule.Orders != GuardOrders.HoldPost) && t < 5f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                bool AtPost(Citizen g) => JobOf(g) != null && Utils.DistanceXZ(g.transform.position, JobOf(g).PostSpot) <= 3f;   // the brain's "at post"
                string Distances() => string.Join(", ", guards.Select(g => $"{g.CitizenName} {(JobOf(g) != null ? Utils.DistanceXZ(g.transform.position, JobOf(g).PostSpot) : -1f):0.0} m").ToArray());
                for (float t = 0f; !guards.All(AtPost) && t < 30f; t += 1f) yield return new WaitForSeconds(1f);
                bool there = guards.All(AtPost) && guards.Any(g => Utils.DistanceXZ(JobOf(g).PostSpot, postAt) < 0.5f);
                string atPosts = Distances();
                int roundsWas = GuardJob.RoundsForTest;
                foreach (Citizen g in guards) JobOf(g)?.PatrolNowForTest();
                yield return new WaitForSeconds(8f);
                bool walked = GuardJob.RoundsForTest > roundsWas;
                towerModule.RequestClearPost();
                towerModule.RequestSetOrders(GuardOrders.Patrol);
                for (float t = 0f; (towerModule.PostAt != null || towerModule.Orders != GuardOrders.Patrol) && t < 5f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                // The control (review of 1.3): on rounds again, a round that is due starts.
                int roundsThen = GuardJob.RoundsForTest;
                foreach (Citizen g in guards) JobOf(g)?.PatrolNowForTest();
                for (float t = 0f; GuardJob.RoundsForTest == roundsThen && t < 8f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                bool roundAgain = GuardJob.RoundsForTest > roundsThen;
                Check($"Holding the post a manager set {Utils.DistanceXZ(postAt, towerAt):0} m from the tower, the guards stand there and walk no rounds " +
                      $"(there {there}: {atPosts}; a round {walked}); on rounds again a round starts ({roundAgain})", there && !walked && roundAgain);

                // 16 (1.3). A monster attacking a settler inside the colony's area, past the guards' watch, is answered.
                float watch16 = GuardLevels.Radius(tower.Level);
                // By the second tower, about 42 m from the first, past its watch, on dry ground (1.4.1's release runs: at the full
                // run's spot, 20 m on from the second tower was water; the Greyling there neither attacked nor could be reached).
                bool Dry(Vector3 p) => ZoneSystem.instance.GetGroundHeight(p) > ZoneSystem.instance.m_waterLevel + 0.5f;
                Vector3 callAt = new[] { forward, (forward + right).normalized, (forward - right).normalized }
                    .Select(d => secondAt + d * 20f)
                    .FirstOrDefault(p => Dry(p) && Dry(p + right * 1.5f) && Utils.DistanceXZ(p, towerAt) > watch16 + 3f);
                if (callAt == Vector3.zero) callAt = secondAt + forward * 20f;
                Citizen called = Citizen.All.FirstOrDefault(c => c != null && c.ColonyId == colony && !guards.Contains(c));
                Character attacker = SpawnCreature("Greyling", callAt, spawned);
                if (called != null)
                {
                    Vector3 stand = callAt + right * 1.5f;
                    stand.y = ZoneSystem.instance.GetGroundHeight(stand);
                    called.transform.position = stand;
                    called.GetComponent<CitizenAI>()?.PauseForTest(90f);
                }
                bool attackerCame = attacker != null, answered = false;
                int killsBefore = guards.Sum(g => g.Record.Kills);
                float answeredIn = 0f;
                for (; attacker != null && !attacker.IsDead() && answeredIn < 60f; answeredIn += 0.5f)
                {
                    MonsterAI brain = attacker.GetBaseAI() as MonsterAI;
                    if (brain != null && called != null)
                    {
                        brain.SetAggravated(true, BaseAI.AggravatedReason.Damage);
                        s_monsterTarget(brain) = called.Body;   // it is after the settler
                    }
                    if (called != null && called.Body != null) called.Body.SetHealth(called.Body.GetMaxHealth());   // he lives through it (review of 1.3)
                    // A guard took it as a call: it was past his watch when he went for it (review of 1.3: killed was not enough).
                    if (!answered && Utils.DistanceXZ(attacker.transform.position, towerAt) > watch16 + 1f && guards.Any(g => JobOf(g)?.Target == attacker)) answered = true;
                    yield return new WaitForSeconds(0.5f);
                }
                called?.GetComponent<CitizenAI>()?.PauseForTest(0f);
                Check($"A Greyling attacking {called?.CitizenName} inside the colony's area {Utils.DistanceXZ(callAt, towerAt):0} m from the tower " +
                      $"(past the guards' watch of {watch16:0} m) is answered as a call ({answered}) and killed (took {answeredIn:0} s)",
                    attackerCame && called != null && answered && (attacker == null || attacker.IsDead()) && answeredIn < 60f);

                // 17 (1.3). The alarm: everyone else shelters, the guards gather where it was sounded; All clear ends it. Sounded
                // inside a small roofed shelter on purpose (1.4.2's release run, 1 in about 10: a spot behind a hut's wall kept
                // a guard going in and out of its door): each guard takes the first of their ring spots that stands open.
                Vector3 alarmAt = hall.transform.position + right * 6f;
                alarmAt.y = ZoneSystem.instance.GetGroundHeight(alarmAt);
                var shelter = new List<GameObject>();
                GameObject wallPiece = ZNetScene.instance.GetPrefab("woodwall"), roofPiece = ZNetScene.instance.GetPrefab("wood_floor");
                if (wallPiece != null && roofPiece != null)
                {
                    shelter.Add(Object.Instantiate(wallPiece, alarmAt + new Vector3(0f, 1f, 1f), Quaternion.identity));
                    shelter.Add(Object.Instantiate(wallPiece, alarmAt + new Vector3(0f, 1f, -1f), Quaternion.identity));
                    shelter.Add(Object.Instantiate(wallPiece, alarmAt + new Vector3(1f, 1f, 0f), Quaternion.Euler(0f, 90f, 0f)));
                    shelter.Add(Object.Instantiate(wallPiece, alarmAt + new Vector3(-1f, 1f, 0f), Quaternion.Euler(0f, 90f, 0f)));
                    shelter.Add(Object.Instantiate(roofPiece, alarmAt + new Vector3(0f, 2f, 0f), Quaternion.identity));
                    // 1.4.3's early full run: a ring spot in the open but walled in (an earlier test's roofless hut there) kept a
                    // guard going in and out of a door for the whole alarm. A roofless yard round the first ring spot, on purpose.
                    GuardPosts.AlarmSpot(1, out float yardX, out float yardZ);
                    Vector3 yard = alarmAt + new Vector3(yardX, 0f, yardZ);
                    yard.y = ZoneSystem.instance.GetGroundHeight(yard);
                    // Nobody standing there is shut in by its walls (its review).
                    foreach (Citizen inYard in Citizen.All.Where(c => c != null && Utils.DistanceXZ(c.transform.position, yard) < 2f).ToList())
                    {
                        Vector3 outside = yard + (yard - alarmAt).normalized * 3f;
                        outside.y = ZoneSystem.instance.GetGroundHeight(outside);
                        inYard.transform.position = outside;
                    }
                    shelter.Add(Object.Instantiate(wallPiece, yard + new Vector3(0f, 1f, 1f), Quaternion.identity));
                    shelter.Add(Object.Instantiate(wallPiece, yard + new Vector3(0f, 1f, -1f), Quaternion.identity));
                    shelter.Add(Object.Instantiate(wallPiece, yard + new Vector3(1f, 1f, 0f), Quaternion.Euler(0f, 90f, 0f)));
                    shelter.Add(Object.Instantiate(wallPiece, yard + new Vector3(-1f, 1f, 0f), Quaternion.Euler(0f, 90f, 0f)));
                }
                spawned.AddRange(shelter);   // removed at the end however the test goes (its review), as the test's other pieces
                // The colony's earth painted first (1.4.3's full run, 2026-10-22: the tower's earth was painted by the minute's
                // check as the alarm sounded, the walking map round it was remade, and a guard had "no way" to their spot).
                foreach (ColonyBuilding b in ColonyBuilding.All.Where(b => b != null && b.IsValid && b.ColonyId == colony).ToList())
                {
                    ConstructionModule site = b.GetComponent<ConstructionModule>();
                    if (site != null && !site.EarthPaintedForTest) site.PaintEarthNow();
                }
                // The game rebuilds its paths round new pieces some seconds later: the alarm sounds once it has (its review), so a
                // guard's first choice already knows the yard is walled in.
                yield return new WaitForSeconds(12f);
                // A building's piece over the spot (the game's own cover test counts a tree's branches as a roof too: the review
                // fix's run had a spot in the open under a tree; and 1.4.3's early full run read the shelter as no roof); and what
                // is above it, for a miss to explain itself.
                int pieceMask = LayerMask.GetMask("piece"), aboveMask = LayerMask.GetMask("piece", "Default", "static_solid", "Default_small");
                bool UnderRoof(Vector3 at) => Physics.Raycast(at + Vector3.up * 0.5f, Vector3.up, 30f, pieceMask, QueryTriggerInteraction.Ignore);
                string Above(Vector3 at) => Physics.Raycast(at + Vector3.up * 0.5f, Vector3.up, out RaycastHit over, 30f, aboveMask, QueryTriggerInteraction.Ignore)
                    ? $"{Utils.GetPrefabName(over.collider.transform.root.gameObject)} {over.point.y - at.y:0.0} m above" : "open sky";
                bool alarmUnderRoof = UnderRoof(alarmAt);
                // Their spot had a way to it when they chose it: not one inside the walled-in yard. (1.4.3's full runs of
                // 2026-10-22: asked again from where a guard stopped, 2 m off by the shelter's wall, the game's path test
                // started inside the walled-in yard and found no way, though they had walked there.)
                bool WalksTo(Citizen g) => JobOf(g) != null && JobOf(g).AlarmSpotHasWayForTest;
                townHall.RequestAlarm(true, alarmAt);
                for (float t = 0f; !townHall.AlarmOn && t < 5f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                Citizen settler = Citizen.All.FirstOrDefault(c => c != null && c.ColonyId == colony && !guards.Contains(c));
                bool sheltering = false, gathered = false;
                for (float t = 0f; !(sheltering && gathered) && t < 40f; t += 1f)
                {
                    yield return new WaitForSeconds(1f);
                    sheltering = settler != null && (settler.Status ?? "").StartsWith("$vc_status_sheltering");
                    gathered = guards.All(g => JobOf(g) != null && Utils.DistanceXZ(JobOf(g).PostSpot, alarmAt) <= 9.5f && AtPost(g)
                        && !UnderRoof(JobOf(g).PostSpot)   // its spot in the open: the first draft passed with a spot inside, a guard by its wall
                        && WalksTo(g));                    // and one they can get to: by the yard's wall is within "at post" too
                }
                string spots = string.Join(", ", guards.Where(g => JobOf(g) != null).Select(g =>
                {
                    Vector3 p = JobOf(g).PostSpot;
                    return $"{g.CitizenName} {Utils.DistanceXZ(p, alarmAt):0.0} m off{(UnderRoof(p) ? " under a roof" : "")} ({Above(p)}), {Utils.DistanceXZ(g.transform.position, p):0.0} m from it" +
                           (WalksTo(g) ? "" : ", no way there");
                }).ToArray());
                townHall.RequestAlarm(false, alarmAt);
                for (float t = 0f; townHall.AlarmOn && t < 5f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                foreach (GameObject piece in shelter)
                    if (piece != null) ZNetScene.instance.Destroy(piece);
                Check($"The alarm, sounded inside a roofed shelter (under a roof {alarmUnderRoof}): {settler?.CitizenName} shelters ({sheltering}, " +
                      $"'{Localization.instance.Localize(settler?.Status ?? "")}'), the guards gather at open spots round it ({gathered}: {spots}), " +
                      $"and All clear ends it ({!townHall.AlarmOn})", sheltering && gathered && !townHall.AlarmOn && alarmUnderRoof);

                // 18 (1.3). A ballista in his patrol, below half, is loaded from what the chests can spare.
                GameObject ballistaPrefab = ZNetScene.instance.GetPrefab("piece_turret");
                Turret ballista = null;
                string ammoName = "";
                if (ballistaPrefab != null)
                {
                    Vector3 ballistaAt = towerAt - right * 8f;
                    ballistaAt.y = ZoneSystem.instance.GetGroundHeight(ballistaAt);
                    GameObject ballistaGo = Object.Instantiate(ballistaPrefab, ballistaAt, Quaternion.identity);
                    spawned.Add(ballistaGo);
                    ballista = ballistaGo.GetComponent<Turret>();
                    ammoName = ballista != null && ballista.m_allowedAmmo != null && ballista.m_allowedAmmo.Count > 0 && ballista.m_allowedAmmo[0].m_ammo != null
                        ? ballista.m_allowedAmmo[0].m_ammo.name : "";
                    if (ammoName.Length > 0) hall.GetComponent<Container>().GetInventory().AddItem(ObjectDB.instance.GetItemPrefab(ammoName), 20);
                }
                int loadedWas = GuardJob.LoadedForTest;
                // Monsters kept away meanwhile: a guard rightly fights before a chore (1.4.3's full run, 2026-10-22: a Neck by
                // the water drew the loading guard off, and the ballista stayed empty for the whole wait).
                for (float t = 0f; ballista != null && ballista.GetAmmo() == 0 && t < 75f; t += 1f)
                {
                    DevHelpers.ClearMonsters(origin, 90f);
                    yield return new WaitForSeconds(1f);
                }
                int inHall = ammoName.Length > 0 ? hall.GetComponent<Container>().GetInventory().CountItems(ObjectDB.instance.GetItemPrefab(ammoName).GetComponent<ItemDrop>().m_itemData.m_shared.m_name) : -1;
                Check($"A ballista in his patrol is loaded from the Town Hall's chest ('{ammoName}': {(ballista != null ? ballista.GetAmmo() : -1)} in it, " +
                      $"{inHall} left of 20 in the chest, {GuardJob.LoadedForTest - loadedWas} put in)",
                    ballista != null && ballista.GetAmmo() > 0 && inHall >= 0 && inHall + ballista.GetAmmo() == 20);
                // Loaded, it never shoots a settler, nor (in the colony) a player (review of 1.3: the game's search has no rule against
                // either; the player was shot in the third run): a settler stands in front of it, then the player.
                Citizen inFront = Citizen.All.FirstOrDefault(c => c != null && c.ColonyId == colony && !guards.Contains(c));
                int ammoThen = ballista != null ? ballista.GetAmmo() : -1, sparedWas = CitizenPatches.SparedForTest, sparedPlayersWas = CitizenPatches.SparedPlayersForTest;
                float healthThen = 0f;
                Vector3 front = ballista != null ? ballista.transform.position + ballista.transform.forward * 8f : origin;
                front.y = ZoneSystem.instance.GetGroundHeight(front);
                if (inFront != null && ballista != null)
                {
                    inFront.transform.position = front;
                    inFront.GetComponent<CitizenAI>()?.PauseForTest(45f);
                    healthThen = inFront.Body.GetHealth();
                }
                // It may shoot something else (a deer, a monster): what counts is that it never aims at a settler or the player.
                bool aimedAtSettler = false, aimedAtPlayer = false;
                Vector3 playerAt = player.transform.position;
                // Up to 40 s, until it has looked over a settler or the player at least once (the second full run of 1.4.0: in 20 s
                // it never looked, and the check could say nothing).
                bool PutToTheTest() => CitizenPatches.SparedForTest + CitizenPatches.SparedPlayersForTest > sparedWas + sparedPlayersWas;
                for (float t = 0f; t < 40f && !(t >= 20f && PutToTheTest()); t += 0.25f)
                {
                    if (Mathf.Approximately(t, 10f) && ballista != null) player.TeleportTo(front + right * 2f, player.transform.rotation, distantTeleport: false);   // the player beside him
                    yield return new WaitForSeconds(0.25f);
                    Character aim = ballista != null ? s_turretTarget(ballista) : null;
                    if (aim != null && aim.GetComponent<Citizen>() != null) aimedAtSettler = true;
                    if (aim != null && aim is Player) aimedAtPlayer = true;
                }
                player.TeleportTo(playerAt, player.transform.rotation, distantTeleport: false);
                bool notShot = inFront != null && ballista != null && !aimedAtSettler && !aimedAtPlayer && inFront.Body.GetHealth() >= healthThen - 0.01f;
                inFront?.GetComponent<CitizenAI>()?.PauseForTest(0f);
                Check($"The loaded ballista never aims at a settler or the player ({inFront?.CitizenName} in front of it 20 s, the player beside him 10 s: " +
                      $"aimed at a settler {aimedAtSettler}, the player {aimedAtPlayer}; ammunition {ammoThen} -> {(ballista != null ? ballista.GetAmmo() : -1)}, " +
                      $"health {healthThen:0} -> {(inFront != null ? inFront.Body.GetHealth() : 0f):0}; passed over a settler {CitizenPatches.SparedForTest - sparedWas} times, " +
                      $"the player {CitizenPatches.SparedPlayersForTest - sparedPlayersWas})",
                    notShot && PutToTheTest());   // it was put to the test

                // 19 (1.3). No monster springs up within a finished tower's circle; and each guard keeps a tally.
                // Its own area (review of 1.3: any base area passed), as wide as its circle; the tally grew with the call's Greyling.
                EffectArea towerArea = EffectArea.GetAllAreas().FirstOrDefault(a => a != null && (a.m_type & EffectArea.Type.PlayerBase) != 0 && a.transform.IsChildOf(tower.transform));
                float circle19 = towerModule.Circle != null ? towerModule.Circle.Value.Radius : -1f, area19 = towerArea != null ? towerArea.GetRadius() : -1f;
                bool noSpawns = towerModule.StopsSpawns && towerArea != null && Mathf.Abs(area19 - circle19) < 0.6f
                                && EffectArea.IsPointInsideArea(towerAt + right * 10f, EffectArea.Type.PlayerBase, 0f) != null;
                int kills = guards.Sum(g => g.Record.Kills);
                Check($"No monster springs up within the tower's circle (its own area {towerArea != null}, {area19:0.0} m for a circle of {circle19:0.0} m: {noSpawns}), " +
                      $"and the guards keep a tally of their kills ({killsBefore} -> {kills})", noSpawns && kills > killsBefore);

                // 10. No hit of his ever landed on a player, a settler or a tame.
                Check($"No hit of a guard landed on a player, a settler or a tame ({CitizenPatches.FriendlyHitsForTest - friendlyBefore})",
                    CitizenPatches.FriendlyHitsForTest == friendlyBefore);
            }
            finally
            {
                Buildings.BuildingSafetyPatches.StopTrackingForTest();
                EnvMan.instance.m_debugTimeOfDay = false;
                AI.CitizenAI.RaidForTest = false;
                ColonyConfig.NewcomersWalkIn.Value = walkInWas;
                ColonyConfig.ColonyRadius.Value = radiusWas;
                ColonyConfig.MaxReach.Value = reachWas;
                // The player of now: one who died meanwhile is a new body (the sixth 1.3 run broke off on the old one).
                Player me = Player.m_localPlayer;
                if (me != null && !me.InGodMode()) me.SetGodMode(true);
                if (player == null) Log.Event($"vc_guardtest: FAIL the player died during the test (it stopped there)");
                Log.Event($"vc_guardtest: done — {s_pass} passed, {s_fail + (player == null ? 1 : 0)} failed");
                me?.Message(MessageHud.MessageType.Center, $"vc_guardtest: {s_pass} passed, {s_fail} failed");
                Cleanup();
            }
        }

        /// <summary>A 30-blunt blow from nobody on the guard (his owner applies it), what it took, then his health back.</summary>
        private static IEnumerator Blow(Character body, System.Action<float> took)
        {
            body.SetHealth(body.GetMaxHealth());
            yield return new WaitForSeconds(0.3f);
            float was = body.GetHealth();
            var hit = new HitData { m_point = body.GetCenterPoint(), m_dir = Vector3.down };
            hit.m_damage.m_blunt = 30f;
            body.Damage(hit);
            yield return new WaitForSeconds(0.5f);
            took(was - body.GetHealth());
            body.SetHealth(body.GetMaxHealth());
        }

        /// <summary>The builder's work done at once (the order is made when the building is placed).</summary>
        private static IEnumerator Finish(ColonyBuilding building)
        {
            ConstructionModule site = building != null ? building.GetComponent<ConstructionModule>() : null;
            for (float t = 0f; site != null && site.Order == null && t < 15f; t += 0.5f) yield return new WaitForSeconds(0.5f);
            if (site != null && site.Order != null) site.FinishNow();
            for (float t = 0f; site != null && !site.IsBuilt && t < 40f; t += 0.5f) yield return new WaitForSeconds(0.5f);
        }

        private static Character SpawnCreature(string prefabName, Vector3 at, List<GameObject> spawned)
        {
            GameObject go = DevHelpers.Spawn(prefabName, at);
            if (go == null) return null;
            spawned.Add(go);
            return go.GetComponent<Character>();
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
            Log.Event($"vc_guardtest: {(ok ? "PASS" : "FAIL")} {what}");
        }
    }
}
