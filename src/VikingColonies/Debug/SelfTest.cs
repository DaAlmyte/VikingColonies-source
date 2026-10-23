using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Jotunn.Entities;
using UnityEngine;
using VikingColonies.AI;
using VikingColonies.Buildings;
using VikingColonies.Citizens;
using VikingColonies.Config;
using VikingColonies.Construction;
using VikingColonies.Core;
using VikingColonies.Gui;
using VikingColonies.Jobs;

namespace VikingColonies.DevTools
{
    /// <summary>Runs the colony flow end to end in a test world and logs PASS/FAIL per step.</summary>
    internal sealed class SelfTestCommand : ConsoleCommand
    {
        public override string Name => "vc_selftest";
        public override string Help => "VikingColonies: place a Town Hall, a Builder's Hut and a Lumberjack's Hut here and test hiring and building (test worlds only, ~4 minutes). Removes what it made afterwards; 'vc_selftest keep' leaves it standing.";
        public override bool IsCheat => true;

        public override void Run(string[] args)
        {
            if (Player.m_localPlayer == null || DevHelpers.RefuseOutsideTestWorld("vc_selftest")) return;
            bool keep = args != null && args.Any(a => string.Equals(a, "keep", System.StringComparison.OrdinalIgnoreCase));
            Plugin.Instance.StartCoroutine(SelfTest.Run(Player.m_localPlayer, keep));
        }
    }

    internal static class SelfTest
    {
        private static int s_pass;
        private static int s_fail;
        private static bool s_walkInWas = true;

        public static IEnumerator Run(Player player, bool keep)
        {
            CitizenAI.RaidForTest = false;   // a run cut short must not leave a raid behind
            s_walkInWas = ColonyConfig.NewcomersWalkIn.Value;
            ColonyConfig.NewcomersWalkIn.Value = false;   // its checks expect settlers by the Town Hall at once (vc_lifetest checks walking in)
            // Its checks expect the work pace and newcomers as before mood (0.17): everyone content, but for the mood check.
            MoodTracker.HoldForTest = true;
            // The checks expect day (at night settlers go indoors instead of working); the night check sets its own time.
            EnvMan.instance.m_debugTimeOfDay = true;
            EnvMan.instance.m_debugTime = 0.5f;
            // No rain: it wears the huts' unroofed pieces down, and the builder mending them all ran the mend check out (the 1.2 full run).
            EnvMan.instance.SetForceEnvironment("Clear");
            s_pass = s_fail = 0;
            Log.Event("vc_selftest: starting (stand on flat ground in a test world)");
            // Hugin's tips (0.11): in the game's tutorial list; showing one marks it seen with the character (undone again).
            // Showing a tip queues it for Hugin; the game marks it seen when the player talks to him (review of 0.11).
            bool tipsIn = HuginTips.Registered;
            ForgetTipForTest(player, HuginTips.Night);
            HuginTips.Unqueue(HuginTips.Night);
            HuginTips.Show(HuginTips.Night);
            bool tipQueued = HuginTips.Queued(HuginTips.Night);
            HuginTips.Unqueue(HuginTips.Night);   // the player still gets it at their first night
            Check($"Hugin's tips are in the game's tutorial list and showing one queues it for Hugin (listed: {tipsIn}, queued: {tipQueued})",
                tipsIn && tipQueued);
            Check($"Every game part the mod reads was found at start-up, and every part of the start-up ran ({GameCheck.Count - GameCheck.Missing.Count} of {GameCheck.Count}" +
                  $"{(GameCheck.Missing.Count > 0 ? "; missing: " + string.Join(", ", GameCheck.Missing) : "")}" +
                  $"{(GameCheck.StartupFailures.Count > 0 ? "; failed: " + string.Join(", ", GameCheck.StartupFailures) : "")})",
                GameCheck.Missing.Count == 0 && GameCheck.StartupFailures.Count == 0);
            // Issue #1 (1.4.1, MyLittleUI): on a chest, our Shift+E goes before every other mod's and our hover line after.
            Patches useHooks = Harmony.GetPatchInfo(AccessTools.Method(typeof(Container), nameof(Container.Interact)));
            Patches hoverHooks = Harmony.GetPatchInfo(AccessTools.Method(typeof(Container), nameof(Container.GetHoverText)));
            Patch ourUse = useHooks?.Prefixes.FirstOrDefault(h => h.owner == Plugin.PluginGuid);
            Patch ourHover = hoverHooks?.Postfixes.FirstOrDefault(h => h.owner == Plugin.PluginGuid);
            // Every hover line of ours goes last, the cairn's "Here lies …" too (1.4.3's compatibility run).
            List<Patch> ourHovers = (hoverHooks?.Postfixes ?? new List<Patch>().AsReadOnly()).Where(h => h.owner == Plugin.PluginGuid).ToList();
            string others = string.Join(", ", (useHooks?.Prefixes ?? new List<Patch>().AsReadOnly()).Concat(hoverHooks?.Postfixes ?? new List<Patch>().AsReadOnly())
                .Where(h => h.owner != Plugin.PluginGuid).Select(h => $"{h.owner} {h.priority}").ToArray());
            Check($"On a chest our Shift+E hook goes first and our hover lines last (ours: Shift+E {ourUse?.priority}, hover {string.Join("/", ourHovers.Select(h => h.priority.ToString()).ToArray())}; " +
                  $"others there: {(others.Length > 0 ? others : "none")})",
                ourUse != null && ourHover != null && ourUse.priority == Priority.First && ourHovers.All(h => h.priority == Priority.Last)
                && useHooks.Prefixes.All(h => h.owner == Plugin.PluginGuid || h.priority <= ourUse.priority)
                && hoverHooks.Postfixes.All(h => h.owner == Plugin.PluginGuid || h.priority >= ourHover.priority)
                // A tie at the lowest priority is broken only by HarmonyAfter (its review: MyLittleUI's hook at 0 is the one that
                // ran after the cairn's line): every hover hook of ours names MyLittleUI, and any other mod's hook there.
                && ourHovers.All(h => h.after != null && h.after.Contains(Gui.BuildingInteractPatches.MyLittleUI))
                && hoverHooks.Postfixes.Where(o => o.owner != Plugin.PluginGuid && o.priority <= Priority.Last)
                    .All(o => ourHovers.All(h => h.after != null && h.after.Contains(o.owner))));
            Patches playerHooks = Harmony.GetPatchInfo(AccessTools.Method(typeof(Player), "Interact", new[] { typeof(GameObject), typeof(bool), typeof(bool) }));
            Patch ourPlayerUse = playerHooks?.Prefixes.FirstOrDefault(h => h.owner == Plugin.PluginGuid);
            string playerOthers = string.Join(", ", (playerHooks?.Prefixes ?? new List<Patch>().AsReadOnly())
                .Where(h => h.owner != Plugin.PluginGuid).Select(h => $"{h.owner} {h.priority}").ToArray());
            Check($"On any colony building our Shift+E (the player's use) goes first (ours: {ourPlayerUse?.priority}; " +
                  $"others there: {(playerOthers.Length > 0 ? playerOthers : "none")})",
                ourPlayerUse != null && ourPlayerUse.priority == Priority.First
                && playerHooks.Prefixes.All(h => h.owner == Plugin.PluginGuid || h.priority <= ourPlayerUse.priority));
            Vector3 origin = player.transform.position;
            // Nothing may spoil the run: the player can't die, and settlers have no monsters to flee from.
            if (!player.InGodMode()) player.SetGodMode(true);
            Log.Event($"vc_selftest: god mode on, {DevHelpers.ClearMonsters(origin, 60f)} monsters removed");
            Vector3 forward = player.transform.forward;
            int citizensBefore = Citizen.All.Count(c => c != null);
            var idsBefore = new System.Collections.Generic.HashSet<string>(Citizen.All.Where(c => c != null).Select(c => c.Id));
            CitizenAI settlerAi = CitizenPrefab.Prefab != null ? CitizenPrefab.Prefab.GetComponent<CitizenAI>() : null;
            Check("Settlers turn while walking", settlerAi != null && settlerAi.m_moveMinAngle >= 90f);
            Check("Hut chests stand without support", HutChestFloats());
            // A mushroom lying on the ground is an item, though the game gives it a Piece (1.4.1: the levelling kept off
            // mushrooms and the site check turned red at them, as at another building's pieces).
            GameObject mushroom = DevHelpers.Spawn("Mushroom", origin + Vector3.up * 0.5f);
            Piece mushroomPiece = mushroom != null ? mushroom.GetComponent<Piece>() : null;
            Check($"A mushroom lying on the ground is a loose item, not a building's piece (spawned: {mushroom != null}, a Piece: {mushroomPiece != null}, " +
                  $"placed as a piece: {mushroom?.GetComponent<ItemDrop>()?.IsPiece()})",
                mushroom != null && (mushroomPiece == null || PiecePlacer.IsLooseItem(mushroomPiece)));
            if (mushroom != null) Remove(mushroom);
            IReadOnlyList<ToolInfo> tools = BuilderTools.Known;
            int axes = tools.Count(t => t.Kind == ToolKind.Axe), pickaxes = tools.Count(t => t.Kind == ToolKind.Pickaxe), hoes = tools.Count(t => t.Kind == ToolKind.Hoe);
            Check($"Builder tools known ({axes} axes, {pickaxes} pickaxes, {hoes} hoes)", axes > 0 && pickaxes > 0 && hoes > 0);
            Check("The hoe's level ground found (builders can flatten sites)", GroundShaper.Ready);

            ColonyBuilding hall = DevHelpers.Place(BuildingTypes.TownHall.PrefabName, origin + forward * 8f, Quaternion.identity, player.GetPlayerID());
            Check("Town Hall placed", hall != null);
            if (hall == null) yield break;
            DevHelpers.KeepNothing(hall);
            yield return new WaitForSeconds(9f);
            string colony = hall.ColonyId;
            int members = Citizen.All.Count(c => c != null && c.Record.ColonyId == colony);
            int created = Citizen.All.Count(c => c != null) - citizensBefore;
            // Citizens without a colony nearby are adopted too, so the colony can be bigger; only the new ones are capped.
            int starting = ColonyConfig.StartingCitizens.Value;
            Check($"Starting citizens arrived ({members} members, {created} new)", members >= starting && created <= starting);
            TownHallModule townHall = hall.GetComponent<TownHallModule>();
            Check("Colony founded once", townHall.Founded);
            Check("Roster lists the members", townHall.Roster.Entries.Count == members);
            SiteVerdict onHall = DevHelpers.Judge(BuildingTypes.LumberjackHut, hall.transform.position, Quaternion.identity);
            Check($"A Lumberjack's Hut site on top of the Town Hall is red ({onHall?.Red})", onHall != null && onHall.Colour == SiteColour.Red);

            Vector3 right = player.transform.right;
            ColonyBuilding builderHut = DevHelpers.Place(BuildingTypes.BuilderHut.PrefabName, origin + forward * 8f - right * 10f, Quaternion.identity);
            Check("Builder's Hut placed", builderHut != null);
            if (builderHut == null) yield break;
            ConstructionModule builderSite = builderHut.GetComponent<ConstructionModule>();
            yield return new WaitForSeconds(12f);
            Check("Builder's Hut has a build order", builderSite != null && builderSite.Order != null && builderSite.Plan.Count > 0);
            Check("Builder's Hut hires before its building stands",
                builderHut.GetComponent<WorkerModule>().FindWorker(out _, out _) == WorkerModule.WorkerState.Loaded);
            int builderPlanned = builderSite.Plan.Count(p => p.IsValid);
            int builderPlaced = builderSite.FinishNow();
            Check($"Finish put up the Builder's Hut ({builderPlaced} of {builderPlanned} pieces)", builderPlaced == builderPlanned);
            string builderSiteId = builderHut.Id;

            // A tree his level can't fell doesn't hold the builder: the site line names the level it needs, he takes the
            // site and says so, and once another hut is ordered he leaves it for that one (the wait is shortened here).
            BuilderJob.WaitOnPlayerSeconds = 10f;
            builderHut.GetComponent<WorkerModule>().FindWorker(out Citizen builder, out _);
            // Spots for the test huts: the first that isn't red here (the ground near the test's start differs from run to run).
            Vector3 Around(float ahead, float aside)
            {
                Vector3 at = origin + forward * ahead + right * aside;
                at.y = ZoneSystem.instance.GetGroundHeight(at);
                return at;
            }
            Vector3 Usable(Vector3? awayFrom, params Vector3[] spots)
            {
                foreach (Vector3 spot in spots)
                {
                    if (awayFrom.HasValue && Utils.DistanceXZ(spot, awayFrom.Value) < 12f) continue;
                    SiteVerdict verdict = DevHelpers.Judge(BuildingTypes.LumberjackHut, spot, Quaternion.identity);
                    if (verdict != null && verdict.Colour != SiteColour.Red) return spot;
                }
                return spots[0];
            }
            Vector3 birchSpot = Usable(null, Around(-6f, 10f), Around(-6f, -10f), Around(-14f, 4f), Around(-14f, -4f));
            GameObject birch = DevHelpers.Spawn("Birch1", DevHelpers.SiteCentre(BuildingTypes.LumberjackHut, birchSpot, Quaternion.identity));
            SiteVerdict onBirch = DevHelpers.Judge(BuildingTypes.LumberjackHut, birchSpot, Quaternion.identity);
            Check($"A birch on a new hut's site is named: {(onBirch != null ? DevHelpers.Plain(SiteText.AboveLevel(onBirch)) : "no site check")}",
                birch != null && onBirch != null && onBirch.Colour == SiteColour.Yellow && onBirch.NeedsLevel == 2
                && onBirch.AboveLevel.Any(c => c.Key == ClearKind.Tree));
            ColonyBuilding birchHut = DevHelpers.Place(BuildingTypes.LumberjackHut.PrefabName, birchSpot, Quaternion.identity);
            ConstructionModule birchSite = birchHut != null ? birchHut.GetComponent<ConstructionModule>() : null;
            for (float waited = 0f; !WaitsForLevel2(birchSite, builder) && waited < 45f; waited += 0.5f)
                yield return new WaitForSeconds(0.5f);
            Check($"The builder takes the birch site and says it needs Builder's Hut level 2 ({(birchSite?.Order != null ? Localization.instance.Localize(birchSite.Order.Needs) : "no order")})",
                WaitsForLevel2(birchSite, builder));

            Vector3 hutSpot = Usable(birchSpot, Around(8f, 10f), Around(8f, -10f), Around(16f, 8f), Around(16f, -8f), Around(2f, 18f), Around(2f, -18f));
            SiteVerdict atSpot = DevHelpers.Judge(BuildingTypes.LumberjackHut, hutSpot, Quaternion.identity);
            Check($"The Lumberjack's Hut spot can be used ({atSpot?.Colour} {(atSpot != null && atSpot.Colour == SiteColour.Red ? atSpot.Red.ToString() : "")})",
                atSpot != null && atSpot.Colour != SiteColour.Red);
            ColonyBuilding hut = DevHelpers.Place(BuildingTypes.LumberjackHut.PrefabName, hutSpot, Quaternion.identity);
            Check("Lumberjack's Hut placed", hut != null);
            if (hut == null)
            {
                BuilderJob.WaitOnPlayerSeconds = (float)OrderPatience.WaitSeconds;
                yield break;
            }
            WorkerModule worker = hut.GetComponent<WorkerModule>();
            ConstructionModule site = hut.GetComponent<ConstructionModule>();
            string hutSiteId = hut.Id;
            for (float waited = 0f; !ClaimedBy(site, builder) && waited < 40f; waited += 0.5f)
                yield return new WaitForSeconds(0.5f);
            Check("Once another hut is ordered, the builder leaves the birch site and takes that one",
                ClaimedBy(site, builder) && !ClaimedBy(birchSite, builder));

            // Town Hall › Work orders (0.9): Do first takes the builder to the birch site, though he had set it aside; Set
            // aside makes him leave it for the other hut; the tab lists both and what they still need; Resume puts it back.
            if (birchSite != null && site != null && builder != null)
            {
                // His own patience back to a full minute: only Set aside can explain him leaving below (review).
                BuilderJob.WaitOnPlayerSeconds = (float)OrderPatience.WaitSeconds;
                birchSite.RequestFirst();
                for (float waited = 0f; !ClaimedBy(birchSite, builder) && waited < 15f; waited += 0.5f) yield return new WaitForSeconds(0.5f);
                Check($"Do first takes the builder to that order at once, even one he had set aside (marked: {birchSite.Order?.First > 0}, his: {ClaimedBy(birchSite, builder)})",
                    ClaimedBy(birchSite, builder));
                birchSite.RequestPause(true);
                for (float waited = 0f; (ClaimedBy(birchSite, builder) || !ClaimedBy(site, builder)) && waited < 8f; waited += 0.5f)
                    yield return new WaitForSeconds(0.5f);
                Check($"Set aside, the order is left and he goes back to the other hut (set aside: {birchSite.Order?.Paused}, on it: {ClaimedBy(birchSite, builder)}, " +
                      $"on the other: {ClaimedBy(site, builder)})",
                    birchSite.Order != null && birchSite.Order.Paused && !ClaimedBy(birchSite, builder) && ClaimedBy(site, builder));
                // Read at once: the window closes itself next frame when the player stands too far from the Town Hall.
                Windows.TownHall.Open(hall);
                Windows.TownHall.ShowTabForTest("orders");
                string ordersText = DevHelpers.Plain(Windows.TownHall.TextForTest());   // without the rows' colour tags
                string Loc(string token) => Localization.instance.Localize(token);
                // What's still to bring leaves the set-aside order out: only the other hut's pieces count.
                // As the window counts it: what the builder already carries for it is on its way (0.26; the 1.3.1 run
                // failed on 2 Wood in his hands).
                Dictionary<string, int> stock = BuilderTools.StoresForBuilders(hall.ColonyId, hall.transform.position);
                stock = HandOver.Available(stock, Construction.HandOverFromBag.BuilderCarries(site));
                // A new Town Hall orders its camp once the camp's design ships (1.4.0): its order is open too, and what it
                // still needs is to bring as well.
                ConstructionModule camp = hall.GetComponent<ConstructionModule>();
                bool campOpen = camp != null && camp.IsValid && camp.IsCamp && camp.Order != null && camp.Order.State != BuildState.Done && !camp.Order.Paused;
                Dictionary<string, int> remaining = site.RemainingNeeds();
                if (campOpen)
                    foreach (KeyValuePair<string, int> need in camp.RemainingNeeds())
                        remaining[need.Key] = (remaining.TryGetValue(need.Key, out int had) ? had : 0) + need.Value;
                Dictionary<string, int> missing = NeedsTally.Shortfall(remaining, stock);
                string expectedNeeds = missing.Count == 0 ? Loc("$vc_orders_needs_none") : string.Format(Loc("$vc_orders_needs"), Loc(PiecePlacer.DescribeItems(missing)));
                Check($"The Work orders tab lists both orders, the set-aside one marked, and what the other still needs ({ordersText}; expected '{expectedNeeds}')",
                    ordersText.Contains(string.Format(Loc("$vc_orders_count"), campOpen ? 3 : 2, 1)) && ordersText.Contains("— " + Loc("$vc_order_status_aside"))
                    && ordersText.Contains(expectedNeeds));
                Windows.TownHall.Close();
                // Leaving a world and entering one again in the same game destroys the windows (Jötunn makes its GUI anew at
                // every scene change): they are built again and open (the whole-mod review of 1.4.1: they never opened again).
                Windows.TownHall.DestroyPanelForTest();
                Windows.Hut.DestroyPanelForTest();
                string hallAgain = "", hutAgain = "";
                try
                {
                    Windows.TownHall.Open(hall);
                    Windows.TownHall.ShowTabForTest("orders");
                    hallAgain = DevHelpers.Plain(Windows.TownHall.TextForTest());
                    Windows.TownHall.Close();
                    Windows.Hut.Open(hut);
                    hutAgain = DevHelpers.Plain(Windows.Hut.TextForTest());
                    Windows.Hut.Close();
                }
                catch (System.Exception e) { hallAgain += $" (threw {e.GetType().Name}: {e.Message})"; }
                Check($"After a world change the Town Hall's and the hut's windows are built again and open (hall: {hallAgain}; hut: {hutAgain})",
                    hallAgain.Contains(string.Format(Loc("$vc_orders_count"), campOpen ? 3 : 2, 1)) && hutAgain.Length > 0 && !hallAgain.Contains("threw"));
                birchSite.RequestPause(false);
                yield return new WaitForSeconds(0.5f);
                Check("Resume puts the order back in the queue", birchSite.Order != null && !birchSite.Order.Paused);
            }
            BuilderJob.WaitOnPlayerSeconds = (float)OrderPatience.WaitSeconds;
            if (birchHut != null) Remove(birchHut.gameObject);
            if (birch != null) Remove(birch);
            yield return new WaitForSeconds(12f);
            Check("Hut joined the colony", hut.ColonyId == colony);
            Check($"The Town Hall's builder may manage the colony; another player may not (owner {ColonyPermissions.OwnerOf(hut)})",
                ColonyPermissions.MayManage(hut, player.GetPlayerID()) && !ColonyPermissions.MayManage(hut, player.GetPlayerID() + 1));
            Check("Lumberjack's Hut has a build order", site != null && site.Order != null && site.Plan.Count > 0);
            Check("Lumberjack's Hut saved its ground level", site != null && site.Order != null && !float.IsNaN(site.Order.Ground));
            Check("Lumberjack's Hut doesn't hire before its building stands", worker.FindWorker(out _, out _) == WorkerModule.WorkerState.None);
            int planned = site.Plan.Count(p => p.IsValid);
            int alreadyUp = site.Plan.Count(p => p.IsValid && PiecePlacer.FindStanding(p) != null);   // the builder may have started
            // Seen: a small beech in a doorway kept settlers out. What grows in front of a door is cleared with the site.
            GameObject doorBeech = site.DoorLandings.Count > 0 ? DevHelpers.Spawn("Beech_small1", CoreVectors.U(site.DoorLandings[0].Centre)) : null;
            yield return new WaitForSeconds(0.5f);
            bool beechListed = doorBeech != null && site.ThingsToClear().Any(t => t.Root == doorBeech);
            int placed = site.FinishNow();
            Check($"Finish put up the Lumberjack's Hut ({placed + alreadyUp} of {planned} pieces)", placed + alreadyUp == planned);
            for (float waited = 0f; !site.IsBuilt && waited < 30f; waited += 0.5f)
                yield return new WaitForSeconds(0.5f);   // the site is flattened spot by spot before the pieces go up
            yield return new WaitForSeconds(10f);
            int standing = site.Plan.Count(p => p.IsValid && PiecePlacer.FindStanding(p) != null);
            // Which piece fell and where (1.4.2: one fell on uneven ground at (1100, 450) and the check didn't say which).
            string gone = string.Join(", ", site.Plan.Where(p => p.IsValid && PiecePlacer.FindStanding(p) == null).Take(3)
                .Select(p => $"{p.Prefab} at {CoreVectors.U(p.Position)}, the ground there {SiteSurvey.GroundAt(p.Position.X, p.Position.Z):0.00}"));
            Check($"Nothing fell within 10 s ({standing} of {planned} standing{(gone.Length > 0 ? "; gone: " + gone : "")})", standing == planned);
            // Destroyed objects go at the end of a frame: looked at after the wait.
            Check($"A small tree in front of the hut's door is cleared with the site (spawned: {doorBeech != null || beechListed}, listed: {beechListed}, gone: {doorBeech == null})",
                beechListed && doorBeech == null);
            Check("Lumberjack's Hut counts as built", site.IsBuilt);
            Check($"The saga tells the Lumberjack's Hut was built ({SagaLines(hall)})", SagaHas(hall, "built", BuildingTypes.LumberjackHut.NameToken));
            CheckDoorways("Lumberjack's Hut", site);
            CheckDoorways("Builder's Hut", builderSite);   // built a while ago: anything that came since shows too
            yield return new WaitForSeconds(6f);
            Check("Auto-hire employed a citizen", worker.FindWorker(out Citizen hired, out _) == WorkerModule.WorkerState.Loaded);

            worker.RequestSetAutoHire(false);
            yield return new WaitForSeconds(1f);
            Check("Auto-hire switched off", !worker.AutoHire);

            worker.RequestFire();
            yield return new WaitForSeconds(2f);
            Check("Fire left the hut without a worker", worker.FindWorker(out _, out _) == WorkerModule.WorkerState.None);
            Check("Fired citizen is jobless", hired != null && !hired.Record.IsEmployed);
            // Seen: a lumberjack fired with wood in his arms kept it for good. Let go, he takes his load to the Town Hall.
            if (hired != null)
            {
                int resinBefore = hall.Storage.Contents().TryGetValue("Resin", out int rs) ? rs : 0;
                hired.Carry.Add("Resin", 3);
                hired.SaveCarry();
                for (float waited = 0f; hired != null && hired.Carry.Total > 0 && waited < 40f; waited += 0.5f)
                    yield return new WaitForSeconds(0.5f);
                int resinAfter = hall.Storage.Contents().TryGetValue("Resin", out int ra) ? ra : 0;
                Check($"A settler let go with a load takes it to the Town Hall's chest (carrying {(hired != null ? hired.Carry.Total : -1)}, resin there {resinBefore} -> {resinAfter})",
                    hired != null && hired.Carry.Total == 0 && resinAfter >= resinBefore + 3);
            }

            worker.RequestHire();
            yield return new WaitForSeconds(2f);
            Check("Hire employed a citizen", worker.FindWorker(out Citizen rehired, out _) == WorkerModule.WorkerState.Loaded);

            // The hired settler goes in to the chest through the hut's closed door, then out to 6 m in front of it.
            // He starts outside: at night his rest walk may already have taken him in.
            Door hutDoor = DevHelpers.DoorOf(site);
            CitizenAI walker = rehired != null ? rehired.GetComponent<CitizenAI>() : null;
            if (hutDoor == null) Check("Door check skipped: this Lumberjack's Hut design has no door", true);
            else if (walker == null) Check("A hired settler to try the hut's door", false);
            else
            {
                Vector3 chest = hut.transform.position;
                Vector3 outward = hutDoor.transform.position - chest;
                outward.y = 0f;
                Vector3 outside = hutDoor.transform.position + outward.normalized * 6f;
                DevHelpers.ClearMonsters(origin, 60f);   // a settler fleeing a monster stops his walks
                DevHelpers.ClearLogs(origin, 40f);   // a log rolled into a doorway would block it (the test's own birch)
                // The earth under its roof painted first (1.4.3's compatibility run, 2026-10-22: painted by the minute's repair
                // check in the middle of this walk, the walking map there was being remade and he went round the hut).
                if (!site.EarthPaintedForTest) site.PaintEarthNow();
                for (float waited = 0f; !site.EarthPaintedForTest && waited < 10f; waited += 0.5f) yield return new WaitForSeconds(0.5f);
                yield return new WaitForSeconds(5f);
                walker.TestWalk(outside, 65f);
                for (float waited = 0f; !walker.TestWalkDone && waited < 60f; waited += 0.5f)
                    yield return new WaitForSeconds(0.5f);
                yield return new WaitForSeconds(2f);   // he closes the door behind him if he came out through it
                // He must get there through the doorway, not reach the chest through a wall or a window: watched where he
                // crosses the door's line, whoever opened the door. (0.24: counted by his own door passes, the check failed
                // whenever he stood under 2 m inside, too near to shut it, and later walked out through it open.)
                int passes = walker.DoorPasses;
                // Into the hut, a step past the door: the chest itself may stand against a wall, reachable from outside.
                var walkIn = new DoorWalk();
                yield return WalkThroughDoor(walker, hutDoor, outward, hutDoor.transform.position - outward.normalized * 1.5f, walkIn);
                for (float waited = 0f; DoorUser.IsOpen(hutDoor) && waited < 5f; waited += 0.5f) yield return new WaitForSeconds(0.5f);
                yield return new WaitForSeconds(1.5f);   // a door he shut ends its swing
                var walkOut = new DoorWalk();
                yield return WalkThroughDoor(walker, hutDoor, outward, outside, walkOut);
                yield return new WaitForSeconds(2f);   // he closes the door once he is 2 m past it
                bool closed = !DoorUser.IsOpen(hutDoor);
                walker.TestWalk(outside, 0f);   // back to work
                Check($"A settler went in and out through the hut's door and closed it behind him (in: {walkIn}, out: {walkOut}, " +
                      $"doors he opened {walker.DoorPasses - passes}, door {(closed ? "closed" : "open")})", walkIn.Through && walkOut.Through && closed);
            }

            // An error in a settler's work doesn't repeat on every tick: he is Dazed for a few seconds, then works again.
            CitizenAI faulty = rehired != null ? rehired.GetComponent<CitizenAI>() : null;
            if (faulty == null) Check("A hired settler for the work-error check", false);
            else
            {
                // No breakfast meanwhile: a new day beginning during the run made breakfast due, and his work (where the error is)
                // didn't run until after it (the 1.1 full run), as for the repair checks.
                bool eatWas = ColonyConfig.SettlersEat.Value;
                ColonyConfig.SettlersEat.Value = false;
                faulty.FailWorkOnceForTest();
                yield return new WaitForSeconds(1f);
                bool dazed = rehired.Status == "$vc_status_dazed";
                yield return new WaitForSeconds(ErrorBackoff.FirstPause + 1f);
                Check($"An error in a settler's work makes him Dazed, then he works again (dazed: {dazed}, now '{Localization.instance.Localize(rehired.Status)}')",
                    dazed && rehired.Status != "$vc_status_dazed" && rehired.Record.IsEmployed);
                // Outside his work too (0.25): the game's creature loop never sees it, so every other creature goes on.
                faulty.FailTickOnceForTest();
                yield return new WaitForSeconds(1f);
                bool dazedOutside = rehired.Status == "$vc_status_dazed";
                // A second error soon after the first pauses him longer (review of 0.25: a fixed wait looked too early).
                for (float waited = 0f; rehired.Status == "$vc_status_dazed" && waited < ErrorBackoff.MaxPause + 1f; waited += 0.5f)
                    yield return new WaitForSeconds(0.5f);
                Check($"An error outside a settler's work makes him Dazed, then he works again (dazed: {dazedOutside}, now '{Localization.instance.Localize(rehired.Status)}')",
                    dazedOutside && rehired.Status != "$vc_status_dazed" && rehired.Record.IsEmployed);
                faulty.ForgetErrorsForTest();   // the test's own errors aren't counted into a real one's report
                ColonyConfig.SettlersEat.Value = eatWas;
            }

            // Indoors: at night, when attacked and in a raid, the lumberjack goes into his hut through its door and shuts it.
            if (hutDoor == null || walker == null || rehired == null) Check("Indoors checks skipped: this Lumberjack's Hut design has no door", true);
            else
            {
                Vector3 chest = hut.transform.position;
                // By the chest with no wall between: a spot just behind a wall is near the chest too.
                bool Inside() => Utils.DistanceXZ(rehired.transform.position, chest) <= IndoorRules.SpotDistance + 1.2f && !walker.WallBetweenForTest(chest);
                bool Is(string status) => rehired.Status == status;
                int woodBefore = Count(hut.Storage.Contents(), "Wood");
                rehired.Carry.Add("Wood", 5);   // night falls while he carries wood: he delivers it first
                rehired.SaveCarry();
                EnvMan.instance.m_debugTimeOfDay = true;
                EnvMan.instance.m_debugTime = 0f;   // midnight
                for (float waited = 0f; !(Is("$vc_status_sleeping") && Inside()) && waited < 60f; waited += 0.5f)
                    yield return new WaitForSeconds(0.5f);
                bool asleep = Is("$vc_status_sleeping") && Inside();
                int woodAfter = Count(hut.Storage.Contents(), "Wood");
                bool delivered = rehired.Carry.Count("Wood") == 0 && woodAfter >= woodBefore + 5;
                yield return new WaitForSeconds(3f);   // he shuts the door once he is past it
                // Another settler sleeping there may be coming through it right then (run 25 of 0.13): he shuts it after him.
                for (float waited = 0f; DoorUser.IsOpen(hutDoor) && waited < 12f; waited += 0.5f) yield return new WaitForSeconds(0.5f);
                bool shut = !DoorUser.IsOpen(hutDoor);
                EnvMan.instance.m_debugTime = 0.5f;   // noon
                for (float waited = 0f; Is("$vc_status_sleeping") && waited < 20f; waited += 0.5f)
                    yield return new WaitForSeconds(0.5f);
                Check($"At night the lumberjack delivers his wood, sleeps in his hut with the door shut, and works again by day (wood in the chest {woodBefore} -> {woodAfter}, " +
                      $"carried {rehired.Carry.Count("Wood")}; asleep inside: {asleep}, door {(shut ? "shut" : "open")}, by day: '{Localization.instance.Localize(rehired.Status)}')",
                    delivered && asleep && shut && !Is("$vc_status_sleeping"));

                // Calm is 15 s after the last monster within 20 m: an animal wandering by the hut kept him in to the end of the
                // wait (0.23 self-test rerun 1). The spot is kept clear while he shelters, and the wait has a margin.
                DevHelpers.ClearMonsters(origin, 60f, animalsToo: true);
                rehired.NoteMonsterHitForTest();   // as if a monster had just hit him
                for (float waited = 0f; !(Is("$vc_status_sheltering") && Inside()) && waited < 30f; waited += 0.5f)
                    yield return new WaitForSeconds(0.5f);
                bool sheltered = Is("$vc_status_sheltering") && Inside();
                for (float waited = 0f; Is("$vc_status_sheltering") && waited < FleeTracker.CalmSeconds + 25f; waited += 0.5f)
                {
                    if (Mathf.Repeat(waited, 5f) < 0.01f) DevHelpers.ClearMonsters(origin, 60f, animalsToo: true);
                    yield return new WaitForSeconds(0.5f);
                }
                Check($"When attacked he shelters in his hut and comes out once it's calm (sheltered: {sheltered}, after: '{Localization.instance.Localize(rehired.Status)}')",
                    sheltered && !Is("$vc_status_sheltering"));

                DevHelpers.ClearMonsters(origin, 60f, animalsToo: true);   // a monster or animal wandering by would keep him in after the raid (see above)
                CitizenAI.RaidForTest = true;
                for (float waited = 0f; !(Is("$vc_status_sheltering") && Inside()) && waited < 30f; waited += 0.5f)
                    yield return new WaitForSeconds(0.5f);
                bool raided = Is("$vc_status_sheltering") && Inside();
                CitizenAI.RaidForTest = false;
                for (float waited = 0f; Is("$vc_status_sheltering") && waited < 20f; waited += 0.5f)
                {
                    if (Mathf.Repeat(waited, 5f) < 0.01f) DevHelpers.ClearMonsters(origin, 60f, animalsToo: true);
                    yield return new WaitForSeconds(0.5f);
                }
                Check($"During a raid he shelters in his hut and comes out when it ends (sheltered: {raided}, after: '{Localization.instance.Localize(rehired.Status)}')",
                    raided && !Is("$vc_status_sheltering"));

                // A monster that reaches him in his hut (it followed him in) makes it no refuge: he leaves it for another, or runs.
                // The monster keeps at him, as a real one would: a hit every 2 s, outside and in there.
                int gaveUp = walker.RefugesGivenUp;
                bool shelteredAgain = false;
                for (float waited = 0f; walker.RefugesGivenUp == gaveUp && waited < 40f; waited += 0.25f)
                {
                    if (Mathf.Repeat(waited, 2f) < 0.01f) rehired.NoteMonsterHitForTest();
                    yield return new WaitForSeconds(0.25f);
                    shelteredAgain |= Is("$vc_status_sheltering") && Inside();
                }
                bool leftHut = walker.RefugesGivenUp > gaveUp;
                for (float waited = 0f; (Is("$vc_status_sheltering") || Is("$vc_status_fleeing")) && waited < FleeTracker.CalmSeconds + 15f; waited += 0.5f)
                    yield return new WaitForSeconds(0.5f);
                walker.ForgetBadRefugesForTest();
                Check($"A monster that reaches him inside his hut makes him leave it (sheltered inside: {shelteredAgain}, left it: {leftHut})", shelteredAgain && leftHut);

                // A hut whose door he can't get through (a player built in front of it) is passed over after a while,
                // instead of him standing at its wall all night.
                Vector3 outward = hutDoor.transform.position - chest;
                outward.y = 0f;
                Vector3 outFront = hutDoor.transform.position + outward.normalized * 6f;
                walker.TestWalk(outFront, 65f);
                for (float waited = 0f; !walker.TestWalkDone && waited < 60f; waited += 0.5f)
                    yield return new WaitForSeconds(0.5f);
                walker.TestWalk(outFront, 0f);   // the walk is over: night may take him
                List<GameObject> blocks = DevHelpers.BlockDoor(hutDoor, chest);
                yield return new WaitForSeconds(3f);   // the ground's paths take the wall in
                int passedBefore = walker.RefugesGivenUp;
                EnvMan.instance.m_debugTime = 0f;   // midnight
                // The give-up clock starts when he chooses that hut, after any errand he finishes first (0.20.1 full run:
                // a lumberjack's errand to his chest took a third of the window).
                for (float waited = 0f; walker.RefugeForTest != hut.Id && walker.RefugesGivenUp == passedBefore && waited < 60f; waited += 0.5f)
                    yield return new WaitForSeconds(0.5f);
                for (float waited = 0f; walker.RefugesGivenUp == passedBefore && waited < IndoorRules.GiveUpSeconds + 20f; waited += 0.5f)
                    yield return new WaitForSeconds(0.5f);
                bool passedOver = walker.RefugesGivenUp > passedBefore;
                float blockedAt = Utils.DistanceXZ(rehired.transform.position, chest);
                bool blockedInside = Inside();
                foreach (GameObject block in blocks)
                    if (block != null) ZNetScene.instance.Destroy(block);
                EnvMan.instance.m_debugTime = 0.5f;   // noon
                walker.ForgetBadRefugesForTest();
                for (float waited = 0f; Is("$vc_status_sleeping") && waited < 20f; waited += 0.5f)
                    yield return new WaitForSeconds(0.5f);
                Check($"A hut whose door is blocked is passed over within {IndoorRules.GiveUpSeconds:0} s (block put up: {blocks.Count > 0}, passed over: {passedOver}, " +
                      $"he stood {blockedAt:0.0} m from its chest, inside: {blockedInside})", blocks.Count > 0 && passedOver);
            }


            // Levels: a hut can't go above the Builder's Hut; vc_build upgrade raises the Builder's Hut, then the hut.
            Check($"A new Lumberjack's Hut is level 1 (level {hut.Level})", hut.Level == 1);
            List<ReplantLine> replantLines = ReplantList.For(hut);
            Check($"A level-1 Lumberjack's Hut offers {replantLines.Count(l => l.Offered)} kinds to replant and greys {replantLines.Count(l => l.NeedsLevel == 2)} until level 2",
                replantLines.Any(l => l.Offered) && replantLines.Any(l => l.NeedsLevel == 2));
            // The Builder's Hut rule on its own: Town Hall levels (1.4) cap huts at the hall's level too (vc_halltest checks that).
            bool hallLevelsWas = ColonyConfig.HallLevels.Value;
            ColonyConfig.HallLevels.Value = false;
            UpgradeLock afterBuilder;
            try
            {
                UpgradeLock beforeBuilder = site.UpgradeCheck();
                Check($"A level-1 Builder's Hut can't take the Lumberjack's Hut to level 2 ({beforeBuilder})", beforeBuilder == UpgradeLock.BuilderTooLow);
                // The feedback ask (1.4.2): this is its moment, the first hut near the player reaching level 2. A test world
                // asks only when armed; it waits a look at the huts at level 1 first.
                Windows.Feedback.Close();
                FeedbackAsker.ArmForTest(false);
                yield return new WaitForSeconds(FeedbackAsker.EverySeconds + 1f);
                builderSite.RequestUpgradeNow();
                for (float waited = 0f; builderHut.Level < 2 && waited < 40f; waited += 0.5f) yield return new WaitForSeconds(0.5f);
                Check($"vc_build upgrade took the Builder's Hut to level 2 (level {builderHut.Level})", builderHut.Level == 2);
                for (float waited = 0f; !Windows.Feedback.IsOpen && waited < 15f; waited += 0.5f) yield return new WaitForSeconds(0.5f);
                string ask = Windows.Feedback.TextForTest();
                Check($"The feedback ask came when a hut near the player reached level 2, with Not now ({ask}; {FeedbackAsker.StateForTest()})",
                    Windows.Feedback.Asking && ask.Contains(Localization.instance.Localize("$vc_feedback_not_now")) && FeedbackAsker.AskedForTest);
                if (!Windows.Feedback.Asking) FeedbackAsker.DisarmForTest();   // it didn't come: it mustn't pop up later in the run
                Windows.Feedback.Close();
                afterBuilder = site.UpgradeCheck();
            }
            finally { ColonyConfig.HallLevels.Value = hallLevelsWas; }   // never left off (it is saved and synced)
            Check($"Then the Lumberjack's Hut may go to level 2 ({afterBuilder})", afterBuilder == UpgradeLock.None);
            site.RequestUpgradeNow();
            for (float waited = 0f; hut.Level < 2 && waited < 40f; waited += 0.5f) yield return new WaitForSeconds(0.5f);
            yield return new WaitForSeconds(10f);   // a piece that won't hold has fallen by now
            int level2Planned = site.Plan.Count(p => p.IsValid);
            int level2Up = site.Plan.Count(p => p.IsValid && PiecePlacer.FindStanding(p) != null);
            int oldLeft = OldPiecesLeft(site);
            Check($"The Lumberjack's Hut is level 2 with its level-2 building ({level2Up} of {level2Planned} pieces up, {oldLeft} old pieces left)",
                hut.Level == 2 && level2Up == level2Planned && oldLeft == 0);
            yield return new WaitForSeconds(1.5f);   // the chest notices its new level within a second
            Check($"The level-2 Lumberjack's Hut's chest has {hut.Storage.Rows} rows", hut.Storage.Rows == 3 + ColonyConfig.RowsPerLevel.Value);
            Check("The saga tells the Lumberjack's Hut is now level 2", SagaHas(hall, "level", BuildingTypes.LumberjackHut.NameToken, "2"));
            Check($"The feedback ask didn't come again for the second hut at level 2 ({FeedbackAsker.StateForTest()})", !Windows.Feedback.IsOpen && FeedbackAsker.AskedForTest);
            FeedbackAsker.DisarmForTest();

            // A change another computer saved a moment before a settler's delivery is kept, not overwritten by this
            // computer's older copy of the chest.
            int Count(Dictionary<string, int> items, string item) => items.TryGetValue(item, out int n) ? n : 0;
            int flintBefore = Count(hut.Storage.Contents(), "Flint");
            DevHelpers.WriteRemoteChange(hut.GetComponent<Container>(), "Flint", 3);
            var delivery = new CarryLoad();
            delivery.Add("Resin", 2);
            hut.Storage.TryDeposit(delivery);
            yield return new WaitForSeconds(1.5f);   // the chest reloads what was saved
            Dictionary<string, int> afterDelivery = hut.Storage.Contents();
            Check($"A change saved elsewhere just before a delivery is kept (flint {flintBefore} -> {Count(afterDelivery, "Flint")}, resin {Count(afterDelivery, "Resin")})",
                Count(afterDelivery, "Flint") == flintBefore + 3 && Count(afterDelivery, "Resin") >= 2);
            Check($"A level-2 lumberjack works {LumberjackJob.WorkRadiusOf(hut):0} m around his hut",
                Mathf.Approximately(LumberjackJob.WorkRadiusOf(hut), ColonyConfig.WorkRadius.Value + ColonyConfig.RadiusPerLevel.Value));

            // Deaths: a falling tree's hit with SettlersSafeFromTrees on and off, then a settler killed by a monster.
            Citizen victim = Citizen.All.FirstOrDefault(c => c != null && !idsBefore.Contains(c.Id) && c.Record.ColonyId == colony && !c.Record.IsEmployed);
            if (victim == null) Check("A jobless test settler for the death checks", false);
            else yield return Plugin.Instance.StartCoroutine(DeathChecks(victim, townHall, origin));

            worker.RequestSetAutoHire(true);
            yield return new WaitForSeconds(1f);
            Log.Event("vc_selftest: removing the test hut");
            ZNetScene.instance.Destroy(hut.gameObject);
            yield return new WaitForSeconds(ColonyRules.GoneConfirmSeconds + 6f);   // a building must be missing this long to count as gone
            Check("Destroying the hut made its worker jobless", rehired != null && !rehired.Record.IsEmployed);
            Check("Town Hall still standing", hall != null && hall.IsValid);

            // The Town Hall: only players break it; lost anyway, the colony lives on, and a new one takes it over.
            WearNTear hallWear = hall != null ? hall.GetComponent<WearNTear>() : null;
            if (hallWear == null) Check("The Town Hall has health to test", false);
            else
            {
                float hallBefore = hallWear.GetHealthPercentage();
                GameObject brutePrefab = ZNetScene.instance.GetPrefab("Greydwarf");
                Vector3 bruteAt = origin + Vector3.right * 20f;
                bruteAt.y = ZoneSystem.instance.GetGroundHeight(bruteAt);
                Character brute = brutePrefab != null ? Object.Instantiate(brutePrefab, bruteAt, Quaternion.identity).GetComponent<Character>() : null;
                var monsterHit = new HitData { m_point = hall.transform.position };
                monsterHit.m_damage.m_blunt = 200f;
                if (brute != null) monsterHit.SetAttacker(brute);
                hallWear.Damage(monsterHit);
                var fireHit = new HitData { m_point = hall.transform.position };   // fire: nobody behind it
                fireHit.m_damage.m_fire = 200f;
                hallWear.Damage(fireHit);
                yield return new WaitForSeconds(1f);
                float afterMonster = hallWear.GetHealthPercentage();
                if (brute != null) Remove(brute.gameObject);
                var playerHit = new HitData { m_point = hall.transform.position };
                playerHit.m_damage.m_blunt = 5f;
                playerHit.SetAttacker(player);
                hallWear.Damage(playerHit);
                yield return new WaitForSeconds(1f);
                float afterPlayer = hallWear.GetHealthPercentage();
                Check($"Only players damage the Town Hall (health {hallBefore:P0}, after a monster and a fire {afterMonster:P0}, after a player {afterPlayer:P0})",
                    Mathf.Approximately(afterMonster, hallBefore) && afterPlayer < afterMonster);
            }

            int graceDays = ColonyConfig.TownHallGraceDays.Value;
            ColonyConfig.TownHallGraceDays.Value = 1;
            Citizen keeper = Citizen.All.FirstOrDefault(c => c != null && c.Record.ColonyId == colony && c.Record.IsEmployed);
            int membersBefore = Citizen.All.Count(c => c != null && c.Record.ColonyId == colony);
            Vector3 hallAt = hall != null ? hall.transform.position : origin + forward * 8f;
            if (hall != null) Remove(hall.gameObject);
            yield return new WaitForSeconds(ColonyRules.GoneConfirmSeconds + 8f);   // gone for good only after this long
            // One check that met the area not ready starts his count again (the 0.26 soak: Ulf and the hut noticed,
            // Knut didn't): a second round for him, with a note of it.
            float late = 0f;
            for (; keeper != null && !ColonyGrace.IsLost(keeper.Record.HallLostAt) && late < ColonyRules.GoneConfirmSeconds + 4f; late += 1f)
                yield return new WaitForSeconds(1f);
            if (late > 0f && keeper != null)
                Log.Info($"vc_selftest: note: {keeper.CitizenName} noticed the Town Hall gone {late:0} s late (gone seen {keeper.GetComponent<CitizenAI>()?.ColonyGoneForTest})");
            bool hutKept = builderHut != null && builderHut.ColonyId == colony && builderHut.GraceDaysLeft == 1;
            bool settlerKept = keeper != null && keeper.Record.ColonyId == colony && keeper.Record.IsEmployed && ColonyGrace.IsLost(keeper.Record.HallLostAt);
            // Which part, and how many of the colony noticed (0.26 soak: once the settler hadn't, with no sign why).
            string keeperState = keeper == null ? "no settler" : $"{keeper.CitizenName}: in the colony {keeper.Record.ColonyId == colony}, employed {keeper.Record.IsEmployed}, " +
                $"knows the hall is gone {ColonyGrace.IsLost(keeper.Record.HallLostAt)}, owned here {keeper.IsOwner}, '{keeper.Status}', " +
                $"gone seen {keeper.GetComponent<CitizenAI>()?.ColonyGoneForTest}";
            int noticed = Citizen.All.Count(c => c != null && c.Record.ColonyId == colony && ColonyGrace.IsLost(c.Record.HallLostAt));
            int colonySize = Citizen.All.Count(c => c != null && c.Record.ColonyId == colony);
            Check($"With the Town Hall gone the colony lives on for its grace days (Builder's Hut: colony kept {builderHut != null && builderHut.ColonyId == colony}, " +
                  $"days left {(builderHut != null ? builderHut.GraceDaysLeft : -1)}; {keeperState}; {noticed} of {colonySize} settlers know the hall is gone)",
                hutKept && settlerKept);
            // In the meantime his hut's door still opens for him (doors are the colony's while its Town Hall is gone).
            DevHelpers.ClearLogs(origin, 40f);
            CitizenAI keeperAi = keeper != null ? keeper.GetComponent<CitizenAI>() : null;
            Door keeperDoor = builderHut != null ? DevHelpers.DoorOf(builderHut.GetComponent<ConstructionModule>()) : null;
            if (keeperAi == null || keeperDoor == null) Check("A settler and a hut door for the door check while the Town Hall is gone", false);
            else
            {
                Vector3 outward = keeperDoor.transform.position - builderHut.transform.position;
                outward.y = 0f;
                Vector3 outFront = keeperDoor.transform.position + outward.normalized * 5f;
                // Seen: a settler the death check removed had left this door open, and the walk went through the open doorway.
                DoorUser.CloseForTest(keeperDoor);
                yield return new WaitForSeconds(1f);
                keeperAi.TestWalk(outFront, 65f);
                for (float waited = 0f; !keeperAi.TestWalkDone && waited < 60f; waited += 0.5f) yield return new WaitForSeconds(0.5f);
                yield return new WaitForSeconds(2f);   // he closes it behind him
                int passesBefore = keeperAi.DoorPasses;
                keeperAi.TestWalk(keeperDoor.transform.position - outward.normalized * 1.5f, 65f);
                for (float waited = 0f; !keeperAi.TestWalkDone && waited < 60f; waited += 0.5f) yield return new WaitForSeconds(0.5f);
                bool walkedIn = keeperAi.TestWalkDone && keeperAi.DoorPasses > passesBefore;
                keeperAi.TestWalk(outFront, 0f);
                Check($"While the Town Hall is gone a settler still goes through his hut's door (in through it: {walkedIn})", walkedIn);
            }
            ColonyBuilding newHall = DevHelpers.Place(BuildingTypes.TownHall.PrefabName, hallAt, Quaternion.identity, player.GetPlayerID());
            DevHelpers.KeepNothing(newHall);
            yield return new WaitForSeconds(9f);   // founding, and the huts notice their Town Hall again
            int membersAfter = Citizen.All.Count(c => c != null && c.Record.ColonyId == colony);
            Check($"A new Town Hall among the huts takes the colony over (its colony is the old one: {newHall != null && newHall.ColonyId == colony}, " +
                  $"settlers {membersBefore} -> {membersAfter}, the hut's days left {(builderHut != null ? builderHut.GraceDaysLeft : -1)})",
                newHall != null && newHall.ColonyId == colony && newHall.TookOver && membersAfter == membersBefore && builderHut != null && builderHut.GraceDaysLeft == 0);
            if (newHall != null) hall = newHall;   // the clean-up removes this one
            ColonyConfig.TownHallGraceDays.Value = graceDays;

            // The Longhouse: the beds of its building are homes, and at night a settler sleeps by his bed.
            Vector3 longhouseSpot = Usable(hutSpot, Around(-14f, 12f), Around(-14f, -12f), Around(18f, 14f), Around(18f, -14f), Around(-4f, 22f));
            ColonyBuilding longhouse = DevHelpers.Place(BuildingTypes.Longhouse.PrefabName, longhouseSpot, Quaternion.identity, player.GetPlayerID());
            ConstructionModule longhouseSite = longhouse != null ? longhouse.GetComponent<ConstructionModule>() : null;
            for (float waited = 0f; longhouseSite != null && longhouseSite.Order == null && waited < 15f; waited += 0.5f)
                yield return new WaitForSeconds(0.5f);   // it joins the colony, then gets its build order
            if (longhouseSite != null && longhouseSite.Order != null) longhouseSite.FinishNow();
            for (float waited = 0f; longhouseSite != null && !longhouseSite.IsBuilt && waited < 40f; waited += 0.5f)
                yield return new WaitForSeconds(0.5f);
            ResidenceModule residence = longhouse != null ? longhouse.GetComponent<ResidenceModule>() : null;
            for (float waited = 0f; residence != null && residence.Beds().Count < 2 && waited < 10f; waited += 0.5f)
                yield return new WaitForSeconds(0.5f);   // the last pieces may stand a moment after it counts as built
            int beds = residence != null ? residence.Beds().Count : 0;
            int settlersHere = Citizen.All.Count(c => c != null && c.Record.ColonyId == colony);
            string homePrefix = longhouse != null ? longhouse.Id + "#" : "-";
            int homed = 0;
            for (float waited = 0f; waited < 20f; waited += 0.5f)
            {
                homed = Citizen.All.Count(c => c != null && c.Record.Home.StartsWith(homePrefix));
                if (homed >= Mathf.Min(beds, settlersHere) && homed > 0) break;
                yield return new WaitForSeconds(0.5f);
            }
            CheckDoorways("Longhouse", longhouseSite);
            // The game's paths learn about a new building's floors a moment after it goes up.
            for (float waited = 0f; residence != null && UnreachableBeds(residence).Count > 0 && waited < WalkingMapSeconds; waited += 1f) yield return new WaitForSeconds(1f);
            CheckBedsReachable(residence);
            Check($"A Longhouse built at level 1 has 2 beds, and its colony's settlers get homes there ({beds} beds, {homed} of {settlersHere} settlers at home)",
                beds == 2 && homed == Mathf.Min(beds, settlersHere) && homed > 0);
            Citizen sleeper = Citizen.All.FirstOrDefault(c => c != null && c.Record.Home.StartsWith(homePrefix));
            if (sleeper == null) Check("A settler with a home for the night check", false);
            else
            {
                DevHelpers.ClearLogs(origin, 50f);
                EnvMan.instance.m_debugTime = 0f;   // midnight
                Vector3 bedAt = Citizen.ToVector3(sleeper.Record.HomePos);
                // His sleeping spot by the bed: in the bunk design, at its foot in the aisle, 2 m from the bed's middle.
                Vector3 spotAt = ResidenceModule.SleepSpotOf(sleeper.Record.Home, sleeper.GetComponent<CitizenAI>()) ?? bedAt;
                // By it, or lying in it (0.15: run 41 found him already in his bed, 0.0 m from its middle).
                bool AtBed() => sleeper != null && sleeper.Status == "$vc_status_sleeping"
                                && (Utils.DistanceXZ(sleeper.transform.position, spotAt) < 1f
                                    || sleeper.GetComponent<CitizenAI>().LyingForTest && Utils.DistanceXZ(sleeper.transform.position, bedAt) < 1f);
                for (float waited = 0f; !AtBed() && waited < 150f; waited += 0.5f)   // a lumberjack delivers his load first
                    yield return new WaitForSeconds(0.5f);
                bool slept = AtBed();
                float fromBed = sleeper != null ? Utils.DistanceXZ(sleeper.transform.position, bedAt) : -1f;
                float fromSpot = sleeper != null ? Utils.DistanceXZ(sleeper.transform.position, spotAt) : -1f;
                EnvMan.instance.m_debugTime = 0.5f;   // noon
                Check($"At night {sleeper.CitizenName} sleeps by or in his bed in the Longhouse ({fromSpot:0.0} m from his spot by it, {fromBed:0.0} m from the bed's middle, " +
                      $"'{Localization.instance.Localize(sleeper.Status)}')", slept);
                for (float waited = 0f; sleeper != null && sleeper.Status == "$vc_status_sleeping" && waited < 20f; waited += 0.5f)
                    yield return new WaitForSeconds(0.5f);
            }

            // Meals: once a day a settler eats the plainest food in the colony's chests; with none he's hungry and slower.
            Citizen eater = Citizen.All.FirstOrDefault(c => c != null && c.Record.ColonyId == colony && c.IsOwner);
            Container larder = hall != null ? hall.GetComponent<Container>() : null;
            if (eater == null || larder == null || hall.Storage == null) Check("A settler and the Town Hall's chest for the meal checks", false);
            else
            {
                EnvMan.instance.m_debugTime = 0.5f;   // noon: nobody eats at night
                int today = EnvMan.instance.GetDay(ZNet.instance.GetTimeSeconds());
                Inventory larderItems = larder.GetInventory();
                larderItems.AddItem(ObjectDB.instance.GetItemPrefab("Raspberry"), 3);
                larderItems.AddItem(ObjectDB.instance.GetItemPrefab("CookedMeat"), 2);
                eater.ForgetMealsForTest();   // the plainest, not the plainest he hasn't had lately (0.17 review M7)
                eater.MissMealForTest();
                // Since 0.15 he walks to the chest and eats there (about 10 s), wherever he was.
                for (float waited = 0f; eater != null && eater.Record.LastMealDay != today && waited < 150f; waited += 0.5f)   // within a minute (0.18)
                    yield return new WaitForSeconds(0.5f);
                Dictionary<string, int> left = hall.Storage.Contents();
                int berries = left.TryGetValue("Raspberry", out int b) ? b : 0;
                int meat = left.TryGetValue("CookedMeat", out int m) ? m : 0;
                // What he ate, and the cooked meat left: he may take his raspberry from a nearer chest (review of 0.18).
                Check($"At his meal {eater.CitizenName} eats the plainest food in the colony's chests ({eater.LastMeal}; {berries} of 3 raspberries and {meat} of 2 cooked meat left in the Town Hall)",
                    eater.Record.LastMealDay == today && !eater.Record.Hungry && eater.LastMeal == "Raspberry" && meat == 2);

                foreach (string item in new[] { "Raspberry", "CookedMeat" })
                    for (ItemDrop.ItemData it = larderItems.GetItem(item, -1, true); it != null; it = larderItems.GetItem(item, -1, true))
                        larderItems.RemoveItem(it);
                float fedSpeed = eater.WorkSpeed;   // his trade and trait change his pace too (0.16)
                eater.MissMealForTest();
                for (float waited = 0f; eater != null && eater.Record.LastMealDay != today && waited < 100f; waited += 0.5f)   // he tries within a minute (0.18), finds nothing, and is hungry
                    yield return new WaitForSeconds(0.5f);
                bool hoverSays = eater.GetComponent<Character>().GetHoverText().Contains(Localization.instance.Localize("$vc_hungry"));
                Check($"With nothing to eat {eater.CitizenName} is hungry: he works slower and his hover says so (work speed {eater.WorkSpeed:0.00}, hover {hoverSays})",
                    eater.Record.Hungry && eater.WorkSpeed < fedSpeed
                    && Mathf.Abs(eater.WorkSpeed - Mathf.Max(Meals.SlowestWork, fedSpeed * ColonyConfig.HungryWorkSpeed.Value)) < 0.01f && hoverSays);
                // 1.4.2, a player's report: food put in after the meal was missed is eaten the same day, not the next.
                larderItems.AddItem(ObjectDB.instance.GetItemPrefab("Raspberry"), 10);   // enough for every hungry settler
                float hungryWait = 0f;
                for (; eater != null && eater.Record.Hungry && hungryWait < Meals.HungryLookSeconds + 40f; hungryWait += 0.5f) yield return new WaitForSeconds(0.5f);
                Check($"Food put in after a hungry settler's meal is eaten the same day ({eater?.CitizenName}: hungry {eater?.Record.Hungry}, " +
                      $"after {hungryWait:0} s, last meal day {eater?.Record.LastMealDay} of {today})",
                    eater != null && !eater.Record.Hungry && eater.Record.LastMealDay == today);
                for (ItemDrop.ItemData it = larderItems.GetItem("Raspberry", -1, true); it != null; it = larderItems.GetItem("Raspberry", -1, true))
                    larderItems.RemoveItem(it);   // none left over for the newcomer checks, which start with no food
                eater.MarkMeal(EnvMan.instance.GetDay(ZNet.instance.GetTimeSeconds()), hungry: false);   // fed again, today (the game's day ran on meanwhile: the player saw "Hungry" stuck over her head)
            }

            // Newcomers: with a free bed and food in the Town Hall's chest one comes and eats it; without food nobody; one a day.
            TownHallModule hallModule = hall != null ? hall.GetComponent<TownHallModule>() : null;
            if (hallModule == null || larder == null || longhouse == null || longhouseSite == null)
                Check("The Town Hall and the Longhouse for the newcomer checks", false);
            else
            {
                EnvMan.instance.m_debugTime = 0.5f;   // noon
                int ColonySize() => Citizen.All.Count(c => c != null && c.Record.ColonyId == colony);
                // Two free beds (one for the newcomer, one to show he's the only one today): the Longhouse goes up a level
                // (two more beds) as often as needed, the Builder's Hut first when the Longhouse would pass it.
                for (int round = 0; hallModule.Needs().FreeBeds < 2 && round < 3; round++)
                {
                    if (longhouse.Level >= builderHut.Level)
                    {
                        int builderLevel = builderHut.Level;
                        builderSite.RequestUpgradeNow();
                        for (float waited = 0f; builderHut.Level == builderLevel && waited < 45f; waited += 0.5f) yield return new WaitForSeconds(0.5f);
                    }
                    int level = longhouse.Level, bedsBefore = residence != null ? residence.Beds().Count : 0;
                    longhouseSite.RequestUpgradeNow();
                    for (float waited = 0f; (longhouse.Level == level || residence == null || residence.Beds().Count <= bedsBefore) && waited < 45f; waited += 0.5f)
                        yield return new WaitForSeconds(0.5f);
                    for (float waited = 0f; hallModule.Needs().FreeBeds < 2 && waited < 5f; waited += 0.5f) yield return new WaitForSeconds(0.5f);
                }
                // The bigger Longhouse, after going up a level. Seen: checked the moment it stood, before the game's paths knew
                // its new floors, every bed was "3 m short".
                for (float waited = 0f; residence != null && UnreachableBeds(residence).Count > 0 && waited < WalkingMapSeconds; waited += 1f) yield return new WaitForSeconds(1f);
                CheckBedsReachable(residence);
                ColonyNeeds room = hallModule.Needs();
                int sizeBefore = ColonySize();
                yield return new WaitForSeconds(12f);   // the Town Hall looks every 10 s
                NewcomerWait noFood = Newcomers.WaitsFor(hallModule.Needs(), hallModule.NewcomerRules);
                string summary = TownHallWindow.Summary(hallModule);
                string saysFood = string.Format(Localization.instance.Localize("$vc_newcomer_needs_food"), ColonyConfig.NewcomerFood.Value);
                Check($"With free beds but no food in the Town Hall nobody comes, and the Town Hall says so ({room.FreeBeds} free beds of {room.Beds}, " +
                      $"{sizeBefore} -> {ColonySize()} settlers, waits for {noFood}; window: '{summary.Replace('\n', ' ')}')",
                    room.FreeBeds >= 2 && ColonySize() == sizeBefore && noFood == NewcomerWait.Food && summary.Contains(saysFood));
                // 1.4.2: the summary says the food it keeps for newcomers. This colony keeps nothing (DevHelpers.KeepNothing), so
                // the note is absent; then a Town Hall's own start (Food (any) x 10) is set for a moment, read, and put back.
                string keptWords = Localization.instance.Localize("$vc_food_kept_newcomers");
                string keptTail = keptWords.Substring(keptWords.IndexOf("{0}", System.StringComparison.Ordinal) + 3);
                ZDO hallZdo = hallModule.GetComponent<ZNetView>()?.GetZDO();
                string keepWas = hallZdo != null ? hallZdo.GetString(SaveKeys.Keep, "") : "";
                string withKeep = "";
                if (hallZdo != null && hallModule.GetComponent<ZNetView>().IsOwner())
                {
                    hallZdo.Set(SaveKeys.Keep, StockList.Decode("", true).Encode());
                    withKeep = TownHallWindow.Summary(hallModule);
                    hallZdo.Set(SaveKeys.Keep, keepWas);
                }
                string saysKept = string.Format(keptWords, StockList.TownHallFood);
                Check($"The Town Hall's summary says how much food it keeps for newcomers, and nothing when it keeps none " +
                      $"(keeping {StockList.TownHallFood}: '{withKeep.Replace('\n', ' ')}'; keeping none: '{summary.Replace('\n', ' ')}')",
                    withKeep.Contains(saysKept) && !summary.Contains(keptTail));

                int foodEach = ColonyConfig.NewcomerFood.Value;
                // Mood (0.17): with free beds and food, nobody joins an unhappy colony, and the Town Hall says why.
                if (ColonyConfig.Mood.Value)
                {
                    List<Citizen> moodMembers = Citizen.All.Where(c => c != null && c.Record.ColonyId == colony).ToList();
                    foreach (Citizen c in moodMembers) c.SetMoodForTest(25, "$vc_mood_nohome:-15,$vc_mood_hungry:-20");
                    // The Town Hall judges the mood from its roster: the food goes in once it sees the colony unhappy (0.22 full
                    // run 1: a newcomer came a second after the food, the roster still content).
                    for (float waited = 0f; hallModule.Needs().Mood != MoodLevel.Unhappy && waited < 30f; waited += 0.5f) yield return new WaitForSeconds(0.5f);
                    larder.GetInventory().AddItem(ObjectDB.instance.GetItemPrefab("Raspberry"), foodEach);
                    int unhappyBefore = ColonySize();
                    yield return new WaitForSeconds(12f);   // the Town Hall looks every 10 s
                    ColonyNeeds unhappy = hallModule.Needs();
                    NewcomerWait unhappyWait = Newcomers.WaitsFor(unhappy, hallModule.NewcomerRules);
                    string unhappySummary = TownHallWindow.Summary(hallModule);
                    Check($"An unhappy colony gets no newcomer, and the Town Hall says why ({unhappyBefore} -> {ColonySize()} settlers, colony {unhappy.Mood}, " +
                          $"waits for {unhappyWait}; window: '{unhappySummary.Replace('\n', ' ')}')",
                        ColonySize() == unhappyBefore && unhappy.Mood == MoodLevel.Unhappy && unhappyWait == NewcomerWait.Mood
                        && unhappySummary.Contains(Localization.instance.Localize("$vc_newcomer_unhappy")));
                    foreach (Citizen c in moodMembers.Where(c => c != null)) c.SetMoodForTest(50);   // content again for the checks after (the roster keeps a known mood: 0.19)
                    for (ItemDrop.ItemData it = larder.GetInventory().GetItem("Raspberry", -1, true); it != null; it = larder.GetInventory().GetItem("Raspberry", -1, true))
                        larder.GetInventory().RemoveItem(it);
                }
                var here = new HashSet<string>(Citizen.All.Where(c => c != null).Select(c => c.Id));
                larder.GetInventory().AddItem(ObjectDB.instance.GetItemPrefab("Raspberry"), foodEach);
                int arrivalDay = EnvMan.instance.GetDay();   // the game's day runs on while the test holds the time of day
                Citizen newcomer = null;
                for (float waited = 0f; newcomer == null && waited < 15f; waited += 0.5f)
                {
                    yield return new WaitForSeconds(0.5f);
                    newcomer = Citizen.All.FirstOrDefault(c => c != null && c.Record.ColonyId == colony && !here.Contains(c.Id));
                }
                int berriesLeft = hall.Storage.Contents().TryGetValue("Raspberry", out int rb) ? rb : 0;
                Check($"With a free bed and {foodEach} food in the Town Hall a newcomer comes and eats it ({(newcomer != null ? newcomer.CitizenName : "nobody")}, " +
                      $"{berriesLeft} raspberries left, message '{NewcomerMessages.LastShown}')",
                    newcomer != null && berriesLeft == 0 && NewcomerMessages.LastShown.Contains(newcomer.Record.Name));
                for (float waited = 0f; newcomer != null && !newcomer.Record.HasHome && waited < 15f; waited += 0.5f)
                    yield return new WaitForSeconds(0.5f);
                Check($"The newcomer gets a bed in the Longhouse ({(newcomer != null ? newcomer.Record.Home : "-")})",
                    newcomer != null && newcomer.Record.Home.StartsWith(longhouse.Id + "#"));
                Check($"The saga tells of the newcomer's arrival ({SagaLines(hallModule.GetComponent<ColonyBuilding>())})",
                    newcomer != null && SagaHas(hallModule.GetComponent<ColonyBuilding>(), "came", newcomer.Record.Name));   // "came" since 0.16

                // Recall (the Town Hall window's button): a settler sent 25 m off comes back by the Town Hall at once.
                CitizenAI recallAi = newcomer != null ? newcomer.GetComponent<CitizenAI>() : null;
                if (recallAi == null) Check("A settler to call back", false);
                else
                {
                    Vector3 recallHallAt = hallModule.transform.position;
                    Vector3 away = recallHallAt + Vector3.right * 25f;
                    away.y = ZoneSystem.instance.GetGroundHeight(away);
                    recallAi.TestWalk(away, 25f);
                    for (float waited = 0f; Utils.DistanceXZ(newcomer.transform.position, recallHallAt) < 15f && waited < 25f; waited += 0.5f)
                        yield return new WaitForSeconds(0.5f);
                    float before = Utils.DistanceXZ(newcomer.transform.position, recallHallAt);
                    recallAi.TestWalk(away, 0f);   // the test walk ends: his own routine again
                    ColonyBuilding refusedHall = hallModule.GetComponent<ColonyBuilding>();
                    newcomer.RecallAsForTest(987654321L);   // a stranger asks
                    yield return new WaitForSeconds(1f);
                    float afterStranger = Utils.DistanceXZ(newcomer.transform.position, recallHallAt);
                    Check($"A player who may not manage the colony can't call a settler back (still {afterStranger:0} m away, saga: {SagaLines(refusedHall)})",
                        afterStranger >= 10f && !SagaHas(refusedHall, "recalled", newcomer.CitizenName));
                    newcomer.RequestRecall();
                    yield return new WaitForSeconds(1f);
                    float after = Utils.DistanceXZ(newcomer.transform.position, recallHallAt);
                    ColonyBuilding recallHall = hallModule.GetComponent<ColonyBuilding>();
                    Check($"Recall brings {newcomer.CitizenName} back to the Town Hall ({before:0} m -> {after:0.0} m away; saga: {SagaLines(recallHall)})",
                        before >= 10f && after <= 6.5f && SagaHas(recallHall, "recalled", newcomer.CitizenName));

                    // The colony keeps its citizens' records (0.10): a settler whose body is lost shows as missing, and
                    // Bring back makes him again as himself (name, job) at the Town Hall.
                    string lostId = newcomer.Id, lostName = newcomer.CitizenName, lostJob = newcomer.Record.Job;
                    for (float waited = 0f; hallModule.Keepsakes.Get(lostId) == null && waited < 10f; waited += 0.5f) yield return new WaitForSeconds(0.5f);
                    hallModule.RequestBringBack(lostId);   // not missing: nothing happens
                    yield return new WaitForSeconds(0.5f);
                    bool notTwice = Citizen.All.Count(c => c != null && c.Id == lostId) == 1;
                    Remove(newcomer.gameObject);   // his body is lost (not a death: he stays on the roster)
                    for (float waited = 0f; !(hallModule.Roster.Get(lostId)?.IsMissing ?? false) && waited < 15f; waited += 0.5f) yield return new WaitForSeconds(0.5f);
                    bool missing = hallModule.Roster.Get(lostId)?.IsMissing ?? false;
                    hallModule.RequestBringBack(lostId);   // only just missing: not yet (he may have stepped out of sight)
                    yield return new WaitForSeconds(1f);
                    bool notSameDay = !Citizen.All.Any(c => c != null && c.Id == lostId);
                    hallModule.BackdateMissingForTest(lostId, 1);   // a day has passed
                    hallModule.RequestBringBack(lostId);
                    Citizen back = null;
                    for (float waited = 0f; back == null && waited < 5f; waited += 0.5f)
                    {
                        yield return new WaitForSeconds(0.5f);
                        back = Citizen.All.FirstOrDefault(c => c != null && c.Id == lostId);
                    }
                    Check($"A settler whose body was lost shows as missing and, after a day, Bring back makes him again as himself (kept: {hallModule.Keepsakes.Get(lostId) != null}, " +
                          $"missing: {missing}, not on the same day: {notSameDay}, back: {(back != null ? back.CitizenName + " the " + back.Record.Job + ", life " + back.Record.Life : "no")}, " +
                          $"never two of him: {notTwice}; saga: {SagaLines(recallHall)})",
                        notTwice && missing && notSameDay && back != null && back.CitizenName == lostName && back.Record.Job == lostJob && back.Record.Life == 1
                        && Utils.DistanceXZ(back.transform.position, recallHallAt) <= 6f && SagaHas(recallHall, "returned", lostName));
                    if (back != null) newcomer = back;

                    // Words above heads (0.11): a hungry settler near the player says so; fed again, the words go.
                    if (newcomer != null && Vector3.Distance(newcomer.transform.position, player.transform.position) <= 14f)
                    {
                        int dayNow = EnvMan.instance.GetDay();
                        // He stays near you while the words are watched (0.24 run: brought back, she went off to chat with
                        // a friend, out of the words' 15 m before her moment came).
                        CitizenAI stays = newcomer.GetComponent<CitizenAI>();
                        stays?.TestWalk(newcomer.transform.position, 40f);
                        newcomer.MarkMeal(dayNow, hungry: true);
                        // You look at him: words show only for a settler on the screen (1.4.1).
                        void LookAtHim() => player.SetLookDir((newcomer.transform.position + Vector3.up * 1.5f - player.GetEyePoint()).normalized);
                        LookAtHim();
                        SettlerWords.RefreshForTest();
                        // "Hungry" shows 8 s in every 30 (0.18): wait for his moment.
                        for (float waited = 0f; SettlerWords.ShownForTest(newcomer) != HelpWords.HungryToken && waited < 32f; waited += 0.5f)
                        {
                            yield return new WaitForSeconds(0.5f);
                            LookAtHim();
                            SettlerWords.RefreshForTest();
                        }
                        int shownHungry = SettlerWords.ShownCount;
                        string hisWords = SettlerWords.ShownForTest(newcomer);
                        bool inChat = Chat.instance != null && Chat.instance.CurrentNpcTexts() > 0;
                        // You turn away from him, sideways (the camera stands behind you: with your back to him it could
                        // stand behind him too): his words go, not left drawn at an odd place on the screen (the player,
                        // 2026-10-19: "messages stack up in weird places on the screen" just out of view).
                        Vector3 toHim = newcomer.transform.position - player.GetEyePoint();
                        toHim.y = 0f;
                        Vector3 aside = Vector3.Cross(Vector3.up, toHim.normalized);
                        for (int f = 0; f < 4; f++)
                        {
                            player.SetLookDir(aside);
                            yield return new WaitForSeconds(0.2f);
                        }
                        SettlerWords.RefreshForTest();
                        string behindWords = SettlerWords.ShownForTest(newcomer);
                        Check($"Words show only for a settler on the screen: with your back to him his words go ('{hisWords}' -> '{behindWords}')",
                            hisWords == HelpWords.HungryToken && string.IsNullOrEmpty(behindWords));
                        LookAtHim();
                        yield return new WaitForSeconds(0.3f);   // the camera follows a moment later
                        LookAtHim();
                        newcomer.MarkMeal(dayNow, hungry: false);
                        SettlerWords.RefreshForTest();
                        // His own words, not the count: fed, he may greet you or chat instead (run mood1: in the rain, two
                        // settlers had words before and after).
                        string hisWordsFed = SettlerWords.ShownForTest(newcomer);
                        HuginTips.Unqueue(HuginTips.Hunger);   // the test's hunger: the player still gets the tip at a real one
                        stays?.TestWalk(newcomer.transform.position, 0f);   // back to his day
                        Check($"A hungry settler near you says so above his head, and the words go once he's fed (shown: {shownHungry}, in the game's NPC texts: {inChat}, " +
                              $"his words '{hisWords}' -> '{hisWordsFed}')", shownHungry >= 1 && inChat && hisWords == HelpWords.HungryToken && hisWordsFed != HelpWords.HungryToken);
                    }
                    else Check($"Words above heads skipped: the newcomer stands {(newcomer != null ? Vector3.Distance(newcomer.transform.position, player.transform.position) : -1):0} m from you", true);
                }

                larder.GetInventory().AddItem(ObjectDB.instance.GetItemPrefab("Raspberry"), foodEach);
                int sizeAfter = ColonySize();
                yield return new WaitForSeconds(12f);
                NewcomerWait nextDay = Newcomers.WaitsFor(hallModule.Needs(), hallModule.NewcomerRules);
                // Run 31: the game's next day began during the test (every 30 minutes); then another one may come.
                bool newDay = EnvMan.instance.GetDay() != arrivalDay;
                Check($"One newcomer a day ({sizeAfter} -> {ColonySize()} settlers with food and a free bed, waits for {nextDay}" +
                      $"{(newDay ? "; a new day began meanwhile" : "")})",
                    newDay || (ColonySize() == sizeAfter && nextDay == NewcomerWait.Tomorrow));
                for (ItemDrop.ItemData it = larder.GetInventory().GetItem("Raspberry", -1, true); it != null; it = larder.GetInventory().GetItem("Raspberry", -1, true))
                    larder.GetInventory().RemoveItem(it);
            }

            // The forager: a Forager's Hut hires one; he picks a wild raspberry bush by his hut and brings the berries
            // home; a planted carrot next to it is the player's and stays.
            EnvMan.instance.m_debugTime = 0.5f;   // noon
            Vector3 foragerSpot = Usable(hutSpot, Around(20f, 0f), Around(-20f, 0f), Around(0f, -24f), Around(24f, 24f), Around(-24f, -20f));
            ColonyBuilding foragerHut = DevHelpers.Place(BuildingTypes.ForagerHut.PrefabName, foragerSpot, Quaternion.identity, player.GetPlayerID());
            ConstructionModule foragerSite = foragerHut != null ? foragerHut.GetComponent<ConstructionModule>() : null;
            for (float waited = 0f; foragerSite != null && foragerSite.Order == null && waited < 15f; waited += 0.5f)
                yield return new WaitForSeconds(0.5f);
            if (foragerSite != null && foragerSite.Order != null) foragerSite.FinishNow();
            for (float waited = 0f; foragerSite != null && !foragerSite.IsBuilt && waited < 40f; waited += 0.5f)
                yield return new WaitForSeconds(0.5f);
            WorkerModule foragerWork = foragerHut != null ? foragerHut.GetComponent<WorkerModule>() : null;
            Citizen forager = null;
            for (float waited = 0f; foragerWork != null && forager == null && waited < 20f; waited += 0.5f)
            {
                yield return new WaitForSeconds(0.5f);
                if (foragerWork.FindWorker(out Citizen found, out _) == WorkerModule.WorkerState.Loaded) forager = found;
            }
            CheckDoorways("Forager's Hut", foragerSite);
            Check($"A Forager's Hut hires a forager ({(forager != null ? forager.CitizenName + ", " + forager.Record.Job : "nobody")})",
                forager != null && forager.Record.Job == JobDefinitions.ForagerId);
            if (forager != null)
            {
                float radiusBefore = ColonyConfig.ForagerWorkRadius.Value;
                ColonyConfig.ForagerWorkRadius.Value = 10f;   // the test's bush, not the meadow's
                Vector3 hutAt = foragerHut.transform.position;
                // Not on cultivated ground (a bush there is a player's): an earlier run's field may lie here (run 21 of 0.12).
                Vector3 bushAt = new[] { new Vector3(4f, 0f, -5f), new Vector3(5f, 0f, 3f), new Vector3(-5f, 0f, 3f), new Vector3(0f, 0f, -6f) }
                    .Select(o => hutAt + o)
                    .FirstOrDefault(p => !(Heightmap.FindHeightmap(p) is Heightmap h && h.IsCultivated(p)));
                if (bushAt == Vector3.zero) bushAt = hutAt + new Vector3(4f, 0f, -5f);
                GameObject bush = DevHelpers.Spawn("RaspberryBush", bushAt);
                GameObject carrot = DevHelpers.Spawn("Pickable_Carrot", hutAt + new Vector3(-4f, 0f, -5f));
                GameObject meat = DevHelpers.Spawn("CookedMeat", hutAt + new Vector3(5f, 0.3f, -5f));   // someone's, by the bush
                int BerriesHome() => foragerHut.Storage.Contents().TryGetValue("Raspberry", out int n) ? n : 0;
                for (float waited = 0f; BerriesHome() == 0 && waited < 90f; waited += 0.5f) yield return new WaitForSeconds(0.5f);
                Pickable carrotPick = carrot != null ? carrot.GetComponent<Pickable>() : null;
                Pickable bushPick = bush != null ? bush.GetComponent<Pickable>() : null;
                Check($"The forager picks a wild raspberry bush by his hut and brings the berries home ({BerriesHome()} in his chest; " +
                      $"bush picked: {bushPick != null && bushPick.GetPicked()}, '{Localization.instance.Localize(forager.Status)}')",
                    BerriesHome() > 0);
                // What counts is that he didn't take it. Seen: a boar came by the test spot and the meat was gone.
                int meatTaken = forager.Carry.Count("CookedMeat") + (foragerHut.Storage.Contents().TryGetValue("CookedMeat", out int m) ? m : 0);
                Check($"He leaves someone's cooked meat lying by the bush (in his hands or his chest: {meatTaken}; still lying there: {meat != null})", meatTaken == 0);
                Check($"He leaves a planted carrot alone (spawned: {carrotPick != null}, picked: {carrotPick != null && carrotPick.GetPicked()}, " +
                      $"grows back: {carrotPick != null && carrotPick.m_respawnTimeMinutes > 0f})",
                    carrotPick != null && !carrotPick.GetPicked());

                // His hut's "What to pick" list: a dandelion (not food) is his too and goes home; unticked, the next one stays.
                // Off cultivated ground, as the bush (runs polish2 and polish3: an earlier run's field lay there, and a plant on
                // a field is a player's, rightly left alone).
                Vector3 Wild(params Vector3[] offsets) =>
                    offsets.Select(o => hutAt + o).FirstOrDefault(p => !(Heightmap.FindHeightmap(p) is Heightmap h && h.IsCultivated(p)));
                Vector3 dandelionAt = Wild(new Vector3(3f, 0f, 5f), new Vector3(5f, 0f, -3f), new Vector3(-5f, 0f, -3f), new Vector3(3f, 0f, -6f), new Vector3(6f, 0f, 2f));
                GameObject dandelion = DevHelpers.Spawn("Pickable_Dandelion", dandelionAt != Vector3.zero ? dandelionAt : hutAt + new Vector3(3f, 0f, 5f));
                int DandelionsHome() => foragerHut.Storage.Contents().TryGetValue("Dandelion", out int d) ? d : 0;
                for (float waited = 0f; DandelionsHome() == 0 && waited < 90f; waited += 0.5f) yield return new WaitForSeconds(0.5f);
                Check($"The forager picks a dandelion too and brings it home ({DandelionsHome()} in his chest; his list: " +
                      $"{string.Join(", ", Jobs.ForageList.Kinds.Select(k => k.Item))})", DandelionsHome() > 0);
                // Flint and branches (the player's decision of 2026-10-15): on his list, unticked until the player ticks them.
                ForageLine flintLine = Jobs.ForageList.For(foragerHut).FirstOrDefault(l => l.Kind.Item == "Flint");
                bool flintOffAtFirst = flintLine != null && !flintLine.Ticked;
                foragerWork.RequestSetPickTicked("Flint", true);
                yield return new WaitForSeconds(1f);
                ForageLine flintTicked = Jobs.ForageList.For(foragerHut).FirstOrDefault(l => l.Kind.Item == "Flint");
                Check($"Flint is on his list unticked to start, and ticks on (on the list: {flintLine != null}, unticked at first: {flintOffAtFirst}, " +
                      $"ticked now: {flintTicked != null && flintTicked.Ticked}; saved '{string.Join(",", foragerWork.PickUnticked)}')",
                    flintOffAtFirst && flintTicked != null && flintTicked.Ticked);
                foragerWork.RequestSetPickTicked("Flint", false);
                foragerWork.RequestSetPickTicked("Dandelion", false);
                yield return new WaitForSeconds(1f);
                Vector3 dandelion2At = Wild(new Vector3(-3f, 0f, 5f), new Vector3(-5f, 0f, 3f), new Vector3(4f, 0f, 4f), new Vector3(-3f, 0f, -6f), new Vector3(-6f, 0f, -2f));
                GameObject dandelion2 = DevHelpers.Spawn("Pickable_Dandelion", dandelion2At != Vector3.zero ? dandelion2At : hutAt + new Vector3(-3f, 0f, 5f));
                Pickable secondPick = dandelion2 != null ? dandelion2.GetComponent<Pickable>() : null;
                yield return new WaitForSeconds(25f);
                Check($"Unticked on his list, he leaves the next dandelion (spawned: {secondPick != null}, picked: {secondPick != null && secondPick.GetPicked()}; " +
                      $"unticked: {string.Join(",", foragerWork.PickUnticked)}; '{Localization.instance.Localize(forager.Status)}')",
                    secondPick != null && !secondPick.GetPicked() && foragerWork.PickUnticked.Contains("Dandelion"));
                foragerWork.RequestSetPickTicked("Dandelion", true);
                if (dandelion != null) Remove(dandelion);
                if (dandelion2 != null) Remove(dandelion2);
                ColonyConfig.ForagerWorkRadius.Value = radiusBefore;
                if (bush != null) Remove(bush);
                if (carrot != null) Remove(carrot);
                if (meat != null) Remove(meat);
            }

            // The farmer (0.12): a Farm hires one; with a Cultivator and carrot seeds in its chest he cultivates a plot and
            // plants a carrot; a ripe carrot on one of his plots goes into the chest; one outside his field stays.
            EnvMan.instance.m_debugTime = 0.5f;   // noon
            Vector3 farmSpot = Usable(foragerSpot, Around(-20f, 20f), Around(20f, -20f), Around(-26f, 0f), Around(0f, 26f), Around(26f, 0f));
            ColonyBuilding farm = DevHelpers.Place(BuildingTypes.Farm.PrefabName, farmSpot, Quaternion.identity, player.GetPlayerID());
            ConstructionModule farmSite = farm != null ? farm.GetComponent<ConstructionModule>() : null;
            for (float waited = 0f; farmSite != null && farmSite.Order == null && waited < 15f; waited += 0.5f)
                yield return new WaitForSeconds(0.5f);
            if (farmSite != null && farmSite.Order != null) farmSite.FinishNow();
            for (float waited = 0f; farmSite != null && !farmSite.IsBuilt && waited < 40f; waited += 0.5f)
                yield return new WaitForSeconds(0.5f);
            WorkerModule farmWork = farm != null ? farm.GetComponent<WorkerModule>() : null;
            Citizen farmer = null;
            for (float waited = 0f; farmWork != null && farmer == null && waited < 25f; waited += 0.5f)
            {
                yield return new WaitForSeconds(0.5f);
                if (farmWork.FindWorker(out Citizen found, out _) == WorkerModule.WorkerState.Loaded) farmer = found;
            }
            // A Farm works only its fields (0.20.1, MineColonies' way): a 5x5 field in front of it, for the Farm's list.
            Colony.FieldMarker farmField = farm != null ? DevHelpers.PlaceField(farm, farm.transform.position + farm.transform.forward * 9f) : null;
            yield return new WaitForSeconds(1f);
            List<Vector3> FieldPlotsOf(ColonyBuilding f) => f != null ? FarmerJob.RulesOf(f).Select(r => CoreVectors.U(r.At)).ToList() : new List<Vector3>();
            Check($"A Farm hires a farmer and works the field marked for it ({(farmer != null ? farmer.CitizenName + ", " + farmer.Record.Job : "nobody")}; " +
                  $"its field: {FieldPlotsOf(farm).Count} plots, {CropList.Crops.Count} crops known)",
                farmer != null && farmer.Record.Job == JobDefinitions.FarmerId && farm != null && FieldPlotsOf(farm).Count > 0);
            // The hut's tick lists turn pages when longer than a page (mods add crops; the game alone has 14).
            if (farm != null)
            {
                Gui.Windows.Grow.Open(farm);
                string firstPage = Gui.Windows.Grow.TextForTest();
                Gui.Windows.Grow.TurnForTest(1);
                string secondPage = Gui.Windows.Grow.TextForTest();
                Gui.Windows.Grow.Close();
                int cropCount = CropList.Crops.Count;
                Check($"The What to grow list turns pages when it's longer than a page ({cropCount} crops; page 1: {firstPage.Contains("1 / ")}, " +
                      $"page 2: {secondPage.Contains("2 / ")})", cropCount <= 12 || (firstPage.Contains("1 / ") && secondPage.Contains("2 / ")));
            }
            var farmThings = new List<GameObject>();
            if (farmer != null)
            {
                Inventory farmChest = farm.GetComponent<Container>().GetInventory();
                farmChest.AddItem(ObjectDB.instance.GetItemPrefab(FarmerJob.CultivatorItem), 1);
                farmChest.AddItem(ObjectDB.instance.GetItemPrefab("CarrotSeeds"), 3);
                List<Vector3> plots = FieldPlotsOf(farm);
                bool Cultivated(Vector3 at)
                {
                    Heightmap ground = Heightmap.FindHeightmap(at);
                    return ground != null && ground.IsCultivated(at);
                }
                int bareBefore = plots.Count(pl => !Cultivated(pl));
                Plant PlantedOnPlot() => WorldScan.Plants().FirstOrDefault(pl => pl != null && Utils.GetPrefabName(pl.gameObject) == "sapling_carrot"
                    && FieldPlots.OnPlot(CoreVectors.V(pl.transform.position), plots.Select(CoreVectors.V)));
                // A plot cultivated by an earlier run is planted first; then he cultivates a new one (run 20 of 0.12).
                for (float waited = 0f; (PlantedOnPlot() == null || (bareBefore > 0 && plots.Count(pl => !Cultivated(pl)) >= bareBefore)) && waited < 120f; waited += 1f)
                    yield return new WaitForSeconds(1f);
                int bareAfter = plots.Count(pl => !Cultivated(pl));
                Check($"The farmer cultivates a plot and plants a carrot from the Farm's seeds (planted: {PlantedOnPlot() != null}, " +
                      $"bare plots {bareBefore} before, {bareAfter} after, seeds left: {farmChest.CountItems("$item_carrotseeds")}, " +
                      $"'{Localization.instance.Localize(farmer.Status)}')", PlantedOnPlot() != null && (bareBefore == 0 || bareAfter < bareBefore));
                // Review of 0.12: the game's own plants check (room, biome, cultivated ground, roof) runs about every 10 s. One of
                // his carrots grows (the full 1.3 run's spot had a tree: under it the game says NoSun, and he clears such a one).
                Plant seedling = PlantedOnPlot();
                for (float waited = 0f; seedling != null && waited < 14f; waited += 1f) yield return new WaitForSeconds(1f);
                List<Plant> hisCarrots = WorldScan.Plants().Where(pl => pl != null && Utils.GetPrefabName(pl.gameObject) == "sapling_carrot"
                    && FieldPlots.OnPlot(CoreVectors.V(pl.transform.position), plots.Select(CoreVectors.V))).ToList();
                Check($"A carrot he planted can grow there (the game says: {string.Join(", ", hisCarrots.Select(pl => pl.GetStatus().ToString()).ToArray())})",
                    hisCarrots.Any(pl => pl.GetStatus() == global::Plant.Status.Healthy));

                // A ripe carrot on a free plot of his (as one grown there), and one outside his field. The carrot-seed crop is
                // unticked meanwhile: with it ticked and few carrot seeds, he sows the carrot again at once (as he should).
                farmWork.RequestSetGrowTicked("sapling_seedcarrot", false);
                // One he has cultivated, so he has been there (run 25 of 0.13: on a slope the last plot was out of his reach).
                List<Vector3> freePlots = plots.Where(pl => !WorldScan.Plants().Any(g => g != null && Utils.DistanceXZ(g.transform.position, pl) < 0.8f)).ToList();
                Vector3 freePlot = freePlots.Where(Cultivated).DefaultIfEmpty(
                    freePlots.Count > 0 ? freePlots[freePlots.Count - 1] : plots.Count > 0 ? plots[plots.Count - 1] : farm.transform.position + Vector3.forward * 5f).First();
                GameObject ripe = DevHelpers.Spawn("Pickable_Carrot", freePlot);
                Vector3 outside = farmField != null
                    ? farmField.transform.position + farm.transform.right * (FieldSizes.New * FieldPlots.Spacing / 2f + 6f)
                    : farm.transform.position + Vector3.right * 15f;
                outside.y = ZoneSystem.instance.GetGroundHeight(outside);
                GameObject notHis = DevHelpers.Spawn("Pickable_Carrot", outside);
                farmThings.Add(ripe);
                farmThings.Add(notHis);
                int CarrotsHome() => farm.Storage.Contents().TryGetValue("Carrot", out int n) ? n : 0;
                for (float waited = 0f; CarrotsHome() == 0 && waited < 90f; waited += 1f) yield return new WaitForSeconds(1f);
                Pickable notHisPick = notHis != null ? notHis.GetComponent<Pickable>() : null;
                Check($"He harvests a ripe carrot on his plot into the Farm's chest, and leaves one outside his field ({CarrotsHome()} in the chest; " +
                      $"the other picked: {notHisPick != null && notHisPick.GetPicked()}; '{Localization.instance.Localize(farmer.Status)}')",
                    CarrotsHome() > 0 && notHisPick != null && !notHisPick.GetPicked());
                farmWork.RequestSetGrowTicked("sapling_seedcarrot", true);
            }
            foreach (GameObject thing in farmThings) if (thing != null) Remove(thing);
            if (farm != null)
            {
                foreach (Plant grown in WorldScan.Plants().Where(pl => pl != null && Utils.DistanceXZ(pl.transform.position, farm.transform.position) <= 25f).ToList())
                    Remove(grown.gameObject);
                // The field back to grass: cultivated ground left behind made a later run's wild bush count as planted.
                foreach (Vector3 plot in FieldPlotsOf(farm)) GroundShaper.Grass(plot);
            }
            if (farmField != null) Remove(farmField.gameObject);

            // A Farm without a field works no ground of its own (0.20.1: fields only, MineColonies' way).
            if (farm != null)
            {
                Vector3 at = farm.transform.position;
                Vector3 secondSpot = Usable(null, at + Vector3.right * 10f, at - Vector3.right * 10f, at + Vector3.forward * 10f, at - Vector3.forward * 10f);
                secondSpot.y = ZoneSystem.instance.GetGroundHeight(secondSpot);
                ColonyBuilding farm2 = DevHelpers.Place(BuildingTypes.Farm.PrefabName, secondSpot, Quaternion.identity, player.GetPlayerID());
                ConstructionModule farm2Site = farm2 != null ? farm2.GetComponent<ConstructionModule>() : null;
                for (float waited = 0f; farm2Site != null && farm2Site.Order == null && waited < 15f; waited += 0.5f)
                    yield return new WaitForSeconds(0.5f);
                if (farm2Site != null && farm2Site.Order != null) farm2Site.FinishNow();
                for (float waited = 0f; farm2Site != null && !farm2Site.IsBuilt && waited < 40f; waited += 0.5f)
                    yield return new WaitForSeconds(0.5f);
                int secondPlots = farm2 != null ? FarmerJob.RulesOf(farm2).Count : -1;
                Check($"A Farm without a field works no ground of its own ({secondPlots} plots)", farm2 != null && secondPlots == 0);
                if (farm2Site != null) farm2Site.RemoveBuilderPieces();
                if (farm2 != null) Remove(farm2.gameObject);
            }

            // Repair (0.13): with nothing to build, the builder mends a damaged piece of a finished hut; a piece knocked out
            // of a finished hut reopens its order as a repair, and he puts it back.
            if (farmSite != null && farmSite.IsBuilt && builder != null)
            {
                DevHelpers.ClearMonsters(farm.transform.position, 40f);   // monsters by a hut hold its rebuilding off
                // No meals meanwhile: a new day starting in the middle sent him to breakfast first, and the three checks
                // ran out behind it (0.26 self-test rerun). Meals are checked elsewhere.
                bool eatWas = ColonyConfig.SettlersEat.Value;
                ColonyConfig.SettlersEat.Value = false;
                // Other open orders are set aside meanwhile, so he has nothing to build.
                List<ConstructionModule> setAside = ConstructionModule.All.Where(s => s != null && s.IsValid && s != farmSite
                    && s.Order is BuildOrderRecord o && o.State != BuildState.Done && !o.Paused).ToList();
                foreach (ConstructionModule s in setAside) s.RequestPause(true);
                // From just outside his own hut: just after a Longhouse went up a level the game's paths don't know its new
                // floors yet, and a builder inside it couldn't find its door; he put the mend off for five minutes and the
                // check ran out (two of three full runs on 2026-10-18). That's walking, checked elsewhere; this is mending.
                builder.RequestRecallToHut();
                yield return new WaitForSeconds(2f);
                var farmPieces = new List<Piece>();
                Piece.GetAllPiecesInRadius(farm.transform.position, 15f, farmPieces);
                List<WearNTear> wears = farmPieces.Where(p => p != null && PiecePlacer.SiteOf(p.gameObject) == farm.Id)
                    .Select(p => p.GetComponent<WearNTear>()).Where(w => w != null).OrderBy(w => w.transform.position.y).ToList();
                WearNTear hurt = wears.Count > 1 ? wears[wears.Count / 2] : null;
                if (hurt != null)
                {
                    ZNetView hurtView = hurt.GetComponent<ZNetView>();
                    if (!hurtView.IsOwner()) hurtView.ClaimOwnership();
                    float half = hurt.m_health * 0.4f;
                    hurtView.GetZDO().Set(ZDOVars.s_health, half);
                    hurtView.InvokeRPC(ZNetView.Everybody, "RPC_HealthChanged", half);
                }
                yield return new WaitForSeconds(1f);
                float hurtBefore = hurt != null ? hurt.GetHealthPercentage() : 1f;
                // Two minutes: a new day can start meanwhile, and his breakfast comes first (worker polish run 2: he mended
                // the wall as the minute ran out).
                for (float waited = 0f; hurt != null && hurt.GetHealthPercentage() < 0.99f && waited < 120f; waited += 1f)
                    yield return new WaitForSeconds(1f);
                Check($"With nothing to build, the builder mends a damaged piece of a finished hut ({(hurt != null ? Utils.GetPrefabName(hurt.gameObject) : "none")}: " +
                      $"{hurtBefore * 100f:0}% whole, then {(hurt != null ? hurt.GetHealthPercentage() * 100f : 0f):0}%; '{Localization.instance.Localize(builder.Status)}')",
                    hurt != null && hurtBefore < 0.75f && hurt.GetHealthPercentage() >= 0.99f);

                // The topmost piece goes (nothing rests on it); the builder gets what it takes in his hut's chest. No monster by
                // the hut meanwhile: repairs wait for one to go (1.4.3's compatibility run: a Neck spawned by the Farm 4 s before).
                float monsterReach = RepairRules.MonsterRange + 10f;
                DevHelpers.ClearMonsters(farm.transform.position, monsterReach);
                WearNTear top = wears.LastOrDefault(w => w != null && w != hurt);
                string topPrefab = top != null ? Utils.GetPrefabName(top.gameObject) : "";
                Vector3 topAt = top != null ? top.transform.position : Vector3.zero;
                if (top != null)
                {
                    foreach (KeyValuePair<string, int> need in PiecePlacer.Needs(topPrefab))
                        builderHut.GetComponent<Container>().GetInventory().AddItem(ObjectDB.instance.GetItemPrefab(need.Key), need.Value);
                    Remove(top.gameObject);
                }
                yield return new WaitForSeconds(1f);
                farmSite.CheckForRepair();   // the minute's check may have done it meanwhile (review of 0.13)
                BuildOrderRecord repair = farmSite.Order;
                int toPutBack = repair != null ? repair.Total - repair.DoneCount - repair.SkippedCount : 0;
                Check($"A finished hut with a piece knocked out has its order reopened as a repair ({topPrefab}; {toPutBack} to put back, " +
                      $"kind {repair?.Kind}, level {repair?.TargetLevel} of {farm.Level}; the hut still works: {farmSite.IsBuilt})",
                    toPutBack >= 1 && repair != null && repair.Kind == OrderKind.Repair && repair.State != BuildState.Done
                    && repair.TargetLevel == farm.Level && farmSite.IsBuilt);
                bool PutBack()
                {
                    var near = new List<Piece>();
                    Piece.GetAllPiecesInRadius(topAt, 0.5f, near);
                    return near.Any(p => p != null && Utils.GetPrefabName(p.gameObject) == topPrefab && Vector3.Distance(p.transform.position, topAt) < 0.1f);
                }
                for (float waited = 0f; !(PutBack() && farmSite.Order?.State == BuildState.Done) && waited < 120f; waited += 1f)
                {
                    if (Mathf.Repeat(waited, 5f) < 0.01f) DevHelpers.ClearMonsters(farm.transform.position, monsterReach);
                    yield return new WaitForSeconds(1f);
                }
                Check($"The builder puts the piece back and the repair is done (back: {PutBack()}, order {farmSite.Order?.State}; " +
                      $"'{Localization.instance.Localize(builder.Status)}')",
                    PutBack() && farmSite.Order?.State == BuildState.Done);

                // Taken away with a hammer (the game's removal, not a blow), the piece is left out, not put back (review of
                // 0.13: a wall knocked out on purpose was walled up again). The one just put back: nothing rests on it.
                var atTop = new List<Piece>();
                Piece.GetAllPiecesInRadius(topAt, 0.5f, atTop);
                WearNTear byHammer = atTop.Where(p => p != null && Utils.GetPrefabName(p.gameObject) == topPrefab)
                    .Select(p => p.GetComponent<WearNTear>()).FirstOrDefault(w => w != null);
                bool hammered = byHammer != null;   // gone once removed: Unity counts it as null then
                if (hammered) byHammer.Remove();
                yield return new WaitForSeconds(2f);
                DevHelpers.ClearMonsters(farm.transform.position, monsterReach);
                farmSite.CheckForRepair();
                Check($"A piece taken away with a hammer is left out, not put back ({topPrefab}; order {farmSite.Order?.State}, " +
                      $"{farmSite.Order?.SkippedCount} left out)",
                    hammered && farmSite.Order?.State == BuildState.Done && farmSite.Order.SkippedCount >= 1);
                ColonyConfig.SettlersEat.Value = eatWas;
                foreach (ConstructionModule s in setAside) if (s != null && s.IsValid) s.RequestPause(false);
            }

            EnvMan.instance.m_debugTimeOfDay = false;   // time runs again
            EnvMan.instance.SetForceEnvironment("");   // and the weather
            ColonyConfig.NewcomersWalkIn.Value = s_walkInWas;   // the player's setting back (0.16 review M5)
            MoodTracker.HoldForTest = false;
            Log.Event($"vc_selftest: done — {s_pass} passed, {s_fail} failed");
            player.Message(MessageHud.MessageType.Center, $"vc_selftest: {s_pass} passed, {s_fail} failed (details in the log)");
            if (keep) yield break;

            // Remove what the test made: its Town Hall and the settlers it spawned. Settlers that already existed
            // and were taken in keep living; with the Town Hall gone they lose this colony and can join another.
            int removed = 0;
            foreach (Citizen spawned in Citizen.All.Where(c => c != null && !idsBefore.Contains(c.Id)).ToList())
            {
                Remove(spawned.gameObject);
                removed++;
            }
            if (hall != null) Remove(hall.gameObject);
            if (builderHut != null) Remove(builderHut.gameObject);
            if (longhouse != null) Remove(longhouse.gameObject);
            if (foragerHut != null) Remove(foragerHut.gameObject);
            if (farm != null) Remove(farm.gameObject);
            var siteIds = new HashSet<string> { builderSiteId, hutSiteId, longhouse != null ? longhouse.Id : "-", foragerHut != null ? foragerHut.Id : "-",
                farm != null ? farm.Id : "-" };
            var pieces = new List<Piece>();
            Piece.GetAllPiecesInRadius(origin, 45f, pieces);
            int removedPieces = 0;
            // Its own huts' pieces, and any whose hut is gone (a test hut removed on the way). Seen: test sites were
            // left with walls, and later runs found no open ground.
            foreach (Piece piece in pieces.Where(p => p != null && (siteIds.Contains(PiecePlacer.SiteOf(p.gameObject)) || DevHelpers.IsOrphanPiece(p))).ToList())
            {
                Remove(piece.gameObject);
                removedPieces++;
            }
            Log.Event($"vc_selftest: cleaned up (the test Town Hall, the Builder's Hut, {removedPieces} building pieces and {removed} settlers removed)");
        }

        /// <summary>A falling tree's hit with SettlersSafeFromTrees on and off, then the settler killed by a Greydwarf.</summary>
        private static IEnumerator DeathChecks(Citizen victim, TownHallModule townHall, Vector3 origin)
        {
            Character body = victim.Body;
            string name = victim.CitizenName, id = victim.Id, colonyId = victim.Record.ColonyId;   // read before he dies
            bool safe = ColonyConfig.SettlersSafeFromTrees.Value;
            ColonyConfig.SettlersSafeFromTrees.Value = true;
            float before = body.GetHealth();
            body.Damage(TreeHit(victim));
            yield return new WaitForSeconds(0.5f);
            Check($"A falling tree doesn't hurt a settler while SettlersSafeFromTrees is on (health {before:0} -> {body.GetHealth():0}; '{SettlerDeaths.LastCloseCall}')",
                Mathf.Approximately(body.GetHealth(), before) && SettlerDeaths.LastCloseCall.Contains("blocked"));
            ColonyConfig.SettlersSafeFromTrees.Value = false;
            body.Damage(TreeHit(victim));
            yield return new WaitForSeconds(0.5f);
            ColonyConfig.SettlersSafeFromTrees.Value = safe;
            Check($"With it off the tree hurts him (health {before:0} -> {body.GetHealth():0}; '{SettlerDeaths.LastCloseCall}')",
                body.GetHealth() < before && SettlerDeaths.LastCloseCall.Contains("health left"));

            CheckCheatKillSpares(victim);   // 1.4.2: before the real death below

            GameObject prefab = ZNetScene.instance.GetPrefab("Greydwarf");
            Vector3 aside = origin + Vector3.right * 20f;
            aside.y = ZoneSystem.instance.GetGroundHeight(aside);
            Character monster = prefab != null ? Object.Instantiate(prefab, aside, Quaternion.identity).GetComponent<Character>() : null;
            int woodBefore = victim.Carry.Count("Wood"), resinBefore = victim.Carry.Count("Resin");   // he may have picked some up
            victim.Carry.Add("Wood", 7);   // he dies carrying this: it goes into his cairn
            victim.Carry.Add("Resin", 3);
            victim.SaveCarry();
            Vector3 fell = victim.transform.position;
            body.Damage(KillHit(victim, monster));
            yield return new WaitForSeconds(2f);
            Check($"A settler's death is logged with its cause ('{SettlerDeaths.LastReport}')",
                SettlerDeaths.LastReport.Contains(name) && SettlerDeaths.LastReport.Contains("killed by"));
            Check($"Players near the colony are told ('{DeathMessages.LastShown}')", DeathMessages.LastShown.Contains(name));
            Check("The roster dropped the dead settler", townHall.Roster.Get(id) == null);
            Check($"Every computer remembers he died, so he can never be brought back (dead: {ColonyDeaths.IsDead(id)}, kept: {townHall.Keepsakes.Get(id) != null})",
                ColonyDeaths.IsDead(id));
            Check($"The saga tells of his death ({SagaLines(townHall.GetComponent<ColonyBuilding>())})",
                SagaHas(townHall.GetComponent<ColonyBuilding>(), "died", name));

            // His cairn: where he fell, with what he carried, his name on hover, a pin on the map; emptied, it crumbles.
            SettlerCairn cairn = SettlerCairn.All.FirstOrDefault(c => c != null && c.SettlerName == name && Utils.DistanceXZ(c.transform.position, fell) < 3f);
            Dictionary<string, int> held = new Dictionary<string, int>();
            if (cairn != null)
                foreach (ItemDrop.ItemData item in cairn.Container.GetInventory().GetAllItems())
                    if (item.m_dropPrefab != null) held[item.m_dropPrefab.name] = (held.TryGetValue(item.m_dropPrefab.name, out int n) ? n : 0) + item.m_stack;
            int wood = held.TryGetValue("Wood", out int w) ? w : 0, resin = held.TryGetValue("Resin", out int r) ? r : 0;
            string hover = cairn != null ? cairn.Container.GetHoverText().Split('\n')[0] : "";
            Minimap.PinData pin = SettlerCairn.PinNear(fell, name);
            Check($"A settler who dies carrying things leaves a cairn with them, his name on it and a pin on the map (cairn: {cairn != null}, " +
                  $"wood {wood}, resin {resin}, hover '{hover}', pin: {pin != null})", cairn != null && wood == woodBefore + 7 && resin == resinBefore + 3 && hover.Contains(name) && pin != null);
            if (pin != null) Minimap.instance.RemovePin(pin);   // the test's pin; a player's goes when they empty the cairn
            if (cairn != null)
            {
                cairn.Container.SetInUse(true);   // as a player does: open it, take everything, close it
                cairn.Container.GetInventory().RemoveAll();
                yield return new WaitForSeconds(0.5f);
                cairn.Container.SetInUse(false);
                for (float waited = 0f; cairn != null && waited < 5f; waited += 0.5f) yield return new WaitForSeconds(0.5f);
                Check("Emptied, the cairn crumbles away", cairn == null);
            }

            // A settler who carried nothing leaves no cairn.
            Citizen bare = Citizen.All.FirstOrDefault(c => c != null && c.Id != id && c.Record.ColonyId == colonyId && !c.Record.IsEmployed && c.Carry.Total == 0);
            if (bare == null) Check("A second jobless test settler for the no-cairn check", false);
            else
            {
                string bareName = bare.CitizenName;
                Vector3 bareFell = bare.transform.position;
                bare.Body.Damage(KillHit(bare, monster));
                yield return new WaitForSeconds(2f);
                // Where he fell: a cairn of an earlier run's settler of the same name may stand nearby (full self-test run 7).
                SettlerCairn his = SettlerCairn.All.FirstOrDefault(c => c != null && c.SettlerName == bareName && Utils.DistanceXZ(c.transform.position, bareFell) < 5f);
                Check($"A settler who carried nothing leaves no cairn ({bareName} died: {SettlerDeaths.LastReport.Contains(bareName)}, cairn where he fell: " +
                      $"{(his != null ? his.transform.position.ToString() : "none")})", his == null && SettlerDeaths.LastReport.Contains(bareName));
            }
            if (monster != null) Remove(monster.gameObject);
        }

        /// <summary>1.4.2: the console's kill blow (killall, killenemies, debug mode's K) leaves a settler standing.</summary>
        private static void CheckCheatKillSpares(Citizen settler)
        {
            if (settler == null || settler.Body == null)
            {
                Check("The console's kill blow spares a settler (nobody to test with)", false);
                return;
            }
            float before = settler.Body.GetHealth();
            int spared = CitizenPatches.CheatKillsSparedForTest;
            var blow = new HitData();
            blow.m_damage.m_damage = 1E+10f;   // as the game's killall and killenemies hit
            settler.Body.Damage(blow);
            Check($"The console's kill blow (killall, killenemies, debug mode's K) spares a settler ({settler.CitizenName}: health {before:0} -> {settler.Body.GetHealth():0}, " +
                  $"dead {settler.Body.IsDead()}, spared {CitizenPatches.CheatKillsSparedForTest - spared})",
                !settler.Body.IsDead() && settler.Body.GetHealth() >= before && CitizenPatches.CheatKillsSparedForTest > spared);
        }

        private static HitData KillHit(Citizen victim, Character monster)
        {
            var kill = new HitData();
            kill.m_damage.m_slash = 10000f;
            kill.m_hitType = HitData.HitType.EnemyHit;
            kill.m_point = victim.transform.position;
            if (monster != null) kill.SetAttacker(monster);
            return kill;
        }

        private static HitData TreeHit(Citizen victim)
        {
            var hit = new HitData();
            hit.m_damage.m_blunt = 20f;
            hit.m_hitType = HitData.HitType.Tree;
            hit.m_point = victim.transform.position;
            return hit;
        }

        private static void Remove(GameObject go)
        {
            ZNetView nview = go.GetComponent<ZNetView>();
            if (nview != null && nview.IsValid() && !nview.IsOwner()) nview.ClaimOwnership();
            ZNetScene.instance.Destroy(go);
        }

        /// <summary>The builder holds this site and it waits for a level-2 Builder's Hut.</summary>
        private static bool WaitsForLevel2(ConstructionModule site, Citizen builder) =>
            ClaimedBy(site, builder) && site.Order.Needs.Contains("$vc_builder_hut_level 2");

        /// <summary>The door stands open with another settler at it (review of 0.11: not just open at some moment).</summary>
        /// <summary>Self-test: a walk through a door, as <see cref="WalkThroughDoor"/> saw it.</summary>
        private sealed class DoorWalk
        {
            public bool Arrived;
            public bool Through;
            public float Along = float.NaN;   // where on the door's line he crossed it, from its middle

            public override string ToString() =>
                !Arrived ? "didn't get there" : Through ? $"through the doorway ({Along:0.0} m off its middle)"
                : float.IsNaN(Along) ? "never crossed the door's line" : $"crossed the door's line {Along:0.0} m off its middle";
        }

        /// <summary>How far through the doorway a settler may cross the door's line, either way from its middle (a 2 m door).</summary>
        private const float DoorwayHalfWidth = 1.5f;

        /// <summary>
        /// Self-test: sends him to <paramref name="to"/> and watches him every frame: through the doorway when he crosses the
        /// door's line towards <paramref name="to"/> within <see cref="DoorwayHalfWidth"/> of its middle (the door opened by
        /// him or by anyone); round the hut or through a wall or a window, he crosses it further along or not at all.
        /// </summary>
        private static IEnumerator WalkThroughDoor(CitizenAI walker, Door door, Vector3 outward, Vector3 to, DoorWalk result)
        {
            Vector3 middle = door.transform.position;
            outward.y = 0f;
            outward.Normalize();
            Vector3 alongDoor = Vector3.Cross(Vector3.up, outward);
            float towards = Mathf.Sign(Vector3.Dot(to - middle, outward));
            float Across(Vector3 at) => Vector3.Dot(at - middle, outward);
            float before = Across(walker.transform.position);
            walker.TestWalk(to, 65f);
            for (float waited = 0f; !walker.TestWalkDone && waited < 60f; waited += Time.deltaTime)
            {
                yield return null;
                if (walker == null) yield break;
                Vector3 at = walker.transform.position;
                float across = Across(at);
                if (before * towards < 0f && across * towards >= 0f)   // over the line, the way he is going
                {
                    float along = Vector3.Dot(at - middle, alongDoor);
                    result.Along = along;
                    result.Through |= Mathf.Abs(along) <= DoorwayHalfWidth;
                }
                before = across;
            }
            result.Arrived = walker != null && walker.TestWalkDone;
            result.Through &= result.Arrived;
        }

        private static readonly HarmonyLib.AccessTools.FieldRef<Player, HashSet<string>> s_shownTutorials =
            HarmonyLib.AccessTools.FieldRefAccess<Player, HashSet<string>>("m_shownTutorials");

        /// <summary>The character hasn't seen this tip (the test shows it, then gives the player their first sight of it back).</summary>
        private static void ForgetTipForTest(Player player, string tip)
        {
            try { s_shownTutorials(player)?.Remove(tip); }
            catch (System.Exception) { }   // the game renamed it: the tip stays seen
        }

        private static bool ClaimedBy(ConstructionModule site, Citizen builder) =>
            site != null && site.IsValid && builder != null && site.Order is BuildOrderRecord order
            && order.Builder == builder.Id && order.HasLiveClaim(ConstructionModule.Now());

        /// <summary>Pieces the builder put up for this hut that aren't part of its current design.</summary>
        private static int OldPiecesLeft(ConstructionModule site)
        {
            var pieces = new List<Piece>();
            Piece.GetAllPiecesInRadius(site.transform.position, 30f, pieces);
            return pieces.Count(p => p != null && PiecePlacer.SiteOf(p.gameObject) == site.Building.Id && !site.MatchesPlan(p));
        }

        private static bool HutChestFloats()
        {
            GameObject prefab = ZNetScene.instance.GetPrefab(BuildingTypes.LumberjackHut.PrefabName);
            WearNTear wear = prefab != null ? prefab.GetComponent<WearNTear>() : null;
            return wear != null && !wear.m_noSupportWear;
        }

        /// <summary>
        /// How long a walk check waits for the game's walking map to take in new floors (15 s until 1.4.3's compatibility run
        /// of 2026-10-22: with 15 other mods the game was slower, and a level-2 Longhouse's bed was still "1,6 m short").
        /// </summary>
        private const float WalkingMapSeconds = 30f;

        /// <summary>Every bed's sleeping spot can be walked to from inside the Longhouse's door. Seen: at level 2 none could.</summary>
        private static void CheckBedsReachable(ResidenceModule residence)
        {
            if (residence == null) return;
            List<HomeBed> beds = residence.Beds();
            List<string> unreachable = UnreachableBeds(residence);
            Check($"Every bed in the level-{residence.Building.Level} Longhouse can be walked to from its door ({beds.Count} beds" +
                  $"{(unreachable.Count > 0 ? "; not: " + string.Join(", ", unreachable) : "")})", beds.Count > 0 && unreachable.Count == 0);
        }

        /// <summary>The Longhouse's beds whose sleeping spot can't be walked to from inside its door, with how far short the way ends.</summary>
        private static List<string> UnreachableBeds(ResidenceModule residence)
        {
            CitizenAI walker = Citizen.All.Where(c => c != null).Select(c => c.GetComponent<CitizenAI>()).FirstOrDefault(a => a != null);
            Vector3 inside = residence.InsideDoor();
            var unreachable = new List<string>();
            foreach (HomeBed bed in residence.Beds())
            {
                Vector3? spot = residence.SleepSpot(bed.Id, walker);
                float gap = spot.HasValue && walker != null ? walker.WayEndGap(inside, spot.Value) : float.MaxValue;
                if (gap >= 0.6f) unreachable.Add($"{bed.Id.Substring(bed.Id.IndexOf('#'))} ({(gap > 1000f ? "no way" : gap.ToString("0.0") + " m short")})");
            }
            return unreachable;
        }

        /// <summary>
        /// Nothing (a bush, a log, a stone, furniture) stands in any of the hut's doorways. The player's idea: settlers stuck at
        /// doors only at some spots could be things in the way. The ground's step at the door is logged alongside.
        /// </summary>
        private static void CheckDoorways(string what, ConstructionModule site)
        {
            if (site == null) return;
            List<Door> doors = DoorClearance.DoorsOf(site);
            if (doors.Count == 0) Log.Event($"vc_selftest: the {what} has no door to look at");
            foreach (Door door in doors)
            {
                List<string> inTheWay = DoorClearance.Obstacles(door, site.Building);
                string steps = DoorClearance.Steps(door, out float highest);
                Vector3 at = door.transform.position;
                Check($"Nothing stands in the {what}'s doorway at ({at.x:0}, {at.z:0}) (in the way: {(inTheWay.Count == 0 ? "nothing" : string.Join(", ", inTheWay))}; " +
                      $"ground 1 m out on each side against the door's foot: {steps} m)", inTheWay.Count == 0);
            }
        }

        /// <summary>The colony's saga (on its Town Hall) has a line of this kind with all these words.</summary>
        private static bool SagaHas(ColonyBuilding hall, string kind, params string[] words)
        {
            TownHallModule module = hall != null ? hall.GetComponent<TownHallModule>() : null;
            return module != null && module.Saga.Page(0, SagaLog.MaxEntries).Any(e => e.Kind == kind && words.All(w => e.Args.Contains(w)));
        }

        /// <summary>The saga's newest lines as the window shows them, for a check's message.</summary>
        private static string SagaLines(ColonyBuilding hall)
        {
            TownHallModule module = hall != null ? hall.GetComponent<TownHallModule>() : null;
            return module == null ? "no Town Hall" : string.Join(" / ", module.Saga.Page(0, 3).Select(SagaText.Line));
        }

        private static void Check(string step, bool ok)
        {
            if (ok) s_pass++;
            else s_fail++;
            if (ok) Log.Event($"vc_selftest: PASS {step}");
            else Log.Warn($"vc_selftest: FAIL {step}");
        }
    }
}
