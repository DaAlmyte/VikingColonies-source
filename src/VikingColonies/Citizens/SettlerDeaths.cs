using System.Linq;
using HarmonyLib;
using UnityEngine;
using VikingColonies.Buildings;
using VikingColonies.Colony;
using VikingColonies.Construction;
using VikingColonies.Core;

namespace VikingColonies.Citizens
{
    /// <summary>
    /// A settler's death, on the computer running him: the cause from the game's last hit, a log line, his cairn with what
    /// he carried (<see cref="SettlerCairn"/>), and the message to players near the colony (<see cref="DeathMessages"/>).
    /// Also the log line when a falling tree or log hits him.
    /// </summary>
    internal static class SettlerDeaths
    {
        private static readonly AccessTools.FieldRef<Character, HitData> s_lastHit = AccessTools.FieldRefAccess<Character, HitData>("m_lastHit");

        /// <summary>The last death line this computer wrote (the self-test reads it).</summary>
        public static string LastReport { get; private set; } = "";

        /// <summary>The last close-call line this computer wrote (the self-test reads it).</summary>
        public static string LastCloseCall { get; private set; } = "";

        public static void Register() => ColonyEvents.CitizenDied += OnDied;

        /// <summary>A falling tree or rolling log hit this settler (on the computer running him).</summary>
        public static void CloseCall(Citizen citizen, float damage, bool blocked, float healthLeft)
        {
            LastCloseCall = DeathLine.CloseCall(citizen.CitizenName, damage, blocked, healthLeft);
            Log.Info(LastCloseCall);
        }

        private static void OnDied(Citizen citizen)
        {
            if (citizen == null || !citizen.IsOwner || citizen.Body == null) return;
            CitizenRecord record = citizen.Record;
            HitData hit = s_lastHit(citizen.Body);   // the game keeps the hit that did damage last: the one that killed
            Character attacker = hit != null ? hit.GetAttacker() : null;
            string attackerName = attacker == null ? "" : attacker.IsPlayer() ? ((Player)attacker).GetPlayerName() : attacker.m_name;
            string hitType = hit != null ? hit.m_hitType.ToString() : "";
            string damageType = hit != null ? hit.m_damage.GetMajorityDamageType().ToString() : "";
            DeathCause cause = DeathCauses.From(hitType, attackerName, damageType);
            if (record.Job == SaveKeys.GuardJob) cause = GuardDuty.Cause(cause);   // he fell defending the colony (1.1)
            LastReport = DeathLine.Format(new DeathFacts
            {
                Name = record.Name,
                Job = record.Job,
                Hut = HutOf(record),
                Colony = Short(record.ColonyId),
                Position = CoreVectors.V(citizen.transform.position),
                Cause = DeathMessages.CauseText(cause),
                HitType = hitType,
                Attacker = attackerName.Length > 0 ? Localization.instance.Localize(attackerName) : "",
                Damage = hit != null ? hit.GetTotalDamage() : 0f,
                DamageType = damageType,
                SecondsSinceHit = double.IsNegativeInfinity(citizen.LastDamageTime) ? -1 : Time.time - citizen.LastDamageTime,
            });
            Log.Event(LastReport);
            CarryLoad held = CarryLoad.Together(citizen.Carry, citizen.InHand);   // what he held for an errand too (0.21)
            SettlerCairn cairn = SettlerCairn.Raise(citizen.transform.position, record.Name, record.Job, cause, held);
            if (cairn != null) Log.Event($"{record.Name}'s cairn stands at ({cairn.transform.position.x:0.0}, {cairn.transform.position.z:0.0}) with {held.Serialize()}");
            DeathMessages.Send(record.Name, record.Job, cause, citizen.transform.position, record.ColonyPos, cairn != null, record.Id);
            Saga.Record(record.ColonyId, "died", record.Name, cause.Token, cause.Arg);
        }

        /// <summary>"lumberjack_hut b1e10ec6 (level 1)", or the hut's id with "not loaded"; "" for a jobless settler.</summary>
        private static string HutOf(CitizenRecord record)
        {
            if (string.IsNullOrEmpty(record.Workplace)) return "";
            ColonyBuilding hut = ColonyBuilding.All.FirstOrDefault(b => b != null && b.IsValid && b.Id == record.Workplace);
            return hut != null ? $"{hut.TypeId} {Short(hut.Id)} (level {hut.Level})" : $"hut {Short(record.Workplace)} (not loaded)";
        }

        /// <summary>Ids are long; the first 8 characters tell them apart (as in vc_report).</summary>
        private static string Short(string id) => string.IsNullOrEmpty(id) ? "" : id.Length <= 8 ? id : id.Substring(0, 8);
    }
}
