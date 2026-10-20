using HarmonyLib;
using VikingColonies.Citizens;
using VikingColonies.Core;
using VikingColonies.Jobs;

namespace VikingColonies.Construction
{
    internal static class BuilderPatches
    {
        /// <summary>
        /// The game slides a character with its attack animation (root motion). A builder's swings don't slide him, so he
        /// stays where he stands while he works, whatever tool he holds. Only settlers whose job is builder.
        /// </summary>
        [HarmonyPatch]
        private static class AddRootMotionHook
        {
            [HarmonyPatch(typeof(Character), nameof(Character.AddRootMotion)), HarmonyPrefix]
            private static bool AddRootMotion(Character __instance)
            {
                if (!__instance.InAttack() || __instance.IsPlayer()) return true;
                Citizen citizen = __instance.GetComponent<Citizen>();
                return !BuildSwing.StaysPutWhileSwinging(citizen != null, IsBuilder(citizen));
            }
        }

        /// <summary>
        /// A builder's swing hits nothing itself: his job puts up the piece, fells the tree, breaks the rock or shapes the
        /// ground the moment the tool lands. So a pickaxe swing never digs a stray hole and a hammer never knocks a wall.
        /// Every other swing is the game's own.
        /// </summary>
        [HarmonyPatch]
        private static class DoMeleeAttackHook
        {
            [HarmonyPatch(typeof(Attack), "DoMeleeAttack"), HarmonyPrefix]
            private static bool DoMeleeAttack(Humanoid ___m_character)
            {
                if (___m_character == null || ___m_character.IsPlayer()) return true;
                Citizen citizen = ___m_character.GetComponent<Citizen>();
                return !BuildSwing.SkipsGameHit(citizen != null, IsBuilder(citizen));
            }
        }

        /// <summary>
        /// A piece of a colony building taken away with a hammer (the game's removal, not a monster's blow): a finished
        /// building leaves it out instead of putting it back (Repair, review of 0.13).
        /// </summary>
        [HarmonyPatch]
        private static class RemoveHook
        {
            [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.Remove)), HarmonyPrefix]
            private static void Remove(WearNTear __instance)
            {
                if (__instance == null) return;
                string site = PiecePlacer.SiteOf(__instance.gameObject);
                if (site.Length == 0) return;
                foreach (ConstructionModule hut in ConstructionModule.All)
                {
                    if (hut == null || !hut.IsValid || hut.Building == null || hut.Building.Id != site) continue;
                    hut.ReportRemoved(__instance.GetComponent<Piece>());
                    return;
                }
            }
        }

        private static bool IsBuilder(Citizen citizen) => citizen != null && citizen.Record.Job == JobDefinitions.BuilderId;
    }
}
