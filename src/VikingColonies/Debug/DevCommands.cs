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
using VikingColonies.Gui;

namespace VikingColonies.DevTools
{
    /// <summary>vc_crops: what the Cultivator can place, and which of it the Farm's "What to grow" list takes (and why not).</summary>
    internal sealed class CropsCommand : ConsoleCommand
    {
        public override string Name => "vc_crops";
        public override string Help => "VikingColonies: list the Cultivator's pieces and which the Farm's list takes as crops (into the log).";
        public override bool IsCheat => true;

        public override void Run(string[] args)
        {
            GameObject cultivator = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab("Cultivator") : null;
            PieceTable table = cultivator != null ? cultivator.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces : null;
            if (table == null)
            {
                BuildCommand.Print("vc_crops: no Cultivator");
                return;
            }
            var crops = new HashSet<string>(Jobs.CropList.Crops.Select(c => c.Piece));
            foreach (GameObject piece in table.m_pieces.Where(p => p != null))
            {
                Plant plant = piece.GetComponent<Plant>();
                Pickable pickable = piece.GetComponent<Pickable>();
                GameObject grown = plant != null && plant.m_grownPrefabs != null ? plant.m_grownPrefabs.FirstOrDefault(g => g != null) : null;
                Pickable grownPick = grown != null ? grown.GetComponent<Pickable>() : null;
                Piece p = piece.GetComponent<Piece>();
                string cost = p != null && p.m_resources != null ? string.Join("+", p.m_resources.Where(r => r.m_resItem != null).Select(r => r.m_amount + " " + r.m_resItem.name)) : "";
                Log.Event($"vc_crops: {piece.name}: {(crops.Contains(piece.name) ? "CROP" : "not a crop")}; plant {plant != null} (grows into {(grown != null ? grown.name : "-")}, " +
                          $"pickable {grownPick != null}{(grownPick != null && grownPick.m_itemPrefab != null ? " " + grownPick.m_itemPrefab.name : "")}); " +
                          $"itself pickable {pickable != null}{(pickable != null && pickable.m_itemPrefab != null ? " " + pickable.m_itemPrefab.name + (pickable.m_respawnTimeMinutes > 0f ? " regrows" : "") : "")}; costs {cost}");
            }
            BuildCommand.Print($"vc_crops: {table.m_pieces.Count} Cultivator pieces, {crops.Count} crops (see the log)");
        }
    }

    /// <summary>
    /// vc_bugreport (0.27): one file a player attaches to a bug report — versions, other mods, the start-up check, the
    /// colonies loaded in his game (buildings, orders, settlers with their recent states and troubles), and the mod's last
    /// warnings and errors. Not a cheat: it only reads, and writes BepInEx/VikingColonies-bugreport.txt. Each part is
    /// written on its own: one that fails (a game update) leaves a note and the rest is still written (review of 0.27).
    /// </summary>
    internal sealed class BugReportCommand : ConsoleCommand
    {
        public override string Name => "vc_bugreport";
        public override string Help => "VikingColonies: 'vc_bugreport' writes BepInEx/VikingColonies-bugreport.txt to attach to a bug report (with BepInEx/LogOutput.log); " +
                                       "'vc_bugreport form' also opens the bug form with it filled in, as the Town Hall's Report a problem button does";
        public override bool IsCheat => false;

        public static string FilePath => System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "VikingColonies-bugreport.txt");

        /// <summary>The report as written to the file: its text, its opening lines (for the bug form) and what it found.</summary>
        internal sealed class Report
        {
            public string Text = "";
            public string Header = "";
            public string Loaded = "nothing loaded";
            /// <summary>Why the file couldn't be written, or null.</summary>
            public string FileError;
        }

        /// <summary>Builds the report and writes the file (vc_bugreport, and the Report a problem button).</summary>
        internal static Report Make()
        {
            var report = new Report();
            var text = new System.Text.StringBuilder();
            string loaded = report.Loaded;
            Section(text, "the versions and the start-up", () => report.Header = Header());
            Section(text, "the colonies", () => "\n" + DevReport.Text(true, out loaded));
            Section(text, "the mod's last warnings and errors", LastWarnings);
            report.Text = text.ToString();
            report.Loaded = loaded;
            try
            {
                System.IO.File.WriteAllText(FilePath, report.Text);
            }
            catch (System.Exception e)
            {
                report.FileError = e.Message;
            }
            return report;
        }

        public override void Run(string[] args)
        {
            string what = args != null && args.Length > 0 ? args[0].ToLowerInvariant() : "";
            if (what == "form")
            {
                BuildCommand.Print("vc_bugreport: " + ReportProblem.Run("the console", args.Length > 1 && args[1].ToLowerInvariant() == "test"));
                return;
            }
            Report report = Make();
            BuildCommand.Print(report.FileError == null
                ? $"vc_bugreport: written ({report.Loaded} here) to {FilePath} — attach it to your report with BepInEx/LogOutput.log " +
                  "(type it near the settler or building with the trouble: only what your game has loaded is in it). " +
                  "'vc_bugreport form', or Report a problem in the Town Hall's window, opens the bug form with it filled in."
                : $"vc_bugreport: couldn't write the report ({report.FileError}); attach BepInEx/LogOutput.log instead");
        }

        /// <summary>One part of the report; a part that fails leaves a note instead, and its error goes to the log in full.</summary>
        private static void Section(System.Text.StringBuilder text, string what, System.Func<string> build)
        {
            try
            {
                text.Append(build());
            }
            catch (System.Exception e)
            {
                text.Append($"({what} couldn't be read: {e.GetType().Name}: {e.Message})\n");
                Log.Error($"vc_bugreport: {what}: {e}");
            }
        }

        private static string Header()
        {
            IEnumerable<string> mods = BepInEx.Bootstrap.Chainloader.PluginInfos.Values
                .Where(p => p?.Metadata != null && p.Metadata.GUID != Plugin.PluginGuid)
                .Select(p => $"{p.Metadata.Name} {p.Metadata.Version}").OrderBy(m => m, System.StringComparer.OrdinalIgnoreCase);
            return BugReport.Header(Plugin.PluginVersion, global::Version.GetVersionString(), mods, GameCheck.Missing, GameCheck.StartupFailures, Network());
        }

        /// <summary>The mod's last warnings and errors, folders shortened (a path can hold the Windows account's name).</summary>
        private static string LastWarnings()
        {
            IReadOnlyList<string> recent = Log.Recent;
            var text = new System.Text.StringBuilder("\n");
            text.Append(recent.Count == 0 ? "No warnings or errors from the mod this session.\n" : "The mod's last warnings and errors:\n");
            foreach (string line in recent) text.Append("  ").Append(Scrub(line).Replace("\r", "").Replace("\n", "\n    ")).Append('\n');
            return text.ToString();
        }

        private static string Scrub(string line)
        {
            string home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
            string scrubbed = line.Replace(BepInEx.Paths.BepInExRootPath, "<BepInEx>").Replace(BepInEx.Paths.GameRootPath, "<game>");
            return string.IsNullOrEmpty(home) ? scrubbed : scrubbed.Replace(home, "~");
        }

        private static string Network()
        {
            ZNet net = ZNet.instance;
            if (net == null) return "at the main menu";
            int peers = net.GetPeers().Count;
            return net.IsServer() ? (peers == 0 ? "single player (or a server alone)" : $"hosting {peers} other player{(peers == 1 ? "" : "s")}") : "a client on a server";
        }
    }

    /// <summary>vc_report: the colonies' state to a text file, once or every few seconds.</summary>
    internal sealed class ReportCommand : ConsoleCommand
    {
        public override string Name => "vc_report";
        public override string Help => "VikingColonies (test worlds): 'vc_report' writes every colony's buildings and settlers to BepInEx/VikingColonies-report.txt; 'vc_report auto <seconds>' keeps rewriting it; 'vc_report off' stops that";
        public override bool IsCheat => true;

        public override void Run(string[] args)
        {
            string what = args != null && args.Length > 0 ? args[0].ToLowerInvariant() : "";
            if (what == "auto")
            {
                float seconds = args.Length > 1 && float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float s) ? Mathf.Max(1f, s) : 10f;
                DevRunner.ReportEvery = seconds;
                BuildCommand.Print($"vc_report: rewritten every {seconds:0} s");
            }
            else if (what == "off")
            {
                DevRunner.ReportEvery = 0f;
                BuildCommand.Print("vc_report: no longer rewritten by itself");
            }
            BuildCommand.Print(DevReport.Write());
        }
    }

    /// <summary>
    /// vc_view (the hut redesign): stands you back from the nearest hut of a kind, looking at its middle, so a picture
    /// (vc_shot) shows its whole building. Default: 16 m from its front-right corner, a three-quarter view.
    /// </summary>
    internal sealed class ViewCommand : ConsoleCommand
    {
        public override string Name => "vc_view";
        public override string Help => "VikingColonies (test worlds): 'vc_view <hut> [distance] [frontright|frontleft|front|back|left|right]' stands you that " +
                                       "far (16 m) from the nearest one, looking at it, for a picture of its building (vc_shot)";
        public override bool IsCheat => true;

        public override void Run(string[] args)
        {
            Player p = Player.m_localPlayer;
            BuildingType type = args != null && args.Length > 0 ? DevHelpers.TypeByWord(args[0]) : null;
            if (p == null || type == null || ZNet.instance == null || !TestWorlds.IsTestWorld(ZNet.instance.GetWorldName()))
            {
                BuildCommand.Print("vc_view: in a test world, say which hut: builder, lumberjack, forager, farm, longhouse, watchtower or townhall");
                return;
            }
            ColonyBuilding b = ColonyBuilding.All.Where(x => x != null && x.IsValid && x.TypeId == type.Id)
                .OrderBy(x => Vector3.Distance(x.transform.position, p.transform.position)).FirstOrDefault();
            if (b == null)
            {
                BuildCommand.Print($"vc_view: no {type.Id} loaded here");
                return;
            }
            float distance = args.Length > 1 && float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float d) ? d : 16f;
            string side = args.Length > 2 ? args[2].ToLowerInvariant() : "frontright";
            Vector3 front = b.transform.forward;   // a hut's door faces its forward
            front.y = 0f;
            front.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, front);
            Vector3 dir = side == "front" ? front : side == "back" ? -front : side == "left" ? -right : side == "right" ? right
                : side == "frontleft" ? front - right : front + right;
            Vector3 stand = b.transform.position + dir.normalized * distance;
            GotoCommand.Go(p, stand, b.transform.position + Vector3.up * 3f);
            BuildCommand.Print($"vc_view: {distance:0} m {side} of the {type.Id} {b.Id.Substring(0, System.Math.Min(8, b.Id.Length))} (level {b.Level})");
        }
    }

    /// <summary>vc_goto: next to the nearest building of a kind, or a settler, looking at it.</summary>
    internal sealed class GotoCommand : ConsoleCommand
    {
        public override string Name => "vc_goto";
        public override string Help => "VikingColonies (test worlds): 'vc_goto townhall|builder|lumberjack|longhouse|forager' takes you outside the nearest one's door (or 5 m from it), looking at it; 'vc_goto <settler name>' to a settler";
        public override bool IsCheat => true;

        public override void Run(string[] args)
        {
            Player p = Player.m_localPlayer;
            string what = args != null ? string.Join(" ", args).Trim() : "";
            if (p == null || what.Length == 0)
            {
                BuildCommand.Print("vc_goto: say where: townhall, builder, lumberjack, longhouse, forager, or a settler's name");
                return;
            }
            Vector3 me = p.transform.position;
            BuildingType type = DevHelpers.TypeByWord(what);
            if (type != null)
            {
                ColonyBuilding b = ColonyBuilding.All.Where(x => x != null && x.IsValid && x.TypeId == type.Id)
                    .OrderBy(x => Vector3.Distance(x.transform.position, me)).FirstOrDefault();
                if (b == null)
                {
                    BuildCommand.Print($"vc_goto: there is no {type.Id} in this part of the world");
                    return;
                }
                Door door = DevHelpers.DoorOf(b.GetComponent<ConstructionModule>());
                Vector3 look = door != null ? door.transform.position : b.transform.position;
                Vector3 away = door != null ? door.transform.position - b.transform.position : me - b.transform.position;
                Go(p, look + Flat(away) * (door != null ? 4f : 5f), look);
                BuildCommand.Print($"vc_goto: at the {type.Id} {b.Id.Substring(0, System.Math.Min(8, b.Id.Length))}{(door != null ? ", outside its door" : "")}");
                return;
            }
            Citizen c = Citizen.All.Where(x => x != null && string.Equals(x.CitizenName, what, System.StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => Vector3.Distance(x.transform.position, me)).FirstOrDefault();
            if (c == null)
            {
                BuildCommand.Print($"vc_goto: no building kind or settler called '{what}' (townhall, builder, lumberjack, longhouse, forager, or a settler's name)");
                return;
            }
            Go(p, c.transform.position + Flat(me - c.transform.position) * 4f, c.transform.position + Vector3.up * 1.2f);
            BuildCommand.Print($"vc_goto: next to {c.CitizenName}");
        }

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 0.01f ? v.normalized : Vector3.back;
        }

        internal static void Go(Player p, Vector3 stand, Vector3 look)
        {
            stand.y = ZoneSystem.instance.GetGroundHeight(stand) + 0.2f;
            Vector3 flat = look - stand;
            flat.y = 0f;
            bool far = Vector3.Distance(p.transform.position, stand) > 50f;
            Plugin.Instance.StartCoroutine(TeleportThenFace(p, stand, flat.sqrMagnitude > 0.01f ? Quaternion.LookRotation(flat) : p.transform.rotation, far, look));
        }

        /// <summary>The game refuses a teleport for a moment after the last one ended: ask again until it takes it.</summary>
        private static IEnumerator TeleportThenFace(Player p, Vector3 stand, Quaternion rotation, bool far, Vector3 look)
        {
            for (int tries = 0; p != null && !p.TeleportTo(stand, rotation, far) && tries < 20; tries++)
                yield return new WaitForSecondsRealtime(0.5f);
            yield return FaceWhenThere(p, look);
        }

        private static IEnumerator FaceWhenThere(Player p, Vector3 look)
        {
            for (float waited = 0f; p != null && p.IsTeleporting() && waited < 20f; waited += 0.25f)
                yield return new WaitForSecondsRealtime(0.25f);
            yield return new WaitForSecondsRealtime(0.25f);
            if (p != null) DevHelpers.Face(p, look);
        }
    }

    /// <summary>vc_doors: for the colony doors near the player, where a settler would stand to use them and whether he gets there.</summary>
    internal sealed class DoorsCommand : ConsoleCommand
    {
        public override string Name => "vc_doors";
        public override string Help => "VikingColonies (test worlds): 'vc_doors [settler name]' lists the doors within 15 m: the spots a settler stands on to go through, " +
                                       "and whether the nearest settler (or the one named) can walk to each";
        public override bool IsCheat => true;

        public override void Run(string[] args)
        {
            Player p = Player.m_localPlayer;
            if (p == null) return;
            Vector3 me = p.transform.position;
            string who = args != null ? string.Join(" ", args).Trim() : "";
            Citizen c = Citizen.All.Where(x => x != null && (who.Length == 0 || string.Equals(x.CitizenName, who, System.StringComparison.OrdinalIgnoreCase)))
                .OrderBy(x => Vector3.Distance(x.transform.position, me)).FirstOrDefault();
            AI.CitizenAI ai = c != null ? c.GetComponent<AI.CitizenAI>() : null;
            if (ai == null)
            {
                BuildCommand.Print("vc_doors: no settler nearby" + (who.Length > 0 ? $" called '{who}'" : ""));
                return;
            }
            List<Door> doors = Object.FindObjectsByType<Door>(FindObjectsSortMode.None).Where(d => Utils.DistanceXZ(d.transform.position, me) <= 15f)
                .OrderBy(d => Utils.DistanceXZ(d.transform.position, me)).ToList();
            BuildCommand.Print($"vc_doors: {doors.Count} doors within 15 m, as {c.CitizenName} at ({c.transform.position.x:0.0}, {c.transform.position.y:0.0}, {c.transform.position.z:0.0}) sees them");
            foreach (Door door in doors) BuildCommand.Print(AI.DoorUser.Probe(door, ai));
        }
    }

    /// <summary>vc_walk: sends a settler walking (in or out of a hut, or to a point), to test doors and paths.</summary>
    internal sealed class WalkCommand : ConsoleCommand
    {
        public override string Name => "vc_walk";
        public override string Help => "VikingColonies (test worlds): 'vc_walk <settler> in|out' walks him into the hut he'd sleep in (to his floor spot) or 5 m out " +
                                       "of its door; 'vc_walk all in|out' every settler within 60 m; 'vc_walk <settler> <x> <z>' to a point; " +
                                       "for a minute, then he goes back to what he was doing";
        public override bool IsCheat => true;

        public override void Run(string[] args)
        {
            if (DevHelpers.RefuseOutsideTestWorld(Name)) return;   // it changes the world (the whole-mod review of 1.4.1)
            if (args == null || args.Length < 2)
            {
                BuildCommand.Print("vc_walk: say who and where: 'vc_walk Bjorn in', 'vc_walk Bjorn out' or 'vc_walk Bjorn 960 550'");
                return;
            }
            if (string.Equals(args[0], "all", System.StringComparison.OrdinalIgnoreCase))
            {
                // Every settler within 60 m of the player, all at once (to crowd a door).
                Vector3 me = Player.m_localPlayer != null ? Player.m_localPlayer.transform.position : Vector3.zero;
                foreach (Citizen near in Citizen.All.Where(x => x != null && Vector3.Distance(x.transform.position, me) <= 60f).ToList())
                    Walk(near, args);
                return;
            }
            Citizen named = Citizen.All.FirstOrDefault(x => x != null && string.Equals(x.CitizenName, args[0], System.StringComparison.OrdinalIgnoreCase));
            if (named == null)
            {
                BuildCommand.Print($"vc_walk: no settler called '{args[0]}' nearby");
                return;
            }
            Walk(named, args);
        }

        private static void Walk(Citizen c, string[] args)
        {
            AI.CitizenAI ai = c.GetComponent<AI.CitizenAI>();
            if (ai == null) return;
            Vector3 to;
            string where;
            if (args.Length >= 3 && float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
                float.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
            {
                to = OnGround(new Vector3(x, c.transform.position.y, z));
                where = $"({x:0.0}, {z:0.0})";
            }
            else
            {
                // The hut he would use at night (his own, else the nearest one his colony can shelter in), and his spot in it.
                List<Refuge> huts = AI.Refuges.For(c.Record.ColonyId);
                int pick = IndoorRules.ChooseRefuge(huts, c.Record.IsEmployed ? c.Record.Workplace : "",
                    new GroundPoint(c.transform.position.x, c.transform.position.z), null);
                ColonyBuilding hut = pick >= 0 ? ColonyBuilding.FindById(huts[pick].Id) : null;
                Door door = hut != null ? DevHelpers.DoorOf(hut.GetComponent<ConstructionModule>()) : null;
                if (door == null)
                {
                    BuildCommand.Print("vc_walk: his colony has no finished hut with a door");
                    return;
                }
                Vector3 chest = hut.transform.position, outward = door.transform.position - chest;
                outward.y = 0f;
                outward.Normalize();
                bool inside = string.Equals(args[1], "in", System.StringComparison.OrdinalIgnoreCase);
                GroundPoint spot = IndoorRules.Spot(huts[pick].Chest, huts[pick].Door, huts[pick].Inward, c.Id);   // his floor spot, as at night
                to = inside ? new Vector3(spot.X, chest.y, spot.Z) : OnGround(door.transform.position + outward * 5f);
                if (!inside)   // somewhere he can get to out there (not up a slope)
                    foreach (float distance in new[] { 5f, 4f, 6f, 3f, 7f })
                    {
                        Vector3 out_ = OnGround(door.transform.position + outward * distance);
                        if (!ai.CanWalkTo(out_)) continue;
                        to = out_;
                        break;
                    }
                where = $"{(inside ? "into" : "out of")} the {hut.TypeId} {hut.Id.Substring(0, System.Math.Min(8, hut.Id.Length))}";
            }
            ai.TestWalk(to, 60f);
            BuildCommand.Print($"vc_walk: {c.CitizenName} walks {where}");
        }

        private static Vector3 OnGround(Vector3 at)
        {
            at.y = ZoneSystem.instance.GetGroundHeight(at);
            return at;
        }
    }

    /// <summary>vc_beds: every Longhouse nearby, its planned beds, whether each stands, and who sleeps in it.</summary>
    internal sealed class BedsCommand : ConsoleCommand
    {
        public override string Name => "vc_beds";
        public override string Help => "VikingColonies: 'vc_beds' lists the Longhouses within 100 m, their planned beds (standing or not, and the nearest bed " +
                                       "piece), and the settlers who have each as home";
        public override bool IsCheat => true;

        public override void Run(string[] args)
        {
            Player p = Player.m_localPlayer;
            if (p == null) return;
            var near = new List<Piece>();
            int houses = 0;
            foreach (ResidenceModule house in ResidenceModule.All.Where(h => h != null && h.Building != null
                         && Vector3.Distance(h.transform.position, p.transform.position) < 100f))
            {
                houses++;
                ConstructionModule site = house.GetComponent<ConstructionModule>();
                ColonyBuilding b = house.Building;
                BuildCommand.Print($"vc_beds: longhouse {Short(b.Id)} level {b.Level}, colony {Short(b.ColonyId)}, " +
                                   $"built {site != null && site.IsBuilt}, plan {(site != null ? site.Plan.Count : 0)} pieces, {house.Beds().Count} beds stand");
                if (site == null) continue;
                foreach (PlannedPiece piece in site.Plan.Where(pp => PiecePlacer.Prefab(pp.Prefab)?.GetComponent<Bed>() != null))
                {
                    Vector3 at = new Vector3(piece.Position.X, piece.Position.Y, piece.Position.Z);
                    near.Clear();
                    Piece.GetAllPiecesInRadius(at, 3f, near);
                    Piece nearest = near.Where(n => n != null && n.GetComponent<Bed>() != null)
                        .OrderBy(n => Vector3.Distance(n.transform.position, at)).FirstOrDefault();
                    string bedId = b.Id + "#" + piece.Index;
                    Vector3 inside = house.InsideDoor();
                    AI.CitizenAI walker = Citizen.All.Where(c => c != null).Select(c => c.GetComponent<AI.CitizenAI>()).FirstOrDefault(a => a != null);
                    Vector3 sleepAt = house.SleepSpot(bedId, walker) ?? at;
                    float gap = walker != null ? walker.WayEndGap(inside, sleepAt) : -1f;
                    BuildCommand.Print(string.Format(CultureInfo.InvariantCulture,
                        "vc_beds:   bed #{0}: its sleeping spot ({1:0.0}, {2:0.0}); from inside the door the way ends {3}",
                        piece.Index, sleepAt.x, sleepAt.z, gap < 0f ? "(no settler to ask)" : gap > 1000f ? "nowhere: no way" : gap < 0.6f ? "there (reachable)" : $"{gap:0.0} m short"));
                    string sleepers = string.Join(", ", Citizen.All.Where(c => c != null && c.Record.Home == bedId).Select(c => c.CitizenName));
                    BuildCommand.Print(string.Format(CultureInfo.InvariantCulture,
                        "vc_beds:   bed #{0} at ({1:0.00}, {2:0.00}, {3:0.00}) valid {4}, stands {5}; nearest bed piece {6}; home of {7}",
                        piece.Index, at.x, at.y, at.z, piece.IsValid, PiecePlacer.FindStanding(piece) != null,
                        nearest == null ? "none within 3 m" : string.Format(CultureInfo.InvariantCulture, "{0:0.00} m away at ({1:0.00}, {2:0.00}, {3:0.00}) turned {4:0} (planned {5:0})",
                            Vector3.Distance(nearest.transform.position, at), nearest.transform.position.x, nearest.transform.position.y, nearest.transform.position.z,
                            nearest.transform.rotation.eulerAngles.y, new Quaternion(piece.Rotation.X, piece.Rotation.Y, piece.Rotation.Z, piece.Rotation.W).eulerAngles.y),
                        sleepers.Length > 0 ? sleepers : "nobody"));
                }
            }
            if (houses == 0) BuildCommand.Print("vc_beds: no Longhouse within 100 m");
        }

        private static string Short(string id) => string.IsNullOrEmpty(id) ? "-" : id.Substring(0, System.Math.Min(8, id.Length));
    }

    /// <summary>vc_settlers add N: more settlers for the nearest colony, to see how the game copes with many.</summary>
    internal sealed class SettlersCommand : ConsoleCommand
    {
        public override string Name => "vc_settlers";
        public override string Help => "VikingColonies (test worlds): 'vc_settlers add <1-50>' spawns that many jobless settlers into the colony of the nearest " +
                                       "Town Hall (within 100 m); 'vc_settlers remove <1-50>' removes up to that many of its jobless settlers without a bed, " +
                                       "'vc_settlers remove <name>' the settler of that name (the one nearest you if several share it); " +
                                       "'vc_settlers forget' takes the settlers marked missing off its Town Hall's roster";
        public override bool IsCheat => true;

        public override void Run(string[] args)
        {
            if (DevHelpers.RefuseOutsideTestWorld(Name)) return;   // it changes the world (the whole-mod review of 1.4.1)
            Player p = Player.m_localPlayer;
            int count = 0;
            if (p != null && args != null && args.Length >= 1 && args[0] == "forget")
            {
                ColonyBuilding near = ColonyBuilding.TownHalls().Where(h => h != null && h.IsValid)
                    .OrderBy(h => Vector3.Distance(h.transform.position, p.transform.position)).FirstOrDefault();
                TownHallModule module = near != null && Vector3.Distance(near.transform.position, p.transform.position) <= 100f ? near.GetComponent<TownHallModule>() : null;
                BuildCommand.Print(module == null ? "vc_settlers: no Town Hall within 100 m"
                    : $"vc_settlers: {module.ForgetForTest()} missing settlers taken off colony {near.ColonyId.Substring(0, 8)}'s roster");
                return;
            }
            bool byName = args != null && args.Length >= 2 && args[0] == "remove" && !int.TryParse(args[1], out _);
            if (p == null || args == null || args.Length < 2 || (args[0] != "add" && args[0] != "remove") ||
                (!byName && (!int.TryParse(args[1], out count) || count < 1 || count > 50)))
            {
                BuildCommand.Print("vc_settlers: say 'vc_settlers add <1-50>', 'vc_settlers remove <1-50>' or 'vc_settlers remove <name>'");
                return;
            }
            ColonyBuilding hall = ColonyBuilding.TownHalls().Where(h => h != null && h.IsValid)
                .OrderBy(h => Vector3.Distance(h.transform.position, p.transform.position)).FirstOrDefault();
            if (hall == null || Vector3.Distance(hall.transform.position, p.transform.position) > 100f)
            {
                BuildCommand.Print("vc_settlers: no Town Hall within 100 m");
                return;
            }
            if (args[0] == "remove")
            {
                // The ones a load test added: no job, no bed. The roster forgets them as missing after a few days.
                List<Citizen> extra = byName
                    ? Citizen.All.Where(c => c != null && c.Record.ColonyId == hall.ColonyId && c.CitizenName == args[1])
                                 .OrderBy(c => Vector3.Distance(c.transform.position, p.transform.position)).Take(1).ToList()
                    : Citizen.All.Where(c => c != null && c.Record.ColonyId == hall.ColonyId && !c.Record.IsEmployed && !c.Record.HasHome)
                                 .Take(count).ToList();
                TownHallModule hallModule = hall.GetComponent<TownHallModule>();
                foreach (Citizen citizen in extra)
                {
                    hallModule?.ForgetForTest(citizen.Record.Id);   // not "missing": gone
                    ZNetView nview = citizen.GetComponent<ZNetView>();
                    if (nview != null && nview.IsValid() && !nview.IsOwner()) nview.ClaimOwnership();
                    ZNetScene.instance.Destroy(citizen.gameObject);
                }
                BuildCommand.Print(byName
                    ? $"vc_settlers: {(extra.Count > 0 ? "removed " + args[1] : "no settler called " + args[1])} in colony {hall.ColonyId.Substring(0, 8)}"
                    : $"vc_settlers: removed {extra.Count} jobless settlers without a bed from colony {hall.ColonyId.Substring(0, 8)}");
                return;
            }
            var rng = new System.Random();
            for (int i = 0; i < count; i++)
            {
                Vector3 at = hall.transform.position + Quaternion.Euler(0f, i * 360f / count, 0f) * Vector3.forward * (3f + i % 3);
                at.y = ZoneSystem.instance.GetGroundHeight(at) + 0.5f;
                Citizen citizen = CitizenPrefab.Spawn(at, Quaternion.identity);
                if (citizen == null)
                {
                    BuildCommand.Print("vc_settlers: the settler body didn't load (see the errors at start-up)");
                    return;
                }
                citizen.InitNew(hall.ColonyId, hall.transform.position, rng);
            }
            BuildCommand.Print($"vc_settlers: {count} settlers joined colony {hall.ColonyId.Substring(0, 8)}; it has {Citizen.All.Count(c => c != null && c.Record.ColonyId == hall.ColonyId)} now");
        }
    }

    /// <summary>vc_perf: how smoothly the game runs with the settlers around, measured over a few seconds.</summary>
    internal sealed class PerfCommand : ConsoleCommand
    {
        public override string Name => "vc_perf";
        public override string Help => "VikingColonies (test worlds): 'vc_perf [seconds]' measures frame times and the settlers' share of them (default 20 s)";
        public override bool IsCheat => true;

        public override void Run(string[] args)
        {
            float seconds = args != null && args.Length > 0 && float.TryParse(args[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float s) ? Mathf.Clamp(s, 2f, 120f) : 20f;
            Plugin.Instance.StartCoroutine(Measure(seconds));
            BuildCommand.Print($"vc_perf: measuring for {seconds:0} s");
        }

        private static IEnumerator Measure(float seconds)
        {
            var frames = new List<float>();
            AI.CitizenAI.ProfileTicks = 0;
            AI.CitizenAI.ProfileCalls = 0;
            AI.CitizenAI.ProfileWorst = 0;
            AI.CitizenAI.ProfileByWork.Clear();
            AI.CitizenAI.ProfileCallsByWork.Clear();
            AI.CitizenAI.ProfileWorstByWork.Clear();
            int collectionsBefore = System.GC.CollectionCount(0);
            Perf.Clear();
            Perf.On = true;
            AI.CitizenAI.Profiling = true;
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end)
            {
                yield return null;
                frames.Add(Time.unscaledDeltaTime * 1000f);
            }
            AI.CitizenAI.Profiling = false;
            Perf.On = false;
            if (frames.Count == 0) yield break;
            frames.Sort();
            float average = frames.Average(), p95 = frames[(int)(frames.Count * 0.95f)], worst = frames[frames.Count - 1];
            double aiMs = AI.CitizenAI.ProfileTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            int loaded = Citizen.All.Count(c => c != null), run = Citizen.All.Count(c => c != null && c.IsOwner);
            string line = $"vc_perf: {frames.Count} frames in {seconds:0} s: {1000f / average:0} fps on average, frame {average:0.0} ms (95% under {p95:0.0} ms, " +
                          $"worst {worst:0.0} ms); settlers {loaded} loaded, {run} run here; their updates {aiMs / frames.Count:0.00} ms a frame " +
                          $"({(run > 0 ? aiMs * 1000.0 / System.Math.Max(1, AI.CitizenAI.ProfileCalls) : 0):0} µs each, {AI.CitizenAI.ProfileCalls / (float)frames.Count:0.0} a frame)";
            BuildCommand.Print(line);
            double toMs = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            string byWork = string.Join(", ", AI.CitizenAI.ProfileByWork.OrderByDescending(w => w.Value).Select(w =>
                $"{w.Key} {w.Value * toMs / frames.Count:0.000} ms a frame ({w.Value * toMs * 1000.0 / System.Math.Max(1, AI.CitizenAI.ProfileCallsByWork[w.Key]):0} µs each, " +
                $"slowest {AI.CitizenAI.ProfileWorstByWork[w.Key] * toMs:0.0} ms)"));
            BuildCommand.Print($"vc_perf: by work: {byWork}; slowest update {AI.CitizenAI.ProfileWorst * toMs:0.00} ms; " +
                               $"{System.GC.CollectionCount(0) - collectionsBefore} garbage collections in {seconds:0} s");
            BuildCommand.Print($"vc_perf: parts: {Perf.Report()}");
            BuildCommand.Print("vc_perf: " + WalkingMap.Describe());
        }
    }

    /// <summary>vc_place: puts a colony building down in front of the player, as the hammer would, without the build menu.</summary>
    internal sealed class PlaceCommand : ConsoleCommand
    {
        public override string Name => "vc_place";
        public override string Help => "VikingColonies (test worlds): 'vc_place townhall|builder|lumberjack|longhouse|forager [turn] [x z]' places it in front of you (or at x z; " +
                                       "~5 ~-12 is 5 m east and 12 m south of you) with the hammer's site check (red is refused); turn is its rotation in degrees, 0 = door to the north";

        /// <summary>A coordinate: a number, or ~ and a distance from where the player stands.</summary>
        private static bool Coordinate(string word, float mine, out float value)
        {
            bool relative = word.StartsWith("~");
            string number = relative ? word.Substring(1) : word;
            if (relative && number.Length == 0) number = "0";
            bool ok = float.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            if (ok && relative) value += mine;
            return ok;
        }
        public override bool IsCheat => true;

        public override void Run(string[] args)
        {
            if (DevHelpers.RefuseOutsideTestWorld(Name)) return;   // it changes the world (the whole-mod review of 1.4.1)
            Player p = Player.m_localPlayer;
            BuildingType type = args != null && args.Length > 0 ? DevHelpers.TypeByWord(args[0]) : null;
            if (p == null || type == null)
            {
                BuildCommand.Print("vc_place: say what: townhall, builder, lumberjack, longhouse or forager, and optionally its turn in degrees and where (x z, or ~dx ~dz)");
                return;
            }
            float turn = args.Length > 1 && float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float t) ? t : 0f;
            Vector3 forward = p.transform.forward;
            forward.y = 0f;
            Vector3 at = p.transform.position + forward.normalized * (type.Id == BuildingTypes.TownHallId ? 8f : 10f);
            if (args.Length > 3 && Coordinate(args[2], p.transform.position.x, out float x) && Coordinate(args[3], p.transform.position.z, out float z))
                at = new Vector3(x, 0f, z);
            at.y = ZoneSystem.instance.GetGroundHeight(at);
            Quaternion rotation = Quaternion.Euler(0f, turn, 0f);
            string site = "";
            if (type.Id != BuildingTypes.TownHallId)   // as with the hammer, only huts get the site check
            {
                SiteVerdict verdict = DevHelpers.Judge(type, at, rotation);
                if (verdict == null)
                {
                    BuildCommand.Print($"vc_place: the {type.Id}'s design can't be read; nothing placed");
                    return;
                }
                site = $" — site {verdict.Colour}: {DevHelpers.Plain(SiteText.Describe(verdict, 0f))}";
                if (verdict.Colour == SiteColour.Red)
                {
                    BuildCommand.Print($"vc_place: not placed{site}");
                    return;
                }
                if (ColonyAreas.ColonyAt(at) == null)
                    site += " (outside every colony's area: it won't join a colony)";
            }
            ColonyBuilding placed = DevHelpers.Place(type.PrefabName, at, rotation, p.GetPlayerID());
            BuildCommand.Print(placed == null
                ? $"vc_place: the {type.Id} couldn't be placed"
                : string.Format(CultureInfo.InvariantCulture, "vc_place: placed a {0} at ({1:0.0}, {2:0.0}, {3:0.0}) turned {4:0}{5}",
                    type.Id, placed.transform.position.x, placed.transform.position.y, placed.transform.position.z, turn, site));
        }
    }

    /// <summary>vc_findflat: a flat, dry meadow spot away from colonies, for a test colony.</summary>
    internal sealed class FindFlatCommand : ConsoleCommand
    {
        private const float Step = 25f;
        private const int Rings = 24;          // up to 600 m away
        private const int MaxVisits = 6;       // spots looked at up close (for trees and buildings)
        private const int MaxTrees = 8;        // the builder clears trees; more in the way of the walks slows tests

        public override string Name => "vc_findflat";
        public override string Help => "VikingColonies (test worlds): 'vc_findflat [size]' takes you to a flat, dry meadow spot of size x size metres (default 40) with at most 8 trees, at least 50 m from any colony";
        public override bool IsCheat => true;

        public override void Run(string[] args)
        {
            Player p = Player.m_localPlayer;
            if (p == null || DevHelpers.RefuseOutsideTestWorld("vc_findflat")) return;
            float size = args != null && args.Length > 0 && float.TryParse(args[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float s) ? Mathf.Clamp(s, 10f, 80f) : 40f;
            Plugin.Instance.StartCoroutine(Search(p, size));
        }

        private static IEnumerator Search(Player p, float size)
        {
            s_colonySpots = ColonySpotsInWorld();
            Log.Info($"vc_findflat: {s_colonySpots.Count} colony buildings and settlers in the world to keep away from");
            float water = ZoneSystem.instance.m_waterLevel;
            int looked = 0, visits = 0, notMeadows = 0, wet = 0, uneven = 0, crowded = 0, located = 0;
            float flattest = float.MaxValue;
            foreach (Vec3 candidate in FlatSpot.Candidates(CoreVectors.V(p.transform.position), Step, Rings).ToList())
            {
                Vector3 at = CoreVectors.U(candidate);
                looked++;
                if (WorldGenerator.instance.GetBiome(at.x, at.z) != Heightmap.Biome.Meadows)
                {
                    notMeadows++;
                    continue;
                }
                // Not where the world placed a location (ruins, a camp, stones): its pieces stood in a test hut's doorway in
                // a fresh world (the test next to other mods, 2026-10-15).
                if (LocationNear(at, size))
                {
                    located++;
                    continue;
                }
                FlatSpot.Verdict verdict = FlatSpot.Judge(Heights(at, size), water, NearestColony(at));
                if (!verdict.Good)
                {
                    if (verdict.Reason.StartsWith("too close")) wet++;
                    else if (verdict.Reason.StartsWith("uneven")) uneven++;
                    else crowded++;
                    if (!verdict.Reason.StartsWith("too close")) flattest = Mathf.Min(flattest, verdict.Spread);
                    continue;
                }
                at.y = Height(at.x, at.z) + 0.3f;
                bool far = Vector3.Distance(p.transform.position, at) > 50f;
                // The game refuses a teleport for a moment after the last one ended: ask again until it takes it.
                for (int tries = 0; !p.TeleportTo(at, p.transform.rotation, far) && tries < 20; tries++)
                    yield return new WaitForSecondsRealtime(0.5f);
                for (float waited = 0f; p != null && p.IsTeleporting() && waited < 30f; waited += 0.5f)
                    yield return new WaitForSecondsRealtime(0.5f);
                yield return new WaitForSecondsRealtime(2f);   // its trees and pieces load
                if (p == null) yield break;
                visits++;
                int trees = Object.FindObjectsByType<TreeBase>(FindObjectsSortMode.None).Count(tr => Distance2(tr.transform.position, at) <= size / 2f);
                var pieces = new List<Piece>();
                Piece.GetAllPiecesInRadius(at, size / 2f, pieces);
                pieces.RemoveAll(PiecePlacer.IsWildVegetation);   // a mod's plantable bushes and trees aren't buildings
                float spread = Heights(at, size).Max() - Heights(at, size).Min();
                // In a test world, pieces left from a test colony that is gone don't count: they are cleared away.
                int orphans = TestWorlds.IsTestWorld(ZNet.instance.GetWorldName()) ? pieces.Count(DevHelpers.IsOrphanPiece) : 0;
                if (trees <= MaxTrees && pieces.Count == orphans)
                {
                    if (orphans > 0)
                        BuildCommand.Print($"vc_findflat: cleared {DevHelpers.RemoveOrphanPieces(at, size / 2f)} pieces left from a test colony that is gone");
                    BuildCommand.Print(string.Format(CultureInfo.InvariantCulture,
                        "vc_findflat: here ({0:0}, {1:0}): ground within {2:0.0} m over {3:0} x {3:0} m, {4} trees, no buildings (looked at {5} spots)",
                        at.x, at.z, spread, size, trees, looked));
                    yield break;
                }
                BuildCommand.Print($"vc_findflat: ({at.x:0}, {at.z:0}) has {trees} trees and {pieces.Count} building pieces; looking further");
                if (visits >= MaxVisits) break;
            }
            BuildCommand.Print($"vc_findflat: no flat, dry, open meadow spot found (looked at {looked} spots: {notMeadows} not meadows, {wet} by water, " +
                               $"{uneven} uneven (flattest {(flattest < float.MaxValue ? flattest.ToString("0.0") + " m" : "-")}), {crowded} near a colony, " +
                               $"{located} by a location" +
                               $"{(visits > 0 ? ", visited " + visits : "")}); try from somewhere else or a smaller size");
        }

        /// <summary>A location the world placed (ruins, a camp, stones) stands within 35 m of the spot (and its size).</summary>
        private static bool LocationNear(Vector3 at, float size)
        {
            float reach = 35f + size / 2f;
            foreach (ZoneSystem.LocationInstance location in ZoneSystem.instance.m_locationInstances.Values)
                if (Distance2(location.m_position, at) <= reach) return true;
            return false;
        }

        private static List<float> Heights(Vector3 centre, float size)
        {
            var heights = new List<float>();
            for (int i = -2; i <= 2; i++)
                for (int j = -2; j <= 2; j++)
                    heights.Add(Height(centre.x + i * size / 4f, centre.z + j * size / 4f));
            return heights;
        }

        /// <summary>The ground as it is where it's loaded (dug or raised), else as the world was made.</summary>
        private static float Height(float x, float z) =>
            ZoneSystem.instance.GetGroundHeight(new Vector3(x, 1000f, z), out float h) ? h : WorldGenerator.instance.GetHeight(x, z);

        private static List<Vector3> s_colonySpots = new List<Vector3>();

        /// <summary>The nearest colony building or settler anywhere in the world (saved data: loaded or not).</summary>
        private static float NearestColony(Vector3 at) =>
            s_colonySpots.Select(c => Distance2(c, at)).DefaultIfEmpty(float.MaxValue).Min();

        /// <summary>Where every colony building and settler is, from the world's saved data (also far away, not loaded).</summary>
        /// <summary>The colony the door scenarios keep on purpose (the player's own gate design).</summary>
        private static readonly Vector3 s_keptColony = new Vector3(960f, 0f, 530f);

        /// <summary>vc_world tidy: visits each group of test colony buildings and settlers in the saved world and wipes it.</summary>
        internal static IEnumerator Tidy(Player p)
        {
            Vector3 home = p.transform.position;
            var groups = new List<Vector3>();
            foreach (Vector3 s in ColonySpotsInWorld())
            {
                if (Utils.DistanceXZ(s, s_keptColony) < 60f) continue;
                if (!groups.Any(g => Utils.DistanceXZ(g, s) < 50f)) groups.Add(s);
            }
            BuildCommand.Print($"vc_world: tidying {groups.Count} places with test colonies (the one at (960, 530) stays)");
            int done = 0;
            foreach (Vector3 g in groups)
            {
                Vector3 stand = g;
                stand.y = ZoneSystem.instance.GetGroundHeight(stand) + 0.5f;
                for (int tries = 0; p != null && !p.TeleportTo(stand, p.transform.rotation, true) && tries < 20; tries++)
                    yield return new WaitForSecondsRealtime(0.5f);
                for (float waited = 0f; p != null && p.IsTeleporting() && waited < 30f; waited += 0.5f)
                    yield return new WaitForSecondsRealtime(0.5f);
                yield return new WaitForSecondsRealtime(3f);   // the area's objects load
                if (p == null) yield break;
                Log.Info($"vc_world: tidy at ({g.x:0}, {g.z:0}): {DevHelpers.WipeColonies(p.transform.position, 60f)}");
                done++;
            }
            // Home again before saying so: a scenario's next teleport is refused while this one runs (the first run's findflat).
            for (int tries = 0; p != null && !p.TeleportTo(home, p.transform.rotation, true) && tries < 20; tries++)
                yield return new WaitForSecondsRealtime(0.5f);
            for (float waited = 0f; p != null && p.IsTeleporting() && waited < 30f; waited += 0.5f)
                yield return new WaitForSecondsRealtime(0.5f);
            yield return new WaitForSecondsRealtime(3f);
            BuildCommand.Print($"vc_world: tidied {done} places; {ColonySpotsInWorld().Count(s => Utils.DistanceXZ(s, s_keptColony) >= 60f)} test colony objects left");
        }

        internal static List<Vector3> ColonySpotsInWorld()
        {
            var spots = new List<Vector3>();
            if (ZDOMan.instance == null) return spots;
            var names = BuildingTypes.All.Select(t => t.PrefabName).Concat(new[] { CitizenPrefab.PrefabName });
            foreach (string name in names)
            {
                var zdos = new List<ZDO>();
                int index = 0;
                while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(name, zdos, ref index)) { }
                spots.AddRange(zdos.Select(z => z.GetPosition()));
            }
            return spots;
        }

        private static float Distance2(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
    }

    /// <summary>vc_world: the world settings that help testing (Valheim's world modifiers), in test worlds only.</summary>
    internal sealed class WorldCommand : ConsoleCommand
    {
        public override string Name => "vc_world";
        public override string Help => "VikingColonies (test worlds): 'vc_world' shows the test settings; 'vc_world peace|raids|build|safe on|off': " +
                                       "peace = monsters leave everyone alone unless attacked, raids = raids happen, build = every piece unlocked and free, " +
                                       "safe = no skill loss and equipped items kept on death; 'vc_world clear' removes the monsters within 60 m (not settlers); 'vc_world tidy' wipes every test colony left in the world (not the one at (960, 530)); 'vc_world wipe [radius]' removes test colonies near you " +
                                       "(their buildings, the builder's pieces, settlers and cairns; default 60 m)";
        public override bool IsCheat => true;

        public override void Run(string[] args)
        {
            ZoneSystem zones = ZoneSystem.instance;
            if (zones == null || ZNet.instance == null) return;
            string setting = args != null && args.Length > 0 ? args[0].ToLowerInvariant() : "";
            string value = args != null && args.Length > 1 ? args[1].ToLowerInvariant() : "";
            if (setting.Length > 0 && DevHelpers.RefuseOutsideTestWorld("vc_world")) return;
            if (setting == "tidy")
            {
                // Every test colony left in the saved world, wiped where it stands (2026-10-18: 79 buildings and settlers
                // left by earlier runs took VCTest's flat meadows, and three suites ran on uneven fallback ground).
                Player tidier = Player.m_localPlayer;
                if (tidier != null) Plugin.Instance.StartCoroutine(FindFlatCommand.Tidy(tidier));
                return;
            }
            if (setting == "wipe")
            {
                Player wiper = Player.m_localPlayer;
                float radius = float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float r) ? Mathf.Clamp(r, 5f, 150f) : 60f;
                if (wiper == null) return;
                BuildCommand.Print($"vc_world: wiped within {radius:0} m: {DevHelpers.WipeColonies(wiper.transform.position, radius)}");
                return;
            }
            if (setting == "clear")   // the game's killenemies would kill the settlers too
            {
                Player player = Player.m_localPlayer;
                int removed = player != null ? DevHelpers.ClearMonsters(player.transform.position, 60f) : 0;
                BuildCommand.Print($"vc_world: {removed} monsters within 60 m removed (settlers, players and tame animals stay)");
                return;
            }
            if (setting.Length > 0)
            {
                if (value != "on" && value != "off")
                {
                    BuildCommand.Print("vc_world: say on or off, e.g. 'vc_world peace on'");
                    return;
                }
                if (!TestWorlds.IsTestWorld(ZNet.instance.GetWorldName()))
                {
                    BuildCommand.Print($"vc_world: only in a test world (its name must contain 'test'); this is {ZNet.instance.GetWorldName()}");
                    return;
                }
                bool on = value == "on";
                switch (setting)
                {
                    case "peace":
                        Key(zones, GlobalKeys.PassiveMobs, on);
                        break;
                    case "raids":   // the world's event rate: 0 = no raids, none set = as normal
                        if (on) zones.RemoveGlobalKey(GlobalKeys.EventRate);
                        else zones.SetGlobalKey(GlobalKeys.EventRate, 0f);
                        break;
                    case "build":
                        Key(zones, GlobalKeys.AllPiecesUnlocked, on);
                        Key(zones, GlobalKeys.NoBuildCost, on);
                        break;
                    case "safe":
                        Key(zones, GlobalKeys.DeathKeepEquip, on);
                        if (on) zones.SetGlobalKey(GlobalKeys.SkillReductionRate, 0f);
                        else zones.RemoveGlobalKey(GlobalKeys.SkillReductionRate);
                        break;
                    default:
                        BuildCommand.Print($"vc_world: no setting called '{setting}' (peace, raids, build, safe)");
                        return;
                }
            }
            bool noRaids = zones.GetGlobalKey(GlobalKeys.EventRate, out float rate) && rate <= 0f;
            bool noSkillLoss = zones.GetGlobalKey(GlobalKeys.SkillReductionRate, out float loss) && loss <= 0f;
            BuildCommand.Print($"vc_world: peace {OnOff(zones.GetGlobalKey(GlobalKeys.PassiveMobs))}, raids {OnOff(!noRaids)}, " +
                               $"build {OnOff(zones.GetGlobalKey(GlobalKeys.AllPiecesUnlocked) && zones.GetGlobalKey(GlobalKeys.NoBuildCost))}, " +
                               $"safe {OnOff(zones.GetGlobalKey(GlobalKeys.DeathKeepEquip) && noSkillLoss)}");
        }

        private static void Key(ZoneSystem zones, GlobalKeys key, bool on)
        {
            if (on) zones.SetGlobalKey(key);
            else zones.RemoveGlobalKey(key);
        }

        private static string OnOff(bool on) => on ? "on" : "off";
    }

    /// <summary>vc_quit: saves the world and closes the game, for restarting it after a new build.</summary>
    internal sealed class QuitCommand : ConsoleCommand
    {
        public override string Name => "vc_quit";
        public override string Help => "VikingColonies (test worlds): saves the world and your character, then closes the game";
        public override bool IsCheat => true;

        public override void Run(string[] args)
        {
            BuildCommand.Print("vc_quit: saving and closing the game");
            Plugin.Instance.StartCoroutine(SaveAndQuit());
        }

        private static IEnumerator SaveAndQuit()
        {
            if (Game.instance != null) Game.instance.SavePlayerProfile(true);
            if (ZNet.instance != null && ZNet.instance.IsServer()) ZNet.instance.Save(true);
            yield return new WaitForSecondsRealtime(3f);
            Application.Quit();
        }
    }

    /// <summary>vc_speed: the whole single-player game runs faster, to watch settlers work without waiting.</summary>
    internal sealed class SpeedCommand : ConsoleCommand
    {
        public override string Name => "vc_speed";
        public override string Help => "VikingColonies (test worlds, single player): 'vc_speed <1-5>' runs the whole game that many times faster; 'vc_speed 1' is normal (also when you leave the world)";
        public override bool IsCheat => true;

        public override void Run(string[] args)
        {
            if (args == null || args.Length == 0 || !int.TryParse(args[0], out int speed))
            {
                BuildCommand.Print($"vc_speed: the game runs {DevRunner.Speed}x; 'vc_speed <1-{GameSpeed.Max}>' changes it");
                return;
            }
            BuildCommand.Print(DevRunner.SetSpeed(speed));
        }
    }
}
