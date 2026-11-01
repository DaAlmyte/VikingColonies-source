using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using Jotunn.Entities;
using UnityEngine;
using VikingColonies.Buildings;
using VikingColonies.Citizens;
using VikingColonies.Construction;
using VikingColonies.Core;

namespace VikingColonies.DevTools
{
    /// <summary>vc_memorialtest (test worlds): the memorial stone and the fallen return (1.5.1), checked in the game.</summary>
    internal sealed class MemorialTestCommand : ConsoleCommand
    {
        public override string Name => "vc_memorialtest";
        public override string Help => "VikingColonies (test worlds): a Chieftain's Camp here, two of its settlers killed; the camp's fallen list, a " +
                                       "memorial stone's words, and the fallen return (the day skipped to morning). Puts the settings back and removes what it made. " +
                                       "'vc_memorialtest show': a memorial stone 6 m in front of you, pictures of its front, side and back (vc_shot memorial-runestone-0, -90, -180), and it's gone again.";
        public override bool IsCheat => true;

        public override void Run(string[] args)
        {
            Player p = Player.m_localPlayer;
            if (p == null || ZNet.instance == null || !TestWorlds.IsTestWorld(ZNet.instance.GetWorldName()))
            {
                BuildCommand.Print("vc_memorialtest: test worlds only");
                return;
            }
            Plugin.Instance.StartCoroutine(args.Length > 0 && args[0] == "show" ? MemorialTest.Show(p) : MemorialTest.Run(p));
        }
    }

    /// <summary>
    /// Written against the game and the mod's older parts (the camp's saved data read by its save keys, the settings by
    /// name), so it builds on the code before the stone too: its checks fail there first.
    /// </summary>
    internal static class MemorialTest
    {
        private static int s_pass, s_fail;

        public static IEnumerator Run(Player player)
        {
            s_pass = s_fail = 0;
            Log.Event("vc_memorialtest: starting");
            if (!player.InGodMode()) player.SetGodMode(true);   // never off: the test character has 1 health
            Plugin.Instance.Config.TryGetEntry("Colony", "FallenReturn", out ConfigEntry<bool> returnOn);
            Plugin.Instance.Config.TryGetEntry("Colony", "FallenReturnDays", out ConfigEntry<int> returnDays);
            bool onWas = returnOn != null && returnOn.Value;
            int daysWas = returnDays != null ? returnDays.Value : 3;
            Vector3 origin = player.transform.position, forward = player.transform.forward;
            forward.y = 0f;
            forward.Normalize();
            var made = new List<GameObject>();
            Character monster = null;
            try
            {
                if (returnOn != null) returnOn.Value = false;
                DevHelpers.ClearMonsters(origin, 60f, animalsToo: true);
                ColonyBuilding hall = DevHelpers.Place(BuildingTypes.TownHall.PrefabName, origin + forward * 8f, Quaternion.LookRotation(forward), player.GetPlayerID());
                if (hall != null) made.Add(hall.gameObject);
                TownHallModule townHall = hall != null ? hall.GetComponent<TownHallModule>() : null;
                for (float t = 0f; townHall != null && !townHall.Founded && t < 20f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                string colony = hall != null ? hall.ColonyId : "";
                List<Citizen> settlers = Citizen.All.Where(c => c != null && c.Record.ColonyId == colony && c.Body != null).ToList();
                if (townHall == null || !townHall.Founded || settlers.Count < 3)
                {
                    Check($"A Chieftain's Camp is founded with its settlers ({settlers.Count})", false);
                    yield break;
                }
                GameObject prefab = ZNetScene.instance.GetPrefab("Greydwarf");
                Vector3 aside = origin + Vector3.right * 20f;
                aside.y = ZoneSystem.instance.GetGroundHeight(aside);
                monster = prefab != null ? Object.Instantiate(prefab, aside, Quaternion.identity).GetComponent<Character>() : null;

                // 1. A death with the setting off: on the camp's fallen list, with no record.
                Citizen first = settlers[0];
                string firstName = first.CitizenName, firstJob = first.Record.Job;
                first.Body.Damage(KillHit(first, monster));
                yield return new WaitForSeconds(3f);
                FallenLine firstLine = Fallen(hall).Lines.FirstOrDefault(l => l.Name == firstName);
                Check($"A settler's death goes on the camp's fallen list, with no record while the return is off (line: {firstLine != null}, " +
                      $"job '{firstLine?.Job}', record {firstLine?.Record.Length ?? -1} characters)",
                    firstLine != null && firstLine.Job == firstJob && firstLine.Record.Length == 0);

                // 2. A memorial stone placed in the colony names them.
                GameObject stonePrefab = ZNetScene.instance.GetPrefab(SaveKeys.MemorialPiece);
                Vector3 side = Vector3.Cross(Vector3.up, forward);
                Vector3 stoneAt = origin + forward * 4f - side * 3f;
                stoneAt.y = ZoneSystem.instance.GetGroundHeight(stoneAt);
                GameObject stone = stonePrefab != null ? Object.Instantiate(stonePrefab, stoneAt, Quaternion.LookRotation(-forward)) : null;
                if (stone != null) made.Add(stone);
                string words = "";
                for (float t = 0f; stone != null && t < 10f && !Words(stone).Contains(firstName); t += 0.5f)
                {
                    yield return new WaitForSeconds(0.5f);
                    words = Words(stone);
                }
                words = stone != null ? Words(stone) : "";
                string hover = stone != null ? stone.GetComponent<Hoverable>()?.GetHoverText() ?? "" : "";
                string heading = Localization.instance.Localize("$vc_memorial");
                Check($"A memorial stone in the colony names the dead, and its hover counts them (stone: {stone != null}, words name them: " +
                      $"{words.Contains(firstName)}, hover '{DevHelpers.Plain(hover).Replace("\n", " / ")}')",
                    stone != null && words.Contains(firstName) && hover.Contains(heading) && hover.Contains("1"));

                // 2b (1.6.0). It wears one of the game's runestones (the look's name read as a word, as the rest is by save key),
                // standing with its foot a little in the ground.
                Transform look = stone != null ? stone.transform.Find("vc_runestone") : null;
                List<Renderer> drawn = look != null ? look.GetComponentsInChildren<MeshRenderer>().Where(r => r.GetComponent<MeshFilter>()?.sharedMesh != null).Cast<Renderer>().ToList() : new List<Renderer>();
                Bounds seen = drawn.Count > 0 ? drawn[0].bounds : new Bounds();
                foreach (Renderer r in drawn) seen.Encapsulate(r.bounds);
                float foot = seen.min.y - stoneAt.y;
                bool box = look != null && look.GetComponent<BoxCollider>() != null;
                // Nothing of the pillar left beside it (review of 1.6.0), a runestone's size and not a whole ruin's, and a look
                // at it from the side finds the stone (what its hover and E go by).
                int pillar = stone != null ? stone.GetComponentsInChildren<Renderer>(true).Count(r => look == null || !r.transform.IsChildOf(look))
                                             + stone.GetComponentsInChildren<Collider>(true).Count(c => look == null || !c.transform.IsChildOf(look)) : -1;
                Vector3 eye = seen.center + side * 4f;
                bool hit = stone != null && Physics.RaycastAll(eye, (seen.center - eye).normalized, 6f)
                    .Any(h => h.collider.GetComponentInParent<Colony.MemorialStone>()?.gameObject == stone);
                Check($"The memorial stone wears one of the game's runestones, its foot in the ground (parts {drawn.Count}, a box to stand and read: {box}, " +
                      $"seen from the side: {hit}, the pillar's parts left: {pillar}, foot {foot:0.00} m from the ground, {seen.size.x:0.0} x {seen.size.z:0.0} m, {seen.size.y:0.0} m tall)",
                    drawn.Count > 0 && box && hit && pillar == 0 && foot <= 0f && foot > -0.5f && seen.size.y > 0.8f && seen.size.y < 6f
                    && Mathf.Max(seen.size.x, seen.size.z) < 5f);

                // 3. The next morning with the return off: nobody comes back, and the list still names them.
                yield return SkipToMorning();
                yield return new WaitForSeconds(15f);
                bool firstBack = Citizen.All.Any(c => c != null && c.Record.ColonyId == colony && c.CitizenName == firstName && c.Body != null && !c.Body.IsDead());
                Check($"With the return off nobody comes back the next morning, and the camp still remembers them (back: {firstBack}, on the list: " +
                      $"{Fallen(hall).Lines.Any(l => l.Name == firstName)})", !firstBack && Fallen(hall).Lines.Any(l => l.Name == firstName));

                // 4. The return on, after 1 day: a second settler killed comes back the next morning, as themselves (with 0 days
                // and the test's morning already come, they'd be back at once, before the check could see them kept).
                if (returnOn != null) returnOn.Value = true;
                if (returnDays != null) returnDays.Value = 1;
                Citizen second = settlers[1];
                string secondName = second.CitizenName, secondId = second.Id, secondTrait = second.Record.Trait;
                second.Record.Woodcutting = 37f;
                second.Save();
                second.Body.Damage(KillHit(second, monster));
                yield return new WaitForSeconds(3f);
                bool recorded = Fallen(hall).Lines.Any(l => l.Name == secondName && l.Record.Length > 0);
                yield return SkipToMorning();
                Citizen back = null;
                for (float t = 0f; back == null && t < 30f; t += 1f)
                {
                    yield return new WaitForSeconds(1f);
                    back = Citizen.All.FirstOrDefault(c => c != null && c.Record.ColonyId == colony && c.CitizenName == secondName && c.Body != null && !c.Body.IsDead());
                }
                FallenLine secondLine = Fallen(hall).Lines.FirstOrDefault(l => l.Name == secondName);
                bool sagaTells = townHall.Saga.Page(0, SagaLog.MaxEntries).Any(e => e.Kind == "fallenback" && e.Args.Count > 0 && e.Args[0] == secondName);
                for (float t = 0f; stone != null && t < 10f && !StoneLine(stone, secondName).Returned; t += 0.5f) yield return new WaitForSeconds(0.5f);
                FallenLine stoneLine = stone != null ? StoneLine(stone, secondName) : null;
                Check($"With the return on, a settler killed comes back the next morning as themselves, with a new id (kept: {recorded}, back: {back != null}, " +
                      $"id new: {back != null && back.Id != secondId}, woodcutting {back?.Record.Woodcutting ?? -1:0}, trait '{back?.Record.Trait}' (was '{secondTrait}'), " +
                      $"returned on the list: {secondLine?.Returned}, the saga tells it: {sagaTells}, the stone marks it: {stoneLine?.Returned})",
                    recorded && back != null && back.Id != secondId && Mathf.Approximately(back.Record.Woodcutting, 37f) && back.Record.Trait == secondTrait
                    && secondLine != null && secondLine.Returned && sagaTells && stoneLine != null && stoneLine.Returned);
                if (back != null) made.Add(back.gameObject);

                // 5. E on the stone opens its words, like a runestone (a picture of the stone, and of its words).
                if (stone != null) player.transform.rotation = Quaternion.LookRotation((stone.transform.position - player.transform.position).normalized);
                yield return new WaitForSeconds(1f);
                new ShotCommand().Run(new[] { "memorial-stone" });
                bool read = stone != null && stone.GetComponent<Interactable>() is Interactable readable && readable.Interact(player, false, false);
                yield return new WaitForSeconds(2f);   // the game's text window fades in (seen: nothing yet at 1 s, the words at 1.5 s)
                bool shown = TextViewer.instance != null && TextViewer.instance.IsVisible();
                new ShotCommand().Run(new[] { "memorial-read" });
                yield return new WaitForSeconds(1f);
                TextViewer.instance?.Hide();
                Check($"E on the memorial stone opens its words, like a runestone (read: {read}, shown: {shown})", read && shown);
            }
            finally
            {
                if (returnOn != null) returnOn.Value = onWas;
                if (returnDays != null) returnDays.Value = daysWas;
                if (monster != null) made.Add(monster.gameObject);
                DevHelpers.ClearMonsters(origin, 60f, animalsToo: true);
                foreach (GameObject go in made.Where(g => g != null)) Remove(go);
                Log.Event($"vc_memorialtest: done — {s_pass} passed, {s_fail} failed");
                Player.m_localPlayer?.Message(MessageHud.MessageType.Center, $"vc_memorialtest: {s_pass} passed, {s_fail} failed");
            }
        }

        /// <summary>The stone's look for the player (1.6.0): one put down in front of them, a picture, and it's taken away.</summary>
        public static IEnumerator Show(Player player)
        {
            GameObject prefab = ZNetScene.instance.GetPrefab(SaveKeys.MemorialPiece);
            Vector3 forward = GameCamera.instance != null ? GameCamera.instance.transform.forward : player.transform.forward;   // where the picture looks
            forward.y = 0f;
            forward.Normalize();
            Vector3 at = player.transform.position + forward * 7f;
            at.y = ZoneSystem.instance.GetGroundHeight(at);
            GameObject stone = prefab != null ? Object.Instantiate(prefab, at, Quaternion.LookRotation(-forward)) : null;
            // Its front, side and back: a picture each.
            foreach (float turn in new[] { 0f, 90f, 180f })
            {
                if (stone != null) stone.transform.rotation = Quaternion.LookRotation(-forward) * Quaternion.Euler(0f, turn, 0f);
                yield return new WaitForSeconds(2f);
                new ShotCommand().Run(new[] { "memorial-runestone-" + turn.ToString("0") });
            }
            yield return new WaitForSeconds(1f);
            if (stone != null) Remove(stone);
            Log.Event($"vc_memorialtest: shown ({(stone != null ? "a stone" : "no stone")})");
        }

        private static ZDO Zdo(GameObject go)
        {
            ZNetView nview = go != null ? go.GetComponent<ZNetView>() : null;
            return nview != null && nview.IsValid() ? nview.GetZDO() : null;
        }

        private static FallenList Fallen(ColonyBuilding hall) => FallenList.Decode(Zdo(hall != null ? hall.gameObject : null)?.GetString(SaveKeys.Fallen, "") ?? "");

        private static string Words(GameObject stone) => Zdo(stone)?.GetString(SaveKeys.MemorialWords, "") ?? "";

        /// <summary>The stone's line for a name, or an empty line (not returned) when it has none.</summary>
        private static FallenLine StoneLine(GameObject stone, string name) =>
            FallenList.Decode(Words(stone)).Lines.FirstOrDefault(l => l.Name == name) ?? new FallenLine();

        /// <summary>The game's own skip to the next morning (as sleeping does), waited out.</summary>
        private static IEnumerator SkipToMorning()
        {
            int day = EnvMan.instance.GetDay();
            EnvMan.instance.SkipToMorning();
            for (float t = 0f; t < 30f && (EnvMan.instance.IsTimeSkipping() || EnvMan.instance.GetDay() == day); t += 0.5f) yield return new WaitForSeconds(0.5f);
            Log.Event($"vc_memorialtest: skipped to the morning of day {EnvMan.instance.GetDay()}");
        }

        private static HitData KillHit(Citizen victim, Character monster)
        {
            var kill = new HitData();
            kill.m_damage.m_slash = 10000f;
            kill.m_hitType = HitData.HitType.EnemyHit;
            kill.m_point = victim.transform.position;
            if (monster != null) kill.SetAttacker(monster);
            return kill;
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
            Log.Event($"vc_memorialtest: {(ok ? "PASS" : "FAIL")} {what}");
        }
    }
}
