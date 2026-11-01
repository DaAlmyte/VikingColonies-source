using HarmonyLib;
using VikingColonies.Construction;

namespace VikingColonies.Buildings
{
    /// <summary>
    /// Logs how a colony building was destroyed, for bug reports: removed with the hammer (no hit), or broken by a hit
    /// (Structural = lost its support, Tree = hit by a falling tree or log, EnemyHit = a monster, ...). A piece the builder
    /// put up for a hut is logged when it breaks (the camp's bonfire gone with no word, 27 Oct); the builder's own take-downs
    /// say so themselves.
    /// </summary>
    [HarmonyPatch(typeof(WearNTear), "Destroy")]
    internal static class BuildingDestroyLog
    {
        private static void Prefix(WearNTear __instance, HitData hitData)
        {
            ColonyBuilding building = __instance.GetComponent<ColonyBuilding>();
            if (building == null)
            {
                string site = hitData != null ? PiecePlacer.SiteOf(__instance.gameObject) : "";
                if (site.Length > 0)
                    Log.Info($"{Utils.GetPrefabName(__instance.gameObject)} at {__instance.transform.position} of the site {site} broke ({hitData.m_hitType}{By(hitData)})");
                return;
            }
            string how = hitData == null ? "removed with the hammer" : $"broke ({hitData.m_hitType}{By(hitData)})";
            Log.Event($"{building.TypeId} {building.Id} destroyed: {how}");
        }

        private static string By(HitData hit)
        {
            Character attacker = hit.GetAttacker();
            return attacker != null ? $" by {attacker.GetHoverName()}" : "";
        }
    }
}
