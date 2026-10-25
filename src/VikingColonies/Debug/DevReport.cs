using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BepInEx;
using UnityEngine;
using VikingColonies.Buildings;
using VikingColonies.Citizens;
using VikingColonies.Construction;
using VikingColonies.Core;

namespace VikingColonies.DevTools
{
    /// <summary>vc_report: the colonies' buildings and settlers as they are now, written to a text file for reading outside the game.</summary>
    internal static class DevReport
    {
        public static string FilePath => Path.Combine(Paths.BepInExRootPath, "VikingColonies-report.txt");

        /// <summary>Writes the report; returns a one-line summary.</summary>
        public static string Write()
        {
            string text = Text(false, out string summary);
            File.WriteAllText(FilePath, text);
            return $"vc_report: {summary} -> {FilePath}";
        }

        /// <summary>The colonies as text; with <paramref name="details"/> each settler's recent states and troubles (vc_bugreport).</summary>
        public static string Text(bool details, out string summary)
        {
            var colonies = new Dictionary<string, ReportColony>();
            var withoutColony = new List<ReportSettler>();
            foreach (ColonyBuilding hall in ColonyBuilding.TownHalls())
                colonies[hall.ColonyId] = new ReportColony { Id = Short(hall.ColonyId), Hall = CoreVectors.V(hall.transform.position) };
            int buildings = 0, settlers = 0;
            foreach (ColonyBuilding b in ColonyBuilding.All.Where(b => b != null && b.IsValid))
            {
                ReportColony colony = ColonyOf(colonies, b.ColonyId);
                colony.Buildings.Add(Building(b));
                buildings++;
            }
            foreach (Citizen c in Citizen.All.Where(c => c != null))
            {
                ReportSettler line = Settler(c, details);
                if (string.IsNullOrEmpty(c.ColonyId)) withoutColony.Add(line);
                else ColonyOf(colonies, c.ColonyId).Settlers.Add(line);
                settlers++;
            }
            int withHistory = colonies.Values.SelectMany(c => c.Settlers).Concat(withoutColony).Count(s => s.Troubles != null && s.Recent != null && s.Recent.Count > 0 && !s.Recent[0].StartsWith(NotHere));
            summary = $"{colonies.Count} colonies, {buildings} buildings, {settlers} settlers" + (details ? $", {withHistory} with recent states" : "");
            return ColonyReport.Format(Header(), colonies.Values.ToList(), withoutColony);
        }

        private static ReportColony ColonyOf(Dictionary<string, ReportColony> colonies, string colonyId)
        {
            string key = colonyId ?? "";
            if (!colonies.TryGetValue(key, out ReportColony colony))
                colonies[key] = colony = new ReportColony { Id = key.Length == 0 ? "(none)" : Short(key) + " (no Town Hall)" };
            return colony;
        }

        private static ReportBuilding Building(ColonyBuilding b)
        {
            ConstructionModule site = b.GetComponent<ConstructionModule>();
            Door door = DevHelpers.DoorOf(site);
            return new ReportBuilding
            {
                Type = b.TypeId,
                Id = Short(b.Id),
                Level = b.Level,
                Position = CoreVectors.V(b.transform.position),
                Turn = b.transform.eulerAngles.y,
                Door = door != null ? CoreVectors.V(door.transform.position) : (Vec3?)null,
                Order = site != null ? OrderLine(site) : "",
                Chest = b.Storage != null ? b.Storage.Contents() : null,
                ChestSize = b.Storage != null ? b.Storage.SizeText : "",
            };
        }

        private static string OrderLine(ConstructionModule site)
        {
            BuildOrderRecord o = site.Order;
            if (o == null) return "";
            string line = $"{o.Kind} to level {o.TargetLevel}, {o.State}, {o.DoneCount} of {o.Total} pieces, {o.TakeAwayLeft} old left, {o.SkippedCount} skipped";
            if (o.Builder.Length > 0) line += $", builder {NameOf(o.Builder)}";
            if (o.Needs.Length > 0) line += $", needs '{Localization.instance.Localize(o.Needs)}'";
            if (o.Blocked.Length > 0) line += $", blocked by '{o.Blocked}'";
            if (o.Kind == OrderKind.Upgrade) line += site.CanCancelUpgrade ? ", can be cancelled" : ", can't be cancelled";
            return line;
        }

        private const string NotHere = "not run by this game now";

        private static ReportSettler Settler(Citizen c, bool details)
        {
            CitizenRecord r = c.Record;
            var line = new ReportSettler
            {
                Name = r.Name,
                Job = r.Job,
                Workplace = Short(r.Workplace),
                Status = string.IsNullOrEmpty(r.Status) ? "" : Localization.instance.Localize(r.Status),
                Position = CoreVectors.V(c.transform.position),
                Carry = c.Carry.Serialize(),
            };
            if (!details) return line;
            if (c.IsOwner)
            {
                line.Recent = c.History.Lines(Time.time, s => Localization.instance.Localize(s));
                line.Troubles = c.GetComponent<AI.CitizenAI>()?.Troubles() ?? "";
            }
            else line.Recent = new List<string> { NotHere + " (too far from you, or another player's game runs them): their recent states and troubles are kept there" };
            return line;
        }

        private static string Header()
        {
            Player p = Player.m_localPlayer;
            string where = p != null ? string.Format(CultureInfo.InvariantCulture, "({0:0.0}, {1:0.0}, {2:0.0})", p.transform.position.x, p.transform.position.y, p.transform.position.z) : "-";
            string day = EnvMan.instance != null ? string.Format(CultureInfo.InvariantCulture, "day {0}, time of day {1:0.00}", EnvMan.instance.GetDay(), EnvMan.instance.GetDayFraction()) : "";
            return $"VikingColonies report {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}, world {ZNet.instance?.GetWorldName()}, {day}, " +
                   $"player at {where}{(p != null && p.InGodMode() ? " (god mode)" : "")}, game speed {DevRunner.Speed}x";
        }

        private static string NameOf(string citizenId) =>
            Citizen.All.FirstOrDefault(c => c != null && c.Id == citizenId)?.CitizenName ?? citizenId;

        /// <summary>Ids are long; the first 8 characters tell them apart in a test world.</summary>
        private static string Short(string id) => string.IsNullOrEmpty(id) ? "" : id.Length <= 8 ? id : id.Substring(0, 8);
    }
}
