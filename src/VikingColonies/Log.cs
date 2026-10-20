using System.Collections.Generic;
using System.Linq;
using VikingColonies.Config;
using VikingColonies.Core;

namespace VikingColonies
{
    /// <summary>
    /// Writes to the BepInEx log (BepInEx/LogOutput.log in the profile). Every settler's step is written only when it helps
    /// to find out why something went wrong (Developer › VerboseLog, or a test world); what a player or a bug report needs
    /// is always written. From the 2026-10 review: players' logs grew by a line per axe swing.
    /// </summary>
    internal static class Log
    {
        private static ZNet s_worldChecked;
        private static bool s_testWorld;

        /// <summary>Every settler's step: written when VerboseLog is on or in a test world.</summary>
        public static void Info(string message)
        {
            if (Verbose) Jotunn.Logger.LogInfo(message);
        }

        /// <summary>Always written: start-up, deaths, the colony's saga, test tools' answers.</summary>
        public static void Event(string message) => Jotunn.Logger.LogInfo(message);

        public static void Warn(string message)
        {
            Keep("warning", message);
            Jotunn.Logger.LogWarning(message);
        }

        public static void Error(string message)
        {
            Keep("error", message);
            Jotunn.Logger.LogError(message);
        }

        /// <summary>The last warnings and errors, oldest first, with when (vc_bugreport, 0.27).</summary>
        internal static IReadOnlyList<string> Recent
        {
            get
            {
                lock (s_recent) return s_recent.ToArray();
            }
        }

        private const int RecentKept = 40;
        private static readonly Queue<string> s_recent = new Queue<string>();

        private static void Keep(string level, string message)
        {
            string text = message ?? "";
            if (text.Length > 400) text = text.Substring(0, 400) + "…";
            lock (s_recent)
            {
                s_recent.Enqueue($"{System.DateTime.Now:HH:mm:ss} {level}: {text}");
                while (s_recent.Count > RecentKept) s_recent.Dequeue();
            }
        }

        /// <summary>Every step is written (worth checking before building a costly message in a hot path).</summary>
        public static bool Verbose => (ColonyConfig.VerboseLog?.Value ?? false) || (ColonyConfig.CommandFile?.Value ?? false) || InTestWorld;

        private static bool InTestWorld
        {
            get
            {
                ZNet net = ZNet.instance;
                if (net == null) return false;
                if (!ReferenceEquals(net, s_worldChecked))   // once per world loaded
                {
                    s_worldChecked = net;
                    s_testWorld = TestWorlds.IsTestWorld(net.GetWorldName());
                }
                return s_testWorld;
            }
        }
    }
}
