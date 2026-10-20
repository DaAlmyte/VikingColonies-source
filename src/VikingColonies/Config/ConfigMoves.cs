using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;

namespace VikingColonies.Config
{
    /// <summary>
    /// A setting that moved to another section keeps the player's value (0.22: Lumberjack.LearningRate became
    /// Colony.LearningRate). BepInEx keeps a line it no longer knows as an orphan; its value goes to the new setting
    /// when that one still has its default, and the old line is dropped from the file.
    /// </summary>
    internal static class ConfigMoves
    {
        public static void Move(ConfigFile config, string oldSection, string key, ConfigEntryBase entry)
        {
            if (config == null || entry == null) return;
            var orphans = AccessTools.Property(typeof(ConfigFile), "OrphanedEntries")?.GetValue(config, null) as Dictionary<ConfigDefinition, string>;
            var old = new ConfigDefinition(oldSection, key);
            if (orphans == null)
            {
                Log.Warn($"Settings: BepInEx's old settings can't be read here; {oldSection}.{key} isn't moved to {entry.Definition.Section}.{key} (set it there by hand)");
                return;
            }
            if (!orphans.TryGetValue(old, out string value)) return;
            orphans.Remove(old);
            bool yours = !Equals(entry.BoxedValue, entry.DefaultValue);
            if (!yours) entry.SetSerializedValue(value);
            config.Save();
            Log.Info(yours ? $"Settings: dropped the old {oldSection}.{key} ({value}); kept yours: {entry.Definition.Section}.{key} = {entry.GetSerializedValue()}"
                           : $"Settings: moved {oldSection}.{key} ({value}) to {entry.Definition.Section}.{key}");
        }
    }
}
