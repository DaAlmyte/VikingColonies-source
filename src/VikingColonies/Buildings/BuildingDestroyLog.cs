using HarmonyLib;

namespace VikingColonies.Buildings
{
    /// <summary>
    /// Logs how a colony building was destroyed, for bug reports: removed with the hammer (no hit), or broken by a hit
    /// (Structural = lost its support, Tree = hit by a falling tree or log, EnemyHit = a monster, ...).
    /// </summary>
    [HarmonyPatch(typeof(WearNTear), "Destroy")]
    internal static class BuildingDestroyLog
    {
        private static void Prefix(WearNTear __instance, HitData hitData)
        {
            ColonyBuilding building = __instance.GetComponent<ColonyBuilding>();
            if (building == null) return;
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
