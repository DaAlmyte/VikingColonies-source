using System.Linq;
using HarmonyLib;
using UnityEngine;
using VikingColonies.Buildings;
using VikingColonies.Colony;
using VikingColonies.Config;
using VikingColonies.Core;

namespace VikingColonies.Gui
{
    /// <summary>
    /// Rules the game never showed, said where they happen (1.4.2, the idea list's "make the hidden rules visible"): a fire
    /// the colony keeps burning says so in its hover, with what it takes from the colony's chests.
    /// </summary>
    internal static class HiddenRules
    {
        /// <summary>"Settlers keep it burning with Wood from the colony's chests (the last 10 stay)", or "" for another fire.</summary>
        internal static string FireLine(Fireplace fire)
        {
            if (fire == null || !ColonyConfig.TendFires.Value || !ColonyConfig.FreeTime.Value || fire.m_infiniteFuel || fire.m_fuelItem == null) return "";
            string colony = ColonyAreas.ColonyAt(fire.transform.position);
            if (string.IsNullOrEmpty(colony) || !ColonyPlaces.Fires(colony).Any(f => f.Fire == fire)) return "";
            // As the settlers choose (FreeTimeRoutine.FireToTend): within reach of a hut someone works at, or the Town Hall.
            Vector3 at = fire.transform.position;
            float fromHome = ColonyBuilding.All
                .Where(b => b != null && b.IsValid && b.ColonyId == colony && (b.IsTownHall || !string.IsNullOrEmpty(b.Type?.JobId)))
                .Select(b => Utils.DistanceXZ(b.transform.position, at)).DefaultIfEmpty(float.MaxValue).Min();
            if (!FreeTimeRules.Tends(true, true, false, fromHome)) return "";
            string fuel = Localization.instance.Localize(fire.m_fuelItem.m_itemData.m_shared.m_name);
            return string.Format(Localization.instance.Localize("$vc_fire_tended"), fuel, FireFuel.Keep);
        }

        [HarmonyPatch]
        private static class FireHoverHook
        {
            [HarmonyPatch(typeof(Fireplace), nameof(Fireplace.GetHoverText)), HarmonyPostfix]
            private static void AddLine(Fireplace __instance, ref string __result)
            {
                string line = FireLine(__instance);
                if (line.Length > 0) __result = (__result ?? "") + "\n<size=80%>" + line + "</size>";
            }
        }
    }
}
