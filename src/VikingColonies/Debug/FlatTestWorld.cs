using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using VikingColonies.Core;

namespace VikingColonies.DevTools
{
    /// <summary>vc_spawninfo: what a player waiting to appear waits for (the start's map marker, its area loaded, a logout
    /// point). Read only. 2026-10-18: a new flat test world never let its first player appear.</summary>
    internal sealed class SpawnInfoCommand : Jotunn.Entities.ConsoleCommand
    {
        public override string Name => "vc_spawninfo";
        public override string Help => "VikingColonies (test tools): 'vc_spawninfo' says what a player waiting to appear waits for";
        public override bool IsCheat => false;

        public override void Run(string[] args)
        {
            var lines = new List<string>();
            ZoneSystem zs = ZoneSystem.instance;
            string name = Game.instance != null ? Game.instance.m_StartLocation : "?";
            if (zs == null) lines.Add("no ZoneSystem");
            else
            {
                Traverse zone = Traverse.Create(zs);
                bool icon = zs.GetLocationIcon(name, out UnityEngine.Vector3 at);
                lines.Add($"start '{name}': marker {(icon ? at.ToString() : "none")}; locations generated {zone.Property("LocationsGenerated").GetValue<bool>()}; " +
                          $"instances {zone.Field("m_locationInstances").GetValue<System.Collections.IDictionary>()?.Count}; icons {zone.Field("m_locationIcons").GetValue<System.Collections.IDictionary>()?.Count}");
                foreach (object instance in zone.Field("m_locationInstances").GetValue<System.Collections.IDictionary>()?.Values ?? new object[0])
                {
                    Traverse i = Traverse.Create(instance);
                    var location = i.Field("m_location").GetValue<ZoneSystem.ZoneLocation>();
                    lines.Add($"  {location?.m_prefabName} at {i.Field("m_position").GetValue<UnityEngine.Vector3>()}, placed {i.Field("m_placed").GetValue<bool>()}, " +
                              $"icon always {location?.m_iconAlways}, icon when placed {location?.m_iconPlaced}");
                }
                if (icon && ZNetScene.instance != null)
                    lines.Add($"area ready at the marker: {ZNetScene.instance.IsAreaReady(at + UnityEngine.Vector3.up * 2f)}; zone loaded: {zs.IsZoneLoaded(at)}");
            }
            if (Game.instance != null)
            {
                Traverse game = Traverse.Create(Game.instance);
                PlayerProfile profile = game.Field("m_playerProfile").GetValue<PlayerProfile>();
                lines.Add($"request respawn {game.Field("m_requestRespawn").GetValue<bool>()}, waited {game.Field("m_respawnWait").GetValue<float>():0} s, " +
                          $"logout point {profile?.HaveLogoutPoint()}, custom spawn {profile?.HaveCustomSpawnPoint()}, player {(Player.m_localPlayer != null)}");
            }
            foreach (string line in lines) Construction.BuildCommand.Print("vc_spawninfo: " + line);
        }
    }


    /// <summary>
    /// The flat test world (2026-10-18, the player's idea): a world whose name starts with VCTestFlat (TestWorlds.IsFlatTestWorld; never a player's "Flattest") is made
    /// as one meadow at one height, with no trees, rocks, rivers or ruins, so the huts' building tests and pictures run on
    /// level ground with nothing in the way (in VCTest a 30 m flat spot was hard to find, and trees stood in the pictures).
    /// Every other world is made as the game makes it; the worker tests stay in VCTest, which has trees to fell.
    /// </summary>
    internal static class FlatTestWorld
    {
        /// <summary>The ground's height: 10 m above the sea (the game's water level is 30 m).</summary>
        internal const float Height = 40f;

        // Read from the game's terrain-building threads as well as the main one.
        private static volatile bool s_on;

        /// <summary>The game is making the flat test world's ground now.</summary>
        internal static bool On => s_on;

        [HarmonyPatch]
        private static class InitializeHook
        {
            // The main menu's background world and every world loaded pass through here: only a flat test world turns it on.
            [HarmonyPatch(typeof(WorldGenerator), "Initialize"), HarmonyPostfix]
            private static void AfterInitialize(World world)
            {
                bool flat = world != null && TestWorlds.IsFlatTestWorld(world.m_name);
                if (flat && !s_on)
                    Log.Event($"Flat test world: '{world.m_name}' is one meadow {Height:0} m high, with no trees, rocks, rivers or ruins");
                s_on = flat;
            }
        }

        [HarmonyPatch]
        private static class BiomeHook
        {
            private static IEnumerable<MethodBase> TargetMethods() =>
                AccessTools.GetDeclaredMethods(typeof(WorldGenerator))
                    .Where(m => m.Name == "GetBiome" && m.ReturnType == typeof(Heightmap.Biome)).Cast<MethodBase>();

            private static void Postfix(ref Heightmap.Biome __result)
            {
                if (s_on) __result = Heightmap.Biome.Meadows;
            }
        }

        [HarmonyPatch]
        private static class HeightHook
        {
            [HarmonyPatch(typeof(WorldGenerator), "GetBiomeHeight"), HarmonyPostfix]
            private static void Flat(ref float __result)
            {
                if (s_on) __result = Height;
            }
        }

        [HarmonyPatch]
        private static class VegetationHook
        {
            // Trees, bushes, rocks, stumps and pickables.
            [HarmonyPatch(typeof(ZoneSystem), "PlaceVegetation"), HarmonyPrefix]
            private static bool Skip() => !s_on;
        }

        [HarmonyPatch]
        private static class LocationsHook
        {
            // Ruins, camps, dungeon entrances and altars (the start temple's place still names where a new player appears).
            [HarmonyPatch(typeof(ZoneSystem), "PlaceLocations"), HarmonyPrefix]
            private static bool Skip() => !s_on;
        }

        [HarmonyPatch]
        private static class GenerateLocationsHook
        {
            // No locations are chosen but the start temple's place, in the middle (where a new player appears): every other
            // location looks for its own biome, which a world of one meadow hasn't got, and the game's search threw and
            // left the world loading for ever (2026-10-18). The game's list of locations is left as it is.
            [HarmonyPatch(typeof(ZoneSystem), "GenerateLocationsIfNeeded"), HarmonyPrefix]
            private static bool OnlyTheStart(ZoneSystem __instance)
            {
                if (!s_on) return true;
                Traverse zone = Traverse.Create(__instance);
                if (zone.Property("LocationsGenerated").GetValue<bool>()) return false;
                string name = Game.instance != null ? Game.instance.m_StartLocation : "StartTemple";
                var start = zone.Method("GetLocation", new[] { typeof(string) }).GetValue<ZoneSystem.ZoneLocation>(name);
                if (start != null)
                    zone.Method("RegisterLocation", new[] { typeof(ZoneSystem.ZoneLocation), typeof(UnityEngine.Vector3), typeof(bool) })
                        .GetValue(start, new UnityEngine.Vector3(0f, Height, 0f), true);
                zone.Field("m_generateLocationsProgress").SetValue(1f);
                zone.Property("LocationsGenerated").SetValue(true);   // its setter tells the world's loading it's done
                Log.Event($"Flat test world: no locations but the start ({(start != null ? name : "none found")}) at the middle");
                return false;
            }
        }
    }
}
