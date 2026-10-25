using System.Collections.Generic;
using Jotunn.Configs;
using Jotunn.Managers;
using UnityEngine;
using VikingColonies.Buildings;
using VikingColonies.Config;
using VikingColonies.Core;

namespace VikingColonies.Construction
{
    /// <summary>
    /// Creative mode (1.5): while a colony building is being placed, the level it goes up at once (0: the builder builds it),
    /// changed with two keys, and the level a building placed by this computer was given.
    /// </summary>
    internal static class CreativePlacing
    {
        private static readonly Dictionary<string, int> s_levels = new Dictionary<string, int>();
        private static ButtonConfig s_up;
        private static ButtonConfig s_down;
        private static int s_keysFrame = -1;
        private static BuildingType s_type;
        private static int s_max;
        private static Pending s_pending;

        private sealed class Pending
        {
            public string TypeId;
            public Vector3 At;
            public int Level;
            public float Time;
        }

        public static void RegisterKeys()
        {
            s_up = new ButtonConfig { Name = "vc_creative_level_up", Config = ColonyConfig.CreativeLevelUpKey, HintToken = "$vc_key_creative_up" };
            s_down = new ButtonConfig { Name = "vc_creative_level_down", Config = ColonyConfig.CreativeLevelDownKey, HintToken = "$vc_key_creative_down" };
            InputManager.Instance.AddButton(Plugin.PluginGuid, s_up);
            InputManager.Instance.AddButton(Plugin.PluginGuid, s_down);
        }

        /// <summary>The chosen level for the building type being placed (0 when none, or not creative).</summary>
        public static int Level => s_type != null && Colony.Creative.LocalIsCreative() && s_levels.TryGetValue(s_type.Id, out int level)
            ? Mathf.Min(level, s_max) : 0;

        /// <summary>Each frame of placing (null when not placing a colony building): the keys, and the limit at the ghost.</summary>
        public static void Update(BuildingType placing, Vector3 at)
        {
            s_type = placing;
            if (placing == null || !Colony.Creative.LocalIsCreative()) return;
            ColonyBuilding colony = SiteScanner.ColonyFor(at);
            int hutCap = colony != null ? HallRules.PerksOf(colony.ColonyId).HutCap : 1;
            s_max = CreativeRules.MaxPlacedLevel(placing.Id == BuildingTypes.TownHallId, hutCap);
            if (Time.frameCount == s_keysFrame || s_up == null) return;   // the game may update the ghost twice in a frame
            s_keysFrame = Time.frameCount;
            int step = ZInput.GetButtonDown(s_up.Name) ? 1 : ZInput.GetButtonDown(s_down.Name) ? -1 : 0;
            if (step == 0) return;
            int current = s_levels.TryGetValue(placing.Id, out int was) ? was : 0;
            s_levels[placing.Id] = CreativeRules.StepPlacedLevel(current, step, s_max);
        }

        /// <summary>The placing line's creative part ("" when not creative or not placing a colony building).</summary>
        public static string Line()
        {
            if (s_type == null || !Colony.Creative.LocalIsCreative()) return "";
            string up = KeyName(ColonyConfig.CreativeLevelUpKey.Value), down = KeyName(ColonyConfig.CreativeLevelDownKey.Value);
            int level = Level;
            return level > 0
                ? string.Format(Localization.instance.Localize("$vc_creative_place"), level, up, down)
                : string.Format(Localization.instance.Localize("$vc_creative_place_builder"), level, up, down);
        }

        /// <summary>Placing now (the game's TryPlacePiece): the chosen level is kept for the building that appears there.</summary>
        public static void NotePlacing(BuildingType type, Vector3 at)
        {
            int level = type != null && type == s_type ? Level : 0;
            s_pending = level > 0 ? new Pending { TypeId = type.Id, At = at, Level = level, Time = UnityEngine.Time.time } : null;
        }

        /// <summary>The level chosen for a building this computer just placed (then forgotten); 0 for any other.</summary>
        public static int TakeFor(ColonyBuilding placed)
        {
            Pending pending = s_pending;
            if (pending == null || placed == null || placed.TypeId != pending.TypeId || UnityEngine.Time.time - pending.Time > 10f
                || Utils.DistanceXZ(placed.transform.position, pending.At) > 1.5f) return 0;   // set on the ground: its height may differ
            s_pending = null;
            return pending.Level;
        }

        /// <summary>vc_creativetest: as if a player had picked <paramref name="level"/> while placing a <paramref name="type"/> there.</summary>
        internal static void NoteForTest(BuildingType type, int level, Vector3 at)
        {
            s_type = type;
            s_max = CreativeRules.MaxPlacedLevel(type.Id == BuildingTypes.TownHallId, HutLevels.MaxLevel);
            s_levels[type.Id] = level;
            NotePlacing(type, at);
            s_type = null;
        }

        private static string KeyName(KeyCode key)
        {
            switch (key)
            {
                case KeyCode.Equals: return "=";
                case KeyCode.Minus: return "-";
                case KeyCode.KeypadPlus: return "Num +";
                case KeyCode.KeypadMinus: return "Num -";
                default: return key.ToString();
            }
        }
    }
}
