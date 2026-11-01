using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Jotunn.Entities;
using UnityEngine;
using VikingColonies.Config;
using VikingColonies.Construction;
using VikingColonies.Gui;

namespace VikingColonies.DevTools
{
    /// <summary>
    /// vc_hugin (test worlds; the player's question of 2026-10-27, "How many times does hugin show up in our mod and how
    /// often?"): Hugin's pace measured as a new player meets him. 'on' turns the raven hints and the mod's tips on, forgets
    /// what this character has heard, and talks to Hugin each time he lands with a line, a few seconds after, as a player
    /// who reads them would; 'off' puts the settings and what the character had heard back. The pace lines are
    /// HuginTips.LogPace's ('vc_hugin: queued|lands with|said ...').
    /// </summary>
    internal sealed class HuginCommand : ConsoleCommand
    {
        public override string Name => "vc_hugin";
        public override string Help => "VikingColonies (test worlds): 'vc_hugin on|off': the raven hints and Hugin's tips on, as for a new character, and " +
                                       "every line Hugin brings heard a few seconds after Hugin lands; 'off' puts the settings and what this character " +
                                       "had heard back; 'morning': the next morning, the clock running (not held as 'tod' holds it)";
        public override bool IsCheat => true;

        public override void Run(string[] args)
        {
            if (DevHelpers.RefuseOutsideTestWorld(Name)) return;
            string word = args.Length > 0 ? args[0] : "";
            BuildCommand.Print(word == "on" ? HuginReader.Start() : word == "off" ? HuginReader.Stop() : word == "morning" ? HuginReader.Morning()
                : "vc_hugin on|off|morning");
        }
    }

    internal static class HuginReader
    {
        /// <summary>A player sees him land, walks over and presses the key: a few seconds.</summary>
        private const float ReadAfterSeconds = 4f;
        /// <summary>He is talked to only from where the game lets a player stay near him (he leaves past 20 m).</summary>
        private const float NearMetres = 20f;

        private static readonly AccessTools.FieldRef<Player, HashSet<string>> s_heard = AccessTools.FieldRefAccess<Player, HashSet<string>>("m_shownTutorials");
        private static readonly AccessTools.FieldRef<Raven, Raven.RavenText> s_current = AccessTools.FieldRefAccess<Raven, Raven.RavenText>("m_currentText");
        private static readonly AccessTools.FieldRef<Raven, bool> s_hasTalked = AccessTools.FieldRefAccess<Raven, bool>("m_hasTalked");
        private static readonly System.Reflection.MethodInfo s_isSpawned = AccessTools.Method(typeof(Raven), "IsSpawned");
        private static readonly System.Reflection.MethodInfo s_isAway = AccessTools.Method(typeof(Raven), "IsAway");
        private static readonly System.Reflection.MethodInfo s_isFlying = AccessTools.Method(typeof(Raven), "IsFlying");
        private static readonly AccessTools.FieldRef<Raven, GameObject> s_ground = AccessTools.FieldRefAccess<Raven, GameObject>("m_groundObject");

        private static Coroutine s_running;
        private static bool s_hintsWas, s_tipsWas;
        private static List<string> s_heardWas;
        private static long s_playerId;
        private static int s_read;

        public static string Start()
        {
            Player player = Player.m_localPlayer;
            if (player == null) return "vc_hugin: no player";
            if (s_running != null) return "vc_hugin: already on";
            HashSet<string> heard = s_heard(player);
            s_hintsWas = Raven.m_tutorialsEnabled;
            s_tipsWas = ColonyConfig.HuginTips.Value;
            s_heardWas = heard.ToList();
            s_playerId = player.GetPlayerID();
            HuginTips.ForgetLandings();
            // As a new character: nothing heard, nothing waiting (the game's own lines too: a new player gets them).
            heard.Clear();
            HuginTips.Unqueue();
            Raven.m_tutorialsEnabled = true;
            ColonyConfig.HuginTips.Value = true;
            s_read = 0;
            s_running = Plugin.Instance.StartCoroutine(Loop());
            string said = $"vc_hugin: on (raven hints were {(s_hintsWas ? "on" : "off")}, tips {(s_tipsWas ? "on" : "off")}; {s_heardWas.Count} lines heard before, forgotten for now)";
            Log.Event(said);
            return said;
        }

        public static string Stop()
        {
            if (s_running == null) return "vc_hugin: not on";
            if (Plugin.Instance != null) Plugin.Instance.StopCoroutine(s_running);
            s_running = null;
            int waiting = HuginTips.Waiting();
            Player player = Player.m_localPlayer;
            // Only into the character it was taken from (review: a later 'off' could give one character another's list).
            if (player != null && player.GetPlayerID() == s_playerId)
            {
                HashSet<string> heard = s_heard(player);
                heard.Clear();
                foreach (string line in s_heardWas) heard.Add(line);
            }
            HuginTips.Unqueue();
            Raven.m_tutorialsEnabled = s_hintsWas;
            ColonyConfig.HuginTips.Value = s_tipsWas;
            string said = $"vc_hugin: off ({s_read} lines heard, {waiting} still waiting; the settings and what was heard put back)";
            Log.Event(said);
            return said;
        }

        /// <summary>
        /// Leaving the world or the game while on: what the character had heard goes back before the game saves it
        /// (review: a run that timed out saved the character with nothing heard).
        /// </summary>
        [HarmonyPatch]
        private static class PutBackOnLogoutHook
        {
            [HarmonyPatch(typeof(Game), nameof(Game.Logout)), HarmonyPrefix]
            private static void PutBack()
            {
                try { if (s_running != null) Stop(); }
                catch (System.Exception e) { Log.Warn($"vc_hugin: couldn't put things back: {e.Message}"); }   // never in the game's way
            }
        }

        [HarmonyPatch]
        private static class PutBackOnQuitHook
        {
            [HarmonyPatch(typeof(Game), "OnApplicationQuit"), HarmonyPrefix]
            private static void PutBack()
            {
                try { if (s_running != null) Stop(); }
                catch (System.Exception e) { Log.Warn($"vc_hugin: couldn't put things back: {e.Message}"); }   // never in the game's way
            }
        }

        /// <summary>The next morning with the clock running: 'tod' holds the time of day where it puts it (the first run's day stayed at 0.30).</summary>
        public static string Morning()
        {
            if (EnvMan.instance == null || ZNet.instance == null) return "vc_hugin: no world";
            // A quarter into the day's clock: the game shows it as about 0.32, past dawn (its 0.25), so the night's tip
            // can't come at once; only forward, as a night's sleep moves it.
            double length = EnvMan.instance.m_dayLengthSec, now = ZNet.instance.GetTimeSeconds();
            double morning = System.Math.Floor(now / length) * length + 0.25 * length;
            if (morning < now) morning += length;
            EnvMan.instance.m_debugTimeOfDay = false;
            ZNet.instance.SetNetTime(morning);
            return $"vc_hugin: morning of day {EnvMan.instance.GetDay(morning)}, the clock running";
        }

        private static Raven s_hugin;

        /// <summary>Hugin himself: the game's one Raven.m_instance is whichever raven woke last, Munin too (the probe run of 27 Oct).</summary>
        private static Raven Hugin()
        {
            if (s_hugin == null) s_hugin = Object.FindObjectsByType<Raven>(FindObjectsSortMode.None).FirstOrDefault(r => r != null && !r.m_isMunin);
            return s_hugin;
        }

        /// <summary>Where Hugin is while lines wait, logged when it changes (at most every 5 s): why he lands or doesn't.</summary>
        private static string State(Raven raven, Player player)
        {
            if (raven == null) return "not made yet";
            string where = (bool)s_isSpawned.Invoke(raven, null) ? "landed" : (bool)s_isAway.Invoke(raven, null) ? "away" : (bool)s_isFlying.Invoke(raven, null) ? "flying" : "between";
            Raven.RavenText text = s_current(raven);
            float metres = Vector3.Distance(player.transform.position, raven.transform.position);
            return $"{where}, with {(text != null ? text.m_key : "nothing")}{(s_hasTalked(raven) ? ", talked" : "")}, "
                   + $"{(metres > 1000f ? "far off" : $"{Mathf.Round(metres / 5f) * 5f:0} m from the player")}"
                   + $"{(LootSpawner.IsMonsterInRange(raven.transform.position, raven.m_enemyCheckDistance) ? ", a monster near" : "")}"
                   + $"{(s_ground(raven) == null ? ", no ground" : "")}{(RandEventSystem.InEvent() ? ", a raid on" : "")}"
                   + $"{(Raven.m_tutorialsEnabled ? "" : ", hints off")}";
        }

        private static IEnumerator Loop()
        {
            Raven.RavenText landedWith = null;
            float landedAt = 0f, toldAt = -99f;
            string told = "";
            while (true)
            {
                yield return new WaitForSeconds(0.5f);
                Player player = Player.m_localPlayer;
                Raven raven = Hugin();
                if (player != null && HuginTips.Waiting() > 0 && Time.time - toldAt >= 5f)
                {
                    string state = State(raven, player);
                    if (state != told)
                    {
                        Log.Event($"vc_hugin: Hugin is {state} ({HuginTips.Waiting()} waiting)");
                        told = state;
                        toldAt = Time.time;
                    }
                }
                if (player == null || raven == null || !(bool)s_isSpawned.Invoke(raven, null))
                {
                    landedWith = null;
                    continue;
                }
                Raven.RavenText text = s_current(raven);
                if (text == null || s_hasTalked(raven)) continue;
                if (text != landedWith)
                {
                    landedWith = text;
                    landedAt = Time.time;
                }
                if (Time.time - landedAt < ReadAfterSeconds || Vector3.Distance(player.transform.position, raven.transform.position) > NearMetres) continue;
                raven.Interact(player, false, false);
                s_read++;
                landedWith = null;
            }
        }
    }
}
