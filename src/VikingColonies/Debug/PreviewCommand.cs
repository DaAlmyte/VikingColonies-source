using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using Jotunn.Entities;
using UnityEngine;
using VikingColonies.Construction;
using VikingColonies.Core;
using static VikingColonies.Construction.CoreVectors;
using Object = UnityEngine.Object;

namespace VikingColonies.DevTools
{
    /// <summary>
    /// vc_preview (the build tools, 2026-10-25; the player: "Can you start working on this?", on very large showcase builds,
    /// tools first): a design put up at once in the flat test world, photographed from set angles in clear noon light, then
    /// again with every piece in the Hammer's own support colour, its load written out piece by piece, and taken down
    /// again: a design's pictures and load map in about a minute instead of a 40-minute run. scripts/preview.py drives it
    /// and makes the sheet. Its pieces carry the gallery's mark (vc_world tidy and wipe take them). Flat test worlds only.
    /// </summary>
    internal sealed class PreviewCommand : ConsoleCommand
    {
        public override string Name => "vc_preview";
        public override string Help => "VikingColonies (the flat test world): 'vc_preview <name> [X Z] [keep]' puts BepInEx/VikingColonies/preview/<name>.blueprint " +
                                       "up at once round X Z (default -3600 -3600; the file's origin on the ground, its front (+z) to the south, in the noon sun), waits for its support to settle, " +
                                       "takes pictures from set angles (p-<name>-<view>) and again in the Hammer's support colours (p-<name>-load-<view>), writes " +
                                       "preview/<name>-load.tsv (each piece's support) and <name>-result.txt, then takes it down ('keep' leaves it up); " +
                                       "'vc_preview clear' takes down what was kept";
        public override bool IsCheat => true;

        public static string Folder => Path.Combine(Paths.BepInExRootPath, "VikingColonies", "preview");

        private const float DefaultX = -3600f, DefaultZ = -3600f;   // open meadow, clear of the gallery, the showcase and old tests
        private const int PlacedPerFrame = 150;
        /// <summary>A design's front (+z) faces south, where the sun stands at noon (the first great hall's front was in shadow).</summary>
        private static readonly Quaternion Turn = Quaternion.Euler(0f, 180f, 0f);
        private static readonly FieldInfo VisualField = AccessTools.Field(typeof(Character), "m_visual");
        private static readonly MethodInfo ColourValueMethod = AccessTools.Method(typeof(WearNTear), "GetSupportColorValue");

        private static bool s_running;
        /// <summary>The world the running preview started in: it removes nothing once the game is in another.</summary>
        private static string s_world = "";
        private static ZNet s_net;
        /// <summary>The running preview's pieces and where, for taking them down if it fails.</summary>
        private static readonly List<GameObject> s_placed = new List<GameObject>();
        private static Vector3 s_origin;
        private static float s_radius;
        /// <summary>The preview's own mark on its pieces (beside the gallery's, so vc_world tidy takes them too): what an
        /// earlier preview left is cleared by it, never the gallery's chests or stamps.</summary>
        private const string PreviewKey = "vc_preview";
        private static readonly List<List<GameObject>> s_kept = new List<List<GameObject>>();

        public override void Run(string[] args)
        {
            args = args ?? new string[0];
            string world = ZNet.instance != null ? ZNet.instance.GetWorldName() : "";
            if (!TestWorlds.IsFlatTestWorld(world))
            {
                Refuse($"only in the flat test world (its name starts with {TestWorlds.FlatTestWorldName}); this is {world}");
                return;
            }
            if (args.Length == 0)
            {
                Refuse("'vc_preview <name> [X Z] [keep]' or 'vc_preview clear'");
                return;
            }
            if (s_running)
            {
                Refuse("a preview is still running");
                return;
            }
            if (args[0].ToLowerInvariant() == "clear")
            {
                BuildCommand.Print($"vc_preview: took down {TakeDownKept()} kept pieces");
                return;
            }
            string name = args[0];
            if (!PreviewName.IsSafe(name))
            {
                Refuse($"'{name}' isn't a plain name (letters, digits, _ and - only)");
                return;
            }
            string file = Path.Combine(Folder, name + ".blueprint");
            if (!File.Exists(file))
            {
                Refuse($"no design at {file}");
                return;
            }
            float x = DefaultX, z = DefaultZ;
            if (args.Length >= 3 && (!Number(args[1], out x) || !Number(args[2], out z)))
            {
                Refuse("X and Z are numbers");
                return;
            }
            bool keep = args.Any(a => a.ToLowerInvariant() == "keep");
            BlueprintFile design;
            try { design = BlueprintFile.Parse(File.ReadAllLines(file)); }
            catch (Exception e)
            {
                Refuse($"couldn't read {file}: {e.Message}");
                return;
            }
            if (Player.m_localPlayer == null || design.Pieces.Count == 0)
            {
                Refuse(Player.m_localPlayer == null ? "no player in the world" : $"{name} has no pieces");
                return;
            }
            s_running = true;
            s_world = world;
            s_net = ZNet.instance;
            s_placed.Clear();
            Plugin.Instance.StartCoroutine(Guarded(name, Preview(name, design, new Vector3(x, 0f, z), keep)));
        }

        private static void Refuse(string why) => BuildCommand.Print("vc_preview: refused: " + why);

        private static bool Number(string word, out float value) =>
            float.TryParse(word, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

        private static IEnumerator Preview(string name, BlueprintFile design, Vector3 origin, bool keep)
        {
            float started = Time.realtimeSinceStartup;
            Player p = Player.m_localPlayer;
            GameObject visual = p != null ? VisualField?.GetValue(p) as GameObject : null;
            List<GameObject> placed = s_placed;
            var shots = new List<KeyValuePair<string, string>>();
            bool cameraOn = false;
            if (p != null && !p.InGodMode()) p.SetGodMode(true);   // never off: the test character has 1 health
            try
            {
                // 1. Go there and wait for its ground (the plan's pivots tell how far it reaches before it stands).
                List<PlannedPiece> rough = BlueprintPlan.PlaceAround(design, V(origin), Q(Turn));
                float reach = 4f;
                foreach (PlannedPiece piece in rough) reach = Mathf.Max(reach, new Vector2(piece.Position.X - origin.x, piece.Position.Z - origin.z).magnitude + 2f);
                Vector3 stand = origin + new Vector3(reach + 12f, 0f, 0f);
                stand.y = ZoneSystem.instance.GetGroundHeight(stand) + 0.5f;
                for (int tries = 0; p != null && Vector3.Distance(p.transform.position, stand) > 8f && !p.TeleportTo(stand, Quaternion.LookRotation(Vector3.left), true) && tries < 20; tries++)
                    yield return new WaitForSecondsRealtime(0.5f);
                for (float waited = 0f; waited < 60f && (p == null || p.IsTeleporting() || !Ready(origin, reach)); waited += 0.5f)
                    yield return new WaitForSecondsRealtime(0.5f);
                if (!Ready(origin, reach))
                {
                    BuildCommand.Print($"vc_preview: done {name}: failed, the ground round ({origin.x:0}, {origin.z:0}) didn't load within a minute");
                    yield break;
                }
                origin.y = ZoneSystem.instance.GetGroundHeight(origin);
                s_origin = origin;
                s_radius = reach + 10f;
                List<PlannedPiece> plan = BlueprintPlan.PlaceAround(design, V(origin), Q(Turn));
                if (!SameWorld())
                {
                    BuildCommand.Print($"vc_preview: done {name}: failed, the game left {s_world}");
                    yield break;
                }
                int cleared = ClearLeftovers(origin, reach + 10f);
                Console("env Clear");
                Console("tod 0.5");
                BuildCommand.Print($"vc_preview: {name}: putting up {plan.Count} pieces round ({origin.x:0}, {origin.z:0}) ({cleared} left from before taken away)");

                // 2. Every piece at once, a few at a frame; no placing sound or dust (thousands at once).
                var byIndex = new GameObject[plan.Count];
                var unknown = new HashSet<string>();
                int unknownPieces = 0;
                foreach (PlannedPiece piece in plan)
                {
                    GameObject prefab = PiecePlacer.Prefab(piece.Prefab);
                    if (prefab == null)
                    {
                        unknown.Add(piece.Prefab);
                        unknownPieces++;
                        continue;
                    }
                    GameObject go = Lay(prefab, piece);
                    byIndex[piece.Index] = go;
                    placed.Add(go);
                    if (placed.Count % PlacedPerFrame == 0) yield return null;
                }
                foreach (string missing in unknown) BuildCommand.Print($"vc_preview: the game has no piece called {missing}");

                // 3. Until the game's support settles: pieces fall as it works it out.
                var settle = new PreviewSettle();
                float since = Time.realtimeSinceStartup, said = since;
                while (!settle.Settled)
                {
                    yield return new WaitForSecondsRealtime(1f);
                    Measure(byIndex, out int standingNow, out float sum);
                    settle.Add(Time.realtimeSinceStartup - since, standingNow, sum);
                    if (Time.realtimeSinceStartup - said >= 15f)
                    {
                        said = Time.realtimeSinceStartup;
                        BuildCommand.Print($"vc_preview: {name}: {standingNow} of {plan.Count} stand, support still settling");
                    }
                }
                Measure(byIndex, out int standing, out _);

                // 4. The pictures: the design's box from the pieces that stand, the camera fitted to it.
                Bounds box = BoxOf(byIndex, origin);
                Camera camera = GameCamera.instance != null ? GameCamera.instance.GetComponent<Camera>() : null;
                float fov = camera != null ? camera.fieldOfView : 65f;
                float aspect = Screen.height > 0 ? (float)Screen.width / Screen.height : 16f / 9f;
                Local(box, origin, out Vector3 min, out Vector3 max);
                BlueprintPiece chest = design.Pieces.FirstOrDefault(p => BlueprintPlan.IsChest(p.Name));
                List<PreviewView> views = PreviewViews.For(V(min), V(max), fov, aspect, chest?.Position);
                ShotCommand.KeepAtLeast(120);
                if (visual != null) visual.SetActive(false);
                foreach (PreviewView view in views)
                {
                    CamCommand.LookFrom(origin + Turn * U(view.Eye), origin + Turn * U(view.Look));
                    cameraOn = true;
                    yield return new WaitForSecondsRealtime(1.2f);   // the far pieces' detail and the shadows catch up
                    shots.Add(new KeyValuePair<string, string>(view.Name, ShotCommand.Save($"p-{name}-{view.Name}")));
                    yield return new WaitForSecondsRealtime(0.3f);
                }
                var wears = byIndex.Where(go => go != null).Select(go => go.GetComponent<WearNTear>()).Where(w => w != null).ToList();
                foreach (PreviewView view in views.Where(v => PreviewViews.LoadViews.Contains(v.Name)))
                {
                    CamCommand.LookFrom(origin + Turn * U(view.Eye), origin + Turn * U(view.Look));
                    for (float t = 0f; t < 1.2f; t += Time.unscaledDeltaTime)
                    {
                        Highlight(wears);
                        yield return null;
                    }
                    Highlight(wears);
                    shots.Add(new KeyValuePair<string, string>("load-" + view.Name, ShotCommand.Save($"p-{name}-load-{view.Name}")));
                    for (int frame = 0; frame < 3; frame++)
                    {
                        yield return null;
                        Highlight(wears);
                    }
                }
                CamCommand.Off();
                cameraOn = false;

                // 5. The load file and the result.
                string loadFile = Path.Combine(Folder, name + "-load.tsv");
                WriteLoad(loadFile, plan, byIndex, origin);
                Vector3 size = max - min;
                string fell = (plan.Count - unknownPieces > standing ? $", {plan.Count - unknownPieces - standing} fell" : "") +
                              (unknownPieces > 0 ? $", {unknownPieces} unknown to the game" : "");
                string result = string.Format(CultureInfo.InvariantCulture,
                    "name {0}\npieces {1}\nstanding {2}\nsize {3:0.#} {4:0.#} {5:0.#}\nat {6:0.#} {7:0.#}\nsettled {8}\nseconds {9:0}\nload {10}\n{11}",
                    name, plan.Count, standing, size.x, size.y, size.z, origin.x, origin.z, settle.GaveUp ? "no" : "yes",
                    Time.realtimeSinceStartup - started, loadFile, string.Join("\n", shots.Where(s => s.Value != null).Select(s => $"shot {s.Key} {s.Value}")));
                File.WriteAllText(Path.Combine(Folder, name + "-result.txt"), result + "\n");
                if (keep) s_kept.Add(placed.Where(go => go != null).ToList());
                else if (SameWorld()) TakeDown(placed, origin, reach + 10f);
                placed.Clear();
                BuildCommand.Print(string.Format(CultureInfo.InvariantCulture,
                    "vc_preview: done {0}: {1} of {2} pieces stand{3}{4}; {5:0.#} x {6:0.#} x {7:0.#} m (wide, high, long); {8} pictures in {9:0} s; {10}",
                    name, standing, plan.Count, fell, settle.GaveUp ? "; support still changing after two minutes" : "",
                    size.x, size.y, size.z, shots.Count(s => s.Value != null), Time.realtimeSinceStartup - started, keep ? "left up (vc_preview clear takes it down)" : "taken down"));
            }
            finally
            {
                if (cameraOn) CamCommand.Off();
                if (visual != null) visual.SetActive(true);
                if (SameWorld())
                {
                    Console("resetenv");   // the game's own weather and time back (a later run in this game expects them)
                    Console("tod -1");
                }
                s_running = false;
            }
        }

        /// <summary>
        /// Runs a preview, and if it throws says so on the line the runner waits for ("vc_preview: done ...: failed") and
        /// takes its pieces down, rather than leave the run waiting ten minutes and the build standing (the review of
        /// 25 Oct). The preview's own finally gives the camera and the player's body back either way.
        /// </summary>
        private static IEnumerator Guarded(string name, IEnumerator preview)
        {
            while (true)
            {
                object current;
                try
                {
                    if (!preview.MoveNext()) yield break;
                    current = preview.Current;
                }
                catch (Exception e)
                {
                    BuildCommand.Print($"vc_preview: done {name}: failed: {e.GetType().Name}: {e.Message}");
                    if (SameWorld()) TakeDown(s_placed, s_origin, s_radius);
                    s_placed.Clear();
                    s_running = false;
                    yield break;
                }
                yield return current;
            }
        }

        /// <summary>Still the world (and the game session) the preview started in.</summary>
        private static bool SameWorld() =>
            ZNet.instance != null && ZNet.instance == s_net && ZNet.instance.GetWorldName() == s_world && TestWorlds.IsFlatTestWorld(s_world);

        private static bool Ready(Vector3 origin, float reach)
        {
            if (ZNetScene.instance == null || ZoneSystem.instance == null) return false;
            foreach (Vector3 at in new[] { origin, origin + new Vector3(reach, 0f, 0f), origin - new Vector3(reach, 0f, 0f), origin + new Vector3(0f, 0f, reach), origin - new Vector3(0f, 0f, reach) })
                if (!ZNetScene.instance.IsAreaReady(at) || Heightmap.FindHeightmap(at) == null) return false;
            return true;
        }

        private static void Console(string line)
        {
            try { global::Console.instance.TryRunCommand(line, false, true); }
            catch (Exception e) { BuildCommand.Print($"vc_preview: '{line}' failed: {e.Message}"); }
        }

        /// <summary>A preview piece: no creator and no hut, the gallery's mark; a fire lit (full of fuel).</summary>
        private static GameObject Lay(GameObject prefab, PlannedPiece piece)
        {
            GameObject go = Object.Instantiate(prefab, U(piece.Position), U(piece.Rotation));
            BuildCommand.MarkCostChest(go);
            ZNetView mark = go.GetComponent<ZNetView>();
            if (mark != null && mark.IsValid()) mark.GetZDO().Set(PreviewKey, true);
            WearNTear wear = go.GetComponent<WearNTear>();
            if (wear != null) wear.OnPlaced();
            Fireplace fire = go.GetComponent<Fireplace>();
            ZNetView view = go.GetComponent<ZNetView>();
            if (fire != null && !fire.m_infiniteFuel && view != null && view.IsValid()) view.GetZDO().Set(ZDOVars.s_fuel, fire.m_maxFuel);
            return go;
        }

        private static void Measure(GameObject[] byIndex, out int standing, out float sum)
        {
            standing = 0;
            sum = 0f;
            foreach (GameObject go in byIndex)
            {
                if (go == null) continue;
                standing++;
                WearNTear wear = go.GetComponent<WearNTear>();
                if (wear != null) sum += SupportProbe.SupportOf(wear);
            }
        }

        /// <summary>The box round the standing pieces' meshes (not a fire's flames or smoke); the plan's spot when none stand.</summary>
        private static Bounds BoxOf(GameObject[] byIndex, Vector3 origin)
        {
            bool any = false;
            var box = new Bounds(origin + Vector3.up, Vector3.one * 2f);
            foreach (GameObject go in byIndex)
            {
                if (go == null) continue;
                foreach (MeshRenderer r in go.GetComponentsInChildren<MeshRenderer>())
                {
                    if (!r.enabled || r.bounds.size.sqrMagnitude > 10000f) continue;   // a stray huge box (an effect) left out
                    if (any) box.Encapsulate(r.bounds);
                    else box = r.bounds;
                    any = true;
                }
            }
            return box;
        }

        /// <summary>The box round the pieces in the design's own space (x right, y up, z front), from the world's box.</summary>
        private static void Local(Bounds box, Vector3 origin, out Vector3 min, out Vector3 max)
        {
            min = Vector3.one * float.MaxValue;
            max = Vector3.one * float.MinValue;
            Quaternion undo = Quaternion.Inverse(Turn);
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? box.min.x : box.max.x, (i & 2) == 0 ? box.min.y : box.max.y, (i & 4) == 0 ? box.min.z : box.max.z);
                Vector3 local = undo * (corner - origin);
                min = Vector3.Min(min, local);
                max = Vector3.Max(max, local);
            }
        }

        private static void Highlight(List<WearNTear> wears)
        {
            foreach (WearNTear w in wears)
                if (w != null) w.Highlight();
        }

        private static void WriteLoad(string path, List<PlannedPiece> plan, GameObject[] byIndex, Vector3 origin)
        {
            var lines = new List<string> { PreviewLoad.Header };
            foreach (PlannedPiece piece in plan)
            {
                GameObject go = byIndex[piece.Index];
                Vec3 at = V(Quaternion.Inverse(Turn) * (U(piece.Position) - origin));
                WearNTear wear = go != null ? go.GetComponent<WearNTear>() : null;
                if (wear == null)
                {
                    lines.Add(PreviewLoad.Line(piece.Index, piece.Prefab, at, go != null, null, null, null, null));
                    continue;
                }
                SupportMaterial m = SupportProbe.MaterialOf(wear);
                float? colour = null;
                try { colour = ColourValueMethod != null ? (float?)(float)ColourValueMethod.Invoke(wear, null) : null; }
                catch (Exception) { colour = null; }
                lines.Add(PreviewLoad.Line(piece.Index, piece.Prefab, at, true, SupportProbe.SupportOf(wear), m.MinSupport, m.MaxSupport, colour));
            }
            File.WriteAllLines(path, lines);
        }

        /// <summary>Takes the preview's pieces down, and the items dropped by those that fell.</summary>
        private static void TakeDown(List<GameObject> placed, Vector3 origin, float radius)
        {
            foreach (GameObject go in placed)
                if (go != null) Remove(go);
            foreach (ItemDrop drop in Object.FindObjectsByType<ItemDrop>(FindObjectsSortMode.None))
                if (drop != null && Vector3.Distance(drop.transform.position, origin) <= radius) Remove(drop.gameObject);
        }

        private static int TakeDownKept()
        {
            int n = 0;
            foreach (List<GameObject> build in s_kept)
                foreach (GameObject go in build)
                    if (go != null && Remove(go)) n++;
            s_kept.Clear();
            return n;
        }

        /// <summary>The pieces an earlier preview here left (kept, or a game closed in the middle): its own mark only.</summary>
        private static int ClearLeftovers(Vector3 origin, float radius)
        {
            var near = new List<Piece>();
            Piece.GetAllPiecesInRadius(origin, radius, near);
            int n = 0;
            foreach (Piece piece in near.Where(IsPreviewPiece).ToList())
                if (Remove(piece.gameObject)) n++;
            return n;
        }

        private static bool IsPreviewPiece(Piece piece)
        {
            ZNetView view = piece != null ? piece.GetComponent<ZNetView>() : null;
            return view != null && view.IsValid() && view.GetZDO().GetBool(PreviewKey);
        }

        private static bool Remove(GameObject go)
        {
            ZNetView view = go.GetComponent<ZNetView>();
            if (view == null || !view.IsValid()) return false;
            if (!view.IsOwner()) view.ClaimOwnership();
            ZNetScene.instance.Destroy(go);
            return true;
        }
    }
}
