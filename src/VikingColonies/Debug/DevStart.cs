using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using VikingColonies.Config;
using VikingColonies.Core;

namespace VikingColonies.DevTools
{
    /// <summary>
    /// Faster test starts (Developer settings, off by default): skips the intro film, and goes straight from the main
    /// menu into a test world with a chosen character — once per launch, and only into a world whose name says "test".
    /// </summary>
    internal static class DevStart
    {
        private static bool s_autoStarted;

        private static bool AutoStartWanted =>
            ColonyConfig.AutoStartWorld.Value.Trim().Length > 0 && ColonyConfig.AutoStartCharacter.Value.Trim().Length > 0;

        [HarmonyPatch]
        private static class SkipIntroHook
        {
            [HarmonyPatch(typeof(FejdStartup), "TryPlayIntroCinematic"), HarmonyPrefix]
            private static bool SkipIntro(ref IEnumerator __result)
            {
                if (!ColonyConfig.SkipIntro.Value && !AutoStartWanted) return true;
                __result = Nothing();
                return false;
            }
        }

        private static IEnumerator Nothing()
        {
            yield break;
        }

        [HarmonyPatch]
        private static class SpawnedHook
        {
            // The scenario runner waits for the player to be in the world. The game writes "Spawned after" only when a
            // player comes back to its logout point; a first visit to a world (the new flat test world, 2026-10-18) wrote
            // nothing, and the runner waited for ever.
            [HarmonyPatch(typeof(Player), "OnSpawned"), HarmonyPostfix]
            private static void AfterSpawned(Player __instance)
            {
                if (__instance == Player.m_localPlayer)
                    Log.Event($"The player is in the world ({(ZNet.instance != null ? ZNet.instance.GetWorldName() : "?")})");
            }
        }

        [HarmonyPatch]
        private static class AfterMenuStartHook
        {
            [HarmonyPatch(typeof(FejdStartup), "Start"), HarmonyPostfix]
            private static void AfterMenuStart(FejdStartup __instance)
            {
                if (s_autoStarted || !AutoStartWanted) return;
                s_autoStarted = true;   // logging out leads back here: start only once
                __instance.StartCoroutine(AutoStart(__instance));
            }
        }

        /// <summary>Does what the clicks would: Start Game, the character, the world, Start.</summary>
        private static IEnumerator AutoStart(FejdStartup menu)
        {
            string world = ColonyConfig.AutoStartWorld.Value.Trim(), character = ColonyConfig.AutoStartCharacter.Value.Trim();
            if (!TestWorlds.IsTestWorld(world))
            {
                Log.Warn($"Auto-start: '{world}' isn't a test world (its name must contain 'test'); not started");
                yield break;
            }
            yield return new WaitForSecondsRealtime(1.5f);
            Traverse menuT = Traverse.Create(menu);
            menuT.Method("OnStartGame").GetValue();
            yield return new WaitForSecondsRealtime(0.5f);
            List<PlayerProfile> profiles = menuT.Field("m_profiles").GetValue<List<PlayerProfile>>();
            PlayerProfile profile = profiles?.Find(p => string.Equals(p.GetName(), character, System.StringComparison.OrdinalIgnoreCase)
                                                    || string.Equals(p.GetFilename(), character, System.StringComparison.OrdinalIgnoreCase));
            if (profile == null)
            {
                Log.Warn($"Auto-start: no character called '{character}'; pick one yourself");
                yield break;
            }
            menuT.Method("SetSelectedProfile", new[] { typeof(string) }).GetValue(profile.GetFilename());
            menuT.Method("OnCharacterStart").GetValue();
            yield return new WaitForSecondsRealtime(0.5f);
            menuT.Method("UpdateWorldList", new[] { typeof(bool) }).GetValue(true);
            List<World> worlds = menuT.Field("m_worlds").GetValue<List<World>>();
            int index = worlds != null ? worlds.FindIndex(w => w.m_name == world) : -1;
            if (index < 0)
            {
                // A test world that doesn't exist yet is made (a fresh save for a test of its own, e.g. other mods alongside),
                // on the land of the first test world there is: the scenarios know where its flat meadows are (a random
                // seed's were all hills; the test next to other mods, 2026-10-15).
                // VCTest's own land first: a full run's fresh world (VCTestRun-…) must have the meadows its scenarios know.
                World landOf = worlds?.FirstOrDefault(w => w != null && w.m_name == "VCTest")
                               ?? worlds?.FirstOrDefault(w => w != null && TestWorlds.IsTestWorld(w.m_name));
                World made = World.GetCreateWorld(world, FileHelpers.FileSource.Local);
                if (made != null && landOf != null)
                {
                    made.m_seedName = landOf.m_seedName;
                    made.m_seed = landOf.m_seed;
                }
                made?.SaveWorldFWLData(System.DateTime.Now);
                Log.Event($"Auto-start: made the test world '{world}' (seed {made?.m_seedName}{(landOf != null ? ", the land of " + landOf.m_name : "")})");
                menuT.Method("UpdateWorldList", new[] { typeof(bool) }).GetValue(true);
                worlds = menuT.Field("m_worlds").GetValue<List<World>>();
                index = worlds != null ? worlds.FindIndex(w => w.m_name == world) : -1;
            }
            if (index < 0)
            {
                Log.Warn($"Auto-start: no world called '{world}'; pick one yourself");
                yield break;
            }
            menuT.Method("SetSelectedWorld", new[] { typeof(int), typeof(bool) }).GetValue(index, false);
            Log.Event($"Auto-start: {profile.GetName()} into {world}");
            menuT.Method("OnWorldStart").GetValue();
        }
    }
}
