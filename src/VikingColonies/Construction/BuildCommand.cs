using System.Collections.Generic;
using System.Linq;
using Jotunn.Entities;
using UnityEngine;
using VikingColonies.Buildings;
using VikingColonies.Core;

namespace VikingColonies.Construction
{
    /// <summary>vc_build: test commands for build orders (needs devcommands).</summary>
    internal sealed class BuildCommand : ConsoleCommand
    {
        public override string Name => "vc_build";
        public override string Help => "VikingColonies: 'vc_build done' counts the hut you look at as built, 'vc_build finish' puts up the rest of its building at once, 'vc_build ready' skips clearing and flattening its site, 'vc_build clear' removes the pieces the builder put up for it (and counts it as built), 'vc_build leftovers' removes the pieces the builder put up for it that its design no longer has, 'vc_build upgrade' upgrades it to its next level at once, 'vc_build order' and 'vc_build cancel' do what the window's Upgrade and Cancel buttons do, 'vc_build window' opens its window, 'vc_build stock' puts what its next level needs in its chest, 'vc_build replant [kind on|off]' shows or changes a Lumberjack's Hut's replanting list, 'vc_build pick [kind on|off]' a Forager's Hut's \"What to pick\" list, 'vc_build give <item> [amount]' puts an item in it, 'vc_build status' shows its build order, 'vc_build costchest' sets a chest beside it holding what its level cost the builder, with a sign naming it (the hut gallery), 'vc_build mend' puts its design's missing pieces back at once (as the builder's repairs do in time), 'vc_build stands' lists its design's pieces that don't stand (fell, or never went up) and those nearest to falling, 'vc_build support <x> <y> <z>' tells how the game holds up the piece standing nearest that spot of the design (its support boxes, what they find, its support); '@<hut>' anywhere names the hut (the nearest of that kind within 40 m) instead of the one you look at";
        public override bool IsCheat => true;

        public override void Run(string[] args)
        {
            if (DevTools.DevHelpers.RefuseOutsideTestWorld(Name)) return;   // it changes the world (the whole-mod review of 1.4.1)
            if (args != null && args.Length > 1 && args[0].ToLowerInvariant() == "costchest" && args[1].ToLowerInvariant() == "clear")
            {
                float radius = args.Length > 2 && float.TryParse(args[2], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float r) ? r : 150f;
                Print(ClearCostChests(Player.m_localPlayer, radius));
                return;
            }
            // '@longhouse' (anywhere in the line) names the hut: the nearest of that kind within 40 m, whatever you look at
            // (the old huts' upgrade test: standing at the Longhouse's door, the Farm's chest was the one looked at).
            string named = args?.FirstOrDefault(a => a.StartsWith("@"));
            if (named != null) args = args.Where(a => a != named).ToArray();
            ConstructionModule site = named != null ? Named(Player.m_localPlayer, named.Substring(1), 40f) : Target(Player.m_localPlayer, 10f);
            if (site == null)
            {
                Print(named != null ? $"vc_build: no {named.Substring(1)} within 40 m." : "vc_build: look at a hut's chest, or stand within 10 m of one.");
                return;
            }
            string what = args != null && args.Length > 0 ? args[0].ToLowerInvariant() : "status";
            switch (what)
            {
                case "done":
                    site.RequestMarkBuilt(false);
                    Print($"vc_build: {site.Building.TypeId} counts as built.");
                    break;
                case "clear":
                    int removed = site.RemoveBuilderPieces();
                    site.RequestMarkBuilt(false);
                    Print($"vc_build: removed {removed} pieces the builder put up for this {site.Building.TypeId}; it counts as built.");
                    break;
                case "leftovers" when args.Length > 1 && args[1].ToLowerInvariant() == "count":
                    BuildOrderRecord done = site.Order;
                    if (done == null || done.State != BuildState.Done) Print($"vc_build leftovers: {site.Building.TypeId}'s order is still under way (its old pieces aren't leftovers yet).");
                    else Print($"vc_build leftovers: {site.RemoveLeftovers(remove: false)} pieces of {site.Building.TypeId}'s that its design no longer has stand.");
                    break;
                case "leftovers":
                    int gone = site.RemoveLeftovers();
                    Print($"vc_build: removed {gone} pieces of {site.Building.TypeId}'s that its design no longer has.");
                    break;
                case "ready":
                    site.RequestAdvance(BuildState.Building);
                    Print($"vc_build: {site.Building.TypeId}'s site counts as prepared; the builder starts building.");
                    break;
                case "finish":
                    if (site.IsUpgrading)
                    {
                        // Seen: "finish" on an upgrade put the new pieces up and counted the old ones as taken away while
                        // they still stood: the level-1 door left in the middle of the level-2 hut trapped its builder.
                        site.RequestUpgradeNow();
                        Print($"vc_build: finishing the upgrade of {site.Building.TypeId} at once: the old pieces go, then the new ones go up.");
                        break;
                    }
                    int placed = site.FinishNow();
                    Print($"vc_build: putting up {placed} pieces (a site not yet flattened is flattened first, which takes a few seconds); then {site.Building.TypeId} counts as built.");
                    break;
                case "upgrade":
                    site.RequestUpgradeNow();
                    Print($"vc_build: upgrading {site.Building.TypeId} from level {site.Building.Level} at once (a bigger site is flattened first, which takes a few seconds).");
                    break;
                case "order":
                    Print(Order(site));
                    break;
                case "support":
                    if (args.Length < 4 || !float.TryParse(args[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float sx)
                        || !float.TryParse(args[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float sy)
                        || !float.TryParse(args[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float sz))
                    {
                        Print("vc_build support <x> <y> <z>: a spot in the hut's design (from its chest, as 'vc_build stands' gives them)");
                        break;
                    }
                    foreach (string line in SupportInfo.Describe(site.transform.TransformPoint(new Vector3(sx, sy, sz)), site.transform))
                        Print("vc_build support: " + line);
                    break;
                case "costchest":   // the gallery's tools put chests, signs and pieces up: test worlds only (1.4's review)
                    if (DevTools.DevHelpers.RefuseOutsideTestWorld("vc_build costchest")) break;
                    Print(CostChest(site));
                    break;
                case "mend":
                    if (DevTools.DevHelpers.RefuseOutsideTestWorld("vc_build mend")) break;
                    Print($"vc_build: put {site.MendNow()} missing pieces of {site.Building.TypeId} back up.");
                    break;
                case "stands":
                    List<string> lines = site.StandReport(out int total, out int standing);
                    Print($"vc_build stands: {site.Building.TypeId} level {site.Building.Level}: {standing} of {total} pieces stand");
                    foreach (string line in lines) Print("vc_build stands: " + line);
                    break;
                case "cancel":
                    if (!site.CanCancelUpgrade) Print($"vc_build: nothing to cancel ({(site.IsUpgrading ? "the builder has changed a piece already" : "no upgrade under way")}).");
                    else
                    {
                        site.RequestCancelUpgrade();
                        Print($"vc_build: the upgrade of {site.Building.TypeId} is cancelled.");
                    }
                    break;
                case "window":
                    Gui.Windows.Hut.Open(site.Building);   // as Shift+E on its chest does
                    break;
                case "stock":
                    Print(Stock(site));
                    break;
                case "replant":
                    Print(Replant(site, args));
                    break;
                case "pick":
                    Print(Pick(site, args));
                    break;
                case "grow":
                    Print(Grow(site, args));
                    break;
                case "give":
                    Print(Give(site, args.Length > 1 ? args[1] : "", args.Length > 2 && int.TryParse(args[2], out int amount) ? amount : 1));
                    break;
                default:
                    Print("vc_build: " + Status(site));
                    break;
            }
        }

        /// <summary>The hut the player looks at, else the nearest within range (of its chest or its door: a longhouse's
        /// chest stands against its back wall, 11 m from where vc_goto leaves you outside its door).</summary>
        public static ConstructionModule Target(Player player, float range)
        {
            if (player == null) return null;
            GameObject hover = player.GetHoverObject();
            ConstructionModule looked = hover != null ? hover.GetComponentInParent<ConstructionModule>() : null;
            if (looked != null && looked.IsValid) return looked;
            Vector3 me = player.transform.position;
            return ConstructionModule.All
                .Where(s => s != null && s.IsValid)
                .Select(s => new { Site = s, Away = Away(s, me) })
                .Where(x => x.Away <= range)
                .OrderBy(x => x.Away)
                .Select(x => x.Site)
                .FirstOrDefault();
        }

        private static ConstructionModule Named(Player player, string word, float range)
        {
            BuildingType type = DevTools.DevHelpers.TypeByWord(word);
            if (player == null || type == null) return null;
            Vector3 me = player.transform.position;
            return ConstructionModule.All
                .Where(s => s != null && s.IsValid && s.Building != null && s.Building.TypeId == type.Id)
                .Select(s => new { Site = s, Away = Away(s, me) })
                .Where(x => x.Away <= range)
                .OrderBy(x => x.Away)
                .Select(x => x.Site)
                .FirstOrDefault();
        }

        private static float Away(ConstructionModule site, Vector3 me)
        {
            float away = Vector3.Distance(site.transform.position, me);
            Door door = HutDoors.DoorOf(site);
            return door != null ? Mathf.Min(away, Vector3.Distance(door.transform.position, me)) : away;
        }

        /// <summary>vc_build replant [kind on|off]: the hut's replanting list, or a kind ticked / unticked (as the window does).</summary>
        private static string Replant(ConstructionModule site, string[] args)
        {
            WorkerModule worker = site.GetComponent<WorkerModule>();
            if (worker == null || site.Building.TypeId != BuildingTypes.LumberjackHutId) return "vc_build: only a Lumberjack's Hut has a replanting list.";
            List<ReplantLine> lines = Jobs.ReplantList.For(site.Building);
            if (args.Length >= 3)
            {
                string word = args[1].ToLowerInvariant();
                ReplantLine line = lines.FirstOrDefault(l => l.Sapling.Sapling.ToLowerInvariant() == word)
                                   ?? lines.FirstOrDefault(l => l.Sapling.Sapling.ToLowerInvariant().StartsWith(word));
                if (line == null) return $"vc_build: no sapling called '{args[1]}'";
                bool on = args[2].ToLowerInvariant() == "on";
                worker.RequestSetTicked(line.Sapling.Sapling, on);
                return $"vc_build: {line.Sapling.Sapling} {(on ? "ticked" : "unticked")}";
            }
            return "vc_build: replanting list: " + string.Join("; ", lines.Select(l => $"{l.Sapling.Sapling} " +
                (l.Ticked ? "ticked" : l.Offered ? "unticked" : l.NeedsLevel > 0 ? $"greyed (needs a level {l.NeedsLevel} hut)" : "greyed (doesn't grow here)")));
        }

        /// <summary>vc_build pick [kind on|off]: the Forager's Hut's "What to pick" list, or a kind ticked / unticked (as the window does).</summary>
        private static string Pick(ConstructionModule site, string[] args)
        {
            WorkerModule worker = site.GetComponent<WorkerModule>();
            if (worker == null || site.Building.TypeId != BuildingTypes.ForagerHutId) return "vc_build: only a Forager's Hut has a \"What to pick\" list.";
            List<ForageLine> lines = Jobs.ForageList.For(site.Building);
            if (args.Length >= 3)
            {
                string word = args[1].ToLowerInvariant();
                ForageLine line = lines.FirstOrDefault(l => l.Kind.Item.ToLowerInvariant() == word)
                                  ?? lines.FirstOrDefault(l => l.Kind.Item.ToLowerInvariant().StartsWith(word));
                if (line == null) return $"vc_build: nothing called '{args[1]}' on the list";
                bool on = args[2].ToLowerInvariant() == "on";
                worker.RequestSetPickTicked(line.Kind.Item, on);
                return $"vc_build: {line.Kind.Item} {(on ? "ticked" : "unticked")}";
            }
            return "vc_build: what to pick: " + string.Join("; ", lines.Select(l => $"{l.Kind.Item} {(l.Ticked ? "ticked" : "unticked")}"));
        }

        /// <summary>vc_build grow [crop on|off]: the Farm's "What to grow" list, or a crop ticked / unticked (as the window does).</summary>
        private static string Grow(ConstructionModule site, string[] args)
        {
            WorkerModule worker = site.GetComponent<WorkerModule>();
            if (worker == null || site.Building.TypeId != BuildingTypes.FarmId) return "vc_build: only a Farm has a What to grow list.";
            List<CropLine> lines = Jobs.CropList.For(site.Building);
            if (args.Length >= 3)
            {
                string word = args[1].ToLowerInvariant();
                CropLine line = lines.FirstOrDefault(l => l.Crop.Piece.ToLowerInvariant() == word)
                                ?? lines.FirstOrDefault(l => l.Crop.Piece.ToLowerInvariant().Contains(word));
                if (line == null) return $"vc_build: no crop called '{args[1]}' on the list";
                bool on = args[2].ToLowerInvariant() == "on";
                worker.RequestSetGrowTicked(line.Crop.Piece, on);
                return $"vc_build: {line.Crop.Piece} {(on ? "ticked" : "unticked")}";
            }
            return $"vc_build: what to grow ({Jobs.FarmerJob.RulesOf(site.Building).Count} plots): " + string.Join("; ", lines.Select(l =>
                $"{l.Crop.Piece} (seed {l.Crop.Seed}) {(l.Ticked ? "ticked" : l.Offered ? "unticked" : "doesn't grow here")}"));
        }

        /// <summary>As the window's Upgrade button: refused with the reason, or ordered.</summary>
        private static string Order(ConstructionModule site)
        {
            UpgradeLock locked = site.UpgradeCheck();
            if (locked != UpgradeLock.None) return $"vc_build: can't order an upgrade of {site.Building.TypeId}: {locked}";
            UpgradePreview preview = site.PreviewUpgrade();
            if (preview == null) return $"vc_build: {site.Building.TypeId}'s next level has no usable design";
            if (preview.Site.Colour == SiteColour.Red) return $"vc_build: the bigger building can't go here: {SiteText.Describe(preview.Site, 0f)}";
            site.RequestUpgrade();
            return $"vc_build: upgrade of {site.Building.TypeId} to level {preview.Level} ordered; it needs {Localization.instance.Localize(PiecePlacer.DescribeItems(preview.Needs))}";
        }

        /// <summary>Puts the materials the hut's next level needs into its chest, as far as they fit.</summary>
        private static string Stock(ConstructionModule site)
        {
            UpgradePreview preview = site.PreviewUpgrade();
            if (preview == null) return $"vc_build: {site.Building.TypeId} has no next level.";
            Inventory inventory = site.GetComponent<Container>()?.GetInventory();
            if (inventory == null) return $"vc_build: {site.Building.TypeId} has no chest.";
            var put = new List<string>();
            foreach (KeyValuePair<string, int> need in preview.Needs)
            {
                GameObject prefab = ObjectDB.instance.GetItemPrefab(need.Key);
                int added = 0;
                while (prefab != null && added < need.Value && inventory.AddItem(prefab, 1)) added++;
                put.Add($"{added} of {need.Value} {need.Key}");
            }
            return $"vc_build: put {string.Join(", ", put)} in the {site.Building.TypeId}'s chest (for level {preview.Level}).";
        }

        /// <summary>
        /// The hut gallery (2026-10-18, the player: every hut at every level, each with a chest of what the builder needed
        /// for it): a black metal chest (more when it fills) beyond the hut's front right corner holding the materials of
        /// the pieces its level added, and a post with a sign each way naming the hut and level.
        /// </summary>
        private static string CostChest(ConstructionModule site)
        {
            Dictionary<string, int> cost = site.LevelCost();
            site.PlanReach(out float maxX, out float maxZ);
            Transform hut = site.transform;
            Vector3 Spot(float x, float z)
            {
                Vector3 p = hut.TransformPoint(new Vector3(x, 0f, z));
                p.y = ZoneSystem.instance.GetGroundHeight(p);
                return p;
            }
            float cx = maxX + 2.5f, cz = maxZ + 2.5f;
            GameObject chestPrefab = PiecePlacer.Prefab("piece_chest_blackmetal");
            if (chestPrefab == null || chestPrefab.GetComponent<Container>() == null) return "vc_build costchest: no black metal chest in this game";
            var parts = new List<string>();
            Inventory chest = null;
            int chests = 0;
            foreach (KeyValuePair<string, int> need in cost.OrderByDescending(n => n.Value))
            {
                GameObject item = ObjectDB.instance.GetItemPrefab(need.Key);
                if (item == null)
                {
                    parts.Add($"{need.Value} {need.Key} (no such item)");
                    continue;
                }
                int left = need.Value;
                while (left > 0)
                {
                    if (chest == null || !chest.AddItem(item, 1))
                    {
                        GameObject box = Object.Instantiate(chestPrefab, Spot(cx + 2.4f * chests, cz), hut.rotation);
                        MarkCostChest(box);
                        chest = box.GetComponent<Container>().GetInventory();
                        chests++;
                        continue;
                    }
                    left--;
                }
                parts.Add($"{need.Value} {Localization.instance.Localize(item.GetComponent<ItemDrop>().m_itemData.m_shared.m_name)}");
            }
            string name = Localization.instance.Localize(Colony.Saga.HutWord(site.Building));
            string label = $"{name}, level {site.Building.Level}";
            Vector3 post = Spot(cx, cz + 2f);
            GameObject pole = PiecePlacer.Prefab("wood_pole2"), signPrefab = PiecePlacer.Prefab("sign");
            if (pole != null) MarkCostChest(Object.Instantiate(pole, post + Vector3.up, hut.rotation));
            foreach (float side in signPrefab != null ? new[] { 1f, -1f } : new float[0])
            {
                GameObject sign = Object.Instantiate(signPrefab, post + Vector3.up * 1.6f + hut.forward * (0.15f * side),
                    hut.rotation * Quaternion.Euler(0f, side > 0 ? 0f : 180f, 0f));
                ZNetView view = sign.GetComponent<ZNetView>();
                if (view != null && view.IsValid()) view.GetZDO().Set(ZDOVars.s_text, label);
                MarkCostChest(sign);
            }
            return $"vc_build costchest: {label}: {(parts.Count == 0 ? "nothing" : string.Join(", ", parts))} in {chests} chest{(chests == 1 ? "" : "s")} beside it";
        }

        /// <summary>Marks what vc_build costchest puts up (its chests, post and signs), so test colonies' wipes take it too.</summary>
        private const string CostChestKey = "vc_costchest";

        private static void MarkCostChest(GameObject go)
        {
            ZNetView view = go != null ? go.GetComponent<ZNetView>() : null;
            if (view != null && view.IsValid()) view.GetZDO().Set(CostChestKey, true);
        }

        /// <summary>A chest, post or sign vc_build costchest put up (vc_world tidy and wipe take these with the colonies).</summary>
        internal static bool IsCostChestPiece(Piece piece)
        {
            ZNetView view = piece != null ? piece.GetComponent<ZNetView>() : null;
            return view != null && view.IsValid() && view.GetZDO().GetBool(CostChestKey);
        }

        /// <summary>
        /// vc_build costchest clear [radius]: the hut gallery's cost chests, posts and signs near you go, those put up before
        /// they were marked too (2026-10-18: the first gallery's 30 stood on in the second's lots: a black metal chest, a
        /// pole or a sign that nobody built and no hut owns). Test worlds only.
        /// </summary>
        private static string ClearCostChests(Player player, float radius)
        {
            if (player == null || ZNet.instance == null || !TestWorlds.IsTestWorld(ZNet.instance.GetWorldName()))
                return "vc_build costchest clear: test worlds only";
            var near = new List<Piece>();
            Piece.GetAllPiecesInRadius(player.transform.position, radius, near);
            int removed = 0;
            foreach (Piece piece in near.Where(p => p != null && (IsCostChestPiece(p) || UnmarkedCostChest(p))).ToList())
            {
                ZNetView view = piece.GetComponent<ZNetView>();
                if (view == null || !view.IsValid()) continue;
                if (!view.IsOwner()) view.ClaimOwnership();
                ZNetScene.instance.Destroy(piece.gameObject);
                removed++;
            }
            return $"vc_build costchest clear: removed {removed} cost chests, posts and signs within {radius:0} m";
        }

        private static bool UnmarkedCostChest(Piece piece)
        {
            string name = Utils.GetPrefabName(piece.gameObject);
            return (name == "piece_chest_blackmetal" || name == "wood_pole2" || name == "sign") && piece.GetCreator() == 0
                   && PiecePlacer.SiteOf(piece.gameObject).Length == 0 && piece.GetComponent<ColonyBuilding>() == null;
        }

        /// <summary>Puts an item (by its prefab name, e.g. Hoe) into the hut's chest, as far as it fits.</summary>
        private static string Give(ConstructionModule site, string item, int amount)
        {
            GameObject prefab = ObjectDB.instance.GetItemPrefab(item);
            Inventory inventory = site.GetComponent<Container>()?.GetInventory();
            if (prefab == null || inventory == null) return $"vc_build: no item called '{item}', or no chest.";
            int added = 0;
            while (added < amount && inventory.AddItem(prefab, 1)) added++;
            return $"vc_build: put {added} of {amount} {item} in the {site.Building.TypeId}'s chest.";
        }

        private static string Status(ConstructionModule site)
        {
            BuildOrderRecord order = site.Order;
            string hut = $"{site.Building.TypeId} {site.Building.Id}";
            if (order == null) return $"{hut}: no build order (placed before builders existed; counts as built)";
            string claim = order.HasLiveClaim(ConstructionModule.Now()) ? "live" : "lapsed";
            string ground = float.IsNaN(order.Ground) ? "-" : order.Ground.ToString("0.00");
            UpgradePreview next = order.State == BuildState.Done ? site.PreviewUpgrade() : null;
            SiteFindings f = next?.Findings;
            string nextLevel = f == null ? "" :
                $"; level {next.Level}'s site: {next.Site.Colour}, red [{string.Join(", ", f.Red)}], floor {f.Floor:0.00}, reaches {f.ReachBelow:0.00} below it, " +
                $"ground at the chest {f.Natural:0.00}, to flatten to {f.Target:0.00}, dig {f.Dig}, raise {f.Raise}";
            return $"{hut}: level {site.Building.Level}, {order.Kind} to level {order.TargetLevel}, {order.State}, {order.DoneCount} of {order.Total} pieces up, " +
                   $"{order.TakeAwayLeft} old pieces to take away, {order.SkippedCount} skipped, " +
                   $"plan of {site.Plan.Count}, ground level {ground} (chest at {site.transform.position.y:0.00}), " +
                   $"builder '{order.Builder}' ({claim}), needs '{order.Needs}'{nextLevel}";
        }

        public static void Print(string text)
        {
            Log.Event(text);
            if (global::Console.instance != null) global::Console.instance.AddString(text);
        }
    }
}
