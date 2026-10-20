using HarmonyLib;
using VikingColonies.Config;
using VikingColonies.Core;

namespace VikingColonies.Citizens
{
    /// <summary>
    /// A falling tree or rolling log hitting a settler, on the computer running him: always logged, and ignored while
    /// SettlersSafeFromTrees is on. Monsters, falls, fire and the rest still hurt settlers; players are untouched.
    /// </summary>
    [HarmonyPatch]
    internal static class TreeHitPatches
    {
        [HarmonyPatch(typeof(Character), "RPC_Damage"), HarmonyPrefix]
        private static bool Before(Character __instance, HitData hit, out float __state)
        {
            __state = -1f;   // no tree hit to log afterwards
            if (hit == null || !__instance.IsOwner() || __instance.IsDead()) return true;
            Citizen citizen = __instance.GetComponent<Citizen>();
            if (citizen == null) return true;
            bool tree = hit.m_hitType == HitData.HitType.Tree, impact = hit.m_hitType == HitData.HitType.Impact;
            if (!SettlerSafety.IsTreeHit(tree, impact, hit.HaveAttacker())) return true;
            if (SettlerSafety.IgnoresHit(ColonyConfig.SettlersSafeFromTrees.Value, tree, impact, hit.HaveAttacker()))
            {
                SettlerDeaths.CloseCall(citizen, hit.GetTotalDamage(), blocked: true, healthLeft: __instance.GetHealth());
                return false;
            }
            __state = __instance.GetHealth();
            return true;
        }

        [HarmonyPatch(typeof(Character), "RPC_Damage"), HarmonyPostfix]
        private static void After(Character __instance, float __state)
        {
            if (__state < 0f) return;
            Citizen citizen = __instance.GetComponent<Citizen>();
            float health = __instance.GetHealth();
            if (citizen != null) SettlerDeaths.CloseCall(citizen, __state - health, blocked: false, healthLeft: health);
        }
    }
}
