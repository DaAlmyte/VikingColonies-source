using System.Collections.Generic;
using HarmonyLib;
using VikingColonies.Citizens;
using VikingColonies.Construction;
using VikingColonies.Core;

namespace VikingColonies.Buildings
{
    [HarmonyPatch]
    internal static class BuildingSafetyPatches
    {
        /// <summary>
        /// Colony buildings — the huts and the pieces the builder put up for them — aren't destroyed by falling trees or
        /// rolling logs; monsters, the hammer and lost support still apply. The Town Hall chest takes damage only from players.
        /// </summary>
        [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.Damage)), HarmonyPrefix]
        private static bool Damage(WearNTear __instance, HitData hit)
        {
            if (hit == null) return true;
            if (hit.HaveAttacker() && BuildingSafety.IgnoresSettlerHit(IsSettler(hit)))   // e.g. the builder's hammer next to a wall, a guard's swing
            {
                SettlerHitsBlockedForTest++;
                return false;
            }
            bool lands = Lands(__instance, hit);
            if (lands && TrackHitsForTest && hit.HaveAttacker()) s_lastHit[__instance] = hit.GetAttacker();
            return lands;
        }

        private static bool Lands(WearNTear piece, HitData hit)
        {
            ColonyBuilding building = piece.GetComponent<ColonyBuilding>();
            if (building != null && building.IsTownHall && !TownHallHits.Counts(hit.GetAttacker() is Player)) return false;   // only players break a Town Hall
            bool colony = building != null || PiecePlacer.SiteOf(piece.gameObject).Length > 0;
            if (!colony) return true;
            return !BuildingSafety.IgnoresHit(hit.m_hitType == HitData.HitType.Tree, hit.m_hitType == HitData.HitType.Impact, hit.HaveAttacker());
        }

        /// <summary>Settlers' blows on a building that did nothing (vc_guardtest).</summary>
        internal static int SettlerHitsBlockedForTest;
        /// <summary>vc_guardtest: who last landed a blow on each piece, while it watches.</summary>
        internal static bool TrackHitsForTest;
        private static readonly Dictionary<WearNTear, Character> s_lastHit = new Dictionary<WearNTear, Character>();

        internal static Character LastHitForTest(WearNTear piece) => piece != null && s_lastHit.TryGetValue(piece, out Character by) ? by : null;

        internal static void StopTrackingForTest()
        {
            TrackHitsForTest = false;
            s_lastHit.Clear();
        }

        private static bool IsSettler(HitData hit)
        {
            Character attacker = hit.GetAttacker();
            return attacker != null && attacker.GetComponent<Citizen>() != null;
        }
    }
}
