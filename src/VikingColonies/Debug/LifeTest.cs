using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Jotunn.Entities;
using UnityEngine;
using VikingColonies.AI;
using VikingColonies.Buildings;
using VikingColonies.Citizens;
using VikingColonies.Colony;
using VikingColonies.Config;
using VikingColonies.Construction;
using VikingColonies.Core;
using VikingColonies.Gui;
using VikingColonies.Jobs;

namespace VikingColonies.DevTools
{
    /// <summary>vc_lifetest (test worlds): the settlers' free time (settler life, 0.14) checked in the game.</summary>
    internal sealed class LifeTestCommand : ConsoleCommand
    {
        public override string Name => "vc_lifetest";
        public override string Help => "VikingColonies (test worlds): place a Town Hall here and check the settlers' free time: a seat, a fire, a chat, " +
                                       "greetings and small talk, recall and the setting (~3 minutes). Removes what it made; 'vc_lifetest keep' leaves it.";
        public override bool IsCheat => true;

        public override void Run(string[] args)
        {
            Player p = Player.m_localPlayer;
            if (p == null || ZNet.instance == null || !TestWorlds.IsTestWorld(ZNet.instance.GetWorldName()))
            {
                BuildCommand.Print("vc_lifetest: test worlds only");
                return;
            }
            bool keep = args != null && args.Any(a => a == "keep");
            Plugin.Instance.StartCoroutine(LifeTest.Guarded(LifeTest.Run(p, keep)));
        }
    }

    internal static class LifeTest
    {
        private static int s_pass, s_fail;

        /// <summary>
        /// Runs the test so that an error ends it with a FAIL and its "done" line (full self-test run 6: a settler burned to
        /// death mid-test, the next check threw, and the runner waited a quarter of an hour for "done").
        /// </summary>
        public static IEnumerator Guarded(IEnumerator test)
        {
            while (true)
            {
                object current = null;
                bool more, failed = false;
                try
                {
                    more = test.MoveNext();
                    if (more) current = test.Current;
                }
                catch (System.Exception e)
                {
                    Check($"The test ran to its end (it stopped: {e.GetType().Name}: {e.Message})", false);
                    Log.Event($"vc_lifetest: done — {s_pass} passed, {s_fail} failed");
                    more = false;
                    failed = true;
                }
                if (!more || failed) yield break;
                yield return current;
            }
        }

        public static IEnumerator Run(Player player, bool keep)
        {
            s_pass = s_fail = 0;
            MoodTracker.HoldForTest = false;   // a self-test cut short may have left moods held (0.17 review M8)
            // A quarter of an hour of the day ahead at least: the game's day runs on while the test holds the time of day, and a
            // new day mid-test brings everyone's breakfast and hunger into the checks (run colony2: a chat cut short, a hungry
            // forager at half pace). On to the next day's start when less is left.
            double nowSeconds = ZNet.instance.GetTimeSeconds(), dayLength = EnvMan.instance.m_dayLengthSec;
            double intoDay = nowSeconds % dayLength;
            if (dayLength - intoDay < 900.0) ZNet.instance.SetNetTime(nowSeconds - intoDay + dayLength + 1.0);
            Log.Event("vc_lifetest: starting (flat ground in a test world)");
            EnvMan.instance.m_debugTimeOfDay = true;
            EnvMan.instance.m_debugTime = 0.5f;
            EnvMan.instance.SetForceEnvironment("Clear");   // no rain: settlers sit on open ground
            if (!player.InGodMode()) player.SetGodMode(true);
            Vector3 origin = player.transform.position, forward = player.transform.forward, right = player.transform.right;
            DevHelpers.ClearMonsters(origin, 60f);
            var before = new HashSet<string>(Citizen.All.Where(c => c != null).Select(c => c.Id));
            var made = new List<GameObject>();
            int greetingsAtStart = SettlerWords.GreetingsShown;
            bool freeTimeWas = ColonyConfig.FreeTime.Value, talkWas = ColonyConfig.SmallTalk.Value, walkInWas = ColonyConfig.NewcomersWalkIn.Value;
            bool moodWas = ColonyConfig.Mood.Value;
            ColonyConfig.NewcomersWalkIn.Value = true;
            float foragerRadiusWas = ColonyConfig.ForagerWorkRadius.Value;
            float colonyRadiusWas = ColonyConfig.ColonyRadius.Value;   // check 8c narrows it a while (put back in finally too)
            ColonyConfig.FreeTime.Value = true;
            ColonyConfig.SmallTalk.Value = true;
            ColonyBuilding hall = null;
            string foragerHutId = "", longhouseId = "", strangerHutId = "";
            try
            {
                hall = DevHelpers.Place(BuildingTypes.TownHall.PrefabName, origin + forward * 8f, Quaternion.identity, player.GetPlayerID());
                Check("Town Hall placed", hall != null);
                if (hall == null) yield break;
                DevHelpers.KeepNothing(hall);
                // The first settlers appear at the colony's edge (0.16): seen the moment they come (they jog 4 m a second).
                List<Citizen> settlers = new List<Citizen>();
                for (float t = 0f; t < 20f && settlers.Count < 4; t += 0.25f)
                {
                    yield return new WaitForSeconds(0.25f);
                    settlers = Citizen.All.Where(c => c != null && c.ColonyId == hall.ColonyId && c.IsOwner).ToList();
                }
                Check($"Settlers came ({settlers.Count})", settlers.Count >= 4);
                if (settlers.Count < 4) yield break;
                float farthestStart = settlers.Max(c => Utils.DistanceXZ(c.transform.position, hall.transform.position));
                bool allArriving = settlers.All(c => c.Record.Arriving);
                // Each measured as he arrives: the first in start their free time and may stroll off while the last walks in
                // (runs polish2 and polish3: 13-14 m after the last had come).
                var arrivedAt = new Dictionary<Citizen, float>();
                for (float t = 0f; t < 90f && settlers.Any(c => c.Record.Arriving); t += 0.25f)
                {
                    yield return new WaitForSeconds(0.25f);
                    foreach (Citizen c in settlers.Where(c => !c.Record.Arriving && !arrivedAt.ContainsKey(c)))
                        arrivedAt[c] = Utils.DistanceXZ(c.transform.position, hall.transform.position);
                }
                foreach (Citizen c in settlers.Where(c => !arrivedAt.ContainsKey(c)))
                    arrivedAt[c] = Utils.DistanceXZ(c.transform.position, hall.transform.position);
                float farthestNow = arrivedAt.Values.Max();
                Check($"The first settlers walk in from the colony's edge to the Town Hall (all arriving at first: {allArriving}, {farthestStart:0} m out; " +
                      $"still arriving: {settlers.Count(c => c.Record.Arriving)}; farthest as he came {farthestNow:0.0} m; callings: " +
                      $"{string.Join(", ", settlers.Select(c => c.Record.Calling))})",
                    allArriving && farthestStart >= 20f && settlers.All(c => !c.Record.Arriving) && farthestNow <= 10f
                    && settlers.Any(c => c.Record.Calling == "builder"));
                yield return new WaitForSeconds(3f);
                Check($"Jobless settlers have free time ({settlers.Count(InFreeTime)} of {settlers.Count}; {FreeTimeRoutine.ActiveCount} on this computer)",
                    settlers.All(InFreeTime));

                // 1.4.1: settlers pass through each other and through you, not shoving (the player: "villagers keep pushing
                // each other and me").
                SettlerBodies.Refresh();
                bool settlersPass = settlers.Count >= 2 && SettlerBodies.IgnoredForTest(settlers[0], settlers[1]);
                bool youPass = settlers.Count >= 1 && SettlerBodies.IgnoredForTest(settlers[0], player);
                Check($"Settlers pass through each other and through you instead of shoving (settlers {settlersPass}, you {youPass}; " +
                      $"{SettlerBodies.PairsForTest} pairs)", settlersPass && youPass);

                // 1. A seat: a bench by the Town Hall; one settler is sent to it.
                Vector3 hallAt = hall.transform.position;
                GameObject bench = DevHelpers.Spawn("piece_bench01", hallAt + right * 6f);
                made.Add(bench);
                Chair[] chairs = bench != null ? bench.GetComponentsInChildren<Chair>() : new Chair[0];   // a bench seats three
                ColonyPlaces.Forget();
                Citizen sitter = settlers[0];
                Routine(sitter).ForceForTest(Leisure.Seat);
                SettlerPose sitterPose = sitter.GetComponent<SettlerPose>();
                for (float t = 0f; t < 40f && !OnSeat(sitterPose, chairs); t += 0.5f) yield return new WaitForSeconds(0.5f);
                float off = sitterPose.Point != null ? Vector3.Distance(sitter.transform.position, sitterPose.Point.position) : -1f;
                string hover = sitter.GetComponent<Character>().GetHoverText().Replace("\n", " / ");
                Check($"A settler sits on a bench by the Town Hall ({sitter.CitizenName}: pose {sitterPose.Current}, {off:0.00} m from his seat; " +
                      $"{chairs.Length} seats on the bench; hover '{hover}')",
                    OnSeat(sitterPose, chairs) && off >= 0f && off <= 0.2f && hover.Contains(Localization.instance.Localize("$vc_doing_seat")));

                // 2. A fire: a lit campfire on the other side; another settler sits by it, facing it.
                GameObject pit = DevHelpers.Spawn("fire_pit", hallAt - right * 7f);
                made.Add(pit);
                Fireplace fire = pit != null ? pit.GetComponent<Fireplace>() : null;
                if (fire != null)
                {
                    ZNetView view = fire.GetComponent<ZNetView>();
                    view.GetZDO().Set(ZDOVars.s_fuel, 6f);
                }
                ColonyPlaces.Forget();
                yield return new WaitForSeconds(1f);
                Citizen warmer = settlers[1];
                Routine(warmer).ForceForTest(Leisure.Fire);
                SettlerPose warmerPose = warmer.GetComponent<SettlerPose>();
                bool ByFire() => fire != null && Routine(warmer).Doing == Leisure.Fire && warmerPose.Current == SettlerPose.Kind.Ground
                                 && Utils.DistanceXZ(warmer.transform.position, fire.transform.position) <= 3.3f;
                yield return new WaitForSeconds(1f);
                for (float t = 0f; t < 40f && !ByFire(); t += 0.5f) yield return new WaitForSeconds(0.5f);
                float fromFire = fire != null ? Utils.DistanceXZ(warmer.transform.position, fire.transform.position) : -1f;
                Vector3 toFire = fire != null ? fire.transform.position - warmer.transform.position : Vector3.zero;
                toFire.y = 0f;
                float angle = toFire.sqrMagnitude > 0.01f ? Vector3.Angle(warmer.transform.forward, toFire) : 999f;
                Check($"A settler sits by a burning fire, facing it ({warmer.CitizenName}: {Routine(warmer).Doing}, pose {warmerPose.Current}, {fromFire:0.0} m off, " +
                      $"{angle:0} degrees from facing it; burning {fire != null && fire.IsBurning()})",
                    // 1.5 m: his spot is 2-2.5 m out and he is there within half a metre (run red1: 1.66 m, from a turned spot).
                    warmerPose.Current == SettlerPose.Kind.Ground && fromFire >= 1.5f && fromFire <= 3.3f && angle <= 45f);

                // 3. A chat: the other two talk, facing each other, with gestures.
                Citizen talker = settlers[2];
                Routine(settlers[3]).ForceForTest(Leisure.Stroll);
                yield return new WaitForSeconds(1f);
                for (float t = 0f; t < 40f && !(Routine(settlers[3]).Doing == Leisure.Stroll && Routine(settlers[3]).Arrived); t += 0.5f)
                    yield return new WaitForSeconds(0.5f);
                int gesturesBefore = settlers.Sum(c => c.GetComponent<SettlerPose>().GesturesPlayed);
                // Straight to that friend (run 39: forced by the dice, the friend had moved on to the fire a moment before).
                Routine(talker).ChatWithForTest(Routine(settlers[3]));
                Citizen friend = null;
                for (float t = 0f; t < 60f; t += 0.5f)
                {
                    friend = Routine(talker).Partner;
                    if (friend != null && Facing(talker, friend) && Facing(friend, talker)
                        && settlers.Sum(c => c.GetComponent<SettlerPose>().GesturesPlayed) > gesturesBefore) break;
                    yield return new WaitForSeconds(0.5f);
                }
                ActivityWords.Decode(talker.Doing, out string talkerDoing, out string talkerWith);
                ActivityWords.Decode(friend != null ? friend.Doing : "", out _, out string friendWith);
                int gestures = settlers.Sum(c => c.GetComponent<SettlerPose>().GesturesPlayed) - gesturesBefore;
                SettlerPose talkerPose = talker.GetComponent<SettlerPose>();
                Check($"Two settlers chat, facing each other, with gestures ({talker.CitizenName} with '{talkerWith}', " +
                      $"{(friend != null ? friend.CitizenName : "nobody")} with '{friendWith}'; {gestures} gestures; " +
                      $"{talker.CitizenName} now {Routine(talker).Doing}, pose {talkerPose.Current} held {talkerPose.Held}; " +
                      $"{settlers[3].CitizenName} now {Routine(settlers[3]).Doing})",
                    friend != null && talkerDoing == ActivityWords.Token(Leisure.Chat) && talkerWith == friend.CitizenName
                    && friendWith == talker.CitizenName && Facing(talker, friend) && Facing(friend, talker) && gestures > 0);
                yield return new WaitForSeconds(15f);
                Citizen still = Routine(talker).Partner;
                Check($"The chat lasts (15 s on: {talker.CitizenName} talking with {(still != null ? still.CitizenName : "nobody")})",
                    friend != null && still == friend && Routine(friend).Partner == talker);

                // 1.4.2, the player: no chatting through a wall. Two crossed walls halfway between two spots 6 m apart, off
                // to the side: they'd meet beside the first, the way between clear; then the walls go.
                Vector3 wallAt = Player.m_localPlayer.transform.position + new Vector3(0f, 0f, 9f);
                if (ZoneSystem.instance != null) wallAt.y = ZoneSystem.instance.GetGroundHeight(wallAt);
                // A metre up: the 1.4.2 release run's walls stood with their middle on the ground and reached only 1 m up, so
                // the chest-high line passed over them. 1 m up they cross it, wherever the piece's pivot is.
                GameObject wallPrefab = ZNetScene.instance.GetPrefab("woodwall");
                GameObject wallA = wallPrefab != null ? Object.Instantiate(wallPrefab, wallAt + Vector3.up * 1f, Quaternion.identity) : null;
                GameObject wallB = wallPrefab != null ? Object.Instantiate(wallPrefab, wallAt + Vector3.up * 1f, Quaternion.identity) : null;
                if (wallB != null) wallB.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
                yield return new WaitForSeconds(0.5f);   // the walls' colliders are in the physics scene
                Vector3 chatLeft = wallAt + new Vector3(-3f, 0f, 0f), chatRight = wallAt + new Vector3(3f, 0f, 0f);
                bool met = FreeTimeRoutine.ChooseMeeting(chatLeft, chatRight, out ChatMeeting meetPlace);
                bool beside = met && Mathf.Abs(meetPlace.First.X - chatLeft.x) < 0.05f && Mathf.Abs(meetPlace.First.Z - chatLeft.z) < 0.05f;
                bool open = wallA != null && wallB != null && !Physics.Linecast(chatLeft + Vector3.up * 1.2f, chatRight + Vector3.up * 1.2f, LayerMask.GetMask("piece"));
                // What the wall is, for a failure to explain itself: its colliders' height above the ground, and their layers.
                Collider[] wallCols = wallA != null ? wallA.GetComponentsInChildren<Collider>() : new Collider[0];
                string wallShape = wallCols.Length == 0 ? "no colliders" :
                    $"{wallCols.Min(c => c.bounds.min.y) - wallAt.y:0.0} to {wallCols.Max(c => c.bounds.max.y) - wallAt.y:0.0} m up, layers " +
                    string.Join("/", wallCols.Select(c => LayerMask.LayerToName(c.gameObject.layer)).Distinct().ToArray());
                if (wallA != null) ZNetScene.instance.Destroy(wallA);
                if (wallB != null) ZNetScene.instance.Destroy(wallB);
                Check($"With a wall halfway between two settlers they'd meet beside one of them, not through it (walls up {wallA != null && wallB != null}, " +
                      $"the wall in the way {!open} ({wallShape}), met {met}, beside the first {beside})",
                    wallA != null && wallB != null && !open && beside);

                // 4. Greeting and small talk: the player walks up to the chatting pair (or was greeted already on the way).
                int talkBefore = SettlerWords.TalkShown;
                Citizen near = friend ?? talker;
                player.TeleportTo(near.transform.position + near.transform.forward * 3f + Vector3.up * 0.3f, player.transform.rotation, false);
                // Looking at them: words show only for settlers on the screen (1.4.1).
                void LookAtPair() => player.SetLookDir((near.transform.position + Vector3.up * 1.5f - player.GetEyePoint()).normalized);
                LookAtPair();
                for (float t = 0f; t < 10f && SettlerWords.GreetingsShown == greetingsAtStart; t += 0.5f)
                {
                    yield return new WaitForSeconds(0.5f);
                    LookAtPair();
                }
                string greeting = SettlerWords.LastGreeting;
                Check($"A settler greets the player by name ('{greeting}', {SettlerWords.GreetingsShown - greetingsAtStart} greetings)",
                    SettlerWords.GreetingsShown > greetingsAtStart && greeting.Contains(player.GetPlayerName()));
                for (float t = 0f; t < 30f && SettlerWords.TalkShown == talkBefore; t += 0.5f)
                {
                    yield return new WaitForSeconds(0.5f);
                    LookAtPair();
                }
                Check($"Settlers make small talk above their heads ('{SettlerWords.LastTalk}')",
                    SettlerWords.TalkShown > talkBefore && SettlerWords.LastTalk.Length > 0 && !SettlerWords.LastTalk.Contains("$")
                    && !SettlerWords.LastTalk.Contains("["));

                // 5. Recall while seated: he stands up and is moved to the Town Hall.
                if (!OnSeat(sitterPose, chairs))
                {
                    Routine(sitter).ForceForTest(Leisure.Seat);
                    for (float t = 0f; t < 40f && !OnSeat(sitterPose, chairs); t += 0.5f) yield return new WaitForSeconds(0.5f);
                }
                bool wasSeated = OnSeat(sitterPose, chairs);
                Vector3 satAt = sitter.transform.position;
                Transform seatWas = sitterPose.Point;
                sitter.RequestRecall();
                // Watched for 2 s: up and away at some moment counts (the 1.4.2 release run: moved 4 m to the Town Hall, they
                // sat down on the same bench seat again within the 2 s, its only free one).
                bool leftSeat = false;
                float fromBench = 0f;
                for (float t = 0f; t < 2f; t += 0.25f)
                {
                    yield return new WaitForSeconds(0.25f);
                    if (!sitterPose.Held || sitterPose.Point != seatWas) leftSeat = true;
                    fromBench = Mathf.Max(fromBench, Utils.DistanceXZ(sitter.transform.position, satAt));   // the spot by the Town Hall can be near the bench
                }
                // Off the seat he was on (his free time starts again where he lands: he may sit down nearby, run 37 took
                // the next seat of the same bench).
                Check($"Recalled from a seat, they stand up and go (was seated {wasSeated}; left the seat {leftSeat}, now pose {sitterPose.Current}, " +
                      $"held {sitterPose.Held}; up to {fromBench:0.0} m from where they sat)",
                    wasSeated && leftSeat && fromBench > 0.3f);

                // 6. A seated worker gets work.
                ColonyConfig.ForagerWorkRadius.Value = 10f;   // the test's bush, not the meadow's
                Vector3 hutSpot = new[] { hallAt + forward * 14f, hallAt - forward * 14f, hallAt + right * 16f, hallAt - right * 16f }
                    .Select(p => new Vector3(p.x, ZoneSystem.instance.GetGroundHeight(p), p.z))
                    .FirstOrDefault(p => DevHelpers.Judge(BuildingTypes.ForagerHut, p, Quaternion.identity) is SiteVerdict v && v.Colour != SiteColour.Red);
                ColonyBuilding foragerHut = hutSpot != Vector3.zero
                    ? DevHelpers.Place(BuildingTypes.ForagerHut.PrefabName, hutSpot, Quaternion.identity, player.GetPlayerID()) : null;
                ConstructionModule site = foragerHut != null ? foragerHut.GetComponent<ConstructionModule>() : null;
                if (foragerHut != null) foragerHutId = foragerHut.Id;
                for (float t = 0f; site != null && site.Order == null && t < 15f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                if (site != null && site.Order != null) site.FinishNow();
                for (float t = 0f; site != null && !site.IsBuilt && t < 40f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                WorkerModule work = foragerHut != null ? foragerHut.GetComponent<WorkerModule>() : null;
                if (work != null)
                    foreach (ForageKind kind in ForageList.Kinds) work.RequestSetPickTicked(kind.Item, false);   // nothing to pick
                Citizen forager = null;
                for (float t = 0f; work != null && forager == null && t < 30f; t += 0.5f)
                {
                    yield return new WaitForSeconds(0.5f);
                    if (work.FindWorker(out Citizen found, out _) == WorkerModule.WorkerState.Loaded) forager = found;
                }
                Chair[] hutChairs = new Chair[0];
                SettlerPose foragerPose = forager != null ? forager.GetComponent<SettlerPose>() : null;
                if (forager != null)
                {
                    // On the hut's far side from the Town Hall: out of the jobless settlers' free-time area (day run 2: they
                    // had taken its seats first).
                    Vector3 away = foragerHut.transform.position - hallAt;
                    away.y = 0f;
                    GameObject hutBench = DevHelpers.Spawn("piece_bench01", foragerHut.transform.position + away.normalized * 5f);
                    made.Add(hutBench);
                    hutChairs = hutBench != null ? hutBench.GetComponentsInChildren<Chair>() : new Chair[0];
                    ColonyPlaces.Forget();
                    for (float t = 0f; !Routine(forager).Active && t < 30f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                    Routine(forager).ForceForTest(Leisure.Seat);
                    // Any seat in his area will do (day run 3: he took the nearer bench by the Town Hall).
                    for (float t = 0f; !Seated(foragerPose) && t < 40f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                }
                Check($"A forager with nothing to pick sits on a bench by his hut ({(forager != null ? forager.CitizenName + ", '" + Localization.instance.Localize(forager.Status) + "'" : "no forager")}; " +
                      $"seated {foragerPose != null && Seated(foragerPose)})",
                    forager != null && Seated(foragerPose) && forager.Status == "$vc_status_nothing_to_pick");
                if (forager != null)
                {
                    work.RequestSetPickTicked("Raspberry", true);
                    // In front of the hut, past its front wall (1.4.0's Forager's Hut reaches 3.4 m forward and 6 m to one side
                    // of its chest: the old spot, 4 m to the side, was inside it and he went in by the door).
                    made.Add(DevHelpers.Spawn("RaspberryBush", foragerHut.transform.position + foragerHut.transform.forward * 6f));
                    for (float t = 0f; t < 30f && (foragerPose.Held || forager.Status != "$vc_status_picking"); t += 0.5f) yield return new WaitForSeconds(0.5f);
                }
                Check($"Work comes: the seated forager gets up and goes to pick ({(forager != null ? $"pose {foragerPose.Current}, held {foragerPose.Held}, '{Localization.instance.Localize(forager.Status)}', free time {Routine(forager).Active}" : "no forager")})",
                    forager != null && !foragerPose.Held && foragerPose.Current == SettlerPose.Kind.None && forager.Status == "$vc_status_picking"
                    && !Routine(forager).Active);
                ColonyConfig.ForagerWorkRadius.Value = foragerRadiusWas;

                // ---- The day (0.15) ----
                List<Citizen> idle = settlers.Where(c => c != null && !c.Record.IsEmployed).ToList();

                // 7. Keeping fires fed: the campfire burnt low, wood in the Town Hall; a jobless settler brings it.
                if (fire != null && hall.Storage != null && idle.Count > 0)
                {
                    ZNetView fireView = fire.GetComponent<ZNetView>();
                    fireView.GetZDO().Set(ZDOVars.s_fuel, 1f);
                    Inventory hallItems = hall.GetComponent<Container>().GetInventory();
                    hallItems.AddItem(ObjectDB.instance.GetItemPrefab("Wood"), 30);
                    int woodBefore = hall.Storage.Contents().TryGetValue("Wood", out int w0) ? w0 : 0;
                    ColonyPlaces.Forget();
                    Citizen tender = idle[0];
                    Routine(tender).ForceForTest(Leisure.TendFire);
                    for (float t = 0f; t < 60f && fireView.GetZDO().GetFloat(ZDOVars.s_fuel) < fire.m_maxFuel / 2f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                    float fuelNow = fireView.GetZDO().GetFloat(ZDOVars.s_fuel);
                    int woodAfter = hall.Storage.Contents().TryGetValue("Wood", out int w1) ? w1 : 0;
                    Check($"A settler keeps the fire fed from the Town Hall's wood ({tender.CitizenName}: fuel 1 -> {fuelNow:0.0} of {fire.m_maxFuel:0}; " +
                          $"wood {woodBefore} -> {woodAfter})",
                        fuelNow > fire.m_maxFuel / 2f && woodAfter < woodBefore && woodAfter >= FireFuel.Keep);
                    string fireHover = fire.GetHoverText() ?? "";
                    Check($"The fire's hover says the colony keeps it burning from its chests (1.4.2: '{fireHover.Replace("\n", " / ")}')",
                        HiddenRules.FireLine(fire).Length > 0 && fireHover.Contains(HiddenRules.FireLine(fire)));
                }
                else Check("A settler keeps the fire fed (no fire, Town Hall chest or jobless settler to test with)", false);

                // 8. Breakfast at the table: his meal is due, raspberries in the Town Hall; he eats there, on the bench by it.
                Citizen eater = idle.Count > 1 ? idle[1] : idle.FirstOrDefault();
                if (eater != null && hall.Storage != null)
                {
                    hall.GetComponent<Container>().GetInventory().AddItem(ObjectDB.instance.GetItemPrefab("Raspberry"), 3);
                    // The chest he went to: the nearest that holds his food, and the forager's may be nearer (0.18). Each chest's
                    // berries are counted now; his own is compared after (review: a forager's delivery broke a colony-wide count).
                    Dictionary<string, int> berriesBefore = ColonyBuilding.All.Where(b => b != null && b.IsValid && b.ColonyId == hall.ColonyId && b.Storage != null)
                        .ToDictionary(b => b.Id, b => b.Storage.Contents().TryGetValue("Raspberry", out int n) ? n : 0);
                    eater.ForgetMealsForTest();   // the plainest: raspberries
                    int today = EnvMan.instance.GetDay(ZNet.instance.GetTimeSeconds());
                    // The bench by the Town Hall free for him: the others' free time paused (run 1 of 0.15: all three seats
                    // were taken, and he ate standing, as he should).
                    ColonyConfig.FreeTime.Value = false;
                    yield return new WaitForSeconds(3f);
                    eater.MissMealForTest();
                    bool sawSeated = false, sawEating = false;
                    SettlerPose eaterPose = eater.GetComponent<SettlerPose>();
                    for (float t = 0f; t < 150f && eater.Record.LastMealDay != today; t += 0.5f)   // he sets off within a minute (0.18)
                    {
                        yield return new WaitForSeconds(0.5f);
                        if (eater.Status == "$vc_status_eating") sawEating = true;
                        if (eaterPose.Current == SettlerPose.Kind.Seat && eater.Doing == "$vc_doing_eating") sawSeated = true;
                    }
                    ColonyConfig.FreeTime.Value = true;
                    string usedChest = eater.GetComponent<CitizenAI>().BreakfastForTest.LastChestForTest;
                    ColonyBuilding used = ColonyBuilding.FindById(usedChest);
                    int berriesFrom = berriesBefore.TryGetValue(usedChest ?? "", out int bf) ? bf : -1;
                    int berriesAfter = used != null && used.Storage != null && used.Storage.Contents().TryGetValue("Raspberry", out int ba) ? ba : 0;
                    bool fromTheChest = berriesAfter == berriesFrom - 1 || used != null && used.TypeId == BuildingTypes.ForagerHutId && berriesAfter >= berriesFrom - 1;
                    Check($"Breakfast at the table: {eater.CitizenName} eats from a colony chest, seated by it (meal day {eater.Record.LastMealDay} of {today}, " +
                          $"{(used != null ? used.TypeId : "no chest")} raspberries {berriesFrom} -> {berriesAfter}, ate {eater.LastMeal}, eating {sawEating}, seated {sawSeated})",
                        eater.Record.LastMealDay == today && !eater.Record.Hungry && eater.LastMeal == "Raspberry" && fromTheChest && sawEating && sawSeated);
                }
                else Check("Breakfast at the table (no jobless settler to test with)", false);

                // A Longhouse for the night: the evening needs a hut to go into, and its beds are homes.
                Vector3 houseSpot = new[] { hallAt - forward * 16f, hallAt + forward * 18f, hallAt - right * 18f, hallAt + right * 20f }
                    .Select(p => new Vector3(p.x, ZoneSystem.instance.GetGroundHeight(p), p.z))
                    .FirstOrDefault(p => DevHelpers.Judge(BuildingTypes.Longhouse, p, Quaternion.identity) is SiteVerdict v && v.Colour != SiteColour.Red);
                ColonyBuilding longhouse = houseSpot != Vector3.zero
                    ? DevHelpers.Place(BuildingTypes.Longhouse.PrefabName, houseSpot, Quaternion.identity, player.GetPlayerID()) : null;
                ConstructionModule houseSite = longhouse != null ? longhouse.GetComponent<ConstructionModule>() : null;
                if (longhouse != null) longhouseId = longhouse.Id;
                for (float t = 0f; houseSite != null && houseSite.Order == null && t < 15f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                if (houseSite != null && houseSite.Order != null) houseSite.FinishNow();
                for (float t = 0f; houseSite != null && !houseSite.IsBuilt && t < 40f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                Citizen sleeper = null;
                for (float t = 0f; longhouse != null && sleeper == null && t < 30f; t += 0.5f)
                {
                    yield return new WaitForSeconds(0.5f);
                    sleeper = settlers.FirstOrDefault(c => c != null && c.Record.HasHome && ResidenceModule.LonghouseOf(c.Record.Home) == longhouse.Id);
                }
                Check($"A Longhouse stands and gives a settler a home ({(sleeper != null ? sleeper.CitizenName + ", " + sleeper.Record.Home : "nobody")})", sleeper != null);
                // 8b. His bed has its number in the Longhouse (0.18: the details line says "Longhouse bed 2").
                int bedNumber = sleeper != null ? ResidenceModule.BedNumberOf(sleeper.Record.Home) : 0;
                int bedsThere = longhouse != null && longhouse.GetComponent<ResidenceModule>() != null ? longhouse.GetComponent<ResidenceModule>().Beds().Count : 0;
                Check($"His home bed has its number in the Longhouse ({(sleeper != null ? sleeper.CitizenName : "nobody")}: bed {bedNumber} of {bedsThere})",
                    bedNumber >= 1 && bedNumber <= bedsThere);

                // 8c (1.4.1). Inside a building the colony doesn't know (a player's house; here a Lumberjack's Hut whose chest
                // is gone), past the colony's edge, at night a settler leaves it by its own door and goes to bed (full runs of
                // 16 and 19 October: inside one whose door stood outside the colony's area, a settler aimed for the Longhouse's
                // door instead, gave up and stayed out all night).
                Vector3 strangerSpot = new[] { hallAt + forward * 34f, hallAt - forward * 34f, hallAt + right * 34f, hallAt - right * 34f }
                    .Select(p => new Vector3(p.x, ZoneSystem.instance.GetGroundHeight(p), p.z))
                    .FirstOrDefault(p => DevHelpers.Judge(BuildingTypes.LumberjackHut, p, Quaternion.identity) is SiteVerdict v && v.Colour != SiteColour.Red);
                ColonyBuilding strangerHut = strangerSpot != Vector3.zero && sleeper != null
                    ? DevHelpers.Place(BuildingTypes.LumberjackHut.PrefabName, strangerSpot, Quaternion.identity, player.GetPlayerID()) : null;
                ConstructionModule strangerSite = strangerHut != null ? strangerHut.GetComponent<ConstructionModule>() : null;
                if (strangerHut != null) strangerHutId = strangerHut.Id;
                for (float t = 0f; strangerSite != null && strangerSite.Order == null && t < 15f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                if (strangerSite != null && strangerSite.Order != null) strangerSite.FinishNow();
                for (float t = 0f; strangerSite != null && !strangerSite.IsBuilt && t < 40f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                bool homeAtNight = false;
                string nightWhy = "no hut";
                if (strangerHut != null && strangerSite != null && strangerSite.IsBuilt && sleeper != null)
                {
                    // Between its chest and its door, inside its walls (as the worker test's wedged settler); then its chest goes,
                    // and with it the colony's knowing it.
                    Door strangerDoor = Jobs.WorldScan.Doors().Where(d => d != null && PiecePlacer.SiteOf(d.gameObject) == strangerHut.Id)
                        .OrderBy(d => Utils.DistanceXZ(d.transform.position, strangerHut.transform.position)).FirstOrDefault();
                    Vector3 chestAt = strangerHut.transform.position;
                    Vector3 inside = chestAt + ((strangerDoor != null ? strangerDoor.transform.position : chestAt + strangerHut.transform.forward) - chestAt).normalized * 1.5f;
                    inside.y = chestAt.y + 0.1f;
                    strangerHut.GetComponent<ZNetView>().ClaimOwnership();
                    ZNetScene.instance.Destroy(strangerHut.gameObject);
                    yield return new WaitForSeconds(1f);
                    ColonyPlaces.Forget();
                    CitizenAI strangerAi = sleeper.GetComponent<CitizenAI>();
                    strangerAi?.PauseForTest(3f);
                    // As a recall puts him: off the bench first (the first 1.4.1 run: seated, the seat pulled her back out).
                    if (strangerAi != null) strangerAi.PlaceForTest(inside);
                    yield return new WaitForSeconds(1.5f);
                    bool wasInside = Utils.DistanceXZ(sleeper.transform.position, inside) < 1.5f;
                    int passesThen = strangerAi != null ? strangerAi.DoorPasses : 0;
                    // The colony's area ends short of it (its door is out there; the Longhouse is still inside).
                    ColonyConfig.ColonyRadius.Value = 26f;
                    EnvMan.instance.m_debugTime = 0f;   // midnight
                    Vector3 bedAt = Citizen.ToVector3(sleeper.Record.HomePos);
                    Vector3 spotAt = ResidenceModule.SleepSpotOf(sleeper.Record.Home, strangerAi) ?? bedAt;
                    bool AtBed() => sleeper != null && sleeper.Status == "$vc_status_sleeping"
                                    && (Utils.DistanceXZ(sleeper.transform.position, spotAt) < 1f
                                        || strangerAi != null && strangerAi.LyingForTest && Utils.DistanceXZ(sleeper.transform.position, bedAt) < 1f);
                    for (float t = 0f; !AtBed() && t < 150f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                    homeAtNight = wasInside && AtBed();
                    ColonyConfig.ColonyRadius.Value = colonyRadiusWas;
                    nightWhy = $"{sleeper.CitizenName}: put inside {wasInside}, {Utils.DistanceXZ(sleeper.transform.position, spotAt):0.0} m from his spot by the bed, " +
                               $"{Utils.DistanceXZ(sleeper.transform.position, inside):0.0} m from where he stood inside, door passes {(strangerAi != null ? strangerAi.DoorPasses - passesThen : -1)}, " +
                               $"'{Localization.instance.Localize(sleeper.Status)}'";
                    EnvMan.instance.m_debugTime = 0.5f;   // noon
                    for (float t = 0f; sleeper != null && sleeper.Status == "$vc_status_sleeping" && t < 20f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                }
                Check($"At night a settler inside a building the colony doesn't know leaves it by its door and goes to bed ({nightWhy})", homeAtNight);

                // 9. The evening: at 0.71 the jobless gather at the burning fire, seated near it or on the ring, facing it.
                if (fire != null) fire.GetComponent<ZNetView>().GetZDO().Set(ZDOVars.s_fuel, 6f);
                // Only the test's fire (or the camp's) burns: 1.4.0's Longhouse has a fire pit under its roof, and a roofed
                // fire draws the evening indoors (the release build's full run: they went into the Longhouse).
                if (hall != null)
                    foreach (FirePlace other in ColonyPlaces.Fires(hall.ColonyId))
                        if (other != null && other.Valid && !other.AtHall && other.Fire != fire)
                            other.Fire.GetComponent<ZNetView>()?.GetZDO()?.Set(ZDOVars.s_fuel, 0f);
                ColonyPlaces.Forget();
                EnvMan.instance.m_debugTime = 0.71f;
                List<Citizen> gatherers = settlers.Where(c => c != null && !c.Record.IsEmployed).ToList();
                // Once the camp's design ships (1.4.0) the colony gathers at the camp's fire before any other burning one.
                bool AtTheFire(Citizen c)
                {
                    if (fire == null || Routine(c).Doing != Leisure.Evening || !Routine(c).Arrived) return false;
                    SettlerPose p = c.GetComponent<SettlerPose>();
                    bool Near(Vector3 at)
                    {
                        float d = Utils.DistanceXZ(c.transform.position, at);
                        return p.Current == SettlerPose.Kind.Seat ? d <= FreeTimeRules.FireNearSeat + 0.5f : d <= 5f;
                    }
                    FirePlace campFire = hall != null ? ColonyPlaces.Fires(hall.ColonyId).FirstOrDefault(f => f != null && f.Valid && f.AtHall && f.Burning) : null;
                    return Near(fire.transform.position) || (campFire != null && Near(campFire.Position));
                }
                for (float t = 0f; t < 60f && !gatherers.All(AtTheFire); t += 0.5f) yield return new WaitForSeconds(0.5f);
                Check($"In the evening the settlers gather round the burning fire ({gatherers.Count(AtTheFire)} of {gatherers.Count} there; " +
                      $"at a fire: {gatherers.Count > 0 && Routine(gatherers[0]).EveningAtFire}; '{(gatherers.Count > 0 ? gatherers[0].GetComponent<Character>().GetHoverText().Replace("\n", " / ") : "")}')",
                    gatherers.Count > 0 && gatherers.All(AtTheFire));

                // 10. Night: nobody stays out by the fire; the one with a home lies in his bed.
                EnvMan.instance.m_debugTime = 0.9f;
                CitizenAI sleeperAi = sleeper != null ? sleeper.GetComponent<CitizenAI>() : null;
                for (float t = 0f; t < 90f && !(sleeperAi != null && sleeperAi.LyingForTest); t += 0.5f) yield return new WaitForSeconds(0.5f);
                int stillOut = settlers.Count(c => c != null && Routine(c).Doing == Leisure.Evening);
                SettlerPose sleeperPose = sleeper != null ? sleeper.GetComponent<SettlerPose>() : null;
                Check($"At night the evening is over, and a settler lies in his own bed ({stillOut} still at the fire; " +
                      $"{(sleeper != null ? $"{sleeper.CitizenName}: pose {sleeperPose.Current}, lying {sleeperAi.LyingForTest}" : "no settler with a home")})",
                    stillOut == 0 && sleeper != null && sleeperAi.LyingForTest && sleeperPose.Current == SettlerPose.Kind.Bed && sleeperPose.Held);

                // 11. Morning: he gets up beside his bed.
                EnvMan.instance.m_debugTime = 0.3f;
                for (float t = 0f; t < 15f && sleeperPose != null && sleeperPose.Held; t += 0.5f) yield return new WaitForSeconds(0.5f);
                Check($"In the morning he gets up ({(sleeper != null ? $"pose {sleeperPose.Current}, held {sleeperPose.Held}" : "nobody")})",
                    sleeperPose != null && !sleeperPose.Held && sleeperPose.Current == SettlerPose.Kind.None);
                EnvMan.instance.m_debugTime = 0.5f;

                // ---- Who they are (0.16) ----
                // 12. A newcomer walks in from the edge; the Saga tells where he came from.
                TownHallModule townHall = hall.GetComponent<TownHallModule>();
                Citizen newcomer = townHall.NewcomerForTest();
                yield return null;
                float startOut = newcomer != null ? Utils.DistanceXZ(newcomer.transform.position, hall.transform.position) : 0f;
                bool walking = newcomer != null && newcomer.Record.Arriving;
                yield return new WaitForSeconds(2f);
                string hoverOnTheWay = newcomer != null ? newcomer.GetComponent<Character>().GetHoverText().Replace("\n", " / ") : "";
                for (float t = 0f; newcomer != null && newcomer.Record.Arriving && t < 90f; t += 1f) yield return new WaitForSeconds(1f);
                SagaEntry came = townHall.Saga.Page(0, 10).FirstOrDefault(e => e.Kind == "came" && newcomer != null && e.Args.Count > 0 && e.Args[0] == newcomer.CitizenName);
                Check($"A newcomer walks in from the colony's edge ({(newcomer != null ? newcomer.CitizenName : "none")}: {startOut:0} m out, arriving {walking}; " +
                      $"hover on the way '{hoverOnTheWay}'; arrived {newcomer != null && !newcomer.Record.Arriving}; saga '{(came != null ? SagaText.Line(came) : "")}')",
                    newcomer != null && walking && startOut >= 20f && !newcomer.Record.Arriving
                    && Utils.DistanceXZ(newcomer.transform.position, hall.transform.position) <= 10f && came != null
                    && hoverOnTheWay.Contains(Localization.instance.Localize("$vc_status_arriving")));
                if (newcomer != null) settlers.Add(newcomer);

                // 12b. A boss's fall reaches the colony (1.4.2): the saga tells it once, and settlers talk of it for a few days.
                // A world with no boss down gets Eikthyr's key for a moment, and everything is put back after.
                ZDO hallZdo = townHall.GetComponent<ZNetView>().GetZDO();
                bool anyDown = TownHallLevels.LatestBeaten(key => ZoneSystem.instance.GetGlobalKey(key)).Length > 0;
                string heardWas = hallZdo.GetString(SaveKeys.BossHeard, "");
                if (anyDown)
                {
                    string latestDown = TownHallLevels.LatestBeaten(key => ZoneSystem.instance.GetGlobalKey(key));
                    for (float t = 0f; hallZdo.GetString(SaveKeys.BossHeard, "") != latestDown && t < 10f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                    Check($"A boss's fall reaches the colony: this world's latest is heard of ({latestDown}; heard '{hallZdo.GetString(SaveKeys.BossHeard, "")}')",
                        hallZdo.GetString(SaveKeys.BossHeard, "") == latestDown);
                }
                else
                {
                    ZoneSystem.instance.SetGlobalKey("defeated_eikthyr");
                    for (float t = 0f; hallZdo.GetString(SaveKeys.BossHeard, "") != "$enemy_eikthyr" && t < 10f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                    int bossDay = EnvMan.instance.GetDay();
                    SagaEntry fell = townHall.Saga.Page(0, 10).FirstOrDefault(e => e.Kind == "bossfell" && e.Args.Count > 0 && e.Args[0] == "$enemy_eikthyr");
                    TalkContext bossTalk = SmallTalk.FromSaga(townHall.Saga, bossDay, "nobody");
                    TalkContext laterTalk = SmallTalk.FromSaga(townHall.Saga, bossDay + SmallTalk.MournDays + 1, "nobody");
                    int fellLines = townHall.Saga.Page(0, SagaLog.MaxEntries).Count(e => e.Kind == "bossfell");
                    yield return new WaitForSeconds(4f);   // a tick later: no second line for the same boss
                    int fellLinesAfter = townHall.Saga.Page(0, SagaLog.MaxEntries).Count(e => e.Kind == "bossfell");
                    ZoneSystem.instance.RemoveGlobalKey("defeated_eikthyr");
                    hallZdo.Set(SaveKeys.BossHeard, heardWas);
                    Check($"A boss's fall reaches the colony once: the saga tells it and settlers talk of it for {SmallTalk.MournDays} days, then not " +
                          $"(saga '{(fell != null ? SagaText.Line(fell) : "")}', lines {fellLines} -> {fellLinesAfter}; talk '{bossTalk.BossToken}' then '{laterTalk.BossToken}')",
                        fell != null && fellLinesAfter == fellLines && bossTalk.BossToken == "$enemy_eikthyr" && laterTalk.BossToken == ""
                        && SagaText.Line(fell).Contains(Localization.instance.Localize("$enemy_eikthyr")));
                }

                // 13. Who he is: a trait and a trade in the hover; a settler from before 0.16 gets them from his id, once.
                Citizen old = settlers.FirstOrDefault(c => c != null && !c.Record.IsEmployed);
                if (old != null)
                {
                    string traitBefore = old.Record.Trait;
                    old.ForgetIdentityForTest();
                    CitizenRecord r = old.Record;
                    string oldHover = old.GetComponent<Character>().GetHoverText().Replace("\n", " / ");
                    Check($"A settler from before 0.16 gets a calling, trait and origin from his id ({r.Calling}, {r.Trait}, {r.Origin}, day {r.Arrived}); " +
                          $"hover '{oldHover}'",
                        r.Calling == Callings.FromId(r.Id, Jobs.JobDefinitions.Ids) && r.Trait == Traits.Name(Traits.FromId(r.Id)) && r.Trait == traitBefore
                        && r.Origin == Origins.FromId(r.Id) && r.Arrived >= 0
                        && oldHover.ToLowerInvariant().Contains(Localization.instance.Localize(Traits.Token(old.TraitKind))));
                }
                else Check("A settler from before 0.16 gets who he is (no jobless settler to test with)", false);

                // 14. Clothes by trade: the jobless in rags, the forager in his own; no armour from them.
                Citizen ragged = settlers.FirstOrDefault(c => c != null && !c.Record.IsEmployed);
                Citizen pickerNow = settlers.FirstOrDefault(c => c != null && c.Record.Job == JobDefinitions.ForagerId);
                string raggedWears = ragged != null ? string.Join(",", ragged.WornItems()) : "";
                string pickerWears = pickerNow != null ? string.Join(",", pickerNow.WornItems()) : "";
                Check($"Settlers wear their trade's clothes, for looks only (jobless: {raggedWears}; forager: {pickerWears}; armour {(pickerNow != null ? pickerNow.Body.GetBodyArmor() : -1):0})",
                    ragged != null && Wardrobe.For("").All(i => raggedWears.Contains(i) || ObjectDB.instance.GetItemPrefab(i) == null)
                    && pickerNow != null && Wardrobe.For(JobDefinitions.ForagerId).All(i => pickerWears.Contains(i) || ObjectDB.instance.GetItemPrefab(i) == null)
                    && pickerNow.Body.GetBodyArmor() == 0f);

                // 14b. Clothes that didn't go on (fired mid-swing: the game won't put clothes on during a swing; review M1) are
                // put on again within moments.
                bool Dressed(Citizen c) => Wardrobe.For(c.Record.Job).All(i => c.WornItems().Contains(i) || ObjectDB.instance.GetItemPrefab(i) == null);
                if (pickerNow != null)
                {
                    pickerNow.UndressForTest();
                    bool bare = !pickerNow.WornItems().Any();
                    for (float t = 0f; t < 15f && !Dressed(pickerNow); t += 0.5f) yield return new WaitForSeconds(0.5f);
                    Check($"Clothes that came off are put on again ({pickerNow.CitizenName}: bare {bare}, then {string.Join(",", pickerNow.WornItems())})",
                        bare && Dressed(pickerNow));
                }
                else Check("Clothes that came off are put on again (no forager to test with)", false);

                // 15. A hut hires someone good at its trade first, and he works faster in it.
                if (work != null && forager != null)
                {
                    Citizen skilled = settlers.FirstOrDefault(c => c != null && !c.Record.IsEmployed && c != forager);
                    if (skilled != null)
                    {
                        // Only he is good at foraging (life run new3: another jobless forager by trade was nearer and rightly hired).
                        foreach (Citizen other in settlers.Where(c => c != null && c != skilled)) other.SetCallingForTest(JobDefinitions.LumberjackId);
                        skilled.SetCallingForTest(JobDefinitions.ForagerId);
                        forager.RequestUnemploy(foragerHut.Id);   // the hut hires again
                        Citizen hired = null;
                        for (float t = 0f; t < 30f && hired == null; t += 0.5f)
                        {
                            yield return new WaitForSeconds(0.5f);
                            if (work.FindWorker(out Citizen found, out _) == WorkerModule.WorkerState.Loaded) hired = found;
                        }
                        float moodFactor = MoodLevels.WorkFactor(skilled.MoodLevel);   // his mood counts too (0.17)
                        float expected = WorkPace.Factor(false, 1f, true, ColonyConfig.CallingBonus.Value, skilled.TraitKind, moodFactor);
                        Check($"A hut hires the settler good at its trade, and he works faster in it (hired {(hired != null ? hired.CitizenName : "nobody")}, " +
                              $"good at foraging: {skilled.CitizenName}; work speed {skilled.WorkSpeed:0.00}, expected {expected:0.00})",
                            hired == skilled && Mathf.Abs(skilled.WorkSpeed - expected) < 0.01f && skilled.WorkSpeed > Traits.WorkFactor(skilled.TraitKind) * moodFactor);
                    }
                    else Check("A hut hires the settler good at its trade (no jobless settler to test with)", false);
                }
                else Check("A hut hires the settler good at its trade (no Forager's Hut to test with)", false);

                // 15b. Hired on his way in (review I3: a hut short of its trade hires a newcomer at the colony's edge): once he
                // works, his hover no longer says where he came from as what he's doing.
                if (work != null && foragerHut != null)
                {
                    // Free time off: nothing else writes what he's doing meanwhile (run red1: his free time did, and hid it).
                    ColonyConfig.FreeTime.Value = false;
                    Citizen onTheWay = townHall.NewcomerForTest();
                    if (onTheWay != null)
                    {
                        settlers.Add(onTheWay);
                        foreach (Citizen other in settlers.Where(c => c != null && c != onTheWay)) other.SetCallingForTest(JobDefinitions.LumberjackId);
                        onTheWay.SetCallingForTest(JobDefinitions.ForagerId);
                        Citizen nowForaging = settlers.FirstOrDefault(c => c != null && c != onTheWay && c.Record.Workplace == foragerHut.Id);
                        if (nowForaging != null) nowForaging.RequestUnemploy(foragerHut.Id);   // the hut hires again: him
                    }
                    bool hiredOnTheWay = false, wasOut = false;
                    for (float t = 0f; t < 30f && !hiredOnTheWay && onTheWay != null; t += 0.5f)
                    {
                        yield return new WaitForSeconds(0.5f);
                        hiredOnTheWay = onTheWay.Record.Workplace == foragerHut.Id;
                        wasOut = Utils.DistanceXZ(onTheWay.transform.position, hall.transform.position) > 10f;
                    }
                    yield return new WaitForSeconds(3f);
                    string cameFrom = onTheWay != null ? onTheWay.Record.Origin : "";
                    string doing = onTheWay != null ? onTheWay.Doing : "";
                    string hoverAtWork = onTheWay != null ? onTheWay.GetComponent<Character>().GetHoverText().Replace("\n", " / ") : "";
                    Check($"A newcomer hired on his way in works without the arriving words ({(onTheWay != null ? onTheWay.CitizenName : "none")}: " +
                          $"hired {hiredOnTheWay} while still out {wasOut}, arriving {onTheWay != null && onTheWay.Record.Arriving}, doing '{doing}', hover '{hoverAtWork}')",
                        onTheWay != null && hiredOnTheWay && !onTheWay.Record.Arriving && cameFrom.Length > 0 && doing != cameFrom
                        && !hoverAtWork.Contains(Localization.instance.Localize(cameFrom)));
                    ColonyConfig.FreeTime.Value = true;
                }
                else Check("A newcomer hired on his way in (no Forager's Hut to test with)", false);

                // ---- Mood (0.17) ----
                ColonyConfig.Mood.Value = true;
                List<Citizen> members = settlers.Where(c => c != null).ToList();
                // 17. Everyone's mood is worked out by itself, with its reasons: at once on his computer, then once a minute.
                for (float t = 0f; t < 80f && members.Any(c => c.Record.Mood == Mood.Unknown); t += 1f) yield return new WaitForSeconds(1f);   // up to 66 s after a load
                Check($"Every settler has a mood with its reasons ({string.Join(", ", members.Select(c => c.CitizenName + " " + c.Record.Mood))})",
                    members.Count > 0 && members.All(c => c.Record.Mood != Mood.Unknown && c.Record.WhyMood.Length > 0));
                MoodTracker.HoldForTest = true;   // from here the test works moods out itself
                int moodDay = EnvMan.instance.GetDay(ZNet.instance.GetTimeSeconds());

                // 18. No home and hungry: unhappy, and his reasons say why.
                Citizen sad = members.FirstOrDefault(c => !c.Record.HasHome && !c.Record.IsEmployed) ?? members.FirstOrDefault(c => !c.Record.HasHome);
                if (sad != null)
                {
                    sad.MakeHungryForTest();
                    sad.GetComponent<CitizenAI>().WorkOutMoodForTest();
                    Check($"A settler with no home who is hungry is unhappy, and his reasons say so ({sad.CitizenName}: {sad.MoodLevel} {sad.Record.Mood}, '{sad.Record.WhyMood}')",
                        sad.MoodLevel <= MoodLevel.Unhappy && sad.Record.WhyMood.Contains("$vc_mood_nohome") && sad.Record.WhyMood.Contains("$vc_mood_hungry"));
                    sad.MarkMeal(moodDay, hungry: false);   // fed again
                }
                else Check("A settler with no home who is hungry is unhappy (everyone has a home)", false);

                // 19. A home and a meal: content or better.
                Citizen glad = members.FirstOrDefault(c => c.Record.HasHome);
                if (glad != null)
                {
                    glad.MarkMeal(moodDay, hungry: false, item: "Raspberry");
                    glad.GetComponent<CitizenAI>().WorkOutMoodForTest();
                    string gladHover = glad.GetComponent<Character>().GetHoverText().Replace("\n", " / ");
                    Check($"With a home and a meal he is content or better, and his hover says so ({glad.CitizenName}: {glad.MoodLevel} {glad.Record.Mood}, " +
                          $"'{glad.Record.WhyMood}'; hover '{gladHover}')",
                        glad.MoodLevel >= MoodLevel.Content && glad.Record.WhyMood.Contains("$vc_mood_home") && glad.Record.WhyMood.Contains("$vc_mood_ate")
                        && gladHover.Contains(Localization.instance.Localize(MoodLevels.Token(glad.MoodLevel))));
                }
                else Check("With a home and a meal he is content (nobody has a home)", false);

                // 20. The work pace follows the mood: miserable 30% slower, joyful 15% faster.
                Citizen paced = members.FirstOrDefault(c => c.Record.IsEmployed) ?? members.First();
                int paceMoodWas = paced.Record.Mood;
                string paceWhyWas = paced.Record.WhyMood;
                paced.SetMoodForTest(50);
                float plainPace = paced.WorkSpeed;
                paced.SetMoodForTest(10);
                float slowPace = paced.WorkSpeed;
                paced.SetMoodForTest(90);
                float fastPace = paced.WorkSpeed;
                paced.SetMoodForTest(paceMoodWas, paceWhyWas);
                Check($"The work pace follows the mood ({paced.CitizenName}: content {plainPace:0.00}, miserable {slowPace:0.00}, joyful {fastPace:0.00})",
                    Mathf.Abs(slowPace - Mathf.Max(Meals.SlowestWork, plainPace * 0.7f)) < 0.01f && Mathf.Abs(fastPace - plainPace * 1.15f) < 0.01f);

                // 21. Varied meals: with raspberries and mushrooms in the Town Hall, two meals running are different.
                Citizen diner = members.FirstOrDefault(c => !c.Record.IsEmployed) ?? members.First();
                Inventory hallChest = hall.GetComponent<Container>().GetInventory();
                hallChest.AddItem(ObjectDB.instance.GetItemPrefab("Raspberry"), 3);
                hallChest.AddItem(ObjectDB.instance.GetItemPrefab("Mushroom"), 3);
                var eaten = new List<string>();
                for (int meal = 0; meal < 2; meal++)
                {
                    diner.MissMealForTest();
                    for (float t = 0f; t < 150f && diner.Record.LastMealDay != moodDay; t += 0.5f) yield return new WaitForSeconds(0.5f);
                    eaten.Add(diner.Record.LastMealDay == moodDay ? diner.LastMeal ?? "" : "");
                }
                Check($"Meals vary: with raspberries and mushrooms in the Town Hall, two meals running are different ({diner.CitizenName}: {string.Join(", ", eaten)})",
                    eaten.Count == 2 && eaten.All(e => e == "Raspberry" || e == "Mushroom") && eaten[0] != eaten[1]);
                MoodTracker.HoldForTest = false;

                // 16. Colony.FreeTime off: everyone stands, and the jobless walk to their spot by the Town Hall.
                Routine(sitter).ForceForTest(Leisure.Seat);
                yield return new WaitForSeconds(15f);
                ColonyConfig.FreeTime.Value = false;
                // The one who slept in the Longhouse walks back across the colony; a new day may have dawned meanwhile, and
                // breakfast comes first (day run 2: all three were at breakfast).
                bool OnSpots() => settlers.Where(c => c != null && !c.Record.IsEmployed).All(c =>
                {
                    GroundPoint spot = JoblessSpot.For(new GroundPoint(hallAt.x, hallAt.z), c.Id);
                    return c.Status != "$vc_status_eating" && Utils.DistanceXZ(c.transform.position, new Vector3(spot.X, 0f, spot.Z)) <= 3f;
                });
                yield return new WaitForSeconds(10f);
                for (float t = 0f; t < 80f && !OnSpots(); t += 1f) yield return new WaitForSeconds(1f);
                List<Citizen> jobless = settlers.Where(c => c != null && !c.Record.IsEmployed).ToList();
                int standing = jobless.Count(c => c.GetComponent<SettlerPose>().Current == SettlerPose.Kind.None && !c.GetComponent<SettlerPose>().Held && c.Doing.Length == 0);
                float farthest = jobless.Max(c =>
                {
                    GroundPoint spot = JoblessSpot.For(new GroundPoint(hallAt.x, hallAt.z), c.Id);
                    return Utils.DistanceXZ(c.transform.position, new Vector3(spot.X, 0f, spot.Z));
                });
                string where = string.Join(", ", jobless.Select(c =>
                {
                    GroundPoint spot = JoblessSpot.For(new GroundPoint(hallAt.x, hallAt.z), c.Id);
                    return $"{c.CitizenName} {Utils.DistanceXZ(c.transform.position, new Vector3(spot.X, 0f, spot.Z)):0.0} m '{Localization.instance.Localize(c.Status)}'";
                }));
                Check($"With Colony.FreeTime off they stand and wait on their spots ({standing} of {jobless.Count} standing; farthest {farthest:0.0} m from his spot: {where})",
                    standing == jobless.Count && farthest <= 3f);
            }
            finally
            {
                ColonyConfig.ColonyRadius.Value = colonyRadiusWas;
                MoodTracker.HoldForTest = false;
                ColonyConfig.Mood.Value = moodWas;
                ColonyConfig.FreeTime.Value = freeTimeWas;
                ColonyConfig.SmallTalk.Value = talkWas;
                ColonyConfig.NewcomersWalkIn.Value = walkInWas;
                ColonyConfig.ForagerWorkRadius.Value = foragerRadiusWas;
                EnvMan.instance.m_debugTimeOfDay = false;
                EnvMan.instance.SetForceEnvironment("");
            }
            Log.Event($"vc_lifetest: done — {s_pass} passed, {s_fail} failed");
            player.Message(MessageHud.MessageType.Center, $"vc_lifetest: {s_pass} passed, {s_fail} failed");
            if (keep) yield break;
            foreach (Citizen c in Citizen.All.Where(c => c != null && !before.Contains(c.Id)).ToList()) Remove(c.gameObject);
            foreach (GameObject go in made.Where(g => g != null)) Remove(go);
            if (hall != null) Remove(hall.gameObject);
            foreach (string id in new[] { foragerHutId, longhouseId })
            {
                ColonyBuilding left = ColonyBuilding.FindById(id);
                if (left != null) Remove(left.gameObject);
            }
            var pieces = new List<Piece>();
            Piece.GetAllPiecesInRadius(origin, 45f, pieces);
            foreach (Piece piece in pieces.Where(p => p != null && PiecePlacer.SiteOf(p.gameObject) is string site && site.Length > 0
                                                      && (site == foragerHutId || site == longhouseId || site == strangerHutId)).ToList())
                Remove(piece.gameObject);
            Log.Event("vc_lifetest: cleaned up");
        }

        private static void Remove(GameObject go)
        {
            ZNetView nview = go.GetComponent<ZNetView>();
            if (nview != null && nview.IsValid() && !nview.IsOwner()) nview.ClaimOwnership();
            ZNetScene.instance.Destroy(go);
        }

        private static FreeTimeRoutine Routine(Citizen c) => c.GetComponent<CitizenAI>().FreeTimeState;

        private static bool InFreeTime(Citizen c) => Routine(c).Active;

        private static bool Seated(SettlerPose pose) => pose != null && pose.Current == SettlerPose.Kind.Seat && pose.Held;

        private static bool OnSeat(SettlerPose pose, Chair[] chairs) =>
            pose.Current == SettlerPose.Kind.Seat && pose.Held && chairs.Any(c => c != null && pose.Point == c.m_attachPoint);

        private static bool Facing(Citizen a, Citizen b)
        {
            Vector3 to = b.transform.position - a.transform.position;
            to.y = 0f;
            return to.sqrMagnitude > 0.01f && Vector3.Angle(a.transform.forward, to) <= 35f;
        }

        private static void Check(string what, bool ok)
        {
            if (ok) s_pass++;
            else s_fail++;
            Log.Event($"vc_lifetest: {(ok ? "PASS" : "FAIL")} {what}");
        }
    }
}
