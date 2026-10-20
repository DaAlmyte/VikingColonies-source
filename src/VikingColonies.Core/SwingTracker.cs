namespace VikingColonies.Core
{
    public struct SwingOutcome
    {
        public bool ApplyDirectHit;
        public bool EnableDirectDamage;
        public bool GiveUp;
    }

    /// <summary>Decides what to do after each axe swing at the current target.</summary>
    public sealed class SwingTracker
    {
        public const int SwingsBeforeDirectDamage = 4;
        public const int SwingsBeforeGivingUp = 12;

        public int SwingsWithoutDamage { get; private set; }

        // Swings that hit nothing at all: only these say real swings don't register. A swing that hit something else
        // registered fine; it was only aimed badly (a stump beside a tree).
        private int _emptySwings;

        public void Reset()
        {
            SwingsWithoutDamage = 0;
            _emptySwings = 0;
        }

        /// <summary>
        /// At the moment the axe lands: with the stand-in on, a swing that missed its target hits it now, so the hit
        /// shows in time with the swing instead of after it.
        /// </summary>
        public static bool ShouldHitAtTrigger(bool directDamageOn, bool hitTarget, bool hitOther = false) =>
            (directDamageOn || hitOther) && !hitTarget;

        /// <param name="targetDamaged">The target's health changed during this swing.</param>
        /// <param name="targetIsMine">This computer owns the target, so its health reading is current.</param>
        /// <param name="directDamageOn">Direct damage is already in use (real swings were found not to register).</param>
        /// <param name="hitSomethingElse">The swing hit another tree, log or rock instead: the target gets a stand-in hit,
        /// but this doesn't count toward switching the stand-in on.</param>
        public SwingOutcome OnSwingFinished(bool targetDamaged, bool targetIsMine, bool directDamageOn, bool hitSomethingElse = false)
        {
            if (targetDamaged)
            {
                Reset();
                return default;
            }
            SwingsWithoutDamage++;
            if (!hitSomethingElse) _emptySwings++;
            // Only a target we own gives a reliable reading, so only it may switch direct damage on;
            // once on, it applies to every target — the game forwards the hit to whoever owns it.
            bool enable = !directDamageOn && targetIsMine && _emptySwings >= SwingsBeforeDirectDamage;
            return new SwingOutcome
            {
                EnableDirectDamage = enable,
                ApplyDirectHit = directDamageOn || enable || hitSomethingElse,
                GiveUp = SwingsWithoutDamage >= SwingsBeforeGivingUp,
            };
        }
    }
}
