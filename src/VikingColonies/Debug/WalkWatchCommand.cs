using System.Collections;
using System.Collections.Generic;
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
    /// vc_walkwatch (1.5.1, the player after their 1.5.0 watch: "I just want it to feel smooth everything"): every walk of a
    /// living colony judged as a watching player's eye would (<see cref="WalkWatch"/>), so a colony watch counts what the
    /// eye sees, not only what goes wrong. The round-the-back walks into huts were seen by the player, never by a count.
    /// </summary>
    internal sealed class WalkWatchCommand : ConsoleCommand
    {
        public override string Name => "vc_walkwatch";
        public override string Help => "VikingColonies (test worlds): 'vc_walkwatch on|off': every walk of every settler this computer runs, judged as a " +
                                       "watching player would: a long way round (over half as long again as the straight way, and 8 m) or standing " +
                                       "still on the way (over 2 s, away from a door); each in the log, and the counts when it's turned off";
        public override bool IsCheat => true;

        public override void Run(string[] args)
        {
            if (DevHelpers.RefuseOutsideTestWorld(Name)) return;
            string word = args.Length > 0 ? args[0] : "";
            BuildCommand.Print(word == "on" ? WalkWatcher.Start() : word == "off" ? WalkWatcher.Stop() : "vc_walkwatch on|off");
        }
    }

    internal static class WalkWatcher
    {
        private const float EverySeconds = 0.25f;
        /// <summary>A walk asked for within this long is still going on (the settlers' brains run every frame or so).</summary>
        private const float WalkingWithin = 0.6f;
        /// <summary>Within this of a door (and on its floor), standing is opening it or waiting a turn.</summary>
        private const float NearDoor = 3f;

        private sealed class Hut
        {
            public Footprint Walls;
            public Vec3 Door;
        }

        private static Coroutine s_running;
        private static readonly Dictionary<string, WalkWatch> s_watches = new Dictionary<string, WalkWatch>();
        private static readonly Dictionary<string, int> s_arrivals = new Dictionary<string, int>();
        private static readonly List<Vector3> s_doors = new List<Vector3>();
        private static readonly List<Hut> s_huts = new List<Hut>();
        private static int s_walks, s_long, s_still;
        private static bool s_errorTold;

        public static string Start()
        {
            if (s_running != null) return "vc_walkwatch: already on";
            s_watches.Clear();
            s_arrivals.Clear();
            s_walks = s_long = s_still = 0;
            s_errorTold = false;
            s_running = Plugin.Instance.StartCoroutine(Loop());
            Log.Event("vc_walkwatch: on");
            return "vc_walkwatch: on: every settler's walk is judged; 'vc_walkwatch off' for the counts";
        }

        public static string Stop()
        {
            if (s_running != null) Plugin.Instance.StopCoroutine(s_running);
            s_running = null;
            string counts = $"vc_walkwatch: {s_walks} walks seen through, {s_long} a long way round, {s_still} stood still on the way";
            Log.Event(counts);
            return counts;
        }

        private static IEnumerator Loop()
        {
            while (true)
            {
                yield return new WaitForSeconds(EverySeconds);
                // Out of the test world (left, or another loaded): it stops (its review: it ran on in any world).
                if (ZNet.instance == null || !TestWorlds.IsTestWorld(ZNet.instance.GetWorldName()))
                {
                    s_running = null;
                    Log.Event("vc_walkwatch: off (out of the test world)");
                    yield break;
                }
                try { Sample(); }
                catch (System.Exception e)
                {
                    if (!s_errorTold) Log.Warn($"vc_walkwatch: {e.Message}");   // once, not four times a second
                    s_errorTold = true;
                }
            }
        }

        private static void Sample()
        {
            if (Player.m_localPlayer == null) return;
            float now = Time.time;
            s_doors.Clear();
            foreach (Door door in Jobs.WorldScan.Doors())
                if (door != null) s_doors.Add(door.transform.position);
            s_huts.Clear();
            foreach (ColonyBuilding building in ColonyBuilding.All)
            {
                ConstructionModule site = building != null && building.IsValid ? building.GetComponent<ConstructionModule>() : null;
                if (site == null || site.IsCamp || site.RoomFootprint == null) continue;
                Door door = HutDoors.DoorOf(site);
                if (door != null) s_huts.Add(new Hut { Walls = site.RoomFootprint.Grow(-SiteSurvey.SiteMargin), Door = CoreVectors.V(door.transform.position) });
            }
            foreach (Citizen c in Citizen.All)
            {
                if (c == null) continue;
                if (!c.IsOwner)
                {
                    s_watches.Remove(c.Id);   // another computer runs them: what was open here isn't known (its review)
                    continue;
                }
                CitizenAI ai = c.GetComponent<CitizenAI>();
                if (ai == null) continue;
                Vector3 at = c.transform.position;
                bool walking = now - ai.LastWalkAt < WalkingWithin && !ai.WalkArrived;
                // By a door only by being near it: the whole walk up to a door, from however far, isn't (its review).
                bool nearDoor = false;
                for (int i = 0; !nearDoor && i < s_doors.Count; i++)
                    nearDoor = Utils.DistanceXZ(s_doors[i], at) < NearDoor && Mathf.Abs(s_doors[i].y - at.y) < 2.5f;
                if (!s_watches.TryGetValue(c.Id, out WalkWatch watch)) s_watches[c.Id] = watch = new WalkWatch();
                s_arrivals.TryGetValue(c.Id, out int seen);
                Vec3? arrived = ai.Arrivals != seen ? CoreVectors.V(ai.ArrivedGoal) : (Vec3?)null;
                s_arrivals[c.Id] = ai.Arrivals;
                WalkEnd? end = watch.Sample(now, CoreVectors.V(at), walking, CoreVectors.V(ai.WalkGoal), nearDoor, arrived);
                if (end.HasValue) Judge(c, end.Value);
            }
        }

        private static Hut HutAround(Vec3 point)
        {
            foreach (Hut hut in s_huts)
                if (hut.Walls.Contains(point)) return hut;
            return null;
        }

        private static void Judge(Citizen c, WalkEnd walk)
        {
            string doing = string.IsNullOrEmpty(c.Status) ? "-" : Localization.instance.Localize(c.Status);
            if (walk.Reached)
            {
                s_walks++;
                // Into or out of a hut, the fair way is by its door, not the straight line through its wall (its review).
                Hut fromHut = HutAround(walk.From), toHut = HutAround(walk.Goal);
                bool throughDoor = fromHut != toHut;
                float fair = WalkCheck.FairWay(walk.From, walk.Goal, fromHut?.Walls, fromHut != null ? fromHut.Door : walk.From,
                    toHut?.Walls, toHut != null ? toHut.Door : walk.Goal);
                if (walk.LongWay(fair, throughDoor))
                {
                    s_long++;
                    Log.Info($"Walk watch: {c.CitizenName} took a long way round to {P(walk.Goal)}: walked {walk.Walked:0} m for a fair way of {fair:0} m " +
                             $"({(throughDoor ? "by a hut's door" : "straight")}), from {P(walk.From)}, in {walk.Seconds:0} s ('{doing}')");
                }
            }
            if (walk.StoodStill)
            {
                s_still++;
                Log.Info($"Walk watch: {c.CitizenName} stood still {walk.LongestStop:0.0} s on the way to {P(walk.Goal)} (from {P(walk.From)}, " +
                         $"{(walk.Reached ? "got there" : "went elsewhere")}, '{doing}')");
            }
        }

        private static string P(Vec3 v) => string.Format(System.Globalization.CultureInfo.InvariantCulture, "({0:0.0}, {1:0.0})", v.X, v.Z);
    }
}
