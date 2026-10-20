using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Jotunn.Entities;
using UnityEngine;
using VikingColonies.Construction;
using VikingColonies.Core;

namespace VikingColonies.DevTools
{
    /// <summary>
    /// vc_piecedump (hut redesign, 2026-10-18): every Hammer piece as the game has it — its collider boxes and overall
    /// size, centre of mass, support numbers, snap points, crafting station, cost and comfort, the box round what can be
    /// seen of it, where its lights sit and where an item stand holds its item (which way a wall light faces) — written as
    /// BepInEx/VikingColonies/pieces.json for the hut design tools (tools/blueprints, which read it as pieces.json).
    /// Only reads prefabs: nothing is placed.
    /// </summary>
    internal sealed class PieceDumpCommand : ConsoleCommand
    {
        public override string Name => "vc_piecedump";
        public override string Help => "VikingColonies (design tools): 'vc_piecedump' writes every Hammer piece's shape, support, snap points and cost to BepInEx/VikingColonies/pieces.json";
        public override bool IsCheat => false;

        public static string FilePath => Path.Combine(Path.Combine(BepInEx.Paths.BepInExRootPath, "VikingColonies"), "pieces.json");

        public override void Run(string[] args)
        {
            GameObject hammer = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab("Hammer") : null;
            PieceTable table = hammer != null ? hammer.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces : null;
            if (table == null)
            {
                BuildCommand.Print("vc_piecedump: no Hammer");
                return;
            }
            var entries = new List<string>();
            int failed = 0;
            foreach (GameObject prefab in table.m_pieces.Where(p => p != null))
            {
                try { entries.Add(Entry(prefab)); }
                catch (Exception e)
                {
                    failed++;
                    Log.Warn($"vc_piecedump: {prefab.name}: {e.Message}");
                }
            }
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllText(FilePath, "[\n" + string.Join(",\n", entries) + "\n]\n", new UTF8Encoding(false));
            BuildCommand.Print($"vc_piecedump: {entries.Count} pieces written to {FilePath}" + (failed > 0 ? $" ({failed} failed, see the log)" : ""));
            int items = WriteItems(table);
            BuildCommand.Print($"vc_piecedump: {items} items (recipes, traders) written to {ItemsPath}");
        }

        public static string ItemsPath => Path.Combine(Path.GetDirectoryName(FilePath), "items.json");

        /// <summary>
        /// items.json (hut redesign, 2026-10-18): every item a Hammer piece costs, and every item their recipes need in turn,
        /// with its recipe (station, level, what it takes, how many it makes) and the traders who sell it (price and the
        /// world key that unlocks it), and every trader's whole stock. The hut tools use it to keep traders' items out of the
        /// designs (the player: "I don't think they should include items from the traders") and to weigh what a level costs.
        /// </summary>
        private static int WriteItems(PieceTable table)
        {
            var sold = new Dictionary<string, List<string>>();
            var traders = new List<string>();
            foreach (GameObject prefab in ZNetScene.instance != null ? ZNetScene.instance.m_prefabs : new List<GameObject>())
            {
                Trader trader = prefab != null ? prefab.GetComponent<Trader>() : null;
                if (trader == null || trader.m_items == null) continue;
                var stock = new List<string>();
                foreach (Trader.TradeItem t in trader.m_items.Where(t => t != null && t.m_prefab != null))
                {
                    string item = t.m_prefab.name;
                    string line = "{" + Pair("trader", Str(prefab.name)) + ", " + Pair("price", t.m_price.ToString(CultureInfo.InvariantCulture)) + ", " +
                                  Pair("stack", t.m_stack.ToString(CultureInfo.InvariantCulture)) + ", " + Pair("key", Str(t.m_requiredGlobalKey ?? "")) + "}";
                    if (!sold.TryGetValue(item, out List<string> by)) sold[item] = by = new List<string>();
                    by.Add(line);
                    stock.Add(Str(item));
                }
                traders.Add("    " + Str(prefab.name) + ": [" + string.Join(", ", stock) + "]");
            }
            var recipes = new Dictionary<string, Recipe>();
            foreach (Recipe r in ObjectDB.instance.m_recipes.Where(r => r != null && r.m_item != null && r.m_enabled))
                if (!recipes.ContainsKey(r.m_item.name)) recipes[r.m_item.name] = r;
            var wanted = new SortedSet<string>(StringComparer.Ordinal);
            var queue = new Queue<string>();
            foreach (GameObject prefab in table.m_pieces.Where(p => p != null))
            {
                Piece piece = prefab.GetComponent<Piece>();
                foreach (Piece.Requirement r in piece != null && piece.m_resources != null ? piece.m_resources : new Piece.Requirement[0])
                    if (r.m_resItem != null && wanted.Add(r.m_resItem.name)) queue.Enqueue(r.m_resItem.name);
            }
            while (queue.Count > 0)
                if (recipes.TryGetValue(queue.Dequeue(), out Recipe r))
                    foreach (Piece.Requirement need in r.m_resources ?? new Piece.Requirement[0])
                        if (need.m_resItem != null && wanted.Add(need.m_resItem.name)) queue.Enqueue(need.m_resItem.name);
            var lines = new List<string>();
            foreach (string item in wanted)
            {
                var fields = new List<string> { Pair("item", Str(item)) };
                GameObject itemPrefab = ObjectDB.instance.GetItemPrefab(item);
                ItemDrop drop = itemPrefab != null ? itemPrefab.GetComponent<ItemDrop>() : null;
                if (drop != null && Localization.instance != null)
                    fields.Add(Pair("english", Str(Localization.instance.Localize(drop.m_itemData.m_shared.m_name))));
                if (recipes.TryGetValue(item, out Recipe recipe))
                    fields.Add(Pair("recipe", "{" + Pair("station", Str(recipe.m_craftingStation != null ? recipe.m_craftingStation.name : "")) + ", " +
                        Pair("level", recipe.m_minStationLevel.ToString(CultureInfo.InvariantCulture)) + ", " +
                        Pair("makes", recipe.m_amount.ToString(CultureInfo.InvariantCulture)) + ", " +
                        Pair("needs", "[" + string.Join(", ", (recipe.m_resources ?? new Piece.Requirement[0]).Where(n => n.m_resItem != null)
                            .Select(n => "{" + Pair("item", Str(n.m_resItem.name)) + ", " + Pair("amount", n.m_amount.ToString(CultureInfo.InvariantCulture)) + "}")) + "]") + "}"));
                if (sold.TryGetValue(item, out List<string> by)) fields.Add(Pair("sold", "[" + string.Join(", ", by) + "]"));
                lines.Add("    {" + string.Join(", ", fields) + "}");
            }
            File.WriteAllText(ItemsPath, "{\n  \"items\": [\n" + string.Join(",\n", lines) + "\n  ],\n  \"traders\": {\n" + string.Join(",\n", traders) + "\n  }\n}\n",
                new UTF8Encoding(false));
            return lines.Count;
        }

        private static string Entry(GameObject prefab)
        {
            Piece piece = prefab.GetComponent<Piece>();
            WearNTear wear = prefab.GetComponent<WearNTear>();
            PieceShape shape = PieceShape.Of(prefab);
            List<PieceShape.WorldBox> boxes = shape.ColliderBoxes().ToList();
            Bounds b = Around(boxes);   // the whole piece's solid parts (2026-10-18: the artisan table's 200 m area box made
                                        // it 200 m tall, and a design put it 100 m up)
            var fields = new List<string>
            {
                Pair("name", Str(prefab.name)),
                Pair("english", Str(piece != null && Localization.instance != null ? Localization.instance.Localize(piece.m_name) : "")),
                Pair("boxes", "[" + string.Join(", ", boxes
                    .Select(x => "{" + Pair("center", Vec(x.Center)) + ", " + Pair("half", Vec(x.Half)) + ", " + Pair("rot", Rot(x.Rotation)) +
                                 (x.Mesh ? ", " + Pair("mesh", "true") : "") + ", " + Pair("layer", Str(LayerMask.LayerToName(x.Layer))) + "}")) + "]"),
                Pair("snaps", "[" + string.Join(", ", SnapPoints(prefab.transform)) + "]"),
                Pair("station", Str(piece != null && piece.m_craftingStation != null ? piece.m_craftingStation.name : "")),
                Pair("costs", "[" + string.Join(", ", (piece != null && piece.m_resources != null ? piece.m_resources : new Piece.Requirement[0])
                    .Where(r => r.m_resItem != null)
                    .Select(r => "{" + Pair("item", Str(r.m_resItem.name)) + ", " + Pair("amount", r.m_amount.ToString(CultureInfo.InvariantCulture)) + "}")) + "]"),
                Pair("comfort", (piece != null ? piece.m_comfort : 0).ToString(CultureInfo.InvariantCulture)),
                Pair("comfortGroup", Str(piece != null ? piece.m_comfortGroup.ToString() : "")),
                Pair("kind", Str(Kind(prefab))),
            };
            if (boxes.Count > 0)   // a piece with no solid collider has no size (the tools then guess one)
            {
                fields.Add(Pair("lo", Vec(b.min)));
                fields.Add(Pair("hi", Vec(b.max)));
            }
            if (Visual(prefab, out Bounds seen))
            {
                fields.Add(Pair("seenLo", Vec(seen.min)));
                fields.Add(Pair("seenHi", Vec(seen.max)));
            }
            // A light's place, burning or not (2026-10-18: wall lanterns and sconces in the generated huts faced along their
            // walls; which way a wall light faces is where its light sits).
            List<string> lights = prefab.GetComponentsInChildren<Light>(true)
                .Select(l => Vec(prefab.transform.InverseTransformPoint(l.transform.position))).ToList();
            if (lights.Count > 0) fields.Add(Pair("lights", "[" + string.Join(", ", lights) + "]"));
            ItemStand stand = prefab.GetComponent<ItemStand>();
            if (stand != null && stand.m_attachOther != null)
                fields.Add(Pair("attach", Vec(prefab.transform.InverseTransformPoint(stand.m_attachOther.position))));
            if (wear != null)
            {
                SupportMaterial m = SupportProbe.MaterialOf(wear);
                fields.Add(Pair("material", Str(wear.m_materialType.ToString())));
                fields.Add(Pair("com", Vec(wear.m_comOffset)));
                fields.Add(Pair("supports", wear.m_supports ? "true" : "false"));
                fields.Add(Pair("support", "{" + Pair("max", F(m.MaxSupport)) + ", " + Pair("min", F(m.MinSupport)) + ", " +
                                           Pair("vertical", F(m.VerticalLoss)) + ", " + Pair("horizontal", F(m.HorizontalLoss)) + "}"));
            }
            return "  {" + string.Join(", ", fields) + "}";
        }

        private static Bounds Around(List<PieceShape.WorldBox> boxes)
        {
            var bounds = default(Bounds);
            bool any = false;
            foreach (PieceShape.WorldBox box in boxes)
            {
                for (int i = 0; i < 8; i++)
                {
                    var corner = box.Center + box.Rotation * Vector3.Scale(box.Half, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    if (!any) bounds = new Bounds(corner, Vector3.zero);
                    else bounds.Encapsulate(corner);
                    any = true;
                }
            }
            return bounds;
        }

        /// <summary>The box round the piece's meshes as it stands whole (not its worn and broken models), in its own space.</summary>
        private static bool Visual(GameObject prefab, out Bounds bounds)
        {
            bounds = default;
            bool any = false;
            Transform root = prefab.transform;
            foreach (MeshFilter mesh in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                MeshRenderer renderer = mesh.GetComponent<MeshRenderer>();
                if (mesh.sharedMesh == null || renderer == null || !renderer.enabled || !PieceShape.ActiveUpTo(mesh.transform, root)) continue;
                Bounds m = mesh.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = m.center + Vector3.Scale(m.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 p = root.InverseTransformPoint(mesh.transform.TransformPoint(corner));
                    if (!any) bounds = new Bounds(p, Vector3.zero);
                    else bounds.Encapsulate(p);
                    any = true;
                }
            }
            return any;
        }

        /// <summary>The root's direct children tagged as snap points, as the game's Piece.GetSnapPoints finds them.</summary>
        private static IEnumerable<string> SnapPoints(Transform root)
        {
            for (int i = 0; i < root.childCount; i++)
            {
                Transform t = root.GetChild(i);
                if (t.CompareTag("snappoint")) yield return Vec(root.InverseTransformPoint(t.position));
            }
        }

        private static string Kind(GameObject prefab)
        {
            if (prefab.GetComponent<Door>() != null) return "door";
            if (prefab.GetComponent<Bed>() != null) return "bed";
            if (prefab.GetComponent<Fireplace>() != null) return "fire";
            if (prefab.GetComponent<Container>() != null) return "container";
            if (prefab.GetComponent<ItemStand>() != null) return "itemstand";
            if (prefab.GetComponent<CraftingStation>() != null) return "station";
            if (prefab.GetComponent<StationExtension>() != null) return "extension";
            if (prefab.GetComponent<Chair>() != null) return "seat";
            return "";
        }

        private static string Pair(string key, string value) => "\"" + key + "\": " + value;
        private static string F(float v) =>
            float.IsNaN(v) || float.IsInfinity(v) ? "null" : v.ToString("0.#####", CultureInfo.InvariantCulture);   // JSON has no NaN
        private static string Vec(Vector3 v) => "[" + F(v.x) + ", " + F(v.y) + ", " + F(v.z) + "]";
        private static string Rot(Quaternion q) => "[" + F(q.x) + ", " + F(q.y) + ", " + F(q.z) + ", " + F(q.w) + "]";

        private static string Str(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in s ?? "")
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }
    }
}
