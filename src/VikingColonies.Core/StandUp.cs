using System;
using System.Collections.Generic;

namespace VikingColonies.Core
{
    /// <summary>
    /// What's weak for a colony's age (1.5.0, the design of 2026-10-21): the Town Hall's level adds to 1.4.1's few. One star
    /// at most, as <see cref="FleeTracker.IsWeak"/>; by name, never by health (a Deathsquito stays a run-from).
    /// </summary>
    public static class WeakRules
    {
        public static bool IsWeakFor(string prefab, int level, int hallLevel)
        {
            if (string.IsNullOrEmpty(prefab) || level > FleeTracker.WeakMostLevel) return false;
            if (FleeTracker.IsWeak(prefab, Math.Max(1, level))) return true;   // level 1's row is 1.4.1's list
            int hall = Math.Max(1, Math.Min(5, hallLevel));
            if (hall >= 2 && prefab == "Greydwarf") return true;   // after Eikthyr: the Black Forest's smallest
            return hall >= 3 && (prefab == "Skeleton" || prefab == "Skeleton_NoArcher");   // after the Elder
        }
    }

    public enum StandUpChoice { CarryOn, Fight, Flee }

    /// <summary>Fight a weak creature off, run, or carry on (the nearest creature that counts, <see cref="FleeTracker.Counts"/>).</summary>
    public static class StandUp
    {
        /// <summary>A weak creature going for a settler this near is fought; farther, they carry on and keep an eye on it.</summary>
        public const float GoToFight = 8f;
        /// <summary>At this much health or less a settler breaks off and runs.</summary>
        public const float BreakOffHealth = 0.5f;

        /// <summary>
        /// Not weak for the colony, a raid or the alarm, or the setting off: today's flee. Hit from afar or hurt to half: flee.
        /// Going for this settler: a timid one runs (unless standing their ground), within 8 m it's fought, farther they carry
        /// on. After someone else: a brave settler fights one within 3 m; standing their ground they carry on; else today's.
        /// </summary>
        public static StandUpChoice Decide(bool weakForColony, bool goesForMe, float distance, bool hitFromAfar, float health01,
            bool standingGround, Trait trait, bool raidOrAlarm, bool fightBackSetting)
        {
            if (!weakForColony || raidOrAlarm || !fightBackSetting || hitFromAfar || health01 <= BreakOffHealth) return StandUpChoice.Flee;
            if (goesForMe)
            {
                if (trait == Trait.Timid && !standingGround) return StandUpChoice.Flee;
                return distance <= GoToFight ? StandUpChoice.Fight : StandUpChoice.CarryOn;
            }
            if (trait == Trait.Brave && distance <= FleeTracker.WeakReach) return StandUpChoice.Fight;
            return standingGround ? StandUpChoice.CarryOn : StandUpChoice.Flee;
        }
    }

    /// <summary>
    /// Standing their ground: fled from creatures of one kind 3 times in 10 minutes, a settler doesn't flee from that kind
    /// for the next 10 (Necks come out of one pond one after another). Kept in memory; the caller notes only weak kinds.
    /// </summary>
    public sealed class StandGround
    {
        public const int StandAfterFlees = 3;
        public const double MemorySeconds = 600;

        private readonly Dictionary<string, List<double>> _flees = new Dictionary<string, List<double>>();
        private readonly Dictionary<string, double> _until = new Dictionary<string, double>();

        public void NoteFlee(string kind, double now)
        {
            if (string.IsNullOrEmpty(kind)) return;
            if (!_flees.TryGetValue(kind, out List<double> times)) _flees[kind] = times = new List<double>();
            times.Add(now);
            times.RemoveAll(t => t < now - MemorySeconds);
            if (times.Count < StandAfterFlees) return;
            _until[kind] = now + MemorySeconds;
            times.Clear();
        }

        public bool Standing(string kind, double now) => !string.IsNullOrEmpty(kind) && _until.TryGetValue(kind, out double until) && now < until;
    }

    /// <summary>
    /// Every settler heals (1.5.0, with fighting back: one left below half would run from every Neck for good): 2% of their
    /// health every 5 s once nothing has hurt them for 20 s; lying in bed, 20% a step, so a night makes them whole.
    /// </summary>
    public static class Healing
    {
        public const float QuietSeconds = 20f, StepSeconds = 5f, StepFraction = 0.02f, InBedFraction = 0.2f;

        public static float Step(float health01, float sinceHurt, bool inBed)
        {
            if (health01 <= 0f) return 0f;
            if (inBed) return Math.Min(1f, health01 + InBedFraction);
            return sinceHurt < QuietSeconds ? health01 : Math.Min(1f, health01 + StepFraction);
        }
    }

    public enum FightHand { Held, Fists }

    public static class FightRules
    {
        /// <summary>A weapon or a torch is swung; a tool, a bow, a shield or nothing in hand: fists (the game's own unarmed blow).</summary>
        public static FightHand Hand(string itemType) =>
            itemType == "OneHandedWeapon" || itemType == "TwoHandedWeapon" || itemType == "TwoHandedWeaponLeft" || itemType == "Torch"
                ? FightHand.Held : FightHand.Fists;

        /// <summary>As <see cref="Hand(string)"/>, and a pickaxe is put away too: its blow digs the ground (its review).</summary>
        public static FightHand Hand(string itemType, string skill) => skill == "Pickaxes" ? FightHand.Fists : Hand(itemType);

        /// <summary>
        /// The creature fought is kept while it lives, still goes for them and is near (its review: in a pack they switched to
        /// the nearest every half second, the blow under way landing anywhere).
        /// </summary>
        public static bool KeepTarget(bool alive, bool goesForMe, float distance) => alive && goesForMe && distance <= StandUp.GoToFight;

        /// <summary>A bite counts for running only if it came after the last fight ended (its review: they hid after winning).</summary>
        public static bool HitCounts(double hitTime, double fightEndedAt) => hitTime > fightEndedAt;

        /// <summary>Swimming, flying, or above what a blow reaches: not fought, they carry on (its review: they waded in after a Neck).</summary>
        public static bool Reachable(bool swimming, bool flying, float aboveGround) => !swimming && !flying && aboveGround <= GuardRules.MaxReachUp;

        /// <summary>A fight that drew them this far from where it began ends: they never chase one across the map (the spec).</summary>
        public static bool TooFarFromStart(float fromStart, float reach) => fromStart > StandUp.GoToFight + reach;

        /// <summary>
        /// No blow landed for as long as a guard allows (the walk there, then 15 s): they give it up (a Neck swimming off, one
        /// on a rock), counted from the fight's start until a blow lands.
        /// </summary>
        public static bool GivesUp(float sinceBlowLanded, float distanceAtStart) => sinceBlowLanded >= GuardRules.GiveUpAfter(distanceAtStart);
    }
}
