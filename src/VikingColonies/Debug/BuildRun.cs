using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jotunn.Entities;
using UnityEngine;
using VikingColonies.Buildings;
using VikingColonies.Citizens;
using VikingColonies.Config;
using VikingColonies.Construction;
using VikingColonies.Core;

namespace VikingColonies.DevTools
{
    /// <summary>
    /// vc_buildrun (huts round 2, 2026-10-18; the player: "I want to test how the builder actually builds the upgrades,
    /// not just the instant tests"): a colony in front of you whose real builders put up each hut, and the Town Hall's
    /// camp, level 1 to 5 from the materials put in their chests, the next level ordered as soon as one stands. Run it at a
    /// raised speed (vc_speed 5). It logs each level's time, what the order said it waited for, and the pieces that don't
    /// stand at the end of each level, then a table.
    /// </summary>
    internal sealed class BuildRunCommand : ConsoleCommand
    {
        public override string Name => "vc_buildrun";
        public override string Help => "VikingColonies (test worlds): 'vc_buildrun [lumberjack forager farm longhouse watchtower builder camp]' puts a colony " +
                                       "in front of you whose builders build each hut (default: all, and the Town Hall's camp) level 1 to 5 from materials " +
                                       "put in its chest; 'vc_buildrun stop' ends it";
        public override bool IsCheat => true;

        public override void Run(string[] args)
        {
            Player p = Player.m_localPlayer;
            if (p == null || ZNet.instance == null || !TestWorlds.IsTestWorld(ZNet.instance.GetWorldName()))
            {
                BuildCommand.Print("vc_buildrun: test worlds only");
                return;
            }
            if (args != null && args.Length > 0 && args[0] == "clear")
            {
                BuildCommand.Print("vc_buildrun: " + BuildRun.Clear());
                return;
            }
            if (args != null && args.Length > 0 && args[0] == "stop")
            {
                BuildRun.Stop = true;
                BuildCommand.Print("vc_buildrun: stopping");
                return;
            }
            if (BuildRun.Running)
            {
                BuildCommand.Print("vc_buildrun: already running ('vc_buildrun stop')");
                return;
            }
            List<string> subjects = args != null && args.Length > 0 ? args.ToList() : BuildRun.All.ToList();
            Plugin.Instance.StartCoroutine(BuildRun.Run(p, subjects));
        }
    }

    internal static class BuildRun
    {
        public static readonly string[] All = { "lumberjack", "forager", "farm", "longhouse", "watchtower", "builder", "camp" };
        /// <summary>Game seconds one level may take before the run gives up on it (40 game minutes).</summary>
        private const double LevelTimeout = 2400.0;
        private const float Spacing = 26f;
        /// <summary>How far in front of the camp the layout's middle lies (between the camp and the row of huts).</summary>
        private const float LayoutMiddle = 24f;
        /// <summary>
        /// The ground the run needs to itself round the layout's middle: the layout reaches 90 m, and a hut placed within a
        /// colony's area joins it, even one whose Town Hall is gone (1.4.3's second build-through: the first try's leftover hut
        /// took in the new Watchtower and Builder's Hut, and no builder came to them).
        /// </summary>
        private const float OwnGround = 150f;

        public static bool Running, Stop;
        /// <summary>The middle of the last run's colony (null when none started: a refused run clears nothing).</summary>
        private static Vector3? s_lastMiddle;

        /// <summary>
        /// vc_buildrun clear: the last run's colony goes (everything of a colony within 120 m of its middle; the run started
        /// only with no other colony within 150 m). Its review: a radius wipe in the scenario also took what a refused run
        /// had found in its way.
        /// </summary>
        public static string Clear()
        {
            if (Running) return "still running ('vc_buildrun stop' first)";
            if (!s_lastMiddle.HasValue) return "no run's colony to clear";
            Vector3 at = s_lastMiddle.Value;
            s_lastMiddle = null;
            return $"cleared the run's colony at ({at.x:0}, {at.z:0}): {DevHelpers.WipeColonies(at, ClearRadius)}";
        }

        private const float ClearRadius = 120f;

        private sealed class Subject
        {
            public string Name;
            public ColonyBuilding Building;
            public ConstructionModule Site;
            public int Level;                 // the level being built, or built
            public double StartedGame, LastAsk;
            public float StartedReal;
            public bool Supplied, Done, Failed;
            public string LastNeeds = "", LastBlocked = "", LastState = "";
            public readonly List<string> Rows = new List<string>();
        }

        private static double GameNow => ZNet.instance != null ? ZNet.instance.GetTimeSeconds() : Time.time;

        public static IEnumerator Run(Player player, List<string> names)
        {
            Running = true;
            Stop = false;
            Log.Event($"vc_buildrun: starting ({string.Join(", ", names)})");
            if (!player.InGodMode()) player.SetGodMode(true);
            Vector3 forward = player.transform.forward, right = player.transform.right;
            forward.y = right.y = 0f;
            forward.Normalize();
            right.Normalize();
            // The whole layout inside the land the game keeps running round the player: they stand in the middle of their zone,
            // the layout's middle on them (1.4.3's build-through: from their zone's edge, the row's end hut lay 12 m inside that
            // land's edge; its three builders stepped past it and stood there frozen, as the game's own creatures do, and every
            // hut's level 2 waited 40 game minutes for them).
            Vector3 middle = ZoneSystem.GetZonePos(ZoneSystem.GetZone(player.transform.position));
            middle.y = ZoneSystem.instance.GetGroundHeight(middle) + 0.5f;   // a teleport with no floor under it goes back (its review)
            for (int tries = 0; !player.TeleportTo(middle, player.transform.rotation, distantTeleport: false) && tries < 20; tries++)
                yield return new WaitForSecondsRealtime(0.5f);
            for (float waited = 0f; player.IsTeleporting() && waited < 30f; waited += 0.5f) yield return new WaitForSecondsRealtime(0.5f);
            yield return new WaitForSecondsRealtime(1f);
            if (Utils.DistanceXZ(player.transform.position, middle) > 2f)
            {
                Log.Event($"vc_buildrun: FAIL the player didn't get to the middle of their zone at ({middle.x:0}, {middle.z:0}) " +
                          $"(standing at ({player.transform.position.x:0}, {player.transform.position.z:0}))");
                Log.Event("vc_buildrun: 0 of 0 reached level 5, 0 gave up");
                Running = false;
                yield break;
            }
            Vector3 origin = middle - forward * LayoutMiddle;
            origin.y = player.transform.position.y;
            Log.Event($"vc_buildrun: the player stands in the middle of their zone at ({middle.x:0}, {middle.z:0}); the camp goes up {LayoutMiddle:0} m behind them");
            string others = OthersNear(middle, OwnGround);
            if (others.Length > 0)
            {
                Log.Event($"vc_buildrun: FAIL the ground isn't the run's own: {others} within {OwnGround:0} m of ({middle.x:0}, {middle.z:0}); " +
                          "clear them (vc_world wipe) or start somewhere else");
                Log.Event("vc_buildrun: 0 of 0 reached level 5, 0 gave up");
                Running = false;
                yield break;
            }
            s_lastMiddle = middle;
            Quaternion facing = Quaternion.LookRotation(forward);
            long me = player.GetPlayerID();
            int startingWas = ColonyConfig.StartingCitizens.Value;
            DevHelpers.SetUnsaved(ColonyConfig.StartingCitizens, 10);   // for this run only: never written to the config file (A8)
            var subjects = new List<Subject>();
            try
            {
                ColonyBuilding hall = DevHelpers.Place(BuildingTypes.TownHall.PrefabName, origin + forward * 4f, facing, me);
                TownHallModule townHall = hall != null ? hall.GetComponent<TownHallModule>() : null;
                for (float t = 0f; townHall != null && !townHall.Founded && t < 30f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                if (hall == null || townHall == null || !townHall.Founded)
                {
                    Log.Event("vc_buildrun: FAIL no Town Hall founded");
                    yield break;
                }
                hall.SetLevel(TownHallLevels.MaxLevel);   // as if every trophy were shown: huts may go to level 5
                Inventory hallChest = hall.GetComponent<Container>().GetInventory();
                foreach (KeyValuePair<string, int> tool in new Dictionary<string, int> { { "Hoe", 1 }, { "PickaxeIron", 1 }, { "AxeIron", 1 }, { "Stone", 200 } })
                    Give(hallChest, tool.Key, tool.Value);

                // Two Builder's Huts behind the camp, built and raised to level 5 at once: their builders do the work.
                for (int i = 0; i < 2; i++)
                {
                    ColonyBuilding support = DevHelpers.Place(BuildingTypes.BuilderHut.PrefabName, origin + forward * 4f + right * (i == 0 ? -26f : 26f), facing, me);
                    yield return AtOnceToLevel(support, HutLevels.MaxLevel);
                }

                // The subjects in a row in front of the camp (each hut's level-5 lot is under 22 m across).
                int n = names.Count(x => x != "camp");
                int k = 0;
                foreach (string name in names)
                {
                    if (name == "camp")
                    {
                        subjects.Add(new Subject { Name = "camp", Building = hall, Site = hall.GetComponent<ConstructionModule>() });
                        continue;
                    }
                    BuildingType type = DevHelpers.TypeByWord(name);
                    if (type == null || type == BuildingTypes.TownHall)
                    {
                        Log.Event($"vc_buildrun: no such hut '{name}'");
                        continue;
                    }
                    Vector3 at = origin + forward * 44f + right * ((k - (n - 1) / 2f) * Spacing);
                    k++;
                    ColonyBuilding hut = DevHelpers.Place(type.PrefabName, at, facing, me);
                    if (hut == null) continue;
                    if (type.Id != BuildingTypes.BuilderHutId) hut.GetComponent<WorkerModule>()?.RequestSetAutoHire(false);   // settlers for builders
                    subjects.Add(new Subject { Name = name, Building = hut, Site = hut.GetComponent<ConstructionModule>() });
                }
                yield return new WaitForSeconds(3f);
                foreach (Subject s in subjects) Begin(s, 1);
                // The camp: ordered by the Town Hall when there is a design for it (vc_build upgrade's shortcut starts it here).
                Subject camp = subjects.FirstOrDefault(s => s.Name == "camp");
                if (camp != null && camp.Site != null && camp.Site.Order == null) camp.Site.RequestStartCamp();

                while (!Stop && subjects.Any(s => !s.Done && !s.Failed))
                {
                    yield return new WaitForSeconds(2f);
                    foreach (Subject s in subjects.Where(x => !x.Done && !x.Failed)) Tick(s, hallChest);
                }
            }
            finally
            {
                DevHelpers.SetUnsaved(ColonyConfig.StartingCitizens, startingWas);
                Running = false;
            }
            Log.Event("vc_buildrun: done. Level times (game minutes, real seconds at this speed):");
            foreach (Subject s in subjects)
                foreach (string row in s.Rows) Log.Event("vc_buildrun: " + row);
            Log.Event($"vc_buildrun: {subjects.Count(s => s.Done)} of {subjects.Count} reached level 5, {subjects.Count(s => s.Failed)} gave up");
        }

        /// <summary>Starts timing a level: its materials go into the hut's chest (what doesn't fit, into the Town Hall's).</summary>
        private static void Begin(Subject s, int level)
        {
            s.Level = level;
            s.StartedGame = GameNow;
            s.StartedReal = Time.realtimeSinceStartup;
            s.Supplied = false;
            s.LastNeeds = s.LastBlocked = s.LastState = "";
            Log.Event($"vc_buildrun: {s.Name} level {level} ordered");
        }

        private static void Tick(Subject s, Inventory hallChest)
        {
            ConstructionModule site = s.Site;
            if (site == null || !site.IsValid)
            {
                s.Failed = true;
                s.Rows.Add($"{s.Name}: gone");
                return;
            }
            BuildOrderRecord order = site.Order;
            int built = s.Name == "camp" ? site.CampLevel : s.Building.Level;
            bool open = order != null && order.State != BuildState.Done && order.TargetLevel == s.Level;
            if (open && !s.Supplied)
            {
                Dictionary<string, int> needs = site.RemainingNeeds();
                Inventory own = s.Name == "camp" ? hallChest : s.Building.GetComponent<Container>()?.GetInventory();
                foreach (KeyValuePair<string, int> need in needs)
                {
                    int want = need.Value + Mathf.CeilToInt(need.Value * 0.1f);   // a little over, for a piece that falls and is put back
                    int left = Give(own, need.Key, want);
                    if (left > 0) left = Give(hallChest, need.Key, left);
                    if (left > 0) Log.Event($"vc_buildrun: {s.Name} level {s.Level}: no room for {left} {need.Key}");
                }
                s.Supplied = true;
                Log.Event($"vc_buildrun: {s.Name} level {s.Level}: {order.Total} pieces, needs {string.Join(", ", needs.Select(x => x.Value + " " + x.Key))}");
            }
            if (open)
            {
                // The camp's order waits for every hut's on purpose (OrderChoice: Later), and here the huts order a new
                // level every few minutes: its clock starts when a builder takes it (2026-10-19: level 2 timed out waiting).
                if (s.Name == "camp" && order.State == BuildState.Waiting && !order.HasLiveClaim(ConstructionModule.Now()))
                {
                    s.StartedGame = GameNow;
                    s.StartedReal = Time.realtimeSinceStartup;
                }
                string state = order.State.ToString();
                if (state != s.LastState) Log.Event($"vc_buildrun: {s.Name} level {s.Level}: {state} ({order.DoneCount} of {order.Total} up)");
                if (order.Needs != s.LastNeeds && order.Needs.Length > 0)
                    Log.Event($"vc_buildrun: {s.Name} level {s.Level}: waits for {Localization.instance.Localize(order.Needs)} " +
                              $"({WhereIs(order.Needs, s.Building.ColonyId)})");
                if (order.Blocked != s.LastBlocked && order.Blocked.Length > 0)
                    Log.Event($"vc_buildrun: {s.Name} level {s.Level}: blocked: {Localization.instance.Localize(order.Blocked)}");
                s.LastState = state;
                s.LastNeeds = order.Needs;
                s.LastBlocked = order.Blocked;
                if (GameNow - s.StartedGame > LevelTimeout)
                {
                    s.Failed = true;
                    int frozen = Citizen.All.Count(c => c != null && c.ColonyId == s.Building.ColonyId && c.Record.Job == Jobs.JobDefinitions.BuilderId &&
                                                        ZNetScene.instance.OutsideActiveArea(c.transform.position));
                    string row = $"{s.Name} level {s.Level}: GAVE UP after {LevelTimeout / 60:0} game minutes: {order.State}, {order.DoneCount} of {order.Total} up, " +
                                 $"{order.SkippedCount} skipped, needs '{order.Needs}', blocked '{order.Blocked}', {frozen} builders outside the land the game runs";
                    s.Rows.Add(row);
                    Log.Event("vc_buildrun: " + row);
                }
                return;
            }
            // The camp grows with the hall on its own: its next level may be ordered the moment this one is done, before a
            // tick sees it done (1.4.3's build-through: camp level 3 was ordered at once, the run never counted level 2,
            // never put out level 3's materials, and the builders sat idle).
            bool campMovedOn = s.Name == "camp" && order != null && order.State != BuildState.Done && order.TargetLevel > s.Level;
            if (built >= s.Level && (order == null || order.State == BuildState.Done || campMovedOn))
            {
                int total = 0, standing = 0;
                List<string> weak = campMovedOn ? new List<string>() : site.StandReport(out total, out standing);
                string row = $"{s.Name} level {s.Level}: {(GameNow - s.StartedGame) / 60.0:0.0} game min, {Time.realtimeSinceStartup - s.StartedReal:0} s; " +
                             (campMovedOn ? "its next level began at once (its pieces are counted with that one)"
                                          : $"{standing} of {total} pieces stand, {order?.SkippedCount ?? 0} skipped");
                s.Rows.Add(row);
                Log.Event("vc_buildrun: " + row);
                foreach (string line in weak.Where(l => l.StartsWith("missing")).Take(6)) Log.Event($"vc_buildrun: {s.Name} level {s.Level}: {line}");
                if (s.Level >= HutLevels.MaxLevel)
                {
                    s.Done = true;
                    return;
                }
                Begin(s, s.Level + 1);
                s.LastAsk = GameNow;
                if (s.Name != "camp") site.RequestUpgrade();   // the camp grows with the hall (level 5 here) on its own
                return;
            }
            // Ordered but not yet open: an upgrade refused (the hut logs why) is asked for again each game minute.
            if (s.Name != "camp" && order != null && order.State == BuildState.Done && built < s.Level && GameNow - s.LastAsk > 60.0)
            {
                s.LastAsk = GameNow;
                site.RequestUpgrade();
            }
        }

        /// <summary>
        /// Where the items an order waits for are (2026-10-19: a camp level waited for "1 Wood" though more had been put in):
        /// in each colony chest (spare for a builder of another building) and in each settler's pockets.
        /// </summary>
        private static string WhereIs(string needs, string colonyId)
        {
            var parts = new List<string>();
            foreach (string need in needs.Split(','))
            {
                string[] words = need.Trim().Split(' ');
                if (words.Length < 2) continue;
                string token = words[words.Length - 1];
                GameObject prefab = ObjectDB.instance != null ? ObjectDB.instance.m_items.FirstOrDefault(i =>
                    i != null && i.GetComponent<ItemDrop>() != null && i.GetComponent<ItemDrop>().m_itemData.m_shared.m_name == token) : null;
                if (prefab == null) continue;
                string item = prefab.name;
                var at = new List<string>();
                foreach (StorageModule store in BuilderTools.ColonyStores(colonyId, null, Vector3.zero))
                {
                    int held = store.Contents().TryGetValue(item, out int n) ? n : 0;
                    if (held > 0) at.Add($"{store.GetComponent<ColonyBuilding>()?.TypeId ?? "chest"} {held} ({store.Spare(item)} spare)");
                }
                foreach (Citizen c in Citizen.All.Where(c => c != null && c.ColonyId == colonyId && c.Carry.Count(item) > 0))
                    at.Add($"{c.CitizenName} carries {c.Carry.Count(item)}");
                parts.Add($"{item}: {(at.Count > 0 ? string.Join(", ", at) : "none anywhere")}");
            }
            return string.Join("; ", parts);
        }

        /// <summary>Other colonies' buildings, settlers and builder's pieces (a gallery's too) near a spot; "" when none.</summary>
        private static string OthersNear(Vector3 centre, float radius)
        {
            // Buildings and settlers from the world's saved objects, loaded or not (its review: settlers load only within the
            // zones round the player, about 96 m, so one 100-150 m off went unseen); pieces as loaded (a hut stands by them).
            List<Vector3> colonyThings = FindFlatCommand.ColonySpotsInWorld().Where(s => Utils.DistanceXZ(s, centre) <= radius).ToList();
            var near = new List<Piece>();
            Piece.GetAllPiecesInRadius(centre, radius, near);
            int pieces = near.Count(p => p != null && (PiecePlacer.SiteOf(p.gameObject).Length > 0 || BuildCommand.IsCostChestPiece(p)));
            if (colonyThings.Count + pieces == 0) return "";
            string nearestNote = colonyThings.Count > 0 ? $" (the nearest {colonyThings.Min(s => Utils.DistanceXZ(s, centre)):0} m off)" : "";
            return $"{colonyThings.Count} colony buildings and settlers, {pieces} builder's pieces{nearestNote}";
        }

        /// <summary>Puts up to <paramref name="amount"/> of an item into the inventory; returns how many didn't fit.</summary>
        private static int Give(Inventory inventory, string item, int amount)
        {
            GameObject prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(item) : null;
            if (inventory == null || prefab == null) return amount;
            int put = 0;
            while (put < amount && inventory.AddItem(prefab, 1)) put++;
            return amount - put;
        }

        /// <summary>A hut put up and raised to the level at once (the test shortcut), waiting for each step.</summary>
        private static IEnumerator AtOnceToLevel(ColonyBuilding hut, int level)
        {
            ConstructionModule site = hut != null ? hut.GetComponent<ConstructionModule>() : null;
            if (site == null) yield break;
            for (float t = 0f; site.Order == null && t < 15f; t += 0.5f) yield return new WaitForSeconds(0.5f);
            site.FinishNow();
            for (float t = 0f; !site.IsBuilt && t < 60f; t += 0.5f) yield return new WaitForSeconds(0.5f);
            while (hut.Level < level)
            {
                int was = hut.Level;
                site.RequestUpgradeNow();
                for (float t = 0f; hut.Level == was && t < 60f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                if (hut.Level == was) break;
            }
            // Raised at once, the old pieces all go before the new ones go up, and a kept piece against a wall that is
            // built again falls (the adze, 2026-10-19): put back now, or its repair draws the builders off the run.
            yield return new WaitForSeconds(2f);
            int mended = site.MendNow();
            Log.Event($"vc_buildrun: a Builder's Hut at level {hut.Level} to work from ({mended} pieces put back)");
        }
    }
}
