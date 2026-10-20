using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using VikingColonies.Citizens;
using VikingColonies.Core;

namespace VikingColonies.Jobs
{
    /// <summary>
    /// Notes what each settler's axe swing actually hit, so the log shows whether real swings land, and stops a swing from
    /// hurting anything but what he aims at (a tree beside the stump he chops, a log, a wall). Runs on the computer that
    /// runs the settler, where its attack happens; the game forwards the damage to the target's owner.
    /// </summary>
    internal static class SwingLog
    {
        public struct Hit
        {
            public GameObject Target;
            public string Name;
            public float Chop;
            public bool Stopped;   // not what he aimed at: it took no damage
        }

        private static readonly Dictionary<Citizen, List<Hit>> s_open = new Dictionary<Citizen, List<Hit>>();
        private static readonly Dictionary<Citizen, System.Action> s_onAxeLands = new Dictionary<Citizen, System.Action>();
        private static readonly Dictionary<Citizen, GameObject> s_aims = new Dictionary<Citizen, GameObject>();
        private static readonly Dictionary<string, SwingTally> s_tallies = new Dictionary<string, SwingTally>();

        /// <summary>True while a job applies a stand-in hit itself, so it isn't counted as what the swing hit.</summary>
        public static bool DirectHit;

        /// <param name="onAxeLands">Called right after the game works out what the swing hit (the moment the axe lands).</param>
        /// <param name="aim">What the swing is for; anything else it hits takes no damage. Null: nothing is stopped.</param>
        public static void BeginSwing(Citizen citizen, System.Action onAxeLands, GameObject aim = null)
        {
            foreach (Citizen gone in s_open.Keys.Where(c => c == null).ToList())
            {
                s_open.Remove(gone);
                s_onAxeLands.Remove(gone);
                s_aims.Remove(gone);
            }
            s_open[citizen] = new List<Hit>();
            s_onAxeLands[citizen] = onAxeLands;
            s_aims[citizen] = aim;
        }

        /// <summary>What the open swing has hit so far; null when no swing is open.</summary>
        public static List<Hit> Peek(Citizen citizen) => s_open.TryGetValue(citizen, out List<Hit> hits) ? hits : null;

        /// <summary>Ends the open swing and returns what it hit; null when no swing was open.</summary>
        public static List<Hit> EndSwing(Citizen citizen)
        {
            s_onAxeLands.Remove(citizen);
            s_aims.Remove(citizen);
            if (!s_open.TryGetValue(citizen, out List<Hit> hits)) return null;
            s_open.Remove(citizen);
            return hits;
        }

        public static SwingTally TallyFor(Citizen citizen)
        {
            string key = citizen.Id ?? "";
            if (!s_tallies.TryGetValue(key, out SwingTally tally)) s_tallies[key] = tally = new SwingTally();
            return tally;
        }

        /// <returns>False to stop the hit: a settler's swing that landed on something other than what he aims at.</returns>
        private static bool Note(Component target, HitData hit)
        {
            if (DirectHit || hit == null || s_open.Count == 0) return true;
            Character attacker = hit.GetAttacker();
            Citizen citizen = attacker != null ? attacker.GetComponent<Citizen>() : null;
            if (citizen == null || !s_open.TryGetValue(citizen, out List<Hit> hits)) return true;
            // A broken aim is == null but not ReferenceEquals null: the swing still mustn't hurt its neighbours.
            bool stopped = s_aims.TryGetValue(citizen, out GameObject aim) && !ReferenceEquals(aim, null) && !ReferenceEquals(aim, target.gameObject);
            hits.Add(new Hit { Target = target.gameObject, Name = Utils.GetPrefabName(target.gameObject), Chop = hit.m_damage.m_chop, Stopped = stopped });
            return !stopped;
        }

        /// <summary>The attack animation's hit moment: the game has just applied the swing's hits.</summary>
        [HarmonyPatch]
        private static class AxeLandsHook
        {
            [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.OnAttackTrigger)), HarmonyPostfix]
            private static void AxeLands(Humanoid __instance)
            {
                if (s_onAxeLands.Count == 0) return;
                Citizen citizen = __instance.GetComponent<Citizen>();
                if (citizen == null || !s_onAxeLands.TryGetValue(citizen, out System.Action onAxeLands)) return;
                // Inside the game's animation event: what goes wrong in his blow stays here (0.26, as the settler's tick).
                try
                {
                    onAxeLands?.Invoke();
                }
                catch (System.Exception e)
                {
                    if (s_toldBlowFailed++ < 3) Log.Error($"{citizen.CitizenName}'s blow failed: {e}");
                }
            }

            private static int s_toldBlowFailed;
        }

        [HarmonyPatch]
        private static class TreeHitHook
        {
            [HarmonyPatch(typeof(TreeBase), nameof(TreeBase.Damage)), HarmonyPrefix]
            private static bool TreeHit(TreeBase __instance, HitData hit) => Note(__instance, hit);
        }

        [HarmonyPatch]
        private static class LogHitHook
        {
            [HarmonyPatch(typeof(TreeLog), nameof(TreeLog.Damage)), HarmonyPrefix]
            private static bool LogHit(TreeLog __instance, HitData hit) => Note(__instance, hit);
        }

        [HarmonyPatch]
        private static class DestructibleHitHook
        {
            [HarmonyPatch(typeof(Destructible), nameof(Destructible.Damage)), HarmonyPrefix]
            private static bool DestructibleHit(Destructible __instance, HitData hit) => Note(__instance, hit);
        }

        [HarmonyPatch]
        private static class RockHitHook
        {
            [HarmonyPatch(typeof(MineRock), nameof(MineRock.Damage)), HarmonyPrefix]
            private static bool RockHit(MineRock __instance, HitData hit) => Note(__instance, hit);
        }

        [HarmonyPatch]
        private static class BigRockHitHook
        {
            [HarmonyPatch(typeof(MineRock5), nameof(MineRock5.Damage)), HarmonyPrefix]
            private static bool BigRockHit(MineRock5 __instance, HitData hit) => Note(__instance, hit);
        }

        [HarmonyPatch]
        private static class PieceHitHook
        {
            [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.Damage)), HarmonyPrefix]
            private static bool PieceHit(WearNTear __instance, HitData hit) => Note(__instance, hit);
        }
    }
}
