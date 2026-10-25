using System.Collections;
using System.Globalization;
using System.Linq;
using BepInEx.Configuration;
using Jotunn.Entities;
using UnityEngine;
using VikingColonies.AI;
using VikingColonies.Buildings;
using VikingColonies.Citizens;
using VikingColonies.Construction;
using VikingColonies.Gui;

namespace VikingColonies.DevTools
{
    /// <summary>
    /// vc_ui: what a player sees, for testing without hands on the game. Opens a colony building's window (as Shift+E) or
    /// its chest (as E) and writes what it shows to the log; logs hover texts nearby; closes everything.
    /// </summary>
    internal sealed class UiCommand : ConsoleCommand
    {
        public override string Name => "vc_ui";
        public override string Help => "VikingColonies (test worlds): 'vc_ui window [townhall|builder|lumberjack|longhouse|forager]' opens the nearest such " +
                                       "building's window (within 8 m) and logs its text; 'vc_ui text' logs the open windows; 'vc_ui treelist' (or replant) opens the " +
                                       "nearest Lumberjack's Hut's Trees list; 'vc_ui order <hut> first|aside|resume|cancel' presses a Work orders button on the open Town Hall window; 'vc_ui report' presses its Report a problem button (the bug form's address goes to the log); " +
                                       "'vc_ui feedback' presses its Feedback button; 'vc_ui feedback discord|github' presses that button in the open Feedback window (the address goes to the log); " +
                                       "'vc_ui feedback arm' forgets the feedback ask on this computer and lets it come in this test world; 'vc_ui feedback due' also makes it due now; 'vc_ui feedback state' logs it; 'vc_ui pick' the nearest Forager's Hut's \"What to pick\" list; 'vc_ui pickables' " +
                                       "logs every pickable the game has (what it gives, whether it grows back, where it grows); 'vc_ui chest' opens the nearest colony chest; 'vc_ui shifte <hut>' logs its chest's hover, then presses Shift+E on it through every mod's hooks; 'vc_ui hover [metres]' " +
                                       "logs the hover texts of settlers and cairns nearby; 'vc_ui tab <tab>' shows a tab of the open window (stock: Keep in stock); " +
                                       "'vc_ui keep <hut> <item|food> <amount>' sets the nearest such building's Keep in stock entry (0 takes it off); " +
                                       "'vc_ui close' closes everything";
        public override bool IsCheat => true;

        public override void Run(string[] args)
        {
            if (DevHelpers.RefuseOutsideTestWorld(Name)) return;   // it changes the world (the whole-mod review of 1.4.1)
            Player p = Player.m_localPlayer;
            if (p == null) return;
            string what = args != null && args.Length > 0 ? args[0].ToLowerInvariant() : "";
            switch (what)
            {
                case "window" when args.Length > 1 && args[1] == "field":
                {
                    Colony.FieldMarker marker = Colony.FieldMarker.All.Where(m => m != null && m.IsValid)
                        .OrderBy(m => Vector3.Distance(m.transform.position, p.transform.position)).FirstOrDefault();
                    if (marker == null || Vector3.Distance(marker.transform.position, p.transform.position) > 8f)
                    {
                        BuildCommand.Print("vc_ui: no field marker within 8 m");
                        return;
                    }
                    Windows.Field.OpenOn(marker);
                    Plugin.Instance.StartCoroutine(LogSoon());
                    break;
                }
                case "window":
                    ColonyBuilding building = Nearest(p, args.Length > 1 ? args[1] : null, 8f);
                    if (building == null)
                    {
                        BuildCommand.Print("vc_ui: no such colony building within 8 m");
                        return;
                    }
                    if (building.IsTownHall) Windows.TownHall.Open(building);
                    else Windows.Hut.Open(building);
                    Plugin.Instance.StartCoroutine(LogSoon());
                    break;
                case "treelist":
                case "replant":   // its name before 0.22
                    ColonyBuilding hut = Nearest(p, "lumberjack", 8f);
                    if (hut == null)
                    {
                        BuildCommand.Print("vc_ui: no Lumberjack's Hut within 8 m");
                        return;
                    }
                    Windows.Trees.Open(hut);
                    Plugin.Instance.StartCoroutine(LogSoon());
                    break;
                case "report":
                    if (!Windows.TownHall.IsOpen)
                    {
                        BuildCommand.Print("vc_ui: open the Town Hall window first (vc_ui window townhall)");
                        return;
                    }
                    Windows.TownHall.PressReport(true);   // test: the address goes to the log; no browser, no clipboard
                    Plugin.Instance.StartCoroutine(LogSoon());
                    break;
                case "feedback":
                    Feedback(args.Length > 1 ? args[1].ToLowerInvariant() : "");
                    break;
                case "order":
                    if (!Windows.TownHall.IsOpen)
                    {
                        BuildCommand.Print("vc_ui: open the Town Hall window first (vc_ui window townhall)");
                        return;
                    }
                    string hutWord = args.Length > 1 ? args[1].ToLowerInvariant() : "";
                    string action = args.Length > 2 ? args[2].ToLowerInvariant() : "";
                    BuildingType orderType = DevHelpers.TypeByWord(hutWord);
                    bool hasOrder = orderType != null && Windows.TownHall.OrderActionForTest(orderType.Id, action);
                    BuildCommand.Print(hasOrder ? $"vc_ui: {action} on the {orderType.Id}'s order" : $"vc_ui: no open order for '{hutWord}'");
                    Plugin.Instance.StartCoroutine(LogSoon());
                    break;
                case "grow":
                    ColonyBuilding farmHut = Nearest(p, "farm", 8f);
                    if (farmHut == null)
                    {
                        BuildCommand.Print("vc_ui: no Farm within 8 m");
                        return;
                    }
                    Windows.Grow.Open(farmHut);
                    Plugin.Instance.StartCoroutine(LogSoon());
                    break;
                case "pick":
                    ColonyBuilding foragerHut = Nearest(p, "forager", 8f);
                    if (foragerHut == null)
                    {
                        BuildCommand.Print("vc_ui: no Forager's Hut within 8 m");
                        return;
                    }
                    Windows.Pick.Open(foragerHut);
                    Plugin.Instance.StartCoroutine(LogSoon());
                    break;
                case "text":
                    LogWindows();
                    break;
                case "pickables":
                    LogPickables();
                    break;
                case "crops":
                    LogCrops();
                    break;
                case "chest":
                    ColonyBuilding store = Nearest(p, args.Length > 1 ? args[1] : null, 8f);
                    Container container = store != null ? store.GetComponent<Container>() : null;
                    if (container == null || InventoryGui.instance == null)
                    {
                        BuildCommand.Print("vc_ui: no colony chest within 8 m");
                        return;
                    }
                    InventoryGui.instance.Show(container);
                    BuildCommand.Print($"vc_ui: opened the {store.TypeId}'s chest ({container.GetInventory().GetWidth()} x {container.GetInventory().GetHeight()}, " +
                                       $"{container.GetInventory().GetAllItems().Count} stacks)");
                    break;
                case "shifte":
                {
                    // vc_ui shifte <hut>: the building's chest hover, then Shift+E on it as the game calls it, through every
                    // mod's hooks (issue #1, 1.4.1: MyLittleUI's rename took Shift+E and dropped "Manage").
                    ColonyBuilding used = Nearest(p, args.Length > 1 ? args[1] : null, 8f);
                    Container chest = used != null ? used.GetComponent<Container>() : null;
                    if (chest == null)
                    {
                        BuildCommand.Print("vc_ui: no colony chest within 8 m");
                        return;
                    }
                    BuildCommand.Print($"vc_ui: the {used.TypeId}'s hover: {(chest.GetHoverText() ?? "").Replace("\n", " / ")}");
                    // As the game does it: the player's Interact on what they look at (it calls the chest's, through every mod's hooks).
                    System.Reflection.MethodInfo press = HarmonyLib.AccessTools.Method(typeof(Player), "Interact", new[] { typeof(GameObject), typeof(bool), typeof(bool) });
                    bool took = press != null;
                    TextRequestWatch.On = true;   // who asks for a text box while it's pressed, to the log
                    try { press?.Invoke(p, new object[] { chest.gameObject, false, true }); }
                    finally { TextRequestWatch.On = false; }
                    WindowBase opened = used.IsTownHall ? (WindowBase)Windows.TownHall : Windows.Hut;
                    // Not TextInput.IsVisible: Jötunn makes it true while a mod window blocks input. A text box asked for is logged
                    // by TextRequestWatch.
                    BuildCommand.Print($"vc_ui: Shift+E on the {used.TypeId}: {(took ? "taken" : "not taken")}, its window {(opened.IsOpen ? "open" : "closed")}");
                    break;
                }
                case "hammer":
                {
                    // vc_ui hammer: the hammer's tabs and where the colony's pieces sit among them (1.4.3, the player's watch of
                    // 1.4.2: the huts showed under Furniture, while the guide said Misc).
                    PieceTable table = ObjectDB.instance?.GetItemPrefab("Hammer")?.GetComponent<ItemDrop>()?.m_itemData?.m_shared?.m_buildPieces;
                    if (table == null)
                    {
                        BuildCommand.Print("vc_ui: hammer: no hammer piece table");
                        return;
                    }
                    var categories = HarmonyLib.Traverse.Create(table).Field("m_categories").GetValue() as System.Collections.IList;
                    var labels = HarmonyLib.Traverse.Create(table).Field("m_categoryLabels").GetValue() as System.Collections.IList;
                    var tabs = new System.Collections.Generic.List<string>();
                    for (int i = 0; categories != null && i < categories.Count; i++)
                        tabs.Add($"{i}: {categories[i]} '{(labels != null && i < labels.Count ? Localization.instance.Localize(labels[i]?.ToString() ?? "") : "?")}'");
                    BuildCommand.Print($"vc_ui: hammer tabs: {string.Join(", ", tabs.ToArray())}");
                    foreach (BuildingType type in BuildingTypes.All)
                    {
                        Piece piece = ZNetScene.instance.GetPrefab(type.PrefabName)?.GetComponent<Piece>();
                        if (piece == null) continue;
                        int index = categories != null ? categories.IndexOf(piece.m_category) : -1;
                        string label = index >= 0 && labels != null && index < labels.Count ? Localization.instance.Localize(labels[index]?.ToString() ?? "") : "none";
                        BuildCommand.Print($"vc_ui: hammer: {type.Id} is in category {piece.m_category} ({(int)piece.m_category}), the tab '{label}'");
                    }
                    break;
                }
                case "hover":
                    float range = args.Length > 1 && float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float r) ? r : 15f;
                    LogHovers(p, range);
                    break;
                case "sleepers":
                    LogSleepers(p, args.Length > 1 && float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float sr) ? sr : 60f);
                    break;
                case "pins":
                    LogPins();
                    break;
                case "place":
                    PlaceMode(p, args.Length > 1 ? args[1] : "lumberjack");
                    break;
                case "field":   // 'vc_ui field [size] [crop]': a field marker of the nearest Farm (within 40 m) 6 m in front of you (the 1.0 play-through)
                {
                    ColonyBuilding farm = Nearest(p, "farm", 40f);
                    int size = args.Length > 1 && int.TryParse(args[1], out int fs) ? fs : VikingColonies.Core.FieldSizes.New;
                    Colony.FieldMarker field = farm != null ? DevHelpers.PlaceField(farm, p.transform.position + p.transform.forward * 6f, size, args.Length > 2 ? args[2] : "") : null;
                    BuildCommand.Print(field != null ? $"vc_ui: a field marker of the farm {farm.Id} at {field.transform.position}, size {size}" : "vc_ui: no Farm within 40 m");
                    break;
                }
                case "give":
                case "take":
                    GiveOrTake(p, what == "give", args);
                    break;
                case "forage":
                    LogForage(p);
                    break;
                case "health":
                    LogHealth(p, args.Length > 1 && float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float hr) ? hr : 40f);
                    break;
                case "wallby":
                    WallBy(p, args.Length > 1 ? args[1] : "");
                    break;
                case "spawn":
                    SpawnAt(p, args);
                    break;
                case "face":
                    // Look at a point (vc_ui face ~0 ~5): for screenshots.
                    if (args.Length > 2)
                    {
                        var target = new Vector3(Coord(args[1], p.transform.position.x), 0f, Coord(args[2], p.transform.position.z));
                        target.y = ZoneSystem.instance.GetGroundHeight(target) + 0.8f;
                        DevHelpers.Face(p, target);
                        BuildCommand.Print($"vc_ui: looking at ({target.x:0.0}, {target.z:0.0})");
                    }
                    break;
                case "signtext":
                {
                    // The nearest sign within 10 m shows this text (vc_ui signtext Carrots): for picking a field marker's look.
                    Sign sign = Object.FindObjectsByType<Sign>(FindObjectsSortMode.None)
                        .Where(s => Vector3.Distance(s.transform.position, p.transform.position) < 10f)
                        .OrderBy(s => Vector3.Distance(s.transform.position, p.transform.position)).FirstOrDefault();
                    ZNetView view = sign != null ? sign.GetComponent<ZNetView>() : null;
                    if (view != null && view.IsValid()) view.GetZDO().Set(ZDOVars.s_text, args.Length > 1 ? string.Join(" ", args.Skip(1)) : "");
                    BuildCommand.Print(sign != null ? "vc_ui: the sign says it" : "vc_ui: no sign within 10 m");
                    break;
                }
                case "keep":
                {
                    // vc_ui keep <hut> <item> <amount>: as the Keep in stock tab's buttons would (0 takes it off the list).
                    ColonyBuilding kept = args.Length > 3 ? Nearest(p, args[1], 8f) : null;
                    if (kept == null || !int.TryParse(args[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int amount))
                    {
                        BuildCommand.Print("vc_ui: 'vc_ui keep <hut> <item> <amount>' near such a building (within 8 m)");
                        return;
                    }
                    string item = args[2] == "food" ? Core.StockList.AnyFood : args[2];
                    BuildCommand.Print(StockTab.SetForTest(kept, item, amount)
                        ? $"vc_ui: the {kept.TypeId} keeps {amount} {item} (asked its owner)" : $"vc_ui: the {kept.TypeId}'s list is full");
                    break;
                }
                case "tab" when Windows.Hut.IsOpen:
                    Windows.Hut.ShowTabForTest(args.Length > 1 ? args[1] : "hut");
                    Plugin.Instance.StartCoroutine(LogSoon());
                    break;
                case "tab":
                    if (!Windows.TownHall.IsOpen) BuildCommand.Print("vc_ui: open the Town Hall's window first (vc_ui window townhall)");
                    else
                    {
                        Windows.TownHall.ShowTabForTest(args.Length > 1 ? args[1] : "citizens");
                        Plugin.Instance.StartCoroutine(LogSoon());
                    }
                    break;
                case "choose":
                    if (!Windows.TownHall.IsOpen) BuildCommand.Print("vc_ui: open the Town Hall's window first (vc_ui window townhall)");
                    else
                    {
                        bool found = args.Length > 1 && Windows.TownHall.ChooseForTest(args[1]);
                        if (found && args.Length > 2 && args[2] == "recall") Windows.TownHall.RecallForTest();
                        if (!found) BuildCommand.Print($"vc_ui: no citizen called '{(args.Length > 1 ? args[1] : "")}' in this Town Hall's roster");
                        Plugin.Instance.StartCoroutine(LogSoon());
                    }
                    break;
                case "recall":
                    Citizen called = args.Length > 1 ? Citizen.All.Where(c => c != null && c.CitizenName == args[1])
                        .OrderBy(c => Vector3.Distance(c.transform.position, p.transform.position)).FirstOrDefault() : null;
                    if (called == null) BuildCommand.Print($"vc_ui: no settler called '{(args.Length > 1 ? args[1] : "")}'");
                    else
                    {
                        called.RequestRecall();
                        BuildCommand.Print($"vc_ui: asked {called.CitizenName} to come back to the Town Hall (from {Vector3.Distance(called.transform.position, p.transform.position):0} m away from you)");
                    }
                    break;
                case "recipe":
                    for (int i = 1; i < args.Length; i++) LogRecipe(args[i]);
                    break;
                case "prefabs":
                {
                    // Every prefab whose name holds one of the words (vc_ui prefabs banner pole sign): for picking models.
                    string[] words = args.Skip(1).Select(w => w.ToLowerInvariant()).ToArray();
                    System.Collections.Generic.List<string> found = ZNetScene.instance.m_prefabs.Where(g => g != null && words.Any(w => g.name.ToLowerInvariant().Contains(w)))
                        .Select(g => g.name + (g.GetComponent<Piece>() != null ? " (piece)" : "")).OrderBy(n => n).ToList();
                    Log.Event($"vc_ui prefabs [{string.Join(" ", words)}]: {found.Count}: {string.Join(", ", found)}");
                    BuildCommand.Print($"vc_ui: {found.Count} prefabs (in the log)");
                    break;
                }
                case "pieces":
                    LogPieces(p, args.Length > 1 ? args[1] : "", args.Length > 2 && float.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float pr) ? pr : 6f);
                    break;
                case "trees":
                    LogTrees(p, args.Length > 1 && float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float tr) ? tr : 15f);
                    break;
                case "cultivate":
                    BuildCommand.Print(Construction.GroundShaper.Cultivate(p.transform.position)
                        ? "vc_ui: the ground where you stand is cultivated (as with a Cultivator)" : "vc_ui: no Cultivator in the game");
                    break;
                case "close":
                    Windows.Hut.Close();
                    Windows.TownHall.Close();
                    Windows.Trees.Close();
                    Windows.Pick.Close();
                    Windows.Grow.Close();
                    Windows.Field.Close();
                    Windows.FieldCrop.Close();
                    Windows.Feedback.Close();
                    if (InventoryGui.instance != null && InventoryGui.IsVisible()) InventoryGui.instance.Hide();
                    ItemDrop.ItemData held = p.GetInventory().GetItem("$item_hammer");
                    if (held != null && p.IsItemEquiped(held)) p.UnequipItem(held);
                    BuildCommand.Print("vc_ui: closed");
                    break;
                default:
                    BuildCommand.Print("vc_ui: say window, text, replant, chest, hover or close");
                    break;
            }
        }

        /// <summary>vc_ui give|take <item> <amount> [building]: puts items into (or takes them out of) the nearest colony chest, the Town Hall's too.</summary>
        private static void GiveOrTake(Player p, bool give, string[] args)
        {
            string item = args.Length > 1 ? args[1] : "";
            int amount = args.Length > 2 && int.TryParse(args[2], out int n) ? n : 1;
            ColonyBuilding store = Nearest(p, args.Length > 3 ? args[3] : null, 12f);
            Container container = store != null ? store.GetComponent<Container>() : null;
            GameObject prefab = ZNetScene.instance.GetPrefab(item);
            if (container == null || prefab == null)
            {
                BuildCommand.Print($"vc_ui: no colony chest within 12 m, or no item called '{item}'");
                return;
            }
            Inventory inventory = container.GetInventory();
            string name = prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_name;
            if (give) inventory.AddItem(prefab, amount);
            else inventory.RemoveItem(name, amount);
            BuildCommand.Print($"vc_ui: the {store.TypeId}'s chest now holds {inventory.CountItems(name)} {item}");
        }

        /// <summary>The pickables within 12 m and whether the forager counts each as his (and why not).</summary>
        private static void LogForage(Player p)
        {
            Vector3 me = p.transform.position;
            Heightmap here = Heightmap.FindHeightmap(me);
            BuildCommand.Print($"vc_ui: the ground where you stand is {(here != null && here.IsCultivated(me) ? "cultivated" : "not cultivated")}");
            foreach (Pickable bush in Object.FindObjectsByType<Pickable>(FindObjectsSortMode.None).Where(b => b != null && Vector3.Distance(b.transform.position, me) <= 12f))
            {
                Vector3 at = bush.transform.position;
                Heightmap ground = Heightmap.FindHeightmap(at);
                Piece piece = bush.GetComponent<Piece>();
                BuildCommand.Print($"vc_ui: {Utils.GetPrefabName(bush.gameObject)} at ({at.x:0.0}, {at.z:0.0}): the forager's {Jobs.ForagerJob.IsHis(bush, new System.Collections.Generic.HashSet<string>())} (on the list, ticked or not) " +
                                   $"(grows back {bush.m_respawnTimeMinutes > 0f}, ready {bush.CanBePicked()}, on cultivated ground {ground != null && ground.IsCultivated(at)}, " +
                                   $"placed by a player {piece != null && piece.GetCreator() != 0L})");
            }
        }

        /// <summary>The health of the colony buildings within range, and of the pieces there that are damaged.</summary>
        private static void LogHealth(Player p, float range)
        {
            Vector3 me = p.transform.position;
            foreach (ColonyBuilding b in ColonyBuilding.All.Where(b => b != null && b.IsValid && Vector3.Distance(b.transform.position, me) <= range))
            {
                WearNTear wear = b.GetComponent<WearNTear>();
                BuildCommand.Print($"vc_ui: {b.TypeId} chest health {(wear != null ? wear.GetHealthPercentage() * 100f : -1f):0} %");
            }
            var pieces = new System.Collections.Generic.List<Piece>();
            Piece.GetAllPiecesInRadius(me, range, pieces);
            var hurt = pieces.Select(x => x != null ? x.GetComponent<WearNTear>() : null)
                .Where(w => w != null && w.GetHealthPercentage() < 0.999f).ToList();
            BuildCommand.Print($"vc_ui: {hurt.Count} of {pieces.Count} pieces within {range:0} m are damaged" +
                               (hurt.Count > 0 ? ": " + string.Join(", ", hurt.Take(12).Select(w => $"{Utils.GetPrefabName(w.gameObject)} {w.GetHealthPercentage() * 100f:0} %")) : ""));
        }

        /// <summary>A wooden wall right beside the nearest tree of that kind (for watching a lumberjack fell it).</summary>
        private static void WallBy(Player p, string treePrefab)
        {
            Vector3 me = p.transform.position;
            GameObject tree = Object.FindObjectsByType<TreeBase>(FindObjectsSortMode.None)
                .Where(t => t != null && (treePrefab.Length == 0 || Utils.GetPrefabName(t.gameObject) == treePrefab))
                .OrderBy(t => Vector3.Distance(t.transform.position, me)).Select(t => t.gameObject).FirstOrDefault();
            GameObject wall = ZNetScene.instance.GetPrefab("woodwall");
            if (tree == null || wall == null)
            {
                BuildCommand.Print("vc_ui: no such tree loaded");
                return;
            }
            Vector3 away = me - tree.transform.position;
            away.y = 0f;
            away = away.sqrMagnitude > 0.01f ? away.normalized : Vector3.forward;
            Vector3 at = tree.transform.position + away * 1.3f;
            at.y = ZoneSystem.instance.GetGroundHeight(at) + 1f;
            Object.Instantiate(wall, at, Quaternion.LookRotation(away));
            BuildCommand.Print($"vc_ui: a woodwall stands 1.3 m from the {Utils.GetPrefabName(tree)} at ({tree.transform.position.x:0.0}, {tree.transform.position.z:0.0})");
        }

        /// <summary>vc_ui spawn <prefab> <x z | ~dx ~dz> [turn] [up]: puts a world object (a tree, a rock, a bush, a wall) exactly there.</summary>
        private static void SpawnAt(Player p, string[] args)
        {
            GameObject prefab = args.Length > 1 ? ZNetScene.instance.GetPrefab(args[1]) : null;
            Vector3 me = p.transform.position;
            float x = me.x, z = me.z;
            if (args.Length > 3)
            {
                x = Coord(args[2], me.x);
                z = Coord(args[3], me.z);
            }
            if (prefab == null)
            {
                BuildCommand.Print($"vc_ui: no prefab called '{(args.Length > 1 ? args[1] : "")}'");
                return;
            }
            // Optional: its turn in degrees, and how high above the ground (a wall's middle stands a metre up).
            float turn = args.Length > 4 ? float.Parse(args[4], CultureInfo.InvariantCulture) : 0f;
            float up = args.Length > 5 ? float.Parse(args[5], CultureInfo.InvariantCulture) : 0f;
            var at = new Vector3(x, 0f, z);
            at.y = ZoneSystem.instance.GetGroundHeight(at) + up;
            Object.Instantiate(prefab, at, Quaternion.Euler(0f, turn, 0f));
            BuildCommand.Print($"vc_ui: a {args[1]} stands at ({at.x:0.0}, {at.y:0.0}, {at.z:0.0})");
        }

        private static float Coord(string word, float mine) =>
            word.StartsWith("~") ? mine + (word.Length > 1 ? float.Parse(word.Substring(1), CultureInfo.InvariantCulture) : 0f)
                                  : float.Parse(word, CultureInfo.InvariantCulture);

        /// <summary>The trees, stumps and logs within range: prefab, where, and the axe tier each needs.</summary>
        private static void LogTrees(Player p, float range)
        {
            Vector3 me = p.transform.position;
            var found = new System.Collections.Generic.List<string>();
            foreach (TreeBase tree in Object.FindObjectsByType<TreeBase>(FindObjectsSortMode.None).Where(t => t != null && Vector3.Distance(t.transform.position, me) <= range))
                found.Add($"{Utils.GetPrefabName(tree.gameObject)} ({tree.transform.position.x:0.0}, {tree.transform.position.z:0.0}) tier {tree.m_minToolTier}");
            foreach (TreeLog log in Object.FindObjectsByType<TreeLog>(FindObjectsSortMode.None).Where(t => t != null && Vector3.Distance(t.transform.position, me) <= range))
                found.Add($"{Utils.GetPrefabName(log.gameObject)} (log) ({log.transform.position.x:0.0}, {log.transform.position.z:0.0}) tier {log.m_minToolTier}");
            foreach (Destructible thing in Object.FindObjectsByType<Destructible>(FindObjectsSortMode.None).Where(t => t != null && Vector3.Distance(t.transform.position, me) <= range))
                found.Add($"{Utils.GetPrefabName(thing.gameObject)} ({thing.GetDestructibleType()}) ({thing.transform.position.x:0.0}, {thing.transform.position.z:0.0}) tier {thing.m_minToolTier}");
            BuildCommand.Print($"vc_ui: {found.Count} trees, logs and stumps within {range:0} m: {string.Join(", ", found)}");
        }

        /// <summary>What a building piece costs, where it's made, and its size (for checking which era a design belongs to).</summary>
        private static void LogRecipe(string prefabName)
        {
            GameObject prefab = ZNetScene.instance.GetPrefab(prefabName);
            Piece piece = prefab != null ? prefab.GetComponent<Piece>() : null;
            if (piece == null)
            {
                BuildCommand.Print($"vc_ui: no building piece called '{prefabName}'");
                return;
            }
            string needs = string.Join(", ", piece.m_resources.Select(r => $"{r.m_amount} {(r.m_resItem != null ? r.m_resItem.name : "?")}"));
            Bounds shape = Construction.PieceShape.Of(prefab).Extents;
            BuildCommand.Print($"vc_ui: {prefabName} ('{Localization.instance.Localize(piece.m_name)}'): {needs}; made at {(piece.m_craftingStation != null ? piece.m_craftingStation.name : "nothing")}; " +
                               $"size {shape.size.x:0.00} x {shape.size.y:0.00} x {shape.size.z:0.00}, centre {shape.center.y:0.00} up");
        }

        /// <summary>vc_ui crops: every plant the Cultivator can plant: its seed, what it grows into and gives, grow time, room, biomes.</summary>
        private static void LogCrops()
        {
            GameObject cultivator = ObjectDB.instance.GetItemPrefab("Cultivator");
            PieceTable table = cultivator != null ? cultivator.GetComponent<ItemDrop>().m_itemData.m_shared.m_buildPieces : null;
            if (table == null)
            {
                BuildCommand.Print("vc_ui: no Cultivator");
                return;
            }
            int count = 0;
            foreach (GameObject pieceObject in table.m_pieces)
            {
                Plant plant = pieceObject != null ? pieceObject.GetComponent<Plant>() : null;
                Piece piece = pieceObject != null ? pieceObject.GetComponent<Piece>() : null;
                if (plant == null || piece == null) continue;
                string seed = string.Join("+", piece.m_resources.Where(r => r.m_resItem != null).Select(r => $"{r.m_amount} {r.m_resItem.name}"));
                string grows = string.Join("/", (plant.m_grownPrefabs ?? new GameObject[0]).Where(g => g != null).Select(g =>
                {
                    Pickable pick = g.GetComponent<Pickable>();
                    return pick != null ? $"{g.name} gives {pick.m_amount} {(pick.m_itemPrefab != null ? pick.m_itemPrefab.name : "-")} (grows back {pick.m_respawnTimeMinutes:0} min)"
                        : g.GetComponent<TreeBase>() != null ? $"{g.name} (a tree)" : g.name;
                }));
                Log.Event(string.Format(CultureInfo.InvariantCulture,
                    "vc_ui crop {0} ({1}): seed {2}; grows into {3}; {4:0}-{5:0} s; room {6:0.0} m; cultivated ground {7}; biomes {8}; heat {9}, cold {10}",
                    pieceObject.name, Localization.instance.Localize(piece.m_name), seed, grows, plant.m_growTime, plant.m_growTimeMax,
                    plant.m_growRadius, plant.m_needCultivatedGround, (int)plant.m_biome, plant.m_tolerateHeat, plant.m_tolerateCold));
                count++;
            }
            BuildCommand.Print($"vc_ui: {count} plants written to the log");
        }

        /// <summary>vc_ui pickables: every pickable the game has, what it gives, whether it grows back and where the world grows it.</summary>
        private static void LogPickables()
        {
            var lines = new System.Collections.Generic.List<string>();
            foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
            {
                Pickable pick = prefab != null ? prefab.GetComponent<Pickable>() : null;
                if (pick == null) continue;
                ItemDrop drop = pick.m_itemPrefab != null ? pick.m_itemPrefab.GetComponent<ItemDrop>() : null;
                int biomes = 0, entries = 0;
                foreach (ZoneSystem.ZoneVegetation veg in ZoneSystem.instance.m_vegetation)
                    if (veg != null && veg.m_enable && veg.m_prefab == prefab) { biomes |= (int)veg.m_biome; entries++; }
                lines.Add(string.Format(CultureInfo.InvariantCulture, "{0}: {1} x{2}, grows back {3:0} min, {4}, food {5:0}, wild in biomes {6} ({7} entries), piece {8}",
                    prefab.name, pick.m_itemPrefab != null ? pick.m_itemPrefab.name : "-", pick.m_amount, pick.m_respawnTimeMinutes,
                    drop != null ? drop.m_itemData.m_shared.m_itemType.ToString() : "-", drop != null ? drop.m_itemData.m_shared.m_food : 0f,
                    biomes, entries, prefab.GetComponent<Piece>() != null));
            }
            foreach (string line in lines.OrderBy(l => l)) Log.Info("vc_ui pickable " + line);
            BuildCommand.Print($"vc_ui: {lines.Count} pickables written to the log");
        }

        /// <summary>Pieces near you (of one prefab, or all) with the building each belongs to.</summary>
        private static void LogPieces(Player p, string prefab, float range)
        {
            var near = new System.Collections.Generic.List<Piece>();
            Piece.GetAllPiecesInRadius(p.transform.position, range, near);
            var found = near.Where(x => x != null && (prefab == "" || prefab == "all" || Utils.GetPrefabName(x.gameObject) == prefab))
                            .Select(x => $"{Utils.GetPrefabName(x.gameObject)} ({x.transform.position.x:0.0}, {x.transform.position.y:0.0}, {x.transform.position.z:0.0}) " +
                                         $"turned {x.transform.rotation.eulerAngles.y:0}, site '{Construction.PiecePlacer.SiteOf(x.gameObject)}'" +
                                         $"{(x.GetComponent<WearNTear>() != null ? ", wears" : "")}{(Construction.PiecePlacer.IsWildVegetation(x) ? ", wild vegetation" : "")}" +
                                         $"{(x.GetComponent<ItemDrop>() != null ? ", an item" : "")}{(x.GetComponent<ItemDrop>()?.IsPiece() == true ? " placed as a piece" : "")}" +
                                         $"{(Construction.PiecePlacer.IsLooseItem(x) ? ", loose" : "")}{(x.IsPlacedByPlayer() ? ", placed by a player" : "")}" +
                                         $", parts: {string.Join("/", x.GetComponents<Component>().Select(c => c.GetType().Name))}")
                            .ToList();
            BuildCommand.Print($"vc_ui: {found.Count} pieces within {range:0} m: {string.Join("; ", found)}");
        }

        /// <summary>Everyone indoors (asleep or sheltering): the hut he went into, and whether he really stands inside it.</summary>
        private static void LogSleepers(Player p, float range)
        {
            Vector3 me = p.transform.position;
            int count = 0, outside = 0;
            foreach (Citizen c in Citizen.All.Where(c => c != null && Vector3.Distance(c.transform.position, me) <= range))
            {
                if (c.Status != "$vc_status_sleeping" && c.Status != "$vc_status_sheltering") continue;
                CitizenAI ai = c.GetComponent<CitizenAI>();
                ColonyBuilding hut = ai != null ? ColonyBuilding.FindById(ai.RefugeIdForTest) : null;
                ConstructionModule site = hut != null ? hut.GetComponent<ConstructionModule>() : null;
                bool withinWalls = site != null && site.SiteFootprint.Contains(CoreVectors.V(c.transform.position));
                bool wall = ai != null && hut != null && ai.WallBetweenForTest(ai.RefugeChestForTest);
                bool inside = withinWalls && !wall;
                if (!inside) outside++;
                count++;
                BuildCommand.Print($"vc_ui: {c.CitizenName} {Localization.instance.Localize(c.Status)} in the {(hut != null ? hut.TypeId + " " + hut.Id.Substring(0, 8) : "(no hut)")}: " +
                                   $"{(inside ? "inside" : "NOT inside")} (within its walls {withinWalls}, a wall between them and the chest {wall}, " +
                                   $"{(hut != null ? Utils.DistanceXZ(c.transform.position, ai.RefugeChestForTest) : -1f):0.0} m from the chest)");
            }
            BuildCommand.Print($"vc_ui: {count} settlers indoors within {range:0} m, {outside} of them not inside");
        }

        private static readonly HarmonyLib.AccessTools.FieldRef<Minimap, System.Collections.Generic.List<Minimap.PinData>> s_pins =
            HarmonyLib.AccessTools.FieldRefAccess<Minimap, System.Collections.Generic.List<Minimap.PinData>>("m_pins");

        private static void LogPins()
        {
            if (Minimap.instance == null) return;
            var names = s_pins(Minimap.instance).Where(pin => !string.IsNullOrEmpty(pin.m_name)).Select(pin => $"{pin.m_name} ({pin.m_pos.x:0}, {pin.m_pos.z:0})").ToList();
            BuildCommand.Print($"vc_ui: {names.Count} named map pins: {string.Join(", ", names)}");
        }

        /// <summary>The hammer in hand with this building's piece chosen, as a player placing it (for the outline and its line).</summary>
        private static void PlaceMode(Player p, string word)
        {
            BuildingType type = DevHelpers.TypeByWord(word);
            GameObject piecePrefab = type != null ? ZNetScene.instance.GetPrefab(type.PrefabName) : null;
            GameObject hammer = ZNetScene.instance.GetPrefab("Hammer");
            if (piecePrefab == null || hammer == null)
            {
                BuildCommand.Print($"vc_ui: no building called '{word}'");
                return;
            }
            Inventory inventory = p.GetInventory();
            ItemDrop.ItemData item = inventory.GetItem("$item_hammer");
            if (item == null && inventory.AddItem(hammer, 1)) item = inventory.GetItem("$item_hammer");
            if (item == null)
            {
                BuildCommand.Print("vc_ui: no room for a hammer");
                return;
            }
            if (!p.IsItemEquiped(item)) p.EquipItem(item);
            bool chosen = p.SetSelectedPiece(piecePrefab.GetComponent<Piece>());
            BuildCommand.Print($"vc_ui: hammer in hand, {type.Id} {(chosen ? "chosen" : "not in the hammer's list (no Town Hall near, or not known yet)")}");
        }

        private static ColonyBuilding Nearest(Player p, string word, float range)
        {
            BuildingType type = string.IsNullOrEmpty(word) ? null : DevHelpers.TypeByWord(word);
            Vector3 me = p.transform.position;
            return ColonyBuilding.All
                .Where(b => b != null && b.IsValid && (type == null || b.TypeId == type.Id) && Vector3.Distance(b.transform.position, me) <= range)
                .OrderBy(b => Vector3.Distance(b.transform.position, me))
                .FirstOrDefault();
        }

        /// <summary>vc_ui feedback: the Town Hall's Feedback button, the Feedback window's buttons, and the ask's state (1.4.2).</summary>
        private static void Feedback(string what)
        {
            switch (what)
            {
                case "":
                    if (!Windows.TownHall.IsOpen)
                    {
                        BuildCommand.Print("vc_ui: open the Town Hall window first (vc_ui window townhall)");
                        return;
                    }
                    Windows.TownHall.PressFeedback();
                    Plugin.Instance.StartCoroutine(LogSoon());
                    return;
                case "discord":
                case "github":
                    if (!Windows.Feedback.IsOpen)
                    {
                        BuildCommand.Print("vc_ui: no Feedback window is open (vc_ui feedback, or vc_ui feedback due)");
                        return;
                    }
                    Windows.Feedback.Press(what == "discord", true);   // test: the address goes to the log; no browser
                    BuildCommand.Print($"vc_ui: pressed {what}; the window is {(Windows.Feedback.IsOpen ? "open" : "closed")}");
                    return;
                case "arm":
                case "due":
                    FeedbackAsker.ArmForTest(what == "due");
                    BuildCommand.Print($"vc_ui: feedback {what}: {FeedbackAsker.StateForTest()}");
                    return;
                case "state":
                    BuildCommand.Print($"vc_ui: feedback {FeedbackAsker.StateForTest()}; window " +
                                       (Windows.Feedback.IsOpen ? Windows.Feedback.Asking ? "open as the ask" : "open" : "closed"));
                    return;
                default:
                    BuildCommand.Print("vc_ui: feedback [discord|github|arm|due|state]");
                    return;
            }
        }

        private static IEnumerator LogSoon()
        {
            yield return new WaitForSecondsRealtime(1f);   // a refresh has filled it in
            LogWindows();
        }

        private static void LogWindows()
        {
            int open = 0;
            foreach (WindowBase window in new WindowBase[] { Windows.Hut, Windows.TownHall, Windows.Trees, Windows.Pick, Windows.Grow, Windows.Feedback })
            {
                if (!window.IsOpen) continue;
                open++;
                BuildCommand.Print($"vc_ui: {window.GetType().Name} shows: {window.TextForTest()}");
            }
            if (open == 0) BuildCommand.Print("vc_ui: no colony window is open");
        }

        private static void LogHovers(Player p, float range)
        {
            Vector3 me = p.transform.position;
            int count = 0;
            foreach (Citizen c in Citizen.All.Where(c => c != null && Vector3.Distance(c.transform.position, me) <= range))
            {
                string text = c.GetComponent<Character>()?.GetHoverText() ?? "";
                BuildCommand.Print($"vc_ui: hover {c.CitizenName}: {text.Replace("\n", " / ")}");
                count++;
            }
            foreach (SettlerCairn cairn in Object.FindObjectsByType<SettlerCairn>(FindObjectsSortMode.None)
                         .Where(k => k != null && Vector3.Distance(k.transform.position, me) <= range))
            {
                Hoverable hoverable = cairn.GetComponent<Hoverable>();
                BuildCommand.Print($"vc_ui: hover cairn: {(hoverable != null ? hoverable.GetHoverText().Replace("\n", " / ") : "?")}");
                count++;
            }
            if (count == 0) BuildCommand.Print($"vc_ui: nobody and no cairn within {range:0} m");
        }
    }

    /// <summary>vc_remove: takes the nearest colony building or world object (by prefab name) away, as the hammer or an axe would.</summary>
    internal sealed class RemoveCommand : ConsoleCommand
    {
        public override string Name => "vc_remove";
        public override string Help => "VikingColonies (test worlds): 'vc_remove townhall|builder|lumberjack|longhouse|forager' removes the nearest such " +
                                       "building (within 30 m); 'vc_remove <prefab> [metres]' removes the nearest world object of that prefab (e.g. Birch1)";
        public override bool IsCheat => true;

        public override void Run(string[] args)
        {
            if (DevHelpers.RefuseOutsideTestWorld(Name)) return;   // it changes the world (the whole-mod review of 1.4.1)
            Player p = Player.m_localPlayer;
            if (p == null || args == null || args.Length < 1)
            {
                BuildCommand.Print("vc_remove: say what");
                return;
            }
            float range = args.Length > 1 && float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float r) ? r : 30f;
            Vector3 me = p.transform.position;
            BuildingType type = DevHelpers.TypeByWord(args[0]);
            GameObject target = type != null
                ? ColonyBuilding.All.Where(b => b != null && b.IsValid && b.TypeId == type.Id && Vector3.Distance(b.transform.position, me) <= range)
                    .OrderBy(b => Vector3.Distance(b.transform.position, me)).Select(b => b.gameObject).FirstOrDefault()
                : Object.FindObjectsByType<ZNetView>(FindObjectsSortMode.None)
                    .Where(v => v != null && v.IsValid() && Utils.GetPrefabName(v.gameObject) == args[0] && Vector3.Distance(v.transform.position, me) <= range)
                    .OrderBy(v => Vector3.Distance(v.transform.position, me)).Select(v => v.gameObject).FirstOrDefault();
            if (target == null)
            {
                BuildCommand.Print($"vc_remove: no {args[0]} within {range:0} m");
                return;
            }
            Vector3 at = target.transform.position;
            target.GetComponent<ZNetView>().ClaimOwnership();
            ZNetScene.instance.Destroy(target);
            BuildCommand.Print($"vc_remove: removed the {args[0]} at ({at.x:0.0}, {at.z:0.0})");
        }
    }

    /// <summary>vc_kill: a Greydwarf's blow kills a settler (optionally carrying wood, so he leaves a cairn), as the self-test does.</summary>
    internal sealed class KillCommand : ConsoleCommand
    {
        public override string Name => "vc_kill";
        public override string Help => "VikingColonies (test worlds): 'vc_kill <settler|nearest> [wood]' has a Greydwarf kill that settler, carrying that much wood first";
        public override bool IsCheat => true;

        public override void Run(string[] args)
        {
            if (DevHelpers.RefuseOutsideTestWorld(Name)) return;   // it changes the world (the whole-mod review of 1.4.1)
            Player p = Player.m_localPlayer;
            if (p == null || args == null || args.Length < 1) return;
            Vector3 me = p.transform.position;
            Citizen victim = args[0] == "nearest"
                ? Citizen.All.Where(c => c != null).OrderBy(c => Vector3.Distance(c.transform.position, me)).FirstOrDefault()
                : Citizen.All.FirstOrDefault(c => c != null && string.Equals(c.CitizenName, args[0], System.StringComparison.OrdinalIgnoreCase));
            if (victim == null || !victim.IsOwner)
            {
                BuildCommand.Print($"vc_kill: no settler '{args[0]}' run by this computer");
                return;
            }
            int wood = args.Length > 1 && int.TryParse(args[1], out int w) ? w : 0;
            if (wood > 0)
            {
                victim.Carry.Add("Wood", wood);
                victim.SaveCarry();
            }
            GameObject prefab = ZNetScene.instance.GetPrefab("Greydwarf");
            Vector3 aside = victim.transform.position + Vector3.right * 15f;
            aside.y = ZoneSystem.instance.GetGroundHeight(aside);
            Character monster = prefab != null ? Object.Instantiate(prefab, aside, Quaternion.identity).GetComponent<Character>() : null;
            var kill = new HitData();
            kill.m_damage.m_slash = 10000f;
            kill.m_hitType = HitData.HitType.EnemyHit;
            kill.m_point = victim.transform.position;
            if (monster != null) kill.SetAttacker(monster);
            string name = victim.CitizenName;
            victim.Body.Damage(kill);
            BuildCommand.Print($"vc_kill: a Greydwarf killed {name}{(wood > 0 ? $" (carrying {wood} wood)" : "")}");
        }
    }

    /// <summary>vc_set Section.Key value: changes a VikingColonies setting at once, as the settings window does (test worlds).</summary>
    internal sealed class SetCommand : ConsoleCommand
    {
        public override string Name => "vc_set";
        public override string Help => "VikingColonies (test worlds): 'vc_set <Section.Key> <value>' changes a setting at once, e.g. 'vc_set Colony.SettlersEat false'; " +
                                       "'vc_set <Section.Key>' shows it";
        public override bool IsCheat => true;

        public override void Run(string[] args)
        {
            if (DevHelpers.RefuseOutsideTestWorld(Name)) return;   // it changes the world (the whole-mod review of 1.4.1)
            if (args == null || args.Length < 1 || !args[0].Contains("."))
            {
                BuildCommand.Print("vc_set: say which setting, e.g. 'vc_set Colony.SettlersEat false'");
                return;
            }
            int dot = args[0].IndexOf('.');
            string section = args[0].Substring(0, dot), key = args[0].Substring(dot + 1);
            ConfigFile config = Plugin.Instance.Config;
            ConfigDefinition definition = config.Keys.FirstOrDefault(k =>
                string.Equals(k.Section.Replace(" ", ""), section.Replace(" ", ""), System.StringComparison.OrdinalIgnoreCase) &&
                string.Equals(k.Key, key, System.StringComparison.OrdinalIgnoreCase));
            if (definition == null)
            {
                BuildCommand.Print($"vc_set: no setting {args[0]}");
                return;
            }
            ConfigEntryBase entry = config[definition];
            if (args.Length > 1)
            {
                string before = entry.GetSerializedValue();
                entry.SetSerializedValue(string.Join(" ", args.Skip(1)));
                BuildCommand.Print($"vc_set: {definition.Section}.{definition.Key} {before} -> {entry.GetSerializedValue()}");
            }
            else BuildCommand.Print($"vc_set: {definition.Section}.{definition.Key} = {entry.GetSerializedValue()}");
        }
    }
}

namespace VikingColonies.DevTools
{
    /// <summary>vc_ui shifte (issue #1): while it presses Shift+E, who asks the game for a text box, to the log.</summary>
    internal static class TextRequestWatch
    {
        internal static bool On;

        [HarmonyLib.HarmonyPatch]
        private static class RequestHook
        {
            [HarmonyLib.HarmonyPatch(typeof(TextInput), nameof(TextInput.RequestText)), HarmonyLib.HarmonyPrefix]
            private static void Asked(TextReceiver sign, string topic)
            {
                if (!On) return;
                var frames = new System.Diagnostics.StackTrace().GetFrames() ?? new System.Diagnostics.StackFrame[0];
                string from = string.Join(" < ", frames.Skip(1).Take(8).Select(f => f.GetMethod()).Where(m => m != null)
                    .Select(m => $"{m.DeclaringType?.Name}.{m.Name}").ToArray());
                Construction.BuildCommand.Print($"vc_ui: a text box was asked for ('{topic}', for {sign?.GetType().Name}): {from}");
            }
        }
    }
}
