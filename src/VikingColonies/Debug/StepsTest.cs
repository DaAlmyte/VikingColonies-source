using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Jotunn.Entities;
using UnityEngine;
using VikingColonies.Buildings;
using VikingColonies.Config;
using VikingColonies.Construction;
using VikingColonies.Core;
using VikingColonies.Gui;

namespace VikingColonies.DevTools
{
    /// <summary>vc_stepstest (test worlds): Hugin's first steps (1.5.1), checked in the game in about a minute.</summary>
    internal sealed class StepsTestCommand : ConsoleCommand
    {
        public override string Name => "vc_stepstest";
        public override string Help => "VikingColonies (test worlds): a Chieftain's Camp here; Hugin's first steps: the next step's line queued once " +
                                       "as it opens, the Overview's next step, nothing with the tutorials off. Removes what it made.";
        public override bool IsCheat => true;

        public override void Run(string[] args)
        {
            Player p = Player.m_localPlayer;
            if (p == null || ZNet.instance == null || !TestWorlds.IsTestWorld(ZNet.instance.GetWorldName()))
            {
                BuildCommand.Print("vc_stepstest: test worlds only");
                return;
            }
            Plugin.Instance.StartCoroutine(StepsTest.Run(p));
        }
    }

    /// <summary>
    /// Written against the game and the mod's older parts only (the tips' queue and the window's text), so it builds on the
    /// code before the steps too: its checks fail there first. Hugin's check runs on the tips' own clock (a few seconds).
    /// </summary>
    internal static class StepsTest
    {
        private static int s_pass, s_fail;
        private static readonly string[] s_lines = { "vc_step_builder_hut_line", "vc_step_worker_line", "vc_step_longhouse_line",
            "vc_step_food_line", "vc_step_watchtower_line", "vc_step_camp_level_2_line", "vc_step_done_line" };

        public static IEnumerator Run(Player player)
        {
            s_pass = s_fail = 0;
            Log.Event("vc_stepstest: starting");
            if (!player.InGodMode()) player.SetGodMode(true);   // never off: the test character has 1 health
            bool tutorialsWas = Raven.m_tutorialsEnabled, tipsWas = ColonyConfig.HuginTips.Value;
            Vector3 origin = player.transform.position, forward = player.transform.forward;
            forward.y = 0f;
            forward.Normalize();
            var made = new List<GameObject>();
            try
            {
                Raven.m_tutorialsEnabled = true;
                ColonyConfig.HuginTips.Value = true;
                foreach (string line in s_lines) SelfTest.ForgetTipForTest(player, line);
                SelfTest.ForgetTipForTest(player, HuginTips.Colony);
                HuginTips.Unqueue();
                DevHelpers.ClearMonsters(origin, 60f, animalsToo: true);
                ColonyBuilding hall = DevHelpers.Place(BuildingTypes.TownHall.PrefabName, origin + forward * 8f, Quaternion.LookRotation(forward), player.GetPlayerID());
                if (hall != null) made.Add(hall.gameObject);
                TownHallModule townHall = hall != null ? hall.GetComponent<TownHallModule>() : null;
                for (float t = 0f; townHall != null && !townHall.Founded && t < 20f; t += 0.5f) yield return new WaitForSeconds(0.5f);
                if (townHall == null || !townHall.Founded)
                {
                    Check("A Chieftain's Camp is founded", false);
                    yield break;
                }

                // 1. A fresh colony: Hugin tells of the Builder's Hut once, in the colony tip (0.11), and step 2's line doesn't
                // say it again on top (review of 1.5.1). Three of the tips' clock ticks after the tip.
                yield return WaitQueued(HuginTips.Colony, 15f);
                yield return new WaitForSeconds(6f);
                bool colonyTip = HuginTips.Queued(HuginTips.Colony), hutLine = HuginTips.Queued("vc_step_builder_hut_line");
                Check($"A fresh colony: Hugin's colony tip says to build a Builder's Hut, and the step's line doesn't say it again (tip: {colonyTip}, step line: {hutLine})",
                    colonyTip && !hutLine);

                // 2. A Builder's Hut finished at once: step 3's line, and step 2's isn't queued again.
                HuginTips.Unqueue();
                ColonyBuilding hut = DevHelpers.Place(BuildingTypes.BuilderHut.PrefabName, origin - forward * 10f, Quaternion.LookRotation(forward), player.GetPlayerID());
                if (hut != null) made.Add(hut.gameObject);
                yield return Finish(hut);
                yield return WaitQueued("vc_step_worker_line", 15f);
                bool worker = HuginTips.Queued("vc_step_worker_line"), again = HuginTips.Queued("vc_step_builder_hut_line");
                Check($"With a Builder's Hut up, Hugin says to give a settler a job, and not the hut again (job: {worker}, hut again: {again})",
                    worker && !again);

                // 3. The Overview shows the next step: step 3, though the builder has the Builder's Hut (the builder's job doesn't
                // tick it, review of 1.5.1), and the Builder's Hut done.
                Windows.TownHall.Open(hall);
                string overview = DevHelpers.Plain(Windows.TownHall.TextForTest());
                Windows.TownHall.Close();
                string next = Localization.instance.Localize("$vc_step_worker"), heading = Localization.instance.Localize("$vc_overview_steps");
                string hutStep = Localization.instance.Localize("$vc_step_builder_hut");
                Check($"The camp's Overview shows Hugin's next step ({overview})",
                    overview.Contains(heading) && overview.Contains(next) && overview.Contains(hutStep));

                // 4. With the game's tutorials off, nothing is queued.
                HuginTips.Unqueue();
                foreach (string line in s_lines) SelfTest.ForgetTipForTest(player, line);
                Raven.m_tutorialsEnabled = false;
                yield return new WaitForSeconds(8f);
                bool any = s_lines.Any(HuginTips.Queued);
                Check($"With the tutorials off Hugin says nothing of the steps (queued: {any})", !any);
            }
            finally
            {
                Raven.m_tutorialsEnabled = tutorialsWas;
                ColonyConfig.HuginTips.Value = tipsWas;
                HuginTips.Unqueue();
                foreach (string line in s_lines) SelfTest.ForgetTipForTest(player, line);
                SelfTest.ForgetTipForTest(player, HuginTips.Colony);
                DevHelpers.ClearMonsters(origin, 60f, animalsToo: true);
                foreach (GameObject go in made.Where(g => g != null)) Remove(go);
                Log.Event($"vc_stepstest: done — {s_pass} passed, {s_fail} failed");
                Player.m_localPlayer?.Message(MessageHud.MessageType.Center, $"vc_stepstest: {s_pass} passed, {s_fail} failed");
            }
        }

        private static IEnumerator WaitQueued(string line, float seconds)
        {
            for (float t = 0f; t < seconds && !HuginTips.Queued(line); t += 0.5f) yield return new WaitForSeconds(0.5f);
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
            Log.Event($"vc_stepstest: {(ok ? "PASS" : "FAIL")} {what}");
        }
    }
}
