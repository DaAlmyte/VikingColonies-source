using System;

namespace VikingColonies.Core
{
    public enum ShelterKind { TownHall, Hut, AwayFromThreat }

    public struct Shelter
    {
        public Shelter(ShelterKind kind, GroundPoint point)
        {
            Kind = kind;
            Point = point;
        }

        public ShelterKind Kind { get; }
        public GroundPoint Point { get; }
    }

    /// <summary>
    /// When a settler runs for shelter: a monster close by or a recent monster hit starts it; it ends once 15 s have
    /// passed since the later of "a monster was within 20 m" and "the last monster hit".
    /// </summary>
    public sealed class FleeTracker
    {
        public const float StartDistance = 12f;
        public const float WatchDistance = 20f;
        public const float HitMemorySeconds = 5f;
        public const float CalmSeconds = 15f;
        public const float RunAwayDistance = 15f;

        private double _lastThreat = double.NegativeInfinity;

        public bool Fleeing { get; private set; }

        /// <param name="nearestMonster">Distance to the nearest monster in metres; float.PositiveInfinity when none.</param>
        /// <param name="lastHitTime">When a monster last hit the settler; double.NegativeInfinity if never.</param>
        /// <param name="now">The current time in seconds.</param>
        /// <param name="startDistance">How near a monster comes before he runs (a brave settler waits longer, a timid one less, 0.16).</param>
        public bool Update(float nearestMonster, double lastHitTime, double now, float startDistance = StartDistance)
        {
            if (nearestMonster <= WatchDistance) _lastThreat = now;
            if (lastHitTime > _lastThreat) _lastThreat = lastHitTime;
            bool hitRecently = now - lastHitTime <= HitMemorySeconds;
            if (!Fleeing) Fleeing = nearestMonster <= startDistance || hitRecently;
            else if (now - _lastThreat >= CalmSeconds) Fleeing = false;
            return Fleeing;
        }

        /// <summary>
        /// A monster counts when it is close, or alerted, or hunting someone, and not asleep. One minding its own
        /// business further away (penned, caged, stuck outside the wall) doesn't keep workers from working. With the
        /// world's passive enemies, only one that was stirred up (alerted or hunting) counts, however close.
        /// </summary>
        public static bool Counts(bool sleeping, bool alerted, bool hasTarget, float distance, bool passiveEnemies = false, bool weak = false,
                                  bool targetsHim = false) =>
            !sleeping && (weak && !targetsHim
                ? (!passiveEnemies || alerted || hasTarget) && (distance <= WeakReach || hasTarget && distance <= WeakFightReach)
                : (!passiveEnemies && distance <= StartDistance) || alerted || hasTarget);

        /// <summary>
        /// A weak creature after someone else counts only this close (and a bite always does). One after this very settler
        /// counts as any monster does: he runs early and keeps off while it is about (the first 1.4.1 necks run: counted only
        /// from 6 m, a Neck came on and bit, he came back as soon as it stood off, and four settlers died in a minute).
        /// </summary>
        public const float WeakReach = 3f;

        /// <summary>
        /// One fighting someone else counts this close: he keeps clear of the fight (the second 1.4.1 necks run: settlers
        /// strolled back among Necks biting at the player, and died).
        /// </summary>
        public const float WeakFightReach = 8f;

        private static readonly System.Collections.Generic.HashSet<string> s_weak =
            new System.Collections.Generic.HashSet<string> { "Neck", "Greyling", "Boar" };

        /// <summary>
        /// Whether a creature is one of the weak few (a Neck, a Greyling, a Boar, by its prefab's name, with its stars): it
        /// counts only right beside a settler (1.4.0's hands-on run: Necks from a pond had the whole colony hide, come out and
        /// hide again, 54 times in 15 minutes, and nothing got built). By name, not by health: a Deathsquito has little
        /// health and a deadly sting (the review of 1.4.1).
        /// </summary>
        public static bool IsWeak(string prefabName, int level = 1) =>
            !string.IsNullOrEmpty(prefabName) && s_weak.Contains(prefabName) && level <= WeakMostLevel;

        /// <summary>The game's level: 1 with no star, 2 with one, 3 with two. Two stars and it is no longer a weak one.</summary>
        public const int WeakMostLevel = 2;

        /// <summary>
        /// The nearer of the Town Hall and the hut, skipping one the monster is nearer to than the settler (running
        /// there means running at it) or is at (within <see cref="ShelterClear"/>: the Town Hall's camp and an unbuilt hut
        /// have no walls; the third 1.4.1 necks run: settlers stood at them while the Neck that followed bit them to death);
        /// with none left, a spot 15 m straight away from the threat.
        /// </summary>
        public static Shelter ChooseShelter(GroundPoint me, GroundPoint? hall, GroundPoint? hut, GroundPoint? threat)
        {
            if (hall.HasValue && !SafeFrom(hall.Value, me, threat)) hall = null;
            if (hut.HasValue && !SafeFrom(hut.Value, me, threat)) hut = null;
            if (hall.HasValue && hut.HasValue)
                return ColonyRules.Distance(me, hall.Value) <= ColonyRules.Distance(me, hut.Value)
                    ? new Shelter(ShelterKind.TownHall, hall.Value)
                    : new Shelter(ShelterKind.Hut, hut.Value);
            if (hall.HasValue) return new Shelter(ShelterKind.TownHall, hall.Value);
            if (hut.HasValue) return new Shelter(ShelterKind.Hut, hut.Value);
            if (!threat.HasValue) return new Shelter(ShelterKind.AwayFromThreat, me);   // nowhere to go and nothing seen: stay
            return RunAway(me, threat.Value);
        }

        private static bool SafeFrom(GroundPoint shelter, GroundPoint me, GroundPoint? threat) =>
            !threat.HasValue || ColonyRules.Distance(shelter, threat.Value) >= Math.Max(ShelterClear, ColonyRules.Distance(me, threat.Value));

        /// <summary>A shelter with the monster this close to it is no shelter: he runs on, away from it.</summary>
        public const float ShelterClear = 5f;

        /// <summary>The monster moved this far from where it was when he chose where to flee: he chooses again.</summary>
        public const float ChooseAgainMoved = 5f;

        /// <summary>
        /// Whether a fleeing settler chooses where to go again: none chosen yet, the monster moved a lot, it bit him since,
        /// or it is at his shelter (the review of the game-check fixes: a settler stood at the camp while the Neck that
        /// followed bit and circled within 5 m). Otherwise a run-away spot stays put.
        /// </summary>
        public static bool ChooseShelterAgain(bool haveShelter, float threatMoved, bool bittenSince, float threatToShelter) =>
            !haveShelter || threatMoved > ChooseAgainMoved || bittenSince || threatToShelter < ShelterClear;

        private static Shelter RunAway(GroundPoint me, GroundPoint threat)
        {
            float dx = me.X - threat.X, dz = me.Z - threat.Z;
            float length = (float)Math.Sqrt(dx * dx + dz * dz);
            if (length < 0.01f)
            {
                dx = 1f;
                dz = 0f;
                length = 1f;
            }
            return new Shelter(ShelterKind.AwayFromThreat,
                new GroundPoint(me.X + dx / length * RunAwayDistance, me.Z + dz / length * RunAwayDistance));
        }
    }
}
