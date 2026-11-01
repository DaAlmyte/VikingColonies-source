using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Jotunn.Entities;
using UnityEngine;
using VikingColonies.AI;
using VikingColonies.Buildings;
using VikingColonies.Citizens;
using VikingColonies.Construction;
using VikingColonies.Core;

namespace VikingColonies.DevTools
{
    /// <summary>
    /// vc_watchdog (the flake hunt and the watchdog, 26 Oct 2026; test worlds only): while on, every 5 s of game time the
    /// colony's basic rules are checked (Core's <see cref="Core.Watchdog"/>: items kept in hand-overs, sane positions,
    /// nobody stuck on the way or inside a wall, no hut over its places) and a break is logged as
    /// 'vc_watchdog: FAIL rule (details)', which the runner counts as a failure. The runner turns it on in every run.
    /// </summary>
    internal sealed class WatchdogCommand : ConsoleCommand
    {
        public override string Name => "vc_watchdog";
        public override string Help => "VikingColonies (test worlds): 'vc_watchdog on|off': the colony's rules checked every 5 s, a break " +
                                       "logged as 'vc_watchdog: FAIL rule (...)'; 'vc_watchdog off RULE [RULE ...]' / 'on RULE' switch rules (" +
                                       string.Join(", ", Core.Watchdog.Rules) + "); 'vc_watchdog' says what's on and what it saw";
        public override bool IsCheat => true;

        public override void Run(string[] args)
        {
            if (ZNet.instance == null || !TestWorlds.IsTestWorld(ZNet.instance.GetWorldName()))
            {
                BuildCommand.Print("vc_watchdog: test worlds only");
                return;
            }
            string word = args.Length > 0 ? args[0].ToLowerInvariant() : "";
            if ((word == "on" || word == "off") && args.Length > 1)
            {
                var unknown = new List<string>();
                foreach (string rule in args.Skip(1))
                    if (!WatchdogRunner.Dog.SetRule(rule.ToLowerInvariant(), word == "on")) unknown.Add(rule);
                Log.Event($"vc_watchdog: rules {word}: {string.Join(" ", args.Skip(1))}" + (unknown.Count > 0 ? $" (no such rule: {string.Join(", ", unknown)})" : ""));
            }
            else if (word == "on")
            {
                WatchdogRunner.Start();
                Log.Event("vc_watchdog: on (" + string.Join(", ", Core.Watchdog.Rules) + ")");
            }
            else if (word == "off")
            {
                WatchdogRunner.On = false;
                Log.Event("vc_watchdog: off");
            }
            BuildCommand.Print(WatchdogRunner.Describe());
        }
    }

    /// <summary>vc_seed N (test worlds only): every random choice the mod makes from now on comes from N (Core's ColonyRandom).</summary>
    internal sealed class SeedCommand : ConsoleCommand
    {
        public override string Name => "vc_seed";
        public override string Help => "VikingColonies (test worlds): 'vc_seed N': the mod's random choices (who is founded and how, newcomers, " +
                                       "pastimes, timing, rolls, spots) come from seed N, a stream per purpose and settler; the game's own " +
                                       "randomness is seeded too but parts from it within seconds (its AI, physics and frame timing are never " +
                                       "exact). 'vc_seed off': unseeded again. 'vc_seed': the seed now";
        public override bool IsCheat => true;

        public override void Run(string[] args)
        {
            if (ZNet.instance == null || !TestWorlds.IsTestWorld(ZNet.instance.GetWorldName()))
            {
                BuildCommand.Print("vc_seed: test worlds only");
                return;
            }
            if (args.Length > 0 && args[0].ToLowerInvariant() == "off")
            {
                ColonyRandom.SetSeed(null);
                Log.Event("vc_seed: off (unseeded)");
            }
            else if (args.Length > 0 && int.TryParse(args[0], out int seed))
            {
                ColonyRandom.SetSeed(seed);
                Random.InitState(seed);
                Log.Event($"vc_seed: seed {seed}");
            }
            else if (args.Length > 0)
            {
                BuildCommand.Print("vc_seed: a whole number, or off");
                return;
            }
            BuildCommand.Print("vc_seed: " + (ColonyRandom.Seed.HasValue ? "seed " + ColonyRandom.Seed.Value : "unseeded"));
        }
    }

    /// <summary>The watchdog's looks, every 5 s of game time while on; off (and the seed cleared) outside a test world.</summary>
    internal sealed class WatchdogRunner : MonoBehaviour
    {
        internal static readonly Core.Watchdog Dog = new Core.Watchdog();
        internal static bool On;
        private static int s_pieceMask;
        private float _nextLook, _nextWorldCheck;
        /// <summary>A line every 10 minutes of game time, so a run's log shows the watchdog was looking, not only that it said nothing.</summary>
        private const float SummarySeconds = 600f;
        internal static int Looks, LastSettlers, LastHuts, Handovers;
        private static float s_nextSummary;

        /// <summary>A run's start (vc_watchdog on): the counts at 0, the first summary in 10 minutes.</summary>
        internal static void Start()
        {
            Dog.Reset();
            Looks = LastSettlers = LastHuts = Handovers = 0;
            s_nextSummary = Time.time + SummarySeconds;
            On = true;
        }

        private static string Summary() =>
            $"vc_watchdog: working: {Looks} looks, {LastSettlers} settlers and {LastHuts} huts at the last, {Handovers} hand-overs compared, " +
            $"{Dog.Seen.Values.Sum()} breaks";

        internal static string Describe() =>
            "vc_watchdog: " + (On ? "on" : "off") + "; rules " +
            string.Join(", ", Core.Watchdog.Rules.Select(r => r + (Dog.IsOn(r) ? "" : " (off)") + " " + Dog.Seen[r])) +
            "; seed " + (ColonyRandom.Seed.HasValue ? ColonyRandom.Seed.Value.ToString() : "none") + "; " + Summary().Substring("vc_watchdog: ".Length);

        private void Update()
        {
            if (Time.time >= _nextWorldCheck)
            {
                _nextWorldCheck = Time.time + 5f;
                bool testWorld = ZNet.instance != null && TestWorlds.IsTestWorld(ZNet.instance.GetWorldName());
                if (!testWorld && (On || ColonyRandom.Seed.HasValue))
                {
                    On = false;   // never in a player's world: a test world left for another in the same game
                    ColonyRandom.SetSeed(null);
                }
            }
            if (!On || Time.time < _nextLook || ZoneSystem.instance == null) return;
            _nextLook = Time.time + Core.Watchdog.LookSeconds;
            try
            {
                List<SettlerFacts> settlers = Settlers();
                List<HutFacts> huts = Huts();
                Looks++;
                LastSettlers = settlers.Count;
                LastHuts = huts.Count;
                foreach (string line in Dog.Look(Time.time, settlers, huts)) Log.Event(line);
                if (Time.time >= s_nextSummary)
                {
                    s_nextSummary = Time.time + SummarySeconds;
                    Log.Event(Summary());
                }
            }
            catch (System.Exception e)
            {
                Log.Warn($"vc_watchdog: a look failed ({e.GetType().Name}: {e.Message})");
            }
        }

        private static List<SettlerFacts> Settlers()
        {
            if (s_pieceMask == 0) s_pieceMask = LayerMask.GetMask("piece");
            var facts = new List<SettlerFacts>();
            foreach (Citizen c in Citizen.All)
            {
                if (c == null || c.Body == null || c.Body.IsDead()) continue;
                Vector3 at = c.transform.position;
                CitizenAI ai = c.GetComponent<CitizenAI>();
                SettlerPose pose = c.GetComponent<SettlerPose>();
                bool resting = pose != null && pose.Current != SettlerPose.Kind.None;
                Vector3 middle = c.Body.GetCenterPoint();
                facts.Add(new SettlerFacts
                {
                    Id = c.Id,
                    Name = c.CitizenName,
                    X = at.x,
                    Y = at.y,
                    Z = at.z,
                    Ground = Finite(at) ? ZoneSystem.instance.GetGroundHeight(at) : float.NaN,
                    Status = c.Status ?? "",
                    Paused = ai == null || !ai.enabled || ai.HeldForTest,
                    Resting = resting,
                    InsideSolid = Finite(middle) && Physics.CheckSphere(middle, 0.1f, s_pieceMask, QueryTriggerInteraction.Ignore),
                });
            }
            return facts;
        }

        private static List<HutFacts> Huts()
        {
            var facts = new List<HutFacts>();
            foreach (ColonyBuilding b in ColonyBuilding.All)
            {
                WorkerModule work = b != null ? b.GetComponent<WorkerModule>() : null;
                if (work == null || !work.Workers(out List<Citizen> loaded, out List<RosterEntry> elsewhere)) continue;
                facts.Add(new HutFacts
                {
                    Id = b.Id,
                    Name = $"{b.TypeId} {Short(b.Id)}",
                    Workers = loaded.Select(c => c.CitizenName).Concat(elsewhere.Select(e => e.Name)).ToList(),
                    Places = work.Capacity,
                });
            }
            return facts;
        }

        internal static string Short(string id) => string.IsNullOrEmpty(id) ? "?" : id.Substring(0, System.Math.Min(8, id.Length));

        private static bool Finite(Vector3 v) =>
            !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z) && !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && !float.IsInfinity(v.z);
    }

    /// <summary>
    /// The watchdog's 'items' rule: each hand-over between a chest and a settler's load (a deposit, a take: couriers,
    /// builders, workers, the hall) keeps every item. Counted before and after only while the watchdog is on.
    /// </summary>
    internal static class WatchdogHandovers
    {
        internal sealed class Before
        {
            public Dictionary<string, int> Chest, Load;
        }

        private static bool Watching => WatchdogRunner.On && WatchdogRunner.Dog.IsOn("items");

        /// <summary>The chest was loaded again from its saved data during this hand-over, with other contents (a friend's change
        /// or a hand-over between computers, in multiplayer): before and after can't be compared (the review of 26 Oct).</summary>
        private static bool s_reloadedOther;

        internal static Before Count(StorageModule chest, CarryLoad load)
        {
            s_reloadedOther = false;
            return Watching && chest != null && load != null ? new Before { Chest = chest.Contents(), Load = Items(load) } : null;
        }

        internal static void Compare(Before before, StorageModule chest, CarryLoad load, string what)
        {
            if (before == null || chest == null || load == null || s_reloadedOther) return;
            WatchdogRunner.Handovers++;
            ColonyBuilding b = chest.GetComponent<ColonyBuilding>();
            string where = b != null ? $"{b.TypeId} {WatchdogRunner.Short(b.Id)}" : chest.name;
            foreach (string line in WatchdogRunner.Dog.Handover(Time.time, $"{what} at {where}", before.Chest, before.Load, chest.Contents(), Items(load),
                         item => ObjectDB.instance != null && ObjectDB.instance.GetItemPrefab(item) != null))
                Log.Event(line);
        }

        private static Dictionary<string, int> Items(CarryLoad load) => load.ItemNames.ToDictionary(i => i, load.Count);

        [HarmonyPatch]
        private static class ReloadHook
        {
            [HarmonyPatch(typeof(StorageModule), "Reload"), HarmonyPrefix]
            private static void Prefix(StorageModule __instance, out Dictionary<string, int> __state) =>
                __state = Watching ? __instance.Contents() : null;

            [HarmonyPatch(typeof(StorageModule), "Reload"), HarmonyPostfix]
            private static void Postfix(StorageModule __instance, Dictionary<string, int> __state)
            {
                if (__state == null) return;
                Dictionary<string, int> now = __instance.Contents();
                if (now.Count != __state.Count || now.Any(kv => !__state.TryGetValue(kv.Key, out int n) || n != kv.Value)) s_reloadedOther = true;
            }
        }

        [HarmonyPatch]
        private static class DepositHook
        {
            [HarmonyPatch(typeof(StorageModule), nameof(StorageModule.TryDeposit)), HarmonyPrefix]
            private static void Prefix(StorageModule __instance, CarryLoad load, out Before __state) => __state = Count(__instance, load);

            [HarmonyPatch(typeof(StorageModule), nameof(StorageModule.TryDeposit)), HarmonyPostfix]
            private static void Postfix(StorageModule __instance, CarryLoad load, Before __state) => Compare(__state, __instance, load, "a deposit");
        }

        [HarmonyPatch]
        private static class TakeUpToHook
        {
            [HarmonyPatch(typeof(StorageModule), nameof(StorageModule.TakeUpTo)), HarmonyPrefix]
            private static void Prefix(StorageModule __instance, CarryLoad into, out Before __state) => __state = Count(__instance, into);

            [HarmonyPatch(typeof(StorageModule), nameof(StorageModule.TakeUpTo)), HarmonyPostfix]
            private static void Postfix(StorageModule __instance, CarryLoad into, Before __state) => Compare(__state, __instance, into, "a take");
        }

        [HarmonyPatch]
        private static class TakeOneHook
        {
            [HarmonyPatch(typeof(StorageModule), nameof(StorageModule.TryTakeItem)), HarmonyPrefix]
            private static void Prefix(StorageModule __instance, CarryLoad into, out Before __state) => __state = Count(__instance, into);

            [HarmonyPatch(typeof(StorageModule), nameof(StorageModule.TryTakeItem)), HarmonyPostfix]
            private static void Postfix(StorageModule __instance, CarryLoad into, Before __state) => Compare(__state, __instance, into, "a take of one");
        }
    }
}
