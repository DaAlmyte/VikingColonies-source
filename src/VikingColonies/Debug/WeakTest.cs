using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using Jotunn.Entities;
using UnityEngine;
using VikingColonies.AI;
using VikingColonies.Buildings;
using VikingColonies.Citizens;
using VikingColonies.Config;
using VikingColonies.Construction;
using VikingColonies.Core;

namespace VikingColonies.DevTools
{
    /// <summary>vc_weaktest (test worlds): settlers stand up to weak animals (1.5.0), checked in the game in a few minutes.</summary>
    internal sealed class WeakTestCommand : ConsoleCommand
    {
        public override string Name => "vc_weaktest";
        public override string Help => "VikingColonies (test worlds): a colony here with a Lumberjack's Hut and a Watchtower; Necks, a Troll and a " +
                                       "Greyling set on its settlers: they fight weak ones off, run when hurt or from strong ones, heal, and the guard " +
                                       "drives a weak one out of the colony. Removes what it made.";
        public override bool IsCheat => true;

        public override void Run(string[] args)
        {
            Player p = Player.m_localPlayer;
            if (p == null || ZNet.instance == null || !TestWorlds.IsTestWorld(ZNet.instance.GetWorldName()))
            {
                BuildCommand.Print("vc_weaktest: test worlds only");
                return;
            }
            Plugin.Instance.StartCoroutine(WeakTest.Run(p));
        }
    }

    /// <summary>
    /// Written against the game and the mod's older parts only (statuses, health, the setting found by name), so it builds on
    /// the code before 1.5.0 too: its new checks fail there first. Checks 2 and 3 hold today's behaviour.
    /// </summary>
    internal static class WeakTest
    {
        private static int s_pass, s_fail;
        private static readonly AccessTools.FieldRef<MonsterAI, Character> s_target = AccessTools.FieldRefAccess<MonsterAI, Character>("m_targetCreature");
        private static readonly System.Reflection.MethodInfo s_setAlerted = AccessTools.Method(typeof(BaseAI), "SetAlerted", new[] { typeof(bool) });

        private sealed class Seen
        {
            public bool Fought, Fled, FoughtBelowHalf;
            /// <summary>How far the nearest watched creature was when they were first seen running (-1: never).</summary>
            public float FledAt = -1f;
        }

        // A settler whose trait a check changes, and the trait to give back (put back in the finally too: the review of 1.5.1).
        private static Citizen s_traitOf;
        private static string s_traitWas;

        public static IEnumerator Run(Player player)
        {
            s_pass = s_fail = 0;
            Log.Event("vc_weaktest: starting");
            if (!player.InGodMode()) player.SetGodMode(true);   // never off: the test character has 1 health
            EnvMan.instance.m_debugTimeOfDay = true;
            EnvMan.instance.m_debugTime = 0.4f;   // day: nobody goes to bed
            Vector3 origin = player.transform.position, forward = player.transform.forward, right = player.transform.right;
            forward.y = right.y = 0f;
            forward.Normalize();
            right.Normalize();
            Quaternion facing = Quaternion.LookRotation(forward);
            var before = new HashSet<string>(Citizen.All.Where(c => c != null).Select(c => c.Id));
            var made = new List<GameObject>();
            ConfigEntry<bool> fightBack = null;
            Plugin.Instance.Config.TryGetEntry(new ConfigDefinition(ConfigNames.ColonySection, "SettlersFightBack"), out fightBack);
            bool fightBackWas = fightBack == null || fightBack.Value;
            bool walkInWas = ColonyConfig.NewcomersWalkIn.Value;
            try
            {
                ColonyConfig.NewcomersWalkIn.Value = false;
                DevHelpers.ClearMonsters(origin, 90f, animalsToo: true);
                ColonyBuilding hall = DevHelpers.Place(BuildingTypes.TownHall.PrefabName, origin + forward * 8f, facing, player.GetPlayerID());
                if (hall != null) made.Add(hall.gameObject);
                DevHelpers.KeepNothing(hall);
                TownHallModule townHall = hall != null ? hall.GetComponent<TownHallModule>() : null;
                for (float t = 0f; townHall != null && !townHall.Founded && t < 20f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                if (townHall == null || !townHall.Founded)
                {
                    Check("A Town Hall is founded", false);
                    yield break;
                }
                Vector3 hallAt = hall.transform.position;
                string colony = hall.ColonyId;

                // A Lumberjack's Hut behind the hall, an axe in its chest, and its worker.
                ColonyBuilding hut = DevHelpers.Place(BuildingTypes.LumberjackHut.PrefabName, OnGround(hallAt - forward * 16f), facing, player.GetPlayerID());
                if (hut != null) made.Add(hut.gameObject);
                yield return Finish(hut);
                hut?.GetComponent<Container>()?.GetInventory()?.AddItem(Prefab("AxeStone"), 1);
                Citizen lumber = null;
                WorkerModule work = hut != null ? hut.GetComponent<WorkerModule>() : null;
                for (float t = 0f; work != null && lumber == null && t < 30f; t += 0.5f)
                {
                    yield return new WaitForSeconds(0.5f);
                    if (work.FindWorker(out Citizen found, out _) == WorkerModule.WorkerState.Loaded) lumber = found;
                }
                if (lumber == null)
                {
                    Check($"A Lumberjack's Hut is put up and hires a worker (hut {hut != null})", false);
                    yield break;
                }
                yield return new WaitForSeconds(3f);

                // The lumberjack's nature fixed for the test: a timid one runs from a Neck going for them, as it should, so checks
                // 1, 7 and 9 failed whenever the hut hired a timid settler (the night of 2026-10-26: Brynja, timid, ran from all
                // three). Hardworking changes only how fast they work. Given back at the end; check 3 makes them brave for itself.
                s_traitOf = lumber;
                s_traitWas = lumber.Record.Trait;
                lumber.Record.Trait = Traits.Name(Trait.Hardworking);

                // The colony's other settlers wait out of the fights until the guard's check: a Neck that turns on one walking
                // by sends the lumberjack running, as it should (the fix pass's run: one strolled past the spot). On the hall's
                // left, 40 m and more from the arena and the Troll: paused, they can't run, and 25 m ahead the Troll went for
                // them and killed all three, so check 3's lumberjack never ran and check 5 had no one to hire (2026-10-25).
                List<Citizen> others = Citizen.All.Where(c => c != null && c != lumber && c.ColonyId == colony).ToList();
                for (int i = 0; i < others.Count; i++)
                {
                    CitizenAI parked = others[i].GetComponent<CitizenAI>();
                    parked?.PlaceForTest(OnGround(hallAt - right * 30f + forward * (2f * i)));
                    parked?.PauseForTest(600f);
                }
                // Each fight on open ground 12 m to the player's right, clear of the hall and the hut: a Neck put "4 m ahead"
                // of where the lumberjack faced could stand in the hut's wall (the fix pass's run: no blow, no bite for 16 s).
                Vector3 arena = OnGround(origin + right * 12f);

                // 1. A Neck goes for the lumberjack: they fight it off (never run), it dies or makes off within 20 s, and after it
                //    they go back to work, not into hiding, though it bit them (its review: the fight's bites started a run).
                Vector3 at = ToArena(lumber, arena);
                Heal(lumber, 1f);
                Character neck = Spawn("Neck", OnGround(at + forward * 4f), made);
                bool neckCame = neck != null;   // once dead the game takes it away: it is == null then
                var seen = new Seen();
                int bites = 0;
                float nextBite = 0f;
                yield return Watch(lumber, new List<Character> { neck }, 20f, seen, () => neck == null || neck.IsDead(), t =>
                {
                    if (!seen.Fought || t < nextBite || bites >= 5 || neck == null || neck.IsDead()) return;
                    bites++;   // a bite a second while it lives: the last is always fresh when it dies
                    nextBite = t + 1f;
                    Hit(lumber.Body, neck, 1f);
                });
                bool neckDone = neck == null || neck.IsDead() || Utils.DistanceXZ(neck.transform.position, lumber.transform.position) > 12f;
                var after = new Seen();
                yield return Watch(lumber, new List<Character>(), 8f, after, null);
                Check($"A Neck going for a settler is fought off, not run from, and they don't hide after ({lumber.CitizenName}: fought {seen.Fought}, " +
                      $"ran {seen.Fled}, bitten {bites}, the Neck dead or off {neckDone}, ran after {after.Fled}, health {lumber.Body.GetHealthPercentage():0.00})",
                    neckCame && seen.Fought && !seen.Fled && neckDone && !after.Fled);
                yield return Calm(origin, lumber);

                // 7. Their blow on anything but what they fight does nothing: a Boar standing by, hit by them mid-fight with another
                //    Neck (its review: the swing's arc hurt what it reached).
                at = ToArena(lumber, arena);
                Heal(lumber, 1f);
                Character boar = Spawn("Boar", OnGround(at + right * 10f), made);
                Freeze(boar);
                float boarWas = boar != null ? boar.GetHealth() : 0f;
                neck = Spawn("Neck", OnGround(at + forward * 4f), made);
                seen = new Seen();
                bool boarHit = false;
                yield return Watch(lumber, new List<Character> { neck }, 20f, seen, () => neck == null || neck.IsDead(), t =>
                {
                    if (!seen.Fought || boarHit || boar == null) return;
                    boarHit = true;
                    Hit(boar, lumber.Body, 2f);
                });
                yield return new WaitForSeconds(1f);
                float boarNow = boar != null ? boar.GetHealth() : 0f;
                Check($"A blow of theirs on anything but what they fight does nothing (a Boar hit mid-fight {boarHit}: {boarWas:0.0} then {boarNow:0.0})",
                    boarHit && boar != null && boarNow >= boarWas - 0.01f);
                yield return Calm(origin, lumber);

                // 9. A Neck held out of reach, 3.5 m up (as on a rock): not fought (they'd stand under it), not run from (its review).
                at = ToArena(lumber, arena);
                Heal(lumber, 1f);
                Vector3 upAt = OnGround(at + forward * 4f) + Vector3.up * 3.5f;
                neck = Spawn("Neck", upAt, made);
                Freeze(neck, hold: true);
                seen = new Seen();
                yield return Watch(lumber, new List<Character> { neck }, 10f, seen, null, t =>
                {
                    if (neck != null) neck.transform.position = upAt;
                });
                Check($"A Neck out of reach is neither fought nor run from ({lumber.CitizenName}: fought {seen.Fought}, ran {seen.Fled})",
                    neck != null && !seen.Fought && !seen.Fled);
                yield return Calm(origin, lumber);

                // 2. At 40% health a Neck is run from.
                at = ToArena(lumber, arena);
                Heal(lumber, 0.4f);
                neck = Spawn("Neck", OnGround(at + forward * 4f), made);
                seen = new Seen();
                yield return Watch(lumber, new List<Character> { neck }, 10f, seen, () => seen.Fled);
                Check($"At 40% health a settler runs from a Neck ({lumber.CitizenName}: ran {seen.Fled}, fought {seen.Fought})", seen.Fled && !seen.Fought);
                yield return Calm(origin, lumber);

                // 3. A Troll sends them running, a brave settler too, in time (1.5.1: the release run's brave lumberjack waited
                //    till 6 m, 0.16's brave range, and the Troll's first blow, 278, killed them as they turned; brave was 9 m, and
                //    one who started at 7.2 m was still caught, 2026-10-25: from a strong monster the brave run at 12 m now).
                //    The Troll starts 14 m off and comes on (20 s: one run before the 1.5.0 fix it came no nearer in 10).
                //    The distance they started running at is told (settlers look for monsters every half second, a Troll
                //    gains about 3.5 m in that: the review of 1.5.1); what counts is that they run and live.
                at = ToArena(lumber, arena);
                Heal(lumber, 1f);
                lumber.Record.Trait = Traits.Name(Trait.Brave);   // this check only
                Character troll = Spawn("Troll", OnGround(at + forward * 14f), made);
                MonsterAI trollBrain = troll != null ? troll.GetComponent<MonsterAI>() : null;
                if (trollBrain != null) s_setAlerted?.Invoke(trollBrain, new object[] { true });
                bool trollCame = troll != null, broughtOn = false;
                float trollNearest = float.PositiveInfinity;
                string statusLast = "";
                seen = new Seen();
                yield return Watch(lumber, new List<Character> { troll }, 20f, seen, () => seen.Fled, t =>
                {
                    // What the Troll did, told with the result (the night of 2026-10-26 it failed with nothing in the log to say why).
                    if (troll == null || troll.IsDead()) return;
                    float now = Vector3.Distance(troll.transform.position, lumber.transform.position);
                    trollNearest = Mathf.Min(trollNearest, now);
                    statusLast = lumber.Status ?? "";
                    // The game's Troll doesn't always come on: the release run of 2026-10-26 had it hang back at 13.6 m for all
                    // 20 s while the lumberjack kept clear of a falling log, so the check judged nothing (the night before,
                    // the same "ran False at -1.0 m"). Still beyond 12 m after 10 s, it's put 11 m off, on the line it came
                    // from, and its own brain takes it from there: what's checked is that they run then, and live.
                    if (broughtOn || t < 10f || now <= 12f) return;
                    broughtOn = true;
                    Vector3 from = troll.transform.position - lumber.transform.position;
                    from.y = 0f;
                    Vector3 spot = OnGround(lumber.transform.position + (from.sqrMagnitude > 0.01f ? from.normalized : forward) * 11f);
                    troll.transform.position = spot;
                    Rigidbody trollBody = troll.GetComponent<Rigidbody>();
                    if (trollBody != null) trollBody.position = spot;   // the transform alone can be put back (the storehouse test)
                });
                yield return new WaitForSeconds(4f);   // still alive once away
                bool lived = lumber != null && lumber.Body != null && !lumber.Body.IsDead();
                lumber.Record.Trait = Traits.Name(Trait.Hardworking);
                Check($"A Troll sends a settler running, a brave one too, in time ({(lumber != null ? lumber.CitizenName : "gone")}: ran {seen.Fled} " +
                      $"at {seen.FledAt:0.0} m, fought {seen.Fought}, alive {lived}; the Troll came {trollCame}, nearest {trollNearest:0.0} m, brought on {broughtOn}, " +
                      $"their status '{statusLast}')", seen.Fled && !seen.Fought && lived);
                if (!lived)
                {
                    Check("Checks 4, 6, 5 and 8 not run: the lumberjack died in check 3", false);
                    yield break;
                }
                yield return Calm(origin, lumber);

                // 4. Two Necks at once on a settler at 56%: they fight, break off at half and run, and live (the plan's Review Focus).
                //    The pair's bites go on while they fight (3% every half second), so half comes for sure, whatever the swings do.
                at = ToArena(lumber, arena);
                Heal(lumber, 0.56f);
                var pair = new List<Character> { Spawn("Neck", OnGround(at + forward * 4f), made), Spawn("Neck", OnGround(at - forward * 4f), made) };
                seen = new Seen();
                float nextNip = 0f;
                yield return Watch(lumber, pair, 30f, seen, null, t =>
                {
                    if (!seen.Fought || seen.Fled || t < nextNip) return;
                    Character biter = pair.Where(n => n != null && !n.IsDead())
                        .OrderBy(n => Vector3.Distance(n.transform.position, lumber.transform.position)).FirstOrDefault();   // the nearer: a bite, not a stone
                    if (biter == null) return;
                    nextNip = t + 0.5f;
                    Hit(lumber.Body, biter, lumber.Body.GetMaxHealth() * 0.03f);
                });
                bool alive = lumber != null && lumber.Body != null && !lumber.Body.IsDead();
                Check($"Two Necks at once: they fight, break off at half health, run and live ({lumber?.CitizenName}: fought {seen.Fought}, ran {seen.Fled}, " +
                      $"fought below half {seen.FoughtBelowHalf}, alive {alive})", seen.Fought && seen.Fled && !seen.FoughtBelowHalf && alive);
                if (!alive) yield break;
                yield return Calm(origin, lumber);

                // 6. A settler hurt to half and left alone for 30 s heals.
                Heal(lumber, 0.5f);
                float hurtAt = lumber.Body.GetHealthPercentage();
                yield return new WaitForSeconds(30f);
                float healedTo = lumber.Body.GetHealthPercentage();
                Check($"A hurt settler heals once left alone ({lumber.CitizenName}: {hurtAt:0.00} then {healedTo:0.00} after 30 s)", healedTo > hurtAt + 0.01f);

                // 5. A guard drives off a Greyling about in the colony, beyond the tower's watch (25 m at level 1), before it bites.
                foreach (Citizen o in others.Where(x => x != null)) o.GetComponent<CitizenAI>()?.PauseForTest(0f);   // one to be hired
                ColonyBuilding tower = DevHelpers.Place(BuildingTypes.Watchtower.PrefabName, OnGround(hallAt - right * 18f), facing, player.GetPlayerID());
                if (tower != null) made.Add(tower.gameObject);
                yield return Finish(tower);
                tower?.GetComponent<Container>()?.GetInventory()?.AddItem(Prefab("Club"), 1);
                Citizen guard = null;
                WorkerModule towerWork = tower != null ? tower.GetComponent<WorkerModule>() : null;
                for (float t = 0f; towerWork != null && guard == null && t < 30f; t += 0.5f)
                {
                    yield return new WaitForSeconds(0.5f);
                    if (towerWork.FindWorker(out Citizen found, out _) == WorkerModule.WorkerState.Loaded) guard = found;
                }
                yield return new WaitForSeconds(5f);   // armed from the chest
                Vector3 greylingAt = OnGround(hallAt + right * 22f);
                Character greyling = Spawn("Greyling", greylingAt, made);
                MonsterAI greyBrain = greyling != null ? greyling.GetComponent<MonsterAI>() : null;
                if (greyBrain != null)
                {
                    // About and stirred up, after no one, standing still: only the new rule makes it the guard's (its review:
                    // it came on and bit, and the guard answered it by the old rules).
                    s_setAlerted?.Invoke(greyBrain, new object[] { true });
                    s_target(greyBrain) = null;
                    greyBrain.enabled = false;
                }
                bool goes = false;
                float fromTower = tower != null ? Utils.DistanceXZ(greylingAt, tower.transform.position) : 0f;
                for (float t = 0f; t < 40f && !goes; t += 0.25f)
                {
                    goes = guard != null && (guard.Status ?? "").StartsWith("$vc_status_driving_off");
                    yield return new WaitForSeconds(0.25f);
                }
                Check($"A guard drives off a Greyling about in the colony beyond the tower's watch ({guard?.CitizenName}, the Greyling {fromTower:0} m from " +
                      $"the tower: gone for {goes}; status '{(guard != null ? Localization.instance.Localize(guard.Status) : "")}')", guard != null && goes);
                yield return Calm(origin, lumber);

                // 8. The setting off: a Neck is run from, as before 1.5.0.
                if (fightBack == null) Check("The setting SettlersFightBack exists", false);
                else
                {
                    fightBack.Value = false;
                    at = ToArena(lumber, arena);
                    Heal(lumber, 1f);
                    neck = Spawn("Neck", OnGround(at + forward * 4f), made);
                    seen = new Seen();
                    yield return Watch(lumber, new List<Character> { neck }, 10f, seen, () => seen.Fled);
                    fightBack.Value = true;
                    Check($"With the setting off a settler runs from a Neck, as before ({lumber.CitizenName}: ran {seen.Fled}, fought {seen.Fought})", seen.Fled && !seen.Fought);
                }
            }
            finally
            {
                GiveTraitBack();
                DevHelpers.ClearMonsters(origin, 90f, animalsToo: true);
                foreach (Citizen c in Citizen.All.Where(c => c != null && !before.Contains(c.Id)).ToList()) Remove(c.gameObject);
                foreach (GameObject go in made.Where(g => g != null)) Remove(go);
                EnvMan.instance.m_debugTimeOfDay = false;
                ColonyConfig.NewcomersWalkIn.Value = walkInWas;
                if (fightBack != null) fightBack.Value = fightBackWas;
                Player me = Player.m_localPlayer;
                if (me != null && !me.InGodMode()) me.SetGodMode(true);
                Log.Event($"vc_weaktest: done — {s_pass} passed, {s_fail} failed");
                me?.Message(MessageHud.MessageType.Center, $"vc_weaktest: {s_pass} passed, {s_fail} failed");
            }
        }

        /// <summary>Every 0.25 s the creatures are kept after <paramref name="who"/>, and what they do is noted.</summary>
        private static IEnumerator Watch(Citizen who, List<Character> monsters, float seconds, Seen seen, System.Func<bool> done,
                                         System.Action<float> each = null)
        {
            for (float t = 0f; t < seconds && who != null; t += 0.25f)
            {
                foreach (Character m in monsters)
                {
                    if (m == null || m.IsDead()) continue;
                    MonsterAI brain = m.GetComponent<MonsterAI>();
                    if (brain != null) s_target(brain) = who.Body;
                }
                string status = who.Status ?? "";
                if (status.StartsWith("$vc_status_fighting_off"))
                {
                    seen.Fought = true;
                    // Well below half while still fighting (40%): a bite that lands just as they reach half can take them a little
                    // under it before they break off. Since 1.5.1 they break off the moment they're at half, not at their next look
                    // half a second on (StandUp.LookAgainNow): before, two Necks' bites took them under 40% in that half second
                    // (both night runs, 2026-10-25).
                    if (who.Body.GetHealthPercentage() < 0.40f) seen.FoughtBelowHalf = true;
                }
                if ((status == "$vc_status_fleeing" || status == "$vc_status_sheltering") && !seen.Fled)
                {
                    seen.Fled = true;
                    seen.FledAt = monsters.Where(m => m != null && !m.IsDead()).Select(m => Vector3.Distance(m.transform.position, who.transform.position))
                        .DefaultIfEmpty(-1f).Min();
                }
                each?.Invoke(t);
                if (done != null && done()) yield break;
                yield return new WaitForSeconds(0.25f);
            }
        }

        private static void GiveTraitBack()
        {
            if (s_traitOf != null && s_traitWas != null) s_traitOf.Record.Trait = s_traitWas;
            s_traitOf = null;
            s_traitWas = null;
        }

        /// <summary>The creatures gone and the settler calm again (a flee ends 15 s after the last threat).</summary>
        private static IEnumerator Calm(Vector3 origin, Citizen who)
        {
            DevHelpers.ClearMonsters(origin, 90f, animalsToo: true);
            yield return new WaitForSeconds(17f);
            if (who != null) Heal(who, 1f);
        }

        private static void Heal(Citizen who, float fraction)
        {
            if (who == null || who.Body == null) return;
            who.Body.SetHealth(who.Body.GetMaxHealth() * fraction);
        }

        /// <summary>Its brain off: it stands where it is, as it is (<paramref name="hold"/>: held in the air too).</summary>
        private static void Freeze(Character c, bool hold = false)
        {
            if (c == null) return;
            BaseAI brain = c.GetBaseAI();
            if (brain != null) brain.enabled = false;
            Rigidbody body = hold ? c.GetComponent<Rigidbody>() : null;
            if (body != null) body.isKinematic = true;
        }

        /// <summary>A blow from <paramref name="by"/>, as the game's own (its attacker known): bites and stray swings.</summary>
        private static void Hit(Character who, Character by, float damage)
        {
            if (who == null || by == null) return;
            var hit = new HitData();
            hit.m_damage.m_blunt = damage;
            hit.m_point = who.GetCenterPoint();
            hit.m_dir = (who.transform.position - by.transform.position).normalized;
            hit.m_attacker = by.GetZDOID();
            who.Damage(hit);
        }

        /// <summary>The settler put on the open ground where the fights are, a moment to land; the spot.</summary>
        private static Vector3 ToArena(Citizen who, Vector3 arena)
        {
            who.GetComponent<CitizenAI>()?.PlaceForTest(arena);
            return arena;
        }

        private static Vector3 OnGround(Vector3 at)
        {
            at.y = ZoneSystem.instance.GetGroundHeight(at);
            return at;
        }

        private static GameObject Prefab(string name) => ObjectDB.instance.GetItemPrefab(name);

        private static Character Spawn(string prefabName, Vector3 at, List<GameObject> made)
        {
            GameObject go = DevHelpers.Spawn(prefabName, at);
            if (go == null) return null;
            made.Add(go);
            return go.GetComponent<Character>();
        }

        private static IEnumerator Finish(ColonyBuilding building)
        {
            ConstructionModule site = building != null ? building.GetComponent<ConstructionModule>() : null;
            for (float t = 0f; site != null && site.Order == null && t < 15f; t += 0.5f) yield return new WaitForSeconds(0.5f);
            if (site != null && site.Order != null) site.FinishNow();
            for (float t = 0f; site != null && !site.IsBuilt && t < 40f; t += 0.5f) yield return new WaitForSeconds(0.5f);
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
            Log.Event($"vc_weaktest: {(ok ? "PASS" : "FAIL")} {what}");
        }
    }
}
