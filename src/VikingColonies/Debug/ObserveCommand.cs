using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Jotunn.Entities;
using UnityEngine;
using VikingColonies.Buildings;
using VikingColonies.Citizens;
using VikingColonies.Construction;
using VikingColonies.Core;

namespace VikingColonies.DevTools
{
    /// <summary>
    /// vc_observe [minutes] (test worlds): a whole working colony put up here at once (Town Hall, Builder's Hut, Lumberjack's
    /// Hut, Forager's Hut, Farm, Longhouse, a fire), stocked as a player would (tools, seeds, food, wood), with one more
    /// Longhouse left for the builder; then every settler's status every 30 s (game time) and, at the end, how each spent
    /// his time. For watching how the workers really work. Leaves the colony standing (vc_world wipe removes it).
    /// </summary>
    internal sealed class ObserveCommand : ConsoleCommand
    {
        public override string Name => "vc_observe";
        public override string Help => "VikingColonies (test worlds): put up a whole stocked colony here and log every settler's status every " +
                                       "30 s for the given game minutes (default 60), then a summary per worker. Leaves it standing.";
        public override bool IsCheat => true;

        public override void Run(string[] args)
        {
            Player p = Player.m_localPlayer;
            if (p == null || ZNet.instance == null || !TestWorlds.IsTestWorld(ZNet.instance.GetWorldName()))
            {
                BuildCommand.Print("vc_observe: test worlds only");
                return;
            }
            float minutes = args != null && args.Length > 0 && float.TryParse(args[0], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float m) ? Mathf.Clamp(m, 1f, 600f) : 60f;
            Plugin.Instance.StartCoroutine(Observe.Run(p, minutes));
        }
    }

    internal static class Observe
    {
        public static IEnumerator Run(Player player, float minutes)
        {
            Log.Event($"vc_observe: setting up a colony for {minutes:0} game minutes");
            if (!player.InGodMode()) player.SetGodMode(true);
            Vector3 origin = player.transform.position, forward = player.transform.forward;
            forward.y = 0f;
            forward.Normalize();
            ColonyBuilding hall = DevHelpers.Place(BuildingTypes.TownHall.PrefabName, origin + forward * 8f, Quaternion.identity, player.GetPlayerID());
            TownHallModule townHall = hall != null ? hall.GetComponent<TownHallModule>() : null;
            for (float t = 0f; townHall != null && !townHall.Founded && t < 20f; t += 0.5f) yield return new WaitForSeconds(0.5f);
            if (townHall == null || !townHall.Founded)
            {
                Log.Event("vc_observe: done — the Town Hall wasn't founded");
                yield break;
            }
            Vector3 centre = hall.transform.position;

            var huts = new List<ColonyBuilding>();
            BuildingType[] kinds = { BuildingTypes.BuilderHut, BuildingTypes.LumberjackHut, BuildingTypes.ForagerHut, BuildingTypes.Farm, BuildingTypes.Longhouse };
            float startAngle = Mathf.Atan2(forward.z, forward.x) * Mathf.Rad2Deg;
            var used = new List<Vector3> { centre, origin };   // not round the player: observation run 4 put a Longhouse door where he stood
            foreach (BuildingType kind in kinds)
            {
                ColonyBuilding hut = PlaceNear(kind, centre, startAngle, used, player);
                if (hut == null)
                {
                    Log.Event($"vc_observe: no room for a {kind.Id}");
                    continue;
                }
                used.Add(hut.transform.position);
                huts.Add(hut);
                ConstructionModule site = hut.GetComponent<ConstructionModule>();
                for (float t = 0f; site != null && site.Order == null && t < 15f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                if (site != null && site.Order != null) site.FinishNow();
                for (float t = 0f; site != null && !site.IsBuilt && t < 40f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                Log.Event($"vc_observe: {kind.Id} at {Fmt(hut.transform.position)}, built {site != null && site.IsBuilt}");
            }

            // Stock it as a player would.
            Give(hall, "Raspberry", 20); Give(hall, "Mushroom", 10); Give(hall, "Blueberries", 10); Give(hall, "Wood", 30);
            foreach (ColonyBuilding hut in huts)
            {
                if (hut.Type == BuildingTypes.BuilderHut)
                {
                    Give(hut, "Wood", 80); Give(hut, "Stone", 30); Give(hut, "AxeFlint", 1); Give(hut, "PickaxeAntler", 1); Give(hut, "Hoe", 1);
                }
                else if (hut.Type == BuildingTypes.LumberjackHut) Give(hut, "Cultivator", 1);
                else if (hut.Type == BuildingTypes.Farm)
                {
                    Give(hut, "Cultivator", 1); Give(hut, "CarrotSeeds", 12); Give(hut, "TurnipSeeds", 8);
                    // A Farm works only its fields (0.20.1): a 5x5 field in front of it, for the Farm's list.
                    DevHelpers.PlaceField(hut, hut.transform.position + hut.transform.forward * 9f);
                }
            }
            GameObject fire = DevHelpers.Spawn("fire_pit", centre + Quaternion.Euler(0f, 90f, 0f) * forward * 6f);
            if (fire != null)
            {
                Fireplace hearth = fire.GetComponent<Fireplace>();
                ZNetView fireView = fire.GetComponent<ZNetView>();
                if (hearth != null && fireView != null && fireView.IsValid()) fireView.GetZDO().Set(ZDOVars.s_fuel, hearth.m_maxFuel);
            }
            // One more for the builder to put up.
            ColonyBuilding toBuild = PlaceNear(BuildingTypes.Longhouse, centre, startAngle + 36f, used, player);
            Log.Event($"vc_observe: an unfinished Longhouse for the builder: {(toBuild != null ? Fmt(toBuild.transform.position) : "no room")}");

            // Watch.
            var samples = new Dictionary<string, Dictionary<string, int>>();
            var jobs = new Dictionary<string, string>();
            float end = minutes * 60f;
            for (float t = 0f; t <= end; t += 30f)
            {
                foreach (Citizen c in Citizen.All.Where(c => c != null && c.Record.ColonyId == hall.ColonyId))
                {
                    string status = DevHelpers.Plain(Localization.instance.Localize(c.Status ?? ""));
                    string doing = c.Doing;
                    string job = c.Record.Job.Length > 0 ? c.Record.Job : "jobless";
                    Log.Event($"vc_observe: t={t / 60f:0.0} {c.CitizenName} {job} '{status}'{(doing.Length > 0 ? " (" + doing + ")" : "")} " +
                              $"carry {c.Carry.Total} at {Fmt(c.transform.position)} mood {c.Record.Mood} tod {EnvMan.instance.GetDayFraction():0.00}");
                    if (!samples.TryGetValue(c.CitizenName, out Dictionary<string, int> mine)) samples[c.CitizenName] = mine = new Dictionary<string, int>();
                    string key = StatusKind(status);
                    mine[key] = mine.TryGetValue(key, out int n) ? n + 1 : 1;
                    jobs[c.CitizenName] = job;
                }
                yield return new WaitForSeconds(30f);
            }
            foreach (KeyValuePair<string, Dictionary<string, int>> s in samples.OrderBy(s => jobs[s.Key]))
            {
                int total = s.Value.Values.Sum();
                Log.Event($"vc_observe: summary {s.Key} ({jobs[s.Key]}): " +
                          string.Join(", ", s.Value.OrderByDescending(v => v.Value).Select(v => $"{v.Key} {100 * v.Value / Mathf.Max(1, total)}%")));
            }
            foreach (ColonyBuilding hut in huts.Concat(new[] { hall }))
                Log.Event($"vc_observe: chest {hut.Type.Id}: " + string.Join(", ", hut.Storage.Contents().Select(kv => kv.Value + " " + kv.Key)));
            Log.Event("vc_observe: done");
        }

        /// <summary>The status without its numbers ("Building Longhouse (12/29)" → "Building Longhouse"), so samples group.</summary>
        private static string StatusKind(string status) =>
            System.Text.RegularExpressions.Regex.Replace(status ?? "", @"\s*\(?\d+(/\d+)?\)?|\s+\d+\s+\w+", "").Trim();

        private static ColonyBuilding PlaceNear(BuildingType type, Vector3 centre, float startAngle, List<Vector3> used, Player player)
        {
            foreach (float radius in new[] { 20f, 27f, 34f })
                for (int i = 0; i < 12; i++)
                {
                    float a = (startAngle + i * 30f) * Mathf.Deg2Rad;
                    Vector3 at = centre + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
                    at.y = ZoneSystem.instance.GetGroundHeight(at);
                    if (used.Any(u => Utils.DistanceXZ(u, at) < 14f)) continue;
                    Quaternion turn = Quaternion.LookRotation(centre - at);   // door towards the Town Hall
                    SiteVerdict verdict = DevHelpers.Judge(type, at, turn);
                    if (verdict == null || verdict.Colour == SiteColour.Red) continue;
                    return DevHelpers.Place(type.PrefabName, at, turn, player.GetPlayerID());
                }
            return null;
        }

        private static void Give(ColonyBuilding hut, string item, int amount)
        {
            GameObject prefab = ObjectDB.instance.GetItemPrefab(item);
            Container chest = hut != null ? hut.GetComponent<Container>() : null;
            if (prefab == null || chest == null) return;
            chest.GetInventory().AddItem(prefab, amount);
        }

        private static string Fmt(Vector3 v) => $"({v.x:0},{v.z:0})";
    }
}
