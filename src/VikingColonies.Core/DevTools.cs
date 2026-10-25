using System;
using System.Collections.Generic;
using System.Linq;

namespace VikingColonies.Core
{
    /// <summary>Auto-start only goes into a test world, so a setting can never start someone's real world.</summary>
    public static class TestWorlds
    {
        /// <summary>The start of every test world's name (VCTest, VCTestMP, VCTestFlat, VCTestRun, VCTestCompat).</summary>
        public const string NameStart = "VCTest";

        /// <summary>
        /// One of our test worlds: its name starts with VCTest. Not any name containing "test" (the review round of
        /// 2026-10-23: a player's "Greatest" or "Contest" got full step-by-step logging, the test commands and the command
        /// file).
        /// </summary>
        public static bool IsTestWorld(string name) =>
            !string.IsNullOrEmpty(name) && name.StartsWith(NameStart, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// A world whose name starts with VCTestFlat is made as one meadow at one height, with no trees, rocks, rivers or
        /// ruins (2026-10-18, the player's idea: the huts' building tests and pictures on level ground). Only our own test
        /// world's name: a player's world that merely says "flat" and "test" ("Flattest") must never be made without its
        /// locations (the mod hub's check of 1.4.0).
        /// </summary>
        public static bool IsFlatTestWorld(string name) =>
            !string.IsNullOrEmpty(name) && name.StartsWith(FlatTestWorldName, StringComparison.OrdinalIgnoreCase);

        public const string FlatTestWorldName = "VCTestFlat";
    }

    /// <summary>
    /// The self-test's block in front of a hut's door: 1 m layers from the ground until it stands 2 m over the doorstep (the
    /// 1.4.0 full run: on uneven ground a 2 m block below a raised doorstep was a step up to it, not a block).
    /// </summary>
    public static class TestDoorBlock
    {
        public static int Layers(float ground, float doorstep) =>
            (int)Math.Ceiling(Math.Max(ground, doorstep) + 2f - ground - 0.001f);
    }

    /// <summary>vc_speed: how many times faster a test world runs.</summary>
    public static class GameSpeed
    {
        public const int Max = 5;

        public static int Clamp(int speed) => Math.Max(1, Math.Min(Max, speed));
    }

    /// <summary>
    /// The test command file: console commands written into a text file and run by the game, so tests need no typing in
    /// the game's console. Only our commands and a few harmless dev commands run; never devcommands itself.
    /// </summary>
    public static class CommandFile
    {
        private static readonly HashSet<string> s_devCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "goto", "pos", "god", "ghost", "tod", "skiptime", "spawn", "nocost", "removedrops", "save",
            "event", "randomevent", "stopevent",
            "env", "resetenv",   // the weather, for pictures (the huts' showcase, 2026-10-18)
        };

        /// <summary>The commands in the file, one per line; blank lines and lines starting with # are left out.</summary>
        public static List<string> Parse(string text)
        {
            var lines = new List<string>();
            if (string.IsNullOrEmpty(text)) return lines;
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length > 0 && !line.StartsWith("#", StringComparison.Ordinal)) lines.Add(line);
            }
            return lines;
        }

        public static bool IsAllowed(string line)
        {
            string name = (line ?? "").Trim().Split(' ')[0];
            return name.StartsWith("vc_", StringComparison.OrdinalIgnoreCase) || s_devCommands.Contains(name);
        }

        /// <summary>
        /// What the file does in this world: runs in a test world, waits while no world is loaded, and refuses (and empties)
        /// anywhere else (the whole-mod review of 1.4.1: a profile with the file on, opened on the player's own world, ran it).
        /// </summary>
        public static CommandFileState In(string world) =>
            string.IsNullOrEmpty(world) ? CommandFileState.Wait
            : TestWorlds.IsTestWorld(world) ? CommandFileState.Run
            : CommandFileState.Refuse;
    }

    public enum CommandFileState { Wait, Run, Refuse }

    /// <summary>vc_watch (0.24.1): which settler to watch and for how long, or which watch to stop.</summary>
    public sealed class WatchRequest
    {
        public const float DefaultSeconds = 60f;
        public const float MaxSeconds = 600f;
        public const string Usage = "vc_watch <settler|nearest> [seconds] (0 stops that watch), or vc_watch off";

        public bool Valid { get; private set; }
        public string Error { get; private set; } = "";
        /// <summary>A settler's name, or "nearest".</summary>
        public string Target { get; private set; } = "";
        public float Seconds { get; private set; }
        public bool Stop { get; private set; }
        public bool StopAll { get; private set; }

        public static WatchRequest Parse(string[] args)
        {
            if (args == null || args.Length == 0 || string.IsNullOrWhiteSpace(args[0])) return new WatchRequest { Error = Usage };
            string target = args[0].Trim();
            if (string.Equals(target, "off", StringComparison.OrdinalIgnoreCase)) return new WatchRequest { Valid = true, StopAll = true };
            float seconds = DefaultSeconds;
            if (args.Length > 1 && (!float.TryParse(args[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out seconds)
                                    || float.IsNaN(seconds)))   // NaN would never end (review of 0.24.1)
                return new WatchRequest { Error = Usage };
            if (seconds <= 0f) return new WatchRequest { Valid = true, Target = target, Stop = true };
            return new WatchRequest { Valid = true, Target = target, Seconds = Math.Max(1f, Math.Min(MaxSeconds, seconds)) };
        }
    }

    /// <summary>vc_watch: a line goes to the log when it changed, or every few seconds while nothing does.</summary>
    public static class WatchRules
    {
        public const double SameLineEvery = 5.0;

        public static bool ShouldLog(string line, string last, double secondsSinceLast) =>
            last == null || line != last || secondsSinceLast >= SameLineEvery;
    }

    /// <summary>vc_shot (0.24.1): a screenshot's file name (the time, then a label), and which old ones go.</summary>
    public static class ShotFile
    {
        public const int MaxLabel = 40;
        /// <summary>How many pictures are kept (a six-hut showcase takes 31; 2026-10-18: its first picture was gone).</summary>
        public const int DefaultKeep = 60;
        /// <summary>The most 'vc_shot keep' allows: about 1.5 GB of pictures.</summary>
        public const int MaxKeep = 500;

        /// <summary>
        /// vc_shot keep: a long picture run keeps more (the hut review, 2026-10-21: 185 pictures, its first huts' gone before
        /// its end), never fewer than the usual 60.
        /// </summary>
        public static int ClampKeep(int keep) => Math.Max(DefaultKeep, Math.Min(MaxKeep, keep));

        public static string Name(DateTime at, string label)
        {
            var safe = new System.Text.StringBuilder();
            bool gap = false;
            foreach (char c in label ?? "")
            {
                bool ok = c < 128 && (char.IsLetterOrDigit(c) || c == '-' || c == '_');
                if (ok)
                {
                    if (gap && safe.Length > 0) safe.Append('_');
                    safe.Append(c);
                    gap = false;
                }
                else gap = true;
            }
            string tag = safe.Length > MaxLabel ? safe.ToString(0, MaxLabel) : safe.ToString();
            return at.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture) + (tag.Length > 0 ? "-" + tag : "") + ".png";
        }

        /// <summary>A picture taken within this many hours is never taken away, however many there are.</summary>
        public const double KeepHours = 24;

        /// <summary>
        /// The names beyond the newest <paramref name="keep"/> (names start with the time, so they sort by it); given
        /// <paramref name="now"/>, not those taken within <see cref="KeepHours"/> (2026-10-21: a 260-picture run's
        /// pictures, kept with 'vc_shot keep', went at the next game's first picture, back at 60, before they were seen).
        /// </summary>
        public static List<string> Oldest(IEnumerable<string> names, int keep, DateTime? now = null)
        {
            List<string> sorted = (names ?? Enumerable.Empty<string>()).OrderBy(n => n, StringComparer.Ordinal).ToList();
            List<string> beyond = sorted.Take(Math.Max(0, sorted.Count - Math.Max(0, keep))).ToList();
            if (now == null) return beyond;
            DateTime since = now.Value.AddHours(-KeepHours);
            // Never more than MaxKeep however new (the review of 2026-10-21: the day's rule had lifted the cap).
            List<string> overCap = sorted.Take(Math.Max(0, sorted.Count - MaxKeep)).ToList();
            return beyond.Where(n => overCap.Contains(n) || !TakenAt(n, out DateTime at) || at < since).ToList();
        }

        private static bool TakenAt(string name, out DateTime at) =>
            DateTime.TryParseExact(name != null && name.Length >= 15 ? name.Substring(0, 15) : "", "yyyyMMdd-HHmmss",
                System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out at);
    }

    /// <summary>vc_findflat: whether a spot suits a test colony, and where to look for one.</summary>
    public static class FlatSpot
    {
        /// <summary>The ground over the spot may vary this much at most (meadows roll; the builder flattens the rest).</summary>
        public const float MaxSpread = 2.5f;
        /// <summary>Its lowest ground stays this far above the water.</summary>
        public const float DryMargin = 1f;
        /// <summary>Other colonies stay at least this far away.</summary>
        public const float MinColonyDistance = 50f;

        public struct Verdict
        {
            public bool Good;
            public float Spread;
            public string Reason;
        }

        /// <param name="heights">Ground heights sampled over the spot.</param>
        /// <param name="nearestColony">Distance to the nearest Town Hall.</param>
        public static Verdict Judge(IReadOnlyList<float> heights, float waterLevel, float nearestColony)
        {
            if (heights == null || heights.Count == 0) return new Verdict { Reason = "no ground samples" };
            float lowest = heights.Min(), spread = heights.Max() - lowest;
            string reason =
                lowest < waterLevel + DryMargin ? $"too close to the water (lowest ground {lowest:0.0}, water at {waterLevel:0.0})" :
                spread > MaxSpread ? $"uneven ({spread:0.0} m between highest and lowest)" :
                nearestColony < MinColonyDistance ? $"a colony {nearestColony:0} m away" : "";
            return new Verdict { Good = reason.Length == 0, Spread = spread, Reason = reason };
        }

        /// <summary>Spots on square rings round the start, <paramref name="step"/> apart, nearest ring first.</summary>
        public static IEnumerable<Vec3> Candidates(Vec3 centre, float step, int rings)
        {
            for (int r = 1; r <= rings; r++)
            {
                for (int i = -r; i <= r; i++)
                {
                    yield return new Vec3(centre.X + i * step, centre.Y, centre.Z - r * step);
                    yield return new Vec3(centre.X + i * step, centre.Y, centre.Z + r * step);
                }
                for (int j = -r + 1; j <= r - 1; j++)
                {
                    yield return new Vec3(centre.X - r * step, centre.Y, centre.Z + j * step);
                    yield return new Vec3(centre.X + r * step, centre.Y, centre.Z + j * step);
                }
            }
        }
    }
}
