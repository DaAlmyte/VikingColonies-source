using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Jotunn.Entities;
using UnityEngine;
using VikingColonies.Buildings;
using VikingColonies.Citizens;
using VikingColonies.AI;
using VikingColonies.Colony;
using VikingColonies.Config;
using VikingColonies.Construction;
using VikingColonies.Core;

namespace VikingColonies.DevTools
{
    /// <summary>vc_pastimetest (test worlds): the settlers' pastimes (1.6), checked in the game.</summary>
    internal sealed class PastimeTestCommand : ConsoleCommand
    {
        public override string Name => "vc_pastimetest";
        public override string Help => "VikingColonies (test worlds): a Chieftain's Camp here with an archery target, a training dummy, a table with two " +
                                       "chairs, a banner pole, a trophy on a stand, a cartography table and a fire; its settlers take pastimes there. " +
                                       "Removes what it made.";
        public override bool IsCheat => true;

        public override void Run(string[] args)
        {
            Player p = Player.m_localPlayer;
            if (p == null || ZNet.instance == null || !TestWorlds.IsTestWorld(ZNet.instance.GetWorldName()))
            {
                BuildCommand.Print("vc_pastimetest: test worlds only");
                return;
            }
            Plugin.Instance.StartCoroutine(PastimeTest.Run(p));
        }
    }

    /// <summary>
    /// Checks the pastimes in the game (the plan's Tasks 3, 4 and 6): each logs "vc_pastimetest: check N PASS|FAIL", then
    /// "vc_pastimetest: done". Written before the settlers take pastimes, so its checks of them fail there first.
    /// </summary>
    internal static class PastimeTest
    {
        private static int s_pass, s_fail;

        public static IEnumerator Run(Player player)
        {
            s_pass = s_fail = 0;
            MoodTracker.HoldForTest = false;
            // A quarter of an hour of the day ahead at least (as the life test: a new day mid-test brings breakfast).
            double nowSeconds = ZNet.instance.GetTimeSeconds(), dayLength = EnvMan.instance.m_dayLengthSec;
            double intoDay = nowSeconds % dayLength;
            if (dayLength - intoDay < 900.0) ZNet.instance.SetNetTime(nowSeconds - intoDay + dayLength + 1.0);
            Log.Event("vc_pastimetest: starting");
            if (!player.InGodMode()) player.SetGodMode(true);   // never off: the test character has 1 health
            EnvMan.instance.m_debugTimeOfDay = true;
            EnvMan.instance.m_debugTime = 0.5f;
            EnvMan.instance.SetForceEnvironment("Clear");
            bool freeTimeWas = ColonyConfig.FreeTime.Value, pastimesWas = ColonyConfig.Pastimes.Value, walkInWas = ColonyConfig.NewcomersWalkIn.Value;
            bool moodWas = ColonyConfig.Mood.Value;
            ColonyConfig.FreeTime.Value = ColonyConfig.Pastimes.Value = ColonyConfig.NewcomersWalkIn.Value = ColonyConfig.Mood.Value = true;
            Vector3 origin = player.transform.position, forward = player.transform.forward;
            forward.y = 0f;
            forward.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Quaternion facing = Quaternion.LookRotation(forward);
            var made = new List<GameObject>();
            try
            {
                DevHelpers.ClearMonsters(origin, 60f, animalsToo: true);
                ColonyBuilding camp = DevHelpers.Place(BuildingTypes.TownHall.PrefabName, origin + forward * 8f, facing, player.GetPlayerID());
                if (camp != null) made.Add(camp.gameObject);
                DevHelpers.KeepNothing(camp);
                TownHallModule townHall = camp != null ? camp.GetComponent<TownHallModule>() : null;
                for (float t = 0f; townHall != null && !townHall.Founded && t < 20f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                if (camp == null || townHall == null || !townHall.Founded)
                {
                    Check(0, $"A Chieftain's Camp is founded (camp {camp != null})", false);
                    yield break;
                }
                string colony = camp.ColonyId;
                Vector3 c = camp.transform.position;

                // The pieces round the camp, each clear of the others (the target's archers stand 9 m before it).
                GameObject target = Spawn("piece_ArcheryTarget", OnGround(c - right * 16f - forward * 4f), facing, made);
                GameObject dummy = Spawn("piece_TrainingDummy", OnGround(c - right * 9f + forward * 8f), facing, made);
                Vector3 tableAt = OnGround(c + right * 10f + forward * 8f);
                GameObject table = Spawn("piece_table", tableAt, facing, made);
                Spawn("piece_chair", tableAt + right * 1f, Quaternion.LookRotation(-right), made);
                Spawn("piece_chair", tableAt - right * 1f, Quaternion.LookRotation(right), made);
                // The camp's banner pole (1.4.3): a 4 m log with a banner hung by it 5.2 m up (held up here, with no beam).
                Vector3 poleAt = OnGround(c + forward * 16f);
                GameObject pole = Spawn("wood_pole_log_4", poleAt, facing, made);
                GameObject banner = Spawn("piece_banner01", poleAt + right * 1f + Vector3.up * 5.2f, facing, made);
                Unsupported(banner);
                GameObject stand = Spawn("itemstand", OnGround(c + right * 16f - forward * 6f) + Vector3.up * 1.2f, Quaternion.LookRotation(-right), made);
                Unsupported(stand);
                GameObject map = Spawn("piece_cartographytable", OnGround(c + right * 16f + forward * 2f), Quaternion.LookRotation(-right), made);
                GameObject fireGo = Spawn("fire_pit", OnGround(c - right * 6f - forward * 8f), facing, made);
                Fireplace fire = fireGo != null ? fireGo.GetComponent<Fireplace>() : null;
                SetFuel(fire, fire != null ? fire.m_maxFuel : 0f);
                yield return new WaitForSeconds(1f);
                yield return PutTrophy(player, stand);

                // 1. The colony finds each piece, with what it holds.
                ColonyPlaces.Forget();
                IReadOnlyList<PropPlace> props = ColonyPlaces.Props(colony);
                PropPlace Found(GameObject go) => go != null ? props.FirstOrDefault(p => p.Piece != null && p.Piece.gameObject == go) : null;
                string Say(string what, PropPlace p, Prop kind) => $"{what} {(p == null ? "missing" : p.Kind == kind ? $"holds {p.Capacity}" : $"as {p.Kind}")}";
                PropPlace fTarget = Found(target), fDummy = Found(dummy), fTable = Found(table), fPole = Found(pole), fStand = Found(stand), fMap = Found(map);
                Check(1, $"The colony finds its pastimes' pieces: {Say("target", fTarget, Prop.Target)}, {Say("dummy", fDummy, Prop.Dummy)}, " +
                         $"{Say("table", fTable, Prop.Table)}, {Say("banner pole", fPole, Prop.Pole)}, {Say("trophy", fStand, Prop.Trophy)}, " +
                         $"{Say("map table", fMap, Prop.MapTable)} ({props.Count} in all)",
                    Is(fTarget, Prop.Target, 2) && Is(fDummy, Prop.Dummy, 2) && Is(fTable, Prop.Table, 2) && Is(fPole, Prop.Pole, 6)
                    && Is(fStand, Prop.Trophy, 2) && Is(fMap, Prop.MapTable, 1));
                foreach (PropPlace p in props)
                    Log.Event($"vc_pastimetest: {p.Kind} at ({p.Position.x:0.0}, {p.Position.z:0.0}), roofed {p.Roofed}, spots " +
                              string.Join(" ", p.Spots.Select(s => $"({s.x:0.0}, {s.y:0.0}, {s.z:0.0})")));

                // A wall beside the dummy (check 4: a practice blow that strays hurts nothing).
                GameObject wall = Spawn("woodwall", OnGround(c - right * 7f + forward * 8f), facing, made);
                float dummyHealth = HealthOf(dummy), wallHealth = HealthOf(wall), playerHealth = player.GetHealth();
                Log.Event($"vc_pastimetest: the dummy's health {dummyHealth:0} ({Parts(dummy)}), the wall's {wallHealth:0}");

                // Four jobless settlers walk in from the colony's edge (as in the life test).
                List<Citizen> settlers = new List<Citizen>();
                for (float t = 0f; t < 30f && settlers.Count < 4; t += 0.5f)
                {
                    yield return new WaitForSeconds(0.5f);
                    settlers = Citizen.All.Where(x => x != null && x.ColonyId == colony && x.IsOwner).ToList();
                }
                for (float t = 0f; t < 90f && settlers.Any(x => x.Record.Arriving); t += 0.5f) yield return new WaitForSeconds(0.5f);
                yield return new WaitForSeconds(3f);
                if (settlers.Count < 4 || !settlers.All(x => Routine(x) != null && Routine(x).Active))
                {
                    Check(2, $"Four jobless settlers in free time ({settlers.Count} came, {settlers.Count(x => Routine(x) != null && Routine(x).Active)} in free time)", false);
                    yield break;
                }
                var health = settlers.ToDictionary(x => x, x => x.GetComponent<Character>().GetHealth());
                var tooClose = new List<string>();
                float wallSeen = wallHealth;
                void Sample()
                {
                    // Who was by the wall when it lost health (check 4's trace).
                    float wallNow = HealthOf(wall);
                    if (wallNow < wallSeen - 0.01f)
                    {
                        Log.Event($"vc_pastimetest: the wall lost {wallSeen - wallNow:0.0} health; near it: " + string.Join(", ", Citizen.All
                            .Where(x => x != null && Vector3.Distance(x.transform.position, wall.transform.position) < 5f)
                            .Select(x => $"{x.CitizenName} ({Routine(x)?.Doing}/{Routine(x)?.CurrentPastime}, {Vector3.Distance(x.transform.position, wall.transform.position):0.0} m, " +
                                         $"in attack {x.Body.InAttack()})")));
                        wallSeen = wallNow;
                    }
                    var at = settlers.Where(x => x != null && Routine(x).CurrentPastime != Pastime.None && Routine(x).Arrived).ToList();
                    for (int i = 0; i < at.Count; i++)
                        for (int j = i + 1; j < at.Count; j++)
                            if (Utils.DistanceXZ(at[i].transform.position, at[j].transform.position) < 0.6f)
                                tooClose.Add($"{at[i].CitizenName} and {at[j].CitizenName} at {Routine(at[i]).CurrentPastime}");
                }

                // 2. Each pastime with its piece, and stretching, sparring and a nap, seen once: forced on one settler (the
                // others strolling, so free to join a pair), their arrival awaited.
                var seen = new List<string>();
                var missed = new List<string>();
                var swung = new List<string>();   // check 4: the blows were played, not skipped
                Pastime[] each = { Pastime.Archery, Pastime.WeaponPractice, Pastime.BoardGame, Pastime.Toast, Pastime.PoleDance, Pastime.Story,
                                   Pastime.Admire, Pastime.Map, Pastime.Stretch, Pastime.Spar, Pastime.Nap };
                for (int n = 0; n < each.Length; n++)
                {
                    Pastime pastime = each[n];
                    Citizen doer = settlers[n % settlers.Count];
                    foreach (Citizen other in settlers) Routine(other).ForceForTest(Leisure.Stroll);   // the doer too: no choice of its own in the wait (a toast took the others)
                    yield return new WaitForSeconds(1.5f);
                    Routine(doer).ForceForTest(pastime);
                    SettlerPose doerPose = doer.GetComponent<SettlerPose>();
                    int gesturesBefore = doerPose != null ? doerPose.GesturesPlayed : 0;
                    bool there = false;
                    for (float t = 0f; t < 45f && !there; t += 0.5f)
                    {
                        yield return new WaitForSeconds(0.5f);
                        Sample();
                        there = Routine(doer).CurrentPastime == pastime && Routine(doer).Arrived;
                    }
                    // A toast stands on the table's floor, not up on a bench or the next table nor below a raised floor (v16's meadhall.txt, 27 Oct).
                    PropPlace toastTable = pastime == Pastime.Toast && there ? Routine(doer).PropForTest : null;
                    float up = toastTable != null && toastTable.Valid ? doer.transform.position.y - toastTable.Position.y : 0f;
                    if (up > PastimeRules.ToastStep + 0.1f || -up > PastimeRules.ToastDrop + 0.1f)
                    {
                        there = false;
                        missed.Add($"Toast ({doer.CitizenName} {up:0.0} m from the table's floor)");
                    }
                    bool blows = pastime == Pastime.WeaponPractice || pastime == Pastime.Spar;
                    for (float t = 0f; there && t < (blows ? 7f : 4f); t += 1f)
                    {
                        yield return new WaitForSeconds(1f);
                        Sample();
                    }
                    if (there || toastTable == null) (there ? seen : missed).Add($"{pastime} ({doer.CitizenName})");
                    if (blows && doerPose != null && doerPose.GesturesPlayed > gesturesBefore) swung.Add($"{pastime} {doerPose.GesturesPlayed - gesturesBefore}");
                }
                Check(2, $"Each pastime is taken: seen {string.Join(", ", seen)}; missed {(missed.Count == 0 ? "none" : string.Join(", ", missed))}", missed.Count == 0);

                // A while left to chance: their own choices.
                foreach (Citizen x in settlers) Routine(x).ChooseAgainForTest();
                for (float t = 0f; t < 60f; t += 1f)
                {
                    yield return new WaitForSeconds(1f);
                    Sample();
                }
                // 3. Never two on one spot.
                Check(3, $"No two settlers stand on one pastime's spot ({(tooClose.Count == 0 ? "none closer than 0.6 m" : string.Join("; ", tooClose.Distinct().Take(5)))})",
                    tooClose.Count == 0);

                // 4. Practice hurts nothing: the dummy, the wall beside it, the settlers, you.
                var hurt = settlers.Where(x => x != null && x.GetComponent<Character>().GetHealth() < health[x] - 0.01f).Select(x => x.CitizenName).ToList();
                Check(4, $"Practice hurts nothing (blows played: {(swung.Count == 0 ? "none" : string.Join(", ", swung))}; dummy {HealthOf(dummy):0}/{dummyHealth:0}, " +
                         $"wall {HealthOf(wall):0}/{wallHealth:0}, you {player.GetHealth():0}/{playerHealth:0}, settlers hurt: {(hurt.Count == 0 ? "none" : string.Join(", ", hurt))})",
                    swung.Count == 2 && dummyHealth > 0f && wallHealth > 0f && HealthOf(dummy) >= dummyHealth - 0.01f && HealthOf(wall) >= wallHealth - 0.01f
                    && player.GetHealth() >= playerHealth - 0.01f && hurt.Count == 0);

                // 8. Three kinds or more: "varied free time" in the mood.
                Citizen varied = settlers.OrderByDescending(x => PastimeMemory.Distinct(x.Record.LastPastimes, Today())).First();
                MoodTracker.WorkOut(varied);
                Check(8, $"Varied free time lifts the mood ({varied.CitizenName}: {PastimeMemory.Distinct(varied.Record.LastPastimes, Today())} kinds, " +
                         $"last {string.Join(" ", varied.Record.LastPastimes ?? new List<string>())}; mood says '{varied.Record.WhyMood}')",
                    PastimeMemory.Distinct(varied.Record.LastPastimes, Today()) >= 3 && (varied.Record.WhyMood ?? "").Contains("$vc_mood_varied_free"));

                // 5. The table taken away mid-game: both get up and choose again within 5 s, no error.
                foreach (Citizen x in settlers) Routine(x).ForceForTest(Leisure.Stroll);   // all four, as in check 2
                yield return new WaitForSeconds(1.5f);
                Routine(settlers[0]).ForceForTest(Pastime.BoardGame);
                List<Citizen> players = new List<Citizen>();
                for (float t = 0f; t < 45f && players.Count < 2; t += 0.5f)
                {
                    yield return new WaitForSeconds(0.5f);
                    players = settlers.Where(x => Routine(x).CurrentPastime == Pastime.BoardGame && Routine(x).Arrived).ToList();
                }
                bool chose = false;
                if (players.Count == 2 && table != null)
                {
                    Remove(table);
                    for (float t = 0f; t < 5f && !chose; t += 0.25f)
                    {
                        yield return new WaitForSeconds(0.25f);
                        chose = players.All(x => Routine(x).CurrentPastime != Pastime.BoardGame);
                    }
                }
                Check(5, $"The table taken away mid-game: they choose again ({players.Count} were playing: {string.Join(", ", players.Select(x => x.CitizenName))}; " +
                         $"now {string.Join(", ", players.Select(x => x.CitizenName + " " + Routine(x).Doing + "/" + Routine(x).CurrentPastime))})",
                    players.Count == 2 && chose);

                // 7. Rain: an open-air target and a roofed table (a floor over it): nobody at the target, the table used.
                Vector3 roofedAt = OnGround(c + right * 4f - forward * 14f);
                GameObject roofedTable = Spawn("piece_table", roofedAt, facing, made);
                Spawn("piece_chair", roofedAt + right * 1f, Quaternion.LookRotation(-right), made);
                Spawn("piece_chair", roofedAt - right * 1f, Quaternion.LookRotation(right), made);
                foreach (float side in new[] { -1f, 1f })
                    Unsupported(Spawn("wood_floor", roofedAt + right * side + Vector3.up * 2.6f, facing, made));
                EnvMan.instance.SetForceEnvironment("Rain");
                yield return new WaitForSeconds(4f);
                ColonyPlaces.Forget();
                PropPlace under = ColonyPlaces.Props(colony).FirstOrDefault(x => x.Piece != null && x.Piece.gameObject == roofedTable);
                foreach (Citizen x in settlers) Routine(x).ForceForTest(Leisure.Stroll);   // all four, as in check 2
                yield return new WaitForSeconds(1.5f);
                Routine(settlers[0]).ForceForTest(Pastime.BoardGame);
                Routine(settlers[2]).ForceForTest(Pastime.Archery);   // not possible in rain at an open-air target
                bool atTarget = false, roofedUsed = false;
                for (float t = 0f; t < 60f; t += 0.5f)
                {
                    yield return new WaitForSeconds(0.5f);
                    atTarget |= settlers.Any(x => Routine(x).CurrentPastime == Pastime.Archery);
                    roofedUsed |= settlers.Any(x => Routine(x).Arrived && Routine(x).PropForTest != null && Routine(x).PropForTest.Piece != null
                                                     && Routine(x).PropForTest.Piece.gameObject == roofedTable);
                    if (t > 15f && roofedUsed) break;
                }
                Check(7, $"In rain only the roofed table is used (wet {EnvMan.IsWet()}, the table roofed {under != null && under.Roofed}; at the target {atTarget}, at the table {roofedUsed})",
                    EnvMan.IsWet() && under != null && under.Roofed && !atTarget && roofedUsed);
                EnvMan.instance.SetForceEnvironment("Clear");

                // 6. A settler mid-pastime when the evening comes stops at once, the dance's pose let go; then some dance at the
                // fire; night: everyone stops. The evening needs a hut to go into (as in the life test): a Longhouse, its own
                // roofed fire put out (a roofed fire draws the evening indoors).
                Vector3 houseSpot = new[] { c - forward * 28f, c + forward * 30f, c - right * 30f, c + right * 32f }
                    .Select(OnGround)
                    .FirstOrDefault(p => DevHelpers.Judge(BuildingTypes.Longhouse, p, Quaternion.identity) is SiteVerdict v && v.Colour != SiteColour.Red);
                ColonyBuilding longhouse = houseSpot != Vector3.zero
                    ? DevHelpers.Place(BuildingTypes.Longhouse.PrefabName, houseSpot, Quaternion.identity, player.GetPlayerID()) : null;
                if (longhouse != null) made.Add(longhouse.gameObject);
                ConstructionModule houseSite = longhouse != null ? longhouse.GetComponent<ConstructionModule>() : null;
                for (float t = 0f; houseSite != null && houseSite.Order == null && t < 15f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                if (houseSite != null && houseSite.Order != null) houseSite.FinishNow();
                for (float t = 0f; houseSite != null && !houseSite.IsBuilt && t < 40f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                ColonyPlaces.Forget();
                foreach (FirePlace other in ColonyPlaces.Fires(colony))
                    if (other != null && other.Valid && !other.AtHall && other.Fire != fire) SetFuel(other.Fire, 0f);
                Log.Event($"vc_pastimetest: a Longhouse for the evening: {(houseSite != null && houseSite.IsBuilt ? "built" : "none")}");
                foreach (Citizen x in settlers) Routine(x).ForceForTest(Leisure.Stroll);   // all four, as in check 2
                yield return new WaitForSeconds(1.5f);
                Citizen dancer = settlers[0];
                SettlerPose dancerPose = dancer.GetComponent<SettlerPose>();
                Routine(dancer).ForceForTest(Pastime.PoleDance);
                bool dancing = false;
                for (float t = 0f; t < 45f && !dancing; t += 0.5f)
                {
                    yield return new WaitForSeconds(0.5f);
                    dancing = Routine(dancer).CurrentPastime == Pastime.PoleDance && Routine(dancer).Arrived && dancerPose.Current == SettlerPose.Kind.Dance;
                }
                int dancesBefore = FreeTimeRoutine.EveningDancesForTest;
                EnvMan.instance.m_debugTime = 0.72f;
                bool stoppedAtOnce = false;
                for (float t = 0f; dancing && t < 3f && !stoppedAtOnce; t += 0.25f)
                {
                    yield return new WaitForSeconds(0.25f);
                    stoppedAtOnce = Routine(dancer).CurrentPastime == Pastime.None && dancerPose.Current != SettlerPose.Kind.Dance;
                }
                FreeTimeRoutine.EveningDanceForTest = true;
                for (float t = 0f; t < 60f && FreeTimeRoutine.EveningDancesForTest == dancesBefore; t += 0.5f) yield return new WaitForSeconds(0.5f);
                int dances = FreeTimeRoutine.EveningDancesForTest - dancesBefore;
                FreeTimeRoutine.EveningDanceForTest = false;
                EnvMan.instance.m_debugTime = 0f;   // midnight
                bool stopped = false;
                for (float t = 0f; t < 30f && !stopped; t += 0.5f)
                {
                    yield return new WaitForSeconds(0.5f);
                    stopped = settlers.All(x => x == null || Routine(x).CurrentPastime == Pastime.None);
                }
                stopped &= settlers.All(x => x == null || x.GetComponent<SettlerPose>().Current != SettlerPose.Kind.Dance);
                Check(6, $"The evening stops a pastime at once ({dancer.CitizenName} dancing round the pole {dancing}, stopped within 3 s {stoppedAtOnce}), " +
                         $"some dance at the fire in the evening ({dances}), and at night everyone stops " +
                         $"({string.Join(", ", settlers.Where(x => x != null).Select(x => x.CitizenName + " " + Routine(x).CurrentPastime + "/" + x.GetComponent<SettlerPose>().Current))})",
                    dancing && stoppedAtOnce && dances > 0 && stopped);
                EnvMan.instance.m_debugTime = 0.5f;
            }
            finally
            {
                FreeTimeRoutine.EveningDanceForTest = false;
                EnvMan.instance.m_debugTimeOfDay = false;
                EnvMan.instance.SetForceEnvironment("");
                ColonyConfig.FreeTime.Value = freeTimeWas;
                ColonyConfig.Pastimes.Value = pastimesWas;
                ColonyConfig.NewcomersWalkIn.Value = walkInWas;
                ColonyConfig.Mood.Value = moodWas;
                DevHelpers.ClearMonsters(origin, 60f, animalsToo: true);
                foreach (GameObject go in made.Where(g => g != null)) Remove(go);
                Log.Event($"vc_pastimetest: done — {s_pass} passed, {s_fail} failed");
                Player.m_localPlayer?.Message(MessageHud.MessageType.Center, $"vc_pastimetest: {s_pass} passed, {s_fail} failed");
            }
        }

        private static bool Is(PropPlace p, Prop kind, int capacity) => p != null && p.Kind == kind && p.Capacity == capacity;

        private static FreeTimeRoutine Routine(Citizen c) => c != null ? c.GetComponent<CitizenAI>()?.FreeTimeState : null;

        /// <summary>
        /// A piece's health, whatever keeps it: a building piece's wear, a creature's body (the training dummy is one), or a
        /// destructible's; -1 when it has none.
        /// </summary>
        private static float HealthOf(GameObject go)
        {
            if (go == null) return -1f;
            Character body = go.GetComponentInChildren<Character>();
            if (body != null) return body.GetHealth();
            ZNetView view = go.GetComponent<ZNetView>();
            if (view == null || !view.IsValid()) return -1f;
            WearNTear wear = go.GetComponentInChildren<WearNTear>();
            Destructible destructible = go.GetComponentInChildren<Destructible>();
            if (wear == null && destructible == null) return -1f;
            return view.GetZDO().GetFloat(ZDOVars.s_health, wear != null ? wear.m_health : destructible.m_health);
        }

        private static string Parts(GameObject go) =>
            go == null ? "none" : string.Join(", ", go.GetComponentsInChildren<Component>().Select(c => c.GetType().Name).Distinct().Where(n => n != "Transform" && n != "MeshFilter" && n != "MeshRenderer").Take(12));

        private static int Today() => EnvMan.instance.GetDay(ZNet.instance.GetTimeSeconds());

        /// <summary>A boar's trophy on the stand, the game's own way (from the player's hands).</summary>
        private static IEnumerator PutTrophy(Player player, GameObject standGo)
        {
            ItemStand stand = standGo != null ? standGo.GetComponentInChildren<ItemStand>() : null;
            if (stand == null) yield break;
            player.GetInventory().AddItem(ObjectDB.instance.GetItemPrefab("TrophyBoar"), 1);
            ItemDrop.ItemData trophy = player.GetInventory().GetAllItems().FirstOrDefault(i => i.m_dropPrefab != null && i.m_dropPrefab.name == "TrophyBoar");
            if (trophy != null) stand.UseItem(player, trophy);
            for (float t = 0f; t < 5f && !stand.HaveAttachment(); t += 0.25f) yield return new WaitForSeconds(0.25f);
            Log.Event($"vc_pastimetest: the stand holds a trophy: {stand.HaveAttachment()}");
        }

        /// <summary>Hung in the air for the test: it keeps standing with nothing under it.</summary>
        private static void Unsupported(GameObject go)
        {
            WearNTear wear = go != null ? go.GetComponent<WearNTear>() : null;
            if (wear != null) wear.m_noSupportWear = false;
        }

        private static void SetFuel(Fireplace fire, float fuel)
        {
            ZNetView view = fire != null ? fire.GetComponent<ZNetView>() : null;
            if (view == null || !view.IsValid()) return;
            if (!view.IsOwner()) view.ClaimOwnership();
            view.GetZDO().Set(ZDOVars.s_fuel, fuel);
        }

        private static GameObject Spawn(string prefab, Vector3 at, Quaternion facing, List<GameObject> made)
        {
            GameObject source = ZNetScene.instance.GetPrefab(prefab);
            GameObject go = source != null ? Object.Instantiate(source, at, facing) : null;
            if (go != null) made.Add(go);
            else Log.Event($"vc_pastimetest: no prefab {prefab}");
            return go;
        }

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

        private static void Check(int n, string what, bool ok)
        {
            if (ok) s_pass++;
            else s_fail++;
            Log.Event($"vc_pastimetest: check {n} {(ok ? "PASS" : "FAIL")} {what}");
        }
    }
}
