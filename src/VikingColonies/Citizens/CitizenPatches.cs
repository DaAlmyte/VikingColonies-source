using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using VikingColonies.Core;

namespace VikingColonies.Citizens
{
    internal static class CitizenPatches
    {
        /// <summary>Hits of a guard that reached a player, a settler or a tame (vc_guardtest: the game's own rules keep it 0).</summary>
        internal static int FriendlyHitsForTest;

        /// <summary>Blows of a settler fighting off a weak creature that reached something else and did nothing (vc_weaktest).</summary>
        internal static int StrayFightHitsForTest;

        [HarmonyPatch]
        private static class HoverNameHook
        {
            [HarmonyPatch(typeof(Character), nameof(Character.GetHoverName)), HarmonyPostfix]
            private static void HoverName(Character __instance, ref string __result)
            {
                Citizen citizen = __instance.GetComponent<Citizen>();
                if (citizen != null) __result = citizen.DisplayName;
            }
        }

        [HarmonyPatch]
        private static class HoverTextHook
        {
            [HarmonyPatch(typeof(Character), nameof(Character.GetHoverText)), HarmonyPostfix]
            private static void HoverText(Character __instance, ref string __result)
            {
                Citizen citizen = __instance.GetComponent<Citizen>();
                if (citizen == null) return;
                string status = Localization.instance.Localize(citizen.Status ?? "");
                // In free time, what he's doing (0.14): "Crops growing (talking with Bjorn)".
                // Carrying (0.21): "carrying 10 Raspberries to the Town Hall", the item and the building in the player's language.
                if (ActivityWords.Decode(citizen.Doing, out string doing, out string partner, out string partnerId) && ActivityWords.InHover(doing))
                    status = ActivityWords.Hover(status, SafeText.Format(Localization.instance.Localize(doing),   // the pattern is the settler's record's
                        Localization.instance.Localize(partner), Localization.instance.Localize(partnerId)));
                __result = string.IsNullOrEmpty(status) ? citizen.DisplayName : citizen.DisplayName + "\n" + status;
                // Who he is (0.16): "Cheerful · good at foraging".
                CitizenRecord r = citizen.Record;
                string who = Localization.instance.Localize(Traits.Token(citizen.TraitKind));
                if (who.Length > 0) who = char.ToUpperInvariant(who[0]) + who.Substring(1);
                if (r.Calling.Length > 0 && r.Calling != Callings.None) who += " · " + Localization.instance.Localize("$vc_good_at_" + r.Calling);
                // His mood (0.17): "Cheerful · good at foraging · Content", the unhappy in orange.
                if (Config.ColonyConfig.Mood.Value && r.HasColony)
                {
                    MoodLevel mood = citizen.MoodLevel;
                    string feels = Localization.instance.Localize(MoodLevels.Token(mood));
                    who += " · " + (MoodLevels.ShowsWords(mood) ? "<color=orange>" + feels + "</color>" : feels);
                }
                __result += "\n<color=#c8c8c8>" + who + "</color>";
                if (citizen.Record.Hungry && Colony.Creative.SettlersEat(citizen.Record.ColonyId))
                    __result += "\n<color=orange>" + Localization.instance.Localize("$vc_hungry") + "</color>";
            }
        }

        /// <summary>
        /// Settlers chop like a player of their own Woodcutting level (a new character at first) instead of the
        /// game's near-full damage for non-player characters.
        /// </summary>
        [HarmonyPatch]
        private static class WoodcuttingStrengthHook
        {
            [HarmonyPatch(typeof(Character), nameof(Character.GetRandomSkillFactor)), HarmonyPostfix]
            private static void WoodcuttingStrength(Character __instance, Skills.SkillType skill, ref float __result)
            {
                if (skill != Skills.SkillType.WoodCutting) return;
                Citizen citizen = __instance.GetComponent<Citizen>();
                if (citizen != null) __result = SettlerSkill.DamageFactor(citizen.Record.Woodcutting, UnityEngine.Random.value) * citizen.WorkSpeed;   // weaker when hungry
            }
        }

        /// <summary>A guard hits like a player of his Fighting level (1.1), with any weapon, as a woodcutter chops by his Woodcutting.</summary>
        [HarmonyPatch]
        private static class FightingStrengthHook
        {
            [HarmonyPatch(typeof(Character), nameof(Character.GetRandomSkillFactor)), HarmonyPostfix]
            private static void FightingStrength(Character __instance, Skills.SkillType skill, ref float __result)
            {
                if (!IsWeaponSkill(skill)) return;
                Citizen citizen = __instance.GetComponent<Citizen>();
                if (citizen != null && citizen.Record.Job == SaveKeys.GuardJob)
                    __result = SettlerSkill.DamageFactor(citizen.Record.Fighting, UnityEngine.Random.value) * citizen.WorkSpeed;   // weaker when hungry
            }
        }

        /// <summary>
        /// Settlers are spared by the console's kill commands and debug mode's K (1.4.2, a player's report: K killed the
        /// whole colony). Only that blow: anything else hurts them as before.
        /// </summary>
        [HarmonyPatch]
        private static class CheatKillHook
        {
            [HarmonyPatch(typeof(Character), nameof(Character.Damage)), HarmonyPrefix]
            private static bool Spare(Character __instance, HitData hit)
            {
                if (hit == null || __instance == null || __instance.GetComponent<Citizen>() == null) return true;
                if (!SettlerSafety.CheatKill(hit.m_damage.m_damage, !hit.m_attacker.IsNone())) return true;
                CheatKillsSparedForTest++;
                return false;
            }
        }

        /// <summary>Console kill blows a settler was spared (the self-test reads it).</summary>
        internal static int CheatKillsSparedForTest;

        /// <summary>
        /// A guard's blow (1.1), on the computer running him: on one of his targets it is practice at Fighting; on anything else
        /// (a deer beside the monster, a player, a settler, a tame) it does nothing (review of 1.1: his swing's arc hits all round).
        /// </summary>
        [HarmonyPatch]
        private static class FightingPracticeHook
        {
            [HarmonyPatch(typeof(Character), nameof(Character.Damage)), HarmonyPrefix]
            private static bool FightingPractice(Character __instance, HitData hit)
            {
                Citizen guard = hit != null ? hit.GetAttacker()?.GetComponent<Citizen>() : null;
                if (guard == null) return true;
                if (guard.Record.Job != SaveKeys.GuardJob)
                {
                    // 1.5.0: a settler fighting off a weak creature practises Fighting when a blow lands on it; the swing's
                    // arc on anything else (a Boar grazing by, a settler, the player) does nothing (its review).
                    AI.CitizenAI fighter = guard.GetComponent<AI.CitizenAI>();
                    if (fighter == null || fighter.FightTarget == null) return true;
                    if (fighter.FightTarget != __instance)
                    {
                        StrayFightHitsForTest++;
                        return false;
                    }
                    guard.Practise(WorkSkill.Fighting);
                    fighter.NoteBlowLanded();
                    return true;
                }
                if (!GuardRules.IsTarget(Jobs.GuardJob.FactsOf(guard.Body, __instance)))
                {
                    if (__instance.GetComponent<Citizen>() != null || __instance.IsPlayer() || __instance.IsTamed())
                    {
                        FriendlyHitsForTest++;
                        Log.Warn($"{guard.CitizenName}'s blow reached {__instance.m_name}, who is no monster (it did nothing)");
                    }
                    return false;
                }
                guard.Practise(WorkSkill.Fighting);
                return true;
            }
        }

        /// <summary>A guard's shield stopped a blow (1.2): three quick ones lower it a moment (he has no stamina to spend).</summary>
        [HarmonyPatch]
        private static class ShieldBlockHook
        {
            [HarmonyPatch(typeof(Humanoid), "BlockAttack"), HarmonyPostfix]
            private static void ShieldBlocked(Humanoid __instance, bool __result)
            {
                if (!__result) return;
                (__instance.GetComponent<AI.CitizenAI>()?.JobForTest as Jobs.GuardJob)?.NoteBlock();
            }
        }

        /// <summary>
        /// A guard's crossbow counts as loaded (review of 1.2: the game loads one only for a player, so it never fired); he waits
        /// its loading time between shots himself (GuardJob).
        /// </summary>
        [HarmonyPatch]
        private static class GuardCrossbowHook
        {
            [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.IsWeaponLoaded)), HarmonyPostfix]
            private static void Loaded(Humanoid __instance, ref bool __result)
            {
                if (!__result && IsGuard(__instance)) __result = true;
            }
        }

        /// <summary>
        /// A guard's armour protects him as a player's does (review of 1.2: the game counts armour only for players). Only a
        /// guard's: other settlers' work clothes stay for looks.
        /// </summary>
        [HarmonyPatch]
        private static class GuardArmourHook
        {
            [HarmonyPatch(typeof(Character), "RPC_Damage"), HarmonyPrefix]
            private static void Armour(Character __instance, HitData hit)
            {
                if (hit == null || !(__instance is Humanoid body) || !IsGuard(body)) return;
                float armour = Jobs.GuardJob.ArmourOf(body);
                if (armour > 0f) hit.ApplyArmor(armour);
            }
        }

        /// <summary>A monster whose last hit was a guard's is his kill (1.3), on the computer the monster dies on.</summary>
        [HarmonyPatch]
        private static class GuardKillHook
        {
            private static readonly AccessTools.FieldRef<Character, HitData> s_lastHit = AccessTools.FieldRefAccess<Character, HitData>("m_lastHit");

            [HarmonyPatch(typeof(Character), "OnDeath"), HarmonyPrefix]
            private static void Died(Character __instance)
            {
                if (__instance == null || __instance.IsPlayer() || __instance.GetComponent<Citizen>() != null) return;
                Citizen guard = s_lastHit(__instance)?.GetAttacker()?.GetComponent<Citizen>();
                if (guard != null && guard.Record != null && guard.Record.Job == SaveKeys.GuardJob) guard.CountKill();
            }
        }

        private static bool IsGuard(Character c)
        {
            Citizen citizen = c != null ? c.GetComponent<Citizen>() : null;
            return citizen != null && citizen.Record != null && citizen.Record.Job == SaveKeys.GuardJob;
        }

        private static bool IsWeaponSkill(Skills.SkillType skill)
        {
            switch (skill)
            {
                case Skills.SkillType.Swords:
                case Skills.SkillType.Knives:
                case Skills.SkillType.Clubs:
                case Skills.SkillType.Polearms:
                case Skills.SkillType.Spears:
                case Skills.SkillType.Axes:
                case Skills.SkillType.Unarmed:
                case Skills.SkillType.Bows:
                case Skills.SkillType.Crossbows:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Players can't hurt settlers (no accidental axe swings), nor can a ballista's bolts (review of 1.3: a bolt has no
        /// attacker, and one shot at a monster may pass a settler); monsters still can.
        /// </summary>
        [HarmonyPatch]
        private static class BlockPlayerDamageHook
        {
            [HarmonyPatch(typeof(Character), nameof(Character.Damage)), HarmonyPrefix]
            private static bool BlockPlayerDamage(Character __instance, HitData hit)
            {
                if (hit == null || __instance.GetComponent<Citizen>() == null) return true;
                return !(hit.GetAttacker() is Player || hit.m_hitType == HitData.HitType.Turret);
            }
        }

        /// <summary>
        /// A ballista never takes a settler for its target, and one in a colony's area (the guards keep those loaded) never a
        /// player either (review of 1.3: the game's search, used only by ballistas, has no rule against either; the player saw
        /// the colony's ballista shoot him in the 1.3 runs). When the nearest it senses is one of them, the game's own search
        /// runs again with them left out of its list, so every other rule of the game's still holds. A ballista outside any
        /// colony keeps the game's own choice of players.
        /// </summary>
        [HarmonyPatch]
        private static class TurretSparesSettlersHook
        {
            [HarmonyPatch(typeof(BaseAI), nameof(BaseAI.FindClosestCreature)), HarmonyPostfix]
            private static void SpareSettlers(ref Character __result, Transform me, Vector3 eyePoint, float hearRange, float viewRange, float viewAngle,
                bool alerted, bool mistVision, bool passiveAggresive, bool includePlayers, bool includeTamed, bool includeEnemies, List<Character> onlyTargets)
            {
                if (s_searching || __result == null || me == null) return;
                bool settler = __result.GetComponent<Citizen>() != null, player = __result is Player;
                if (!settler && !player) return;
                Turret turret = me.GetComponentInParent<Turret>();
                if (turret == null) return;
                bool inColony = Buildings.ColonyAreas.ColonyAt(turret.transform.position) != null;
                if (player && !inColony) return;
                string spared = settler ? __result.GetComponent<Citizen>().CitizenName : "a player";
                List<Character> all = Character.GetAllCharacters();
                var kept = new List<Character>(all);
                Character instead;
                s_searching = true;
                try
                {
                    all.RemoveAll(c => c != null && (c.GetComponent<Citizen>() != null || (inColony && c is Player)));
                    instead = BaseAI.FindClosestCreature(me, eyePoint, hearRange, viewRange, viewAngle, alerted, mistVision, passiveAggresive,
                        includePlayers, includeTamed, includeEnemies, onlyTargets);
                }
                finally
                {
                    all.Clear();
                    all.AddRange(kept);   // the game's list as it was, in its order
                    s_searching = false;
                }
                if (instead != null && (instead.GetComponent<Citizen>() != null || (inColony && instead is Player))) instead = null;   // whatever the list was
                if (s_logSpared)
                {
                    s_logSpared = false;
                    Log.Info($"A ballista passes over {spared}" + (instead != null ? $" for the {instead.m_name}" : " (nothing else in sight)") + " (once a session)");
                }
                if (settler) SparedForTest++;
                else SparedPlayersForTest++;
                __result = instead;
            }

            private static bool s_searching, s_logSpared = true;
        }

        /// <summary>Times a ballista passed over a settler, and a colony's over a player (vc_guardtest).</summary>
        internal static int SparedForTest, SparedPlayersForTest;
    }
}
