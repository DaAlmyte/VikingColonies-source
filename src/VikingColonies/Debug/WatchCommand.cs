using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using HarmonyLib;
using Jotunn.Entities;
using UnityEngine;
using VikingColonies.AI;
using VikingColonies.Citizens;
using VikingColonies.Construction;
using VikingColonies.Core;

namespace VikingColonies.DevTools
{
    /// <summary>
    /// vc_watch (0.24.1): one settler's walking in the log every half second — where he is, what he does, his path and
    /// goal, the door near him — so a strange walk is seen as it happens instead of guessed at from its end (the 0.24
    /// door check needed a trace build for that).
    /// </summary>
    internal sealed class WatchCommand : ConsoleCommand
    {
        public override string Name => "vc_watch";
        public override string Help => "VikingColonies (test worlds): '" + WatchRequest.Usage + "': a settler's place, status, path, " +
                                       "goal and the door near him in the log every 0.5 s while it changes (else every 5 s); up to 4 at once";
        public override bool IsCheat => true;

        public override void Run(string[] args)
        {
            WatchRequest request = WatchRequest.Parse(args);
            BuildCommand.Print(request.Valid ? Watcher.Apply(request, Player.m_localPlayer) : request.Error);
        }
    }

    internal static class Watcher
    {
        private const int MaxWatches = 4;
        private const float EverySeconds = 0.5f;

        private sealed class Watch
        {
            public Citizen Settler;
            public string Name;
            public float Start, End, LastAt;
            public string Last;
            public int Lines;
        }

        private static readonly List<Watch> s_watches = new List<Watch>();
        private static bool s_running;

        public static string Apply(WatchRequest request, Player player)
        {
            if (request.StopAll)
            {
                int count = s_watches.Count;
                s_watches.Clear();
                return $"vc_watch: stopped {count} watch{(count == 1 ? "" : "es")}";
            }
            Citizen settler = Find(request.Target, player);
            bool nearest = string.Equals(request.Target, "nearest", StringComparison.OrdinalIgnoreCase);
            if (settler == null) return $"vc_watch: no settler {(nearest ? "loaded near you" : "named " + request.Target + " loaded here")}";
            Watch known = s_watches.FirstOrDefault(w => w.Settler == settler);
            if (request.Stop)
            {
                s_watches.RemoveAll(w => w.Settler == settler);
                return $"vc_watch: {(known != null ? "stopped watching" : "wasn't watching")} {settler.CitizenName}";
            }
            float now = Time.realtimeSinceStartup;
            if (known != null)
            {
                known.End = now + request.Seconds;
                return $"vc_watch: watching {settler.CitizenName} for {request.Seconds:0} s more";
            }
            if (s_watches.Count >= MaxWatches) return $"vc_watch: already watching {MaxWatches} settlers ('vc_watch off' stops them)";
            s_watches.Add(new Watch { Settler = settler, Name = settler.CitizenName, Start = now, End = now + request.Seconds, LastAt = now });
            if (!s_running) Plugin.Instance.StartCoroutine(Run());
            return $"vc_watch: watching {settler.CitizenName} for {request.Seconds:0} s (a line every 0.5 s while something changes, else every 5 s)";
        }

        /// <summary>The settler of that name nearest the player, or the nearest settler for "nearest".</summary>
        private static Citizen Find(string target, Player player)
        {
            Vector3 me = player != null ? player.transform.position : Vector3.zero;
            bool nearest = string.Equals(target, "nearest", StringComparison.OrdinalIgnoreCase);
            return Citizen.All.Where(c => c != null && (nearest || string.Equals(c.CitizenName, target, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(c => Vector3.Distance(c.transform.position, me)).FirstOrDefault();
        }

        private static IEnumerator Run()
        {
            s_running = true;
            while (s_watches.Count > 0)
            {
                float now = Time.realtimeSinceStartup;
                for (int i = s_watches.Count - 1; i >= 0; i--)
                {
                    Watch w = s_watches[i];
                    if (w.Settler == null)
                    {
                        Log.Event($"vc_watch: {w.Name}: gone (unloaded or dead) after {now - w.Start:0.0} s");
                        s_watches.RemoveAt(i);
                        continue;
                    }
                    if (now >= w.End)
                    {
                        Log.Event($"vc_watch: {w.Name}: done ({w.Lines} lines in {now - w.Start:0} s)");
                        s_watches.RemoveAt(i);
                        continue;
                    }
                    string line;
                    try { line = Describe(w.Settler); }
                    catch (Exception e) { line = "can't be described: " + e.Message; }
                    if (!WatchRules.ShouldLog(line, w.Last, now - w.LastAt)) continue;
                    Log.Event($"vc_watch: {w.Name} {now - w.Start:0.0} s: {line}");
                    w.Last = line;
                    w.LastAt = now;
                    w.Lines++;
                }
                yield return new WaitForSecondsRealtime(EverySeconds);
            }
            s_running = false;
        }

        private static string Describe(Citizen c)
        {
            Vector3 p = c.transform.position;
            string status = (c.Status ?? "").Replace("$vc_status_", "");
            string job = c.Record != null && !string.IsNullOrEmpty(c.Record.Job) ? c.Record.Job : "none";
            CitizenAI ai = c.GetComponent<CitizenAI>();
            return $"at ({p.x:0.0}, {p.y:0.0}, {p.z:0.0}), status {(status.Length > 0 ? status : "none")}, job {job}" +
                   (ai != null ? ", " + ai.WatchState() : "");
        }
    }

    /// <summary>vc_shot (0.24.1): a screenshot from inside the game, so a test looks without taking over the player's screen.</summary>
    internal sealed class ShotCommand : ConsoleCommand
    {
        /// <summary>How many pictures are kept: 60, or more for this game after 'vc_shot keep' (a long picture run).</summary>
        private static int s_keep = ShotFile.DefaultKeep;

        public override string Name => "vc_shot";
        public override string Help => "VikingColonies (test worlds): 'vc_shot [label]' saves a screenshot into BepInEx/VikingColonies/shots (the newest 60 are kept, and every one of the last day); " +
                                       "'vc_shot keep <count>' keeps more until the game closes (a long picture run; at most 500)";
        public override bool IsCheat => true;

        public static string Folder => Path.Combine(Paths.BepInExRootPath, "VikingColonies", "shots");

        public override void Run(string[] args)
        {
            if (args != null && args.Length == 2 && args[0].ToLowerInvariant() == "keep" && int.TryParse(args[1], out int keep))
            {
                s_keep = ShotFile.ClampKeep(keep);
                BuildCommand.Print($"vc_shot: the newest {s_keep} pictures are kept until the game closes");
                return;
            }
            try
            {
                Directory.CreateDirectory(Folder);
                foreach (string old in ShotFile.Oldest(Directory.GetFiles(Folder, "*.png").Select(Path.GetFileName), s_keep - 1, DateTime.Now))
                    File.Delete(Path.Combine(Folder, old));
                string path = Path.Combine(Folder, ShotFile.Name(DateTime.Now, args != null && args.Length > 0 ? string.Join("-", args) : null));
                ScreenCapture.CaptureScreenshot(path);   // written at the end of the frame
                BuildCommand.Print($"vc_shot: saving {path}");
            }
            catch (Exception e) { BuildCommand.Print($"vc_shot: couldn't save a screenshot: {e.Message}"); }
        }
    }

    /// <summary>The game's walking map for vc_perf (0.24.1): its tiles and the links between them; Unity allows about 65,535 links.</summary>
    internal static class WalkingMap
    {
        public const int LinksNearLimit = 49000;   // 75% of Unity's ~65,535
        private static readonly System.Reflection.FieldInfo s_tiles = AccessTools.Field(typeof(Pathfinding), "m_tiles");

        public static string Describe()
        {
            try
            {
                if (!(s_tiles?.GetValue(Pathfinding.instance) is IDictionary map)) return "walking map: unknown (the game renamed its parts)";
                int links = 0;
                foreach (object tile in map.Values)
                {
                    if (tile == null) continue;
                    Type type = tile.GetType();
                    links += Count(AccessTools.Field(type, "m_links1")?.GetValue(tile)) + Count(AccessTools.Field(type, "m_links2")?.GetValue(tile));
                }
                return $"walking map: {map.Count} tiles, {links} links" +
                       (links > LinksNearLimit ? " — near Unity's limit of about 65,535 (paths may start failing)" : "");
            }
            catch (Exception e) { return "walking map: unknown (" + e.Message + ")"; }
        }

        private static int Count(object list) => list is ICollection c ? c.Count : 0;
    }
}
