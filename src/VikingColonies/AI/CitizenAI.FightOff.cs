using System;
using UnityEngine;
using VikingColonies.Buildings;
using VikingColonies.Config;
using VikingColonies.Core;
using VikingColonies.Jobs;

namespace VikingColonies.AI
{
    /// <summary>
    /// Settlers stand up to weak animals (1.5.0; the design of 2026-10-21, the player's answers 2026-10-23): a weak creature
    /// going for a settler is fought off with what's in their hands, or fists, and they run only when hurt (<see cref="StandUp"/>).
    /// A small melee of its own; the guard's fight keeps its shield and bow rules.
    /// </summary>
    public partial class CitizenAI
    {
        private Character _fightTarget;
        private float _fightSince, _fightStartDistance, _lastBlowAt = float.NegativeInfinity;
        private Vector3 _fightFrom;
        private double _fightEndedAt = double.NegativeInfinity;
        private ItemDrop.ItemData _putAwayForFight;
        private float _healAt;
        private readonly StandGround _standGround = new StandGround();
        /// <summary>Creatures they gave up on (out of reach, no blow landing, drawn too far): left alone a minute (its review).</summary>
        private readonly TargetBlacklist _fightGivenUp = new TargetBlacklist(GuardRules.IgnoreSeconds);
        private static readonly HarmonyLib.AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> s_rightItem = RightItemRef();

        private static HarmonyLib.AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> RightItemRef()
        {
            try { return HarmonyLib.AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_rightItem"); }
            catch (Exception) { return null; }   // a game update renamed it (GameCheck says so): they swing what they hold
        }

        /// <summary>The weak creature they are fighting off, or null (the Fighting practice hook and vc_weaktest read it).</summary>
        internal Character FightTarget => _fightTarget;

        /// <summary>
        /// A fight is on, though what they fought may be gone: the game destroys a creature as it dies, and Unity's == null
        /// then says no fight, which skipped its end (the fix pass's game run: the fight's bites then started a run).
        /// </summary>
        private bool Fighting => !ReferenceEquals(_fightTarget, null);

        /// <summary>A blow of theirs landed on the creature they fight (the practice hook): the give-up clock starts again.</summary>
        internal void NoteBlowLanded() => _lastBlowAt = Time.time;

        /// <summary>
        /// What they do about the nearest creature that counts (it is the nearest; nothing strong is about). One out of reach
        /// (swimming, flying, high up) or given up on is left alone: they carry on (its review: they waded in after a Neck).
        /// </summary>
        private StandUpChoice DecideAbout(Character other, float distance, int hallLevel)
        {
            Humanoid body = _citizen.Body;
            string prefab = Utils.GetPrefabName(other.gameObject);
            MonsterAI ai = other.GetComponent<MonsterAI>();
            bool goesForMe = ai != null && ai.GetTargetCreature() == body;
            bool hitFromAfar = Time.time - _citizen.LastMonsterHitTime <= FleeTracker.HitMemorySeconds && _citizen.LastMonsterHitDistance > StandUp.GoToFight;
            bool raidOrAlarm = RaidAtColony() || TownHallModule.ColonyAlarmFor(_citizen.ColonyId, out _);
            StandUpChoice choice = StandUp.Decide(WeakRules.IsWeakFor(prefab, other.GetLevel(), hallLevel), goesForMe, distance, hitFromAfar,
                body.GetHealthPercentage(), _standGround.Standing(prefab, Time.time), _citizen.TraitKind, raidOrAlarm,
                ColonyConfig.SettlersFightBack.Value);
            if (choice == StandUpChoice.Fight && (!Reachable(other) || _fightGivenUp.IsBlocked(IdOf(other), Time.time))) return StandUpChoice.CarryOn;
            return choice;
        }

        private static bool Reachable(Character c)
        {
            Vector3 at = c.transform.position;
            float ground = ZoneSystem.instance != null ? ZoneSystem.instance.GetGroundHeight(at) : at.y;
            return FightRules.Reachable(c.IsSwimming(), c.IsFlying(), at.y - ground);
        }

        private static string IdOf(Character c)
        {
            ZNetView nview = c.GetComponent<ZNetView>();
            return nview != null && nview.IsValid() ? nview.GetZDO().m_uid.ToString() : c.GetInstanceID().ToString();
        }

        /// <summary>The fight they're in goes on: it lives, still goes for them and is near (<see cref="FightRules.KeepTarget"/>).</summary>
        private bool KeepsFighting(out float distance)
        {
            distance = float.PositiveInfinity;
            Character t = _fightTarget;
            if (t == null) return false;
            bool alive = !t.IsDead() && t.gameObject.activeInHierarchy;
            MonsterAI ai = alive ? t.GetComponent<MonsterAI>() : null;
            distance = alive ? Vector3.Distance(transform.position, t.transform.position) : float.PositiveInfinity;
            return FightRules.KeepTarget(alive, ai != null && ai.GetTargetCreature() == _citizen.Body, distance)
                   && _citizen.Body.GetHealthPercentage() > StandUp.BreakOffHealth;
        }

        private void StartFight(Character target, float distance)
        {
            if (Fighting)
            {
                // Another goes for them now: the blow under way finishes first, then they turn (its review: switching mid-swing
                // let that blow land on walls and crops); the hand stays as it is.
                if (_citizen.Body.InAttack()) return;
                SwingLog.EndSwing(_citizen);
                Log.Info($"{_citizen.CitizenName} turns to the {target.m_name}");
                Aim(target, distance);
                return;
            }
            if (_breakfast.Running) _breakfast.Interrupt();   // up from the table first (its review: held in the chair, bitten)
            Aim(target, distance);
            Humanoid body = _citizen.Body;
            ItemDrop.ItemData held = s_rightItem != null ? s_rightItem(body) : null;
            bool fists = held == null || FightRules.Hand(held.m_shared.m_itemType.ToString(), held.m_shared.m_skillType.ToString()) == FightHand.Fists;
            if (held != null && fists)
            {
                body.UnequipItem(held, false);   // a hoe or a pickaxe isn't swung at a Neck: fists (a pickaxe would dig the ground)
                _putAwayForFight = held;
            }
            Log.Info($"{_citizen.CitizenName} stands up to the {target.m_name} ({(fists ? "fists" : Localization.instance.Localize(held.m_shared.m_name))}, {distance:0.0} m away)");
        }

        private void Aim(Character target, float distance)
        {
            _fightTarget = target;
            _fightSince = Time.time;
            _lastBlowAt = float.NegativeInfinity;
            _fightStartDistance = distance;
            _fightFrom = transform.position;
        }

        /// <summary>Ends the fight; the hand's item back, and the job goes on unless they run now.</summary>
        private void EndFight(string why, bool resume)
        {
            if (!Fighting && _putAwayForFight == null) return;
            Log.Info($"{_citizen.CitizenName} stops fighting{(Fighting ? " the " + _fightTarget.m_name : "")}: {why}");
            SwingLog.EndSwing(_citizen);
            _fightTarget = null;
            _fightEndedAt = Time.time;
            Humanoid body = _citizen.Body;
            if (_putAwayForFight != null && body.GetInventory().ContainsItem(_putAwayForFight)) body.EquipItem(_putAwayForFight, false);
            _putAwayForFight = null;
            if (!resume) return;
            try
            {
                _job?.Resume();
            }
            catch (Exception e)
            {
                OnWorkError(e);
            }
        }

        /// <summary>Gives the creature up (left alone a minute) and ends the fight.</summary>
        private void GiveUpFight(string why)
        {
            if (_fightTarget != null) _fightGivenUp.Add(IdOf(_fightTarget), Time.time);
            EndFight(why, resume: true);
        }

        /// <summary>
        /// Every settler heals (1.5.0): 2% every 5 s once nothing has hurt them for 20 s, fast in bed (<see cref="Healing"/>).
        /// The game saves a character's health: nothing new is saved.
        /// </summary>
        private void HealStep()
        {
            Humanoid body = _citizen.Body;
            if (body == null || body.IsDead()) return;
            float max = body.GetMaxHealth();
            if (max <= 0f) return;
            float now01 = body.GetHealth() / max;
            float next = Healing.Step(now01, (float)(Time.time - _citizen.LastDamageTime), _lying);
            if (next > now01 + 0.0001f) body.SetHealth(next * max);
        }

        /// <summary>A flee from a weak kind is counted: the third in 10 minutes and they stand their ground for the next 10.</summary>
        private void NoteFleeFrom(Character other, int hallLevel)
        {
            string prefab = Utils.GetPrefabName(other.gameObject);
            if (!WeakRules.IsWeakFor(prefab, other.GetLevel(), hallLevel)) return;
            bool before = _standGround.Standing(prefab, Time.time);
            _standGround.NoteFlee(prefab, Time.time);
            if (!before && _standGround.Standing(prefab, Time.time))
                Log.Info($"{_citizen.CitizenName} stands their ground: fled from the {other.m_name} {StandGround.StandAfterFlees} times in 10 minutes");
        }

        /// <summary>
        /// One tick of the fight: up to its reach, face it, the game's own blow (only what they aim at is hurt, as the
        /// lumberjack's axe). False when it's over: dead, gone, off, out of reach, drawn too far, or no blow lands for too long.
        /// </summary>
        private bool FightOff()
        {
            Character t = _fightTarget;
            if (t == null || t.IsDead() || !t.gameObject.activeInHierarchy)
            {
                EndFight("it's gone", resume: true);
                return false;
            }
            float gap = Utils.DistanceXZ(transform.position, t.transform.position);
            if (gap > FleeTracker.StartDistance)
            {
                EndFight("it's off", resume: true);
                return false;
            }
            Humanoid body = _citizen.Body;
            ItemDrop.ItemData weapon = body.GetCurrentWeapon();
            float reach = Mathf.Max(1.5f, weapon != null ? weapon.m_shared.m_attack.m_attackRange : 1.5f) + t.GetRadius();
            if (!Reachable(t))
            {
                GiveUpFight("out of reach");
                return false;
            }
            if (FightRules.TooFarFromStart(Utils.DistanceXZ(transform.position, _fightFrom), reach))
            {
                GiveUpFight("it drew them too far");
                return false;
            }
            if (FightRules.GivesUp(Time.time - Mathf.Max(_fightSince, _lastBlowAt), _fightStartDistance))
            {
                GiveUpFight("no blow lands");
                return false;
            }
            _citizen.Status = "$vc_status_fighting_off " + t.GetHoverName();
            Vector3 at = t.transform.position;
            if (gap > reach)
            {
                if (gap > reach + 3f) GoTo(at, reach * 0.8f, hurry: true);
                else StepTowards(at);
                return true;
            }
            Idle();
            Face(at);
            if (!body.InAttack() && IsFacing(at) && body.StartAttack(t, false)) SwingLog.BeginSwing(_citizen, null, t.gameObject);
            return true;
        }
    }
}
