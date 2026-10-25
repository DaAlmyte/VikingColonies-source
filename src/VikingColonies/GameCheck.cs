using System;
using System.Collections.Generic;
using HarmonyLib;

namespace VikingColonies
{
    /// <summary>
    /// At start-up: the game's private parts the mod reads, and what stops working without each. A Valheim update can
    /// rename one; this says so in the log at once instead of a feature quietly switching off (from the 2026-10 review).
    /// </summary>
    internal static class GameCheck
    {
        private struct Part
        {
            public Type Type;
            /// <summary>For a part looked up by type name: that name (Type is null when the game has no such type).</summary>
            public string TypeName;
            public string Name;
            public bool IsMethod;
            public string Feature;
            /// <summary>For a field the mod reads with a set type: that type (a renamed type breaks it too); null for any.</summary>
            public Type FieldType;
        }

        private static readonly Part[] s_parts =
        {
            Field(typeof(BaseAI), "m_path", "settlers looking ahead for doors and getting unstuck"),
            Field(typeof(BaseAI), "m_lastFindPathTime", "settlers finding a new way at once"),
            Field(typeof(Character), "m_maxAirAltitude", "no fall damage when a settler is moved out or recalled"),
            Field(typeof(Character), "m_lastHit", "the cause of a settler's death"),
            Field(typeof(Humanoid), "m_rightItem", "what a settler holds when they fight off a weak creature"),
            Field(typeof(PrivateArea), "m_allAreas", "doors in warded areas and colony permissions"),
            Method(typeof(PrivateArea), "IsEnabled", "doors in warded areas"),
            Method(typeof(PrivateArea), "IsInside", "doors in warded areas and colony permissions"),
            Method(typeof(PrivateArea), "IsPermitted", "doors in warded areas and colony permissions"),
            Field(typeof(PrivateArea), "m_piece", "doors in warded areas and colony permissions"),
            Field(typeof(Inventory), "m_height", "hut chests growing with their level"),
            Method(typeof(Container), "Load", "settlers' deliveries in multiplayer"),
            Field(typeof(Chat), "m_hideTimer", "settler deaths shown in chat"),
            Field(typeof(InventoryGui), "m_currentContainer", "settler cairns"),
            Field(typeof(Minimap), "m_pins", "map pins for settler cairns"),
            Field(typeof(Player), "m_placementGhost", "the placement outline and colony rings"),
            Field(typeof(Player), "m_placementStatus", "the placement colour of huts"),
            Method(typeof(Player), "SetPlacementGhostValid", "the placement colour of huts"),
            Field(typeof(Player), "m_buildPieces", "the placement outline"),
            Field(typeof(WearNTear), "s_rayMask", "the builder's support check"),
            Field(typeof(WearNTear), "s_terrainLayer", "the builder's support check"),
            Field(typeof(WearNTear), "m_support", "the builder's support check"),
            Method(typeof(WearNTear), "GetMaterialProperties", "the builder's support check"),
            Field(typeof(Pickable), "m_nview", "the forager"),
            Field(typeof(Game), "m_timeScale", "vc_speed (a test tool)"),
            Field(typeof(Character), "m_animator", "settlers sitting, lying and gesturing"),
            Field(typeof(Character), "m_zanim", "settlers sitting, lying and gesturing"),
            Method(typeof(Humanoid), "ShowHandItems", "settlers taking their tool out again after sitting"),
            Field(typeof(Character), "m_blocking", "guards holding their shield up"),
            Field(typeof(Humanoid), "m_attackDrawTime", "guards drawing their bow"),
            Typed(typeof(ZNetScene), "m_instances", typeof(Dictionary<ZDO, ZNetView>), "fast world scans (the slow way is used instead: settlers may make the game stutter)"),
            Field(typeof(Pathfinding), "m_tiles", "vc_perf's walking map (a test tool)"),
            Named("UnifiedPopup", "IsVisible", true, "the test runner naming a popup (a test tool)"),
            Named("UnifiedPopup", "instance", false, "the test runner naming a popup (a test tool)"),
            Named("UnifiedPopup", "headerText", false, "the test runner naming a popup (a test tool)"),
            Named("UnifiedPopup", "bodyText", false, "the test runner naming a popup (a test tool)"),
        };

        /// <summary>The parts not found at start-up, as "Type.Name (what stops working)"; empty when all are there.</summary>
        public static readonly List<string> Missing = new List<string>();

        /// <summary>The parts of the start-up that failed (a hook whose game method is gone), from <see cref="Core.StartupSteps"/> (0.25).</summary>
        public static readonly List<string> StartupFailures = new List<string>();

        public static int Count => s_parts.Length;

        public static void Run()
        {
            Missing.Clear();
            foreach (Part part in s_parts)
            {
                bool found;
                try
                {
                    found = part.Type != null && (part.IsMethod ? AccessTools.Method(part.Type, part.Name) != null
                        : AccessTools.Field(part.Type, part.Name) is System.Reflection.FieldInfo field && (part.FieldType == null || field.FieldType == part.FieldType));
                }
                catch (Exception)
                {
                    found = false;
                }
                if (found) continue;
                string line = $"{(part.Type != null ? part.Type.Name : part.TypeName)}.{part.Name} ({part.Feature})";
                Missing.Add(line);
                Log.Warn($"Game check: {line} is missing — a Valheim update? That part of the mod won't work until it is updated.");
            }
            foreach (string failure in StartupFailures)
                Log.Error($"Game check: a part of the start-up failed: {failure} — a Valheim update? That part of the mod won't work until it is updated; the rest runs.");
            if (Missing.Count == 0) Log.Event($"Game check: all {s_parts.Length} game parts the mod reads were found" +
                                              (StartupFailures.Count == 0 ? ", and every part of the start-up ran" : $"; {StartupFailures.Count} parts of the start-up failed"));
            else Log.Warn($"Game check: {Missing.Count} of {s_parts.Length} game parts missing; see the lines above");
        }

        private static Part Field(Type type, string name, string feature) => new Part { Type = type, Name = name, IsMethod = false, Feature = feature };
        private static Part Method(Type type, string name, string feature) => new Part { Type = type, Name = name, IsMethod = true, Feature = feature };
        /// <summary>A part of a game type looked up by its name (a test tool's: a renamed type only switches the tool off).</summary>
        private static Part Named(string typeName, string name, bool isMethod, string feature) =>
            new Part { Type = AccessTools.TypeByName(typeName), TypeName = typeName, Name = name, IsMethod = isMethod, Feature = feature };
        private static Part Typed(Type type, string name, Type fieldType, string feature) =>
            new Part { Type = type, Name = name, IsMethod = false, Feature = feature, FieldType = fieldType };
    }
}
