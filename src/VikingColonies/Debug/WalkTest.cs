using System.Collections;
using System.Collections.Generic;
using System.Linq;
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
    /// vc_walktest (test worlds): settlers' walks into and out of every hut, door shut (1.5.1, the player's 1.5.0 watch:
    /// settlers went round the back of a hut before going in at its door; "I just want it to feel smooth").
    /// </summary>
    internal sealed class WalkTestCommand : ConsoleCommand
    {
        public override string Name => "vc_walktest";
        public override string Help => "VikingColonies (test worlds): 'vc_walktest [hut|all] [level] [shelter]' puts each hut (or one) up here at once at " +
                                       "that level (1 by default), walks a settler in to its chest from four sides, running in to shelter and out to " +
                                       "behind it, its door shut each time, and checks every walk keeps near the shortest way round with no standing " +
                                       "about. With 'shelter': a settler sheltering in it is hit by a monster at its door (they stay in) and by one in " +
                                       "there with them (they leave it). Removes what it made.";
        public override bool IsCheat => true;

        public override void Run(string[] args)
        {
            Player p = Player.m_localPlayer;
            if (p == null || DevHelpers.RefuseOutsideTestWorld(Name)) return;
            string word = args.Length > 0 ? args[0] : "all";
            List<BuildingType> types = word == "all"
                ? BuildingTypes.All.Where(t => t != BuildingTypes.TownHall).ToList()
                : new List<BuildingType> { DevHelpers.TypeByWord(word) }.Where(t => t != null && t != BuildingTypes.TownHall).ToList();
            int level = args.Length > 1 && int.TryParse(args[1], out int l) ? Mathf.Clamp(l, 1, HutLevels.MaxLevel) : 1;
            if (types.Count == 0)
            {
                BuildCommand.Print($"vc_walktest: no hut called '{word}'");
                return;
            }
            bool shelter = args.Length > 2 && args[2] == "shelter";
            Plugin.Instance.StartCoroutine(WalkTest.Run(p, types, level, shelter));
        }
    }

    internal static class WalkTest
    {
        /// <summary>Walks start this far from the middle of the hut.</summary>
        private const float StartOut = 20f;
        private const float SampleSeconds = 0.2f;
        private const float WalkLimit = 60f;
        /// <summary>Within this of the door, standing still is opening it or waiting a turn.</summary>
        private const float NearDoor = 3f;
        /// <summary>The walk out ends this far behind the back wall (a Longhouse household off to work behind it, 0.20).</summary>
        private const float BehindWall = 5f;
        /// <summary>A walk ended this near its goal, on its side of the walls, got there (a test walk stops within 1 m, and a margin).</summary>
        private const float ReachedWithin = 2.5f;
        /// <summary>
        /// Between walks the walker rests until let go for the next (2026-10-24's game run: in a second's rest their free time
        /// began and they sat down on the ground, and getting up took 2.6-3 s of the next walk, counted as standing still).
        /// </summary>
        private const float RestBetween = 3600f;
        /// <summary>A failed walk's line has where they were this often (2026-10-24: a walk too long, and nothing said where).</summary>
        private const float TrackSeconds = 1f;
        /// <summary>
        /// Before each hut the ground this far each way round its spot goes back to the flat world's meadow (2026-10-24: each
        /// hut levelled the one spot 0.13 m below the last, and runs inherited earlier runs' pits, up to 2.8 m deep). Every
        /// level-5 lot is under 22 m across, its edge smoothed beyond it, and the walks start 20 m from its middle.
        /// </summary>
        private const float GroundHalf = 23f;
        /// <summary>The ground counts as back when every metre of the inner square is this near the meadow's height.</summary>
        private const float GroundWithin = 0.1f;
        /// <summary>Between batches of levellings, as a builder waits between its own (ConstructionModule's FinishStepSeconds), and a margin.</summary>
        private const float LevelSettle = 0.4f;

        private static int s_pass, s_fail;

        /// <summary>A walk out begins where the walker stands when they're this near its start (where the walk in ended).</summary>
        private const float OutFromWhereTheyStand = 2.5f;

        private struct Walk
        {
            public string Name;
            public Vector3 Start, Goal;
            public bool Hurry, Out;
        }

        public static IEnumerator Run(Player player, List<BuildingType> types, int level, bool shelter = false)
        {
            s_pass = s_fail = 0;
            Log.Event($"vc_walktest: starting, {types.Count} huts at level {level}{(shelter ? ", sheltering" : "")}");
            yield return Walks(player, types, level, shelter);
            // Said whatever happened, so a scenario waiting for it doesn't wait out its time (its review).
            Log.Event($"vc_walktest: done — {s_pass} passed, {s_fail} failed");
            player.Message(MessageHud.MessageType.Center, $"vc_walktest: {s_pass} passed, {s_fail} failed");
        }

        private static IEnumerator Walks(Player player, List<BuildingType> types, int level, bool shelter)
        {
            if (!player.InGodMode()) player.SetGodMode(true);
            Vector3 origin = player.transform.position;
            Vector3 forward = player.transform.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 0.01f ? forward.normalized : Vector3.forward;
            Quaternion facing = Quaternion.LookRotation(forward);
            var before = new HashSet<string>(Citizen.All.Where(c => c != null).Select(c => c.Id));
            Vector3 hutAt = origin + forward * 15f;
            ColonyBuilding hall = null;
            ColonyBuilding hut = null;
            var paused = new List<CitizenAI>();
            CitizenAI walker = null;
            EnvMan.instance.m_debugTimeOfDay = true;
            EnvMan.instance.m_debugTime = 0.5f;   // midday: night would take them indoors
            try
            {
                hall = DevHelpers.Place(BuildingTypes.TownHall.PrefabName, origin - forward * 27f, facing, player.GetPlayerID());
                TownHallModule townHall = hall != null ? hall.GetComponent<TownHallModule>() : null;
                for (float t = 0f; townHall != null && !townHall.Founded && t < 30f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                if (townHall == null || !townHall.Founded)
                {
                    Check("A Town Hall founded for the walks", false);
                    yield break;
                }
                DevHelpers.KeepNothing(hall);
                hall.SetLevel(TownHallLevels.MaxLevel);   // huts may go to level 5, and the colony's area reaches every start
                Citizen walking = null;
                for (float t = 0f; walking == null && t < 60f; t += 1f)
                {
                    walking = Citizen.All.FirstOrDefault(c => c != null && c.ColonyId == hall.ColonyId && c.Body != null && !before.Contains(c.Id));
                    if (walking == null) yield return new WaitForSeconds(1f);
                }
                walker = walking != null ? walking.GetComponent<CitizenAI>() : null;
                if (walker == null)
                {
                    Check("A settler of the test's colony to walk", false);
                    yield break;
                }
                walker.PlaceForTest(walking.transform.position);   // up from the ground, if they sat down: long done before the first walk
                walker.PauseForTest(RestBetween);
                yield return new WaitForSeconds(3f);   // the others of the colony have come by now: they stand by the camp
                foreach (Citizen other in Citizen.All.Where(c => c != null && c != walking && c.ColonyId == hall.ColonyId).ToList())
                {
                    CitizenAI ai = other.GetComponent<CitizenAI>();
                    if (ai == null) continue;
                    ai.PlaceForTest(hall.transform.position - forward * 6f);
                    ai.PauseForTest(3600f);
                    paused.Add(ai);
                }
                Log.Event($"vc_walktest: {walking.CitizenName} walks (runs at {walking.Body.m_runSpeed:0.0} m/s, jogs at {walking.Body.m_speed:0.0}); " +
                          $"the walking map's tiles are {TileSize():0} m");

                foreach (BuildingType type in types)
                {
                    DevHelpers.ClearMonsters(hutAt, 60f);
                    bool flat = true;
                    yield return LevelGround(hutAt, ok => flat = ok);
                    if (!flat)
                    {
                        Check($"The ground at the {type.Id}'s spot back at the meadow's {FlatTestWorld.Height:0.0} m for the walks", false);
                        continue;
                    }
                    hut = DevHelpers.Place(type.PrefabName, hutAt, facing, player.GetPlayerID());
                    yield return BuildRun.AtOnceToLevel(hut, level);
                    ConstructionModule site = hut != null ? hut.GetComponent<ConstructionModule>() : null;
                    for (float t = 0f; hut != null && hut.ColonyId != hall.ColonyId && t < 10f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                    if (site == null || !site.IsBuilt || hut.Level != level)
                    {
                        Check($"The {type.Id} put up at level {level} for the walks (built {site != null && site.IsBuilt}, level {(hut != null ? hut.Level : 0)})", false);
                        yield return RemoveHut(hut, hutAt);
                        hut = null;
                        continue;
                    }
                    // The earth under its roof painted first: painted later, the walking map is remade mid-walk (the self-test, 1.4.3).
                    if (!site.EarthPaintedForTest) site.PaintEarthNow();
                    for (float t = 0f; !site.EarthPaintedForTest && t < 10f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                    Door door = DevHelpers.DoorOf(site);
                    Footprint room = site.RoomFootprint;
                    if (door == null || room == null)
                    {
                        Check($"{type.Id} level {level}: no door{(room == null ? " or walls" : "")}, nothing to walk through", true);
                        yield return RemoveHut(hut, hutAt);
                        hut = null;
                        continue;
                    }
                    if (DoorUser.IsOpen(door)) DoorUser.CloseForTest(door);
                    yield return new WaitForSeconds(12f);   // the game's walking map is remade from what stands every 10 s
                    if (shelter) yield return Shelter(type.Id, level, hut, door, room, walker, walking);
                    else
                        foreach (Walk walk in WalksFor(hut, door, room, walking.Id))
                            yield return Try(type.Id, level, walk, walker, door, room, site.SiteFootprint ?? room);
                    yield return RemoveHut(hut, hutAt);
                    hut = null;
                }
            }
            finally
            {
                if (walker != null) walker.AttackForTest = false;
                EnvMan.instance.m_debugTimeOfDay = false;
                foreach (CitizenAI ai in paused.Where(a => a != null)) ai.PauseForTest(0f);
                if (walker != null)
                {
                    walker.TestWalk(walker.transform.position, 0f);
                    walker.PauseForTest(0f);
                }
            }
            if (hut != null) yield return RemoveHut(hut, hutAt);
            yield return LevelGround(hutAt, ok => { });   // the flat world left as it was found, for the next run
            foreach (Citizen c in Citizen.All.Where(c => c != null && !before.Contains(c.Id)).ToList()) Remove(c.gameObject);
            if (hall != null) Remove(hall.gameObject);
            Log.Event("vc_walktest: cleaned up");
        }

        /// <summary>
        /// In to its chest from the front, both sides and the back; running in to shelter from a back corner; and from the
        /// shelter spot out to behind the back wall.
        /// </summary>
        private static List<Walk> WalksFor(ColonyBuilding hut, Door door, Footprint room, string walkerId)
        {
            Footprint walls = room.Grow(-SiteSurvey.SiteMargin);
            Vector3 centre = CoreVectors.U(walls.Centre);
            Vector3 outward = door.transform.forward;
            outward.y = 0f;
            outward = outward.normalized;
            if (Vector3.Dot(outward, door.transform.position - centre) < 0f) outward = -outward;   // away from its middle
            Vector3 side = Vector3.Cross(Vector3.up, outward);
            var walks = new List<Walk>();
            Vector3 Out(Vector3 way) => OnGround(centre + way.normalized * StartOut);
            if (BuilderJob.OpenSideOf(hut.transform, out Vector3 chestSide))
            {
                walks.Add(new Walk { Name = "in to its chest from the front", Start = Out(outward), Goal = chestSide });
                walks.Add(new Walk { Name = "in to its chest from the left", Start = Out(side), Goal = chestSide });
                walks.Add(new Walk { Name = "in to its chest from the right", Start = Out(-side), Goal = chestSide });
                walks.Add(new Walk { Name = "in to its chest from the back", Start = Out(-outward), Goal = chestSide });
            }
            else Log.Event($"vc_walktest: {hut.TypeId}: no open side by its chest, no walks to it");
            Vector3 chest = hut.transform.position;
            Vector3 inward = -outward;
            GroundPoint spot = CitizenAI.ShelterSpot(new GroundPoint(chest.x, chest.z), new GroundPoint(door.transform.position.x, door.transform.position.z),
                new GroundPoint(inward.x, inward.z), walkerId, chest.y);   // the one they'd shelter on, inside its walls (1.5.1)
            Vector3 shelter = new Vector3(spot.X, chest.y, spot.Z);
            walks.Add(new Walk { Name = "running in to shelter from a back corner", Start = Out(side - outward), Goal = shelter, Hurry = true });
            float back = 0f;
            while (back < 30f && walls.Contains(CoreVectors.V(centre - outward * back))) back += 0.25f;
            walks.Add(new Walk { Name = "out to behind its back wall", Start = shelter, Goal = OnGround(centre - outward * (back + BehindWall)), Out = true });
            return walks;
        }

        /// <param name="outline">All the hut's pieces on the ground (its site), the shortest way goes round: a lean-to or a woodpile
        /// past its room's end wall can't be walked through (2026-10-24: the level-5 Builder's Hut's lean-to, 4 m past its
        /// room, was gone round tight and the walk counted 1 m too long against the room's walls).</param>
        private static IEnumerator Try(string hutType, int level, Walk walk, CitizenAI walker, Door door, Footprint room, Footprint outline)
        {
            if (DoorUser.IsOpen(door)) DoorUser.CloseForTest(door);
            yield return new WaitForSeconds(1.5f);   // its swing ends
            // The walk out begins where the walk in to shelter ended, when it got there (2026-10-25: placed on the spot, a
            // walker stood in the Lumberjack's Hut's workbench and never moved).
            if (walk.Out && Flat(walker.transform.position, walk.Start) <= OutFromWhereTheyStand) walk.Start = walker.transform.position;
            else walker.PlaceForTest(walk.Start);
            walker.PauseForTest(RestBetween);
            walker.ForgetDoorsForTest();   // a first walk (its review: four walks to one chest refused the door just used)
            Footprint walls = room.Grow(-SiteSurvey.SiteMargin), pieces = outline.Grow(-SiteSurvey.SiteMargin);
            Vector3 doorAt = door.transform.position;
            // The game's walking map shut at the door too: it is remade from what stands every 10 s, and the last walk had
            // the door open a moment (its review: a map still open there would walk them straight in, past the test).
            Vector3 centre = CoreVectors.U(walls.Centre);
            Vector3 outward = doorAt - centre;
            outward.y = 0f;
            Vector3 outside = OnGround(doorAt + outward.normalized * 1.5f), inside = OnGround(doorAt - outward.normalized * 1.5f);
            for (float t = 0f; walker.WayReaches(outside, inside) && t < 15f; t += 0.5f) yield return new WaitForSeconds(0.5f);
            if (walker.WayReaches(outside, inside))
            {
                Check($"{hutType} level {level}, {walk.Name}: the walking map still goes through its shut door after 15 s, nothing to test", false);
                yield break;
            }
            // ...and has a way from the start to its side of the door (2026-10-24: at level 5 the first walk of 5 huts stood
            // 5 s at the start on both codes, "the walking map said 0": its map not yet made round a hut put up 1 to 5 at once).
            Vector3 near = walls.Contains(CoreVectors.V(walk.Start)) ? inside : outside;   // the door's side the walk starts on (a Watchtower's shelter spot was outside its walls)
            for (float t = 0f; !walker.WayReaches(walk.Start, near) && t < 15f; t += 0.5f) yield return new WaitForSeconds(0.5f);
            if (!walker.WayReaches(walk.Start, near))
            {
                Check($"{hutType} level {level}, {walk.Name}: the walking map has no way from the start to the door after 15 s, nothing to test", false);
                yield break;
            }
            // ...and to the door's own stand-off spots on that side, the ones the settler's door check walks to (1.5.1's walking
            // check, 26 Oct: the first walk after the Builder's Hut went up to level 5 at once had a way to the spot 1.5 m out
            // but to neither of the door's, "the walking map said 0", and went 35 m for 21 round the half-made map).
            // The side the walk starts on is the side of its spot by the door, not of its start: a start behind or beside the hut
            // lies behind the door's line too (the first fix's run: 56 walks from the back and sides waited 15 s for spots inside).
            int startSide = DoorRules.SideOf(CoreVectors.V(doorAt), CoreVectors.V(door.transform.forward), CoreVectors.V(near));
            for (float t = 0f; !ReachesStandOff(walker, walk.Start, door, startSide) && t < 15f; t += 0.5f) yield return new WaitForSeconds(0.5f);
            // Some huts' inside spots stand in their furniture for good (the door check then goes by the hut's shape): walk anyway.
            if (!ReachesStandOff(walker, walk.Start, door, startSide))
                BuildCommand.Print($"vc_walktest: {hutType} level {level}, {walk.Name}: no way to the door's own spots on side {startSide} after 15 s; walking anyway");
            yield return new WaitForSeconds(0.5f);
            float shortest = walk.Out
                ? Flat(walk.Start, doorAt) + WalkCheck.ShortestAround(CoreVectors.V(doorAt), CoreVectors.V(walk.Goal), pieces)
                : WalkCheck.ShortestAround(CoreVectors.V(walk.Start), CoreVectors.V(doorAt), pieces) + Flat(doorAt, walk.Goal);
            // The game's own way from the start to its side of the door, for a failed walk's line (2026-10-24: its corners
            // went 9-14 m out from a 4 by 6 m hut on open ground; was it the game's way, or ours?).
            List<Vector3> gameWay = walker.WayForTest(walk.Start, near);
            var trace = new WalkTrace();
            float awayMetres = 0f, awaySeconds = 0f;
            Vector3 last = walker.transform.position;
            float started = Time.time;
            var track = new List<string> { Where(last) };
            float trackAt = TrackSeconds;
            walker.PauseForTest(0f);   // let go now, standing
            walker.TestWalk(walk.Goal, WalkLimit + 5f, walk.Hurry);
            trace.Add(0f, CoreVectors.V(last), Flat(last, doorAt) < NearDoor);
            while (!walker.TestWalkDone && Time.time - started < WalkLimit)
            {
                yield return new WaitForSeconds(SampleSeconds);
                if (walker == null) yield break;
                Vector3 at = walker.transform.position;
                bool nearDoor = Flat(at, doorAt) < NearDoor;
                trace.Add(Time.time - started, CoreVectors.V(at), nearDoor);
                if (Time.time - started >= trackAt)
                {
                    trackAt += TrackSeconds;
                    track.Add(Where(at) + Heading(walker));
                }
                if (!nearDoor && !walls.Contains(CoreVectors.V(at)))   // out in the open: the pace they keep
                {
                    awayMetres += Flat(at, last);
                    awaySeconds += SampleSeconds;
                }
                last = at;
            }
            Vector3 end = walker.transform.position;
            // Got there: on the goal's side of the walls and by it, not at the end of a way behind a wall (its review).
            bool reached = walker.TestWalkDone && walls.Contains(CoreVectors.V(end)) == walls.Contains(CoreVectors.V(walk.Goal)) && Flat(end, walk.Goal) <= ReachedWithin;
            float seconds = Time.time - started;
            walker.TestWalk(walk.Goal, 0f);
            walker.PauseForTest(RestBetween);
            track.Add(Where(end));
            bool ok = reached && WalkCheck.Smooth(trace.Walked, shortest, trace.LongestStop);
            Check($"{hutType} level {level}, {walk.Name}{(walk.Hurry ? " (running)" : "")}: walked {trace.Walked:0.0} m, the shortest way {shortest:0.0} m, " +
                  $"stood still {trace.LongestStop:0.0} s away from the door, {seconds:0.0} s, {(awaySeconds > 0f ? awayMetres / awaySeconds : 0f):0.0} m/s out in the open" +
                  (reached ? "" : $", never got there (stopped {Flat(end, walk.Goal):0.0} m off{(walls.Contains(CoreVectors.V(end)) != walls.Contains(CoreVectors.V(walk.Goal)) ? ", the walls between" : "")})") +
                  (ok ? "" : $"; the door at {Where(doorAt)}, the walls round {Where(centre)} ({walls.HalfX * 2f:0.0} by {walls.HalfZ * 2f:0.0} m), the goal {Where(walk.Goal)}; " +
                             $"the game's way from the start to the door {WayLength(walk.Start, gameWay):0.0} m: {string.Join(" ", gameWay.Select(Where))}; " +
                             $"where they were each second: {string.Join(" ", track)}"),
                ok);
        }

        private sealed class Outcome
        {
            public bool Ok;
        }

        /// <summary>
        /// Sheltering in the hut from an attack (1.5.1, the player's 1.5.0 try: a Troll at the hut hit the forager inside, who
        /// walked out to another hut, into it): hit by a monster outside at its door they stay in; hit through its back wall
        /// they leave by the clear door; hit by one in there with them, by the door, they leave it (since 0.5); hurt to half
        /// with one at the door they make a run for it.
        /// </summary>
        private static IEnumerator Shelter(string hutType, int level, ColonyBuilding hut, Door door, Footprint room, CitizenAI walker, Citizen who)
        {
            Footprint walls = room.Grow(-SiteSurvey.SiteMargin);
            Vector3 centre = CoreVectors.U(walls.Centre);
            Vector3 outward = door.transform.forward;
            outward.y = 0f;
            outward = outward.normalized;
            if (Vector3.Dot(outward, door.transform.position - centre) < 0f) outward = -outward;
            float back = 0f;
            while (back < 30f && walls.Contains(CoreVectors.V(centre - outward * back))) back += 0.25f;
            Vector3 atDoor = door.transform.position + outward * 1.5f;          // a Troll reaching in at the door
            Vector3 behind = centre - outward * (back + 1.5f);                  // one hitting through the back wall
            Vector3 byDoorInside = door.transform.position - outward * 1f;      // one that followed them in, stopped by the door
            string name = $"{hutType} level {level}: sheltering";
            float fullHealth = who.Body.GetMaxHealth();
            walker.AttackForTest = true;
            walker.PauseForTest(0f);   // their rest between huts over: in to shelter
            try
            {
                var inside = new Outcome();
                yield return GoIn(walker, hut, door, outward, inside);
                if (!inside.Ok)
                {
                    Check($"{name}, a settler goes in (in hut '{walker.RefugeIdForTest}', inside {walker.InsideForTest})", false);
                    yield break;
                }
                int gaveUp = walker.RefugesGivenUp;
                yield return Hits(who, walker, atDoor, gaveUp, 6f);
                Check($"{name}, hit three times by a monster outside at its door, they stay in (gave it up {walker.RefugesGivenUp - gaveUp} times, " +
                      $"inside {walker.InsideForTest})", walker.RefugesGivenUp == gaveUp && walker.RefugeIdForTest == hut.Id && walker.InsideForTest);
                yield return Hits(who, walker, behind, gaveUp, 6f);
                Check($"{name}, hit through its back wall, they leave by the clear door (gave it up {walker.RefugesGivenUp - gaveUp} times)", walker.RefugesGivenUp > gaveUp);

                yield return GoIn(walker, hut, door, outward, inside);
                gaveUp = walker.RefugesGivenUp;
                if (inside.Ok) yield return Hits(who, walker, byDoorInside, gaveUp, 6f);
                Check($"{name}, hit by a monster in there with them by the door, they leave it (in: {inside.Ok}, gave it up {walker.RefugesGivenUp - gaveUp} times)",
                    inside.Ok && walker.RefugesGivenUp > gaveUp);

                yield return GoIn(walker, hut, door, outward, inside);
                gaveUp = walker.RefugesGivenUp;
                who.Body.SetHealth(fullHealth * 0.4f);
                if (inside.Ok) yield return Hits(who, walker, atDoor, gaveUp, 6f);
                Check($"{name}, hurt to 40% with a monster at its door, they make a run for it (in: {inside.Ok}, gave it up {walker.RefugesGivenUp - gaveUp} times)",
                    inside.Ok && walker.RefugesGivenUp > gaveUp);
            }
            finally
            {
                walker.AttackForTest = false;
                walker.ForgetBadRefugesForTest();
                walker.PauseForTest(RestBetween);
                if (who != null && who.Body != null) who.Body.SetHealth(fullHealth);
            }
            yield return new WaitForSeconds(2f);
        }

        /// <summary>The walker in the hut to shelter (from just outside its door when not in there already), on their spot.</summary>
        private static IEnumerator GoIn(CitizenAI walker, ColonyBuilding hut, Door door, Vector3 outward, Outcome inside)
        {
            walker.ForgetBadRefugesForTest();
            // Its door as on a first try (2026-10-24's game runs: a passing the last check cut short left the door "tried
            // lately" for this goal, and they stood outside it until the 40 s were up, in two huts of 14).
            walker.ForgetDoorsForTest();
            if (!(walker.InsideForTest && walker.RefugeIdForTest == hut.Id))
            {
                if (DoorUser.IsOpen(door)) DoorUser.CloseForTest(door);
                walker.PlaceForTest(OnGround(door.transform.position + outward * 4f));
            }
            for (float t = 0f; !(walker.InsideForTest && walker.RefugeIdForTest == hut.Id) && t < 40f; t += 0.5f) yield return new WaitForSeconds(0.5f);
            inside.Ok = walker.InsideForTest && walker.RefugeIdForTest == hut.Id;
            if (inside.Ok) yield return new WaitForSeconds(3f);   // on their spot
        }

        /// <summary>A monster standing there hits them every 2 s for a while, or until they give the hut up.</summary>
        private static IEnumerator Hits(Citizen who, CitizenAI walker, Vector3 from, int gaveUp, float seconds)
        {
            for (float t = 0f; walker.RefugesGivenUp == gaveUp && t < seconds; t += 0.5f)
            {
                if (Mathf.Repeat(t, 2f) < 0.01f) who.NoteMonsterHitForTest(from);
                yield return new WaitForSeconds(0.5f);
            }
        }

        private static string Where(Vector3 at) => $"({at.x:0.0}; {at.z:0.0})";

        /// <summary>The game's walking map is made in square tiles joined at their edges (2026-10-24: a way bent through a tile's corner by a hut).</summary>
        private static float TileSize()
        {
            object size = Pathfinding.instance != null ? HarmonyLib.AccessTools.Field(typeof(Pathfinding), "m_tileSize")?.GetValue(Pathfinding.instance) : null;
            return size is float f ? f : 0f;
        }

        private static float WayLength(Vector3 from, List<Vector3> way)
        {
            float length = 0f;
            foreach (Vector3 corner in way)
            {
                length += Flat(from, corner);
                from = corner;
            }
            return length;
        }

        /// <summary>
        /// Where the game's path took them next and where it ended, and "door" while passing one (2026-10-24's runs: past the
        /// door and back, and the track alone didn't say why).
        /// </summary>
        private static string Heading(CitizenAI walker)
        {
            List<Vector3> way = walker.WayForTest();
            return (way.Count > 0 ? $">{Where(way[0])}{(way.Count > 1 ? $"..{Where(way[way.Count - 1])}" : "")}" : "") + (walker.UsingDoor ? " door" : "");
        }

        /// <summary>
        /// The flat test world's ground round the hut's spot put back to its meadow's height (up to three passes, as a
        /// builder levels), so every hut goes up on ground as the world made it. Elsewhere the ground stays as it is.
        /// </summary>
        private static IEnumerator LevelGround(Vector3 at, System.Action<bool> done)
        {
            if (ZNet.instance == null || !TestWorlds.IsFlatTestWorld(ZNet.instance.GetWorldName()))
            {
                done(true);
                yield break;
            }
            if (!GroundShaper.Ready)
            {
                Log.Event("vc_walktest: no hoe's levelling in the game, the ground at the test spot stays as it is");
                done(false);
                yield break;
            }
            float height = FlatTestWorld.Height;
            GroundSpan(at, out float lowWas, out float highWas, out Vector3 lowAt);
            float low = lowWas, high = highWas;
            for (int pass = 0; pass < 3 && (height - low > GroundWithin || high - height > GroundWithin); pass++)
            {
                foreach (List<GroundPoint> batch in WalkCheck.LevelBatches(new GroundPoint(at.x, at.z), GroundHalf, GroundPlan.LevelRadius))
                {
                    foreach (GroundPoint spot in batch) GroundShaper.Level(new Vector3(spot.X, height, spot.Z));
                    yield return new WaitForSeconds(LevelSettle);   // the ground settles before the next batch reads it
                }
                yield return new WaitForSeconds(2f);   // the ground's shape and its colliders made again
                GroundSpan(at, out low, out high, out lowAt);
            }
            foreach (ItemDrop item in Object.FindObjectsByType<ItemDrop>(FindObjectsSortMode.None))   // anything the levelling turned up
                if (item != null && Flat(item.transform.position, at) <= GroundHalf + 2f) Remove(item.gameObject);
            bool level = height - low <= GroundWithin && high - height <= GroundWithin;
            if (lowWas != low || highWas != high || !level)
                Log.Event($"vc_walktest: the ground at the test spot {(level ? "back at" : "NOT back at")} the meadow's {height:0.0} m " +
                          $"(it was {lowWas:0.00} to {highWas:0.00} m, now {low:0.00} to {high:0.00} m{(level ? "" : $", the lowest at {Where(lowAt)}")})");
            done(level);
        }

        /// <summary>The lowest and highest ground on a metre's grid within the levelled square, a levelling's reach in from its edge.</summary>
        private static void GroundSpan(Vector3 at, out float low, out float high, out Vector3 lowAt)
        {
            lowAt = at;
            low = float.MaxValue;
            high = float.MinValue;
            float inner = GroundHalf - GroundPlan.LevelRadius;
            for (float x = -inner; x <= inner; x += 1f)
                for (float z = -inner; z <= inner; z += 1f)
                {
                    float y = ZoneSystem.instance.GetGroundHeight(new Vector3(at.x + x, at.y, at.z + z));
                    if (y < low) lowAt = new Vector3(at.x + x, y, at.z + z);
                    low = Mathf.Min(low, y);
                    high = Mathf.Max(high, y);
                }
        }

        private static IEnumerator RemoveHut(ColonyBuilding hut, Vector3 at)
        {
            if (hut != null) Remove(hut.gameObject);
            yield return new WaitForSeconds(1f);
            DevHelpers.RemoveOrphanPieces(at, 30f);
            yield return new WaitForSeconds(1f);
        }

        private static float Flat(Vector3 a, Vector3 b) => Utils.DistanceXZ(a, b);

        /// <summary>The walking map has a way from here to one of the door's stand-off spots on this side (side 0, in the doorway: yes).</summary>
        private static bool ReachesStandOff(CitizenAI walker, Vector3 from, Door door, int side) =>
            side == 0 || DoorRules.StandOffs.Any(d => walker.WayReaches(from, DoorUser.Spot(door, side, d)));

        private static Vector3 OnGround(Vector3 at)
        {
            at.y = ZoneSystem.instance.GetGroundHeight(at);
            return at;
        }

        private static void Remove(GameObject go)
        {
            ZNetView nview = go.GetComponent<ZNetView>();
            if (nview != null && nview.IsValid() && !nview.IsOwner()) nview.ClaimOwnership();
            ZNetScene.instance.Destroy(go);
        }

        private static void Check(string what, bool ok)
        {
            if (ok) s_pass++;
            else s_fail++;
            Log.Event($"vc_walktest: {(ok ? "PASS" : "FAIL")} {what}");
        }
    }
}
